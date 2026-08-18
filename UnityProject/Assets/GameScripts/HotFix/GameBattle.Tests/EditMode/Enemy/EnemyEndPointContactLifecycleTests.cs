using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Enemy
{
    // ============================================================================
    // 敌军终点攻击一次性化 + 统一回收 生命周期测试
    // ----------------------------------------------------------------------------
    // 验证目标：
    //   1. 抵达终点：ReceiveEndPointAttack 恰好调用 1 次（多帧不重复）、伤害 1、
    //      攻击者 ID 正确、车道正确（玩家车道敌→玩家阿斗桩，对手车道敌→对手阿斗桩）。
    //   2. 目标拒绝伤害（桩返回 false）仍视为已尝试：攻击只 1 次且仍请求移除。
    //   3. 终点/击杀回收用真实 Factory + 对象池 + EnemyManager 装配，验证完整链路：
    //      终点攻击一次 → 以 ReachedEndPoint 原因移除 → EnemyRemoved(..., false) 一次 →
    //      归还池一次 → 继续推进不再扣血、不再回收。
    //   4. 重复请求移除（同 id 两次）仍只回收一次。
    //   5. 被击杀（Hit 归零）：reason=Killed，playDeathEffect=true，也恰好归还池一次。
    //   6. Settling GameOver()：全部敌军恰好归还一次，管理器 Count/空间索引/池 ActiveCount
    //      归零一致。
    // ============================================================================

    /// <summary>
    /// 敌军终点攻击与统一回收生命周期测试。
    /// </summary>
    /// <remarks>
    /// 使用真实 EnemyBase 子类 + 真实 EnemyManager + EnemyFactory + BattleObjectPool +
    /// Mob0Enemy，验证终点攻击一次性、按车道绑定、带原因移除、池回收完整性。
    /// 装配顺序与 BattleRuntimeFactory 正式运行时一致：Acquire → Configure → InitializeStats
    /// → Init → BeginMoving → Register。不依赖 Unity 场景。
    /// </remarks>
    [TestFixture]
    internal sealed class EnemyEndPointContactLifecycleTests
    {
        private const float CellSize = 80f;
        private const int GridSize = 80;

        // ====================================================================
        // 测试桩
        // ====================================================================

        /// <summary>记录终点攻击调用的 IEnemyEndPointAttackTarget 桩。</summary>
        private sealed class RecordingTarget : IEnemyEndPointAttackTarget
        {
            private readonly bool _acceptsDamage;

            internal RecordingTarget(bool acceptsDamage = true)
            {
                _acceptsDamage = acceptsDamage;
            }

            /// <summary>终点攻击累计次数。</summary>
            public int AttackCount;

            /// <summary>上次攻击的请求载荷。</summary>
            public EndPointAttackRequest? LastRequest;

            public bool ReceiveEndPointAttack(EndPointAttackRequest request)
            {
                AttackCount++;
                LastRequest = request;
                return _acceptsDamage;
            }
        }

        /// <summary>
        /// 测试用 EnemyBase 子类，暴露 protected API，记录死亡请求并可转发到管理器。
        /// </summary>
        private sealed class LifecycleEnemy : EnemyBase
        {
            /// <summary>死亡请求累计次数。</summary>
            public int DeathRequestedCount;

            /// <summary>上次死亡请求的 ID。</summary>
            public int LastDeathRequestedEnemyId;

            /// <summary>上次死亡请求的原因。</summary>
            public EnemyRemovalReason LastDeathRequestedReason;

            /// <summary>
            /// 配置测试依赖并注入记录 + 可选转发的死亡请求回调。
            /// </summary>
            /// <param name="map">地图数据。</param>
            /// <param name="target">终点攻击目标。</param>
            /// <param name="onDeathRequested">
            /// 可选的外部移除回调（如 EnemyManager.RequestRemoveEnemy）。记录后继续转发。
            /// </param>
            internal void ConfigureForTest(
                MapData map,
                IEnemyEndPointAttackTarget target,
                EnemyDeathRequestHandler onDeathRequested = null)
            {
                Configure(
                    map,
                    CellSize,
                    target,
                    onEnemyKilled: (killedId, attackerId, reward, isPlayerLane) => { },
                    onDeathRequested: (killedId, reason) =>
                    {
                        DeathRequestedCount++;
                        LastDeathRequestedEnemyId = killedId;
                        LastDeathRequestedReason = reason;
                        onDeathRequested?.Invoke(killedId, reason);
                    });
            }

            internal void InitForTest(bool isPlayerLane, int maxHealth)
            {
                Init(isPlayerLane, maxHealth, width: 40f, height: 40f);
            }

            internal void StartMoving() => BeginMoving();

            internal void AssignIdForTest(int id) => AssignRuntimeId(id);
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>直线路径：玩家 [(0,0),(1,0),(2,0),(3,0)]，对手反向。</summary>
        private static MapData BuildLinearPathMapData()
        {
            var grid = new List<IReadOnlyList<string>>
            {
                new List<string> { "0_1" },
                new List<string> { "0_1" },
                new List<string> { "0_1" },
                new List<string> { "0_1" },
            };
            var playerPath = new List<GridPosition>
            {
                new GridPosition(0, 0), new GridPosition(1, 0),
                new GridPosition(2, 0), new GridPosition(3, 0),
            };
            var opponentPath = new List<GridPosition>
            {
                new GridPosition(3, 0), new GridPosition(2, 0),
                new GridPosition(1, 0), new GridPosition(0, 0),
            };
            return MapData.FromColumnMajorGrid(
                grid, DecodeCell, mapIndex: 0,
                playerStart: new GridPosition(0, 0),
                playerEnd: new GridPosition(3, 0),
                opponentStart: new GridPosition(3, 0),
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

        /// <summary>让敌人沿直线路径走完全程（8 帧 × 1600ms，80px/帧避免过冲振荡）。</summary>
        private static void WalkToEnd(IEnemyEntity enemy)
        {
            for (int i = 0; i < 8; i++)
            {
                enemy.Update(1600);
            }
        }

        /// <summary>
        /// 按正式运行时顺序装配一个真实 Mob0Enemy：Acquire → Configure → InitializeStats →
        /// Init → BeginMoving → Register。
        /// Configure 为 protected，通过反射调用（与 BattleRuntimeFactory 一致）。
        /// </summary>
        private static Mob0Enemy SpawnRealEnemy(
            EnemyFactory factory,
            EnemyManager manager,
            MapData map,
            IEnemyEndPointAttackTarget target,
            bool isPlayerLane,
            int maxHealth)
        {
            Mob0Enemy enemy = factory.Acquire();

            // 反射调用 protected EnemyBase.Configure，注入 map/cellSize/target/击杀/移除回调。
            EnemyDeathRequestHandler onDeathRequested = (killedId, reason) =>
                manager.RequestRemoveEnemy(killedId, reason);
            typeof(EnemyBase).InvokeMember(
                "Configure",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod,
                null, enemy, new object[]
                {
                    map, CellSize, target,
                    (EnemyKilledHandler)((killedId, attackerId, reward, lane) => { }),
                    onDeathRequested,
                });

            enemy.InitializeStats(new Mob0EnemyInitStats(
                healthByWave: new[] { maxHealth, maxHealth },
                speed: 50, contactDamage: 1, rewardGold: 1));
            enemy.Init(isPlayerLane, maxHealth, width: 40f, height: 40f);
            enemy.BeginMoving();
            manager.Register(enemy);
            return enemy;
        }

        // ====================================================================
        // 1. 终点攻击一次性 + 车道绑定
        // ====================================================================

        [Test]
        [Description("玩家车道敌人抵达终点：攻击玩家方阿斗桩恰好一次，伤害 1，攻击者 ID 正确。"
            + " 后续帧不重复攻击、不重复请求回收。")]
        public void EndPointAttack_PlayerLane_AttacksPlayerTargetExactlyOnce()
        {
            MapData map = BuildLinearPathMapData();
            var target = new RecordingTarget();

            var enemy = new LifecycleEnemy();
            enemy.ConfigureForTest(map, target);
            enemy.AssignIdForTest(1);
            enemy.InitForTest(isPlayerLane: true, maxHealth: 100);
            enemy.StartMoving();

            WalkToEnd(enemy);

            Assert.AreEqual(1, target.AttackCount, "抵达终点攻击玩家阿斗恰好一次。");
            Assert.AreEqual(1, target.LastRequest.Value.Damage, "终点攻击伤害为 1。");
            Assert.IsTrue(target.LastRequest.Value.IsPlayerLane, "目标车道为玩家方。");
            Assert.AreEqual(1, target.LastRequest.Value.AttackerRuntimeId, "攻击者 ID 为敌人 ID。");

            // 后续帧不重复攻击、不重复请求回收。
            enemy.Update(1600);
            enemy.Update(1600);
            Assert.AreEqual(1, target.AttackCount, "终点攻击严格一次性。");
            Assert.AreEqual(1, enemy.DeathRequestedCount, "回收请求严格一次性。");
        }

        [Test]
        [Description("对手车道敌人抵达终点：攻击对手方阿斗桩恰好一次，玩家方阿斗不受影响。")]
        public void EndPointAttack_OpponentLane_AttacksOpponentTargetOnly()
        {
            MapData map = BuildLinearPathMapData();
            var playerTarget = new RecordingTarget();
            var opponentTarget = new RecordingTarget();

            var enemy = new LifecycleEnemy();
            enemy.ConfigureForTest(map, opponentTarget);
            enemy.InitForTest(isPlayerLane: false, maxHealth: 100);
            enemy.StartMoving();

            WalkToEnd(enemy);

            // 对手车道敌人只攻击对手方阿斗，玩家方阿斗不受影响。
            Assert.AreEqual(1, opponentTarget.AttackCount, "抵达终点攻击对手阿斗恰好一次。");
            Assert.AreEqual(0, playerTarget.AttackCount, "对手车道敌人不攻击玩家阿斗。");
            Assert.IsFalse(opponentTarget.LastRequest.Value.IsPlayerLane, "目标车道为对手方。");
        }

        [Test]
        [Description("目标拒绝伤害仍视为已尝试：攻击只 1 次且仍请求移除。")]
        public void EndPointAttack_TargetRejects_StillAttemptedOnceAndRemoved()
        {
            MapData map = BuildLinearPathMapData();
            var target = new RecordingTarget(acceptsDamage: false);

            var enemy = new LifecycleEnemy();
            enemy.ConfigureForTest(map, target);
            enemy.InitForTest(isPlayerLane: true, maxHealth: 100);
            enemy.StartMoving();

            WalkToEnd(enemy);

            Assert.AreEqual(1, target.AttackCount, "目标拒绝伤害仍视为已尝试（1 次）。");
            Assert.AreEqual(1, enemy.DeathRequestedCount, "目标拒绝伤害仍请求移除。");
            Assert.AreEqual(
                EnemyRemovalReason.ReachedEndPoint, enemy.LastDeathRequestedReason,
                "移除原因为 ReachedEndPoint。");
        }

        // ====================================================================
        // 2. 终点回收（真实 Factory + 池 + 管理器完整链路）
        // ====================================================================

        [Test]
        [Description("抵达终点（真实装配）：终点攻击一次、仅本车道阿斗受伤、以 ReachedEndPoint 移除、"
            + " EnemyRemoved(..., false) 一次、归还池一次、继续推进不再扣血不再回收。")]
        public void EndPointRecycling_RealAssembly_ReleasesOnceAndNoRepeat()
        {
            MapData map = BuildLinearPathMapData();
            var idAllocator = new RuntimeIdAllocator();
            var pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            var factory = new EnemyFactory(idAllocator, pool);
            var mgr = new EnemyManager(GridSize);
            mgr.ReleaseEnemy = enemy => factory.Release((Mob0Enemy)enemy);

            int removedCount = 0;
            bool removedWithDeathEffect = true;
            mgr.EnemyRemoved += (id, playDeathEffect) =>
            {
                removedCount++;
                removedWithDeathEffect = playDeathEffect;
            };

            var target = new RecordingTarget();
            Mob0Enemy enemy = SpawnRealEnemy(
                factory, mgr, map, target, isPlayerLane: true, maxHealth: 10);

            Assert.AreEqual(1, pool.ActiveCount, "装配后池活动租借 1。");
            Assert.AreEqual(1, mgr.Count, "装配后管理器有 1 个敌人。");

            // 走完全程触发终点攻击并请求移除；EnemyManager.Update 结束处理移除队列。
            WalkToEnd(enemy);
            mgr.Update(0);

            Assert.AreEqual(1, target.AttackCount, "终点攻击恰好一次。");
            Assert.AreEqual(1, target.LastRequest.Value.Damage, "终点攻击伤害为 1。");
            Assert.AreEqual(0, mgr.Count, "移除后管理器集合为空。");
            Assert.IsFalse(mgr.HasSpatialRegistration(1), "空间索引已清除。");
            Assert.AreEqual(1, removedCount, "EnemyRemoved 触发一次。");
            Assert.IsFalse(removedWithDeathEffect, "ReachedEndPoint 不播死亡表现。");
            Assert.AreEqual(1, factory.RecoverCount, "Factory 收到 1 次 Release。");
            Assert.AreEqual(0, pool.ActiveCount, "池活动租借归零。");

            // 继续推进多帧：不再扣血、不再回收。
            int removedAfter = removedCount;
            enemy.Update(1600);
            enemy.Update(1600);
            Assert.AreEqual(1, target.AttackCount, "后续帧不再攻击。");
            Assert.AreEqual(removedAfter, removedCount, "后续帧不再触发移除。");
            Assert.AreEqual(1, factory.RecoverCount, "后续帧不再重复回收。");
        }

        [Test]
        [Description("重复请求移除同 ID：只注销一次、只 Release 一次。")]
        public void RemoveQueue_DuplicateRequest_ReleasesOnlyOnce()
        {
            MapData map = BuildLinearPathMapData();
            var idAllocator = new RuntimeIdAllocator();
            var pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            var factory = new EnemyFactory(idAllocator, pool);
            var mgr = new EnemyManager(GridSize);
            mgr.ReleaseEnemy = enemy => factory.Release((Mob0Enemy)enemy);

            var target = new RecordingTarget();
            SpawnRealEnemy(factory, mgr, map, target, isPlayerLane: true, maxHealth: 10);

            mgr.RequestRemoveEnemy(1, EnemyRemovalReason.ReachedEndPoint);
            mgr.RequestRemoveEnemy(1, EnemyRemovalReason.Forced);
            mgr.RequestRemoveEnemy(1, EnemyRemovalReason.Killed);
            mgr.ProcessRemoveQueue();

            Assert.AreEqual(0, mgr.Count, "重复请求只移除一次。");
            Assert.AreEqual(1, factory.RecoverCount, "重复请求只 Release 一次。");
            Assert.AreEqual(0, pool.ActiveCount, "池活动租借归零。");
        }

        // ====================================================================
        // 3. 击杀回收（真实装配）
        // ====================================================================

        [Test]
        [Description("被击杀（真实装配）：reason=Killed，EnemyRemoved 播死亡表现，恰好归还池一次。")]
        public void Killed_HitToZero_PlaysDeathEffectAndReleasesOnce()
        {
            MapData map = BuildLinearPathMapData();
            var idAllocator = new RuntimeIdAllocator();
            var pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            var factory = new EnemyFactory(idAllocator, pool);
            var mgr = new EnemyManager(GridSize);
            mgr.ReleaseEnemy = enemy => factory.Release((Mob0Enemy)enemy);

            bool removedWithDeathEffect = false;
            int removedCount = 0;
            mgr.EnemyRemoved += (id, playDeathEffect) =>
            {
                removedCount++;
                removedWithDeathEffect = playDeathEffect;
            };

            var target = new RecordingTarget();
            Mob0Enemy enemy = SpawnRealEnemy(
                factory, mgr, map, target, isPlayerLane: true, maxHealth: 10);

            // 一击致死：EnemyBase.Hit 归零请求以 Killed 原因移除。
            enemy.Hit(10, attackerId: 99);
            mgr.Update(0);

            Assert.AreEqual(0, mgr.Count, "击杀后管理器集合为空。");
            Assert.IsTrue(removedWithDeathEffect, "Killed 保留死亡表现。");
            Assert.AreEqual(1, removedCount, "EnemyRemoved 触发一次。");
            Assert.AreEqual(1, factory.RecoverCount, "被击杀也恰好归还池一次。");
            Assert.AreEqual(0, pool.ActiveCount, "池活动租借归零。");
        }

        // ====================================================================
        // 4. Settling 批量清理
        // ====================================================================

        [Test]
        [Description("Settling GameOver：全部敌军恰好归还一次，管理器/空间索引/池计数一致。"
            + " 每个 Mob0Enemy 在 Init 前完成 Configure（与正式运行时顺序一致）。")]
        public void Settling_GameOver_ReleasesAllEnemiesExactlyOnce()
        {
            MapData map = BuildLinearPathMapData();
            var idAllocator = new RuntimeIdAllocator();
            var pool = new BattleObjectPool<Mob0Enemy>(() => new Mob0Enemy());
            var factory = new EnemyFactory(idAllocator, pool);
            var mgr = new EnemyManager(GridSize);
            mgr.ReleaseEnemy = enemy => factory.Release((Mob0Enemy)enemy);

            for (int i = 0; i < 3; i++)
            {
                SpawnRealEnemy(factory, mgr, map, new RecordingTarget(), isPlayerLane: true, maxHealth: 10);
            }

            Assert.AreEqual(3, mgr.Count, "登记 3 个敌人。");
            Assert.AreEqual(3, pool.ActiveCount, "池活动租借 3。");

            mgr.GameOver();

            Assert.AreEqual(0, mgr.Count, "Settling 后管理器集合为空。");
            Assert.AreEqual(0, mgr.SpatialCellCount, "Settling 后空间索引为空。");
            Assert.AreEqual(0, pool.ActiveCount, "Settling 后池活动租借归零（全部归还）。");
            Assert.AreEqual(3, factory.RecoverCount, "Factory 收到 3 次 Release。");

            // 三个敌人均回到空闲池：再次 GameOver 不重复归还（幂等），RecoverCount 不再增长。
            mgr.GameOver();
            Assert.AreEqual(3, factory.RecoverCount, "重复 GameOver 不重复归还。");
        }
    }
}
