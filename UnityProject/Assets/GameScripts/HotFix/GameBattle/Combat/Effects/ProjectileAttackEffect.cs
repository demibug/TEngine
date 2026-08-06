using System;

namespace GameBattle
{
    // ============================================================================
    // 任务 5.8：ProjectileAttackEffect —— 把弓兵攻击桥接到效果系统的适配器
    // ----------------------------------------------------------------------------
    // 职责（design.md 第 1 节目录表 / Combat/Effects/ProjectileAttackEffect.cs）：
    //   把弓兵攻击转换成投射物创建请求，不自行二次更新投射物。
    //
    // 来源证据（还原工程 ProjectileAttackEffect.js:1-53）：
    //   还原工程 ProjectileAttackEffect 是一个统一投射物创建/回收适配器；具体飞行和命中
    //   仍由 ProjectileManager 负责。核心数据：
    //     - type: 'projectile'
    //     - owner: 攻击者引用
    //     - projectileManager: 投射物管理器
    //     - projectile: 已创建的投射物
    //     - active: 是否活动
    //   核心方法：
    //     - launch({ owner, projectileManager, config, startPoint })：
    //         projectileManager.create({...config, attacker: owner}, startPoint)
    //         → projectile.fire() → active = Boolean(projectile)
    //     - adopt({ owner, projectileManager, projectile })：接管已有投射物
    //     - update()：if (!projectile.active) cleanup('projectile-complete')；返回 active
    //     - cleanup(reason)：若投射物仍活动，调 projectileManager.remove(projectile, reason)
    //                       → reset()
    //
    // 决策依据：
    //   - design.md 第 1 节目录表："把弓兵攻击转换成投射物创建请求，不自行二次更新投射物。"
    //   - design.md 第 193 行文件职责表："把弓兵攻击转换成投射物创建请求，不自行二次更新投射物。"
    //   - task 5.8 约束：ProjectileAttackEffect implements IAttackEffect；Update delegates
    //     to ProjectileBase.Advance。但 task 5.8 核心约束"投射物每子步只由 Manager 推进一次"
    //     与"Update delegates to Advance"存在冲突——若 Effect.Update 也调 Advance，则同一
    //     投射物每子步被推进两次（一次 ProjectileManager.Update，一次 AttackEffectManager.Update）。
    //     解决方案：Effect.Update 为空操作（只检查投射物是否完成以更新 Active 标记），
    //     推进的唯一入口是 ProjectileManager.Update。详见下方设计说明。
    //   - spec battle-simulation "Update phases are explicit and single-owned"：每个系统
    //     每子步最多更新一次。投射物推进的唯一拥有者是 ProjectileManager。
    //   - spec battle-simulation "Effect manager participates in a substep"：攻击效果每子步
    //     只累计一次 stepMs，不因嵌套 Manager 调用而双倍推进。
    //   - AttackEffectManager.IAttackEffect 契约（task 5.3）：Active / Owner / Update / Cancel。
    //
    // 唯一推进拥有者设计（task 5.8 核心约束的冲突解决）：
    //   task 5.8 描述"Update delegates to ProjectileBase.Advance"与"投射物每子步只由 Manager
    //   推进一次"表面冲突。实际语义是：ProjectileAttackEffect 作为 IAttackEffect 被
    //   AttackEffectManager 跟踪/取消，但其 Update 不重复推进投射物（否则双轨推进）。
    //   设计决策：
    //     1. ProjectileManager.Update 是投射物推进的唯一入口（调用 ProjectileBase.Advance）。
    //     2. ProjectileAttackEffect.Update 为空操作——只检查投射物是否已完成以更新 Active 标记，
    //        供 AttackEffectManager 判断是否移除本效果。
    //     3. ProjectileAttackEffect.Cancel 委托到 ProjectileManager.Remove，取消投射物。
    //   这满足"投射物每子步只由 Manager 推进一次"的核心约束，同时让效果系统能跟踪/取消投射物。
    //   对应还原工程 ProjectileAttackEffect.js:39-43 的 update()——它本身不调 movement.update，
    //   只检查 projectile.active 状态，与本项目设计一致。
    //
    // 不变量：
    //   1. 唯一推进：Effect.Update 不调 Advance，推进由 ProjectileManager.Update 唯一负责。
    //   2. 效果系统桥接：Effect 供 AttackEffectManager 跟踪/取消，Cancel 委托到 Manager.Remove。
    //   3. 池化友好：实现 IPoolableBattleObject，ResetState 后等价于新构造。
    //   4. 幂等 Cancel：重复调用安全。
    // ============================================================================

