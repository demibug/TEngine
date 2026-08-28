namespace GameBattle
{
    // ============================================================================
    // 伤害飘字：EnemyDamageViewData —— 敌人受击伤害表现 DTO
    // ----------------------------------------------------------------------------
    // 职责：
    //   把"敌人受击原始伤害"收敛为不可变 DTO，贯通 EnemyManager.EnemyDamaged →
    //   BattlePresenter → IBattleViewPort.OnEnemyDamaged → Null/Unity 端口。
    //
    // 字段语义：
    //   - RuntimeId：本次租借的敌人运行时 ID。
    //   - RawDamage：Hit 传入的原始伤害值（>0 才会构造本 DTO；显示原值而非实际生命 delta，
    //     过量伤害也显示原始伤害）。
    // 不变量：
    //   1. 不可变值类型：构造后不可修改，逐次命中不产生 DTO 堆分配。
    //   2. 只携带标量与键，不持有 Enemy 实体或 Unity 对象引用（design.md:214 端口参数约束）。
    //   3. 仅在 EnemyBase.Hit 有效正伤害路径构造（Buff/最大生命变化不触发）。
    // ============================================================================

    /// <summary>
    /// 敌人受击伤害表现 DTO：runtimeId + 原始伤害。
    /// </summary>
    /// <remarks>
    /// <para><b>贯通链：</b>由 <see cref="EnemyManager"/> 在敌人受击扣血后构造并经
    /// <see cref="EnemyManager.EnemyDamaged"/> 发布；<see cref="BattlePresenter"/>
    /// 原样转发；<see cref="IBattleViewPort.OnEnemyDamaged"/> 的 Null 实现忽略，
    /// <see cref="UnityBattleViewPort"/> 交给 <see cref="DamageNumberSystem"/>
    /// 在敌人 HitEffectPoint 显示原工程风格伤害飘字。</para>
    ///
    /// <para><b>显示原始伤害：</b><see cref="RawDamage"/> 为 <c>EnemyBase.Hit</c> 传入的
    /// 原始伤害值，而非实际生命 delta——过量伤害（伤害大于剩余血量）仍显示原始数值。
    /// 仅 <see cref="EnemyBase.Hit"/> 有效正伤害路径构造本 DTO；Buff 生命变化
    /// （CommitMaximumHealth/CommitCurrentHealthModifier）不经过 Hit，不触发飘字。</para>
    /// </remarks>
    public readonly struct EnemyDamageViewData
    {
        /// <summary>敌人运行时 ID。</summary>
        public int RuntimeId { get; }

        /// <summary>Hit 传入的原始伤害值（&gt; 0）。显示原值，而非实际生命 delta。</summary>
        public int RawDamage { get; }

        /// <summary>构造敌人受击伤害表现 DTO。</summary>
        /// <param name="runtimeId">敌人运行时 ID。</param>
        /// <param name="rawDamage">Hit 原始伤害值（&gt; 0）。</param>
        public EnemyDamageViewData(int runtimeId, int rawDamage)
        {
            RuntimeId = runtimeId;
            RawDamage = rawDamage;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"EnemyDamageViewData(runtimeId={RuntimeId}, rawDamage={RawDamage})";
        }
    }
}
