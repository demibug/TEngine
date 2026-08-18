using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 4.1：IBossWavePort + UnavailableBossWavePort —— 最小 Boss 波交接端口
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 8 / specs/ordered-wave-plan/spec.md）：
    //   Boss 行通过最小端口与具体 Boss 实现解耦。本 change 只定义端口契约，
    //   不实现任何 Boss 实体/技能/表现；生产默认注入不可用实现。
    //
    // 契约（任务已决定）：
    //   1. IsAvailable：Boss 波能力是否可用（生产默认 false）。
    //   2. SupportedBossKeys：只读支持键列表。
    //   3. Spawn(BossWaveSpawnRequest) -> WaveEntityHandle：按 bossKey/lane/waveOrder/
    //      difficulty 请求一次 Boss 出生。
    //   4. Stop/Cleanup 幂等：GameOver/Cancel 时幂等停止并清理本局 Boss。
    //   5. 生产默认 Unavailable 实现不创建 Boss，Spawn 显式失败；
    //      状态机在 Boss 行遇到不可用端口时显式失败，不静默跳过或降级成普通波。
    //
    // 不变量：
    //   1. 端口不泄漏 Skill/Boss 内部模型，只承载波次需要的出生/清理语义。
    //   2. 纯逻辑，无反射/dynamic/Emit，热更安全。
    // ============================================================================

    /// <summary>
    /// 最小 Boss 波交接端口：Boss 行的出生与清理边界。
    /// </summary>
    /// <remarks>
    /// <para><b>能力描述：</b><see cref="IsAvailable"/> 声明能力是否可用，
    /// <see cref="SupportedBossKeys"/> 声明支持的 bossKey。启动前的能力校验
    /// （<see cref="BattleRuntimeCapabilities"/>）在世界加载前拒绝"所选计划含 Boss 但
    /// 端口不可用"的配置。</para>
    /// <para><b>Spawn：</b>按 <see cref="BossWaveSpawnRequest"/> 请求一次 Boss 出生并返回
    /// 波次所有权 handle；失败必须抛异常（不返回无效 handle）。移除事实与普通敌人共用
    /// <see cref="WaveEntityHandle"/> 语义。</para>
    /// <para><b>清理：</b><see cref="Stop"/> 幂等停止（GameOver/Cancel 时调用），
    /// <see cref="Cleanup"/> 幂等移除本局 Boss 实体；重复调用为空操作。</para>
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 WaveManager 与测试使用。</para>
    /// </remarks>
    internal interface IBossWavePort
    {
        /// <summary>Boss 波能力是否可用（生产默认 false）。</summary>
        bool IsAvailable { get; }

        /// <summary>支持的 bossKey 只读列表。</summary>
        IReadOnlyList<string> SupportedBossKeys { get; }

        /// <summary>
        /// 请求一次 Boss 出生（按请求的 bossKey/lane/waveOrder/difficulty）。
        /// </summary>
        /// <param name="request">Boss 出生请求（不可为 null）。</param>
        /// <returns>成功出生后的波次所有权 handle。</returns>
        /// <exception cref="InvalidOperationException">端口不可用或键不受支持（显式失败）。</exception>
        WaveEntityHandle Spawn(BossWaveSpawnRequest request);

        /// <summary>幂等停止：不再出生新 Boss（GameOver/Cancel 时调用）。</summary>
        void Stop();

        /// <summary>幂等清理：移除本局 Boss 实体与所有权。重复调用为空操作。</summary>
        void Cleanup();
    }

    /// <summary>
    /// 生产默认不可用 Boss 波端口：不创建任何 Boss，Spawn 显式失败。
    /// </summary>
    /// <remarks>
    /// <para>本 change 不实现任何 Boss；生产启动注入本实现，所选计划含 Boss 行时由
    /// 能力校验在启动前拒绝。状态机遇到 Boss 行时 <see cref="IsAvailable"/> 为 false，
    /// 显式失败而不静默跳过/降级/提前完成。</para>
    /// <para><see cref="Stop"/>/<see cref="Cleanup"/> 为幂等空操作。</para>
    /// </remarks>
    internal sealed class UnavailableBossWavePort : IBossWavePort
    {
        /// <summary>
        /// 生产默认不可用端口的共享实例（Boss 波交接端口，无内部可变状态）。
        /// </summary>
        /// <remarks>
        /// <para>生产启动注入本实例：<see cref="IsAvailable"/> 为 false，Spawn 显式失败。
        /// 默认 active plan 不含 Boss 行，由能力校验在世界加载前拒绝含 Boss 的计划
        /// （spec "Reject Boss content when the capability is absent"）。</para>
        /// <para><see cref="Stop"/>/<see cref="Cleanup"/> 为幂等空操作，跨局安全复用。</para>
        /// </remarks>
        public static readonly UnavailableBossWavePort Instance = new UnavailableBossWavePort();

        /// <summary>能力不可用。</summary>
        public bool IsAvailable => false;

        /// <summary>不支持任何 bossKey。</summary>
        public IReadOnlyList<string> SupportedBossKeys => Array.Empty<string>();

        /// <summary>显式失败：生产默认实现不创建 Boss。</summary>
        public WaveEntityHandle Spawn(BossWaveSpawnRequest request)
        {
            throw new InvalidOperationException(
                "Boss 波端口不可用：生产默认实现不创建 Boss，Spawn 显式失败");
        }

        /// <summary>幂等空操作。</summary>
        public void Stop()
        {
        }

        /// <summary>幂等空操作。</summary>
        public void Cleanup()
        {
        }
    }
}
