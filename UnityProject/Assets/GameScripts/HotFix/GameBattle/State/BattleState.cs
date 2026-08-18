using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.7：BattleState —— 单局权威可变状态
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / State/BattleState.cs）：
    //   保存双方生命、金币、波次等权威可变状态；只由本局逻辑修改。
    //
    // 来源证据（BattleState.js:1-119）：
    //   还原工程 BattleState 持有 currentRound、gold、playerHealth、opponentHealth、
    //   killCount、bossKillCount、playerMaxHealth、opponentMaxHealth、initialGold、
    //   playerRecruitCost、opponentRecruitCost、opponentGold、opponentAttackMultiplier、
    //   isGameOver、contactOccurred、startTime、resultStar、endlessMode、maxRounds 等字段。
    //   setter 在修改 gold/health 时经 EventBus 发送 GOLD_CHANGED/HEALTH_CHANGED/BATTLE_FINISHED。
    //
    // 决策依据（design.md 决策 5 / spec battle-runtime-lifecycle / spec battle-simulation）：
    //   - 删除 SingletonBase、CombatServices（design 决策 5），状态改为强类型注入的
    //     独立对象，不再挂在全局 GameDataCore 单例上。
    //   - 状态修改只允许经规则服务提交（design.md 目录表 / task 3.7 验证要求）：
    //     BattleState 自身不公开 setter，只暴露只读属性；状态变更通过显式的
    //     Apply* 方法提交，这些方法由 BattleManager/WaveManager/BattleEconomy/
    //     BattleTarget 等规则服务在发生点同步调用。
    //   - 每局新建/销毁：重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新 State。
    //   - 事件边界（design.md 决策 4 / spec battle-event-boundary）：
    //     还原工程在 setter 中直接发送 EventBus 事件；C# 移植不再在 State 内直接
    //     发送全局事件。状态变更事实由规则服务经 BattleInternalSignalHub（低频局部）
    //     或 IBattleUiEvent（UI 事实）转发，避免 State 耦合全局总线。
    //
    // 不变量：
    //   1. 独占单局状态：本实例归属 BattleRuntime，不跨局复用。
    //   2. 状态修改只经 Apply* 方法：不公开 setter，外部只读。
    //   3. 生命非负：ApplyDamage 保证不低于 0，归零时不在此处直接冻结结果
    //      （结果冻结由 BattleResultBuilder.TryFreeze 唯一入口处理，spec battle-simulation
    //      "Battle result is frozen once"）。
    // ============================================================================

    /// <summary>
    /// 单局权威可变状态：双方生命、金币、波次、击杀数等。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md State/BattleState.cs）：</b>保存双方生命、金币、波次等
    /// 权威可变状态，只由本局逻辑修改。替代还原工程 <c>BattleState.js</c> 的全局实例
    /// （<c>BattleState.js:11-117</c>）。</para>
    ///
    /// <para><b>状态修改只允许经规则服务提交（task 3.7 验证要求）：</b>
    /// 本类型不公开 setter，只暴露只读属性。状态变更通过显式的 <c>Apply*</c> 方法提交，
    /// 这些方法由 <c>BattleManager</c>、<c>WaveManager</c>、<c>BattleEconomy</c>、
    /// <c>BattleTarget</c> 等规则服务在发生点同步调用。外部（如 Presenter）只读访问
    /// 经 <see cref="BattleReadModel"/>。</para>
    ///
    /// <para><b>不在 State 内发送全局事件（design.md 决策 4）：</b>
    /// 还原工程在 <c>gold</c>/<c>playerHealth</c>/<c>opponentHealth</c> setter 中直接
    /// 发送 <c>GOLD_CHANGED</c>/<c>HEALTH_CHANGED</c>/<c>BATTLE_FINISHED</c>
    /// （<c>BattleState.js:56-77</c>）。C# 移植不在 State 内直接发送全局事件；
    /// 事实转发由规则服务经 <c>BattleInternalSignalHub</c> 或 <c>IBattleUiEvent</c> 承担，
    /// 避免 State 耦合 TEngine <c>GameEvent</c> 总线。生命归零的胜负事实由
    /// <c>BattleResultBuilder.TryFreeze</c> 唯一入口冻结（spec "Battle result is frozen once"）。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 <see cref="BattleRuntimeFactory"/> 产生新 State。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部规则服务使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class BattleState
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>默认初始金币（对应 BattleState.js:19 initialGold=20）。</summary>
        public const int DefaultInitialGold = 20;

        /// <summary>默认招募刷新基础消耗（对应 BattleState.js:20-21 playerRecruitCost=10）。</summary>
        public const int DefaultRecruitCost = 10;

        /// <summary>默认最大生命（对应 BattleState.js:23,27 playerMaxHealth=3）。</summary>
        public const int DefaultMaxHealth = 3;

        /// <summary>默认最大波次（对应 BattleState.js:18 maxRounds=20）。</summary>
        public const int DefaultMaxRounds = 20;

        // ====================================================================
        // 只读权威状态属性
        // ====================================================================

        /// <summary>当前波次（玩家与 AI 共享，对应 currentRound）。从 0 开始，beginWave 时 +1。</summary>
        public int CurrentRound { get; private set; }

        /// <summary>是否无尽模式（本期固定 false，对应 endlessMode）。</summary>
        public bool EndlessMode { get; private set; }

        /// <summary>最大波次（对应 maxRounds=20）。</summary>
        public int MaxRounds { get; private set; }

        /// <summary>初始金币（对应 initialGold=20），startGame 时发放给双方。</summary>
        public int InitialGold { get; private set; }

        /// <summary>玩家方招募/刷新基础消耗（对应 playerRecruitCost=10，每次刷新 +2）。</summary>
        public int PlayerRecruitCost { get; private set; }

        /// <summary>对手方招募/刷新基础消耗（对应 opponentRecruitCost=10）。</summary>
        public int OpponentRecruitCost { get; private set; }

        /// <summary>玩家方最大生命（对应 playerMaxHealth=3）。</summary>
        public int PlayerMaxHealth { get; private set; }

        /// <summary>玩家方当前生命（对应 _playerHealth）。</summary>
        public int PlayerHealth { get; private set; }

        /// <summary>玩家方当前金币（对应 _gold）。</summary>
        public int PlayerGold { get; private set; }

        /// <summary>对手方最大生命（对应 opponentMaxHealth=3）。</summary>
        public int OpponentMaxHealth { get; private set; }

        /// <summary>对手方当前生命（对应 _opponentHealth）。</summary>
        public int OpponentHealth { get; private set; }

        /// <summary>对手方当前金币（对应 opponentGold）。</summary>
        public int OpponentGold { get; private set; }

        /// <summary>对手方攻击倍率（对应 opponentAttackMultiplier=1，本期固定 1）。</summary>
        public int OpponentAttackMultiplier { get; private set; }

        /// <summary>总击杀数（对应 killCount，startGame 时重置为 0）。</summary>
        public int KillCount { get; private set; }

        /// <summary>Boss 击杀数（对应 bossKillCount，本期 skipBoss 固定 0）。</summary>
        public int BossKillCount { get; private set; }

        /// <summary>是否已结束（对应 isGameOver）。</summary>
        public bool IsGameOver { get; private set; }

        /// <summary>是否已发生接触伤害（对应 contactOccurred，防止重复接触）。</summary>
        public bool ContactOccurred { get; private set; }

        /// <summary>战斗开始时间戳（毫秒，对应 startTime，用于结果时长计算）。</summary>
        public long StartTimeMs { get; private set; }

        /// <summary>结果星级（对应 resultStar，0~3）。</summary>
        public int ResultStar { get; private set; }

        /// <summary>标准战斗延迟是否启用（对应 standardBattleDelayEnabled=true）。</summary>
        public bool StandardBattleDelayEnabled { get; private set; }

        /// <summary>波间延迟时长（毫秒，对应 delayTime=10000）。</summary>
        public long DelayTimeMs { get; private set; }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造单局战斗状态，使用默认初始值。
        /// </summary>
        /// <remarks>
        /// 默认值对应还原工程 <c>BattleState.js:15-46</c> 构造函数初始化。
        /// 由 <see cref="BattleRuntimeFactory"/> 在每次 <c>Create</c> 时构造新实例。
        /// 后续配置快照接入后，初始值由 <c>BattleConfigSnapshot</c> 注入（task 3.3）。
        /// </remarks>
        internal BattleState()
        {
            CurrentRound = 0;
            EndlessMode = false;
            MaxRounds = DefaultMaxRounds;
            InitialGold = DefaultInitialGold;
            PlayerRecruitCost = DefaultRecruitCost;
            OpponentRecruitCost = DefaultRecruitCost;
            PlayerMaxHealth = DefaultMaxHealth;
            PlayerHealth = DefaultMaxHealth;
            PlayerGold = 0;
            OpponentMaxHealth = DefaultMaxHealth;
            OpponentHealth = DefaultMaxHealth;
            OpponentGold = 0;
            OpponentAttackMultiplier = 1;
            KillCount = 0;
            BossKillCount = 0;
            IsGameOver = false;
            ContactOccurred = false;
            StartTimeMs = 0;
            ResultStar = 0;
            StandardBattleDelayEnabled = true;
            DelayTimeMs = 10000;
        }

        // ====================================================================
        // 规则服务提交入口（Apply* 方法）
        // --------------------------------------------------------------------
        // 这些方法是状态变更的唯一合法入口。外部不得通过反射或引用修改字段。
        // 每个 Apply 方法的调用方都是 GameBattle 内部的规则服务：
        //   - BattleManager.startGame / beginWave / gameOver
        //   - BattleEconomy.spend / award / payRefresh
        //   - BattleTarget.applyDamage（受击）
        //   - EnemyManager.onEnemyDeath（击杀计数）
        // ====================================================================

        /// <summary>
        /// 开始一局：重置局内状态并发放初始金币（对应 BattleState.js:79-88 startGame）。
        /// </summary>
        /// <param name="nowMs">战斗开始时间戳（毫秒）。</param>
        /// <remarks>
        /// 对应还原工程 <c>BattleState.startGame()</c>：
        /// <c>isGameOver=false; contactOccurred=false; killCount=0; bossKillCount=0;
        /// resultStar=0; startTime=Date.now()</c>。
        /// 初始金币由 <c>BattleManager.startGame</c> 在调用本方法后通过
        /// <see cref="ApplyGoldDelta"/> 发放（<c>BattleManager.js:60-61</c>）。
        /// </remarks>
        internal void ApplyStartGame(long nowMs)
        {
            IsGameOver = false;
            ContactOccurred = false;
            KillCount = 0;
            BossKillCount = 0;
            ResultStar = 0;
            StartTimeMs = nowMs;
            DelayTimeMs = StandardBattleDelayEnabled ? 10000 : 0;
            // 金币发放由 BattleManager 经 ApplyGoldDelta 提交，此处不直接设置。
        }

        /// <summary>
        /// 进入新波次：波次 +1（对应 BattleManager.js:112 currentRound += 1）。
        /// </summary>
        /// <remarks>
        /// <para><b>legacy（任务 4.8）：</b>旧生产链的波次推进入口，保留供既有测试与
        /// 兼容路径使用。新生产链（有序波次状态机）不再经本方法推进——CurrentRound 由
        /// <see cref="ApplyWaveStarted"/> 按真实 WaveManager.CurrentOrder 同步。</para>
        /// </remarks>
        internal void ApplyBeginWave()
        {
            CurrentRound += 1;
        }

        /// <summary>
        /// 以所选有序波次计划初始化显示/统计轮数（任务 4.8）。
        /// </summary>
        /// <param name="planRowCount">计划行数（<c>plan.Rows.Count</c>）。</param>
        /// <remarks>
        /// <para><b>派生显示/统计值：</b><see cref="MaxRounds"/> 从此只作为计划行数的只读
        /// 显示/统计来源，绝不再参与波次推进或胜利判断（spec ordered-wave-plan
        /// "计划长度是显示轮数上限的唯一派生来源"）。CurrentRound 重置为 0，
        /// 之后由 <see cref="ApplyWaveStarted"/> 按真实 order 同步。</para>
        /// </remarks>
        internal void ApplyConfigurePlan(int planRowCount)
        {
            MaxRounds = Math.Max(0, planRowCount);
            CurrentRound = 0;
        }

        /// <summary>
        /// 同步当前波次到真实 <see cref="WaveManager.CurrentOrder"/>（任务 4.8）。
        /// </summary>
        /// <param name="order">波次状态机当前行 order（1-based）。</param>
        /// <remarks>
        /// <para>由 <see cref="BattleManager"/> 在收到 WaveManager.<c>WaveStarted(order)</c>
        /// 单次事实时调用，使 <see cref="CurrentRound"/> 与真实逐波计划保持一致；结果冻结前
        /// 再次以最后真实 order 调用，确保 ResultBuilder 读取的 round 为最后真实 order。
        /// 本方法不参与推进/成功判断。</para>
        /// </remarks>
        internal void ApplyWaveStarted(int order)
        {
            CurrentRound = Math.Max(1, order);
        }

        /// <summary>
        /// 应用金币增量（对应 BattleEconomy.js:22-23 setBalance / BattleManager.js:60 gold += initialGold）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="delta">金币增量（正为获得，负为消耗）。</param>
        /// <remarks>
        /// 由 <c>BattleEconomy.spend/award</c> 和 <c>BattleManager.startGame</c>
        /// （发放 initialGold）调用。金币可为负数（消耗），但结果不低于 0。
        /// </remarks>
        internal void ApplyGoldDelta(bool isPlayerSide, int delta)
        {
            if (isPlayerSide)
            {
                PlayerGold = Math.Max(0, PlayerGold + delta);
            }
            else
            {
                OpponentGold = Math.Max(0, OpponentGold + delta);
            }
        }

        /// <summary>
        /// 直接设置金币余额（对应 BattleEconomy.js:22 setBalance）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="value">目标余额（不低于 0）。</param>
        /// <remarks>
        /// 供 <c>BattleEconomy</c> 在 spend/award 后同步绝对值使用。优先使用
        /// <see cref="ApplyGoldDelta"/> 表达增量语义；本方法用于需要设置绝对值的场景。
        /// </remarks>
        internal void ApplyGoldSet(bool isPlayerSide, int value)
        {
            int clamped = Math.Max(0, value);
            if (isPlayerSide)
            {
                PlayerGold = clamped;
            }
            else
            {
                OpponentGold = clamped;
            }
        }

        /// <summary>
        /// 应用伤害到目标生命（对应 BattleState.js:56-77 playerHealth/opponentHealth setter）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方目标受击，false=对手方目标受击。</param>
        /// <param name="damage">伤害值（正数）。</param>
        /// <remarks>
        /// <para>由 <c>BattleTarget.applyDamage</c> 在受击发生点调用。</para>
        /// <para>生命不低于 0。归零时不在此处直接冻结结果——结果冻结由
        /// <c>BattleResultBuilder.TryFreeze</c> 唯一入口处理
        /// （spec "Battle result is frozen once"），避免多处触发完成事实。</para>
        /// <para>还原工程在 setter 中发送 <c>HEALTH_CHANGED</c>/<c>BATTLE_FINISHED</c>；
        /// C# 移植不在 State 内发送事件，事实转发由规则服务承担（design 决策 4）。</para>
        /// </remarks>
        internal void ApplyDamage(bool isPlayerSide, int damage)
        {
            if (damage <= 0)
            {
                return;
            }

            if (isPlayerSide)
            {
                PlayerHealth = Math.Max(0, PlayerHealth - damage);
            }
            else
            {
                // 对应 BattleState.js:72：standardBattleDelayEnabled 关闭时对手不受伤。
                if (!StandardBattleDelayEnabled)
                {
                    return;
                }
                OpponentHealth = Math.Max(0, OpponentHealth - damage);
            }
        }

        /// <summary>
        /// 记录一次击杀（对应 BattleManager 在敌人死亡时 killCount += 1）。
        /// </summary>
        internal void ApplyEnemyKill()
        {
            KillCount += 1;
        }

        /// <summary>
        /// 记录一次 Boss 击杀（spec "Boss reward and counters commit once"）。
        /// </summary>
        /// <remarks>
        /// <para>由 Boss 击杀回调在 Enemy 首次死亡边界恰好一次调用，与
        /// <see cref="ApplyEnemyKill"/> 并列：Boss 死亡同时 +1 KillCount 与 +1 BossKillCount。
        /// endpoint/forced/Settling 移除不触发。</para>
        /// </remarks>
        internal void ApplyBossKill()
        {
            BossKillCount += 1;
        }

        /// <summary>
        /// 标记接触伤害已发生（对应 contactOccurred=true，防止重复接触）。
        /// </summary>
        internal void ApplyContactOccurred()
        {
            ContactOccurred = true;
        }

        /// <summary>
        /// 设置招募刷新消耗（对应 BattleEconomy.js:36-37 playerRecruitCost += 2）。
        /// </summary>
        /// <param name="isPlayerSide">true=玩家方，false=对手方。</param>
        /// <param name="cost">新的刷新消耗。</param>
        internal void ApplyRecruitCost(bool isPlayerSide, int cost)
        {
            if (isPlayerSide)
            {
                PlayerRecruitCost = cost;
            }
            else
            {
                OpponentRecruitCost = cost;
            }
        }

        /// <summary>
        /// 设置结果星级（对应 resultStar，由 BattleResultBuilder 在冻结时依据剩余生命计算）。
        /// </summary>
        /// <param name="star">星级（0~3）。</param>
        internal void ApplyResultStar(int star)
        {
            ResultStar = Math.Max(0, Math.Min(3, star));
        }

        /// <summary>
        /// 结束一局：重置局间状态（对应 BattleState.js:90-116 gameOver）。
        /// </summary>
        /// <remarks>
        /// 对应还原工程 <c>BattleState.gameOver()</c>：重置招募消耗、波次、生命、金币、
        /// 接触标志、击杀数等。本方法在结果冻结后由 <c>BattleManager</c> 调用。
        /// 注意：<c>IsGameOver</c> 在原工程中被设为 false（gameOver 重置为初始），
        /// C# 保持一致语义；真正"已结束"事实由 <c>BattleRuntime.IsResultPublished</c> 表达。
        /// </remarks>
        internal void ApplyGameOver()
        {
            PlayerRecruitCost = DefaultRecruitCost;
            OpponentRecruitCost = DefaultRecruitCost;
            CurrentRound = 0;
            PlayerHealth = PlayerMaxHealth;
            PlayerGold = 0;
            OpponentHealth = OpponentMaxHealth;
            OpponentGold = 0;
            OpponentAttackMultiplier = 1;
            ContactOccurred = false;
            StartTimeMs = 0;
            KillCount = 0;
            BossKillCount = 0;
            ResultStar = 0;
            IsGameOver = false;
        }
    }
}