    /// <summary>
    /// 把弓兵攻击桥接到 <see cref="ProjectileManager"/> 的攻击效果适配器。
    /// </summary>
    /// <remarks>
    /// <para><b>职责（design.md 目录表 / 第 193 行）：</b>把弓兵攻击转换成投射物创建请求，
    /// 不自行二次更新投射物。替代还原工程 <c>ProjectileAttackEffect.js</c>
    /// （<c>ProjectileAttackEffect.js:1-53</c>）。</para>
    ///
    /// <para><b>效果系统桥接（task 5.8）：</b>
    /// 本类型实现 <see cref="IAttackEffect"/>，供 <see cref="AttackEffectManager"/> 跟踪与取消。
    /// 这样 AttackEffectManager 可以在 Settling 静默清理或所有者死亡时统一取消投射物，
    /// 而无需直接知道 <see cref="ProjectileManager"/> 的存在。</para>
    ///
    /// <para><b>唯一推进拥有者（task 5.8 核心约束的冲突解决）：</b>
    /// task 5.8 描述"Update delegates to ProjectileBase.Advance"与"投射物每子步只由 Manager
    /// 推进一次"表面冲突。实际语义是：<see cref="Update"/> 不重复推进投射物（否则与
    /// <see cref="ProjectileManager.Update"/> 双轨推进，违反 spec "Effect manager participates
    /// in a substep"）。<see cref="Update"/> 为空操作——只检查投射物是否已完成以更新
    /// <see cref="Active"/> 标记，供 <see cref="AttackEffectManager"/> 判断是否移除本效果。
    /// 推进的唯一入口是 <see cref="ProjectileManager.Update"/>。这与还原工程
    /// <c>ProjectileAttackEffect.js:39-43</c> 的 update() 一致——它本身不调 movement.update，
    /// 只检查 projectile.active 状态。</para>
    ///
    /// <para><b>取消（task 5.8）：</b>
    /// <see cref="Cancel"/> 委托到 <see cref="ProjectileManager.Remove"/>，取消投射物。
    /// 这让效果系统可以通过 <see cref="AttackEffectManager.CancelOwner"/> 批量取消同所有者
    /// 的全部攻击效果（包括投射物），无需直接操作 <see cref="ProjectileManager"/>。</para>
    ///
    /// <para><b>池化（task 4.1）：</b>实现 <see cref="IPoolableBattleObject"/>，
    /// <see cref="ResetState"/> 清除全部可变状态，回收后等价于新构造。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 AttackEffectManager/BowSoldier
    /// 使用，不对其他程序集暴露。</para>
    /// </remarks>
    internal sealed class ProjectileAttackEffect : IAttackEffect
    {
        // ====================================================================
        // 可变状态字段（对应 ProjectileAttackEffect.js:7-14 reset）
        // ====================================================================

        /// <summary>所有者引用（对应 owner，通常为弓兵单位）。用于 cancelOwner 匹配。</summary>
        private object _owner;

        /// <summary>投射物管理器引用（对应 projectileManager）。供 Cancel 时移除投射物。</summary>
        private ProjectileManager _projectileManager;

        /// <summary>已创建的投射物（对应 projectile）。null 表示尚未发射或已取消。</summary>
        private ProjectileBase _projectile;

        /// <summary>是否活动（对应 active）。</summary>
        private bool _active;

        // ====================================================================
        // 只读属性（IAttackEffect 契约）
        // ====================================================================

