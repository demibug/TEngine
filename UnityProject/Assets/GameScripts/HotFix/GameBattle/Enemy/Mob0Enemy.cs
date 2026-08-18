using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 3.2：Mob0Enemy —— 普通敌人薄类型（共享行为上移到 ConfiguredEnemyBase）
    // ----------------------------------------------------------------------------
    // 职责（design.md 决策 6 / specs/configured-enemy-spawning/spec.md）：
    //   固定 enemyKey="Mob0"、typeIndex=0 的 sealed 薄类型；共享行为（配置数值、
    //   出生、死亡表现边界、presentation-completed 守卫、Reset 契约）集中在
    //   ConfiguredEnemyBase。
    //
    // 保留的旧链临时兼容面（下一波会迁移并删除）：
    //   - InitializeStats(Mob0EnemyInitStats) / GetInitialHealth()：
    //     BattleRuntimeFactory 与既有测试仍以 Acquire → Configure → InitializeStats →
    //     Init → BeginMoving → Register 顺序装配 Mob0。本类型保留这两个旧入口，
    //     经 SetResolvedStats 注入数值（最大血量取 healthByWave[0]）。
    //   - Mob0EnemyInitStats 结构：旧数值快照类型，与 EnemyFactory 旧构造路径配套。
    //
    // 来源证据：
    //   - Mob0Enemy.js:11-58：resourcePath/typeIndex=0/visualPoolKey='mob'。
    //   - NormalEnemyBase.js:22-231：数值初始化与死亡表现边界（本 change 已上移）。
    //   - BattleDataCore.js:60-89 resolveNormalStats：health = healthByWave[wave] *
    //     strategyMultiplier * earlyMultiplier。
    //
    // 不变量：
    //   1. 薄类型：只固定身份，不复制共享逻辑。
    //   2. 纯逻辑层：不持有 Unity GameObject/Animator/Sprite 引用。
    //   3. 不移植灵魂投射/吹飞（design.md:316）。
    // ============================================================================

    /// <summary>
    /// Mob0 普通敌人薄类型：固定 <see cref="EnemyKey"/>="Mob0" 与 <see cref="TypeIndex"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>共享行为上移（design.md 决策 6）：</b>配置数值、出生、死亡表现边界、
    /// presentation-completed 守卫与 Reset 契约集中在 <see cref="ConfiguredEnemyBase"/>；
    /// 本类型只固定身份。旧链 <c>InitializeStats/GetInitialHealth</c> 作为临时兼容保留，
    /// 下一波迁移后删除。</para>
    ///
    /// <para><b>命中锚点：</b><see cref="ProjectileAimOffsetY"/> 对应 Prefab
    /// HitEffectPoint=(0,+0.5) 世界单位反推的逻辑偏移（Unity 世界向上 +0.5 → 逻辑
    /// Y-down -40px）。</para>
    /// </remarks>
    internal sealed class Mob0Enemy : ConfiguredEnemyBase
    {
        /// <summary>构造 Mob0 敌人实例（无参构造供 <see cref="BattleObjectPool{Mob0Enemy}"/> 使用）。</summary>
        internal Mob0Enemy()
        {
        }

        /// <inheritdoc/>
        internal override string EnemyKey => "Mob0";

        /// <inheritdoc/>
        internal override int TypeIndex => 0;

        // ====================================================================
        // 旧链临时兼容面（下一波迁移删除）
        // ====================================================================

        /// <summary>
        /// 【临时兼容】按旧 <see cref="Mob0EnemyInitStats"/> 注入数值（最大血量取 healthByWave[0]）。
        /// </summary>
        /// <remarks>
        /// <para>供 BattleRuntimeFactory 与既有测试的旧装配顺序使用（Acquire →
        /// Configure → InitializeStats → Init）。新链使用
        /// <see cref="ConfiguredEnemyBase.ConfiguredInit"/> 一次性完成注入与初始化。</para>
        /// <para>最大血量取 <see cref="Mob0EnemyInitStats.HealthByWave"/> 首项（旧链语义，
        /// 无难度乘数）；当前血量由 <see cref="EnemyBase.Init"/> 在出生时按 maxHealth 设置。</para>
        /// </remarks>
        internal void InitializeStats(Mob0EnemyInitStats stats)
        {
            int baseHealth = stats.HealthByWave.Count > 0 ? stats.HealthByWave[0] : 0;
            SetResolvedStats(new ConfiguredEnemyResolvedStats(
                maxHealth: baseHealth,
                moveSpeed: stats.Speed,
                contactDamage: stats.ContactDamage,
                rewardGold: stats.RewardGold));
        }

        /// <summary>
        /// 【临时兼容】获取初始血量（已注入数值的最大生命），供旧链传给
        /// <see cref="EnemyBase.Init"/> 完成血量初始化。
        /// </summary>
        /// <returns>已注入的最大生命；未初始化返回 0。</returns>
        internal int GetInitialHealth()
        {
            return Stats.HasValue ? Stats.Value.MaxHealth : 0;
        }

        /// <inheritdoc/>
        /// <remarks>Mob0 命中锚点 X 偏移为 0。</remarks>
        public override float ProjectileAimOffsetX => 0f;

        /// <inheritdoc/>
        /// <remarks>见 <see cref="ProjectileAimOffsetX"/> 注释；Y 偏移 -40 对应命中点低于矩形上边缘。</remarks>
        public override float ProjectileAimOffsetY => -40f;
    }

    // ========================================================================
    // Mob0EnemyInitStats —— 旧 Mob0 数值初始化快照（旧链临时兼容）
    // ========================================================================

    /// <summary>
    /// 【临时兼容】旧 Mob0 敌人数值初始化快照，包含 healthByWave/speed/contactDamage/rewardGold。
    /// </summary>
    /// <remarks>
    /// <para>数值来自 Luban EnemyStats 表（GameConfig.battle.EnemyStats）。本结构仅供
    /// 旧链装配顺序（BattleRuntimeFactory / 既有测试）使用；新链数值统一经
    /// <see cref="EnemyStatsResolver"/> 从 <see cref="EnemyDefinitionSnapshot"/> 解析。</para>
    /// <para>不可变值类型，构造后不修改。</para>
    /// </remarks>
    internal readonly struct Mob0EnemyInitStats
    {
        /// <summary>各波次基础血量（对应 EnemyStats.HealthByWave）。</summary>
        public readonly System.Collections.Generic.IReadOnlyList<int> HealthByWave;

        /// <summary>移动速度 px/s（对应 EnemyStats.MoveSpeed）。</summary>
        public readonly int Speed;

        /// <summary>接触目标伤害（对应 EnemyStats.ContactDamage）。</summary>
        public readonly int ContactDamage;

        /// <summary>击杀奖励金币（对应 EnemyStats.RewardGold）。</summary>
        public readonly int RewardGold;

        /// <summary>构造旧 Mob0 数值初始化快照。</summary>
        /// <param name="healthByWave">各波次基础血量只读列表（不可为 null）。</param>
        /// <param name="speed">移动速度 px/s。</param>
        /// <param name="contactDamage">接触目标伤害。</param>
        /// <param name="rewardGold">击杀奖励金币。</param>
        internal Mob0EnemyInitStats(
            System.Collections.Generic.IReadOnlyList<int> healthByWave,
            int speed,
            int contactDamage,
            int rewardGold)
        {
            HealthByWave = healthByWave
                ?? throw new ArgumentNullException(nameof(healthByWave));
            Speed = speed;
            ContactDamage = contactDamage;
            RewardGold = rewardGold;
        }
    }
}
