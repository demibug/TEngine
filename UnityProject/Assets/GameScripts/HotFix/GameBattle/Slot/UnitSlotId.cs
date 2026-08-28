namespace GameBattle
{
    // ============================================================================
    // 槽位领域：UnitSlotId —— 单局内固定不变的槽位标识
    // ----------------------------------------------------------------------------
    // 职责（最终方案"固定槽位"一节）：
    //   定义槽位区域、单位种类与固定槽位标识。槽位本身不移动，只有单位在槽位间
    //   迁移。每个槽位拥有单局内固定不变的 SlotId（由 UnitSlotBoard 在初始化时
    //   确定性分配），伴随阵营、区域与战场格子坐标（仅 Battle 槽有效）。
    //
    // 不变量：
    //   1. Id 在单局内唯一且固定不变，绝不因单位迁移而改变。
    //   2. GridPosition 仅对 Battle 槽有意义；Reserve 槽为 default（(0,0) 占位）。
    //   3. 值类型不可变，构造后不可修改。
    // ============================================================================

    /// <summary>
    /// 槽位区域：战场（Battle）或待上场（Reserve）。
    /// </summary>
    /// <remarks>
    /// <para>战场槽对应地图可建造格；待上场槽是局内的固定槽位，与手牌长度无关。</para>
    /// </remarks>
    internal enum SlotZone
    {
        /// <summary>战场槽：单位进入后激活战斗实例。</summary>
        Battle = 0,

        /// <summary>待上场槽：单位处于预备状态，不参与攻击调度。</summary>
        Reserve = 1,
    }

    /// <summary>
    /// 单位种类（本期只支持士兵，为后续武将等扩展预留）。
    /// </summary>
    internal enum UnitKind
    {
        /// <summary>士兵（刀/弓/枪/骑四兵之一）。</summary>
        Soldier = 0,

        /// <summary>武将合成字，只能停留在玩家待上场槽。</summary>
        GeneralPart = 1,

        /// <summary>已合成武将，可进入战场并复用配置指定的战斗原型。</summary>
        General = 2,

        /// <summary>一次性道具，只能停留在待上场槽。</summary>
        Prop = 3,
    }

    internal enum PropType
    {
        None = 0,
        Shovel = 1,
    }

    /// <summary>
    /// 单局内固定不变的槽位标识值对象。
    /// </summary>
    /// <remarks>
    /// <para>由 <see cref="UnitSlotBoard"/> 在初始化时确定性分配 <see cref="Id"/>，
    /// 之后保持不变。单位在槽位间迁移不会改变槽位本身的 <see cref="UnitSlotId"/>。</para>
    /// </remarks>
    internal readonly struct UnitSlotId
    {
        /// <summary>固定槽位标识（单局内唯一、不变）。Invalid 槽为 -1。</summary>
        public readonly int Id;

        /// <summary>阵营：true=玩家方，false=对手方。</summary>
        public readonly bool Side;

        /// <summary>槽位区域（战场 / 待上场）。</summary>
        public readonly SlotZone Zone;

        /// <summary>战场格子坐标（仅 Battle 槽有效；Reserve 槽为 default 占位）。</summary>
        public readonly GridPosition GridPosition;

        /// <summary>是否有效槽位。Invalid 槽为 false。</summary>
        public bool IsValid => Id >= 0;

        /// <summary>无效槽位哨兵。</summary>
        public static readonly UnitSlotId Invalid = new UnitSlotId(-1, true, SlotZone.Reserve, default);

        /// <summary>
        /// 构造固定槽位标识。
        /// </summary>
        /// <param name="id">固定槽位标识（单局内唯一）。</param>
        /// <param name="side">阵营：true=玩家方，false=对手方。</param>
        /// <param name="zone">槽位区域。</param>
        /// <param name="gridPosition">战场格子坐标（仅 Battle 槽有效）。</param>
        public UnitSlotId(int id, bool side, SlotZone zone, GridPosition gridPosition)
        {
            Id = id;
            Side = side;
            Zone = zone;
            GridPosition = gridPosition;
        }

        /// <summary>
        /// 判断两个槽位标识是否相等。
        /// </summary>
        public bool Equals(UnitSlotId other)
            => Id == other.Id && Side == other.Side && Zone == other.Zone
               && GridPosition == other.GridPosition;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is UnitSlotId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Id;
                hash = (hash * 397) ^ Side.GetHashCode();
                hash = (hash * 397) ^ (int)Zone;
                hash = (hash * 397) ^ GridPosition.GetHashCode();
                return hash;
            }
        }

        /// <summary>相等运算符。</summary>
        public static bool operator ==(UnitSlotId left, UnitSlotId right) => left.Equals(right);

        /// <summary>不等运算符。</summary>
        public static bool operator !=(UnitSlotId left, UnitSlotId right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"Slot(Id={Id}, Side={(Side ? "Player" : "Opponent")}, Zone={Zone}, Grid={GridPosition})";
    }
}
