using System;

namespace GameBattle
{
    /// <summary>远程攻击在释放点发现首选目标失效时的处理规则。</summary>
    internal enum RangedLostTargetPolicy
    {
        /// <summary>不再选择替代目标，本轮不生成投射物。</summary>
        CancelRelease = 0,

        /// <summary>只在开火时锁定方向的锥形范围内稳定选择替代目标。</summary>
        RetargetWithinAimCone = 1,

        /// <summary>忽略方向，选择当前攻击范围内稳定顺序的第一个目标。</summary>
        RetargetAnyInRange = 2,
    }

    /// <summary>一轮远程攻击动画播放期间的角色表现朝向规则。</summary>
    internal enum RangedVisualAimPolicy
    {
        /// <summary>攻击开始时锁定朝向，释放点不再转身。</summary>
        LockForAttack = 0,

        /// <summary>释放点按最终目标刷新朝向。</summary>
        UpdateAtRelease = 1,
    }

    /// <summary>
    /// 远程攻击的代码侧参数集合。当前由硬编码预设提供，后续配置表只需映射到本值对象。
    /// </summary>
    internal readonly struct RangedAttackParameters
    {
        internal RangedAttackParameters(
            int animationFrameCount,
            int releaseFrameIndex,
            RangedLostTargetPolicy lostTargetPolicy,
            float retargetConeDegrees,
            RangedVisualAimPolicy visualAimPolicy,
            float projectileCurveHeight,
            float defaultProjectileSpeedScale)
        {
            if (animationFrameCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(animationFrameCount));
            }
            if (releaseFrameIndex < 0 || releaseFrameIndex >= animationFrameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(releaseFrameIndex));
            }
            if (retargetConeDegrees < 0f || retargetConeDegrees > 180f)
            {
                throw new ArgumentOutOfRangeException(nameof(retargetConeDegrees));
            }
            if (!Enum.IsDefined(typeof(RangedLostTargetPolicy), lostTargetPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(lostTargetPolicy));
            }
            if (!Enum.IsDefined(typeof(RangedVisualAimPolicy), visualAimPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(visualAimPolicy));
            }
            if (projectileCurveHeight < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(projectileCurveHeight));
            }
            if (defaultProjectileSpeedScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultProjectileSpeedScale));
            }

            AnimationFrameCount = animationFrameCount;
            ReleaseFrameIndex = releaseFrameIndex;
            LostTargetPolicy = lostTargetPolicy;
            RetargetConeDegrees = retargetConeDegrees;
            VisualAimPolicy = visualAimPolicy;
            ProjectileCurveHeight = projectileCurveHeight;
            DefaultProjectileSpeedScale = defaultProjectileSpeedScale;
        }

        internal int AnimationFrameCount { get; }
        internal int ReleaseFrameIndex { get; }
        internal RangedLostTargetPolicy LostTargetPolicy { get; }
        internal float RetargetConeDegrees { get; }
        internal RangedVisualAimPolicy VisualAimPolicy { get; }
        internal float ProjectileCurveHeight { get; }
        internal float DefaultProjectileSpeedScale { get; }

        /// <summary>按动画帧比例计算逻辑释放延迟，并向上取整避免提前出手。</summary>
        internal long CalculateReleaseDelayMs(float attackIntervalSeconds)
        {
            float effectiveInterval = attackIntervalSeconds > 0f ? attackIntervalSeconds : 1f;
            long delayMs = (long)Math.Ceiling(
                effectiveInterval * 1000d * ReleaseFrameIndex / AnimationFrameCount);
            return delayMs;
        }
    }

    /// <summary>当前远程攻击的硬编码参数预设；后续由配置映射替代。</summary>
    internal static class RangedAttackPresets
    {
        /// <summary>普通弓兵：30 帧、第 17 帧释放、25°锥形换靶、本轮锁定人物朝向。</summary>
        internal static readonly RangedAttackParameters Bow = new RangedAttackParameters(
            animationFrameCount: 30,
            releaseFrameIndex: 17,
            lostTargetPolicy: RangedLostTargetPolicy.RetargetWithinAimCone,
            retargetConeDegrees: 25f,
            visualAimPolicy: RangedVisualAimPolicy.LockForAttack,
            projectileCurveHeight: 120f,
            defaultProjectileSpeedScale: 1.75f);
    }
}
