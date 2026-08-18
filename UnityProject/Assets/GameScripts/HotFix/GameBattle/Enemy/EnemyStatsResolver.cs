using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.3：EnemyStatsResolver —— 唯一普通敌人数值解析器
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 6 / specs/configured-enemy-spawning/spec.md
    //   "Difficulty selection is explicit" / "Configured stats determine every normal enemy"）：
    //   以 difficultyIndex 同时直接索引 EnemyDefinitionSnapshot.HealthByWave、
    //   所选 strategy profile 与 EarlyRoundHealthMultipliers；越界显式失败，不夹取、
    //   无 fallback。最大生命按 "基础血量 × 策略乘数 × 早期乘数" 用 double 计算，
    //   最终只调用一次 Math.Round(value, MidpointRounding.AwayFromZero) 转 int 且最小 1。
    //   速度/接触伤害/奖励直接来自同一 EnemyDefinitionSnapshot。
    //
    // 来源证据（BattleDataCore.js:60-89 resolveNormalStats）：
    //   health = healthByWave[wave] * strategyMultiplier * earlyMultiplier；
    //   speed = cfg.speed。C# 移植收敛为唯一解析器，不在各敌人类型重复转换。
    //
    // 不变量：
    //   1. 唯一解析器：全部普通敌人生成路径共用本类型，禁止内嵌数值 fallback。
    //   2. 越界显式失败：difficultyIndex 越界抛 EnemyStatsResolutionException。
    //   3. 一次舍入：double 乘法后只舍入一次（AwayFromZero），结果最小 1。
    // ============================================================================

    /// <summary>
    /// 普通敌人数值解析失败异常：越界难度/策略/早期乘数时显式失败，不夹取。
    /// </summary>
    /// <remarks>
    /// <para>配置校验在启动前应已保证引用合法；本异常作为运行期防御性显式失败，
    /// 不得被夹取或 fallback 掩盖（spec "Reject an invalid difficulty index"）。</para>
    /// </remarks>
    internal sealed class EnemyStatsResolutionException : Exception
    {
        /// <summary>构造解析失败异常。</summary>
        internal EnemyStatsResolutionException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// 已解析的普通敌人实例数值（不可变值类型）。
    /// </summary>
    /// <remarks>
    /// <para>普通敌人出生时由 <see cref="EnemyStatsResolver.Resolve"/> 一次性得到
    /// 最大生命、移动速度、接触伤害和击杀奖励；速度/接触伤害/奖励与血量曲线来自
    /// 同一 <see cref="EnemyDefinitionSnapshot"/>（spec "Configured stats determine
    /// every normal enemy instance"）。</para>
    /// </remarks>
    internal readonly struct ConfiguredEnemyResolvedStats
    {
        /// <summary>解析后的最大生命（>= 1）。</summary>
        public readonly int MaxHealth;

        /// <summary>移动速度（px/s，来自定义）。</summary>
        public readonly int MoveSpeed;

        /// <summary>接触目标伤害（来自定义）。</summary>
        public readonly int ContactDamage;

        /// <summary>击杀奖励金币（来自定义）。</summary>
        public readonly int RewardGold;

        /// <summary>构造解析后的实例数值。</summary>
        internal ConfiguredEnemyResolvedStats(
            int maxHealth,
            int moveSpeed,
            int contactDamage,
            int rewardGold)
        {
            MaxHealth = maxHealth;
            MoveSpeed = moveSpeed;
            ContactDamage = contactDamage;
            RewardGold = rewardGold;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"ConfiguredEnemyResolvedStats(maxHealth={MaxHealth}, moveSpeed={MoveSpeed}, " +
                   $"contactDamage={ContactDamage}, rewardGold={RewardGold})";
        }
    }

    /// <summary>
    /// 唯一普通敌人数值解析器：按 difficultyIndex 同时索引血量曲线、策略 profile
    /// 与早期乘数，统一一次舍入，速度/接触伤害/奖励直接来自同一敌人定义。
    /// </summary>
    /// <remarks>
    /// <para><b>唯一解析器（design.md:125）：</b>Mob0～Mob3 全部经本类型解析数值，
    /// 不在各敌人类型内嵌血量数组/固定速度/固定伤害/固定奖励 fallback。</para>
    ///
    /// <para><b>索引语义（spec "Difficulty selection is explicit"）：</b>
    /// <paramref name="difficultyIndex"/> 同时直接索引
    /// <see cref="EnemyDefinitionSnapshot.HealthByWave"/>、
    /// <paramref name="strategyProfile"/> 与
    /// <see cref="EnemyDefinitionSnapshot.EarlyRoundHealthMultipliers"/>；
    /// 三者任一越界都显式失败（<see cref="EnemyStatsResolutionException"/>），
    /// 不夹取到首尾值，不使用默认乘数掩盖错误。</para>
    ///
    /// <para><b>一次舍入（spec "Configured stats determine every normal enemy instance"）：</b>
    /// 以 double 计算 baseHealth × strategyMultiplier × earlyMultiplier，
    /// 最终只调用一次 <see cref="Math.Round(double, MidpointRounding)"/>
    /// （<see cref="MidpointRounding.AwayFromZero"/>）转 int 且最小 1；
    /// 相同输入必定得到相同数值。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 EnemyFactory 使用，
    /// 不对其他程序集暴露。</para>
    /// </remarks>
    internal static class EnemyStatsResolver
    {
        /// <summary>
        /// 解析普通敌人实例数值。
        /// </summary>
        /// <param name="definition">敌人定义快照（不可为 null）。</param>
        /// <param name="difficultyIndex">0-based 难度索引（同时索引血量曲线、策略 profile、早期乘数）。</param>
        /// <param name="strategyProfile">该行显式引用的策略乘数 profile（不可为 null）。</param>
        /// <returns>解析后的实例数值（最大生命 >= 1）。</returns>
        /// <exception cref="ArgumentNullException">definition 或 strategyProfile 为 null。</exception>
        /// <exception cref="EnemyStatsResolutionException">
        /// difficultyIndex 为负，或超出 HealthByWave / strategyProfile / EarlyRoundHealthMultipliers 任一范围。</exception>
        /// <remarks>
        /// <para>乘法在 double 上进行：<c>value = (double)baseHealth * strategyMultiplier * earlyMultiplier</c>。</para>
        /// <para>最终只舍入一次：<c>maxHealth = max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero))</c>。</para>
        /// </remarks>
        internal static ConfiguredEnemyResolvedStats Resolve(
            EnemyDefinitionSnapshot definition,
            int difficultyIndex,
            IReadOnlyList<float> strategyProfile)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (difficultyIndex < 0)
            {
                throw new EnemyStatsResolutionException(
                    $"difficultyIndex={difficultyIndex} 为负，普通敌人难度索引必须 >= 0");
            }

            if (difficultyIndex >= definition.HealthByWave.Count)
            {
                throw new EnemyStatsResolutionException(
                    $"difficultyIndex={difficultyIndex} 超出 HealthByWave 范围（Count={definition.HealthByWave.Count}）");
            }

            if (strategyProfile == null || difficultyIndex >= strategyProfile.Count)
            {
                throw new EnemyStatsResolutionException(
                    $"difficultyIndex={difficultyIndex} 超出 strategyProfile 范围（Count={strategyProfile?.Count ?? 0}）");
            }

            if (difficultyIndex >= definition.EarlyRoundHealthMultipliers.Count)
            {
                throw new EnemyStatsResolutionException(
                    $"difficultyIndex={difficultyIndex} 超出 EarlyRoundHealthMultipliers 范围" +
                    $"（Count={definition.EarlyRoundHealthMultipliers.Count}）");
            }

            // 以 double 计算：baseHealth × strategyMultiplier × earlyMultiplier。
            double baseHealth = definition.HealthByWave[difficultyIndex];
            double strategyMultiplier = strategyProfile[difficultyIndex];
            double earlyMultiplier = definition.EarlyRoundHealthMultipliers[difficultyIndex];
            double value = baseHealth * strategyMultiplier * earlyMultiplier;

            // 最终只调用一次舍入（AwayFromZero），结果最小 1。
            int maxHealth = Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));

            // 速度/接触伤害/奖励直接来自同一敌人定义（无 fallback）。
            return new ConfiguredEnemyResolvedStats(
                maxHealth,
                definition.MoveSpeed,
                definition.ContactDamage,
                definition.RewardGold);
        }
    }
}