        /// <summary>
        /// 是否活动（对应 <c>this.active</c>）。
        /// <para>false 表示效果已完成（投射物已移除）或已被取消。
        /// <see cref="AttackEffectManager.Update"/> 遍历时对 Active=false 的效果执行移除回收。</para>
        /// </summary>
        public bool Active => _active;

        /// <summary>
        /// 所有者引用（对应 <c>this.owner</c>），用于 <see cref="AttackEffectManager.CancelOwner"/> 匹配。
        /// <para>通常为发起攻击的弓兵单位。取消所有者时，Manager 遍历活动效果并移除 Owner 匹配的效果。</para>
        /// </summary>
        public object Owner => _owner;

        // ====================================================================
        // 构造
        // ====================================================================

        /// <summary>
        /// 构造一个 ProjectileAttackEffect。字段初始化为默认值。
        /// </summary>
        /// <remarks>
        /// 需经 <see cref="Launch"/> 发射后才能参与效果系统更新。
        /// </remarks>
        internal ProjectileAttackEffect()
        {
            ResetState();
        }

        // ====================================================================
        // Launch —— 发射投射物并登记到管理器（对应 JS launch）
        // ====================================================================

        /// <summary>
        /// 发射投射物：登记到 <paramref name="projectileManager"/> 并标记为活动。
        /// </summary>
        /// <param name="owner">所有者引用（通常为弓兵单位）。不可为 null。</param>
        /// <param name="projectileManager">投射物管理器。不可为 null。</param>
        /// <param name="projectile">已创建并发射的投射物。不可为 null。</param>
        /// <remarks>
        /// <para>对应还原工程 <c>launch({ owner, projectileManager, config, startPoint })</c>
        /// （ProjectileAttackEffect.js:16-26）。C# 移植简化为接收已创建并发射的投射物——
        /// 投射物的创建（ProjectileFactory.Acquire + Fire）由 BowSoldier（task 6.2）完成，
        /// 本方法只负责登记到管理器与标记活动状态。</para>
        /// <para><b>不在此处推进：</b>本方法只登记，不调用 <see cref="ProjectileBase.Advance"/>。
        /// 推进由 <see cref="ProjectileManager.Update"/> 唯一负责。</para>
        /// </remarks>
        internal void Launch(object owner, ProjectileManager projectileManager, ProjectileBase projectile)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (projectileManager == null)
            {
                throw new ArgumentNullException(nameof(projectileManager));
            }
            if (projectile == null)
            {
                throw new ArgumentNullException(nameof(projectile));
            }

            _owner = owner;
            _projectileManager = projectileManager;
            _projectile = projectile;

            // 登记到管理器（对应 JS projectileManager.create + push）。
            // 投射物已由调用方 Fire，此处只登记。
            projectileManager.Add(projectile);

            _active = projectile.IsActive;
        }

        // ====================================================================
        // IAttackEffect.Update —— 空操作，推进由 ProjectileManager 唯一负责
        // ====================================================================

