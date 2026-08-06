namespace GameBattle
{
    // ============================================================================
    // 任务 6.2：CavalrySoldier —— 骑兵，创建 150ms 两段横扫效果
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 183 行 / Unit/CavalrySoldier.cs）：
    //   创建 150ms 两段横扫效果。
    //
    // 来源证据（还原工程 CavalrySoldier.js:41-58，经 5.4 CavalrySweepEffect.cs 注释引用）：
    //   CavalrySoldier 继承 SoldierBase，核心行为：
    //     - attack()：创建两个 CavalrySweepEffect 实例
    //       实例1: multiplier=0.5, radius=attackRange/2, delayMs=150
    //       实例2: multiplier=0.5, radius=attackRange,   delayMs=150
    //     两个实例均由 AttackEffectManager 推进，各自在 150ms 时命中（范围查询+去重），
    //     在 270ms 时完成。每个实例独立 hitSet，可命中同一敌人（两段伤害）。
    //     总伤害 = damage*0.5 + damage*0.5 = damage（分两段）。
    //
    // 决策依据：
    //   - design.md 第 183 行：创建 150ms 两段横扫效果。
    //   - design.md 第 9 行：规则层不持有 Unity GameObject 或表现组件。
    //   - task 6.2 约束：override PerformAttack 连接两个 CavalrySweepEffect；
    //     不引入武将、技能、升级或 AI 行为。
    //   - task 5.4 已实现 CavalrySweepEffect（150ms 单段范围命中，270ms 完成）。
    //   - "两段"由单位层创建两个效果实例实现（匹配 JS CavalrySoldier.attack），
    //     本类型效果层为单段 150ms 命中。
    //
    // 与攻击效果的连接：
    //   PerformAttack 中：
    //     1. 创建第一个 CavalrySweepEffect：multiplier=0.5, radius=AttackRange/2。
    //     2. 创建第二个 CavalrySweepEffect：multiplier=0.5, radius=AttackRange。
    //     3. 两个效果各自 Launch 并经 AttackEffectManager.Add 登记。
    //   骑兵不查询目标——CavalrySweepEffect 在命中时自行查询半径内目标。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/表现组件。
    //   2. 池化友好：ResetState 继承 SoldierBase（无专属字段需扩展）。
    //   3. 两段伤害：两个独立效果实例，各自 150ms 命中，独立 hitSet。
    //   4. 不引入武将/技能/升级/AI。
    // ============================================================================

    /// <summary>
    /// 骑兵：创建 150ms 两段横扫效果（task 6.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 183 行）：</b>创建 150ms 两段横扫效果。
    /// 替代还原工程 <c>CavalrySoldier.js</c>。</para>
    ///
    /// <para><b>攻击流程（对应 JS <c>attack()</c> CavalrySoldier.js:41-58）：</b>
    /// <see cref="PerformAttack"/> 中创建两个 <see cref="CavalrySweepEffect"/> 实例：
    /// 实例1 <c>multiplier=0.5, radius=AttackRange/2</c>；
    /// 实例2 <c>multiplier=0.5, radius=AttackRange</c>。
    /// 两个实例各自 Launch 并经 <see cref="AttackEffectManager.Add"/> 登记。
    /// 各自在 150ms 时命中（范围查询+去重），270ms 完成。独立 hitSet 可命中同一敌人，
    /// 合计两段伤害 = damage*0.5 + damage*0.5 = damage。</para>
    ///
    /// <para><b>"双段"语义（对应 JS CavalrySoldier.attack）：</b>
    /// 骑兵单位创建两个效果实例实现双段（匹配 JS）。本类型效果层（CavalrySweepEffect）
    /// 为单段 150ms 命中，"双段"由单位层创建两实例实现。</para>
    ///
    /// <para><b>范围命中（与刀兵单目标不同）：</b>
    /// 骑兵不查询目标，<see cref="CavalrySweepEffect"/> 在命中时自行查询半径内目标。</para>
    ///
    /// <para><b>纯逻辑（design.md 第 9 行）：</b>不持有 Unity GameObject 或表现组件。</para>
    ///
    /// <para><b>池化（task 4.1）：</b>继承 <see cref="SoldierBase"/> 的 ResetState 契约，
    /// 无专属字段需扩展。池化由 UnitFactory（task 6.3）统一管理。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部 UnitFactory（task 6.3）
    /// 创建使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class CavalrySoldier : SoldierBase
    {
        // ====================================================================
        // 常量（对应 JS CavalrySoldier 双段参数）
        // ====================================================================

        /// <summary>骑兵双段伤害倍率（对应 JS <c>multiplier=0.5</c>）。每段为总伤害的一半。</summary>
        private const float SweepMultiplier = 0.5f;

        // ====================================================================
        // Configure —— 注入运行时依赖（继承 SoldierBase，无额外依赖）
        // ====================================================================

        /// <summary>
        /// 配置骑兵运行时依赖。委托 <see cref="SoldierBase.Configure"/> 注入通用依赖。
        /// </summary>
        /// <remarks>
        /// 骑兵无投射物管理器等额外依赖，直接委托基类。4 兵种 Configure 覆写统一签名
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
        // PerformAttack —— 创建两段横扫效果（对应 JS CavalrySoldier.attack:41-58）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 执行骑兵攻击：创建两个 <see cref="CavalrySweepEffect"/>（不同半径），各自登记到管理器。
        /// </summary>
        /// <remarks>
        /// <para><b>对应 JS <c>CavalrySoldier.attack()</c>（CavalrySoldier.js:41-58）：</b>
        /// 创建两个 CavalrySweepEffect 实例，multiplier 均为 0.5，半径分别为
        /// <c>attackRange/2</c> 与 <c>attackRange</c>。两实例各自 Launch + Add。</para>
        ///
        /// <para><b>不查询目标（与刀兵/弓兵不同）：</b>
        /// 骑兵 <c>attack()</c> 直接创建效果，<see cref="CavalrySweepEffect"/> 在 150ms 命中时
        /// 自行查询半径内目标。</para>
        ///
        /// <para><b>伤害值：</b>使用 <see cref="SoldierBase.AttackDamage"/> 作为基础伤害，
        /// 每段乘以 <see cref="SweepMultiplier"/>(0.5)。两段合计 = AttackDamage。</para>
        ///
        /// <para><b>半径（对应 JS）：</b>
        /// 实例1 radius = AttackRange * 0.5（近段横扫）；
        /// 实例2 radius = AttackRange（远段横扫）。</para>
        ///
        /// <para><b>效果创建：</b>两个 <see cref="CavalrySweepEffect.Launch"/> 各自接收
        /// owner/resolver/enemyManager/damage/cellWidth/cellHeight/multiplier/radius。
        /// 效果经 <see cref="AttackEffectManager.Add"/> 登记后，由 Manager 唯一推进，
        /// 150ms 后命中半径内敌人，270ms 完成回收。独立 hitSet 可命中同一敌人。</para>
        /// </remarks>
        protected internal override void PerformAttack()
        {
            // --- 实例1：近段横扫，radius=AttackRange/2 ---
            var sweep1 = new CavalrySweepEffect();
            sweep1.Launch(
                this,
                AttackResolver,
                EnemyManager,
                AttackDamage,
                CellSize,
                CellSize,
                multiplier: SweepMultiplier,
                radius: AttackRange * 0.5f);
            AttackEffectManager.Add(sweep1);

            // --- 实例2：远段横扫，radius=AttackRange ---
            var sweep2 = new CavalrySweepEffect();
            sweep2.Launch(
                this,
                AttackResolver,
                EnemyManager,
                AttackDamage,
                CellSize,
                CellSize,
                multiplier: SweepMultiplier,
                radius: AttackRange);
            AttackEffectManager.Add(sweep2);
        }
    }
}
