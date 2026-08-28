using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Combat
{
    [TestFixture]
    internal sealed class RangedAttackParametersTests
    {
        private const float CellSize = 80f;
        private const float OriginX = 420f;
        private const float OriginY = 320f;
        private const float LockedAimPointX = 640f;
        private const float LockedAimPointY = 340f;

        private sealed class TestEnemy : IEnemyEntity
        {
            internal int IdValue;
            internal float XValue;
            internal float YValue;
            internal bool Targetable = true;

            public int Id => IdValue;
            public bool IsPlayerLane => false;
            public int CurrentState => Targetable ? 1 : 4;
            public float X => XValue;
            public float Y => YValue;
            public float Width => 40f;
            public float Height => 40f;
            public float ProjectileAimOffsetX => 0f;
            public float ProjectileAimOffsetY => 0f;
            public float RemainingPathDistance => 100f;
            public int CurrentPathIndex => 0;
            public int Health => Targetable ? 100 : 0;
            public int MaxHealth => 100;
            public void Update(long deltaMs) { }
            public bool Hit(int damage, int attackerId) => false;
            public bool GameOver()
            {
                Targetable = false;
                return true;
            }
            public bool IsTargetableBy(bool playerSide) => Targetable;
        }

        [Test]
        public void BowPreset_CentralizesCurrentHardcodedValues()
        {
            RangedAttackParameters parameters = RangedAttackPresets.Bow;

            Assert.AreEqual(30, parameters.AnimationFrameCount);
            Assert.AreEqual(17, parameters.ReleaseFrameIndex);
            Assert.AreEqual(RangedLostTargetPolicy.RetargetWithinAimCone,
                parameters.LostTargetPolicy);
            Assert.AreEqual(25f, parameters.RetargetConeDegrees);
            Assert.AreEqual(RangedVisualAimPolicy.LockForAttack, parameters.VisualAimPolicy);
            Assert.AreEqual(120f, parameters.ProjectileCurveHeight);
            Assert.AreEqual(1.75f, parameters.DefaultProjectileSpeedScale);
            Assert.AreEqual(454L, parameters.CalculateReleaseDelayMs(0.8f));
        }

        [Test]
        public void CancelRelease_PreferredLost_DoesNotSelectExistingCandidate()
        {
            EnemyManager enemyManager = CreateManagerWithCandidate(
                id: 2, x: 620f, y: 300f);
            RangedAttackParameters parameters = CreateParameters(
                RangedLostTargetPolicy.CancelRelease);

            bool resolved = Resolve(enemyManager, parameters, out EnemyTargetDto finalTarget);

            Assert.IsFalse(resolved);
            Assert.IsFalse(finalTarget.IsValid);
        }

        [Test]
        public void RetargetAnyInRange_PreferredLost_SelectsCandidateBehindLockedAim()
        {
            EnemyManager enemyManager = CreateManagerWithCandidate(
                id: 2, x: 200f, y: 300f);
            RangedAttackParameters parameters = CreateParameters(
                RangedLostTargetPolicy.RetargetAnyInRange);

            bool resolved = Resolve(enemyManager, parameters, out EnemyTargetDto finalTarget);

            Assert.IsTrue(resolved);
            Assert.AreEqual(2, finalTarget.Id);
        }

        [Test]
        public void RetargetWithinAimCone_SkipsOutsideCandidateAndUsesCompatibleCandidate()
        {
            var enemyManager = new EnemyManager(80, null);
            Register(enemyManager, id: 2, x: 200f, y: 300f);
            Register(enemyManager, id: 3, x: 620f, y: 300f);
            RangedAttackParameters parameters = CreateParameters(
                RangedLostTargetPolicy.RetargetWithinAimCone,
                retargetConeDegrees: 25f);

            bool resolved = Resolve(enemyManager, parameters, out EnemyTargetDto finalTarget);

            Assert.IsTrue(resolved);
            Assert.AreEqual(3, finalTarget.Id);
        }

        [Test]
        public void Constructor_RejectsInvalidFrameAndConeConfiguration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RangedAttackParameters(
                animationFrameCount: 0,
                releaseFrameIndex: 0,
                lostTargetPolicy: RangedLostTargetPolicy.CancelRelease,
                retargetConeDegrees: 0f,
                visualAimPolicy: RangedVisualAimPolicy.LockForAttack,
                projectileCurveHeight: 0f,
                defaultProjectileSpeedScale: 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RangedAttackParameters(
                animationFrameCount: 30,
                releaseFrameIndex: 17,
                lostTargetPolicy: RangedLostTargetPolicy.RetargetWithinAimCone,
                retargetConeDegrees: 181f,
                visualAimPolicy: RangedVisualAimPolicy.LockForAttack,
                projectileCurveHeight: 120f,
                defaultProjectileSpeedScale: 1.75f));
        }

        private static RangedAttackParameters CreateParameters(
            RangedLostTargetPolicy lostTargetPolicy,
            float retargetConeDegrees = 25f)
        {
            return new RangedAttackParameters(
                animationFrameCount: 30,
                releaseFrameIndex: 17,
                lostTargetPolicy: lostTargetPolicy,
                retargetConeDegrees: retargetConeDegrees,
                visualAimPolicy: RangedVisualAimPolicy.LockForAttack,
                projectileCurveHeight: 120f,
                defaultProjectileSpeedScale: 1.75f);
        }

        private static EnemyManager CreateManagerWithCandidate(int id, float x, float y)
        {
            var enemyManager = new EnemyManager(80, null);
            Register(enemyManager, id, x, y);
            return enemyManager;
        }

        private static void Register(EnemyManager enemyManager, int id, float x, float y)
        {
            enemyManager.Register(new TestEnemy
            {
                IdValue = id,
                XValue = x,
                YValue = y,
            });
        }

        private static bool Resolve(
            EnemyManager enemyManager,
            RangedAttackParameters parameters,
            out EnemyTargetDto finalTarget)
        {
            var resolver = new AttackResolver();
            bool resolved = RangedReleaseTargetResolver.TryResolve(
                resolver,
                enemyManager,
                preferredTargetId: 1,
                originX: OriginX,
                originY: OriginY,
                lockedAimPointX: LockedAimPointX,
                lockedAimPointY: LockedAimPointY,
                attackRange: 400f,
                playerSide: true,
                cellWidth: CellSize,
                cellHeight: CellSize,
                parameters: parameters,
                out finalTarget);
            return resolved;
        }
    }
}
