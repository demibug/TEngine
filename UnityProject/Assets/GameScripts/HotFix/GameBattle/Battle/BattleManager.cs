using System;
using TEngine;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.10 + 任务 4.6-4.8：BattleManager —— 职责受限的战斗规则协调器
    // ----------------------------------------------------------------------------
    // 职责（design.md 目录表 / Battle/BattleManager.cs / design.md:170）：
    //   只保留战斗启动/终止、目标生命完成事实与结果冻结协调；不再生成敌人，
    //   不再根据 MaxRounds 推进波次。
    //
    // 职责边界（task 3.10 / task 4.6-4.8）：
    //   - 只拥有规则状态、完成事实与胜负冻结协调。
    //   - 不实现 TEngine IUpdateModule（不拥有框架 OnUpdate）。
    //   - 不实现时间拆步（500ms 截断、80ms 子步由 BattleSimulation 唯一负责）。
    //   - 不实现 Scene/UI/资源生命周期（由 BattleModule/BattleRuntime 负责）。
    //   - 波次推进唯一由 WaveManager（有序波次状态机）在 WaveSpawn phase 的
    //     Update(stepMs) 驱动；本类型不再持有 spawn state/计时/数量状态机。
    //
    // 任务 4.6：BattleSimulation.WaveSpawn phase 唯一调用 waveManager.Update(stepMs)，
    //   不再调用本类型的旧 UpdateSpawnState。波次生成（Normal 出生）由
    //   BattleRuntimeFactory 注入的 NormalWaveSpawnHandler 经 EnemyFactory 完成。
    //
    // 任务 4.7：胜利只消费 WaveManager.AllConfiguredWavesCompleted 单次事实——
    //   本类型订阅后调用单一成功协调入口 HandleAllWavesCompleted → TryFreezeResult(true)。
    //   玩家生命归零仍立即 TryFreezeResult(false)，失败优先且不等待波次；
    //   对手生命归零只保留状态，不直接成功（成功必须等全部配置波清场）。
    //
    // 任务 4.8：BattleState.MaxRounds 为启动时从计划行数设置的派生显示/统计值，
    //   本类型不再用它推进或判断胜利；CurrentRound 由 WaveStarted(order) 同步到真实
    //   WaveManager.CurrentOrder，冻结前确保保留最后真实 order。
    //
    // 不变量：
    //   1. 独占单局状态：规则状态字段归属本实例，不跨局复用。
    //   2. 不实现 IUpdateModule：不挂载到 ModuleSystem._updateExecuteList。
    //   3. 不拥有时间拆步：只消费 BattleSimulation 传入的 stepMs（本类型已不消费时间）。
    //   4. 胜负只经 BattleResultBuilder.TryFreeze 冻结：本类型只调用 TryFreezeResult 唯一入口。
    //   5. 成功只经 AllConfiguredWavesCompleted → HandleAllWavesCompleted 单一入口；
    //      失败（玩家生命归零）立即冻结，迟到完成不能覆盖已冻结结果（幂等）。
    // ============================================================================

    /// <summary>
    /// 职责受限的战斗规则协调器：只保留战斗启动/终止、目标生命完成事实与结果冻结协调。
    /// </summary>
    /// <remarks>
    /// <para><b>职责受限（design.md:170 / task 3.10 验证要求）：</b>
    /// 本类型只拥有规则状态、完成事实与胜负冻结协调。<b>不</b>实现 TEngine
    /// <c>IUpdateModule</c>（不拥有框架 <c>OnUpdate</c>），<b>不</b>实现时间拆步
    /// （500ms 截断、80ms 子步由 <see cref="BattleSimulation"/> 唯一负责），<b>不</b>实现
    /// Scene/UI/资源生命周期（由 <c>BattleModule</c>/<see cref="BattleRuntime"/> 负责）。</para>
    ///
    /// <para><b>波次推进唯一 owner（design.md 决策 4 / task 4.6）：</b>
    /// 逐波状态机由 <see cref="WaveManager"/> 唯一拥有，只在 <see cref="BattleSimulation"/>
    /// 的 <see cref="BattleUpdatePhase.WaveSpawn"/> phase 以 <c>waveManager.Update(stepMs)</c>
    /// 推进。本类型不持有 spawn state、elapsed/unit count、固定 typeIndex 或 MaxRounds 推进
    /// 分支；旧生产链的这些成员已在 task 4.6 移除。</para>
    ///
    /// <para><b>胜利闸（design.md 决策 9 / task 4.7）：</b>
    /// 本类型在构造时订阅 <see cref="WaveManager.AllConfiguredWavesCompleted"/>，收到后调用
    /// <see cref="HandleAllWavesCompleted"/>（单一成功协调入口）→
    /// <see cref="TryFreezeResult"/>（<c>playerWin=true</c>）。玩家生命归零仍立即
    /// <see cref="CheckHealthFreeze"/> → <c>TryFreezeResult(false)</c>；对手生命归零只保留
    /// 状态，不直接成功。TryFreeze/ResultBuilder 仍是唯一冻结单入口且幂等——失败已冻结时
    /// 迟到的完成事实不会覆盖。</para>
    ///
    /// <para><b>每局新建/销毁（spec "Restart creates clean per-battle state"）：</b>
    /// 重开销毁旧 Runtime，新建 Runtime 时由 Factory 产生新 BattleManager。
    /// <see cref="StartGame"/> 只负责战斗开始协调，不启动第二状态机。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 <see cref="BattleRuntime"/>
    /// 在生命周期调用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class BattleManager
    {
        // ====================================================================
        // 日志标签
        // ====================================================================

        private const string LogTag = "[BattleManager]";

        // ====================================================================
        // 配置依赖（不可变，构造时注入）
        // ====================================================================

        /// <summary>
        /// 本局不可变配置快照。用于读取经济初始金币等协调参数。
        /// </summary>
        private readonly BattleConfigSnapshot _config;

        // ====================================================================
        // 规则服务依赖（强类型注入，替代还原工程 configure 字符串容器）
        // ====================================================================

        /// <summary>
        /// 权威战斗状态。本类型经 Apply* 方法提交规则变更（生命归零、波次同步等）。
        /// </summary>
        private readonly BattleState _state;

        /// <summary>
        /// 波次管理器。有序波次状态机唯一 owner；本类型订阅其
        /// <see cref="WaveManager.WaveStarted"/>（CurrentRound 同步）与
        /// <see cref="WaveManager.AllConfiguredWavesCompleted"/>（成功闸）两个单次事实。
        /// </summary>
        private readonly WaveManager _waveManager;

        /// <summary>
        /// 经济服务。由 <c>BattleInputController</c> 在输入事务中调用，
        /// 本类型只在 <see cref="StartGame"/> 中触发其 StartGame 生命周期钩子。
        /// </summary>
        private readonly BattleEconomy _economy;

        /// <summary>
        /// 结果冻结器。本类型在完成事实发生点调用其 <c>TryFreeze</c> 唯一入口。
        /// </summary>
        /// <remarks>
        /// TryFreeze/ResultBuilder 是唯一冻结单入口且幂等：首次成功后后续调用返回 false，
        /// 失败已冻结时迟到的完成事实不会覆盖。
        /// </remarks>
        private readonly BattleResultBuilder _resultBuilder;

        // ====================================================================
        // 单局可变状态
        // ====================================================================

        /// <summary>是否已 startGame（StartGame 置 true，GameOver 置 false）。</summary>
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
        /// <para><b>职责受限声明（task 3.10）：</b>本构造函数不注册任何 TEngine 模块更新回调、
        /// 不创建 Scene/UI 对象、不加载资源、不启动任何 Timer。只订阅 WaveManager 的两个
        /// 单次事实（<see cref="WaveManager.WaveStarted"/> / <see cref="WaveManager.AllConfiguredWavesCompleted"/>），
        /// 订阅生命周期随 Runtime 实例销毁而终结（不跨局复用）。</para>
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
            _started = false;

            // 任务 4.7：全部配置波完成是唯一成功闸。本订阅只在最后一行首次 Completed 且
            // 全局无活动 handle 时被 WaveManager 触发一次；TryFreeze 幂等保证失败已冻结时
            // 迟到的完成不会覆盖。
            _waveManager.AllConfiguredWavesCompleted += HandleAllWavesCompleted;

            // 任务 4.8：每行开始同步 CurrentRound 到真实 WaveManager.CurrentOrder（只读显示/统计）。
            _waveManager.WaveStarted += order => _state.ApplyWaveStarted(order);
        }

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>是否已 startGame。</summary>
        internal bool IsStarted => _started;

        /// <summary>当前波次号（1-based，0 表示尚未开始）。由 WaveManager 逐行同步。</summary>
        internal int CurrentRound => _state.CurrentRound;

        // ====================================================================
        // 生命周期
        // ====================================================================

        /// <summary>
        /// 开始一局：只负责战斗开始协调，不启动第二波次状态机。
        /// </summary>
        /// <param name="startNowMs">战斗开始时间戳（毫秒）。</param>
        /// <param name="spawnStrategyIndex">
        /// <b>legacy 参数（任务 4.6 后不再使用）</b>：旧链按权重选择生成策略索引；
        /// 新链策略由逐波计划行显式引用 profile，本参数被忽略，保留仅为兼容调用方
        /// （BattleModule / 既有测试）签名。
        /// </param>
        /// <remarks>
        /// <para><b>协调职责（task 4.6）：</b></para>
        /// <list type="number">
        /// <item>重置权威状态：isGameOver=false、killCount=0、startTime 等（<see cref="BattleState.ApplyStartGame"/>）。</item>
        /// <item>发放双方初始金币（经 <see cref="BattleState.ApplyGoldDelta"/> 提交）。</item>
        /// <item>以所选计划行数设置 MaxRounds 显示/统计值并重置 CurrentRound=0
        /// （<see cref="BattleState.ApplyConfigurePlan"/>）。</item>
        /// <item>触发经济 startGame 生命周期钩子。</item>
        /// </list>
        /// <para><b>不启动第二状态机：</b>有序波次状态机在 WaveSpawn phase 的首次
        /// <c>waveManager.Update(stepMs)</c> 自动进入首行，本方法不需要额外启动波次。</para>
        /// </remarks>
        internal void StartGame(long startNowMs, int spawnStrategyIndex = -1)
        {
            // 重置权威状态。
            _state.ApplyStartGame(startNowMs);

            // 发放初始金币（对应 BattleManager.js:60-61 gold += initialGold）。
            int initialGold = _config.Economy.InitialGold;
            _state.ApplyGoldDelta(isPlayerSide: true, initialGold);
            _state.ApplyGoldDelta(isPlayerSide: false, initialGold);

            // 任务 4.8：MaxRounds 从计划行数派生（只读显示/统计），CurrentRound 重置为 0。
            _state.ApplyConfigurePlan(_waveManager.PlannedRowCount);

            // 触发经济 startGame 生命周期钩子。
            _economy.StartGame();

            _started = true;

            Log.Info(
                $"{LogTag} StartGame，initialGold={initialGold} " +
                $"maxRounds(planRows)={_state.MaxRounds} delayTime={_state.DelayTimeMs}ms");
        }

        /// <summary>
        /// 结束一局：重置规则状态并停止波次状态机。
        /// </summary>
        /// <remarks>
        /// <para>对应还原工程 BattleManager.js:204-220 gameOver 的规则部分：
        /// <list type="bullet">
        /// <item>置 <see cref="IsStarted"/>=false。</item>
        /// <item>先停止 <see cref="WaveManager"/>（<c>Stop()</c> 先置 stopped 再清待出生与
        /// 所有权，阻止清理/迟到事实生成新波或误判胜利——task 4.9 的清理顺序基础）。</item>
        /// <item><see cref="BattleState.ApplyGameOver"/> 重置波次/生命/金币等。</item>
        /// </list></para>
        /// <para><b>不调用 ResultBuilder</b>：gameOver 是状态重置，不是结果冻结。
        /// 结果冻结由 <see cref="TryFreezeResult"/> 在完成事实发生点处理。</para>
        /// </remarks>
        internal void GameOver()
        {
            _started = false;

            // 先停止 WaveManager：停止后 Update/迟到移除不再出生或发布完成。
            _waveManager.Stop();
            _state.ApplyGameOver();

            Log.Info($"{LogTag} GameOver，规则状态已重置");
        }

        // ====================================================================
        // 成功协调（唯一成功入口，task 4.7）
        // ====================================================================

        /// <summary>
        /// 全部配置波完成协调入口：只由 <see cref="WaveManager.AllConfiguredWavesCompleted"/>
        /// 事件触发（任务 4.7 唯一成功闸）。
        /// </summary>
        /// <remarks>
        /// <para>本方法是"全部配置波真实清场"到"冻结胜利"之间的<b>单一成功协调入口</b>：
        /// 冻结前确保 <see cref="BattleState.CurrentRound"/> 保留最后真实 order
        /// （<see cref="WaveManager.CurrentOrder"/>），再经 <see cref="TryFreezeResult"/>（true）
        /// 冻结。TryFreeze/ResultBuilder 幂等：若玩家失败已先冻结，本入口的迟到成功不会覆盖。</para>
        /// </remarks>
        private void HandleAllWavesCompleted()
        {
            // 任务 4.8：冻结前确保 CurrentRound = 最后真实 order（幂等同步）。
            _state.ApplyWaveStarted(_waveManager.CurrentOrder);
            TryFreezeResult(playerWin: true);
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
        /// C# 移植统一经 <see cref="BattleResultBuilder"/>.<c>TryFreeze</c> 冻结，
        /// 保证"冻结顺序中第一个完成事实胜出"。失败（玩家生命归零）优先：首次 TryFreeze(false)
        /// 成功后，迟到 <see cref="AllConfiguredWavesCompleted"/> 调用的
        /// <see cref="TryFreezeResult"/>（true）返回 false，不覆盖失败结果。</para>
        /// <para><b>决策 0.4 中止语义：</b>首次 TryFreeze 成功后只完成当前同步提交并返回，
        /// <see cref="BattleSimulation"/> 在紧随的检查点跳过当前子步后续 phase 与当前帧
        /// 剩余子步，由 <see cref="BattleRuntime.EnterSettling"/> 统一静默清理。</para>
        /// </remarks>
        internal bool TryFreezeResult(bool playerWin)
        {
            bool frozen = _resultBuilder.TryFreeze(playerWin);
            if (frozen)
            {
                Log.Info(
                    $"{LogTag} 结果首次冻结成功 playerWin={playerWin} round={_state.CurrentRound}");
            }
            return frozen;
        }

        /// <summary>
        /// 检查生命归零胜负条件：玩家侧归零立即失败；对手侧归零只保留状态，不直接成功。
        /// 由 <c>BattleTarget</c> 在受击发生点调用。
        /// </summary>
        /// <param name="isPlayerSide">受击方是否为玩家侧（isPlayerLaneTarget）。</param>
        /// <remarks>
        /// <para><b>调用时机（design.md:173 BattleTarget）：</b>
        /// <c>BattleTarget.applyDamage</c> 在受击发生点调用 <see cref="BattleState.ApplyDamage"/>
        /// 后，检查受击方生命是否归零。归零时调用本方法。</para>
        /// <para><b>任务 4.7 语义（spec ordered-wave-plan "Fail before the plan finishes"）：</b>
        /// <list type="bullet">
        /// <item>玩家侧归零 → 立即 <see cref="TryFreezeResult"/>(false)，失败优先且不等待波次。</item>
        /// <item>对手侧归零 → 只保留状态（<see cref="BattleState.ApplyDamage"/> 已归零），
        /// 不得直接成功；成功必须等 <see cref="AllConfiguredWavesCompleted"/>。</item>
        /// </list></para>
        /// <para><b>幂等：</b>若 ResultBuilder 已冻结，本方法为空操作。</para>
        /// </remarks>
        internal void CheckHealthFreeze(bool isPlayerSide)
        {
            int health = isPlayerSide ? _state.PlayerHealth : _state.OpponentHealth;
            if (health > 0)
            {
                return;
            }

            if (!isPlayerSide)
            {
                // 对手侧归零：只保留状态，不得直接成功（成功必须等全部配置波清场）。
                Log.Info($"{LogTag} 对手侧生命归零，保留状态等待 AllConfiguredWavesCompleted 成功闸");
                return;
            }

            // 玩家侧归零：立即失败（失败优先且不等待剩余波次）。
            TryFreezeResult(playerWin: false);
        }
    }
}
