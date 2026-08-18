using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Config
{
    // ============================================================================
    // 任务 3.3：BattleConfigSnapshot 单元测试
    // ----------------------------------------------------------------------------
    // 验证内容（specs/battle-config-snapshot/spec.md）：
    //   1. BattleConfigSnapshot 是不可变类型
    //   2. JsonBattleConfigProvider 从黄金 JSON 读取完整配置
    //   3. 所有配置字段（地图、敌人、波次、单位、经济、投射物、牌组）存在且有效
    //   4. 黄金基线值与 GoldenBattleFixtures 一致
    //
    // 注意：LubanBattleConfigProvider 需要 Luban Tables 实例，由 ConfigSystem 加载。
    //   本测试只验证 JSON Provider（黄金基线），Luban Provider 的对照验证在 task 3.13。
    // ============================================================================

    /// <summary>
    /// BattleConfigSnapshot 与 JsonBattleConfigProvider 的不可变性和完整性测试。
    /// </summary>
    [TestFixture]
    public class BattleConfigSnapshotTests
    {
        // ====================================================================
        // 不可变性测试
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_ReturnsImmutableSnapshot()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            // 验证 SourceTag
            Assert.AreEqual("Json", snapshot.SourceTag, "JSON Provider 的 SourceTag 应为 'Json'");

            // 验证不可变性：字段不为 null
            Assert.IsNotNull(snapshot.Map, "Map 不应为 null");
            Assert.IsNotNull(snapshot.Enemy, "Enemy 不应为 null");
            Assert.IsNotNull(snapshot.Wave, "Wave 不应为 null");
            Assert.IsNotNull(snapshot.Units, "Units 不应为 null");
            Assert.IsNotNull(snapshot.UnitLevel, "UnitLevel 不应为 null");
            Assert.IsNotNull(snapshot.Economy, "Economy 不应为 null");
            Assert.IsNotNull(snapshot.Deck, "Deck 不应为 null");
            Assert.IsNotNull(snapshot.Projectile, "Projectile 不应为 null");
            Assert.IsNotNull(snapshot.BuffCatalog, "BuffCatalog 不应为 null");
            Assert.IsNotNull(snapshot.SkillCatalog, "SkillCatalog 不应为 null");
            Assert.IsNotNull(snapshot.MissingFieldNotes, "MissingFieldNotes 不应为 null");

            // JSON Provider 不产生缺失标注
            Assert.AreEqual(0, snapshot.MissingFieldNotes.Count,
                "JSON Provider 包含完整配置，不应有缺失字段标注");
        }

        [Test]
        public void Snapshot_JsonProvider_ConfigurationChangesAfterStart_DoesNotAffectSnapshot()
        {
            // spec "Configuration changes after battle starts"：
            // 当前战斗继续使用启动时冻结的配置快照
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot1 = provider.GetSnapshot();
            BattleConfigSnapshot snapshot2 = provider.GetSnapshot();

            // 两次调用返回独立快照（Provider 不持有可变状态）
            Assert.AreNotSame(snapshot1, snapshot2, "两次调用应返回独立快照实例");
            Assert.AreEqual(snapshot1.Economy.InitialGold, snapshot2.Economy.InitialGold,
                "两次快照的金币值应相等");
        }

        // ====================================================================
        // 地图配置测试
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_Map_HasCorrectDimensions()
        {
            // spec "Load the minimal map"：8 列、10 行
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.AreEqual(8, snapshot.Map.Width, "地图宽度应为 8 列");
            Assert.AreEqual(10, snapshot.Map.Height, "地图高度应为 10 行");
            Assert.AreEqual(0, snapshot.Map.MapIndex, "地图索引应为 0");
        }

        [Test]
        public void Snapshot_JsonProvider_Map_BoundsValidation()
        {
            // spec "Load the minimal map"：合法 x 范围为 0..7、合法 y 范围为 0..9
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.IsTrue(snapshot.Map.IsInside(0, 0), "(0,0) 应在地图内");
            Assert.IsTrue(snapshot.Map.IsInside(7, 9), "(7,9) 应在地图内");
            Assert.IsFalse(snapshot.Map.IsInside(8, 0), "(8,0) 应越界");
            Assert.IsFalse(snapshot.Map.IsInside(0, 10), "(0,10) 应越界");
        }

        [Test]
        public void Snapshot_JsonProvider_Map_PlayerBuildableCells()
        {
            // golden-battle-bundle.json playerBuildableCells
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            // 验证玩家可建造格
            Assert.IsTrue(snapshot.Map.IsBuildableForSide(true, 3, 1), "(3,1) 应为玩家可建造格");
            Assert.IsTrue(snapshot.Map.IsBuildableForSide(true, 4, 2), "(4,2) 应为玩家可建造格");
            Assert.IsFalse(snapshot.Map.IsBuildableForSide(true, 2, 7), "(2,7) 不应为玩家可建造格");
        }

        [Test]
        public void Snapshot_JsonProvider_Map_OpponentBuildableCells()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            // 验证对手可建造格
            Assert.IsTrue(snapshot.Map.IsBuildableForSide(false, 2, 7), "(2,7) 应为对手可建造格");
            Assert.IsTrue(snapshot.Map.IsBuildableForSide(false, 3, 8), "(3,8) 应为对手可建造格");
            Assert.IsFalse(snapshot.Map.IsBuildableForSide(false, 3, 1), "(3,1) 不应为对手可建造格");
        }

        [Test]
        public void Snapshot_JsonProvider_Map_PathLength()
        {
            // golden-battle-bundle.json playerPathLength=17, opponentPathLength=17
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.AreEqual(17, snapshot.Map.GetPlayerPath().Count, "玩家路径长度应为 17");
            Assert.AreEqual(17, snapshot.Map.GetOpponentPath().Count, "对手路径长度应为 17");
        }

        // ====================================================================
        // 敌人配置测试
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_Enemy_HasCorrectValues()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.AreEqual("Mob0", snapshot.Enemy.Type, "敌人类型应为 Mob0");
            Assert.AreEqual(0, snapshot.Enemy.MapEnemyTypeIndex, "地图敌人类型索引应为 0");
            Assert.AreEqual(50, snapshot.Enemy.Speed, "敌人速度应为 50");
            Assert.AreEqual(1, snapshot.Enemy.ContactDamage, "接触伤害应为 1");
            Assert.AreEqual(20, snapshot.Enemy.HealthByWave.Count, "各波次血量数组应有 20 个元素");
            Assert.AreEqual(10, snapshot.Enemy.HealthByWave[0], "第 1 波血量应为 10");
            Assert.AreEqual(17315, snapshot.Enemy.HealthByWave[19], "第 20 波血量应为 17315");
            Assert.AreEqual(10, snapshot.Enemy.EarlyRoundHealthMultipliers.Count,
                "早期波次乘数应有 10 个元素");
            Assert.AreEqual(0.6f, snapshot.Enemy.EarlyRoundHealthMultipliers[0],
                "第 1 轮早期乘数应为 0.6");
        }

        // ====================================================================
        // 波次配置测试
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_Wave_HasCorrectValues()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.AreEqual(20, snapshot.Wave.WaveUnitCounts.Count, "波次数量应为 20");
            Assert.AreEqual(10, snapshot.Wave.WaveUnitCounts[0], "第 1 波怪物数应为 10");
            Assert.AreEqual(61, snapshot.Wave.WaveUnitCounts[19], "第 20 波怪物数应为 61");
            Assert.IsTrue(snapshot.Wave.SkipBoss, "本期应跳过 Boss");
            Assert.AreEqual(10000, snapshot.Wave.DelayTimeMs, "波间延迟应为 10000ms");
            Assert.AreEqual(20, snapshot.Wave.MaxRounds, "最大波次应为 20");

            // 生成策略
            Assert.AreEqual(3, snapshot.Wave.SpawnStrategyWeights.Count, "生成策略权重应有 3 个");
            Assert.AreEqual(5, snapshot.Wave.SpawnStrategyWeights[0], "第一个策略权重应为 5");
            Assert.AreEqual(3, snapshot.Wave.SpawnStrategies.Count, "应有 3 个生成策略表");
        }

        // ====================================================================
        // 单位配置测试
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_Units_HasFourSoldiers()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.AreEqual(4, snapshot.Units.Count, "应有 4 个单位配置");

            // 刀兵
            Assert.AreEqual("刀", snapshot.Units[0].Text, "第一个单位应为刀");
            Assert.AreEqual(1.5f, snapshot.Units[0].RangeCells, "刀兵攻击距离应为 1.5");
            Assert.AreEqual(3, snapshot.Units[0].AttackDamage, "刀兵攻击力应为 3");

            // 弓兵
            Assert.AreEqual("弓", snapshot.Units[1].Text, "第二个单位应为弓");
            Assert.AreEqual(3.5f, snapshot.Units[1].RangeCells, "弓兵攻击距离应为 3.5");

            // 枪兵
            Assert.AreEqual("枪", snapshot.Units[2].Text, "第三个单位应为枪");
            Assert.AreEqual(2.5f, snapshot.Units[2].RangeCells, "枪兵攻击距离应为 2.5");

            // 骑兵
            Assert.AreEqual("骑", snapshot.Units[3].Text, "第四个单位应为骑");
            Assert.AreEqual(2.0f, snapshot.Units[3].RangeCells, "骑兵攻击距离应为 2.0");
        }

        [Test]
        public void Snapshot_JsonProvider_UnitLevel_HasCorrectValues()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.AreEqual(3, snapshot.UnitLevel.MaxLevel, "最大等级应为 3");
            Assert.AreEqual(1f, snapshot.UnitLevel.DamageLevelMultipliers[0], "1级伤害乘数应为 1");
        }

        // ====================================================================
        // 经济配置测试
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_Economy_HasCorrectValues()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.AreEqual(20, snapshot.Economy.InitialGold, "初始金币应为 20");
            Assert.AreEqual(10, snapshot.Economy.RefreshCostStart, "刷新起始消耗应为 10");
            Assert.AreEqual(2, snapshot.Economy.RefreshCostIncrement, "刷新递增应为 2");
            Assert.AreEqual(1, snapshot.Economy.UnitBaseCost, "单位基础消耗应为 1");
            Assert.AreEqual(5, snapshot.Economy.HandSize, "手牌大小应为 5");
            Assert.AreEqual(3, snapshot.Economy.PlayerMaxHealth, "玩家最大生命应为 3");
            Assert.AreEqual(3, snapshot.Economy.OpponentMaxHealth, "对手最大生命应为 3");
        }

        // ====================================================================
        // 牌组配置测试
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_Deck_HasCorrectValues()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.IsTrue(snapshot.Deck.MinimalMode, "应为最简模式");
            Assert.AreEqual(4, snapshot.Deck.BaseSoldierTexts.Count, "基础兵字表应有 4 个元素");
            Assert.AreEqual("刀", snapshot.Deck.BaseSoldierTexts[0], "第一个兵应为刀");
            Assert.AreEqual("弓", snapshot.Deck.BaseSoldierTexts[1], "第二个兵应为弓");
            Assert.AreEqual("枪", snapshot.Deck.BaseSoldierTexts[2], "第三个兵应为枪");
            Assert.AreEqual("骑", snapshot.Deck.BaseSoldierTexts[3], "第四个兵应为骑");
            Assert.AreEqual(5, snapshot.Deck.HandSize, "手牌大小应为 5");
            Assert.AreEqual(1, snapshot.Deck.DefaultLevel, "默认等级应为 1");
            Assert.AreEqual(1, snapshot.Deck.BaseUnitCost, "基础单位消耗应为 1");
        }

        // ====================================================================
        // 投射物配置测试
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_Projectile_HasCorrectValues()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.AreEqual("SimpleDynamicArrow", snapshot.Projectile.PrimaryType,
                "主要投射物类型应为 SimpleDynamicArrow");
            Assert.AreEqual("TargetEnemyBezierMovement", snapshot.Projectile.MovementStrategy,
                "移动策略应为 TargetEnemyBezierMovement");
            Assert.AreEqual("HitEnemyStrategy", snapshot.Projectile.HitStrategy,
                "命中策略应为 HitEnemyStrategy");
        }

        // ====================================================================
        // IBattleConfigProvider 接口测试
        // ====================================================================

        [Test]
        public void IBattleConfigProvider_JsonProvider_ImplementsInterface()
        {
            IBattleConfigProvider provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.IsNotNull(snapshot, "IBattleConfigProvider.GetSnapshot 不应返回 null");
        }

        // ====================================================================
        // 不可变性验证（集合只读）
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_CollectionsAreReadOnly()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            // 验证集合为 IReadOnlyList（不可转型为可变集合修改）
            Assert.IsTrue(snapshot.Units is IReadOnlyList<UnitConfigSnapshot>,
                "Units 应为 IReadOnlyList<UnitConfigSnapshot>");
            Assert.IsTrue(snapshot.MissingFieldNotes is IReadOnlyList<string>,
                "MissingFieldNotes 应为 IReadOnlyList<string>");
            Assert.IsTrue(snapshot.Enemy.HealthByWave is IReadOnlyList<int>,
                "HealthByWave 应为 IReadOnlyList<int>");
        }

        // ====================================================================
        // 敌人目录 / 有序波次计划测试（tasks 2.1/2.2/2.6）
        // ====================================================================

        [Test]
        public void Snapshot_JsonProvider_EnemyCatalog_HasFourMobs()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.IsNotNull(snapshot.EnemyCatalog, "EnemyCatalog 不应为 null");
            Assert.AreEqual(4, snapshot.EnemyCatalog.Definitions.Count, "敌人目录应有 4 个定义");

            // 按 typeIndex 升序的稳定键列表
            Assert.AreEqual(4, snapshot.EnemyCatalog.NormalKeys.Count, "NormalKeys 应有 4 个键");
            Assert.AreEqual("Mob0", snapshot.EnemyCatalog.NormalKeys[0], "首个普通敌人键应为 Mob0");
            Assert.AreEqual("Mob3", snapshot.EnemyCatalog.NormalKeys[3], "末个普通敌人键应为 Mob3");

            // 双索引查询
            Assert.IsTrue(snapshot.EnemyCatalog.TryGetByKey("Mob2", out EnemyDefinitionSnapshot byKey),
                "应按 enemyKey 命中 Mob2");
            Assert.AreEqual(2, byKey.TypeIndex, "Mob2 的 typeIndex 应为 2");
            Assert.IsTrue(snapshot.EnemyCatalog.TryGetByTypeIndex(3, out EnemyDefinitionSnapshot byTypeIndex),
                "应按 typeIndex 命中");
            Assert.AreEqual("Mob3", byTypeIndex.Key, "typeIndex=3 应为 Mob3");
            Assert.IsFalse(snapshot.EnemyCatalog.TryGetByKey("Zombie", out _), "未知键不应命中");
        }

        [Test]
        public void Snapshot_JsonProvider_EnemyCatalog_DefinitionsAreValid()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            foreach (EnemyDefinitionSnapshot def in snapshot.EnemyCatalog.Definitions)
            {
                Assert.IsFalse(string.IsNullOrEmpty(def.ResourceAddress), "资源地址不应为空");
                Assert.Greater(def.MoveSpeed, 0, "移动速度应为正");
                Assert.Greater(def.HealthByWave.Count, 0, "血量曲线不应为空");
                Assert.Greater(def.EarlyRoundHealthMultipliers.Count, 0, "早期乘数不应为空");
                Assert.GreaterOrEqual(def.ContactDamage, 0, "接触伤害应非负");
                Assert.GreaterOrEqual(def.RewardGold, 0, "击杀奖励应非负");
            }
        }

        [Test]
        public void Snapshot_JsonProvider_OrderedWavePlan_HasOrderedRowsAndProfiles()
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            Assert.IsNotNull(snapshot.OrderedWavePlan, "OrderedWavePlan 不应为 null");
            Assert.AreEqual("golden", snapshot.OrderedWavePlan.ActivePlanId, "activePlanId 应为 golden");

            IReadOnlyList<WavePlanEntry> rows = snapshot.OrderedWavePlan.Rows;
            Assert.AreEqual(4, rows.Count, "黄金计划应有 4 行");
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.AreEqual(i + 1, rows[i].Order, "行应按 order 升序");
            }

            // 显式引用的策略 profile（0/1/2）被保留
            Assert.AreEqual(3, snapshot.OrderedWavePlan.ReferencedProfileIndexes.Count, "应保留 3 个 profile");
            Assert.IsTrue(snapshot.OrderedWavePlan.TryGetProfile(0, out IReadOnlyList<float> profile0));
            Assert.AreEqual(20, profile0.Count, "profile0 应有 20 项乘数");
        }

        [Test]
        public void Snapshot_JsonProvider_JsonAndLuban_EquivalentKeyFields()
        {
            // Json/Luban 等价性关键字段校验：目录与计划在 JSON 黄金路径下应满足
            // 与生产 Provider 相同的结构约束（行升序、键可解析、profile 显式引用）。
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();

            BattleConfigValidationResult result = BattleConfigValidator.Validate(snapshot);
            Assert.IsTrue(result.IsValid, "黄金 JSON 快照的目录+计划应通过校验。");
        }

        // ====================================================================
        // 深拷贝与不可变性测试（tasks 2.1/2.2）
        // ====================================================================

        [Test]
        public void EnemyDefinitionSnapshot_DeepCopiesSourceCollections()
        {
            var sourceHealth = new List<int> { 10, 11, 12 };
            var sourceMultipliers = new List<float> { 0.6f, 0.7f };
            var definition = new EnemyDefinitionSnapshot(
                0, "Mob0", "Mob0", 50, sourceHealth, sourceMultipliers, 1, 1);

            // 源集合后续修改不影响快照
            sourceHealth.Add(999);
            sourceMultipliers[0] = 0f;

            Assert.AreEqual(3, definition.HealthByWave.Count, "快照血量曲线应为构造时的深拷贝");
            Assert.AreEqual(10, definition.HealthByWave[0], "首项血量不应被源修改影响");
            Assert.AreEqual(0.6f, definition.EarlyRoundHealthMultipliers[0], "早期乘数不应被源修改影响");
        }

        [Test]
        public void OrderedWavePlanSnapshot_DeepCopiesProfiles()
        {
            var sourceProfile = new List<float> { 1f, 1.2f, 1.5f };
            var profiles = new Dictionary<int, IReadOnlyList<float>>
            {
                [0] = sourceProfile,
            };
            var plan = new OrderedWavePlanSnapshot("golden", new[] { NormalRowFixture(1) }, profiles);

            sourceProfile[0] = 99f;

            Assert.IsTrue(plan.TryGetProfile(0, out IReadOnlyList<float> profile));
            Assert.AreEqual(1f, profile[0], "profile 应为构造时的深拷贝");
        }

        private static WavePlanEntry NormalRowFixture(int order)
        {
            return new WavePlanEntry(
                "golden", order, WavePlanKind.Normal, "Mob0", 3, 0, "",
                1000, 500, 500, true, true, 0);
        }
    }
}
