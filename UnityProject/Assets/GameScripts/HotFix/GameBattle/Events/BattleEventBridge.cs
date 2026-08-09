using System;
using GameCommon.Battle;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 7.2：BattleEventBridge —— 单局事件桥接器
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Events/BattleEventBridge.cs /
    //   specs/battle-event-boundary/spec.md）：
    //   把开始/结果等少量事实转换成 GameCommon 公共 DTO 并发送 TEngine GameEvent；
    //   把 BattleInternalSignalHub 的单局低频内部信号桥接到 IBattleUiEvent UI 通知。
    //
    // 事件三层边界（design.md 第 4 节 / spec battle-event-boundary）：
    //   1. 内部一致性：直接调用。
    //   2. 单局一对多低频事实：BattleInternalSignalHub（task 7.1）。
    //   3. UI 强类型事实：IBattleUiEvent；跨程序集事实：IBattlePublicEvent（GameCommon DTO）。
    //   本类型负责第 2→3 层桥接（内部信号→UI 事件）与第 3 层跨程序集发送
    //   （GameCommon DTO→IBattlePublicEvent）。
    //
    // 1.6 spike 结论（game-event-spike-record.md §3.1/§3.2/§5/§7）：
    //   - GameEventHelper.Init() 已在 GameApp.Entrance 第一行（GameLogic 程序集）最先调用，
    //     保证全局 EventMgr 就绪。本类型在任何接口事件访问之前，Init 已完成。
    //   - 跨程序集公共事件接口 IBattlePublicEvent 定义在 GameCommon，由 GameLogic 单点
    //     GameEventHelper.Init() 注册。本类型通过 GameEvent.Get<IBattlePublicEvent>() 发送，
    //     不自行注册接口实现。
    //   - 战斗 UI 事件 IBattleUiEvent 与公共事件接口一同定义在 GameCommon
    //     （[EventInterface]），本类型通过 GameEvent.Get<IBattleUiEvent>() 发送，并复用
    //     GameApp.Entrance 的 GameEventHelper.Init() 唯一初始化入口；不再依赖 GameBattle
    //     单独生成或执行第二套 Helper。
    //
    // 签名唯一性（spec "Event signatures are unambiguous"）：
    //   每个事件名只有一个参数签名。本类型不提供同名重载：
    //   - OnHealthChanged 只接受 HealthChangedUiFact
    //   - OnGoldChanged 只接受 GoldChangedUiFact
    //   - OnRoundSpawnPrepared 只接受 RoundSpawnPreparedUiFact
    //   - OnBattleFrozen 只接受 BattleFrozenUiFact
    //   - 跨程序集 OnBattleStarted 只接受 BattleLoadoutDto
    //   - 跨程序集 OnBattleFinished 只接受 BattleResultDto
    //
    // 跨程序集只传 GameCommon 不可变 DTO（spec "Cross-assembly events use immutable
    //   common contracts"）：
    //   PublishBattleStarted / PublishBattleFinished 只接受 GameCommon 的
    //   BattleLoadoutDto / BattleResultDto（readonly struct，不可变），
    //   不暴露 GameBattle 内部实体、Manager、集合或具体 UI 类型。
    //
    // 订阅生命周期（spec "Event subscriptions follow runtime lifetime"）：
    //   本类型订阅 BattleInternalSignalHub 的四个信号，订阅句柄由本类型持有，
    //   在 Dispose 时逐一退订。BattleRuntimeScope 通过 TrackDisposable 登记本类型，
    //   在 Settling 静默清理、失败回滚或 Dispose 时调用 Dispose 批量退订。
    //   保证旧运行时的 Bridge 不会在新一局或迟到信号中被回调
    //   （spec "Restart after listeners were registered"）。
    //
    // 不变量：
    //   1. 只桥接低频事实，不承担高频逐实体推进（design.md 第 4 节）。
    //   2. 每个事件名只有一个参数签名。
    //   3. 跨程序集只传 GameCommon 不可变 DTO。
    //   4. 订阅随 Runtime 生命周期释放（Dispose 幂等）。
    //   5. 不跨局复用：每局由 BattleRuntimeFactory 新建，随 Runtime 销毁而 Dispose。
    // ============================================================================

    /// <summary>
    /// 单局事件桥接器：把内部信号桥接到 UI 事件，把开始/结果事实转换成 GameCommon DTO
    /// 并发送 TEngine GameEvent。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Events/BattleEventBridge.cs / spec battle-event-boundary）：</b>
    /// 把开始/结果等少量事实转换成 <c>GameCommon</c> 公共 DTO 并发送 TEngine <c>GameEvent</c>；
    /// 把 <see cref="BattleInternalSignalHub"/> 的单局低频内部信号桥接到
    /// <see cref="IBattleUiEvent"/> UI 通知。</para>
    ///
    /// <para><b>事件三层边界（design.md 第 4 节）：</b>
    /// 本类型负责第 2→3 层桥接（内部信号→UI 事件）与第 3 层跨程序集发送
    /// （<c>GameCommon</c> DTO→<c>IBattlePublicEvent</c>）。</para>
    ///
    /// <para><b>1.6 spike 结论（game-event-spike-record.md §3.1/§3.2/§5/§7）：</b>
    /// <c>GameEventHelper.Init()</c> 已在 <c>GameApp.Entrance</c> 第一行最先调用，
    /// 保证全局 EventMgr 就绪。本类型在任何接口事件访问之前，Init 已完成。
    /// 跨程序集公共事件接口 <c>IBattlePublicEvent</c> 定义在 <c>GameCommon</c>，
    /// 由 <c>GameLogic</c> 单点 <c>GameEventHelper.Init()</c> 注册。本类型通过
    /// <c>GameEvent.Get&lt;IBattlePublicEvent&gt;()</c> 发送，不自行注册接口实现。</para>
    ///
    /// <para><b>签名唯一性（spec "Event signatures are unambiguous"）：</b>
    /// 每个事件名只有一个参数签名，不提供同名重载。</para>
    ///
    /// <para><b>跨程序集只传 GameCommon 不可变 DTO（spec "Cross-assembly events use immutable
    /// common contracts"）：</b>
    /// <see cref="PublishBattleStarted"/> / <see cref="PublishBattleFinished"/> 只接受
    /// <c>GameCommon</c> 的 <c>BattleLoadoutDto</c> / <c>BattleResultDto</c>（readonly struct，
    /// 不可变），不暴露 GameBattle 内部实体、Manager、集合或具体 UI 类型。</para>
    ///
    /// <para><b>订阅生命周期（spec "Event subscriptions follow runtime lifetime"）：</b>
    /// 本类型订阅 <see cref="BattleInternalSignalHub"/> 的四个信号，订阅句柄由本类型持有，
    /// 在 <see cref="Dispose"/> 时逐一退订。<see cref="BattleRuntimeScope"/> 通过
    /// <see cref="BattleRuntimeScope.TrackDisposable"/> 登记本类型，在 Settling 静默清理、
    /// 失败回滚或 Dispose 时调用 <see cref="Dispose"/> 批量退订。</para>
    ///
    /// <para><b>不跨局复用：</b>每局由 <see cref="BattleRuntimeFactory"/> 新建，
    /// 随 <see cref="BattleRuntime"/> 销毁而 <see cref="Dispose"/>。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 <see cref="BattleRuntime"/>/
    /// <see cref="BattleRuntimeFactory"/> 使用，不对其他程序集暴露。跨程序集事实经
    /// <c>IBattlePublicEvent</c> 的 DTO 传递，不暴露本类型。</para>
    /// </remarks>
    internal sealed class BattleEventBridge : IDisposable
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>
        /// 日志标签前缀，便于在日志中筛选事件桥接相关条目。
        /// </summary>
        private const string LogTag = "[BattleEventBridge]";

        // ====================================================================
        // 只读依赖
        // ====================================================================

        /// <summary>
        /// 本局内部信号中枢（task 7.1 产物）。
        /// <para>本类型订阅其四个信号，桥接到 <see cref="IBattleUiEvent"/> UI 事件。
        /// 信号发布由规则服务触发（如 <c>BattleTarget.ApplyDamage</c> 触发 HealthChanged），
        /// 本类型只负责转发，不负责发布信号。</para>
        /// </summary>
        private readonly BattleInternalSignalHub _signalHub;

        // ====================================================================
        // 订阅句柄
        // --------------------------------------------------------------------
        // 持有 SignalHub 四个信号的退订句柄，Dispose 时逐一退订。
        // 不使用 GameEventMgr 管理（SignalHub 订阅不是 GameEvent 监听），
        // 直接持有 IUnsubscribeHandle。
        // ====================================================================

        /// <summary>HealthChanged 信号退订句柄。</summary>
        private IUnsubscribeHandle _healthChangedHandle;

        /// <summary>GoldChanged 信号退订句柄。</summary>
        private IUnsubscribeHandle _goldChangedHandle;

        /// <summary>RoundSpawnPrepared 信号退订句柄。</summary>
        private IUnsubscribeHandle _roundSpawnPreparedHandle;

        /// <summary>BattleFrozen 信号退订句柄。</summary>
        private IUnsubscribeHandle _battleFrozenHandle;

        // ====================================================================
        // 生命周期状态
        // ====================================================================

        /// <summary>
        /// 是否已 Dispose（Bridge 已销毁）。
        /// <para>Dispose 后所有发送与桥接方法为空操作，重复 Dispose 幂等。
        /// BattleModule 在重开/退出时经 RuntimeScope 释放本实例。</para>
        /// </summary>
        public bool IsDisposed { get; private set; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造单局事件桥接器，订阅 SignalHub 的四个信号。
        /// </summary>
        /// <param name="signalHub">
        /// 本局内部信号中枢（非 null）。由 <see cref="BattleRuntimeFactory"/> 构造并注入。
        /// 本类型订阅其四个信号，桥接到 <see cref="IBattleUiEvent"/>。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="signalHub"/> 为 null。
        /// </exception>
        /// <remarks>
        /// <para>构造时立即订阅 SignalHub 的四个信号，桥接到对应 UI 事件。
        /// 订阅句柄由本类型持有，Dispose 时逐一退订。</para>
        /// <para>前提：<c>GameEventHelper.Init()</c> 已在 <c>GameApp.Entrance</c> 第一行
        /// 最先调用（1.6 spike §3.2 唯一初始化入口），保证全局 EventMgr 就绪。
        /// 本构造函数不调用 Init()——Init 只在组合根（GameLogic 程序集）调用一次。</para>
        /// </remarks>
        internal BattleEventBridge(BattleInternalSignalHub signalHub)
        {
            _signalHub = signalHub ?? throw new ArgumentNullException(nameof(signalHub));

            // 订阅 SignalHub 的四个信号，桥接到 IBattleUiEvent。
            // 每个订阅的回调把内部信号载荷转换成 UI 事实载荷，经 GameEvent 发送。
            // 若 GameEvent.Get<IBattleUiEvent>() 返回 default（接口未注册），回调安全无操作
            // （IBattleUiEvent_Gen 的实现可能为 null，调用会 NRE——此处用 try-catch 防御，
            // 不阻断其他信号桥接）。
            _healthChangedHandle = _signalHub.HealthChanged.Subscribe(OnHealthChangedSignal);
            _goldChangedHandle = _signalHub.GoldChanged.Subscribe(OnGoldChangedSignal);
            _roundSpawnPreparedHandle = _signalHub.RoundSpawnPrepared.Subscribe(OnRoundSpawnPreparedSignal);
            _battleFrozenHandle = _signalHub.BattleFrozen.Subscribe(OnBattleFrozenSignal);

            Log.Info($"{LogTag} 构造完成，已订阅 SignalHub 四个信号");
        }

        // ====================================================================
        // 信号→UI 事件桥接回调
        // --------------------------------------------------------------------
        // 每个回调把内部信号载荷转换成 UI 事实载荷，经 GameEvent.Get<IBattleUiEvent>()
        // 发送。单个回调异常被 SignalHub 捕获（task 7.1 Publish 已 try-catch），
        // 不阻断其他订阅者；此处额外用 try-catch 防御 GameEvent.Get 返回 default 的
        // NRE 场景（1.6 spike 风险 A：接口未注册时 Get 返回 default）。
        // ====================================================================

        /// <summary>
        /// HealthChanged 信号桥接：把 <see cref="HealthChangedFact"/> 转换成
        /// <see cref="HealthChangedUiFact"/>，经 <c>GameEvent.Get&lt;IBattleUiEvent&gt;()</c> 发送。
        /// </summary>
        private void OnHealthChangedSignal(HealthChangedFact fact)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                var uiFact = new HealthChangedUiFact(
                    fact.IsPlayerSide,
                    fact.CurrentHealth,
                    fact.Delta);
                GameEvent.Get<IBattleUiEvent>()?.OnHealthChanged(uiFact);
            }
            catch (Exception ex)
            {
                // 1.6 spike 风险 A：接口未注册时 GameEvent.Get 可能返回 default，
                // 调用 OnHealthChanged 会 NRE。捕获不阻断其他信号桥接。
                Log.Error($"{LogTag} 桥接 HealthChanged 异常: {ex}");
            }
        }

        /// <summary>
        /// GoldChanged 信号桥接：把 <see cref="GoldChangedFact"/> 转换成
        /// <see cref="GoldChangedUiFact"/>，经 <c>GameEvent.Get&lt;IBattleUiEvent&gt;()</c> 发送。
        /// </summary>
        private void OnGoldChangedSignal(GoldChangedFact fact)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                var uiFact = new GoldChangedUiFact(
                    fact.IsPlayerSide,
                    fact.CurrentGold,
                    fact.Delta);
                GameEvent.Get<IBattleUiEvent>()?.OnGoldChanged(uiFact);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 桥接 GoldChanged 异常: {ex}");
            }
        }

        /// <summary>
        /// RoundSpawnPrepared 信号桥接：把 <see cref="WaveSpawnPlan"/> 转换成
        /// <see cref="RoundSpawnPreparedUiFact"/>，经 <c>GameEvent.Get&lt;IBattleUiEvent&gt;()</c> 发送。
        /// </summary>
        /// <remarks>
        /// <para>从 WaveSpawnPlan 提取 UI 需要的稳定字段（当前波次、计划敌人数量），
        /// 不暴露完整 WaveSpawnPlan 内部结构（spec "UI receives typed battle facts"：
        /// UI 接收不可变快照，不需要回写内部状态）。</para>
        /// </remarks>
        private void OnRoundSpawnPreparedSignal(WaveSpawnPlan plan)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                // 从 WaveSpawnPlan 提取 UI 需要的稳定字段。
                // WaveSpawnPlan 是 task 3.9 定义的 internal sealed class，此处只读取
                // 波次号（Round）与 Mob0 生成数量（NormalCount）。
                int round = plan.Round;
                int enemyCount = plan.NormalCount;
                var uiFact = new RoundSpawnPreparedUiFact(round, enemyCount);
                GameEvent.Get<IBattleUiEvent>()?.OnRoundSpawnPrepared(uiFact);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 桥接 RoundSpawnPrepared 异常: {ex}");
            }
        }

        /// <summary>
        /// BattleFrozen 信号桥接：把 <see cref="BattleFrozenFact"/> 转换成
        /// <see cref="BattleFrozenUiFact"/>，经 <c>GameEvent.Get&lt;IBattleUiEvent&gt;()</c> 发送。
        /// </summary>
        private void OnBattleFrozenSignal(BattleFrozenFact fact)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                var uiFact = new BattleFrozenUiFact(
                    fact.IsWinCandidate,
                    fact.FrozenAtMs);
                GameEvent.Get<IBattleUiEvent>()?.OnBattleFrozen(uiFact);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 桥接 BattleFrozen 异常: {ex}");
            }
        }

        // ====================================================================
        // 跨程序集公共事件发送（GameCommon DTO → IBattlePublicEvent）
        // --------------------------------------------------------------------
        // 这两个方法把开始/结果事实转换成 GameCommon 不可变 DTO，经
        // GameEvent.Get<IBattlePublicEvent>() 发送。接收方（GameLogic 或其他程序集）
        // 能收到正确强类型 DTO（spec "Cross-assembly events use immutable common contracts"）。
        //
        // 前提：GameEventHelper.Init() 已在 GameApp.Entrance 最先调用（1.6 spike §3.2），
        //   IBattlePublicEvent 由 GameLogic 程序集的 GameEventHelper.Init() 注册。
        //   本方法不调用 Init()——Init 只在组合根调用一次。
        // ====================================================================

        /// <summary>
        /// 发布跨程序集战斗开始事实。
        /// </summary>
        /// <param name="loadout">
        /// 本局不可变装载 DTO（GameCommon 公共契约）。由 <c>BattleModule</c> 在战斗进入
        /// 运行状态后调用。接收方（GameLogic 或其他程序集）能收到正确强类型 DTO。
        /// </param>
        /// <remarks>
        /// <para><b>跨程序集只传 GameCommon 不可变 DTO（spec "Cross-assembly events use
        /// immutable common contracts"）：</b>
        /// 只接受 <c>GameCommon.Battle.BattleLoadoutDto</c>（readonly struct，不可变），
        /// 不暴露 GameBattle 内部实体或 Manager。</para>
        ///
        /// <para><b>签名唯一性：</b>
        /// OnBattleStarted 只以 <c>BattleLoadoutDto</c> 参数签名发布，无同名重载。</para>
        ///
        /// <para><b>前提（1.6 spike §3.2）：</b>
        /// <c>GameEventHelper.Init()</c> 已在 <c>GameApp.Entrance</c> 第一行最先调用，
        /// <c>IBattlePublicEvent</c> 由 <c>GameLogic</c> 程序集注册。</para>
        /// </remarks>
        internal void PublishBattleStarted(BattleLoadoutDto loadout)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                GameEvent.Get<IBattlePublicEvent>()?.OnBattleStarted(loadout);
                Log.Info($"{LogTag} 发布 OnBattleStarted mapId={loadout.MapId} round={loadout.Round}");
            }
            catch (Exception ex)
            {
                // 1.6 spike 风险 A：接口未注册时 Get 返回 default，调用会 NRE。
                // 捕获不阻断战斗流程，只记录错误。
                Log.Error($"{LogTag} 发布 OnBattleStarted 异常: {ex}");
            }
        }

        /// <summary>
        /// 发布跨程序集战斗完成事实。
        /// </summary>
        /// <param name="result">
        /// 已冻结的不可变战斗结果 DTO（GameCommon 公共契约）。由 <c>BattleRuntime</c>
        /// 在 Settling 静默清理完成后调用一次（spec "Runtime quiescence and cleanup have
        /// one ordered owner"：完成静默后发布一次已冻结的不可变结果）。
        /// 接收方无法通过该 DTO 修改 GameBattle 内部状态。
        /// </param>
        /// <remarks>
        /// <para><b>跨程序集只传 GameCommon 不可变 DTO（spec "Cross-assembly events use
        /// immutable common contracts"）：</b>
        /// 只接受 <c>GameCommon.Battle.BattleResultDto</c>（readonly struct，不可变），
        /// 不暴露 GameBattle 内部实体、Manager 或集合。</para>
        ///
        /// <para><b>签名唯一性：</b>
        /// OnBattleFinished 只以 <c>BattleResultDto</c> 参数签名发布，无同名重载。</para>
        ///
        /// <para><b>前提（1.6 spike §3.2）：</b>
        /// <c>GameEventHelper.Init()</c> 已在 <c>GameApp.Entrance</c> 第一行最先调用，
        /// <c>IBattlePublicEvent</c> 由 <c>GameLogic</c> 程序集注册。</para>
        /// </remarks>
        internal void PublishBattleFinished(BattleResultDto result)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                GameEvent.Get<IBattlePublicEvent>()?.OnBattleFinished(result);
                Log.Info(
                    $"{LogTag} 发布 OnBattleFinished isWin={result.IsWin} star={result.Star} " +
                    $"round={result.Round} killCount={result.KillCount}");
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} 发布 OnBattleFinished 异常: {ex}");
            }
        }

        // ====================================================================
        // Dispose —— 退订全部 SignalHub 信号
        // ====================================================================

        /// <summary>
        /// 销毁本桥接器，逐一退订 <see cref="BattleInternalSignalHub"/> 的四个信号。
        /// </summary>
        /// <remarks>
        /// <para><b>销毁时机（spec "Event subscriptions follow runtime lifetime"）：</b>
        /// 由 <see cref="BattleRuntimeScope"/> 经 <see cref="BattleRuntimeScope.TrackDisposable"/>
        /// 登记，在 Settling 静默清理、失败回滚或 Dispose 时调用。</para>
        ///
        /// <para><b>幂等：</b>重复 Dispose 安全。首次调用退订全部信号，后续调用为空操作。</para>
        ///
        /// <para><b>不跨局复用：</b>Dispose 后本实例不再可用。重开由
        /// <see cref="BattleRuntimeFactory"/> 新建 Bridge。</para>
        /// </remarks>
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            // 逐一退订 SignalHub 信号（逆序，与订阅顺序相反）。
            TryUnsubscribe(ref _battleFrozenHandle, nameof(_battleFrozenHandle));
            TryUnsubscribe(ref _roundSpawnPreparedHandle, nameof(_roundSpawnPreparedHandle));
            TryUnsubscribe(ref _goldChangedHandle, nameof(_goldChangedHandle));
            TryUnsubscribe(ref _healthChangedHandle, nameof(_healthChangedHandle));

            Log.Info($"{LogTag} Dispose 完成，已退订全部 SignalHub 信号");
        }

        /// <summary>
        /// 安全退订单个订阅句柄（捕获异常不阻断后续退订）。
        /// </summary>
        private static void TryUnsubscribe(ref IUnsubscribeHandle handle, string name)
        {
            if (handle == null)
            {
                return;
            }

            try
            {
                handle.Unsubscribe();
            }
            catch (Exception ex)
            {
                // 单个退订异常不阻断其他退订。
                Log.Error($"{LogTag} 退订 {name} 异常: {ex}");
            }
            finally
            {
                handle = null;
            }
        }
    }
}
