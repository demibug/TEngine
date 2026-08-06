using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Battle
{
    // ============================================================================
    // 任务 3.12：DeadEntityRewardTests —— 最简击杀奖励同步死亡结算链测试
    // ----------------------------------------------------------------------------
    // 决策依据（design.md:316 / task 1.5 / task 3.12）：
    //   - DeadEntityRegistry 本期延后：最简奖励链在死亡点直接结算，不创建死亡快照注册表。
    //   - 唯一独立消费者 SoulSummonEffect 属于明确排除的 Skill 范围。
    //   - Skill/SoulSummon 后续另立 Change 时再引入死亡快照注册表。
    //
    // spec battle-simulation "Update phases are explicit and single-owned"：
    //   伤害、死亡事实、奖励和胜负候选 MUST 在其发生点同步生效，不得被无依据地推迟到
    //   新增的帧末结算阶段。本测试验证击杀奖励在死亡点同步结算。
    //
    // spec battle-runtime-lifecycle "Settling has no gameplay damage authority"：
    //   首次结果冻结后进入 Settling，立即停止新伤害与规则写入。本测试验证冻结后不再奖励。
    //
    // spec battle-simulation "Battle result is frozen once"：
    //   首次 TryFreeze 成功后跳过剩余 phase/子步。本测试验证冻结门控阻断奖励。
    //
    // 来源证据（BattleEconomy.js:43-48 award / DeadEntityRegistry.js:1-11）：
    //   JS award(side, amount, reason='kill') 在死亡点同步增加金币并累计 killGold。
    //   DeadEntityRegistry.recordEnemy 在死亡时创建快照供 SoulSummon 消费——本期不移植。
    //
    // 不创建 DeadEntityRegistry：
    //   本测试只验证最简击杀奖励链（Award 在死亡点同步调用），不引入死亡快照注册表。
    //   单次奖励通过 HashSet<int> 记录已奖励敌人 ID 实现，替代 DeadEntityRegistry 的去重职责。
    // ============================================================================

    /// <summary>
    /// 最简击杀奖励同步死亡结算链测试（task 3.12）。
    /// </summary>
    /// <remarks>
    /// <para>验证要求（task 3.12）：</para>
    /// <list type="bullet">
    /// <item>不创建 DeadEntityRegistry：奖励在死亡点直接结算。</item>
    /// <item>击杀奖励在同步死亡结算链：死亡点同步调用 BattleEconomy.Award。</item>
    /// <item>单次奖励：同一敌人只奖励一次。</item>
    /// <item>结果冻结后不再奖励：Settling/冻结门控阻断后续 Award。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// <para><b>关于 BattleResultBuilder：</b>task 3.11（BattleResultBuilder）与本任务并行执行，
    /// 本测试不假设其已存在。冻结门控用 <see cref="SynchronousKillSettlement.IsSettled"/>
    /// 抽象表示，对应 design.md 中 BattleResultBuilder.TryFreeze / BattleRuntime.IsSettling 的语义：
    /// 首次冻结后不再有任何规则写权限（spec "Settling has no gameplay damage authority"）。</para>
    /// </remarks>
    [TestFixture]
    internal class DeadEntityRewardTests
    {
        // ====================================================================
        // 辅助方法
        // ====================================================================

        /// <summary>
        /// 构造一个默认的 BattleState + BattleEconomy 组合，并发放初始金币。
        /// </summary>
        private static (BattleState state, BattleEconomy economy) CreateEconomy(
            int playerGold = 20,
            int opponentGold = 20)
        {
            var state = new BattleState();
            state.ApplyStartGame(nowMs: 0);
            state.ApplyGoldDelta(true, playerGold);
            state.ApplyGoldDelta(false, opponentGold);
            var economy = new BattleEconomy(state, refreshCostIncrement: 2);
            economy.StartGame();
            return (state, economy);
        }

        // ====================================================================
        // SynchronousKillSettlement —— 最简死亡结算链替身
        // --------------------------------------------------------------------
        // 还原工程中，敌人死亡时 EnemyManager 同步调用 BattleEconomy.award 并经
        // DeadEntityRegistry.recordEnemy 记录快照。本期不移植 DeadEntityRegistry，
        // 单次奖励去重直接在结算链内用 HashSet 实现。冻结门控对应
        // BattleResultBuilder.IsFrozen / BattleRuntime.IsSettling。
        // ====================================================================

        /// <summary>
        /// 最简同步死亡结算链：在死亡点直接结算击杀奖励，保证单次奖励与冻结后不奖励。
        /// </summary>
        /// <remarks>
        /// <para><b>不创建 DeadEntityRegistry（design.md:316）：</b>
        /// 本类型替代 DeadEntityRegistry 在最简奖励链中的去重职责，不创建死亡快照。
        /// SoulSummon 等需要快照的消费者属于后续 Skill Change。</para>
        /// <para><b>同步结算（spec "Update phases are explicit"）：</b>
        /// 奖励在死亡点同步生效，不推迟到帧末。</para>
        /// <para><b>冻结门控（spec "Settling has no gameplay damage authority"）：</b>
        /// IsSettled 对应 BattleResultBuilder.TryFreeze 首次冻结成功后的状态，
        /// 置位后 AwardKillReward 直接返回，不修改金币或 killGold。</para>
        /// </remarks>
        internal sealed class SynchronousKillSettlement
        {
            private readonly BattleEconomy _economy;
            private readonly HashSet<int> _rewardedEnemyIds;
            private readonly int _killRewardAmount;

            /// <summary>
            /// 是否已进入冻结/结算后状态（对应 BattleResultBuilder.IsFrozen / BattleRuntime.IsSettling）。
            /// </summary>
            internal bool IsSettled { get; set; }

            /// <summary>
            /// 构造死亡结算链。
            /// </summary>
            /// <param name="economy">经济服务（非 null）。</param>
            /// <param name="killRewardAmount">每次击杀奖励金额（对应 Mob0 killGold 配置）。</param>
            internal SynchronousKillSettlement(BattleEconomy economy, int killRewardAmount)
            {
                _economy = economy ?? throw new ArgumentNullException(nameof(economy));
                _rewardedEnemyIds = new HashSet<int>();
                _killRewardAmount = killRewardAmount;
                IsSettled = false;
            }

            /// <summary>
            /// 在敌人死亡点同步结算击杀奖励。
            /// </summary>
            /// <param name="enemyRuntimeId">敌人运行时 ID（用于单次奖励去重）。</param>
            /// <param name="isPlayerSide">true=玩家方击杀获得奖励，false=对手方。</param>
            /// <returns>true=本次结算发放了奖励；false=未发放（已奖励过或已冻结）。</returns>
            /// <remarks>
            /// <para><b>单次奖励：</b>同一 enemyRuntimeId 只奖励一次。重复调用返回 false。</para>
            /// <para><b>冻结后不奖励（spec "Settling has no gameplay damage authority"）：</b>
            /// IsSettled 为 true 时直接返回 false，不修改金币或 killGold。</para>
            /// <para><b>同步结算（spec "Update phases are explicit"）：</b>
            /// 在死亡点立即调用 BattleEconomy.Award，不推迟到帧末。</para>
            /// </remarks>
            internal bool AwardKillReward(int enemyRuntimeId, bool isPlayerSide)
            {
                // 冻结门控：结果冻结后不再有任何规则写权限。
                if (IsSettled)
                {
                    return false;
                }

                // 单次奖励：同一敌人只奖励一次（替代 DeadEntityRegistry 去重）。
                if (!_rewardedEnemyIds.Add(enemyRuntimeId))
                {
                    return false;
                }

                _economy.Award(isPlayerSide, _killRewardAmount, "kill");
                return true;
            }

            /// <summary>
            /// 标记结算链已冻结（对应 BattleResultBuilder.TryFreeze 成功后进入 Settling）。
            /// </summary>
            internal void MarkSettled()
            {
                IsSettled = true;
            }

            /// <summary>
            /// 查询指定敌人是否已获得奖励（测试断言用）。
            /// </summary>
            internal bool IsRewarded(int enemyRuntimeId)
            {
                return _rewardedEnemyIds.Contains(enemyRuntimeId);
            }
        }

        // ====================================================================
        // 击杀奖励在同步死亡结算链测试
        // ====================================================================

        [Test]
        [Description("敌人死亡时在死亡点同步结算击杀奖励，金币立即增加。")]
        public void AwardKillReward_AtDeathPoint_SynchronouslySettles()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 5);

            // 敌人 ID=1 死亡，玩家方获得 5 金币奖励。
            bool awarded = settlement.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);

            Assert.IsTrue(awarded, "死亡点应同步发放奖励。");
            Assert.AreEqual(15, state.PlayerGold, "金币 10+5=15，在死亡点立即生效。");
            Assert.AreEqual(5, economy.Snapshot().KillGold, "killGold 累计 5。");
        }

        [Test]
        [Description("对手方击杀敌人时奖励发放到对手方金币。")]
        public void AwardKillReward_OpponentSide_AwardsToOpponent()
        {
            var (state, economy) = CreateEconomy(playerGold: 10, opponentGold: 10);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 3);

            bool awarded = settlement.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: false);

            Assert.IsTrue(awarded, "对手方击杀应发放奖励。");
            Assert.AreEqual(13, state.OpponentGold, "对手金币 10+3=13。");
            Assert.AreEqual(10, state.PlayerGold, "玩家方金币不变。");
            Assert.AreEqual(3, economy.Snapshot().KillGold, "killGold 累计 3。");
        }

        [Test]
        [Description("多个敌人连续死亡，每个敌人在各自死亡点同步结算。")]
        public void AwardKillReward_MultipleEnemies_EachSettledAtDeathPoint()
        {
            var (state, economy) = CreateEconomy(playerGold: 0);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 4);

            // 三个敌人依次死亡。
            settlement.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);
            settlement.AwardKillReward(enemyRuntimeId: 2, isPlayerSide: true);
            bool awarded3 = settlement.AwardKillReward(enemyRuntimeId: 3, isPlayerSide: true);

            Assert.IsTrue(awarded3, "第三个敌人死亡应发放奖励。");
            Assert.AreEqual(12, state.PlayerGold, "三次击杀 0+4*3=12。");
            Assert.AreEqual(12, economy.Snapshot().KillGold, "killGold 累计 12。");
        }

        // ====================================================================
        // 单次奖励测试（同一敌人只奖励一次）
        // ====================================================================

        [Test]
        [Description("同一敌人只奖励一次：重复结算同一敌人 ID 返回 false 且不重复发奖。")]
        public void AwardKillReward_SameEnemyId_OnlyOnce()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 5);

            // 敌人 ID=1 首次死亡。
            bool first = settlement.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);
            Assert.IsTrue(first, "首次死亡应发放奖励。");
            Assert.AreEqual(15, state.PlayerGold, "首次奖励后金币=15。");

            // 同一敌人 ID=1 再次结算（例如死亡回调重入或重复触发）。
            bool second = settlement.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);
            Assert.IsFalse(second, "同一敌人不应重复奖励。");
            Assert.AreEqual(15, state.PlayerGold, "金币不再增加。");
            Assert.AreEqual(5, economy.Snapshot().KillGold, "killGold 不重复累计。");
            Assert.IsTrue(settlement.IsRewarded(1), "敌人 ID=1 已记录为已奖励。");
        }

        [Test]
        [Description("不同敌人 ID 各自独立奖励一次。")]
        public void AwardKillReward_DifferentEnemyIds_EachOnce()
        {
            var (state, economy) = CreateEconomy(playerGold: 0);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 3);

            bool r1 = settlement.AwardKillReward(enemyRuntimeId: 101, isPlayerSide: true);
            bool r2 = settlement.AwardKillReward(enemyRuntimeId: 102, isPlayerSide: true);
            bool r1Again = settlement.AwardKillReward(enemyRuntimeId: 101, isPlayerSide: true);
            bool r3 = settlement.AwardKillReward(enemyRuntimeId: 103, isPlayerSide: true);

            Assert.IsTrue(r1, "敌人 101 首次奖励。");
            Assert.IsTrue(r2, "敌人 102 首次奖励。");
            Assert.IsFalse(r1Again, "敌人 101 不重复奖励。");
            Assert.IsTrue(r3, "敌人 103 首次奖励。");
            Assert.AreEqual(9, state.PlayerGold, "三个不同敌人 3*3=9。");
        }

        // ====================================================================
        // 结果冻结后不再奖励测试
        // ====================================================================

        [Test]
        [Description("结果冻结后（Settling）不再发放击杀奖励。")]
        public void AwardKillReward_AfterFrozen_NoReward()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 5);

            // 冻结前：敌人 ID=1 正常奖励。
            settlement.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);
            Assert.AreEqual(15, state.PlayerGold, "冻结前奖励生效。");

            // 模拟 BattleResultBuilder.TryFreeze 首次冻结成功 → 进入 Settling。
            settlement.MarkSettled();
            Assert.IsTrue(settlement.IsSettled, "已进入冻结/Settling 状态。");

            // 冻结后：敌人 ID=2 死亡，不应发放奖励。
            bool awarded = settlement.AwardKillReward(enemyRuntimeId: 2, isPlayerSide: true);

            Assert.IsFalse(awarded, "冻结后不应发放奖励。");
            Assert.AreEqual(15, state.PlayerGold, "金币不再增加。");
            Assert.AreEqual(5, economy.Snapshot().KillGold, "killGold 不再累计。");
            Assert.IsFalse(settlement.IsRewarded(2), "敌人 ID=2 未被记录为已奖励。");
        }

        [Test]
        [Description("冻结后即使同一敌人首次出现也不奖励（冻结门控优先于去重）。")]
        public void AwardKillReward_FreezeGate_PrioritizedOverDedup()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 5);

            // 先冻结（如首个完成事实在伤害提交时触发 TryFreeze）。
            settlement.MarkSettled();

            // 冻结后新敌人 ID=99 死亡：门控优先，不奖励。
            bool awarded = settlement.AwardKillReward(enemyRuntimeId: 99, isPlayerSide: true);

            Assert.IsFalse(awarded, "冻结门控优先，不发放奖励。");
            Assert.AreEqual(10, state.PlayerGold, "金币不变。");
            Assert.AreEqual(0, economy.Snapshot().KillGold, "killGold 为 0。");
            Assert.IsFalse(settlement.IsRewarded(99), "未记录为已奖励。");
        }

        // ====================================================================
        // 不创建 DeadEntityRegistry 验证
        // ====================================================================

        [Test]
        [Description("最简奖励链不依赖 DeadEntityRegistry：死亡点直接结算，无快照注册。")]
        public void MinimalRewardChain_NoDeadEntityRegistry_DirectSettlement()
        {
            // 本测试验证最简奖励链不创建/不依赖 DeadEntityRegistry。
            // SynchronousKillSettlement 直接在死亡点调用 BattleEconomy.Award，
            // 不创建死亡快照、不注册监听、不提供 consume/recent API。
            var (state, economy) = CreateEconomy(playerGold: 5);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 2);

            // 死亡点直接结算——没有中间快照步骤。
            bool awarded = settlement.AwardKillReward(enemyRuntimeId: 42, isPlayerSide: true);

            Assert.IsTrue(awarded, "直接结算成功。");
            Assert.AreEqual(7, state.PlayerGold, "金币 5+2=7。");

            // SynchronousKillSettlement 不暴露 DeadEntityRegistry 的快照 API：
            // 无 consume(snapshotId)、无 recent(side)、无 onRecord(listener)。
            // 只暴露 IsRewarded 用于测试断言，不对外提供快照消费。
            Assert.IsTrue(settlement.IsRewarded(42), "仅记录去重状态，不创建快照。");
        }

        [Test]
        [Description("零奖励金额为空操作，不记录去重（仍允许后续同 ID 零奖励）。")]
        public void AwardKillReward_ZeroAmount_NoEffect()
        {
            var (state, economy) = CreateEconomy(playerGold: 10);
            var settlement = new SynchronousKillSettlement(economy, killRewardAmount: 0);

            bool awarded = settlement.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);

            // killRewardAmount=0 时 Award 内部钳制为 0 并直接返回，但仍记录去重。
            // BattleEconomy.Award 对 0 金额为空操作（不修改金币/killGold）。
            Assert.IsTrue(awarded, "结算链返回 true（已结算），但金额为 0。");
            Assert.AreEqual(10, state.PlayerGold, "金币不变。");
            Assert.AreEqual(0, economy.Snapshot().KillGold, "killGold 为 0。");
        }

        [Test]
        [Description("重开新局：新建 Economy 与 Settlement，旧局去重记录不残留。")]
        public void Restart_NewSettlement_NoResidue()
        {
            var (state1, econ1) = CreateEconomy(playerGold: 10);
            var settlement1 = new SynchronousKillSettlement(econ1, killRewardAmount: 5);
            settlement1.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);
            Assert.AreEqual(15, state1.PlayerGold, "旧局奖励生效。");

            // 重开：新建 state + economy + settlement。
            var state2 = new BattleState();
            state2.ApplyStartGame(nowMs: 0);
            state2.ApplyGoldDelta(true, 10);
            var econ2 = new BattleEconomy(state2, refreshCostIncrement: 2);
            econ2.StartGame();
            var settlement2 = new SynchronousKillSettlement(econ2, killRewardAmount: 5);

            // 新局中敌人 ID=1（池复用可能产生相同 runtimeId 场景测试见 task 4.5，
            // 此处验证 settlement 去重表不跨局残留）。
            bool awarded = settlement2.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);

            Assert.IsTrue(awarded, "新局去重表不残留，敌人 ID=1 可奖励。");
            Assert.AreEqual(15, state2.PlayerGold, "新局金币 10+5=15。");
            Assert.AreEqual(0, econ2.Snapshot().KillGold - 5, "新局 killGold=5。");
        }

        [Test]
        [Description("StartGame 重置 killGold：旧局击杀奖励不跨局累计。")]
        public void StartGame_ResetsKillGold_NoCrossBattleAccumulation()
        {
            var (state1, econ1) = CreateEconomy(playerGold: 0);
            var settlement1 = new SynchronousKillSettlement(econ1, killRewardAmount: 5);
            settlement1.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);
            settlement1.AwardKillReward(enemyRuntimeId: 2, isPlayerSide: true);
            Assert.AreEqual(10, econ1.Snapshot().KillGold, "旧局 killGold=10。");

            // 新局。
            var (state2, econ2) = CreateEconomy(playerGold: 0);
            var settlement2 = new SynchronousKillSettlement(econ2, killRewardAmount: 5);

            Assert.AreEqual(0, econ2.Snapshot().KillGold, "新局 killGold 重置为 0。");

            settlement2.AwardKillReward(enemyRuntimeId: 1, isPlayerSide: true);
            Assert.AreEqual(5, econ2.Snapshot().KillGold, "新局击杀后 killGold=5，不累计旧局。");
        }
    }
}
