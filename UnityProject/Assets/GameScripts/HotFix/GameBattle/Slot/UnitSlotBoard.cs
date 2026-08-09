using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 槽位领域：UnitSlotBoard —— 槽位、单位与换槽不变量的集中维护者
    // ----------------------------------------------------------------------------
    // 职责（最终方案"核心架构"一节 + 修复阶段 P0"规则事务与唯一真值"）：
    //   新增一个深模块 UnitSlotBoard，集中维护槽、单位和换槽不变量。UI、
    //   DeckManager、UnitRegistry 不再分别保存一份"单位在哪里"的状态。
    //
    //   输入层只提交两个命令：Recruit(side) 与 DropUnit(sourceSlotId, targetSlotId)。
    //   DropUnit 内部根据目标槽是否为空，决定执行换槽还是合并。拖动过程中不修改
    //   规则状态，只在松手时提交一次命令，复用现有 CommandId 幂等机制。
    //
    // 原子事务（修复阶段 P0）：
    //   换槽/合并拆为两个阶段：
    //   1. TryPlanDrop —— 只读校验 + 生成 <see cref="SlotDropPlan"/>，不修改任何状态。
    //   2. CommitDrop —— 按计划一次性提交槽位状态（BoardRevision 校验防版本冲突）。
    //   BattleInputController 在两者之间准备战斗实例；任何准备失败都返回失败，
    //   槽位不变化（"激活失败不回滚"的旧问题被消除）。
    //
    // 固定槽位：
    //   每个槽位拥有单局内固定不变的 SlotId（UnitSlotId.Id）。单位卡才会在槽位
    //   之间迁移，槽位本身不移动。Battle 槽由地图可建造格生成；Reserve 槽数量
    //   由配置决定。
    //
    // 拖放规则（最终方案"冻结后的玩法规则" + 修复）：
    //   1. 空目标槽：把单位从源槽换到目标槽，源槽变空。
    //      先检查 sourceSlot.Side == sourceUnit.Side == targetSlot.Side（跨阵营空槽拦截）。
    //   2. 有目标单位且满足合并条件：目标单位升一级，源单位消失，源槽变空。
    //   3. 有目标单位但不满足条件：不修改任何逻辑状态，表现弹回源槽。
    //   4. 合并条件：不同单位、同阵营、同兵种、同等级、低于配置最大等级。
    //   5. 合并免费且单次执行，不自动连锁。
    //   6. 合并结果保留目标 UnitId 和目标 SlotId；升级保留目标攻击冷却。
    //   7. 所有拖动行为都不自动补充待上场单位。
    //   8. 征兵只处理待上场槽，绝不影响战场槽；批次数量必须等于槽位数。
    //
    // 事实发布：
    //   换槽/合并/征兵替换的事实由 <see cref="BattleInputController"/> 在完整事务成功
    //   后发布到 BattleInternalSignalHub。本类型不直接触发 C# 事件（删除旧未接线事件）。
    //
    // 不变量：
    //   1. SlotId 固定不变，单位迁移不改变槽位标识。
    //   2. 同一槽位同一时刻最多占用一个单位。
    //   3. BattleUnit 是局内权威数据；SoldierBase 只是 BattleSlot 的战斗运行时对象。
    //   4. 征兵只处理 Reserve 槽，绝不触碰 Battle 槽。
    //   5. 合并不修改源单位（源单位消失），结果保留目标 UnitId 与目标 SlotId。
    //   6. 任何槽位修改都必须先经 TryPlanDrop 校验，再经 CommitDrop 提交。
    // ============================================================================

    /// <summary>
    /// 换槽/合并操作拒绝原因。
    /// </summary>
    internal enum SlotDropRejectReason
    {
        /// <summary>无拒绝（成功）。</summary>
        None = 0,

        /// <summary>源槽非法（无效槽位标识）。</summary>
        InvalidSource = 1,

        /// <summary>目标槽非法（无效槽位标识）。</summary>
        InvalidTarget = 2,

        /// <summary>源槽为空，无单位可移动。</summary>
        SourceEmpty = 3,

        /// <summary>源槽与目标槽相同。</summary>
        SameSlot = 4,

        /// <summary>源单位与目标单位阵营不同，或空目标槽跨阵营。</summary>
        CrossSide = 5,

        /// <summary>目标单位不满足合并条件（不同兵种/不同等级等）。</summary>
        TargetMismatch = 6,

        /// <summary>目标单位已满级，不可继续合并。</summary>
        MaxLevelReached = 7,
    }

    /// <summary>
    /// 换槽/合并操作结果（TryPlanDrop 的返回）。
    /// </summary>
    internal readonly struct SlotDropResult
    {
        /// <summary>是否成功。</summary>
        public readonly bool Success;

        /// <summary>拒绝原因（成功时为 None）。</summary>
        public readonly SlotDropRejectReason RejectReason;

        /// <summary>诊断信息（仅日志用）。</summary>
        public readonly string DiagnosticMessage;

        /// <summary>生成的事务计划（成功时有效，供 CommitDrop 提交）。</summary>
        public readonly SlotDropPlan Plan;

        /// <summary>是否发生了合并（成功且目标有单位时；便捷属性委托 Plan）。</summary>
        public bool IsMerge => Success && Plan.IsMerge;

        /// <summary>合并或换槽后的目标槽占用单位（便捷属性委托 Plan）。</summary>
        public BattleUnit? ResultUnit => Success ? Plan.ResultUnit : null;

        private SlotDropResult(
            bool success,
            SlotDropRejectReason rejectReason,
            string diagnosticMessage,
            SlotDropPlan plan)
        {
            Success = success;
            RejectReason = rejectReason;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
            Plan = plan;
        }

        /// <summary>构造成功结果（携带事务计划）。</summary>
        internal static SlotDropResult Ok(SlotDropPlan plan)
            => new SlotDropResult(true, SlotDropRejectReason.None, string.Empty, plan);

        /// <summary>构造失败结果。</summary>
        internal static SlotDropResult Fail(SlotDropRejectReason reason, string message)
            => new SlotDropResult(false, reason, message, default);
    }

    /// <summary>
    /// 集中维护槽、单位和换槽不变量的深模块（最终方案"核心架构" + 修复阶段 P0）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责：</b>集中维护槽位、单位与换槽不变量。UI、DeckManager、
    /// UnitRegistry 不再分别保存一份"单位在哪里"的状态。</para>
    /// <para><b>原子事务：</b>换槽/合并拆为 <see cref="TryPlanDrop"/>（只读校验）与
    /// <see cref="CommitDrop"/>（一次性提交）。任何失败都返回失败，槽位不变化。</para>
    /// <para><b>只读快照：</b>通过 <see cref="Snapshot"/> 暴露只读
    /// <see cref="UnitSlotSnapshot"/>，禁止 UI 直接修改内部槽位状态。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部规则服务与表现层使用。</para>
    /// </remarks>
    internal sealed class UnitSlotBoard
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[UnitSlotBoard]";

        // ====================================================================
        // 槽位存储
        // ====================================================================

        /// <summary>
        /// 全部槽位（id → 槽位）。槽位本身固定，只更新 Occupant 占用状态。
        /// </summary>
        private readonly Dictionary<int, UnitSlot> _slotsById = new Dictionary<int, UnitSlot>();

        /// <summary>下一单位 ID（BattleUnit 分配，递增，不复用）。</summary>
        private int _nextUnitId = 1;

        /// <summary>最大等级（合并上限，来自 UnitLevelConfigSnapshot.MaxLevel）。</summary>
        private readonly int _maxLevel;

        /// <summary>是否已初始化（Initialize 后置位）。</summary>
        private bool _initialized;

        /// <summary>槽位修订号（每次 CommitDrop/ReplaceReserve 递增，用于事务版本冲突校验）。</summary>
        private int _revision;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造槽位面板。
        /// </summary>
        /// <param name="maxLevel">最大等级（合并上限，来自 UnitLevelConfigSnapshot.MaxLevel）。</param>
        internal UnitSlotBoard(int maxLevel)
        {
            _maxLevel = maxLevel > 0 ? maxLevel : RecruitDefinitions.MaxLevel;
        }

        // ====================================================================
        // 初始化 —— 生成固定战场槽与待上场槽
        // ====================================================================

        /// <summary>
        /// 生成固定槽位：玩家/对手战场槽（由地图可建造格）与待上场槽（由 reserveSlotCount）。
        /// </summary>
        /// <param name="mapData">地图数据，用于枚举可建造格。</param>
        /// <param name="reserveSlotCount">每侧待上场槽数量。</param>
        /// <remarks>
        /// <para>槽位 ID 确定性分配：先玩家战场槽、再对手战场槽、再玩家待上场槽、
        /// 再对手待上场槽，各按确定性顺序递增。</para>
        /// <para><b>幂等：</b>重复调用会清空并重建全部槽位（开局调用一次）。</para>
        /// </remarks>
        internal void Initialize(MapData mapData, int reserveSlotCount)
        {
            if (mapData == null)
            {
                throw new ArgumentNullException(nameof(mapData));
            }

            _slotsById.Clear();

            int nextId = 0;
            // 玩家战场槽。
            nextId = CreateBattleSlotsForSide(mapData, isPlayerSide: true, nextId);
            // 对手战场槽。
            nextId = CreateBattleSlotsForSide(mapData, isPlayerSide: false, nextId);
            // 玩家待上场槽。
            nextId = CreateReserveSlotsForSide(isPlayerSide: true, reserveSlotCount, nextId);
            // 对手待上场槽。
            CreateReserveSlotsForSide(isPlayerSide: false, reserveSlotCount, nextId);

            _initialized = true;
            _revision = 0;
        }

        /// <summary>为指定阵营创建战场槽（按地图遍历顺序确定性分配 ID）。</summary>
        private int CreateBattleSlotsForSide(MapData mapData, bool isPlayerSide, int nextId)
        {
            for (int y = 0; y < mapData.Height; y++)
            {
                for (int x = 0; x < mapData.Width; x++)
                {
                    if (mapData.IsBuildableForSide(isPlayerSide, x, y))
                    {
                        var slotId = new UnitSlotId(nextId, isPlayerSide, SlotZone.Battle, new GridPosition(x, y));
                        _slotsById[nextId] = new UnitSlot(slotId, occupant: null);
                        nextId++;
                    }
                }
            }

            return nextId;
        }

        /// <summary>为指定阵营创建待上场槽（固定数量）。</summary>
        private int CreateReserveSlotsForSide(bool isPlayerSide, int reserveSlotCount, int nextId)
        {
            int count = reserveSlotCount > 0 ? reserveSlotCount : RecruitDefinitions.ReserveSlotCount;
            for (int index = 0; index < count; index++)
            {
                var slotId = new UnitSlotId(nextId, isPlayerSide, SlotZone.Reserve, default);
                _slotsById[nextId] = new UnitSlot(slotId, occupant: null);
                nextId++;
            }

            return nextId;
        }

        // ====================================================================
        // 只读查询
        // ====================================================================

        /// <summary>是否已初始化。</summary>
        internal bool IsInitialized => _initialized;

        /// <summary>槽位总数。</summary>
        internal int SlotCount => _slotsById.Count;

        /// <summary>当前修订号（诊断/版本冲突校验用）。</summary>
        internal int Revision => _revision;

        /// <summary>获取指定槽位标识的槽位视图。</summary>
        internal UnitSlot GetSlot(UnitSlotId slotId)
        {
            return GetSlotById(slotId.Id);
        }

        /// <summary>按固定 ID 获取槽位视图。</summary>
        internal UnitSlot GetSlotById(int id)
        {
            return _slotsById.TryGetValue(id, out UnitSlot slot) ? slot : default;
        }

        /// <summary>固定槽位 ID 是否存在（有效性校验）。</summary>
        internal bool ContainsSlotId(int id)
        {
            return _slotsById.ContainsKey(id);
        }

        /// <summary>获取指定阵营指定区域的槽位视图列表（按固定 ID 顺序）。</summary>
        internal IReadOnlyList<UnitSlot> GetSlots(bool isPlayerSide, SlotZone zone)
        {
            var result = new List<UnitSlot>();
            foreach (UnitSlot slot in _slotsById.Values)
            {
                if (slot.SlotId.Side == isPlayerSide && slot.SlotId.Zone == zone)
                {
                    result.Add(slot);
                }
            }

            return result;
        }

        /// <summary>获取全部槽位视图列表（按固定 ID 顺序，供控制器冷却写回遍历）。</summary>
        internal IReadOnlyList<UnitSlot> GetAllSlots()
        {
            var result = new List<UnitSlot>(_slotsById.Count);
            foreach (UnitSlot slot in _slotsById.Values)
            {
                result.Add(slot);
            }

            return result;
        }

        /// <summary>按战场格子坐标查找玩家侧槽位。</summary>
        internal bool TryFindBattleSlot(bool isPlayerSide, GridPosition gridPosition, out UnitSlotId slotId)
        {
            foreach (UnitSlot slot in _slotsById.Values)
            {
                if (slot.SlotId.Side == isPlayerSide
                    && slot.SlotId.Zone == SlotZone.Battle
                    && slot.SlotId.GridPosition == gridPosition)
                {
                    slotId = slot.SlotId;
                    return true;
                }
            }

            slotId = UnitSlotId.Invalid;
            return false;
        }

        /// <summary>获取槽位当前占用单位；空槽返回 null。</summary>
        internal BattleUnit? GetOccupant(UnitSlotId slotId)
        {
            return GetSlotById(slotId.Id).Occupant;
        }

        /// <summary>
        /// 更新指定槽位占用单位的攻击冷却（修复 P0：下场时写回 BattleUnit）。
        /// </summary>
        /// <param name="slotId">固定槽位 ID。</param>
        /// <param name="lastAttackTimeMs">上次攻击时间戳（毫秒）。</param>
        /// <returns>true=找到并更新；false=槽位不存在或为空。</returns>
        /// <remarks>
        /// 下场时把 <see cref="SoldierBase.LastAttackTimeMs"/> 写回 BattleUnit，
        /// 保证"上下场不刷新攻击冷却"。
        /// </remarks>
        internal bool UpdateOccupantCooldown(int slotId, long lastAttackTimeMs)
        {
            if (!_slotsById.TryGetValue(slotId, out UnitSlot slot) || !slot.Occupant.HasValue)
            {
                return false;
            }

            BattleUnit updated = slot.Occupant.Value.WithAttackCooldown(lastAttackTimeMs);
            _slotsById[slotId] = new UnitSlot(slot.SlotId, updated);
            _revision++;
            return true;
        }

        /// <summary>
        /// 按局内单位 ID 更新其所在槽位的攻击冷却（修复 P0：战斗内实时冷却同步）。
        /// </summary>
        /// <param name="unitId">局内单位权威 ID。</param>
        /// <param name="lastAttackTimeMs">实时攻击冷却（毫秒）。</param>
        /// <returns>true=找到并更新；false=该单位不在任何槽位。</returns>
        internal bool UpdateOccupantCooldownByUnitId(int unitId, long lastAttackTimeMs)
        {
            foreach (KeyValuePair<int, UnitSlot> pair in _slotsById)
            {
                BattleUnit? occupant = pair.Value.Occupant;
                if (occupant.HasValue && occupant.Value.UnitId == unitId)
                {
                    return UpdateOccupantCooldown(pair.Key, lastAttackTimeMs);
                }
            }

            return false;
        }

        // ====================================================================
        // 换槽 / 合并 —— 便捷入口：Plan + Commit 一次完成（供测试/简化调用方）
        // ====================================================================

        /// <summary>
        /// 便捷执行一次拖放：Plan（只读校验）成功后立即 Commit 提交槽位状态。
        /// </summary>
        /// <param name="sourceSlotId">源槽位标识。</param>
        /// <param name="targetSlotId">目标槽位标识。</param>
        /// <returns>
        /// 成功时 <see cref="SlotDropResult.Plan"/> 携带已提交的计划；失败时携带拒绝原因。
        /// </returns>
        /// <remarks>
        /// <para>用于不涉及战斗实例准备的纯槽位操作（测试、表现层只改槽位场景）。
        /// 战斗流程应使用 <see cref="TryPlanDrop"/> + <see cref="CommitDrop"/> 分离调用，
        /// 以便 BattleInputController 在两者之间准备战斗实例。</para>
        /// </remarks>
        internal SlotDropResult DropUnit(UnitSlotId sourceSlotId, UnitSlotId targetSlotId)
        {
            SlotDropResult result = TryPlanDrop(sourceSlotId, targetSlotId);
            if (!result.Success)
            {
                return result;
            }

            if (!CommitDrop(result.Plan))
            {
                return SlotDropResult.Fail(
                    SlotDropRejectReason.InvalidSource,
                    "提交槽位失败（版本冲突）");
            }

            return result;
        }

        // ====================================================================
        // 换槽 / 合并 —— 阶段 1：TryPlanDrop（只读校验，不修改状态）
        // ====================================================================

        /// <summary>
        /// 尝试为一次拖放生成换槽/合并事务计划。只读校验，不修改任何状态。
        /// </summary>
        /// <param name="sourceSlotId">源槽位标识。</param>
        /// <param name="targetSlotId">目标槽位标识。</param>
        /// <returns>
        /// 成功时 <see cref="SlotDropResult.Plan"/> 携带完整事务计划，供
        /// <see cref="CommitDrop"/> 提交；失败时携带拒绝原因，不修改任何状态。
        /// </returns>
        /// <remarks>
        /// <para><b>校验顺序（最终方案 + 修复）：</b></para>
        /// <list type="number">
        /// <item>已初始化、源/目标槽合法、源非空、源目标不同。</item>
        /// <item><b>跨阵营检查（修复）：</b>无论目标是否为空，先检查
        ///   <c>sourceSlot.Side == sourceUnit.Side == targetSlot.Side</c>。
        ///   空目标槽跨阵营也返回 <see cref="SlotDropRejectReason.CrossSide"/>。</item>
        /// <item>目标为空 → 换槽计划（源移动到目标，源槽变空）。</item>
        /// <item>目标有单位 → 合并条件检查（同兵种/同等级/未满级）。</item>
        /// </list>
        /// </remarks>
        internal SlotDropResult TryPlanDrop(UnitSlotId sourceSlotId, UnitSlotId targetSlotId)
        {
            if (!_initialized)
            {
                return SlotDropResult.Fail(SlotDropRejectReason.InvalidSource, "UnitSlotBoard 未初始化");
            }

            if (!sourceSlotId.IsValid || !_slotsById.ContainsKey(sourceSlotId.Id))
            {
                return SlotDropResult.Fail(SlotDropRejectReason.InvalidSource, $"源槽 {sourceSlotId} 非法");
            }

            if (!targetSlotId.IsValid || !_slotsById.ContainsKey(targetSlotId.Id))
            {
                return SlotDropResult.Fail(SlotDropRejectReason.InvalidTarget, $"目标槽 {targetSlotId} 非法");
            }

            if (sourceSlotId.Id == targetSlotId.Id)
            {
                return SlotDropResult.Fail(SlotDropRejectReason.SameSlot, "源槽与目标槽相同");
            }

            UnitSlot sourceSlot = _slotsById[sourceSlotId.Id];
            BattleUnit? sourceUnit = sourceSlot.Occupant;
            if (!sourceUnit.HasValue)
            {
                return SlotDropResult.Fail(SlotDropRejectReason.SourceEmpty, $"源槽 {sourceSlotId} 为空");
            }

            UnitSlot targetSlot = _slotsById[targetSlotId.Id];
            BattleUnit? targetUnit = targetSlot.Occupant;

            // 跨阵营检查：无论目标是否为空，源单位与两个槽位必须同阵营。
            // 修复 P0：空目标槽跨阵营移动也拦截（原实现只在目标有单位时检查）。
            if (sourceUnit.Value.Side != sourceSlot.SlotId.Side
                || sourceUnit.Value.Side != targetSlot.SlotId.Side)
            {
                return SlotDropResult.Fail(
                    SlotDropRejectReason.CrossSide,
                    $"跨阵营移动被拦截：sourceUnit.Side={sourceUnit.Value.Side} " +
                    $"sourceSlot.Side={sourceSlot.SlotId.Side} targetSlot.Side={targetSlot.SlotId.Side}");
            }

            // 空目标槽：换槽计划。
            if (!targetUnit.HasValue)
            {
                var movePlan = new SlotDropPlan(
                    sourceSlotId: sourceSlotId,
                    targetSlotId: targetSlotId,
                    isMerge: false,
                    resultUnit: sourceUnit,
                    consumedSourceUnit: null,
                    sourceBefore: sourceUnit,
                    targetBefore: null,
                    boardRevision: _revision);
                return SlotDropResult.Ok(movePlan);
            }

            // 有目标单位：检查合并条件。
            BattleUnit source = sourceUnit.Value;
            BattleUnit target = targetUnit.Value;

            if (source.Kind != target.Kind || source.SoldierType != target.SoldierType)
            {
                return SlotDropResult.Fail(
                    SlotDropRejectReason.TargetMismatch,
                    $"合并要求同兵种（source={source.SoldierText} target={target.SoldierText}）");
            }

            if (source.Level != target.Level)
            {
                return SlotDropResult.Fail(
                    SlotDropRejectReason.TargetMismatch,
                    $"合并要求同等级（source Lv={source.Level} target Lv={target.Level}）");
            }

            if (target.Level >= _maxLevel)
            {
                return SlotDropResult.Fail(
                    SlotDropRejectReason.MaxLevelReached,
                    $"目标单位已满级（Lv={target.Level}，最大 {_maxLevel}）");
            }

            // 满足合并条件：目标升一级（保留目标冷却），源消失。
            BattleUnit merged = target.WithLevel(target.Level + 1);
            var mergePlan = new SlotDropPlan(
                sourceSlotId: sourceSlotId,
                targetSlotId: targetSlotId,
                isMerge: true,
                resultUnit: merged,
                consumedSourceUnit: source,
                sourceBefore: source,
                targetBefore: target,
                boardRevision: _revision);
            return SlotDropResult.Ok(mergePlan);
        }

        // ====================================================================
        // 换槽 / 合并 —— 阶段 2：CommitDrop（一次性提交）
        // ====================================================================

        /// <summary>
        /// 按事务计划一次性提交槽位状态（版本冲突校验 + 替换占用）。
        /// </summary>
        /// <param name="plan">由 <see cref="TryPlanDrop"/> 生成的事务计划。</param>
        /// <returns>true=提交成功；false=版本冲突（BoardRevision 不匹配）。</returns>
        /// <remarks>
        /// <para><b>版本冲突校验：</b>提交时校验 <see cref="SlotDropPlan.BoardRevision"/>
        /// 等于当前 <see cref="Revision"/>。若期间发生其他槽位修改，提交失败，
        /// 由调用方返回失败（事务版本冲突拒绝原因）。</para>
        /// <para><b>提交语义：</b>IsMerge 时目标槽写结果单位、源槽清空；否则源槽清空、
        /// 目标槽写源单位。本方法不触发任何 C# 事件（事实由控制器发布）。</para>
        /// </remarks>
        internal bool CommitDrop(SlotDropPlan plan)
        {
            if (!_initialized)
            {
                return false;
            }

            // 版本冲突校验：计划生成后槽位被其他事务修改则拒绝。
            if (plan.BoardRevision != _revision)
            {
                return false;
            }

            if (plan.IsMerge)
            {
                _slotsById[plan.TargetSlotId.Id] = new UnitSlot(plan.TargetSlotId, plan.ResultUnit);
                _slotsById[plan.SourceSlotId.Id] = new UnitSlot(plan.SourceSlotId, occupant: null);
            }
            else
            {
                _slotsById[plan.TargetSlotId.Id] = new UnitSlot(plan.TargetSlotId, plan.ResultUnit);
                _slotsById[plan.SourceSlotId.Id] = new UnitSlot(plan.SourceSlotId, occupant: null);
            }

            _revision++;
            return true;
        }

        /// <summary>
        /// 回滚一次已提交的换槽/合并事务，恢复计划生成前的槽位快照（修复 P0 原子事务）。
        /// </summary>
        /// <param name="plan">已提交的事务计划。</param>
        /// <returns>true=回滚成功。</returns>
        /// <remarks>
        /// <para>供 <see cref="BattleInputController"/> 在 CommitRuntime 抛错时调用：
        /// 把源槽恢复为 <see cref="SlotDropPlan.SourceBefore"/>、目标槽恢复为
        /// <see cref="SlotDropPlan.TargetBefore"/>，并递增修订号。</para>
        /// </remarks>
        internal bool RollbackDrop(SlotDropPlan plan)
        {
            if (!_initialized)
            {
                return false;
            }

            _slotsById[plan.SourceSlotId.Id] = new UnitSlot(plan.SourceSlotId, plan.SourceBefore);
            _slotsById[plan.TargetSlotId.Id] = new UnitSlot(plan.TargetSlotId, plan.TargetBefore);
            _revision++;
            return true;
        }

        // ====================================================================
        // 征兵替换（只处理待上场槽）
        // ====================================================================

        /// <summary>
        /// 征兵替换：清除指定阵营的全部待上场单位（不论等级），用新批次填满待上场槽。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="newBatch">新生成的 1 级批次（由 RecruitManager 生成）。</param>
        /// <returns>
        /// true=替换成功；false=批次数量与待上场槽数量不一致（征兵必须填满全部待上场槽）。
        /// </returns>
        /// <remarks>
        /// <para><b>只处理待上场槽（最终方案）：</b>征兵只处理 Reserve 槽，绝不影响 Battle 槽。</para>
        /// <para><b>严格要求批次数量等于槽位数（修复 P0）：</b>批次数量与待上场槽数量
        /// 不一致时返回 false，不修改任何状态（"征兵必须填满全部待上场槽"）。</para>
        /// <para><b>扣费与校验由调用方（BattleInputController）完成：</b>本方法只做状态替换，
        /// 失败时调用方不会调用本方法（"失败不清槽、不扣费"）。</para>
        /// </remarks>
        internal bool ReplaceReserve(bool isPlayerSide, IReadOnlyList<BattleUnit> newBatch)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{LogTag} UnitSlotBoard 未初始化");
            }

            IReadOnlyList<UnitSlot> reserveSlots = GetSlots(isPlayerSide, SlotZone.Reserve);

            // 严格要求批次数量等于槽位数（征兵必须填满全部待上场槽）。
            if (newBatch == null || newBatch.Count != reserveSlots.Count)
            {
                return false;
            }

            // 先构造完整新状态，再一次性提交（修复 P0：避免部分修改）。
            var updated = new Dictionary<int, UnitSlot>(_slotsById);
            for (int i = 0; i < reserveSlots.Count; i++)
            {
                UnitSlot slot = reserveSlots[i];
                updated[slot.SlotId.Id] = new UnitSlot(slot.SlotId, newBatch[i]);
            }

            // 一次性替换待上场槽状态。
            foreach (KeyValuePair<int, UnitSlot> pair in updated)
            {
                _slotsById[pair.Key] = pair.Value;
            }

            _revision++;
            return true;
        }

        // ====================================================================
        // 单位 ID 分配
        // ====================================================================

        /// <summary>
        /// 分配下一个局内单位 ID（递增，不复用）。
        /// </summary>
        internal int AllocateUnitId()
        {
            int id = _nextUnitId;
            _nextUnitId++;
            return id;
        }

        // ====================================================================
        // 快照
        // ====================================================================

        /// <summary>
        /// 生成槽位只读快照，供 UI / 表现层 / 测试消费。
        /// </summary>
        /// <returns>只读槽位快照。</returns>
        internal UnitSlotSnapshot Snapshot()
        {
            return new UnitSlotSnapshot(this);
        }

        // ====================================================================
        // 清理
        // ====================================================================

        /// <summary>
        /// 战斗结束清理：清空全部槽位，标记未初始化。
        /// </summary>
        /// <remarks>幂等。不写存档；重开由 Factory 新建本实例。</remarks>
        internal void GameOver()
        {
            _slotsById.Clear();
            _initialized = false;
            _revision = 0;
        }
    }

    // ========================================================================
    // 槽位只读快照
    // ========================================================================

    /// <summary>
    /// 槽位只读快照：禁止 UI 直接修改内部槽位状态，只提供只读查询委托。
    /// </summary>
    /// <remarks>
    /// <para>只读视图：本快照不持有可变槽位引用，只提供对 UnitSlotBoard 的只读
    /// 查询委托。调用方只能查询，不能修改。</para>
    /// </remarks>
    internal readonly struct UnitSlotSnapshot
    {
        /// <summary>只读查询委托（指向 UnitSlotBoard）。</summary>
        private readonly UnitSlotBoard _board;

        /// <summary>构造只读快照。</summary>
        internal UnitSlotSnapshot(UnitSlotBoard board)
        {
            _board = board;
        }

        /// <summary>按固定 ID 获取槽位视图（无效 ID 返回 default）。</summary>
        internal UnitSlot GetSlotById(int id) => _board.GetSlotById(id);

        /// <summary>获取指定阵营指定区域的槽位列表（按固定 ID 顺序）。</summary>
        internal IReadOnlyList<UnitSlot> GetSlots(bool isPlayerSide, SlotZone zone)
            => _board.GetSlots(isPlayerSide, zone);

        /// <summary>获取全部槽位视图（按固定 ID 顺序，供控制器冷却写回遍历）。</summary>
        internal IReadOnlyList<UnitSlot> GetAllSlots()
            => _board.GetAllSlots();
    }
}
