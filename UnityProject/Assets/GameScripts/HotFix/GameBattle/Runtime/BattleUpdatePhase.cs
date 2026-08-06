namespace GameBattle
{
    /// <summary>
    /// 战斗逻辑子步内显式固定的更新阶段顺序（决策 0.3 冻结，design.md 第 2 节）。
    /// </summary>
    /// <remarks>
    /// <para>顺序权威来自还原工程实际代码证据：</para>
    /// <para>1. <c>GameLoop.js:31-55</c> 的 Map 插入顺序 —— <c>EnemyManager.update</c> 先于 <c>ProjectileManager.update</c>，<c>ProjectileManager</c> 先于 <c>BattleManager.update</c>（<c>ProjectileManager.test.js:6-15</c> 断言 <c>enemyMgr &lt; bulletMgr &lt; BattleMgr</c>）。</para>
    /// <para>2. <c>BattleManager.js:78-84</c> 内部顺序 —— <c>_updateSpawnState</c>（波次/生成）先于 <c>_updateUnitAttacks</c>（单位攻击调度），后者先于 <c>AttackEffectManager.update</c>（攻击效果）。</para>
    /// <para>3. 实测证据：BattleManager 在当前子步生成的敌人 <c>updateCount=0</c>，下一子步才首次移动；攻击释放时序（弓兵创建投射物）发生在既有弹道推进之后，故新箭从下一子步才移动；单位攻击调度新建的近战效果在当前子步的攻击效果阶段立即累计一次 <c>stepMs</c>。</para>
    /// <para>Unity 映射不复制通用字符串 callback Map，由 <see cref="BattleSimulation"/> 与 <see cref="BattleActionScheduler"/> 显式固化为下列顺序。死亡、奖励和胜负不是独立帧末阶段，而是命中/接触发生点的同步副作用。</para>
    /// </remarks>
    public enum BattleUpdatePhase
    {
        /// <summary>
        /// 帧级到期动作与输入边界：先处理本帧已到期的规则回调（接触伤害、刀兵命中、攻击释放等到期动作），
        /// 再消费本帧输入命令。对应 design.md “帧级到期动作/输入边界”前置阶段。
        /// </summary>
        /// <remarks>
        /// 到期动作的时间戳判断使用 <c>frameNowMs</c>（同帧所有子步观察同一值），与冷却语义一致（决策 0.9、12.2.2 证据）。
        /// </remarks>
        DueActionsAndInput = 0,

        /// <summary>
        /// 敌人阶段：敌人沿路径移动、接触目标并同步维护空间事实（<c>EnemyManager.update</c>）。
        /// 位移与接触冷却分属两个时钟：移动读 <c>stepMs</c>，接触攻击冷却读 <c>frameNowMs</c>。
        /// </summary>
        Enemy = 1,

        /// <summary>
        /// 既有弹道阶段：推进已存在的投射物移动、命中判定与逆序移除（<c>ProjectileManager.update</c>）。
        /// 在本阶段之后创建的新投射物（由攻击释放产生）直到下一子步才首次移动。
        /// </summary>
        Projectile = 2,

        /// <summary>
        /// 攻击释放时序阶段：处理单位攻击调度触发的攻击释放（弓兵创建投射物、近战效果创建等）。
        /// 位于既有弹道推进之后，因此新箭从下一子步才移动（spec.md “Projectile is launched after projectile phase”）。
        /// </summary>
        AttackRelease = 3,

        /// <summary>
        /// 波次/生成阶段：确定性 Mob0 波次计划按子步刷怪（<c>WaveManager</c>，对应 <c>BattleManager._updateSpawnState</c>）。
        /// 当前子步生成的敌人可被后续攻击阶段观察，但直到下一子步才首次移动（spec.md “Enemy is spawned during battle-rule phase”）。
        /// </summary>
        WaveSpawn = 4,

        /// <summary>
        /// 单位攻击调度阶段：每子步只推进一次单位冷却、选取目标并触发一次攻击（<c>AttackScheduler</c>，对应 <c>BattleManager._updateUnitAttacks</c>）。
        /// 冷却判断使用同帧固定 <c>frameNowMs</c>，确保每个单位每子步只触发一次攻击。
        /// </summary>
        UnitAttack = 5,

        /// <summary>
        /// 攻击效果阶段：推进近战/范围攻击效果累计（<c>AttackEffectManager.update</c>）。
        /// 单位攻击调度在本子步创建的近战效果在此阶段立即累计一次本子步 <c>stepMs</c>（spec.md “Melee effect is created by attack scheduling”）。
        /// </summary>
        AttackEffect = 6,
    }
}
