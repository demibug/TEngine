using System;
using System.Collections.Generic;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Golden
{
    // ============================================================================
    // 任务 6.10：Phase5GoldenTrajectoryTests —— 从购买放置到波次结算的无表现最简闭环
    // ----------------------------------------------------------------------------
    // 验证内容（task 6.10 / specs/battle-simulation/spec.md
    //   "Input commands are atomic" / "Battle result is frozen once"）：
    //   1. 无表现最简闭环：开始战斗 → 购买放置 → 波次推进 → 结算
    //   2. 对照 JS Phase 5 黄金轨迹（GoldenBattleFixtures.TrajectoryFacts T1-T10）
    //   3. 验证任一失败路径无部分提交（spec "Input commands are atomic"）
    //
    // 测试策略：
    //   由于 BattleRuntimeFactory.Create 依赖 ConfigSystem.Instance.Tables（Luban），
    //   测试环境无法直接调用。本测试使用 JsonBattleConfigProvider 手动构建全部 Manager，
    //   组装 phaseHandlers 并驱动 BattleSimulation.Advance，验证闭环逻辑。
    //   这等效于 BattleRuntimeFactory.Create 的组装产物 + BattleModule.Update 的帧驱动。
    //
    // 黄金轨迹对照（GoldenBattleFixtures.TrajectoryFacts）：
    //   T4-place-4-soldiers: 4 兵放置成功，unitManager.count==4
    //   T1-wave1-spawn: tickN(160) 后 enemyManager.count>0
    //   T9-cleanup-pool: gameOver 后 enemy/unit/projectile/effect 全部归零
    //   结果冻结：首个 BATTLE_FINISHED 事实胜出（幂等）
    //
    // 本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // ============================================================================

    /// <summary>
    /// Phase 5 黄金轨迹对照测试：从购买放置到波次结算的无表现最简闭环（task 6.10）。
    /// </summary>
    /// <remarks>
    /// <para>验证无表现闭环可运行（至少在代码逻辑层面），对照 JS Phase 5 黄金轨迹，
    /// 并验证任一失败路径无部分提交。</para>
    /// <para><b>spec "Input commands are atomic"：</b>
    /// 购买并放置等复合命令 MUST 要么完成全部校验、扣费、创建、放置、消耗和补牌，
    /// 要么恢复到执行前状态。</para>
    /// <para><b>spec "Battle result is frozen once"：</b>
    /// 首次事实成功冻结后幂等，TryFreeze 不在伤害调用栈内重入销毁 Manager。</para>
    /// </remarks>
    [TestFixture]
    internal class Phase5GoldenTrajectoryTests
    {
        // ====================================================================
        // 测试用配置工厂
        // ====================================================================

        /// <summary>
        /// 创建黄金基线配置快照（使用 JsonBattleConfigProvider）。
        /// </summary>
        private static BattleConfigSnapshot CreateGoldenSnapshot(int maxRounds = 20)
        {
            var provider = new JsonBattleConfigProvider();
            BattleConfigSnapshot snapshot = provider.GetSnapshot();
            if (maxRounds == 20)
            {
                return snapshot;
            }

            // 需要自定义 maxRounds 时重建 WaveConfigSnapshot。
            WaveConfigSnapshot originalWave = snapshot.Wave;
            WaveConfigSnapshot customWave = new WaveConfigSnapshot(
                originalWave.WaveUnitCounts,
                originalWave.BossWaveNumbers,
                originalWave.BossSpawnChances,
                originalWave.SpawnStrategyWeights,
                originalWave.SpawnStrategies,
                originalWave.SkipBoss,
                originalWave.DelayTimeMs,
                maxRounds);

            return new BattleConfigSnapshot(
                snapshot.Map,
                snapshot.Enemy,
                customWave,
                snapshot.Units,
                snapshot.UnitLevel,
                snapshot.Economy,
                snapshot.Deck,
                snapshot.Projectile,
                snapshot.MissingFieldNotes,
                snapshot.SourceTag,
                snapshot.EnemyCatalog,
                snapshot.OrderedWavePlan);
        }

        /// <summary>
        /// 组装测试用运行时上下文（等效于 BattleRuntimeFactory.Create 的组装产物，
        /// 但使用 JsonBattleConfigProvider 而非 LubanBattleConfigProvider）。
        /// </summary>
        private sealed class TestRuntimeContext
        {
            public BattleConfigSnapshot ConfigSnapshot;
            public BattleState BattleState;
            public BattleReadModel ReadModel;
            public BattleResultBuilder ResultBuilder;
            public BattleManager BattleManager;
            public WaveManager WaveManager;
            public BattleEconomy Economy;
            public PlacementReservationRegistry ReservationRegistry;
            public EnemyManager EnemyManager;
            public AttackEffectManager AttackEffectManager;
            public AttackResolver AttackResolver;
            public ProjectileManager ProjectileManager;
            public AttackScheduler AttackScheduler;
            public UnitFactory UnitFactory;
            public UnitRegistry UnitRegistry;
            public BattleInputController InputController;
            public UnitSlotBoard SlotBoard;
            public RecruitManager RecruitManager;
            public UnitLevelService LevelService;
            public BattleActionScheduler ActionScheduler;
            public BattleSimulation Simulation;
            public RuntimeIdAllocator IdAllocator;
        }

        /// <summary>
        /// 组装测试用运行时上下文（不依赖 ConfigSystem）。
        /// </summary>
        private static TestRuntimeContext BuildContext(int maxRounds = 20, int randomSeed = 42)
        {
            BattleConfigSnapshot config = CreateGoldenSnapshot(maxRounds);

            const int gridSize = EnemyManager.DefaultGridSize;
            const float cellSize = 80f;

            var idAllocator = new RuntimeIdAllocator();
            var battleState = new BattleState();
            var readModel = new BattleReadModel(battleState, idAllocator);
            var resultBuilder = new BattleResultBuilder(readModel);

            var enemyManager = new EnemyManager(gridSize);
            var attackEffectManager = new AttackEffectManager();
            var attackResolver = new AttackResolver();
            var arrowPool = new BattleObjectPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            var projectileFactory = new ProjectileFactory(idAllocator, arrowPool, enemyManager, cellSize);
            var projectileManager = new ProjectileManager(projectileFactory);

            // 任务 4.6：用敌人目录 + 池作用域构造类型化 EnemyFactory（封闭注册表 + 四池），
            // 与 BattleRuntimeFactory 正式运行时一致（新生产链）。
            var poolScope = new BattlePoolScope();
            var enemyFactory = new EnemyFactory(idAllocator, config.EnemyCatalog, poolScope);

            // 敌军回收桥接：与 BattleRuntimeFactory 正式运行时一致，EnemyManager 统一移除时
            // 按 ConfiguredEnemyBase/实际 key 归还正确池（禁止 Mob0 强转）。
            enemyManager.ReleaseEnemy = enemy =>
            {
                if (enemy is ConfiguredEnemyBase configured)
                {
                    enemyFactory.Release(configured);
                }
            };

            const int opponentAttackMultiplier = 1;
            var knifePool = new BattleObjectPool<KnifeSoldier>(() => new KnifeSoldier());
            var bowPool = new BattleObjectPool<BowSoldier>(() => new BowSoldier());
            var spearPool = new BattleObjectPool<SpearSoldier>(() => new SpearSoldier());
            var cavalryPool = new BattleObjectPool<CavalrySoldier>(() => new CavalrySoldier());

            var unitFactory = new UnitFactory(
                idAllocator,
                knifePool, bowPool, spearPool, cavalryPool,
                enemyManager, attackResolver, attackEffectManager,
                projectileFactory, projectileManager,
                cellSize, opponentAttackMultiplier,
                new UnitLevelService(config.UnitLevel));

            var unitRegistry = new UnitRegistry(unitFactory, cellSize);

            int refreshCostIncrement = config.Economy.RefreshCostIncrement;
            var economy = new BattleEconomy(battleState, refreshCostIncrement);

            var randomSource = new SeededRandomSource(randomSeed);

            // 最终方案：构造槽位面板、征兵服务与等级服务。
            var levelService = new UnitLevelService(config.UnitLevel);
            var slotBoard = new UnitSlotBoard(levelService.MaxLevel);
            slotBoard.Initialize(config.Map, RecruitDefinitions.ReserveSlotCount);
            var recruitManager = new RecruitManager(randomSource, slotBoard, RecruitDefinitions.ReserveSlotCount);
            // 开局免费填满待上场槽。
            slotBoard.ReplaceReserve(true, recruitManager.GenerateBatch(true));
            slotBoard.ReplaceReserve(false, recruitManager.GenerateBatch(false));

            var reservationRegistry = new PlacementReservationRegistry();

            var inputController = new BattleInputController(
                slotBoard, recruitManager, levelService, economy, unitRegistry, config);

            // 任务 4.6：新生产链 WaveManager（有序计划 + Normal spawn handler + 不可用 Boss 端口）。
            // normalSpawnHandler 经闭包捕获 playerTarget/opponentTarget（BattleManager 之后绑定），
            // 实际出生发生在模拟 phase，此时两个 target 均已绑定。
            BattleTarget playerTarget = null;
            BattleTarget opponentTarget = null;
            NormalWaveSpawnHandler normalSpawnHandler = request =>
            {
                if (!config.EnemyCatalog.TryGetByKey(
                        request.EnemyKey, out EnemyDefinitionSnapshot definition))
                {
                    throw new InvalidOperationException(
                        $"[Phase5GoldenTrajectoryTests] 未知敌人键 '{request.EnemyKey}'（order={request.WaveOrder}）");
                }

                IEnemyEndPointAttackTarget endPointTarget =
                    request.IsPlayerLane ? playerTarget : opponentTarget;
                var spawnRequest = new EnemySpawnRequest(
                    enemyKey: request.EnemyKey,
                    isPlayerLane: request.IsPlayerLane,
                    waveOrder: request.WaveOrder,
                    difficultyIndex: request.DifficultyIndex,
                    strategyProfile: request.StrategyProfile,
                    map: config.Map,
                    cellSize: cellSize,
                    endPointTarget: endPointTarget,
                    onEnemyKilled: (killedId, attackerId, reward, lane) => { },
                    onDeathRequested: (killedId, reason) => enemyManager.RequestRemoveEnemy(killedId, reason),
                    width: 40f,
                    height: 40f);

                ConfiguredEnemyBase enemy = enemyFactory.Acquire(spawnRequest);
                try
                {
                    enemyManager.Register(enemy);
                }
                catch
                {
                    enemyFactory.Release(enemy);
                    throw;
                }

                return WaveEntityHandle.FromEnemyLease(enemy.CurrentLease);
            };

            var waveManager = new WaveManager(
                config.OrderedWavePlan, normalSpawnHandler, UnavailableBossWavePort.Instance);

            // 任务 4.6：EnemyManager 波次所有权移除事实接线（reason 不改变波次计数）。
            enemyManager.WaveEntityRemoved += (identity, _) => waveManager.OnEntityRemoved(identity);

            var battleManager = new BattleManager(
                config, battleState, waveManager, economy, resultBuilder);

            // 构造 BattleTarget 并绑定，使敌人接触目标时能触发实际战斗结算。
            playerTarget = new BattleTarget();
            playerTarget.Bind(battleState, battleManager, resultBuilder, isPlayerLaneTarget: true);
            opponentTarget = new BattleTarget();
            opponentTarget.Bind(battleState, battleManager, resultBuilder, isPlayerLaneTarget: false);

            var actionScheduler = new BattleActionScheduler();
            var attackScheduler = new AttackScheduler(
                actionScheduler, attackResolver, cellSize, cellSize);

            // 构造 phaseHandlers 连接到实际 Manager 的 update 方法。
            int phaseCount = Enum.GetValues(typeof(BattleUpdatePhase)).Length;
            Action<long, long, BattleUpdatePhase>[] phaseHandlers =
                new Action<long, long, BattleUpdatePhase>[phaseCount];

            phaseHandlers[(int)BattleUpdatePhase.DueActionsAndInput] =
                (frameNow, step, phase) => actionScheduler.FlushDueActions(step);
            phaseHandlers[(int)BattleUpdatePhase.Enemy] =
                (frameNow, step, phase) => enemyManager.Update(step);
            phaseHandlers[(int)BattleUpdatePhase.Projectile] =
                (frameNow, step, phase) => projectileManager.Update(frameNow, step);
            phaseHandlers[(int)BattleUpdatePhase.AttackRelease] =
                (frameNow, step, phase) => { };
            phaseHandlers[(int)BattleUpdatePhase.WaveSpawn] =
                (frameNow, step, phase) => waveManager.Update(step);
            phaseHandlers[(int)BattleUpdatePhase.UnitAttack] =
                (frameNow, step, phase) =>
                {
                    IReadOnlyList<SoldierBase> units = unitRegistry.GetActiveUnits();
                    attackScheduler.Update(units, enemyManager);
                };
            phaseHandlers[(int)BattleUpdatePhase.AttackEffect] =
                (frameNow, step, phase) => attackEffectManager.Update(step);

            Func<bool> tryFreezeHandler = () => resultBuilder.IsFrozen;
            var simulation = new BattleSimulation(phaseHandlers, tryFreezeHandler, actionScheduler);

            return new TestRuntimeContext
            {
                ConfigSnapshot = config,
                BattleState = battleState,
                ReadModel = readModel,
                ResultBuilder = resultBuilder,
                BattleManager = battleManager,
                WaveManager = waveManager,
                Economy = economy,
                ReservationRegistry = reservationRegistry,
                EnemyManager = enemyManager,
                AttackEffectManager = attackEffectManager,
                AttackResolver = attackResolver,
                ProjectileManager = projectileManager,
                AttackScheduler = attackScheduler,
                UnitFactory = unitFactory,
                UnitRegistry = unitRegistry,
                InputController = inputController,
                SlotBoard = slotBoard,
                RecruitManager = recruitManager,
                LevelService = levelService,
                ActionScheduler = actionScheduler,
                Simulation = simulation,
                IdAllocator = idAllocator,
            };
        }

        /// <summary>
        /// 启动战斗（等效于 BattleModule.DefaultEntryHandler 中的 StartGame 调用链）。
        /// </summary>
        private static void StartBattle(TestRuntimeContext ctx, long startNowMs = 0)
        {
            ctx.BattleManager.StartGame(startNowMs, spawnStrategyIndex: 0);
            ctx.InputController.StartGame();
        }

        /// <summary>
        /// 从待上场槽把前 N 个单位上场到玩家战场槽（最终方案 DropUnit 换槽流程）。
        /// </summary>
        private static void PlaceFirstNToBattle(TestRuntimeContext ctx, int count)
        {
            IReadOnlyList<UnitSlot> reserves = ctx.SlotBoard.GetSlots(true, SlotZone.Reserve);
            IReadOnlyList<UnitSlot> battles = ctx.SlotBoard.GetSlots(true, SlotZone.Battle);

            for (int i = 0; i < count && i < reserves.Count && i < battles.Count; i++)
            {
                if (reserves[i].IsEmpty)
                {
                    continue;
                }

                BattleInputResult result = ctx.InputController.Execute(
                    BattleInputCommand.CreateDropUnit(
                        commandId: 1000 + i,
                        sourceSlotId: reserves[i].SlotId.Id,
                        targetSlotId: battles[i].SlotId.Id));
                Assert.IsTrue(result.IsSuccess,
                    $"DropUnit 换槽 {i + 1} 应成功：{result.DiagnosticMessage}");
            }
        }

        // ====================================================================
        // 无表现最简闭环测试
        // ====================================================================

        [Test]
        [Description("无表现最简闭环：开始战斗 → 待上场单位上场到战场 → 波次推进 → 敌人接触目标 → 结算，" +
                     "验证全链路可运行。")]
        public void HeadlessClosedLoop_StartPlaceWaveSettle_RunsToCompletion()
        {
            TestRuntimeContext ctx = BuildContext(maxRounds: 20);

            // 开始战斗
            StartBattle(ctx);
            Assert.IsTrue(ctx.BattleManager.IsStarted, "BattleManager 应已启动");
            Assert.AreEqual(GoldenBattleFixtures.EconomyConfig.InitialGold,
                ctx.BattleState.PlayerGold, "开始后应发放初始金币");

            // 上场 4 个待上场单位到玩家战场槽（最终方案 DropUnit 换槽流程）。
            PlaceFirstNToBattle(ctx, count: 4);

            // 黄金轨迹 T4 断言：4 兵上场成功，unitManager.count==4
            Assert.AreEqual(4, ctx.UnitRegistry.Count,
                "4 兵上场后 UnitRegistry.Count 应为 4");

            // 波次推进：驱动 Simulation.Advance 推进足够时间让全部顺序波次生成、移动并完成清场。
            // 当前计划包含 4 个串行行，最长允许 2500 个 tick（200000ms），结果冻结后立即停止。
            const int maxTicks = 2500;
            const int stepMs = 80;
            long frameNow = 0;
            for (int tick = 0; tick < maxTicks; tick++)
            {
                frameNow += stepMs;
                ctx.Simulation.Advance(frameNow);

                // 检查是否已冻结
                if (ctx.ResultBuilder.IsFrozen)
                {
                    break;
                }
            }

            // 黄金轨迹 T7：结果冻结（首信号胜出，幂等）。
            // 任务 4.7 语义：胜利只由 AllConfiguredWavesCompleted 决定；若玩家侧目标先被敌方
            // 接触清空则先冻结失败。无论哪种，有限时间内的闭环都恰好产生一次冻结结果。
            Assert.IsTrue(ctx.ResultBuilder.IsFrozen,
                $"推进 {frameNow}ms 后结果应已冻结：" +
                $"order={ctx.WaveManager.CurrentOrder}, state={ctx.WaveManager.State}, " +
                $"activeHandles={ctx.WaveManager.ActiveHandleCount}, enemies={ctx.EnemyManager.Count}, " +
                $"playerHealth={ctx.BattleState.PlayerHealth}, opponentHealth={ctx.BattleState.OpponentHealth}");
            Assert.IsTrue(ctx.ResultBuilder.FrozenResult.HasValue,
                "冻结后 FrozenResult 应有值");

            // 验证幂等：再次 TryFreeze 返回 false
            bool secondFreeze = ctx.ResultBuilder.TryFreeze(true);
            Assert.IsFalse(secondFreeze,
                "已冻结后再次 TryFreeze 应返回 false（幂等，黄金轨迹 ResultFreeze.Rule）");
        }

        [Test]
        [Description("黄金轨迹 T1-wave1-spawn：推进足够时间后 EnemyManager.Count > 0（波次开始刷怪）。")]
        public void GoldenTrajectory_T1_Wave1Spawn_EnemyCountGreaterThanZero()
        {
            TestRuntimeContext ctx = BuildContext(maxRounds: 20);
            StartBattle(ctx);

            // delayTime=10000ms，推进 12 秒让第一波开始刷怪。
            // 第一波在 delayTime(10000ms) 到期后 beginWave，随后按 spawnInterval(1500ms) 刷怪。
            // 推进 12000ms 时，delayTime 已过，第一波已 beginWave，首个 spawnInterval 到期时
            // SpawnPairWhenDue 调用 OnSpawnEnemy 生成玩家方与对手方各一个 Mob0。
            long frameNow = 0;
            for (int tick = 0; tick < 150; tick++)
            {
                frameNow += 80;
                ctx.Simulation.Advance(frameNow);
            }

            // 黄金轨迹 T1：tickN(160) 后 enemyManager.count > 0
            // task 6.10 闭环返工：OnSpawnEnemy 已接入，SpawnPairWhenDue 真正创建敌人，
            // 推进足够时间后 EnemyManager.Count 应大于 0（恢复被放弃的 T1 断言）。
            Assert.IsTrue(ctx.BattleManager.IsStarted, "BattleManager 应已启动");
            Assert.GreaterOrEqual(ctx.BattleState.CurrentRound, 1,
                "推进 12 秒后应已开始第一波（CurrentRound >= 1）");
            Assert.Greater(ctx.EnemyManager.Count, 0,
                "推进 12 秒后 EnemyManager.Count 应大于 0（黄金轨迹 T1-wave1-spawn，OnSpawnEnemy 已接入）");
        }

        [Test]
        [Description("黄金轨迹 T4-place-4-soldiers：4 兵上场成功后 UnitRegistry.Count==4。")]
        public void GoldenTrajectory_T4_Place4Soldiers_CountIsFour()
        {
            TestRuntimeContext ctx = BuildContext();
            StartBattle(ctx);

            // 上场 4 兵（最终方案 DropUnit 换槽流程）。
            PlaceFirstNToBattle(ctx, count: 4);

            Assert.AreEqual(4, ctx.UnitRegistry.Count,
                "4 兵上场后 UnitRegistry.Count 应为 4");
            Assert.AreEqual(4, ctx.UnitRegistry.PlayerSoldierCount,
                "4 兵均为玩家方，PlayerSoldierCount 应为 4");
        }

        // ====================================================================
        // 失败路径无部分提交测试（spec "Input commands are atomic"）
        // ====================================================================

        [Test]
        [Description("spec 'Input commands are atomic'：征兵馒头不足时不清槽、不扣费。")]
        public void Recruit_InsufficientGold_NoPartialCommit()
        {
            TestRuntimeContext ctx = BuildContext();
            StartBattle(ctx);

            // 把玩家金币消耗到 0（征兵费用 >= 1）。
            ctx.BattleState.ApplyGoldDelta(true, -GoldenBattleFixtures.EconomyConfig.InitialGold);
            Assert.AreEqual(0, ctx.BattleState.PlayerGold, "前置：消耗后余额应为 0");

            // 记录征兵前的待上场槽占用。
            IReadOnlyList<UnitSlot> before = ctx.SlotBoard.GetSlots(true, SlotZone.Reserve);
            int occupiedBefore = 0;
            foreach (UnitSlot slot in before)
            {
                if (!slot.IsEmpty)
                {
                    occupiedBefore++;
                }
            }
            Assert.Greater(occupiedBefore, 0, "前置：开局已免费填满待上场槽");

            BattleInputResult result = ctx.InputController.Execute(
                BattleInputCommand.CreateRecruit(commandId: 100, playerSide: true));

            Assert.IsFalse(result.IsSuccess, "馒头不足应失败");
            Assert.AreEqual(BattleInputRejectReason.InsufficientGoldForRecruit, result.RejectReason,
                "失败原因应为 InsufficientGoldForRecruit");
            Assert.AreEqual(0, ctx.BattleState.PlayerGold, "馒头不足失败后不应扣费");

            // 待上场槽未被清除。
            IReadOnlyList<UnitSlot> after = ctx.SlotBoard.GetSlots(true, SlotZone.Reserve);
            int occupiedAfter = 0;
            foreach (UnitSlot slot in after)
            {
                if (!slot.IsEmpty)
                {
                    occupiedAfter++;
                }
            }
            Assert.AreEqual(occupiedBefore, occupiedAfter, "馒头不足失败后待上场槽未被清除");
        }

        /// <summary>用指定单位列表填满指定阵营全部待上场槽（ReplaceReserve 严格要求批次数量等于槽位数）。</summary>
        private static void FillReserveAll(TestRuntimeContext ctx, bool isPlayerSide, params BattleUnit[] units)
        {
            IReadOnlyList<UnitSlot> slots = ctx.SlotBoard.GetSlots(isPlayerSide, SlotZone.Reserve);
            var batch = new BattleUnit[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                batch[i] = i < units.Length
                    ? units[i]
                    : new BattleUnit(
                        unitId: 8000 + i, side: isPlayerSide, kind: UnitKind.Soldier,
                        soldierType: SoldierType.Knife, soldierText: "刀", level: 1);
            }

            ctx.SlotBoard.ReplaceReserve(isPlayerSide, batch);
        }

        [Test]
        [Description("spec 'Input commands are atomic'：DropUnit 目标不匹配时不修改任何槽位状态。")]
        public void DropUnit_TargetMismatch_NoStateChange()
        {
            TestRuntimeContext ctx = BuildContext();
            StartBattle(ctx);

            IReadOnlyList<UnitSlot> reserves = ctx.SlotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源槽放 1 级刀兵，目标槽放 1 级弓兵（不同兵种不可合并）。
            var knife = new BattleUnit(
                5000, true, UnitKind.Soldier, SoldierType.Knife, "刀", 1);
            var bow = new BattleUnit(
                5001, true, UnitKind.Soldier, SoldierType.Bow, "弓", 1);
            FillReserveAll(ctx, true, knife, bow);

            int sourceUnitId = ctx.SlotBoard.GetSlot(source.SlotId).OccupantUnitId;
            int targetUnitId = ctx.SlotBoard.GetSlot(target.SlotId).OccupantUnitId;

            BattleInputResult result = ctx.InputController.Execute(
                BattleInputCommand.CreateDropUnit(101, source.SlotId.Id, target.SlotId.Id));

            Assert.IsFalse(result.IsSuccess, "不同兵种应失败");
            Assert.AreEqual(BattleInputRejectReason.TargetMismatch, result.RejectReason,
                "失败原因应为 TargetMismatch");
            Assert.AreEqual(sourceUnitId, ctx.SlotBoard.GetSlot(source.SlotId).OccupantUnitId,
                "源槽单位未变");
            Assert.AreEqual(targetUnitId, ctx.SlotBoard.GetSlot(target.SlotId).OccupantUnitId,
                "目标槽单位未变");
            Assert.AreEqual(0, ctx.UnitRegistry.Count, "未创建/修改战斗实例");
        }

        [Test]
        [Description("spec 'Input commands are atomic'：DropUnit 目标满级时不修改任何槽位状态。")]
        public void DropUnit_MaxLevel_NoStateChange()
        {
            TestRuntimeContext ctx = BuildContext();
            StartBattle(ctx);

            IReadOnlyList<UnitSlot> reserves = ctx.SlotBoard.GetSlots(true, SlotZone.Reserve);
            UnitSlot source = reserves[0];
            UnitSlot target = reserves[1];

            // 源与目标都是 3 级刀兵（满级不可合并）。
            var sourceUnit = new BattleUnit(
                6000, true, UnitKind.Soldier, SoldierType.Knife, "刀", 3);
            var targetUnit = new BattleUnit(
                6001, true, UnitKind.Soldier, SoldierType.Knife, "刀", 3);
            FillReserveAll(ctx, true, sourceUnit, targetUnit);

            BattleInputResult result = ctx.InputController.Execute(
                BattleInputCommand.CreateDropUnit(102, source.SlotId.Id, target.SlotId.Id));

            Assert.IsFalse(result.IsSuccess, "满级应失败");
            Assert.AreEqual(BattleInputRejectReason.MaxLevelReached, result.RejectReason,
                "失败原因应为 MaxLevelReached");
            Assert.IsFalse(ctx.SlotBoard.GetSlot(source.SlotId).IsEmpty, "源槽未变");
            Assert.AreEqual(3, ctx.SlotBoard.GetSlot(target.SlotId).Occupant.Value.Level, "目标等级未变");
        }

        // ====================================================================
        // 决策 0.8 CommandId 去重测试
        // ====================================================================

        [Test]
        [Description("决策 0.8 CommandId 去重：同一 CommandId 重复提交返回首次结果，不再次扣费。")]
        public void CommandIdDedup_SameIdReturnsFirstResult_NoSideEffect()
        {
            TestRuntimeContext ctx = BuildContext();
            StartBattle(ctx);

            int goldBefore = ctx.BattleState.PlayerGold;
            int recruitCost = ctx.Economy.GetRefreshCost(true);

            var command = BattleInputCommand.CreateRecruit(commandId: 400, playerSide: true);
            BattleInputResult result1 = ctx.InputController.Execute(command);
            Assert.IsTrue(result1.IsSuccess, "首次征兵应成功");

            BattleInputResult result2 = ctx.InputController.Execute(command);
            Assert.IsTrue(result2.IsSuccess, "重复提交应返回首次成功结果");
            Assert.AreEqual(result1, result2, "相同 CommandId 返回首次结果");

            Assert.AreEqual(goldBefore - recruitCost, ctx.BattleState.PlayerGold,
                "重复提交不应再次扣费");
        }

        [Test]
        [Description("决策 0.8 CommandId 去重：不同 CommandId 即使 payload 相同也按独立命令处理。")]
        public void CommandIdDedup_DifferentIdIndependentCommand()
        {
            TestRuntimeContext ctx = BuildContext();
            StartBattle(ctx);

            int goldBefore = ctx.BattleState.PlayerGold;
            int firstCost = ctx.Economy.GetRefreshCost(true);

            BattleInputResult result1 = ctx.InputController.Execute(
                BattleInputCommand.CreateRecruit(commandId: 500, playerSide: true));
            Assert.IsTrue(result1.IsSuccess, "首次征兵应成功");

            BattleInputResult result2 = ctx.InputController.Execute(
                BattleInputCommand.CreateRecruit(commandId: 501, playerSide: true));
            Assert.IsFalse(result2.IsSuccess, "不同 CommandId 应独立执行余额校验");
            Assert.AreEqual(BattleInputRejectReason.InsufficientGoldForRecruit, result2.RejectReason,
                "第二次征兵费用递增后超过剩余金币，应明确拒绝");

            Assert.AreEqual(goldBefore - firstCost, ctx.BattleState.PlayerGold,
                "失败的独立命令不应再次扣费");
            Assert.AreEqual(1, ctx.Economy.GetRefreshCount(true),
                "失败的独立命令不应推进征兵次数");
        }

        [Test]
        [Description("黄金轨迹 T9-cleanup-pool：GameOver 后全部 Manager 集合归零。")]
        public void GoldenTrajectory_T9_Cleanup_AllManagersCleared()
        {
            TestRuntimeContext ctx = BuildContext();
            StartBattle(ctx);

            // 上场 2 个单位。
            PlaceFirstNToBattle(ctx, count: 2);
            Assert.AreEqual(2, ctx.UnitRegistry.Count, "上场后应有 2 个单位");

            // 模拟 Settling 清理（等效于 BattleRuntime.EnterSettling 步骤 3-7）
            ctx.AttackEffectManager.Clear();
            ctx.ProjectileManager.Clear();
            ctx.EnemyManager.GameOver();
            ctx.UnitRegistry.ClearForSettling();
            ctx.BattleManager.GameOver();
            ctx.WaveManager.Stop();
            ctx.WaveManager.Cleanup();
            ctx.Economy.GameOver();
            ctx.SlotBoard.GameOver();
            ctx.ReservationRegistry.Clear();

            // 黄金轨迹 T9：gameOver 后全部 count==0
            Assert.AreEqual(0, ctx.UnitRegistry.Count, "UnitRegistry 应清零");
            Assert.AreEqual(0, ctx.EnemyManager.Count, "EnemyManager 应清零");
            Assert.AreEqual(0, ctx.ReservationRegistry.Count, "ReservationRegistry 应清零");
            Assert.AreEqual(0, ctx.SlotBoard.SlotCount, "SlotBoard 应清空");
        }

        [Test]
        [Description("黄金轨迹 T10-restart-next：GameOver 重置后新局可启动。")]
        public void GoldenTrajectory_T10_RestartAfterGameOver_NewBattleStarts()
        {
            TestRuntimeContext ctx = BuildContext();
            StartBattle(ctx);

            // 上场一个单位。
            PlaceFirstNToBattle(ctx, count: 1);
            Assert.AreEqual(1, ctx.UnitRegistry.Count, "上场后应有 1 个单位");

            // GameOver 清理。
            ctx.BattleManager.GameOver();
            ctx.InputController.GameOver();
            ctx.SlotBoard.GameOver();

            Assert.IsFalse(ctx.BattleManager.IsStarted, "GameOver 后 BattleManager 应未启动");

            // 新局启动。
            ctx.BattleManager.StartGame(0, 0);
            ctx.InputController.StartGame();

            Assert.IsTrue(ctx.BattleManager.IsStarted, "新局 BattleManager 应已启动");
            Assert.AreEqual(GoldenBattleFixtures.EconomyConfig.InitialGold,
                ctx.BattleState.PlayerGold, "新局应重新发放初始金币");
        }
    }
}
