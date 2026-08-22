using System;
using System.Collections.Generic;
using TEngine;
using YooAsset;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.8：BattleRuntimeScope
    // ----------------------------------------------------------------------------
    // 职责（design.md / specs/battle-runtime-lifecycle/spec.md）：
    //   跟踪本局战斗取得的全部可释放所有权，并在 Settling 静默清理、失败初始化
    //   回滚或 Exit 时按注册逆序幂等释放。替代还原工程字符串服务容器
    //   CombatServices 与隐式全局单例（design 决策 5：删除 SingletonBase /
    //   CombatServices / GameObjectEventProxy）。
    //
    // 跟踪的所有权类别（对应 spec "Runtime quiescence and cleanup have one
    //   ordered owner" 与 "Partial initialization is recoverable"）：
    //   1. GameEvent 局部监听 —— 通过 GameEventMgr 批量注册的 int 事件监听，
    //      Release 时 Clear 一次性解除（event-system.md 推荐：非 UI 类用
    //      GameEventMgr 避免泄漏）。
    //   2. 到期动作 —— BattleActionScheduler 注册的接触伤害 / 刀兵命中 / 攻击
    //      释放等帧级到期回调（battle-simulation/spec.md）。
    //   3. 表现回调 —— Presenter / View 注册的异步完成或动画回调。
    //   4. 资源租约 —— YooAsset AssetHandle（实现 IDisposable，Release 幂等）
    //      与通过 GameModule.Resource.UnloadAsset 释放的裸资源对象
    //      （resource-api.md：LoadAssetAsync 与 UnloadAsset 必须配对）。
    //   5. 池租借 —— BattleObjectPool 的 Acquire/Release 对称租借
    //      （task 4.1 IPoolableBattleObject，本期由 Scope 统一登记释放动作）。
    //   6. 内部信号中枢 —— BattleInternalSignalHub 的单局信号订阅，Clear 批量解除。
    //   7. Scene 租约 —— task 7.6 战斗 Scene，经 GameModule.Scene.UnloadAsync 释放。
    //   8. FUI Package 租约 —— task 7.6 GameFUI PackageLease，Release 递减引用计数。
    //
    // 释放语义：
    //   - 幂等：重复 Dispose / Rollback 安全，每条登记只释放一次。
    //   - 逆序（LIFO）：按注册的反向顺序释放，保证依赖顺序（先释放依赖方）。
    //   - 失败回滚：Rollback 只释放本次已登记的所有权，不触碰未登记项。
    // ============================================================================

    /// <summary>
    /// 单局运行时所有权作用域。
    /// <para>跟踪一局战斗中取得的全部可释放所有权（GameEvent 局部监听、到期动作、
    /// 表现回调、资源租约、池租借、内部信号订阅、Scene 租约、FUI Package 租约），
    /// 并提供幂等逆序释放与失败初始化回滚。</para>
    /// <para>本类型替代还原工程的字符串服务容器 <c>CombatServices</c> 与隐式全局单例：
    /// 所有权显式登记、显式释放，不依赖全局查找（design 决策 5）。</para>
    /// </summary>
    internal sealed class BattleRuntimeScope : IDisposable
    {
        /// <summary>
        /// 单条所有权登记项。
        /// <para>每项携带一个幂等释放动作；释放后置空，重复释放安全。</para>
        /// </summary>
        private sealed class ScopeEntry
        {
            /// <summary>所有权类别，用于诊断与断言。</summary>
            public readonly OwnershipKind Kind;

            /// <summary>可选诊断标签（如资源 location、事件名），仅用于日志。</summary>
            public readonly string Tag;

            // 幂等释放动作；执行后置空，重复调用安全。
            private Action _releaseAction;

            internal ScopeEntry(OwnershipKind kind, string tag, Action releaseAction)
            {
                Kind = kind;
                Tag = tag;
                _releaseAction = releaseAction;
            }

            /// <summary>是否尚未释放（用于断言与诊断）。</summary>
            internal bool IsAlive => _releaseAction != null;

            /// <summary>执行释放并置空，保证幂等。重复调用为空操作。</summary>
            internal void Release()
            {
                Action toRelease = _releaseAction;
                _releaseAction = null;
                toRelease?.Invoke();
            }
        }

        /// <summary>
        /// 所有权类别枚举。对应跟踪的六类所有权，加一个通用兜底。
        /// </summary>
        internal enum OwnershipKind
        {
            /// <summary> GameEvent 局部监听（GameEventMgr 批量解除）。 </summary>
            GameEventListener,

            /// <summary> 到期动作（BattleActionScheduler 注册的回调）。 </summary>
            ScheduledAction,

            /// <summary> 表现回调（Presenter / View 异步完成或动画回调）。 </summary>
            PresentationCallback,

            /// <summary> 资源租约（AssetHandle / UnloadAsset 裸资源）。 </summary>
            ResourceLease,

            /// <summary> 池租借（BattleObjectPool Acquire/Release）。 </summary>
            PoolRental,

            /// <summary> 单局内部信号订阅（BattleInternalSignalHub 批量解除）。 </summary>
            InternalSignalHub,

            /// <summary> 战斗 Scene 租约（GameModule.Scene.LoadSceneAsync 加载，GameModule.Scene.UnloadAsync 释放）。 </summary>
            SceneLease,

            /// <summary> FUI Package 租约（GameFUI PackageLease，Release 递减引用计数）。 </summary>
            FuiPackageLease,

            /// <summary> 通用可释放对象（IDisposable 或自定义释放动作兜底）。 </summary>
            Generic,
        }

        // 登记列表：按注册顺序追加，释放时逆序遍历（LIFO）。
        private readonly List<ScopeEntry> _entries = new List<ScopeEntry>();

        // 是否已完整 Dispose。true 后所有登记 API 拒绝新增，Dispose 幂等。
        private bool _disposed;

        // 是否正在释放中（防止释放过程中重入登记或重复 Dispose）。
        private bool _disposing;

        /// <summary>
        /// 是否已完成完整释放（Dispose 已调用）。
        /// <para>用于 BattleRuntime / BattleModule 判断作用域是否已清理。</para>
        /// </summary>
        internal bool IsDisposed => _disposed;

        /// <summary>
        /// 当前已登记且尚未释放的所有权数量。
        /// <para>Settling 静默清理断言（spec：断言没有活动 Timer、回调或租借对象）
        /// 在完整释放后应为 0。</para>
        /// </summary>
        internal int AliveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _entries.Count; ++i)
                {
                    if (_entries[i].IsAlive)
                    {
                        ++count;
                    }
                }
                return count;
            }
        }

        // ------------------------------------------------------------------------
        // 登记 API：每类所有权提供语义化重载，统一转成幂等释放动作登记。
        // 登记顺序即依赖建立顺序；逆序释放保证依赖方先于被依赖方释放。
        // ------------------------------------------------------------------------

        /// <summary>
        /// 登记一个 <see cref="GameEventMgr"/> 局部事件管理器的所有权。
        /// <para>释放时调用 <see cref="GameEventMgr.Clear"/> 一次性解除全部已注册监听
        /// （event-system.md：非 UI 类用 GameEventMgr 批量管理，Clear 一次性移除）。
        /// Clear 本身幂等。</para>
        /// </summary>
        /// <param name="eventMgr">本局局部事件管理器；不可为 null。</param>
        /// <param name="tag">可选诊断标签。</param>
        internal void TrackGameEventMgr(GameEventMgr eventMgr, string tag = null)
        {
            if (eventMgr == null)
            {
                throw new ArgumentNullException(nameof(eventMgr));
            }
            Track(OwnershipKind.GameEventListener, tag, eventMgr.Clear);
        }

        /// <summary>
        /// 登记一个到期动作回调的所有权。
        /// <para>用于 <c>BattleActionScheduler</c> 注册的接触伤害、刀兵命中、攻击释放等
        /// 帧级到期回调（battle-simulation/spec.md）。释放时执行
        /// <paramref name="cancelAction"/> 取消该回调。</para>
        /// </summary>
        /// <param name="cancelAction">取消该到期动作的释放动作；不可为 null。</param>
        /// <param name="tag">可选诊断标签（如动作描述）。</param>
        internal void TrackScheduledAction(Action cancelAction, string tag = null)
        {
            if (cancelAction == null)
            {
                throw new ArgumentNullException(nameof(cancelAction));
            }
            Track(OwnershipKind.ScheduledAction, tag, cancelAction);
        }

        /// <summary>
        /// 登记一个表现回调的所有权。
        /// <para>用于 Presenter / View 注册的异步完成或动画回调。释放时执行
        /// <paramref name="releaseAction"/> 取消该回调，避免迟到回调在 Runtime 销毁后
        /// 触发（spec：退出完成后任何迟到回调均因战斗代次或当前打开身份失效）。</para>
        /// </summary>
        /// <param name="releaseAction">取消该表现回调的释放动作；不可为 null。</param>
        /// <param name="tag">可选诊断标签。</param>
        internal void TrackPresentationCallback(Action releaseAction, string tag = null)
        {
            if (releaseAction == null)
            {
                throw new ArgumentNullException(nameof(releaseAction));
            }
            Track(OwnershipKind.PresentationCallback, tag, releaseAction);
        }

        /// <summary>
        /// 登记一个 YooAsset <see cref="AssetHandle"/> 资源租约的所有权。
        /// <para>释放时调用 <see cref="AssetHandle.Release"/>（幂等：句柄无效时直接返回，
        /// 见 HandleBase.Release）。对应 LoadAssetAsyncHandle 加载的资源
        /// （resource-api.md：句柄式加载需精细控制释放）。</para>
        /// </summary>
        /// <param name="handle">本局加载的资源句柄；不可为 null。</param>
        /// <param name="tag">可选诊断标签（如资源 location）。</param>
        internal void TrackAssetHandle(AssetHandle handle, string tag = null)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }
            Track(OwnershipKind.ResourceLease, tag, () =>
            {
                // HandleBase.Release 幂等：IsValidWithWarning 为 false 时直接返回。
                if (handle.IsValid)
                {
                    handle.Release();
                }
            });
        }

        /// <summary>
        /// 登记一个通过 <c>GameModule.Resource.UnloadAsset</c> 释放的裸资源租约。
        /// <para>用于 <c>LoadAssetAsync&lt;T&gt;</c> 返回的 TextAsset / SO 等非句柄资源
        /// （resource-api.md：LoadAssetAsync 与 UnloadAsset 必须配对）。
        /// 调用方需提供资源对象与卸载动作；通常
        /// <c>TrackResourceLease(asset, () => GameModule.Resource.UnloadAsset(asset))</c>。</para>
        /// </summary>
        /// <param name="releaseAction">卸载该资源的释放动作；不可为 null。</param>
        /// <param name="tag">可选诊断标签（如资源 location）。</param>
        internal void TrackResourceLease(Action releaseAction, string tag = null)
        {
            if (releaseAction == null)
            {
                throw new ArgumentNullException(nameof(releaseAction));
            }
            Track(OwnershipKind.ResourceLease, tag, releaseAction);
        }

        /// <summary>
        /// 登记一次池租借的所有权。
        /// <para>用于 <c>BattleObjectPool</c> 的 Acquire/Release 对称租借（task 4.1）。
        /// 释放时执行 <paramref name="releaseAction"/> 归还对象到池并完成 Reset。
        /// Acquire/Release 必须对称，否则池复用污染。</para>
        /// </summary>
        /// <param name="releaseAction">归还该租借对象的释放动作；不可为 null。</param>
        /// <param name="tag">可选诊断标签（如对象类型）。</param>
        internal void TrackPoolRental(Action releaseAction, string tag = null)
        {
            if (releaseAction == null)
            {
                throw new ArgumentNullException(nameof(releaseAction));
            }
            Track(OwnershipKind.PoolRental, tag, releaseAction);
        }

        /// <summary>
        /// 登记一个 <see cref="BattleInternalSignalHub"/> 的所有权。
        /// <para>释放时调用 <see cref="BattleInternalSignalHub.Clear"/> 一次性解除全部信号订阅
        /// （task 7.1）。对应 spec "Event subscriptions follow runtime lifetime"：
        /// 所有非 UI 战斗监听 MUST 由所属运行时批量跟踪并在重开、退出和 Shutdown 时解除。
        /// Clear 本身幂等。</para>
        /// <para>本登记由 <see cref="BattleRuntimeFactory"/> 在构造 SignalHub 后立即登记到 Scope，
        /// 保证失败回滚或 Dispose 时信号订阅被批量解除，旧运行时对象不会被回调
        /// （spec "Restart after listeners were registered"）。</para>
        /// </summary>
        /// <param name="hub">本局内部信号中枢；不可为 null。</param>
        /// <param name="tag">可选诊断标签。</param>
        internal void TrackSignalHub(BattleInternalSignalHub hub, string tag = null)
        {
            if (hub == null)
            {
                throw new ArgumentNullException(nameof(hub));
            }
            Track(OwnershipKind.InternalSignalHub, tag, hub.Clear);
        }

        /// <summary>
        /// 登记一个战斗 Scene 租约的所有权（task 7.6）。
        /// <para>用于通过 <c>ModuleSystem.GetModule&lt;ISceneModule&gt;().LoadSceneAsync</c> 加载的战斗场景。
        /// 释放时调用 <paramref name="unloadAction"/> 异步卸载场景（调用方提供
        /// <c>() => GameModule.Scene.UnloadAsync(sceneLocation)</c> 或等效释放动作）。
        /// 幂等：释放动作内部本身安全。</para>
        /// <para><b>所有权（决策 0.11 / spec battle-hotfix-integration）：</b>BattleModule 只释放自己加载的 Scene，
        /// 不释放应用级配置或其他模块加载的场景。本登记确保失败回滚/Exit 时战斗 Scene 被卸载。</para>
        /// </summary>
        /// <param name="unloadAction">卸载该场景的释放动作；不可为 null。该动作应异步执行卸载但本方法不 await。</param>
        /// <param name="tag">可选诊断标签（如场景 location）。</param>
        internal void TrackSceneLease(Action unloadAction, string tag = null)
        {
            if (unloadAction == null)
            {
                throw new ArgumentNullException(nameof(unloadAction));
            }
            Track(OwnershipKind.SceneLease, tag, unloadAction);
        }

        /// <summary>
        /// 登记一个 FUI Package 租约的所有权（task 7.6）。
        /// <para>用于通过 GameFUI <c>PackageLoader.AcquireAsync</c> 获取的 <c>PackageLease</c>。
        /// 释放时调用 <paramref name="releaseAction"/> 递减引用计数（调用方提供
        /// <c>() => packageLease.Release()</c>）。</para>
        /// <para><b>所有权（决策 0.11 / spec battle-hotfix-integration）：</b>BattleModule 只释放自己获取的 FUI 租约。
        /// GameFUI 的共享包记录由 GameFUI 模块自身管理，本登记只负责递减 BattleModule 持有的引用。</para>
        /// <para>注意：GameBattle asmdef 不引用 GameFUI，因此本方法接收的是释放动作委托，
        /// 而非直接引用 <c>PackageLease</c> 类型。调用方（BattleModule 经抽象端口）负责包装。</para>
        /// </summary>
        /// <param name="releaseAction">释放该 FUI 包租约的动作；不可为 null。应调用 PackageLease.Release()。</param>
        /// <param name="tag">可选诊断标签（如包名）。</param>
        internal void TrackFuiPackageLease(Action releaseAction, string tag = null)
        {
            if (releaseAction == null)
            {
                throw new ArgumentNullException(nameof(releaseAction));
            }
            Track(OwnershipKind.FuiPackageLease, tag, releaseAction);
        }

        /// <summary>
        /// 登记一个通用 <see cref="IDisposable"/> 的所有权。
        /// <para>兜底登记项；优先使用语义化重载以便诊断与断言分类。</para>
        /// </summary>
        /// <param name="disposable">可释放对象；不可为 null。</param>
        /// <param name="tag">可选诊断标签。</param>
        internal void TrackDisposable(IDisposable disposable, string tag = null)
        {
            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }
            Track(OwnershipKind.Generic, tag, disposable.Dispose);
        }

        /// <summary>
        /// 登记一个自定义释放动作的所有权。
        /// <para>最通用的登记入口；语义化重载内部均转调本方法。
        /// 释放动作必须幂等（重复调用安全）。</para>
        /// </summary>
        /// <param name="kind">所有权类别。</param>
        /// <param name="tag">可选诊断标签。</param>
        /// <param name="releaseAction">幂等释放动作；不可为 null。</param>
        internal void Track(OwnershipKind kind, string tag, Action releaseAction)
        {
            if (releaseAction == null)
            {
                throw new ArgumentNullException(nameof(releaseAction));
            }
            if (_disposed)
            {
                // 已完整释放后拒绝新增登记，防止向已销毁作用域注入悬垂所有权。
                Log.Warning(
                    $"[BattleRuntimeScope] 已释放，拒绝新增登记 kind={kind} tag={tag ?? "<null>"}");
                return;
            }
            if (_disposing)
            {
                // 释放过程中重入登记通常是释放动作副作用，警告但不抛出。
                Log.Warning(
                    $"[BattleRuntimeScope] 释放过程中拒绝重入登记 kind={kind} tag={tag ?? "<null>"}");
                return;
            }
            _entries.Add(new ScopeEntry(kind, tag, releaseAction));
        }

        // ------------------------------------------------------------------------
        // 释放 API
        // ------------------------------------------------------------------------

        /// <summary>
        /// 完整释放全部登记的所有权（幂等逆序）。
        /// <para>对应 spec “Runtime quiescence and cleanup have one ordered owner”：
        /// Settling 静默清理与 Exit 共用本入口。按注册逆序释放（LIFO），保证依赖方
        /// 先于被依赖方释放。重复调用为空操作（幂等）。</para>
        /// <para>本方法不抛出：单条释放异常被捕获并记录，不阻断后续释放，
        /// 保证清理尽可能完整。</para>
        /// </summary>
        internal void Release()
        {
            Dispose();
        }

        /// <summary>
        /// 失败初始化回滚：只释放本次已登记的所有权（幂等逆序）。
        /// <para>对应 spec “Partial initialization is recoverable”：任一依赖失败时
        /// 只回滚本次已取得的所有权，按反向依赖顺序清理已完成部分。
        /// 与 <see cref="Dispose"/> 行为一致，语义上强调“部分回滚后作用域终结”。</para>
        /// </summary>
        internal void Rollback()
        {
            Dispose();
        }

        /// <summary>
        /// 释放全部登记的所有权（幂等逆序）。实现 <see cref="IDisposable"/>。
        /// <para>逆序遍历 <see cref="_entries"/>，逐条幂等释放；释放中重入登记被拒绝。
        /// 重复 Dispose 安全。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                // 幂等：已释放则直接返回。
                return;
            }
            _disposing = true;
            try
            {
                // 逆序释放（LIFO）：后登记的依赖方先释放。
                for (int i = _entries.Count - 1; i >= 0; --i)
                {
                    ScopeEntry entry = _entries[i];
                    if (!entry.IsAlive)
                    {
                        // 已被单独释放，跳过。
                        continue;
                    }
                    try
                    {
                        entry.Release();
                    }
                    catch (Exception ex)
                    {
                        // 单条释放异常不阻断后续清理，保证尽可能完整。
                        Log.Error(
                            $"[BattleRuntimeScope] 释放异常 kind={entry.Kind} tag={entry.Tag ?? "<null>"}: {ex}");
                    }
                }
                _entries.Clear();
            }
            finally
            {
                _disposing = false;
                _disposed = true;
            }
        }

        // ------------------------------------------------------------------------
        // 诊断 API（供 Settling 静默清理断言使用）
        // ------------------------------------------------------------------------

        /// <summary>
        /// 断言全部所有权已释放。
        /// <para>对应 spec “断言没有活动 Timer、回调或租借对象”。Settling 静默清理
        /// 完成后调用；若仍有活动登记则记录 Error 并返回 false。</para>
        /// </summary>
        /// <returns>已全部释放返回 true；仍有活动项返回 false。</returns>
        internal bool AssertAllReleased()
        {
            int alive = AliveCount;
            if (alive > 0)
            {
                Log.Error($"[BattleRuntimeScope] 仍有 {alive} 项活动所有权未释放：");
                for (int i = 0; i < _entries.Count; ++i)
                {
                    ScopeEntry entry = _entries[i];
                    if (entry.IsAlive)
                    {
                        Log.Error(
                            $"[BattleRuntimeScope]   未释放 kind={entry.Kind} tag={entry.Tag ?? "<null>"}");
                    }
                }
                return false;
            }
            return true;
        }
    }
}
