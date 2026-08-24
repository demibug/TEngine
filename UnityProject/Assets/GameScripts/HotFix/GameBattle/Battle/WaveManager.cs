using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // WaveManager —— 有序波次状态机（OrderedWavePlanSnapshot 唯一生产模型）
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 4/5/8 / specs/ordered-wave-plan/spec.md / task 4.1-4.5）：
    //   直接消费 OrderedWavePlanSnapshot，按 Pending→PreDelay→Spawning→
    //   WaitingForClear→Completed 逐行推进；每次成功 spawn 返回 WaveEntityHandle
    //   并登记 HashSet，完整值幂等移除；最后一行完成时发布 AllConfiguredWavesCompleted
    //   单次事实。
    //
    // 时间语义（仅由 Update(stepMs) 推进，stepMs<0 显式失败）：
    //   - 行首次进入后，preDelay 达到的该次更新提交首个出生序号；
    //     preDelay=0 可在进入行的该次更新出生。
    //   - Normal 后续出生序号按 spawnInterval；每个序号内双路启用时先玩家路、后电脑路；
    //     normalCount 是每个启用车道数量。
    //   - Boss 在 preDelay 到期时按启用车道各 Spawn 一次，玩家路先于电脑路；不冒充 Normal。
    //   - 最后一批 Spawn 后立即转 WaitingForClear 并从此刻开始 postDelay，
    //     但同一更新不把该行判 Completed。
    //   - WaitingForClear 仅在全部出生已提交/取消、postDelay 到期、本行活动 handle
    //     集合为空时提交一次 Completed。
    //   - 行 Completed 后绝不在同一次 Update 进入下一行；下一行最早下一次 Update 开始，
    //     零延迟也不可一子步穿越多行。
    //
    // 生命周期（Stop / Cleanup）：
    //   - Stop：先置 stopped，再清待出生序号与所有权，并幂等停止 Boss 端口。
    //   - Cleanup：释放本局波次所有权并幂等清理 Boss 端口；置 stopped 防重启，
    //     不重启状态机，也不发布 AllConfiguredWavesCompleted。
    //   - AllConfiguredWavesCompleted 只在最后一行首次 Completed 且全局无活动 handle
    //     时发布一次，是唯一成功闸。
    //
    // 不变量：
    //   1. 只消费 OrderedWavePlanSnapshot，绝不读取 waveUnitCounts/Boss 概率数组/
    //      randomSource/MaxRounds。
    //   2. 每次成功出生登记完整 WaveEntityHandle；相同 runtimeId 但 generation/
    //      waveOrder/kind 任一不同的迟到事实不匹配，不减少活动计数。
    //   3. 每局由 BattleRuntimeFactory 新建，不跨局复用（spec "Restart creates clean
    //      per-battle state"）。
    // ============================================================================

    /// <summary>
    /// 有序波次状态机：直接消费 <see cref="OrderedWavePlanSnapshot"/>，按五态逐行推进，
    /// 双路固定顺序出生并维护活动 <see cref="WaveEntityHandle"/> 的 generation 守卫。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 决策 4/5/8 / specs/ordered-wave-plan/spec.md）：</b>
    /// 逐行消费 <see cref="OrderedWavePlanSnapshot"/>；每次成功 spawn 返回
    /// <see cref="WaveEntityHandle"/> 并登记 <c>HashSet</c>，完整值幂等移除；
    /// 最后一行完成时发布 <see cref="AllConfiguredWavesCompleted"/> 单次事实。</para>
    /// <para><b>时间推进唯一入口：</b><see cref="Update"/>；行 Completed 后绝不在
    /// 同一次 Update 进入下一行。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部
    /// <see cref="BattleRuntimeFactory"/> 与测试使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class WaveManager
    {
        // ====================================================================
        // 新链注入依赖（不可变，构造时注入）
        // ====================================================================

        /// <summary>本局有序波次计划快照（不可变；新生产链唯一计划来源）。</summary>
        private readonly OrderedWavePlanSnapshot _orderedPlan;

        /// <summary>普通敌人出生 handler（新链出生端口）。</summary>
        private readonly NormalWaveSpawnHandler _normalSpawnHandler;

        /// <summary>Boss 波交接端口（生产默认注入不可用实现）。</summary>
        private readonly IBossWavePort _bossPort;

        // ====================================================================
        // 新链运行时状态（单局可变）
        // ====================================================================

        /// <summary>当前行索引（-1 表示尚未进入任何行）。</summary>
        private int _rowIndex = -1;

        /// <summary>当前行状态（Pending 表示未开始）。</summary>
        private WaveRuntimeState _rowState = WaveRuntimeState.Pending;

        /// <summary>当前行首次出生前置延迟剩余（毫秒，PreDelay 状态使用）。</summary>
        private long _preDelayRemaining;

        /// <summary>下一个出生序号距到期剩余（毫秒，Spawning 状态使用）。</summary>
        private long _spawnTimer;

        /// <summary>当前行清场结束等待剩余（毫秒，WaitingForClear 状态使用）。</summary>
        private long _postDelayRemaining;

        /// <summary>下一个待提交的出生序号（0-based，批序号）。</summary>
        private int _nextOrdinal;

        /// <summary>当前行出生批序号总数。</summary>
        private int _totalOrdinals;

        /// <summary>是否已停止（GameOver/Cancel/Stop 先置 stopped 再清待出生与所有权）。</summary>
        private bool _stopped;

        /// <summary>全部配置波是否已完成（最后一行完成且无活动 handle）。</summary>
        private bool _allCompleted;

        /// <summary>AllConfiguredWavesCompleted 是否已发布（单次事实守卫）。</summary>
        private bool _allCompletedPublished;

        /// <summary>当前局活动 handle 集合（完整值幂等登记/移除）。</summary>
        private readonly HashSet<WaveEntityHandle> _activeHandles = new HashSet<WaveEntityHandle>();

        // ====================================================================
        // 新链事件（唯一 AllConfiguredWavesCompleted 事实）
        // ====================================================================

        /// <summary>
        /// 全部配置波完成事实（单次发布）。
        /// </summary>
        /// <remarks>
        /// <para>只在最后一行首次 Completed 且全局无活动 handle 时发布一次。
        /// 本事实是唯一成功闸：BattleManager（4.7 接入）收到后调用单一成功协调入口
        /// → 既有 TryFreeze(true)。Stop/取消后不得发布本事实。</para>
        /// </remarks>
        internal event Action AllConfiguredWavesCompleted;

        /// <summary>
        /// 单行波次开始事实（每行恰好一次）。
        /// </summary>
        /// <remarks>
        /// <para><b>任务 4.8 接入（BattleState 同步）：</b>每行进入（BeginRow）时发布一次
        /// <c>order</c>，供 <see cref="BattleManager"/> 同步
        /// <see cref="BattleState.CurrentRound"/> 到真实 WaveManager.CurrentOrder（只读显示/统计），
        /// 使结果冻结前 <c>CurrentRound</c> 保留最后真实 order。MaxRounds 绝不参与推进/成功。</para>
        /// <para>仅为每行进入时的单次事实；Stop/取消后不再发布。</para>
        /// </remarks>
        internal event Action<int> WaveStarted;

        // ====================================================================
        // 新链只读状态（供测试 / Runtime 接入；不暴露可变集合）
        // ====================================================================

        /// <summary>当前行 order（未进入任何行时为 -1）。</summary>
        internal int CurrentOrder => _rowIndex >= 0 ? _orderedPlan.Rows[_rowIndex].Order : -1;

        /// <summary>当前行状态。</summary>
        internal WaveRuntimeState State => _rowState;

        /// <summary>计划行数（派生显示轮数来源）。</summary>
        internal int PlannedRowCount => _orderedPlan.Rows.Count;

        /// <summary>当前局活动 handle 数量。</summary>
        internal int ActiveHandleCount => _activeHandles.Count;

        /// <summary>当前局活动 handle 只读快照（每次调用生成拷贝，不暴露内部集合）。</summary>
        internal IReadOnlyList<WaveEntityHandle> ActiveHandles => GetActiveHandlesSnapshot();

        /// <summary>是否已停止（停止后 Update/迟到移除不得出生或发布完成）。</summary>
        internal bool IsStopped => _stopped;

        /// <summary>全部配置波是否已完成。</summary>
        internal bool AllWavesCompleted => _allCompleted;

        // ====================================================================
        // 构造 —— 新生产链（直接消费有序波次计划 + 出生端口）
        // ====================================================================

        /// <summary>
        /// 构造有序波次状态机（新生产链入口）。
        /// </summary>
        /// <param name="plan">有序波次计划快照（不可为 null）。</param>
        /// <param name="normalSpawnHandler">普通敌人出生 handler（不可为 null）。</param>
        /// <param name="bossPort">Boss 波交接端口（不可为 null；生产注入
        /// <see cref="UnavailableBossWavePort"/>）。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        /// <remarks>
        /// <para>由下一波 BattleRuntimeFactory/Runtime 在每次 Create 时构造新实例，
        /// 不跨局复用（spec "Restart creates clean per-battle state"）。</para>
        /// <para>本构造只消费 <see cref="OrderedWavePlanSnapshot"/>，绝不读取
        /// legacy 的 waveUnitCounts/Boss 概率数组/randomSource/MaxRounds。</para>
        /// <para>计划为空时视为立即全部完成（防御；Validator 会拒绝空计划）。</para>
        /// </remarks>
        internal WaveManager(
            OrderedWavePlanSnapshot plan,
            NormalWaveSpawnHandler normalSpawnHandler,
            IBossWavePort bossPort)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (normalSpawnHandler == null)
            {
                throw new ArgumentNullException(nameof(normalSpawnHandler));
            }

            if (bossPort == null)
            {
                throw new ArgumentNullException(nameof(bossPort));
            }

            _orderedPlan = plan;
            _normalSpawnHandler = normalSpawnHandler;
            _bossPort = bossPort;
            _rowState = WaveRuntimeState.Pending;
            _rowIndex = -1;

            if (plan.Rows.Count == 0)
            {
                _allCompleted = true;
            }
        }

        // ====================================================================
        // Update —— 唯一时间推进入口
        // ====================================================================

        /// <summary>
        /// 推进波次状态机一个时间步。
        /// </summary>
        /// <param name="stepMs">子步时长（毫秒，非负）。</param>
        /// <exception cref="ArgumentOutOfRangeException">stepMs 为负。</exception>
        /// <remarks>
        /// <para>只由本方法推进；行 Completed 后绝不在同一次 Update 进入下一行，
        /// 下一行最早下一次 Update 开始（零延迟也不可一子步穿越多行）。</para>
        /// <para>已停止（<see cref="IsStopped"/>）或全部完成（<see cref="AllWavesCompleted"/>）
        /// 时本方法为空操作，不产生出生也不发布完成。</para>
        /// </remarks>
        internal void Update(long stepMs)
        {
            if (stepMs < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stepMs), $"Update stepMs 不能为负，实际 {stepMs}");
            }

            if (_stopped || _allCompleted)
            {
                return;
            }

            if (_rowIndex < 0)
            {
                // 首行：本次 Update 进入并立即推进。
                BeginRow(0);
            }
            else if (_rowState == WaveRuntimeState.Completed)
            {
                // 上一行已 Completed：下一行最早本次 Update 开始（绝不在上一行完成的那次 Update 进入）。
                if (_rowIndex + 1 >= _orderedPlan.Rows.Count)
                {
                    return;
                }

                BeginRow(_rowIndex + 1);
            }

            AdvanceRow(stepMs);
        }

        // ====================================================================
        // 停止 / 移除 —— 取消与迟到事实边界
        // ====================================================================

        /// <summary>
        /// 停止波次：先置 stopped，再清待出生序号与所有权，并幂等停止 Boss 端口。
        /// </summary>
        /// <remarks>
        /// <para>GameOver/Cancel/Settling 前置调用（task 4.9 调整顺序：先停止 WaveManager
        /// 再清理实体）。停止后 <see cref="Update"/> 为空操作，迟到 remove 不再出生或
        /// 发布完成；调用 <see cref="IBossWavePort.Stop"/> 幂等。</para>
        /// </remarks>
        internal void Stop()
        {
            _stopped = true;
            _nextOrdinal = _totalOrdinals;
            _rowState = WaveRuntimeState.Pending;
            _activeHandles.Clear();
            _bossPort.Stop();
        }

        /// <summary>
        /// 清理本局波次所有权并幂等清理 Boss 端口（GameOver/Cancel/Settling/Dispose 清理尾部调用）。
        /// </summary>
        /// <remarks>
        /// <para><b>职责（task 4.9）：</b>调用 <see cref="IBossWavePort.Cleanup"/> 移除本局
        /// Boss 实体与所有权，并清空活动 handle 与本局行状态。幂等：重复调用安全。</para>
        /// <para><b>与 <see cref="Stop"/> 的分工：</b><see cref="Stop"/> 阻断逻辑
        /// （置 stopped、清待出生与 handle、调用 <c>IBossWavePort.Stop</c>）；本方法只负责
        /// 所有权释放，置 stopped 防止重启，但<b>不会</b>重启状态机，也<b>不会</b>发布
        /// <see cref="AllConfiguredWavesCompleted"/>。</para>
        /// </remarks>
        internal void Cleanup()
        {
            _stopped = true;
            _rowIndex = -1;
            _rowState = WaveRuntimeState.Pending;
            _nextOrdinal = _totalOrdinals;
            _preDelayRemaining = 0;
            _spawnTimer = 0;
            _postDelayRemaining = 0;
            _activeHandles.Clear();
            _bossPort.Cleanup();
        }

        /// <summary>
        /// 按完整 <see cref="WaveEntityHandle"/> 幂等移除活动实体（同一 handle 重复移除为空操作）。
        /// </summary>
        /// <param name="handle">完整波次所有权 handle（含 runtimeId/generation/waveOrder/kind）。</param>
        /// <remarks>
        /// <para>相同 runtimeId 但 generation 不同的迟到事实不匹配，不减少活动计数；
        /// 停止后调用亦为空操作。本方法不直接提交行完成——完成只在后续
        /// <see cref="Update"/> 的 WaitingForClear 判定中发生。</para>
        /// </remarks>
        internal void OnEntityRemoved(WaveEntityHandle handle)
        {
            _activeHandles.Remove(handle);
        }

        /// <summary>
        /// 按普通敌人租借身份幂等移除（把 <see cref="EnemyLeaseIdentity"/> 转换为
        /// Normal handle 后移除；供 EnemyManager 移除事实接线）。
        /// </summary>
        /// <param name="lease">普通敌人租借身份。</param>
        internal void OnEntityRemoved(EnemyLeaseIdentity lease)
        {
            OnEntityRemoved(WaveEntityHandle.FromEnemyLease(lease));
        }

        // ====================================================================
        // 状态机推进（私有）
        // ====================================================================

        /// <summary>进入指定行：置 PreDelay 并初始化该行时序/出生序号。</summary>
        private void BeginRow(int index)
        {
            _rowIndex = index;
            WavePlanEntry entry = _orderedPlan.Rows[index];
            _rowState = WaveRuntimeState.PreDelay;
            _preDelayRemaining = entry.PreDelayMs;
            _spawnTimer = 0;
            _postDelayRemaining = 0;
            _nextOrdinal = 0;
            _totalOrdinals = ComputeTotalOrdinals(entry);

            // 任务 4.8：每行开始发布单次 WaveStarted(order) 事实，
            // 供 BattleManager 同步 BattleState.CurrentRound 到真实 order。
            WaveStarted?.Invoke(entry.Order);
        }

        /// <summary>按当前行状态推进一个时间步。</summary>
        private void AdvanceRow(long stepMs)
        {
            switch (_rowState)
            {
                case WaveRuntimeState.PreDelay:
                    AdvancePreDelay(stepMs);
                    break;

                case WaveRuntimeState.Spawning:
                    AdvanceSpawning(stepMs);
                    break;

                case WaveRuntimeState.WaitingForClear:
                    AdvanceWaitingForClear(stepMs);
                    break;

                case WaveRuntimeState.Completed:
                case WaveRuntimeState.Pending:
                default:
                    // Completed：不在此推进，下一行由 Update 下一次调用进入。
                    break;
            }
        }

        /// <summary>PreDelay 推进：到期时提交首个出生序号，并按剩余时间结转间隔计时。</summary>
        private void AdvancePreDelay(long stepMs)
        {
            long leftover = stepMs;
            if (_preDelayRemaining > 0)
            {
                if (leftover < _preDelayRemaining)
                {
                    _preDelayRemaining -= leftover;
                    return;
                }

                leftover -= _preDelayRemaining;
                _preDelayRemaining = 0;
            }

            // preDelay 已到期（preDelay=0 时进入行的该次更新即可出生）：提交首个出生序号。
            CommitOrdinal(0);
            _nextOrdinal = 1;
            _rowState = WaveRuntimeState.Spawning;

            WavePlanEntry entry = _orderedPlan.Rows[_rowIndex];
            _spawnTimer = entry.SpawnIntervalMs - leftover;
            while (_nextOrdinal < _totalOrdinals && _spawnTimer <= 0)
            {
                CommitOrdinal(_nextOrdinal);
                _nextOrdinal++;
                _spawnTimer += entry.SpawnIntervalMs;
            }

            if (_nextOrdinal >= _totalOrdinals)
            {
                EnterWaitingForClear();
            }
        }

        /// <summary>Spawning 推进：间隔到期时提交出生序号，全部提交后立即转 WaitingForClear。</summary>
        private void AdvanceSpawning(long stepMs)
        {
            WavePlanEntry entry = _orderedPlan.Rows[_rowIndex];
            _spawnTimer -= stepMs;
            while (_nextOrdinal < _totalOrdinals && _spawnTimer <= 0)
            {
                CommitOrdinal(_nextOrdinal);
                _nextOrdinal++;
                _spawnTimer += entry.SpawnIntervalMs;
            }

            if (_nextOrdinal >= _totalOrdinals)
            {
                EnterWaitingForClear();
            }
        }

        /// <summary>WaitingForClear 推进：postDelay 到期且本行活动集合为空时提交一次 Completed。</summary>
        private void AdvanceWaitingForClear(long stepMs)
        {
            _postDelayRemaining -= stepMs;
            if (_postDelayRemaining > 0)
            {
                return;
            }

            if (CountActiveForOrder(_orderedPlan.Rows[_rowIndex].Order) > 0)
            {
                return;
            }

            CompleteRow();
        }

        /// <summary>进入 WaitingForClear：最后一次出生后立即从此刻开始 postDelay，本更新不判 Completed。</summary>
        private void EnterWaitingForClear()
        {
            _rowState = WaveRuntimeState.WaitingForClear;
            _postDelayRemaining = _orderedPlan.Rows[_rowIndex].PostDelayMs;
        }

        /// <summary>提交本行 Completed（仅一次）；最后一行完成时发布 AllConfiguredWavesCompleted 单次事实。</summary>
        private void CompleteRow()
        {
            _rowState = WaveRuntimeState.Completed;

            if (_rowIndex != _orderedPlan.Rows.Count - 1)
            {
                return;
            }

            if (_activeHandles.Count > 0)
            {
                return;
            }

            _allCompleted = true;
            if (!_allCompletedPublished)
            {
                _allCompletedPublished = true;
                AllConfiguredWavesCompleted?.Invoke();
            }
        }

        /// <summary>提交一个出生序号：Normal 按启用车道（先玩家路、后电脑路），Boss 按端口。</summary>
        private void CommitOrdinal(int ordinal)
        {
            WavePlanEntry entry = _orderedPlan.Rows[_rowIndex];
            if (entry.PlayerLane)
            {
                SpawnForLane(entry, isPlayerLane: true);
            }

            if (entry.OpponentLane)
            {
                SpawnForLane(entry, isPlayerLane: false);
            }
        }

        /// <summary>按行类型与车道请求出生并登记成功 handle。</summary>
        /// <exception cref="InvalidOperationException">Boss 端口不可用或 spawn 返回无效 handle。</exception>
        private void SpawnForLane(WavePlanEntry entry, bool isPlayerLane)
        {
            WaveEntityHandle handle;
            if (entry.Kind == WavePlanKind.Boss)
            {
                if (!_bossPort.IsAvailable)
                {
                    throw new InvalidOperationException(
                        $"Boss 波端口不可用：order={entry.Order} 的 Boss 行无法出生，禁止静默跳过/降级");
                }

                handle = _bossPort.Spawn(new BossWaveSpawnRequest(
                    entry.BossId.Value,
                    isPlayerLane,
                    entry.Order,
                    entry.DifficultyIndex,
                    entry.StrategyProfile,
                    ResolveProfile(entry.StrategyProfile)));
            }
            else
            {
                handle = _normalSpawnHandler(new NormalWaveSpawnRequest(
                    entry.EnemyId.Value,
                    isPlayerLane,
                    entry.Order,
                    entry.DifficultyIndex,
                    entry.StrategyProfile,
                    ResolveProfile(entry.StrategyProfile)));
            }

            if (!handle.IsValid)
            {
                throw new InvalidOperationException(
                    $"波次出生返回无效 handle（runtimeId<=0）：order={entry.Order}, kind={entry.Kind}");
            }

            _activeHandles.Add(handle);
        }

        /// <summary>解析策略 profile：按行显式引用的源表索引返回只读乘数数组。</summary>
        /// <exception cref="InvalidOperationException">索引未被计划引用（应已被 Validator 拒绝）。</exception>
        private IReadOnlyList<float> ResolveProfile(int profileIndex)
        {
            if (_orderedPlan.TryGetProfile(profileIndex, out IReadOnlyList<float> profile))
            {
                return profile;
            }

            throw new InvalidOperationException(
                $"策略 profile 索引 {profileIndex} 未被所选计划引用（order={(_rowIndex >= 0 ? _orderedPlan.Rows[_rowIndex].Order : 0)}）");
        }

        /// <summary>计算当前行出生批序号总数（Normal=normalCount；Boss=1）。</summary>
        private static int ComputeTotalOrdinals(WavePlanEntry entry)
        {
            if (entry.Kind == WavePlanKind.Boss)
            {
                return 1;
            }

            return entry.NormalCount;
        }

        /// <summary>统计指定 waveOrder 的活动 handle 数量。</summary>
        private int CountActiveForOrder(int waveOrder)
        {
            int count = 0;
            foreach (WaveEntityHandle handle in _activeHandles)
            {
                if (handle.WaveOrder == waveOrder)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>返回活动 handle 只读快照（数组拷贝，不暴露内部集合）。</summary>
        private IReadOnlyList<WaveEntityHandle> GetActiveHandlesSnapshot()
        {
            var copy = new WaveEntityHandle[_activeHandles.Count];
            _activeHandles.CopyTo(copy);
            return copy;
        }
    }
}