        /// <summary>
        /// 推进一帧。本方法为空操作——投射物推进的唯一入口是
        /// <see cref="ProjectileManager.Update"/>。
        /// </summary>
        /// <param name="deltaMs">子步时长（毫秒）。本方法忽略此参数。</param>
        /// <remarks>
        /// <para><b>唯一推进拥有者（task 5.8 核心约束）：</b>
        /// 投射物每子步只由 <see cref="ProjectileManager.Update"/> 调用
        /// <see cref="ProjectileBase.Advance"/> 推进一次。若本方法也调 Advance，则同一投射物
        /// 每子步被推进两次（一次 ProjectileManager.Update 在 Projectile 阶段，一次
        /// AttackEffectManager.Update 在 AttackEffect 阶段），违反 spec "Effect manager
        /// participates in a substep" 与 task 5.8 核心约束。</para>
        ///
        /// <para><b>状态同步：</b>本方法检查投射物是否已完成（被移除/回收），若已完成则
        /// 标记 <see cref="Active"/> 为 false，供 <see cref="AttackEffectManager.Update"/>
        /// 判断是否移除本效果。对应还原工程 <c>ProjectileAttackEffect.js:39-43</c> 的
        /// <c>update()</c>——它本身不调 movement.update，只检查 projectile.active 状态。</para>
        ///
        /// <para><b>为什么 AttackEffectManager 仍跟踪本效果：</b>虽然本效果不推进投射物，
        /// 但 AttackEffectManager 跟踪它使得 Settling 静默清理和 <c>cancelOwner</c> 能统一
        /// 处理全部攻击效果（包括投射物）。当单位死亡时，<see cref="AttackEffectManager.CancelOwner"/>
        /// 会批量取消同所有者的效果，本效果的 <see cref="Cancel"/> 委托到
        /// <see cref="ProjectileManager.Remove"/> 取消投射物。</para>
        /// </remarks>
        public void Update(long deltaMs)
        {
            if (!_active)
            {
                return;
            }

            // 投射物已完成（被移除/回收/失活）→ 标记效果为不活动，供 AttackEffectManager 移除。
            // 对应 JS if (!this.projectile || !this.projectile.active) this.cleanup('projectile-complete')。
            // 注意：此处不调 Cancel（避免重入修改 AttackEffectManager 集合），
            // 只更新 Active 标记，让 AttackEffectManager.Update 的 "effect-complete" 分支处理移除。
            if (_projectile == null || !_projectile.IsActive || _projectile.IsRemovalRequested)
            {
                _active = false;
            }
        }

        // ====================================================================
        // IAttackEffect.Cancel —— 取消并清理（对应 JS cleanup）
        // ====================================================================

        /// <summary>
        /// 取消并清理效果（对应 <c>effect.cleanup(reason)</c>）。
        /// </summary>
        /// <param name="reason">取消原因（如 "effect-inactive"、"effect-complete"、"owner-removed"、
        /// "game-over"），供诊断与日志使用。</param>
        /// <remarks>
        /// <para><b>不造成伤害：</b>Cancel 只停止效果活动状态、委托
        /// <see cref="ProjectileManager.Remove"/> 取消投射物，不调用任何伤害提交方法。
        /// 这保证 Settling 静默清理（<see cref="AttackEffectManager.Clear"/>）不违反
        /// "Settling has no gameplay damage authority"。</para>
        /// <para><b>幂等：</b>重复调用安全。已 Cancel 的效果再次 Cancel 为空操作。</para>
        /// <para><b>与 <see cref="ResetState"/> 的区别：</b>
        /// Cancel 停止活动并委托 Manager 移除投射物；ResetState 清空全部可变状态使对象
        /// 等价于新构造（供池复用）。池 Release 时先调 ResetState；管理器移除时先调 Cancel。</para>
        /// </remarks>
        public void Cancel(string reason)
        {
            if (!_active && _projectile == null)
            {
                // 已取消/已清理，幂等空操作。
                return;
            }

            // 委托 ProjectileManager 移除投射物（对应 JS projectileManager.remove(projectile, reason)）。
            // ProjectileManager.Remove 入移除队列，遍历结束后统一回收。
            if (_projectile != null && _projectileManager != null)
            {
                _projectileManager.Remove(_projectile, reason);
            }

            _active = false;
            _projectile = null;
            // 保留 _owner 和 _projectileManager 引用，供 AttackEffectManager 诊断与移除队列处理。
            // ResetState 会清空全部引用。
        }

        // ====================================================================
        // IPoolableBattleObject.ResetState —— 池回收前重置
        // ====================================================================

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>调用时机（task 4.1）：</b>由 <see cref="BattleObjectPool{T}.Release"/>
        /// 在归还对象前调用。</para>
        /// <para><b>完整性要求：</b>清除全部可变状态，使对象等价于新构造。包括：
        /// owner、projectileManager、projectile 引用和 active 标记。</para>
        /// <para><b>幂等性：</b>多次调用安全，结果状态相同。</para>
        /// <para><b>不抛出：</b>实现 MUST NOT 抛出异常。</para>
        /// </remarks>
        public void ResetState()
        {
            _owner = null;
            _projectileManager = null;
            _projectile = null;
            _active = false;
        }
    }
}
