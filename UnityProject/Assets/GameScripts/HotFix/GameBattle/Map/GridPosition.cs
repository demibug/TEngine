namespace GameBattle
{
    // ============================================================================
    // 任务 3.6：GridPosition —— 强类型 x/y 坐标值对象
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节 / specs/battle-config-snapshot/spec.md）：
    //   还原工程原始地图数据为列优先 grid[x][y]（x=列、y=行）。为减少行列颠倒风险，
    //   本类型作为强类型 x/y 值对象贯穿业务层，替代裸 int 参数列表。
    //
    //   本类型仅是值对象，不承担布局、尺寸或路径职责；不引用 UnityEngine.Vector2Int，
    //   以保持 GameBattle 纯逻辑层不依赖 UnityEngine 核心模块之外的类型。
    //
    // 不变量：
    //   1. 不可变值类型（struct）。
    //   2. X 表示列索引，Y 表示行索引，语义与还原工程 grid[x][y] 一致。
    //   3. 不内嵌地图尺寸或边界判断——边界由 MapData.IsInside 负责。
    // ============================================================================

    /// <summary>
    /// 强类型地图格子坐标。X 为列索引，Y 为行索引，语义对应还原工程列优先 grid[x][y]。
    /// </summary>
    /// <remarks>
    /// <para>用于在业务层传递坐标时减少行列颠倒风险。本类型是值对象，不持有地图尺寸或边界信息，
    /// 边界判断由 <see cref="MapData.IsInside"/> 负责。</para>
    /// <para>坐标语义（决策 0.5 / spec "Map coordinates have one canonical representation"）：
    /// 还原工程原始地图数据为列优先 <c>grid[x][y]</c>（x 表示列、y 表示行），仅在适配层读取一次。
    /// 本类型 X/Y 命名与该语义保持一致。</para>
    /// </remarks>
    public readonly struct GridPosition
    {
        /// <summary>
        /// 列索引（对应还原工程 grid[x][y] 的 x 维度）。
        /// </summary>
        public readonly int X;

        /// <summary>
        /// 行索引（对应还原工程 grid[x][y] 的 y 维度）。
        /// </summary>
        public readonly int Y;

        /// <summary>
        /// 构造一个坐标。
        /// </summary>
        /// <param name="x">列索引。</param>
        /// <param name="y">行索引。</param>
        public GridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// 按列、行构造坐标的便捷方法，语义等价于 <see cref="GridPosition(int, int)"/>。
        /// </summary>
        /// <param name="x">列索引。</param>
        /// <param name="y">行索引。</param>
        /// <returns>对应坐标。</returns>
        public static GridPosition FromColumnRow(int x, int y) => new GridPosition(x, y);

        /// <summary>
        /// 判断两个坐标是否相等。
        /// </summary>
        public bool Equals(GridPosition other) => X == other.X && Y == other.Y;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GridPosition other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (X * 397) ^ Y;

        /// <summary>
        /// 相等运算符。
        /// </summary>
        public static bool operator ==(GridPosition left, GridPosition right) => left.Equals(right);

        /// <summary>
        /// 不等运算符。
        /// </summary>
        public static bool operator !=(GridPosition left, GridPosition right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"({X}, {Y})";
    }
}
