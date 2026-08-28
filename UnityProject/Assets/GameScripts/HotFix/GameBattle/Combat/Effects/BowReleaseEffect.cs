using System;

namespace GameBattle
{
    /// <summary>
    /// 弓兵攻击延迟释放效果：在攻击动画第 17 帧开始时解析最终目标并实际创建箭矢。
    /// </summary>
    /// <remarks>
    /// <para><b>职责：</b>把"弓兵攻击 → 实际射箭"之间加入延迟，对齐原工程 STOPPED 事件契约
    /// （当前临时按弓兵 30 帧攻击动画的第 17 帧计算）。本效果由 <see cref="AttackEffectManager.Update"/>
    /// 每子步推进，累计 elapsed 达到释放延迟后解析最终目标并调用
    /// <see cref="BowSoldier.LaunchArrow"/> 创建箭矢。</para>
    ///
    /// <para><b>参数化释放规则：</b>到达释放点时对初始目标做首选验证；首选失效时由
    /// <see cref="RangedAttackParameters.LostTargetPolicy"/> 决定取消、锥形换靶或射程内任意换靶；
    /// 失败则不创建箭矢，Release Effect 正常完成。不再次更新冷却时间戳、不重新触发攻击动画、
    /// 不创建第二个 Release Effect。</para>
    ///
    /// <para><b>已释放箭矢不重定向（spec "已释放普通投射物不得重定向"）：</b>箭矢一旦创建，
    /// 其目标 ID 固定，飞行中目标死亡由 Projectile 子系统既有逻辑处理，本效果不再干预。</para>
    ///
    /// <para><b>架构约束（design 决策 4 / 红线）：</b>表现层只读逻辑层、不回写规则状态。
    /// 射箭必须在逻辑层完成，因此用 <see cref="IAttackEffect"/> 承载延迟计时与目标解析，
    /// 而非表现层动画事件回调规则层。</para>
    ///
    /// <para><b>生命周期：</b>弓兵死亡/战斗结束时 <see cref="AttackEffectManager.CancelOwner"/>
    /// 会取消本效果（未发射的箭不再创建）。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BowSoldier / AttackEffectManager 使用。</para>
    /// </remarks>
    internal sealed class BowReleaseEffect : IAttackEffect
    {
        private BowSoldier _owner;
        private AttackResolver _resolver;
        private EnemyManager _enemyManager;
        private int _targetId;
        private float _startX;
        private float _startY;
        private float _attackRange;
        private float _cellSize;
        private float _lockedAimPointX;
        private float _lockedAimPointY;
        private RangedAttackParameters _parameters;
        private long _releaseDelayMs;
        private long _elapsed;
        private bool _active;
        private bool _released;

        /// <inheritdoc/>
        public bool Active => _active;

        /// <inheritdoc/>
        public object Owner => _owner;

        /// <summary>
        /// 初始化延迟释放效果并标记活动。
        /// </summary>
        /// <param name="owner">弓兵所有者（提供 LaunchArrow 实现）。不可为 null。</param>
        /// <param name="resolver">攻击解析服务（非 null），释放点首选/回退解析。</param>
        /// <param name="enemyManager">敌人管理器（非 null），释放点按 ID 查找与稳定查询。</param>
        /// <param name="initialTarget">调度器单次选择的初始目标。</param>
        /// <param name="startX">发射起点逻辑 X。</param>
        /// <param name="startY">发射起点逻辑 Y。</param>
        /// <param name="attackRange">攻击范围（释放点回退查询半径，design 决策 4.2）。</param>
        /// <param name="cellSize">敌人格子尺寸（回退查询透传给 AttackResolver）。</param>
        /// <param name="attackIntervalSeconds">本次攻击的有效攻击间隔（秒）。</param>
        /// <param name="parameters">本次远程攻击参数快照。</param>
        internal void Launch(
            BowSoldier owner,
            AttackResolver resolver,
            EnemyManager enemyManager,
            EnemyTargetDto initialTarget,
            float startX,
            float startY,
            float attackRange,
            float cellSize,
            float attackIntervalSeconds,
            RangedAttackParameters parameters)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _enemyManager = enemyManager ?? throw new ArgumentNullException(nameof(enemyManager));
            _targetId = initialTarget.Id;
            _startX = startX;
            _startY = startY;
            _attackRange = attackRange;
            _cellSize = cellSize;
            _lockedAimPointX = initialTarget.X + cellSize / 2f;
            _lockedAimPointY = initialTarget.Y + cellSize / 2f;
            _parameters = parameters;
            _releaseDelayMs = parameters.CalculateReleaseDelayMs(attackIntervalSeconds);
            _elapsed = 0L;
            _released = false;
            _active = true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>累计 elapsed，达到释放延迟时解析最终目标（首选/回退），成功则调用
        /// <see cref="BowSoldier.LaunchArrow"/> 创建一支箭矢，然后标记 Active=false 完成。
        /// 解析失败则不创建箭矢并正常完成。</para>
        /// </remarks>
        public void Update(long deltaMs)
        {
            if (!_active || _released)
            {
                return;
            }

            _elapsed += deltaMs;
            if (_elapsed < _releaseDelayMs)
            {
                return;
            }

            _released = true;
            _active = false;

            // 防御：owner 或依赖若已失效（游戏结束前被取消）则不发射。
            if (_owner == null || _resolver == null || _enemyManager == null)
            {
                return;
            }

            // 首选有效时继续攻击首选；首选失效时按远程攻击参数决定是否及如何稳定换靶。
            if (!TryResolveReleaseTarget(out EnemyTargetDto finalTarget))
            {
                // 无替代目标：本次攻击不创建箭矢并完成（spec "无替代目标时攻击无伤害完成"）。
                // 不回滚冷却，不重新触发攻击动画——动画由表现层自然结束。
                return;
            }

            // 解析成功：按最终目标创建一支箭矢（design 决策 4.3）。
            // LaunchArrow 按 VisualAimPolicy 决定是否刷新人物朝向，并只创建一支箭。
            // 不再次更新时间戳、不重新触发攻击动画、不创建第二个 Release Effect。
            _owner.LaunchArrow(finalTarget, _startX, _startY, _parameters);
        }

        /// <inheritdoc/>
        /// <remarks>取消后不再发射箭矢。幂等。</remarks>
        public void Cancel(string reason)
        {
            _active = false;
            _released = true;
            _owner = null;
            _resolver = null;
            _enemyManager = null;
        }

        /// <inheritdoc/>
        public void ResetState()
        {
            _owner = null;
            _resolver = null;
            _enemyManager = null;
            _targetId = 0;
            _startX = 0f;
            _startY = 0f;
            _attackRange = 0f;
            _cellSize = 0f;
            _lockedAimPointX = 0f;
            _lockedAimPointY = 0f;
            _parameters = default;
            _releaseDelayMs = 0L;
            _elapsed = 0L;
            _active = false;
            _released = false;
        }

        /// <summary>
        /// 解析释放点最终目标：首选仍有效则保留；否则委托通用远程目标解析模块执行参数策略。
        /// </summary>
        private bool TryResolveReleaseTarget(out EnemyTargetDto finalTarget)
        {
            bool resolved = RangedReleaseTargetResolver.TryResolve(
                _resolver,
                _enemyManager,
                _targetId,
                _startX,
                _startY,
                _lockedAimPointX,
                _lockedAimPointY,
                _attackRange,
                _owner.Side,
                _cellSize,
                _cellSize,
                _parameters,
                out finalTarget);
            return resolved;
        }
    }
}
