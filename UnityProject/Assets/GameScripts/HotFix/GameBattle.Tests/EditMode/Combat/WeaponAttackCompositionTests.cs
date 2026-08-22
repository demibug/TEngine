using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    // ============================================================================
    // 任务 4.2 + 5.2：Weapon 攻击力组合测试
    // ----------------------------------------------------------------------------
    // 验证内容（specs/player-weapon-runtime/spec.md
    //   "Weapon attack power composes exactly once"）：
    //   1. 等级基础 20 + 基础武器 1 + 无 Buff → 有效攻击力 = 21。
    //   2. 等级基础 20 + 武器 1 + Buff flat 2 + ratio 1.5 →
    //      遵循 Buff 契约 (20 + 1 + 2) * 1.5 = 34.5 → 四舍五入 35。
    //   3. 等级变化不重复乘武器平值（等级倍率只作用于基础攻击力）。
    //   4. 武器初始化不覆盖/不重复 Buff 拥有的贡献（重复 ApplyBasicWeapon 幂等）。
    //   5. Buff 移除后恢复 基础 + 武器。
    // ============================================================================

    /// <summary>
    /// Weapon 攻击力组合测试（base+weapon+buff 只组合一次，等级不重乘武器）。
    /// </summary>
    [TestFixture]
    internal class WeaponAttackCompositionTests
    {
        private const float UnitWidth = 40f;
        private const float UnitHeight = 40f;
        private const float CellSize = 80f;
        private const int GridSize = 80;
        private const int OpponentAttackMultiplier = 1;

        /// <summary>基础攻击力（等级 1 倍率 1，供 base=20 场景）。</summary>
        private const int BaseAttack = 20;

        private RuntimeIdAllocator _idAllocator;
        private BattlePoolScope _poolScope;
        private BattleObjectPool<KnifeSoldier> _knifePool;
        private BattleObjectPool<BowSoldier> _bowPool;
        private BattleObjectPool<SpearSoldier> _spearPool;
        private BattleObjectPool<CavalrySoldier> _cavalryPool;
        private EnemyManager _enemyManager;
        private AttackResolver _attackResolver;
        private AttackEffectManager _attackEffectManager;
        private BattleObjectPool<SimpleDynamicArrow> _arrowPool;
        private ProjectileFactory _projectileFactory;
        private ProjectileManager _projectileManager;
        private UnitLevelService _levelService;
        private UnitFactory _factory;
        private BuffManager _buffManager;
        private UnitRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _idAllocator = new RuntimeIdAllocator();
            _poolScope = new BattlePoolScope();
            _knifePool = _poolScope.GetPool<KnifeSoldier>(() => new KnifeSoldier());
            _bowPool = _poolScope.GetPool<BowSoldier>(() => new BowSoldier());
            _spearPool = _poolScope.GetPool<SpearSoldier>(() => new SpearSoldier());
            _cavalryPool = _poolScope.GetPool<CavalrySoldier>(() => new CavalrySoldier());

            _enemyManager = new EnemyManager(GridSize);
            _attackResolver = new AttackResolver();
            _attackEffectManager = new AttackEffectManager();

            _arrowPool = _poolScope.GetPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            _projectileFactory = new ProjectileFactory(
                _idAllocator, _arrowPool, _enemyManager, CellSize);
            _projectileManager = new ProjectileManager(_projectileFactory);

            _levelService = new UnitLevelService(new UnitLevelConfigSnapshot(
                maxLevel: 3,
                damageLevelMultipliers: new float[] { 1f, 2f, 3f },
                attackSpeedLevelMultipliers: new float[] { 1f, 1f, 1f }));

            _factory = new UnitFactory(
                _idAllocator,
                _knifePool, _bowPool, _spearPool, _cavalryPool,
                _enemyManager, _attackResolver, _attackEffectManager,
                _projectileFactory, _projectileManager,
                CellSize, OpponentAttackMultiplier,
                _levelService);

            var definitions = new[]
            {
                new BuffDefinitionSnapshot(
                    0, "攻击", "", BuffKind.Numeric,
                    new[] { (int)BuffNumericChannel.AttackPower },
                    BuffStackPolicy.Add, 3, ""),
            };
            _buffManager = new BuffManager(
                new BuffCatalogSnapshot(definitions), new BattleActionScheduler());

            _registry = new UnitRegistry(_factory, CellSize, _buffManager, GoldenResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _registry?.GameOver();
            _buffManager?.Dispose();
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        private static WeaponCatalogSnapshot GoldenCatalog()
        {
            return new WeaponCatalogSnapshot(new[]
            {
                new WeaponDefinitionSnapshot(0, WeaponType.Bow, 1, true, "Basic"),
                new WeaponDefinitionSnapshot(10, WeaponType.Spear, 1, true, "Basic"),
                new WeaponDefinitionSnapshot(20, WeaponType.Knife, 1, true, "Basic"),
                new WeaponDefinitionSnapshot(31, WeaponType.Sword, 1, true, "Basic"),
            });
        }

        private static BasicWeaponResolver GoldenResolver()
        {
            return new BasicWeaponResolver(GoldenCatalog());
        }

        private static UnitConfigSnapshot KnifeConfig()
        {
            return new UnitConfigSnapshot(
                0, "刀", "knife", rangeCells: 2f, attackDamage: BaseAttack,
                attackIntervalSeconds: 0.8f, damageMode: "单体", targetPolicy: "nearest");
        }

        /// <summary>激活一个指定等级、指定阵营的刀兵（玩家自动装备默认武器）。</summary>
        private SoldierBase ActivateKnife(int level, bool side = true)
        {
            return _registry.ActivateBattleUnit(
                new BattleUnit(
                    100, side, UnitKind.Soldier, SoldierType.Knife, "刀", level),
                KnifeConfig(), _levelService, 0, 0, UnitWidth, UnitHeight);
        }

        /// <summary>对玩家刀兵追加攻击力 Buff（flat 2 + ratio 1.5），返回目标 handle。</summary>
        private BuffTargetHandle ApplyAttackBuff(SoldierBase soldier)
        {
            BuffTargetHandle handle = ((IBuffTarget)soldier).Handle;
            BuffOperationResult flat = _buffManager.Apply(new BuffApplyRequest(
                0, handle, new BuffSourceHandle(1), 2,
                BuffValueMode.Flat, BuffTimeMode.Permanent, 0));
            Assert.AreEqual(BuffOperationStatus.Applied, flat.Status, "flat Buff 应应用成功");

            BuffOperationResult ratio = _buffManager.Apply(new BuffApplyRequest(
                0, handle, new BuffSourceHandle(2), 0.5,
                BuffValueMode.Ratio, BuffTimeMode.Permanent, 0));
            Assert.AreEqual(BuffOperationStatus.Applied, ratio.Status, "ratio Buff 应应用成功");
            return handle;
        }

        // ====================================================================
        // 等级基础 20 → 21（spec "Level base is twenty"）
        // ====================================================================

        [Test]
        [Description("等级基础 20 + 基础武器 1 + 无 Buff → 有效攻击力 = 21。")]
        public void LevelBase20_Weapon1_NoBuff_Effective21()
        {
            SoldierBase soldier = ActivateKnife(level: 1);

            Assert.IsTrue(soldier.HasWeapon, "玩家刀兵应装备默认武器");
            Assert.AreEqual(20, soldier.WeaponId, "刀兵应解析到 id=20");
            Assert.AreEqual(1, soldier.WeaponAttackPower, "武器附加攻击力应为 1");
            Assert.AreEqual(21, soldier.AttackDamageForTest,
                "等级基础 20 + 武器 1 = 21");
        }

        // ====================================================================
        // Buff 公式（spec "Weapon and Buff are active"）
        // ====================================================================

        [Test]
        [Description("等级基础 20 + 武器 1 + Buff flat 2 + ratio 1.5 → (20+1+2)*1.5=34.5 → 35。")]
        public void WeaponAndBuffActive_UsesContractFormula()
        {
            SoldierBase soldier = ActivateKnife(level: 1);
            BuffTargetHandle handle = ApplyAttackBuff(soldier);

            Assert.AreEqual(35, soldier.AttackDamageForTest,
                "(20 + 1 + 2) * 1.5 = 34.5 → 四舍五入 35");

            // 移除 Buff 后恢复 基础 + 武器（Weapon 值不被 Buff 覆盖，也不残留 Buff 值）。
            IReadOnlyList<BuffInstanceSnapshot> snapshots =
                _buffManager.GetTargetSnapshots(handle);
            Assert.IsTrue(snapshots.Count > 0, "应有活动 Buff 实例");
            for (int i = 0; i < snapshots.Count; i++)
            {
                _buffManager.RemoveInstance(snapshots[i].InstanceId);
            }

            Assert.AreEqual(21, soldier.AttackDamageForTest,
                "Buff 移除后恢复 基础 20 + 武器 1 = 21");
        }

        // ====================================================================
        // 等级倍率不重乘武器（spec "The level multiplier MUST NOT multiply the Weapon flat value again"）
        // ====================================================================

        [Test]
        [Description("等级 2 倍率 2：基础 20→40，武器保持 +1，攻击力 = 41（而非 42）。")]
        public void LevelChange_DoesNotMultiplyWeaponAgain()
        {
            SoldierBase soldier = ActivateKnife(level: 1);
            Assert.AreEqual(21, soldier.AttackDamageForTest, "等级 1：20 + 1 = 21");

            soldier.ApplyLevel(2);
            Assert.AreEqual(40, soldier.AttackDamageForTest - soldier.WeaponAttackPower,
                "等级 2 基础 = 20 × 2 = 40");
            Assert.AreEqual(1, soldier.WeaponAttackPower, "等级变化不得放大武器平值");
            Assert.AreEqual(41, soldier.AttackDamageForTest,
                "等级 2：40 + 1 = 41（若重乘武器会是 42）");
        }

        [Test]
        [Description("带 Buff 时升级：武器平值仍只计入一次（40+1+2)*1.5=64.5 → 65，而非 66）。")]
        public void LevelChange_WithBuff_WeaponCountedOnce()
        {
            SoldierBase soldier = ActivateKnife(level: 1);
            ApplyAttackBuff(soldier);
            Assert.AreEqual(35, soldier.AttackDamageForTest, "等级 1 带 Buff：(20+1+2)*1.5=34.5 → 35");

            soldier.ApplyLevel(2);
            BuffTargetHandle handle = ((IBuffTarget)soldier).Handle;
            BuffOperationResult refresh = _buffManager.RefreshTarget(
                handle, new BuffNumericChannel[] { BuffNumericChannel.AttackPower });
            Assert.AreEqual(BuffOperationStatus.Refreshed, refresh.Status, "等级变化后应重算 Buff");

            Assert.AreEqual(65, soldier.AttackDamageForTest,
                "等级 2 带 Buff：(40+1+2)*1.5=64.5 → 65（若重乘武器会是 66）");
        }

        // ====================================================================
        // 武器初始化不覆盖/不重复 Buff 贡献（spec "Weapon initialization MUST NOT overwrite or duplicate"）
        // ====================================================================

        [Test]
        [Description("重复 ApplyBasicWeapon 幂等：不重复计入 Buff 与武器贡献。")]
        public void ReapplyWeapon_DoesNotDuplicateContribution()
        {
            SoldierBase soldier = ActivateKnife(level: 1);
            ApplyAttackBuff(soldier);
            Assert.AreEqual(35, soldier.AttackDamageForTest, "应用 Buff 后为 35");

            // 模拟重复初始化写入同一武器（池复用路径不会发生，但防御幂等）。
            soldier.ApplyBasicWeapon(20, 1);

            Assert.AreEqual(35, soldier.AttackDamageForTest,
                "重复写入同武器不得重复计入 Buff/武器贡献");
            Assert.AreEqual(1, soldier.WeaponAttackPower, "武器附加攻击力仍为 1");
        }
    }
}