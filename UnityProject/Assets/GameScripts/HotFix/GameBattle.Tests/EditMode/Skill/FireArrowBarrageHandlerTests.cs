using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Skill
{
    // ============================================================================
    // 黄忠 FireArrowBarrage（火箭烈）专用 Skill handler 单元测试
    // ----------------------------------------------------------------------------
    // 验证要求（Contract）：
    //   1. 固定序列随机源、多目标稳定顺序、每目标一次 Next(1,3)；Lv1/Lv4 factor1、Lv5 factor2。
    //   2. 每箭 explicit damage=当前 EffectiveAttackDamage*2；Effect 当下不扣血，
    //      推进 ProjectileManager 后按箭数扣血。
    //   3. 5.5 格边界、范围外、lane 筛选。
    //   4. stale generation/owner 离场 no-op；effect 后 owner 离场箭继续命中。
    //   5. 无 Buff、Complete/Cancel no-op、缺专用配置 no-op。
    //
    // 测试策略：
    //   使用真实 EnemyManager + AttackResolver + ProjectileFactory + ProjectileManager +
    //   AttackEffectManager、真实 EnemyBase 测试子类（重写 BuffGeneration）、
    //   真实 UnitFactory 创建的 SoldierBase 作为 owner、固定序列 IRandomSource。
    //   复用 BattleShoutHandlerTests 的夹具构建模式（地图/敌人/owner）。
    // ============================================================================

    [TestFixture]
    internal class FireArrowBarrageHandlerTests
    {
        private const float CellSize = 80f;
        private const float UnitWidth = 40f;
        private const float UnitHeight = 40f;
        private const float EnemyWidth = 40f;
        private const float EnemyHeight = 40f;
        private const int GridSize = 80;
        private const int OpponentAttackMultiplier = 1;
        private const float RangeTiles = 5.5f;
        private const float EffectDamageMultiplier = 2.0f;

        // ====================================================================
        // 固定序列随机源 —— 按预设序列依次返回 Next(1,3) 值，确定性可复现
        // ====================================================================

        private sealed class SequenceRandomSource : IRandomSource
        {
            private readonly int[] _values;
            private int _index;

            internal SequenceRandomSource(params int[] values)
            {
                _values = values;
                _index = 0;
            }

            internal int CallCount { get; private set; }
            internal int ConsumedIndex => _index;

            public int Next(int min, int max)
            {
                if (max <= min)
                {
                    throw new ArgumentOutOfRangeException(nameof(max));
                }

                int v = _index < _values.Length ? _values[_index] : _values[_values.Length - 1];
                _index++;
                CallCount++;
                return v;
            }

            public float NextUnit() => 0f;
            public int Next(int max) => 0;
            public void Shuffle<T>(IList<T> list) { }
        }

        // ====================================================================
        // BuffGenerationEnemy —— EnemyBase 测试子类，重写 BuffGeneration 为正值
        // --------------------------------------------------------------------
        // 与 BattleShoutHandlerTests 一致：EnemyBase 基类 BuffGeneration=0，
        // Register 时 BuffManager.RegisterTarget 需要 Generation>0 才有效。
        // ====================================================================

        private class BuffGenerationEnemy : EnemyBase
        {
            private long _generation;

            internal void ConfigureForTest(
                MapData map,
                float cellSize,
                int runtimeId,
                bool isPlayerLane = true,
                int maxHealth = 100)
            {
                Configure(
                    map,
                    cellSize,
                    new NopEndPointTarget(),
                    onEnemyKilled: (_, __, ___, ____) => { },
                    onDeathRequested: (_, _) => { });

                AssignRuntimeId(runtimeId);
                _generation += 1;
                Init(isPlayerLane, maxHealth, EnemyWidth, EnemyHeight);
                BeginMoving();
            }

            internal void SetTestPosition(float x, float y)
            {
                XField = x;
                YField = y;
            }

            protected override long BuffGeneration => _generation;

            internal long TestGeneration => _generation;
        }

        private sealed class NopEndPointTarget : IEnemyEndPointAttackTarget
        {
            public bool ReceiveEndPointAttack(EndPointAttackRequest request) => true;
        }

        // ====================================================================
        // 地图构造（与 BattleShoutHandlerTests 一致）
        // ====================================================================

        private static MapData BuildLinearPathMapData()
        {
            var grid = new List<IReadOnlyList<string>>
            {
                new List<string> { "0_1" },
                new List<string> { "0_1" },
                new List<string> { "0_1" },
                new List<string> { "0_1" },
                new List<string> { "0_1" },
                new List<string> { "0_1" },
                new List<string> { "0_1" },
                new List<string> { "0_1" },
            };

            var playerPath = new List<GridPosition>
            {
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(2, 0),
                new GridPosition(3, 0),
                new GridPosition(4, 0),
                new GridPosition(5, 0),
                new GridPosition(6, 0),
                new GridPosition(7, 0),
            };

            var opponentPath = new List<GridPosition>
            {
                new GridPosition(7, 0),
                new GridPosition(6, 0),
                new GridPosition(5, 0),
                new GridPosition(4, 0),
                new GridPosition(3, 0),
                new GridPosition(2, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 0),
            };

            return MapData.FromColumnMajorGrid(
                grid, DecodeCell, mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(7, 0),
                opponentStart: new GridPosition(7, 0),
                opponentEnd: new GridPosition(0, 0),
                playerPath: playerPath,
                opponentPath: opponentPath);
        }

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

        // ====================================================================
        // 测试夹具构建
        // ====================================================================

        private RuntimeIdAllocator _idAllocator;
        private BattlePoolScope _poolScope;
        private BattleObjectPool<KnifeSoldier> _knifePool;
        private EnemyManager _enemyManager;
        private AttackResolver _attackResolver;
        private AttackEffectManager _attackEffectManager;
        private ProjectileFactory _projectileFactory;
        private ProjectileManager _projectileManager;
        private BattleObjectPool<SimpleDynamicArrow> _arrowPool;
        private UnitFactory _factory;
        private UnitRegistry _unitRegistry;
        private BuffManager _buffManager;
        private BattleActionScheduler _scheduler;
        private MapData _map;

        private static SkillDefinitionSnapshot FireArrowBarrageDef => new SkillDefinitionSnapshot(
            "FireArrowBarrage", SkillCategory.Active, 6000, "FireArrowBarrage",
            null, null, RangeTiles, null, EffectDamageMultiplier);

        [SetUp]
        public void SetUp()
        {
            _idAllocator = new RuntimeIdAllocator();
            _poolScope = new BattlePoolScope();
            _knifePool = _poolScope.GetPool<KnifeSoldier>(() => new KnifeSoldier());

            _scheduler = new BattleActionScheduler();
            _scheduler.BeginFrame(0);

            _buffManager = new BuffManager(
                new BuffCatalogSnapshot(Array.Empty<BuffDefinitionSnapshot>()),
                _scheduler);

            _enemyManager = new EnemyManager(GridSize, null, _buffManager);
            _attackResolver = new AttackResolver();
            _attackEffectManager = new AttackEffectManager();

            _arrowPool = _poolScope.GetPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            _projectileFactory = new ProjectileFactory(
                _idAllocator, _arrowPool, _enemyManager, CellSize);
            _projectileManager = new ProjectileManager(_projectileFactory);

            _factory = new UnitFactory(
                _idAllocator,
                _knifePool, _poolScope.GetPool<BowSoldier>(() => new BowSoldier()),
                _poolScope.GetPool<SpearSoldier>(() => new SpearSoldier()),
                _poolScope.GetPool<CavalrySoldier>(() => new CavalrySoldier()),
                _enemyManager, _attackResolver, _attackEffectManager,
                _projectileFactory, _projectileManager,
                CellSize, OpponentAttackMultiplier);

            _unitRegistry = new UnitRegistry(_factory, CellSize, _buffManager);
            _map = BuildLinearPathMapData();
        }

        /// <summary>创建一个已放置并激活的玩家方刀兵 owner（指定等级）。</summary>
        private SoldierBase CreateOwner(int gridX, int gridY, int level)
        {
            var config = new UnitConfigSnapshot(0, "刀", "knife", 1.5f, 3, 0.8f, "单体", "nearest");
            var battleUnit = new BattleUnit(100, true, UnitKind.Soldier, SoldierType.Knife, "刀", level);
            SoldierBase soldier = _unitRegistry.ActivateBattleUnit(
                battleUnit, config, null, gridX, gridY, UnitWidth, UnitHeight);
            return soldier;
        }

        /// <summary>创建并注册一个敌人到 EnemyManager。</summary>
        private BuffGenerationEnemy CreateAndRegisterEnemy(
            int id,
            bool isPlayerLane,
            float x,
            float y,
            int maxHealth = 100)
        {
            var enemy = new BuffGenerationEnemy();
            enemy.ConfigureForTest(_map, CellSize, runtimeId: id, isPlayerLane: isPlayerLane, maxHealth: maxHealth);
            enemy.SetTestPosition(x, y);
            _enemyManager.Register(enemy);
            return enemy;
        }

        /// <summary>构造 FireArrowBarrageHandler。</summary>
        private FireArrowBarrageHandler CreateHandler(IRandomSource randomSource)
        {
            return new FireArrowBarrageHandler(
                FireArrowBarrageDef,
                _unitRegistry,
                _enemyManager,
                _attackResolver,
                _projectileFactory,
                _projectileManager,
                _attackEffectManager,
                randomSource,
                CellSize);
        }

        /// <summary>推进 ProjectileManager 直到全部箭矢命中/移除（或达上限防死循环）。</summary>
        private void AdvanceProjectilesToHit(int maxSteps = 200)
        {
            long frame = 1000;
            for (int i = 0; i < maxSteps; i++)
            {
                if (_projectileManager.ActiveCount == 0 && i > 0)
                {
                    break;
                }

                _projectileManager.Update(frame, 80);
                frame += 80;
            }
        }

        // ====================================================================
        // 1. 固定序列随机源、多目标稳定顺序、每目标一次 Next(1,3)
        // ====================================================================

        [Test]
        [Description("多目标稳定顺序：每目标恰好一次 Next(1,3)，随机消费顺序由目标顺序决定。"
                     + "Lv1 factor=1，roll=2 → 每目标 2 箭。")]
        public void Effect_Lv1_StableOrder_PerTargetOneRoll_Factor1()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0, level: 1);
            // owner 中心 = (2*80+20, 0+20) = (180, 20)。RangeTiles=5.5 → range=440px。

            BuffGenerationEnemy enemyA = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);
            BuffGenerationEnemy enemyB = CreateAndRegisterEnemy(2, isPlayerLane: true, x: 300f, y: 0f);

            // 固定序列：第一个目标 roll=2，第二个目标 roll=1。
            var random = new SequenceRandomSource(2, 1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBeforeA = enemyA.Health;
            int healthBeforeB = enemyB.Health;

            handler.Effect(context);

            // Effect 当下不扣血。
            Assert.AreEqual(healthBeforeA, enemyA.Health, "Effect 当下 enemyA 不扣血。");
            Assert.AreEqual(healthBeforeB, enemyB.Health, "Effect 当下 enemyB 不扣血。");

            // 每目标恰好一次随机调用。
            Assert.AreEqual(2, random.CallCount, "每目标恰好一次 Next(1,3)。");

            // 推进投射物至命中。
            AdvanceProjectilesToHit();

            // Lv1 factor=1：enemyA roll=2 → 2 箭，enemyB roll=1 → 1 箭。
            int expectedDamagePerArrow = owner.EffectiveAttackDamage * 2;
            Assert.AreEqual(healthBeforeA - 2 * expectedDamagePerArrow, enemyA.Health,
                "enemyA 承受 2 箭伤害。");
            Assert.AreEqual(healthBeforeB - 1 * expectedDamagePerArrow, enemyB.Health,
                "enemyB 承受 1 箭伤害。");
        }

        [Test]
        [Description("Lv4 factor=1：(4-1)/2=1，每目标箭数=roll*1。")]
        public void Effect_Lv4_Factor1()
        {
            SoldierBase owner = CreateOwner(gridX: 0, gridY: 0, level: 4);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 80f, y: 0f);

            var random = new SequenceRandomSource(1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            handler.Effect(context);
            AdvanceProjectilesToHit();

            int perArrow = owner.EffectiveAttackDamage * 2;
            Assert.AreEqual(healthBefore - 1 * perArrow, enemy.Health,
                "Lv4 factor=1 roll=1 → 1 箭。");
        }

        [Test]
        [Description("Lv5 factor=2：(5-1)/2=2，每目标箭数=roll*2。")]
        public void Effect_Lv5_Factor2()
        {
            SoldierBase owner = CreateOwner(gridX: 0, gridY: 0, level: 5);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 80f, y: 0f);

            var random = new SequenceRandomSource(1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            handler.Effect(context);
            AdvanceProjectilesToHit();

            int perArrow = owner.EffectiveAttackDamage * 2;
            Assert.AreEqual(healthBefore - 2 * perArrow, enemy.Health,
                "Lv5 factor=2 roll=1 → 2 箭。");
        }

        // ====================================================================
        // 2. 每箭 explicit damage=当前 EffectiveAttackDamage*2；Effect 当下不扣血
        // ====================================================================

        [Test]
        [Description("每箭 explicit damage=EffectiveAttackDamage*2；Effect 当下不扣血，"
                     + "推进 ProjectileManager 后按箭数扣血。")]
        public void Effect_ExplicitDamage_NoImmediateDamage_AfterAdvanceDamages()
        {
            SoldierBase owner = CreateOwner(gridX: 0, gridY: 0, level: 1);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 80f, y: 0f);

            var random = new SequenceRandomSource(2); // roll=2 → 2 箭
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            int expectedPerArrow = owner.EffectiveAttackDamage * 2;

            handler.Effect(context);

            // Effect 当下不扣血。
            Assert.AreEqual(healthBefore, enemy.Health, "Effect 当下不扣血。");

            AdvanceProjectilesToHit();

            Assert.AreEqual(healthBefore - 2 * expectedPerArrow, enemy.Health,
                "推进后承受 2 箭 × EffectiveAttackDamage*2 伤害。");
        }

        // ====================================================================
        // 3. 5.5 格边界、范围外、lane 筛选
        // ====================================================================

        [Test]
        [Description("5.5 格边界：范围内敌方受影响，范围外不受影响。")]
        public void Effect_RangeBoundary_5_5Tiles_InRangeAffected_OutOfRangeNot()
        {
            SoldierBase owner = CreateOwner(gridX: 0, gridY: 0, level: 1);
            // owner 中心 = (20, 20)。RangeTiles=5.5 → range=440px。
            // 范围内：敌人中心 (80+20, 20) = (100, 20)，距 owner 中心 80px。
            BuffGenerationEnemy inRange = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 80f, y: 0f);
            // 范围外：敌人中心 (6*80+20, 20) = (500, 20)，距 owner 中心 480px > 440px。
            BuffGenerationEnemy outOfRange = CreateAndRegisterEnemy(2, isPlayerLane: true, x: 480f, y: 0f);

            var random = new SequenceRandomSource(1, 1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBeforeInRange = inRange.Health;
            int healthBeforeOutOfRange = outOfRange.Health;

            handler.Effect(context);
            AdvanceProjectilesToHit();

            Assert.AreEqual(healthBeforeInRange - owner.EffectiveAttackDamage * 2, inRange.Health,
                "范围内敌人承受 1 箭伤害。");
            Assert.AreEqual(healthBeforeOutOfRange, outOfRange.Health,
                "范围外敌人不受影响。");
        }

        [Test]
        [Description("lane 筛选：owner 为玩家方时查询玩家方敌人，对手方敌人不受影响。")]
        public void Effect_LaneFilter_OppositeLaneNotAffected()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0, level: 1);
            // owner.Side=true → 查询 IsPlayerLane=true 的敌人。

            BuffGenerationEnemy sameLane = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);
            BuffGenerationEnemy oppositeLane = CreateAndRegisterEnemy(2, isPlayerLane: false, x: 200f, y: 0f);

            var random = new SequenceRandomSource(1, 1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBeforeSame = sameLane.Health;
            int healthBeforeOpposite = oppositeLane.Health;

            handler.Effect(context);
            AdvanceProjectilesToHit();

            Assert.AreEqual(healthBeforeSame - owner.EffectiveAttackDamage * 2, sameLane.Health,
                "同侧（玩家方）敌人承受箭矢伤害。");
            Assert.AreEqual(healthBeforeOpposite, oppositeLane.Health,
                "对手方敌人不受影响。");
        }

        // ====================================================================
        // 4. stale generation/owner 离场 no-op；effect 后 owner 离场箭继续命中
        // ====================================================================

        [Test]
        [Description("stale generation：owner.Generation 与当前 LifecycleGeneration 不一致时 no-op。")]
        public void Effect_StaleGeneration_NoOp()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0, level: 1);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            var random = new SequenceRandomSource(1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration + 999),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            handler.Effect(context);
            AdvanceProjectilesToHit();

            Assert.AreEqual(healthBefore, enemy.Health, "stale generation 不发射箭矢。");
            Assert.AreEqual(0, random.CallCount, "stale generation 不消费随机。");
        }

        [Test]
        [Description("owner 离场（不在 UnitRegistry 中）时 effect no-op。")]
        public void Effect_OwnerNotInRegistry_NoOp()
        {
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            var random = new SequenceRandomSource(1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(99999, 1),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            handler.Effect(context);
            AdvanceProjectilesToHit();

            Assert.AreEqual(healthBefore, enemy.Health, "owner 不在注册表中时不发射箭矢。");
            Assert.AreEqual(0, random.CallCount, "owner 不存在时不消费随机。");
        }

        [Test]
        [Description("effect 后 owner 离场：已发射箭矢继续命中（不因 owner 离场取消）。")]
        public void Effect_OwnerLeavesAfterEffect_ArrowsContinueToHit()
        {
            SoldierBase owner = CreateOwner(gridX: 0, gridY: 0, level: 1);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 80f, y: 0f);

            var random = new SequenceRandomSource(2); // roll=2 → 2 箭
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            int expectedPerArrow = owner.EffectiveAttackDamage * 2;

            handler.Effect(context);

            // owner 离场：从 UnitRegistry 移除。
            _unitRegistry.Remove(owner.Id);

            // 已发射箭矢继续命中。
            AdvanceProjectilesToHit();

            Assert.AreEqual(healthBefore - 2 * expectedPerArrow, enemy.Health,
                "owner 离场后已发射箭矢继续命中。");
        }

        // ====================================================================
        // 5. 无 Buff、Complete/Cancel no-op、缺专用配置 no-op
        // ====================================================================

        [Test]
        [Description("无 Buff：Effect 后敌人无 Buff 施加。")]
        public void Effect_NoBuffApplied()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0, level: 1);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            var random = new SequenceRandomSource(1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int buffBefore = _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count;

            handler.Effect(context);

            int buffAfter = _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count;

            Assert.AreEqual(buffBefore, buffAfter, "Effect 不施加 Buff。");
        }

        [Test]
        [Description("Complete no-op：不产生任何效果或副作用。")]
        public void Complete_NoOp()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0, level: 1);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            var random = new SequenceRandomSource(1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            handler.Complete(context);

            AdvanceProjectilesToHit();
            Assert.AreEqual(healthBefore, enemy.Health, "Complete 不发射箭矢。");
        }

        [Test]
        [Description("Cancel(effectCommitted=false) no-op：不发射箭矢。")]
        public void Cancel_BeforeEffect_NoOp()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0, level: 1);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            var random = new SequenceRandomSource(1);
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            handler.Cancel(context, effectCommitted: false);

            AdvanceProjectilesToHit();
            Assert.AreEqual(healthBefore, enemy.Health, "Cancel 不发射箭矢。");
        }

        [Test]
        [Description("Cancel(effectCommitted=true) no-op：不取消已发射箭矢（由 ProjectileManager 负责）。")]
        public void Cancel_AfterEffect_ArrowsPersist()
        {
            SoldierBase owner = CreateOwner(gridX: 0, gridY: 0, level: 1);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 80f, y: 0f);

            var random = new SequenceRandomSource(2); // roll=2 → 2 箭
            FireArrowBarrageHandler handler = CreateHandler(random);
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            int expectedPerArrow = owner.EffectiveAttackDamage * 2;

            handler.Effect(context);

            // Cancel(effectCommitted=true) 不取消已发射箭矢。
            handler.Cancel(context, effectCommitted: true);

            AdvanceProjectilesToHit();
            Assert.AreEqual(healthBefore - 2 * expectedPerArrow, enemy.Health,
                "Cancel(effectCommitted=true) 后已发射箭矢仍命中。");
        }

        [Test]
        [Description("缺专用配置（RangeTiles/EffectDamageMultiplier 为 null）时 no-op。")]
        public void Effect_MissingConfig_NoOp()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0, level: 1);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            var def = new SkillDefinitionSnapshot(
                "FireArrowBarrage", SkillCategory.Active, 6000, "FireArrowBarrage",
                null, null, null, null, null);
            var random = new SequenceRandomSource(1);
            var handler = new FireArrowBarrageHandler(
                def, _unitRegistry, _enemyManager, _attackResolver,
                _projectileFactory, _projectileManager, _attackEffectManager,
                random, CellSize);

            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "FireArrowBarrage",
                0);

            int healthBefore = enemy.Health;
            handler.Effect(context);
            AdvanceProjectilesToHit();

            Assert.AreEqual(healthBefore, enemy.Health, "缺配置时不发射箭矢。");
            Assert.AreEqual(0, random.CallCount, "缺配置时不消费随机。");
        }
    }
}
