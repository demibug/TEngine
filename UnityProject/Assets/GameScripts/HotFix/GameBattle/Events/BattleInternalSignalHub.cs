using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.1：BattleInternalSignalHub —— 单局低频一对多内部事实信号中枢
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Events/BattleInternalSignalHub.cs /
    //   specs/battle-event-boundary/spec.md / specs/battle-runtime-lifecycle/spec.md）：
    //   仅承载需要一对多的单局低频内部事实。不承担核心一致性和跨局全局通信。
    //
    // 事件三层边界（design.md 第 4 节 / spec battle-event-boundary）：
    //   1. 敌人注册、空间索引、伤害、回收、输入事务等一致性操作使用直接调用
    //      （spec "Internal consistency does not depend on global listeners"）。
    //   2. 单局内确需一对多的低频事实使用本类型（BattleInternalSignalHub），
    //      订阅由 BattleRuntimeScope 批量释放
    //      （spec "Event subscriptions follow runtime lifetime"）。
    //   3. UI 使用 IBattleUiEvent 强类型事实；跨程序集只发送
    //      GameCommon/IBattlePublicEvent 定义的开始/完成等不可变 DTO
    //      （spec "Cross-assembly events use immutable common contracts"）。
    //
    // 本类型只负责第 2 层。核心一致性（第 1 层）继续使用直接调用，
    //   不因本类型存在而把所有内部通信都改成信号订阅。判断准则：
    //   - 一对一且同步即时的调用 → 直接调用（如 EnemyManager.Register、AttackResolver.SubmitDamage）。
    //   - 一对多且低频的事实 → 经本类型发布（如 ROUND_SPAWN_PREPARED(plan)、HEALTH_CHANGED）。
    //   - 高频逐实体推进（如每子步的位置/动画） → 不经本类型，避免全局分发成本
    //     （design.md 第 4 节：不把 59 个模块全部迁成事件驱动）。
    //
    // 订阅生命周期（spec "Event subscriptions follow runtime lifetime"）：
    //   所有非 UI 战斗监听 MUST 由所属运行时批量跟踪并在重开、退出和 Shutdown 时解除。
    //   本类型提供 Clear() 一次性解除全部订阅；由 BattleRuntimeScope.TrackSignalHub
    //   登记到作用域，在 Settling 静默清理、失败回滚或 Dispose 时批量释放。
    //   订阅者无需自行持有取消句柄，Scope 释放即保证旧运行时的信号不会回调到
    //   已销毁对象（spec "Restart after listeners were registered"）。
    //
    // 事件签名唯一性（spec "Event signatures are unambiguous"）：
    //   每个类型化事实具有唯一参数契约；相同名称不得同时以有参数和无参数形式发布。
    //   本类型通过强类型 Signal<T> 槽位保证：每个槽位对应一个已定义签名，
    //   不提供同名无参重载。例如 RoundSpawnPrepared 只接受 WaveSpawnPlan 参数。
    //
    // 不变量：
    //   1. 仅承载单局低频一对多内部事实，不承担核心一致性。
    //   2. 订阅由 BattleRuntimeScope 批量解除（Clear 幂等）。
    //   3. 每个信号槽位具有唯一参数签名。
    //   4. 不跨局复用：每局由 BattleRuntimeFactory 新建，随 Runtime 销毁而 Clear。
    // ============================================================================

    /// <summary>
    /// 单局低频一对多内部事实信号中枢。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Events/BattleInternalSignalHub.cs）：</b>
    /// 仅承载需要一对多的单局低频内部事实。不承担核心一致性（敌人注册、空间索引、
    /// 伤害、回收等继续使用直接调用），不承担跨局全局通信（开始/完成等跨程序集事实
    /// 由 <c>IBattlePublicEvent</c> 经 TEngine <c>GameEvent</c> 发送）。</para>
    ///
    /// <para><b>事件三层边界（design.md 第 4 节 / spec battle-event-boundary）：</b>
    /// <list type="number">
    /// <item>内部一致性：直接调用（如 <c>EnemyManager.Register</c>、<c>AttackResolver.SubmitDamage</c>）。</item>
    /// <item>单局一对多低频事实：本类型（如 <see cref="RoundSpawnPrepared"/>、<see cref="HealthChanged"/>）。</item>
    /// <item>跨程序集事实：<c>GameCommon/IBattlePublicEvent</c> 不可变 DTO（开始/完成）。</item>
    /// </list>
    /// 本类型只负责第 2 层。</para>
    ///
    /// <para><b>订阅生命周期（spec "Event subscriptions follow runtime lifetime"）：</b>
    /// 订阅由 <see cref="BattleRuntimeScope.TrackSignalHub"/> 登记到作用域，
    /// 在 Settling 静默清理、失败回滚或 Dispose 时由 <see cref="Clear"/> 批量解除。
    /// 订阅者无需自行持有取消句柄。重开/退出后旧订阅全部解除，
    /// 新一局信号不会回调旧运行时对象（spec "Restart after listeners were registered"）。</para>
    ///
    /// <para><b>签名唯一性（spec "Event signatures are unambiguous"）：</b>
    /// 每个信号槽位对应一个已定义的强类型签名，不提供同名无参重载。
    /// 例如 <see cref="RoundSpawnPrepared"/> 只接受 <see cref="WaveSpawnPlan"/> 参数。</para>
    ///
    /// <para><b>不跨局复用：</b>每局由 <see cref="BattleRuntimeFactory"/> 新建，
    /// 随 <see cref="BattleRuntime"/> 销毁而 <see cref="Clear"/>。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部规则服务与表现层订阅使用，
    /// 不对其他程序集暴露。跨程序集事实走 <c>IBattlePublicEvent</c>。</para>
    /// </remarks>
    internal sealed class BattleInternalSignalHub
    {
        // ====================================================================
        // 已定义的低频一对多信号槽位
        // --------------------------------------------------------------------
        // 每个槽位是一个强类型 Signal<T>，对应一个唯一参数签名的事实。
        // 新增信号槽位时必须保证：签名唯一、低频、一对多、单局内部事实。
        // 不承载核心一致性（直接调用）与跨程序集事实（IBattlePublicEvent）。
        // ====================================================================

        /// <summary>
        /// 波次生成计划准备完成事实（对应还原工程 ROUND_SPAWN_PREPARED(plan)）。
        /// </summary>
        /// <remarks>
        /// <para><b>签名唯一性（spec "Event signatures are unambiguous"）：</b>
        /// 本信号只以带 <see cref="WaveSpawnPlan"/> 参数的唯一签名发布，不保留无参重载。
        /// <see cref="WaveManager.PlanRound"/> 是该事实的唯一发布源；
        /// <c>BattleManager</c> 不二次发布同名无参事件（task 3.9 约束）。</para>
        ///
        /// <para><b>发布源接入（task 7.1）：</b>
        /// <see cref="BattleRuntimeFactory"/> 在组装阶段把
        /// <see cref="WaveManager.OnRoundSpawnPrepared"/> 桥接到本信号：
        /// <c>waveManager.OnRoundSpawnPrepared = plan => hub.RoundSpawnPrepared.Publish(plan)</c>。
        /// 这样 <see cref="WaveManager"/> 仍按直接调用语义触发委托（design 决策 4），
        /// 本信号负责把事实一对多分发给需要订阅的内部组件。</para>
        ///
        /// <para><b>低频依据：</b>每波次只发布一次，属于单局低频事实。</para>
        /// </remarks>
        public Signal<WaveSpawnPlan> RoundSpawnPrepared { get; } = new Signal<WaveSpawnPlan>();

        /// <summary>
        /// 战斗目标生命变化事实（对应还原工程 HEALTH_CHANGED）。
        /// </summary>
        /// <remarks>
        /// <para><b>载荷：</b>目标侧（玩家/对手）、当前生命、变化量。</para>
        /// <para><b>低频依据：</b>生命变化由受击触发，单局内次数有限，属于低频事实。</para>
        /// <para><b>发布源：</b>由规则服务（如 <c>BattleTarget.ApplyDamage</c>）在生命变化点发布。
        /// 本期 task 7.1 只提供信号槽位，发布源接入由后续 task（7.2/7.4 UI 事实）按需连接。
        /// 未接入发布源时订阅者不会收到回调，不影响核心一致性（伤害提交仍走直接调用）。</para>
        /// </remarks>
        public Signal<HealthChangedFact> HealthChanged { get; } = new Signal<HealthChangedFact>();

        /// <summary>
        /// 金币变化事实（对应还原工程 GOLD_CHANGED）。
        /// </summary>
        /// <remarks>
        /// <para><b>载荷：</b>目标侧（玩家/对手）、当前金币、变化量。</para>
        /// <para><b>低频依据：</b>金币变化由招募/刷新/击杀奖励触发，单局内次数有限。</para>
        /// <para><b>发布源：</b>由 <c>BattleEconomy</c> 在余额变更点发布。本期 task 7.1 只提供
        /// 信号槽位，发布源接入由后续 task 按需连接。</para>
        /// </remarks>
        public Signal<GoldChangedFact> GoldChanged { get; } = new Signal<GoldChangedFact>();

        /// <summary>
        /// 战斗结果已冻结事实（首次 TryFreeze 成功后发布一次）。
        /// </summary>
        /// <remarks>
        /// <para><b>载荷：</b>胜负候选与冻结点逻辑时间戳。</para>
        /// <para><b>低频依据：</b>单局只发布一次（幂等冻结，决策 1.4）。</para>
        /// <para><b>发布源：</b>由 <c>BattleResultBuilder.TryFreeze</c> 在首次冻结成功后发布。
        /// 本期 task 7.1 只提供信号槽位，发布源接入由后续 task 按需连接。</para>
        /// <para><b>与 IBattlePublicEvent 的关系：</b>本信号是单局内部事实，
        /// 供表现层/诊断订阅；跨程序集的最终结果发布仍由
        /// <see cref="GameCommon.Battle.IBattlePublicEvent.OnBattleFinished"/> 承担
        /// （spec "Cross-assembly events use immutable common contracts"）。</para>
        /// </remarks>
        public Signal<BattleFrozenFact> BattleFrozen { get; } = new Signal<BattleFrozenFact>();

        /// <summary>
        /// 槽位占用状态变化事实（最终方案：换槽/征兵替换/清空）。
        /// </summary>
        /// <remarks>
        /// <para><b>载荷：</b>固定槽位标识与变化后的占用单位（空槽为 null）。</para>
        /// <para><b>低频依据：</b>单次拖放/征兵只触发有限次槽位变化。</para>
        /// <para><b>发布源：</b>由 <see cref="UnitSlotBoard"/> 在换槽/征兵替换/清空时发布。</para>
        /// </remarks>
        public Signal<SlotChangedFact> SlotChanged { get; } = new Signal<SlotChangedFact>();

        /// <summary>
        /// 单位合并升级事实（最终方案：目标槽、升级后的单位、新等级）。
        /// </summary>
        /// <remarks>
        /// <para><b>低频依据：</b>合并免费且单次执行，不自动连锁。</para>
        /// <para><b>发布源：</b>由 <see cref="UnitSlotBoard"/> 在合并成功时发布。</para>
        /// </remarks>
        public Signal<UnitMergedFact> UnitMerged { get; } = new Signal<UnitMergedFact>();

        /// <summary>
        /// 征兵完成事实（最终方案：阵营与当前征兵费用）。
        /// </summary>
        /// <remarks>
        /// <para><b>低频依据：</b>点击征兵才触发，单局内次数有限。</para>
        /// <para><b>发布源：</b>由 <see cref="BattleInputController"/> 在征兵成功后发布。</para>
        /// </remarks>
        public Signal<RecruitCompletedFact> RecruitCompleted { get; } = new Signal<RecruitCompletedFact>();

        // ====================================================================
        // 批量解除订阅
        // ====================================================================

        /// <summary>
        /// 是否已清空全部订阅。Clear 后置位，新的订阅仍被允许（理论上调用方在
        /// Dispose 前不应再订阅，但本类型不强制，保持灵活性）。
        /// </summary>
        public bool IsCleared { get; private set; }

        /// <summary>
        /// 一次性解除全部信号订阅（幂等）。
        /// </summary>
        /// <remarks>
        /// <para>由 <see cref="BattleRuntimeScope.TrackSignalHub"/> 登记到作用域，
        /// 在 Settling 静默清理、失败回滚或 Dispose 时批量调用。</para>
        /// <para>幂等：重复调用安全。清空后所有已注册订阅者不再被回调，
        /// 保证旧运行时对象不会被新一局或迟到事实回调
        /// （spec "Restart after listeners were registered"）。</para>
        /// <para>本方法不抛出：单个信号的清理异常被捕获并记录，不阻断后续清理。</para>
        /// </remarks>
        internal void Clear()
        {
            if (IsCleared)
            {
                return;
            }

            IsCleared = true;
            TryClearSignal(RoundSpawnPrepared, nameof(RoundSpawnPrepared));
            TryClearSignal(HealthChanged, nameof(HealthChanged));
            TryClearSignal(GoldChanged, nameof(GoldChanged));
            TryClearSignal(BattleFrozen, nameof(BattleFrozen));
            TryClearSignal(SlotChanged, nameof(SlotChanged));
            TryClearSignal(UnitMerged, nameof(UnitMerged));
            TryClearSignal(RecruitCompleted, nameof(RecruitCompleted));
        }

        /// <summary>
        /// 安全清空单个信号槽位（捕获异常不阻断后续清理）。
        /// </summary>
        private static void TryClearSignal<T>(Signal<T> signal, string name)
        {
            if (signal == null)
            {
                return;
            }

            try
            {
                signal.Clear();
            }
            catch (Exception ex)
            {
                // 单个信号清理异常不阻断其他信号与后续清理步骤。
                Log.Error(
                    $"[BattleInternalSignalHub] 清理信号 {name} 异常: {ex}");
            }
        }
    }

    // ============================================================================
    // 强类型信号槽位：每个 Signal<T> 对应一个唯一参数签名的事实。
    // 订阅返回 IUnsubscribeHandle，订阅者可选持有以单独退订；
    // 不持有时由 BattleInternalSignalHub.Clear 批量退订。
    // ============================================================================

    /// <summary>
    /// 强类型一对多信号槽位，对应一个唯一参数签名的事实。
    /// </summary>
    /// <typeparam name="T">事实载荷类型（不可变值或只读快照）。</typeparam>
    /// <remarks>
    /// <para><b>签名唯一性（spec "Event signatures are unambiguous"）：</b>
    /// 每个 <see cref="Signal{T}"/> 实例对应一个已定义签名的事实，只接受 <typeparamref name="T"/>
    /// 参数，不提供同名无参重载。</para>
    /// <para><b>订阅生命周期：</b>订阅者可选持有 <see cref="Subscribe"/> 返回的句柄单独退订；
    /// 不持有时由所属 <see cref="BattleInternalSignalHub.Clear"/> 批量退订
    /// （spec "Event subscriptions follow runtime lifetime"）。</para>
    /// <para><b>线程语义：</b>本类型非线程安全，只在战斗逻辑线程（Unity 主线程）使用。
    /// 发布与订阅均同步执行，符合 design 决策 4“局部同步通信”。</para>
    /// </remarks>
    internal sealed class Signal<T>
    {
        // 订阅者列表。使用 List 保证发布顺序与订阅顺序一致（稳定）。
        // 发布时遍历快照副本，避免订阅者在回调中修改集合造成未定义行为。
        private readonly List<Action<T>> _handlers = new List<Action<T>>();
        private bool _cleared;

        /// <summary>
        /// 订阅一个回调。返回可选的退订句柄。
        /// </summary>
        /// <param name="handler">事实回调；不可为 null。</param>
        /// <returns>退订句柄。调用 <see cref="IUnsubscribeHandle.Unsubscribe"/> 单独退订；
        /// 不调用则由 <see cref="BattleInternalSignalHub.Clear"/> 批量退订。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 null。</exception>
        public IUnsubscribeHandle Subscribe(Action<T> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _handlers.Add(handler);
            return new UnsubscribeHandle(this, handler);
        }

        /// <summary>
        /// 发布事实给全部订阅者（同步、按订阅顺序）。
        /// </summary>
        /// <param name="payload">事实载荷。</param>
        /// <remarks>
        /// <para>发布时遍历订阅者列表的快照副本，避免订阅者在回调中 Subscribe/Unsubscribe
        /// 修改原集合造成未定义行为。新订阅者在本次发布中不会收到回调。</para>
        /// <para>单个订阅者异常被捕获并记录，不阻断后续订阅者回调，
        /// 保证一个订阅者的错误不影响其他订阅者接收事实。</para>
        /// </remarks>
        public void Publish(T payload)
        {
            if (_cleared || _handlers.Count == 0)
            {
                return;
            }

            // 取快照副本，避免回调中修改集合。
            int count = _handlers.Count;
            Action<T>[] snapshot = new Action<T>[count];
            for (int i = 0; i < count; ++i)
            {
                snapshot[i] = _handlers[i];
            }

            for (int i = 0; i < count; ++i)
            {
                try
                {
                    snapshot[i](payload);
                }
                catch (Exception ex)
                {
                    // 单个订阅者异常不阻断其他订阅者。
                    Log.Error(
                        $"[Signal<{typeof(T).Name}>] 订阅者回调异常: {ex}");
                }
            }
        }

        /// <summary>
        /// 清空全部订阅（幂等）。
        /// </summary>
        internal void Clear()
        {
            if (_cleared)
            {
                return;
            }

            _cleared = true;
            _handlers.Clear();
        }

        /// <summary>
        /// 单独退订一个回调（由 UnsubscribeHandle 调用）。
        /// </summary>
        private void Unsubscribe(Action<T> handler)
        {
            if (_cleared)
            {
                return;
            }

            _handlers.Remove(handler);
        }

        /// <summary>
        /// 退订句柄实现。持有目标信号与回调引用，Unsubscribe 时从信号移除。
        /// </summary>
        private sealed class UnsubscribeHandle : IUnsubscribeHandle
        {
            private Signal<T> _signal;
            private Action<T> _handler;

            internal UnsubscribeHandle(Signal<T> signal, Action<T> handler)
            {
                _signal = signal;
                _handler = handler;
            }

            public void Unsubscribe()
            {
                Signal<T> signal = _signal;
                Action<T> handler = _handler;
                _signal = null;
                _handler = null;
                signal?.Unsubscribe(handler);
            }
        }
    }

    /// <summary>
    /// 可选退订句柄。订阅者持有时可单独退订；不持有时由 SignalHub.Clear 批量退订。
    /// </summary>
    internal interface IUnsubscribeHandle
    {
        /// <summary>单独退订该订阅。幂等：重复调用安全。</summary>
        void Unsubscribe();
    }

    // ============================================================================
    // 信号载荷（不可变值类型，spec "UI receives typed battle facts" /
    // spec "Cross-assembly events use immutable common contracts" 的局部内化）
    // --------------------------------------------------------------------
    // 这些载荷只在单局内部使用，不跨程序集。跨程序集事实使用 GameCommon DTO。
    // ============================================================================

    /// <summary>
    /// 生命变化事实载荷（不可变）。
    /// </summary>
    /// <remarks>
    /// 对应 spec "UI receives typed battle facts" Scenario: Health changes：
    /// UI 接收包含目标侧、当前生命和变化量的类型安全通知。
    /// 本载荷为单局内部事实，UI 通知经 IBattleUiEvent 转发（task 7.2）。
    /// </remarks>
    internal readonly struct HealthChangedFact
    {
        /// <summary>是否为玩家方目标。</summary>
        public readonly bool IsPlayerSide;

        /// <summary>变化后的当前生命。</summary>
        public readonly int CurrentHealth;

        /// <summary>本次变化量（正为恢复，负为伤害）。</summary>
        public readonly int Delta;

        /// <summary>构造不可变生命变化事实。</summary>
        public HealthChangedFact(bool isPlayerSide, int currentHealth, int delta)
        {
            IsPlayerSide = isPlayerSide;
            CurrentHealth = currentHealth;
            Delta = delta;
        }
    }

    /// <summary>
    /// 金币变化事实载荷（不可变）。
    /// </summary>
    internal readonly struct GoldChangedFact
    {
        /// <summary>是否为玩家方。</summary>
        public readonly bool IsPlayerSide;

        /// <summary>变化后的当前金币。</summary>
        public readonly int CurrentGold;

        /// <summary>本次变化量（正为获得，负为消耗）。</summary>
        public readonly int Delta;

        /// <summary>构造不可变金币变化事实。</summary>
        public GoldChangedFact(bool isPlayerSide, int currentGold, int delta)
        {
            IsPlayerSide = isPlayerSide;
            CurrentGold = currentGold;
            Delta = delta;
        }
    }

    /// <summary>
    /// 战斗结果冻结事实载荷（不可变）。
    /// </summary>
    internal readonly struct BattleFrozenFact
    {
        /// <summary>胜负候选（true=玩家胜，false=玩家败）。</summary>
        public readonly bool IsWinCandidate;

        /// <summary>冻结点逻辑时间戳（毫秒）。</summary>
        public readonly long FrozenAtMs;

        /// <summary>构造不可变战斗冻结事实。</summary>
        public BattleFrozenFact(bool isWinCandidate, long frozenAtMs)
        {
            IsWinCandidate = isWinCandidate;
            FrozenAtMs = frozenAtMs;
        }
    }

    /// <summary>
    /// 槽位占用状态变化事实载荷（不可变）。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="UnitSlotBoard"/> 在换槽/征兵替换/清空时发布。空槽的 Occupant 为 null。
    /// </remarks>
    internal readonly struct SlotChangedFact
    {
        /// <summary>发生变化的固定槽位标识。</summary>
        public readonly UnitSlotId SlotId;

        /// <summary>变化后的占用单位；空槽为 null。</summary>
        public readonly BattleUnit? Occupant;

        /// <summary>构造不可变槽位变化事实。</summary>
        public SlotChangedFact(UnitSlotId slotId, BattleUnit? occupant)
        {
            SlotId = slotId;
            Occupant = occupant;
        }
    }

    /// <summary>
    /// 单位合并升级事实载荷（不可变）。
    /// </summary>
    internal readonly struct UnitMergedFact
    {
        /// <summary>合并结果所在的目标固定槽位标识。</summary>
        public readonly UnitSlotId TargetSlotId;

        /// <summary>合并后的目标单位（新等级）。</summary>
        public readonly BattleUnit MergedUnit;

        /// <summary>合并后的新等级。</summary>
        public readonly int NewLevel;

        /// <summary>构造不可变合并升级事实。</summary>
        public UnitMergedFact(UnitSlotId targetSlotId, BattleUnit mergedUnit, int newLevel)
        {
            TargetSlotId = targetSlotId;
            MergedUnit = mergedUnit;
            NewLevel = newLevel;
        }
    }

    /// <summary>
    /// 征兵完成事实载荷（不可变）。
    /// </summary>
    internal readonly struct RecruitCompletedFact
    {
        /// <summary>是否为玩家方。</summary>
        public readonly bool IsPlayerSide;

        /// <summary>本次征兵消耗的馒头（费用）。</summary>
        public readonly int Cost;

        /// <summary>下一次征兵费用。</summary>
        public readonly int NextCost;

        /// <summary>构造不可变征兵完成事实。</summary>
        public RecruitCompletedFact(bool isPlayerSide, int cost, int nextCost)
        {
            IsPlayerSide = isPlayerSide;
            Cost = cost;
            NextCost = nextCost;
        }
    }
}
