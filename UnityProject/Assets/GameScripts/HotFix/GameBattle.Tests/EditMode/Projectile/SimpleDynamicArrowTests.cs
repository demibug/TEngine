using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Projectile
{
    /// <summary>
    /// SimpleDynamicArrow 单元测试（task 5.7）。
    /// </summary>
    /// <remarks>
    /// <para>验证 task 5.7 的全部关键要求：</para>
    /// <list type="bullet">
    /// <item>发射：Fire 后 IsActive=true，起始位置记录正确。</item>
    /// <item>推进：Advance 后位置沿贝塞尔曲线移动，进度递增。</item>
    /// <item>命中：Hit 对有效敌人提交伤害，去重防止多次命中。</item>
    /// <item>目标死亡处置：飞行中目标死亡后保留最后终点，到达后请求移除。</item>
    /// <item>回收 Reset 无污染：Release 后旧 ID/目标/策略引用清除，池复用无残留。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    internal class SimpleDynamicArrowTests
    {
        // ====================================================================
        // 测试用桩：敌人实体
        // ====================================================================

        /// <summary>
        /// 最小可测试敌人实体桩，实现 IEnemyEntity。
        /// </summary>
        private sealed class TestEnemy : IEnemyEntity
        {
            internal int IdValue;
            internal float XValue;
            internal float YValue;
            internal int HealthValue;
            internal int MaxHealthValue;
            internal int StateValue;
            internal bool TargetableValue;
            internal int LastHitDamage;
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
                LastHitDamage = damage;
                LastAttackerId = attackerId;
                if (HealthValue <= 0)
                {
                    StateValue = 4; // DEAD
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

            public bool IsTargetableBy(bool playerSide) => TargetableValue && StateValue != 0 && StateValue != 4;
        }

        // ====================================================================
        // 测试夹具创建
        // ====================================================================

        /// <summary>
        /// 创建帧时间戳常量。Acquire 使用 CreationFrame，Advance 使用 NextFrame
        /// 以绕过新箭首帧守卫（保证测试中推进立即生效）。
        /// </summary>
        private const long CreationFrame = 1000;
        private const long NextFrame = 1080;

        /// <summary>
        /// 创建一个可供测试的 ProjectileFactory 实例，绑定独立的 RuntimeIdAllocator、
        /// EnemyManager 和 BattleObjectPool&lt;SimpleDynamicArrow&gt;。
        /// </summary>
        private static ProjectileFactory CreateFactory(
            out BattleObjectPool<SimpleDynamicArrow> pool,
            out EnemyManager enemyManager)
        {
            var idAllocator = new RuntimeIdAllocator();
            pool = new BattleObjectPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            enemyManager = new EnemyManager(80, null);
            return new ProjectileFactory(idAllocator, pool, enemyManager, 80f);
        }

        /// <summary>
        /// 创建并登记一个测试敌人到 EnemyManager。
        /// </summary>
        private static TestEnemy CreateAndRegisterEnemy(EnemyManager enemyManager, int id, float x, float y, int health = 100)
        {
            var enemy = new TestEnemy
            {
                IdValue = id,
                XValue = x,
                YValue = y,
                HealthValue = health,
                StateValue = 1, // MOVING
                TargetableValue = true,
            };
            enemyManager.Register(enemy);
            return enemy;
        }

        /// <summary>
        /// 获取一个 SimpleDynamicArrow 并 Fire，使用 CreationFrame 标记创建帧。
        /// </summary>
        private static SimpleDynamicArrow AcquireAndFire(
            ProjectileFactory factory,
            int targetId,
            int attackerId = 10,
            int attackerDamage = 20,
            bool explicitDamage = true,
            int damage = 20,
            float speedScale = 1.75f,
            float curveHeight = 120f,
            float fireX = 100f,
            float fireY = 200f)
        {
            // 使用命名参数确保 CreationFrame 绑定到 creationFrameMs（long），
            // 而非误绑到 speedScale（float）。Acquire 签名：
            // (targetId, attackerId, attackerDamage, explicitDamage, damage,
            //  speedScale=1.75f, curveHeight=120f, creationFrameMs=0)
            SimpleDynamicArrow arrow = factory.Acquire(
                targetId, attackerId, attackerDamage, explicitDamage, damage,
                speedScale: speedScale,
                curveHeight: curveHeight,
                creationFrameMs: CreationFrame);
            arrow.Fire(fireX, fireY);
            return arrow;
        }

        // ====================================================================
        // 发射测试
        // ====================================================================

        [Test]
        [Description("Fire 后 IsActive=true，起始位置记录正确。")]
        public void Fire_SetsActiveAndRecordsStartPosition()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);

            Assert.IsTrue(arrow.IsActive, "Fire 后应 IsActive=true");
            Assert.AreEqual(100f, arrow.StartX, "起始位置 X 应为发射点");
            Assert.AreEqual(200f, arrow.StartY, "起始位置 Y 应为发射点");
            Assert.AreEqual(100f, arrow.X, "当前位置 X 应为发射点");
            Assert.AreEqual(200f, arrow.Y, "当前位置 Y 应为发射点");
        }

        [Test]
        [Description("Fire 后重复调用不重新激活。")]
        public void Fire_Twice_DoesNotReactivate()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);
            arrow.Advance(NextFrame, 16);

            float xAfterAdvance = arrow.X;
            arrow.Fire(999f, 999f); // 重复 Fire 不应生效

            Assert.IsTrue(arrow.IsActive);
            Assert.AreEqual(xAfterAdvance, arrow.X, "重复 Fire 不应改变位置");
        }

        // ====================================================================
        // 推进测试
        // ====================================================================

        [Test]
        [Description("Advance 后进度递增，位置沿贝塞尔曲线移动。")]
        public void Advance_IncreasesProgressAndMovesPosition()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, speedScale: 1.75f);

            float xBefore = arrow.X;
            float yBefore = arrow.Y;

            arrow.Advance(NextFrame, 80); // 使用 NextFrame 绕过首帧守卫

            // 进度应大于 0。
            Assert.Greater(arrow.Movement.Progress, 0.0, "推进后进度应大于 0");
            // 位置应发生改变。
            bool positionChanged = Math.Abs(arrow.X - xBefore) > 0.001f || Math.Abs(arrow.Y - yBefore) > 0.001f;
            Assert.IsTrue(positionChanged, "推进后位置应改变");
        }

        [Test]
        [Description("未激活的投射物 Advance 不推进。")]
        public void Advance_Inactive_DoesNotMove()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = factory.Acquire(
                targetId: 1, attackerId: 10, attackerDamage: 20,
                explicitDamage: true, damage: 20, creationFrameMs: CreationFrame);

            // 不调用 Fire，直接 Advance
            arrow.Advance(NextFrame, 80);

            Assert.IsFalse(arrow.IsActive);
            Assert.AreEqual(0.0, arrow.Movement.Progress, "未激活时进度应保持 0");
        }

        [Test]
        [Description("stepMs <= 0 时不推进。")]
        public void Advance_ZeroOrNegativeStep_DoesNotMove()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);

            arrow.Advance(NextFrame, 0);
            Assert.AreEqual(0.0, arrow.Movement.Progress, "stepMs=0 不应推进");

            arrow.Advance(NextFrame, -10);
            Assert.AreEqual(0.0, arrow.Movement.Progress, "stepMs<0 不应推进");
        }

        [Test]
        [Description("新箭在创建帧内 Advance 不推进（首帧守卫），下一帧才移动。")]
        public void Advance_CreationFrameGuard_SkipsFirstFrame()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);

            // 使用 CreationFrame 推进 → 首帧守卫应跳过
            arrow.Advance(CreationFrame, 80);
            Assert.AreEqual(0.0, arrow.Movement.Progress, "创建帧内不应推进");

            // 使用 NextFrame 推进 → 正常推进
            arrow.Advance(NextFrame, 80);
            Assert.Greater(arrow.Movement.Progress, 0.0, "下一帧应正常推进");
        }

        // ====================================================================
        // 命中测试
        // ====================================================================

        [Test]
        [Description("Hit 对有效敌人提交伤害并去重。")]
        public void Hit_DealsDamageAndDeduplicates()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, damage: 30);

            bool firstHit = arrow.Hit(1);
            Assert.IsTrue(firstHit, "首次命中应返回 true");
            Assert.AreEqual(70, enemy.HealthValue, "敌人血量应减少 30");
            Assert.AreEqual(30, enemy.LastHitDamage);
            Assert.AreEqual(10, enemy.LastAttackerId);

            bool secondHit = arrow.Hit(1);
            Assert.IsFalse(secondHit, "同一敌人第二次命中应被去重");
            Assert.AreEqual(70, enemy.HealthValue, "去重后血量不应再减少");
        }

        [Test]
        [Description("Hit 不存在的敌人 ID 返回 false。")]
        public void Hit_NonexistentEnemy_ReturnsFalse()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);

            bool result = arrow.Hit(999); // 不存在的 ID
            Assert.IsFalse(result, "不存在的敌人 ID 命中应返回 false");
        }

        [Test]
        [Description("显式伤害与攻击者攻击力回退。")]
        public void Damage_ExplicitVsAttackerFallback()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            // 显式伤害
            SimpleDynamicArrow arrow1 = factory.Acquire(
                targetId: 1, attackerId: 10, attackerDamage: 50,
                explicitDamage: true, damage: 30, creationFrameMs: CreationFrame);
            Assert.AreEqual(30, arrow1.Damage, "显式伤害应使用传入值");

            // 回退到攻击者攻击力
            SimpleDynamicArrow arrow2 = factory.Acquire(
                targetId: 1, attackerId: 10, attackerDamage: 50,
                explicitDamage: false, damage: 0, creationFrameMs: CreationFrame);
            Assert.AreEqual(50, arrow2.Damage, "未显式指定时应回退到攻击者攻击力");
        }

        // ====================================================================
        // 目标死亡处置测试
        // ====================================================================

        [Test]
        [Description("目标在发射前已死亡：attach 时请求移除。")]
        public void TargetDeadBeforeFire_RequestsRemoval()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);

            // 不登记任何敌人 → targetMissing=true
            SimpleDynamicArrow arrow = factory.Acquire(
                targetId: 999, attackerId: 10, attackerDamage: 20,
                explicitDamage: true, damage: 20, creationFrameMs: CreationFrame);

            // Acquire 中 movement.Attach 检测到 targetMissing → RequestRemove(true)
            Assert.IsTrue(arrow.IsRemovalRequested, "目标不存在时应请求移除");
            Assert.IsTrue(arrow.IsImmediateRemoval, "应请求立即移除");
        }

        [Test]
        [Description("飞行中目标死亡后保留最后终点，到达后请求移除。")]
        public void TargetDiesInFlight_KeepsLastEndpointAndRequestsRemove()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, speedScale: 1.75f);

            Assert.IsFalse(arrow.Movement.TargetMissing, "发射时目标应存活");

            // 模拟飞行中目标死亡：注销敌人
            enemyManager.Unregister(1);

            // 继续推进，目标已丢失但保留最后终点
            long frameNow = NextFrame;
            arrow.Advance(frameNow, 80);
            Assert.IsTrue(arrow.Movement.TargetMissing, "目标死亡后应 targetMissing=true");

            // 继续推进直到到达终点或进度满，应请求移除
            for (int i = 0; i < 20 && !arrow.IsRemovalRequested; i++)
            {
                frameNow += 80;
                arrow.Advance(frameNow, 80);
            }

            Assert.IsTrue(arrow.IsRemovalRequested, "到达最后终点后应请求移除");
        }

        // ====================================================================
        // 回收 Reset 无污染测试
        // ====================================================================

        [Test]
        [Description("Release 后旧 ID/目标/策略引用清除。")]
        public void Release_ClearsAllState()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);
            arrow.Advance(NextFrame, 80);
            arrow.Hit(1);

            int oldId = arrow.ProjectileId;
            Assert.Greater(oldId, 0, "Acquire 后应有有效 ID");

            // 回收
            bool recovered = factory.Release(arrow);
            Assert.IsTrue(recovered, "首次 Release 应成功");

            // 验证状态清除
            Assert.IsTrue(arrow.IsRecovered, "回收后 IsRecovered=true");
            Assert.AreEqual(ProjectileBase.InvalidId, arrow.ProjectileId, "回收后 ID 应为 InvalidId");
            Assert.IsNull(arrow.Movement, "回收后移动策略引用应清除");
            Assert.IsNull(arrow.HitStrategy, "回收后命中策略引用应清除");
            Assert.IsFalse(arrow.IsActive, "回收后应非活动");
        }

        [Test]
        [Description("池复用无污染：Acquire 复用对象后状态等价于新构造。")]
        public void PoolReuse_NoContamination()
        {
            ProjectileFactory factory = CreateFactory(out var pool, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            // 第一次使用
            SimpleDynamicArrow arrow1 = AcquireAndFire(factory, targetId: 1);
            int firstId = arrow1.ProjectileId;
            arrow1.Advance(NextFrame, 80);
            arrow1.Hit(1);

            // 回收
            factory.Release(arrow1);
            Assert.AreEqual(1, pool.FreeCount, "回收后应有一个空闲对象");

            // 第二次使用（复用同一对象）
            CreateAndRegisterEnemy(enemyManager, 2, 500f, 300f);
            SimpleDynamicArrow arrow2 = factory.Acquire(
                targetId: 2, attackerId: 20, attackerDamage: 25,
                explicitDamage: true, damage: 25, creationFrameMs: CreationFrame + 1000);
            int secondId = arrow2.ProjectileId;

            // 验证新 ID
            Assert.AreNotEqual(firstId, secondId, "池复用应分配新 ID");
            Assert.Greater(secondId, firstId, "新 ID 应大于旧 ID");

            // 验证状态无污染
            Assert.IsFalse(arrow2.IsActive, "复用对象应非活动");
            Assert.IsFalse(arrow2.IsRemovalRequested, "复用对象不应有残留移除请求");
            Assert.AreEqual(0, arrow2.HitEnemyIds.Count, "复用对象命中集合应为空");
            Assert.AreEqual(25, arrow2.Damage, "复用对象伤害应为新值");

            // 验证是同一对象实例（池复用）
            Assert.AreSame(arrow1, arrow2, "池复用应返回同一对象实例");
        }

        [Test]
        [Description("重复 Release 返回 false。")]
        public void Release_Twice_ReturnsFalse()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);

            bool first = factory.Release(arrow);
            Assert.IsTrue(first);

            bool second = factory.Release(arrow);
            Assert.IsFalse(second, "重复 Release 应返回 false");
        }

        [Test]
        [Description("Release null 返回 false。")]
        public void Release_Null_ReturnsFalse()
        {
            ProjectileFactory factory = CreateFactory(out _, out _);
            Assert.IsFalse(factory.Release(null));
        }

        // ====================================================================
        // 移除请求与延迟测试
        // ====================================================================

        [Test]
        [Description("RequestRemove 设置移除标记。")]
        public void RequestRemove_SetsFlags()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);
            Assert.IsFalse(arrow.IsRemovalRequested);

            arrow.RequestRemove(false);
            Assert.IsTrue(arrow.IsRemovalRequested);
            Assert.IsFalse(arrow.IsImmediateRemoval);

            arrow.RequestRemove(true);
            Assert.IsTrue(arrow.IsImmediateRemoval);
        }

        [Test]
        [Description("TickRemoveDelay 无延迟时立即返回 true。")]
        public void TickRemoveDelay_NoDelay_ReturnsTrueImmediately()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1);

            // removeDelayMs=0（Acquire 默认）
            Assert.IsTrue(arrow.TickRemoveDelay(80), "无延迟应立即返回 true");
        }

        // ====================================================================
        // 命中策略测试
        // ====================================================================

        [Test]
        [Description("HitEnemyStrategy Apply 对目标提交伤害并标记完成。")]
        public void HitStrategy_AppliesDamageAndMarksCompleted()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, damage: 40);

            HitEnemyStrategy strategy = arrow.HitStrategy;
            Assert.IsFalse(strategy.IsCompleted);

            bool shouldRemove = strategy.Apply(arrow);

            Assert.IsTrue(strategy.IsCompleted, "Apply 后应标记完成");
            Assert.IsTrue(shouldRemove, "removeAfterHit=true 应返回 true");
            Assert.AreEqual(60, enemy.HealthValue, "敌人应受到 40 伤害");
        }

        [Test]
        [Description("HitEnemyStrategy ShouldTrigger 在 requestRemove 模式下受 shouldRemove 控制。")]
        public void HitStrategy_ShouldTrigger_RequestRemoveMode()
        {
            var strategy = new HitEnemyStrategy();
            strategy.Reset(targetId: 1, null, 0, true, "requestRemove");

            Assert.IsFalse(strategy.ShouldTrigger(false, false), "无触发条件应返回 false");
            Assert.IsFalse(strategy.ShouldTrigger(false, true), "hitEnable 不影响 requestRemove 模式");
            Assert.IsTrue(strategy.ShouldTrigger(true, false), "shouldRemove 应触发");
        }

        [Test]
        [Description("HitEnemyStrategy 延迟倒计时正确。")]
        public void HitStrategy_DelayCountdown()
        {
            var strategy = new HitEnemyStrategy();
            strategy.Reset(targetId: 1, null, delayMs: 100, true, "requestRemove");

            // 首次 TickDelay 设置 delayStarted=true，不到期
            Assert.IsFalse(strategy.TickDelay(50), "首次 tick 不到期");
            Assert.IsFalse(strategy.TickDelay(50), "50ms 后仍不到期");
            Assert.IsTrue(strategy.TickDelay(50), "100ms 后到期");
        }

        // ====================================================================
        // 构造校验测试
        // ====================================================================

        [Test]
        [Description("ProjectileFactory 构造时 idAllocator 为 null 抛 ArgumentNullException。")]
        public void Constructor_NullIdAllocator_Throws()
        {
            var pool = new BattleObjectPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            var enemyManager = new EnemyManager(80, null);
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(
                () => new ProjectileFactory(null, pool, enemyManager, 80f));
        }

        [Test]
        [Description("ProjectileFactory 构造时 arrowPool 为 null 抛 ArgumentNullException。")]
        public void Constructor_NullPool_Throws()
        {
            var idAllocator = new RuntimeIdAllocator();
            var enemyManager = new EnemyManager(80, null);
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(
                () => new ProjectileFactory(idAllocator, null, enemyManager, 80f));
        }

        [Test]
        [Description("ProjectileFactory 构造时 enemyManager 为 null 抛 ArgumentNullException。")]
        public void Constructor_NullEnemyManager_Throws()
        {
            var idAllocator = new RuntimeIdAllocator();
            var pool = new BattleObjectPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            // ReSharper disable once AssignNullToNotNullAttribute
            Assert.Throws<ArgumentNullException>(
                () => new ProjectileFactory(idAllocator, pool, null, 80f));
        }

        // ====================================================================
        // ProjectileMath 复用验证
        // ====================================================================

        [Test]
        [Description("TargetEnemyBezierMovement 使用 ProjectileMath 的贝塞尔位置函数。")]
        public void Movement_UsesProjectileMath_BezierPosition()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1,
                speedScale: 1.0f, curveHeight: 50f, fireX: 0f, fireY: 0f);

            // 推进使位置沿贝塞尔曲线移动
            // 终点 = (400+0, 300+0) = (400, 300)（瞄准点 = enemy.X + ProjectileAimOffsetX）
            // 控制点 = ((0+400)/2, (0+300)/2 - 50) = (200, 100)
            arrow.Advance(NextFrame, 80);
            arrow.Advance(NextFrame + 80, 80);
            arrow.Advance(NextFrame + 160, 80);

            // 位置应在起点和终点之间（X 在 0~400 之间）
            Assert.Greater(arrow.X, 0f, "推进后 X 应大于起点 0");
            Assert.Less(arrow.X, 400f, "推进后 X 应小于终点 400");
        }
    }
}
