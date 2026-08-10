namespace GameBattle
{
    /// <summary>
    /// 敌军终点攻击载荷：攻击者运行时 ID / 目标车道 / 伤害值。
    /// </summary>
    /// <remarks>
    /// <para>不可变值类型，避免终点攻击在传递中暴露阿斗状态或 UI 引用。
    /// EnemyBase 只依赖"可被终点攻击的目标"契约，不直接引用 BattleTarget、BattleState
    /// 或任何表现对象。</para>
    /// </remarks>
    internal readonly struct EndPointAttackRequest
    {
        /// <summary>攻击者运行时 ID（对应 EnemyBase._id）。</summary>
        public readonly int AttackerRuntimeId;

        /// <summary>目标车道：true=玩家方车道，false=对手方车道。</summary>
        public readonly bool IsPlayerLane;

        /// <summary>终点攻击伤害值（当前固定为 1）。</summary>
        public readonly int Damage;

        internal EndPointAttackRequest(int attackerRuntimeId, bool isPlayerLane, int damage)
        {
            AttackerRuntimeId = attackerRuntimeId;
            IsPlayerLane = isPlayerLane;
            Damage = damage;
        }
    }

    /// <summary>
    /// 可被敌军终点攻击的目标契约。
    /// </summary>
    /// <remarks>
    /// <para>由 <see cref="BattleTarget"/> 实现，内部复用其既有
    /// <c>ApplyDamage(damage, sourceRuntimeId)</c> 对阿斗扣血。EnemyBase 只依赖本接口，
    /// 保持敌军与阿斗状态、UI、胜负结算解耦。</para>
    /// <para>返回 true=本次终点攻击生效（目标接受伤害）；false=目标已死亡、已冻结或拒绝伤害。</para>
    /// </remarks>
    internal interface IEnemyEndPointAttackTarget
    {
        /// <summary>
        /// 接收一次敌军终点攻击。
        /// </summary>
        /// <param name="request">终点攻击载荷。</param>
        /// <returns>true=攻击生效（目标接受伤害）；false=目标已死亡或拒绝伤害。</returns>
        bool ReceiveEndPointAttack(EndPointAttackRequest request);
    }
}
