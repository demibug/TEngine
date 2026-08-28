using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 6.2：BowSoldier —— 弓兵，选择目标并创建 SimpleDynamicArrow 攻击链
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 181 行 / Unit/BowSoldier.cs）：
    //   选择目标并创建 SimpleDynamicArrow 攻击链。
    //
    // 来源证据（还原工程 BowSoldier.js，经 5.7/5.8 注释引用）：
    //   BowSoldier 继承 SoldierBase，核心行为：
    //     - attack()：调用 launchArrow()
    //     - launchArrow()：selectTarget → 创建箭矢 → fire → 创建 ProjectileAttackEffect
    //   BowSoldier.launchArrow（经 SimpleDynamicArrow.cs 注释引用）：
    //     hitStrategy = HitEnemyStrategy.create({ targetId })
    //     movement = TargetEnemyBezierMovement.create({ enemyManager, curveHeight: 120, ... })
    //     config = { type, damage, speedScale: 1.75, hitStrategy, movement }
    //     projectile = projectileManager.create(config, startPoint)
    //     projectile.fire()
    //     attackEffectManager.add(new ProjectileAttackEffect().launch({owner, projectileManager, projectile}))
    //   speedScale=1.75, curveHeight=120（对应 BowSoldier 默认值）
    //
    // 决策依据：
    //   - design.md 第 181 行：选择目标并创建 SimpleDynamicArrow 攻击链。
    //   - design.md 第 9 行：规则层不持有 Unity GameObject 或表现组件。
    //   - task 6.2 约束：override PerformAttack 连接 ProjectileAttackEffect/ProjectileFactory；
    //     不引入武将、技能、升级或 AI 行为。
    //   - task 5.7 已实现 ProjectileFactory.Acquire（创建 SimpleDynamicArrow + 绑定策略）。
    //   - task 5.8 已实现 ProjectileAttackEffect（桥接到 ProjectileManager）。
    //   - spec battle-simulation "Projectile is launched after projectile phase"：
    //     弓兵在 AttackRelease/AttackEffect 阶段创建箭矢，箭矢下一子步才移动。
    //
    // 与攻击效果的连接：
    //   PerformAttack 中：
    //     1. 经 AttackResolver.QueryTargets 查询目标（对应 JS selectTarget）。
    //     2. 取第一个目标（对应 JS targets[0]）。
    //     3. 经 ProjectileFactory.Acquire 创建 SimpleDynamicArrow（含移动/命中策略）。
    //     4. arrow.Fire(centerX, centerY)（对应 JS projectile.fire()）。
    //     5. 创建 ProjectileAttackEffect，Launch(owner, projectileManager, arrow)。
    //     6. 经 AttackEffectManager.Add 登记效果。
    //
    // 不变量：
    //   1. 纯逻辑：不持有 Unity GameObject/MonoBehaviour/表现组件。
    //   2. 池化友好：ResetState 覆写扩展清理 ProjectileFactory/ProjectileManager 引用。
    //   3. 单目标：箭矢追踪单个目标 ID。
    //   4. 不引入武将/技能/升级/AI。
    // ============================================================================

    /// <summary>
    /// 弓兵：选择目标并创建 SimpleDynamicArrow 攻击链（task 6.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 第 181 行）：</b>选择目标并创建 SimpleDynamicArrow 攻击链。
    /// 替代还原工程 <c>BowSoldier.js</c>。</para>
    ///
    /// <para><b>攻击流程（对应 JS <c>attack() → launchArrow()</c>）：</b>
    /// <see cref="PerformAttack"/> 中查询目标，取第一个目标，经 <see cref="ProjectileFactory.Acquire"/>
    /// 创建 <see cref="SimpleDynamicArrow"/>，<see cref="ProjectileBase.Fire"/> 发射，
    /// 创建 <see cref="ProjectileAttackEffect"/> 桥接到 <see cref="ProjectileManager"/>，
    /// 经 <see cref="AttackEffectManager.Add"/> 登记。</para>
    ///
    /// <para><b>纯逻辑（design.md 第 9 行）：</b>不持有 Unity GameObject 或表现组件。</para>
    ///
    /// <para><b>额外依赖（task 6.2 兵种扩展）：</b>弓兵需要 <see cref="ProjectileFactory"/>
    /// 与 <see cref="ProjectileManager"/>，在 <see cref="Configure"/> 中扩展注入。
    /// ResetState 覆写扩展清理这两个引用。</para>
    ///
    /// <para><b>本类型为 internal sealed：</b>只供 GameBattle 内部 UnitFactory（task 6.3）
    /// 创建使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class BowSoldier : SoldierBase
    {
        /// <summary>当前弓兵远程攻击硬编码预设；后续由单位配置映射提供。</summary>
        private static readonly RangedAttackParameters AttackParameters = RangedAttackPresets.Bow;

        private float _arrowSpeedScale = AttackParameters.DefaultProjectileSpeedScale;

        internal float ArrowSpeedScaleForDiagnostics => _arrowSpeedScale;

        // ====================================================================
        // 弓兵专属依赖
        // ====================================================================

        /// <summary>投射物工厂，供 Acquire 创建 SimpleDynamicArrow。回收后置 null。</summary>
        private ProjectileFactory _projectileFactory;

        /// <summary>投射物管理器，供 ProjectileAttackEffect 登记箭矢。回收后置 null。</summary>
        private ProjectileManager _projectileManager;

        // ====================================================================
        // Configure —— 注入运行时依赖（扩展 SoldierBase，增加投射物依赖）
        // ====================================================================

        /// <summary>
        /// 配置弓兵运行时依赖。委托 <see cref="SoldierBase.Configure"/> 注入通用依赖，
        /// 额外注入投射物工厂与管理器。
        /// </summary>
        /// <param name="projectileFactory">投射物工厂（非 null），供创建 SimpleDynamicArrow。</param>
        /// <param name="projectileManager">投射物管理器（非 null），供登记箭矢。</param>
        /// <exception cref="ArgumentNullException">投射物工厂或管理器为 null。</exception>
        /// <remarks>
        /// 弓兵是唯一需要投射物依赖的兵种。对应 JS BowSoldier.configure 注入 projectileManager。
        /// 4 兵种 Configure 覆写统一签名供 UnitFactory（task 6.3）按兵种调用。
        /// </remarks>
        internal void Configure(
            EnemyManager enemyManager,
            AttackResolver attackResolver,
            AttackEffectManager attackEffectManager,
            ProjectileFactory projectileFactory,
            ProjectileManager projectileManager,
            float cellSize,
            int opponentAttackMultiplier)
        {
            if (projectileFactory == null)
            {
                throw new ArgumentNullException(nameof(projectileFactory));
            }
            if (projectileManager == null)
            {
                throw new ArgumentNullException(nameof(projectileManager));
            }

            base.Configure(enemyManager, attackResolver, attackEffectManager,
                cellSize, opponentAttackMultiplier);

            _projectileFactory = projectileFactory;
            _projectileManager = projectileManager;
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
        internal void InitStats(UnitConfigSnapshot config)
        {
            InitializeStats(config);
            // 配置表使用整数速度（如 200）；既有投射物移动器消费 scale，适配为 100=1.0。
            _arrowSpeedScale = config != null && config.ProjectileSpeed > 0
                ? config.ProjectileSpeed / 100f
                : AttackParameters.DefaultProjectileSpeedScale;
        }

        // ====================================================================
        // PerformAttack —— 创建箭矢攻击链（对应 JS launchArrow）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 执行弓兵攻击：用调度器传入的初始目标建立前摇朝向，登记延迟释放效果。
        /// </summary>
        /// <param name="initialTarget">调度器为本次攻击选定的唯一初始目标。</param>
        /// <remarks>
        /// <para><b>对应 JS <c>launchArrow()</c> 前摇部分：</b>
        /// 使用初始目标计算前摇朝向 → 创建 BowReleaseEffect → Add 到管理器。
        /// 箭矢不在此时创建，由 BowReleaseEffect 在释放点解析最终目标后调用
        /// <see cref="LaunchArrow"/> 实际发射。</para>
        ///
        /// <para><b>目标选择（design 决策 2）：</b>初始目标由 AttackScheduler 单次选择并传入，
        /// 兵种不再独立二次查询。释放点目标失效时由 <see cref="BowReleaseEffect"/> 经
        /// <see cref="AttackResolver"/> 执行有限稳定重选（design 决策 4）。</para>
        ///
        /// <para><b>本轮锁定朝向：</b>按初始目标位置计算角色本体旋转角（仅表现状态）。
        /// 释放点即使在小角度锥形内更换目标也不再旋转人物，避免完整 2D 序列帧中途跳向。</para>
        ///
        /// <para><b>新箭下一子步才移动（spec "Projectile is launched after projectile phase"）：</b>
        /// 箭矢在释放点创建后，下一子步才首次移动。</para>
        /// </remarks>
        protected internal override void PerformAttack(EnemyTargetDto initialTarget)
        {
            // 使用调度器为本次攻击选定的初始目标（design 决策 2：一次攻击只选择一次初始目标）。
            // 不再在 PerformAttack 开头重复 QueryTargets——初始目标由 AttackScheduler 单次选择并传入。
            // 原工程以箭矢初始切线角旋转整个人物，不能使用 XScale 镜像。
            // 目标取矩形中心，与原 BowSoldier._targetCenter 的 gridWidth/gridHeight/2 语义一致。
            float lockedAimAngleDegrees = CalculateLaunchAngleDegrees(
                CenterX,
                CenterY,
                initialTarget,
                CellSize,
                AttackParameters.ProjectileCurveHeight);
            SetBodyRotation(lockedAimAngleDegrees);

            // 登记延迟释放效果：攻击动画到第 17 帧开始时触发射箭。
            // 箭矢不在此刻创建，由 BowReleaseEffect 在释放延迟后调用 LaunchArrow 实际发射。
            // 释放点会验证首选目标并在锁定方向锥形内稳定回退，但不会改变本轮人物朝向。
            var releaseEffect = new BowReleaseEffect();
            releaseEffect.Launch(
                this,
                AttackResolver,
                EnemyManager,
                initialTarget,
                CenterX,
                CenterY,
                AttackRange,
                CellSize,
                AttackIntervalSeconds,
                AttackParameters);

            // 登记到攻击效果管理器（对应 JS attackEffectManager.add(effect)）。
            AttackEffectManager.Add(releaseEffect);
        }

        /// <summary>
        /// 创建并发射箭矢（对应 JS <c>launchArrow</c>）。
        /// </summary>
        /// <param name="finalTarget">释放点首选/回退解析后的最终目标 DTO。</param>
        /// <param name="centerX">发射起点逻辑 X（本单位中心）。</param>
        /// <param name="centerY">发射起点逻辑 Y（本单位中心）。</param>
        /// <param name="parameters">本次远程攻击参数快照。</param>
        /// <remarks>
        /// <para>由 <see cref="BowReleaseEffect"/> 在释放延迟到达且目标解析成功后调用
        /// （对齐原工程 STOPPED 事件 → launchArrow）。本方法根据远程攻击参数决定是否在
        /// 释放点刷新人物朝向，然后创建箭矢；当前弓兵预设使用整轮锁定朝向。</para>
        /// <para>箭矢一旦创建，其目标 ID 固定，飞行中目标死亡不重定向（spec
        /// "已释放普通投射物不得重定向"）。</para>
        /// </remarks>
        internal void LaunchArrow(
            EnemyTargetDto finalTarget,
            float centerX,
            float centerY,
            RangedAttackParameters parameters)
        {
            int targetId = finalTarget.Id;

            if (parameters.VisualAimPolicy == RangedVisualAimPolicy.UpdateAtRelease)
            {
                float finalAimAngleDegrees = CalculateLaunchAngleDegrees(
                    centerX,
                    centerY,
                    finalTarget,
                    CellSize,
                    parameters.ProjectileCurveHeight);
                SetBodyRotation(finalAimAngleDegrees);
            }

            // 创建箭矢（对应 JS projectileManager.create → ProjectileFactory.Acquire）。
            // 弧高与默认速度来自当前远程攻击参数预设，速度可继续被单位配置覆盖。
            SimpleDynamicArrow arrow = _projectileFactory.Acquire(
                targetId,
                Id,
                attackerDamage: AttackDamage,
                explicitDamage: true,
                damage: AttackDamage,
                speedScale: _arrowSpeedScale,
                curveHeight: parameters.ProjectileCurveHeight);

            // 从本单位逻辑中心发射箭矢（对应 JS projectile.fire(startX, startY)）。
            arrow.Fire(centerX, centerY);

            // 创建投射物攻击效果并桥接到 ProjectileManager（对应 JS ProjectileAttackEffect.launch）。
            var effect = new ProjectileAttackEffect();
            effect.Launch(this, _projectileManager, arrow);

            // 登记到攻击效果管理器（对应 JS attackEffectManager.add(effect)）。
            AttackEffectManager.Add(effect);
        }

        /// <summary>
        /// 按现有二次贝塞尔初始切线计算一轮攻击的锁定表现角度。
        /// </summary>
        internal static float CalculateLaunchAngleDegrees(
            float startX,
            float startY,
            EnemyTargetDto target,
            float cellSize,
            float projectileCurveHeight)
        {
            float targetCenterX = target.X + cellSize / 2f;
            float targetCenterY = target.Y + cellSize / 2f;
            float controlX = startX + (targetCenterX - startX) / 2f;
            float controlY = startY + (targetCenterY - startY) / 2f - projectileCurveHeight;
            float angleDegrees = (float)(ProjectileMath.QuadraticTangentDegrees(
                startX, startY, controlX, controlY, targetCenterX, targetCenterY, 0d) + 90d);
            return angleDegrees;
        }

        // ====================================================================
        // ResetState —— 扩展清理弓兵专属依赖（override）
        // ====================================================================

        /// <inheritdoc/>
        /// <summary>
        /// 重置对象到等价于新构造的状态。扩展清理弓兵专属的投射物工厂与管理器引用，
        /// 再调基类清理通用字段。
        /// </summary>
        /// <remarks>
        /// <para><b>弓兵专属清理：</b>_projectileFactory / _projectileManager 置 null，
        /// 保证池复用后不残留旧局投射物依赖引用。</para>
        /// <para><b>幂等性：</b>多次调用安全。</para>
        /// <para><b>不抛出：</b>遵循 IPoolableBattleObject 契约。</para>
        /// </remarks>
        public override void ResetState()
        {
            _projectileFactory = null;
            _projectileManager = null;
            _arrowSpeedScale = AttackParameters.DefaultProjectileSpeedScale;
            base.ResetState();
        }
    }
}
