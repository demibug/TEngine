using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Battle
{
    // ============================================================================
    // 任务 3.10 + 任务 4.6-4.8：BattleManager 单元测试（协调器角色）
    // ----------------------------------------------------------------------------
    // 验证要求（task 3.10 / task 4.6-4.8）：
    //   1. 职责受限：不实现 IUpdateModule/时间拆步/Scene/UI/资源。
    //   2. 只保留战斗启动/终止、目标生命完成事实与结果冻结协调；不再生成敌人。
    //   3. StartGame 只负责战斗开始协调（MaxRounds 从计划行数派生、CurrentRound=0），
    //      不启动第二状态机（WaveManager 在 WaveSpawn phase 的首次 Update 自动进入首行）。
    //   4. 每行开始经 WaveStarted(order) 同步 CurrentRound 到真实 WaveManager.CurrentOrder。
    //   5. AllConfiguredWavesCompleted 是唯一成功闸：收到后调用单一成功协调入口 → TryFreeze(true)。
    //   6. 玩家侧生命归零立即 TryFreeze(false)；对手侧归零只保留状态，不直接成功。
    //   7. TryFreeze/ResultBuilder 幂等：失败已冻结时迟到的完成不能覆盖。
    //   8. GameOver 重置状态并先停止 WaveManager。
    //
    // spec ordered-wave-plan "AllConfiguredWavesCompleted is the only success gate"：
    //   - 最后 spawn 不判胜；最后 handle 移除 + postDelay 后仅一次胜利。
    //   - 玩家生命归零可以立即冻结失败，不得等待剩余配置波。
    //
    // 本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // ============================================================================

    /// <summary>
    /// BattleManager 职责受限、协调角色与胜负闸测试（task 3.10 / 4.6-4.8）。
    /// </summary>
    [TestFixture]
    internal class BattleManagerTests
    {
        // ====================================================================
        // 测试替身
        // ====================================================================

        /// <summary>记录式普通敌人出生替身：返回递增 runtimeId 的 Normal handle。</summary>
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
            bool opponentLane = false)
        {
            return new WavePlanEntry(
                "test", order, WavePlanKind.Normal, "Mob0", normalCount, 0, "",
                preDelayMs, spawnIntervalMs, postDelayMs, playerLane, opponentLane, 0);
        }

        /// <summary>构造最小合法配置快照（BattleManager 只需 Economy/legacy 段非空 + 有序计划）。</summary>
        private static BattleConfigSnapshot CreateTestSnapshot(
            OrderedWavePlanSnapshot plan,
            EnemyConfigSnapshot legacyEnemy = null,
            WaveConfigSnapshot legacyWave = null)
        {
            MapData map = MapData.FromColumnMajorGrid(
                columnMajorGrid: new IReadOnlyList<string>[]
                {
                    new[] { "0_1", "0_1" },
                    new[] { "0_1", "0_1" },
                },
                cellDecoder: BattleConfigNormalizer.DecodeCell,
                mapIndex: 0,
                playerStart: new GridPosition(0, 1),
                playerEnd: new GridPosition(1, 1),
                opponentStart: new GridPosition(1, 0),
                opponentEnd: new GridPosition(0, 0),
                playerPath: new[] { new GridPosition(0, 1), new GridPosition(1, 1) },
                opponentPath: new[] { new GridPosition(1, 0), new GridPosition(0, 0) });

            return new BattleConfigSnapshot(
                map: map,
                enemy: legacyEnemy ?? new EnemyConfigSnapshot(
                    "Mob0", 0, 50, new[] { 10 }, new float[] { 1f }, 1),
                wave: legacyWave ?? new WaveConfigSnapshot(
                    waveUnitCounts: new[] { 1 }, bossWaveNumbers: Array.Empty<int>(),
                    bossSpawnChances: Array.Empty<float>(), spawnStrategyWeights: new[] { 1 },
                    spawnStrategies: new IReadOnlyList<float>[] { new float[] { 1f } },
                    skipBoss: true, delayTimeMs: 100, maxRounds: 1),
                units: Array.Empty<UnitConfigSnapshot>(),
                unitLevel: new UnitLevelConfigSnapshot(3, new[] { 1f, 1.5f, 2f }, new[] { 1f, 1.2f, 1.5f }),
                economy: new EconomyConfigSnapshot(20, 10, 2, 1, 5, 3, 3),
                deck: new DeckConfigSnapshot(true, new[] { "刀", "弓", "枪", "骑" }, 5, 1, 1),
                projectile: new ProjectileConfigSnapshot(new[] { "SimpleDynamicArrow" }, "SimpleDynamicArrow",
                    "TargetEnemyBezierMovement", "HitEnemyStrategy"),
                missingFieldNotes: Array.Empty<string>(),
                sourceTag: "Test",
                orderedWavePlan: plan);
        }

        /// <summary>
        /// 以新链组装 BattleManager 及依赖（有序计划 + 记录式出生替身）。
        /// </summary>
        private static (BattleManager manager, BattleState state, WaveManager waveManager,
            RecordingNormalSpawner spawner, BattleResultBuilder resultBuilder)
            CreateManager(params WavePlanEntry[] rows)
        {
            OrderedWavePlanSnapshot plan = MakePlan(rows);
            BattleConfigSnapshot snapshot = CreateTestSnapshot(plan);
            BattleState state = new BattleState();
            var spawner = new RecordingNormalSpawner();
            WaveManager waveManager = new WaveManager(
                plan, spawner.Spawn, UnavailableBossWavePort.Instance);
            BattleEconomy economy = new BattleEconomy(state, refreshCostIncrement: 2);
            BattleReadModel readModel = new BattleReadModel(state, new RuntimeIdAllocator());
            BattleResultBuilder resultBuilder = new BattleResultBuilder(readModel);

            BattleManager manager = new BattleManager(
                snapshot, state, waveManager, economy, resultBuilder);
            return (manager, state, waveManager, spawner, resultBuilder);
        }

        // ====================================================================
        // 职责受限测试
        // ====================================================================

        [Test]
        [Description("BattleManager 不实现 TEngine IUpdateModule（不拥有框架 OnUpdate）。")]
        public void DoesNotImplement_IUpdateModule()
        {
            (BattleManager manager, _, _, _, _) = CreateManager(NormalRow(1, 1, 0, 0, 0));

            Type managerType = manager.GetType();
            Type iupdateModuleType = Type.GetType("TEngine.IUpdateModule, TEngine");
            if (iupdateModuleType != null)
            {
                Assert.IsFalse(
                    iupdateModuleType.IsAssignableFrom(managerType),
                    "BattleManager 不得实现 TEngine.IUpdateModule。");
            }

            Assert.IsNull(
                managerType.GetMethod("OnUpdate"),
                "BattleManager 不得暴露 OnUpdate 方法。");
        }

        [Test]
        [Description("BattleManager 不再拥有波次生成状态机：不暴露旧 UpdateSpawnState/UnitsThisWave/固定 typeIndex。")]
        public void DoesNotOwn_OldSpawnStateMachine()
        {
            (BattleManager manager, _, _, _, _) = CreateManager(NormalRow(1, 1, 0, 0, 0));

            Type managerType = manager.GetType();
            Assert.IsNull(managerType.GetMethod("UpdateSpawnState"),
                "旧生产 spawn state 状态机入口已移除（WaveSpawn phase 唯一调用 WaveManager）。");
            Assert.IsNull(managerType.GetMethod("SpawnPairWhenDue"),
                "旧 SpawnPairWhenDue 已移除。");
            Assert.IsNull(managerType.GetMethod("BeginWave"),
                "旧 BeginWave 已移除。");
            Assert.IsNull(managerType.GetProperty("UnitsThisWave"),
                "旧 unitsThisWave 计数已移除。");
            Assert.IsNull(managerType.GetProperty("SpawnIndex"),
                "旧 spawnIndex 计数已移除。");
        }

        [Test]
        [Description("BattleManager 不拥有时间拆步：不暴露 Advance/ClampFrameDelta。")]
        public void DoesNotOwn_TimeSplitting()
        {
            (BattleManager manager, _, _, _, _) = CreateManager(NormalRow(1, 1, 0, 0, 0));

            Type managerType = manager.GetType();
            Assert.IsFalse(managerType.GetMethod("Advance") != null,
                "BattleManager 不应暴露 Advance 方法（时间推进由 BattleSimulation 负责）。");
            Assert.IsFalse(managerType.GetMethod("ClampFrameDelta") != null,
                "BattleManager 不应暴露截断方法。");
        }

        // ====================================================================
        // 规则状态测试（任务 4.6/4.8）
        // ====================================================================

        [Test]
        [Description("StartGame 发放双方初始金币、MaxRounds 从计划行数派生、CurrentRound=0。")]
        public void StartGame_GrantsGold_SetsDerivedMaxRounds()
        {
            (BattleManager manager, BattleState state, _, _, _) =
                CreateManager(NormalRow(1, 1, 0, 0, 0), NormalRow(2, 1, 0, 0, 0), NormalRow(3, 1, 0, 0, 0));

            manager.StartGame(startNowMs: 1000);

            Assert.AreEqual(BattleState.DefaultInitialGold, state.PlayerGold, "玩家方应获得初始金币。");
            Assert.AreEqual(BattleState.DefaultInitialGold, state.OpponentGold, "对手方应获得初始金币。");
            Assert.AreEqual(3, state.MaxRounds, "MaxRounds 应从计划行数（plan.Rows.Count=3）派生。");
            Assert.AreEqual(0, state.CurrentRound, "StartGame 后 CurrentRound=0。");
            Assert.AreEqual(1000, state.StartTimeMs, "开始时间戳应设置。");
            Assert.IsTrue(manager.IsStarted, "IsStarted 应为 true。");
        }

        [Test]
        [Description("冲突 legacy Enemy/Wave 数组、Boss 概率、skipBoss 与 MaxRounds 不改变逐行出生、推进和胜利；MaxRounds 只从计划行数派生。")]
        public void ConflictingLegacyFields_DoNotChangeOrderedPlanBehavior()
        {
            OrderedWavePlanSnapshot plan = MakePlan(
                NormalRow(1, 1, 0, 0, 0, playerLane: true),
                NormalRow(2, 1, 0, 0, 0, playerLane: true));
            var legacyEnemy = new EnemyConfigSnapshot(
                string.Empty, 99, -10, Array.Empty<int>(), Array.Empty<float>(), -1);
            var legacyWave = new WaveConfigSnapshot(
                waveUnitCounts: new[] { 999 },
                bossWaveNumbers: new[] { 1, 2 },
                bossSpawnChances: new[] { -1f, 2f },
                spawnStrategyWeights: new[] { -5, 0 },
                spawnStrategies: Array.Empty<IReadOnlyList<float>>(),
                skipBoss: false,
                delayTimeMs: -100,
                maxRounds: 999);
            BattleConfigSnapshot snapshot = CreateTestSnapshot(plan, legacyEnemy, legacyWave);
            BattleState state = new BattleState();
            var spawner = new RecordingNormalSpawner();
            WaveManager waveManager = new WaveManager(
                plan, spawner.Spawn, UnavailableBossWavePort.Instance);
            BattleReadModel readModel = new BattleReadModel(state, new RuntimeIdAllocator());
            var resultBuilder = new BattleResultBuilder(readModel);
            var manager = new BattleManager(
                snapshot,
                state,
                waveManager,
                new BattleEconomy(state, refreshCostIncrement: 2),
                resultBuilder);

            manager.StartGame(0);
            Assert.AreEqual(2, state.MaxRounds, "显示/统计轮数必须来自 plan.Rows.Count，而非 legacy MaxRounds=999。");

            waveManager.Update(1);
            Assert.AreEqual(1, spawner.Requests.Count, "第 1 行只按计划出生 1 个，不读 waveUnitCounts=999。");
            Assert.AreEqual(1, spawner.Requests[0].WaveOrder);
            waveManager.OnEntityRemoved(spawner.Handles[0]);
            waveManager.Update(1);
            waveManager.Update(1);

            Assert.AreEqual(2, spawner.Requests.Count, "第 2 行按显式顺序出生，不受 Boss 数组/概率/skipBoss 影响。");
            Assert.AreEqual(2, spawner.Requests[1].WaveOrder);
            waveManager.OnEntityRemoved(spawner.Handles[1]);
            waveManager.Update(1);

            Assert.IsTrue(waveManager.AllWavesCompleted);
            Assert.IsTrue(resultBuilder.IsFrozen);
            Assert.IsTrue(resultBuilder.FrozenResult.Value.IsWin);
            Assert.AreEqual(2, resultBuilder.FrozenResult.Value.Round);
        }

        [Test]
        [Description("StartGame 不启动第二状态机：新链 WaveManager 保持 Pending，首次 Update 才进入首行。")]
        public void StartGame_DoesNotStartSecondWaveStateMachine()
        {
            (BattleManager manager, BattleState state, WaveManager waveManager, _, _) =
                CreateManager(NormalRow(1, 1, 0, 0, 0));

            manager.StartGame(startNowMs: 0);

            // 不调用任何 Update：WaveManager 新链尚未进入任何行（自动在 WaveSpawn phase 首次 Update 启动）。
            Assert.AreEqual(WaveRuntimeState.Pending, waveManager.State, "新链状态机未被 StartGame 启动。");
            Assert.AreEqual(0, waveManager.ActiveHandleCount, "StartGame 不生成任何敌人。");
            Assert.AreEqual(0, state.CurrentRound, "StartGame 后 CurrentRound=0。");

            // 首次 Update（等效 WaveSpawn phase）才进入首行并出生。
            waveManager.Update(1);
            Assert.AreEqual(1, waveManager.CurrentOrder, "首次 Update 进入首行。");
            Assert.AreEqual(1, state.CurrentRound, "WaveStarted(order=1) 同步 CurrentRound。");
        }

        [Test]
        [Description("WaveStarted(order) 逐行同步 CurrentRound 到真实 WaveManager.CurrentOrder（任务 4.8）。")]
        public void WaveStarted_SyncsCurrentRound_ToRealOrder()
        {
            (BattleManager manager, BattleState state, WaveManager waveManager,
                RecordingNormalSpawner spawner, _) =
                CreateManager(
                    NormalRow(1, 1, 0, 0, 0, playerLane: true),
                    NormalRow(2, 1, 0, 0, 0, playerLane: true));

            manager.StartGame(0);

            // 行 1
            waveManager.Update(1);
            Assert.AreEqual(1, state.CurrentRound, "行 1 开始时同步为 1。");

            // 行 1 清场完成 → 下一 Update 开始行 2
            waveManager.OnEntityRemoved(spawner.Handles[0]);
            waveManager.Update(1); // 行 1 完成
            Assert.AreEqual(1, state.CurrentRound, "完成行不改变 CurrentRound（仍为真实 order=1）。");
            waveManager.Update(1); // 进入行 2
            Assert.AreEqual(2, state.CurrentRound, "行 2 开始时同步为 2。");
        }

        // ====================================================================
        // 唯一成功闸（任务 4.7）
        // ====================================================================

        [Test]
        [Description("最后 spawn 不判胜；最后 handle 移除 + postDelay 后仅一次胜利（AllConfiguredWavesCompleted → TryFreeze(true)）。")]
        public void AllConfiguredWavesCompleted_FreezesWinOnce_OnlyAfterClear()
        {
            (BattleManager manager, _, WaveManager waveManager,
                RecordingNormalSpawner spawner, BattleResultBuilder resultBuilder) =
                CreateManager(NormalRow(1, 1, 0, 0, 100, playerLane: true));

            manager.StartGame(0);

            // 最后出生：不判胜。
            waveManager.Update(1);
            Assert.IsFalse(resultBuilder.IsFrozen, "最后出生不判胜（最后 spawn 不是完成）。");

            // postDelay 到期但 handle 未清：不判胜。
            waveManager.Update(100);
            Assert.IsFalse(resultBuilder.IsFrozen, "postDelay 到但活动 handle 未清不判胜。");

            // 最后 handle 移除 + 后续 Update 完成 → 仅一次胜利。
            waveManager.OnEntityRemoved(spawner.Handles[0]);
            waveManager.Update(1);
            Assert.IsTrue(resultBuilder.IsFrozen, "最后 handle 移除后完成 → 冻结一次。");
            Assert.IsTrue(resultBuilder.FrozenResult.HasValue, "FrozenResult 有值。");
            Assert.IsTrue(resultBuilder.FrozenResult.Value.IsWin, "全部配置波清场 → 玩家胜利。");
            Assert.AreEqual(1, resultBuilder.FrozenResult.Value.Round, "冻结结果保留真实最后 order=1。");

            // 幂等：再次完成事实不覆盖。
            Assert.IsFalse(manager.TryFreezeResult(playerWin: false), "已冻结后 TryFreeze 返回 false。");
        }

        [Test]
        [Description("玩家中途死亡立即失败，之后 AllConfiguredWavesCompleted 迟到完成不覆盖失败结果。")]
        public void PlayerDeathFreezesLoss_LateCompletionDoesNotOverride()
        {
            (BattleManager manager, BattleState state, WaveManager waveManager,
                RecordingNormalSpawner spawner, BattleResultBuilder resultBuilder) =
                CreateManager(
                    NormalRow(1, 1, 0, 0, 0, playerLane: true),
                    NormalRow(2, 1, 0, 0, 0, playerLane: true));

            manager.StartGame(0);

            // 玩家生命归零：立即失败（不等待剩余波次）。
            while (state.PlayerHealth > 0)
            {
                state.ApplyDamage(isPlayerSide: true, damage: 1);
            }

            manager.CheckHealthFreeze(isPlayerSide: true);
            Assert.IsTrue(resultBuilder.IsFrozen, "玩家生命归零立即冻结。");
            Assert.IsFalse(resultBuilder.FrozenResult.Value.IsWin, "玩家失败。");

            // 之后全部配置波完成：迟到成功不能覆盖失败。
            waveManager.Update(1);       // 行 1 出生
            waveManager.OnEntityRemoved(spawner.Handles[0]);
            waveManager.Update(1);       // 行 1 完成
            waveManager.Update(1);       // 行 2 出生
            waveManager.OnEntityRemoved(spawner.Handles[1]);
            waveManager.Update(1);       // 行 2 完成 → AllConfiguredWavesCompleted
            Assert.IsTrue(waveManager.AllWavesCompleted, "波次状态机全部完成。");
            Assert.IsTrue(resultBuilder.IsFrozen, "结果仍保持已冻结。");
            Assert.IsFalse(resultBuilder.FrozenResult.Value.IsWin,
                "迟到完成不覆盖已冻结的失败结果（幂等，第一个完成事实胜出）。");
        }

        [Test]
        [Description("对手生命归零只保留状态，不直接成功（成功必须等 AllConfiguredWavesCompleted）。")]
        public void OpponentDeath_DoesNotFreezeWin_Early()
        {
            (BattleManager manager, BattleState state, WaveManager waveManager,
                RecordingNormalSpawner spawner, BattleResultBuilder resultBuilder) =
                CreateManager(NormalRow(1, 1, 0, 0, 0, playerLane: true));

            manager.StartGame(0);

            // 对手生命归零：只保留状态（ApplyDamage 已归零），不直接冻结成功。
            while (state.OpponentHealth > 0)
            {
                state.ApplyDamage(isPlayerSide: false, damage: 1);
            }

            Assert.AreEqual(0, state.OpponentHealth, "对手生命归零（状态保留）。");
            manager.CheckHealthFreeze(isPlayerSide: false);
            Assert.IsFalse(resultBuilder.IsFrozen, "对手归零不直接成功（必须等全部配置波清场）。");

            // 清场后经唯一成功闸冻结胜利。
            waveManager.Update(1);
            waveManager.OnEntityRemoved(spawner.Handles[0]);
            waveManager.Update(1);
            Assert.IsTrue(resultBuilder.IsFrozen, "全部配置波清场后才胜利。");
            Assert.IsTrue(resultBuilder.FrozenResult.Value.IsWin, "胜利。");
        }

        // ====================================================================
        // CheckHealthFreeze（任务 4.7）
        // ====================================================================

        [Test]
        [Description("CheckHealthFreeze 玩家侧生命归零立即 TryFreeze(false)。")]
        public void CheckHealthFreeze_PlayerZero_FreezesLoss()
        {
            (BattleManager manager, BattleState state, _, _, BattleResultBuilder resultBuilder) =
                CreateManager(NormalRow(1, 1, 0, 0, 0));

            manager.StartGame(0);
            while (state.PlayerHealth > 0)
            {
                state.ApplyDamage(isPlayerSide: true, damage: 1);
            }

            manager.CheckHealthFreeze(isPlayerSide: true);

            Assert.IsTrue(resultBuilder.IsFrozen, "玩家归零立即冻结。");
            Assert.IsFalse(resultBuilder.FrozenResult.Value.IsWin, "玩家失败。");
        }

        [Test]
        [Description("CheckHealthFreeze 对手侧生命归零不冻结（成功必须等 AllConfiguredWavesCompleted）。")]
        public void CheckHealthFreeze_OpponentZero_DoesNotFreeze()
        {
            (BattleManager manager, BattleState state, _, _, BattleResultBuilder resultBuilder) =
                CreateManager(NormalRow(1, 1, 0, 0, 0));

            manager.StartGame(0);
            while (state.OpponentHealth > 0)
            {
                state.ApplyDamage(isPlayerSide: false, damage: 1);
            }

            manager.CheckHealthFreeze(isPlayerSide: false);

            Assert.IsFalse(resultBuilder.IsFrozen, "对手归零不直接冻结成功。");
        }

        [Test]
        [Description("CheckHealthFreeze 生命未归零时为空操作。")]
        public void CheckHealthFreeze_HealthPositive_DoesNotFreeze()
        {
            (BattleManager manager, BattleState state, _, _, BattleResultBuilder resultBuilder) =
                CreateManager(NormalRow(1, 1, 0, 0, 0));

            manager.StartGame(0);
            int playerHealthBefore = state.PlayerHealth;
            manager.CheckHealthFreeze(isPlayerSide: true);

            Assert.IsFalse(resultBuilder.IsFrozen, "生命未归零不冻结。");
            Assert.AreEqual(playerHealthBefore, state.PlayerHealth, "不修改状态。");
        }

        // ====================================================================
        // GameOver（任务 4.6）
        // ====================================================================

        [Test]
        [Description("GameOver 重置规则状态并先停止 WaveManager（阻止清理/迟到事实生成新波或误判胜利）。")]
        public void GameOver_ResetsState_AndStopsWaveManager()
        {
            (BattleManager manager, BattleState state, WaveManager waveManager,
                RecordingNormalSpawner spawner, _) =
                CreateManager(NormalRow(1, 1, 0, 0, 0));

            manager.StartGame(0);
            waveManager.Update(1);   // 进入行 1 并出生
            Assert.AreEqual(1, waveManager.ActiveHandleCount, "前置：有活动 handle。");
            Assert.IsTrue(manager.IsStarted, "已开始。");

            manager.GameOver();

            Assert.IsFalse(manager.IsStarted, "GameOver 后 IsStarted=false。");
            Assert.IsTrue(waveManager.IsStopped, "WaveManager 已停止（先停止再清所有权）。");
            Assert.AreEqual(0, waveManager.ActiveHandleCount, "停止后所有权清空。");
            Assert.AreEqual(0, state.CurrentRound, "波次号重置为 0（BattleState.ApplyGameOver）。");

            // 停止后 Update 为空操作：不再出生/完成。
            waveManager.Update(1000);
            Assert.AreEqual(1, spawner.Requests.Count, "停止后不再出生。");
        }

        // ====================================================================
        // 结果冻结幂等（决策 0.4）
        // ====================================================================

        [Test]
        [Description("TryFreezeResult 幂等：首次成功后后续调用不重复冻结。")]
        public void TryFreezeResult_Idempotent_AfterFirstFreeze()
        {
            (BattleManager manager, _, _, _, BattleResultBuilder resultBuilder) =
                CreateManager(NormalRow(1, 1, 0, 0, 0));

            bool firstResult = manager.TryFreezeResult(playerWin: true);
            bool secondResult = manager.TryFreezeResult(playerWin: false);

            Assert.IsTrue(firstResult, "首次冻结成功。");
            Assert.IsFalse(secondResult, "后续冻结返回 false（幂等）。");
            Assert.IsTrue(resultBuilder.FrozenResult.Value.IsWin, "首个完成事实胜出。");
        }

        [Test]
        [Description("TryFreezeResult 不在伤害调用栈内重入销毁 Manager 或集合。")]
        public void TryFreezeResult_DoesNotDestroyCollections_InCallStack()
        {
            (BattleManager manager, _, _, _, _) = CreateManager(NormalRow(1, 1, 0, 0, 0));
            manager.StartGame(0);

            manager.TryFreezeResult(playerWin: true);

            Assert.IsTrue(manager.IsStarted, "BattleManager 实例在 TryFreeze 后仍可用。");
        }
    }
}
