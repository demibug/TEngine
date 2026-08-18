namespace GameBattle
{
    // ============================================================================
    // 任务 4.2：WaveRuntimeState —— 逐行波次运行状态
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 4 / specs/ordered-wave-plan/spec.md）：
    //   WaveManager 逐行状态机 Pending → PreDelay → Spawning → WaitingForClear →
    //   Completed 的枚举。行完成后绝不在同一次 Update 进入下一行。
    // ============================================================================

    /// <summary>
    /// 逐行波次运行状态（单一状态机）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><see cref="Pending"/>：尚未开始。</item>
    /// <item><see cref="PreDelay"/>：行已进入，等待首次出生前置延迟。</item>
    /// <item><see cref="Spawning"/>：按 spawnInterval 提交后续出生序号。</item>
    /// <item><see cref="WaitingForClear"/>：出生已全部提交，等待 postDelay 到期且本行活动实体清空。</item>
    /// <item><see cref="Completed"/>：本行仅提交一次完成事实。</item>
    /// </list>
    /// </remarks>
    internal enum WaveRuntimeState
    {
        /// <summary>未开始。</summary>
        Pending = 0,

        /// <summary>等待首次出生前置延迟。</summary>
        PreDelay = 1,

        /// <summary>按间隔提交出生序号。</summary>
        Spawning = 2,

        /// <summary>等待清场（postDelay + 活动实体为零）。</summary>
        WaitingForClear = 3,

        /// <summary>本行完成（仅一次）。</summary>
        Completed = 4,
    }
}
