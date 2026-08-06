using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Map
{
    /// <summary>
    /// GridPosition、GridCell、MapData 单元测试（task 3.6）。
    /// </summary>
    /// <remarks>
    /// <para>验证 spec battle-config-snapshot "Map coordinates have one canonical representation"：</para>
    /// <list type="bullet">
    /// <item>GridPosition 是值类型（struct），包含 X/Y 坐标。</item>
    /// <item>GridCell 封装格子属性（可建造、路径、可行走、阻挡、阵营）。</item>
    /// <item>MapData 只暴露 GetCell(x, y)、IsInside(x, y) 和可建造/路径 API。</item>
    /// <item>内部布局不暴露嵌套数组（grid[x][y] 只在适配层 FromColumnMajorGrid 读取一次）。</item>
    /// <item>用非对称地图（8 列 × 10 行，宽≠高）证明源 grid[x][y] 列优先布局未发生转置。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class MapDataTests
    {
        // ====================================================================
        // 测试用非对称地图常量（8 列 × 10 行，宽≠高）
        // ====================================================================

        /// <summary>
        /// 测试地图列数（x 维度）。8 != 10，确保非对称。
        /// </summary>
        private const int AsymWidth = 8;

        /// <summary>
        /// 测试地图行数（y 维度）。10 != 8，确保非对称。
        /// </summary>
        private const int AsymHeight = 10;

        /// <summary>
        /// 构造一个 8×10 非对称测试地图的列优先 grid[x][y] 源数据。
        /// </summary>
        /// <remarks>
        /// <para>使用与还原工程一致的 "kind_lane" 字符串编码：
        /// kind: 0=通道, 1=可建造, 2=阻挡；lane: 1=玩家, 0=对手。</para>
        /// <para>本测试地图故意在特定坐标放置可辨识的格子，用于断言 GetCell(x, y)
        /// 严格按 x=列、y=行 索引，不发生转置。</para>
        /// <para>布局概要（x=列 0..7，y=行 0..9）：</para>
        /// <list type="bullet">
        /// <item>(0, 0) = "0_1" 通道（玩家 lane）</item>
        /// <item>(1, 0) = "1_1" 玩家可建造</item>
        /// <item>(0, 1) = "1_0" 对手可建造</item>
        /// <item>(7, 9) = "2_0" 阻挡</item>
        /// <item>(7, 0) = "0_0" 通道（对手 lane）</item>
        /// <item>(0, 9) = "0_0" 通道</item>
        /// <item>其余默认 "0_1" 通道或 "2_0" 阻挡</item>
        /// </list>
        /// </remarks>
        private static IReadOnlyList<IReadOnlyList<string>> BuildAsymmetricColumnMajorGrid()
        {
            // 外层 length = Width = 8（列），内层 length = Height = 10（行）。
            var grid = new List<List<string>>(AsymWidth);
            for (int x = 0; x < AsymWidth; x++)
            {
                var column = new List<string>(AsymHeight);
                for (int y = 0; y < AsymHeight; y++)
                {
                    // 默认阻挡
                    column.Add("2_0");
                }

                grid.Add(column);
            }

            // 标记点（与 GetCell 转置断言配合）。
            grid[0][0] = "0_1"; // 通道，玩家 lane
            grid[1][0] = "1_1"; // 玩家可建造（列 1, 行 0）
            grid[0][1] = "1_0"; // 对手可建造（列 0, 行 1）
            grid[7][0] = "0_0"; // 通道，对手 lane
            grid[0][9] = "0_0"; // 通道
            grid[7][9] = "2_0"; // 阻挡
            // 中间留若干通道
            grid[4][5] = "0_1";
            grid[5][5] = "1_1"; // 玩家可建造（列 5, 行 5）

            var readonlyGrid = new List<IReadOnlyList<string>>(grid.Count);
            for (int i = 0; i < grid.Count; i++)
            {
                readonlyGrid.Add(grid[i]);
            }

            return readonlyGrid;
        }

        /// <summary>
        /// 把源 "kind_lane" 字符串解码为 GridCell（与还原工程 MapData.js 语义一致）。
        /// </summary>
        private static GridCell DecodeCell(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return new GridCell(GridCellKind.Blocked, BuildableSide.None);
            }

            string[] parts = code.Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int kind) || !int.TryParse(parts[1], out int lane))
            {
                return new GridCell(GridCellKind.Blocked, BuildableSide.None);
            }

            GridCellKind cellKind = kind switch
            {
                0 => GridCellKind.Passage,
                1 => GridCellKind.Buildable,
                _ => GridCellKind.Blocked,
            };

            BuildableSide side = lane switch
            {
                1 => BuildableSide.Player,
                0 => BuildableSide.Opponent,
                _ => BuildableSide.None,
            };

            return new GridCell(cellKind, side);
        }

        /// <summary>
        /// 构造最小路径占位（A* 非本期必需，路径数据由适配层注入）。
        /// </summary>
        private static IReadOnlyList<GridPosition> EmptyPath => Array.Empty<GridPosition>();

        /// <summary>
        /// 构造测试用 MapData（8×10 非对称）。
        /// </summary>
        private static MapData BuildAsymmetricMapData()
        {
            return MapData.FromColumnMajorGrid(
                BuildAsymmetricColumnMajorGrid(),
                DecodeCell,
                mapIndex: 0,
                playerStart: new GridPosition(0, 8),
                playerEnd: new GridPosition(7, 9),
                opponentStart: new GridPosition(7, 1),
                opponentEnd: new GridPosition(0, 0),
                playerPath: EmptyPath,
                opponentPath: EmptyPath);
        }

        // ====================================================================
        // GridPosition 测试
        // ====================================================================

        [Test]
        [Description("GridPosition 是值类型（struct），包含 X/Y 坐标。")]
        public void GridPosition_IsValueType_WithXAndY()
        {
            Type type = typeof(GridPosition);
            Assert.IsTrue(type.IsValueType, "GridPosition 必须是值类型（struct）。");
            Assert.IsTrue(type.IsValueType && !type.IsEnum, "GridPosition 必须是 struct 而非 enum。");

            var pos = new GridPosition(3, 7);
            Assert.AreEqual(3, pos.X, "X 坐标应为构造值。");
            Assert.AreEqual(7, pos.Y, "Y 坐标应为构造值。");
        }

        [Test]
        [Description("GridPosition 值相等语义：相同 X/Y 相等，不同则不等。")]
        public void GridPosition_ValueEquality()
        {
            var a = new GridPosition(2, 5);
            var b = new GridPosition(2, 5);
            var c = new GridPosition(5, 2);

            Assert.IsTrue(a == b, "相同 X/Y 应相等。");
            Assert.IsTrue(a.Equals(b), "Equals 应返回 true。");
            Assert.IsFalse(a != b, "相同 X/Y 不应不等。");
            Assert.IsFalse(a.Equals(c), "(2,5) 与 (5,2) 不应相等——验证 X/Y 不互换。");
            Assert.IsTrue(a != c, "(2,5) 与 (5,2) 应不等——验证 X/Y 不互换。");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "相等坐标 hash 应一致。");
        }

        // ====================================================================
        // GridCell 测试
        // ====================================================================

        [Test]
        [Description("GridCell 是值类型，封装格子属性（可建造、可行走、阻挡、阵营）。")]
        public void GridCell_EncodesKindAndSide()
        {
            var passage = new GridCell(GridCellKind.Passage, BuildableSide.None);
            Assert.IsTrue(passage.IsPassage, "Passage 类型 IsPassage=true。");
            Assert.IsTrue(passage.IsWalkable, "通道应可行走。");
            Assert.IsFalse(passage.IsBuildable, "通道不可建造。");
            Assert.IsFalse(passage.IsBlocked, "通道非阻挡。");

            var playerBuildable = new GridCell(GridCellKind.Buildable, BuildableSide.Player);
            Assert.IsTrue(playerBuildable.IsBuildable, "Buildable 类型 IsBuildable=true。");
            Assert.IsTrue(playerBuildable.IsBuildableForSide(true), "玩家可建造格对玩家方应可建造。");
            Assert.IsFalse(playerBuildable.IsBuildableForSide(false), "玩家可建造格对对手方不可建造。");
            Assert.IsFalse(playerBuildable.IsWalkable, "可建造格不可行走。");

            var opponentBuildable = new GridCell(GridCellKind.Buildable, BuildableSide.Opponent);
            Assert.IsTrue(opponentBuildable.IsBuildableForSide(false), "对手可建造格对对手方应可建造。");
            Assert.IsFalse(opponentBuildable.IsBuildableForSide(true), "对手可建造格对玩家方不可建造。");

            var blocked = new GridCell(GridCellKind.Blocked, BuildableSide.None);
            Assert.IsTrue(blocked.IsBlocked, "Blocked 类型 IsBlocked=true。");
            Assert.IsFalse(blocked.IsWalkable, "阻挡格不可行走。");
            Assert.IsFalse(blocked.IsBuildable, "阻挡格不可建造。");
        }

        [Test]
        [Description("GridCell 非 Buildable 类型强制 Side=None，避免误用阵营。")]
        public void GridCell_NonBuildable_ForcesNoneSide()
        {
            // 构造时传 Player，但 kind 非 Buildable，应被强制为 None。
            var passage = new GridCell(GridCellKind.Passage, BuildableSide.Player);
            Assert.AreEqual(BuildableSide.None, passage.Side, "非 Buildable 格 Side 应为 None。");
            Assert.IsFalse(passage.IsBuildableForSide(true), "通道格不应可建造。");

            var blocked = new GridCell(GridCellKind.Blocked, BuildableSide.Opponent);
            Assert.AreEqual(BuildableSide.None, blocked.Side, "非 Buildable 格 Side 应为 None。");
        }

        // ====================================================================
        // MapData API 暴露面测试
        // ====================================================================

        [Test]
        [Description("MapData 只暴露坐标 API，不暴露嵌套数组或一维原始数组布局。")]
        public void MapData_DoesNotExposeNestedArrayLayout()
        {
            Type type = typeof(MapData);

            // 确认公共方法集合只包含坐标 API 与只读路径 API，不包含返回嵌套数组或一维原始数组的成员。
            MethodInfo[] publicMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (MethodInfo m in publicMethods)
            {
                // 允许的返回类型：GridCell、bool、IReadOnlyList<GridPosition>、int(string) 等。
                // 禁止返回 GridCell[][] 或 GridCell[]（原始布局）。
                Assert.IsFalse(
                    m.ReturnType == typeof(GridCell[][]),
                    $"{m.Name} 不得返回 GridCell[][]（嵌套数组布局不对外暴露）。");
                Assert.IsFalse(
                    m.ReturnType == typeof(GridCell[]),
                    $"{m.Name} 不得返回 GridCell[]（一维原始布局不对外暴露）。");
            }

            // 确认不存在返回源嵌套数组或原始一维数组的公共属性。
            PropertyInfo[] publicProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo p in publicProps)
            {
                Assert.IsFalse(
                    p.PropertyType == typeof(GridCell[][]),
                    $"{p.Name} 不得是 GridCell[][] 类型。");
                Assert.IsFalse(
                    p.PropertyType == typeof(GridCell[]),
                    $"{p.Name} 不得是 GridCell[] 类型。");
            }

            // 确认核心坐标 API 存在。
            Assert.IsNotNull(
                type.GetMethod("GetCell", new[] { typeof(int), typeof(int) }),
                "必须暴露 GetCell(int x, int y)。");
            Assert.IsNotNull(
                type.GetMethod("IsInside", new[] { typeof(int), typeof(int) }),
                "必须暴露 IsInside(int x, int y)。");
        }

        [Test]
        [Description("MapData.IsInside 在非对称 8×10 地图上边界正确（x∈0..7, y∈0..9）。")]
        public void IsInside_AsymmetricMap_BoundsCorrect()
        {
            MapData map = BuildAsymmetricMapData();
            Assert.AreEqual(AsymWidth, map.Width, "Width 应为 8（列数）。");
            Assert.AreEqual(AsymHeight, map.Height, "Height 应为 10（行数）。");
            Assert.AreNotEqual(map.Width, map.Height, "测试地图必须非对称（宽≠高），否则无法证明转置。");

            // 边界内
            Assert.IsTrue(map.IsInside(0, 0), "(0,0) 应在范围内。");
            Assert.IsTrue(map.IsInside(7, 9), "(7,9) 应在范围内（最大列,最大行）。");
            Assert.IsTrue(map.IsInside(0, 9), "(0,9) 应在范围内。");
            Assert.IsTrue(map.IsInside(7, 0), "(7,0) 应在范围内。");

            // 边界外
            Assert.IsFalse(map.IsInside(8, 0), "x=8 越界（Width=8）。");
            Assert.IsFalse(map.IsInside(0, 10), "y=10 越界（Height=10）。");
            Assert.IsFalse(map.IsInside(-1, 0), "x=-1 越界。");
            Assert.IsFalse(map.IsInside(0, -1), "y=-1 越界。");
            Assert.IsFalse(map.IsInside(8, 10), "(8,10) 双维越界。");
        }

        // ====================================================================
        // 非对称地图转置证明（核心不变量）
        // ====================================================================

        [Test]
        [Description(
            "非对称 8×10 地图：GetCell(x, y) 严格按 x=列、y=行 索引，源 grid[x][y] " +
            "列优先布局只在 FromColumnMajorGrid 读取一次且未发生转置。")]
        public void AsymmetricMap_GetCell_NoTranspose()
        {
            MapData map = BuildAsymmetricMapData();

            // 关键转置证明：源 grid[1][0] = "1_1"（玩家可建造），grid[0][1] = "1_0"（对手可建造）。
            // 若发生转置，GetCell(1, 0) 会错误返回对手可建造，GetCell(0, 1) 会错误返回玩家可建造。
            GridCell cell10 = map.GetCell(1, 0);
            GridCell cell01 = map.GetCell(0, 1);

            Assert.AreEqual(GridCellKind.Buildable, cell10.Kind, "GetCell(1,0) 应为可建造（源 grid[1][0]='1_1'）。");
            Assert.AreEqual(BuildableSide.Player, cell10.Side, "GetCell(1,0) 应为玩家方（源 grid[1][0]='1_1' lane=1）。");

            Assert.AreEqual(GridCellKind.Buildable, cell01.Kind, "GetCell(0,1) 应为可建造（源 grid[0][1]='1_0'）。");
            Assert.AreEqual(BuildableSide.Opponent, cell01.Side, "GetCell(0,1) 应为对手方（源 grid[0][1]='1_0' lane=0）。");

            // 交叉断言：两者不等，证明 (1,0) 与 (0,1) 未被互换。
            Assert.AreNotEqual(cell10, cell01, "(1,0) 与 (0,1) 格子属性应不同——若相同说明发生转置。");
            Assert.AreNotEqual(cell10.Side, cell01.Side, "(1,0) 与 (0,1) 阵营应不同。");

            // 其余标记点验证（确保非对称行列不被整体翻转）。
            // 源 grid[7][0] = "0_0"（通道，对手 lane），grid[0][9] = "0_0"（通道）。
            Assert.IsTrue(map.GetCell(7, 0).IsPassage, "GetCell(7,0) 应为通道（源 grid[7][0]='0_0'）。");
            Assert.IsTrue(map.GetCell(0, 9).IsPassage, "GetCell(0,9) 应为通道（源 grid[0][9]='0_0'）。");

            // 源 grid[7][9] = "2_0"（阻挡）。
            Assert.IsTrue(map.GetCell(7, 9).IsBlocked, "GetCell(7,9) 应为阻挡（源 grid[7][9]='2_0'）。");

            // 源 grid[5][5] = "1_1"（玩家可建造）。
            GridCell cell55 = map.GetCell(5, 5);
            Assert.AreEqual(GridCellKind.Buildable, cell55.Kind, "GetCell(5,5) 应为可建造。");
            Assert.AreEqual(BuildableSide.Player, cell55.Side, "GetCell(5,5) 应为玩家方。");

            // 源 grid[4][5] = "0_1"（通道，玩家 lane）。
            Assert.IsTrue(map.GetCell(4, 5).IsPassage, "GetCell(4,5) 应为通道。");
        }

        [Test]
        [Description(
            "非对称地图整体扫描：逐格比对 GetCell(x, y) 与源 grid[x][y] 解码结果一致，" +
            "证明 8×10 地图所有 80 格均未发生行列转置或整体翻转。")]
        public void AsymmetricMap_FullScan_NoTranspose()
        {
            IReadOnlyList<IReadOnlyList<string>> source = BuildAsymmetricColumnMajorGrid();
            MapData map = BuildAsymmetricMapData();

            int mismatches = 0;
            for (int x = 0; x < AsymWidth; x++)
            {
                for (int y = 0; y < AsymHeight; y++)
                {
                    GridCell expected = DecodeCell(source[x][y]);
                    GridCell actual = map.GetCell(x, y);
                    if (!expected.Equals(actual))
                    {
                        mismatches++;
                    }
                }
            }

            Assert.AreEqual(0, mismatches,
                $"8×10 地图全部 {AsymWidth * AsymHeight} 格应与源 grid[x][y] 一一对应，不发生转置。");
        }

        [Test]
        [Description("GetCell 越界抛出 ArgumentOutOfRangeException。")]
        public void GetCell_OutOfBounds_Throws()
        {
            MapData map = BuildAsymmetricMapData();
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(8, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(0, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(-1, 0));
        }

        // ====================================================================
        // 可建造 API 测试
        // ====================================================================

        [Test]
        [Description("IsBuildableForSide 在非对称地图上按坐标与阵营正确判定。")]
        public void IsBuildableForSide_CoordinateAndSideCorrect()
        {
            MapData map = BuildAsymmetricMapData();

            // (1,0) 玩家可建造
            Assert.IsTrue(map.IsBuildableForSide(true, 1, 0), "(1,0) 玩家可建造。");
            Assert.IsFalse(map.IsBuildableForSide(false, 1, 0), "(1,0) 对手不可建造。");

            // (0,1) 对手可建造
            Assert.IsFalse(map.IsBuildableForSide(true, 0, 1), "(0,1) 玩家不可建造。");
            Assert.IsTrue(map.IsBuildableForSide(false, 0, 1), "(0,1) 对手可建造。");

            // (5,5) 玩家可建造
            Assert.IsTrue(map.IsBuildableForSide(true, 5, 5), "(5,5) 玩家可建造。");

            // 通道与阻挡格对任一阵营均不可建造
            Assert.IsFalse(map.IsBuildableForSide(true, 0, 0), "(0,0) 通道不可建造。");
            Assert.IsFalse(map.IsBuildableForSide(false, 0, 0), "(0,0) 通道不可建造。");
            Assert.IsFalse(map.IsBuildableForSide(true, 7, 9), "(7,9) 阻挡不可建造。");

            // 越界返回 false（不抛异常）
            Assert.IsFalse(map.IsBuildableForSide(true, 8, 0), "越界 IsBuildableForSide 返回 false。");
            Assert.IsFalse(map.IsBuildableForSide(false, 0, 10), "越界 IsBuildableForSide 返回 false。");
        }

        // ====================================================================
        // 路径 API 测试
        // ====================================================================

        [Test]
        [Description("GetPlayerPath/GetOpponentPath 返回适配层注入的只读路径，不暴露内部可变集合。")]
        public void PathApi_ReturnsReadOnlyInjectedPath()
        {
            var playerPath = new GridPosition[]
            {
                new GridPosition(0, 8),
                new GridPosition(0, 7),
                new GridPosition(1, 7),
            };
            var opponentPath = new GridPosition[]
            {
                new GridPosition(7, 1),
                new GridPosition(7, 2),
            };

            MapData map = MapData.FromColumnMajorGrid(
                BuildAsymmetricColumnMajorGrid(),
                DecodeCell,
                mapIndex: 0,
                playerStart: new GridPosition(0, 8),
                playerEnd: new GridPosition(7, 9),
                opponentStart: new GridPosition(7, 1),
                opponentEnd: new GridPosition(0, 0),
                playerPath: playerPath,
                opponentPath: opponentPath);

            IReadOnlyList<GridPosition> gotPlayer = map.GetPlayerPath();
            IReadOnlyList<GridPosition> gotOpponent = map.GetOpponentPath();

            Assert.AreEqual(playerPath.Length, gotPlayer.Count, "玩家路径长度应与注入一致。");
            Assert.AreEqual(opponentPath.Length, gotOpponent.Count, "对手路径长度应与注入一致。");
            Assert.AreEqual(new GridPosition(0, 8), gotPlayer[0], "玩家路径首点。");
            Assert.AreEqual(new GridPosition(7, 1), gotOpponent[0], "对手路径首点。");

            // GetPathForSide 按阵营返回
            Assert.AreSame(gotPlayer, map.GetPathForSide(true), "GetPathForSide(true) 返回玩家路径。");
            Assert.AreSame(gotOpponent, map.GetPathForSide(false), "GetPathForSide(false) 返回对手路径。");
        }

        // ====================================================================
        // 适配层单次读取证明
        // ====================================================================

        [Test]
        [Description(
            "FromColumnMajorGrid 是源 grid[x][y] 列优先布局的唯一读取点：构造后业务层" +
            "经 GetCell 访问，不再接触嵌套数组；非矩形源数据在适配层即被拒绝。")]
        public void FromColumnMajorGrid_IsSoleAdapter_NonRectangularRejected()
        {
            // 构造非矩形源数据：第二列长度不一致。
            var bad = new List<IReadOnlyList<string>>
            {
                new List<string> { "0_1", "0_1", "0_1" },
                new List<string> { "0_1", "0_1" }, // 长度不一致
            };

            Assert.Throws<ArgumentException>(() =>
                MapData.FromColumnMajorGrid(
                    bad,
                    DecodeCell,
                    mapIndex: 0,
                    playerStart: new GridPosition(0, 0),
                    playerEnd: new GridPosition(1, 2),
                    opponentStart: new GridPosition(1, 0),
                    opponentEnd: new GridPosition(0, 2),
                    playerPath: EmptyPath,
                    opponentPath: EmptyPath));
        }

        [Test]
        [Description("构造时起终点越界被拒绝，保证路径 API 与 IsInside 语义一致。")]
        public void Constructor_OutOfBoundsEndpoints_Rejected()
        {
            Assert.Throws<ArgumentException>(() =>
                MapData.FromColumnMajorGrid(
                    BuildAsymmetricColumnMajorGrid(),
                    DecodeCell,
                    mapIndex: 0,
                    playerStart: new GridPosition(8, 0), // 越界
                    playerEnd: new GridPosition(7, 9),
                    opponentStart: new GridPosition(7, 1),
                    opponentEnd: new GridPosition(0, 0),
                    playerPath: EmptyPath,
                    opponentPath: EmptyPath));
        }
    }
}
