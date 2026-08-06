using System;
using GameCommon.Battle;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.11：BattleResultBuilder —— 唯一结果冻结点
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / State/BattleResultBuilder.cs / design.md:170 胜负冻结）：
    //   在唯一结算点依据稳定优先级冻结一次最终结果 DTO。
    //
    // 核心契约（task 3.11 / specs/battle-runtime-lifecycle "Settling has no gameplay
    // damage authority" / specs/battle-parity-verification "Result freeze and Settling
    // quiescence are verified"）：
    //   1. 只冻结一次（幂等，决策 1.4 第一个完成事实胜出）：
    //      - 首次 TryFreeze 成功冻结 <see cref="BattleResultDto"/>；后续调用返回 false 且不修改结果。
    //      - 对应还原工程 BattleState.js:56-77 的 BATTLE_FINISHED 守卫逻辑——
    //        playerHealth<=0 触发 BATTLE_FINISHED(false)；opponentHealth<=0 触发
    //        BATTLE_FINISHED(true)；BattleManager.js:106 maxRounds 到达触发 BATTLE_FINISHED(true)。
    //        多处可能同时产生完成事实，但"第一个完成事实胜出"，其余被忽略。
    //   2. 冻结后无规则写权限：
    //      - 冻结后任何 Manager、到期动作和表现回调均无规则写权限。
    //      - 本类型通过 <see cref="IsFrozen"/> 暴露冻结状态，供 BattleSimulation 在检查点
    //        中止剩余迭代（BattleSimulation.cs:246 TryFreeze），供 BattleRuntime.EnterSettling
    //        执行静默清理（BattleRuntime.cs:338），供各 Manager 在写入口断言未冻结。
    //   3. 结果保留源 BattleResult 的稳定标量字段（task 28 BattleResultDto 已定义）：
    //      - 排除 raw 与可变 object 集合（weaponFragments、bj、raw）。
    //      - 未启用字段使用明确零值或 Normal 语义。
    //
    // 来源证据：
    //   - BattleResult.js:4-7：结果字段与 calculateStar 公式（hp>=max?3:hp>=ceil(max/2)?2:1; 失败=0）。
    //   - BattleResult.js:7 fromRuntime：battleDuration = max(0, now - startTime)；
    //     star = resultStar || calculateStar；gameMode = endlessMode?'endless':'normal'；
    //     endlessRound = endlessMode ? currentRound : 0。
    //   - BattleState.js:56-77：BATTLE_FINISHED 首个事实胜出守卫。
    //   - BattleManager.js:106：maxRounds 到达触发 BATTLE_FINISHED(true)。
    //
    // 决策依据：
    //   - design.md:284：统一 BattleResultBuilder.TryFreeze 只作为幂等入口，保持实际代码
    //     "第一个 BATTLE_FINISHED 事实胜出"，而不是重新批处理双方状态。
    //   - design.md:286 / 决策 0.4：首次 TryFreeze 成功后只完成当前同步提交并中止剩余
    //     phase/子步；TryFreeze 不在伤害调用栈内重入销毁 Manager 或集合。
    //   - 决策 1.4：BATTLE_FINISHED 幂等守卫"第一个完成事实胜出"。
    //
    // 不变量：
    //   1. 幂等：TryFreeze 首次成功返回 true；后续调用返回 false 且不修改已冻结结果。
    //   2. 冻结后只读：IsFrozen 为 true 后，TryFreeze、TryFreezeWith 均不修改结果，
    //      GetFrozenResult 返回首次冻结的不可变快照。
    //   3. 每局新建/销毁：不跨局复用，重开由 BattleRuntimeFactory 新建。
    //   4. 不持有可变集合：只持有 BattleReadModel 引用（只读视图）与单个可空结果快照。
    // ============================================================================

    /// <summary>
    /// 唯一结果冻结点：依据稳定优先级冻结一次最终结果 DTO，冻结后无规则写权限。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md State/BattleResultBuilder.cs / design.md:170 胜负冻结）：</b>
    /// 在唯一结算点依据稳定优先级冻结一次最终结果 DTO，替代还原工程
    /// <c>BattleState.js:56-77</c> 在 setter 中直接发送 <c>BATTLE_FINISHED</c>
    /// 与 <c>BattleResult.js:7 fromRuntime</c> 在多处构造结果的可能竞争。</para>
    ///
    /// <para><b>只冻结一次（幂等，决策 1.4 第一个完成事实胜出）：</b>
    /// <see cref="TryFreeze"/> 首次成功冻结 <see cref="BattleResultDto"/>；后续调用返回 false
    /// 且不修改已冻结结果。对应还原工程 <c>BATTLE_FINISHED</c> 守卫"第一个完成事实胜出"——
    /// playerHealth&lt;=0 触发判负、opponentHealth&lt;=0 触发判胜、maxRounds 到达触发判胜，
    /// 多处可能同时产生完成事实，但只有第一个调用成功冻结，其余被忽略。</para>
    ///
    /// <para><b>冻结后无规则写权限（task 3.11 / spec "Settling has no gameplay damage authority"）：</b>
    /// 冻结后任何 Manager、到期动作和表现回调均无规则写权限。本类型通过 <see cref="IsFrozen"/>
    /// 暴露冻结状态，供 <see cref="BattleSimulation"/> 在检查点中止剩余迭代
    /// （<see cref="BattleSimulation.TryFreeze"/>），供 <see cref="BattleRuntime.EnterSettling"/>
    /// 执行静默清理，供各 Manager 在写入口断言未冻结。</para>
    ///
    /// <para><b>结果保留源 BattleResult 的稳定标量字段（task 28 BattleResultDto 已定义）：</b>
    /// 排除 <c>raw</c> 与可变 <c>object</c> 集合（<c>weaponFragments</c>、<c>bj</c>、<c>raw</c>）。
    /// 未启用字段使用明确零值或 <see cref="BattleGameMode.Normal"/> 语义。</para>
    ///
    /// <para><b>不在伤害调用栈内重入销毁 Manager（决策 0.4 / design.md:286）：</b>
    /// <see cref="TryFreeze"/> 只冻结结果快照并置位标记，不直接销毁 Manager 或集合；
    /// 集合清理由 <see cref="BattleRuntime.EnterSettling"/> 在静默检查点统一执行。
    /// 调用方（如 <c>BattleTarget.applyDamage</c>）在 <see cref="TryFreeze"/> 返回后正常完成
    /// 当前同步提交，由 <see cref="BattleSimulation"/> 在紧随的检查点中止剩余迭代。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 <see cref="BattleRuntimeFactory"/> 产生新实例，
    /// 不复用旧局冻结状态。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部规则服务与 <see cref="BattleSimulation"/>
    /// 使用，不对其他程序集暴露。跨程序集的只读事实通过
    /// <see cref="GameCommon.Battle.BattleResultDto"/> 传递。</para>
    /// </remarks>
    internal sealed class BattleResultBuilder
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        /// <summary>
        /// 日志标签前缀，便于在日志中筛选结果冻结相关条目。
        /// </summary>
        private const string LogTag = "[BattleResultBuilder]";

        // ====================================================================
        // 只读依赖
        // ====================================================================

        /// <summary>
        /// 关联的只读状态视图。本类型只通过 <see cref="BattleReadModel.SnapshotResultInputs"/>
        /// 读取稳定标量，不直接持有或修改 <see cref="BattleState"/>。
        /// </summary>
        private readonly BattleReadModel _readModel;

        // ====================================================================
        // 冻结状态
        // ====================================================================

        /// <summary>
        /// 是否已冻结。首次 <see cref="TryFreeze"/> 成功后置位，之后不可逆。
        /// <para>供 <see cref="BattleSimulation"/> 在 <c>TryFreeze</c> 检查点判断是否中止
        /// （<see cref="BattleSimulation.TryFreeze"/> 调用本方法后据返回值置位
        /// <see cref="BattleSimulation.IsFrozen"/>），供各 Manager 在写入口断言未冻结。</para>
        /// </summary>
        public bool IsFrozen { get; private set; }

        /// <summary>
        /// 已冻结的结果快照。未冻结时为 null；首次冻结后不可变。
        /// <para>供 <see cref="BattleRuntime"/> 在 Settling 静默清理完成后发布一次
        /// <see cref="GameCommon.Battle.IBattlePublicEvent.OnBattleFinished"/>。</para>
        /// </summary>
        public BattleResultDto? FrozenResult { get; private set; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造结果冻结器，注入只读状态视图。
        /// </summary>
        /// <param name="readModel">
        /// 关联的只读状态视图（非 null）。本类型只通过
        /// <see cref="BattleReadModel.SnapshotResultInputs"/> 读取稳定标量，
        /// 不直接持有或修改 <see cref="BattleState"/>。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="readModel"/> 为 null。
        /// </exception>
        /// <remarks>
        /// 由 <see cref="BattleRuntimeFactory"/> 在每次 <c>Create</c> 时构造新实例，
        /// 保证每局冻结状态独立。本类型不跨局复用。
        /// </remarks>
        internal BattleResultBuilder(BattleReadModel readModel)
        {
            _readModel = readModel ?? throw new ArgumentNullException(nameof(readModel));
        }

        // ====================================================================
        // 冻结入口
        // --------------------------------------------------------------------
        // 本方法是结果冻结的唯一合法入口。BattleSimulation.TryFreeze 的
        // _tryFreezeHandler 回调指向本方法（task 2.11 连接点）。
        // ====================================================================

        /// <summary>
        /// 幂等结果冻结入口：依据当前只读状态判断完成事实并冻结一次最终结果。
        /// </summary>
        /// <param name="isWinCandidate">
        /// 调用方依据完成事实判断的胜负候选：
        /// <list type="bullet">
        /// <item><c>false</c>：玩家方目标生命归零（对应 BattleState.js:61 BATTLE_FINISHED(false)）。</item>
        /// <item><c>true</c>：对手方目标生命归零（对应 BattleState.js:76 BATTLE_FINISHED(true)），
        /// 或 maxRounds 到达（对应 BattleManager.js:106 BATTLE_FINISHED(true)）。</item>
        /// </list>
        /// 首次调用生效；后续调用（无论候选值）被忽略，保持"第一个完成事实胜出"。
        /// </param>
        /// <param name="nowMs">
        /// 冻结点的逻辑时间戳（毫秒），用于计算 <see cref="BattleResultDto.BattleDurationMs"/>。
        /// 对应还原工程 <c>BattleResult.js:7</c> 的 <c>now()</c>。默认 0 表示未提供逻辑时钟，
        /// 此时若 <see cref="BattleResultInputs.StartTimeMs"/> 大于 0，时长按 0 计（安全最小值）。
        /// 生产接线（后续 task）由调用方传入 <see cref="BattleSimulation.FrameNowMs"/> 或等价逻辑时间戳。
        /// </param>
        /// <returns>
        /// 是否首次冻结成功。已冻结时返回 false，不修改已冻结结果。
        /// </returns>
        /// <remarks>
        /// <para><b>幂等（决策 1.4 第一个完成事实胜出）：</b>
        /// 首次成功冻结返回 true；后续调用返回 false 且不修改 <see cref="FrozenResult"/>
        /// 与 <see cref="IsFrozen"/>。对应还原工程 <c>BATTLE_FINISHED</c> 守卫——
        /// 多处可能同时产生完成事实，但只有第一个调用成功冻结。</para>
        ///
        /// <para><b>不在伤害调用栈内重入销毁 Manager（决策 0.4 / design.md:286）：</b>
        /// 本方法只冻结结果快照并置位标记，不直接销毁 Manager 或集合。调用方
        /// （如 <c>BattleTarget.applyDamage</c>）在本方法返回后正常完成当前同步提交，
        /// 由 <see cref="BattleSimulation"/> 在紧随的检查点中止剩余迭代，由
        /// <see cref="BattleRuntime.EnterSettling"/> 在静默检查点统一清理集合。</para>
        ///
        /// <para><b>冻结后无规则写权限（task 3.11）：</b>
        /// 冻结后任何 Manager、到期动作和表现回调均无规则写权限。本方法置位
        /// <see cref="IsFrozen"/> 后，调用方通过 <see cref="IsFrozen"/> 断言拒绝迟到写入。</para>
        ///
        /// <para><b>结果保留稳定标量字段（task 28 BattleResultDto）：</b>
        /// 排除 <c>raw</c> 与可变 <c>object</c> 集合；未启用字段使用明确零值或 Normal 语义。</para>
        ///
        /// <para><b>逻辑时间源（决策 0.9）：</b>
        /// <paramref name="nowMs"/> 为逻辑时间戳，不使用真实时间（<c>Date.now()</c>/
        /// <c>Environment.TickCount64</c>）。调用方传入 <see cref="BattleSimulation.FrameNowMs"/>
        /// 或等价逻辑时钟，保证黄金轨迹可复现。</para>
        /// </remarks>
        internal bool TryFreeze(bool isWinCandidate, long nowMs = 0)
        {
            if (IsFrozen)
            {
                // 幂等：已冻结则忽略后续完成事实，保持"第一个完成事实胜出"。
                // 决策 1.4 / design.md:284。
                return false;
            }

            BattleResultInputs inputs = _readModel.SnapshotResultInputs();
            BattleResultDto result = BuildResult(isWinCandidate, nowMs, inputs);

            FrozenResult = result;
            IsFrozen = true;

            Log.Info(
                $"{LogTag} 首次冻结完成 isWin={result.IsWin} star={result.Star} " +
                $"round={result.Round} killCount={result.KillCount} " +
                $"playerHp={result.PlayerTargetHealth} opponentHp={result.OpponentTargetHealth} " +
                $"durationMs={result.BattleDurationMs}");

            return true;
        }

        // ====================================================================
        // 只读访问
        // ====================================================================

        /// <summary>
        /// 获取已冻结的结果快照。未冻结时抛出 <see cref="InvalidOperationException"/>。
        /// </summary>
        /// <returns>已冻结的不可变结果 DTO。</returns>
        /// <exception cref="InvalidOperationException">
        /// 尚未冻结（<see cref="IsFrozen"/> 为 false）。
        /// </exception>
        /// <remarks>
        /// 供 <see cref="BattleRuntime"/> 在 Settling 静默清理完成后发布一次
        /// <see cref="GameCommon.Battle.IBattlePublicEvent.OnBattleFinished"/>。
        /// 调用方 MUST 先检查 <see cref="IsFrozen"/> 或在确定已冻结后调用。
        /// </remarks>
        internal BattleResultDto GetFrozenResult()
        {
            if (!IsFrozen || !FrozenResult.HasValue)
            {
                throw new InvalidOperationException(
                    $"{LogTag} 尚未冻结结果，不能获取 FrozenResult。先调用 TryFreeze。");
            }

            return FrozenResult.Value;
        }

        // ====================================================================
        // 结果构建
        // --------------------------------------------------------------------
        // 依据 BattleResult.js:4-7 的字段映射与 calculateStar 公式构建不可变结果。
        // ====================================================================

        /// <summary>
        /// 依据完成事实候选与只读状态输入构建不可变结果 DTO。
        /// </summary>
        /// <param name="isWinCandidate">调用方依据完成事实判断的胜负候选。</param>
        /// <param name="nowMs">冻结点的逻辑时间戳（毫秒），用于计算战斗时长。</param>
        /// <param name="inputs">只读状态输入快照（来自 <see cref="BattleReadModel.SnapshotResultInputs"/>）。</param>
        /// <returns>不可变 <see cref="BattleResultDto"/>。</returns>
        /// <remarks>
        /// <para><b>字段映射（对应 BattleResult.js:4-7 fromRuntime）：</b>
        /// <list type="bullet">
        /// <item><c>isWin</c>：使用 <paramref name="isWinCandidate"/>（第一个完成事实的胜负判定）。</item>
        /// <item><c>star</c>：优先使用 <c>inputs.ResultStar</c>（若已由规则服务设置），
        /// 否则按 <c>calculateStar</c> 公式依据玩家方剩余生命比例计算。
        /// 对应 <c>BattleResult.js:6 calculateStar</c>：
        /// <c>失败=0；hp&gt;=max?3:hp&gt;=ceil(max/2)?2:1</c>。</item>
        /// <item><c>gold</c>：<c>inputs.PlayerGold</c>（结束时玩家金币快照）。</item>
        /// <item><c>battleDuration</c>：<c>max(0, now - startTime)</c>。
        /// <paramref name="nowMs"/> 为逻辑时间戳（决策 0.9），不使用真实时间；
        /// <paramref name="inputs"/>.<see cref="BattleResultInputs.StartTimeMs"/> 由
        /// <see cref="BattleState.ApplyStartGame"/> 在战斗开始时设置。</item>
        /// <item><c>round</c>：<c>inputs.CurrentRound</c>。</item>
        /// <item><c>playerTargetHealth</c>：<c>inputs.PlayerHealth</c>。</item>
        /// <item><c>opponentTargetHealth</c>：<c>inputs.OpponentHealth</c>。</item>
        /// <item><c>killCount</c>：<c>inputs.KillCount</c>。</item>
        /// <item><c>bossKillCount</c>：本期 skipBoss 固定 0（<c>inputs.BossKillCount</c>）。</item>
        /// <item><c>endlessRound</c>：<c>inputs.EndlessMode ? inputs.CurrentRound : 0</c>。
        /// 本期 <see cref="BattleState.EndlessMode"/> 固定 false，故为 0。</item>
        /// <item><c>gameMode</c>：<c>inputs.EndlessMode ? Endless : Normal</c>。本期固定 Normal。</item>
        /// </list></para>
        ///
        /// <para><b>排除 raw 与可变 object 集合（task 28）：</b>
        /// 不映射 <c>raw</c>、<c>weaponFragments</c>、<c>bj</c>，这些字段不进入稳定 DTO。</para>
        ///
        /// <para><b>星级计算公式（BattleResult.js:6）：</b>
        /// <code>
        /// if (!isWin) return 0;
        /// int max = max(1, playerMaxHealth);
        /// int hp = max(0, playerHealth);
        /// return hp &gt;= max ? 3 : hp &gt;= ceil(max / 2.0) ? 2 : 1;
        /// </code>
        /// 当 <paramref name="inputs"/>.<see cref="BattleResultInputs.ResultStar"/> 已为正数时
        /// 优先使用，避免在规则服务已设置星级后覆盖。对应 <c>BattleResult.js:7</c>
        /// <c>star: battle.resultStar || this.calculateStar(isWin, battle)</c>。</para>
        /// </remarks>
        private static BattleResultDto BuildResult(
            bool isWinCandidate,
            long nowMs,
            in BattleResultInputs inputs)
        {
            int star = CalculateStar(isWinCandidate, inputs);
            long battleDurationMs = ComputeBattleDurationMs(nowMs, inputs.StartTimeMs);

            BattleGameMode gameMode = inputs.EndlessMode
                ? BattleGameMode.Endless
                : BattleGameMode.Normal;

            int endlessRound = inputs.EndlessMode ? inputs.CurrentRound : 0;

            return new BattleResultDto(
                isWin: isWinCandidate,
                star: star,
                gold: inputs.PlayerGold,
                battleDurationMs: battleDurationMs,
                round: inputs.CurrentRound,
                playerTargetHealth: inputs.PlayerHealth,
                opponentTargetHealth: inputs.OpponentHealth,
                killCount: inputs.KillCount,
                bossKillCount: inputs.BossKillCount,
                endlessRound: endlessRound,
                gameMode: gameMode);
        }

        /// <summary>
        /// 依据剩余生命比例计算星级（对应 BattleResult.js:6 calculateStar）。
        /// </summary>
        /// <param name="isWin">是否胜利。失败固定 0 星。</param>
        /// <param name="inputs">只读状态输入快照。</param>
        /// <returns>星级（0~3）。</returns>
        /// <remarks>
        /// 公式：<c>失败=0；hp&gt;=max?3:hp&gt;=ceil(max/2)?2:1</c>。
        /// 若 <paramref name="inputs"/>.<see cref="BattleResultInputs.ResultStar"/> 已为正数
        /// （规则服务在冻结前已设置），优先使用，避免覆盖。
        /// 对应 <c>BattleResult.js:7 star: battle.resultStar || calculateStar(...)</c>。
        /// </remarks>
        private static int CalculateStar(bool isWin, in BattleResultInputs inputs)
        {
            if (!isWin)
            {
                // 失败固定 0 星。BattleResult.js:6 if(!isWin)return 0。
                return 0;
            }

            // 规则服务已设置正星级时不覆盖。BattleResult.js:7 resultStar || calculateStar。
            if (inputs.ResultStar > 0)
            {
                return inputs.ResultStar;
            }

            // BattleResult.js:6 calculateStar：
            //   const max = max(1, Number(battle.playerMaxHealth)||3);
            //   const hp = max(0, Number(battle.playerHealth)||0);
            //   return hp >= max ? 3 : hp >= ceil(max/2) ? 2 : 1;
            int max = Math.Max(1, inputs.PlayerMaxHealth);
            int hp = Math.Max(0, inputs.PlayerHealth);

            if (hp >= max)
            {
                return 3;
            }

            // Math.Ceiling(max / 2.0) 对应 JS Math.ceil(max/2)。
            int halfThreshold = (int)Math.Ceiling(max / 2.0);
            return hp >= halfThreshold ? 2 : 1;
        }

        /// <summary>
        /// 计算战斗时长（毫秒）。对应 BattleResult.js:7
        /// <c>duration = battle.startTime ? Math.max(0, end - battle.startTime) : 0</c>。
        /// </summary>
        /// <param name="nowMs">冻结点的逻辑时间戳（毫秒）。</param>
        /// <param name="startTimeMs">战斗开始时间戳（毫秒）。</param>
        /// <returns>max(0, nowMs - startTimeMs)。未开始或 nowMs 不足时为 0。</returns>
        /// <remarks>
        /// 以逻辑时间戳为源（决策 0.9），不使用真实时间。黄金轨迹对照以
        /// <c>startTime</c> 与冻结点逻辑时间戳差值为准。
        /// </remarks>
        private static long ComputeBattleDurationMs(long nowMs, long startTimeMs)
        {
            if (startTimeMs <= 0)
            {
                // 未开始计时，时长为 0。BattleResult.js:7 battle.startTime ? ... : 0。
                return 0;
            }

            long duration = nowMs - startTimeMs;
            return Math.Max(0, duration);
        }
    }
}
