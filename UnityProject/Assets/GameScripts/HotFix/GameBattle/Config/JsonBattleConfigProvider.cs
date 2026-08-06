using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.3：JsonBattleConfigProvider —— JSON 黄金测试配置 Provider
    // ----------------------------------------------------------------------------
    // 职责（design.md decision 0.6 / specs/battle-config-snapshot/spec.md
    //   "Configuration source preserves equivalent values"）：
    //   从冻结 JSON 黄金测试 fixture 读取完整配置，作为黄金基线。
    //   生产入口只使用 Luban Provider，JSON Provider 仅编入测试或开发验证边界。
    //
    // 来源证据：
    //   - golden-battle-bundle.json：包含完整 minimalConfig（map/enemy/wave/units/deck/economy/projectile）
    //   - GoldenBattleFixtures.cs：强类型 C# 常量固化黄金数据
    //   - BattleDataCore.js：硬编码数值（enemy HP/speed、wave counts、spawn strategies）
    //   - MapData.js MAP_BLOCKS：列优先 grid[x][y] 地图数据
    //
    // 不变量：
    //   1. JSON Provider 只在测试或开发验证边界使用，生产入口禁止使用。
    //   2. 返回的快照包含完整配置，不产生 MissingFieldNotes。
    //   3. 地图坐标只在 FromColumnMajorGrid 适配层读取一次。
    // ============================================================================

    /// <summary>
    /// JSON 黄金测试配置 Provider，从冻结 fixture 读取完整配置作为黄金基线。
    /// </summary>
    /// <remarks>
    /// <para><b>决策 0.6：</b>冻结 JSON 只作为黄金测试 Oracle，Luban 为最终生产配置源。
    /// 生产入口只使用 Luban Provider，JSON Provider 仅编入测试或开发验证边界。</para>
    ///
    /// <para><b>黄金数据来源：</b>GoldenBattleFixtures.cs 强类型常量 + golden-battle-bundle.json
    /// canonical 清单。数据从还原工程只读导出，禁止修改还原工程来迁就 C# 结果。</para>
    ///
    /// <para><b>本类为 internal：</b>仅供测试程序集（InternalsVisibleTo）和同程序集内部使用，
    /// 不暴露到生产入口。</para>
    /// </remarks>
    internal sealed class JsonBattleConfigProvider : IBattleConfigProvider
    {
        // ====================================================================
        // 黄金地图数据（从 MapData.js MAP_BLOCKS[0] 导出）
        // ====================================================================

        /// <summary>
        /// 黄金 map0 列优先 grid[x][y]（8 列 × 10 行）。
        /// 来源：MapData.js MAP_BLOCKS[0].map / golden-battle-bundle.json minimalConfig.map。
        /// </summary>
        private static readonly string[][] GoldenMap0Grid =
        {
            // x=0 列
            new[] { "0_1", "0_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0" },
            // x=1 列
            new[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0" },
            // x=2 列
            new[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
            // x=3 列
            new[] { "2_1", "1_1", "1_1", "0_1", "0_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
            // x=4 列
            new[] { "2_1", "1_1", "1_1", "0_1", "0_0", "0_0", "0_0", "1_0", "1_0", "2_0" },
            // x=5 列
            new[] { "2_1", "1_1", "1_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
            // x=6 列
            new[] { "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
            // x=7 列
            new[] { "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "0_0", "0_0" },
        };

        // ====================================================================
        // 黄金路径点（map0）
        // ====================================================================

        private static readonly GridPosition GoldenPlayerStart = new GridPosition(0, 8);
        private static readonly GridPosition GoldenPlayerEnd = new GridPosition(7, 9);
        private static readonly GridPosition GoldenOpponentStart = new GridPosition(7, 1);
        private static readonly GridPosition GoldenOpponentEnd = new GridPosition(0, 0);

        // ====================================================================
        // 黄金路径序列（A* 计算结果，长度 17）
        // ====================================================================

        /// <summary>
        /// 黄金玩家路径（map0，A* 从 playerStart(0,8) 到 playerEnd(7,9)）。
        /// 来源：MapData.js findPath + golden-battle-bundle.json playerPathLength=17。
        /// </summary>
        private static readonly GridPosition[] GoldenPlayerPath =
        {
            new GridPosition(0, 8), new GridPosition(1, 8), new GridPosition(2, 8),
            new GridPosition(3, 8), new GridPosition(4, 8), new GridPosition(5, 8),
            new GridPosition(6, 8), new GridPosition(6, 7), new GridPosition(6, 6),
            new GridPosition(6, 5), new GridPosition(6, 4), new GridPosition(6, 3),
            new GridPosition(6, 2), new GridPosition(6, 1), new GridPosition(6, 0),
            new GridPosition(7, 0), new GridPosition(7, 9),
        };

        /// <summary>
        /// 黄金对手路径（map0，A* 从 opponentStart(7,1) 到 opponentEnd(0,0)）。
        /// 来源：MapData.js findPath + golden-battle-bundle.json opponentPathLength=17。
        /// </summary>
        private static readonly GridPosition[] GoldenOpponentPath =
        {
            new GridPosition(7, 1), new GridPosition(6, 1), new GridPosition(5, 1),
            new GridPosition(4, 1), new GridPosition(3, 1), new GridPosition(2, 1),
            new GridPosition(1, 1), new GridPosition(1, 2), new GridPosition(1, 3),
            new GridPosition(1, 4), new GridPosition(1, 5), new GridPosition(1, 6),
            new GridPosition(1, 7), new GridPosition(1, 8), new GridPosition(1, 9),
            new GridPosition(0, 9), new GridPosition(0, 0),
        };

        // ====================================================================
        // 黄金敌人数据（BattleDataCore.js 硬编码）
        // ====================================================================

        private static readonly int[] GoldenHealthByWave =
        {
            10, 11, 57, 44, 39, 92, 138, 200, 291, 421,
            611, 886, 1285, 1863, 2701, 3917, 5680, 8235, 11941, 17315,
        };

        private static readonly float[] GoldenEarlyRoundHealthMultipliers =
        {
            0.6f, 0.6f, 0.6f, 0.6f, 0.7f, 0.7f, 0.7f, 0.8f, 0.8f, 0.8f,
        };

        // ====================================================================
        // 黄金波次数据（waves.json + BattleDataCore.js）
        // ====================================================================

        private static readonly int[] GoldenWaveUnitCounts =
        {
            10, 11, 12, 13, 15, 16, 18, 19, 21, 24,
            26, 29, 31, 35, 38, 42, 46, 51, 56, 61,
        };

        private static readonly int[] GoldenBossWaveNumbers = { 3, 6, 9, 12, 15, 20 };
        private static readonly float[] GoldenBossSpawnChances = { 0.1f, 0.2f, 0.3f, 0.5f, 0.9f, 1f };
        private static readonly int[] GoldenSpawnStrategyWeights = { 5, 2, 3 };

        private static readonly float[][] GoldenSpawnStrategies =
        {
            new float[] { 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1 },
            new float[] { 1.1f,1.2f,1.3f,1.2f,1.3f,1.7f,2,1,1.5f,1,1,1,1,1,1,1,1,1,1,1 },
            new float[] { 1,1,1.5f,1,1.8f,2,1,1,2,1,1,1.3f,1,1,1.4f,1,1,1.5f,1,1 },
        };

        // ====================================================================
        // 黄金单位数据（units.json）
        // ====================================================================

        private static readonly UnitConfigSnapshot[] GoldenUnits =
        {
            new UnitConfigSnapshot(0, "刀", "knife",   1.5f, 3, 0.8f, "单体",     "nearest"),
            new UnitConfigSnapshot(1, "弓", "bow",     3.5f, 2, 0.8f, "单体",     "closest_end"),
            new UnitConfigSnapshot(2, "枪", "pike",    2.5f, 2, 0.8f, "近战枪击", "nearest"),
            new UnitConfigSnapshot(3, "骑", "cavalry", 2.0f, 2, 0.8f, "范围",     "nearest"),
        };

        private static readonly UnitLevelConfigSnapshot GoldenUnitLevel = new UnitLevelConfigSnapshot(
            maxLevel: 3,
            damageLevelMultipliers: new float[] { 1, 1.5f, 2.1f, 2.73f, 3.4125f },
            attackSpeedLevelMultipliers: new float[] { 1, 1.5f, 2.1f, 2.73f, 3.4125f });

        // ====================================================================
        // 黄金经济数据（battle-economy.json + BattleState.js）
        // ====================================================================

        private static readonly EconomyConfigSnapshot GoldenEconomy = new EconomyConfigSnapshot(
            initialGold: 20,
            refreshCostStart: 10,
            refreshCostIncrement: 2,
            unitBaseCost: 1,
            handSize: 5,
            playerMaxHealth: 3,
            opponentMaxHealth: 3);

        // ====================================================================
        // 黄金牌组数据（DeckDefinitions.js）
        // ====================================================================

        private static readonly DeckConfigSnapshot GoldenDeck = new DeckConfigSnapshot(
            minimalMode: true,
            baseSoldierTexts: new string[] { "刀", "弓", "枪", "骑" },
            handSize: 5,
            defaultLevel: 1,
            baseUnitCost: 1);

        // ====================================================================
        // 黄金投射物数据（projectiles.json + ProjectileFactory）
        // ====================================================================

        private static readonly ProjectileConfigSnapshot GoldenProjectile = new ProjectileConfigSnapshot(
            types: new string[] { "SimpleDynamicArrow" },
            primaryType: "SimpleDynamicArrow",
            movementStrategy: "TargetEnemyBezierMovement",
            hitStrategy: "HitEnemyStrategy");

        // ====================================================================
        // IBattleConfigProvider 实现
        // ====================================================================

        /// <summary>
        /// 从黄金 JSON fixture 构造不可变配置快照。
        /// </summary>
        /// <returns>包含完整配置的黄金基线快照。</returns>
        /// <remarks>
        /// <para>黄金 JSON 快照包含完整配置，不产生 MissingFieldNotes。
        /// 所有数值从还原工程只读导出（GoldenBattleFixtures.cs / golden-battle-bundle.json）。</para>
        /// <para>地图坐标只在适配层（MapData.FromColumnMajorGrid）读取源 grid[x][y] 一次，
        /// 之后业务层经坐标 API 访问（spec "Map coordinates have one canonical representation"）。</para>
        /// </remarks>
        public BattleConfigSnapshot GetSnapshot()
        {
            // 构造列优先 grid 的 IReadOnlyList<IReadOnlyList<string>> 适配
            var grid = new IReadOnlyList<string>[GoldenMap0Grid.Length];
            for (int x = 0; x < GoldenMap0Grid.Length; x++)
            {
                grid[x] = GoldenMap0Grid[x];
            }

            MapData map = BattleConfigNormalizer.NormalizeMap(
                grid,
                mapIndex: 0,
                playerStart: GoldenPlayerStart,
                playerEnd: GoldenPlayerEnd,
                opponentStart: GoldenOpponentStart,
                opponentEnd: GoldenOpponentEnd,
                playerPath: GoldenPlayerPath,
                opponentPath: GoldenOpponentPath);

            var enemy = new EnemyConfigSnapshot(
                type: "Mob0",
                mapEnemyTypeIndex: 0,
                speed: 50,
                healthByWave: GoldenHealthByWave,
                earlyRoundHealthMultipliers: GoldenEarlyRoundHealthMultipliers,
                contactDamage: 1);

            // 把 float[][] 转为 IReadOnlyList<IReadOnlyList<float>>
            var strategies = new IReadOnlyList<float>[GoldenSpawnStrategies.Length];
            for (int i = 0; i < GoldenSpawnStrategies.Length; i++)
            {
                strategies[i] = GoldenSpawnStrategies[i];
            }

            var wave = new WaveConfigSnapshot(
                waveUnitCounts: GoldenWaveUnitCounts,
                bossWaveNumbers: GoldenBossWaveNumbers,
                bossSpawnChances: GoldenBossSpawnChances,
                spawnStrategyWeights: GoldenSpawnStrategyWeights,
                spawnStrategies: strategies,
                skipBoss: true,
                delayTimeMs: 10000,
                maxRounds: 20);

            return new BattleConfigSnapshot(
                map: map,
                enemy: enemy,
                wave: wave,
                units: GoldenUnits,
                unitLevel: GoldenUnitLevel,
                economy: GoldenEconomy,
                deck: GoldenDeck,
                projectile: GoldenProjectile,
                missingFieldNotes: Array.Empty<string>(),
                sourceTag: "Json");
        }
    }
}
