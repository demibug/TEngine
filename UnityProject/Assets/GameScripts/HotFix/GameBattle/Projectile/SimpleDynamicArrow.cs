namespace GameBattle
{
    // ============================================================================
    // 任务 5.7：SimpleDynamicArrow —— 本期唯一 COMPLETE 箭矢类型
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 196 行 / Projectile/SimpleDynamicArrow.cs）：
    //   本期唯一 COMPLETE 箭矢类型，组合 TargetEnemyBezierMovement 和 HitEnemyStrategy。
    //
    // 来源证据（还原工程 SimpleDynamicArrow.js:1-85）：
    //   - 原始符号 rd，重建状态 COMPLETE
    //   - 继承 ProjectileBase
    //   - onReset(config)：设置 alternateHitEffect（表现层标记，C# 规则层不持有）
    //   - applyHit(enemy)：enemy.hit(damage, attacker) + applyImpactEffects(enemy)
    //   - onRecover()：重置表现参数（C# 规则层只需清逻辑字段）
    //   - BowSoldier.launchArrow 创建时注入：
    //       hitStrategy = HitEnemyStrategy.create({ targetId })
    //       movement = TargetEnemyBezierMovement.create({ enemyManager, gameData, curveHeight: 120, ... })
    //       config = { type, appearance, damage, speedScale: 1.75, hitStrategy, movement }
    //
    // 决策依据：
    //   - design.md 第 9 行：纯逻辑，不持有 Unity GameObject/Image。
    //   - design.md 决策 5：删除 Laya/表现依赖。
    //   - spec battle-simulation：伤害在发生点同步生效。
    //   - task 5.7 约束：组合 TargetEnemyBezierMovement 和 HitEnemyStrategy 具体类。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Image/Sprite/RenderNode 等表现组件。
    //   2. 组合具体移动与命中策略，不创建接口。
    //   3. ResetState 后移动/命中策略引用清除。
    // ============================================================================

    /// <summary>
    /// 本期唯一 COMPLETE 箭矢类型，组合追踪目标贝塞尔移动与单体命中策略。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 196 行）：</b>本期唯一 COMPLETE 箭矢类型，
    /// 组合 <see cref="TargetEnemyBezierMovement"/> 和 <see cref="HitEnemyStrategy"/>。
    /// 替代还原工程 <c>SimpleDynamicArrow.js</c>（原始符号 rd，重建状态 COMPLETE）。</para>
    ///
    /// <para><b>纯逻辑（design.md 第 9 行）：</b>
    /// 还原工程 SimpleDynamicArrow 持有 <c>this.imageNode</c>（Laya Image）和
    /// <c>this.renderNode</c>（Laya Sprite）。C# 移植删除全部表现依赖，
    /// 只保留规则层的伤害提交逻辑。</para>
    ///
    /// <para><b>组合具体策略（task 5.7 约束）：</b>
    /// 不创建 IProjectileMovement/IProjectileHitStrategy 接口。移动和命中策略以具体类
    /// 组合注入，由 ProjectileFactory 在 Acquire 时创建并绑定。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 ProjectileFactory/ProjectileManager
    /// 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class SimpleDynamicArrow : ProjectileBase
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>投射物类型键（对应 SimpleDynamicArrow.projectileTypeKey）。</summary>
        internal const string ProjectileTypeKey = "SimpleDynamicArrow";

        /// <summary>默认投射物逻辑高度（对应 SimpleDynamicArrow.js:23 renderNode.size(22, 72)）。</summary>
        /// <remarks>用于 TargetEnemyBezierMovement 的命中半径计算（height / 1.5）。</remarks>
        internal const float DefaultHeight = 72f;

        // ====================================================================
        // 可变状态字段
        // ====================================================================

        /// <summary>绑定的移动策略（由 ProjectileFactory.Acquire 注入）。</summary>
        private TargetEnemyBezierMovement _movement;

        /// <summary>绑定的命中策略（由 ProjectileFactory.Acquire 注入）。</summary>
        private HitEnemyStrategy _hitStrategy;

        // ====================================================================
        // 只读属性
        // ====================================================================

        /// <summary>绑定的移动策略（供 ProjectileManager 推进）。</summary>
        internal TargetEnemyBezierMovement Movement => _movement;

        /// <summary>绑定的命中策略（供 ProjectileManager 触发命中）。</summary>
        internal HitEnemyStrategy HitStrategy => _hitStrategy;

        /// <summary>
        /// 目标敌人运行时 ID（task 8.1 轨迹采集）。
        /// <para>从 <see cref="Movement"/>.<see cref="TargetEnemyBezierMovement.TargetId"/> 读取真实值。
        /// 目标未设置或已失效时返回 -1，供黄金轨迹对照工具判断
        /// "Arrow target dies during flight" 场景（spec battle-parity-verification）。</para>
        /// <para>移动策略未绑定（池回收后）时返回基类默认 -1。</para>
        /// </summary>
        internal override int TargetId
        {
            get
            {
                int id = _movement != null ? _movement.TargetId : InvalidId;
                return id > 0 ? id : InvalidId;
            }
        }

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造一个 SimpleDynamicArrow。字段初始化为默认值。
        /// </summary>
        /// <remarks>
        /// 移动和命中策略由 <see cref="ProjectileFactory.Acquire"/> 创建并经
        /// <see cref="BindStrategies"/> 绑定。
        /// </remarks>
        internal SimpleDynamicArrow()
        {
            _movement = null;
            _hitStrategy = null;
        }

        // ====================================================================
        // BindStrategies —— 绑定移动与命中策略
        // ====================================================================

        /// <summary>
        /// 绑定移动与命中策略。由 ProjectileFactory.Acquire 在分配 ID 后调用。
        /// </summary>
        /// <param name="movement">移动策略（不可为 null）。</param>
        /// <param name="hitStrategy">命中策略（不可为 null）。</param>
        internal void BindStrategies(
            TargetEnemyBezierMovement movement,
            HitEnemyStrategy hitStrategy)
        {
            _movement = movement ?? throw new System.ArgumentNullException(nameof(movement));
            _hitStrategy = hitStrategy ?? throw new System.ArgumentNullException(nameof(hitStrategy));
        }

        // ====================================================================
        // OnReset —— 重置扩展（对应 SimpleDynamicArrow.js:43-45 onReset）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>对应还原工程 <c>onReset(config)</c>（SimpleDynamicArrow.js:43-45）：
        /// 设置 alternateHitEffect（表现层标记）。C# 规则层不持有表现标记，此方法为空。</para>
        /// </remarks>
        protected override void OnReset() { }

        // ====================================================================
        // OnFire —— 发射扩展（对应 movement.onFire）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>触发移动策略的 <c>OnFire</c>，初始化贝塞尔曲线参数。</para>
        /// </remarks>
        protected override void OnFire()
        {
            _movement?.OnFire();
        }

        // ====================================================================
        // OnUpdate —— 帧更新（对应 movement.update + projectile.update）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>调用移动策略 <see cref="TargetEnemyBezierMovement.Update"/> 推进贝塞尔位移。
        /// 对应还原工程 <c>ProjectileManager.update</c> 中
        /// <c>projectile.movement.update(deltaMs, projectile.speedScale)</c>
        /// （ProjectileManager.js:80）。</para>
        /// </remarks>
        protected override void OnUpdate(long stepMs)
        {
            _movement?.Update(stepMs, SpeedScale);
        }

        // ====================================================================
        // ApplyHit —— 执行命中伤害（对应 SimpleDynamicArrow.js:47-62 applyHit）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>对应还原工程 <c>applyHit(enemy)</c>（SimpleDynamicArrow.js:47-62）：
        /// 调用 <c>enemy.hit(damage, attacker)</c> 提交伤害。C# 移植使用
        /// <see cref="IEnemyEntity.Hit"/>，通过 <see cref="EnemyManager"/> 提交。</para>
        /// <para>还原工程还调用 <c>applyImpactEffects</c>（燃烧/击退/范围/弹射），
        /// 本期 SimpleDynamicArrow 不移植 impact 效果（design.md 本期范围裁剪）。</para>
        /// </remarks>
        protected override bool ApplyHit(IEnemyEntity enemy)
        {
            return enemy.Hit(Damage, AttackerId);
        }

        // ====================================================================
        // OnRecover —— 回收扩展（对应 SimpleDynamicArrow.js:64-74 onRecover）
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>对应还原工程 <c>onRecover()</c>（SimpleDynamicArrow.js:64-74）：
        /// 重置表现参数。C# 规则层清除策略引用。</para>
        /// </remarks>
        protected override void OnRecover()
        {
            // 回收移动与命中策略（清除内部状态与引用）。
            _movement?.Recover();
            _hitStrategy?.Recover();
        }

        // ====================================================================
        // IPoolableBattleObject.ResetState —— 池回收前重置
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para>清除全部可变状态，包括移动与命中策略引用。
        /// 回收后对象等价于新构造，保证池复用无污染。</para>
        /// <para>策略对象的生命周期由 ProjectileFactory 管理（策略对象本身也可池化），
        /// 此处只清除投射物对策略的引用。</para>
        /// </remarks>
        public override void ResetState()
        {
            base.ResetState();
            _movement = null;
            _hitStrategy = null;
        }
    }
}
