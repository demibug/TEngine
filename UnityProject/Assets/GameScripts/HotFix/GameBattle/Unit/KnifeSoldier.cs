namespace GameBattle
{
    // ============================================================================
    // 任务 6.2：KnifeSoldier —— 刀兵，创建 500ms 逻辑命中的刀兵攻击效果
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 180 行 / Unit/KnifeSoldier.cs）：
    //   创建 500ms 逻辑命中的刀兵攻击效果。
    //
    // 来源证据（还原工程 KnifeSoldier.js，经 5.4 KnifeAttackEffect.cs 注释引用）：
    //   KnifeSoldier 继承 SoldierBase，核心行为：
    //     - attack()：调用 performKnifeAttack()
    //     - performKnifeAttack()：选择目标 → 创建 KnifeAttackEffect →
    //       attackEffectManager.add(effect)
    //   KnifeAttackTimeline.start（已合入 KnifeAttackEffect，task 5.5）：
    //     delayMs = KNIFE_HIT_DELAY_BASE_MS(500) / playbackRate
    //     创建 record → launch effect → add to manager
    //   刀兵为单目标延迟命中：500ms 后对选定目标命中一次。
    //
    // 决策依据：
    //   - design.md 第 180 行：创建 500ms 逻辑命中的刀兵攻击效果。
    //   - design.md 第 9 行：规则层不持有 Unity GameObject 或表现组件。
    //   - design.md 决策 5：删除 SingletonBase/CombatServices，强类型注入。
    //   - task 6.2 约束：override PerformAttack 连接 KnifeAttackEffect；
    //     不引入武将、技能、升级或 AI 行为。
    //   - task 5.4 已实现 KnifeAttackEffect（500ms 单目标延迟命中），
    //     本任务只负责创建并登记效果。
    //
    // 与攻击效果的连接：
    //   PerformAttack 中：
    //     1. 经 AttackResolver.QueryTargets 查询目标（对应 JS performKnifeAttack 的目标选择）。
    //     2. 取第一个目标（对应 JS targets[0]）。
    //     3. 创建 KnifeAttackEffect（new，池化由 task 6.3 UnitFactory 统一管理）。
    //     4. Launch(owner, resolver, enemyManager, targetId, damage)。
    //     5. 经 AttackEffectManager.Add 登记效果。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/表现组件。
    //   2. 池化友好：ResetState 继承 SoldierBase（无专属字段需扩展）。
    //   3. 单目标：KnifeAttackEffect 针对单个目标 ID。
    //   4. 不引入武将/技能/升级/AI。
    // ============================================================================

    /// <summary>
    /// 刀兵：创建 500ms 逻辑命中的刀兵攻击效果（task 6.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 180 行）：</b>创建 500ms 逻辑命中的刀兵攻击效果。
    /// 替代还原工程 <c>KnifeSoldier.js</c>。</para>
    ///
    /// <para><b>攻击流程（对应 JS <c>attack() → performKnifeAttack()</c>）：</b>
    /// <see cref="PerformAttack"/> 中查询目标，取第一个目标，创建 <see cref="KnifeAttackEffect"/>
    /// 并经 <see cref="AttackEffectManager.Add"/> 登记效果。效果在 500ms 后对目标命中一次
    /// （task 5.4 已实现时序）。</para>
    ///
    /// <para><b>纯逻辑（design.md 第 9 行）：</b>不持有 Unity GameObject 或表现组件。</para>
    ///
    /// <para><b>池化（task 4.1）：</b>继承 <see cref="SoldierBase"/> 的 ResetState 契约，
    /// 无专属字段需扩展。池化由 UnitFactory（task 6.3）统一管理。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部 UnitFactory（task 6.3）
    /// 创建使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class KnifeSoldier : SoldierBase
    {
        // ====================================================================
        // Configure —— 注入运行时依赖（继承 SoldierBase，无额外依赖）
        // ====================================================================

        /// <summary>
        /// 配置刀兵运行时依赖。委托 <see cref="SoldierBase.Configure"/> 注入通用依赖。
        /// </summary>
        /// <remarks>
        /// 刀兵无投射物管理器等额外依赖，直接委托基类。4 兵种 Configure 覆写统一签名
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
        // PerformAttack —— 创建刀兵攻击效果（对应 JS performKnifeAttack）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 执行刀兵攻击：用调度器传入的初始目标创建 500ms <see cref="KnifeAttackEffect"/> 并登记到管理器。
        /// </summary>
        /// <param name="initialTarget">调度器为本次攻击选定的唯一初始目标。</param>
        /// <remarks>
        /// <para><b>对应 JS <c>performKnifeAttack()</c>：</b>
        /// 使用初始目标 → 创建 KnifeAttackEffect → Launch → Add 到管理器。</para>
        ///
        /// <para><b>目标选择（design 决策 2）：</b>初始目标由 AttackScheduler 单次选择并传入，
        /// 兵种不再独立二次查询。目标在命中点失效时由 <see cref="KnifeAttackEffect"/> 经
        /// <see cref="AttackResolver"/> 执行有限稳定重选（design 决策 3/4）。</para>
        ///
        /// <para><b>伤害值：</b>使用 <see cref="SoldierBase.AttackDamage"/>（baseAttackPower +
        /// addAttackPower，本期 addAttackPower=0）。</para>
        ///
        /// <para><b>效果创建：</b><see cref="KnifeAttackEffect.Launch"/> 接收 owner/resolver/
        /// enemyManager/targetId/damage/attackRange/cellSize。攻击范围与格子尺寸供命中点
        /// 有限重选查询使用。效果经 <see cref="AttackEffectManager.Add"/> 登记后，
        /// 由 Manager 在 AttackEffect 阶段唯一推进，500ms 后命中目标。</para>
        /// </remarks>
        protected internal override void PerformAttack(EnemyTargetDto initialTarget)
        {
            // 使用调度器为本次攻击选定的初始目标（design 决策 2：一次攻击只选择一次初始目标）。
            // 不再在 PerformAttack 开头重复 QueryTargets——初始目标由 AttackScheduler 单次选择并传入。
            int targetId = initialTarget.Id;

            // 创建刀兵攻击效果并启动（对应 JS KnifeAttackTimeline.start → effect.launch）。
            // 池化由 task 6.3 UnitFactory 统一管理；当前 new 创建，回收委托 null。
            var effect = new KnifeAttackEffect();
            effect.Launch(
                this,
                AttackResolver,
                EnemyManager,
                targetId,
                AttackDamage,
                AttackRange,
                CellSize);

            // 登记到攻击效果管理器（对应 JS attackEffectManager.add(effect)）。
            AttackEffectManager.Add(effect);
        }
    }
}
