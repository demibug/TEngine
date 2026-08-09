using System;

namespace GameBattle
{
    /// <summary>
    /// 弓兵攻击延迟释放效果：在攻击动画第 17 帧开始时实际创建箭矢。
    /// </summary>
    /// <remarks>
    /// <para><b>职责：</b>把"弓兵攻击 → 实际射箭"之间加入延迟，对齐原工程 STOPPED 事件契约
    /// （当前临时按弓兵 30 帧攻击动画的第 17 帧计算）。本效果由 <see cref="AttackEffectManager.Update"/>
    /// 每子步推进，累计 elapsed 达到释放延迟后调用 <see cref="BowSoldier.LaunchArrow"/> 创建箭矢。</para>
    ///
    /// <para><b>架构约束（design 决策 4 / 红线）：</b>表现层只读逻辑层、不回写规则状态。
    /// 射箭必须在逻辑层完成，因此用 <see cref="IAttackEffect"/> 承载延迟计时，而非表现层动画事件
    /// 回调规则层。</para>
    ///
    /// <para><b>释放延迟：</b>有效攻击间隔 × 17 / 30。攻速变化后的下一次攻击会使用
    /// 新的有效攻击间隔，使箭矢始终与第 17 帧开始对齐。</para>
    ///
    /// <para><b>生命周期：</b>弓兵死亡/战斗结束时 <see cref="AttackEffectManager.CancelOwner"/>
    /// 会取消本效果（未发射的箭不再创建）。</para>
    ///
    /// <para><b>本类型为 internal：</b>只供 GameBattle 内部 BowSoldier / AttackEffectManager 使用。</para>
    /// </remarks>
    internal sealed class BowReleaseEffect : IAttackEffect
    {
        /// <summary>
        /// 弓兵攻击动画总帧数。
        /// TODO：后续从单位攻击动画时序配置读取。
        /// </summary>
        private const int AttackFrameCount = 30;

        /// <summary>
        /// 弓兵攻击动画的出箭帧索引（从 0 开始）。
        /// TODO：后续从单位攻击动画时序配置读取。
        /// </summary>
        private const int ReleaseFrameIndex = 17;

        private BowSoldier _owner;
        private int _targetId;
        private float _startX;
        private float _startY;
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
        /// <param name="targetId">目标敌人运行时 ID。</param>
        /// <param name="startX">发射起点逻辑 X。</param>
        /// <param name="startY">发射起点逻辑 Y。</param>
        /// <param name="attackIntervalSeconds">本次攻击的有效攻击间隔（秒）。</param>
        internal void Launch(
            BowSoldier owner,
            int targetId,
            float startX,
            float startY,
            float attackIntervalSeconds)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _targetId = targetId;
            _startX = startX;
            _startY = startY;
            _releaseDelayMs = CalculateReleaseDelayMs(attackIntervalSeconds);
            _elapsed = 0L;
            _released = false;
            _active = true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>累计 elapsed，达到释放延迟时调用
        /// <see cref="BowSoldier.LaunchArrow"/> 创建箭矢，然后标记 Active=false 完成。</para>
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

            // 释放点到达：实际创建并发射箭矢（对应 JS launchArrow）。
            // 防御：owner 若已失效（游戏结束前被取消）则不发射。
            if (_owner != null)
            {
                _owner.LaunchArrow(_targetId, _startX, _startY);
            }
        }

        /// <inheritdoc/>
        /// <remarks>取消后不再发射箭矢。幂等。</remarks>
        public void Cancel(string reason)
        {
            _active = false;
            _released = true;
            _owner = null;
        }

        /// <inheritdoc/>
        public void ResetState()
        {
            _owner = null;
            _targetId = 0;
            _startX = 0f;
            _startY = 0f;
            _releaseDelayMs = 0L;
            _elapsed = 0L;
            _active = false;
            _released = false;
        }

        /// <summary>计算第 17 帧开始时的释放延迟，并向上取整到毫秒避免提前出箭。</summary>
        private static long CalculateReleaseDelayMs(float attackIntervalSeconds)
        {
            float effectiveInterval = attackIntervalSeconds > 0f ? attackIntervalSeconds : 1f;
            return (long)Math.Ceiling(
                effectiveInterval * 1000d * ReleaseFrameIndex / AttackFrameCount);
        }
    }
}
