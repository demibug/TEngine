using System;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.10：BattleManager —— 职责受限的战斗规则协调器
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Battle/BattleManager.cs / design.md:170）：
    //   管理波次运行态、战斗规则和胜负条件；不拥有框架 OnUpdate、Scene 或 UI。
    //
    // 职责边界（task 3.10 验证要求 / design.md 第 2 节）：
    //   - 只拥有规则状态、波次关联和胜负判断。
    //   - 不实现 TEngine IUpdateModule（不拥有框架 OnUpdate）。
    //   - 不实现时间拆步（500ms 截断、80ms 子步由 BattleSimulation 唯一负责）。
    //   - 不实现 Scene/UI/资源生命周期（由 BattleModule/BattleRuntime 负责）。
    //   - 引用 WaveManager（波次关联）、BattleEconomy（经济）、BattleResultBuilder
    //     （胜负冻结）、BattleState（权威状态）。
    //
    // 来源证据（BattleManager.js:20-253）：
    //   还原工程 BattleManager 继承 SingletonBase，由 GameLoop 字符串 callback Map 驱动。
    //   startGame 发放初始金币、选择生成策略、注册到 GameLoop。
    //   update(deltaMs) 内部顺序：_updateSpawnState → _updateUnitAttacks →
    //   AttackEffectManager.update。
    //   _updateSpawnState 管理 WAITING_TO_START → SPAWNING → WAITING_AFTER_WAVE 状态机，
    //   延迟 delayTime 后 beginWave，按 spawnInterval 刷怪，波间 interWaveDelay 后下一波，
    //   currentRound >= maxRounds 时 BATTLE_FINISHED(true)。
    //   _beginWave 调 waveManager.beginRound(currentRound) 获取 plan，设置 unitsThisWave。
    //   _spawnPairWhenDue 按 spawnInterval 调 waveManager.spawnNormalPair。
    //   gameOver 重置状态并通知 WaveManager/WeaponManager/AttackEffectManager。
    //
    // C# 移植决策（design.md 第 2 节 / 决策 0.1/0.4）：
    //   1. 删除 SingletonBase（design 决策 5），改为强类型注入的 internal 类。
    //   2. 不实现 IUpdateModule：由 BattleSimulation 在固定阶段回调本类型的方法，
    //      而非由 TEngine 框架直接轮询。更新阶段顺序由 BattleSimulation.PhaseOrder 冻结
    //      （BattleSimulation.cs:211-222）。
    //   3. 不自行拆步：deltaMs 由 BattleSimulation 以 stepMs 传入，本类型只消费时间，
    //      不截断或拆分。
    //   4. 胜负冻结经 BattleResultBuilder.TryFreeze 唯一入口（spec "Battle result is
    //      frozen once" / 决策 0.4）。本类型在生命归零、maxRounds 达成等完成事实发生点
    //      调用 TryFreeze，不自行直接发布 BATTLE_FINISHED。
    //   5. ROUND_SPAWN_PREPARED(plan) 只由 WaveManager 唯一发布（task 3.9 约束），
    //      本类型禁止二次发布同名无参事件（spec "Event signatures are unambiguous"）。
    //      本类型只消费 WaveManager.BeginRound 返回的 plan，不再自行 event。
    //
    // 决策 0.4 中止语义：
    //   首次 TryFreeze 成功后只完成当前同步提交并中止剩余 phase/子步。本类型在调用
    //   TryFreeze 后正常返回，不重入销毁集合；BattleSimulation 在紧随的检查点跳过当前
    //   子步后续 phase 与当前帧剩余子步，再由 BattleRuntime.EnterSettling 统一静默清理。
    //
    // 不变量：
    //   1. 独占单局状态：波次状态机字段归属本实例，不跨局复用。
    //   2. 不实现 IUpdateModule：不挂载到 ModuleSystem._updateExecuteList。
    //   3. 不拥有时间拆步：只消费 BattleSimulation 传入的 stepMs。
    //   4. 胜负只经 BattleResultBuilder.TryFreeze 冻结：不直接发布 BATTLE_FINISHED。
    //   5. ROUND_SPAWN_PREPARED 只由 WaveManager 发布：本类型不二次发布。
    // ============================================================================

    /// <summary>
    /// 职责受限的战斗规则协调器：管理波次运行态、战斗规则和胜负条件。
    /// </summary>
    /// <remarks>
    /// <para><b>职责受限（design.md:170 / task 3.10 验证要求）：</b>
    /// 本类型只拥有规则状态、波次关联和胜负判断。<b>不</b>实现 TEngine
    /// <c>IUpdateModule</c>（不拥有框架 <c>OnUpdate</c>），<b>不</b>实现时间拆步
    /// （500ms 截断、80ms 子步由 <see cref="BattleSimulation"/> 唯一负责），<b>不</b>实现
    /// Scene/UI/资源生命周期（由 <c>BattleModule</c>/<see cref="BattleRuntime"/> 负责）。</para>
    ///
    /// <para><b>更新驱动方式（design.md 第 2 节 / 决策 0.1）：</b>
    /// 还原工程 BattleManager 由 GameLoop 字符串 callback Map 驱动 <c>update(deltaMs)</c>。
    /// C# 移植改为由 <see cref="BattleSimulation"/> 在固定阶段
    /// （<see cref="BattleUpdatePhase.WaveSpawn"/>/<see cref="BattleUpdatePhase.UnitAttack"/>
    /// 等）回调本类型的方法。阶段顺序由 <c>BattleSimulation.PhaseOrder</c> 冻结
    /// （<c>BattleSimulation.cs:211-222</c>），本类型不自行决定阶段顺序。</para>
    ///
    /// <para><b>波次关联（design.md:171 / task 3.9）：</b>
    /// 本类型持有 <see cref="WaveManager"/> 并在波次状态机到达生成点时调用
    /// <see cref="WaveManager.BeginRound"/> 获取 <see cref="WaveSpawnPlan"/>。
    /// <see cref="WaveManager"/> 负责确定性 Mob0 波次计划与唯一
    /// <c>ROUND_SPAWN_PREPARED(plan)</c> 发布；本类型 <b>不</b>二次发布同名无参事件
    /// （spec "Event signatures are unambiguous"）。</para>
    ///
    /// <para><b>胜负判断（spec "Battle result is frozen once" / 决策 0.4）：</b>
    /// 本类型在生命归零、最大波次达成等完成事实发生点调用
    /// <see cref="BattleResultBuilder"/>.<c>TryFreeze</c> 唯一入口。首次冻结成功后只完成
    /// 当前同步提交并返回，<see cref="BattleSimulation"/> 在紧随的检查点中止剩余
    /// phase/子步，由 <see cref="BattleRuntime.EnterSettling"/> 统一静默清理。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新 BattleManager。
    /// <see cref="StartGame"/> 重置波次状态机。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 <see cref="BattleRuntime"/>
    /// 在阶段回调中调用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class BattleManager
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[BattleManager]";

        // ====================================================================
        // 波次运行态（对应 BattleManager.js:7-12 BattleManagerState）
        // ====================================================================

        /// <summary>
        /// 波次运行状态（对应还原工程 BattleManager.js:7-12 BattleManagerState）。
        /// </summary>
        /// <remarks>
        /// 还原工程使用 IDLE/WAITING_TO_START/SPAWNING/WAITING_AFTER_WAVE 四态。
        /// C# 移植保持同一状态机语义，由 <see cref="UpdateSpawnState"/> 驱动迁移。
        /// </remarks>
        private enum SpawnState
        {
            /// <summary>空闲：尚未 startGame 或已 gameOver（对应 IDLE=0）。</summary>
            Idle = 0,

            /// <summary>等待开始：startGame 后等待 delayTime 到期或双方放置完成（对应 WAITING_TO_START=1）。</summary>
            WaitingToStart = 1,

            /// <summary>生成中：波次进行中，按 spawnInterval 刷怪（对应 SPAWNING=2）。</summary>
            Spawning = 2,

            /// <summary>波次后等待：本波刷完，等待 interWaveDelay 后下一波（对应 WAITING_AFTER_WAVE=3）。</summary>
            WaitingAfterWave = 3,
        }

        // ====================================================================
        // 配置依赖（不可变，构造时注入）
        // ====================================================================

        /// <summary>
        /// 本局不可变配置快照。用于读取波次/经济/地图等配置参数。
        /// </summary>
        private readonly BattleConfigSnapshot _config;

        /// <summary>
        /// 波间延迟（毫秒，对应 BattleManager.js:24 interWaveDelayMs=5000）。
        /// </summary>
        private readonly long _interWaveDelayMs;

        /// <summary>
        /// 刷怪间隔（毫秒，对应 BattleManager.js:25 spawnIntervalMs=1500）。
        /// </summary>
        private readonly long _spawnIntervalMs;

        // ====================================================================
        // 规则服务依赖（强类型注入，替代还原工程 configure 字符串容器）
        // ====================================================================

        /// <summary>
        /// 权威战斗状态。由本类型经 Apply* 方法提交规则变更（生命归零、波次递增等）。
        /// </summary>
        private readonly BattleState _state;

        /// <summary>
        /// 波次管理器。提供确定性 <see cref="WaveSpawnPlan"/> 与唯一
        /// ROUND_SPAWN_PREPARED(plan) 发布。
        /// </summary>
        private readonly WaveManager _waveManager;

        /// <summary>
        /// 经济服务。由 <c>BattleInputController</c>（task 6.7）在输入事务中调用，
        /// 本类型只在 <see cref="StartGame"/> 中触发其 StartGame 生命周期钩子。
        /// </summary>
        private readonly BattleEconomy _economy;

        /// <summary>
        /// 结果冻结器。本类型在完成事实发生点调用其 <c>TryFreeze</c> 唯一入口。
        /// </summary>
        /// <remarks>
        /// <para><b>并行任务契约推断（task 49 同批次并行新建）：</b>
        /// BattleResultBuilder 由 task 49 并行新建，本类型按 design.md:163
        /// "State/BattleResultBuilder.cs：在唯一结算点依据稳定优先级冻结一次最终结果 DTO"
        /// 与 spec "Battle result is frozen once" 推断其契约：</para>
        /// <list type="bullet">
        /// <item><c>TryFreeze()</c>：幂等结果冻结入口，返回 bool（true=首次冻结成功）。</item>
        /// <item>首次成功后后续调用返回 false（幂等）。</item>
        /// <item>不在伤害调用栈内重入销毁 Manager 或集合（spec "Freeze occurs inside a manager update"）。</item>
        /// </list>
        /// <para>task 49 完成后若实际 API 与此推断冲突，由 task 49 或集成步骤修正调用方，
        /// 并按 SKILL 规则 4 在回答中说明冲突。</para>
        /// </remarks>
        private readonly BattleResultBuilder _resultBuilder;

        // ====================================================================
        // 波次运行态（单局可变）
        // ====================================================================

        /// <summary>当前波次运行状态。</summary>
        private SpawnState _spawnState = SpawnState.Idle;

        /// <summary>
        /// 当前波次内累计时间（毫秒，对应 BattleManager.js:26 elapsedMs）。
        /// 用于判断 delayTime 到期、spawnInterval 到期、interWaveDelay 到期。
        /// </summary>
        private long _elapsedMs;

        /// <summary>
        /// 当前波次已刷怪索引（对应 BattleManager.js:27 spawnIndex）。
        /// </summary>
        private int _spawnIndex;

        /// <summary>
        /// 当前波次应刷怪总数（对应 BattleManager.js:28 unitsThisWave）。
        /// 由 <see cref="WaveSpawnPlan.NormalCount"/> 设置。
        /// </summary>
        private int _unitsThisWave;

        /// <summary>
        /// 是否已 startGame（对应 BattleManager.js:31 started）。
        /// </summary>
        private bool _started;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造职责受限的战斗规则协调器。
        /// </summary>
        /// <param name="config">本局不可变配置快照（非 null）。</param>
        /// <param name="state">权威战斗状态（非 null）。</param>
        /// <param name="waveManager">波次管理器（非 null）。</param>
        /// <param name="economy">经济服务（非 null）。</param>
        /// <param name="resultBuilder">结果冻结器（非 null）。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        /// <remarks>
        /// <para>由 <see cref="BattleRuntimeFactory"/> 在每次 Create 时构造新实例。
        /// 不跨局复用（spec "Restart creates clean per-battle state"）。</para>
        /// <para><b>职责受限声明（task 3.10 验证要求）：</b>
        /// 本构造函数不注册任何 TEngine 模块更新回调、不创建 Scene/UI 对象、
        /// 不加载资源、不启动任何 Timer。本类型只持有规则服务引用与波次状态。</para>
        /// </remarks>
        internal BattleManager(
            BattleConfigSnapshot config,
            BattleState state,
            WaveManager waveManager,
            BattleEconomy economy,
            BattleResultBuilder resultBuilder)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _waveManager = waveManager ?? throw new ArgumentNullException(nameof(waveManager));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _resultBuilder = resultBuilder ?? throw new ArgumentNullException(nameof(resultBuilder));

            // 波间延迟与刷怪间隔来自配置（还原工程硬编码 5000/1500，C# 由配置注入）。
            // 若配置未提供则使用还原工程默认值，保证行为基线一致。
            _interWaveDelayMs = config.Wave.DelayTimeMs > 0
                ? config.Wave.DelayTimeMs
                : DefaultInterWaveDelayMs;
            _spawnIntervalMs = DefaultSpawnIntervalMs;

            _spawnState = SpawnState.Idle;
            _elapsedMs = 0;
            _spawnIndex = 0;
            _unitsThisWave = 0;
            _started = false;
        }

        // ====================================================================
        // 默认常量（对应还原工程硬编码）
        // ====================================================================

        /// <summary>默认波间延迟（毫秒，对应 BattleManager.js:24 interWaveDelayMs=5000）。</summary>
        private const long DefaultInterWaveDelayMs = 5000;

        /// <summary>默认刷怪间隔（毫秒，对应 BattleManager.js:25 spawnIntervalMs=1500）。</summary>
        private const long DefaultSpawnIntervalMs = 1500;

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>是否已 startGame。</summary>
        internal bool IsStarted => _started;

        /// <summary>当前波次运行状态（诊断用）。</summary>
        internal string CurrentSpawnState => _spawnState.ToString();

        /// <summary>当前波次号（1-based，0 表示尚未开始）。</summary>
        internal int CurrentRound => _state.CurrentRound;

        /// <summary>当前波次应刷怪总数。</summary>
        internal int UnitsThisWave => _unitsThisWave;

        /// <summary>当前波次已刷怪索引。</summary>
        internal int SpawnIndex => _spawnIndex;

        // ====================================================================
        // 生命周期
        // ====================================================================

        /// <summary>
        /// 开始一局：重置局内规则状态，进入等待开始状态。
        /// 对应还原工程 BattleManager.js:57-76 startGame。
        /// </summary>
        /// <param name="startNowMs">战斗开始时间戳（毫秒，对应 <c>this.now()</c>）。</param>
        /// <param name="spawnStrategyIndex">
        /// 由调用方（BattleRuntimeFactory/BattleModule）通过 weightedIndex 选定的生成策略索引。
        /// 对应 BattleManager.js:67 <c>random.weightedIndex(spawnStrategyWeights)</c>。
        /// 权属在调用方（本类型不持有随机源）；传入 -1 表示未选择，WaveManager 使用默认 0。
        /// </param>
        /// <remarks>
        /// <para><b>对应 BattleManager.js:57-76 startGame：</b></para>
        /// <list type="number">
        /// <item>placementReservations.clear() —— 由 <c>BattleInputController</c> 负责，本类型不持有预留注册表。</item>
        /// <item>发放初始金币：gold += initialGold（通过 <see cref="BattleState.ApplyGoldDelta"/> 提交）。</item>
        /// <item>设置 startTime（通过 <see cref="BattleState.ApplyStartGame"/> 提交）。</item>
        /// <item>重置 killCount/bossKillCount（由 <see cref="BattleState.ApplyStartGame"/> 完成）。</item>
        /// <item>状态 → WAITING_TO_START。</item>
        /// <item>选择生成策略索引（权属在调用方，本类型只透传给 WaveManager）。</item>
        /// <item>注册 GameLoop callback —— C# 移植删除，由 BattleSimulation 阶段回调替代。</item>
        /// <item>waveManager.startGame() —— 透传策略索引。</item>
        /// <item>economy.startGame() —— 触发经济生命周期钩子。</item>
        /// </list>
        /// <para><b>不注册框架更新（task 3.10 验证要求）：</b>
        /// 还原工程在 startGame 中 <c>gameLoop.register('BattleMgr', this, this.update)</c>。
        /// C# 移植不注册任何框架回调；本类型的方法由 <see cref="BattleSimulation"/> 阶段回调
        /// 在 <see cref="BattleUpdatePhase.WaveSpawn"/> 等位置调用。</para>
        /// </remarks>
        internal void StartGame(long startNowMs, int spawnStrategyIndex = -1)
        {
            // 重置权威状态：isGameOver=false、killCount=0、startTime 等。
            _state.ApplyStartGame(startNowMs);

            // 发放初始金币（对应 BattleManager.js:60-61 gold += initialGold）。
            // 还原工程在 startGame 中给双方各发 initialGold。
            int initialGold = _config.Economy.InitialGold;
            _state.ApplyGoldDelta(isPlayerSide: true, initialGold);
            _state.ApplyGoldDelta(isPlayerSide: false, initialGold);

            // 触发经济与波次管理器的 startGame 生命周期钩子。
            _economy.StartGame();
            _waveManager.StartGame(spawnStrategyIndex);

            // 进入等待开始状态，等待 delayTime 到期后 beginWave。
            _spawnState = SpawnState.WaitingToStart;
            _elapsedMs = 0;
            _spawnIndex = 0;
            _unitsThisWave = 0;
            _started = true;

            Log.Info(
                $"{LogTag} StartGame，initialGold={initialGold} strategyIndex={spawnStrategyIndex} " +
                $"delayTime={_state.DelayTimeMs}ms");
        }

        /// <summary>
        /// 结束一局：重置规则状态。
        /// 对应还原工程 BattleManager.js:204-220 gameOver。
        /// </summary>
        /// <remarks>
        /// <para>对应 BattleManager.js:204-220 gameOver：</para>
        /// <list type="bullet">
        /// <item>laya.timer.clearAll(this) —— C# 移植删除，无 Laya Timer。</item>
        /// <item>gameLoop.unregister('BattleMgr') —— C# 移植删除，无 GameLoop 注册。</item>
        /// <item>battleState.currentRound=0、elapsedMs=0、spawnIndex=0、unitsThisWave=0
        /// —— 由 <see cref="BattleState.ApplyGameOver"/> 与本方法重置。</item>
        /// <item>waveManager.gameOver() —— 透传。</item>
        /// <item>attackEffectManager.gameOver() —— 由 AttackEffectManager（task 5.3）负责，
        /// 本类型不持有其引用（职责受限：不拥有攻击效果生命周期）。</item>
        /// <item>experienceEventBus.off —— C# 移植删除，无全局 EventBus。</item>
        /// </list>
        /// <para><b>不调用 ResultBuilder</b>：gameOver 是状态重置，不是结果冻结。
        /// 结果冻结由 <see cref="TryFreezeResult"/> 在完成事实发生点处理。</para>
        /// </remarks>
        internal void GameOver()
        {
            _spawnState = SpawnState.Idle;
            _elapsedMs = 0;
            _spawnIndex = 0;
            _unitsThisWave = 0;
            _started = false;

            _waveManager.GameOver();
            _state.ApplyGameOver();

            Log.Info($"{LogTag} GameOver，规则状态已重置");
        }

        // ====================================================================
        // 阶段回调入口（由 BattleSimulation 在固定阶段调用）
        // ====================================================================

        /// <summary>
        /// 波次/生成阶段回调：推进波次状态机并按间隔刷怪。
        /// 对应还原工程 BattleManager.js:86-109 _updateSpawnState。
        /// </summary>
        /// <param name="frameNowMs">本帧时间戳（毫秒，同帧所有子步不变）。</param>
        /// <param name="stepMs">当前子步时长（毫秒，驱动 elapsedMs 累计）。</param>
        /// <remarks>
        /// <para><b>调用位置（BattleSimulation.cs:211-222 PhaseOrder）：</b>
        /// 本方法由 <see cref="BattleSimulation"/> 在
        /// <see cref="BattleUpdatePhase.WaveSpawn"/> 阶段每子步调用一次。
        /// 阶段顺序已冻结，本类型不自行决定调用时机。</para>
        ///
        /// <para><b>状态机（对应 BattleManager.js:86-109）：</b></para>
        /// <list type="bullet">
        /// <item>WaitingToStart：elapsedMs &gt;= delayTime 时 → Spawning + beginWave。</item>
        /// <item>Spawning：elapsedMs &gt;= spawnInterval 时 → spawnPairWhenDue。</item>
        /// <item>WaitingAfterWave：elapsedMs &gt;= interWaveDelay 时 → 下一波或 BATTLE_FINISHED。</item>
        /// </list>
        ///
        /// <para><b>不自行拆步（task 3.10 验证要求）：</b>
        /// <paramref name="stepMs"/> 由 <see cref="BattleSimulation"/> 拆步后传入，
        /// 本类型只累加 <c>_elapsedMs += stepMs</c>，不截断或拆分。</para>
        ///
        /// <para><b>胜负判断（spec "Battle result is frozen once"）：</b>
        /// 最大波次达成时调用 <see cref="TryFreezeResult"/>（playerWin=true）。
        /// 首次冻结成功后正常返回，<see cref="BattleSimulation"/> 在检查点中止剩余 phase。</para>
        /// </remarks>
        internal void UpdateSpawnState(long frameNowMs, long stepMs)
        {
            if (!_started)
            {
                return;
            }

            _elapsedMs += stepMs;

            switch (_spawnState)
            {
                case SpawnState.WaitingToStart:
                    // 等待 delayTime 到期后开始第一波。
                    // 还原工程还检查 playerPlacementComplete && opponentPlacementComplete，
                    // 本期不实现放置完成信号，只按 delayTime 判断。
                    if (_elapsedMs >= _state.DelayTimeMs)
                    {
                        _elapsedMs = 0;
                        _spawnState = SpawnState.Spawning;
                        BeginWave();
                    }
                    break;

                case SpawnState.Spawning:
                    // 按 spawnInterval 刷怪。
                    if (_elapsedMs >= _spawnIntervalMs)
                    {
                        _elapsedMs = 0;
                        SpawnPairWhenDue();
                    }
                    break;

                case SpawnState.WaitingAfterWave:
                    // 波间延迟后下一波或结束。
                    if (_elapsedMs >= _interWaveDelayMs)
                    {
                        _elapsedMs = 0;
                        if (_state.EndlessMode || _state.CurrentRound < _state.MaxRounds)
                        {
                            _spawnState = SpawnState.Spawning;
                            BeginWave();
                        }
                        else
                        {
                            // 最大波次达成：玩家胜利（对应 BattleManager.js:106
                            // eventBus.event(GameEvents.BATTLE_FINISHED, true)）。
                            // C# 移植经 ResultBuilder.TryFreeze 唯一入口冻结。
                            TryFreezeResult(playerWin: true);
                        }
                    }
                    break;
            }
        }

        // ====================================================================
        // 波次刷怪接入委托（task 6.10 闭环返工：接入 EnemyManager.Spawn）
        // --------------------------------------------------------------------
        // design.md 目录表限制 BattleManager 不直接持有 EnemyManager/EnemyFactory，
        // 但 _spawnPairWhenDue 必须真正创建敌人才能让闭环完成"波次结算"。
        // 解决方式：由 BattleRuntimeFactory 在组装阶段注入刷怪委托，
        // BattleManager 只负责按 spawnInterval 调用委托，不持有具体 Manager 引用。
        // 委托签名 (isPlayerLane, typeIndex)：生成一个指定阵营与类型的敌人。
        // 本期 typeIndex 固定 0（Mob0），多类型延后到后续 change。
        // ====================================================================

        /// <summary>
        /// 波次刷怪委托：由 <see cref="BattleRuntimeFactory"/> 注入，负责实际创建敌人
        /// 并登记到 <see cref="EnemyManager"/>。
        /// <para><b>接入原因（task 6.10 闭环返工）：</b>
        /// 原 <see cref="SpawnPairWhenDue"/> 只推进 <c>_spawnIndex</c> 状态机，
        /// 不调用 EnemyManager.Spawn，导致闭环中敌人从未实际生成，
        /// 测试只能通过 maxRounds 超时触发冻结而非实际战斗结算。</para>
        /// <para><b>职责边界（design.md:170）：</b>
        /// 本类型不直接持有 EnemyManager/EnemyFactory（职责受限），
        /// 通过委托解耦：BattleManager 只按 spawnInterval 调用委托，
        /// 委托实现负责 Acquire → Configure → InitializeStats → Init → Register。</para>
        /// <para>委托可为 null：无注入时 <see cref="SpawnPairWhenDue"/> 只推进状态机（兼容旧测试）。</para>
        /// </summary>
        internal Action<bool, int> OnSpawnEnemy { get; set; }

        // ====================================================================
        // 波次开始与刷怪（对应 BattleManager.js:111-145）
        // ====================================================================

        /// <summary>
        /// 开始一个新波次：递增波次号并从 WaveManager 获取生成计划。
        /// 对应还原工程 BattleManager.js:111-129 _beginWave。
        /// </summary>
        /// <remarks>
        /// <para><b>对应 BattleManager.js:111-129 _beginWave：</b></para>
        /// <list type="number">
        /// <item>currentRound += 1（通过 <see cref="BattleState.ApplyBeginWave"/> 提交）。</item>
        /// <item>eventBus.event(ROUND_STARTED) —— C# 移植删除，不二次发布
        /// （spec "Event signatures are unambiguous"，ROUND_SPAWN_PREPARED 只由 WaveManager 发布）。</item>
        /// <item>plan = waveManager.beginRound(currentRound) —— 获取确定性生成计划。</item>
        /// <item>unitsThisWave = plan.normalCount —— 设置本波刷怪总数。</item>
        /// <item>spawnIndex = 0 —— 重置刷怪索引。</item>
        /// <item>选择 specialSpawnIndex —— 本期不实现 specialSpawnPolicy，跳过。</item>
        /// <item>eventBus.event(ROUND_SPAWN_PREPARED) —— C# 移植删除，
        /// WaveManager.PlanRound 已唯一发布 ROUND_SPAWN_PREPARED(plan)。</item>
        /// </list>
        /// <para><b>不二次发布 ROUND_SPAWN_PREPARED（task 3.9 约束）：</b>
        /// 还原工程在 _beginWave 末尾 <c>eventBus.event(GameEvents.ROUND_SPAWN_PREPARED)</c>
        /// 是无参二次发布。C# 移植禁止此行为：WaveManager.PlanRound 已带 plan 唯一发布，
        /// 本类型只消费 plan.normalCount。</para>
        /// </remarks>
        private void BeginWave()
        {
            // 递增波次号（对应 BattleManager.js:112 currentRound += 1）。
            _state.ApplyBeginWave();

            // 从 WaveManager 获取确定性生成计划（对应 BattleManager.js:114-115）。
            WaveSpawnPlan plan = _waveManager.BeginRound(_state.CurrentRound);
            _unitsThisWave = plan.NormalCount;

            // 重置刷怪索引（对应 BattleManager.js:125）。
            _spawnIndex = 0;

            // specialSpawnIndex 选择（对应 BattleManager.js:126-127）：
            // 本期不实现 specialSpawnPolicy，playerSpecialSpawnIndex/opponentSpecialSpawnIndex
            // 固定 -1（无特殊生成），不调用 random.range。

            Log.Info(
                $"{LogTag} BeginWave round={_state.CurrentRound} unitsThisWave={_unitsThisWave} " +
                $"boss={plan.Boss}");
        }

        /// <summary>
        /// 按刷怪间隔生成一对 Mob0（玩家方与对手方各一）。
        /// 对应还原工程 BattleManager.js:131-145 _spawnPairWhenDue。
        /// </summary>
        /// <remarks>
        /// <para><b>对应 BattleManager.js:131-145 _spawnPairWhenDue：</b></para>
        /// <list type="number">
        /// <item>elapsedMs &gt;= spawnInterval 时重置 elapsedMs=0（已在调用前由 UpdateSpawnState 重置）。</item>
        /// <item>waveManager.spawnNormalPair(spawnIndex, ...) —— 调 WaveManager 刷怪。</item>
        /// <item>spawnIndex += 1。</item>
        /// <item>spawnIndex &gt;= unitsThisWave 时 → WaitingAfterWave + 重置 spawnIndex。</item>
        /// </list>
        /// <para><b>本期刷怪委托接入（task 6.10 闭环返工）：</b>
        /// 还原工程在无 WaveManager 时直接调 enemyManager.spawn，有 WaveManager 时调
        /// waveManager.spawnNormalPair。C# 移植改为由 <see cref="OnSpawnEnemy"/> 委托
        /// 实际创建敌人（Acquire → Configure → Init → Register），
        /// 委托由 BattleRuntimeFactory 在组装阶段注入。本类型不直接持有
        /// EnemyManager/EnemyFactory（职责受限 design.md:170）。</para>
        /// <para><b>不持有 EnemyManager（task 3.10 职责受限）：</b>
        /// 本类型只持有 WaveManager 引用，不直接持有 EnemyManager/UnitRegistry/
        /// AttackEffectManager 等。敌人/单位/攻击效果的生命周期由各自 Manager 负责，
        /// 本类型只协调波次状态机与胜负判断。</para>
        /// </remarks>
        private void SpawnPairWhenDue()
        {
            // task 6.10 闭环返工：调用 OnSpawnEnemy 委托真正创建敌人。
            // 委托由 BattleRuntimeFactory 注入，负责 Acquire → Configure →
            // InitializeStats → Init → Register 全流程。
            // 委托为 null 时（兼容旧测试）只推进 spawnIndex 状态机，不创建敌人。
            if (OnSpawnEnemy != null)
            {
                // 玩家方与对手方各生成一个 Mob0（对应 spawnNormalPair 语义）。
                // typeIndex 固定 0（Mob0），多类型延后到后续 change。
                OnSpawnEnemy.Invoke(true, 0);
                OnSpawnEnemy.Invoke(false, 0);
            }

            _spawnIndex += 1;

            if (_spawnIndex >= _unitsThisWave)
            {
                // 本波刷怪完成，进入波间等待。
                _spawnIndex = 0;
                _spawnState = SpawnState.WaitingAfterWave;
            }
        }

        // ====================================================================
        // 胜负判断（唯一入口经 BattleResultBuilder.TryFreeze）
        // ====================================================================

        /// <summary>
        /// 尝试冻结战斗结果。在完成事实发生点调用 BattleResultBuilder.TryFreeze 唯一入口。
        /// </summary>
        /// <param name="playerWin">是否玩家胜利。</param>
        /// <returns>是否首次冻结成功（后续调用返回 false，幂等）。</returns>
        /// <remarks>
        /// <para><b>唯一冻结入口（spec "Battle result is frozen once" / 决策 0.4）：</b>
        /// 还原工程在多处直接 <c>eventBus.event(GameEvents.BATTLE_FINISHED, isWin)</c>。
        /// C# 移植统一经 <see cref="BattleResultBuilder"/>.<c>TryFreeze</c> 冻结，
        /// 保证"冻结顺序中第一个完成事实胜出"。</para>
        ///
        /// <para><b>决策 0.4 中止语义：</b>
        /// 首次 TryFreeze 成功后只完成当前同步提交并返回，不重入销毁集合。
        /// <see cref="BattleSimulation"/> 在紧随的检查点跳过当前子步后续 phase 与当前帧
        /// 剩余子步，由 <see cref="BattleRuntime.EnterSettling"/> 统一静默清理。</para>
        ///
        /// <para><b>不在伤害调用栈内重入销毁（spec "Freeze occurs inside a manager update"）：</b>
        /// 本方法不销毁 Manager 或集合，只委托 ResultBuilder 冻结结果。</para>
        ///
        /// <para><b>并行任务契约推断（task 49 同批次并行新建 BattleResultBuilder）：</b>
        /// 推断依据：</para>
        /// <list type="bullet">
        /// <item>design.md:163 "State/BattleResultBuilder.cs：在唯一结算点依据稳定优先级冻结一次最终结果 DTO"。</item>
        /// <item>spec "Battle result is frozen once"：首次事实成功冻结后幂等。</item>
        /// <item>BattleReadModel.cs:153-170 <c>SnapshotResultInputs()</c> 返回
        /// <c>BattleResultInputs</c>（含 playerHealth/opponentHealth/isGameOver 等标量），
        /// 供 ResultBuilder 读取。</item>
        /// <item>还原工程 BattleResult.js:7 <c>fromRuntime({isWin, ...})</c> 接收显式 isWin
        /// 参数，由调用方依据完成事实传入。</item>
        /// </list>
        /// <para>据此推断 <c>TryFreeze(bool isWin)</c> 签名：首次成功返回 true，后续返回 false。
        /// playerWin 直接映射为 isWin。若 task 49 实际 API 为无参并自行从
        /// <see cref="BattleState"/> 生命值判断 isWin，由集成步骤修正此处调用（移除参数），
        /// 并按 SKILL 规则 4 说明冲突。</para>
        /// </remarks>
        internal bool TryFreezeResult(bool playerWin)
        {
            // 经 BattleResultBuilder 唯一入口冻结。
            // playerWin 语义：true=玩家胜利（对手生命归零或最大波次达成），
            // false=玩家失败（玩家生命归零）。
            bool frozen = _resultBuilder.TryFreeze(playerWin);
            if (frozen)
            {
                Log.Info(
                    $"{LogTag} 结果首次冻结成功 playerWin={playerWin} round={_state.CurrentRound}");
            }
            return frozen;
        }

        /// <summary>
        /// 检查生命归零胜负条件并在满足时尝试冻结。
        /// 由 <c>BattleTarget</c>（task 4.2）在受击发生点调用。
        /// </summary>
        /// <param name="isPlayerSide">受击方是否为玩家方。</param>
        /// <remarks>
        /// <para><b>调用时机（design.md:173 BattleTarget）：</b>
        /// <c>BattleTarget.applyDamage</c> 在受击发生点调用 <see cref="BattleState.ApplyDamage"/>
        /// 后，检查受击方生命是否归零。归零时调用本方法触发胜负冻结。</para>
        /// <para>玩家方归零 → 玩家失败（playerWin=false）；
        /// 对手方归零 → 玩家胜利（playerWin=true）。</para>
        /// <para><b>幂等：</b>若 ResultBuilder 已冻结，本方法为空操作。</para>
        /// </remarks>
        internal void CheckHealthFreeze(bool isPlayerSide)
        {
            int health = isPlayerSide ? _state.PlayerHealth : _state.OpponentHealth;
            if (health <= 0)
            {
                // 受击方生命归零：对方胜利。
                TryFreezeResult(playerWin: !isPlayerSide);
            }
        }
    }
}
