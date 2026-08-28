using System;
using System.Collections.Generic;

namespace GameBattle
{
    /// <summary>
    /// 远程攻击释放点的通用目标解析：保留有效首选目标，并按参数决定是否及如何换靶。
    /// </summary>
    internal static class RangedReleaseTargetResolver
    {
        internal static bool TryResolve(
            AttackResolver resolver,
            EnemyManager enemyManager,
            int preferredTargetId,
            float originX,
            float originY,
            float lockedAimPointX,
            float lockedAimPointY,
            float attackRange,
            bool playerSide,
            float cellWidth,
            float cellHeight,
            RangedAttackParameters parameters,
            out EnemyTargetDto finalTarget)
        {
            finalTarget = EnemyTargetDto.Invalid;
            if (resolver == null || enemyManager == null)
            {
                return false;
            }

            IEnemyEntity preferred = enemyManager.GetById(preferredTargetId);
            if (preferred != null && preferred.IsTargetableBy(playerSide))
            {
                finalTarget = new EnemyTargetDto(
                    preferred.Id,
                    preferred.X,
                    preferred.Y,
                    preferred.RemainingPathDistance);
                return true;
            }

            if (parameters.LostTargetPolicy == RangedLostTargetPolicy.CancelRelease)
            {
                return false;
            }

            List<EnemyTargetDto> candidates = resolver.QueryTargets(
                enemyManager,
                originX,
                originY,
                attackRange,
                playerSide,
                cellWidth,
                cellHeight);
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            if (parameters.LostTargetPolicy == RangedLostTargetPolicy.RetargetAnyInRange)
            {
                finalTarget = candidates[0];
                return true;
            }

            for (int index = 0; index < candidates.Count; index++)
            {
                EnemyTargetDto candidate = candidates[index];
                float candidateAimPointX = candidate.X + cellWidth / 2f;
                float candidateAimPointY = candidate.Y + cellHeight / 2f;
                float angleDeltaDegrees = CalculateAbsoluteAimDeltaDegrees(
                    originX,
                    originY,
                    lockedAimPointX,
                    lockedAimPointY,
                    candidateAimPointX,
                    candidateAimPointY);
                if (angleDeltaDegrees <= parameters.RetargetConeDegrees)
                {
                    finalTarget = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>计算从同一原点指向两个瞄准点的最小绝对夹角，结果范围为 [0, 180]。</summary>
        internal static float CalculateAbsoluteAimDeltaDegrees(
            float originX,
            float originY,
            float firstAimPointX,
            float firstAimPointY,
            float secondAimPointX,
            float secondAimPointY)
        {
            double firstAngle = Math.Atan2(firstAimPointY - originY, firstAimPointX - originX)
                * 180d / Math.PI;
            double secondAngle = Math.Atan2(secondAimPointY - originY, secondAimPointX - originX)
                * 180d / Math.PI;
            double delta = (secondAngle - firstAngle) % 360d;
            if (delta > 180d)
            {
                delta -= 360d;
            }
            else if (delta < -180d)
            {
                delta += 360d;
            }

            float result = (float)Math.Abs(delta);
            return result;
        }
    }
}
