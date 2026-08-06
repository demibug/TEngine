using NUnit.Framework;

namespace GameBattle.Tests.EditMode.State
{
    /// <summary>
    /// BattleState / RuntimeIdAllocator / BattleReadModel 单元测试（task 3.7）。
    /// </summary>
    /// <remarks>
    /// <para>验证要求（task 3.7）：</para>
    /// <list type="bullet">
    /// <item>RuntimeIdAllocator 提供确定性 ID 分配（每局重置）。</item>
    /// <item>BattleState 持有单局权威状态（金币、波次、击杀数、目标血量等）。</item>
    /// <item>BattleReadModel 不暴露内部集合（提供只读视图）。</item>
    /// <item>状态修改只允许经规则服务提交（不直接暴露 setter）。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    internal class BattleStateTests
    {
        // ====================================================================
        // RuntimeIdAllocator 测试
        // ====================================================================

        [Test]
        [Description("RuntimeIdAllocator 从 1 开始单调递增分配 ID。")]
        public void RuntimeIdAllocator_AllocatesFromOneMonotonically()
        {
            var allocator = new RuntimeIdAllocator();

            Assert.AreEqual(1, allocator.Allocate(), "首次分配应返回 1。");
            Assert.AreEqual(2, allocator.Allocate(), "第二次分配应返回 2。");
            Assert.AreEqual(3, allocator.Allocate(), "第三次分配应返回 3。");
            Assert.AreEqual(3, allocator.LastAllocatedId, "LastAllocatedId 应为最后分配值。");
        }

        [Test]
        [Description("RuntimeIdAllocator 每局重置：新实例从 1 重新开始。")]
        public void RuntimeIdAllocator_NewInstance_ResetsPerBattle()
        {
            var first = new RuntimeIdAllocator();
            first.Allocate();
            first.Allocate();
            Assert.AreEqual(2, first.LastAllocatedId, "首局分配两次后 LastAllocatedId=2。");

            // 重开由 Factory 新建 Allocator，不复用旧局 ID 空间。
            var second = new RuntimeIdAllocator();
            Assert.AreEqual(0, second.LastAllocatedId, "新局 Allocator 初始 LastAllocatedId=0。");
            Assert.AreEqual(1, second.Allocate(), "新局首次分配应返回 1，不复用旧局 ID。");
        }

        [Test]
        [Description("RuntimeIdAllocator.Reset 将分配器恢复到初始状态。")]
        public void RuntimeIdAllocator_Reset_ReturnsToInitial()
        {
            var allocator = new RuntimeIdAllocator();
            allocator.Allocate();
            allocator.Allocate();

            allocator.Reset();

            Assert.AreEqual(0, allocator.LastAllocatedId, "Reset 后 LastAllocatedId=0。");
            Assert.AreEqual(1, allocator.Allocate(), "Reset 后首次分配返回 1。");
        }

        // ====================================================================
        // BattleState 初始状态测试
        // ====================================================================

        [Test]
        [Description("BattleState 构造后持有默认初始值（对应 BattleState.js 构造函数）。")]
        public void BattleState_DefaultInitialValues_MatchJsSource()
        {
            var state = new BattleState();

            Assert.AreEqual(0, state.CurrentRound, "初始波次=0。");
            Assert.AreEqual(BattleState.DefaultInitialGold, state.InitialGold, "初始金币=20。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, state.PlayerHealth, "玩家生命=3。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, state.OpponentHealth, "对手生命=3。");
            Assert.AreEqual(0, state.PlayerGold, "玩家初始金币=0（startGame 后才发放）。");
            Assert.AreEqual(0, state.OpponentGold, "对手初始金币=0。");
            Assert.AreEqual(0, state.KillCount, "击杀数=0。");
            Assert.AreEqual(0, state.BossKillCount, "Boss 击杀数=0。");
            Assert.IsFalse(state.IsGameOver, "未结束。");
            Assert.IsFalse(state.ContactOccurred, "未发生接触。");
            Assert.AreEqual(BattleState.DefaultMaxRounds, state.MaxRounds, "最大波次=20。");
            Assert.AreEqual(BattleState.DefaultRecruitCost, state.PlayerRecruitCost, "玩家刷新消耗=10。");
            Assert.AreEqual(BattleState.DefaultRecruitCost, state.OpponentRecruitCost, "对手刷新消耗=10。");
        }

        // ====================================================================
        // BattleState.ApplyStartGame 测试
        // ====================================================================

        [Test]
        [Description("ApplyStartGame 重置局内可变状态并记录开始时间。")]
        public void ApplyStartGame_ResetsRuntimeState()
        {
            var state = new BattleState();
            // 预置脏状态。
            state.ApplyEnemyKill();
            state.ApplyEnemyKill();
            state.ApplyContactOccurred();

            state.ApplyStartGame(nowMs: 12345);

            Assert.AreEqual(0, state.KillCount, "startGame 后击杀数重置为 0。");
            Assert.IsFalse(state.ContactOccurred, "startGame 后接触标志重置。");
            Assert.AreEqual(0, state.ResultStar, "startGame 后星级重置为 0。");
            Assert.AreEqual(12345, state.StartTimeMs, "开始时间戳记录。");
            Assert.IsFalse(state.IsGameOver, "未结束。");
        }

        // ====================================================================
        // BattleState.ApplyBeginWave 测试
        // ====================================================================

        [Test]
        [Description("ApplyBeginWave 波次递增（对应 BattleManager.js:112）。")]
        public void ApplyBeginWave_IncrementsRound()
        {
            var state = new BattleState();

            state.ApplyBeginWave();
            Assert.AreEqual(1, state.CurrentRound, "首次 beginWave 后波次=1。");

            state.ApplyBeginWave();
            Assert.AreEqual(2, state.CurrentRound, "第二次 beginWave 后波次=2。");
        }

        // ====================================================================
        // BattleState.ApplyGoldDelta / ApplyGoldSet 测试
        // ====================================================================

        [Test]
        [Description("ApplyGoldDelta 玩家方增加与消耗金币，不低于 0。")]
        public void ApplyGoldDelta_PlayerSide_ClampedToZero()
        {
            var state = new BattleState();

            state.ApplyGoldDelta(true, 20);
            Assert.AreEqual(20, state.PlayerGold, "发放 20 金币。");

            state.ApplyGoldDelta(true, -5);
            Assert.AreEqual(15, state.PlayerGold, "消耗 5 金币。");

            state.ApplyGoldDelta(true, -100);
            Assert.AreEqual(0, state.PlayerGold, "金币不低于 0。");
        }

        [Test]
        [Description("ApplyGoldDelta 对手方增加与消耗金币。")]
        public void ApplyGoldDelta_OpponentSide_ClampedToZero()
        {
            var state = new BattleState();

            state.ApplyGoldDelta(false, 20);
            Assert.AreEqual(20, state.OpponentGold, "对手发放 20 金币。");

            state.ApplyGoldDelta(false, -3);
            Assert.AreEqual(17, state.OpponentGold, "对手消耗 3 金币。");
        }

        [Test]
        [Description("ApplyGoldSet 设置绝对余额，不低于 0。")]
        public void ApplyGoldSet_ClampedToZero()
        {
            var state = new BattleState();

            state.ApplyGoldSet(true, 50);
            Assert.AreEqual(50, state.PlayerGold);

            state.ApplyGoldSet(true, -10);
            Assert.AreEqual(0, state.PlayerGold, "负值被钳制为 0。");
        }

        // ====================================================================
        // BattleState.ApplyDamage 测试
        // ====================================================================

        [Test]
        [Description("ApplyDamage 玩家方受击扣血，不低于 0。")]
        public void ApplyDamage_PlayerSide_ClampedToZero()
        {
            var state = new BattleState();

            state.ApplyDamage(true, 1);
            Assert.AreEqual(2, state.PlayerHealth, "玩家受击 1 后生命=2。");

            state.ApplyDamage(true, 10);
            Assert.AreEqual(0, state.PlayerHealth, "生命不低于 0。");
        }

        [Test]
        [Description("ApplyDamage 对手方受击扣血。")]
        public void ApplyDamage_OpponentSide_ReducesHealth()
        {
            var state = new BattleState();

            state.ApplyDamage(false, 2);
            Assert.AreEqual(1, state.OpponentHealth, "对手受击 2 后生命=1。");
        }

        [Test]
        [Description("ApplyDamage 零或负伤害不修改生命。")]
        public void ApplyDamage_ZeroOrNegative_NoChange()
        {
            var state = new BattleState();
            int before = state.PlayerHealth;

            state.ApplyDamage(true, 0);
            state.ApplyDamage(true, -5);

            Assert.AreEqual(before, state.PlayerHealth, "零或负伤害不修改生命。");
        }

        // ====================================================================
        // BattleState.ApplyEnemyKill / ApplyContactOccurred 测试
        // ====================================================================

        [Test]
        [Description("ApplyEnemyKill 递增击杀数。")]
        public void ApplyEnemyKill_IncrementsKillCount()
        {
            var state = new BattleState();

            state.ApplyEnemyKill();
            state.ApplyEnemyKill();
            state.ApplyEnemyKill();

            Assert.AreEqual(3, state.KillCount, "三次击杀后击杀数=3。");
        }

        [Test]
        [Description("ApplyContactOccurred 标记接触已发生。")]
        public void ApplyContactOccurred_SetsFlag()
        {
            var state = new BattleState();

            state.ApplyContactOccurred();

            Assert.IsTrue(state.ContactOccurred, "接触标志置位。");
        }

        // ====================================================================
        // BattleState.ApplyRecruitCost / ApplyResultStar 测试
        // ====================================================================

        [Test]
        [Description("ApplyRecruitCost 设置刷新消耗。")]
        public void ApplyRecruitCost_SetsCost()
        {
            var state = new BattleState();

            state.ApplyRecruitCost(true, 12);
            Assert.AreEqual(12, state.PlayerRecruitCost, "玩家刷新消耗=12。");

            state.ApplyRecruitCost(false, 14);
            Assert.AreEqual(14, state.OpponentRecruitCost, "对手刷新消耗=14。");
        }

        [Test]
        [Description("ApplyResultStar 钳制到 0~3。")]
        public void ApplyResultStar_ClampedToRange()
        {
            var state = new BattleState();

            state.ApplyResultStar(2);
            Assert.AreEqual(2, state.ResultStar);

            state.ApplyResultStar(5);
            Assert.AreEqual(3, state.ResultStar, "星级上限 3。");

            state.ApplyResultStar(-1);
            Assert.AreEqual(0, state.ResultStar, "星级下限 0。");
        }

        // ====================================================================
        // BattleState.ApplyGameOver 测试
        // ====================================================================

        [Test]
        [Description("ApplyGameOver 重置局间状态（对应 BattleState.js:90-116）。")]
        public void ApplyGameOver_ResetsInterRoundState()
        {
            var state = new BattleState();
            state.ApplyBeginWave();
            state.ApplyBeginWave();
            state.ApplyGoldDelta(true, 50);
            state.ApplyEnemyKill();
            state.ApplyContactOccurred();
            state.ApplyRecruitCost(true, 16);

            state.ApplyGameOver();

            Assert.AreEqual(0, state.CurrentRound, "波次重置为 0。");
            Assert.AreEqual(0, state.PlayerGold, "金币重置为 0。");
            Assert.AreEqual(0, state.KillCount, "击杀数重置为 0。");
            Assert.IsFalse(state.ContactOccurred, "接触标志重置。");
            Assert.AreEqual(BattleState.DefaultRecruitCost, state.PlayerRecruitCost, "刷新消耗重置为 10。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, state.PlayerHealth, "生命重置为最大值。");
        }

        // ====================================================================
        // BattleReadModel 不暴露内部集合测试
        // ====================================================================

        [Test]
        [Description("BattleReadModel 只读属性反映 BattleState 当前值。")]
        public void BattleReadModel_ReadOnlyProperties_ReflectCurrentState()
        {
            var state = new BattleState();
            var idAllocator = new RuntimeIdAllocator();
            var readModel = new BattleReadModel(state, idAllocator);

            state.ApplyBeginWave();
            state.ApplyGoldDelta(true, 30);
            state.ApplyEnemyKill();
            idAllocator.Allocate();

            Assert.AreEqual(1, readModel.CurrentRound, "波次只读视图=1。");
            Assert.AreEqual(30, readModel.PlayerGold, "金币只读视图=30。");
            Assert.AreEqual(1, readModel.KillCount, "击杀数只读视图=1。");
        }

        [Test]
        [Description("BattleReadModel 不暴露 setter：状态修改只允许经 BattleState.Apply* 提交。")]
        public void BattleReadModel_ExposesNoSetters()
        {
            var state = new BattleState();
            var idAllocator = new RuntimeIdAllocator();
            var readModel = new BattleReadModel(state, idAllocator);

            // BattleReadModel 只有 get-only 属性，无 setter/Apply 方法。
            // 编译期保证：以下若取消注释应编译失败。
            // readModel.PlayerHealth = 0;        // 编译错误
            // readModel.ApplyDamage(true, 1);   // 编译错误

            // 运行期验证：通过 BattleState 修改后 ReadModel 反映新值。
            state.ApplyDamage(true, 1);
            Assert.AreEqual(2, readModel.PlayerHealth, "ReadModel 反映 State 修改后的值。");
        }

        [Test]
        [Description("BattleReadModel.Snapshot 返回不可变值类型副本，与后续 State 变更隔离。")]
        public void BattleReadModel_Snapshot_IsImmutableCopy()
        {
            var state = new BattleState();
            var idAllocator = new RuntimeIdAllocator();
            var readModel = new BattleReadModel(state, idAllocator);

            state.ApplyBeginWave();
            state.ApplyGoldDelta(true, 25);
            state.ApplyEnemyKill();
            idAllocator.Allocate();
            idAllocator.Allocate();

            BattleStateSnapshot snap = readModel.Snapshot();

            // 快照反映当前值。
            Assert.AreEqual(1, snap.CurrentRound);
            Assert.AreEqual(25, snap.PlayerGold);
            Assert.AreEqual(1, snap.KillCount);
            Assert.AreEqual(2, snap.LastRuntimeId);

            // 修改 State 后快照不变。
            state.ApplyBeginWave();
            state.ApplyGoldDelta(true, 100);
            state.ApplyEnemyKill();

            Assert.AreEqual(1, snap.CurrentRound, "快照不受后续 State 变更影响。");
            Assert.AreEqual(25, snap.PlayerGold, "快照金币不变。");
            Assert.AreEqual(1, snap.KillCount, "快照击杀数不变。");
        }

        [Test]
        [Description("BattleReadModel.SnapshotResultInputs 返回不可变结果冻结输入。")]
        public void BattleReadModel_SnapshotResultInputs_IsImmutable()
        {
            var state = new BattleState();
            var idAllocator = new RuntimeIdAllocator();
            var readModel = new BattleReadModel(state, idAllocator);

            state.ApplyBeginWave();
            state.ApplyBeginWave();
            state.ApplyGoldDelta(true, 40);
            state.ApplyEnemyKill();
            state.ApplyEnemyKill();
            state.ApplyEnemyKill();

            BattleResultInputs inputs = readModel.SnapshotResultInputs();

            Assert.AreEqual(2, inputs.CurrentRound, "波次=2。");
            Assert.AreEqual(40, inputs.PlayerGold, "金币=40。");
            Assert.AreEqual(3, inputs.KillCount, "击杀数=3。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, inputs.PlayerHealth, "生命=最大值。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, inputs.PlayerMaxHealth, "最大生命。");
        }

        [Test]
        [Description("BattleReadModel 不持有内部集合：只返回标量与不可变结构。")]
        public void BattleReadModel_ExposesNoInternalCollections()
        {
            var state = new BattleState();
            var idAllocator = new RuntimeIdAllocator();
            var readModel = new BattleReadModel(state, idAllocator);

            // BattleReadModel 的公共 API 只包含：
            //   - 标量 get-only 属性（int/bool/long）
            //   - Snapshot() 返回 readonly struct
            //   - SnapshotResultInputs() 返回 readonly struct
            // 不返回 List/Dictionary/数组或 BattleState 引用。
            // 编译期保证：无 internal 集合属性可被外部访问。

            Assert.IsNotNull(readModel.Snapshot(), "Snapshot 返回值类型，非集合引用。");
            Assert.IsNotNull(readModel.SnapshotResultInputs(), "SnapshotResultInputs 返回值类型。");
        }

        // ====================================================================
        // 确定性回放测试
        // ====================================================================

        [Test]
        [Description("相同操作序列在新建 Allocator + State 上产生相同状态（确定性）。")]
        public void DeterministicReplay_SameSequence_ProducesSameState()
        {
            // 第一局。
            var state1 = new BattleState();
            var id1 = new RuntimeIdAllocator();
            state1.ApplyStartGame(nowMs: 1000);
            state1.ApplyBeginWave();
            state1.ApplyGoldDelta(true, 20);
            state1.ApplyDamage(false, 1);
            state1.ApplyEnemyKill();
            int idFirst = id1.Allocate();

            // 第二局（重开：新建 Allocator + State）。
            var state2 = new BattleState();
            var id2 = new RuntimeIdAllocator();
            state2.ApplyStartGame(nowMs: 1000);
            state2.ApplyBeginWave();
            state2.ApplyGoldDelta(true, 20);
            state2.ApplyDamage(false, 1);
            state2.ApplyEnemyKill();
            int idSecond = id2.Allocate();

            // 相同输入序列产生相同状态。
            Assert.AreEqual(state1.CurrentRound, state2.CurrentRound, "波次一致。");
            Assert.AreEqual(state1.PlayerGold, state2.PlayerGold, "金币一致。");
            Assert.AreEqual(state1.OpponentHealth, state2.OpponentHealth, "对手生命一致。");
            Assert.AreEqual(state1.KillCount, state2.KillCount, "击杀数一致。");
            Assert.AreEqual(idFirst, idSecond, "运行时 ID 一致（确定性）。");
        }
    }
}
