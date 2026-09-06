using System;
using System.Collections.Generic;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 输入命令执行控制器：征兵（Recruit）与换槽/合并（DropUnit）
    // ----------------------------------------------------------------------------
    // 职责（最终方案"核心架构"一节）：
    //   输入层只提交两个命令：Recruit(side) 与 DropUnit(sourceSlotId, targetSlotId)。
    //   本类型是 GameBattle 内部对输入命令的唯一执行入口，接收不可变的
    //   <see cref="BattleInputCommand"/>，返回不可变的 <see cref="BattleInputResult"/>。
    //
    // 征兵流程（ExecuteRecruit）：
    //   生成完整批次 → 验证馒头 → 扣费 → 清空待上场单位 → 填满槽位 → 提交。
    //   失败时不清槽、不扣费（spec "Input commands are atomic"）。
    //
    // 换槽/合并/互换流程（ExecuteDropUnit）：
    //   完全通过 UnitSlotBoard 修改槽位与单位状态，全程不访问经济模块。
    //   Runtime 从两槽 before/after 推导迁移（R-R 仅 Board；B-B 移动活动实例；
    //   R-B/B-R 一方下场保留冷却、一方上场；Merge 保持现有语义与特效）。
    //   目标在战场时经 UnitRegistry 激活/复用战斗实例；离开战场时解除战斗实例但保留
    //   BattleUnit（最终方案"战场槽换位时复用同一战斗实例；下场时解除战斗实例"）。
    //
    // CommandId 语义（决策 0.8）：
    //   每条命令携带单局 CommandId。同一 ID 重复提交返回首次结果，不再次扣费、清槽或
    //   换槽；不同 ID 即使 payload 相同也按独立命令处理。缓存由本类型持有，随 Runtime
    //   清理调用 ClearProcessedCommands 清空，不跨局保留。
    //
    // 与 Runtime 的关系：
    //   BattleInputController 由 BattleRuntimeFactory 在每局构造时创建并注入到
    //   BattleRuntime。本类型不跨局复用，随 Runtime 销毁。
    //
    // 主线程串行执行：
    //   所有输入在 Unity 主线程通过 Runtime 串行队列执行。Execute 本身为同步方法。
    //
    // 本类型为 internal sealed：只供 GameBattle 内部 BattleRuntime / BattleRuntimeFactory
    // 使用，不对其他程序集暴露。对外公共输入经 IBattleModule 转发。
    // ============================================================================

    /// <summary>
    /// 输入命令执行控制器：原子执行征兵和换槽/合并命令，任一步失败时回滚。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（最终方案"核心架构"）：</b>输入层只提交两个命令：
    /// Recruit(side) 与 DropUnit(sourceSlotId, targetSlotId)。本类型原子执行，
    /// 任一步失败时回滚到调用前状态（spec "Input commands are atomic"）。</para>
    ///
    /// <para><b>征兵流程（ExecuteRecruit）：</b>
    /// 生成完整批次 → 验证馒头 → 扣费 → 清空待上场单位 → 填满槽位 → 提交。
    /// 扣费失败时不清槽、不扣费。征兵只处理待上场槽，绝不影响战场槽。</para>
    ///
    /// <para><b>换槽/合并流程（ExecuteDropUnit）：</b>
    /// 完全通过 <see cref="UnitSlotBoard"/> 修改槽位与单位状态，全程不访问经济模块。
    /// 目标在战场时经 <see cref="UnitRegistry"/> 激活/复用战斗实例；离开战场时解除
    /// 战斗实例但保留 <see cref="BattleUnit"/>（最终方案"战场槽换位时复用同一战斗实例；
    /// 下场时解除战斗实例"）。</para>
    ///
    /// <para><b>CommandId 语义（决策 0.8）：</b>
    /// 同一 CommandId 重复提交返回首次结果，不再次扣费/清槽/换槽；不同 ID 按独立命令
    /// 处理。缓存由本类型持有（_processedCommands），随 Runtime 清理清空，不跨局保留。</para>
    ///
    /// <para><b>每局新建/销毁：</b>由 BattleRuntimeFactory 在每局构造时创建，随 Runtime
    /// 销毁，不跨局复用（spec "Restart creates clean per-battle state"）。</para>
    /// </remarks>
    internal sealed class BattleInputController
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[BattleInputController]";

        // ====================================================================
        // 注入依赖（全部 readonly，构造后不可替换）
        // ====================================================================

        /// <summary>
        /// 槽位面板：集中维护槽、单位和换槽不变量（最终方案"核心架构"）。
        /// </summary>
        private readonly UnitSlotBoard _slotBoard;

        /// <summary>
        /// 征兵服务：随机生成一整批 1 级四兵（最终方案 Recruit/RecruitManager）。
        /// </summary>
        private readonly RecruitManager _recruitManager;

        /// <summary>
        /// 等级数值服务：校验最大等级、解析伤害/攻速倍率、统一应用等级数值。
        /// </summary>
        private readonly UnitLevelService _levelService;

        /// <summary>
        /// 经济服务：征兵扣费/退还（最终方案整批征兵扣费）。
        /// </summary>
        private readonly BattleEconomy _economy;

        /// <summary>
        /// 单位注册表：激活/解除战场槽中的战斗实例（最终方案"激活战场槽中的单位"）。
        /// </summary>
        private readonly UnitRegistry _unitRegistry;

        /// <summary>
        /// 本局不可变战斗配置快照：按兵种文字查找 UnitConfigSnapshot 供激活战斗实例。
        /// </summary>
        private readonly BattleConfigSnapshot _configSnapshot;

        /// <summary>
        /// 单局内部信号中枢（修复 P0：完整事务成功后发布槽位/合并/征兵事实）。
        /// </summary>
        private readonly BattleInternalSignalHub _signalHub;

        /// <summary>单局可变地图状态；模板仍由 BattleConfigSnapshot.Map 持有且不可变。</summary>
        private readonly BattleMapState _mapState;

        internal BattleMapState MapState => _mapState;

        // ====================================================================
        // 局内可变状态
        // ====================================================================

        /// <summary>
        /// 是否已启动（对应 JS BattleInputController.started）。
        /// StartGame 置 true，GameOver 置 false。未启动时 Execute 返回失败。
        /// </summary>
        private bool _started;

        /// <summary>
        /// 已处理 CommandId → 首次执行结果的缓存（决策 0.8）。
        /// </summary>
        private readonly Dictionary<int, BattleInputResult> _processedCommands
            = new Dictionary<int, BattleInputResult>();

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造输入命令执行控制器。
        /// </summary>
        /// <param name="slotBoard">槽位面板（非 null）。</param>
        /// <param name="recruitManager">征兵服务（非 null）。</param>
        /// <param name="levelService">等级数值服务（非 null）。</param>
        /// <param name="economy">经济服务（非 null）。</param>
        /// <param name="unitRegistry">单位注册表（非 null）。</param>
        /// <param name="configSnapshot">本局不可变战斗配置快照（非 null）。</param>
        /// <param name="signalHub">单局内部信号中枢（可为 null；null 时成功事务不发布事实）。</param>
        /// <exception cref="ArgumentNullException">任一必需参数为 null。</exception>
        internal BattleInputController(
            UnitSlotBoard slotBoard,
            RecruitManager recruitManager,
            UnitLevelService levelService,
            BattleEconomy economy,
            UnitRegistry unitRegistry,
            BattleConfigSnapshot configSnapshot,
            BattleInternalSignalHub signalHub = null,
            BattleMapState mapState = null)
        {
            _slotBoard = slotBoard ?? throw new ArgumentNullException(nameof(slotBoard));
            _recruitManager = recruitManager ?? throw new ArgumentNullException(nameof(recruitManager));
            _levelService = levelService ?? throw new ArgumentNullException(nameof(levelService));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _unitRegistry = unitRegistry ?? throw new ArgumentNullException(nameof(unitRegistry));
            _configSnapshot = configSnapshot ?? throw new ArgumentNullException(nameof(configSnapshot));
            _signalHub = signalHub;
            _mapState = mapState ?? new BattleMapState(configSnapshot.Map);

            _started = false;
        }

        // ====================================================================
        // 生命周期钩子
        // ====================================================================

        /// <summary>
        /// 启动输入控制器：标记已启动状态。
        /// </summary>
        /// <remarks>由 BattleRuntime / BattleManager 在战斗开始时调用。调用后 Execute 接受命令。</remarks>
        internal void StartGame()
        {
            _started = true;
        }

        /// <summary>
        /// 结束输入控制器：标记未启动状态，拒绝后续命令。
        /// </summary>
        internal void GameOver()
        {
            _started = false;
            _mapState.Clear();
        }

        /// <summary>
        /// 清空已处理 CommandId 缓存（决策 0.8）。
        /// </summary>
        internal void ClearProcessedCommands()
        {
            if (_processedCommands.Count > 0)
            {
                _processedCommands.Clear();
            }
        }

        // ====================================================================
        // Execute —— 命令执行入口（CommandId 去重）
        // ====================================================================

        /// <summary>
        /// 执行一条输入命令，返回不可变结果。
        /// </summary>
        /// <param name="command">不可变输入命令（携带单局 CommandId 与强类型载荷）。</param>
        /// <returns>不可变执行结果。成功时携带 CommandId；失败时携带稳定拒绝原因。</returns>
        /// <remarks>
        /// <para><b>CommandId 去重（决策 0.8）：</b>Execute 入口先查缓存：若 CommandId 已存在，
        /// 直接返回缓存结果，不再次执行事务；否则执行完整原子事务并把首次结果写入缓存。</para>
        /// <para><b>未启动拒绝：</b>未调用 <see cref="StartGame"/> 即调用 Execute 时返回失败结果。</para>
        /// </remarks>
        internal BattleInputResult Execute(BattleInputCommand command)
        {
            int commandId = command.CommandId;
            if (_processedCommands.TryGetValue(commandId, out BattleInputResult cached))
            {
                return cached;
            }

            BattleInputResult result = ExecuteInternal(command);
            _processedCommands[commandId] = result;
            return result;
        }

        /// <summary>
        /// Execute 的内部实现：未启动守卫 + 命令分派。
        /// </summary>
        private BattleInputResult ExecuteInternal(BattleInputCommand command)
        {
            if (!_started)
            {
                return BattleInputResult.Fail(
                    command.CommandId,
                    BattleInputRejectReason.Unknown,
                    "输入控制器未启动");
            }

            switch (command.CommandType)
            {
                case BattleInputCommandType.Recruit:
                    return ExecuteRecruit(command);

                case BattleInputCommandType.DropUnit:
                    return ExecuteDropUnit(command);

                case BattleInputCommandType.UseShovel:
                    return ExecuteUseShovel(command);

                case BattleInputCommandType.UseFarmer:
                    return ExecuteUseFarmer(command);

                case BattleInputCommandType.ReclaimUnit:
                    return ExecuteReclaimUnit(command);

                default:
                    return BattleInputResult.Fail(
                        command.CommandId,
                        BattleInputRejectReason.UnsupportedCommand,
                        $"不支持的命令类型 {command.CommandType}");
            }
        }

        private BattleInputResult ExecuteUseShovel(BattleInputCommand command)
        {
            UseShovelPayload payload = command.UseShovelPayload;
            if (!_slotBoard.ContainsSlotId(payload.SourceReserveSlotId))
            {
                return BattleInputResult.Fail(
                    command.CommandId,
                    BattleInputRejectReason.InvalidShovelSource,
                    $"铲子来源槽 {payload.SourceReserveSlotId} 不存在");
            }

            UnitSlot source = _slotBoard.GetSlotById(payload.SourceReserveSlotId);
            if (source.SlotId.Zone != SlotZone.Reserve
                || !source.Occupant.HasValue
                || !source.Occupant.Value.IsShovel)
            {
                return BattleInputResult.Fail(
                    command.CommandId,
                    BattleInputRejectReason.InvalidShovelSource,
                    $"来源槽 {payload.SourceReserveSlotId} 不是待上场铲子");
            }

            bool playerSide = source.SlotId.Side;
            OpenTileRejectReason openReject = _mapState.CanOpenTile(playerSide, payload.Target);
            if (openReject != OpenTileRejectReason.None)
            {
                return MapOpenTileRejection(command.CommandId, openReject, payload.Target);
            }

            OpenTileResult openResult = _mapState.TryOpenTile(playerSide, payload.Target);
            if (!openResult.IsSuccess)
            {
                return MapOpenTileRejection(command.CommandId, openResult.RejectReason, payload.Target);
            }

            if (!_slotBoard.TryCommitShovelUse(
                    payload.SourceReserveSlotId,
                    payload.Target,
                    out ShovelBoardChange boardChange))
            {
                bool rolledBack = _mapState.TryRollback(openResult.Change);
                if (!rolledBack)
                {
                    Log.Error($"{LogTag} 铲子槽位提交失败且地图回滚失败，target={payload.Target}");
                }

                return BattleInputResult.Fail(
                    command.CommandId,
                    BattleInputRejectReason.ShovelTransactionFailed,
                    $"铲子槽位事务提交失败，target={payload.Target}");
            }

            _signalHub?.SlotChanged.Publish(
                new SlotChangedFact(boardChange.SourceSlotId, occupant: null));
            _signalHub?.SlotChanged.Publish(
                new SlotChangedFact(boardChange.AddedBattleSlotId, occupant: null));
            _signalHub?.TileOpened.Publish(
                new TileOpenedFact(
                    playerSide,
                    payload.Target,
                    boardChange.SourceSlotId,
                    boardChange.AddedBattleSlotId));

            return BattleInputResult.Ok(command.CommandId);
        }

        private static BattleInputResult MapOpenTileRejection(
            int commandId,
            OpenTileRejectReason rejectReason,
            GridPosition target,
            bool farmer = false)
        {
            BattleInputRejectReason inputReason = rejectReason switch
            {
                OpenTileRejectReason.WrongSide => farmer
                    ? BattleInputRejectReason.FarmerTargetWrongSide
                    : BattleInputRejectReason.ShovelTargetWrongSide,
                OpenTileRejectReason.AlreadyOpened => farmer
                    ? BattleInputRejectReason.FarmerTargetAlreadyOpened
                    : BattleInputRejectReason.ShovelTargetAlreadyOpened,
                _ => farmer
                    ? BattleInputRejectReason.InvalidFarmerTarget
                    : BattleInputRejectReason.InvalidShovelTarget,
            };

            return BattleInputResult.Fail(
                commandId,
                inputReason,
                $"{(farmer ? "农民" : "铲子")}目标 {target} 非法：{rejectReason}");
        }

        private BattleInputResult ExecuteUseFarmer(BattleInputCommand command)
        {
            UseFarmerPayload payload = command.UseFarmerPayload;
            if (!_slotBoard.ContainsSlotId(payload.SourceReserveSlotId))
            {
                return BattleInputResult.Fail(
                    command.CommandId,
                    BattleInputRejectReason.InvalidFarmerSource,
                    $"农民来源槽 {payload.SourceReserveSlotId} 不存在");
            }

            UnitSlot source = _slotBoard.GetSlotById(payload.SourceReserveSlotId);
            if (source.SlotId.Zone != SlotZone.Reserve
                || !source.Occupant.HasValue
                || source.Occupant.Value.Kind != UnitKind.Farmer)
            {
                return BattleInputResult.Fail(
                    command.CommandId,
                    BattleInputRejectReason.InvalidFarmerSource,
                    $"来源槽 {payload.SourceReserveSlotId} 不是待上场农民");
            }

            bool playerSide = source.SlotId.Side;
            OpenTileRejectReason openReject = _mapState.CanOpenTile(playerSide, payload.Target);
            if (openReject != OpenTileRejectReason.None)
            {
                return MapOpenTileRejection(
                    command.CommandId,
                    openReject,
                    payload.Target,
                    farmer: true);
            }

            OpenTileResult openResult = _mapState.TryOpenTile(playerSide, payload.Target);
            if (!openResult.IsSuccess)
            {
                return MapOpenTileRejection(
                    command.CommandId,
                    openResult.RejectReason,
                    payload.Target,
                    farmer: true);
            }

            if (!_slotBoard.TryCommitFarmerUse(
                    payload.SourceReserveSlotId,
                    payload.Target,
                    out UnitSlotId addedBattleSlotId))
            {
                bool rolledBack = _mapState.TryRollback(openResult.Change);
                if (!rolledBack)
                {
                    Log.Error($"{LogTag} 农民槽位提交失败且地图回滚失败，target={payload.Target}");
                }

                return BattleInputResult.Fail(
                    command.CommandId,
                    BattleInputRejectReason.FarmerTransactionFailed,
                    $"农民槽位事务提交失败，target={payload.Target}");
            }

            _signalHub?.SlotChanged.Publish(
                new SlotChangedFact(source.SlotId, occupant: null));
            _signalHub?.SlotChanged.Publish(
                new SlotChangedFact(addedBattleSlotId, occupant: null));
            _signalHub?.TileOpened.Publish(
                new TileOpenedFact(
                    playerSide,
                    payload.Target,
                    source.SlotId,
                    addedBattleSlotId));

            return BattleInputResult.Ok(command.CommandId);
        }

        /// <summary>
        /// 执行 raw QX 对应的 ReclaimUnit：只回收对手战场单位，并按单位等级返还金币。
        /// </summary>
        private BattleInputResult ExecuteReclaimUnit(BattleInputCommand command)
        {
            ReclaimUnitPayload payload = command.ReclaimUnitPayload;
            int commandId = command.CommandId;

            if (!_slotBoard.ContainsSlotId(payload.SourceBattleSlotId))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidReclaimSource,
                    $"回收来源槽 {payload.SourceBattleSlotId} 不存在");
            }

            UnitSlot source = _slotBoard.GetSlotById(payload.SourceBattleSlotId);
            if (source.SlotId.Zone != SlotZone.Battle
                || source.SlotId.Side
                || !source.Occupant.HasValue
                || source.Occupant.Value.UnitId != payload.ExpectedUnitId
                || (source.Occupant.Value.Kind != UnitKind.Soldier
                    && source.Occupant.Value.Kind != UnitKind.General))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidReclaimSource,
                    $"回收来源槽 {payload.SourceBattleSlotId} 不是期望的对手战场单位");
            }

            // 先导入活动实例的实时冷却，避免任何下游读取到过期板状态。
            SyncSlotLiveCooldown(source.SlotId);
            source = _slotBoard.GetSlotById(payload.SourceBattleSlotId);
            if (!source.Occupant.HasValue
                || source.Occupant.Value.UnitId != payload.ExpectedUnitId)
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidReclaimSource,
                    $"回收来源槽 {payload.SourceBattleSlotId} 在冷却同步后已变化");
            }

            if (_unitRegistry.GetActiveByUnitId(payload.ExpectedUnitId) == null)
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidReclaimSource,
                    $"单位 {payload.ExpectedUnitId} 没有活动战斗实例");
            }

            if (!_slotBoard.TryCommitReclaim(
                    payload.SourceBattleSlotId,
                    payload.ExpectedUnitId,
                    out ReclaimBoardChange boardChange))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.ReclaimTransactionFailed,
                    $"回收槽位提交失败，source={payload.SourceBattleSlotId}");
            }

            try
            {
                long cooldown = _unitRegistry.DeactivateBattleUnit(
                    boardChange.ReclaimedUnit.UnitId);
                if (cooldown < 0L)
                {
                    _slotBoard.TryRollbackReclaim(boardChange);
                    return BattleInputResult.Fail(
                        commandId,
                        BattleInputRejectReason.ReclaimTransactionFailed,
                        $"单位 {boardChange.ReclaimedUnit.UnitId} 战斗实例回收失败");
                }

                _economy.Award(
                    isPlayerSide: false,
                    amount: boardChange.ReclaimedUnit.Level,
                    reason: "reclaim");
            }
            catch (Exception ex)
            {
                bool rolledBack = _slotBoard.TryRollbackReclaim(boardChange);
                if (!rolledBack)
                {
                    Log.Error(
                        $"{LogTag} ReclaimUnit 失败且槽位回滚失败，unit={boardChange.ReclaimedUnit.UnitId}");
                }

                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.ReclaimTransactionFailed,
                    $"回收事务失败：{ex.GetType().Name}");
            }

            if (_signalHub != null)
            {
                for (int i = 0; i < boardChange.Mutations.Count; i++)
                {
                    ReclaimSlotMutation mutation = boardChange.Mutations[i];
                    _signalHub.SlotChanged.Publish(
                        new SlotChangedFact(mutation.SlotId, occupant: null));
                }
            }

            return BattleInputResult.Ok(commandId);
        }

        // ====================================================================
        // ExecuteRecruit —— 征兵命令执行
        // ====================================================================

        /// <summary>
        /// 执行征兵命令的原子事务：预检馒头 → 生成完整批次 → 扣费 → 填满槽位 → 发布事实。
        /// </summary>
        /// <param name="command">征兵命令（载荷为 <see cref="RecruitPayload"/>）。</param>
        /// <returns>执行结果。</returns>
        /// <remarks>
        /// <para><b>流程（最终方案 + 修复 P0）：</b></para>
        /// <list type="number">
        /// <item><b>预检馒头：</b>经 <see cref="BattleEconomy.CanPayRecruitBatch"/> 无副作用校验。
        ///   余额不足返回失败，<b>不生成批次、不推进征兵随机数</b>（修复 P0）。</item>
        /// <item><b>生成完整批次：</b>经 <see cref="RecruitManager.GenerateBatch"/> 生成
        ///   一整批 1 级刀/弓/枪/骑（数量 = 待上场槽数量）。</item>
        /// <item><b>扣费：</b>经 <see cref="BattleEconomy.TryPayRecruitBatch"/> 扣除整批
        ///   征兵费用。失败不清槽、不扣费。</item>
        /// <item><b>填满：</b>经 <see cref="UnitSlotBoard.ReplaceReserve"/> 清除全部待上场
        ///   单位（不论等级）并填满 1 级四兵。批次数量必须等于槽位数，否则失败并退还扣费。</item>
        /// <item><b>发布事实：</b>成功后发布 <see cref="RecruitCompletedFact"/>。</item>
        /// </list>
        /// <para><b>征兵只处理待上场槽，绝不影响战场槽。</b></para>
        /// </remarks>
        private BattleInputResult ExecuteRecruit(BattleInputCommand command)
        {
            RecruitPayload payload = command.RecruitPayload;
            bool side = payload.PlayerSide;
            int commandId = command.CommandId;

            // 步骤 1：预检馒头（无副作用）。金币不足时不生成批次、不推进征兵随机数。
            if (!_economy.CanPayRecruitBatch(side))
            {
                int cost = _economy.GetRefreshCost(side);
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InsufficientGoldForRecruit,
                    $"金币不足，无法支付征兵费用 {cost} side={side}");
            }

            // 步骤 2：生成完整批次（征兵只生成 1 级单位，数量 = 待上场槽数量）。
            IReadOnlyList<BattleUnit> batch = _recruitManager.GenerateBatch(side);

            // 步骤 3：扣费（整批征兵费用）。失败不清槽、不扣费。
            EconomyResult payResult = _economy.TryPayRecruitBatch(side);
            if (!payResult.Success)
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InsufficientGoldForRecruit,
                    $"金币不足，无法支付征兵费用 {payResult.Amount} side={side}");
            }

            // 步骤 4：清空待上场单位并填满新批次（只处理 Reserve 槽）。
            // 批次数量必须等于槽位数；否则失败并退还扣费（修复 P0：征兵必须填满全部待上场槽）。
            try
            {
                if (!_slotBoard.ReplaceReserve(side, batch))
                {
                    _economy.Refund(side, payResult.Amount, "recruit-rollback");
                    Log.Error($"{LogTag} Recruit 填满待上场槽失败（批次数量与槽位数不一致），已退还扣费 {payResult.Amount}");
                    return BattleInputResult.Fail(
                        commandId,
                        BattleInputRejectReason.Unknown,
                        "征兵批次数量与待上场槽数量不一致");
                }
            }
            catch (Exception ex)
            {
                // 填槽异常（理论不可达，ReplaceReserve 只在未初始化时抛异常）：退还扣费。
                _economy.Refund(side, payResult.Amount, "recruit-rollback");
                Log.Error($"{LogTag} Recruit 填满待上场槽异常，已退还扣费 {payResult.Amount}: {ex}");
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.Unknown,
                    $"填满待上场槽失败：{ex.GetType().Name}");
            }

            // 步骤 5：发布征兵完成事实（完整事务成功后）。
            _signalHub?.RecruitCompleted.Publish(
                new RecruitCompletedFact(side, payResult.Amount, payResult.NextRefreshCost));

            return BattleInputResult.Ok(commandId);
        }

        // ====================================================================
        // ExecuteDropUnit —— 换槽/合并命令执行
        // ====================================================================

        /// <summary>
        /// 执行换槽/合并/互换命令：Sync Cooldown → Plan → Prepare(真 Acquire) → Commit Board → Commit Runtime → Publish。
        /// 全程不访问经济模块。
        /// </summary>
        /// <param name="command">换槽/合并命令（载荷为 <see cref="DropUnitPayload"/>）。</param>
        /// <returns>执行结果。</returns>
        /// <remarks>
        /// <para><b>真原子事务（修复 P0）：</b></para>
        /// <list type="number">
        /// <item><b>Sync Cooldown：</b>把源/目标战场槽占用单位的实时冷却写回 Board，
        ///   避免换格/战场合并用过期冷却覆盖真实冷却。</item>
        /// <item><b>Plan：</b>经 <see cref="UnitSlotBoard.TryPlanDrop"/> 只读校验并生成
        ///   <see cref="SlotDropPlan"/>。失败返回拒绝原因，不修改任何状态。</item>
        /// <item><b>Prepare Runtime：</b>按计划推导全部战场落点：首次上场时真 Acquire
        ///   （对象池/配置/等级/冷却都可能抛错），失败返回失败，槽位不变化，不留下半初始化实例。</item>
        /// <item><b>Commit Board：</b>经 <see cref="UnitSlotBoard.CommitDrop"/> 一次性提交
        ///   源/目标两槽的 After 状态（版本冲突校验；冲突时释放已准备实例）。</item>
        /// <item><b>Commit Runtime：</b>激活/复用/解除战斗实例。抛错时经
        ///   <see cref="UnitSlotBoard.RollbackDrop"/> 回滚 Board 并释放已准备实例，
        ///   保证"槽位已移动但战斗实例未创建"的半提交不可能发生。</item>
        /// <item><b>Publish：</b>完整事务成功后发布两槽最终状态的
        ///   <see cref="SlotChangedFact"/> / <see cref="UnitMergedFact"/>。</item>
        /// </list>
        /// <para><b>统一拖放规则：</b>空目标 Move；同兵种（Kind/SoldierType 权威）同等级
        /// 未满级 Merge；其余占用目标 Swap。Runtime 从两槽 before/after 推导：R-R 仅 Board；
        /// B-B 移动活动实例；R-B/B-R 一方下场保留冷却、一方上场；Merge 保持现有语义与特效。</para>
        /// <para><b>合并条件：</b>不同单位、同阵营、同兵种、同等级、低于配置最大等级。
        /// 合并免费且单次执行，不自动连锁。合并结果保留目标 UnitId 和目标 SlotId。</para>
        /// </remarks>
        private BattleInputResult ExecuteDropUnit(BattleInputCommand command)
        {
            DropUnitPayload payload = command.DropUnitPayload;
            int commandId = command.CommandId;

            // 槽位有效性校验：无效 ID 直接返回拒绝，避免 default 槽位误判。
            if (!_slotBoard.ContainsSlotId(payload.SourceSlotId))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidSourceSlot,
                    $"源槽 {payload.SourceSlotId} 不存在");
            }

            if (!_slotBoard.ContainsSlotId(payload.TargetSlotId))
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.InvalidTargetSlot,
                    $"目标槽 {payload.TargetSlotId} 不存在");
            }

            UnitSlotId sourceSlotId = _slotBoard.GetSlotById(payload.SourceSlotId).SlotId;
            UnitSlotId targetSlotId = _slotBoard.GetSlotById(payload.TargetSlotId).SlotId;

            UnitSlot sourceSlot = _slotBoard.GetSlotById(payload.SourceSlotId);
            if (sourceSlot.Occupant.HasValue
                && (sourceSlot.Occupant.Value.Kind == UnitKind.Prop
                    || sourceSlot.Occupant.Value.Kind == UnitKind.Farmer)
                && targetSlotId.Zone == SlotZone.Battle)
            {
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.UnitZoneRestricted,
                    "道具或农民不能通过普通换槽命令放入战场，请使用专用命令");
            }

            // 阶段 0：战斗内冷却同步（修复 P0）。
            // 先把源/目标 Battle 槽占用单位的实时冷却写回 Board，避免换格/战场合并
            // 用过期冷却覆盖真实 SoldierBase 冷却（"所有槽位迁移不刷新攻击冷却"）。
            SyncLiveCooldowns(sourceSlotId, targetSlotId);

            // 阶段 1：Plan（只读校验，不修改任何状态）。
            SlotDropResult dropResult = _slotBoard.TryPlanDrop(sourceSlotId, targetSlotId);
            if (!dropResult.Success)
            {
                return MapDropRejection(dropResult, commandId);
            }

            SlotDropPlan plan = dropResult.Plan;

            // 阶段 2：Prepare Runtime（真 Acquire，可抛错；失败槽位不变化）。
            PreparedRuntime prepared;
            try
            {
                prepared = PrepareRuntime(plan);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} DropUnit 准备战斗实例失败，槽位不变化: {ex}");
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.BattleInstancePrepareFailed,
                    $"战斗实例准备失败：{ex.GetType().Name}");
            }

            // 阶段 3：Commit Board（一次性提交槽位状态，版本冲突校验）。
            if (!_slotBoard.CommitDrop(plan))
            {
                Log.Error($"{LogTag} DropUnit 提交槽位失败（版本冲突），释放已准备实例");
                ReleasePrepared(prepared);
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.TransactionVersionConflict,
                    "槽位事务版本冲突，请重试");
            }

            // 阶段 4：Commit Runtime（激活/复用/解除；抛错则回滚 Board 并释放实例）。
            try
            {
                CommitRuntime(prepared, plan);
            }
            catch (Exception ex)
            {
                Log.Error($"{LogTag} DropUnit 提交战斗实例失败，回滚槽位状态: {ex}");
                _slotBoard.RollbackDrop(plan);
                ReleasePrepared(prepared);
                return BattleInputResult.Fail(
                    commandId,
                    BattleInputRejectReason.BattleInstancePrepareFailed,
                    $"战斗实例提交失败：{ex.GetType().Name}");
            }

            // 阶段 5：Publish（完整事务成功后发布事实）。
            PublishDropFacts(plan);

            return BattleInputResult.Ok(commandId);
        }

        /// <summary>
        /// 战斗内冷却同步：把源/目标战场槽占用单位的实时冷却写回 Board（修复 P0）。
        /// </summary>
        /// <remarks>
        /// 单位留在战场攻击期间，Board 中保存的冷却可能过期。战场换格、战场目标合并、
        /// 战场源合并都会使用 Board 中单位，若不先同步，会用过期冷却覆盖真实冷却。
        /// </remarks>
        private void SyncLiveCooldowns(UnitSlotId sourceSlotId, UnitSlotId targetSlotId)
        {
            SyncSlotLiveCooldown(sourceSlotId);
            SyncSlotLiveCooldown(targetSlotId);
        }

        private void SyncSlotLiveCooldown(UnitSlotId slotId)
        {
            if (slotId.Zone != SlotZone.Battle)
            {
                return;
            }

            BattleUnit? occupant = _slotBoard.GetOccupant(slotId);
            if (!occupant.HasValue)
            {
                return;
            }

            if (_unitRegistry.TryGetLiveCooldown(occupant.Value.UnitId, out long liveCooldown))
            {
                _slotBoard.UpdateOccupantCooldownByUnitId(
                    occupant.Value.UnitId, liveCooldown);
            }
        }

        /// <summary>换槽/合并/互换战斗实例准备结果。</summary>
        private struct PreparedRuntime
        {
            /// <summary>全部需在战场放置的单位（首次上场已 Acquire，已在战场则复用）。</summary>
            internal List<PreparedActivation> Activations;

            /// <summary>需解除战斗实例的单位 ID 列表（合并源或下场单位）。</summary>
            internal List<int> DeactivateUnitIds;
        }

        /// <summary>单个战场落点的准备结果。</summary>
        private struct PreparedActivation
        {
            /// <summary>落点单位（事务后位于战场槽的单位）。</summary>
            internal BattleUnit Unit;

            /// <summary>落点单位的配置快照（首次上场与复用路径都需要）。</summary>
            internal UnitConfigSnapshot Config;

            /// <summary>落点战场格。</summary>
            internal GridPosition Grid;

            /// <summary>首次上场时已 Acquire 待激活的实例（null=复用已有实例）。</summary>
            internal SoldierBase NewInstance;
        }

        /// <summary>
        /// 据事务计划准备战斗实例（真 Acquire + 配置校验；可抛错，失败槽位不变化）。
        /// </summary>
        /// <remarks>
        /// <para>从两槽 before/after 推导 Runtime 迁移：所有"事务后位于战场"的单位
        /// 逐一准备配置与首次上场实例（已在战场则复用，无需 Acquire）；所有"原在战场
        /// 但事务后离开战场"的单位列入解除列表（下场保留冷却）。</para>
        /// <para>全部可能抛错的首次上场 Acquire 都在本阶段完成，保证 Commit Board 后
        /// 的 Commit Runtime 只做不会因配置缺失而失败的操作（避免运行时半提交）。</para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">配置缺失或 Acquire 失败。</exception>
        private PreparedRuntime PrepareRuntime(SlotDropPlan plan)
        {
            var prepared = new PreparedRuntime
            {
                Activations = new List<PreparedActivation>(),
                DeactivateUnitIds = new List<int>(),
            };

            var activationIds = new HashSet<int>();
            IReadOnlyList<SlotDropMutation> mutations = plan.Mutations;

            try
            {
                // 双格 General 的两格共享 UnitId，只在左半格准备一个战斗实例。
                for (int index = 0; index < mutations.Count; index++)
                {
                    SlotDropMutation mutation = mutations[index];
                    if (!mutation.After.HasValue || mutation.SlotId.Zone != SlotZone.Battle)
                    {
                        continue;
                    }

                    BattleUnit unit = mutation.After.Value;
                    if (unit.Kind == UnitKind.GeneralPart)
                    {
                        continue;
                    }

                    if (unit.Kind == UnitKind.General && !unit.IsGeneralPrimaryCell)
                    {
                        continue;
                    }

                    if (activationIds.Add(unit.UnitId))
                    {
                        prepared.Activations.Add(PrepareActivation(unit, mutation.SlotId));
                    }
                }

                // 解除：事务前在战场、事务后已完全离开战场的单位。双格 General 只解除一次。
                var deactivateIds = new HashSet<int>();
                for (int index = 0; index < mutations.Count; index++)
                {
                    SlotDropMutation mutation = mutations[index];
                    if (!mutation.Before.HasValue || mutation.SlotId.Zone != SlotZone.Battle)
                    {
                        continue;
                    }

                    BattleUnit beforeUnit = mutation.Before.Value;
                    if (beforeUnit.Kind == UnitKind.GeneralPart)
                    {
                        continue;
                    }

                    int unitId = beforeUnit.UnitId;
                    if (!EndsInBattle(plan, unitId) && deactivateIds.Add(unitId))
                    {
                        prepared.DeactivateUnitIds.Add(unitId);
                    }
                }
            }
            catch
            {
                ReleasePrepared(prepared);
                throw;
            }

            return prepared;
        }

        /// <summary>准备单个战场落点：解析配置，首次上场时真 Acquire（可抛错）。</summary>
        private PreparedActivation PrepareActivation(BattleUnit unit, UnitSlotId slotId)
        {
            UnitConfigSnapshot config = FindUnitConfig(unit);
            if (config == null)
            {
                throw new InvalidOperationException(
                    $"配置中无兵种 {unit.SoldierText}");
            }

            SoldierBase newInstance = _unitRegistry.GetActiveByUnitId(unit.UnitId) == null
                ? _unitRegistry.PrepareBattleInstance(
                    unit, config, DefaultUnitWidth, DefaultUnitHeight)
                : null;

            return new PreparedActivation
            {
                Unit = unit,
                Config = config,
                Grid = slotId.GridPosition,
                NewInstance = newInstance,
            };
        }

        /// <summary>按单位 ID 判断其事务后是否仍位于战场（用于解除判定）。</summary>
        /// <remarks>
        /// <para>双格 General 的两格共享 UnitId：任一格（主/副格）仍在战场都视为仍在场，
        /// 复用同一战斗实例。</para>
        /// <para>解散后的 GeneralPart 不是战斗参与者：即使 GeneralPart 保留了原 General 的
        /// UnitId，也不能让旧战斗运行时继续存活（分离字牌不参战、不占 Spine），因此
        /// GeneralPart 落点不参与本判定。普通士兵与仍完整的 General 半格判定不变。</para>
        /// </remarks>
        private static bool EndsInBattle(SlotDropPlan plan, int unitId)
        {
            IReadOnlyList<SlotDropMutation> mutations = plan.Mutations;
            for (int index = 0; index < mutations.Count; index++)
            {
                SlotDropMutation mutation = mutations[index];
                if (mutation.SlotId.Zone == SlotZone.Battle
                    && mutation.After.HasValue
                    && mutation.After.Value.Kind != UnitKind.GeneralPart
                    && mutation.After.Value.UnitId == unitId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>释放已准备但未激活的实例（事务失败回滚用）。</summary>
        private void ReleasePrepared(PreparedRuntime prepared)
        {
            if (prepared.Activations == null)
            {
                return;
            }

            for (int i = 0; i < prepared.Activations.Count; i++)
            {
                SoldierBase newInstance = prepared.Activations[i].NewInstance;
                if (newInstance != null)
                {
                    try
                    {
                        _unitRegistry.ReleasePrepared(newInstance);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"{LogTag} 释放已准备战斗实例异常: {ex}");
                    }
                }
            }
        }

        /// <summary>提交战斗实例变化（激活/复用/解除；抛错时由调用方回滚 Board）。</summary>
        private void CommitRuntime(PreparedRuntime prepared, SlotDropPlan plan)
        {
            // 激活/复用：全部战场落点先激活，再解除下场单位（避免同格瞬时冲突）。
            for (int i = 0; i < prepared.Activations.Count; i++)
            {
                PreparedActivation activation = prepared.Activations[i];
                if (activation.NewInstance != null)
                {
                    // 首次上场：激活已准备实例。
                    _unitRegistry.ActivatePrepared(
                        activation.Unit,
                        activation.NewInstance,
                        _levelService,
                        activation.Grid.X,
                        activation.Grid.Y,
                        activation.Config);
                }
                else
                {
                    // 已在战场：复用同一实例并重新放置（战场换格/互换/合并）。
                    _unitRegistry.ActivateBattleUnit(
                        activation.Unit,
                        activation.Config,
                        _levelService,
                        activation.Grid.X,
                        activation.Grid.Y,
                        DefaultUnitWidth,
                        DefaultUnitHeight);
                }
            }

            for (int i = 0; i < prepared.DeactivateUnitIds.Count; i++)
            {
                int unitId = prepared.DeactivateUnitIds[i];
                // 下场/合并源：导出冷却（写回 BattleUnit），取消未释放攻击并回池。
                long cooldown = _unitRegistry.DeactivateBattleUnit(unitId);
                if (cooldown >= 0L)
                {
                    WriteBackCooldown(unitId, cooldown);
                }
            }
        }

        /// <summary>把战斗实例冷却写回槽位中的 BattleUnit（修复 P0）。</summary>
        private void WriteBackCooldown(int unitId, long lastAttackTimeMs)
        {
            // 源单位在合并中被消耗（不存在于任何槽位），无需写回。
            // 下场场景：源单位移动到非战场槽，需更新其冷却。
            _slotBoard.UpdateOccupantCooldownByUnitId(unitId, lastAttackTimeMs);
        }

        /// <summary>发布换槽/合并/互换事实（完整事务成功后）。</summary>
        private void PublishDropFacts(SlotDropPlan plan)
        {
            if (_signalHub == null)
            {
                return;
            }

            // 槽位变化：普通单位发布两槽；双格 General 原子发布全部四槽。
            IReadOnlyList<SlotDropMutation> mutations = plan.Mutations;
            for (int index = 0; index < mutations.Count; index++)
            {
                SlotDropMutation mutation = mutations[index];
                _signalHub.SlotChanged.Publish(new SlotChangedFact(
                    mutation.SlotId, mutation.After));
            }

            if (plan.IsMerge && plan.TargetAfter.HasValue)
            {
                _signalHub.UnitMerged.Publish(new UnitMergedFact(
                    plan.TargetSlotId, plan.TargetAfter.Value, plan.TargetAfter.Value.Level));
            }

            if (plan.IsSynthesize)
            {
                for (int index = 0; index < mutations.Count; index++)
                {
                    SlotDropMutation mutation = mutations[index];
                    if (mutation.After.HasValue
                        && mutation.After.Value.IsGeneralPrimaryCell)
                    {
                        _signalHub.GeneralSynthesized.Publish(new GeneralSynthesizedFact(
                            mutation.SlotId, mutation.After.Value));
                        break;
                    }
                }
            }
        }

        /// <summary>把换槽/合并拒绝原因映射为输入命令拒绝原因。</summary>
        private static BattleInputResult MapDropRejection(SlotDropResult dropResult, int commandId)
        {
            switch (dropResult.RejectReason)
            {
                case SlotDropRejectReason.InvalidSource:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.InvalidSourceSlot,
                        dropResult.DiagnosticMessage);
                case SlotDropRejectReason.InvalidTarget:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.InvalidTargetSlot,
                        dropResult.DiagnosticMessage);
                case SlotDropRejectReason.SourceEmpty:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.SourceSlotEmpty,
                        dropResult.DiagnosticMessage);
                case SlotDropRejectReason.SameSlot:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.SameSlot,
                        dropResult.DiagnosticMessage);
                case SlotDropRejectReason.CrossSide:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.CrossSideMerge,
                        dropResult.DiagnosticMessage);
                case SlotDropRejectReason.MaxLevelReached:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.MaxLevelReached,
                        dropResult.DiagnosticMessage);
                case SlotDropRejectReason.TargetMismatch:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.TargetMismatch,
                        dropResult.DiagnosticMessage);
                case SlotDropRejectReason.UnitZoneRestricted:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.UnitZoneRestricted,
                        dropResult.DiagnosticMessage);
                case SlotDropRejectReason.HorizontalPairUnavailable:
                case SlotDropRejectReason.GeneralPairWouldSplit:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.InvalidTargetSlot,
                        dropResult.DiagnosticMessage);
                default:
                    return BattleInputResult.Fail(commandId, BattleInputRejectReason.Unknown,
                        dropResult.DiagnosticMessage);
            }
        }

        /// <summary>按兵种文字查找 UnitConfigSnapshot。</summary>
        private UnitConfigSnapshot FindUnitConfig(BattleUnit unit)
        {
            if (unit.Kind == UnitKind.General)
            {
                GeneralConfigSnapshot general = _configSnapshot.GeneralCatalog.GetByIndexOrDefault(unit.GeneralIndex);
                if (general == null)
                {
                    return null;
                }

                return general.ToUnitConfigSnapshot();
            }

            IReadOnlyList<UnitConfigSnapshot> units = _configSnapshot.Units;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Text == unit.SoldierText)
                {
                    return units[i];
                }
            }

            return null;
        }

        /// <summary>默认单位逻辑宽度（像素，约定 40，对应半个格子）。</summary>
        private const float DefaultUnitWidth = 40f;

        /// <summary>默认单位逻辑高度（像素，约定 40，对应半个格子）。</summary>
        private const float DefaultUnitHeight = 40f;
    }
}
