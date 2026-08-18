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
        /// 黄金玩家游戏路径（map0，A* 从 playerStart(0,8) 到 playerEnd(7,9)）。
        /// 来源：MapData.js findPath + golden-battle-bundle.json playerPathLength=17。
        /// </summary>
        private static readonly GridPosition[] GoldenPlayerPath =
        {
            new GridPosition(0, 8), new GridPosition(0, 7), new GridPosition(0, 6),
            new GridPosition(0, 5), new GridPosition(1, 5), new GridPosition(2, 5),
            new GridPosition(3, 5), new GridPosition(4, 5), new GridPosition(4, 4),
            new GridPosition(5, 4), new GridPosition(6, 4), new GridPosition(7, 4),
            new GridPosition(7, 5), new GridPosition(7, 6), new GridPosition(7, 7),
            new GridPosition(7, 8), new GridPosition(7, 9),
        };

        /// <summary>
        /// 黄金对手游戏路径（map0，A* 从 opponentStart(7,1) 到 opponentEnd(0,0)）。
        /// 来源：MapData.js findPath + golden-battle-bundle.json opponentPathLength=17。
        /// </summary>
        private static readonly GridPosition[] GoldenOpponentPath =
        {
            new GridPosition(7, 1), new GridPosition(7, 2), new GridPosition(7, 3),
            new GridPosition(6, 3), new GridPosition(5, 3), new GridPosition(4, 3),
            new GridPosition(3, 3), new GridPosition(3, 4), new GridPosition(3, 5),
            new GridPosition(2, 5), new GridPosition(1, 5), new GridPosition(0, 5),
            new GridPosition(0, 4), new GridPosition(0, 3), new GridPosition(0, 2),
            new GridPosition(0, 1), new GridPosition(0, 0),
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

        /// <summary>
        /// 黄金有序波次计划（activePlanId="golden"）。行覆盖 Mob0～Mob3 四类普通敌人，
        /// 显式引用策略 profile 0/1/2，与黄金 spawnStrategies 表保持一致。
        /// </summary>
        private static OrderedWavePlanSnapshot BuildGoldenWavePlan()
        {
            var rows = new List<WavePlanEntry>
            {
                // 与 Luban Provider 保持同一规范化语义：源行空 enemyKey 已按 map EnemyTypeIndex=0
                // 解析为 Mob0，WaveManager/NormalWaveSpawnRequest 只消费生效后的非空键。
                new WavePlanEntry("golden", 1, WavePlanKind.Normal, "Mob0", 3, 0, "", 1000, 500, 500, true, true, 0),
                new WavePlanEntry("golden", 2, WavePlanKind.Normal, "Mob1", 2, 1, "", 1000, 600, 500, true, false, 1),
                new WavePlanEntry("golden", 3, WavePlanKind.Normal, "Mob2", 2, 2, "", 1000, 600, 500, false, true, 2),
                new WavePlanEntry("golden", 4, WavePlanKind.Normal, "Mob3", 1, 3, "", 1000, 700, 500, true, true, 0),
            };

            var profiles = new Dictionary<int, IReadOnlyList<float>>
            {
                [0] = GoldenSpawnStrategies[0],
                [1] = GoldenSpawnStrategies[1],
                [2] = GoldenSpawnStrategies[2],
            };

            return new OrderedWavePlanSnapshot("golden", rows, profiles);
        }

        // ====================================================================
        // 黄金敌人目录数据（BattleDataCore.js 硬编码）
        // ====================================================================

        /// <summary>
        /// 黄金敌人目录（Mob0～Mob3，typeIndex 0～3）。四类第一版复用同一数值曲线
        /// （design.md 决策 2），差异由 key/typeIndex/资源地址体现。
        /// </summary>
        private static EnemyCatalogSnapshot BuildGoldenEnemyCatalog()
        {
            var definitions = new List<EnemyDefinitionSnapshot>(4);
            for (int i = 0; i < 4; i++)
            {
                definitions.Add(new EnemyDefinitionSnapshot(
                    typeIndex: i,
                    key: $"Mob{i}",
                    resourceAddress: $"Mob{i}",
                    moveSpeed: 50,
                    healthByWave: GoldenHealthByWave,
                    earlyRoundHealthMultipliers: GoldenEarlyRoundHealthMultipliers,
                    contactDamage: 1,
                    rewardGold: 1));
            }

            return new EnemyCatalogSnapshot(definitions);
        }

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
        // 黄金 Buff 目录数据（buff.xlsx 全量行，与生产 TbBuff 等价）
        // ====================================================================

        /// <summary>
        /// 黄金 Buff 目录（type 0～19，与 buff.xlsx 导出行完全等价）。
        /// </summary>
        /// <remarks>
        /// <para>显式构造等价目录：type/name/label/kind/channels 与用户导出的
        /// battle_tbbuff.bytes 逐行一致；stackPolicy=Add、maxStacks=1、conflictKey 为空
        /// 同样显式写入（buff-config-mapping.md）。不按 type 推导任何字段。</para>
        /// <para>type 12（knockback，state channel 5）与 type 14（burnStatic，
        /// state channel 4）为原表既有合法行；其 4/5 通道的一次性/复合语义在目标申请
        /// 阶段处理，本配置目录保持可加载。</para>
        /// </remarks>
        private static BuffCatalogSnapshot BuildGoldenBuffCatalog()
        {
            var definitions = new List<BuffDefinitionSnapshot>(20)
            {
                GoldenDef(0, "attPower", "", BuffKind.Numeric, new[] { 0 }),
                GoldenDef(1, "attSpeed", "", BuffKind.Numeric, new[] { 1 }),
                GoldenDef(2, "attRange", "", BuffKind.Numeric, new[] { 2 }),
                GoldenDef(3, "moveSpeed", "", BuffKind.Numeric, new[] { 3 }),
                GoldenDef(4, "maxHp", "", BuffKind.Numeric, new[] { 4 }),
                GoldenDef(5, "hp", "", BuffKind.Numeric, new[] { 5 }),
                GoldenDef(6, "scale", "", BuffKind.Numeric, new[] { 6 }),
                GoldenDef(7, "custom", "", BuffKind.Custom, Array.Empty<int>()),
                GoldenDef(8, "stun", "晕眩", BuffKind.State, new[] { 1, 0 }),
                GoldenDef(9, "fall", "跌倒", BuffKind.State, new[] { 0 }),
                GoldenDef(10, "pierce", "穿刺", BuffKind.State, new[] { 0 }),
                GoldenDef(11, "electrocute", "电击", BuffKind.State, new[] { 1, 0 }),
                GoldenDef(12, "knockback", "击退", BuffKind.State, new[] { 5 }),
                GoldenDef(13, "chaos", "混乱", BuffKind.State, new[] { 1, 0, 2 }),
                GoldenDef(14, "burnStatic", "火焰灼烧", BuffKind.State, new[] { 4 }),
                GoldenDef(15, "limit", "", BuffKind.State, new[] { 6 }),
                GoldenDef(16, "lock", "封锁", BuffKind.State, new[] { 1, 2 }),
                GoldenDef(17, "knockdown", "跌倒", BuffKind.State, new[] { 1 }),
                GoldenDef(18, "suppression", "压制", BuffKind.State, new[] { 3 }),
                GoldenDef(19, "charm", "魅惑", BuffKind.State, new[] { 2, 3 }),
            };

            return new BuffCatalogSnapshot(definitions);
        }

        /// <summary>
        /// 构造单条黄金 Buff 定义（显式 stackPolicy=Add / maxStacks=1 / conflictKey 为空）。
        /// </summary>
        private static BuffDefinitionSnapshot GoldenDef(
            int type, string name, string label, BuffKind kind, IReadOnlyList<int> channels)
        {
            return new BuffDefinitionSnapshot(
                type, name, label, kind, channels,
                BuffStackPolicy.Add, maxStacks: 1, conflictKey: string.Empty);
        }

        // ====================================================================
        // 黄金 Skill 目录数据（skill.xlsx 全量行，与生产 TbSkill 等价）
        // ====================================================================

        /// <summary>
        /// 黄金 Skill 目录（19 行，与 skill.xlsx 导出行完全等价）。
        /// </summary>
        /// <remarks>
        /// <para>显式构造等价目录：key/category/handlerKey/
        /// effectBuffType/effectDurationMs 与用户导出的 battle_tbskill.bytes 逐行一致；
        /// CooldownSeconds 以 checked 转成毫秒（0 → 0ms，8 → 8000ms 等）。
        /// 不按 key 推导任何字段。</para>
        /// <para>6 个 active + 1 个 passive + 12 个 boss 行；handlerKey 与 key 相同
        /// （本 change 注册 SoulCapture 生产 handler，其余行可留在目录但不得 attach）。
        /// EffectBuffType/EffectDurationMs/RangeTiles 只读透传：仅 SoulCapture 携带
        /// buff type 13 / 2000ms / range 2，框架不解释其语义。</para>
        /// </remarks>
        private static SkillCatalogSnapshot BuildGoldenSkillCatalog()
        {
            var definitions = new List<SkillDefinitionSnapshot>(19)
            {
                GoldenSkill("LeapSlash", SkillCategory.Active, 0, null, null, null),
                GoldenSkill("SevenInSevenOut", SkillCategory.Active, 0, null, null, null),
                GoldenSkill("BattleShout", SkillCategory.Active, 0, null, null, null),
                GoldenSkill("HolySword", SkillCategory.Active, 0, null, null, null),
                GoldenSkill("ArrowRain", SkillCategory.Active, 0, null, null, null),
                GoldenSkill("FireArrowBarrage", SkillCategory.Active, 0, null, null, null),
                GoldenSkill("StunPassive", SkillCategory.Passive, 0, null, null, null),
                GoldenSkill("SoulCapture", SkillCategory.Boss, 8000, 13, 2000, 2f),
                GoldenSkill("SoulSummon", SkillCategory.Boss, 8000, null, null, 3f),
                GoldenSkill("Inspire", SkillCategory.Boss, 10000, null, null, 2f),
                GoldenSkill("Demolition", SkillCategory.Boss, 10000, null, null, 10f),
                GoldenSkill("RainStorm", SkillCategory.Boss, 3000, null, null, 10f),
                GoldenSkill("Enthrall", SkillCategory.Boss, 10000, null, null, 10f),
                GoldenSkill("CavalryOrder", SkillCategory.Boss, 8000, null, null, 0f),
                GoldenSkill("FangTianHalberd", SkillCategory.Boss, 10000, null, null, 2.5f),
                GoldenSkill("Devour", SkillCategory.Boss, 10000, null, null, 1.5f),
                GoldenSkill("Madness", SkillCategory.Boss, 12000, null, null, 2f),
                GoldenSkill("DevourEyes", SkillCategory.Boss, 8000, null, null, 2f),
                GoldenSkill("WarlordSeal", SkillCategory.Boss, 12000, null, null, 10f),
            };

            return new SkillCatalogSnapshot(definitions);
        }

        /// <summary>
        /// 构造单条黄金 Skill 定义（handlerKey 与 key 相同，EffectBuffType/EffectDurationMs 显式透传）。
        /// </summary>
        private static SkillDefinitionSnapshot GoldenSkill(
            string key,
            SkillCategory category,
            long cooldownMs,
            int? effectBuffType,
            int? effectDurationMs,
            float? rangeTiles)
        {
            return new SkillDefinitionSnapshot(
                key, category, cooldownMs, key, effectBuffType, effectDurationMs, rangeTiles);
        }

        // ====================================================================
        // 黄金 Boss 目录数据（boss.xlsx 全量行，与生产 TbBoss 等价）
        // ====================================================================

        /// <summary>
        /// 黄金 Boss 目录（12 行，与 boss.xlsx 导出行完全等价）。
        /// </summary>
        /// <remarks>
        /// <para>显式构造等价目录：key/skillKey/animationKey/resourcePath/
        /// attackAnimation/idleAnimation/timeline/enabled/healthMultiplier/moveSpeed/
        /// contactDamage/rewardGold/logicalWidth/logicalHeight 与用户导出的
        /// battle_tbboss.bytes 逐行一致。不按 key 推导任何字段。</para>
        /// <para>首期只启用 ZhangLiang（enabled=true，skillKey=SoulCapture，
        /// timeline=500/1400，healthMultiplier=7，moveSpeed=10，contactDamage=1，
        /// rewardGold=10，logicalWidth=84.33，logicalHeight=101.25，attackliang/goliang）；
        /// 其余 11 行 disabled，MUST NOT 可出生。resourcePath 因 Spine 4.3 兼容资产未
        /// 就绪保持空串——这是 production resource closure 的 7.x 门禁，gameplay 校验
        /// 不因空资源路径判非法（spec "Pure logic test runs without a prefab"）。</para>
        /// </remarks>
        private static BossCatalogSnapshot BuildGoldenBossCatalog()
        {
            var definitions = new List<BossDefinitionSnapshot>(12)
            {
                GoldenBoss("ZhangLiang", "张梁", "SoulCapture", "boss0", "", "attackliang", "goliang",
                    500, 1400, true, 7f, 10f, 1, 10, 84.33f, 101.25f),
                GoldenBoss("ZhangBao", "张宝", "SoulSummon", "boss0", "", "attackbao", "gobao",
                    0, 1000, false, 10f, 10f, 1, 10, 84.33f, 101.25f),
                GoldenBoss("ZhangJiao", "张角", "Inspire", "boss0", "", "attackjiao", "gojiao",
                    500, 1000, false, 15f, 10f, 1, 10, 84.33f, 101.25f),
                GoldenBoss("SunShangXiang", "孙尚香", "Demolition", "boss1", "", "attackxiang", "goxiang",
                    500, 1000, false, 7f, 10f, 1, 10, 79.69f, 96.08f),
                GoldenBoss("ZhenFu", "甄姬", "RainStorm", "boss1", "", "attackzhen", "gozhen",
                    900, 1000, false, 10f, 10f, 1, 10, 79.69f, 96.08f),
                GoldenBoss("DiaoChan", "貂蝉", "Enthrall", "boss1", "", "attackdiao", "godiao",
                    200, 1200, false, 15f, 10f, 1, 10, 79.69f, 96.08f),
                GoldenBoss("HuaXiong", "华雄", "CavalryOrder", "huaXiong", "", "attackhx", "gohx",
                    500, 1000, false, 7f, 10f, 1, 10, 90.92f, 164.08f),
                GoldenBoss("LvBu", "吕布", "FangTianHalberd", "lvBu", "", "attacklvbu", "golvbu",
                    650, 1000, false, 10f, 10f, 1, 10, 302.23f, 302.23f),
                GoldenBoss("DongZhuo", "董卓", "Devour", "dongZhuo", "", "attackdz", "godz",
                    500, 1400, false, 15f, 10f, 1, 10, 285.5f, 256f),
                GoldenBoss("DianWei", "典韦", "Madness", "boss2", "", "attackdian", "godian",
                    800, 1400, false, 7f, 10f, 1, 10, 58.1f, 83.39f),
                GoldenBoss("XiaHouDun", "夏侯惇", "DevourEyes", "boss2", "", "attackdun", "goxia",
                    1000, 1500, false, 10f, 10f, 1, 10, 58.1f, 83.39f),
                GoldenBoss("CaoCao", "曹操", "WarlordSeal", "boss2", "", "attackcao", "gocao",
                    900, 1000, false, 15f, 10f, 1, 10, 58.1f, 83.39f),
            };

            return new BossCatalogSnapshot(definitions);
        }

        /// <summary>
        /// 构造单条黄金 Boss 定义（显式逐字段，不按 key 推导）。
        /// </summary>
        private static BossDefinitionSnapshot GoldenBoss(
            string key,
            string name,
            string skillKey,
            string animationKey,
            string resourcePath,
            string attackAnimation,
            string idleAnimation,
            int effectAtMs,
            int completeAtMs,
            bool enabled,
            float healthMultiplier,
            float moveSpeed,
            int contactDamage,
            int rewardGold,
            float logicalWidth,
            float logicalHeight)
        {
            return new BossDefinitionSnapshot(
                key, name, skillKey, animationKey, resourcePath, attackAnimation, idleAnimation,
                new BossTimelineSnapshot(effectAtMs, completeAtMs),
                enabled, healthMultiplier, moveSpeed, contactDamage, rewardGold,
                logicalWidth, logicalHeight);
        }

        // ====================================================================
        // 黄金武器目录数据（weapon.xlsx 全量行，与生产 TbWeapon 等价）
        // ====================================================================

        /// <summary>
        /// 黄金武器目录（44 行，id 0～43，与 weapon.xlsx 导出行完全等价）。
        /// </summary>
        /// <remarks>
        /// <para>显式构造等价目录：id/type/addAttPower/enabled/handlerKey 与用户导出的
        /// battle_tbweapon.bytes 逐行一致。不按 id 推导任何字段。</para>
        /// <para>首期只启用 id ∈ {0, 10, 20, 31} 四条 Basic +1（type 分别对应
        /// Bow/Spear/Knife/Sword），其余 40 行 disabled（handlerKey 空串），
        /// MUST NOT 产生运行时状态或行为对象。</para>
        /// </remarks>
        private static WeaponCatalogSnapshot BuildGoldenWeaponCatalog()
        {
            var definitions = new List<WeaponDefinitionSnapshot>(44)
            {
                GoldenWeapon(0, WeaponType.Bow, 1, true, "Basic"),
                GoldenWeapon(1, WeaponType.Bow, 2, false, null),
                GoldenWeapon(2, WeaponType.Bow, 2, false, null),
                GoldenWeapon(3, WeaponType.Bow, 3, false, null),
                GoldenWeapon(4, WeaponType.Bow, 4, false, null),
                GoldenWeapon(5, WeaponType.Bow, 5, false, null),
                GoldenWeapon(6, WeaponType.Bow, 5, false, null),
                GoldenWeapon(7, WeaponType.Bow, 6, false, null),
                GoldenWeapon(8, WeaponType.Bow, 6, false, null),
                GoldenWeapon(9, WeaponType.Bow, 6, false, null),
                GoldenWeapon(10, WeaponType.Spear, 1, true, "Basic"),
                GoldenWeapon(11, WeaponType.Spear, 2, false, null),
                GoldenWeapon(12, WeaponType.Spear, 3, false, null),
                GoldenWeapon(13, WeaponType.Spear, 4, false, null),
                GoldenWeapon(14, WeaponType.Spear, 4, false, null),
                GoldenWeapon(15, WeaponType.Spear, 5, false, null),
                GoldenWeapon(16, WeaponType.Spear, 6, false, null),
                GoldenWeapon(17, WeaponType.Spear, 6, false, null),
                GoldenWeapon(18, WeaponType.Spear, 9, false, null),
                GoldenWeapon(19, WeaponType.Spear, 10, false, null),
                GoldenWeapon(20, WeaponType.Knife, 1, true, "Basic"),
                GoldenWeapon(21, WeaponType.Knife, 2, false, null),
                GoldenWeapon(22, WeaponType.Knife, 3, false, null),
                GoldenWeapon(23, WeaponType.Knife, 4, false, null),
                GoldenWeapon(24, WeaponType.Knife, 4, false, null),
                GoldenWeapon(25, WeaponType.Knife, 5, false, null),
                GoldenWeapon(26, WeaponType.Knife, 6, false, null),
                GoldenWeapon(27, WeaponType.Knife, 7, false, null),
                GoldenWeapon(28, WeaponType.Knife, 8, false, null),
                GoldenWeapon(29, WeaponType.Knife, 10, false, null),
                GoldenWeapon(30, WeaponType.Knife, 10, false, null),
                GoldenWeapon(31, WeaponType.Sword, 1, true, "Basic"),
                GoldenWeapon(32, WeaponType.Sword, 2, false, null),
                GoldenWeapon(33, WeaponType.Sword, 3, false, null),
                GoldenWeapon(34, WeaponType.Sword, 4, false, null),
                GoldenWeapon(35, WeaponType.Sword, 5, false, null),
                GoldenWeapon(36, WeaponType.Sword, 6, false, null),
                GoldenWeapon(37, WeaponType.Sword, 6, false, null),
                GoldenWeapon(38, WeaponType.Sword, 7, false, null),
                GoldenWeapon(39, WeaponType.Sword, 8, false, null),
                GoldenWeapon(40, WeaponType.Sword, 9, false, null),
                GoldenWeapon(41, WeaponType.Sword, 9, false, null),
                GoldenWeapon(42, WeaponType.Sword, 9, false, null),
                GoldenWeapon(43, WeaponType.Sword, 10, false, null),
            };

            return new WeaponCatalogSnapshot(definitions);
        }

        /// <summary>
        /// 构造单条黄金武器定义（显式逐字段，不按 id 推导）。
        /// </summary>
        private static WeaponDefinitionSnapshot GoldenWeapon(
            int id,
            WeaponType type,
            int addAttackPower,
            bool enabled,
            string handlerKey)
        {
            return new WeaponDefinitionSnapshot(id, type, addAttackPower, enabled, handlerKey);
        }

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

            // 新生产链权威：敌人目录 + 有序波次计划 + Buff 目录（tasks 2.2/2.6/3.3）。
            EnemyCatalogSnapshot catalog = BuildGoldenEnemyCatalog();
            OrderedWavePlanSnapshot orderedWavePlan = BuildGoldenWavePlan();
            BuffCatalogSnapshot buffCatalog = BuildGoldenBuffCatalog();

            // 新生产链权威：Skill 目录（task 2.2，与 skill.xlsx 全量行等价）。
            SkillCatalogSnapshot skillCatalog = BuildGoldenSkillCatalog();

            // 本 change 权威：Boss 目录（与 boss.xlsx 全量行等价）。
            BossCatalogSnapshot bossCatalog = BuildGoldenBossCatalog();

            // 本 change 权威：武器目录（与 weapon.xlsx 全量行等价）。
            WeaponCatalogSnapshot weaponCatalog = BuildGoldenWeaponCatalog();

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
                sourceTag: "Json",
                enemyCatalog: catalog,
                orderedWavePlan: orderedWavePlan,
                buffCatalog: buffCatalog,
                skillCatalog: skillCatalog,
                bossCatalog: bossCatalog,
                weaponCatalog: weaponCatalog);
        }
    }
}
