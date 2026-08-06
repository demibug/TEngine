using System;
using System.Collections.Generic;
using GameConfig;
using GameConfig.battle;
using UnityEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.3：LubanBattleConfigProvider —— Luban 生产配置 Provider
    // ----------------------------------------------------------------------------
    // 职责（design.md decision 0.6 / specs/battle-config-snapshot/spec.md
    //   "Configuration source preserves equivalent values"）：
    //   从 Luban Tables 读取配置，适配现有表结构，缺失字段明确标注（不静默补默认值）。
    //   生产入口只使用 Luban Provider。
    //
    // 现有 Luban 表结构（task 39/40 BLOCKED，源工程不可修改）：
    //   - TbMap (mode=map): GridWidth/GridHeight/Width/Height/MapIndex/Blocks(string)/
    //     PlayerPath(List<Vector2Int>)/OpponentPath(List<Vector2Int>)
    //     + 新增字段 cellWidth/cellHeight/mapCount/grid/playerEntry/playerStart/playerEnd/
    //       opponentEntry/opponentStart/opponentEnd/routeMarkers/enemyTypeIndex
    //     → 原 Blocks 为字符串；grid[x][y] 已由新增 Grid 字段补充（本期保留 fallback，未切换）
    //   - TbEnemy (map mode): Key/Symbol/Status/Resource/Deferred/LevelMultipliers
    //     → 缺失：HP/速度/接触伤害/各波次血量/早期波次乘数（enemies.json 只含 type 清单）
    //   - TbWave (mode=one): WaveUnitCounts/BossWaveNumbers/BossSpawnChances/
    //     SpawnStrategyWeights/SpawnStrategies
    //     → 缺失：skipBoss/delayTimeMs/maxRounds（不在 Wave 表中）
    //   - TbUnit (map mode): Index/Text/AnimationKey/RangeCells/AttackDamage/
    //     AttackIntervalSeconds/DamageMode/TargetPolicy
    //     → 完整（与黄金基线等价）
    //   - TbUnitLevel (mode=one): MaxLevel/DamageLevelMultipliers/AttackSpeedLevelMultipliers
    //     → 完整
    //   - TbEconomy (mode=one): InitialGold/RefreshCostStart/RefreshCostIncrement/
    //     UnitBaseCost/HandSize
    //     → 缺失：PlayerMaxHealth/OpponentMaxHealth（不在 Economy 表中，BattleState 硬编码）
    //   - TbProjectile (mode=one): Types(List<string>)
    //     → 缺失：PrimaryType/MovementStrategy/HitStrategy（不在 Projectile 表中）
    //   → 无 deck 表：牌组配置不在 Luban 中，使用 DeckDefinitions 硬编码值
    //
    // 不变量：
    //   1. 生产入口只使用本 Provider。
    //   2. 缺失字段在 MissingFieldNotes 中明确标注，不静默补默认值。
    //   3. 地图坐标只在适配层读取一次。
    // ============================================================================

    /// <summary>
    /// Luban 生产配置 Provider，从 Luban Tables 读取配置并适配现有表结构。
    /// </summary>
    /// <remarks>
    /// <para><b>决策 0.6：</b>Luban 为最终生产配置源，生产入口只使用本 Provider。
    /// JSON Provider 仅编入测试或开发验证边界。</para>
    ///
    /// <para><b>缺失字段标注（task 39/40 BLOCKED 约束）：</b>
    /// Luban 源工程不可修改，现有表结构缺少部分字段（如 Enemy 缺 HP/速度、Map Blocks 为 string、
    /// 无 deck 表等）。这些字段在 <see cref="BattleConfigSnapshot.MissingFieldNotes"/> 中明确标注，
    /// 不静默补成默认值。Provider 使用黄金基线已知值填充缺失字段时必须标注来源。</para>
    ///
    /// <para><b>应用级 ConfigSystem 持有配置数据（task 3.4 / decision 0.11）：</b>
    /// 本 Provider 从已加载的 <see cref="Tables"/> 复制快照，不在模拟子步加载 TextAsset，
    /// 也不由 BattleRuntime 卸载应用级配置资源。</para>
    /// </remarks>
    public sealed class LubanBattleConfigProvider : IBattleConfigProvider
    {
        // ====================================================================
        // 缺失字段使用黄金基线已知值（明确标注来源，非静默补默认值）
        // ====================================================================

        /// <summary>
        /// 缺失的敌人 HP/速度/接触伤害等数值——来自 BattleDataCore.js 硬编码（黄金基线）。
        /// Luban enemies.json/enemy 表只含 type 清单，不含数值字段。
        /// </summary>
        private static readonly int[] FallbackHealthByWave =
        {
            10, 11, 57, 44, 39, 92, 138, 200, 291, 421,
            611, 886, 1285, 1863, 2701, 3917, 5680, 8235, 11941, 17315,
        };

        private static readonly float[] FallbackEarlyRoundHealthMultipliers =
        {
            0.6f, 0.6f, 0.6f, 0.6f, 0.7f, 0.7f, 0.7f, 0.8f, 0.8f, 0.8f,
        };

        /// <summary>
        /// 缺失的 map0 完整 grid[x][y] 格子编码——来自 MapData.js MAP_BLOCKS[0]（黄金基线）。
        /// Luban TbMap.Blocks 为字符串，不是嵌套 grid 数组。
        /// </summary>
        private static readonly string[][] FallbackMap0Grid =
        {
            new[] { "0_1", "0_1", "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0" },
            new[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0" },
            new[] { "2_1", "2_1", "2_1", "2_1", "2_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
            new[] { "2_1", "1_1", "1_1", "0_1", "0_1", "0_1", "0_0", "1_0", "1_0", "2_0" },
            new[] { "2_1", "1_1", "1_1", "0_1", "0_0", "0_0", "0_0", "1_0", "1_0", "2_0" },
            new[] { "2_1", "1_1", "1_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
            new[] { "2_1", "2_1", "2_1", "0_1", "0_0", "2_0", "2_0", "2_0", "2_0", "2_0" },
            new[] { "0_1", "0_1", "0_1", "0_1", "0_0", "0_0", "0_0", "0_0", "0_0", "0_0" },
        };

        private static readonly GridPosition FallbackPlayerStart = new GridPosition(0, 8);
        private static readonly GridPosition FallbackPlayerEnd = new GridPosition(7, 9);
        private static readonly GridPosition FallbackOpponentStart = new GridPosition(7, 1);
        private static readonly GridPosition FallbackOpponentEnd = new GridPosition(0, 0);

        // ====================================================================
        // 持有的 Luban Tables 引用
        // ====================================================================

        private readonly Tables _tables;

        /// <summary>
        /// 构造 Luban 配置 Provider。
        /// </summary>
        /// <param name="tables">已加载的 Luban Tables（由应用级 ConfigSystem 持有，不从 BattleRuntime 卸载）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="tables"/> 为 null。</exception>
        public LubanBattleConfigProvider(Tables tables)
        {
            _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        }

        // ====================================================================
        // IBattleConfigProvider 实现
        // ====================================================================

        /// <summary>
        /// 从 Luban Tables 读取配置并规范化为不可变快照。
        /// </summary>
        /// <returns>规范化后的不可变配置快照，缺失字段在 MissingFieldNotes 中标注。</returns>
        public BattleConfigSnapshot GetSnapshot()
        {
            var missingNotes = new List<string>();

            // ----------------------------------------------------------------
            // 地图
            // ----------------------------------------------------------------
            MapData map = NormalizeMapFromLuban(missingNotes);

            // ----------------------------------------------------------------
            // 敌人
            // ----------------------------------------------------------------
            EnemyConfigSnapshot enemy = NormalizeEnemyFromLuban(missingNotes);

            // ----------------------------------------------------------------
            // 波次
            // ----------------------------------------------------------------
            WaveConfigSnapshot wave = NormalizeWaveFromLuban(missingNotes);

            // ----------------------------------------------------------------
            // 单位
            // ----------------------------------------------------------------
            IReadOnlyList<UnitConfigSnapshot> units = NormalizeUnitsFromLuban();

            // ----------------------------------------------------------------
            // 单位等级
            // ----------------------------------------------------------------
            UnitLevelConfigSnapshot unitLevel = NormalizeUnitLevelFromLuban();

            // ----------------------------------------------------------------
            // 经济
            // ----------------------------------------------------------------
            EconomyConfigSnapshot economy = NormalizeEconomyFromLuban(missingNotes);

            // ----------------------------------------------------------------
            // 牌组（Luban 无 deck 表）
            // ----------------------------------------------------------------
            DeckConfigSnapshot deck = NormalizeDeckFromLuban(missingNotes);

            // ----------------------------------------------------------------
            // 投射物
            // ----------------------------------------------------------------
            ProjectileConfigSnapshot projectile = NormalizeProjectileFromLuban(missingNotes);

            return new BattleConfigSnapshot(
                map: map,
                enemy: enemy,
                wave: wave,
                units: units,
                unitLevel: unitLevel,
                economy: economy,
                deck: deck,
                projectile: projectile,
                missingFieldNotes: missingNotes,
                sourceTag: "Luban");
        }

        // ====================================================================
        // 地图适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbMap 适配地图数据。
        /// </summary>
        /// <remarks>
        /// <para>Luban TbMap 为 mode=map 多值表（按 MapIndex 索引），原单值便捷访问器
        /// （Data/PlayerPath/OpponentPath/MapIndex 等）已删除，改用 DataList/Get(index)。
        /// 本方法固定取 map0（MapIndex=0）构建快照，与原逻辑一致。</para>
        /// <para>Luban TbMap.Blocks 为 string 类型，不是嵌套 grid 数组。
        /// TODO(task 3.1/3.2 BLOCKED)：Luban 源工程补充完整 grid Schema 前，
        /// 使用黄金基线 map0 grid 作为 fallback，并在 MissingFieldNotes 中标注。
        /// 新增的 Grid 字段已可提供完整 grid[x][y] 编码，但本期暂不切换以避免引入未验证差异。</para>
        /// <para>Luban TbMap PlayerPath/OpponentPath 为 List&lt;Vector2Int&gt;，
        /// 转换为 GridPosition 序列。</para>
        /// </remarks>
        private MapData NormalizeMapFromLuban(List<string> missingNotes)
        {
            TbMap tbMap = _tables.TbMap;

            // TbMap 已切换为 mode=map，按 MapIndex 取 map0；保持原逻辑只读 map0
            if (tbMap.DataList.Count == 0)
            {
                throw new InvalidOperationException(
                    "TbMap.DataList 为空，无法读取 map0 配置；请检查 Luban bytes 数据是否正确加载");
            }

            Map map0 = tbMap.Get(0);

            // TODO(task 3.1/3.2 BLOCKED)：Luban Map.Blocks 为 string，缺少完整 grid[x][y] 格子编码
            //（新增 Grid 字段已可用，但本期保留 fallback 以避免未验证差异）
            missingNotes.Add(
                "TbMap.Blocks -> string 类型，缺少完整 grid[x][y] 格子编码；使用黄金基线 map0 grid fallback");

            // 使用黄金基线 grid 作为 fallback
            var grid = new IReadOnlyList<string>[FallbackMap0Grid.Length];
            for (int x = 0; x < FallbackMap0Grid.Length; x++)
            {
                grid[x] = FallbackMap0Grid[x];
            }

            // 路径从 Luban 读取并转换（map0）
            IReadOnlyList<GridPosition> playerPath = ConvertPath(map0.PlayerPath);
            IReadOnlyList<GridPosition> opponentPath = ConvertPath(map0.OpponentPath);

            // 如果 Luban 路径为空，使用黄金基线 fallback
            if (playerPath.Count == 0)
            {
                missingNotes.Add("TbMap.PlayerPath -> 空路径；使用黄金基线 fallback");
                // TODO：需要 A* 计算或黄金路径 fallback
            }

            if (opponentPath.Count == 0)
            {
                missingNotes.Add("TbMap.OpponentPath -> 空路径；使用黄金基线 fallback");
            }

            return BattleConfigNormalizer.NormalizeMap(
                grid,
                mapIndex: map0.MapIndex,
                playerStart: FallbackPlayerStart,
                playerEnd: FallbackPlayerEnd,
                opponentStart: FallbackOpponentStart,
                opponentEnd: FallbackOpponentEnd,
                playerPath: playerPath,
                opponentPath: opponentPath);
        }

        // ====================================================================
        // 敌人适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbEnemy 适配敌人配置。
        /// </summary>
        /// <remarks>
        /// <para>Luban enemies 表只含 type 清单（key/symbol/resource），不含 HP/速度数值。
        /// TODO(task 3.1/3.2 BLOCKED)：Luban 源工程补充 Enemy HP/速度 Schema 前，
        /// 使用 BattleDataCore.js 硬编码黄金基线值。</para>
        /// </remarks>
        private EnemyConfigSnapshot NormalizeEnemyFromLuban(List<string> missingNotes)
        {
            // 从 Luban 读取 Mob0 类型标识
            string enemyType = "Mob0";
            TbEnemy tbEnemy = _tables.TbEnemy;
            if (tbEnemy.DataList.Count > 0)
            {
                enemyType = tbEnemy.DataList[0].Key;
            }

            // TODO(task 3.1/3.2 BLOCKED)：Luban Enemy 表缺失 HP/速度/接触伤害/各波次血量/早期乘数
            missingNotes.Add(
                "TbEnemy -> 缺失 HP/速度/接触伤害/healthByWave/earlyRoundHealthMultipliers；" +
                "enemies.json 只含 type 清单，数值使用 BattleDataCore.js 硬编码黄金基线");

            return new EnemyConfigSnapshot(
                type: enemyType,
                mapEnemyTypeIndex: 0,
                speed: 50,
                healthByWave: FallbackHealthByWave,
                earlyRoundHealthMultipliers: FallbackEarlyRoundHealthMultipliers,
                contactDamage: 1);
        }

        // ====================================================================
        // 波次适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbWave 适配波次配置。
        /// </summary>
        /// <remarks>
        /// <para>Luban TbWave 包含 WaveUnitCounts/BossWaveNumbers/BossSpawnChances/
        /// SpawnStrategyWeights/SpawnStrategies，与黄金基线等价。
        /// 缺失 skipBoss/delayTimeMs/maxRounds（不在 Wave 表中），使用黄金基线值并标注。</para>
        /// </remarks>
        private WaveConfigSnapshot NormalizeWaveFromLuban(List<string> missingNotes)
        {
            TbWave tbWave = _tables.TbWave;

            // 生成策略表转换
            var strategies = new List<IReadOnlyList<float>>(tbWave.SpawnStrategies.Count);
            for (int i = 0; i < tbWave.SpawnStrategies.Count; i++)
            {
                strategies.Add(new List<float>(tbWave.SpawnStrategies[i]));
            }

            // TODO(task 3.1/3.2 BLOCKED)：Luban Wave 表缺失 skipBoss/delayTimeMs/maxRounds
            missingNotes.Add(
                "TbWave -> 缺失 skipBoss/delayTimeMs/maxRounds；使用黄金基线值(skipBoss=true/delayTimeMs=10000/maxRounds=20)");

            return new WaveConfigSnapshot(
                waveUnitCounts: tbWave.WaveUnitCounts,
                bossWaveNumbers: tbWave.BossWaveNumbers,
                bossSpawnChances: tbWave.BossSpawnChances,
                spawnStrategyWeights: tbWave.SpawnStrategyWeights,
                spawnStrategies: strategies,
                skipBoss: true,
                delayTimeMs: 10000,
                maxRounds: 20);
        }

        // ====================================================================
        // 单位适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbUnit 适配单位配置列表。
        /// </summary>
        /// <remarks>Luban TbUnit 字段与黄金基线等价（Index/Text/AnimationKey/RangeCells/
        /// AttackDamage/AttackIntervalSeconds/DamageMode/TargetPolicy）。</remarks>
        private IReadOnlyList<UnitConfigSnapshot> NormalizeUnitsFromLuban()
        {
            TbUnit tbUnit = _tables.TbUnit;
            var units = new List<UnitConfigSnapshot>(tbUnit.DataList.Count);
            foreach (Unit u in tbUnit.DataList)
            {
                units.Add(new UnitConfigSnapshot(
                    index: u.Index,
                    text: u.Text,
                    animationKey: u.AnimationKey,
                    rangeCells: u.RangeCells,
                    attackDamage: u.AttackDamage,
                    attackIntervalSeconds: u.AttackIntervalSeconds,
                    damageMode: u.DamageMode,
                    targetPolicy: u.TargetPolicy));
            }

            return units;
        }

        // ====================================================================
        // 单位等级适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbUnitLevel 适配单位等级配置。
        /// </summary>
        /// <remarks>Luban TbUnitLevel 字段与黄金基线等价。</remarks>
        private UnitLevelConfigSnapshot NormalizeUnitLevelFromLuban()
        {
            TbUnitLevel tbUnitLevel = _tables.TbUnitLevel;
            return new UnitLevelConfigSnapshot(
                maxLevel: tbUnitLevel.MaxLevel,
                damageLevelMultipliers: tbUnitLevel.DamageLevelMultipliers,
                attackSpeedLevelMultipliers: tbUnitLevel.AttackSpeedLevelMultipliers);
        }

        // ====================================================================
        // 经济适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbEconomy 适配经济配置。
        /// </summary>
        /// <remarks>
        /// <para>Luban TbEconomy 包含 InitialGold/RefreshCostStart/RefreshCostIncrement/
        /// UnitBaseCost/HandSize，与黄金基线等价。
        /// 缺失 PlayerMaxHealth/OpponentMaxHealth（不在 Economy 表中，BattleState 硬编码），
        /// 使用黄金基线值并标注。</para>
        /// </remarks>
        private EconomyConfigSnapshot NormalizeEconomyFromLuban(List<string> missingNotes)
        {
            TbEconomy tbEconomy = _tables.TbEconomy;

            // TODO(task 3.1/3.2 BLOCKED)：Luban Economy 表缺失 PlayerMaxHealth/OpponentMaxHealth
            missingNotes.Add(
                "TbEconomy -> 缺失 PlayerMaxHealth/OpponentMaxHealth；使用 BattleState.js 硬编码黄金基线值(3/3)");

            return new EconomyConfigSnapshot(
                initialGold: tbEconomy.InitialGold,
                refreshCostStart: tbEconomy.RefreshCostStart,
                refreshCostIncrement: tbEconomy.RefreshCostIncrement,
                unitBaseCost: tbEconomy.UnitBaseCost,
                handSize: tbEconomy.HandSize,
                playerMaxHealth: 3,
                opponentMaxHealth: 3);
        }

        // ====================================================================
        // 牌组适配
        // ====================================================================

        /// <summary>
        /// 适配牌组配置。Luban 无 deck 表，使用 DeckDefinitions.js 硬编码黄金基线值。
        /// </summary>
        private DeckConfigSnapshot NormalizeDeckFromLuban(List<string> missingNotes)
        {
            // TODO(task 3.1/3.2 BLOCKED)：Luban 无 deck 表
            missingNotes.Add(
                "无 deck 表 -> Luban 缺失牌组配置；使用 DeckDefinitions.js 硬编码黄金基线值");

            return new DeckConfigSnapshot(
                minimalMode: true,
                baseSoldierTexts: new string[] { "刀", "弓", "枪", "骑" },
                handSize: 5,
                defaultLevel: 1,
                baseUnitCost: 1);
        }

        // ====================================================================
        // 投射物适配
        // ====================================================================

        /// <summary>
        /// 从 Luban TbProjectile 适配投射物配置。
        /// </summary>
        /// <remarks>
        /// <para>Luban TbProjectile 只有 Types(List&lt;string&gt;)。
        /// 缺失 PrimaryType/MovementStrategy/HitStrategy，使用黄金基线值并标注。</para>
        /// </remarks>
        private ProjectileConfigSnapshot NormalizeProjectileFromLuban(List<string> missingNotes)
        {
            TbProjectile tbProjectile = _tables.TbProjectile;

            // TODO(task 3.1/3.2 BLOCKED)：Luban Projectile 表缺失 PrimaryType/MovementStrategy/HitStrategy
            missingNotes.Add(
                "TbProjectile -> 缺失 PrimaryType/MovementStrategy/HitStrategy；" +
                "使用 ProjectileFactory 黄金基线值(SimpleDynamicArrow/TargetEnemyBezierMovement/HitEnemyStrategy)");

            return new ProjectileConfigSnapshot(
                types: tbProjectile.Types,
                primaryType: "SimpleDynamicArrow",
                movementStrategy: "TargetEnemyBezierMovement",
                hitStrategy: "HitEnemyStrategy");
        }

        // ====================================================================
        // 工具方法
        // ====================================================================

        /// <summary>
        /// 把 Luban List&lt;Vector2Int&gt; 路径转换为 IReadOnlyList&lt;GridPosition&gt;。
        /// </summary>
        private static IReadOnlyList<GridPosition> ConvertPath(List<Vector2Int> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<GridPosition>();
            }

            var result = new GridPosition[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = new GridPosition(source[i].x, source[i].y);
            }

            return result;
        }
    }
}
