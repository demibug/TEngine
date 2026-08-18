using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 2.8：BattleRuntimeCapabilities —— 启动事务的运行时能力声明
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 3 / specs/ordered-wave-plan/spec.md
    //   "Reject Boss content when the capability is absent"）：
    //   在创建战斗运行时前，向 Boss 波能力 gate 声明当前运行时是否支持 Boss 波，
    //   以及支持哪些 bossKey。所选计划含 Boss 行而能力未声明支持时，
    //   在世界加载前显式拒绝（不得静默跳过、降级成普通波或提前完成）。
    //
    // 不变量：
    //   1. 不可变：SupportedBossKeys 深拷贝为只读数组。
    //   2. None 保留给纯 Normal/显式无 Boss 的测试；Production 仅声明 ZhangLiang。
    //   3. SupportsBossKey 同时要求能力可用且 bossKey 在显式集合内。
    // ============================================================================

    /// <summary>
    /// 一次战斗启动事务的运行时能力声明（不可变），供启动前能力校验消费。
    /// </summary>
    /// <remarks>
    /// <para><b>能力集合：</b><see cref="None"/> 明确不支持 Boss；
    /// <see cref="Production"/> 仅支持首期 ZhangLiang。</para>
    /// <para><b>注入：</b>测试可构造受控能力实例，验证不支持/未知 key 的前置拒绝。</para>
    /// </remarks>
    public sealed class BattleRuntimeCapabilities
    {
        /// <summary>
        /// 是否支持 Boss 波。
        /// </summary>
        public bool SupportsBossWaves { get; }

        /// <summary>
        /// 支持显式只读的 bossKey 集合（仅当 <see cref="SupportsBossWaves"/> 为 true 时有意义）。
        /// </summary>
        public IReadOnlyList<string> SupportedBossKeys { get; }

        /// <summary>
        /// 无 Boss 能力：不支持 Boss 波、无受支持 bossKey。
        /// </summary>
        public static BattleRuntimeCapabilities None { get; } =
            new BattleRuntimeCapabilities(supportsBossWaves: false, supportedBossKeys: Array.Empty<string>());

        /// <summary>当前生产能力：仅支持 ZhangLiang Boss 波。</summary>
        public static BattleRuntimeCapabilities Production { get; } =
            new BattleRuntimeCapabilities(supportsBossWaves: true, supportedBossKeys: new[] { "ZhangLiang" });

        /// <summary>构造运行时能力声明。</summary>
        /// <param name="supportsBossWaves">是否支持 Boss 波。</param>
        /// <param name="supportedBossKeys">支持的 bossKey 集合（构造时深拷贝为只读数组）。</param>
        public BattleRuntimeCapabilities(bool supportsBossWaves, IReadOnlyList<string> supportedBossKeys)
        {
            SupportsBossWaves = supportsBossWaves;

            IReadOnlyList<string> source = supportedBossKeys ?? Array.Empty<string>();
            var keys = new string[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                keys[i] = source[i];
            }

            SupportedBossKeys = keys;
        }

        /// <summary>
        /// 判断是否支持指定 bossKey：要求 Boss 波能力可用且 bossKey 在显式集合内。
        /// </summary>
        /// <param name="bossKey">Boss 敌人键。</param>
        /// <returns>支持时返回 true。</returns>
        public bool SupportsBossKey(string bossKey)
        {
            if (!SupportsBossWaves || string.IsNullOrEmpty(bossKey))
            {
                return false;
            }

            for (int i = 0; i < SupportedBossKeys.Count; i++)
            {
                if (string.Equals(SupportedBossKeys[i], bossKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
