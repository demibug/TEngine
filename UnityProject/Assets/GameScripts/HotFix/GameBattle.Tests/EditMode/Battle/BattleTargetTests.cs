using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Battle
{
    // ============================================================================
    // 任务 4.2：BattleTargetTests —— 纯逻辑 BattleTarget 受击、存活、胜负提交与迟到伤害拒绝测试
    // ----------------------------------------------------------------------------
    // 验证要求（task 4.2）：
    //   1. 纯逻辑：不持有 Unity GameObject / 表现组件。
    //   2. 受击、存活、胜负提交经 BattleState 委托。
    //   3. 目标死亡/胜负冻结后拒绝迟到伤害。
    //
    // spec battle-simulation "Battle result is frozen once"：
    //   首次 TryFreeze 成功后只完成当前同步提交并中止剩余 phase/子步。
    //   TryFreeze 不在伤害调用栈内重入销毁 Manager 或集合。
    //
    // spec battle-runtime-lifecycle "Settling has no gameplay damage authority"：
    //   首次结果冻结后进入 Settling，立即停止新伤害与规则写入。
    //
    // spec battle-simulation "Update phases are explicit and single-owned"：
    //   伤害、死亡事实、奖励和胜负候选 MUST 在其发生点同步生效，不得被无依据地推迟到
    //   新增的帧末结算阶段。
    //
    // 来源证据（BattleTarget.js:1-69 / EnemyBase.js:420-446）：
    //   receiveEnemyContact(amount, sourceEnemy)：校验 amount、判 alive、设置 contactOccurred、
    //   扣减 health、记录 damageLog、归零置 DESTROYED、返回 true/false。
    //   EnemyBase.attackBattleTarget() 在 50ms 延迟后调用 receiveEnemyContact(1, this)。
    //
    // 并行任务契约说明：
    //   BattleResultBuilder（task 49 产物）实际构造签名为
    //   BattleResultBuilder(BattleReadModel)（单参数只读状态视图）。
    //   BattleReadModel 构造签名为 BattleReadModel(BattleState, RuntimeIdAllocator)。
    //   TryFreeze 签名为 bool TryFreeze(bool isWinCandidate, long nowMs = 0)。
    //   IsFrozen 为 public bool 属性。本测试复用此契约。
    //
    //   BattleManager.CheckHealthFreeze(bool isPlayerSide) 为 internal void 方法
    //   （BattleManager.cs:622）。任务 4.7 起语义：玩家侧生命归零 → TryFreezeResult(false)
    //   立即失败；对手侧生命归零只保留状态，不直接成功（成功必须等
    //   AllConfiguredWavesCompleted）。本测试通过 BattleTarget.ApplyDamage 间接触发该路径，
    //   不直接调用 CheckHealthFreeze。
    // ============================================================================

    /// <summary>
    /// 纯逻辑 BattleTarget 受击、存活、胜负提交与迟到伤害拒绝测试（task 4.2）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>纯逻辑：不持有 Unity GameObject / MonoBehaviour / 表现组件。</item>
    /// <item>状态委托：生命值委托 BattleState，受击经 ApplyDamage 提交。</item>
    /// <item>受击：ApplyDamage 正常扣减生命、记录 damageLog、设置 contactOccurred。</item>
    /// <item>存活：IsAlive 委托 BattleState 生命值。</item>
    /// <item>胜负提交：生命归零时经 BattleManager.CheckHealthFreeze 触发 TryFreeze。</item>
    /// <item>拒绝迟到伤害：目标死亡后 ApplyDamage 返回 false 且不修改状态。</item>
    /// <item>拒绝冻结后伤害：胜负冻结后 ApplyDamage 返回 false 且不修改状态。</item>
    /// <item>池复用：Reset 清空引用与 damageLog，可重新 Bind。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleTargetTests
    {
        // ====================================================================
        // 测试用配置工厂（复用 BattleManagerTests 的最小配置模式）
        // ====================================================================

        /// <summary>
        /// 创建黄金基线敌人配置快照（复用 BattleManagerTests.CreateGoldenEnemyConfig）。
        /// </summary>
        private static EnemyConfigSnapshot CreateGoldenEnemyConfig()
        {
            return new EnemyConfigSnapshot(
                type: "Mob0",
                mapEnemyTypeIndex: 0,
                speed: 50,
                healthByWave: new[] { 10, 11, 57, 44, 39, 92, 138, 200, 291, 421,
                                      611, 886, 1285, 1863, 2701, 3917, 5680, 8235, 11941, 17315 },
                earlyRoundHealthMultipliers: new[] { 0.6f, 0.6f, 0.6f, 0.6f, 0.7f, 0.7f, 0.7f, 0.8f, 0.8f, 0.8f },
                contactDamage: 1);
        }

        /// <summary>
        /// 创建测试用配置快照（复用 BattleManagerTests.CreateTestSnapshot 的最小模式）。
        /// </summary>
        private static BattleConfigSnapshot CreateTestSnapshot(int maxRounds = 20)
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

            WaveConfigSnapshot wave = new WaveConfigSnapshot(
                waveUnitCounts: new[] { 3, 3, 3 },
                bossWaveNumbers: Array.Empty<int>(),
                bossSpawnChances: Array.Empty<float>(),
                spawnStrategyWeights: new[] { 1 },
                spawnStrategies: new IReadOnlyList<float>[]
                {
                    new float[] { 1, 1, 1 },
                },
                skipBoss: true,
                delayTimeMs: 100,
                maxRounds: maxRounds);

            return new BattleConfigSnapshot(
                map: map,
                enemy: CreateGoldenEnemyConfig(),
                wave: wave,
                units: Array.Empty<UnitConfigSnapshot>(),
                unitLevel: new UnitLevelConfigSnapshot(3, new[] { 1f, 1.5f, 2f }, new[] { 1f, 1.2f, 1.5f }),
                economy: new EconomyConfigSnapshot(20, 10, 2, 1, 5, 3, 3),
                deck: new DeckConfigSnapshot(true, new[] { "刀", "弓", "枪", "骑" }, 5, 1, 1),
                projectile: new ProjectileConfigSnapshot(new[] { "SimpleDynamicArrow" }, "SimpleDynamicArrow", "TargetEnemyBezierMovement", "HitEnemyStrategy"),
                missingFieldNotes: Array.Empty<string>(),
                sourceTag: "Test");
        }

        /// <summary>
        /// 构造测试用有序波次计划（默认 profile 0 = [1f]），供 WaveManager 三参数构造。
        /// </summary>
        private static OrderedWavePlanSnapshot MakePlan(params WavePlanEntry[] rows)
        {
            var profiles = new Dictionary<int, IReadOnlyList<float>>
            {
                [0] = new float[] { 1f },
            };
            return new OrderedWavePlanSnapshot("test", rows, profiles);
        }

        /// <summary>构造单路普通 Normal 行（本测试不驱动波次推进，仅满足构造依赖）。</summary>
        private static WavePlanEntry NormalRow(int order)
        {
            return new WavePlanEntry(
                "test", order, WavePlanKind.Normal, "Mob0", 1, 0, "",
                preDelayMs: 0, spawnIntervalMs: 0, postDelayMs: 0,
                playerLane: true, opponentLane: false, strategyProfile: 0);
        }

        /// <summary>
        /// 创建测试用 BattleManager、BattleState、BattleResultBuilder 及关联依赖。
        /// </summary>
        /// <returns> BattleManager 及关联依赖元组。</returns>
        private static (BattleManager manager, BattleState state, BattleResultBuilder resultBuilder,
            BattleReadModel readModel) CreateManagerDeps(int maxRounds = 20)
        {
            BattleConfigSnapshot snapshot = CreateTestSnapshot(maxRounds);
            BattleState state = new BattleState();
            // WaveManager 有序三参数契约（新生产链）：本测试不驱动波次状态机，
            // 注入最小单行计划 + 记录式出生替身 + 不可用 Boss 端口，仅满足 BattleManager 构造依赖。
            OrderedWavePlanSnapshot plan = MakePlan(NormalRow(1));
            WaveManager waveManager = new WaveManager(
                plan, _ => new WaveEntityHandle(1, 1, 1, WaveEntityKind.Normal),
                UnavailableBossWavePort.Instance);
            BattleEconomy economy = new BattleEconomy(state, refreshCostIncrement: 2);

            // BattleResultBuilder 由 task 49 产出，构造签名为
            // BattleResultBuilder(BattleReadModel)（单参数只读状态视图）。
            RuntimeIdAllocator idAllocator = new RuntimeIdAllocator();
            BattleReadModel readModel = new BattleReadModel(state, idAllocator);
            BattleResultBuilder resultBuilder = new BattleResultBuilder(readModel);

            BattleManager manager = new BattleManager(
                snapshot, state, waveManager, economy, resultBuilder);
            return (manager, state, resultBuilder, readModel);
        }

        /// <summary>
        /// 创建已绑定的玩家方 BattleTarget。
        /// </summary>
        private static (BattleTarget target, BattleState state, BattleManager manager,
            BattleResultBuilder resultBuilder) CreateBoundPlayerTarget(int maxRounds = 20)
        {
            var (manager, state, resultBuilder, _) = CreateManagerDeps(maxRounds);
            state.ApplyStartGame(nowMs: 0);

            BattleTarget target = new BattleTarget();
            target.Bind(state, manager, resultBuilder, isPlayerLaneTarget: true);
            return (target, state, manager, resultBuilder);
        }

        /// <summary>
        /// 创建已绑定的对手方 BattleTarget。
        /// </summary>
        private static (BattleTarget target, BattleState state, BattleManager manager,
            BattleResultBuilder resultBuilder) CreateBoundOpponentTarget(int maxRounds = 20)
        {
            var (manager, state, resultBuilder, _) = CreateManagerDeps(maxRounds);
            state.ApplyStartGame(nowMs: 0);

            BattleTarget target = new BattleTarget();
            target.Bind(state, manager, resultBuilder, isPlayerLaneTarget: false);
            return (target, state, manager, resultBuilder);
        }

        // ====================================================================
        // 纯逻辑验证测试
        // ====================================================================

        [Test]
        [Description("BattleTarget 不继承 MonoBehaviour，不持有 Unity 表现组件。"
            + " design.md:9 逻辑层不依赖 MonoBehaviour。")]
        public void IsPureLogic_NoMonoBehaviourOrGameObject()
        {
            var (target, _, _, _) = CreateBoundPlayerTarget();

            Type targetType = target.GetType();

            // 不继承 MonoBehaviour（TEngine/UnityEngine 中的类型）。
            Type monoType = Type.GetType("UnityEngine.MonoBehaviour, UnityEngine.CoreModule");
            if (monoType != null)
            {
                Assert.IsFalse(
                    monoType.IsAssignableFrom(targetType),
                    "BattleTarget 不得继承 MonoBehaviour，否则引入 Unity 生命周期依赖。");
            }

            // 不持有 GameObject / Transform 等表现组件字段。
            // 通过反射检查私有字段类型名不含 GameObject/Transform/Sprite/Animator 等表现类型。
            var fields = targetType.GetFields(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                string fieldTypeName = field.FieldType.Name;
                Assert.IsFalse(
                    fieldTypeName.Contains("GameObject")
                        || fieldTypeName.Contains("Transform")
                        || fieldTypeName.Contains("Sprite")
                        || fieldTypeName.Contains("Animator"),
                    $"BattleTarget 不得持有表现组件字段 {field.Name}: {fieldTypeName}。");
            }
        }

        // ====================================================================
        // 状态委托与存活验证测试
        // ====================================================================

        [Test]
        [Description("Health 委托 BattleState.PlayerHealth（玩家方目标）。"
            + " 对应 BattleTarget.js:36-42 health getter。")]
        public void Health_PlayerTarget_DelegatesToBattleState()
        {
            var (target, state, _, _) = CreateBoundPlayerTarget();

            // 默认生命 = DefaultMaxHealth = 3。
            Assert.AreEqual(state.PlayerHealth, target.Health, "玩家方目标 Health 委托 BattleState.PlayerHealth。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, target.Health, "默认生命=3。");
        }

        [Test]
        [Description("Health 委托 BattleState.OpponentHealth（对手方目标）。"
            + " 对应 BattleTarget.js:36-42 health getter。")]
        public void Health_OpponentTarget_DelegatesToBattleState()
        {
            var (target, state, _, _) = CreateBoundOpponentTarget();

            Assert.AreEqual(state.OpponentHealth, target.Health, "对手方目标 Health 委托 BattleState.OpponentHealth。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, target.Health, "默认生命=3。");
        }

        [Test]
        [Description("IsAlive 委托 Health > 0。对应 BattleTarget.js:42 alive getter。")]
        public void IsAlive_DelegatesToHealth()
        {
            var (target, state, _, _) = CreateBoundPlayerTarget();

            Assert.IsTrue(target.IsAlive, "生命=3 时存活。");

            // 扣到 0。
            state.ApplyDamage(true, 3);
            Assert.AreEqual(0, target.Health, "生命归零。");
            Assert.IsFalse(target.IsAlive, "生命=0 时不存活。");
        }

        [Test]
        [Description("未绑定目标 Health=0、IsAlive=false、IsBound=false。")]
        public void UnboundTarget_HealthZero_NotAlive()
        {
            BattleTarget target = new BattleTarget();

            Assert.AreEqual(0, target.Health, "未绑定 Health=0。");
            Assert.IsFalse(target.IsAlive, "未绑定不存活。");
            Assert.IsFalse(target.IsBound, "未绑定 IsBound=false。");
        }

        // ====================================================================
        // 受击验证测试
        // ====================================================================

        [Test]
        [Description("ApplyDamage 正常扣减生命并返回 true。"
            + " 对应 BattleTarget.js:44-55 receiveEnemyContact。"
            + " 状态委托：经 BattleState.ApplyDamage 提交。")]
        public void ApplyDamage_NormalDamage_ReducesHealthAndReturnsTrue()
        {
            var (target, state, _, _) = CreateBoundPlayerTarget();
            int before = target.Health; // 3

            bool applied = target.ApplyDamage(amount: 1, sourceRuntimeId: 42);

            Assert.IsTrue(applied, "正常受击返回 true。");
            Assert.AreEqual(before - 1, target.Health, "生命 3-1=2。");
            Assert.AreEqual(2, state.PlayerHealth, "BattleState.PlayerHealth 同步扣减。");
            Assert.IsTrue(state.ContactOccurred, "contactOccurred 经 ApplyContactOccurred 设置。");
        }

        [Test]
        [Description("ApplyDamage 记录伤害日志（对应 damageLog.push）。"
            + " 对应 BattleTarget.js:52。")]
        public void ApplyDamage_RecordsDamageLog()
        {
            var (target, _, _, _) = CreateBoundPlayerTarget();
            int before = target.Health; // 3

            target.ApplyDamage(amount: 1, sourceRuntimeId: 7);
            target.ApplyDamage(amount: 2, sourceRuntimeId: 8);

            Assert.AreEqual(2, target.DamageLog.Count, "两次受击记录两条日志。");

            // 第一条：amount=1, before=3, after=2, source=7。
            TargetDamageRecord r1 = target.DamageLog[0];
            Assert.AreEqual(1, r1.Amount, "第一条 amount=1。");
            Assert.AreEqual(before, r1.Before, "第一条 before=3。");
            Assert.AreEqual(2, r1.After, "第一条 after=2。");
            Assert.AreEqual(7, r1.SourceRuntimeId, "第一条 source=7。");

            // 第二条：amount=2, before=2, after=0, source=8。
            TargetDamageRecord r2 = target.DamageLog[1];
            Assert.AreEqual(2, r2.Amount, "第二条 amount=2。");
            Assert.AreEqual(2, r2.Before, "第二条 before=2。");
            Assert.AreEqual(0, r2.After, "第二条 after=0。");
            Assert.AreEqual(8, r2.SourceRuntimeId, "第二条 source=8。");
        }

        [Test]
        [Description("ApplyDamage 非正数 amount 抛 ArgumentOutOfRangeException。"
            + " 对应 BattleTarget.js:46 TypeError。")]
        public void ApplyDamage_NonPositiveAmount_Throws()
        {
            var (target, _, _, _) = CreateBoundPlayerTarget();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => target.ApplyDamage(amount: 0),
                "amount=0 抛 ArgumentOutOfRangeException。");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => target.ApplyDamage(amount: -1),
                "amount=-1 抛 ArgumentOutOfRangeException。");
        }

        [Test]
        [Description("未绑定目标 ApplyDamage 抛 InvalidOperationException。"
            + " 对应 BattleTarget.js:45 未绑定 battleState 抛 Error。")]
        public void ApplyDamage_UnboundTarget_Throws()
        {
            BattleTarget target = new BattleTarget();

            Assert.Throws<InvalidOperationException>(
                () => target.ApplyDamage(amount: 1),
                "未绑定目标受击抛 InvalidOperationException。");
        }

        [Test]
        [Description("对手方目标 ApplyDamage 扣减 OpponentHealth。")]
        public void ApplyDamage_OpponentTarget_ReducesOpponentHealth()
        {
            var (target, state, _, _) = CreateBoundOpponentTarget();
            int before = target.Health; // 3

            bool applied = target.ApplyDamage(amount: 1, sourceRuntimeId: 1);

            Assert.IsTrue(applied, "对手方目标受击返回 true。");
            Assert.AreEqual(before - 1, target.Health, "对手方生命 3-1=2。");
            Assert.AreEqual(2, state.OpponentHealth, "BattleState.OpponentHealth 同步扣减。");
        }

        // ====================================================================
        // 胜负提交验证测试
        // ====================================================================

        [Test]
        [Description("生命归零时触发胜负冻结：玩家方归零 → 玩家失败（playerWin=false）。"
            + " 对应 BattleState.js:61 BATTLE_FINISHED(false)。"
            + " spec 'Battle result is frozen once'。")]
        public void ApplyDamage_PlayerHealthZero_TriggersLossFreeze()
        {
            var (target, state, _, resultBuilder) = CreateBoundPlayerTarget();

            Assert.IsFalse(resultBuilder.IsFrozen, "冻结前 IsFrozen=false。");

            // 玩家方生命 3 → 0（一次 3 点伤害）。
            bool applied = target.ApplyDamage(amount: 3, sourceRuntimeId: 1);

            Assert.IsTrue(applied, "受击正常返回 true。");
            Assert.AreEqual(0, state.PlayerHealth, "玩家方生命归零。");
            Assert.IsTrue(resultBuilder.IsFrozen, "生命归零触发 TryFreeze。");
            Assert.IsTrue(resultBuilder.FrozenResult.HasValue, "FrozenResult 已冻结。");
            Assert.IsFalse(resultBuilder.FrozenResult.Value.IsWin, "玩家方归零 → 玩家失败。");
            Assert.IsTrue(target.IsDestroyed, "目标已摧毁。");
        }

        [Test]
        [Description("对手方生命归零只保留状态，不直接成功（任务 4.7：成功必须等 AllConfiguredWavesCompleted）。"
            + " 对应 design.md 决策 9 / spec ordered-wave-plan 'Fail before the plan finishes'。"
            + " spec 'Battle result is frozen once'。")]
        public void ApplyDamage_OpponentHealthZero_KeepsState_NoWinFreeze()
        {
            var (target, state, _, resultBuilder) = CreateBoundOpponentTarget();

            Assert.IsFalse(resultBuilder.IsFrozen, "冻结前 IsFrozen=false。");

            // 对手方生命 3 → 0。
            bool applied = target.ApplyDamage(amount: 3, sourceRuntimeId: 1);

            Assert.IsTrue(applied, "受击正常返回 true。");
            Assert.AreEqual(0, state.OpponentHealth, "对手方生命归零（状态保留）。");
            Assert.IsFalse(resultBuilder.IsFrozen, "对手归零不直接成功（成功必须等全部配置波清场）。");
            Assert.IsTrue(target.IsDestroyed, "目标已摧毁（拒绝后续受击）。");
        }

        [Test]
        [Description("生命归零时胜负在发生点同步生效，不推迟到帧末。"
            + " spec 'Update phases are explicit and single-owned'。")]
        public void ApplyDamage_HealthZero_FreezesSynchronouslyAtDamagePoint()
        {
            var (target, _, _, resultBuilder) = CreateBoundPlayerTarget();

            // 多段伤害在同一同步调用链内：第一段扣到 1，第二段扣到 0 并触发冻结。
            target.ApplyDamage(amount: 2, sourceRuntimeId: 1);
            Assert.IsFalse(resultBuilder.IsFrozen, "生命=1 时不冻结。");

            target.ApplyDamage(amount: 1, sourceRuntimeId: 2);
            Assert.IsTrue(resultBuilder.IsFrozen, "生命归零时同步冻结，不推迟。");
        }

        // ====================================================================
        // 拒绝迟到伤害验证测试
        // ====================================================================

        [Test]
        [Description("目标死亡后 ApplyDamage 返回 false 且不修改生命。"
            + " 对应 BattleTarget.js:47 if (!this.alive) return false。")]
        public void ApplyDamage_AfterDeath_Rejected()
        {
            var (target, state, _, _) = CreateBoundPlayerTarget();

            // 第一击致命：3 → 0。
            target.ApplyDamage(amount: 3, sourceRuntimeId: 1);
            Assert.AreEqual(0, state.PlayerHealth, "生命归零。");
            Assert.IsFalse(target.IsAlive, "目标已死亡。");

            // 迟到伤害：被拒绝。
            bool applied = target.ApplyDamage(amount: 1, sourceRuntimeId: 2);

            Assert.IsFalse(applied, "死亡后受击返回 false。");
            Assert.AreEqual(0, state.PlayerHealth, "生命不变，不扣为负。");
        }

        [Test]
        [Description("目标死亡后迟到伤害不追加 damageLog 条目。")]
        public void ApplyDamage_AfterDeath_NoNewDamageLog()
        {
            var (target, _, _, _) = CreateBoundPlayerTarget();

            target.ApplyDamage(amount: 3, sourceRuntimeId: 1);
            int logCountAfterDeath = target.DamageLog.Count;

            bool applied = target.ApplyDamage(amount: 1, sourceRuntimeId: 2);

            Assert.IsFalse(applied, "迟到伤害被拒绝。");
            Assert.AreEqual(logCountAfterDeath, target.DamageLog.Count, "不追加伤害日志。");
        }

        [Test]
        [Description("胜负冻结后 ApplyDamage 返回 false 且不修改生命。"
            + " spec 'Settling has no gameplay damage authority'。"
            + " 扩展守卫：覆盖空中弹道在冻结后才命中的迟到场景。")]
        public void ApplyDamage_AfterFrozen_Rejected()
        {
            var (target, state, manager, resultBuilder) = CreateBoundPlayerTarget();

            // 玩家方目标归零触发冻结（任务 4.7：玩家侧归零立即失败）。
            target.ApplyDamage(amount: 3, sourceRuntimeId: 1);
            Assert.IsTrue(resultBuilder.IsFrozen, "已冻结。");
            Assert.AreEqual(0, state.PlayerHealth, "玩家方生命归零。");

            // 构造"对手方目标未死亡但胜负已冻结"场景：
            // 手动 Bind 一个新的对手方目标到已冻结的 resultBuilder（同一局 BattleState/Manager）。
            // 对手方生命仍为 3（未死亡），但 resultBuilder 已冻结。
            BattleTarget frozenTarget = new BattleTarget();
            frozenTarget.Bind(state, manager, resultBuilder, isPlayerLaneTarget: false);
            Assert.AreEqual(3, frozenTarget.Health, "对手方目标未死亡。");
            Assert.IsTrue(resultBuilder.IsFrozen, "胜负已冻结。");

            bool applied = frozenTarget.ApplyDamage(amount: 1, sourceRuntimeId: 9);

            Assert.IsFalse(applied, "冻结后受击返回 false。");
            Assert.AreEqual(3, frozenTarget.Health, "生命不变。");
            Assert.AreEqual(3, state.OpponentHealth, "BattleState.OpponentHealth 不变。");
        }

        [Test]
        [Description("冻结门控优先于 alive 检查：即使目标存活，冻结后也拒绝受击。"
            + " 决策 0.4：首次 TryFreeze 后后续完成事实被忽略。")]
        public void ApplyDamage_FreezeGate_PrioritizedOverAlive()
        {
            var (target, state, _, resultBuilder) = CreateBoundPlayerTarget();
            var (manager, _, _, _) = CreateManagerDeps();

            // 手动冻结 resultBuilder（模拟其他完成事实先触发，如 maxRounds 达成）。
            resultBuilder.TryFreeze(isWinCandidate: true, nowMs: 1000);
            Assert.IsTrue(resultBuilder.IsFrozen, "已冻结。");

            // 玩家方目标未死亡（生命=3），但胜负已冻结。
            Assert.IsTrue(target.IsAlive, "目标仍存活。");

            // 重新 Bind target 到已冻结的 resultBuilder（同一局 BattleState/Manager）。
            target.Reset();
            target.Bind(state, manager, resultBuilder, isPlayerLaneTarget: true);

            bool applied = target.ApplyDamage(amount: 1, sourceRuntimeId: 1);

            Assert.IsFalse(applied, "冻结门控优先，拒绝受击。");
            Assert.AreEqual(3, state.PlayerHealth, "生命不变。");
        }

        // ====================================================================
        // 不在伤害调用栈内重入销毁验证
        // ====================================================================

        [Test]
        [Description("ApplyDamage 触发冻结后正常返回 true，不重入销毁 Manager 或集合。"
            + " spec 'Freeze occurs inside a manager update'。"
            + " 决策 0.4：TryFreeze 不在伤害调用栈内重入销毁。")]
        public void ApplyDamage_TriggersFreeze_NoReentrantDestruction()
        {
            var (target, _, manager, resultBuilder) = CreateBoundPlayerTarget();

            // 玩家方归零触发冻结（任务 4.7：玩家侧归零立即失败）。
            bool applied = target.ApplyDamage(amount: 3, sourceRuntimeId: 1);

            // 冻结成功后本方法正常返回 true（完成当前同步提交）。
            Assert.IsTrue(applied, "触发冻结的受击正常返回 true。");
            Assert.IsTrue(resultBuilder.IsFrozen, "已冻结。");

            // Manager 仍可访问（未被销毁）；测试夹具未启动波次，所以当前轮次仍为 0。
            Assert.AreEqual(0, manager.CurrentRound, "Manager 未被重入销毁，且未凭空推进波次。");
            // 目标已标记摧毁但未被销毁。
            Assert.IsTrue(target.IsDestroyed, "目标已摧毁。");
        }

        // ====================================================================
        // 接触标记验证
        // ====================================================================

        [Test]
        [Description("ApplyDamage 经 BattleState.ApplyContactOccurred 设置 contactOccurred。"
            + " 对应 BattleTarget.js:49。")]
        public void ApplyDamage_SetsContactOccurred()
        {
            var (target, state, _, _) = CreateBoundPlayerTarget();

            Assert.IsFalse(state.ContactOccurred, "受击前 contactOccurred=false。");

            target.ApplyDamage(amount: 1, sourceRuntimeId: 1);

            Assert.IsTrue(state.ContactOccurred, "受击后 contactOccurred=true。");
        }

        // ====================================================================
        // 池复用验证测试
        // ====================================================================

        [Test]
        [Description("Reset 清空引用与 damageLog，置 Pooled 状态。"
            + " 对应 BattleTarget.js:56-63 Td()。")]
        public void Reset_ClearsReferencesAndDamageLog()
        {
            var (target, _, _, _) = CreateBoundPlayerTarget();

            target.ApplyDamage(amount: 1, sourceRuntimeId: 1);
            Assert.AreEqual(1, target.DamageLog.Count, "受击后日志非空。");
            Assert.IsTrue(target.IsBound, "已绑定。");

            target.Reset();

            Assert.IsTrue(target.IsPooled, "Reset 后 IsPooled=true。");
            Assert.IsFalse(target.IsBound, "Reset 后 IsBound=false。");
            Assert.AreEqual(0, target.Health, "Reset 后 Health=0（引用已清空）。");
            Assert.IsFalse(target.IsAlive, "Reset 后不存活。");
            Assert.AreEqual(0, target.DamageLog.Count, "Reset 后日志清空。");
        }

        [Test]
        [Description("Reset 后可重新 Bind 到新局 BattleState/Manager。")]
        public void Reset_CanRebind_NewBattle()
        {
            var (target1, state1, manager1, resultBuilder1) = CreateBoundPlayerTarget();
            target1.ApplyDamage(amount: 1, sourceRuntimeId: 1);

            target1.Reset();

            // 新局依赖。
            var (manager2, state2, resultBuilder2, _) = CreateManagerDeps();
            state2.ApplyStartGame(nowMs: 100);

            target1.Bind(state2, manager2, resultBuilder2, isPlayerLaneTarget: false);

            Assert.IsTrue(target1.IsBound, "重新绑定后 IsBound=true。");
            Assert.IsFalse(target1.IsPlayerLaneTarget, "新局绑定对手方。");
            Assert.AreEqual(state2.OpponentHealth, target1.Health, "Health 委托新局 BattleState。");
            Assert.AreEqual(0, target1.DamageLog.Count, "重新 Bind 清空旧 damageLog。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, target1.Health, "新局生命=3。");
        }

        [Test]
        [Description("MarkEnded 标记目标已结束，后续 ApplyDamage 抛 InvalidOperationException（非 Active）。"
            + " 对应 BattleTarget.js:64 gameOver()。")]
        public void MarkEnded_RejectsSubsequentDamage()
        {
            var (target, _, _, _) = CreateBoundPlayerTarget();

            target.MarkEnded();

            // Ended 状态下 ApplyDamage 抛异常（非 Active）。
            Assert.Throws<InvalidOperationException>(
                () => target.ApplyDamage(amount: 1),
                "Ended 状态下受击抛 InvalidOperationException。");
        }

        [Test]
        [Description("Reset 后 MarkEnded 不再标记（已 Pooled 不倒退）。")]
        public void MarkEnded_AfterReset_NoEffect()
        {
            var (target, _, _, _) = CreateBoundPlayerTarget();

            target.Reset();
            target.MarkEnded();

            Assert.IsTrue(target.IsPooled, "Reset 后 MarkEnded 不改变 Pooled 状态。");
        }

        // ====================================================================
        // 边界场景验证
        // ====================================================================

        [Test]
        [Description("多段伤害逐步扣减，最后一击触发冻结（玩家侧归零 → 失败）。")]
        public void ApplyDamage_MultipleHits_LastHitFreezes()
        {
            var (target, _, _, resultBuilder) = CreateBoundPlayerTarget();

            // 3 → 2。
            target.ApplyDamage(amount: 1, sourceRuntimeId: 1);
            Assert.IsFalse(resultBuilder.IsFrozen, "生命=2 不冻结。");
            Assert.IsFalse(target.IsDestroyed, "目标未摧毁。");

            // 2 → 1。
            target.ApplyDamage(amount: 1, sourceRuntimeId: 2);
            Assert.IsFalse(resultBuilder.IsFrozen, "生命=1 不冻结。");
            Assert.IsFalse(target.IsDestroyed, "目标未摧毁。");

            // 1 → 0，触发冻结。
            target.ApplyDamage(amount: 1, sourceRuntimeId: 3);
            Assert.IsTrue(resultBuilder.IsFrozen, "生命归零触发冻结。");
            Assert.IsTrue(target.IsDestroyed, "目标已摧毁。");
            Assert.AreEqual(3, target.DamageLog.Count, "三段伤害记录三条日志。");
        }

        [Test]
        [Description("超额伤害不使生命为负：BattleState.ApplyDamage 钳制到 0。"
            + " task 3.7 不变量 3：生命非负。")]
        public void ApplyDamage_Overkill_HealthClampedToZero()
        {
            var (target, state, _, _) = CreateBoundPlayerTarget();

            // 一次 10 点伤害超过生命 3。
            target.ApplyDamage(amount: 10, sourceRuntimeId: 1);

            Assert.AreEqual(0, state.PlayerHealth, "生命钳制到 0，不为负。");
            Assert.AreEqual(0, target.Health, "Health=0。");
            Assert.IsFalse(target.IsAlive, "不存活。");
            Assert.IsTrue(target.IsDestroyed, "已摧毁。");

            // 伤害日志记录 before=3, after=0（而非 -7）。
            TargetDamageRecord record = target.DamageLog[0];
            Assert.AreEqual(3, record.Before, "before=3。");
            Assert.AreEqual(0, record.After, "after=0（钳制后）。");
        }
    }
}
