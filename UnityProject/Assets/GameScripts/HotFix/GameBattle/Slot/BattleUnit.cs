namespace GameBattle
{
    // ============================================================================
    // 槽位领域：BattleUnit —— 局内单位权威数据
    // ----------------------------------------------------------------------------
    // 职责（最终方案"固定槽位"一节）：
    //   BattleUnit 是局内权威数据，描述一个单位卡的兵种、阵营与当前等级。
    //   单位卡在槽位之间迁移，槽位本身不移动；SoldierBase 只是单位位于 BattleSlot
    //   时创建的战斗运行时对象，不能作为待上场单位的数据源。
    //
    // 与 UnitCard 的关系：
    //   UnitCard 是旧牌组模型的不可变牌值对象，本期由本类型替代。本类型保留
    //   局内单位身份（UnitId 递增分配）、兵种、阵营与等级，删除单卡 Cost 与
    //   "卡被消费后补牌"的语义。Level 变成真正的局内单位等级。
    //
    // 不变量：
    //   1. 不可变值类型：UnitId/Side/UnitKind/SoldierType 构造后不可修改。
    //   2. UnitId 由 UnitSlotBoard 递增分配，单局内唯一，不复用旧 ID。
    //   3. Level 由合并/征兵修改，通过返回新副本表达（不就地修改）。
    // ============================================================================

    /// <summary>
    /// 局内单位权威数据（兵种、阵营与等级）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（最终方案）：</b>局内单位权威数据，替代旧 <see cref="UnitCard"/>
    /// 的手牌卡语义。单位在固定槽位之间迁移，本类型描述单位自身，不描述槽位。</para>
    /// <para><b>Level 语义：</b>Level 是真正的局内单位等级，由合并升级或征兵重置。
    /// 修改等级通过 <see cref="WithLevel"/> 返回新副本，保持不可变性。</para>
    /// </remarks>
    internal readonly struct BattleUnit
    {
        /// <summary>局内单位唯一标识（由 UnitSlotBoard 递增分配，不复用）。</summary>
        public readonly int UnitId;

        /// <summary>阵营：true=玩家方，false=对手方。</summary>
        public readonly bool Side;

        /// <summary>单位种类（本期只支持士兵）。</summary>
        public readonly UnitKind Kind;

        /// <summary>兵种类型（刀/弓/枪/骑，仅当 Kind=Soldier 时有效）。</summary>
        public readonly SoldierType SoldierType;

        /// <summary>兵种文字（"刀"/"弓"/"枪"/"骑"，表现层直接使用）。</summary>
        public readonly string SoldierText;

        /// <summary>当前等级（1..MaxLevel）。</summary>
        public readonly int Level;

        /// <summary>
        /// 上次攻击时间戳（毫秒，攻击冷却状态）。
        /// <para>上下场时写回/导入，保证"上下场不刷新攻击冷却"（最终方案）。</para>
        /// </summary>
        public readonly long LastAttackTimeMs;

        public readonly int GeneralIndex;
        public readonly string GeneralName;
        public readonly string GeneralPartText;

        /// <summary>General 双格中的半格序号：0=左字，1=右字；非 General 为 -1。</summary>
        public readonly int GeneralCellIndex;

        /// <summary>
        /// General 半格合成前对应的独立字牌 ID；General 解散时恢复该身份。
        /// GeneralPart 时等于 <see cref="UnitId"/>，普通士兵为 -1。
        /// </summary>
        public readonly int GeneralPartUnitId;

        /// <summary>道具类型；仅当 <see cref="Kind"/> 为 <see cref="UnitKind.Prop"/> 时有效。</summary>
        public readonly PropType PropType;

        public bool IsGeneralPrimaryCell => Kind == UnitKind.General && GeneralCellIndex == 0;
        public bool IsShovel => Kind == UnitKind.Prop && PropType == PropType.Shovel;

        /// <summary>
        /// 构造局内单位权威数据。
        /// </summary>
        /// <param name="unitId">局内单位唯一标识。</param>
        /// <param name="side">阵营：true=玩家方，false=对手方。</param>
        /// <param name="kind">单位种类（士兵）。</param>
        /// <param name="soldierType">兵种类型。</param>
        /// <param name="soldierText">兵种文字。</param>
        /// <param name="level">当前等级（至少 1）。</param>
        /// <param name="lastAttackTimeMs">上次攻击时间戳（毫秒，攻击冷却状态）。</param>
        internal BattleUnit(
            int unitId,
            bool side,
            UnitKind kind,
            SoldierType soldierType,
            string soldierText,
            int level,
            long lastAttackTimeMs = 0L,
            int generalIndex = -1,
            string generalName = "",
            string generalPartText = "",
            int generalCellIndex = -1,
            int generalPartUnitId = -1,
            PropType propType = PropType.None)
        {
            UnitId = unitId;
            Side = side;
            Kind = kind;
            SoldierType = soldierType;
            SoldierText = soldierText ?? string.Empty;
            Level = level > 0 ? level : 1;
            LastAttackTimeMs = lastAttackTimeMs;
            GeneralIndex = generalIndex;
            GeneralName = generalName ?? string.Empty;
            GeneralPartText = generalPartText ?? string.Empty;
            GeneralCellIndex = kind == UnitKind.General ? generalCellIndex : -1;
            GeneralPartUnitId = kind == UnitKind.GeneralPart
                ? unitId
                : kind == UnitKind.General ? generalPartUnitId : -1;
            PropType = kind == UnitKind.Prop ? propType : PropType.None;
        }

        internal static BattleUnit CreateShovel(int unitId, bool side)
            => new BattleUnit(
                unitId,
                side,
                UnitKind.Prop,
                default,
                "铲",
                level: 1,
                propType: PropType.Shovel);

        internal static BattleUnit CreateGeneralPart(
            int unitId,
            bool side,
            string partText,
            int level = 1,
            long lastAttackTimeMs = 0L)
            => new BattleUnit(unitId, side, UnitKind.GeneralPart, default, string.Empty, level,
                lastAttackTimeMs,
                generalPartText: partText);

        internal static BattleUnit CreateGeneral(int unitId, bool side, GeneralConfigSnapshot definition)
            => CreateGeneralCell(unitId, side, definition, 0);

        internal static BattleUnit CreateGeneralCell(
            int unitId,
            bool side,
            GeneralConfigSnapshot definition,
            int cellIndex,
            int generalPartUnitId = -1,
            int level = 1,
            long lastAttackTimeMs = 0L)
            => new BattleUnit(
                unitId,
                side,
                UnitKind.General,
                definition.LogicalSoldierType,
                definition.LogicalSoldierText,
                level,
                lastAttackTimeMs,
                generalIndex: definition.Index,
                generalName: definition.Name,
                generalPartText: definition.PartWords[cellIndex],
                generalCellIndex: cellIndex,
                generalPartUnitId: generalPartUnitId > 0 ? generalPartUnitId : unitId);

        /// <summary>把 General 半格还原为合成前的独立武将字。</summary>
        internal BattleUnit ToGeneralPart()
        {
            int partUnitId = GeneralPartUnitId > 0 ? GeneralPartUnitId : UnitId;
            return CreateGeneralPart(partUnitId, Side, GeneralPartText, Level, LastAttackTimeMs);
        }

        /// <summary>
        /// 返回同单位但等级为 <paramref name="newLevel"/> 的新副本（保留攻击冷却）。
        /// </summary>
        /// <param name="newLevel">新等级（合并升级时 = 当前等级 + 1）。</param>
        /// <returns>等级变更后的新单位副本。</returns>
        /// <remarks>
        /// 合并升级保留目标单位的冷却时间戳（最终方案"上下场不刷新攻击冷却"）。
        /// </remarks>
        internal BattleUnit WithLevel(int newLevel)
            => new BattleUnit(UnitId, Side, Kind, SoldierType, SoldierText, newLevel, LastAttackTimeMs,
                GeneralIndex, GeneralName, GeneralPartText, GeneralCellIndex, GeneralPartUnitId, PropType);

        /// <summary>
        /// 返回同单位但攻击冷却为 <paramref name="lastAttackTimeMs"/> 的新副本。
        /// </summary>
        /// <param name="lastAttackTimeMs">上次攻击时间戳（毫秒，攻击冷却状态）。</param>
        /// <returns>冷却更新后的新单位副本。</returns>
        /// <remarks>
        /// 下场时把 <see cref="SoldierBase.LastAttackTimeMs"/> 写回 BattleUnit，重新上场时导入。
        /// </remarks>
        internal BattleUnit WithAttackCooldown(long lastAttackTimeMs)
            => new BattleUnit(UnitId, Side, Kind, SoldierType, SoldierText, Level, lastAttackTimeMs,
                GeneralIndex, GeneralName, GeneralPartText, GeneralCellIndex, GeneralPartUnitId, PropType);

        /// <inheritdoc/>
        public override string ToString()
            => $"[BattleUnit Id={UnitId} Kind={Kind} Cell={GeneralCellIndex} PartId={GeneralPartUnitId} Text={SoldierText}{GeneralPartText}{GeneralName} " +
               $"Side={(Side ? "Player" : "Opponent")} Lv={Level} Cd={LastAttackTimeMs}]";
    }
}
