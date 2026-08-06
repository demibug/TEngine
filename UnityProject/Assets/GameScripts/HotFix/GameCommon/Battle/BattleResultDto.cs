namespace GameCommon.Battle
{
    /// <summary>
    /// 不可变战斗结果 DTO（跨程序集公共契约）。
    /// </summary>
    /// <remarks>
    /// 归属：GameCommon 是跨程序集公共契约的唯一归属（见 specs/battle-event-boundary：
    /// 跨程序集事实使用 GameCommon 不可变 DTO + TEngine GameEvent）。本 DTO 由 GameBattle
    /// 的 BattleResultBuilder 在唯一结算点冻结一次（spec battle-runtime-lifecycle “Runtime
    /// quiescence and cleanup have one ordered owner”），经 IBattlePublicEvent.OnBattleFinished
    /// 发布；接收方无法通过它修改 GameBattle 内部状态。
    ///
    /// 字段来源：还原工程 src/battle/BattleResult.js 与 unity-export/config/battle-result-schema.json
    /// 的稳定标量字段。本 DTO 严格保留 schema 列出的稳定标量字段，并按 task 2.3 要求：
    /// - 排除 <c>raw</c>（可变 object，仅含 sceneName 等表现噪声，不属稳定事实）。
    /// - 排除 <c>weaponFragments</c>（Array&lt;object&gt; 可变 object 集合，接收方无法安全不可变持有）。
    /// - 排除 <c>bj</c>（JS 既有但未进入稳定 schema 的边界字段，避免越界保留）。
    ///
    /// 本期未启用字段使用明确零值或 Normal 语义（不得为 null 或隐式默认）：
    /// - BossKillCount：本期 skipBoss（spec），明确 0。
    /// - EndlessRound：本期 gameMode=Normal 而非 Endless，明确 0。
    /// - GameMode：明确 <see cref="BattleGameMode.Normal"/>（spec 本期不含无尽模式）。
    /// - ResultState：由 isWin 派生为 Win/Lose，构造时强制一致，不保留隐式默认。
    ///
    /// 不可变性：本结构为 readonly struct，全部字段为 readonly，构造后不可修改；
    /// 不持有任何可变集合或 object 引用。
    /// </remarks>
    public readonly struct BattleResultDto
    {
        /// <summary>是否胜利。</summary>
        public readonly bool IsWin;

        /// <summary>星级（0~3，由 BattleResult.calculateStar 依据剩余生命比例得出）。</summary>
        public readonly int Star;

        /// <summary>结束时玩家金币快照。</summary>
        public readonly int Gold;

        /// <summary>战斗时长（毫秒，对应 JS battleDuration）。0 表示未启用或未计时。</summary>
        public readonly long BattleDurationMs;

        /// <summary>结束时回合数（对应 JS round）。</summary>
        public readonly int Round;

        /// <summary>结束时玩家方目标剩余生命。</summary>
        public readonly int PlayerTargetHealth;

        /// <summary>结束时对手方目标剩余生命。</summary>
        public readonly int OpponentTargetHealth;

        /// <summary>击杀数（对应 JS killCount）。</summary>
        public readonly int KillCount;

        /// <summary>
        /// Boss 击杀数。本期 <see cref="WaveManager"/> 显式 skipBoss（spec），明确 0，
        /// 不使用隐式默认。
        /// </summary>
        public readonly int BossKillCount;

        /// <summary>
        /// 无尽模式回合数。本期 <see cref="GameMode"/> 为 Normal，明确 0。
        /// </summary>
        public readonly int EndlessRound;

        /// <summary>游戏模式。本期固定 <see cref="BattleGameMode.Normal"/>。</summary>
        public readonly BattleGameMode GameMode;

        /// <summary>结果状态。由 IsWin 派生，构造时强制一致。</summary>
        public readonly BattleResultState ResultState;

        /// <summary>
        /// 构造不可变战斗结果。ResultState 由 isWin 派生以保证一致，不接收外部覆盖。
        /// </summary>
        /// <param name="isWin">是否胜利。</param>
        /// <param name="star">星级。</param>
        /// <param name="gold">金币。</param>
        /// <param name="battleDurationMs">战斗时长（毫秒）。</param>
        /// <param name="round">回合数。</param>
        /// <param name="playerTargetHealth">玩家方目标剩余生命。</param>
        /// <param name="opponentTargetHealth">对手方目标剩余生命。</param>
        /// <param name="killCount">击杀数。</param>
        /// <param name="bossKillCount">Boss 击杀数（本期 0）。</param>
        /// <param name="endlessRound">无尽回合数（本期 0）。</param>
        /// <param name="gameMode">游戏模式（本期 Normal）。</param>
        public BattleResultDto(
            bool isWin,
            int star,
            int gold,
            long battleDurationMs,
            int round,
            int playerTargetHealth,
            int opponentTargetHealth,
            int killCount,
            int bossKillCount,
            int endlessRound,
            BattleGameMode gameMode)
        {
            IsWin = isWin;
            Star = star;
            Gold = gold;
            BattleDurationMs = battleDurationMs;
            Round = round;
            PlayerTargetHealth = playerTargetHealth;
            OpponentTargetHealth = opponentTargetHealth;
            KillCount = killCount;
            BossKillCount = bossKillCount;
            EndlessRound = endlessRound;
            GameMode = gameMode;
            // 由 isWin 派生 ResultState，保证一致，不保留隐式默认。
            ResultState = isWin ? BattleResultState.Win : BattleResultState.Lose;
        }

        /// <summary>
        /// 本期最简默认失败结果（normal 语义占位），供未实现结算路径或黄金基线对照使用。
        /// 生产结果必须由 BattleResultBuilder 依据真实事实冻结。
        /// </summary>
        public static BattleResultDto CreateMinimalDefault()
            => new BattleResultDto(
                isWin: false,
                star: 0,
                gold: 0,
                battleDurationMs: 0,
                round: 0,
                playerTargetHealth: 0,
                opponentTargetHealth: 0,
                killCount: 0,
                bossKillCount: 0,
                endlessRound: 0,
                gameMode: BattleGameMode.Normal);
    }

    /// <summary>
    /// 游戏模式枚举。本期仅 <see cref="Normal"/>；无尽模式后续另行引入。
    /// </summary>
    public enum BattleGameMode
    {
        /// <summary>正常模式（本期唯一支持）。</summary>
        Normal = 0,

        /// <summary>无尽模式（本期未启用，占位，不得在构造时使用）。</summary>
        Endless = 1,
    }

    /// <summary>
    /// 战斗结果状态枚举。由 IsWin 派生，与 schema resultState (WIN|LOSE) 对应。
    /// </summary>
    public enum BattleResultState
    {
        /// <summary>失败。</summary>
        Lose = 0,

        /// <summary>胜利。</summary>
        Win = 1,
    }
}
