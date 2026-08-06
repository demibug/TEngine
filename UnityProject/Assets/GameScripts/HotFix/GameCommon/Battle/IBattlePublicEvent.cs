using TEngine;

namespace GameCommon.Battle
{
    /// <summary>
    /// 跨程序集公共战斗事件契约（[EventInterface] 强类型接口事件）。
    /// </summary>
    /// <remarks>
    /// 归属：GameCommon 是跨程序集公共事件契约的唯一归属（见 specs/battle-event-boundary：
    /// 跨程序集事实使用 GameCommon 不可变 DTO + TEngine GameEvent；design 决策 4 第 3 层；
    /// task 1.6 spike 结论：跨程序集公共事件接口定义在 GameCommon，由 GameLogic 组合根
    /// GameEventHelper.Init() 单点注册）。
    ///
    /// 边界：
    /// - 本接口仅承载跨程序集低频事实（开始/完成），参数为 GameCommon 不可变 DTO。
    /// - GameBattle 内部一致性使用直接调用；单局一对多低频事实使用 BattleInternalSignalHub；
    ///   战斗 UI 通知使用 IBattleUiEvent（task 7.2）。本接口不承担高频模拟或内部一致性。
    /// - 每个事实具有唯一参数契约（spec battle-event-boundary “Event signatures are
    ///   unambiguous”），相同名称不得同时以有参和无参形式发布。
    ///
    /// 使用方式（参考 1.6 spike 与 TEngine event-system.md）：
    /// - 发送（GameBattle 的 BattleEventBridge）：GameEvent.Get&lt;IBattlePublicEvent&gt;().OnBattleFinished(result);
    /// - 监听（GameLogic 或其他程序集）：实现接口并经本程序集 GameEventHelper.Init() 注册，
    ///   或用 GameEvent.AddEventListener&lt;BattleResultDto&gt;(IBattlePublicEvent_Event.OnBattleFinished, handler)。
    /// - 前提：GameEventHelper.Init() 已在 GameApp.Entrance 最先调用（1.6 spike 唯一初始化入口）。
    ///
    /// 事件组：EEventGroup.GroupLogic（战斗属逻辑层；EEventGroup 仅 GroupUI/GroupLogic 两值，
    /// 不新增枚举值以免修改 TEngine，见 1.6 spike §3.3）。
    ///
    /// 注意：本接口为正式公共事件体系（task 2.3/7.2）。task 1.6 的 IBattlePublicEventSpike
    /// 为 spike 占位，保留不动，本任务不删除以避免与其它任务冲突。
    /// </remarks>
    [EventInterface(EEventGroup.GroupLogic)]
    public interface IBattlePublicEvent
    {
        /// <summary>
        /// 战斗开始事实。在一局运行时进入运行状态后由 GameBattle 发布一次，
        /// 携带本局不可变装载信息，供跨程序集接收方记录或展示。
        /// </summary>
        /// <param name="loadout">本局不可变装载 DTO（地图、种子、配置版本/hash 占位、牌组预设）。</param>
        void OnBattleStarted(BattleLoadoutDto loadout);

        /// <summary>
        /// 战斗完成事实。在首次结果冻结并由 BattleRuntime 完成静默清理后由 GameBattle
        /// 发布一次（spec battle-runtime-lifecycle “Runtime quiescence and cleanup have
        /// one ordered owner”：完成静默后发布一次已冻结的不可变结果），携带不可变结果 DTO。
        /// 接收方无法通过该 DTO 修改 GameBattle 内部状态（spec battle-event-boundary
        /// “Cross-assembly events use immutable common contracts”）。
        /// </summary>
        /// <param name="result">已冻结的不可变战斗结果 DTO。</param>
        void OnBattleFinished(BattleResultDto result);
    }
}
