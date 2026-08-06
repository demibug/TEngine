using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.6：MapData —— 扁平封装布局的地图数据，只暴露坐标 API
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节 / specs/battle-config-snapshot/spec.md
    //   "Map coordinates have one canonical representation"）：
    //   还原工程原始地图数据为列优先 grid[x][y]（x=列、y=行），仅在适配层读取一次。
    //   C# 业务层 MUST NOT 暴露嵌套数组布局，统一通过 GetCell(x, y)、IsInside(x, y)
    //   等坐标 API 访问。本类型是业务层的唯一地图访问入口。
    //
    //   内部布局采用扁平一维数组 _cells[x * Height + y]（列优先），与还原工程 grid[x][y]
    //   的列优先语义一致；但该数组为 private，外部无法直接访问，避免布局泄漏。
    //
    //   本类型只负责格子查询、边界判断与可建造/可行走语义；A* 寻路不是本期必需（design.md
    //   "A* 不是首期必需路径"），路径数据由适配层在构造时注入。
    //
    // 不变量：
    //   1. 构造后不可变（除预留的 game-over 状态标记外，格子数据不变）。
    //   2. 不暴露嵌套数组或一维原始数组；外部只能经 GetCell/IsInside 等坐标 API 访问。
    //   3. 源 grid[x][y] 列优先布局只在适配层（FromColumnMajorGrid / 构造）读取一次，
    //      之后业务层不再接触嵌套数组。
    //   4. 非对称地图（Width != Height）不会发生转置：GetCell(x, y) 严格按
    //      x=列、y=行 索引到 _cells[x * Height + y]。
    // ============================================================================

    /// <summary>
    /// 扁平封装布局的地图数据，只暴露 <see cref="GetCell"/>、<see cref="IsInside"/>
    /// 和可建造/路径 API，不暴露嵌套数组布局。
    /// </summary>
    /// <remarks>
    /// <para><b>坐标约定（决策 0.5 / spec "Map coordinates have one canonical representation"）：</b></para>
    /// <para>还原工程原始地图数据为列优先 <c>grid[x][y]</c>（x 表示列、y 表示行），仅在适配层读取一次。
    /// 本类型 X 为列索引（0..Width-1），Y 为行索引（0..Height-1），与该语义一致。</para>
    ///
    /// <para><b>内部布局（不对外暴露）：</b></para>
    /// <para>内部采用扁平一维数组 <c>_cells[x * Height + y]</c>（列优先），与还原工程 grid[x][y]
    /// 列优先语义一致。该数组为 private，外部只能通过 <see cref="GetCell"/> / <see cref="IsInside"/>
    /// 等坐标 API 访问，避免嵌套数组布局泄漏到业务层。</para>
    ///
    /// <para><b>非对称地图不转置：</b></para>
    /// <para>对于 Width != Height 的非对称地图（如 8 列 × 10 行），<see cref="GetCell"/> 严格按
    /// x=列、y=行 索引，不发生转置。测试用非对称地图证明该不变量。</para>
    ///
    /// <para><b>A* 寻路：</b></para>
    /// <para>A* 不是本期必需路径（design.md）。路径数据由适配层在构造时注入，
    /// 本类型只提供 <see cref="GetPlayerPath"/> / <see cref="GetOpponentPath"/> 只读访问。</para>
    /// </remarks>
    public sealed class MapData
    {
        // ====================================================================
        // 内部扁平布局（private，不对外暴露）
        // ====================================================================

        /// <summary>
        /// 扁平一维格子数组，列优先存储：<c>_cells[x * Height + y]</c>。
        /// <para>该数组为 private，外部无法直接访问，避免嵌套/扁平布局泄漏到业务层。
        /// 列优先顺序与还原工程 grid[x][y] 一致，确保非对称地图不转置。</para>
        /// </summary>
        private readonly GridCell[] _cells;

        /// <summary>
        /// 玩家路径点（只读副本，由适配层注入）。
        /// </summary>
        private readonly IReadOnlyList<GridPosition> _playerPath;

        /// <summary>
        /// 对手路径点（只读副本，由适配层注入）。
        /// </summary>
        private readonly IReadOnlyList<GridPosition> _opponentPath;

        // ====================================================================
        // 只读尺寸与入口
        // ====================================================================

        /// <summary>
        /// 地图列数（x 维度，0..Width-1）。
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// 地图行数（y 维度，0..Height-1）。
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// 地图索引（用于多地图标识）。
        /// </summary>
        public int MapIndex { get; }

        /// <summary>
        /// 玩家路径起点坐标。
        /// </summary>
        public GridPosition PlayerStart { get; }

        /// <summary>
        /// 玩家路径终点坐标。
        /// </summary>
        public GridPosition PlayerEnd { get; }

        /// <summary>
        /// 对手路径起点坐标。
        /// </summary>
        public GridPosition OpponentStart { get; }

        /// <summary>
        /// 对手路径终点坐标。
        /// </summary>
        public GridPosition OpponentEnd { get; }

        // ====================================================================
        // 构造（仅在适配层读取源布局一次）
        // ====================================================================

        /// <summary>
        /// 构造地图数据，直接以扁平一维格子数组初始化。
        /// </summary>
        /// <param name="cells">
        /// 扁平一维格子数组，必须列优先排列，长度为 <paramref name="width"/> * <paramref name="height"/>。
        /// 索引顺序：<c>cells[x * height + y]</c>。
        /// </param>
        /// <param name="width">地图列数（x 维度）。</param>
        /// <param name="height">地图行数（y 维度）。</param>
        /// <param name="mapIndex">地图索引。</param>
        /// <param name="playerStart">玩家路径起点。</param>
        /// <param name="playerEnd">玩家路径终点。</param>
        /// <param name="opponentStart">对手路径起点。</param>
        /// <param name="opponentEnd">对手路径终点。</param>
        /// <param name="playerPath">玩家路径点序列（由适配层注入，不可变）。</param>
        /// <param name="opponentPath">对手路径点序列（由适配层注入，不可变）。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="cells"/>、<paramref name="playerPath"/> 或 <paramref name="opponentPath"/> 为 null。
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="cells"/> 长度不等于 <paramref name="width"/> * <paramref name="height"/>，
        /// 或尺寸非正，或路径起终点越界。
        /// </exception>
        /// <remarks>
        /// <para>本构造函数为内部适配层入口，业务代码不直接调用；后续 Provider/Normalizer
        /// （task 3.3）会通过 <see cref="FromColumnMajorGrid"/> 把还原工程 grid[x][y]
        /// 或 Luban 数据规范化为本类型。</para>
        /// <para>源 grid[x][y] 列优先布局只在 <see cref="FromColumnMajorGrid"/> 读取一次，
        /// 之后业务层经坐标 API 访问，不再接触嵌套数组。</para>
        /// </remarks>
        internal MapData(
            GridCell[] cells,
            int width,
            int height,
            int mapIndex,
            GridPosition playerStart,
            GridPosition playerEnd,
            GridPosition opponentStart,
            GridPosition opponentEnd,
            IReadOnlyList<GridPosition> playerPath,
            IReadOnlyList<GridPosition> opponentPath)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            if (playerPath == null)
            {
                throw new ArgumentNullException(nameof(playerPath));
            }

            if (opponentPath == null)
            {
                throw new ArgumentNullException(nameof(opponentPath));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException(
                    $"地图尺寸必须为正：width={width} height={height}",
                    width <= 0 ? nameof(width) : nameof(height));
            }

            if (cells.Length != width * height)
            {
                throw new ArgumentException(
                    $"cells 长度 {cells.Length} 不等于 width*height = {width * height}",
                    nameof(cells));
            }

            Width = width;
            Height = height;
            MapIndex = mapIndex;
            _cells = cells;

            PlayerStart = playerStart;
            PlayerEnd = playerEnd;
            OpponentStart = opponentStart;
            OpponentEnd = opponentEnd;
            _playerPath = playerPath;
            _opponentPath = opponentPath;

            // 校验起终点在地图范围内。
            if (!IsInside(playerStart))
            {
                throw new ArgumentException(
                    $"playerStart {playerStart} 越界（width={width} height={height}）",
                    nameof(playerStart));
            }

            if (!IsInside(playerEnd))
            {
                throw new ArgumentException(
                    $"playerEnd {playerEnd} 越界（width={width} height={height}）",
                    nameof(playerEnd));
            }

            if (!IsInside(opponentStart))
            {
                throw new ArgumentException(
                    $"opponentStart {opponentStart} 越界（width={width} height={height}）",
                    nameof(opponentStart));
            }

            if (!IsInside(opponentEnd))
            {
                throw new ArgumentException(
                    $"opponentEnd {opponentEnd} 越界（width={width} height={height}）",
                    nameof(opponentEnd));
            }
        }

        // ====================================================================
        // 适配层入口：从列优先 grid[x][y] 构造（只在此处读取源布局一次）
        // ====================================================================

        /// <summary>
        /// 从还原工程列优先 <c>grid[x][y]</c> 布局构造地图数据。
        /// </summary>
        /// <param name="columnMajorGrid">
        /// 列优先嵌套数组：<c>columnMajorGrid[x][y]</c>，x 为列索引（0..width-1），y 为行索引（0..height-1）。
        /// 外层数组长度为 width，每个内层数组长度为 height。
        /// </param>
        /// <param name="cellDecoder">
        /// 把源格子编码（如字符串 "kind_lane"）解析为 <see cref="GridCell"/> 的适配函数。
        /// 只在此处调用一次，之后业务层不再接触原始编码。
        /// </param>
        /// <param name="mapIndex">地图索引。</param>
        /// <param name="playerStart">玩家路径起点。</param>
        /// <param name="playerEnd">玩家路径终点。</param>
        /// <param name="opponentStart">对手路径起点。</param>
        /// <param name="opponentEnd">对手路径终点。</param>
        /// <param name="playerPath">玩家路径点序列。</param>
        /// <param name="opponentPath">对手路径点序列。</param>
        /// <returns>规范化 <see cref="MapData"/>。</returns>
        /// <exception cref="ArgumentNullException">
        /// 任一参数为 null。
        /// </exception>
        /// <exception cref="ArgumentException">
        /// 嵌套数组非矩形或尺寸非正。
        /// </exception>
        /// <remarks>
        /// <para><b>本方法是源 grid[x][y] 列优先布局的唯一读取点（决策 0.5）。</b></para>
        /// <para>还原工程原始地图数据为列优先 grid[x][y]（x=列、y=行），只在此适配层读取一次
        /// 并扁平化为内部一维数组 <c>_cells[x * Height + y]</c>。之后业务层经
        /// <see cref="GetCell"/> / <see cref="IsInside"/> 访问，不再接触嵌套数组，
        /// 保证源布局不泄漏、非对称地图不转置。</para>
        /// <para>Luban 数据同样经适配函数（或等价构造路径）规范化为本类型，
        /// 字段等价通过对照验证（task 3.13）。</para>
        /// <para>本方法为 internal，仅供同程序集的适配层（BattleConfigNormalizer/Provider，task 3.3）
        /// 与测试程序集（<c>InternalsVisibleTo</c>）调用，其他业务程序集不得直接构造 MapData，
        /// 只能经适配层获取实例后通过坐标 API 访问。</para>
        /// </remarks>
        internal static MapData FromColumnMajorGrid(
            IReadOnlyList<IReadOnlyList<string>> columnMajorGrid,
            Func<string, GridCell> cellDecoder,
            int mapIndex,
            GridPosition playerStart,
            GridPosition playerEnd,
            GridPosition opponentStart,
            GridPosition opponentEnd,
            IReadOnlyList<GridPosition> playerPath,
            IReadOnlyList<GridPosition> opponentPath)
        {
            if (columnMajorGrid == null)
            {
                throw new ArgumentNullException(nameof(columnMajorGrid));
            }

            if (cellDecoder == null)
            {
                throw new ArgumentNullException(nameof(cellDecoder));
            }

            if (playerPath == null)
            {
                throw new ArgumentNullException(nameof(playerPath));
            }

            if (opponentPath == null)
            {
                throw new ArgumentNullException(nameof(opponentPath));
            }

            int width = columnMajorGrid.Count;
            if (width <= 0)
            {
                throw new ArgumentException(
                    $"列优先 grid 外层长度为 {width}，必须为正",
                    nameof(columnMajorGrid));
            }

            int height = columnMajorGrid[0]?.Count ?? 0;
            if (height <= 0)
            {
                throw new ArgumentException(
                    $"列优先 grid 内层长度为 {height}，必须为正",
                    nameof(columnMajorGrid));
            }

            // 校验矩形并解码到扁平数组。只在此处遍历源嵌套数组一次。
            var cells = new GridCell[width * height];
            for (int x = 0; x < width; x++)
            {
                IReadOnlyList<string> column = columnMajorGrid[x];
                if (column == null)
                {
                    throw new ArgumentException(
                        $"列 {x} 为 null",
                        nameof(columnMajorGrid));
                }

                if (column.Count != height)
                {
                    throw new ArgumentException(
                        $"列 {x} 长度 {column.Count} 不等于首列长度 {height}，嵌套数组非矩形",
                        nameof(columnMajorGrid));
                }

                for (int y = 0; y < height; y++)
                {
                    // 列优先扁平索引：x * Height + y，与还原工程 grid[x][y] 语义一致。
                    cells[x * height + y] = cellDecoder(column[y]);
                }
            }

            return new MapData(
                cells,
                width,
                height,
                mapIndex,
                playerStart,
                playerEnd,
                opponentStart,
                opponentEnd,
                playerPath,
                opponentPath);
        }

        // ====================================================================
        // 坐标 API（业务层唯一访问入口，不暴露嵌套数组）
        // ====================================================================

        /// <summary>
        /// 判断坐标是否在地图范围内。
        /// </summary>
        /// <param name="x">列索引。</param>
        /// <param name="y">行索引。</param>
        /// <returns>在 [0,Width) × [0,Height) 范围内返回 true。</returns>
        public bool IsInside(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        /// <summary>
        /// 判断坐标是否在地图范围内。
        /// </summary>
        /// <param name="position">坐标。</param>
        /// <returns>在范围内返回 true。</returns>
        public bool IsInside(GridPosition position) => IsInside(position.X, position.Y);

        /// <summary>
        /// 获取指定坐标的格子属性。
        /// </summary>
        /// <param name="x">列索引。</param>
        /// <param name="y">行索引。</param>
        /// <returns>对应坐标的 <see cref="GridCell"/>。</returns>
        /// <exception cref="ArgumentOutOfRangeException">坐标越界。</exception>
        /// <remarks>
        /// <para>本方法是业务层获取格子属性的唯一入口，不暴露内部一维或嵌套数组。</para>
        /// <para>索引语义严格为 x=列、y=行，与还原工程 grid[x][y] 一致；非对称地图不转置。</para>
        /// </remarks>
        public GridCell GetCell(int x, int y)
        {
            if (!IsInside(x, y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"坐标 ({x}, {y}) 越界（width={Width} height={Height}）");
            }

            // 列优先扁平索引：x * Height + y。
            return _cells[x * Height + y];
        }

        /// <summary>
        /// 获取指定坐标的格子属性。
        /// </summary>
        /// <param name="position">坐标。</param>
        /// <returns>对应坐标的 <see cref="GridCell"/>。</returns>
        public GridCell GetCell(GridPosition position) => GetCell(position.X, position.Y);

        // ====================================================================
        // 可建造 API
        // ====================================================================

        /// <summary>
        /// 指定坐标是否为指定阵营的可建造格。
        /// </summary>
        /// <param name="playerSide">true 表示玩家方，false 表示对手方。</param>
        /// <param name="x">列索引。</param>
        /// <param name="y">行索引。</param>
        /// <returns>在范围内且为可建造格且阵营匹配时返回 true；越界返回 false。</returns>
        public bool IsBuildableForSide(bool playerSide, int x, int y)
        {
            if (!IsInside(x, y))
            {
                return false;
            }

            return GetCell(x, y).IsBuildableForSide(playerSide);
        }

        /// <summary>
        /// 指定坐标是否为指定阵营的可建造格。
        /// </summary>
        /// <param name="playerSide">true 表示玩家方，false 表示对手方。</param>
        /// <param name="position">坐标。</param>
        /// <returns>可建造且阵营匹配时返回 true。</returns>
        public bool IsBuildableForSide(bool playerSide, GridPosition position)
            => IsBuildableForSide(playerSide, position.X, position.Y);

        // ====================================================================
        // 路径 API（只读访问，路径由适配层注入）
        // ====================================================================

        /// <summary>
        /// 获取玩家路径点（只读）。
        /// </summary>
        /// <returns>玩家路径点只读列表。</returns>
        /// <remarks>
        /// <para>路径数据由适配层在构造时注入（A* 不是本期必需路径，design.md）。
        /// 返回 <see cref="IReadOnlyList{T}"/> 不暴露内部可变集合。</para>
        /// </remarks>
        public IReadOnlyList<GridPosition> GetPlayerPath() => _playerPath;

        /// <summary>
        /// 获取对手路径点（只读）。
        /// </summary>
        /// <returns>对手路径点只读列表。</returns>
        public IReadOnlyList<GridPosition> GetOpponentPath() => _opponentPath;

        /// <summary>
        /// 按阵营获取路径点（只读）。
        /// </summary>
        /// <param name="playerSide">true 返回玩家路径，false 返回对手路径。</param>
        /// <returns>对应阵营的路径点只读列表。</returns>
        public IReadOnlyList<GridPosition> GetPathForSide(bool playerSide)
            => playerSide ? _playerPath : _opponentPath;
    }
}
