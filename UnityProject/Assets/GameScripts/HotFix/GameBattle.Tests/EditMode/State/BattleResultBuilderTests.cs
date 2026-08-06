using System;
using GameCommon.Battle;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.State
{
    /// <summary>
    /// BattleResultBuilder 单元测试（OpenSpec change port-minimal-battle-to-gamebattle
    /// task 3.11）。
    /// </summary>
    /// <remarks>
    /// <para>验证要求（task 3.11）：</para>
    /// <list type="bullet">
    /// <item>只冻结一次（重复 TryFreeze 返回首次结果）。</item>
    /// <item>冻结后写权限拒绝：IsFrozen 置位后 TryFreeze 返回 false 且不修改已冻结结果。</item>
    /// <item>结果字段稳定：保留源 BattleResult 的稳定标量字段，排除 raw 与可变 object 集合。</item>
    /// <item>星级计算与 BattleResult.js:6 calculateStar 一致。</item>
    /// <item>首个完成事实胜出（决策 1.4）：不同候选值后到调用被忽略。</item>
    /// </list>
    ///
    /// <para>覆盖 spec：</para>
    /// <list type="bullet">
    /// <item>battle-runtime-lifecycle "Settling has no gameplay damage authority"：
    /// 冻结后无规则写权限。</item>
    /// <item>battle-parity-verification "Result freeze and Settling quiescence are verified"：
    /// 首次完成事实发生在不同 phase 时的冻结语义。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    internal class BattleResultBuilderTests
    {
        // ====================================================================
        // 辅助：构造 (state, idAllocator, readModel, builder) 四元组
        // ====================================================================

        private static (BattleState state, RuntimeIdAllocator idAllocator,
            BattleReadModel readModel, BattleResultBuilder builder) CreateFixture()
        {
            var state = new BattleState();
            var idAllocator = new RuntimeIdAllocator();
            var readModel = new BattleReadModel(state, idAllocator);
            var builder = new BattleResultBuilder(readModel);
            return (state, idAllocator, readModel, builder);
        }

        // ====================================================================
        // 幂等性测试
        // ====================================================================

        [Test]
        [Description("首次 TryFreeze 成功返回 true 并置位 IsFrozen。"
            + " 决策 1.4 第一个完成事实胜出。")]
        public void TryFreeze_FirstCall_ReturnsTrueAndSetsFrozen()
        {
            var (_, _, _, builder) = CreateFixture();

            Assert.IsFalse(builder.IsFrozen, "未冻结时 IsFrozen 应为 false。");
            Assert.IsFalse(builder.FrozenResult.HasValue, "未冻结时 FrozenResult 应为 null。");

            bool firstResult = builder.TryFreeze(isWinCandidate: true, nowMs: 1000);

            Assert.IsTrue(firstResult, "首次 TryFreeze 应返回 true。");
            Assert.IsTrue(builder.IsFrozen, "首次冻结后 IsFrozen 应为 true。");
            Assert.IsTrue(builder.FrozenResult.HasValue, "首次冻结后 FrozenResult 应有值。");
        }

        [Test]
        [Description("重复 TryFreeze 返回 false 且不修改已冻结结果。"
            + " 决策 1.4 第一个完成事实胜出；design.md:284 幂等入口。")]
        public void TryFreeze_RepeatCall_ReturnsFalseAndKeepsFirstResult()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);
            state.ApplyBeginWave();
            state.ApplyEnemyKill();

            // 首次冻结：判胜。
            bool firstResult = builder.TryFreeze(isWinCandidate: true, nowMs: 5000);
            BattleResultDto firstFrozen = builder.FrozenResult.Value;

            Assert.IsTrue(firstResult, "首次 TryFreeze 应返回 true。");
            Assert.AreEqual(true, firstFrozen.IsWin, "首次冻结应为判胜。");
            Assert.AreEqual(1, firstFrozen.KillCount, "首次冻结击杀数=1。");
            Assert.AreEqual(1, firstFrozen.Round, "首次冻结波次=1。");

            // 重复调用：即使候选值不同（判负），也返回 false 且保持首次结果。
            bool secondResult = builder.TryFreeze(isWinCandidate: false, nowMs: 9999);

            Assert.IsFalse(secondResult, "重复 TryFreeze 应返回 false。");
            Assert.IsTrue(builder.IsFrozen, "IsFrozen 仍为 true。");

            BattleResultDto secondFrozen = builder.FrozenResult.Value;
            Assert.AreEqual(firstFrozen.IsWin, secondFrozen.IsWin, "IsWin 不变。");
            Assert.AreEqual(firstFrozen.Star, secondFrozen.Star, "Star 不变。");
            Assert.AreEqual(firstFrozen.KillCount, secondFrozen.KillCount, "KillCount 不变。");
            Assert.AreEqual(firstFrozen.Round, secondFrozen.Round, "Round 不变。");
            Assert.AreEqual(firstFrozen.BattleDurationMs, secondFrozen.BattleDurationMs,
                "BattleDurationMs 不变（首次冻结时间戳固化）。");
        }

        [Test]
        [Description("多次重复 TryFreeze 全部返回 false，结果始终为首次快照。")]
        public void TryFreeze_MultipleRepeatCalls_AllReturnFalse()
        {
            var (_, _, _, builder) = CreateFixture();

            Assert.IsTrue(builder.TryFreeze(isWinCandidate: false, nowMs: 100),
                "首次冻结应成功。");

            for (int i = 0; i < 5; i++)
            {
                Assert.IsFalse(builder.TryFreeze(isWinCandidate: true, nowMs: 200 + i),
                    $"第 {i + 2} 次调用应返回 false。");
            }

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.IsFalse(frozen.IsWin, "首次候选为判负，结果保持判负。");
        }

        // ====================================================================
        // 冻结后写权限拒绝测试
        // ====================================================================

        [Test]
        [Description("冻结后 IsFrozen 为 true：Manager/到期动作/表现回调据此时拒绝写。"
            + " task 3.11 冻结后无规则写权限。")]
        public void IsFrozen_AfterFreeze_TrueForWriteAuthorityCheck()
        {
            var (_, _, _, builder) = CreateFixture();

            builder.TryFreeze(isWinCandidate: true, nowMs: 1000);

            // Manager 在写入口断言 !builder.IsFrozen；本测试验证该标记可靠。
            Assert.IsTrue(builder.IsFrozen, "冻结后 IsFrozen 必须为 true，供 Manager 断言。");

            // 重复 TryFreeze 不修改状态（写权限拒绝的体现）。
            bool repeat = builder.TryFreeze(isWinCandidate: false, nowMs: 2000);
            Assert.IsFalse(repeat, "冻结后 TryFreeze 应拒绝（返回 false）。");
            Assert.IsTrue(builder.IsFrozen, "IsFrozen 不可逆。");
        }

        [Test]
        [Description("冻结后 GetFrozenResult 返回首次快照，不受后续状态变更影响。"
            + " 冻结后无规则写权限：即使 BattleState 变化，已冻结结果不可变。")]
        public void GetFrozenResult_AfterFreeze_ImmutableToStateChanges()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);
            state.ApplyBeginWave();
            state.ApplyEnemyKill();

            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);
            BattleResultDto frozenBefore = builder.GetFrozenResult();

            // 冻结后继续修改 BattleState（模拟迟到的伤害/击杀）。
            state.ApplyEnemyKill();
            state.ApplyEnemyKill();
            state.ApplyDamage(true, 1);
            state.ApplyBeginWave();

            BattleResultDto frozenAfter = builder.GetFrozenResult();

            Assert.AreEqual(frozenBefore.KillCount, frozenAfter.KillCount,
                "冻结后 BattleState 的击杀数变化不影响已冻结结果。");
            Assert.AreEqual(frozenBefore.Round, frozenAfter.Round,
                "冻结后 BattleState 的波次变化不影响已冻结结果。");
            Assert.AreEqual(frozenBefore.PlayerTargetHealth, frozenAfter.PlayerTargetHealth,
                "冻结后 BattleState 的生命变化不影响已冻结结果。");
        }

        [Test]
        [Description("未冻结时 GetFrozenResult 抛出 InvalidOperationException。")]
        public void GetFrozenResult_BeforeFreeze_Throws()
        {
            var (_, _, _, builder) = CreateFixture();

            Assert.Throws<InvalidOperationException>(() => builder.GetFrozenResult(),
                "未冻结时获取结果应抛异常。");
        }

        // ====================================================================
        // 首个完成事实胜出测试（决策 1.4）
        // ====================================================================

        [Test]
        [Description("玩家方目标生命归零（BATTLE_FINISHED(false)）首次冻结判负。"
            + " BattleState.js:61。")]
        public void TryFreeze_PlayerTargetDestroyed_FreezesAsLoss()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);

            // 玩家方生命归零。
            state.ApplyDamage(true, BattleState.DefaultMaxHealth);

            bool result = builder.TryFreeze(isWinCandidate: false, nowMs: 5000);

            Assert.IsTrue(result, "首次冻结应成功。");
            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.IsFalse(frozen.IsWin, "玩家方目标归零应判负。");
            Assert.AreEqual(BattleResultState.Lose, frozen.ResultState,
                "ResultState 应为 Lose。");
            Assert.AreEqual(0, frozen.PlayerTargetHealth, "玩家方目标生命=0。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, frozen.OpponentTargetHealth,
                "对手方目标生命仍为最大值。");
        }

        [Test]
        [Description("对手方目标生命归零（BATTLE_FINISHED(true)）首次冻结判胜。"
            + " BattleState.js:76。")]
        public void TryFreeze_OpponentTargetDestroyed_FreezesAsWin()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);

            // 对手方生命归零。
            state.ApplyDamage(false, BattleState.DefaultMaxHealth);

            bool result = builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            Assert.IsTrue(result, "首次冻结应成功。");
            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.IsTrue(frozen.IsWin, "对手方目标归零应判胜。");
            Assert.AreEqual(BattleResultState.Win, frozen.ResultState,
                "ResultState 应为 Win。");
            Assert.AreEqual(0, frozen.OpponentTargetHealth, "对手方目标生命=0。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, frozen.PlayerTargetHealth,
                "玩家方目标生命仍为最大值。");
        }

        [Test]
        [Description("首个完成事实胜出：判负先到，后续判胜被忽略。"
            + " 决策 1.4 / GoldenBattleFixtures.ResultFreeze.Rule。")]
        public void FirstFactWins_LossFirstThenWinCandidate_SecondIgnored()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);

            // 玩家方先归零（判负先到）。
            state.ApplyDamage(true, BattleState.DefaultMaxHealth);
            Assert.IsTrue(builder.TryFreeze(isWinCandidate: false, nowMs: 5000),
                "首次冻结（判负）应成功。");

            // 随后对手方也归零（判胜后到），应被忽略。
            state.ApplyDamage(false, BattleState.DefaultMaxHealth);
            Assert.IsFalse(builder.TryFreeze(isWinCandidate: true, nowMs: 6000),
                "后续判胜应被忽略（幂等）。");

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.IsFalse(frozen.IsWin, "首个完成事实（判负）胜出。");
        }

        [Test]
        [Description("首个完成事实胜出：判胜先到，后续判负被忽略。"
            + " 决策 1.4 / GoldenBattleFixtures T8-win-opponent-adou。")]
        public void FirstFactWins_WinFirstThenLossCandidate_SecondIgnored()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);

            // 对手方先归零（判胜先到）。
            state.ApplyDamage(false, BattleState.DefaultMaxHealth);
            Assert.IsTrue(builder.TryFreeze(isWinCandidate: true, nowMs: 5000),
                "首次冻结（判胜）应成功。");

            // 随后玩家方也归零（判负后到），应被忽略。
            state.ApplyDamage(true, BattleState.DefaultMaxHealth);
            Assert.IsFalse(builder.TryFreeze(isWinCandidate: false, nowMs: 6000),
                "后续判负应被忽略（幂等）。");

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.IsTrue(frozen.IsWin, "首个完成事实（判胜）胜出。");
        }

        // ====================================================================
        // 星级计算测试（BattleResult.js:6 calculateStar）
        // ====================================================================

        [Test]
        [Description("calculateStar：失败固定 0 星。BattleResult.js:6 if(!isWin)return 0。")]
        public void CalculateStar_Loss_AlwaysZero()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);

            // 失败时即使满血也是 0 星。
            builder.TryFreeze(isWinCandidate: false, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(0, frozen.Star, "失败固定 0 星。");
        }

        [Test]
        [Description("calculateStar：胜利且满血=3 星。BattleResult.js:6 hp>=max?3。")]
        public void CalculateStar_WinFullHealth_ThreeStars()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);

            // 满血判胜。
            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(3, frozen.Star, "满血判胜=3 星。");
        }

        [Test]
        [Description("calculateStar：胜利且生命>=ceil(max/2)=2 星。"
            + " BattleResult.js:6 hp>=ceil(max/2)?2。")]
        public void CalculateStar_WinHalfHealth_TwoStars()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);

            // 默认 maxHealth=3，ceil(3/2)=2。生命=2 时应为 2 星。
            state.ApplyDamage(true, 1); // 3 -> 2
            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(2, frozen.Star, "生命=2（>=ceil(3/2)=2）应=2 星。");
            Assert.AreEqual(2, frozen.PlayerTargetHealth, "玩家方生命=2。");
        }

        [Test]
        [Description("calculateStar：胜利且生命<ceil(max/2)=1 星。"
            + " BattleResult.js:6 :1。")]
        public void CalculateStar_WinLowHealth_OneStar()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);

            // 默认 maxHealth=3，ceil(3/2)=2。生命=1 时应为 1 星。
            state.ApplyDamage(true, 2); // 3 -> 1
            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(1, frozen.Star, "生命=1（<ceil(3/2)=2）应=1 星。");
            Assert.AreEqual(1, frozen.PlayerTargetHealth, "玩家方生命=1。");
        }

        [Test]
        [Description("calculateStar：规则服务已设置 ResultStar 时不覆盖。"
            + " BattleResult.js:7 star: resultStar || calculateStar。")]
        public void CalculateStar_PresetResultStar_NotOverwritten()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);
            state.ApplyDamage(true, 2); // 3 -> 1（calculateStar 会给 1 星）
            state.ApplyResultStar(2);   // 规则服务显式设置 2 星

            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(2, frozen.Star, "规则服务设置的星级优先，不被 calculateStar 覆盖。");
        }

        // ====================================================================
        // 结果字段稳定性测试（task 28 BattleResultDto）
        // ====================================================================

        [Test]
        [Description("结果保留稳定标量字段：gold/round/killCount/playerHp/opponentHp。"
            + " BattleResult.js:4-7 fromRuntime 字段映射。")]
        public void FrozenResult_PreservesStableScalarFields()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);
            state.ApplyBeginWave();
            state.ApplyBeginWave();
            state.ApplyGoldDelta(true, 40);
            state.ApplyEnemyKill();
            state.ApplyEnemyKill();
            state.ApplyEnemyKill();
            state.ApplyDamage(false, 1); // 对手 3 -> 2

            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(40, frozen.Gold, "gold=40。");
            Assert.AreEqual(2, frozen.Round, "round=2。");
            Assert.AreEqual(3, frozen.KillCount, "killCount=3。");
            Assert.AreEqual(BattleState.DefaultMaxHealth, frozen.PlayerTargetHealth,
                "playerTargetHealth=3（未受伤）。");
            Assert.AreEqual(2, frozen.OpponentTargetHealth, "opponentTargetHealth=2。");
        }

        [Test]
        [Description("结果排除 raw 与可变 object 集合：BattleResultDto 不含 weaponFragments/bj/raw。"
            + " task 28 排除约束。")]
        public void FrozenResult_ExcludesRawAndMutableObjectCollections()
        {
            var (_, _, _, builder) = CreateFixture();

            builder.TryFreeze(isWinCandidate: true, nowMs: 1000);

            BattleResultDto frozen = builder.FrozenResult.Value;

            // BattleResultDto 为 readonly struct，全部字段为标量/枚举，无 object 集合。
            // 编译期保证：DTO 定义中无 weaponFragments/bj/raw 字段。
            // 本测试通过反射验证字段类型全部为标量/枚举，不含可变集合。
            Type dtoType = typeof(BattleResultDto);
            System.Reflection.FieldInfo[] fields = dtoType.GetFields();
            CollectionAssert.IsNotEmpty(fields, "BattleResultDto 应有字段。");

            foreach (System.Reflection.FieldInfo field in fields)
            {
                Type fieldType = field.FieldType;
                bool isScalar = fieldType == typeof(bool)
                    || fieldType == typeof(int)
                    || fieldType == typeof(long)
                    || fieldType.IsEnum;
                Assert.IsTrue(isScalar,
                    $"字段 {field.Name} 类型 {fieldType.Name} 应为标量/枚举，排除可变 object 集合。");
            }
        }

        [Test]
        [Description("本期未启用字段使用明确零值或 Normal 语义："
            + "BossKillCount=0、EndlessRound=0、GameMode=Normal。"
            + " BattleResultDto task 28 约束。")]
        public void FrozenResult_UnusedFields_UseExplicitZeroOrNormal()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 100);
            state.ApplyBeginWave();

            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(0, frozen.BossKillCount, "本期 skipBoss，BossKillCount=0。");
            Assert.AreEqual(0, frozen.EndlessRound, "本期非无尽，EndlessRound=0。");
            Assert.AreEqual(BattleGameMode.Normal, frozen.GameMode, "本期 GameMode=Normal。");
        }

        [Test]
        [Description("ResultState 由 IsWin 派生一致：Win->Win、Lose->Lose。"
            + " BattleResultDto 构造时强制。")]
        public void ResultState_DerivedFromIsWin_Consistent()
        {
            var (_, _, _, winBuilder) = CreateFixture();
            winBuilder.TryFreeze(isWinCandidate: true, nowMs: 1000);
            BattleResultDto winResult = winBuilder.FrozenResult.Value;
            Assert.AreEqual(BattleResultState.Win, winResult.ResultState, "IsWin=true -> Win。");

            var (_, _, _, loseBuilder) = CreateFixture();
            loseBuilder.TryFreeze(isWinCandidate: false, nowMs: 1000);
            BattleResultDto loseResult = loseBuilder.FrozenResult.Value;
            Assert.AreEqual(BattleResultState.Lose, loseResult.ResultState, "IsWin=false -> Lose。");
        }

        // ====================================================================
        // 战斗时长计算测试（BattleResult.js:7 duration = max(0, now - startTime)）
        // ====================================================================

        [Test]
        [Description("战斗时长 = max(0, nowMs - startTimeMs)。"
            + " BattleResult.js:7 duration。")]
        public void BattleDuration_EqualsNowMinusStartTime()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 1000);

            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(4000, frozen.BattleDurationMs, "5000 - 1000 = 4000ms。");
        }

        [Test]
        [Description("未开始计时时长为 0。BattleResult.js:7 battle.startTime ? ... : 0。")]
        public void BattleDuration_NoStartTime_Zero()
        {
            var (_, _, _, builder) = CreateFixture();

            // 未调用 ApplyStartGame，StartTimeMs=0。
            builder.TryFreeze(isWinCandidate: true, nowMs: 5000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(0, frozen.BattleDurationMs, "未开始计时时长=0。");
        }

        [Test]
        [Description("nowMs 小于 startTimeMs 时长为 0（max 保护）。"
            + " BattleResult.js:7 Math.max(0, end - startTime)。")]
        public void BattleDuration_NowBeforeStart_Zero()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 5000);

            // nowMs < startTimeMs。
            builder.TryFreeze(isWinCandidate: true, nowMs: 1000);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(0, frozen.BattleDurationMs, "max(0, 1000-5000)=0。");
        }

        [Test]
        [Description("nowMs 默认 0 且已开始计时时长为 0（安全最小值）。")]
        public void BattleDuration_DefaultNow_ZeroWhenStarted()
        {
            var (state, _, _, builder) = CreateFixture();
            state.ApplyStartGame(nowMs: 1000);

            // 不传 nowMs，使用默认 0。
            builder.TryFreeze(isWinCandidate: true);

            BattleResultDto frozen = builder.FrozenResult.Value;
            Assert.AreEqual(0, frozen.BattleDurationMs, "nowMs=0 < startTimeMs=1000 -> 0。");
        }

        // ====================================================================
        // 构造与每局新建测试
        // ====================================================================

        [Test]
        [Description("构造拒绝 null readModel。")]
        public void Constructor_NullReadModel_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new BattleResultBuilder(null),
                "构造拒绝 null readModel。");
        }

        [Test]
        [Description("每局新建：两个独立 Builder 实例的冻结状态互不影响。"
            + " spec Restart creates clean per-battle state。")]
        public void NewInstance_PerBattle_Isolated()
        {
            var (state1, _, _, builder1) = CreateFixture();
            state1.ApplyStartGame(nowMs: 100);
            builder1.TryFreeze(isWinCandidate: true, nowMs: 5000);

            // 第二局：新建 Builder。
            var (state2, _, _, builder2) = CreateFixture();
            Assert.IsFalse(builder2.IsFrozen, "新局 Builder 未冻结。");
            Assert.IsFalse(builder2.FrozenResult.HasValue, "新局 Builder 无冻结结果。");

            // 新局独立冻结。
            state2.ApplyStartGame(nowMs: 200);
            state2.ApplyBeginWave();
            bool result = builder2.TryFreeze(isWinCandidate: false, nowMs: 6000);

            Assert.IsTrue(result, "新局首次冻结应成功。");
            Assert.IsFalse(builder2.FrozenResult.Value.IsWin, "新局判负。");
            Assert.IsTrue(builder1.IsFrozen, "旧局冻结状态不受新局影响。");
        }
    }
}
