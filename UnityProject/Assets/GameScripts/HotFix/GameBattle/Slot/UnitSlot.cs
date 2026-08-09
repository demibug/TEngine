namespace GameBattle
{
    // ============================================================================
    // 槽位领域：UnitSlot —— 固定槽位及其占用状态
    // ----------------------------------------------------------------------------
    // 职责（最终方案"固定槽位"一节）：
    //   表示一个拥有固定 UnitSlotId 的槽位，以及当前占用它的 BattleUnit。
    //   空槽的 OccupantUnitId 为 InvalidUnitId（-1）。
    //
    // 与 UnitSlotBoard 的关系：
    //   本类型是 UnitSlotBoard 暴露给外部（UI、表现层、测试）的只读槽位视图，
    //   携带固定槽位标识与当前占用单位快照。单位迁移只改变槽位的占用状态，
    //   不改变槽位本身的标识与区域。
    // ============================================================================

    /// <summary>
    /// 固定槽位及其占用状态的只读视图。
    /// </summary>
    /// <remarks>
    /// <para><b>固定槽位（最终方案）：</b>每个槽位拥有单局内固定不变的
    /// <see cref="SlotId"/>。单位卡在槽位间迁移，槽位本身不移动。</para>
    /// <para><b>空槽：</b>没有单位占用时 <see cref="OccupantUnitId"/> 为
    /// <see cref="InvalidUnitId"/>，<see cref="Occupant"/> 为 null。</para>
    /// </remarks>
    internal readonly struct UnitSlot
    {
        /// <summary>无效单位 ID 哨兵，用于表达空槽。</summary>
        public const int InvalidUnitId = -1;

        /// <summary>固定槽位标识（单局内唯一、不变）。</summary>
        public readonly UnitSlotId SlotId;

        /// <summary>当前占用单位的权威数据；空槽为 null。</summary>
        public readonly BattleUnit? Occupant;

        /// <summary>当前占用单位 ID；空槽为 <see cref="InvalidUnitId"/>。</summary>
        public int OccupantUnitId => Occupant.HasValue ? Occupant.Value.UnitId : InvalidUnitId;

        /// <summary>是否为空槽。</summary>
        public bool IsEmpty => !Occupant.HasValue;

        /// <summary>
        /// 构造槽位视图。
        /// </summary>
        /// <param name="slotId">固定槽位标识。</param>
        /// <param name="occupant">当前占用单位（可空）。</param>
        internal UnitSlot(UnitSlotId slotId, BattleUnit? occupant)
        {
            SlotId = slotId;
            Occupant = occupant;
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"{SlotId} Occupant={(Occupant.HasValue ? Occupant.Value.ToString() : "Empty")}";
    }
}
