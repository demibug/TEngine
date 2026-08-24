namespace GameBattle
{
    // ============================================================================
    // 任务 3.2：Mob3Enemy —— 普通敌人薄类型
    // ----------------------------------------------------------------------------
    // 固定 enemyKey="Mob3"、typeIndex=3，共享行为集中在 ConfiguredEnemyBase。
    // 技能召唤类（Zombie/Cavalry/Puppet）不接入普通敌人工厂，不占用 typeIndex。
    // ============================================================================

    /// <summary>
    /// Mob3 普通敌人薄类型：固定 <see cref="ResName"/>="Mob3" 与 <see cref="TypeIndex"/>。
    /// </summary>
    /// <remarks>
    /// <para>所有共享行为（数值初始化、出生、死亡表现边界、presentation-completed 守卫、
    /// Reset 契约）集中在 <see cref="ConfiguredEnemyBase"/>；本类型只固定身份，
    /// 供工厂注册、池类型与测试失败点保持显式。</para>
    /// </remarks>
    internal sealed class Mob3Enemy : ConfiguredEnemyBase
    {
        /// <summary>构造 Mob3 敌人实例（无参构造供 <see cref="BattleObjectPool{Mob3Enemy}"/> 使用）。</summary>
        internal Mob3Enemy()
        {
        }

        /// <inheritdoc/>
        internal override string ResName => "Mob3";

        /// <inheritdoc/>
        internal override int TypeIndex => 3;
    }
}
