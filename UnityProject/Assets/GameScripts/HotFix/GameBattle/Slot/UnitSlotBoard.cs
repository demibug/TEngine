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
    // 拖放规则（统一拖放规则：Move / Merge / Swap）：
    //   1. 空目标槽：Move —— 把单位从源槽换到目标槽，源槽变空。
    //      先检查 sourceSlot.Side == sourceUnit.Side == targetSlot.Side（跨阵营空槽拦截）。
    //   2. 有目标单位且满足合并条件：Merge —— 目标单位升一级，源单位消失，源槽变空。
    //   3. 有目标单位但不满足合并条件：Swap —— 两单位互换位置，不消耗任何单位。
    //   4. 合并条件：不同单位、同阵营、同兵种（Kind/SoldierType 权威）、同等级、低于配置最大等级。
    //   5. 合并免费且单次执行，不自动连锁。
    //   6. 合并结果保留目标 UnitId 和目标 SlotId；升级保留目标攻击冷却。
    //   7. 所有拖动行为都不自动补充待上场单位。
    //   8. 征兵只处理待上场槽，绝不影响战场槽；批次数量必须等于槽位数。
    //   9. 武将字（GeneralPart）：单字拖到占用的单字格基础动作永远是 Swap，拖到空格是 Move。
    //      基础动作后按最终槽位布局检测同区域横向有序配方（张|飞/黄|忠 左字+右字）：
    //      只有最终成序且包含本次移动字牌时才在同一事务内合成；最终反序不合成。
    //      合成武将身份复用配方中未拖动字牌的 UnitId，拖动字牌被消耗。
    //   10. General 只是一对相邻字牌的合成态；拖动命中其中任一格时，先把该 General
    //       解散为两个 GeneralPart，再只对命中的源格和目标格执行单格 Move / Swap。
    //   11. 目标 General 同样先解散；一空一占、两个士兵或两个 General 均只处理实际
    //       命中的目标格。换位后仅检查本次移动影响的两个最终落点是否重新满足有序配方。
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

        /// <summary>目标单位不满足合并条件（不同兵种/不同等级等；统一规则下已走 Swap）。</summary>
        TargetMismatch = 6,

        /// <summary>目标单位已满级，不可继续合并（统一规则下已走 Swap）。</summary>
        MaxLevelReached = 7,

        /// <summary>武将字区域限制（历史保留：已允许字牌进出战场，不再产生）。</summary>
        UnitZoneRestricted = 8,

        /// <summary>历史双格整体拖动拒绝原因（保留枚举值兼容，不再产生）。</summary>
        HorizontalPairUnavailable = 9,

        /// <summary>历史双格整体拖动拒绝原因（保留枚举值兼容，不再产生）。</summary>
        GeneralPairWouldSplit = 10,
    }

    /// <summary>
    /// 换槽/合并/互换操作结果（TryPlanDrop 的返回）。
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

        /// <summary>是否发生了互换（成功且目标占用不满足合并条件时；便捷属性委托 Plan）。</summary>
        public bool IsSwap => Success && Plan.IsSwap;

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

        /// <summary>下一槽位 ID。初始化后动态开垦槽从全部固定槽之后递增，不复用。</summary>
        private int _nextSlotId;

        /// <summary>最大等级（合并上限，来自 UnitLevelConfigSnapshot.MaxLevel）。</summary>
        private readonly int _maxLevel;

        /// <summary>启用武将的有序配方索引（左字+右字，反序不命中）。</summary>
        private readonly GeneralCatalogSnapshot _generalCatalog;

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
        internal UnitSlotBoard(int maxLevel, GeneralCatalogSnapshot generalCatalog = null)
        {
            _maxLevel = maxLevel > 0 ? maxLevel : RecruitDefinitions.MaxLevel;
            _generalCatalog = generalCatalog ?? new GeneralCatalogSnapshot(Array.Empty<GeneralConfigSnapshot>());
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
            nextId = CreateReserveSlotsForSide(isPlayerSide: false, reserveSlotCount, nextId);

            _initialized = true;
            _revision = 0;
            _nextSlotId = nextId;
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

            result.Sort((left, right) => left.SlotId.Id.CompareTo(right.SlotId.Id));

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

            result.Sort((left, right) => left.SlotId.Id.CompareTo(right.SlotId.Id));

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

        internal bool TryCommitShovelUse(
            int sourceSlotId,
            GridPosition target,
            out ShovelBoardChange change)
        {
            change = default;
            if (!_initialized
                || !_slotsById.TryGetValue(sourceSlotId, out UnitSlot source)
                || source.SlotId.Zone != SlotZone.Reserve
                || !source.Occupant.HasValue
                || !source.Occupant.Value.IsShovel
                || TryFindBattleSlot(source.SlotId.Side, target, out _))
            {
                return false;
            }

            int revisionBefore = _revision;
            BattleUnit shovel = source.Occupant.Value;
            int slotIdValue = _nextSlotId;
            _nextSlotId++;
            var battleSlotId = new UnitSlotId(
                slotIdValue,
                source.SlotId.Side,
                SlotZone.Battle,
                target);

            _slotsById[sourceSlotId] = new UnitSlot(source.SlotId, occupant: null);
            _slotsById.Add(slotIdValue, new UnitSlot(battleSlotId, occupant: null));
            _revision++;
            change = new ShovelBoardChange(
                source.SlotId,
                shovel,
                battleSlotId,
                revisionBefore,
                _revision);
            return true;
        }

        /// <summary>
        /// 消费待上场农民并为其开垦目标格。农民不是战斗单位，成功后只新增空战场槽。
        /// </summary>
        internal bool TryCommitFarmerUse(
            int sourceSlotId,
            GridPosition target,
            out UnitSlotId addedBattleSlotId)
        {
            addedBattleSlotId = UnitSlotId.Invalid;
            if (!_initialized
                || !_slotsById.TryGetValue(sourceSlotId, out UnitSlot source)
                || source.SlotId.Zone != SlotZone.Reserve
                || !source.Occupant.HasValue
                || source.Occupant.Value.Kind != UnitKind.Farmer
                || TryFindBattleSlot(source.SlotId.Side, target, out _))
            {
                return false;
            }

            int slotIdValue = _nextSlotId;
            _nextSlotId++;
            var battleSlotId = new UnitSlotId(
                slotIdValue,
                source.SlotId.Side,
                SlotZone.Battle,
                target);

            _slotsById[sourceSlotId] = new UnitSlot(source.SlotId, occupant: null);
            _slotsById.Add(slotIdValue, new UnitSlot(battleSlotId, occupant: null));
            _revision++;
            addedBattleSlotId = battleSlotId;
            return true;
        }

        internal bool TryRollbackShovelUse(ShovelBoardChange change)
        {
            if (!change.IsValid
                || _revision != change.RevisionAfter
                || !_slotsById.TryGetValue(change.SourceSlotId.Id, out UnitSlot source)
                || source.Occupant.HasValue
                || !_slotsById.TryGetValue(change.AddedBattleSlotId.Id, out UnitSlot added)
                || added.SlotId != change.AddedBattleSlotId
                || added.Occupant.HasValue)
            {
                return false;
            }

            _slotsById[change.SourceSlotId.Id] = new UnitSlot(change.SourceSlotId, change.Shovel);
            _slotsById.Remove(change.AddedBattleSlotId.Id);
            _revision++;
            return true;
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

            if (slot.Occupant.Value.Kind == UnitKind.General)
            {
                return UpdateOccupantCooldownByUnitId(
                    slot.Occupant.Value.UnitId, lastAttackTimeMs);
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
            var matchingSlotIds = new List<int>(2);
            foreach (KeyValuePair<int, UnitSlot> pair in _slotsById)
            {
                BattleUnit? occupant = pair.Value.Occupant;
                if (occupant.HasValue && occupant.Value.UnitId == unitId)
                {
                    matchingSlotIds.Add(pair.Key);
                }
            }

            for (int index = 0; index < matchingSlotIds.Count; index++)
            {
                int slotId = matchingSlotIds[index];
                UnitSlot slot = _slotsById[slotId];
                BattleUnit unit = slot.Occupant.Value.WithAttackCooldown(lastAttackTimeMs);
                _slotsById[slotId] = new UnitSlot(slot.SlotId, unit);
            }

            if (matchingSlotIds.Count == 0)
            {
                return false;
            }

            _revision++;
            return true;
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
        /// <para><b>校验顺序（统一拖放规则）：</b></para>
        /// <list type="number">
        /// <item>已初始化、源/目标槽合法、源非空、源目标不同。</item>
        /// <item><b>跨阵营检查（修复）：</b>无论目标是否为空，先检查
        ///   <c>sourceSlot.Side == sourceUnit.Side == targetSlot.Side</c>。
        ///   空目标槽跨阵营也返回 <see cref="SlotDropRejectReason.CrossSide"/>。</item>
        /// <item>目标为空 → 基础动作 Move（源移动到目标，源槽变空）。</item>
        /// <item>目标有单位且满足合并条件（士兵同兵种/同等级/未满级）→ 基础动作 Merge。</item>
        /// <item>目标有单位但不满足合并条件 → 基础动作 Swap（两单位互换位置）。
        ///   武将字对占用单字格永远先 Swap。</item>
        /// <item>源为武将字且最终落点在 Battle 时，基础动作后再按最终槽位布局检测
        ///   包含本次移动字牌的横向有序配方；命中则在同一事务内改写为双格 General。
        ///   Reserve 中武将字只执行 Move/Swap，不合成。</item>
        /// <item>源或目标为 General 时，先在虚拟布局中解散为两个 GeneralPart，再按点击格
        ///   执行普通单格 Move / Swap。历史残缺 General 也降级为已有字牌后继续。</item>
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

            return TryPlanOriginalSingleCellDrop(sourceSlotId, targetSlotId);
        }

        /// <summary>
        /// 原版单格拖放：General 先还原为独立字牌，再只处理点击的源格和目标格。
        /// 所有拆将、移动、交换和重新合成均写入同一个事务计划。
        /// </summary>
        private SlotDropResult TryPlanOriginalSingleCellDrop(
            UnitSlotId sourceSlotId,
            UnitSlotId targetSlotId)
        {
            var virtualOccupants = new Dictionary<int, BattleUnit?>();
            var changedSlotIds = new HashSet<int>();
            var disassembledGeneralIds = new HashSet<int>();

            BattleUnit sourceBefore = _slotsById[sourceSlotId.Id].Occupant.Value;
            BattleUnit? targetBefore = _slotsById[targetSlotId.Id].Occupant;
            DisassembleGeneral(sourceBefore, virtualOccupants, changedSlotIds, disassembledGeneralIds);
            if (targetBefore.HasValue)
            {
                DisassembleGeneral(
                    targetBefore.Value, virtualOccupants, changedSlotIds, disassembledGeneralIds);
            }

            BattleUnit? virtualSource = GetVirtualOccupant(virtualOccupants, sourceSlotId);
            BattleUnit? virtualTarget = GetVirtualOccupant(virtualOccupants, targetSlotId);
            if (!virtualSource.HasValue)
            {
                return SlotDropResult.Fail(
                    SlotDropRejectReason.SourceEmpty,
                    $"源槽 {sourceSlotId} 在拆将后为空");
            }

            BattleUnit source = virtualSource.Value;
            BattleUnit? target = virtualTarget;
            SlotDropOperationType operationType;
            if (!target.HasValue)
            {
                operationType = SlotDropOperationType.Move;
                SetVirtualOccupant(virtualOccupants, changedSlotIds, sourceSlotId, null);
                SetVirtualOccupant(virtualOccupants, changedSlotIds, targetSlotId, source);
            }
            else
            {
                BattleUnit targetUnit = target.Value;
                bool canMerge = source.Kind == UnitKind.Soldier
                    && targetUnit.Kind == UnitKind.Soldier
                    && source.SoldierType == targetUnit.SoldierType
                    && source.Level == targetUnit.Level
                    && targetUnit.Level < _maxLevel;
                if (canMerge)
                {
                    operationType = SlotDropOperationType.Merge;
                    SetVirtualOccupant(virtualOccupants, changedSlotIds, sourceSlotId, null);
                    SetVirtualOccupant(
                        virtualOccupants,
                        changedSlotIds,
                        targetSlotId,
                        targetUnit.WithLevel(targetUnit.Level + 1));
                }
                else
                {
                    operationType = SlotDropOperationType.Swap;
                    SetVirtualOccupant(
                        virtualOccupants, changedSlotIds, sourceSlotId, targetUnit);
                    SetVirtualOccupant(
                        virtualOccupants, changedSlotIds, targetSlotId, source);
                }
            }

            // 原版先回调拖动源，再回调被换出的目标：按 target → source 顺序检查。
            bool synthesized = TrySynthesizeAround(
                targetSlotId, source.UnitId, virtualOccupants, changedSlotIds);
            if (target.HasValue)
            {
                synthesized |= TrySynthesizeAround(
                    sourceSlotId, target.Value.UnitId, virtualOccupants, changedSlotIds);
            }

            if (synthesized)
            {
                operationType = SlotDropOperationType.Synthesize;
            }

            var orderedSlotIds = new List<int>(changedSlotIds);
            orderedSlotIds.Sort();
            var mutations = new SlotDropMutation[orderedSlotIds.Count];
            for (int index = 0; index < orderedSlotIds.Count; index++)
            {
                int slotId = orderedSlotIds[index];
                UnitSlot current = _slotsById[slotId];
                mutations[index] = new SlotDropMutation(
                    current.SlotId,
                    current.Occupant,
                    GetVirtualOccupant(virtualOccupants, current.SlotId));
            }

            return SlotDropResult.Ok(new SlotDropPlan(
                sourceSlotId,
                targetSlotId,
                operationType,
                mutations,
                _revision));
        }

        /// <summary>把完整或历史残缺 General 的所有已找到半格还原为独立字牌。</summary>
        private void DisassembleGeneral(
            BattleUnit unit,
            Dictionary<int, BattleUnit?> virtualOccupants,
            HashSet<int> changedSlotIds,
            HashSet<int> disassembledGeneralIds)
        {
            if (unit.Kind != UnitKind.General || !disassembledGeneralIds.Add(unit.UnitId))
            {
                return;
            }

            foreach (UnitSlot slot in _slotsById.Values)
            {
                BattleUnit? occupant = slot.Occupant;
                if (!occupant.HasValue
                    || occupant.Value.Kind != UnitKind.General
                    || occupant.Value.UnitId != unit.UnitId)
                {
                    continue;
                }

                SetVirtualOccupant(
                    virtualOccupants,
                    changedSlotIds,
                    slot.SlotId,
                    occupant.Value.ToGeneralPart());
            }
        }

        private BattleUnit? GetVirtualOccupant(
            Dictionary<int, BattleUnit?> virtualOccupants,
            UnitSlotId slotId)
        {
            return virtualOccupants.TryGetValue(slotId.Id, out BattleUnit? occupant)
                ? occupant
                : _slotsById[slotId.Id].Occupant;
        }

        private static void SetVirtualOccupant(
            Dictionary<int, BattleUnit?> virtualOccupants,
            HashSet<int> changedSlotIds,
            UnitSlotId slotId,
            BattleUnit? occupant)
        {
            virtualOccupants[slotId.Id] = occupant;
            changedSlotIds.Add(slotId.Id);
        }

        /// <summary>只检查一个实际移动落点附近的有序配方，不扫描无关旧布局。</summary>
        private bool TrySynthesizeAround(
            UnitSlotId movedSlotId,
            int movedPartUnitId,
            Dictionary<int, BattleUnit?> virtualOccupants,
            HashSet<int> changedSlotIds)
        {
            if (movedSlotId.Zone != SlotZone.Battle)
            {
                return false;
            }

            BattleUnit? moved = GetVirtualOccupant(virtualOccupants, movedSlotId);
            if (!moved.HasValue || moved.Value.Kind != UnitKind.GeneralPart)
            {
                return false;
            }

            if (TryGetHorizontalNeighbor(movedSlotId, -1, out UnitSlotId leftId)
                && TrySynthesizePair(
                    leftId,
                    movedSlotId,
                    movedPartUnitId,
                    virtualOccupants,
                    changedSlotIds))
            {
                return true;
            }

            return TryGetHorizontalNeighbor(movedSlotId, 1, out UnitSlotId rightId)
                && TrySynthesizePair(
                    movedSlotId,
                    rightId,
                    movedPartUnitId,
                    virtualOccupants,
                    changedSlotIds);
        }

        private bool TrySynthesizePair(
            UnitSlotId leftSlotId,
            UnitSlotId rightSlotId,
            int movedPartUnitId,
            Dictionary<int, BattleUnit?> virtualOccupants,
            HashSet<int> changedSlotIds)
        {
            BattleUnit? leftCandidate = GetVirtualOccupant(virtualOccupants, leftSlotId);
            BattleUnit? rightCandidate = GetVirtualOccupant(virtualOccupants, rightSlotId);
            if (!leftCandidate.HasValue
                || !rightCandidate.HasValue
                || leftCandidate.Value.Kind != UnitKind.GeneralPart
                || rightCandidate.Value.Kind != UnitKind.GeneralPart
                || leftCandidate.Value.Side != rightCandidate.Value.Side)
            {
                return false;
            }

            BattleUnit leftPart = leftCandidate.Value;
            BattleUnit rightPart = rightCandidate.Value;
            GeneralConfigSnapshot definition = _generalCatalog.GetByRecipeOrDefault(
                leftPart.GeneralPartText, rightPart.GeneralPartText);
            if (definition == null)
            {
                return false;
            }

            BattleUnit identityPart = leftPart.UnitId == movedPartUnitId
                ? rightPart
                : leftPart;
            int generalUnitId = identityPart.UnitId;
            int level = Math.Max(leftPart.Level, rightPart.Level);
            long cooldown = identityPart.LastAttackTimeMs;
            BattleUnit leftCell = BattleUnit.CreateGeneralCell(
                generalUnitId,
                leftPart.Side,
                definition,
                0,
                leftPart.UnitId,
                level,
                cooldown);
            BattleUnit rightCell = BattleUnit.CreateGeneralCell(
                generalUnitId,
                rightPart.Side,
                definition,
                1,
                rightPart.UnitId,
                level,
                cooldown);
            SetVirtualOccupant(
                virtualOccupants, changedSlotIds, leftSlotId, leftCell);
            SetVirtualOccupant(
                virtualOccupants, changedSlotIds, rightSlotId, rightCell);
            return true;
        }

        private bool TryGetHorizontalNeighbor(
            UnitSlotId slotId,
            int offset,
            out UnitSlotId neighbor)
        {
            if (offset != -1 && offset != 1)
            {
                neighbor = UnitSlotId.Invalid;
                return false;
            }

            if (slotId.Zone == SlotZone.Battle)
            {
                GridPosition target = new GridPosition(
                    slotId.GridPosition.X + offset,
                    slotId.GridPosition.Y);
                return TryFindBattleSlot(slotId.Side, target, out neighbor);
            }

            IReadOnlyList<UnitSlot> slots = GetSlots(slotId.Side, slotId.Zone);
            for (int index = 0; index < slots.Count; index++)
            {
                if (slots[index].SlotId.Id != slotId.Id)
                {
                    continue;
                }

                int neighborIndex = index + offset;
                if (neighborIndex >= 0 && neighborIndex < slots.Count)
                {
                    neighbor = slots[neighborIndex].SlotId;
                    return true;
                }
                break;
            }

            neighbor = UnitSlotId.Invalid;
            return false;
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
        /// <para><b>提交语义：</b>一次性把源槽写为 <see cref="SlotDropPlan.SourceAfter"/>、
        /// 目标槽写为 <see cref="SlotDropPlan.TargetAfter"/>（Move 源变空目标写入；
        /// Merge 源变空目标升级；Swap 两槽互换）。本方法不触发任何 C# 事件
        /// （事实由控制器发布）。</para>
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

            IReadOnlyList<SlotDropMutation> mutations = plan.Mutations;
            for (int index = 0; index < mutations.Count; index++)
            {
                SlotDropMutation mutation = mutations[index];
                _slotsById[mutation.SlotId.Id] = new UnitSlot(mutation.SlotId, mutation.After);
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

            IReadOnlyList<SlotDropMutation> mutations = plan.Mutations;
            for (int index = 0; index < mutations.Count; index++)
            {
                SlotDropMutation mutation = mutations[index];
                _slotsById[mutation.SlotId.Id] = new UnitSlot(mutation.SlotId, mutation.Before);
            }
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
            _nextSlotId = 0;
        }
    }

    internal readonly struct ShovelBoardChange
    {
        internal readonly UnitSlotId SourceSlotId;
        internal readonly BattleUnit Shovel;
        internal readonly UnitSlotId AddedBattleSlotId;
        internal readonly int RevisionBefore;
        internal readonly int RevisionAfter;
        internal bool IsValid => SourceSlotId.IsValid
                                 && AddedBattleSlotId.IsValid
                                 && Shovel.IsShovel
                                 && RevisionAfter > RevisionBefore;

        internal ShovelBoardChange(
            UnitSlotId sourceSlotId,
            BattleUnit shovel,
            UnitSlotId addedBattleSlotId,
            int revisionBefore,
            int revisionAfter)
        {
            SourceSlotId = sourceSlotId;
            Shovel = shovel;
            AddedBattleSlotId = addedBattleSlotId;
            RevisionBefore = revisionBefore;
            RevisionAfter = revisionAfter;
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
