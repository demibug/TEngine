using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.8：BattleEconomy —— 经济验证、扣费、退还、刷新费用计算
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Battle/BattleEconomy.cs）：
    //   处理招募、刷新、击杀奖励的校验与余额变更。
    //
    // 来源证据（BattleEconomy.js:1-51）：
    //   还原工程 BattleEconomy 持有 killGold/spentGold/refreshCount，委托 BattleState
    //   存储 gold/opponentGold/playerRecruitCost/opponentRecruitCost。核心方法：
    //     - balance(side): 返回玩家或对手金币余额
    //     - canAfford(side, amount): 余额是否足够
    //     - spend(side, amount, reason): 扣费，不足返回失败结果
    //     - payRefresh(side): 扣除当前刷新费用并递增下次费用（+refreshCostIncrement）
    //     - recruitCost(card): 计算招募费用（card.cost 或 card.level，不低于 1）
    //     - payRecruit(side, card): 扣除招募费用
    //     - award(side, amount, reason): 增加金币，kill 原因累计 killGold
    //     - snapshot(): 返回只读快照
    //
    // 决策依据：
    //   - 决策 0.8：购买放置命令携带 CommandId，预留-扣费-创建-放置是原子事务
    //     （task 77 完整事务，task 46 只做经济基础能力）。
    //   - design.md 决策 4 / spec battle-simulation "Input commands are atomic"：
    //     扣费失败时返回结构化结果，不抛异常；调用方（BattleInputController task 6.7）
    //     负责补偿回滚。
    //   - design.md 决策 5：删除 SingletonBase/CombatServices，经济服务改为强类型注入。
    //
    // 与 BattleState 的关系：
    //   BattleEconomy 不持有自己的金币余额副本，而是通过 BattleState.ApplyGoldDelta/
    //   ApplyGoldSet/ApplyRecruitCost 提交变更。这保证 BattleState 仍是唯一的权威状态根
    //   （task 3.7 不变量：状态修改只允许经 Apply* 方法提交）。
    //
    // 不变量：
    //   1. 独占单局状态：killGold/spentGold/refreshCount 归属本实例，不跨局复用。
    //   2. 金币变更经 BattleState.Apply* 提交，不直接修改 BattleState 字段。
    //   3. 余额不足时返回结构化失败结果，不抛异常，不修改任何状态。
    //   4. 刷新费用递增：每次 payRefresh 成功后，下次费用 += refreshCostIncrement。
    // ============================================================================

    /// <summary>
    /// 经济服务：处理招募、刷新、击杀奖励的校验与余额变更。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Battle/BattleEconomy.cs）：</b>处理招募、刷新、击杀奖励的
    /// 校验与余额变更。替代还原工程 <c>BattleEconomy.js</c> 全局服务
    /// （<c>BattleEconomy.js:7-51</c>）。</para>
    ///
    /// <para><b>与 BattleState 的关系（task 3.7 不变量）：</b>
    /// 本类型不持有自己的金币余额副本，而是通过 <see cref="BattleState.ApplyGoldDelta"/>/
    /// <see cref="BattleState.ApplyGoldSet"/>/<see cref="BattleState.ApplyRecruitCost"/>
    /// 提交变更，保证 BattleState 仍是唯一权威状态根。</para>
    ///
    /// <para><b>原子事务支持（决策 0.8 / spec "Input commands are atomic"）：</b>
    /// <see cref="TrySpend"/> 返回结构化结果，余额不足时不修改状态。
    /// <see cref="Refund"/> 提供补偿回滚入口，供 <c>BattleInputController</c>（task 6.7）
    /// 在创建/放置失败时逆序补偿扣费。本类型只提供基础能力，完整事务编排由 task 77 实现。</para>
    ///
    /// <para><b>每局新建/销毁：</b>重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新实例。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部规则服务使用。</para>
    /// </remarks>
    internal sealed class BattleEconomy
    {
        // ====================================================================
        // 配置（不可变，由 BattleConfigSnapshot.Economy 注入）
        // ====================================================================

        /// <summary>
        /// 刷新费用递增量（对应 BattleEconomy.js:36-37 的 +2，由配置注入）。
        /// </summary>
        private readonly int _refreshCostIncrement;

        // ====================================================================
        // 局内可变状态
        // ====================================================================

        /// <summary>
        /// 关联的权威状态（金币余额、刷新费用存储在 BattleState 中）。
        /// </summary>
        private readonly BattleState _state;

        /// <summary>
        /// 累计击杀奖励金币（对应 BattleEconomy.js:11 killGold）。
        /// </summary>
        private int _killGold;

        /// <summary>
        /// 累计消耗金币（对应 BattleEconomy.js:12 spentGold）。
        /// </summary>
        private int _spentGold;

        /// <summary>
        /// 玩家方刷新次数（对应 BattleEconomy.js:13 refreshCount.player）。
        /// </summary>
        private int _playerRefreshCount;

        /// <summary>
        /// 对手方刷新次数（对应 BattleEconomy.js:13 refreshCount.opponent）。
        /// </summary>
        private int _opponentRefreshCount;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造经济服务，注入权威状态与刷新递增配置。
        /// </summary>
        /// <param name="state">权威战斗状态（非 null），金币与刷新费用存储在其中。</param>
        /// <param name="refreshCostIncrement">
        /// 每次刷新成功后递增的费用量（对应 BattleEconomy.js:36 的 +2，
        /// 由 <see cref="EconomyConfigSnapshot.RefreshCostIncrement"/> 注入）。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> 为 null。</exception>
        /// <remarks>
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在每次 Create 时构造新实例。</para>
        /// <para>初始金币发放由 <c>BattleManager.startGame</c> 经
        /// <see cref="BattleState.ApplyGoldDelta"/> 提交，不由本类型负责。</para>
        /// </remarks>
        internal BattleEconomy(BattleState state, int refreshCostIncrement)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _refreshCostIncrement = refreshCostIncrement;
            _killGold = 0;
            _spentGold = 0;
            _playerRefreshCount = 0;
            _opponentRefreshCount = 0;
        }

        // ====================================================================
        // 只读查询
        // ====================================================================

        /// <summary>
        /// 获取指定方的当前金币余额（对应 BattleEconomy.js:21 balance）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <returns>当前金币余额。</returns>
        internal int GetBalance(bool isPlayerSide)
        {
            return isPlayerSide ? _state.PlayerGold : _state.OpponentGold;
        }

        /// <summary>
        /// 获取指定方的当前刷新费用（对应 BattleEconomy.js:23 refreshCost）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <returns>当前刷新费用。</returns>
        internal int GetRefreshCost(bool isPlayerSide)
        {
            return isPlayerSide ? _state.PlayerRecruitCost : _state.OpponentRecruitCost;
        }

        /// <summary>
        /// 判断指定方是否能负担指定金额（对应 BattleEconomy.js:24 canAfford）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="amount">金额（负数视为 0）。</param>
        /// <returns>余额 >= 金额时返回 true。</returns>
        internal bool CanAfford(bool isPlayerSide, int amount)
        {
            int clamped = Math.Max(0, amount);
            return GetBalance(isPlayerSide) >= clamped;
        }

        /// <summary>
        /// 获取指定方的刷新次数。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <returns>累计刷新次数。</returns>
        internal int GetRefreshCount(bool isPlayerSide)
        {
            return isPlayerSide ? _playerRefreshCount : _opponentRefreshCount;
        }

        // ====================================================================
        // 扣费 / 退还
        // ====================================================================

        /// <summary>
        /// 尝试扣除指定方的金币（对应 BattleEconomy.js:25-31 spend）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="amount">扣除金额（负数视为 0）。</param>
        /// <param name="reason">扣费原因（recruit/refresh/battle 等）。</param>
        /// <returns>
        /// 成功时 <see cref="EconomyResult.Success"/> 为 true 并记录金额与原因；
        /// 余额不足时为 false 且不修改任何状态。
        /// </returns>
        /// <remarks>
        /// <para><b>原子性（spec "Input commands are atomic"）：</b>
        /// 余额不足时直接返回失败，不修改 BattleState 或本实例的任何计数。</para>
        /// <para>扣费成功后累计 <c>_spentGold</c>，并通过
        /// <see cref="BattleState.ApplyGoldDelta"/> 提交到权威状态。</para>
        /// </remarks>
        internal EconomyResult TrySpend(bool isPlayerSide, int amount, string reason)
        {
            int clamped = Math.Max(0, amount);

            if (!CanAfford(isPlayerSide, clamped))
            {
                return EconomyResult.Failed(
                    EconomyFailureReason.InsufficientGold,
                    clamped,
                    reason,
                    "金币不足");
            }

            _state.ApplyGoldDelta(isPlayerSide, -clamped);
            _spentGold += clamped;

            return EconomyResult.Succeeded(clamped, reason);
        }

        /// <summary>
        /// 退还已扣除的金币（补偿回滚入口，对应原子事务失败时的逆序补偿）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="amount">退还金额（正数）。</param>
        /// <param name="reason">原扣费原因，用于日志。</param>
        /// <remarks>
        /// <para><b>补偿语义（决策 0.8 / spec "Unit creation fails after reservation"）：</b>
        /// 当扣费成功但后续步骤（创建/放置）失败时，调用方（BattleInputController task 6.7）
        /// 调用本方法退还已扣金币。退还金额为正数，通过
        /// <see cref="BattleState.ApplyGoldDelta"/> 提交。</para>
        /// <para>退还不会使 <c>_spentGold</c> 变为负数（钳制到 0）。</para>
        /// </remarks>
        internal void Refund(bool isPlayerSide, int amount, string reason)
        {
            int clamped = Math.Max(0, amount);
            if (clamped == 0)
            {
                return;
            }

            _state.ApplyGoldDelta(isPlayerSide, clamped);
            _spentGold = Math.Max(0, _spentGold - clamped);
        }

        // ====================================================================
        // 刷新费用
        // ====================================================================

        /// <summary>
        /// 支付刷新费用并递增下次费用（对应 BattleEconomy.js:32-40 payRefresh）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <returns>
        /// 成功时包含下次费用 <see cref="EconomyResult.NextRefreshCost"/>；
        /// 余额不足时返回失败，不修改状态。
        /// </returns>
        /// <remarks>
        /// <para>对应还原工程 <c>payRefresh</c>：扣除当前
        /// <c>playerRecruitCost/opponentRecruitCost</c>，成功后递增
        /// <c>+refreshCostIncrement</c>，并累计刷新次数。</para>
        /// </remarks>
        internal EconomyResult TryPayRefresh(bool isPlayerSide)
        {
            int cost = GetRefreshCost(isPlayerSide);
            EconomyResult result = TrySpend(isPlayerSide, cost, "refresh");

            if (!result.Success)
            {
                return result;
            }

            int nextCost = cost + _refreshCostIncrement;
            _state.ApplyRecruitCost(isPlayerSide, nextCost);

            if (isPlayerSide)
            {
                _playerRefreshCount += 1;
            }
            else
            {
                _opponentRefreshCount += 1;
            }

            return EconomyResult.SucceededWithNextCost(cost, "refresh", nextCost);
        }

        // ====================================================================
        // 招募费用
        // ====================================================================

        /// <summary>
        /// 计算卡牌招募费用（对应 BattleEconomy.js:41 recruitCost）。
        /// </summary>
        /// <param name="cardCost">卡牌费用。</param>
        /// <param name="cardLevel">卡牌等级（fallback，当 cardCost <= 0 时使用）。</param>
        /// <returns>招募费用，不低于 1。</returns>
        /// <remarks>
        /// 对应还原工程 <c>recruitCost(card)</c>：<c>Math.max(1, card.cost || card.level || 1)</c>。
        /// C# 移植拆分为显式参数，不依赖 card 对象。
        /// </remarks>
        internal static int RecruitCost(int cardCost, int cardLevel)
        {
            int cost = cardCost > 0 ? cardCost : (cardLevel > 0 ? cardLevel : 1);
            return Math.Max(1, cost);
        }

        /// <summary>
        /// 支付招募费用（对应 BattleEconomy.js:42 payRecruit）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="cardCost">卡牌费用。</param>
        /// <param name="cardLevel">卡牌等级（fallback）。</param>
        /// <returns>扣费结果。</returns>
        internal EconomyResult TryPayRecruit(bool isPlayerSide, int cardCost, int cardLevel)
        {
            int cost = RecruitCost(cardCost, cardLevel);
            return TrySpend(isPlayerSide, cost, "recruit");
        }

        // ====================================================================
        // 击杀奖励
        // ====================================================================

        /// <summary>
        /// 发放金币奖励（对应 BattleEconomy.js:43-48 award）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="amount">奖励金额（负数视为 0）。</param>
        /// <param name="reason">奖励原因（kill/battle 等），kill 原因累计 killGold。</param>
        /// <remarks>
        /// 对应还原工程 <c>award</c>：增加指定方金币，若原因为 kill 则累计 <c>killGold</c>。
        /// 金币通过 <see cref="BattleState.ApplyGoldDelta"/> 提交。
        /// </remarks>
        internal void Award(bool isPlayerSide, int amount, string reason)
        {
            int clamped = Math.Max(0, amount);
            if (clamped == 0)
            {
                return;
            }

            _state.ApplyGoldDelta(isPlayerSide, clamped);

            if (reason == "kill")
            {
                _killGold += clamped;
            }
        }

        // ====================================================================
        // 生命周期
        // ====================================================================

        /// <summary>
        /// 开始一局：重置局内经济计数（对应 BattleEconomy.js:15-20 startGame）。
        /// </summary>
        /// <remarks>
        /// 重置 killGold/spentGold/refreshCount。金币余额与刷新费用由
        /// <c>BattleManager.startGame</c> 经 BattleState.Apply* 设置，不由本方法负责。
        /// </remarks>
        internal void StartGame()
        {
            _killGold = 0;
            _spentGold = 0;
            _playerRefreshCount = 0;
            _opponentRefreshCount = 0;
        }

        /// <summary>
        /// 结束一局（对应 BattleEconomy.js:49 gameOver 空实现，保留钩子）。
        /// </summary>
        internal void GameOver()
        {
            // 还原工程 gameOver 为空操作，保留方法以对齐生命周期钩子。
        }

        // ====================================================================
        // 快照
        // ====================================================================

        /// <summary>
        /// 返回经济状态的只读快照（对应 BattleEconomy.js:50 snapshot）。
        /// </summary>
        /// <returns>不可变经济快照。</returns>
        internal EconomySnapshot Snapshot()
        {
            return new EconomySnapshot(
                playerGold: _state.PlayerGold,
                opponentGold: _state.OpponentGold,
                spentGold: _spentGold,
                killGold: _killGold,
                playerRefreshCount: _playerRefreshCount,
                opponentRefreshCount: _opponentRefreshCount);
        }
    }

    // ========================================================================
    // 经济操作结果与快照结构
    // ========================================================================

    /// <summary>
    /// 经济操作失败原因。
    /// </summary>
    internal enum EconomyFailureReason
    {
        /// <summary>无失败。</summary>
        None = 0,

        /// <summary>金币余额不足。</summary>
        InsufficientGold = 1,
    }

    /// <summary>
    /// 经济操作结果（扣费/刷新/招募），对应还原工程 spend/payRefresh 返回的 {success, amount, reason}。
    /// </summary>
    /// <remarks>
    /// <para>使用结构化结果而非异常表达正常校验失败（spec "Input commands are atomic"）。</para>
    /// <para>调用方据 <see cref="Success"/> 判断是否继续，失败时不修改任何状态。</para>
    /// </remarks>
    internal readonly struct EconomyResult
    {
        /// <summary>是否成功。</summary>
        public readonly bool Success;

        /// <summary>操作金额（成功时为实际扣除金额，失败时为请求金额）。</summary>
        public readonly int Amount;

        /// <summary>操作原因（recruit/refresh/battle 等）。</summary>
        public readonly string Reason;

        /// <summary>失败原因（成功时为 None）。</summary>
        public readonly EconomyFailureReason FailureReason;

        /// <summary>失败诊断信息（成功时为空）。</summary>
        public readonly string FailureMessage;

        /// <summary>下次刷新费用（仅 payRefresh 成功时有意义）。</summary>
        public readonly int NextRefreshCost;

        private EconomyResult(
            bool success,
            int amount,
            string reason,
            EconomyFailureReason failureReason,
            string failureMessage,
            int nextRefreshCost)
        {
            Success = success;
            Amount = amount;
            Reason = reason;
            FailureReason = failureReason;
            FailureMessage = failureMessage;
            NextRefreshCost = nextRefreshCost;
        }

        /// <summary>
        /// 构造成功结果。
        /// </summary>
        internal static EconomyResult Succeeded(int amount, string reason)
        {
            return new EconomyResult(
                success: true,
                amount: amount,
                reason: reason,
                failureReason: EconomyFailureReason.None,
                failureMessage: string.Empty,
                nextRefreshCost: 0);
        }

        /// <summary>
        /// 构造带下次费用的成功结果（用于 payRefresh）。
        /// </summary>
        internal static EconomyResult SucceededWithNextCost(int amount, string reason, int nextRefreshCost)
        {
            return new EconomyResult(
                success: true,
                amount: amount,
                reason: reason,
                failureReason: EconomyFailureReason.None,
                failureMessage: string.Empty,
                nextRefreshCost: nextRefreshCost);
        }

        /// <summary>
        /// 构造失败结果。
        /// </summary>
        internal static EconomyResult Failed(
            EconomyFailureReason reason,
            int amount,
            string operationReason,
            string message)
        {
            return new EconomyResult(
                success: false,
                amount: amount,
                reason: operationReason,
                failureReason: reason,
                failureMessage: message,
                nextRefreshCost: 0);
        }
    }

    /// <summary>
    /// 经济状态只读快照（对应 BattleEconomy.js:50 snapshot）。
    /// </summary>
    internal readonly struct EconomySnapshot
    {
        /// <summary>玩家方金币。</summary>
        public readonly int PlayerGold;

        /// <summary>对手方金币。</summary>
        public readonly int OpponentGold;

        /// <summary>累计消耗金币。</summary>
        public readonly int SpentGold;

        /// <summary>累计击杀奖励金币。</summary>
        public readonly int KillGold;

        /// <summary>玩家方刷新次数。</summary>
        public readonly int PlayerRefreshCount;

        /// <summary>对手方刷新次数。</summary>
        public readonly int OpponentRefreshCount;

        internal EconomySnapshot(
            int playerGold,
            int opponentGold,
            int spentGold,
            int killGold,
            int playerRefreshCount,
            int opponentRefreshCount)
        {
            PlayerGold = playerGold;
            OpponentGold = opponentGold;
            SpentGold = spentGold;
            KillGold = killGold;
            PlayerRefreshCount = playerRefreshCount;
            OpponentRefreshCount = opponentRefreshCount;
        }
    }
}
