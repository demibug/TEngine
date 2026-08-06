namespace GameBattle
{
    // ============================================================================
    // 任务 3.6：GridCell —— 规范化格子属性值对象
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节）：
    //   表示规范化格子属性，不保存 Unity GameObject。封装格子类型（通道/可建造/阻挡）
    //   与可建造阵营等业务语义，供 MapData.GetCell 返回。
    //
    //   还原工程 grid[x][y] 元素为字符串 "kind_lane"（kind: 0=通道, 1=可建造, 2=阻挡；
    //   lane: 1=玩家, 0=对手）。适配层把该字符串一次性解析为本类型，业务层不再接触
    //   嵌套数组或字符串编码。
    //
    // 不变量：
    //   1. 不可变值类型（struct）。
    //   2. 不持有 Unity 对象或表现引用。
    //   3. 格子类型与阵营语义由枚举显式表达，不暴露原始字符串编码。
    // ============================================================================

    /// <summary>
    /// 地图格子类型：通道、可建造、阻挡。
    /// </summary>
    /// <remarks>
    /// <para>对应还原工程 <c>grid[x][y]</c> 元素字符串 "kind_lane" 的 kind 维度：</para>
    /// <list type="bullet">
    /// <item><c>Passage</c>：kind=0，通道（可行走，不可建造）。</item>
    /// <item><c>Buildable</c>：kind=1，可建造格。</item>
    /// <item><c>Blocked</c>：kind=2，阻挡格（不可行走、不可建造）。</item>
    /// </list>
    /// </remarks>
    public enum GridCellKind
    {
        /// <summary>通道（可行走，不可建造）。对应 kind=0。</summary>
        Passage = 0,

        /// <summary>可建造格。对应 kind=1。</summary>
        Buildable = 1,

        /// <summary>阻挡格（不可行走、不可建造）。对应 kind=2。</summary>
        Blocked = 2,
    }

    /// <summary>
    /// 可建造格所属阵营。
    /// </summary>
    /// <remarks>
    /// <para>对应还原工程 <c>grid[x][y]</c> 元素字符串 "kind_lane" 的 lane 维度：
    /// lane=1 为玩家方，lane=0 为对手方。只有 <see cref="GridCellKind.Buildable"/> 格才有阵营归属。</para>
    /// </remarks>
    public enum BuildableSide
    {
        /// <summary>对手方可建造格（lane=0）。</summary>
        Opponent = 0,

        /// <summary>玩家方可建造格（lane=1）。</summary>
        Player = 1,

        /// <summary>无阵营归属（非可建造格使用）。</summary>
        None = -1,
    }

    /// <summary>
    /// 规范化地图格子属性值对象，不保存 Unity GameObject。
    /// </summary>
    /// <remarks>
    /// <para>适配层把还原工程 <c>grid[x][y]</c> 的字符串编码 "kind_lane" 一次性解析为本类型，
    /// 业务层只通过 <see cref="MapData.GetCell"/> 获取本类型实例，不再接触嵌套数组或原始字符串。</para>
    /// <para>不可变值类型，线程安全；不持有表现引用。</para>
    /// </remarks>
    public readonly struct GridCell
    {
        /// <summary>
        /// 格子类型。
        /// </summary>
        public readonly GridCellKind Kind;

        /// <summary>
        /// 可建造格所属阵营；非可建造格为 <see cref="BuildableSide.None"/>。
        /// </summary>
        public readonly BuildableSide Side;

        /// <summary>
        /// 构造一个格子属性。
        /// </summary>
        /// <param name="kind">格子类型。</param>
        /// <param name="side">可建造格所属阵营；非可建造格应传 <see cref="BuildableSide.None"/>。</param>
        public GridCell(GridCellKind kind, BuildableSide side)
        {
            Kind = kind;
            Side = kind == GridCellKind.Buildable ? side : BuildableSide.None;
        }

        /// <summary>
        /// 是否为通道（可行走）。
        /// </summary>
        public bool IsPassage => Kind == GridCellKind.Passage;

        /// <summary>
        /// 是否为可建造格。
        /// </summary>
        public bool IsBuildable => Kind == GridCellKind.Buildable;

        /// <summary>
        /// 是否为阻挡格。
        /// </summary>
        public bool IsBlocked => Kind == GridCellKind.Blocked;

        /// <summary>
        /// 指定阵营是否可在本格建造。
        /// </summary>
        /// <param name="playerSide">true 表示玩家方，false 表示对手方。</param>
        /// <returns>本格为可建造格且阵营匹配时返回 true。</returns>
        public bool IsBuildableForSide(bool playerSide)
        {
            if (Kind != GridCellKind.Buildable)
            {
                return false;
            }

            return playerSide ? Side == BuildableSide.Player : Side == BuildableSide.Opponent;
        }

        /// <summary>
        /// 是否可行走（A* 路径只允许通过通道格）。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 <c>MapData.findPath</c> 中 <c>map[x][y] !== '0_0' &amp;&amp; map[x][y] !== '0_1'</c>
        /// 的反向判定：kind=0 的通道格可行走，其余 kind 不可行走。</para>
        /// </remarks>
        public bool IsWalkable => Kind == GridCellKind.Passage;

        /// <summary>
        /// 判断两个格子是否相等。
        /// </summary>
        public bool Equals(GridCell other) => Kind == other.Kind && Side == other.Side;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GridCell other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ((int)Kind * 397) ^ (int)Side;

        /// <summary>
        /// 相等运算符。
        /// </summary>
        public static bool operator ==(GridCell left, GridCell right) => left.Equals(right);

        /// <summary>
        /// 不等运算符。
        /// </summary>
        public static bool operator !=(GridCell left, GridCell right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"{Kind}_{Side}";
    }
}
