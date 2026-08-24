using System;
using System.Collections.Generic;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.4：BossStatsResolver —— Boss 数值解析器（复用普通基线 + 配置倍率）
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 2 / specs/zhang-liang-boss-runtime/spec.md
    //   "ZhangLiang stats resolve deterministically"）：
    //   Boss 最大生命 = 普通敌人基线（EnemyStatsResolver 解析的行 map/difficulty/
    //   strategy 基线）× 配置 healthMultiplier，最终只调用一次
    //   Math.Round(value, AwayFromZero)；速度/接触伤害/奖励/逻辑尺寸全部来自
    //   Boss 配置，无运行时 fallback。
    // ============================================================================

    /// <summary>
    /// Boss 数值解析器：复用 <see cref="EnemyStatsResolver"/> 的普通基线并应用
    /// <see cref="BossDefinitionSnapshot.HealthMultiplier"/>。
    /// </summary>
    /// <remarks>
    /// <para>spec "Baseline health is H"：Boss 行先按普通基线解析 H，再取
    /// <c>roundAwayFromZero(H × healthMultiplier)</c> 作为最大生命。相同输入必定
    /// 得到相同数值（确定性命中）。</para>
    /// <para>速度/接触伤害/奖励直接来自 Boss 定义（MoveSpeed/ContactDamage/RewardGold），
    /// 缺配置由启动校验器拒绝，不在此处 fallback。</para>
    /// </remarks>
    internal static class BossStatsResolver
    {
        /// <summary>
        /// 解析 Boss 实例数值。
        /// </summary>
        /// <param name="baselineDefinition">普通敌人基线定义（按地图 EnemyTypeIndex 解析）。</param>
        /// <param name="bossDefinition">Boss 定义（不可为 null）。</param>
        /// <param name="difficultyIndex">0-based 难度索引。</param>
        /// <param name="strategyProfile">该行显式引用的策略乘数 profile（不可为 null）。</param>
        /// <returns>解析后的 Boss 数值（最大生命 &gt;= 1）。</returns>
        /// <exception cref="ArgumentNullException">baselineDefinition 或 bossDefinition 为 null。</exception>
        /// <exception cref="EnemyStatsResolutionException">难度索引越界或配置非法（不夹取）。</exception>
        internal static ConfiguredEnemyResolvedStats Resolve(
            EnemyDefinitionSnapshot baselineDefinition,
            BossDefinitionSnapshot bossDefinition,
            int difficultyIndex,
            IReadOnlyList<float> strategyProfile)
        {
            if (baselineDefinition == null)
            {
                throw new ArgumentNullException(nameof(baselineDefinition));
            }

            if (bossDefinition == null)
            {
                throw new ArgumentNullException(nameof(bossDefinition));
            }

            // 普通基线：EnemyStatsResolver.Resolve 唯一入口（H）。
            ConfiguredEnemyResolvedStats baseline = EnemyStatsResolver.Resolve(
                baselineDefinition, difficultyIndex, strategyProfile);

            // maxHealth = roundAwayFromZero(H × healthMultiplier)，只舍入一次，最小 1。
            double value = (double)baseline.MaxHealth * bossDefinition.HealthMultiplier;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 1d || value > int.MaxValue)
            {
                throw new EnemyStatsResolutionException(
                    $"Boss id={bossDefinition.Id} 解析最大生命={value} 非法（H={baseline.MaxHealth}, " +
                    $"healthMultiplier={bossDefinition.HealthMultiplier}），禁止运行时 fallback");
            }

            int maxHealth = checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

            return new ConfiguredEnemyResolvedStats(
                maxHealth,
                (int)bossDefinition.MoveSpeed,
                bossDefinition.ContactDamage,
                bossDefinition.RewardGold);
        }
    }
}
