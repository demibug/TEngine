using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Battle
{
    /// <summary>
    /// BattleEconomy 单元测试（task 3.8）。
    /// </summary>
    /// <remarks>
    /// <para>验证要求（task 3.8）：</para>
    /// <list type="bullet">
    /// <item>余额不足：TrySpend/TryPayRefresh/TryPayRecruit 在余额不足时返回失败，不修改状态。</item>
    /// <item>扣费成功：扣费后金币余额正确减少，spentGold 累计正确。</item>
    /// <item>退还补偿：Refund 正确退还金币，spentGold 递减。</item>
    /// <item>刷新费用递增：TryPayRefresh 成功后下次费用递增，refreshCount 累计。</item>
    /// <item>击杀奖励：Award 正确增加金币，kill 原因累计 killGold。</item>
    /// <item>快照：Snapshot 返回正确的只读快照。</item>
    /// <item>生命周期：StartGame 重置局内计数。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleEconomyTests
    {
        // ====================================================================
        // 辅助方法
        // ====================================================================

        /// <summary>
        /// 构造一个默认的 BattleState + BattleEconomy 组合，并发放初始金币。
        /// </summary>
        private static (BattleState state, BattleEconomy economy) CreateEconomy(
            int playerGold = 20,
            int opponentGold = 20,
            int refreshCostIncrement = 2)
        {
            var state = new BattleState();
            state.ApplyStartGame(nowMs: 0);
            state.ApplyGoldDelta(true, playerGold);
            state.ApplyGoldDelta(false, opponentGold);
            var economy = new BattleEconomy(state, refreshCostIncrement);
            economy.StartGame();
            return (state, economy);
        }

        // ====================================================================
        // 余额查询测试
        // ====================================================================

        [Test]
        [Description("GetBalance 返回玩家方与对手方金币余额。")]
        public void GetBalance_ReturnsCorrectSide()
        {
            var (state, economy) = CreateEconomy(playerGold: 30, opponentGold: 15);

            Assert.AreEqual(30, economy.GetBalance(true), "玩家方余额=30。");
            Assert.AreEqual(15, economy.GetBalance(false), "对手方余额=15。");
        }

        [Test]
        [Description("CanAfford 判断余额是否足够。")]
        public void CanAfford_ChecksBalance()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);

            Assert.IsTrue(economy.CanAfford(true, 5), "10 >= 5，可负担。");
            Assert.IsTrue(economy.CanAfford(true, 10), "10 >= 10，可负担。");
            Assert.IsFalse(economy.CanAfford(true, 11), "10 < 11，不可负担。");
            Assert.IsTrue(economy.CanAfford(true, -5), "负数视为 0，可负担。");
        }

        // ====================================================================
        // TrySpend 余额不足测试
        // ====================================================================

        [Test]
        [Description("TrySpend 余额不足时返回失败，不修改金币与 spentGold。")]
        public void TrySpend_InsufficientGold_ReturnsFailure_NoMutation()
        {
            var (state, economy) = CreateEconomy(playerGold: 5);
            int beforeGold = state.PlayerGold;
            EconomySnapshot beforeSnap = economy.Snapshot();

            EconomyResult result = economy.TrySpend(true, 10, "recruit");

            Assert.IsFalse(result.Success, "余额不足应返回失败。");
            Assert.AreEqual(EconomyFailureReason.InsufficientGold, result.FailureReason);
            Assert.AreEqual(10, result.Amount, "失败金额为请求金额。");
            Assert.AreEqual(beforeGold, state.PlayerGold, "金币未修改。");
            Assert.AreEqual(beforeSnap.SpentGold, economy.Snapshot().SpentGold, "spentGold 未修改。");
        }

        // ====================================================================
        // TrySpend 成功测试
        // ====================================================================

        [Test]
        [Description("TrySpend 成功时扣除金币并累计 spentGold。")]
        public void TrySpend_Success_DeductsAndAccumulates()
        {
            var (state, economy) = CreateEconomy(playerGold: 20);

            EconomyResult result = economy.TrySpend(true, 5, "recruit");

            Assert.IsTrue(result.Success, "余额充足应成功。");
            Assert.AreEqual(5, result.Amount, "扣除金额=5。");
            Assert.AreEqual(15, state.PlayerGold, "扣除后金币=15。");
            Assert.AreEqual(5, economy.Snapshot().SpentGold, "spentGold=5。");
        }

        [Test]
        [Description("TrySpend 负数金额视为 0，成功但不实际扣费。")]
        public void TrySpend_NegativeAmount_TreatedAsZero()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);

            EconomyResult result = economy.TrySpend(true, -5, "test");

            Assert.IsTrue(result.Success, "金额视为 0，应成功。");
            Assert.AreEqual(0, result.Amount, "金额钳制为 0。");
            Assert.AreEqual(10, state.PlayerGold, "金币不变。");
        }

        // ====================================================================
        // Refund 退还补偿测试
        // ====================================================================

        [Test]
        [Description("Refund 退还已扣金币，spentGold 递减（补偿回滚）。")]
        public void Refund_RestoresGold_AndDecreasesSpentGold()
        {
            var (state, economy) = CreateEconomy(playerGold: 20);

            economy.TrySpend(true, 8, "recruit");
            Assert.AreEqual(12, state.PlayerGold, "扣费后金币=12。");
            Assert.AreEqual(8, economy.Snapshot().SpentGold, "spentGold=8。");

            economy.Refund(true, 8, "recruit");

            Assert.AreEqual(20, state.PlayerGold, "退还后金币恢复为 20。");
            Assert.AreEqual(0, economy.Snapshot().SpentGold, "spentGold 恢复为 0。");
        }

        [Test]
        [Description("Refund 负数或零金额为空操作。")]
        public void Refund_ZeroOrNegative_NoOp()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);
            economy.TrySpend(true, 3, "recruit");
            int goldBefore = state.PlayerGold;

            economy.Refund(true, 0, "test");
            economy.Refund(true, -5, "test");

            Assert.AreEqual(goldBefore, state.PlayerGold, "零或负退还不修改金币。");
        }

        [Test]
        [Description("Refund 退还金额超过 spentGold 时 spentGold 钳制为 0。")]
        public void Refund_ExceedingSpentGold_ClampedToZero()
        {
            var (state, economy) = CreateEconomy(playerGold: 20);

            economy.TrySpend(true, 5, "recruit");
            economy.Refund(true, 100, "over-refund");

            Assert.AreEqual(0, economy.Snapshot().SpentGold, "spentGold 不低于 0。");
        }

        // ====================================================================
        // TryPayRefresh 刷新费用测试
        // ====================================================================

        [Test]
        [Description("TryPayRefresh 成功时扣除当前费用并递增下次费用。")]
        public void TryPayRefresh_Success_DeductsAndIncrements()
        {
            var (state, economy) = CreateEconomy(playerGold: 30, refreshCostIncrement: 2);
            // 默认刷新费用=10（BattleState.DefaultRecruitCost）。
            Assert.AreEqual(10, economy.GetRefreshCost(true), "初始刷新费用=10。");

            EconomyResult result = economy.TryPayRefresh(true);

            Assert.IsTrue(result.Success, "余额 30 >= 10，应成功。");
            Assert.AreEqual(10, result.Amount, "扣除当前费用 10。");
            Assert.AreEqual(12, result.NextRefreshCost, "下次费用=10+2=12。");
            Assert.AreEqual(20, state.PlayerGold, "扣除后金币=30-10=20。");
            Assert.AreEqual(12, economy.GetRefreshCost(true), "刷新费用递增为 12。");
            Assert.AreEqual(1, economy.GetRefreshCount(true), "刷新次数=1。");
        }

        [Test]
        [Description("TryPayRefresh 余额不足时返回失败，不递增费用、不累计次数。")]
        public void TryPayRefresh_InsufficientGold_NoMutation()
        {
            var (state, economy) = CreateEconomy(playerGold: 5, refreshCostIncrement: 2);
            int costBefore = economy.GetRefreshCost(true);

            EconomyResult result = economy.TryPayRefresh(true);

            Assert.IsFalse(result.Success, "余额 5 < 10，应失败。");
            Assert.AreEqual(costBefore, economy.GetRefreshCost(true), "费用未递增。");
            Assert.AreEqual(0, economy.GetRefreshCount(true), "次数未累计。");
            Assert.AreEqual(5, state.PlayerGold, "金币未修改。");
        }

        [Test]
        [Description("TryPayRefresh 连续刷新：费用每次递增。")]
        public void TryPayRefresh_Consecutive_EachIncrements()
        {
            var (state, economy) = CreateEconomy(playerGold: 100, refreshCostIncrement: 2);

            economy.TryPayRefresh(true);
            Assert.AreEqual(12, economy.GetRefreshCost(true), "第 1 次后费用=12。");

            economy.TryPayRefresh(true);
            Assert.AreEqual(14, economy.GetRefreshCost(true), "第 2 次后费用=14。");

            economy.TryPayRefresh(true);
            Assert.AreEqual(16, economy.GetRefreshCost(true), "第 3 次后费用=16。");
            Assert.AreEqual(3, economy.GetRefreshCount(true), "刷新次数=3。");
        }

        // ====================================================================
        // TryPayRecruit 招募费用测试
        // ====================================================================

        [Test]
        [Description("RecruitCost 使用 cardCost，不低于 1。")]
        public void RecruitCost_UsesCardCost_MinOne()
        {
            Assert.AreEqual(3, BattleEconomy.RecruitCost(3, 1), "cardCost=3 优先。");
            Assert.AreEqual(1, BattleEconomy.RecruitCost(0, 0), "都为 0 时返回 1。");
            Assert.AreEqual(1, BattleEconomy.RecruitCost(-1, 0), "负数时返回 1。");
        }

        [Test]
        [Description("RecruitCost cardCost<=0 时 fallback 到 cardLevel。")]
        public void RecruitCost_FallbackToLevel()
        {
            Assert.AreEqual(2, BattleEconomy.RecruitCost(0, 2), "cardCost=0 fallback 到 level=2。");
            Assert.AreEqual(5, BattleEconomy.RecruitCost(-1, 5), "cardCost=-1 fallback 到 level=5。");
        }

        [Test]
        [Description("TryPayRecruit 成功时扣除招募费用。")]
        public void TryPayRecruit_Success_Deducts()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);

            EconomyResult result = economy.TryPayRecruit(true, cardCost: 3, cardLevel: 1);

            Assert.IsTrue(result.Success, "余额 10 >= 3，应成功。");
            Assert.AreEqual(3, result.Amount, "扣除 3。");
            Assert.AreEqual(7, state.PlayerGold, "扣除后金币=7。");
            Assert.AreEqual("recruit", result.Reason, "原因为 recruit。");
        }

        [Test]
        [Description("TryPayRecruit 余额不足时返回失败。")]
        public void TryPayRecruit_InsufficientGold_Fails()
        {
            var (state, economy) = CreateEconomy(playerGold: 2);

            EconomyResult result = economy.TryPayRecruit(true, cardCost: 5, cardLevel: 1);

            Assert.IsFalse(result.Success, "余额 2 < 5，应失败。");
            Assert.AreEqual(2, state.PlayerGold, "金币未修改。");
        }

        // ====================================================================
        // Award 击杀奖励测试
        // ====================================================================

        [Test]
        [Description("Award kill 原因增加金币并累计 killGold。")]
        public void Award_KillReason_AccumulatesKillGold()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);

            economy.Award(true, 5, "kill");

            Assert.AreEqual(15, state.PlayerGold, "金币 10+5=15。");
            Assert.AreEqual(5, economy.Snapshot().KillGold, "killGold=5。");
        }

        [Test]
        [Description("Award 非 kill 原因增加金币但不累计 killGold。")]
        public void Award_NonKillReason_DoesNotAccumulateKillGold()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);

            economy.Award(true, 5, "battle");

            Assert.AreEqual(15, state.PlayerGold, "金币增加。");
            Assert.AreEqual(0, economy.Snapshot().KillGold, "非 kill 原因不累计 killGold。");
        }

        [Test]
        [Description("Award 零或负金额为空操作。")]
        public void Award_ZeroOrNegative_NoOp()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);
            int before = state.PlayerGold;

            economy.Award(true, 0, "kill");
            economy.Award(true, -5, "kill");

            Assert.AreEqual(before, state.PlayerGold, "金币不变。");
            Assert.AreEqual(0, economy.Snapshot().KillGold, "killGold 不变。");
        }

        [Test]
        [Description("Award 对手方增加对手金币。")]
        public void Award_OpponentSide_IncreasesOpponentGold()
        {
            var (state, economy) = CreateEconomy(playerGold: 10, opponentGold: 10);

            economy.Award(false, 7, "kill");

            Assert.AreEqual(17, state.OpponentGold, "对手金币 10+7=17。");
            Assert.AreEqual(7, economy.Snapshot().KillGold, "killGold=7。");
        }

        // ====================================================================
        // Snapshot 快照测试
        // ====================================================================

        [Test]
        [Description("Snapshot 返回当前经济状态的只读快照。")]
        public void Snapshot_ReflectsCurrentState()
        {
            var (state, economy) = CreateEconomy(playerGold: 30, opponentGold: 15);

            economy.TrySpend(true, 5, "recruit");
            economy.TryPayRefresh(true); // 扣 10，递增费用
            economy.Award(false, 3, "kill");

            EconomySnapshot snap = economy.Snapshot();

            Assert.AreEqual(15, snap.PlayerGold, "玩家金币=30-5-10=15。");
            Assert.AreEqual(18, snap.OpponentGold, "对手金币=15+3=18。");
            Assert.AreEqual(15, snap.SpentGold, "spentGold=5+10=15。");
            Assert.AreEqual(3, snap.KillGold, "killGold=3。");
            Assert.AreEqual(1, snap.PlayerRefreshCount, "玩家刷新次数=1。");
            Assert.AreEqual(0, snap.OpponentRefreshCount, "对手刷新次数=0。");
        }

        // ====================================================================
        // 生命周期测试
        // ====================================================================

        [Test]
        [Description("StartGame 重置 killGold/spentGold/refreshCount。")]
        public void StartGame_ResetsCounters()
        {
            var (state, economy) = CreateEconomy(playerGold: 30);

            economy.TrySpend(true, 5, "recruit");
            economy.TryPayRefresh(true);
            economy.Award(true, 3, "kill");

            // 重开：新建 state + economy。
            var state2 = new BattleState();
            state2.ApplyStartGame(nowMs: 0);
            state2.ApplyGoldDelta(true, 30);
            var economy2 = new BattleEconomy(state2, 2);
            economy2.StartGame();

            Assert.AreEqual(0, economy2.Snapshot().SpentGold, "新局 spentGold=0。");
            Assert.AreEqual(0, economy2.Snapshot().KillGold, "新局 killGold=0。");
            Assert.AreEqual(0, economy2.GetRefreshCount(true), "新局刷新次数=0。");
        }

        [Test]
        [Description("GameOver 不抛异常，保留钩子（对应 JS 空实现）。")]
        public void GameOver_DoesNotThrow()
        {
            var (state, economy) = CreateEconomy();
            Assert.DoesNotThrow(() => economy.GameOver());
        }

        // ====================================================================
        // 补偿回滚综合测试
        // ====================================================================

        [Test]
        [Description("扣费后退还：模拟创建失败时的补偿回滚，金币恢复到扣费前。")]
        public void Compensation_SpendThenRefund_RestoresToBefore()
        {
            var (state, economy) = CreateEconomy(playerGold: 20);
            int goldBefore = state.PlayerGold;

            // 模拟购买放置事务：扣费成功 → 创建失败 → 退还。
            EconomyResult spendResult = economy.TrySpend(true, 5, "recruit");
            Assert.IsTrue(spendResult.Success, "扣费应成功。");
            Assert.AreEqual(15, state.PlayerGold, "扣费后金币=15。");

            // 创建失败，补偿退还。
            economy.Refund(true, spendResult.Amount, "recruit");

            Assert.AreEqual(goldBefore, state.PlayerGold, "退还后金币恢复到扣费前。");
            Assert.AreEqual(0, economy.Snapshot().SpentGold, "spentGold 恢复为 0。");
        }

        [Test]
        [Description("刷新扣费后退还：模拟刷新后失败时的补偿回滚。")]
        public void Compensation_RefreshThenRefund_RestoresGold()
        {
            var (state, economy) = CreateEconomy(playerGold: 20, refreshCostIncrement: 2);
            int goldBefore = state.PlayerGold;

            EconomyResult refreshResult = economy.TryPayRefresh(true);
            Assert.IsTrue(refreshResult.Success, "刷新应成功。");
            Assert.AreEqual(10, state.PlayerGold, "刷新后金币=20-10=10。");

            // 补偿退还扣费金额。注意：刷新费用递增不回滚（仅退还金币）。
            economy.Refund(true, refreshResult.Amount, "refresh");

            Assert.AreEqual(goldBefore, state.PlayerGold, "退还后金币恢复。");
        }

        // ====================================================================
        // 对手方经济测试
        // ====================================================================

        [Test]
        [Description("对手方扣费/退还/刷新独立于玩家方。")]
        public void OpponentSide_Operations_Independent()
        {
            var (state, economy) = CreateEconomy(playerGold: 20, opponentGold: 20);

            economy.TrySpend(false, 3, "recruit");
            Assert.AreEqual(17, state.OpponentGold, "对手扣费后金币=17。");
            Assert.AreEqual(20, state.PlayerGold, "玩家金币不变。");

            economy.Refund(false, 3, "recruit");
            Assert.AreEqual(20, state.OpponentGold, "对手退还后金币恢复。");

            economy.TryPayRefresh(false);
            Assert.AreEqual(10, state.OpponentGold, "对手刷新后金币=20-10=10。");
            Assert.AreEqual(12, economy.GetRefreshCost(false), "对手刷新费用递增为 12。");
            Assert.AreEqual(1, economy.GetRefreshCount(false), "对手刷新次数=1。");
            Assert.AreEqual(0, economy.GetRefreshCount(true), "玩家刷新次数仍为 0。");
        }

        // ====================================================================
        // 确定性测试
        // ====================================================================

        [Test]
        [Description("相同操作序列在新建 Economy 上产生相同状态（确定性）。")]
        public void Deterministic_SameSequence_SameState()
        {
            // 第一局。
            var (state1, econ1) = CreateEconomy(playerGold: 30);
            econ1.TrySpend(true, 5, "recruit");
            econ1.TryPayRefresh(true);
            econ1.Award(false, 3, "kill");
            EconomySnapshot snap1 = econ1.Snapshot();

            // 第二局（重开）。
            var state2 = new BattleState();
            state2.ApplyStartGame(nowMs: 0);
            state2.ApplyGoldDelta(true, 30);
            state2.ApplyGoldDelta(false, 20);
            var econ2 = new BattleEconomy(state2, 2);
            econ2.StartGame();
            econ2.TrySpend(true, 5, "recruit");
            econ2.TryPayRefresh(true);
            econ2.Award(false, 3, "kill");
            EconomySnapshot snap2 = econ2.Snapshot();

            Assert.AreEqual(snap1.PlayerGold, snap2.PlayerGold, "玩家金币一致。");
            Assert.AreEqual(snap1.OpponentGold, snap2.OpponentGold, "对手金币一致。");
            Assert.AreEqual(snap1.SpentGold, snap2.SpentGold, "spentGold 一致。");
            Assert.AreEqual(snap1.KillGold, snap2.KillGold, "killGold 一致。");
            Assert.AreEqual(snap1.PlayerRefreshCount, snap2.PlayerRefreshCount, "刷新次数一致。");
        }
    }
}
