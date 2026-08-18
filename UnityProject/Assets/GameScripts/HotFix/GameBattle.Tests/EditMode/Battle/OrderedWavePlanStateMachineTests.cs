using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Battle
{
    // ============================================================================
    // 任务 4.10（纯逻辑部分）：有序波次状态机 EditMode 测试
    // ----------------------------------------------------------------------------
    // 覆盖（task 4.1-4.5 + 4.10 验收清单）：
    //   1. 单路/双路 cardinality 与顺序（同序号先玩家路、后电脑路）。
    //   2. preDelay / spawnInterval / postDelay 边界。
    //   3. 零延迟不跨行（Completed 后下一行最早下一次 Update 开始）。
    //   4. Normal → Boss → Normal 任意 order fake port 推进。
    //   5. Boss 双路各一次；unavailable 端口显式失败。
    //   6. 最后 spawn 仍有 handle 不完成。
    //   7. postDelay 已到但 handle 未清不完成，最后 handle 移除后下一 Update 完成。
    //   8. 完整 handle generation 匹配与重复移除幂等；FromEnemyLease 转换。
    //   9. AllConfiguredWavesCompleted 仅一次。
    //   10. Stop during spawning 不再出生/完成；Boss port Stop 幂等。
    //   11. stepMs < 0 显式失败。
    //   12. Stop 后 Forced 移除事实不促成完成/胜利（task 4.9 顺序契约）。
    //   13. Cleanup 调用 Boss port Cleanup、清所有权，不重启、不发布 AllCompleted（task 4.9）。
    //
    // 纯逻辑：不接触 Scene/FUI/资源加载；出生端口全部为测试替身。
    // ============================================================================

    /// <summary>
    /// 有序波次状态机（WaveManager 新生产链）纯逻辑测试。
    /// </summary>
    [TestFixture]
    internal class OrderedWavePlanStateMachineTests
    {
        // ====================================================================
        // 测试替身
        // ====================================================================

        /// <summary>
        /// 记录式普通敌人出生替身：记录请求，返回递增 runtimeId 的 Normal handle。
        /// </summary>
        private sealed class RecordingNormalSpawner
        {
            public readonly List<NormalWaveSpawnRequest> Requests = new List<NormalWaveSpawnRequest>();

            public readonly List<WaveEntityHandle> Handles = new List<WaveEntityHandle>();

            private int _nextRuntimeId = 1;

            public WaveEntityHandle Spawn(NormalWaveSpawnRequest request)
            {
                Requests.Add(request);
                var handle = new WaveEntityHandle(
                    _nextRuntimeId++, 1, request.WaveOrder, WaveEntityKind.Normal);
                Handles.Add(handle);
                return handle;
            }
        }

        /// <summary>
        /// 记录式 Boss 端口替身：按请求返回 Kind=Boss 的 handle；可配置失败。
        /// </summary>
        private sealed class FakeBossWavePort : IBossWavePort
        {
            public readonly List<BossWaveSpawnRequest> Requests = new List<BossWaveSpawnRequest>();

            public readonly List<WaveEntityHandle> Handles = new List<WaveEntityHandle>();

            public readonly List<string> SupportedKeys = new List<string>();

            public bool IsAvailable { get; set; } = true;

            public bool ThrowOnSpawn { get; set; }

            public int StopCalls { get; private set; }

            public int CleanupCalls { get; private set; }

            private int _nextRuntimeId = 1000;

            IReadOnlyList<string> IBossWavePort.SupportedBossKeys => SupportedKeys;

            public WaveEntityHandle Spawn(BossWaveSpawnRequest request)
            {
                if (ThrowOnSpawn)
                {
                    throw new InvalidOperationException("fake boss port unavailable");
                }

                Requests.Add(request);
                var handle = new WaveEntityHandle(
                    _nextRuntimeId++, 1, request.WaveOrder, WaveEntityKind.Boss);
                Handles.Add(handle);
                return handle;
            }

            public void Stop()
            {
                StopCalls++;
            }

            public void Cleanup()
            {
                CleanupCalls++;
            }
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>构造计划快照（默认 profile 0 = [1f]）。</summary>
        private static OrderedWavePlanSnapshot MakePlan(params WavePlanEntry[] rows)
        {
            var profiles = new Dictionary<int, IReadOnlyList<float>>
            {
                [0] = new float[] { 1f },
            };
            return new OrderedWavePlanSnapshot("test", rows, profiles);
        }

        /// <summary>构造 Normal 行。</summary>
        private static WavePlanEntry NormalRow(
            int order,
            int normalCount,
            long preDelayMs,
            long spawnIntervalMs,
            long postDelayMs,
            bool playerLane = true,
            bool opponentLane = false,
            int difficultyIndex = 0,
            string enemyKey = "Mob0",
            int strategyProfile = 0)
        {
            return new WavePlanEntry(
                "test", order, WavePlanKind.Normal, enemyKey, normalCount, difficultyIndex, "",
                preDelayMs, spawnIntervalMs, postDelayMs, playerLane, opponentLane, strategyProfile);
        }

        /// <summary>构造 Boss 行。</summary>
        private static WavePlanEntry BossRow(
            int order,
            string bossKey,
            long preDelayMs,
            long spawnIntervalMs,
            long postDelayMs,
            bool playerLane = true,
            bool opponentLane = false,
            int difficultyIndex = 0)
        {
            return new WavePlanEntry(
                "test", order, WavePlanKind.Boss, "", 0, difficultyIndex, bossKey,
                preDelayMs, spawnIntervalMs, postDelayMs, playerLane, opponentLane, 0);
        }

        // ====================================================================
        // 输入合法性
        // ====================================================================

        [Test]
        [Description("Update 的 stepMs 为负时显式失败。")]
        public void Update_NegativeStep_Throws()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 1, 0, 0, 0)), spawner.Spawn, new UnavailableBossWavePort());

            Assert.Throws<ArgumentOutOfRangeException>(() => wm.Update(-1));
        }

        [Test]
        [Description("新构造任一注入依赖为 null 抛 ArgumentNullException。")]
        public void Constructor_NullDependencies_Throws()
        {
            OrderedWavePlanSnapshot plan = MakePlan(NormalRow(1, 1, 0, 0, 0));
            var spawner = new RecordingNormalSpawner();

            Assert.Throws<ArgumentNullException>(() => new WaveManager(null, spawner.Spawn, new UnavailableBossWavePort()));
            Assert.Throws<ArgumentNullException>(() => new WaveManager(plan, null, new UnavailableBossWavePort()));
            Assert.Throws<ArgumentNullException>(() => new WaveManager(plan, spawner.Spawn, null));
        }

        // ====================================================================
        // 单路 / 双路 cardinality 与顺序
        // ====================================================================

        [Test]
        [Description("单玩家路：normalCount 按该车道计数，全部为玩家路。")]
        public void SinglePlayerLane_NormalCount_SpawnsPerLaneCount()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 2, 0, 100, 0, playerLane: true, opponentLane: false)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(1);   // ordinal 0
            wm.Update(100); // ordinal 1 → WaitingForClear

            Assert.AreEqual(2, spawner.Requests.Count, "单玩家路 normalCount=2 应出生 2 只。");
            foreach (NormalWaveSpawnRequest request in spawner.Requests)
            {
                Assert.IsTrue(request.IsPlayerLane, "单玩家路全部走玩家路。");
                Assert.AreEqual(1, request.WaveOrder, "waveOrder 应为行 order=1。");
            }

            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State, "最后出生后转 WaitingForClear。");
            Assert.AreEqual(2, wm.ActiveHandleCount, "两只活动 handle 已登记。");
        }

        [Test]
        [Description("双路：同序号先玩家路、后电脑路；normalCount 是每个启用车道数量。")]
        public void DoubleLane_SameOrdinal_PlayerFirstThenOpponent()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 2, 0, 0, 0, playerLane: true, opponentLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(1); // ordinal 0（玩家、电脑）→ ordinal 1（玩家、电脑），interval=0 同更新全提交

            bool[] expectedLaneOrder = { true, false, true, false };
            Assert.AreEqual(4, spawner.Requests.Count, "normalCount=2 双路共 4 只。");
            for (int i = 0; i < expectedLaneOrder.Length; i++)
            {
                Assert.AreEqual(
                    expectedLaneOrder[i], spawner.Requests[i].IsPlayerLane,
                    $"第 {i + 1} 次出生应 {expectedLaneOrder[i]}。");
            }

            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State, "全部出生后转 WaitingForClear。");
        }

        [Test]
        [Description("单电脑路：normalCount 按电脑路计数。")]
        public void SingleOpponentLane_NormalCount_SpawnsPerLaneCount()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 3, 0, 0, 0, playerLane: false, opponentLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(1);

            Assert.AreEqual(3, spawner.Requests.Count, "单电脑路 normalCount=3 应出生 3 只。");
            foreach (NormalWaveSpawnRequest request in spawner.Requests)
            {
                Assert.IsFalse(request.IsPlayerLane, "单电脑路全部走电脑路。");
            }
        }

        // ====================================================================
        // preDelay / spawnInterval / postDelay 边界
        // ====================================================================

        [Test]
        [Description("preDelay=100：首次出生在 preDelay 达到的那次 Update 提交。")]
        public void PreDelay_ReachedUpdate_CommitsFirstSpawn()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 1, 100, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(50);
            Assert.AreEqual(0, spawner.Requests.Count, "preDelay 未到不出生。");
            Assert.AreEqual(WaveRuntimeState.PreDelay, wm.State);

            wm.Update(50); // 累计 100 → 首次出生
            Assert.AreEqual(1, spawner.Requests.Count, "preDelay=100 达到时首次出生。");
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State);
        }

        [Test]
        [Description("preDelay=0：进入行的该次 Update 即可出生。")]
        public void PreDelay_Zero_SpawnsOnEnteringUpdate()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(1);

            Assert.AreEqual(1, spawner.Requests.Count, "preDelay=0 进入行即出生。");
        }

        [Test]
        [Description("spawnInterval：后续出生序号按间隔推进。")]
        public void SpawnInterval_AdvancesOrdinalPerStep()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 3, 0, 100, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(50);   // ordinal 0
            Assert.AreEqual(1, spawner.Requests.Count);

            wm.Update(50);   // 累计间隔 100 → ordinal 1
            Assert.AreEqual(2, spawner.Requests.Count);

            wm.Update(100);  // ordinal 2 → WaitingForClear
            Assert.AreEqual(3, spawner.Requests.Count);
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State);
            Assert.AreEqual(3, wm.ActiveHandleCount, "三只活动 handle 登记。");
        }

        [Test]
        [Description("大步长跨过多个间隔：同一次 Update 可提交多个出生序号（确定性推进）。")]
        public void SpawnInterval_LargeStep_CommitsMultipleOrdinals()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 3, 0, 100, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(250); // preDelay 0：ordinal0 + 剩余 250 内 ordinal1、ordinal2 均到期

            Assert.AreEqual(3, spawner.Requests.Count, "250ms 覆盖 3 个 100ms 序号。");
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State);
        }

        [Test]
        [Description("postDelay：最后出生后需等 postDelay 到期且 handle 清空才完成。")]
        public void PostDelay_ElapsedAfterLastSpawn_ThenRemovalCompletes()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 1, 0, 0, 100, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(1);   // 出生 → WaitingForClear（postDelay 100）
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State);

            wm.Update(100); // postDelay 到期但 handle 未清 → 不完成
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State, "postDelay 到但 handle 未清不完成。");

            wm.OnEntityRemoved(spawner.Handles[0]);
            wm.Update(1);   // 最后 handle 移除后下一 Update 完成
            Assert.AreEqual(WaveRuntimeState.Completed, wm.State, "handle 清空后下一 Update 完成。");
            Assert.AreEqual(1, wm.CurrentOrder, "完成行 order=1。");
        }

        // ====================================================================
        // 零延迟不跨行
        // ====================================================================

        [Test]
        [Description("零延迟也不可一子步穿越多行：Completed 后下一行最早下一次 Update 开始。")]
        public void ZeroDelay_DoesNotCrossRowsInSameUpdate()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(
                    NormalRow(1, 1, 0, 0, 0, playerLane: true),
                    NormalRow(2, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            // 第一次 Update：进入行 1 并出生 → WaitingForClear
            wm.Update(1);
            Assert.AreEqual(1, spawner.Requests.Count, "行 1 首次出生。");
            Assert.AreEqual(1, wm.CurrentOrder);
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State);

            // 移除行 1 handle；下一次 Update 只完成行 1，不得进入行 2
            wm.OnEntityRemoved(spawner.Handles[0]);
            wm.Update(1);
            Assert.AreEqual(WaveRuntimeState.Completed, wm.State, "行 1 完成。");
            Assert.AreEqual(1, wm.CurrentOrder, "同一 Update 完成行 1 后不得进入行 2。");
            Assert.AreEqual(1, spawner.Requests.Count, "行 2 不得在同一 Update 出生。");

            // 下一次 Update 才进入行 2
            wm.Update(1);
            Assert.AreEqual(2, wm.CurrentOrder, "下一行最早下一次 Update 开始。");
            Assert.AreEqual(2, spawner.Requests.Count, "行 2 首次出生。");
        }

        // ====================================================================
        // Normal → Boss → Normal（任意 order fake port）
        // ====================================================================

        [Test]
        [Description("Normal(order=1) → Boss(order=2) → Normal(order=3) 严格按配置 order 推进。")]
        public void NormalBossNormal_ProgressionInConfiguredOrder()
        {
            var spawner = new RecordingNormalSpawner();
            var bossPort = new FakeBossWavePort();
            bossPort.SupportedKeys.Add("ZhangLiang");
            var wm = new WaveManager(
                MakePlan(
                    NormalRow(1, 1, 0, 0, 0, playerLane: true),
                    BossRow(2, "ZhangLiang", 0, 0, 0, playerLane: true, opponentLane: false),
                    NormalRow(3, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, bossPort);

            // 行 1 Normal
            wm.Update(1);
            Assert.AreEqual(1, spawner.Requests.Count);
            Assert.AreEqual(1, spawner.Requests[0].WaveOrder);
            Assert.AreEqual(0, bossPort.Requests.Count);

            // 行 2 Boss
            wm.OnEntityRemoved(spawner.Handles[0]);
            wm.Update(1); // 行 1 完成
            wm.Update(1); // 进入行 2 → Boss 出生
            Assert.AreEqual(1, bossPort.Requests.Count, "Boss 行经端口出生。");
            Assert.AreEqual("ZhangLiang", bossPort.Requests[0].BossKey);
            Assert.AreEqual(2, bossPort.Requests[0].WaveOrder);
            Assert.AreEqual(2, wm.CurrentOrder, "Boss 行 order=2 推进。");
            Assert.AreEqual(1, spawner.Requests.Count, "Boss 行不产生普通敌人（不冒充 Normal）。");

            // 行 3 Normal
            wm.OnEntityRemoved(bossPort.Handles[0]);
            wm.Update(1); // 行 2 完成
            wm.Update(1); // 进入行 3 → Normal 出生
            Assert.AreEqual(2, spawner.Requests.Count, "行 3 普通敌人出生。");
            Assert.AreEqual(3, spawner.Requests[1].WaveOrder);
            Assert.AreEqual(3, wm.CurrentOrder);
        }

        [Test]
        [Description("Boss 行可位于 order=1（任意位置插波）。")]
        public void BossFirstRow_ThenNormal_Progression()
        {
            var spawner = new RecordingNormalSpawner();
            var bossPort = new FakeBossWavePort();
            var wm = new WaveManager(
                MakePlan(
                    BossRow(1, "ZhangBao", 0, 0, 0, playerLane: true),
                    NormalRow(2, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, bossPort);

            wm.Update(1);
            Assert.AreEqual(1, bossPort.Requests.Count, "首行 Boss 出生。");
            Assert.AreEqual(1, bossPort.Requests[0].WaveOrder);
            Assert.AreEqual(0, spawner.Requests.Count);

            wm.OnEntityRemoved(bossPort.Handles[0]);
            wm.Update(1); // Boss 行完成
            wm.Update(1); // 进入 Normal 行
            Assert.AreEqual(1, spawner.Requests.Count, "随后 Normal 行出生。");
        }

        // ====================================================================
        // Boss 双路与 unavailable 显式失败
        // ====================================================================

        [Test]
        [Description("Boss 行双路各 Spawn 一次，玩家路先于电脑路。")]
        public void BossRow_DoubleLane_SpawnsOncePerEnabledLane_PlayerFirst()
        {
            var spawner = new RecordingNormalSpawner();
            var bossPort = new FakeBossWavePort();
            var wm = new WaveManager(
                MakePlan(BossRow(1, "ZhangLiang", 0, 0, 0, playerLane: true, opponentLane: true)),
                spawner.Spawn, bossPort);

            wm.Update(1);

            Assert.AreEqual(2, bossPort.Requests.Count, "Boss 双路各一次。");
            Assert.IsTrue(bossPort.Requests[0].IsPlayerLane, "玩家路先。");
            Assert.IsFalse(bossPort.Requests[1].IsPlayerLane, "电脑路后。");
            Assert.AreEqual(1, bossPort.Requests[0].WaveOrder);
            Assert.AreEqual(2, wm.ActiveHandleCount, "两只 Boss handle 登记。");
            Assert.AreEqual(0, spawner.Requests.Count, "Boss 行不产生普通敌人。");
        }

        [Test]
        [Description("Boss 行遇到 unavailable 端口显式失败，不静默跳过/降级。")]
        public void BossRow_UnavailablePort_ExplicitFailure()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(BossRow(1, "ZhangLiang", 0, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            Assert.Throws<InvalidOperationException>(() => wm.Update(1), "unavailable 端口 Spawn 显式失败。");
            Assert.AreEqual(0, wm.ActiveHandleCount, "失败后无活动 handle。");
        }

        // ====================================================================
        // 最后 spawn 不完成 / 清场后完成
        // ====================================================================

        [Test]
        [Description("最后出生后仍有存活 handle 时保持 WaitingForClear，下一行不得开始。")]
        public void LastSpawn_ActiveHandle_DoesNotCompleteRow()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(1);   // 最后一次出生
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State);

            wm.Update(1);   // postDelay=0 到期但 handle 未清
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State, "handle 未清不完成。");
            Assert.AreEqual(1, wm.CurrentOrder);
            Assert.IsFalse(wm.AllWavesCompleted, "未全部完成。");
        }

        // ====================================================================
        // 完整 handle generation 匹配与幂等移除
        // ====================================================================

        [Test]
        [Description("相同 runtimeId 不同 generation/waveOrder/kind 不匹配；重复移除幂等。")]
        public void Handle_MismatchAndDuplicateRemoval_Idempotent()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            wm.Update(1);
            WaveEntityHandle live = spawner.Handles[0]; // (id=1, gen=1, order=1, Normal)
            Assert.AreEqual(1, wm.ActiveHandleCount);

            wm.OnEntityRemoved(new WaveEntityHandle(live.RuntimeId, 2, live.WaveOrder, WaveEntityKind.Normal));
            Assert.AreEqual(1, wm.ActiveHandleCount, "generation 不同不匹配。");

            wm.OnEntityRemoved(new WaveEntityHandle(live.RuntimeId, live.Generation, 99, WaveEntityKind.Normal));
            Assert.AreEqual(1, wm.ActiveHandleCount, "waveOrder 不同不匹配。");

            wm.OnEntityRemoved(new WaveEntityHandle(live.RuntimeId, live.Generation, live.WaveOrder, WaveEntityKind.Boss));
            Assert.AreEqual(1, wm.ActiveHandleCount, "kind 不同不匹配。");

            wm.OnEntityRemoved(live);
            Assert.AreEqual(0, wm.ActiveHandleCount, "完整值匹配移除。");

            wm.OnEntityRemoved(live);
            Assert.AreEqual(0, wm.ActiveHandleCount, "重复移除幂等。");
        }

        [Test]
        [Description("FromEnemyLease 提供从 EnemyLeaseIdentity 转换 Normal handle 的清晰入口。")]
        public void FromEnemyLease_ConvertsToNormalHandle()
        {
            var lease = new EnemyLeaseIdentity(7, 3, 2);
            WaveEntityHandle handle = WaveEntityHandle.FromEnemyLease(lease);

            Assert.AreEqual(new WaveEntityHandle(7, 3, 2, WaveEntityKind.Normal), handle);
            Assert.AreEqual(WaveEntityKind.Normal, handle.Kind);
            Assert.IsTrue(handle.IsValid);

            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());
            wm.Update(1);
            Assert.AreEqual(1, wm.ActiveHandleCount);

            wm.OnEntityRemoved(new EnemyLeaseIdentity(spawner.Handles[0].RuntimeId, 1, 1));
            Assert.AreEqual(0, wm.ActiveHandleCount, "EnemyLeaseIdentity 重载幂等移除。");
        }

        // ====================================================================
        // AllConfiguredWavesCompleted 仅一次
        // ====================================================================

        [Test]
        [Description("最后一行完成后 AllConfiguredWavesCompleted 只发布一次。")]
        public void AllConfiguredWavesCompleted_FiresExactlyOnce()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(
                    NormalRow(1, 1, 0, 0, 0, playerLane: true),
                    NormalRow(2, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            int completedCalls = 0;
            wm.AllConfiguredWavesCompleted += () => completedCalls++;

            // 行 1
            wm.Update(1);
            wm.OnEntityRemoved(spawner.Handles[0]);
            wm.Update(1); // 行 1 完成
            Assert.AreEqual(0, completedCalls, "非最后一行不发布。");

            // 行 2
            wm.Update(1);
            wm.OnEntityRemoved(spawner.Handles[1]);
            wm.Update(1); // 行 2 完成（最后一行）→ 发布一次
            Assert.AreEqual(1, completedCalls, "最后一行完成发布一次。");
            Assert.IsTrue(wm.AllWavesCompleted, "AllWavesCompleted=true。");

            // 后续 Update / 迟到移除不再发布
            wm.Update(1);
            wm.OnEntityRemoved(spawner.Handles[0]);
            wm.OnEntityRemoved(spawner.Handles[1]);
            Assert.AreEqual(1, completedCalls, "仅发布一次。");
        }

        // ====================================================================
        // Stop during spawning
        // ====================================================================

        [Test]
        [Description("Spawning 中 Stop：不再出生/完成，Boss port Stop 幂等。")]
        public void StopDuringSpawning_NoMoreSpawnsNoCompletion_BossPortStopIdempotent()
        {
            var spawner = new RecordingNormalSpawner();
            var bossPort = new FakeBossWavePort();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 5, 0, 100, 0, playerLane: true)),
                spawner.Spawn, bossPort);

            int completedCalls = 0;
            wm.AllConfiguredWavesCompleted += () => completedCalls++;

            wm.Update(1);   // ordinal 0
            wm.Update(1);
            wm.Update(1);
            Assert.AreEqual(1, spawner.Requests.Count, "仅首个序号出生。");
            Assert.AreEqual(1, wm.ActiveHandleCount);

            wm.Stop();
            Assert.IsTrue(wm.IsStopped, "Stop 后 IsStopped=true。");
            Assert.AreEqual(0, wm.ActiveHandleCount, "Stop 清空所有权。");
            Assert.AreEqual(1, bossPort.StopCalls, "调用 Boss port Stop 一次。");

            wm.Update(1000); // 停止后 Update 为空操作
            wm.Update(1);
            Assert.AreEqual(1, spawner.Requests.Count, "停止后不再出生。");
            Assert.IsFalse(wm.AllWavesCompleted, "停止后不完成。");
            Assert.AreEqual(0, completedCalls, "停止后不发布完成。");

            wm.Stop();       // 重复 Stop 幂等且不抛
            Assert.AreEqual(2, bossPort.StopCalls, "Boss port Stop 幂等（重复调用安全）。");
            Assert.AreEqual(WaveRuntimeState.Pending, wm.State, "停止后状态复位为 Pending。");
        }

        // ====================================================================
        // Stop 后 Forced 移除 / Stop+Cleanup（task 4.9 顺序契约）
        // ====================================================================

        [Test]
        [Description("task 4.9：有活 handle 时先 Stop，再模拟 Forced OnEntityRemoved 并多次 Update，" +
                     "不得完成/发布 AllConfiguredWavesCompleted（Forced 移除事实不促成胜利）。")]
        public void StopWithActiveHandles_ForcedRemovalAndUpdates_NoCompletionNoAllCompleted()
        {
            var spawner = new RecordingNormalSpawner();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 1, 0, 0, 0, playerLane: true)),
                spawner.Spawn, new UnavailableBossWavePort());

            int completedCalls = 0;
            wm.AllConfiguredWavesCompleted += () => completedCalls++;

            wm.Update(1);   // 出生 → WaitingForClear，handle 活动
            Assert.AreEqual(1, wm.ActiveHandleCount, "出生后有活动 handle。");
            Assert.AreEqual(WaveRuntimeState.WaitingForClear, wm.State);

            // EnemyManager.GameOver 之前的 Stop：置 stopped、清空活动 handle（顺序契约前置）。
            wm.Stop();
            Assert.IsTrue(wm.IsStopped, "Stop 后 IsStopped=true。");
            Assert.AreEqual(0, wm.ActiveHandleCount, "Stop 清空活动 handle。");

            // 模拟 EnemyManager 批量 Forced 移除事实（停止后为幂等空操作）。
            wm.OnEntityRemoved(spawner.Handles[0]);

            // 迟到 Update：postDelay 早已到期，但已停止，不得完成/发布。
            for (int i = 0; i < 5; i++)
            {
                wm.Update(1000);
            }

            Assert.AreEqual(1, spawner.Requests.Count, "停止后不再出生。");
            Assert.IsFalse(wm.AllWavesCompleted, "停止后不完成。");
            Assert.AreEqual(0, completedCalls, "停止后不发布 AllConfiguredWavesCompleted。");
        }

        [Test]
        [Description("task 4.9：Stop+Cleanup（含重复调用）后无出生/完成、活动 handle 清空，" +
                     "FakeBossPort Stop/Cleanup 每次安全调用且幂等。")]
        public void StopThenCleanup_Repeated_NoSpawnNoComplete_HandlesCleared_PortCallsSafe()
        {
            var spawner = new RecordingNormalSpawner();
            var bossPort = new FakeBossWavePort();
            var wm = new WaveManager(
                MakePlan(NormalRow(1, 3, 0, 100, 0, playerLane: true)),
                spawner.Spawn, bossPort);

            int completedCalls = 0;
            wm.AllConfiguredWavesCompleted += () => completedCalls++;

            wm.Update(1);   // ordinal 0
            wm.Update(100); // ordinal 1
            Assert.AreEqual(2, wm.ActiveHandleCount, "两只有活动 handle。");
            Assert.AreEqual(2, spawner.Requests.Count);

            wm.Stop();
            Assert.IsTrue(wm.IsStopped, "Stop 置 stopped。");
            Assert.AreEqual(0, wm.ActiveHandleCount, "Stop 清空活动 handle。");
            Assert.AreEqual(1, bossPort.StopCalls, "Stop 调用 Boss 端口 Stop 一次。");

            wm.Cleanup();
            Assert.IsTrue(wm.IsStopped, "Cleanup 保持 stopped（防重启）。");
            Assert.AreEqual(0, wm.ActiveHandleCount, "Cleanup 清空活动 handle。");
            Assert.AreEqual(1, bossPort.CleanupCalls, "Cleanup 调用 Boss 端口 Cleanup 一次。");

            // 重复调用幂等且安全：不抛、状态不变、端口 Stop/Cleanup 每次安全调用。
            wm.Stop();
            wm.Cleanup();
            wm.Stop();
            wm.Cleanup();
            Assert.AreEqual(3, bossPort.StopCalls, "重复 Stop 每次安全调用端口 Stop；Cleanup 不越权调用 Stop。");
            Assert.AreEqual(3, bossPort.CleanupCalls, "重复 Cleanup 每次安全调用端口 Cleanup。");

            // 清理后迟到 Forced 移除 + Update 无出生/无完成/无发布。
            wm.OnEntityRemoved(spawner.Handles[0]);
            wm.Update(1000);
            wm.Update(1000);
            Assert.AreEqual(2, spawner.Requests.Count, "清理后不再出生。");
            Assert.IsFalse(wm.AllWavesCompleted, "清理后不完成。");
            Assert.AreEqual(0, completedCalls, "清理后不发布 AllConfiguredWavesCompleted。");
        }
    }
}
