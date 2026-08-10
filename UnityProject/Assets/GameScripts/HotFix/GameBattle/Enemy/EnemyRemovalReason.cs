namespace GameBattle
{
    /// <summary>
    /// 敌军移除原因：驱动移除时的表现与回收语义。
    /// </summary>
    /// <remarks>
    /// <para>由 <see cref="EnemyManager"/> 统一消费：按原因决定注销时是否播放死亡表现，
    /// 并保证同一敌军在任何原因下都恰好归还对象池一次。</para>
    /// </remarks>
    internal enum EnemyRemovalReason
    {
        /// <summary>被我方上场士兵击杀，保留死亡表现（playDeathEffect=true）。</summary>
        Killed,

        /// <summary>到终点攻击阿斗后回收，不播放死亡特效（playDeathEffect=false）。</summary>
        ReachedEndPoint,

        /// <summary>强制清场 / 战斗结束批量清理（playDeathEffect=false）。</summary>
        Forced,
    }
}
