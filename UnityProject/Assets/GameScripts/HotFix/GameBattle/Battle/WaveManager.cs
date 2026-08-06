using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.9：WaveManager —— 确定性 Mob0 波次计划与唯一 ROUND_SPAWN_PREPARED(plan) 发布
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Battle/WaveManager.cs / specs/battle-simulation/spec.md
    //   / specs/battle-event-boundary/spec.md "Event signatures are unambiguous"）：
    //   生成确定性 Mob0 波次计划并按子步刷 Mob0；本期显式跳过 Boss。
    //   通过唯一 ROUND_SPAWN_PREPARED(plan) 签名发布波次准备完成事实，
    //   禁止 BattleManager 二次发布同名无参事件。
    //
    // 来源证据（WaveManager.js:1-46）：
    //   还原工程 WaveManager 继承 SingletonBase，持有 currentPlan/started/roundPlans/planHistory。
    //   configure 注入 gameData/enemyManager/bossManager/eventBus/randomSource/logger/skipBoss。
    //   planRound(round) 依据 waveUnitCounts 计算 normalCount，依据 bossWaveNumbers/
    //   bossSpawnChances/randomSource 决定 boss，生成 plan 并经 EventBus 发送
    //   GameEvents.ROUND_SPAWN_PREPARED(plan)。
    //   beginRound(round) 调 planRound 并在 boss=true 时触发 bossManager.spawn。
    //   spawnNormalPair(index,...) 调 enemyManager.spawn 生成一对 Mob0。
    //   skipBoss=true 时 boss 始终为 false，不读 bossWaveNumbers/bossSpawnChances，
    //   bossManager 从必填降为可选。
    //
    // 还原工程 BattleManager.js:111-129 _beginWave：
    //   currentRound += 1; eventBus.event(ROUND_STARTED);
    //   plan = waveManager.beginRound(currentRound); unitsThisWave = plan.normalCount;
    //   ... eventBus.event(ROUND_SPAWN_PREPARED);  // ← 无参二次发布
    //
    // C# 移植决策（spec battle-event-boundary "Event signatures are unambiguous"）：
    //   1. ROUND_SPAWN_PREPARED 只由 WaveManager 以带 plan 的唯一签名发布一次。
    //   2. BattleManager（task 3.10）禁止二次发布同名无参事件——它只消费 plan，
    //      不再自行 event(ROUND_SPAWN_PREPARED)。
    //   3. 本期内部通信使用直接回调（Action<WaveSpawnPlan>），不引入全局 EventBus
    //      （design 决策 4：内部一致性优先直接调用）。BattleInternalSignalHub（task 7.1）
    //      接入后改为局部信号，仍保持唯一带 plan 签名。
    //
    // 确定性要求（spec battle-simulation "Simulation is reproducible"）：
    //   - normalCount 只由配置 waveUnitCounts 与 round 确定，不依赖未声明随机。
    //   - skipBoss=true 时 boss 决策完全跳过，不消耗随机源。
    //   - plan 结果只由配置与 round 确定，相同配置+round 产生相同 plan。
    //
    // 不变量：
    //   1. 独占单局状态：currentPlan/roundPlans/planHistory 归属本实例，不跨局复用。
    //   2. ROUND_SPAWN_PREPARED(plan) 唯一签名：只发布一次、带 plan 参数。
    //   3. skipBoss 显式行为：skipBoss=true 时 boss 始终 false，不访问 boss 配置字段。
    //   4. normalCount 来自配置：不硬编码，不静默补默认值。
    // ============================================================================

    /// <summary>
    /// 确定性波次生成计划（不可变）。对应还原工程 WaveManager.js:38 plan 对象。
    /// </summary>
    /// <remarks>
    /// <para><b>确定性（spec "Simulation is reproducible"）：</b>
    /// 相同配置快照 + round 产生相同 plan。normalCount 由 <c>WaveUnitCounts</c> 确定，
    /// boss 由 <c>SkipBoss</c> + <c>BossWaveNumbers</c> + <c>BossSpawnChances</c> + 随机源确定
    /// （skipBoss=true 时 boss 恒 false，不消耗随机源）。</para>
    ///
    /// <para><b>唯一 ROUND_SPAWN_PREPARED(plan) 签名载荷：</b>
    /// 本对象作为 ROUND_SPAWN_PREPARED 事实的唯一参数传递给订阅者
    /// （spec "Event signatures are unambiguous"）。BattleManager 不再二次发布同名无参事件。</para>
    ///
    /// <para><b>字段对照（WaveManager.js:38）：</b>
    /// <list type="bullet">
    /// <item>round: 波次号（1-based）。</item>
    /// <item>normalCount: 该波 Mob0 生成数量，来自 waveUnitCounts。</item>
    /// <item>normalTypeIndex: Mob0 类型索引，来自 MapEnemyTypeIndex。</item>
    /// <item>boss: 是否为 Boss 波（skipBoss=true 时恒 false）。</item>
    /// <item>bossIndex: Boss 类型索引（boss=false 时为 -1）。</item>
    /// <item>bossKey: Boss 类型键（boss=false 时为 null）。</item>
    /// <item>bossSpawned: Boss 是否已生成（本期 skipBoss 固定 false）。</item>
    /// <item>spawnStrategyIndex: 选定的生成策略索引（用于确定性回放诊断）。</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal sealed class WaveSpawnPlan
    {
        /// <summary>波次号（1-based，对应 BattleState.CurrentRound）。</summary>
        public int Round { get; }

        /// <summary>该波 Mob0 生成数量（来自 WaveUnitCounts，endlessMode 下按公式外推）。</summary>
        public int NormalCount { get; }

        /// <summary>Mob0 敌人类型索引（来自 EnemyConfigSnapshot.MapEnemyTypeIndex）。</summary>
        public int NormalTypeIndex { get; }

        /// <summary>是否为 Boss 波。skipBoss=true 时恒 false。</summary>
        public bool Boss { get; }

        /// <summary>Boss 类型索引。boss=false 时为 -1。</summary>
        public int BossIndex { get; }

        /// <summary>Boss 类型键。boss=false 时为 null。</summary>
        public string BossKey { get; }

        /// <summary>Boss 是否已生成。本期 skipBoss 固定 false。</summary>
        public bool BossSpawned { get; internal set; }

        /// <summary>
        /// 选定的生成策略索引（来自 startGame 时 weightedIndex 选择）。
        /// 用于确定性回放诊断，不直接参与 normalCount 计算。
        /// </summary>
        public int SpawnStrategyIndex { get; }

        /// <summary>
        /// 构造不可变波次生成计划。
        /// </summary>
        /// <param name="round">波次号（1-based）。</param>
        /// <param name="normalCount">Mob0 生成数量。</param>
        /// <param name="normalTypeIndex">Mob0 类型索引。</param>
        /// <param name="boss">是否为 Boss 波。</param>
        /// <param name="bossIndex">Boss 类型索引（boss=false 时传 -1）。</param>
        /// <param name="bossKey">Boss 类型键（boss=false 时传 null）。</param>
        /// <param name="bossSpawned">Boss 是否已生成。</param>
        /// <param name="spawnStrategyIndex">选定的生成策略索引。</param>
        internal WaveSpawnPlan(
            int round,
            int normalCount,
            int normalTypeIndex,
            bool boss,
            int bossIndex,
            string bossKey,
            bool bossSpawned,
            int spawnStrategyIndex)
        {
            Round = round;
            NormalCount = normalCount;
            NormalTypeIndex = normalTypeIndex;
            Boss = boss;
            BossIndex = bossIndex;
            BossKey = bossKey;
            BossSpawned = bossSpawned;
            SpawnStrategyIndex = spawnStrategyIndex;
        }

        /// <summary>
        /// 返回计划的浅拷贝快照（用于 planHistory，防止外部修改 currentPlan 影响历史记录）。
        /// </summary>
        internal WaveSpawnPlan CloneSnapshot()
        {
            return new WaveSpawnPlan(
                Round, NormalCount, NormalTypeIndex, Boss, BossIndex, BossKey, BossSpawned,
                SpawnStrategyIndex);
        }
    }

    /// <summary>
    /// 确定性 Mob0 波次计划生成与刷怪管理器。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md Battle/WaveManager.cs）：</b>
    /// 生成确定性波次计划并按子步刷 Mob0；本期显式跳过 Boss。
    /// 替代还原工程 <c>WaveManager.js</c> 全局单例（<c>WaveManager.js:10-45</c>）。</para>
    ///
    /// <para><b>确定性波次计划（spec "Simulation is reproducible"）：</b>
    /// <see cref="PlanRound"/> 依据配置快照的 <c>WaveUnitCounts</c> 与 round 计算
    /// <c>normalCount</c>，不依赖未声明随机。skipBoss=true 时 boss 决策完全跳过。
    /// 相同配置 + round 产生相同 plan（boss 决策由随机源确定时，相同随机序列产生相同 boss 结果）。</para>
    ///
    /// <para><b>唯一 ROUND_SPAWN_PREPARED(plan) 签名（spec "Event signatures are unambiguous"）：</b>
    /// <see cref="PlanRound"/> 通过 <see cref="OnRoundSpawnPrepared"/> 回调发布一次带 plan 的事实。
    /// BattleManager（task 3.10）MUST NOT 二次发布同名无参事件——它只消费 plan，
    /// 不再自行调用 <c>OnRoundSpawnPrepared</c>。本期使用直接回调（design 决策 4：
    /// 内部一致性优先直接调用），BattleInternalSignalHub（task 7.1）接入后改为局部信号，
    /// 仍保持唯一带 plan 签名。</para>
    ///
    /// <para><b>显式 skipBoss 行为（WaveManager.js:27）：</b>
    /// skipBoss=true 时 boss 始终为 false，不读 <c>BossWaveNumbers</c>/<c>BossSpawnChances</c>，
    /// 不消耗随机源。Boss 生成入口（<see cref="BeginRound"/>）在 skipBoss 模式下不触发任何 Boss 操作。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新 WaveManager。
    /// <see cref="StartGame"/> 重置 currentPlan/roundPlans/planHistory。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BattleManager 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class WaveManager
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[WaveManager]";

        // ====================================================================
        // 配置依赖（不可变，构造时注入）
        // ====================================================================

        /// <summary>
        /// 本局波次配置快照（不可变）。来自 <see cref="BattleConfigSnapshot.Wave"/>。
        /// </summary>
        private readonly WaveConfigSnapshot _waveConfig;

        /// <summary>
        /// 本局敌人配置快照（不可变）。来自 <see cref="BattleConfigSnapshot.Enemy"/>，
        /// 用于读取 <c>MapEnemyTypeIndex</c> 作为 normalTypeIndex。
        /// </summary>
        private readonly EnemyConfigSnapshot _enemyConfig;

        /// <summary>
        /// 本局战斗状态。用于读取 <c>EndlessMode</c>/<c>MaxRounds</c> 判断无尽模式外推。
        /// </summary>
        private readonly BattleState _battleState;

        /// <summary>
        /// 随机源委托，用于 Boss 决策（skipBoss=true 时不调用）。
        /// 对应还原工程 WaveManager.js:12 randomSource 参数。
        /// 签约定：返回 [0,1) 区间 float，等价 JS Math.random()。
        /// </summary>
        private readonly Func<float> _randomSource;

        /// <summary>
        /// Boss 类型键列表，对应还原工程 EnemyFactory.js:75 BOSS_TYPE_KEYS。
        /// skipBoss=true 时不访问此列表。
        /// </summary>
        private static readonly string[] BossTypeKeys =
        {
            "ZhangLiang", "ZhangBao", "ZhangJiao", "SunShangXiang",
            "ZhenFu", "DiaoChan", "HuaXiong", "LvBu",
            "DongZhuo", "DianWei", "XiaHouDun", "CaoCao",
        };

        // ====================================================================
        // 运行时状态（单局可变）
        // ====================================================================

        /// <summary>
        /// 当前波次计划。null 表示尚未 beginRound 或已 gameOver。
        /// 对应还原工程 WaveManager.js:11 currentPlan。
        /// </summary>
        private WaveSpawnPlan _currentPlan;

        /// <summary>
        /// 是否已 startGame。对应还原工程 WaveManager.js:11 started。
        /// </summary>
        private bool _started;

        /// <summary>
        /// 各波次计划映射（round → plan），用于跨波次查询。
        /// 对应还原工程 WaveManager.js:11 roundPlans。
        /// </summary>
        private readonly Dictionary<int, WaveSpawnPlan> _roundPlans = new Dictionary<int, WaveSpawnPlan>();

        /// <summary>
        /// 计划历史（按 beginRound 顺序），用于诊断与回放。
        /// 对应还原工程 WaveManager.js:11 planHistory。
        /// </summary>
        private readonly List<WaveSpawnPlan> _planHistory = new List<WaveSpawnPlan>();

        /// <summary>
        /// Boss 决策缓存（round → bool），确保同一波次 Boss 决策只做一次。
        /// 对应还原工程 WaveManager.js:30 battle.bossDecisionByRound[round]。
        /// skipBoss=true 时不使用此缓存。
        /// </summary>
        private readonly Dictionary<int, bool> _bossDecisionByRound = new Dictionary<int, bool>();

        /// <summary>
        /// Boss 类型缓存（round → bossIndex），确保同一波次 Boss 类型只选一次。
        /// 对应还原工程 WaveManager.js:34 battle.bossTypeByRound[round]。
        /// skipBoss=true 时不使用此缓存。
        /// </summary>
        private readonly Dictionary<int, int> _bossTypeByRound = new Dictionary<int, int>();

        /// <summary>
        /// Boss 轮换索引。对应还原工程 WaveManager.js:34 data.bossRotationIndex。
        /// skipBoss=true 时不递增。
        /// </summary>
        private int _bossRotationIndex;

        /// <summary>
        /// 选定的生成策略索引。由 StartGame 时通过 weightedIndex 选择。
        /// 对应还原工程 BattleManager.js:67 strategyIndex。
        /// </summary>
        private int _spawnStrategyIndex;

        // ====================================================================
        // 事件回调（唯一 ROUND_SPAWN_PREPARED(plan) 发布入口）
        // ====================================================================

        /// <summary>
        /// ROUND_SPAWN_PREPARED(plan) 事实的唯一发布回调。
        /// </summary>
        /// <remarks>
        /// <para><b>唯一签名（spec "Event signatures are unambiguous"）：</b>
        /// 本回调是 ROUND_SPAWN_PREPARED 事实的唯一发布入口，携带 <see cref="WaveSpawnPlan"/> 参数。
        /// BattleManager（task 3.10）MUST NOT 二次发布同名无参事件。</para>
        /// <para><b>本期使用直接回调（design 决策 4）：</b>
        /// 内部一致性优先直接调用。BattleInternalSignalHub（task 7.1）接入后改为局部信号，
        /// 仍保持唯一带 plan 签名，不新增无参重载。</para>
        /// <para>可为 null：无订阅者时 <see cref="PlanRound"/> 仍生成计划，只跳过回调。</para>
        /// </remarks>
        public Action<WaveSpawnPlan> OnRoundSpawnPrepared { get; set; }

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>当前波次计划（null 表示尚未 beginRound 或已 gameOver）。</summary>
        public WaveSpawnPlan CurrentPlan => _currentPlan;

        /// <summary>是否已 startGame。</summary>
        public bool IsStarted => _started;

        /// <summary>计划历史只读视图（按 beginRound 顺序）。</summary>
        public IReadOnlyList<WaveSpawnPlan> PlanHistory => _planHistory;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造波次管理器。
        /// </summary>
        /// <param name="configSnapshot">本局不可变配置快照。</param>
        /// <param name="battleState">本局战斗状态。</param>
        /// <param name="randomSource">
        /// 随机源委托，返回 [0,1) float，用于 Boss 决策。
        /// skipBoss=true 时不调用，可传 null。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="configSnapshot"/> 或 <paramref name="battleState"/> 为 null。
        /// </exception>
        /// <remarks>
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在每次 Create 时构造新实例。
        /// 不跨局复用（spec "Restart creates clean per-battle state"）。</para>
        /// <para>randomSource 在 skipBoss=true 时可为 null；skipBoss=false 时 MUST 非 null，
        /// 否则 <see cref="PlanRound"/> 在 Boss 波次会抛 <see cref="InvalidOperationException"/>。</para>
        /// </remarks>
        internal WaveManager(
            BattleConfigSnapshot configSnapshot,
            BattleState battleState,
            Func<float> randomSource = null)
        {
            if (configSnapshot == null)
            {
                throw new ArgumentNullException(nameof(configSnapshot));
            }

            if (battleState == null)
            {
                throw new ArgumentNullException(nameof(battleState));
            }

            _waveConfig = configSnapshot.Wave
                ?? throw new InvalidOperationException("configSnapshot.Wave 不可为 null");
            _enemyConfig = configSnapshot.Enemy
                ?? throw new InvalidOperationException("configSnapshot.Enemy 不可为 null");
            _battleState = battleState;
            _randomSource = randomSource;
            _bossRotationIndex = 0;
            _spawnStrategyIndex = -1;
        }

        // ====================================================================
        // 生命周期
        // ====================================================================

        /// <summary>
        /// 开始一局：重置局内波次状态。
        /// 对应还原工程 WaveManager.js:19 startGame。
        /// </summary>
        /// <remarks>
        /// 重置 currentPlan=null、started=true、roundPlans 清空、planHistory 清空。
        /// 同时重置 bossDecisionByRound/bossTypeByRound/bossRotationIndex。
        /// 选定生成策略索引（对应 BattleManager.js:67 weightedIndex）。
        /// </remarks>
        /// <param name="spawnStrategyIndex">
        /// 由 BattleManager.startGame 通过 weightedIndex 选定的策略索引。
        /// 传入 -1 表示未选择（使用默认策略 0）。
        /// </param>
        internal void StartGame(int spawnStrategyIndex = -1)
        {
            _started = true;
            _currentPlan = null;
            _roundPlans.Clear();
            _planHistory.Clear();
            _bossDecisionByRound.Clear();
            _bossTypeByRound.Clear();
            _bossRotationIndex = 0;

            // 生成策略索引：由 BattleManager 在 startGame 时通过 weightedIndex 选择。
            // 对应 BattleManager.js:67-69：
            //   const strategyIndex = this.random.weightedIndex(spawnStrategyWeights);
            //   this.battleState.spawnStrategy = spawnStrategies[strategyIndex];
            // WaveManager 只记录索引用于 plan 诊断，不自行选择（权属在 BattleManager）。
            _spawnStrategyIndex = spawnStrategyIndex >= 0
                ? spawnStrategyIndex
                : 0;
        }

        /// <summary>
        /// 结束一局：重置波次状态。
        /// 对应还原工程 WaveManager.js:44 gameOver。
        /// </summary>
        internal void GameOver()
        {
            _started = false;
            _currentPlan = null;
            _roundPlans.Clear();
            _planHistory.Clear();
            _bossDecisionByRound.Clear();
            _bossTypeByRound.Clear();
            _bossRotationIndex = 0;
        }

        // ====================================================================
        // 波次计划生成（确定性）
        // ====================================================================

        /// <summary>
        /// 生成指定波次的确定性 Mob0 生成计划，并通过 <see cref="OnRoundSpawnPrepared"/> 发布唯一事实。
        /// 对应还原工程 WaveManager.js:20-40 planRound。
        /// </summary>
        /// <param name="round">波次号（1-based）。</param>
        /// <returns>不可变波次生成计划。</returns>
        /// <exception cref="InvalidOperationException">
        /// 波次号超出 waveUnitCounts 范围且非无尽模式，或 skipBoss=false 但 randomSource 为 null。
        /// </exception>
        /// <remarks>
        /// <para><b>确定性 normalCount 计算（WaveManager.js:23）：</b>
        /// <code>
        /// endlessMode &amp;&amp; round &gt; counts.Length
        ///   ? counts[counts.Length - 1] + 2 * (round - counts.Length)
        ///   : counts[Math.Min(round, counts.Length) - 1]
        /// </code>
        /// 非无尽模式只取 waveUnitCounts 内的值；无尽模式超出范围时按公式外推。</para>
        ///
        /// <para><b>显式 skipBoss 行为（WaveManager.js:27）：</b>
        /// skipBoss=true 时 boss 始终 false，不读 BossWaveNumbers/BossSpawnChances，
        /// 不消耗 randomSource。</para>
        ///
        /// <para><b>唯一 ROUND_SPAWN_PREPARED(plan) 发布：</b>
        /// 计划生成后通过 <see cref="OnRoundSpawnPrepared"/> 回调发布一次。
        /// BattleManager MUST NOT 二次发布同名无参事件（spec "Event signatures are unambiguous"）。</para>
        /// </remarks>
        internal WaveSpawnPlan PlanRound(int round)
        {
            // 计算 normalCount（确定性，来自 waveUnitCounts）
            int normalCount = ComputeNormalCount(round);

            // Boss 决策（skipBoss=true 时显式跳过）
            bool boss = false;
            int bossIndex = -1;
            string bossKey = null;

            if (!_waveConfig.SkipBoss)
            {
                // skipBoss=false 路径：按 bossWaveNumbers/bossSpawnChances/randomSource 决策
                // 对应 WaveManager.js:27-37
                if (_randomSource == null)
                {
                    throw new InvalidOperationException(
                        $"skipBoss=false 但 randomSource 为 null，round={round} 的 Boss 决策需要随机源");
                }

                int bossWaveIndex = IndexOfBossWave(round);
                if (bossWaveIndex >= 0)
                {
                    // Boss 决策缓存：同一波次只决策一次
                    if (!_bossDecisionByRound.TryGetValue(round, out bool decision))
                    {
                        float chance = _waveConfig.BossSpawnChances[bossWaveIndex];
                        decision = _randomSource() < chance;
                        _bossDecisionByRound[round] = decision;
                    }

                    boss = decision;
                    if (boss)
                    {
                        // Boss 类型选择（缓存的 bossIndex 优先）
                        if (_bossTypeByRound.TryGetValue(round, out int cachedIndex))
                        {
                            bossIndex = cachedIndex;
                        }
                        else
                        {
                            // 对应 WaveManager.js:34：
                            // bossIndex = map.mapIndex * 3 + data.bossRotationIndex
                            // 本期 mapIndex=0，故 bossIndex = bossRotationIndex
                            bossIndex = _bossRotationIndex % BossTypeKeys.Length;
                            _bossTypeByRound[round] = bossIndex;
                            _bossRotationIndex = (_bossRotationIndex + 1) % BossTypeKeys.Length;
                        }

                        bossKey = BossTypeKeys[bossIndex];
                        if (string.IsNullOrEmpty(bossKey))
                        {
                            throw new InvalidOperationException(
                                $"未知 Boss 类型索引 {bossIndex}，round={round}");
                        }
                    }
                }
            }

            // 构造不可变计划
            var plan = new WaveSpawnPlan(
                round: round,
                normalCount: normalCount,
                normalTypeIndex: _enemyConfig.MapEnemyTypeIndex,
                boss: boss,
                bossIndex: bossIndex,
                bossKey: bossKey,
                bossSpawned: false,
                spawnStrategyIndex: _spawnStrategyIndex);

            // 登记到 roundPlans 与 planHistory
            _roundPlans[round] = plan;
            _planHistory.Add(plan.CloneSnapshot());
            _currentPlan = plan;

            // 唯一 ROUND_SPAWN_PREPARED(plan) 发布
            // spec "Event signatures are unambiguous"：
            //   只使用一个已定义的签名发布该事实，不保留冲突重载。
            // BattleManager（task 3.10）禁止二次发布同名无参事件。
            OnRoundSpawnPrepared?.Invoke(plan);

            return plan;
        }

        /// <summary>
        /// 开始一个波次：生成计划并（boss=true 时）触发 Boss 生成。
        /// 对应还原工程 WaveManager.js:42 beginRound。
        /// </summary>
        /// <param name="round">波次号（1-based）。</param>
        /// <returns>该波次的生成计划。</returns>
        /// <remarks>
        /// <para>本期 skipBoss=true 时 BeginRound 只生成计划，不触发任何 Boss 操作。
        /// bossManager 在 skipBoss 模式下不注入（对应 JS 中 bossManager null-guard）。</para>
        /// <para>boss=true 且 plan.bossSpawned=false 时，应由 BattleManager 负责实际 Boss 生成
        /// （本期不实现 Boss 生成，task 3.10 BattleManager 接入时处理）。</para>
        /// </remarks>
        internal WaveSpawnPlan BeginRound(int round)
        {
            WaveSpawnPlan plan = PlanRound(round);

            // Boss 生成入口（skipBoss=true 时 boss 恒 false，此分支不执行）
            // 对应 WaveManager.js:42：
            //   if(plan.boss && !plan.bossSpawned && this.bossManager)
            //     this.bossManager.spawn(plan.bossKey, true); ... plan.bossSpawned = true;
            // 本期不注入 bossManager，Boss 生成由 BattleManager（task 3.10）负责。
            // 标记 bossSpawned 由 BattleManager 在实际生成后调用 MarkBossSpawned。

            return plan;
        }

        /// <summary>
        /// 标记 Boss 已生成。由 BattleManager 在 Boss 生成完成后调用。
        /// 对应还原工程 WaveManager.js:42 plan.bossSpawned=true。
        /// </summary>
        /// <param name="round">波次号。</param>
        internal void MarkBossSpawned(int round)
        {
            if (_currentPlan != null && _currentPlan.Round == round)
            {
                _currentPlan.BossSpawned = true;
            }

            // 同步到 planHistory 最后一条
            if (_planHistory.Count > 0)
            {
                WaveSpawnPlan last = _planHistory[_planHistory.Count - 1];
                if (last.Round == round)
                {
                    last.BossSpawned = true;
                }
            }
        }

        // ====================================================================
        // 确定性 normalCount 计算
        // ====================================================================

        /// <summary>
        /// 计算指定波次的 Mob0 生成数量。
        /// 对应还原工程 WaveManager.js:23。
        /// </summary>
        /// <param name="round">波次号（1-based）。</param>
        /// <returns>Mob0 生成数量。</returns>
        /// <exception cref="InvalidOperationException">
        /// 波次号超出 waveUnitCounts 范围且非无尽模式，或 waveUnitCounts 为空。
        /// </exception>
        private int ComputeNormalCount(int round)
        {
            IReadOnlyList<int> counts = _waveConfig.WaveUnitCounts;
            if (counts.Count == 0)
            {
                throw new InvalidOperationException(
                    "WaveUnitCounts 为空，无法计算 normalCount");
            }

            bool endlessMode = _battleState.EndlessMode;

            // 对应 WaveManager.js:23：
            //   endlessMode && round > counts.length
            //     ? counts[counts.length - 1] + 2 * (round - counts.length)
            //     : counts[Math.min(round, counts.length) - 1]
            if (endlessMode && round > counts.Count)
            {
                int last = counts[counts.Count - 1];
                return last + 2 * (round - counts.Count);
            }

            // 非无尽模式：取 waveUnitCounts[min(round, len) - 1]
            int index = Math.Min(round, counts.Count) - 1;
            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"波次号 {round} 非法，计算索引 {index} 为负");
            }

            int normalCount = counts[index];
            if (!IsFinite(normalCount))
            {
                throw new InvalidOperationException(
                    $"波次 {round} 的 normalCount={normalCount} 非有限值");
            }

            return normalCount;
        }

        /// <summary>
        /// 查找波次号在 BossWaveNumbers 中的索引。
        /// 对应还原工程 WaveManager.js:28 data.bossWaveNumbers.indexOf(round)。
        /// </summary>
        private int IndexOfBossWave(int round)
        {
            IReadOnlyList<int> bossWaves = _waveConfig.BossWaveNumbers;
            for (int i = 0; i < bossWaves.Count; i++)
            {
                if (bossWaves[i] == round)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 判断 int 值是否"有限"（非 int.MinValue/MaxValue 边界异常值）。
        /// 对应 JS Number.isFinite，C# int 不会产生 NaN/Infinity，
        /// 此处只防御极端哨兵值。
        /// </summary>
        private static bool IsFinite(int value)
        {
            return value != int.MinValue && value != int.MaxValue;
        }
    }
}
