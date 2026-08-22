using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Unit
{
    // ============================================================================
    // 任务 4.1/4.3/4.4/4.5 + 5.1：Weapon 运行时接入测试
    // ----------------------------------------------------------------------------
    // 验证内容（design.md 决策 3/4 / specs/player-weapon-runtime/spec.md
    //   "Player Soldiers receive one default weapon value" /
    //   "Pool reset removes weapon state"）：
    //   1. 四类玩家兵种经 UnitRegistry 创建后各获得映射武器 id（Knife→20、
    //      Bow→0、Spear→10、Cavalry→31）且 WeaponAttackPower=1，攻击力 = 基础 + 1。
    //   2. 对手 Soldier 不装备：WeaponId=-1、WeaponAttackPower=0、HasWeapon=false。
    //   3. ResetState 无条件清除武器状态（普通移除、池复用）。
    //   4. 解析失败（映射缺失）沿创建事务回滚：士兵归还池、无半初始化残留，
    //      池复用后无武器残留。
    // ============================================================================

    /// <summary>
    /// Weapon 运行时接入测试（玩家默认武器 + Opponent 跳过 + reset/回滚/池复用）。
    /// </summary>
    [TestFixture]
    internal class WeaponRuntimeTests
    {
        private const float UnitWidth = 40f;
        private const float UnitHeight = 40f;
        private const float CellSize = 80f;
        private const int GridSize = 80;
        private const int OpponentAttackMultiplier = 1;

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
        private UnitFactory _factory;
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

            _factory = new UnitFactory(
                _idAllocator,
                _knifePool, _bowPool, _spearPool, _cavalryPool,
                _enemyManager, _attackResolver, _attackEffectManager,
                _projectileFactory, _projectileManager,
                CellSize, OpponentAttackMultiplier);

            _registry = new UnitRegistry(_factory, CellSize, null, GoldenResolver());
        }

        [TearDown]
        public void TearDown()
        {
            _registry?.GameOver();
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>构造四 Basic +1 启用的合法武器目录（与 weapon.xlsx 首期一致）。</summary>
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

        /// <summary>构造默认基础武器 resolver（四类玩家均可解析）。</summary>
        private static BasicWeaponResolver GoldenResolver()
        {
            return new BasicWeaponResolver(GoldenCatalog());
        }

        /// <summary>构造指定兵种配置（基础攻击力统一 20，便于 +1 断言）。</summary>
        private static UnitConfigSnapshot MakeConfig(int index, string text, string animKey)
        {
            return new UnitConfigSnapshot(
                index, text, animKey, rangeCells: 2f, attackDamage: 20,
                attackIntervalSeconds: 0.8f, damageMode: "单体", targetPolicy: "nearest");
        }

        private static UnitConfigSnapshot KnifeConfig => MakeConfig(0, "刀", "knife");
        private static UnitConfigSnapshot BowConfig => MakeConfig(1, "弓", "bow");
        private static UnitConfigSnapshot SpearConfig => MakeConfig(2, "枪", "pike");
        private static UnitConfigSnapshot CavalryConfig => MakeConfig(3, "骑", "cavalry");

        /// <summary>四类玩家兵种及其期望武器 id（显式映射：Knife→20/Bow→0/Spear→10/Cavalry→31）。</summary>
        private static readonly (SoldierType Type, UnitConfigSnapshot Config, int ExpectedId)[] PlayerCases =
        {
            (SoldierType.Knife, KnifeConfig, 20),
            (SoldierType.Bow, BowConfig, 0),
            (SoldierType.Spear, SpearConfig, 10),
            (SoldierType.Cavalry, CavalryConfig, 31),
        };

        // ====================================================================
        // 四玩家类别获得指定武器（task 5.1）
        // ====================================================================

        [Test]
        [Description("四类玩家兵种各获得映射武器 id 且 WeaponAttackPower=1，攻击力=基础+1。")]
        public void CreateAndPlace_PlayerFourCategories_GetMappedWeaponPlusOne()
        {
            for (int i = 0; i < PlayerCases.Length; i++)
            {
                (SoldierType type, UnitConfigSnapshot config, int expectedId) = PlayerCases[i];
                SoldierBase soldier = _registry.CreateAndPlace(
                    type, config, side: true, i, 0, UnitWidth, UnitHeight);

                Assert.IsTrue(soldier.Side, $"{type} 应为玩家方");
                Assert.IsTrue(soldier.HasWeapon, $"{type} 应装备基础武器");
                Assert.AreEqual(expectedId, soldier.WeaponId, $"{type} 应解析到 id={expectedId}");
                Assert.AreEqual(1, soldier.WeaponAttackPower, $"{type} 附加攻击力应为 1");
                Assert.AreEqual(21, soldier.AttackDamageForTest,
                    $"{type} 基础 20 + 武器 1 = 21");
            }
        }

        [Test]
        [Description("对手 Soldier 不装备：WeaponId=-1、WeaponAttackPower=0、HasWeapon=false。")]
        public void CreateAndPlace_Opponent_NoWeapon_ZeroContribution()
        {
            for (int i = 0; i < PlayerCases.Length; i++)
            {
                (SoldierType type, UnitConfigSnapshot config, _) = PlayerCases[i];
                SoldierBase soldier = _registry.CreateAndPlace(
                    type, config, side: false, i, 0, UnitWidth, UnitHeight);

                Assert.IsFalse(soldier.Side, $"{type} 应为对手方");
                Assert.IsFalse(soldier.HasWeapon, $"{type} 对手不应装备基础武器");
                Assert.AreEqual(-1, soldier.WeaponId, $"{type} 对手 WeaponId 应为 -1");
                Assert.AreEqual(0, soldier.WeaponAttackPower, $"{type} 对手附加攻击力应为 0");
                Assert.AreEqual(20, soldier.AttackDamageForTest,
                    $"{type} 对手无武器攻击力应为基础 20");
            }
        }

        // ====================================================================
        // ResetState 清除武器状态（task 4.5 / 5.1）
        // ====================================================================

        [Test]
        [Description("ResetState 无条件清除 WeaponId/WeaponAttackPower，重复 reset 幂等。")]
        public void ResetState_ClearsWeaponState_Idempotent()
        {
            SoldierBase soldier = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, side: true, 0, 0, UnitWidth, UnitHeight);
            Assert.IsTrue(soldier.HasWeapon, "装备后应持有武器");

            soldier.ResetState();
            Assert.IsFalse(soldier.HasWeapon, "ResetState 后应无武器");
            Assert.AreEqual(-1, soldier.WeaponId, "ResetState 后 WeaponId=-1");
            Assert.AreEqual(0, soldier.WeaponAttackPower, "ResetState 后 WeaponAttackPower=0");

            soldier.ResetState();
            Assert.IsFalse(soldier.HasWeapon, "重复 ResetState 仍应无武器（幂等）");
        }

        [Test]
        [Description("带武器玩家移除回池后复用为对手：新租借无武器残留（spec armed player returns to pool）。")]
        public void Remove_ArmedPlayer_PoolReuseAsOpponent_NoWeaponLeak()
        {
            SoldierBase armed = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, side: true, 0, 0, UnitWidth, UnitHeight);
            Assert.IsTrue(armed.HasWeapon, "玩家刀兵应装备武器");

            Assert.IsTrue(_registry.Remove(armed.Id), "移除刀兵并归还池");
            Assert.AreEqual(-1, armed.Id, "Remove 后 ResetState 已执行，Id=-1");
            Assert.IsFalse(armed.HasWeapon, "Remove 后武器状态应被清除");

            // 复用同一池对象租借为对手。
            SoldierBase opponent = _registry.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, side: false, 1, 1, UnitWidth, UnitHeight);
            Assert.IsFalse(opponent.HasWeapon, "复用为对手不应残留武器");
            Assert.AreEqual(-1, opponent.WeaponId, "复用后 WeaponId=-1");
            Assert.AreEqual(0, opponent.WeaponAttackPower, "复用后 WeaponAttackPower=0");
            Assert.AreEqual(20, opponent.AttackDamageForTest, "复用后攻击力为基础值，无武器残留");
        }

        [Test]
        [Description("带武器玩家移除回池后复用为玩家：重新解析并写入新武器，无旧残留。")]
        public void Remove_ArmedPlayer_PoolReuseAsPlayer_ReappliesWeapon()
        {
            SoldierBase first = _registry.CreateAndPlace(
                SoldierType.Cavalry, CavalryConfig, side: true, 0, 0, UnitWidth, UnitHeight);
            Assert.AreEqual(31, first.WeaponId, "骑兵首次应解析到 id=31");

            Assert.IsTrue(_registry.Remove(first.Id), "移除骑兵并归还池");

            SoldierBase reused = _registry.CreateAndPlace(
                SoldierType.Cavalry, CavalryConfig, side: true, 1, 1, UnitWidth, UnitHeight);
            Assert.IsTrue(reused.HasWeapon, "复用玩家骑兵应重新装备武器");
            Assert.AreEqual(31, reused.WeaponId, "复用后仍应解析到 id=31");
            Assert.AreEqual(1, reused.WeaponAttackPower, "复用后附加攻击力应为 1");
            Assert.AreEqual(21, reused.AttackDamageForTest, "复用后攻击力=基础 20 + 武器 1");
        }

        // ====================================================================
        // 解析失败回滚（task 4.4）
        // ====================================================================

        /// <summary>
        /// 对指定玩家兵种无法解析默认武器的 resolver（供验证"玩家缺失默认必须失败而非 fallback"）。
        /// </summary>
        private sealed class MissingMappingResolver : BasicWeaponResolver
        {
            private readonly SoldierType _unresolvable;

            internal MissingMappingResolver(WeaponCatalogSnapshot catalog, SoldierType unresolvable)
                : base(catalog)
            {
                _unresolvable = unresolvable;
            }

            internal override bool TryResolve(SoldierType type, out WeaponDefinitionSnapshot definition)
            {
                if (type == _unresolvable)
                {
                    definition = null;
                    return false;
                }

                return base.TryResolve(type, out definition);
            }
        }

        [Test]
        [Description("PrepareBattleInstance 玩家映射缺失 → 抛 InvalidOperationException，士兵归还池、无残留。")]
        public void PrepareBattleInstance_ResolveFailure_RollsBack_NoWeaponResidue()
        {
            var failing = new UnitRegistry(
                _factory, CellSize, null,
                new MissingMappingResolver(GoldenCatalog(), SoldierType.Bow));
            var unit = new BattleUnit(1, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1);

            Assert.Throws<InvalidOperationException>(
                () => failing.PrepareBattleInstance(unit, BowConfig, UnitWidth, UnitHeight),
                "玩家弓兵缺少默认武器映射必须抛错，不 fallback");
            Assert.AreEqual(0, failing.Count, "回滚后不应有已注册单位");

            // 同池复用为对手弓兵：无武器残留（失败回滚的士兵已 ResetState）。
            SoldierBase opponent = failing.CreateAndPlace(
                SoldierType.Bow, BowConfig, side: false, 0, 0, UnitWidth, UnitHeight);
            Assert.IsFalse(opponent.HasWeapon, "回滚对象复用后不应残留武器");
            Assert.AreEqual(-1, opponent.WeaponId, "回滚对象复用后 WeaponId=-1");
            Assert.AreEqual(0, opponent.WeaponAttackPower, "回滚对象复用后 WeaponAttackPower=0");
        }

        [Test]
        [Description("ActivateBattleUnit 玩家映射缺失 → 抛 InvalidOperationException 并回滚，无半初始化单位。")]
        public void ActivateBattleUnit_ResolveFailure_RollsBack_NoWeaponResidue()
        {
            var failing = new UnitRegistry(
                _factory, CellSize, null,
                new MissingMappingResolver(GoldenCatalog(), SoldierType.Spear));
            var unit = new BattleUnit(1, true, UnitKind.Soldier, SoldierType.Spear, "枪", 1);

            Assert.Throws<InvalidOperationException>(
                () => failing.ActivateBattleUnit(
                    unit, SpearConfig, null, 0, 0, UnitWidth, UnitHeight),
                "玩家枪兵缺少默认武器映射必须抛错，不 fallback");
            Assert.AreEqual(0, failing.Count, "回滚后不应有已注册单位");

            // 同池复用为对手枪兵：无武器残留。
            SoldierBase opponent = failing.CreateAndPlace(
                SoldierType.Spear, SpearConfig, side: false, 1, 1, UnitWidth, UnitHeight);
            Assert.IsFalse(opponent.HasWeapon, "回滚对象复用后不应残留武器");
            Assert.AreEqual(20, opponent.AttackDamageForTest, "回滚对象复用后攻击力为基础值");
        }

        [Test]
        [Description("解析失败不影响其他兵种：同一 resolver 下其余玩家类别仍可正常装备。")]
        public void ResolveFailure_OtherCategories_StillEquip()
        {
            var failing = new UnitRegistry(
                _factory, CellSize, null,
                new MissingMappingResolver(GoldenCatalog(), SoldierType.Bow));

            SoldierBase knife = failing.CreateAndPlace(
                SoldierType.Knife, KnifeConfig, side: true, 0, 0, UnitWidth, UnitHeight);
            Assert.IsTrue(knife.HasWeapon, "刀兵映射不受弓兵缺失影响");
            Assert.AreEqual(20, knife.WeaponId, "刀兵应解析到 id=20");
        }
    }
}