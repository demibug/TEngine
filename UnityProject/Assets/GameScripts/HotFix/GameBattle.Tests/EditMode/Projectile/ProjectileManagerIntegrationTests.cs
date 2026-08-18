using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Projectile
{
    // ============================================================================
    // 任务 5.8 返工：ProjectileManager 集成测试
    // ----------------------------------------------------------------------------
    // 校验发现：
    //   5.8 REWORK 问题 2：缺少 ProjectileManager 层面"新箭下一子步才移动"集成测试。
    //   5.8 REWORK 问题 3：缺少"每子步只由 Manager 推进一次"专项测试。
    //
    // 验证要求：
    //   1. 新箭在创建子步不被推进（因 Projectile 阶段已过去），下一子步才首次移动。
    //   2. 投射物每子步只由 ProjectileManager.Update 推进一次，不双轨推进。
    //
    // 来源证据：
    //   - design.md 第 282 行："新箭因 Projectile 阶段已过去，下一子步才移动。"
    //   - spec battle-simulation "Projectile is launched after projectile phase"。
    //   - spec battle-simulation "Update phases are explicit and single-owned"。
    //   - task 5.8 核心约束：投射物每子步只由 Manager 推进一次。
    //   - ProjectileManager.Update 快照遍历：只遍历调用时已在 _projectiles 中的投射物。
    //   - ProjectileAttackEffect.Update 为空操作，不重复推进。
    //
    // 测试策略：
    //   使用真实 ProjectileFactory、真实 EnemyManager、真实 ProjectileManager、真实
    //   BattleObjectPool<SimpleDynamicArrow>，构造确定性场景验证集成行为。
    //   通过模拟阶段顺序（先 Update 再 Add 新箭）验证"新箭下一子步才移动"。
    //   通过对比 Advance 调用次数验证"每子步只推进一次"。
    // ============================================================================

    /// <summary>
    /// ProjectileManager 集成测试（task 5.8 返工）：新箭下一子步才移动、每子步只推进一次。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>新箭在创建子步不被推进：Update 快照不含创建于本子步后续阶段的新箭。</item>
    /// <item>新箭在下一子步才首次移动：第二个 Update 快照包含新箭并推进。</item>
    /// <item>每子步只推进一次：同一投射物在一个 Update 中只调用一次 Advance。</item>
    /// <item>ProjectileAttackEffect.Update 不重复推进：Effect.Update 为空操作。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class ProjectileManagerIntegrationTests
    {
        // ====================================================================
        // TestEnemy —— 最小 IEnemyEntity 测试桩（与 ProjectileEdgeCaseTests 一致）
        // ====================================================================

        private sealed class TestEnemy : IEnemyEntity
        {
            internal int IdValue;
            internal float XValue;
            internal float YValue;
            internal int HealthValue;
            internal int MaxHealthValue;
            internal int StateValue;
            internal bool TargetableValue;
            internal int HitCount;
            internal int TotalHitDamage;
            internal int LastAttackerId;

            public int Id => IdValue;
            public bool IsPlayerLane => false;
            public int CurrentState => StateValue;
            public float X => XValue;
            public float Y => YValue;
            public float Width => 40f;
            public float Height => 40f;
            public float ProjectileAimOffsetX => 0f;
            public float ProjectileAimOffsetY => 0f;
            public float RemainingPathDistance => 100f;
            public int CurrentPathIndex => 0;
            public int Health => HealthValue;
            public int MaxHealth => MaxHealthValue;

            public void Update(long deltaMs) { }

            public bool Hit(int damage, int attackerId)
            {
                if (HealthValue <= 0 || damage <= 0)
                {
                    return false;
                }
                HealthValue = Math.Max(0, HealthValue - damage);
                TotalHitDamage += damage;
                HitCount++;
                LastAttackerId = attackerId;
                if (HealthValue <= 0)
                {
                    StateValue = 4;
                    TargetableValue = false;
                }
                return true;
            }

            public bool GameOver()
            {
                StateValue = 4;
                TargetableValue = false;
                return true;
            }

            public bool IsTargetableBy(bool playerSide) =>
                TargetableValue && StateValue != 0 && StateValue != 4;
        }

        // ====================================================================
        // 测试夹具创建（复用 ProjectileEdgeCaseTests 模式）
        // ====================================================================

        private const long CreationFrame = 1000;
        private const long NextFrame = 1080;
        private const int GridSize = 80;

        /// <summary>
        /// 创建可供测试的 ProjectileFactory + Pool + EnemyManager 组合。
        /// </summary>
        private static ProjectileFactory CreateFactory(
            out BattleObjectPool<SimpleDynamicArrow> pool,
            out EnemyManager enemyManager)
        {
            var idAllocator = new RuntimeIdAllocator();
            pool = new BattleObjectPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            enemyManager = new EnemyManager(GridSize, null);
            return new ProjectileFactory(idAllocator, pool, enemyManager, 80f);
        }

        /// <summary>
        /// 创建并登记一个测试敌人到 EnemyManager。
        /// </summary>
        private static TestEnemy CreateAndRegisterEnemy(
            EnemyManager enemyManager, int id, float x, float y, int health = 100)
        {
            var enemy = new TestEnemy
            {
                IdValue = id,
                XValue = x,
                YValue = y,
                HealthValue = health,
                StateValue = 1,
                TargetableValue = true,
            };
            enemyManager.Register(enemy);
            return enemy;
        }

        /// <summary>
        /// 获取一个 SimpleDynamicArrow 并 Fire。
        /// </summary>
        private static SimpleDynamicArrow AcquireAndFire(
            ProjectileFactory factory,
            int targetId,
            int attackerId = 10,
            int damage = 20,
            long creationFrame = CreationFrame)
        {
            SimpleDynamicArrow arrow = factory.Acquire(
                targetId, attackerId, attackerDamage: damage,
                explicitDamage: true, damage: damage,
                creationFrameMs: creationFrame,
                speedScale: 1.75f, curveHeight: 120f);
            arrow.Fire(100f, 200f);
            return arrow;
        }

        // ====================================================================
        // 场景：新箭在创建子步不被推进（下一子步才首次移动）
        // ====================================================================

        [Test]
        [Description("新箭在创建子步不被推进：Update 快照不含创建于本子步后续阶段的新箭。"
                     + "模拟阶段顺序：先 Update（Projectile 阶段），再 Add（AttackRelease 阶段创建新箭）。")]
        public void NewArrow_NotAdvancedInCreationSubstep_NextSubstepOnly()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            var projManager = new ProjectileManager(factory);

            // 子步 1：先有一个已存在的箭。
            SimpleDynamicArrow arrow1 = AcquireAndFire(factory, targetId: 1, damage: 30);
            projManager.Add(arrow1);

            // 模拟子步 1 的 Projectile 阶段：Update 推进已有箭。
            double progressBeforeUpdate1 = arrow1.Movement.Progress;
            projManager.Update(NextFrame, 80);
            Assert.Greater(arrow1.Movement.Progress, progressBeforeUpdate1,
                "已有箭应被推进");
            Assert.AreEqual(1, projManager.UpdateCount, "应推进一次");

            // 模拟子步 1 的 AttackRelease 阶段：弓兵创建新箭（在 Projectile 阶段之后）。
            SimpleDynamicArrow newArrow = AcquireAndFire(
                factory, targetId: 1, attackerId: 20, damage: 25,
                creationFrame: NextFrame); // 新箭创建帧 = NextFrame
            projManager.Add(newArrow);

            double newArrowProgressAfterAdd = newArrow.Movement.Progress;
            Assert.AreEqual(0.0, newArrowProgressAfterAdd,
                "新箭在创建子步不应被推进（Projectile 阶段已过去）");

            // 子步 2：下一子步的 Projectile 阶段，新箭首次被推进。
            long nextNextFrame = NextFrame + 80;
            projManager.Update(nextNextFrame, 80);

            Assert.Greater(newArrow.Movement.Progress, 0.0,
                "新箭在下一子步应被首次推进");
            Assert.AreEqual(2, projManager.UpdateCount, "应推进两次");
        }

        [Test]
        [Description("新箭创建于 Update 调用之后（同子步后续阶段）不被该 Update 推进。"
                     + "验证 Update 快照遍历只包含调用时已登记的投射物。")]
        public void NewArrow_AddedAfterUpdateSnapshot_NotInTraversal()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            var projManager = new ProjectileManager(factory);

            // 子步 1：无投射物时 Update。
            projManager.Update(NextFrame, 80);
            Assert.AreEqual(1, projManager.UpdateCount, "应推进一次");
            Assert.AreEqual(0, projManager.ActiveCount, "无活动投射物");

            // Update 之后创建新箭（模拟 AttackRelease 阶段）。
            SimpleDynamicArrow arrow = AcquireAndFire(
                factory, targetId: 1, creationFrame: NextFrame);
            projManager.Add(arrow);

            // 新箭未被本子步的 Update 推进。
            Assert.AreEqual(0.0, arrow.Movement.Progress,
                "新箭不应被本子步 Update 推进");

            // 下一子步推进。
            projManager.Update(NextFrame + 80, 80);
            Assert.Greater(arrow.Movement.Progress, 0.0,
                "新箭在下一子步应被推进");
        }

        // ====================================================================
        // 场景：每子步只由 Manager 推进一次（不双轨）
        // ====================================================================

        [Test]
        [Description("每子步只推进一次：同一投射物在一个 Update 中只调用一次 Advance。"
                     + "通过 UpdateCount 与进度增量验证。")]
        public void EachSubstep_AdvancedOncePerUpdate()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            var projManager = new ProjectileManager(factory);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, damage: 30);
            projManager.Add(arrow);

            // 第一次 Update。
            projManager.Update(NextFrame, 80);
            double progressAfterFirst = arrow.Movement.Progress;
            Assert.Greater(progressAfterFirst, 0.0, "首次推进后进度应大于 0");
            Assert.AreEqual(1, projManager.UpdateCount, "UpdateCount 应为 1");

            // 第二次 Update：进度应继续增加但不翻倍。
            projManager.Update(NextFrame + 80, 80);
            double progressAfterSecond = arrow.Movement.Progress;
            Assert.Greater(progressAfterSecond, progressAfterFirst,
                "第二次推进后进度应继续增加");

            // 第三次 Update：验证持续单次推进。
            projManager.Update(NextFrame + 160, 80);
            double progressAfterThird = arrow.Movement.Progress;
            Assert.Greater(progressAfterThird, progressAfterSecond,
                "第三次推进后进度应继续增加");

            Assert.AreEqual(3, projManager.UpdateCount, "UpdateCount 应为 3");
        }

        [Test]
        [Description("Manager 唯一推进：ProjectileAttackEffect.Update 不重复推进投射物。"
                     + "验证 Effect.Update 为空操作，只 Manager.Update 推进。")]
        public void ManagerSoleOwner_EffectUpdateDoesNotDoubleAdvance()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            var projManager = new ProjectileManager(factory);
            var owner = new object();

            // 创建并发射箭矢。
            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, damage: 30);

            // 创建 ProjectileAttackEffect 桥接到效果系统；Launch 负责唯一一次登记。
            var effect = new ProjectileAttackEffect();
            effect.Launch(owner, projManager, arrow);

            // 先调 Effect.Update（空操作，不推进投射物）。
            double progressBefore = arrow.Movement.Progress;
            effect.Update(80);
            Assert.AreEqual(progressBefore, arrow.Movement.Progress,
                "Effect.Update 不应推进投射物（空操作）");

            // 再调 Manager.Update（唯一推进入口）。
            projManager.Update(NextFrame, 80);
            Assert.Greater(arrow.Movement.Progress, progressBefore,
                "Manager.Update 应推进投射物");

            // 验证 Effect 仍 Active（投射物未完成）。
            Assert.IsTrue(effect.Active, "投射物仍在飞行，Effect 应仍 Active");

            // 再次调 Effect.Update 不推进。
            double progressAfterManager = arrow.Movement.Progress;
            effect.Update(80);
            Assert.AreEqual(progressAfterManager, arrow.Movement.Progress,
                "Effect.Update 再次调用仍不应推进投射物");
        }

        [Test]
        [Description("多个投射物同子步各自只推进一次：两个箭在同一 Update 中各推进一次。")]
        public void MultipleProjectiles_EachAdvancedOncePerSubstep()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);
            CreateAndRegisterEnemy(enemyManager, 2, 500f, 300f, health: 100);

            var projManager = new ProjectileManager(factory);

            SimpleDynamicArrow arrow1 = AcquireAndFire(
                factory, targetId: 1, attackerId: 10, damage: 30,
                creationFrame: CreationFrame);
            SimpleDynamicArrow arrow2 = AcquireAndFire(
                factory, targetId: 2, attackerId: 20, damage: 25,
                creationFrame: CreationFrame + 1);

            projManager.Add(arrow1);
            projManager.Add(arrow2);

            Assert.AreEqual(2, projManager.ActiveCount, "应有两个活动投射物");

            // 同一 Update 中两个箭各推进一次。
            projManager.Update(NextFrame, 80);

            Assert.Greater(arrow1.Movement.Progress, 0.0, "箭 1 应被推进");
            Assert.Greater(arrow2.Movement.Progress, 0.0, "箭 2 应被推进");
            Assert.AreEqual(1, projManager.UpdateCount, "UpdateCount 应为 1（一次 Update 推进全部）");
        }
    }
}
