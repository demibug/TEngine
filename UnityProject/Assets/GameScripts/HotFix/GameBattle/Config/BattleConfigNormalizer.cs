using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.3：BattleConfigNormalizer —— 配置规范化器
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节 / decision 6 / specs/battle-config-snapshot/spec.md）：
    //   把不同数据源（JSON 黄金基线 / Luban 生产数据）转换成同一
    //   <see cref="BattleConfigSnapshot"/> 形态和单位，供双源对照。
    //
    //   还原工程原始地图数据为列优先 grid[x][y]（x=列、y=行），仅在适配层（Provider）
    //   读取一次；Normalizer 把字符串编码 "kind_lane" 解析为 GridCell，并构造 MapData。
    //   C# 业务层不暴露嵌套数组布局，统一通过 GetCell(x,y)、IsInside(x,y) 等坐标 API
    //   访问地图（spec "Map coordinates have one canonical representation"）。
    //
    // 不变量：
    //   1. 规范化结果为不可变 BattleConfigSnapshot。
    //   2. 缺失字段在 MissingFieldNotes 中明确标注，不静默补默认值。
    //   3. 坐标适配只在 NormalizeMap 中读取源 grid[x][y] 一次。
    // ============================================================================

    /// <summary>
    /// 把不同配置源的原始数据规范化为不可变 <see cref="BattleConfigSnapshot"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>决策 6 / spec "Configuration source preserves equivalent values"：</b>
    /// 无论配置来自冻结 JSON 还是 Luban，系统必须证明地图、波次、敌人、单位和牌组的
    /// 规范化快照与黄金基线等价。Normalizer 统一转换不同数据源为同一快照形态。</para>
    ///
    /// <para><b>坐标约定（决策 0.5 / spec "Map coordinates have one canonical representation"）：</b>
    /// 还原工程原始地图数据为列优先 grid[x][y]（x=列、y=行），仅在适配层读取一次。
    /// Normalizer 把字符串编码 "kind_lane" 解析为 GridCell，经 MapData.FromColumnMajorGrid
    /// 构造不可变 MapData。业务层不暴露嵌套数组布局。</para>
    /// </remarks>
    internal static class BattleConfigNormalizer
    {
        // ====================================================================
        // 地图格子解码（"kind_lane" -> GridCell）
        // ====================================================================

        /// <summary>
        /// 把还原工程字符串编码 "kind_lane" 解析为 <see cref="GridCell"/>。
        /// </summary>
        /// <param name="code">格子编码，如 "1_0"=本机玩家可建造, "1_1"=对手可建造, "0_*"=通道, "2_*"=阻挡。</param>
        /// <returns>规范化格子属性。</returns>
        /// <remarks>
        /// <para>编码格式来自 <c>MapData.js</c> MAP_BLOCKS 和 <c>golden-battle-bundle.json</c>
        /// cellKindLaneFormat："字符串 'kind_lane'"。源数据的 lane 表示地图半场，
        /// 当前竖屏本机视角下 lane=0 位于下半场，归本机玩家；lane=1 位于上半场，归对手。
        /// '0_*'=路径、'2_*'=装饰"。</para>
        /// <para>kind: 0=通道(Passage), 1=可建造(Buildable), 2=阻挡(Blocked)。
        /// 只有 Buildable 格有阵营归属。</para>
        /// </remarks>
        internal static GridCell DecodeCell(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return new GridCell(GridCellKind.Blocked, BuildableSide.None);
            }

            string[] parts = code.Split('_');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int kind) || !int.TryParse(parts[1], out int lane))
            {
                return new GridCell(GridCellKind.Blocked, BuildableSide.None);
            }

            GridCellKind cellKind = kind switch
            {
                0 => GridCellKind.Passage,
                1 => GridCellKind.Buildable,
                _ => GridCellKind.Blocked,
            };

            BuildableSide side = cellKind == GridCellKind.Buildable
                ? (lane == 0 ? BuildableSide.Player : BuildableSide.Opponent)
                : BuildableSide.None;

            return new GridCell(cellKind, side);
        }

        // ====================================================================
        // 地图规范化
        // ====================================================================

        /// <summary>
        /// 从列优先 grid[x][y] 字符串网格构造 <see cref="MapData"/>（兼容 map0 数据旧入口）。
        /// </summary>
        /// <param name="columnMajorGrid">列优先嵌套字符串数组：columnMajorGrid[x][y]。</param>
        /// <param name="mapIndex">地图索引。</param>
        /// <param name="playerStart">玩家路径起点。</param>
        /// <param name="playerEnd">玩家路径终点。</param>
        /// <param name="opponentStart">对手路径起点。</param>
        /// <param name="opponentEnd">对手路径终点。</param>
        /// <param name="playerPath">玩家路径点序列。</param>
        /// <param name="opponentPath">对手路径点序列。</param>
        /// <returns>规范化 <see cref="MapData"/>（新增运行字段按 map0 数据填充）。</returns>
        /// <remarks>
        /// 旧入口只保留为现有夹具/兼容调用的 map0 adapter；生产 Provider 必须使用
        /// 完整参数重载消费整行地图配置（design.md 决策 1）。
        /// </remarks>
        internal static MapData NormalizeMap(
            IReadOnlyList<IReadOnlyList<string>> columnMajorGrid,
            int mapIndex,
            GridPosition playerStart,
            GridPosition playerEnd,
            GridPosition opponentStart,
            GridPosition opponentEnd,
            IReadOnlyList<GridPosition> playerPath,
            IReadOnlyList<GridPosition> opponentPath)
        {
            return NormalizeMap(
                columnMajorGrid,
                mapIndex,
                playerStart,
                playerEnd,
                opponentStart,
                opponentEnd,
                playerPath,
                opponentPath,
                name: "Map0",
                resourceAddress: "BattleMap0",
                cellWidth: 80,
                cellHeight: 80,
                playerEntry: new GridPosition(playerStart.X, playerStart.Y + 1),
                opponentEntry: new GridPosition(opponentStart.X, opponentStart.Y - 1),
                routeMarkers: Array.Empty<GridPosition>(),
                enemyTypeIndex: 0);
        }

        /// <summary>
        /// 从列优先 grid[x][y] 字符串网格构造完整地图运行快照（生产入口）。
        /// </summary>
        /// <param name="columnMajorGrid">列优先嵌套字符串数组：columnMajorGrid[x][y]。</param>
        /// <param name="mapIndex">地图索引。</param>
        /// <param name="playerStart">玩家路径起点。</param>
        /// <param name="playerEnd">玩家路径终点。</param>
        /// <param name="opponentStart">对手路径起点。</param>
        /// <param name="opponentEnd">对手路径终点。</param>
        /// <param name="playerPath">玩家路径点序列。</param>
        /// <param name="opponentPath">对手路径点序列。</param>
        /// <param name="name">地图诊断名称。</param>
        /// <param name="resourceAddress">Unity/YooAsset 地图资源地址。</param>
        /// <param name="cellWidth">格子像素宽。</param>
        /// <param name="cellHeight">格子像素高。</param>
        /// <param name="playerEntry">玩家入口坐标。</param>
        /// <param name="opponentEntry">对手入口坐标。</param>
        /// <param name="routeMarkers">表现层路径标记。</param>
        /// <param name="enemyTypeIndex">本图敌人类型索引。</param>
        /// <returns>完整地图运行快照。</returns>
        /// <remarks>
        /// <para>源 grid[x][y] 列优先布局只在 <see cref="MapData.FromColumnMajorGrid"/> 读取一次，
        /// 之后业务层经坐标 API 访问，保证源布局不泄漏、非对称地图不转置。</para>
        /// <para>生产 Provider 必须使用本完整入口消费整行地图配置（design.md 决策 1）。</para>
        /// </remarks>
        internal static MapData NormalizeMap(
            IReadOnlyList<IReadOnlyList<string>> columnMajorGrid,
            int mapIndex,
            GridPosition playerStart,
            GridPosition playerEnd,
            GridPosition opponentStart,
            GridPosition opponentEnd,
            IReadOnlyList<GridPosition> playerPath,
            IReadOnlyList<GridPosition> opponentPath,
            string name,
            string resourceAddress,
            int cellWidth,
            int cellHeight,
            GridPosition playerEntry,
            GridPosition opponentEntry,
            IReadOnlyList<GridPosition> routeMarkers,
            int enemyTypeIndex)
        {
            return MapData.FromColumnMajorGrid(
                columnMajorGrid,
                DecodeCell,
                mapIndex,
                playerStart,
                playerEnd,
                opponentStart,
                opponentEnd,
                playerPath,
                opponentPath,
                name,
                resourceAddress,
                cellWidth,
                cellHeight,
                playerEntry,
                opponentEntry,
                routeMarkers,
                enemyTypeIndex);
        }

        // ====================================================================
        // 波次策略规范化
        // ====================================================================

        /// <summary>
        /// 把源 float[][] 生成策略表转换为不可变 IReadOnlyList<IReadOnlyList<float>>。
        /// </summary>
        internal static IReadOnlyList<IReadOnlyList<float>> NormalizeSpawnStrategies(
            IReadOnlyList<IReadOnlyList<float>> source)
        {
            if (source == null)
            {
                return Array.Empty<IReadOnlyList<float>>();
            }

            var result = new List<IReadOnlyList<float>>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                IReadOnlyList<float> inner = source[i];
                result.Add(inner != null ? new List<float>(inner) : new List<float>());
            }

            return result;
        }

        /// <summary>
        /// 把源 float[][] 生成策略表转换为不可变 IReadOnlyList<IReadOnlyList<float>>（接受 List<List<float>> 重载）。
        /// </summary>
        internal static IReadOnlyList<IReadOnlyList<float>> NormalizeSpawnStrategies(
            System.Collections.Generic.List<System.Collections.Generic.List<float>> source)
        {
            if (source == null)
            {
                return Array.Empty<IReadOnlyList<float>>();
            }

            var result = new List<IReadOnlyList<float>>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var inner = source[i];
                result.Add(inner != null ? new List<float>(inner) : new List<float>());
            }

            return result;
        }
    }
}
