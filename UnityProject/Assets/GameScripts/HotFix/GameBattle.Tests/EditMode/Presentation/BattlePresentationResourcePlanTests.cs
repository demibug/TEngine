using System;
using System.Collections.Generic;
using GameBattle;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Presentation
{
    [TestFixture]
    internal sealed class BattlePresentationResourcePlanTests
    {
        [Test]
        public void CollectEnemyResourceAddresses_UsesNormalRowsAndStableFirstOccurrenceOrder()
        {
            BattleConfigSnapshot snapshot = CreateSnapshot(
                new[]
                {
                    NormalRow(1, "Mob2"),
                    BossRow(2),
                    NormalRow(3, "Mob1"),
                    NormalRow(4, "Mob2"),
                    NormalRow(5, "Mob3"),
                });

            IReadOnlyList<string> addresses =
                BattlePresentationResourcePlan.CollectEnemyResourceAddresses(snapshot);

            CollectionAssert.AreEqual(
                new[] { "Mob2", "ZhangLiangPrefab", "Mob1", "Mob3" },
                addresses,
                "表现预加载地址必须来自所选 Normal/Boss 行，并按首次出现顺序稳定去重。");
        }

        [Test]
        public void CollectEnemyResourceAddresses_UnknownNormalEnemy_FailsExplicitly()
        {
            BattleConfigSnapshot snapshot = CreateSnapshot(new[] { NormalRow(1, "MissingMob") });

            BattleConfigDataException exception = Assert.Throws<BattleConfigDataException>(() =>
                BattlePresentationResourcePlan.CollectEnemyResourceAddresses(snapshot));

            Assert.AreEqual(BattleConfigErrorCategory.EnemyKeyUnknown, exception.Category);
            StringAssert.Contains("WavePlan.1.EnemyKey", exception.Path);
        }

        [Test]
        public void EnemySpawnViewData_IsImmutableValuePayload()
        {
            var dto = new EnemySpawnViewData(41, "Mob3", "Mob3", true, 12.5f, -3f);

            Assert.AreEqual(41, dto.RuntimeId);
            Assert.AreEqual("Mob3", dto.EnemyKey);
            Assert.AreEqual("Mob3", dto.ResourceAddress);
            Assert.IsTrue(dto.IsPlayerLane);
            Assert.AreEqual(12.5f, dto.LogicX);
            Assert.AreEqual(-3f, dto.LogicY);
        }

        [Test]
        public void CollectEnemyResourceAddresses_EmptyBossResource_FailsProductionExplicitly()
        {
            BattleConfigSnapshot snapshot = CreateSnapshot(
                new[] { BossRow(1) },
                bossResourcePath: string.Empty);

            BattlePresentationLoadException exception = Assert.Throws<BattlePresentationLoadException>(() =>
                BattlePresentationResourcePlan.CollectEnemyResourceAddresses(snapshot));

            Assert.AreEqual("BossPresentationUnavailable", exception.Operation);
            StringAssert.Contains("ZhangLiang", exception.ResourceAddress);
        }

        [Test]
        public void CollectEnemyResourceAddresses_EmptyBossResource_AllowsNullViewLogic()
        {
            BattleConfigSnapshot snapshot = CreateSnapshot(
                new[] { BossRow(1), NormalRow(2, "Mob1") },
                bossResourcePath: string.Empty);

            IReadOnlyList<string> addresses =
                BattlePresentationResourcePlan.CollectEnemyResourceAddresses(
                    snapshot, requireBossPresentation: false);

            CollectionAssert.AreEqual(new[] { "Mob1" }, addresses);
        }

        [Test]
        public void EnemySpawnViewData_CarriesBossPresentationContract()
        {
            var dto = new EnemySpawnViewData(
                42, "ZhangLiang", "ZhangLiangPrefab", true, 10f, 20f,
                EnemyPresentationKind.Boss, "ZhangLiang", 84.33f, 101.25f,
                "goliang", "attackliang");

            Assert.AreEqual(EnemyPresentationKind.Boss, dto.Kind);
            Assert.AreEqual("ZhangLiang", dto.BossKey);
            Assert.AreEqual(84.33f, dto.LogicalWidth);
            Assert.AreEqual(101.25f, dto.LogicalHeight);
            Assert.AreEqual("goliang", dto.IdleAnimationKey);
            Assert.AreEqual("attackliang", dto.SkillAnimationKey);
        }

        private static BattleConfigSnapshot CreateSnapshot(
            IReadOnlyList<WavePlanEntry> rows,
            string bossResourcePath = "ZhangLiangPrefab")
        {
            MapData map = MapData.FromColumnMajorGrid(
                new IReadOnlyList<string>[]
                {
                    new[] { "0_1", "0_1" },
                    new[] { "0_1", "0_1" },
                },
                BattleConfigNormalizer.DecodeCell,
                0,
                new GridPosition(0, 1),
                new GridPosition(1, 1),
                new GridPosition(1, 0),
                new GridPosition(0, 0),
                new[] { new GridPosition(0, 1), new GridPosition(1, 1) },
                new[] { new GridPosition(1, 0), new GridPosition(0, 0) });

            var definitions = new[]
            {
                Definition(0, "Mob0"),
                Definition(1, "Mob1"),
                Definition(2, "Mob2"),
                Definition(3, "Mob3"),
            };
            var profiles = new Dictionary<int, IReadOnlyList<float>>
            {
                [0] = new float[] { 1f },
            };

            return new BattleConfigSnapshot(
                map,
                new EnemyConfigSnapshot("Mob0", 0, 50, new[] { 10 }, new[] { 1f }, 1),
                new WaveConfigSnapshot(
                    new[] { 1 }, Array.Empty<int>(), Array.Empty<float>(), new[] { 1 },
                    new IReadOnlyList<float>[] { new float[] { 1f } }, true, 100, 1),
                Array.Empty<UnitConfigSnapshot>(),
                new UnitLevelConfigSnapshot(3, new[] { 1f }, new[] { 1f }),
                new EconomyConfigSnapshot(20, 10, 2, 1, 5, 3, 3),
                new DeckConfigSnapshot(true, new[] { "刀", "弓", "枪", "骑" }, 5, 1, 1),
                new ProjectileConfigSnapshot(
                    new[] { "SimpleDynamicArrow" }, "SimpleDynamicArrow",
                    "TargetEnemyBezierMovement", "HitEnemyStrategy"),
                Array.Empty<string>(),
                "Test",
                new EnemyCatalogSnapshot(definitions),
                new OrderedWavePlanSnapshot("test", rows, profiles),
                bossCatalog: new BossCatalogSnapshot(new[]
                {
                    new BossDefinitionSnapshot(
                        "ZhangLiang", "张梁", "SoulCapture", "boss0", bossResourcePath,
                        "attackliang", "goliang", new BossTimelineSnapshot(500, 1400),
                        true, 7f, 10f, 1, 10, 84.33f, 101.25f),
                }));
        }

        private static EnemyDefinitionSnapshot Definition(int typeIndex, string key)
        {
            return new EnemyDefinitionSnapshot(
                typeIndex, key, key, 50, new[] { 10 }, new[] { 1f }, 1, 1);
        }

        private static WavePlanEntry NormalRow(int order, string enemyKey)
        {
            return new WavePlanEntry(
                "test", order, WavePlanKind.Normal, enemyKey, 1, 0, string.Empty,
                0, 0, 0, true, true, 0);
        }

        private static WavePlanEntry BossRow(int order)
        {
            return new WavePlanEntry(
                "test", order, WavePlanKind.Boss, string.Empty, 0, 0, "ZhangLiang",
                0, 0, 0, true, true, 0);
        }
    }
}
