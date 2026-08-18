using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Enemy
{
    // ============================================================================
    // 任务 3.1/3.2/3.3/3.4/3.6：配置化普通敌人运行时基础波 EditMode 测试
    // ----------------------------------------------------------------------------
    // 覆盖（task 3.8 的敌人侧要求 + 本波验收清单）：
    //   1. Mob0～Mob3 四 key/type 构造（薄类型固定身份）。
    //   2. 四池独立租借/回收/复用（BattlePoolScope 每类型独立池）。
    //   3. unknown key：封闭注册表显式失败，不创建占位敌人、不泄漏租借。
    //   4. 数值公式：baseHealth × strategyMultiplier × earlyMultiplier 用 double 计算，
    //      最终只一次 Math.Round(AwayFromZero)，最小 1；速度/接触伤害/奖励来自同一定义。
    //   5. difficulty/profile/early 越界：显式失败，不夹取。
    //   6. generation 在复用时单调变化（task 3.6）。
    //   7. Reset 后旧 waveOrder/旧回调不存活；迟到 presentation 回调被世代守卫拒绝。
    //   8. Acquire 初始化失败回滚本次租借到正确池。
    //
    // 本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束。
    // ============================================================================

    /// <summary>
    /// 配置化普通敌人运行时基础波测试：四类型、四池、数值解析、generation/租借身份与回滚。
    /// </summary>
    [TestFixture]
    internal class ConfiguredEnemySpawningTests
    {
        /// <summary>格子尺寸（px，对应 map.gridWidth=80）。</summary>
        private const float CellSize = 80f;

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>构造直线路径 MapData：玩家 [(0,0),(1,0),(2,0),(3,0)]，对手反向。</summary>
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

        /// <summary>把源 "kind_lane" 字符串解码为 GridCell。</summary>
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

        /// <summary>构造包含 Mob0～Mob3 四条定义的敌人目录。</summary>
        private static EnemyCatalogSnapshot BuildFourEnemyCatalog()
        {
            return new EnemyCatalogSnapshot(new List<EnemyDefinitionSnapshot>
            {
                new EnemyDefinitionSnapshot(0, "Mob0", "enemy_mob0", 50, new[] { 10, 20, 30 }, new float[] { 1f, 1.1f, 1.2f }, 1, 1),
                new EnemyDefinitionSnapshot(1, "Mob1", "enemy_mob1", 55, new[] { 12, 24, 36 }, new float[] { 1f, 1.1f, 1.2f }, 2, 2),
                new EnemyDefinitionSnapshot(2, "Mob2", "enemy_mob2", 60, new[] { 15, 30, 45 }, new float[] { 1f, 1.2f, 1.3f }, 3, 3),
                new EnemyDefinitionSnapshot(3, "Mob3", "enemy_mob3", 65, new[] { 18, 36, 54 }, new float[] { 1f, 1.3f, 1.4f }, 4, 4),
            });
        }

        /// <summary>Buff 生命协议用目录：仅把 Mob0 基础生命固定为 100。</summary>
        private static EnemyCatalogSnapshot BuildBuffEnemyCatalog()
        {
            return new EnemyCatalogSnapshot(new List<EnemyDefinitionSnapshot>
            {
                new EnemyDefinitionSnapshot(0, "Mob0", "enemy_mob0", 50, new[] { 100, 100, 100 }, new float[] { 1f, 1f, 1f }, 1, 1),
                new EnemyDefinitionSnapshot(1, "Mob1", "enemy_mob1", 55, new[] { 12, 24, 36 }, new float[] { 1f, 1.1f, 1.2f }, 2, 2),
                new EnemyDefinitionSnapshot(2, "Mob2", "enemy_mob2", 60, new[] { 15, 30, 45 }, new float[] { 1f, 1.2f, 1.3f }, 3, 3),
                new EnemyDefinitionSnapshot(3, "Mob3", "enemy_mob3", 65, new[] { 18, 36, 54 }, new float[] { 1f, 1.3f, 1.4f }, 4, 4),
            });
        }

        /// <summary>用新链构造路径创建敌人工厂（目录 + BattlePoolScope）。</summary>
        private static EnemyFactory CreateFactory(EnemyCatalogSnapshot catalog, BattlePoolScope scope)
        {
            return new EnemyFactory(new RuntimeIdAllocator(), catalog, scope);
        }

        /// <summary>构造出生请求（profile 默认 3 项 1.0）。</summary>
        private static EnemySpawnRequest CreateRequest(
            string key,
            MapData map,
            int waveOrder = 1,
            int difficultyIndex = 0,
            IReadOnlyList<float> profile = null,
            bool isPlayerLane = true)
        {
            return new EnemySpawnRequest(
                enemyKey: key,
                isPlayerLane: isPlayerLane,
                waveOrder: waveOrder,
                difficultyIndex: difficultyIndex,
                strategyProfile: profile ?? new float[] { 1f, 1f, 1f },
                map: map,
                cellSize: CellSize,
                endPointTarget: new NoOpEndPointTarget(),
                onEnemyKilled: (killedId, attackerId, reward, lane) => { },
                onDeathRequested: (killedId, reason) => { });
        }

        /// <summary>恒返回 true 的终点攻击目标桩。</summary>
        private sealed class NoOpEndPointTarget : IEnemyEndPointAttackTarget
        {
            public bool ReceiveEndPointAttack(EndPointAttackRequest request) => true;
        }

        // ====================================================================
        // 1. 四 key/type 构造（薄类型固定身份）
        // ====================================================================

        [Test]
        [Description("Mob0～Mob3 四个薄类型固定各自 enemyKey 与 typeIndex（0～3）。")]
        public void FourTypes_FixedKeyAndTypeIndex()
        {
            var mob0 = new Mob0Enemy();
            var mob1 = new Mob1Enemy();
            var mob2 = new Mob2Enemy();
            var mob3 = new Mob3Enemy();

            Assert.AreEqual("Mob0", mob0.EnemyKey);
            Assert.AreEqual(0, mob0.TypeIndex);
            Assert.AreEqual("Mob1", mob1.EnemyKey);
            Assert.AreEqual(1, mob1.TypeIndex);
            Assert.AreEqual("Mob2", mob2.EnemyKey);
            Assert.AreEqual(2, mob2.TypeIndex);
            Assert.AreEqual("Mob3", mob3.EnemyKey);
            Assert.AreEqual(3, mob3.TypeIndex);
        }

        // ====================================================================
        // 2. 四池独立租借/回收/复用
        // ====================================================================

        [Test]
        [Description("EnemyFactory 由目录建四键注册表：每类型独立池，租借/回收/复用互不干扰。")]
        public void FourPools_IndependentRentalRelease_ReusePerType()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();
            ConfiguredEnemyBase mob0 = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 1));
            ConfiguredEnemyBase mob1 = factory.Acquire(CreateRequest("Mob1", map, waveOrder: 2));
            ConfiguredEnemyBase mob2 = factory.Acquire(CreateRequest("Mob2", map, waveOrder: 3));
            ConfiguredEnemyBase mob3 = factory.Acquire(CreateRequest("Mob3", map, waveOrder: 4));

            Assert.IsInstanceOf<Mob0Enemy>(mob0, "Mob0 键返回 Mob0Enemy。");
            Assert.IsInstanceOf<Mob1Enemy>(mob1, "Mob1 键返回 Mob1Enemy。");
            Assert.IsInstanceOf<Mob2Enemy>(mob2, "Mob2 键返回 Mob2Enemy。");
            Assert.IsInstanceOf<Mob3Enemy>(mob3, "Mob3 键返回 Mob3Enemy。");
            Assert.AreEqual(4, scope.PoolCount, "四个类型各一个独立池。");

            // 回收 Mob0/Mob1，再按同键租借应复用同一对象；其他池不受影响。
            Assert.IsTrue(factory.Release(mob0), "Release Mob0。");
            Assert.IsTrue(factory.Release(mob1), "Release Mob1。");

            ConfiguredEnemyBase mob0Again = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 5));
            ConfiguredEnemyBase mob1Again = factory.Acquire(CreateRequest("Mob1", map, waveOrder: 6));

            Assert.AreSame(mob0, mob0Again, "Mob0 池复用同一对象（LIFO）。");
            Assert.AreSame(mob1, mob1Again, "Mob1 池复用同一对象（LIFO）。");
            Assert.IsNotInstanceOf<Mob1Enemy>(mob0Again, "Mob0 键不会返回 Mob1 实例。");

            factory.Release(mob2);
            factory.Release(mob3);
            factory.Release(mob0Again);
            factory.Release(mob1Again);
            Assert.IsTrue(scope.AssertAllActiveReleased(), "全部租借对称归还。");
        }

        // ====================================================================
        // 3. unknown key
        // ====================================================================

        [Test]
        [Description("未知敌人键在 Acquire 时显式失败，不创建占位敌人、不触发任何池租借。")]
        public void Acquire_UnknownKey_ThrowsAndNoLeak()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();
            int registeredPoolCount = scope.PoolCount;

            EnemySpawnRequest zombie = CreateRequest("Zombie", map, waveOrder: 1);

            Assert.Throws<ArgumentException>(() => factory.Acquire(zombie), "未知键显式失败。");
            Assert.AreEqual(registeredPoolCount, scope.PoolCount, "未知键不额外创建池。");
            Assert.IsTrue(scope.AssertAllActiveReleased(), "未知键不泄漏租借。");
        }

        // ====================================================================
        // 4. 数值公式与一次舍入（AwayFromZero）
        // ====================================================================

        [Test]
        [Description("最大生命 = baseHealth × strategyMultiplier × earlyMultiplier，最终只一次 AwayFromZero 舍入。")]
        public void Resolver_Formula_OneRoundAwayFromZero()
        {
            // 3 × 1.5 × 1.3 = 5.85 → AwayFromZero → 6。
            var def = new EnemyDefinitionSnapshot(0, "Mob0", "addr", 50, new[] { 3 }, new float[] { 1.3f }, 1, 1);
            ConfiguredEnemyResolvedStats resolved = EnemyStatsResolver.Resolve(def, 0, new float[] { 1.5f });

            Assert.AreEqual(6, resolved.MaxHealth, "5.85 一次 AwayFromZero 舍入为 6。");

            // 2 × 1.25 × 1.0 = 2.5（中点）→ AwayFromZero → 3（非银行家舍入 2）。
            var defMid = new EnemyDefinitionSnapshot(1, "Mob1", "addr", 60, new[] { 2 }, new float[] { 1.0f }, 2, 2);
            ConfiguredEnemyResolvedStats mid = EnemyStatsResolver.Resolve(defMid, 0, new float[] { 1.25f });

            Assert.AreEqual(3, mid.MaxHealth, "中点 2.5 以 AwayFromZero 舍入为 3。");
        }

        [Test]
        [Description("速度/接触伤害/奖励直接来自同一敌人定义，不 fallback。")]
        public void Resolver_SpeedContactReward_FromSameDefinition()
        {
            var def = new EnemyDefinitionSnapshot(2, "Mob2", "addr", 77, new[] { 10 }, new float[] { 1f }, 8, 9);
            ConfiguredEnemyResolvedStats resolved = EnemyStatsResolver.Resolve(def, 0, new float[] { 1f });

            Assert.AreEqual(77, resolved.MoveSpeed, "移动速度来自定义。");
            Assert.AreEqual(8, resolved.ContactDamage, "接触伤害来自定义。");
            Assert.AreEqual(9, resolved.RewardGold, "击杀奖励来自定义。");
        }

        [Test]
        [Description("舍入结果最小为 1（不允许 0 血敌人）。")]
        public void Resolver_MinimumHealthIsOne()
        {
            var def = new EnemyDefinitionSnapshot(0, "Mob0", "addr", 50, new[] { 1 }, new float[] { 0.4f }, 1, 1);
            ConfiguredEnemyResolvedStats resolved = EnemyStatsResolver.Resolve(def, 0, new float[] { 1.0f });

            Assert.AreEqual(1, resolved.MaxHealth, "1×1.0×0.4=0.4 → 舍入 0 → 最小钳到 1。");
        }

        // ====================================================================
        // 5. difficulty/profile/early 越界显式失败
        // ====================================================================

        [Test]
        [Description("difficultyIndex 越界（负/超血量曲线/profile/早期乘数）显式失败，不夹取。")]
        public void Resolver_OutOfRange_ThrowsWithoutClamping()
        {
            var def = new EnemyDefinitionSnapshot(0, "Mob0", "addr", 50, new[] { 10, 20 }, new float[] { 1f, 1f }, 1, 1);

            // 负索引。
            Assert.Throws<EnemyStatsResolutionException>(
                () => EnemyStatsResolver.Resolve(def, -1, new float[] { 1f, 1f }));
            // 超出 HealthByWave。
            Assert.Throws<EnemyStatsResolutionException>(
                () => EnemyStatsResolver.Resolve(def, 2, new float[] { 1f, 1f }));
            // 超出 strategyProfile。
            Assert.Throws<EnemyStatsResolutionException>(
                () => EnemyStatsResolver.Resolve(def, 1, new float[] { 1f }));
            // 超出 EarlyRoundHealthMultipliers。
            var defEarlyShort = new EnemyDefinitionSnapshot(0, "Mob0", "addr", 50, new[] { 10, 20 }, new float[] { 1f }, 1, 1);
            Assert.Throws<EnemyStatsResolutionException>(
                () => EnemyStatsResolver.Resolve(defEarlyShort, 1, new float[] { 1f, 1f }));
        }

        [Test]
        [Description("工厂按请求解析难度数值：difficulty 决定血量，profile 决定乘数。")]
        public void Factory_Acquire_ResolvesConfiguredStats()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();

            // Mob2 difficulty=1：baseHealth=30 × profile=1.2 × early=1.2 = 43.2 → 43。
            ConfiguredEnemyBase enemy = factory.Acquire(
                CreateRequest("Mob2", map, waveOrder: 3, difficultyIndex: 1, profile: new float[] { 1f, 1.2f, 1f }));

            Assert.IsInstanceOf<Mob2Enemy>(enemy, "返回 Mob2Enemy。");
            Assert.AreEqual(43, enemy.MaxHealthBaseValue, "30×1.2×1.2=43.2 一次舍入为 43。");
            Assert.AreEqual(60, enemy.BaseMoveSpeedValue, "速度来自 Mob2 定义。");
            Assert.AreEqual(3, enemy.Stats.Value.ContactDamage, "接触伤害来自 Mob2 定义。");
            Assert.AreEqual(3, enemy.Stats.Value.RewardGold, "奖励来自 Mob2 定义。");
            Assert.AreEqual((int)EnemyRuntimeState.Moving, enemy.CurrentState, "工厂 Acquire 后已开始移动。");
            Assert.IsTrue(enemy.CurrentLease.IsValid, "租借身份有效。");
            Assert.AreEqual(3, enemy.CurrentLease.WaveOrder, "租借身份携带 waveOrder。");

            factory.Release(enemy);
            Assert.IsTrue(scope.AssertAllActiveReleased(), "全部租借对称归还。");
        }

        // ====================================================================
        // 6. generation 在复用时单调变化（task 3.6）
        // ====================================================================

        [Test]
        [Description("每次租借 generation 单调递增；复用同一对象时 generation 改变并携带新 waveOrder。")]
        public void Generation_MonotonicallyIncrementsOnReuse()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();

            ConfiguredEnemyBase first = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 1));
            Assert.AreEqual(1, first.Generation, "首次租借 generation=1。");
            EnemyLeaseIdentity firstLease = first.CurrentLease;
            Assert.AreEqual(1, firstLease.WaveOrder, "首租借 waveOrder=1。");

            factory.Release(first);

            ConfiguredEnemyBase second = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 2));
            Assert.AreSame(first, second, "复用同一对象实例。");
            Assert.AreEqual(2, second.Generation, "复用后 generation 递增为 2。");
            Assert.AreNotEqual(firstLease, second.CurrentLease, "两次租借身份不同（generation/waveOrder 均变化）。");
            Assert.AreEqual(2, second.CurrentLease.WaveOrder, "新租借 waveOrder=2。");
            Assert.IsTrue(second.CurrentLease.IsValid, "新租借身份有效。");

            factory.Release(second);
            Assert.IsTrue(scope.AssertAllActiveReleased(), "全部租借对称归还。");
        }

        // ====================================================================
        // 7. Reset 后旧 waveOrder/旧回调不存活；迟到 presentation 被拒绝
        // ====================================================================

        [Test]
        [Description("Reset 清空 waveOrder 与死亡表现状态；旧租借的迟到完成回调被世代守卫拒绝。")]
        public void Reset_ClearsWaveOrder_RejectsStalePresentationCallback()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();

            // 租借 1：waveOrder=5，触发死亡表现。
            ConfiguredEnemyBase first = factory.Acquire(CreateRequest("Mob1", map, waveOrder: 5));
            first.BeginDeath();
            Assert.IsTrue(first.IsDeathPresentationStarted, "第一轮死亡表现已开始。");
            Assert.AreEqual(5, first.WaveOrder, "租借 1 waveOrder=5。");

            // Release → ResetState 清空 waveOrder、死亡表现边界与 callbacks。
            factory.Release(first);
            Assert.AreEqual(0, first.WaveOrder, "Reset 后 waveOrder 清空。");
            Assert.IsFalse(first.IsDeathPresentationStarted, "Reset 后死亡表现标记清空。");
            Assert.IsFalse(first.IsDeathScheduled, "Reset 后调度标记清空。");
            Assert.IsFalse(first.CurrentLease.IsValid, "Reset 后租借身份失效（runtimeId=0）。");

            // 租借 2：新 waveOrder；旧租借的迟到完成回调不得生效。
            ConfiguredEnemyBase second = factory.Acquire(CreateRequest("Mob1", map, waveOrder: 7));
            Assert.AreSame(first, second, "复用同一对象。");
            Assert.AreEqual(7, second.WaveOrder, "新租借 waveOrder=7。");

            // 迟到回调（来自旧租借）：未调度/世代不符 → 空操作。
            second.OnDeathPresentationCompleted();
            Assert.IsFalse(second.IsDeathPresentationCompleted, "迟到回调不置已完成。");
            Assert.IsFalse(second.InPool, "迟到回调不触发 GameOver 回收。");

            // 新租借正常 BeginDeath → 完成回调生效。
            second.BeginDeath();
            second.OnDeathPresentationCompleted();
            Assert.IsTrue(second.IsDeathPresentationCompleted, "新租借完成回调生效。");
            Assert.IsTrue(second.InPool, "完成后标记入池（GameOver）。");

            factory.Release(second);
            Assert.IsTrue(scope.AssertAllActiveReleased(), "全部租借对称归还。");
        }

        [Test]
        [Description("Reset 清空本次 callbacks：Reset 后受击不再触发旧击杀回调，也不重复奖励。")]
        public void Reset_ClearsCallbacks_NoStaleKillReward()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();

            int killCallbacks = 0;
            int rewardGold = 1;

            // 租借 1：注入记录击杀回调。
            var request1 = new EnemySpawnRequest(
                enemyKey: "Mob0",
                isPlayerLane: true,
                waveOrder: 1,
                difficultyIndex: 0,
                strategyProfile: new float[] { 1f },
                map: map,
                cellSize: CellSize,
                endPointTarget: new NoOpEndPointTarget(),
                onEnemyKilled: (killedId, attackerId, reward, lane) => { killCallbacks++; rewardGold = reward; },
                onDeathRequested: (killedId, reason) => { });
            ConfiguredEnemyBase first = factory.Acquire(request1);

            // 释放（ResetState 将 _onEnemyKilled 置 null）。
            factory.Release(first);

            // 租借 2：不注入回调。Reset 后旧回调不存活——击杀不触发旧回调。
            ConfiguredEnemyBase second = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 2));
            second.Hit(10, attackerId: 1);

            Assert.AreEqual(0, killCallbacks, "Reset 后旧击杀回调不存活（_onEnemyKilled 已清空）。");
            Assert.AreEqual(1, rewardGold, "奖励不重复提交。");

            factory.Release(second);
            Assert.IsTrue(scope.AssertAllActiveReleased(), "全部租借对称归还。");
        }

        // ====================================================================
        // 8. Acquire 初始化失败回滚池
        // ====================================================================

        [Test]
        [Description("Acquire 初始化失败（difficulty 越界）把本次租借回滚到正确池，不泄漏活动租借。")]
        public void Acquire_InitializationFailure_RollsBackRentalToCorrectPool()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();

            // difficultyIndex 越界 → EnemyStatsResolver 显式失败 → 工厂回滚本次租借后重抛。
            EnemySpawnRequest badRequest = CreateRequest("Mob2", map, waveOrder: 1, difficultyIndex: 999);
            Assert.Throws<EnemyStatsResolutionException>(() => factory.Acquire(badRequest));

            // 若失败租借未回滚，后续成功 Acquire + Release 后仍残留 1 个活动租借。
            ConfiguredEnemyBase good = factory.Acquire(CreateRequest("Mob2", map, waveOrder: 1));
            factory.Release(good);
            Assert.IsTrue(scope.AssertAllActiveReleased(), "失败租借已回滚（否则活动计数残留）。");
        }

        // ====================================================================
        // 9. EnemyManager generation-aware 内部移除事实（task 3.7 / design 决策 5）
        // ====================================================================

        /// <summary>构造带回收桥接的 EnemyManager（ReleaseEnemy 按 ConfiguredEnemyBase 归还正确池）。</summary>
        private static EnemyManager CreateManager(EnemyFactory factory, BuffManager buffManager = null)
        {
            var manager = new EnemyManager(
                EnemyManager.DefaultGridSize,
                buffManager: buffManager);
            manager.ReleaseEnemy = enemy =>
            {
                if (enemy is ConfiguredEnemyBase configured)
                {
                    factory.Release(configured);
                }
            };
            return manager;
        }

        [Test]
        [Description("Killed/ReachedEndPoint/Forced 内部移除事实各恰好一次且 identity（runtimeId/generation/waveOrder）正确；" +
                     "既有 EnemyRemoved 表现事实与死亡表现语义不变。")]
        public void WaveEntityRemoved_FactsExactlyOnce_IdentityAndReasonDistinguishable()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();
            EnemyManager manager = CreateManager(factory);

            var waveFacts = new List<(WaveEntityHandle handle, EnemyRemovalReason reason)>();
            manager.WaveEntityRemoved += (handle, reason) => waveFacts.Add((handle, reason));
            var removedFacts = new List<(int id, bool playDeathEffect)>();
            manager.EnemyRemoved += (id, playDeathEffect) => removedFacts.Add((id, playDeathEffect));

            // 波次 5：Killed。
            ConfiguredEnemyBase killed = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 5));
            manager.Register(killed);
            EnemyLeaseIdentity killedLease = killed.CurrentLease;
            manager.RequestRemoveEnemy(killed.RuntimeId, EnemyRemovalReason.Killed);
            manager.ProcessRemoveQueue();

            Assert.AreEqual(1, waveFacts.Count, "Killed 内部事实恰好一次。");
            Assert.AreEqual(WaveEntityHandle.FromEnemyLease(killedLease), waveFacts[0].handle,
                "Killed handle = 登记租借身份 + Normal kind。");
            Assert.AreEqual(EnemyRemovalReason.Killed, waveFacts[0].reason, "Killed 原因可区分。");
            Assert.AreEqual(1, removedFacts.Count, "Killed 表现事实一次。");
            Assert.IsTrue(removedFacts[0].playDeathEffect, "Killed 保留死亡表现。");

            // 波次 6：ReachedEndPoint（复用同对象，generation 递增）。
            ConfiguredEnemyBase reached = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 6));
            manager.Register(reached);
            EnemyLeaseIdentity reachedLease = reached.CurrentLease;
            Assert.AreNotEqual(killedLease, reachedLease, "复用后身份变化（generation 递增）。");
            manager.RequestRemoveEnemy(reached.RuntimeId, EnemyRemovalReason.ReachedEndPoint);
            manager.ProcessRemoveQueue();

            Assert.AreEqual(2, waveFacts.Count, "ReachedEndPoint 内部事实恰好一次。");
            Assert.AreEqual(WaveEntityHandle.FromEnemyLease(reachedLease), waveFacts[1].handle,
                "ReachedEndPoint handle 正确。");
            Assert.AreEqual(EnemyRemovalReason.ReachedEndPoint, waveFacts[1].reason, "ReachedEndPoint 原因可区分。");
            Assert.IsFalse(removedFacts[1].playDeathEffect, "ReachedEndPoint 不播放死亡表现。");

            // 波次 7：Forced（战斗结束批量清理）。
            ConfiguredEnemyBase forced = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 7));
            manager.Register(forced);
            EnemyLeaseIdentity forcedLease = forced.CurrentLease;
            manager.GameOver();

            Assert.AreEqual(3, waveFacts.Count, "Forced 内部事实恰好一次。");
            Assert.AreEqual(WaveEntityHandle.FromEnemyLease(forcedLease), waveFacts[2].handle,
                "Forced handle 正确。");
            Assert.AreEqual(EnemyRemovalReason.Forced, waveFacts[2].reason, "Forced 原因可区分。");
            Assert.IsFalse(removedFacts[2].playDeathEffect, "Forced 不播放死亡表现。");
            Assert.IsTrue(scope.AssertAllActiveReleased(), "全部租借对称归还。");
        }

        [Test]
        [Description("旧世代迟到移除请求在同对象被池复用后不得误删新租借（generation 守卫）。")]
        public void WaveEntityRemoved_StaleOldGenerationRequest_DoesNotRemoveNewLease()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();
            EnemyManager manager = CreateManager(factory);

            var waveFacts = new List<(WaveEntityHandle handle, EnemyRemovalReason reason)>();
            manager.WaveEntityRemoved += (handle, reason) => waveFacts.Add((handle, reason));

            // 租借 1：id=1, gen=1, waveOrder=1。
            ConfiguredEnemyBase first = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 1));
            manager.Register(first);
            EnemyLeaseIdentity firstLease = first.CurrentLease;

            // 请求移除（入队 Killed）后未处理即被释放到池（模拟清理路径绕过移除队列）。
            manager.RequestRemoveEnemy(first.RuntimeId, EnemyRemovalReason.Killed);
            manager.ReleaseEnemy(first);

            // 同一对象被新租借：id=2, gen=2, waveOrder=2。
            ConfiguredEnemyBase second = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 2));
            Assert.AreSame(first, second, "复用同一对象实例。");
            manager.Register(second);
            EnemyLeaseIdentity secondLease = second.CurrentLease;
            Assert.AreNotEqual(firstLease, secondLease, "新旧租借身份不同（generation 变化）。");

            // 处理队列：入队身份 (id=1,gen=1) 与当前实体身份 (id=2,gen=2) 不匹配 → 迟到请求幂等忽略。
            manager.ProcessRemoveQueue();

            Assert.AreEqual(0, waveFacts.Count, "迟到旧世代请求不触发移除事实。");
            Assert.IsNotNull(manager.GetById(second.RuntimeId), "新租借未被误删。");
            Assert.AreEqual(secondLease, second.CurrentLease, "新租借身份保持有效。");

            // 清理：移除陈旧登记，再正常移除新租借。
            manager.Unregister(firstLease.RuntimeId);
            manager.RequestRemoveEnemy(second.RuntimeId, EnemyRemovalReason.Killed);
            manager.ProcessRemoveQueue();

            Assert.AreEqual(1, waveFacts.Count, "新租借的移除事实恰好一次。");
            Assert.AreEqual(WaveEntityHandle.FromEnemyLease(secondLease), waveFacts[0].handle,
                "新租借移除 handle 正确。");
            Assert.IsTrue(scope.AssertAllActiveReleased(), "全部租借对称归还。");
        }

        [Test]
        [Description("重复移除请求（同一 ID 多次 ForceRemove）只入队一次：处理恰好一次、回收恰好一次、事实恰好一次。")]
        public void DuplicateRemoval_ProcessedExactlyOnce_ReleasedOnce()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildFourEnemyCatalog(), scope);
            MapData map = BuildLinearPathMapData();
            EnemyManager manager = CreateManager(factory);

            int waveFactCount = 0;
            manager.WaveEntityRemoved += (identity, reason) => waveFactCount++;

            ConfiguredEnemyBase enemy = factory.Acquire(CreateRequest("Mob0", map, waveOrder: 3));
            manager.Register(enemy);

            manager.ForceRemove(enemy.RuntimeId);
            manager.ForceRemove(enemy.RuntimeId);
            manager.ForceRemove(enemy.RuntimeId);

            manager.ProcessRemoveQueue();
            manager.ProcessRemoveQueue(); // 二次处理幂等。

            Assert.AreEqual(1, waveFactCount, "重复移除只触发一次内部事实。");
            Assert.AreEqual(0, manager.Count, "敌人已移除。");
            Assert.IsTrue(scope.AssertAllActiveReleased(), "重复移除只回收一次。");
        }

        [Test]
        [Description("Enemy Buff 适配支持移动/生命/控制通道，生命归零复用既有击杀边界，池复用不继承旧层。")]
        public void BuffAdapter_HealthDeathAndPoolReuse_AreDeterministic()
        {
            var scope = new BattlePoolScope();
            var factory = CreateFactory(BuildBuffEnemyCatalog(), scope);
            var scheduler = new BattleActionScheduler();
            var buffManager = new BuffManager(
                new BuffCatalogSnapshot(new[]
                {
                    NumericBuff(3, BuffNumericChannel.MoveSpeed),
                    NumericBuff(4, BuffNumericChannel.MaxHealth),
                    NumericBuff(5, BuffNumericChannel.CurrentHealth),
                    NumericBuff(6, BuffNumericChannel.Scale),
                    StateBuff(8, BuffStateChannel.MovementDisabled),
                }),
                scheduler);
            EnemyManager manager = CreateManager(factory, buffManager);
            MapData map = BuildLinearPathMapData();

            var kills = new List<(int attackerId, int reward)>();
            EnemySpawnRequest firstRequest = new EnemySpawnRequest(
                "Mob0",
                true,
                1,
                0,
                new float[] { 1f, 1f, 1f },
                map,
                CellSize,
                new NoOpEndPointTarget(),
                (killedId, attackerId, reward, lane) => kills.Add((attackerId, reward)),
                (killedId, reason) => manager.RequestRemoveEnemy(killedId, reason));

            ConfiguredEnemyBase first = factory.Acquire(firstRequest);
            manager.Register(first);
            IBuffTarget firstTarget = first;
            BuffTargetHandle firstHandle = firstTarget.Handle;

            Assert.AreEqual(50, first.EffectiveMoveSpeedForTest);
            Assert.AreEqual(100, first.Health);
            Assert.AreEqual(100, first.MaxHealth);
            Assert.IsTrue(first.Hit(30, 7));

            BuffOperationResult speed = buffManager.Apply(BuffRequest(
                3, firstHandle, new BuffSourceHandle(1), 10));
            BuffOperationResult maximum = buffManager.Apply(BuffRequest(
                4, firstHandle, new BuffSourceHandle(2, 42), 50));
            BuffOperationResult stopped = buffManager.Apply(BuffRequest(
                8, firstHandle, new BuffSourceHandle(3), 0));

            Assert.AreEqual(BuffOperationStatus.Applied, speed.Status);
            Assert.AreEqual(60, first.EffectiveMoveSpeedForTest);
            Assert.AreEqual(120, first.Health, "最大生命增加时保留已损失生命。");
            Assert.AreEqual(150, first.MaxHealth);
            Assert.IsTrue(first.MovementStoppedForTest);
            Assert.AreEqual(
                BuffOperationStatus.UnsupportedTarget,
                buffManager.Apply(BuffRequest(6, firstHandle, new BuffSourceHandle(4), 1)).Status,
                "首期 Enemy 不支持 Scale，必须显式拒绝。");

            Assert.AreEqual(BuffOperationStatus.Removed, buffManager.RemoveInstance(maximum.InstanceId).Status);
            Assert.AreEqual(70, first.Health);
            Assert.AreEqual(100, first.MaxHealth);
            Assert.AreEqual(BuffOperationStatus.Removed, buffManager.RemoveInstance(stopped.InstanceId).Status);
            Assert.IsFalse(first.MovementStoppedForTest);

            maximum = buffManager.Apply(BuffRequest(
                4, firstHandle, new BuffSourceHandle(5, 42), 100));
            Assert.AreEqual(170, first.Health);
            Assert.AreEqual(200, first.MaxHealth);
            Assert.IsTrue(first.Hit(140, 7));
            Assert.AreEqual(30, first.Health);

            Assert.AreEqual(BuffOperationStatus.Removed, buffManager.RemoveInstance(maximum.InstanceId).Status);
            Assert.AreEqual(0, first.Health, "撤销最大生命层可使当前生命归零。");
            Assert.AreEqual(1, kills.Count, "Buff 导致的死亡仍只提交一次击杀奖励。");
            Assert.AreEqual(42, kills[0].attackerId, "死亡归因使用触发归零的 Buff 来源攻击者。");
            Assert.AreEqual(1, kills[0].reward);
            Assert.IsFalse(first.Hit(1, 99), "死亡后重复受击不重复结算。");
            Assert.AreEqual(1, kills.Count);

            manager.ProcessRemoveQueue();
            Assert.AreEqual(0, buffManager.ActiveInstanceCount);
            Assert.AreEqual(0, buffManager.RegisteredTargetCount);

            int secondKillCount = 0;
            int secondAttackerId = int.MinValue;
            EnemySpawnRequest secondRequest = new EnemySpawnRequest(
                "Mob0",
                true,
                2,
                0,
                new float[] { 1f, 1f, 1f },
                map,
                CellSize,
                new NoOpEndPointTarget(),
                (killedId, attackerId, reward, lane) =>
                {
                    secondKillCount++;
                    secondAttackerId = attackerId;
                },
                (killedId, reason) => manager.RequestRemoveEnemy(killedId, reason));

            ConfiguredEnemyBase second = factory.Acquire(secondRequest);
            Assert.AreSame(first, second, "同类型池应复用对象以验证世代隔离。");
            manager.Register(second);
            IBuffTarget secondTarget = second;
            Assert.AreNotEqual(firstHandle, secondTarget.Handle);
            Assert.AreEqual(50, second.EffectiveMoveSpeedForTest);
            Assert.IsFalse(second.MovementStoppedForTest);
            Assert.AreEqual(100, second.Health);
            var staleSameRuntimeId = new BuffTargetHandle(
                BuffEntityKind.Enemy,
                secondTarget.Handle.RuntimeId,
                firstHandle.Generation);
            Assert.AreEqual(
                BuffOperationStatus.StaleTarget,
                buffManager.Apply(BuffRequest(3, staleSameRuntimeId, new BuffSourceHandle(6), 1)).Status,
                "旧世代句柄不能修改复用后的新租借。");

            Assert.AreEqual(
                BuffOperationStatus.Applied,
                buffManager.Apply(BuffRequest(
                    5, secondTarget.Handle, new BuffSourceHandle(7), -100)).Status);
            Assert.AreEqual(0, second.Health);
            Assert.AreEqual(1, secondKillCount);
            Assert.AreEqual(-1, secondAttackerId, "无攻击者的系统 Buff 使用明确哨兵归因。");

            manager.ProcessRemoveQueue();
            Assert.AreEqual(0, buffManager.ActiveInstanceCount);
            Assert.AreEqual(0, buffManager.RegisteredTargetCount);
            Assert.AreEqual(0, buffManager.OwnedScheduleCount);
            Assert.IsTrue(scope.AssertAllActiveReleased(), "两次租借均已对称归还。");
        }

        private static BuffDefinitionSnapshot NumericBuff(int type, BuffNumericChannel channel)
        {
            return new BuffDefinitionSnapshot(
                type,
                $"numeric{type}",
                string.Empty,
                BuffKind.Numeric,
                new[] { (int)channel },
                BuffStackPolicy.Add,
                8,
                string.Empty);
        }

        private static BuffDefinitionSnapshot StateBuff(int type, BuffStateChannel channel)
        {
            return new BuffDefinitionSnapshot(
                type,
                $"state{type}",
                string.Empty,
                BuffKind.State,
                new[] { (int)channel },
                BuffStackPolicy.Add,
                8,
                string.Empty);
        }

        private static BuffApplyRequest BuffRequest(
            int type,
            BuffTargetHandle target,
            BuffSourceHandle source,
            double value)
        {
            return new BuffApplyRequest(
                type,
                target,
                source,
                value,
                BuffValueMode.Flat,
                BuffTimeMode.Permanent,
                0);
        }
    }
}
