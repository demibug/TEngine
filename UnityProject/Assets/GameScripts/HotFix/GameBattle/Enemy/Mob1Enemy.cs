namespace GameBattle
{
    // ============================================================================
    // 任务 3.2：Mob1Enemy —— 普通敌人薄类型
    // ----------------------------------------------------------------------------
    // 固定 enemyKey="Mob1"、typeIndex=1，共享行为集中在 ConfiguredEnemyBase。
    // 技能召唤类（Zombie/Cavalry/Puppet）不接入普通敌人工厂，不占用 typeIndex。
    // ============================================================================

    /// <summary>
    /// Mob1 普通敌人薄类型：固定 <see cref="ResName"/>="Mob1" 与 <see cref="TypeIndex"/>。
    /// </summary>
    /// <remarks>
    /// <para>所有共享行为（数值初始化、出生、死亡表现边界、presentation-completed 守卫、
    /// Reset 契约）集中在 <see cref="ConfiguredEnemyBase"/>；本类型只固定身份，
    /// 供工厂注册、池类型与测试失败点保持显式。</para>
    /// </remarks>
    internal sealed class Mob1Enemy : ConfiguredEnemyBase
    {
        /// <summary>构造 Mob1 敌人实例（无参构造供 <see cref="BattleObjectPool{Mob1Enemy}"/> 使用）。</summary>
        internal Mob1Enemy()
        {
        }

        /// <inheritdoc/>
        internal override string ResName => "Mob1";

        /// <inheritdoc/>
        internal override int TypeIndex => 1;
    }
}
