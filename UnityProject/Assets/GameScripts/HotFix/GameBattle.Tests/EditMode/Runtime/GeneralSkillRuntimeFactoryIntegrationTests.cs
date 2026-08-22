using System;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Runtime
{
    // ============================================================================
    // BattleRuntimeFactory 武将主动技能装配 EditMode 集成测试
    // ----------------------------------------------------------------------------
    // 直接通过 BattleRuntimeFactory.Create（注入构造快照 + BattlePoolScope）验证
    // 生产装配的武将主动技能链（共享 SkillRunner）：
    //   1. 快照含张飞(BattleShout) + 黄忠(FireArrowBarrage) 且无 Boss：
    //      Factory Create 成功、assembly.SkillRunner 非 null；两个 General 依次通过
    //      UnitRegistry.ActivateBattleUnit 上场后 SkillRunner OwnerCount=2，且
    //      TryGetState 能分别读到 BattleShout/FireArrowBarrage。
    //   2. 同一快照两个武将 SkillKey 引用生效，ClearForSettling 后
    //      SkillRunner OwnerCount=0（生命周期清理）。
    //   3. 无武将 SkillKey 且无 Boss 的兼容快照：Factory Create 成功并保持
    //      assembly.SkillRunner == null。
    //
    // 本测试不接触 Scene、FUI 或资源加载，不通过反射探测私有字段，不复制生产逻辑，
    // 不运行 Unity，符合纯逻辑 EditMode 约束。
    // ============================================================================

    /// <summary>
    /// BattleRuntimeFactory 武将主动技能装配集成测试。
    /// </summary>
    [TestFixture]
    internal class GeneralSkillRuntimeFactoryIntegrationTests
    {
        // ====================================================================
        // 常量
        // ====================================================================

        private const float UnitWidth = 40f;
        private const float UnitHeight = 40f;

        // ====================================================================
        // 快照构造
        // ====================================================================

        /// <summary>
        /// 以 JsonBattleConfigProvider golden 快照为基底，替换为最小合法连续通道 MapData，
        /// 构造含张飞(BattleShout) + 黄忠(FireArrowBarrage) 的 SkillCatalog 与 GeneralCatalog，
        /// 保留 golden 的 EnemyCatalog/OrderedWavePlan/BuffCatalog/WeaponCatalog 等。
        /// </summary>
        private static BattleConfigSnapshot CreateGeneralSkillSnapshot()
        {
            BattleConfigSnapshot golden = new JsonBattleConfigProvider().GetSnapshot();

            // 最小合法连续通道地图（3x3 全 Passage），替换 golden 中不满足当前契约的路径。
            const int width = 3;
            const int height = 3;
            var cells = new GridCell[width * height];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = new GridCell(GridCellKind.Passage, BuildableSide.None);
            }

            var map = new MapData(
                cells,
                width,
                height,
                mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, 2),
                opponentStart: new GridPosition(2, 2),
                opponentEnd: new GridPosition(2, 0),
                playerPath: new[]
                {
                    new GridPosition(0, 0),
                    new GridPosition(0, 1),
                    new GridPosition(0, 2),
                },
                opponentPath: new[]
                {
                    new GridPosition(2, 2),
                    new GridPosition(2, 1),
                    new GridPosition(2, 0),
                });

            // 构造 SkillCatalog：BattleShout + FireArrowBarrage + SoulCapture（保留 golden
            // Boss 技能定义以兼容 BossCatalog 引用，虽然本快照无 Boss 行）。
            // Validator 对武将引用的主动技能要求：
            //   - Category == Active
            //   - TriggerAttackCount 有值且 > 0
            //   - handlerKey 非空
            //   - BattleShout: RangeTiles > 0, EffectBuffType 有值, EffectDurationMs > 0
            //   - FireArrowBarrage: RangeTiles > 0, EffectDamageMultiplier 有值且正
            var skillCatalog = new SkillCatalogSnapshot(new[]
            {
                new SkillDefinitionSnapshot(
                    key: "BattleShout",
                    category: SkillCategory.Active,
                    cooldownMs: 0,
                    handlerKey: "BattleShout",
                    effectBuffType: 8,
                    effectDurationMs: 2000,
                    rangeTiles: 3.5f,
                    triggerAttackCount: 15,
                    effectDamageMultiplier: null),
                new SkillDefinitionSnapshot(
                    key: "FireArrowBarrage",
                    category: SkillCategory.Active,
                    cooldownMs: 0,
                    handlerKey: "FireArrowBarrage",
                    effectBuffType: null,
                    effectDurationMs: null,
                    rangeTiles: 5.5f,
                    triggerAttackCount: 30,
                    effectDamageMultiplier: 2.0f),
                new SkillDefinitionSnapshot(
                    key: "SoulCapture",
                    category: SkillCategory.Boss,
                    cooldownMs: 8000,
                    handlerKey: "SoulCapture",
                    effectBuffType: 13,
                    effectDurationMs: 2000,
                    rangeTiles: 2f),
            });

            // 构造 GeneralCatalog：张飞(Pike, BattleShout) + 黄忠(Bow, FireArrowBarrage)。
            // Validator 要求：
            //   - PartWords.Count == 2，两字非空且不同
            //   - RangeCells > 0, AttackDamage > 0, AttackIntervalSeconds > 0, PartRecruitWeight > 0
            //   - PrefabAddress/AnimationKey/DamageMode/TargetPolicy 非空
            //   - Bow: ProjectileType 非空且在 snapshot.Projectile.Types 中, ProjectileSpeed > 0
            //   - Pike: 无 ProjectileType
            var generalCatalog = new GeneralCatalogSnapshot(new[]
            {
                new GeneralConfigSnapshot(
                    index: 1,
                    name: "张飞",
                    family: "张",
                    partWords: new[] { "张", "飞" },
                    combatArchetype: GeneralCombatArchetype.Pike,
                    rangeCells: 2.5f,
                    attackDamage: 2,
                    attackIntervalSeconds: 0.8f,
                    damageMode: "近战枪击",
                    targetPolicy: "nearest",
                    prefabAddress: "SpearSoldier",
                    animationKey: "pike",
                    projectileType: "",
                    projectileSpeed: 0,
                    partRecruitWeight: 1,
                    skillKey: "BattleShout"),
                new GeneralConfigSnapshot(
                    index: 2,
                    name: "黄忠",
                    family: "黄",
                    partWords: new[] { "黄", "忠" },
                    combatArchetype: GeneralCombatArchetype.Bow,
                    rangeCells: 3.5f,
                    attackDamage: 2,
                    attackIntervalSeconds: 0.8f,
                    damageMode: "单体",
                    targetPolicy: "closest_end",
                    prefabAddress: "BowSoldier",
                    animationKey: "bow",
                    projectileType: "SimpleDynamicArrow",
                    projectileSpeed: 200,
                    partRecruitWeight: 1,
                    skillKey: "FireArrowBarrage"),
            });

            return new BattleConfigSnapshot(
                map: map,
                enemy: golden.Enemy,
                wave: golden.Wave,
                units: golden.Units,
                unitLevel: golden.UnitLevel,
                economy: golden.Economy,
                deck: golden.Deck,
                projectile: golden.Projectile,
                missingFieldNotes: golden.MissingFieldNotes,
                sourceTag: "Test",
                enemyCatalog: golden.EnemyCatalog,
                orderedWavePlan: golden.OrderedWavePlan,
                buffCatalog: golden.BuffCatalog,
                skillCatalog: skillCatalog,
                bossCatalog: golden.BossCatalog,
                weaponCatalog: golden.WeaponCatalog,
                generalCatalog: generalCatalog);
        }

        /// <summary>
        /// 以 golden 快照为基底替换为最小合法连续通道 MapData，不构造 GeneralCatalog
        /// （无武将 SkillKey），保留 golden 的 EnemyCatalog/OrderedWavePlan（全 Normal 行，无 Boss）。
        /// </summary>
        private static BattleConfigSnapshot CreateNoGeneralSkillSnapshot()
        {
            BattleConfigSnapshot golden = new JsonBattleConfigProvider().GetSnapshot();

            const int width = 3;
            const int height = 3;
            var cells = new GridCell[width * height];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = new GridCell(GridCellKind.Passage, BuildableSide.None);
            }

            var map = new MapData(
                cells,
                width,
                height,
                mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(0, 2),
                opponentStart: new GridPosition(2, 2),
                opponentEnd: new GridPosition(2, 0),
                playerPath: new[]
                {
                    new GridPosition(0, 0),
                    new GridPosition(0, 1),
                    new GridPosition(0, 2),
                },
                opponentPath: new[]
                {
                    new GridPosition(2, 2),
                    new GridPosition(2, 1),
                    new GridPosition(2, 0),
                });

            return new BattleConfigSnapshot(
                map: map,
                enemy: golden.Enemy,
                wave: golden.Wave,
                units: golden.Units,
                unitLevel: golden.UnitLevel,
                economy: golden.Economy,
                deck: golden.Deck,
                projectile: golden.Projectile,
                missingFieldNotes: golden.MissingFieldNotes,
                sourceTag: "Test",
                enemyCatalog: golden.EnemyCatalog,
                orderedWavePlan: golden.OrderedWavePlan,
                buffCatalog: golden.BuffCatalog,
                skillCatalog: golden.SkillCatalog,
                bossCatalog: golden.BossCatalog,
                weaponCatalog: golden.WeaponCatalog);
            // generalCatalog 不传：默认空 GeneralCatalogSnapshot，无武将 SkillKey。
        }

        // ====================================================================
        // 测试 1：两个武将上场后 SkillRunner OwnerCount=2 且 TryGetState 可读
        // ====================================================================

        [Test]
        [Description("快照含张飞(BattleShout)和黄忠(FireArrowBarrage)无 Boss：Factory Create 成功、" +
                     "SkillRunner 非 null；两个 General 依次 ActivateBattleUnit 上场后 OwnerCount=2，" +
                     "TryGetState 分别读到 BattleShout/FireArrowBarrage。")]
        public void TwoGenerals_WithSkillKeys_FactoryCreatesAndActivatesSkills()
        {
            BattleConfigSnapshot snapshot = CreateGeneralSkillSnapshot();
            var loadout = new BattleLoadoutDto(
                mapId: 0, round: 0, randomSeed: 42, configVersion: 0,
                configHash: string.Empty, deckPreset: BattleDeckPreset.Normal);
            var poolScope = new BattlePoolScope();

            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout, poolScope, bindings: null, configSnapshot: snapshot);

            Assert.IsTrue(assembly.IsSuccess, $"组装应成功：{assembly.DiagnosticMessage}");
            Assert.IsNotNull(assembly.SkillRunner, "有武将 SkillKey 时应装配 SkillRunner。");

            // 张飞上场。
            GeneralConfigSnapshot zhangFei = snapshot.GeneralCatalog.GetByIndexOrDefault(1);
            Assert.IsNotNull(zhangFei, "张飞定义应存在。");
            BattleUnit zhangFeiUnit = BattleUnit.CreateGeneral(500, true, zhangFei);
            SoldierBase zhangFeiSoldier = assembly.UnitRegistry.ActivateBattleUnit(
                zhangFeiUnit, zhangFei.ToUnitConfigSnapshot(), assembly.LevelService,
                0, 1, UnitWidth, UnitHeight);

            Assert.IsNotNull(zhangFeiSoldier, "张飞应成功上场。");
            Assert.AreEqual(1, assembly.SkillRunner.OwnerCount, "张飞上场后 OwnerCount=1。");

            var zhangFeiHandle = new SkillOwnerHandle(
                zhangFeiSoldier.Id, zhangFeiSoldier.LifecycleGeneration);
            Assert.IsTrue(
                assembly.SkillRunner.TryGetState(zhangFeiHandle, "BattleShout", out SkillStateSnapshot zhangFeiState),
                "张飞上场后应能 TryGetState BattleShout。");
            Assert.IsFalse(zhangFeiState.IsRunning, "首次上场 BattleShout 不应正在运行。");

            // 黄忠上场。
            GeneralConfigSnapshot huangZhong = snapshot.GeneralCatalog.GetByIndexOrDefault(2);
            Assert.IsNotNull(huangZhong, "黄忠定义应存在。");
            BattleUnit huangZhongUnit = BattleUnit.CreateGeneral(501, true, huangZhong);
            SoldierBase huangZhongSoldier = assembly.UnitRegistry.ActivateBattleUnit(
                huangZhongUnit, huangZhong.ToUnitConfigSnapshot(), assembly.LevelService,
                1, 1, UnitWidth, UnitHeight);

            Assert.IsNotNull(huangZhongSoldier, "黄忠应成功上场。");
            Assert.AreEqual(2, assembly.SkillRunner.OwnerCount, "两个武将上场后 OwnerCount=2。");

            var huangZhongHandle = new SkillOwnerHandle(
                huangZhongSoldier.Id, huangZhongSoldier.LifecycleGeneration);
            Assert.IsTrue(
                assembly.SkillRunner.TryGetState(huangZhongHandle, "FireArrowBarrage", out SkillStateSnapshot huangZhongState),
                "黄忠上场后应能 TryGetState FireArrowBarrage。");
            Assert.IsFalse(huangZhongState.IsRunning, "首次上场 FireArrowBarrage 不应正在运行。");

            // 清理。
            assembly.UnitRegistry.ClearForSettling();
            Assert.IsTrue(poolScope.AssertAllActiveReleased(), "清理后池租借应对称归还。");
        }

        // ====================================================================
        // 测试 2：ClearForSettling 后 SkillRunner OwnerCount=0（生命周期清理）
        // ====================================================================

        [Test]
        [Description("同一快照两个武将 SkillKey 引用生效，ClearForSettling 后 " +
                     "SkillRunner OwnerCount=0（生命周期清理）。")]
        public void ClearForSettling_ClearsSkillRunnerOwners()
        {
            BattleConfigSnapshot snapshot = CreateGeneralSkillSnapshot();
            var loadout = new BattleLoadoutDto(
                mapId: 0, round: 0, randomSeed: 42, configVersion: 0,
                configHash: string.Empty, deckPreset: BattleDeckPreset.Normal);
            var poolScope = new BattlePoolScope();

            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout, poolScope, bindings: null, configSnapshot: snapshot);

            Assert.IsTrue(assembly.IsSuccess, assembly.DiagnosticMessage);
            Assert.IsNotNull(assembly.SkillRunner, "应装配 SkillRunner。");

            GeneralConfigSnapshot zhangFei = snapshot.GeneralCatalog.GetByIndexOrDefault(1);
            GeneralConfigSnapshot huangZhong = snapshot.GeneralCatalog.GetByIndexOrDefault(2);
            BattleUnit zhangFeiUnit = BattleUnit.CreateGeneral(500, true, zhangFei);
            BattleUnit huangZhongUnit = BattleUnit.CreateGeneral(501, true, huangZhong);

            assembly.UnitRegistry.ActivateBattleUnit(
                zhangFeiUnit, zhangFei.ToUnitConfigSnapshot(), assembly.LevelService,
                0, 1, UnitWidth, UnitHeight);
            assembly.UnitRegistry.ActivateBattleUnit(
                huangZhongUnit, huangZhong.ToUnitConfigSnapshot(), assembly.LevelService,
                1, 1, UnitWidth, UnitHeight);

            Assert.AreEqual(2, assembly.SkillRunner.OwnerCount, "两个武将上场后 OwnerCount=2。");

            // ClearForSettling 等价于 GameOver，移除全部活动单位并调用 _skillRuntime.Clear()。
            assembly.UnitRegistry.ClearForSettling();

            Assert.AreEqual(0, assembly.SkillRunner.OwnerCount,
                "ClearForSettling 后 SkillRunner OwnerCount 应为 0（生命周期清理）。");
            Assert.IsTrue(poolScope.AssertAllActiveReleased(), "清理后池租借应对称归还。");
        }

        // ====================================================================
        // 测试 3：无武将 SkillKey 且无 Boss 的兼容快照 SkillRunner == null
        // ====================================================================

        [Test]
        [Description("无武将 SkillKey 且无 Boss 的兼容快照：Factory Create 成功并保持 " +
                     "assembly.SkillRunner == null。")]
        public void NoGeneralSkill_NoBoss_FactoryCreatesWithNullSkillRunner()
        {
            BattleConfigSnapshot snapshot = CreateNoGeneralSkillSnapshot();
            var loadout = new BattleLoadoutDto(
                mapId: 0, round: 0, randomSeed: 42, configVersion: 0,
                configHash: string.Empty, deckPreset: BattleDeckPreset.Normal);
            var poolScope = new BattlePoolScope();

            BattleRuntimeAssembly assembly = BattleRuntimeFactory.Create(
                loadout, poolScope, bindings: null, configSnapshot: snapshot);

            Assert.IsTrue(assembly.IsSuccess, $"组装应成功：{assembly.DiagnosticMessage}");
            Assert.IsNull(assembly.SkillRunner,
                "无武将 SkillKey 且无 Boss 时 SkillRunner 应为 null（旧兼容路径）。");

            // 确认无 Boss 行。
            bool hasBoss = false;
            foreach (WavePlanEntry row in snapshot.OrderedWavePlan.Rows)
            {
                if (row.Kind == WavePlanKind.Boss)
                {
                    hasBoss = true;
                    break;
                }
            }
            Assert.IsFalse(hasBoss, "golden 快照应无 Boss 行。");

            // 无单位/敌人产生，池应已对称。
            Assert.IsTrue(poolScope.AssertAllActiveReleased(), "无实体产生时池应已对称。");
        }
    }
}
