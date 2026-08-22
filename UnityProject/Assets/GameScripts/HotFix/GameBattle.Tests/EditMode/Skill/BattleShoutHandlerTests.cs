using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Skill
{
    // ============================================================================
    // 张飞 BattleShout（大喝）专用 Skill handler 单元测试
    // ----------------------------------------------------------------------------
    // 验证要求（Contract）：
    //   1. generation-safe/current owner：effect 时重新解析 owner，stale generation/离场 no-op。
    //   2. 敌我阵营与 3.5 格边界：范围内敌方目标受影响，范围外/同侧不受影响。
    //   3. 范围内多个目标各一次 Buff8：每目标恰好一次 Apply。
    //   4. stale generation/owner 离场 effect no-op。
    //   5. 提交后 owner 离场不提前清 Buff。
    //   6. 断言无直接伤害。
    //
    // 测试策略：
    //   使用真实 BuffManager（Buff8=State, MovementDisabled+AttackDisabled）、
    //   真实 EnemyManager + AttackResolver、真实 EnemyBase 测试子类（重写 BuffGeneration）、
    //   真实 UnitFactory 创建的 SoldierBase 作为 owner。
    // ============================================================================

    [TestFixture]
    internal class BattleShoutHandlerTests
    {
        private const float CellSize = 80f;
        private const float UnitWidth = 40f;
        private const float UnitHeight = 40f;
        private const float EnemyWidth = 40f;
        private const float EnemyHeight = 40f;
        private const int GridSize = 80;
        private const int OpponentAttackMultiplier = 1;
        private const float RangeTiles = 3.5f;
        private const int EffectBuffType = 8;
        private const int EffectDurationMs = 2000;

        // ====================================================================
        // Buff8 定义：State, MovementDisabled(0) + AttackDisabled(1), Refresh, 1 stack
        // ====================================================================

        private static BuffDefinitionSnapshot Buff8Def => new BuffDefinitionSnapshot(
            8, "stun", "晕眩", BuffKind.State,
            new[] { (int)BuffStateChannel.MovementDisabled, (int)BuffStateChannel.AttackDisabled },
            BuffStackPolicy.Refresh, 1, string.Empty);

        private static SkillDefinitionSnapshot BattleShoutDef => new SkillDefinitionSnapshot(
            "BattleShout", SkillCategory.Active, 6000, "BattleShout",
            EffectBuffType, EffectDurationMs, RangeTiles, 15, null);

        // ====================================================================
        // BuffGenerationEnemy —— EnemyBase 测试子类，重写 BuffGeneration 为正值
        // --------------------------------------------------------------------
        // EnemyBase 基类 BuffGeneration=0，BuffTargetHandle.IsValid 要求 Generation>0。
        // 测试子类每次 Init 递增 generation，使 Buff 目标句柄有效。
        // ====================================================================

        private class BuffGenerationEnemy : EnemyBase
        {
            private long _generation;

            internal void ConfigureForTest(
                MapData map,
                float cellSize,
                bool isPlayerLane = true,
                int maxHealth = 100)
            {
                Configure(
                    map,
                    cellSize,
                    new NopEndPointTarget(),
                    onEnemyKilled: (_, __, ___, ____) => { },
                    onDeathRequested: (_, _) => { });

                AssignRuntimeId(1);
                _generation += 1;
                Init(isPlayerLane, maxHealth, EnemyWidth, EnemyHeight);
                BeginMoving();
            }

            /// <summary>覆盖逻辑位置以控制与 owner 的距离（测试专用）。</summary>
            internal void SetTestPosition(float x, float y)
            {
                XField = x;
                YField = y;
            }

            protected override long BuffGeneration => _generation;

            internal long TestGeneration => _generation;
            internal bool TestMovementStopped => MovementStoppedForTest;
            internal bool TestBuffMovementDisabled => BuffMovementDisabledForTest;
            internal bool TestBuffAttackDisabled => BuffAttackDisabledForTest;
        }

        private sealed class NopEndPointTarget : IEnemyEndPointAttackTarget
        {
            public bool ReceiveEndPointAttack(EndPointAttackRequest request) => true;
        }

        // ====================================================================
        // 地图构造
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

        [SetUp]
        public void SetUp()
        {
            _idAllocator = new RuntimeIdAllocator();
            _poolScope = new BattlePoolScope();
            _knifePool = _poolScope.GetPool<KnifeSoldier>(() => new KnifeSoldier());

            _scheduler = new BattleActionScheduler();
            _scheduler.BeginFrame(0);

            _buffManager = new BuffManager(
                new BuffCatalogSnapshot(new[] { Buff8Def }),
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

        /// <summary>创建一个已放置并激活的玩家方刀兵 owner。</summary>
        private SoldierBase CreateOwner(int gridX = 2, int gridY = 0)
        {
            var config = new UnitConfigSnapshot(0, "刀", "knife", 1.5f, 3, 0.8f, "单体", "nearest");
            var battleUnit = new BattleUnit(100, true, UnitKind.Soldier, SoldierType.Knife, "刀", 1);
            SoldierBase soldier = _unitRegistry.ActivateBattleUnit(
                battleUnit, config, null, gridX, gridY, UnitWidth, UnitHeight);
            return soldier;
        }

        /// <summary>创建并注册一个敌人到 EnemyManager。</summary>
        private BuffGenerationEnemy CreateAndRegisterEnemy(
            int id,
            bool isPlayerLane,
            float x,
            float y)
        {
            var enemy = new BuffGenerationEnemy();
            enemy.ConfigureForTest(_map, CellSize, isPlayerLane: isPlayerLane, maxHealth: 100);
            // 覆盖位置以控制与 owner 的距离。
            enemy.SetTestPosition(x, y);
            _enemyManager.Register(enemy);
            return enemy;
        }

        /// <summary>构造 BattleShoutHandler。</summary>
        private BattleShoutHandler CreateHandler()
        {
            return new BattleShoutHandler(
                BattleShoutDef,
                _unitRegistry,
                _enemyManager,
                _attackResolver,
                _buffManager,
                CellSize);
        }

        /// <summary>推进调度器到指定帧时间戳并 flush 到期动作。</summary>
        private void Step(long frameNowMs)
        {
            _scheduler.BeginFrame(frameNowMs);
            _scheduler.FlushDueActions(1);
        }

        // ====================================================================
        // 测试
        // ====================================================================

        [Test]
        [Description("generation-safe/current owner：有效 owner 范围内敌方目标各受一次 Buff8。")]
        public void Effect_ValidOwner_AppliesBuff8ToEachInRangeEnemy()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            // owner 中心 = (2*80 + 20, 0 + 20) = (180, 20)。
            // RangeTiles=3.5 → range=280px。范围覆盖 x∈[-100, 460]。

            // 两个玩家方敌人（owner.Side=true → 查询 IsPlayerLane=true 的敌人）。
            BuffGenerationEnemy enemyA = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);
            BuffGenerationEnemy enemyB = CreateAndRegisterEnemy(2, isPlayerLane: true, x: 300f, y: 0f);

            int beforeA = _buffManager.GetTargetSnapshots(((IBuffTarget)enemyA).Handle).Count;
            int beforeB = _buffManager.GetTargetSnapshots(((IBuffTarget)enemyB).Handle).Count;

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Effect(context);

            int afterA = _buffManager.GetTargetSnapshots(((IBuffTarget)enemyA).Handle).Count;
            int afterB = _buffManager.GetTargetSnapshots(((IBuffTarget)enemyB).Handle).Count;

            Assert.AreEqual(beforeA + 1, afterA, "enemyA 获得一次 Buff8。");
            Assert.AreEqual(beforeB + 1, afterB, "enemyB 获得一次 Buff8。");
            Assert.IsTrue(enemyA.TestBuffMovementDisabled, "enemyA 移动禁用生效。");
            Assert.IsTrue(enemyA.TestBuffAttackDisabled, "enemyA 攻击禁用生效。");
            Assert.IsTrue(enemyB.TestBuffMovementDisabled, "enemyB 移动禁用生效。");
            Assert.IsTrue(enemyB.TestBuffAttackDisabled, "enemyB 攻击禁用生效。");
        }

        [Test]
        [Description("3.5 格边界：范围内敌方受影响，范围外不受影响。")]
        public void Effect_RangeBoundary_InRangeAffected_OutOfRangeNot()
        {
            SoldierBase owner = CreateOwner(gridX: 0, gridY: 0);
            // owner 中心 = (0*80 + 20, 0 + 20) = (20, 20)。
            // RangeTiles=3.5 → range=280px。

            // 在范围内（距 owner 中心约 80px）：敌人中心 (80+20, 20) = (100, 20)。
            BuffGenerationEnemy inRange = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 80f, y: 0f);
            // 在范围外（距 owner 中心约 480px）：敌人中心 (480+20, 20) = (500, 20)。
            BuffGenerationEnemy outOfRange = CreateAndRegisterEnemy(2, isPlayerLane: true, x: 480f, y: 0f);

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Effect(context);

            Assert.AreEqual(1, _buffManager.GetTargetSnapshots(((IBuffTarget)inRange).Handle).Count,
                "范围内敌人获得一次 Buff8。");
            Assert.AreEqual(0, _buffManager.GetTargetSnapshots(((IBuffTarget)outOfRange).Handle).Count,
                "范围外敌人未获得 Buff8。");
        }

        [Test]
        [Description("同侧不受影响：owner 为玩家方时查询玩家方敌人，对手方敌人不受影响。")]
        public void Effect_OppositeSideEnemy_NotAffected()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            // owner.Side=true → 查询 IsPlayerLane=true 的敌人。
            // 对手方敌人（IsPlayerLane=false）不应被命中。

            BuffGenerationEnemy sameLane = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);
            BuffGenerationEnemy oppositeLane = CreateAndRegisterEnemy(2, isPlayerLane: false, x: 200f, y: 0f);

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Effect(context);

            Assert.AreEqual(1, _buffManager.GetTargetSnapshots(((IBuffTarget)sameLane).Handle).Count,
                "同侧（玩家方）敌人获得 Buff8。");
            Assert.AreEqual(0, _buffManager.GetTargetSnapshots(((IBuffTarget)oppositeLane).Handle).Count,
                "对手方敌人未获得 Buff8（IsTargetableBy 过滤阵营不匹配）。");
        }

        [Test]
        [Description("stale generation：owner.Generation 与当前 LifecycleGeneration 不一致时 no-op。")]
        public void Effect_StaleGeneration_NoOp()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            BattleShoutHandler handler = CreateHandler();
            // 使用错误的 generation（owner.LifecycleGeneration + 999）。
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration + 999),
                "BattleShout",
                0);

            handler.Effect(context);

            Assert.AreEqual(0, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "stale generation 不施加 Buff8。");
            Assert.IsFalse(enemy.TestBuffMovementDisabled, "stale generation 不影响敌人状态。");
        }

        [Test]
        [Description("owner 离场（不在 UnitRegistry 中）时 effect no-op。")]
        public void Effect_OwnerNotInRegistry_NoOp()
        {
            // 创建一个 owner 但不放入 UnitRegistry（使用不存在的 runtimeId）。
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(99999, 1),
                "BattleShout",
                0);

            handler.Effect(context);

            Assert.AreEqual(0, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "owner 不在注册表中时不施加 Buff8。");
        }

        [Test]
        [Description("提交后 owner 离场不提前清 Buff：Buff 由 BuffManager 生命周期负责。")]
        public void Effect_OwnerLeavesAfterCommit_BuffPersists()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Effect(context);
            Assert.AreEqual(1, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "effect 后敌人获得 Buff8。");

            // owner 离场：从 UnitRegistry 移除。
            _unitRegistry.Remove(owner.Id);

            // Buff 仍存在（不因 owner 离场清除）。
            Assert.AreEqual(1, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "owner 离场后 Buff8 仍存在。");
            Assert.IsTrue(enemy.TestBuffMovementDisabled, "owner 离场后移动禁用仍生效。");

            // Cancel 不清已提交 Buff。
            handler.Cancel(context, effectCommitted: true);
            Assert.AreEqual(1, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "Cancel(effectCommitted=true) 不清已提交 Buff8。");
        }

        [Test]
        [Description("Cancel(effectCommitted=false) 不施加任何效果（Effect 未执行）。")]
        public void Cancel_BeforeEffect_NoBuff()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Cancel(context, effectCommitted: false);
            Assert.AreEqual(0, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "Cancel(effectCommitted=false) 后无 Buff8。");
        }

        [Test]
        [Description("Complete no-op：不产生任何效果或副作用。")]
        public void Complete_NoOp()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Complete(context);
            Assert.AreEqual(0, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "Complete 不施加 Buff8。");
        }

        [Test]
        [Description("无直接伤害：effect 后敌人血量不变。")]
        public void Effect_NoDirectDamage()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);
            int healthBefore = enemy.Health;

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Effect(context);

            Assert.AreEqual(healthBefore, enemy.Health, "effect 不造成直接伤害。");
        }

        [Test]
        [Description("Buff8 到期后状态清除：2000ms 后移动/攻击禁用解除。")]
        public void Effect_Buff8Expires_After2000ms()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            BattleShoutHandler handler = CreateHandler();
            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Effect(context);
            Assert.IsTrue(enemy.TestBuffMovementDisabled, "effect 后移动禁用生效。");
            Assert.IsTrue(enemy.TestBuffAttackDisabled, "effect 后攻击禁用生效。");

            // 1999ms 仍在。
            Step(1999);
            Assert.AreEqual(1, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "1999ms 时 Buff8 仍存在。");
            Assert.IsTrue(enemy.TestBuffMovementDisabled, "1999ms 时移动禁用仍生效。");

            // 2000ms 到期清除。
            Step(2000);
            Assert.AreEqual(0, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "2000ms 时 Buff8 已到期清除。");
            Assert.IsFalse(enemy.TestBuffMovementDisabled, "2000ms 后移动禁用解除。");
            Assert.IsFalse(enemy.TestBuffAttackDisabled, "2000ms 后攻击禁用解除。");
        }

        [Test]
        [Description("缺专用配置（EffectBuffType/EffectDurationMs/RangeTiles 为 null）时 no-op。")]
        public void Effect_MissingConfig_NoOp()
        {
            SoldierBase owner = CreateOwner(gridX: 2, gridY: 0);
            BuffGenerationEnemy enemy = CreateAndRegisterEnemy(1, isPlayerLane: true, x: 200f, y: 0f);

            var def = new SkillDefinitionSnapshot(
                "BattleShout", SkillCategory.Active, 6000, "BattleShout",
                null, null, null, 15, null);
            var handler = new BattleShoutHandler(
                def, _unitRegistry, _enemyManager, _attackResolver, _buffManager, CellSize);

            var context = new SkillActivationContext(
                new SkillOwnerHandle(owner.Id, owner.LifecycleGeneration),
                "BattleShout",
                0);

            handler.Effect(context);

            Assert.AreEqual(0, _buffManager.GetTargetSnapshots(((IBuffTarget)enemy).Handle).Count,
                "缺配置时不施加 Buff8。");
        }
    }
}
