namespace GameBattle
{
    // ============================================================================
    // 任务 6.2：SpearSoldier —— 枪兵，创建 360ms 枪兵命中效果
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 182 行 / Unit/SpearSoldier.cs）：
    //   创建 360ms 枪兵命中效果。
    //
    // 来源证据（还原工程 SpearSoldier.js，经 5.4 PikeAttackEffect.cs 注释引用）：
    //   SpearSoldier 继承 SoldierBase，核心行为：
    //     - attack()：创建 PikeAttackEffect → launch → attackEffectManager.add
    //   PikeAttackEffect.launch（task 5.4 已实现）：
    //     hitAtMs = PIKE_HIT_DELAY_MS(360) / playbackRate
    //     durationMs = PIKE_EFFECT_DURATION_MS(480) / playbackRate
    //     super.launch({owner, enemyManager, damage, radius, durationMs, hitAtMs})
    //   枪兵为范围延迟命中：360ms 后对半径内敌人命中，480ms 完成回收。
    //   radius=attackRange（对应 JS SpearSoldier.js:54 radius:this.attackRange）。
    //
    // 决策依据：
    //   - design.md 第 182 行：创建 360ms 枪兵命中效果。
    //   - design.md 第 9 行：规则层不持有 Unity GameObject 或表现组件。
    //   - task 6.2 约束：override PerformAttack 连接 PikeAttackEffect；
    //     不引入武将、技能、升级或 AI 行为。
    //   - task 5.4 已实现 PikeAttackEffect（360ms 范围延迟命中）。
    //
    // 与攻击效果的连接：
    //   PerformAttack 中：
    //     1. 创建 PikeAttackEffect（new）。
    //     2. Launch(owner, resolver, enemyManager, damage, cellWidth, cellHeight, radius)。
    //     3. 经 AttackEffectManager.Add 登记效果。
    //   枪兵不查询目标——PikeAttackEffect 在命中时自行查询半径内目标（对应 JS hit()）。
    //   枪兵 attack() 直接创建效果，不二次查询目标（与刀兵/弓兵不同）。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/表现组件。
    //   2. 池化友好：ResetState 继承 SoldierBase（无专属字段需扩展）。
    //   3. 范围命中：PikeAttackEffect 在 360ms 时查询半径内敌人。
    //   4. 不引入武将/技能/升级/AI。
    // ============================================================================

    /// <summary>
    /// 枪兵：创建 360ms 枪兵命中效果（task 6.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 182 行）：</b>创建 360ms 枪兵命中效果。
    /// 替代还原工程 <c>SpearSoldier.js</c>。</para>
    ///
    /// <para><b>攻击流程（对应 JS <c>attack()</c>）：</b>
    /// <see cref="PerformAttack"/> 中创建 <see cref="PikeAttackEffect"/> 并 Launch，
    /// 经 <see cref="AttackEffectManager.Add"/> 登记效果。效果在 360ms 后对半径内敌人命中
    /// （task 5.4 已实现时序）。</para>
    ///
    /// <para><b>范围命中（与刀兵单目标不同）：</b>
    /// 枪兵不查询目标，<see cref="PikeAttackEffect"/> 在命中时自行查询半径内目标
    /// （对应 JS <c>hit()</c>）。枪兵 <c>attack()</c> 直接创建效果。</para>
    ///
    /// <para><b>纯逻辑（design.md 第 9 行）：</b>不持有 Unity GameObject 或表现组件。</para>
    ///
    /// <para><b>池化（task 4.1）：</b>继承 <see cref="SoldierBase"/> 的 ResetState 契约，
    /// 无专属字段需扩展。池化由 UnitFactory（task 6.3）统一管理。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部 UnitFactory（task 6.3）
    /// 创建使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class SpearSoldier : SoldierBase
    {
        // ====================================================================
        // Configure —— 注入运行时依赖（继承 SoldierBase，无额外依赖）
        // ====================================================================

        /// <summary>
        /// 配置枪兵运行时依赖。委托 <see cref="SoldierBase.Configure"/> 注入通用依赖。
        /// </summary>
        /// <remarks>
        /// 枪兵无投射物管理器等额外依赖，直接委托基类。4 兵种 Configure 覆写统一签名
        /// 供 UnitFactory（task 6.3）按兵种调用。
        /// </remarks>
        internal void Configure(
            EnemyManager enemyManager,
            AttackResolver attackResolver,
            AttackEffectManager attackEffectManager,
            float cellSize,
            int opponentAttackMultiplier)
        {
            base.Configure(enemyManager, attackResolver, attackEffectManager,
                cellSize, opponentAttackMultiplier);
        }

        // ====================================================================
        // 初始化入口（供 UnitFactory task 6.3 与测试调用）
        // ====================================================================

        /// <summary>暴露 AssignRuntimeId 供 UnitFactory/测试调用。</summary>
        internal void AssignRuntimeIdForTest(int id) => AssignRuntimeId(id);

        /// <summary>暴露 Init 供 UnitFactory/测试调用。</summary>
        internal void InitForTest(string unitText, bool side, float width, float height)
            => Init(unitText, side, width, height);

        /// <summary>暴露 ActivatePlacement 供 UnitFactory/测试调用。</summary>
        internal void ActivateAt(float pixelX, float pixelY) => ActivatePlacement(pixelX, pixelY);

        /// <summary>暴露 InitializeStats 供 UnitFactory/测试调用。</summary>
        internal void InitStats(UnitConfigSnapshot config) => InitializeStats(config);

        // ====================================================================
        // PerformAttack —— 创建枪兵攻击效果（对应 JS attack）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 执行枪兵攻击：创建 360ms <see cref="PikeAttackEffect"/> 并登记到管理器。
        /// </summary>
        /// <remarks>
        /// <para><b>对应 JS <c>attack()</c>：</b>
        /// 创建 PikeAttackEffect → Launch → Add 到管理器。</para>
        ///
        /// <para><b>不查询目标（与刀兵/弓兵不同）：</b>
        /// 枪兵 <c>attack()</c> 直接创建效果，<see cref="PikeAttackEffect"/> 在 360ms 命中时
        /// 自行查询半径内目标（对应 JS <c>hit()</c> 经 <c>queryEnemyObjects</c>）。
        /// AttackScheduler 已保证调用前存在目标，此处不需要二次查询。</para>
        ///
        /// <para><b>伤害值：</b>使用 <see cref="SoldierBase.AttackDamage"/>。</para>
        ///
        /// <para><b>命中半径：</b>AttackRange（对应 JS <c>radius:this.attackRange</c>，
        /// SpearSoldier.js:54）。</para>
        ///
        /// <para><b>效果创建：</b><see cref="PikeAttackEffect.Launch"/> 接收 owner/resolver/
        /// enemyManager/damage/cellWidth/cellHeight/radius。效果经
        /// <see cref="AttackEffectManager.Add"/> 登记后，由 Manager 唯一推进，
        /// 360ms 后命中半径内敌人，480ms 完成回收。</para>
        /// </remarks>
        protected internal override void PerformAttack()
        {
            // 创建枪兵攻击效果并启动（对应 JS attack → PikeAttackEffect.launch）。
            // radius=AttackRange 对应 JS radius:this.attackRange（SpearSoldier.js:54）。
            var effect = new PikeAttackEffect();
            effect.Launch(
                this,
                AttackResolver,
                EnemyManager,
                AttackDamage,
                CellSize,
                CellSize,
                radius: AttackRange);

            // 登记到攻击效果管理器（对应 JS attackEffectManager.add(effect)）。
            AttackEffectManager.Add(effect);
        }
    }
}
