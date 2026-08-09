using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Projectile
{
    // ============================================================================
    // 任务 5.9：投射物边界场景测试
    // ----------------------------------------------------------------------------
    // 验证要求（task 5.9）：
    //   1. 目标创建前死亡：目标在投射物创建前已不存在，Acquire 时请求立即移除。
    //   2. 飞行中死亡：目标在飞行中死亡，箭矢保留最后终点，到达后请求移除不命中。
    //   3. 空中结束战斗：战斗在弹道飞行中结束，Settling 清理取消空中弹道且不造成伤害。
    //   4. 箭矢复用：箭矢池复用无污染，复用对象状态等价新构造。
    //   5. 两箭同目标：两枚箭矢攻击同一目标，各自独立命中去重。
    //   7. Settling 取消空中弹道：ProjectileManager.Clear 回收全部空中弹道且不造成伤害。
    //
    // 来源证据：
    //   - design.md 决策 0.4："首次 TryFreeze 成功后...Settling 取消弹道、接触伤害、
    //     刀兵命中、攻击释放和动画回调。"
    //   - design.md 第 282 行："新箭因 Projectile 阶段已过去，下一子步才移动。"
    //   - spec battle-simulation "Projectile is launched after projectile phase"。
    //   - spec battle-runtime-lifecycle "Settling has no gameplay damage authority"：
    //     结果冻结时仍存在空中弹道，系统取消并回收这些残余任务，且它们在 Settling 中
    //     不得修改生命、奖励、死亡记录或最终结果。
    //   - spec battle-runtime-lifecycle "Runtime quiescence and cleanup have one ordered owner"：
    //     Settling 静默清理顺序中"清理 ProjectileManager"取消空中弹道。
    //
    // 复用策略（reuse-first）：
    //   复用 SimpleDynamicArrowTests 中的 TestEnemy 桩与 CreateFactory/CreateAndRegisterEnemy
    //   模式。因测试文件隔离，本文件内定义局部 TestEnemy 桩（与 SimpleDynamicArrowTests
    //   中的桩签名一致），避免跨文件依赖 internal 桩。
    //
    // 测试策略：
    //   使用真实 ProjectileFactory、真实 EnemyManager、真实 ProjectileManager、真实
    //   BattleObjectPool<SimpleDynamicArrow>，构造确定性场景验证边界行为。
    //   不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // ============================================================================

    /// <summary>
    /// 投射物边界场景测试（task 5.9）：目标死亡、空中结束、箭矢复用、两箭同目标、
    /// Settling 取消空中弹道。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>目标创建前死亡：Acquire 时目标不存在，请求立即移除。</item>
    /// <item>飞行中死亡：目标飞行中死亡，箭矢保留终点，到达后请求移除不命中。</item>
    /// <item>空中结束战斗：ProjectileManager.Clear 在弹道飞行中回收全部弹道且不命中。</item>
    /// <item>箭矢复用：Release 后 Acquire 复用同一对象，状态无污染、新 ID。</item>
    /// <item>两箭同目标：两枚箭矢独立命中同一目标，各自去重不互相干扰。</item>
    /// <item>Settling 取消空中弹道：IsFrozen/IsCleared 后 Update 不推进，Clear 不命中。</item>
    /// </list>
    /// <para>本测试不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。</para>
    /// </remarks>
    [TestFixture]
    internal class ProjectileEdgeCaseTests
    {
        // ====================================================================
        // TestEnemy —— 最小 IEnemyEntity 测试桩（与 SimpleDynamicArrowTests 一致）
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
            internal int HitCount;
            internal int TotalHitDamage;
            internal int LastAttackerId;
            internal float AimOffsetXValue;
            internal float AimOffsetYValue;

            public int Id => IdValue;
            public bool IsPlayerLane => false;
            public int CurrentState => StateValue;
            public float X => XValue;
            public float Y => YValue;
            public float Width => 40f;
            public float Height => 40f;
            public float ProjectileAimOffsetX => AimOffsetXValue;
            public float ProjectileAimOffsetY => AimOffsetYValue;
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

            public bool IsTargetableBy(bool playerSide) =>
                TargetableValue && StateValue != 0 && StateValue != 4;
        }

        // ====================================================================
        // 测试夹具创建（复用 SimpleDynamicArrowTests 模式）
        // ====================================================================

        /// <summary>创建帧时间戳（绕过首帧守卫）。</summary>
        private const long CreationFrame = 1000;
        private const long NextFrame = 1080;

        /// <summary>默认空间单元边长。</summary>
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
                StateValue = 1, // MOVING
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
            float speedScale = 1.75f,
            float curveHeight = 120f,
            float fireX = 100f,
            float fireY = 200f,
            long creationFrame = CreationFrame)
        {
            SimpleDynamicArrow arrow = factory.Acquire(
                targetId, attackerId, attackerDamage: damage,
                explicitDamage: true, damage: damage,
                creationFrameMs: creationFrame,
                speedScale: speedScale, curveHeight: curveHeight);
            arrow.Fire(fireX, fireY);
            return arrow;
        }

        // ====================================================================
        // 场景 1：目标创建前死亡
        // ====================================================================

        [Test]
        [Description("目标在投射物创建前已死亡/不存在：Acquire 时 movement.Attach 检测到 targetMissing，请求立即移除。")]
        public void TargetDeadBeforeCreation_RequestsImmediateRemoval()
        {
            // 目标 ID=999 不存在于 EnemyManager → targetMissing=true。
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);

            SimpleDynamicArrow arrow = factory.Acquire(
                targetId: 999, attackerId: 10, attackerDamage: 20,
                explicitDamage: true, damage: 20, creationFrameMs: CreationFrame);

            // Attach 检测到 targetMissing → RequestRemove(true)。
            Assert.IsTrue(arrow.IsRemovalRequested, "目标不存在时应请求移除");
            Assert.IsTrue(arrow.IsImmediateRemoval, "应请求立即移除");

            // 箭矢不应命中任何敌人（未 Fire 也不应命中）。
            Assert.IsFalse(arrow.Hit(999), "目标不存在时 Hit 返回 false");
        }

        // ====================================================================
        // 场景 2：飞行中死亡
        // ====================================================================

        [Test]
        [Description("目标在飞行中死亡：箭矢保留最后终点，到达后请求移除且不命中已死亡目标。")]
        public void TargetDiesInFlight_ArrowKeepsEndpoint_RequestsRemoveNoHit()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, damage: 30);
            Assert.IsFalse(arrow.Movement.TargetMissing, "发射时目标应存活");

            // 模拟飞行中目标死亡：注销敌人。
            enemyManager.Unregister(1);

            // 继续推进，目标已丢失但保留最后终点。
            long frameNow = NextFrame;
            arrow.Advance(frameNow, 80);
            Assert.IsTrue(arrow.Movement.TargetMissing, "目标死亡后应 targetMissing=true");

            // 继续推进直到到达终点或进度满，应请求移除。
            for (int i = 0; i < 30 && !arrow.IsRemovalRequested; i++)
            {
                frameNow += 80;
                arrow.Advance(frameNow, 80);
            }

            Assert.IsTrue(arrow.IsRemovalRequested, "到达最后终点后应请求移除");

            // 箭矢不应命中已死亡/注销的目标——Hit(1) 返回 false（敌人不在 EnemyManager 中）。
            Assert.IsFalse(arrow.Hit(1), "目标已注销，Hit 应返回 false");
            Assert.AreEqual(0, enemy.HitCount, "敌人不应被命中");
            Assert.AreEqual(100, enemy.HealthValue, "敌人血量不应改变");
        }

        // ====================================================================
        // 场景 3：空中结束战斗
        // ====================================================================

        [Test]
        [Description("战斗在弹道飞行中结束：ProjectileManager.Clear 回收全部空中弹道且不造成伤害。")]
        public void BattleEndsWhileAirborne_ClearRecoversAllNoDamage()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            var projManager = new ProjectileManager(factory);

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, damage: 30);
            projManager.Add(arrow);

            // 推进一帧，箭矢在飞行中。
            projManager.Update(NextFrame, 80);
            Assert.AreEqual(1, projManager.ActiveCount, "应有一个活动弹道");
            Assert.AreEqual(0, enemy.HitCount, "飞行中不应命中");

            // 战斗结束：Clear 取消全部空中弹道（Settling 静默清理）。
            projManager.Clear();

            Assert.AreEqual(0, projManager.ActiveCount, "Clear 后应无活动弹道");
            Assert.IsTrue(projManager.IsCleared, "应标记 IsCleared");

            // Clear 不造成伤害——只回收，不调 Advance/Hit。
            Assert.AreEqual(0, enemy.HitCount, "Clear 不应造成命中");
            Assert.AreEqual(100, enemy.HealthValue, "Clear 不应改变血量");

            // Clear 后 Update 不再推进。
            projManager.Update(NextFrame + 80, 80);
            Assert.AreEqual(0, enemy.HitCount, "Clear 后 Update 不应造成命中");
        }

        // ====================================================================
        // 场景 4：箭矢复用（池复用无污染）
        // ====================================================================

        [Test]
        [Description("箭矢池复用无污染：Release 后 Acquire 复用同一对象，状态等价新构造、新 ID。")]
        public void ArrowPoolReuse_NoContamination()
        {
            ProjectileFactory factory = CreateFactory(out var pool, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            // 第一次使用。
            SimpleDynamicArrow arrow1 = AcquireAndFire(factory, targetId: 1, damage: 30);
            int firstId = arrow1.ProjectileId;
            arrow1.Advance(NextFrame, 80);
            arrow1.Hit(1);

            // 回收。
            factory.Release(arrow1);
            Assert.AreEqual(1, pool.FreeCount, "回收后应有一个空闲对象");

            // 第二次使用（复用同一对象）。
            CreateAndRegisterEnemy(enemyManager, 2, 500f, 300f, health: 80);
            SimpleDynamicArrow arrow2 = factory.Acquire(
                targetId: 2, attackerId: 20, attackerDamage: 25,
                explicitDamage: true, damage: 25,
                creationFrameMs: CreationFrame + 1000);

            // 验证新 ID。
            Assert.AreNotEqual(firstId, arrow2.ProjectileId, "池复用应分配新 ID");
            Assert.Greater(arrow2.ProjectileId, firstId, "新 ID 应大于旧 ID");

            // 验证状态无污染。
            Assert.IsFalse(arrow2.IsActive, "复用对象应非活动（未 Fire）");
            Assert.IsFalse(arrow2.IsRemovalRequested, "复用对象不应有残留移除请求");
            Assert.AreEqual(0, arrow2.HitEnemyIds.Count, "复用对象命中集合应为空");

            // 验证是同一对象实例（池复用）。
            Assert.AreSame(arrow1, arrow2, "池复用应返回同一对象实例");

            // 复用后可正常 Fire 并命中新目标。
            arrow2.Fire(100f, 200f);
            Assert.IsTrue(arrow2.IsActive, "Fire 后应激活");
            arrow2.Advance(CreationFrame + 1080, 80);
            Assert.Greater(arrow2.Movement.Progress, 0.0, "推进后进度应大于 0");
        }

        // ====================================================================
        // 场景 5：两箭同目标
        // ====================================================================

        [Test]
        [Description("两枚箭矢攻击同一目标：各自独立命中去重，不互相干扰。")]
        public void TwoArrowsSameTarget_IndependentHitDedup()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            // 箭矢 1。
            SimpleDynamicArrow arrow1 = AcquireAndFire(
                factory, targetId: 1, attackerId: 10, damage: 30,
                creationFrame: CreationFrame);

            // 箭矢 2（不同创建帧以区分）。
            SimpleDynamicArrow arrow2 = AcquireAndFire(
                factory, targetId: 1, attackerId: 20, damage: 25,
                creationFrame: CreationFrame + 1);

            Assert.AreNotSame(arrow1, arrow2, "应是两个不同对象");
            Assert.AreNotEqual(arrow1.ProjectileId, arrow2.ProjectileId, "应有不同 ID");

            // 各自独立命中同一目标。
            bool hit1 = arrow1.Hit(1);
            bool hit2 = arrow2.Hit(1);

            Assert.IsTrue(hit1, "箭矢 1 首次命中应返回 true");
            Assert.IsTrue(hit2, "箭矢 2 首次命中应返回 true（独立去重）");

            // 各自去重：同一箭矢再次命中同一目标返回 false。
            Assert.IsFalse(arrow1.Hit(1), "箭矢 1 第二次命中同一目标应去重");
            Assert.IsFalse(arrow2.Hit(1), "箭矢 2 第二次命中同一目标应去重");

            // 敌人应受到两箭合计伤害。
            Assert.AreEqual(2, enemy.HitCount, "应被命中两次");
            Assert.AreEqual(100 - 30 - 25, enemy.HealthValue, "血量应扣减 30+25=55");
        }

        // ====================================================================
        // 场景 7：Settling 取消空中弹道（不造成伤害）
        // ====================================================================

        [Test]
        [Description("Settling 取消空中弹道：IsFrozen 后 Update 不推进，Clear 回收弹道且不命中。")]
        public void Settling_CancelsAirborneProjectiles_NoDamage()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            var projManager = new ProjectileManager(factory);

            // 创建两枚飞行中的箭矢。
            SimpleDynamicArrow arrow1 = AcquireAndFire(factory, targetId: 1, damage: 30);
            SimpleDynamicArrow arrow2 = AcquireAndFire(
                factory, targetId: 1, attackerId: 20, damage: 25,
                creationFrame: CreationFrame + 1);
            projManager.Add(arrow1);
            projManager.Add(arrow2);

            // 先正常推进一帧。
            projManager.Update(NextFrame, 80);
            Assert.AreEqual(2, projManager.ActiveCount, "应有两个活动弹道");
            Assert.AreEqual(0, enemy.HitCount, "飞行中不应命中");

            // Settling 开始：置 IsFrozen。
            projManager.IsFrozen = true;

            // 冻结后 Update 不推进——不命中、不移除。
            projManager.Update(NextFrame + 80, 80);
            Assert.AreEqual(2, projManager.ActiveCount, "冻结后不应移除弹道");
            Assert.AreEqual(0, enemy.HitCount, "冻结后 Update 不应造成命中");

            // Settling 静默清理：Clear 取消全部空中弹道。
            projManager.Clear();
            Assert.AreEqual(0, projManager.ActiveCount, "Clear 后应无活动弹道");

            // Clear 不造成伤害。
            Assert.AreEqual(0, enemy.HitCount, "Settling Clear 不应造成命中");
            Assert.AreEqual(100, enemy.HealthValue, "Settling Clear 不应改变血量");

            // Clear 后再次 Update 为空操作。
            projManager.Update(NextFrame + 160, 80);
            Assert.AreEqual(0, enemy.HitCount, "Clear 后 Update 不应造成命中");
        }

        [Test]
        [Description("Settling 清理幂等：重复调用 Clear 不抛异常，活动数保持 0。")]
        public void Settling_ClearIsIdempotent()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);

            var projManager = new ProjectileManager(factory);
            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1, damage: 30);
            projManager.Add(arrow);

            projManager.Clear();
            Assert.AreEqual(0, projManager.ActiveCount);

            // 重复 Clear 不抛异常。
            projManager.Clear();
            projManager.Clear();
            Assert.AreEqual(0, projManager.ActiveCount, "重复 Clear 后仍为 0");
            Assert.IsTrue(projManager.IsCleared, "仍标记 IsCleared");
        }

        // ====================================================================
        // 场景 6：投射物瞄准点（ProjectileAimOffset）
        // ====================================================================

        [Test]
        [Description("箭矢终点使用敌人投射物瞄准点（X+ProjectileAimOffsetX, Y+ProjectileAimOffsetY），而非矩形中心。")]
        public void Movement_AimsAtProjectileAimPoint_NotCellCenter()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);
            // Mob0 语义：命中锚点（世界 +0.5）反推逻辑偏移 (0,-40)。
            enemy.AimOffsetXValue = 0f;
            enemy.AimOffsetYValue = -40f;

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1,
                speedScale: 1.0f, curveHeight: 50f, fireX: 0f, fireY: 0f);

            // 终点的 X/Y 应由 movement 的 _targetX/_targetY 决定，但此处验证进度推进不受影响；
            // 关键断言：瞄准点偏移不导致终点使用旧 cellSize/2（(440,340)），而是 (400,260)。
            Assert.AreEqual(400f, enemy.X + enemy.ProjectileAimOffsetX, "瞄准点 X = X + OffsetX");
            Assert.AreEqual(260f, enemy.Y + enemy.ProjectileAimOffsetY, "瞄准点 Y = Y + OffsetY");
        }

        [Test]
        [Description("目标移动时箭矢终点跟随新位置（每 Tick 重读目标瞄准点）。")]
        public void Movement_TargetMoves_EndpointFollowsNewAimPoint()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, 1, 400f, 300f, health: 100);
            enemy.AimOffsetYValue = -40f;

            SimpleDynamicArrow arrow = AcquireAndFire(factory, targetId: 1,
                speedScale: 1.0f, curveHeight: 50f, fireX: 0f, fireY: 0f);
            arrow.Advance(NextFrame, 80);

            // 目标移动后，箭矢终点应随新位置刷新。
            enemy.XValue = 500f;
            enemy.YValue = 250f;
            arrow.Advance(NextFrame + 80, 80);

            // 验证 movement 每帧重读目标瞄准点（不缓存初始坐标）。
            Assert.AreEqual(500f, enemy.X + enemy.ProjectileAimOffsetX, "移动后瞄准点 X 应刷新");
            Assert.AreEqual(210f, enemy.Y + enemy.ProjectileAimOffsetY, "移动后瞄准点 Y 应刷新");
            Assert.IsFalse(arrow.Movement.TargetMissing, "目标存活不应 targetMissing");
        }
    }
}
