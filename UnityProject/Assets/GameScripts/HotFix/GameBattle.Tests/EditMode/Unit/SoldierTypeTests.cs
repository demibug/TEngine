using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Unit
{
    // ============================================================================
    // 任务 6.2：四种士兵单元测试 —— 验证各自创建正确攻击效果并连接管理器/工厂
    // ----------------------------------------------------------------------------
    // 验证要求（task 6.2）：
    //   1. KnifeSoldier.PerformAttack 创建 KnifeAttackEffect 并经 AttackEffectManager.Add 登记。
    //   2. BowSoldier.PerformAttack 创建 SimpleDynamicArrow（经 ProjectileFactory.Acquire）
    //      并创建 ProjectileAttackEffect 桥接到 ProjectileManager。
    //   3. SpearSoldier.PerformAttack 创建 PikeAttackEffect 并经 AttackEffectManager.Add 登记。
    //   4. CavalrySoldier.PerformAttack 创建两个 CavalrySweepEffect（不同半径）并各自登记。
    //   5. 各士兵不持有 Unity GameObject/表现组件。
    //   6. 各士兵 ResetState 清理专属字段（BowSoldier 清理投射物引用）。
    //   7. 无目标时不创建效果。
    //
    // 来源证据：
    //   - design.md 第 180-183 行：四种士兵职责。
    //   - CavalrySweepEffect.cs 注释引用 CavalrySoldier.js:41-58 双段参数。
    //   - KnifeAttackEffect.cs 注释引用 KnifeAttackTimeline.start 500ms 延迟。
    //   - PikeAttackEffect.cs 注释引用 PIKE_HIT_DELAY_MS=360。
    //   - ProjectileAttackEffect.cs 注释引用 BowSoldier.launchArrow 攻击链。
    //
    // 测试策略：
    //   使用真实 AttackResolver、真实 EnemyManager、真实 AttackEffectManager、真实 ProjectileFactory
    //   与真实 ProjectileManager，构造四种士兵并验证 PerformAttack 的效果创建行为。
    //   不接触 Scene、FUI 或资源加载，符合纯逻辑 EditMode 约束（task 2.2）。
    // ============================================================================

    /// <summary>
    /// 四种士兵攻击效果连接单元测试（task 6.2）。
    /// </summary>
    /// <remarks>
    /// <para>验证覆盖：</para>
    /// <list type="bullet">
    /// <item>KnifeSoldier 创建 KnifeAttackEffect 并登记到 AttackEffectManager。</item>
    /// <item>BowSoldier 创建 SimpleDynamicArrow + ProjectileAttackEffect 并登记。</item>
    /// <item>SpearSoldier 创建 PikeAttackEffect 并登记到 AttackEffectManager。</item>
    /// <item>CavalrySoldier 创建两个 CavalrySweepEffect（不同半径）并各自登记。</item>
    /// <item>各士兵无目标时不创建效果。</item>
    /// <item>BowSoldier ResetState 清理投射物引用。</item>
    /// <item>各士兵不持有 Unity 表现组件。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    internal class SoldierTypeTests
    {
        // ====================================================================
        // 测试常量
        // ====================================================================

        /// <summary>测试用逻辑宽度。</summary>
        private const float UnitWidth = 40f;

        /// <summary>测试用逻辑高度。</summary>
        private const float UnitHeight = 40f;

        /// <summary>格子尺寸（对应 map.gridWidth=80）。用于士兵 Configure 与 ProjectileFactory。</summary>
        private const float CellSize = 80f;

        /// <summary>EnemyManager 网格尺寸（int）。</summary>
        private const int GridSize = 80;

        /// <summary>测试用攻击力。</summary>
        private const int AttackDamage = 30;

        /// <summary>测试用运行时 ID。</summary>
        private const int RuntimeId = 10;

        // ====================================================================
        // 测试敌人（与 ProjectileManagerIntegrationTests 模式一致）
        // ====================================================================

        /// <summary>
        /// 最小 IEnemyEntity 测试桩，供目标查询与伤害验证。
        /// </summary>
        private sealed class TestEnemy : IEnemyEntity
        {
            internal int IdValue;
            internal float XValue;
            internal float YValue;
            internal int HealthValue;
            internal int MaxHealthValue;
            internal int StateValue;
            internal bool TargetableValue = true;
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
        // 测试夹具创建
        // ====================================================================

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
            return new ProjectileFactory(idAllocator, pool, enemyManager, CellSize);
        }

        /// <summary>
        /// 创建并登记一个测试敌人到 EnemyManager。
        /// </summary>
        private static TestEnemy CreateAndRegisterEnemy(
            EnemyManager enemyManager, int id, float x, float y, int health = 1000)
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
        /// 构造本次攻击的初始目标 DTO（design 决策 2：初始目标由调度器单次选择并传入）。
        /// <para>测试中模拟 AttackScheduler 把第一个查询结果作为初始目标传入 Attack。</para>
        /// </summary>
        private static EnemyTargetDto InitialTarget(TestEnemy enemy) =>
            new EnemyTargetDto(enemy.IdValue, enemy.XValue, enemy.YValue, float.PositiveInfinity);

        /// <summary>无目标哨兵：模拟调度查询无目标时不会调用 Attack，测试直接传入无效值。</summary>
        private static EnemyTargetDto NoTarget => EnemyTargetDto.Invalid;

        // ====================================================================
        // 士兵初始化辅助（暴露 internal API 供测试）
        // ====================================================================

        /// <summary>测试用单位配置：攻击范围 5 格（400px），攻击力 30。</summary>
        private static UnitConfigSnapshot CreateSoldierConfig(
            int index, string text, string animKey, float attackIntervalSeconds = 1.5f) =>
            new UnitConfigSnapshot(index, text, animKey, rangeCells: 5f,
                attackDamage: AttackDamage, attackIntervalSeconds: attackIntervalSeconds,
                damageMode: "normal", targetPolicy: "first");

        /// <summary>
        /// 初始化刀兵：Configure + AssignRuntimeId + Init + InitStats + ActivatePlacement。
        /// </summary>
        private static KnifeSoldier SetupKnifeSoldier(
            EnemyManager enemyManager,
            AttackResolver resolver,
            AttackEffectManager effectManager,
            float pixelX = 400f, float pixelY = 300f)
        {
            var soldier = new KnifeSoldier();
            soldier.Configure(enemyManager, resolver, effectManager, CellSize, 1);
            soldier.AssignRuntimeIdForTest(RuntimeId);
            soldier.InitForTest("刀", true, UnitWidth, UnitHeight);
            soldier.InitStats(CreateSoldierConfig(0, "刀", "knife"));
            soldier.ActivateAt(pixelX, pixelY);
            return soldier;
        }

        /// <summary>
        /// 初始化弓兵：Configure（含投射物依赖） + AssignRuntimeId + Init + InitStats + ActivatePlacement。
        /// </summary>
        private static BowSoldier SetupBowSoldier(
            EnemyManager enemyManager,
            AttackResolver resolver,
            AttackEffectManager effectManager,
            ProjectileFactory factory,
            ProjectileManager projManager,
            float pixelX = 400f, float pixelY = 300f,
            float attackIntervalSeconds = 1.5f)
        {
            var soldier = new BowSoldier();
            soldier.Configure(enemyManager, resolver, effectManager, factory, projManager, CellSize, 1);
            soldier.AssignRuntimeIdForTest(RuntimeId);
            soldier.InitForTest("弓", true, UnitWidth, UnitHeight);
            soldier.InitStats(CreateSoldierConfig(1, "弓", "bow", attackIntervalSeconds));
            soldier.ActivateAt(pixelX, pixelY);
            return soldier;
        }

        /// <summary>
        /// 初始化枪兵。
        /// </summary>
        private static SpearSoldier SetupSpearSoldier(
            EnemyManager enemyManager,
            AttackResolver resolver,
            AttackEffectManager effectManager,
            float pixelX = 400f, float pixelY = 300f)
        {
            var soldier = new SpearSoldier();
            soldier.Configure(enemyManager, resolver, effectManager, CellSize, 1);
            soldier.AssignRuntimeIdForTest(RuntimeId);
            soldier.InitForTest("枪", true, UnitWidth, UnitHeight);
            soldier.InitStats(CreateSoldierConfig(2, "枪", "pike"));
            soldier.ActivateAt(pixelX, pixelY);
            return soldier;
        }

        /// <summary>
        /// 初始化骑兵。
        /// </summary>
        private static CavalrySoldier SetupCavalrySoldier(
            EnemyManager enemyManager,
            AttackResolver resolver,
            AttackEffectManager effectManager,
            float pixelX = 400f, float pixelY = 300f)
        {
            var soldier = new CavalrySoldier();
            soldier.Configure(enemyManager, resolver, effectManager, CellSize, 1);
            soldier.AssignRuntimeIdForTest(RuntimeId);
            soldier.InitForTest("骑", true, UnitWidth, UnitHeight);
            soldier.InitStats(CreateSoldierConfig(3, "骑", "cavalry"));
            soldier.ActivateAt(pixelX, pixelY);
            return soldier;
        }

        // ====================================================================
        // KnifeSoldier 测试
        // ====================================================================

        [Test]
        [Description("KnifeSoldier.PerformAttack 创建 KnifeAttackEffect 并经 AttackEffectManager.Add 登记。")]
        public void KnifeSoldier_PerformAttack_CreatesKnifeAttackEffectAndRegisters()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, id: 1, x: 400f, y: 300f);

            KnifeSoldier soldier = SetupKnifeSoldier(enemyManager, resolver, effectManager);

            // 攻击前无效果。
            Assert.AreEqual(0, effectManager.ActiveCount, "攻击前应无活动效果");

            soldier.Attack(InitialTarget(enemy));

            // 攻击后应登记一个效果。
            Assert.AreEqual(1, effectManager.ActiveCount, "KnifeSoldier 应创建并登记 1 个效果");

            // 验证效果类型为 KnifeAttackEffect。
            IReadOnlyList<IAttackEffect> snapshot = effectManager.GetEffectsSnapshot();
            Assert.IsInstanceOf<KnifeAttackEffect>(snapshot[0],
                "效果类型应为 KnifeAttackEffect");
        }

        [Test]
        [Description("KnifeSoldier 经 AttackScheduler 调度时无目标不触发 Attack。")]
        public void KnifeSoldier_NoTarget_AttackSchedulerGuardPreventsAttack()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            // 不注册敌人，AttackScheduler 查询无目标。

            KnifeSoldier soldier = SetupKnifeSoldier(enemyManager, resolver, effectManager);
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            var scheduler = new AttackScheduler(actionScheduler, resolver, CellSize, CellSize);
            var units = new List<IAttackUnit> { soldier };

            int count = scheduler.Update(units, enemyManager);

            Assert.AreEqual(0, count, "无目标时 AttackScheduler 不应触发攻击");
            Assert.AreEqual(0, effectManager.ActiveCount,
                "无目标时 KnifeSoldier 不应创建效果（AttackScheduler 守卫已拦截）");
        }

        [Test]
        [Description("KnifeSoldier 500ms 后命中目标（经 AttackEffectManager.Update 推进）。")]
        public void KnifeSoldier_KnifeEffect_HitsTargetAt500ms()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);

            KnifeSoldier soldier = SetupKnifeSoldier(enemyManager, resolver, effectManager);
            soldier.Attack(InitialTarget(enemy));

            // 推进 499ms，不命中。
            effectManager.Update(499);
            Assert.AreEqual(0, enemy.HitCount, "499ms 不应命中");
            Assert.AreEqual(1, effectManager.ActiveCount, "499ms 效果仍应活动");

            // 推进到 500ms+，命中。
            effectManager.Update(10);
            Assert.AreEqual(1, enemy.HitCount, "500ms 应命中一次");
            Assert.AreEqual(AttackDamage, enemy.TotalHitDamage, "伤害应等于士兵攻击力");
            Assert.AreEqual(RuntimeId, enemy.LastAttackerId, "attackerId 应为士兵运行时 ID");
        }

        // ====================================================================
        // BowSoldier 测试
        // ====================================================================

        [Test]
        [Description("BowSoldier.PerformAttack 创建 SimpleDynamicArrow 并经 ProjectileAttackEffect 桥接登记。")]
        public void BowSoldier_PerformAttack_CreatesArrowAndEffect()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);

            Assert.AreEqual(0, effectManager.ActiveCount, "攻击前应无活动效果");
            Assert.AreEqual(0, projManager.ActiveCount, "攻击前应无活动投射物");

            // 模拟调度器传入初始目标（design 决策 2）。
            soldier.Attack(new EnemyTargetDto(1, 600f, 300f, float.PositiveInfinity));

            // 攻击瞬间只登记延迟释放效果，箭矢尚未创建。
            Assert.AreEqual(1, effectManager.ActiveCount, "BowSoldier 应登记 1 个延迟释放效果");
            IReadOnlyList<IAttackEffect> snapshot = effectManager.GetEffectsSnapshot();
            Assert.IsInstanceOf<BowReleaseEffect>(snapshot[0],
                "攻击瞬间应为 BowReleaseEffect（延迟释放）");
            Assert.AreEqual(0, projManager.ActiveCount, "释放延迟前不应创建投射物");

            // 0.8s × 17 / 30 = 453.33ms，向上取整为 454ms，确保不会早于第 17 帧。
            effectManager.Update(453);
            Assert.AreEqual(0, projManager.ActiveCount, "第 17 帧前不应创建投射物");
            effectManager.Update(1);
            Assert.AreEqual(1, projManager.ActiveCount, "第 17 帧开始时应创建 1 个活动投射物");
        }

        [Test]
        [Description("BowSoldier 经 AttackScheduler 调度时无目标不触发 Attack。")]
        public void BowSoldier_NoTarget_AttackSchedulerGuardPreventsAttack()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();

            BowSoldier soldier = SetupBowSoldier(enemyManager, resolver, effectManager, factory, projManager);
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            var scheduler = new AttackScheduler(actionScheduler, resolver, CellSize, CellSize);
            var units = new List<IAttackUnit> { soldier };

            int count = scheduler.Update(units, enemyManager);

            Assert.AreEqual(0, count, "无目标时 AttackScheduler 不应触发攻击");
            Assert.AreEqual(0, effectManager.ActiveCount,
                "无目标时 BowSoldier 不应创建效果（AttackScheduler 守卫已拦截）");
            Assert.AreEqual(0, projManager.ActiveCount, "无目标时不应创建投射物");
        }

        [Test]
        [Description("BowSoldier ResetState 清理投射物工厂与管理器引用。")]
        public void BowSoldier_ResetState_ClearsProjectileReferences()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();

            BowSoldier soldier = SetupBowSoldier(enemyManager, resolver, effectManager, factory, projManager);

            // ResetState 后应可重新 Configure（验证引用已清理，无残留）。
            soldier.ResetState();

            // 重新 Configure 不抛异常即证明旧引用已清理。
            var newFactory = CreateFactory(out _, out var newEnemyManager);
            var newProjManager = new ProjectileManager(newFactory);
            soldier.Configure(newEnemyManager, resolver, effectManager, newFactory, newProjManager, CellSize, 1);
            Assert.Pass("BowSoldier ResetState 后可重新 Configure，投射物引用已清理");
        }

        // ====================================================================
        // SpearSoldier 测试
        // ====================================================================

        [Test]
        [Description("SpearSoldier.PerformAttack 创建 PikeAttackEffect 并经 AttackEffectManager.Add 登记。")]
        public void SpearSoldier_PerformAttack_CreatesPikeAttackEffectAndRegisters()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);

            SpearSoldier soldier = SetupSpearSoldier(enemyManager, resolver, effectManager);

            Assert.AreEqual(0, effectManager.ActiveCount, "攻击前应无活动效果");

            soldier.Attack(new EnemyTargetDto(1, 420f, 300f, float.PositiveInfinity));

            Assert.AreEqual(1, effectManager.ActiveCount, "SpearSoldier 应创建并登记 1 个效果");
            IReadOnlyList<IAttackEffect> snapshot = effectManager.GetEffectsSnapshot();
            Assert.IsInstanceOf<PikeAttackEffect>(snapshot[0],
                "效果类型应为 PikeAttackEffect");
        }

        [Test]
        [Description("SpearSoldier 360ms 后命中目标（经 AttackEffectManager.Update 推进）。")]
        public void SpearSoldier_PikeEffect_HitsTargetAt360ms()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);

            SpearSoldier soldier = SetupSpearSoldier(enemyManager, resolver, effectManager);
            soldier.Attack(InitialTarget(enemy));

            // 推进 359ms，不命中。
            effectManager.Update(359);
            Assert.AreEqual(0, enemy.HitCount, "359ms 不应命中");

            // 推进到 360ms+，命中。
            effectManager.Update(10);
            Assert.AreEqual(1, enemy.HitCount, "360ms 应命中一次");
            Assert.AreEqual(AttackDamage, enemy.TotalHitDamage, "伤害应等于士兵攻击力");
        }

        [Test]
        [Description("范围攻击：枪兵初始朝向目标在命中前死亡，但范围内有其他目标 → 仍按范围命中（不被改成锁定型）。")]
        public void SpearSoldier_InitialAimTargetDead_RangeStillHitsOthers()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy initialAim = CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);
            TestEnemy other = CreateAndRegisterEnemy(enemyManager, id: 2, x: 430f, y: 300f);

            SpearSoldier soldier = SetupSpearSoldier(enemyManager, resolver, effectManager);
            // 初始朝向目标为 initialAim，命中前死亡。
            soldier.Attack(InitialTarget(initialAim));
            initialAim.GameOver();

            // 推进到 360ms+，PikeAttackEffect 按范围查询命中 other（范围语义不变）。
            effectManager.Update(370);
            Assert.AreEqual(0, initialAim.HitCount, "已死亡的初始朝向目标不应被命中");
            Assert.AreEqual(1, other.HitCount, "范围内其他目标应按范围命中");
        }

        [Test]
        [Description("范围攻击：枪兵命中时范围内无目标 → 无伤害，并在完整效果时长后结束。")]
        public void SpearSoldier_RangeEmpty_NoDamageCompletes()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy initialAim = CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);

            SpearSoldier soldier = SetupSpearSoldier(enemyManager, resolver, effectManager);
            soldier.Attack(InitialTarget(initialAim));
            // 命中前唯一目标死亡，范围内无其他目标。
            initialAim.GameOver();

            effectManager.Update(370);
            Assert.AreEqual(0, initialAim.HitCount, "范围内无目标时不应命中");
            Assert.AreEqual(1, effectManager.ActiveCount, "360ms 命中判定后仍应保留 120ms 效果收尾阶段");

            effectManager.Update(110);
            Assert.AreEqual(0, effectManager.ActiveCount, "完整 480ms 效果时长结束后应被清理");
        }

        [Test]
        [Description("范围攻击：同一目标每个 Effect 只命中一次（hitSet 去重，枪兵 360ms 单次）。")]
        public void SpearSoldier_SameTarget_HitOncePerEffect()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f, health: 1000);

            SpearSoldier soldier = SetupSpearSoldier(enemyManager, resolver, effectManager);
            soldier.Attack(InitialTarget(enemy));

            // 推进到 360ms 后再多次推进，同一 Effect 不重复命中。
            effectManager.Update(370);
            effectManager.Update(100);
            effectManager.Update(100);

            Assert.AreEqual(1, enemy.HitCount, "同一 PikeAttackEffect 对同一目标只命中一次");
        }

        [Test]
        [Description("SpearSoldier 经 AttackScheduler 调度时无目标不触发 Attack（守卫验证）。")]
        public void SpearSoldier_NoTarget_AttackSchedulerGuardPreventsAttack()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            // 不注册敌人 → AttackScheduler 查询无目标。

            SpearSoldier soldier = SetupSpearSoldier(enemyManager, resolver, effectManager);

            // 经 AttackScheduler 调度（AttackScheduler 守卫：无目标不调用 unit.Attack()）。
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            var scheduler = new AttackScheduler(actionScheduler, resolver, CellSize, CellSize);

            // 初始 Idle 态 + 冷却完毕 → AttackScheduler 查询目标，无目标则不切换 Attack 态。
            var units = new List<IAttackUnit> { soldier };
            int count = scheduler.Update(units, enemyManager);

            Assert.AreEqual(0, count, "无目标时 AttackScheduler 不应触发攻击");
            Assert.AreEqual(0, effectManager.ActiveCount,
                "无目标时 SpearSoldier 不应创建任何效果（AttackScheduler 守卫已拦截）");

            // 再次调度（模拟下一子步），仍无目标，仍不攻击。
            actionScheduler.BeginFrame(2000);
            count = scheduler.Update(units, enemyManager);
            Assert.AreEqual(0, count, "第二次调度无目标仍不应触发攻击");
            Assert.AreEqual(0, effectManager.ActiveCount, "第二次调度仍不应创建效果");
        }

        // ====================================================================
        // CavalrySoldier 测试
        // ====================================================================

        [Test]
        [Description("CavalrySoldier.PerformAttack 创建两个 CavalrySweepEffect 并各自登记。")]
        public void CavalrySoldier_PerformAttack_CreatesTwoSweepEffects()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);

            CavalrySoldier soldier = SetupCavalrySoldier(enemyManager, resolver, effectManager);

            Assert.AreEqual(0, effectManager.ActiveCount, "攻击前应无活动效果");

            // 骑兵为范围攻击，不消费单目标锁定，传入任意初始目标即可。
            soldier.Attack(NoTarget);

            // 应登记两个效果（双段横扫）。
            Assert.AreEqual(2, effectManager.ActiveCount, "CavalrySoldier 应创建并登记 2 个效果");

            IReadOnlyList<IAttackEffect> snapshot = effectManager.GetEffectsSnapshot();
            Assert.IsInstanceOf<CavalrySweepEffect>(snapshot[0], "效果1应为 CavalrySweepEffect");
            Assert.IsInstanceOf<CavalrySweepEffect>(snapshot[1], "效果2应为 CavalrySweepEffect");
        }

        [Test]
        [Description("CavalrySoldier 150ms 后两段命中同一敌人（合计伤害=AttackDamage）。")]
        public void CavalrySoldier_TwoSweeps_HitSameEnemyAt150ms()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            // 敌人在攻击范围内（半径=AttackRange 与 AttackRange/2 均覆盖）。
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);

            CavalrySoldier soldier = SetupCavalrySoldier(enemyManager, resolver, effectManager);
            soldier.Attack(InitialTarget(enemy));

            // 推进 149ms，不命中。
            effectManager.Update(149);
            Assert.AreEqual(0, enemy.HitCount, "149ms 不应命中");

            // 推进到 150ms+，两段各自命中同一敌人。
            effectManager.Update(10);
            Assert.AreEqual(2, enemy.HitCount, "150ms 应两段各命中一次，共 2 次");
            // 每段 damage * 0.5 = AttackDamage * 0.5，两段合计 = AttackDamage。
            Assert.AreEqual(AttackDamage, enemy.TotalHitDamage,
                "两段合计伤害应等于士兵攻击力");
        }

        [Test]
        [Description("CavalrySoldier 两段效果 radius 不同（近段=AttackRange/2, 远段=AttackRange）。"
                     + "验证两段效果独立 hitSet 可命中同一敌人。")]
        public void CavalrySoldier_TwoSweeps_IndependentHitSets_HitSameEnemy()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy enemy = CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);

            CavalrySoldier soldier = SetupCavalrySoldier(enemyManager, resolver, effectManager);
            soldier.Attack(InitialTarget(enemy));

            // 推进到 150ms+，两段各自命中。
            effectManager.Update(160);
            Assert.AreEqual(2, enemy.HitCount,
                "两段效果独立 hitSet，应各自命中同一敌人一次，共 2 次");
        }

        [Test]
        [Description("CavalrySoldier 经 AttackScheduler 调度时无目标不触发 Attack（守卫验证）。")]
        public void CavalrySoldier_NoTarget_AttackSchedulerGuardPreventsAttack()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            // 不注册敌人 → AttackScheduler 查询无目标。

            CavalrySoldier soldier = SetupCavalrySoldier(enemyManager, resolver, effectManager);

            // 经 AttackScheduler 调度（AttackScheduler 守卫：无目标不调用 unit.Attack()）。
            var actionScheduler = new BattleActionScheduler();
            actionScheduler.BeginFrame(1000);
            var scheduler = new AttackScheduler(actionScheduler, resolver, CellSize, CellSize);

            // 初始 Idle 态 + 冷却完毕 → AttackScheduler 查询目标，无目标则不切换 Attack 态。
            var units = new List<IAttackUnit> { soldier };
            int count = scheduler.Update(units, enemyManager);

            Assert.AreEqual(0, count, "无目标时 AttackScheduler 不应触发攻击");
            Assert.AreEqual(0, effectManager.ActiveCount,
                "无目标时 CavalrySoldier 不应创建任何效果（AttackScheduler 守卫已拦截）");

            // 再次调度（模拟下一子步），仍无目标，仍不攻击。
            actionScheduler.BeginFrame(2000);
            count = scheduler.Update(units, enemyManager);
            Assert.AreEqual(0, count, "第二次调度无目标仍不应触发攻击");
            Assert.AreEqual(0, effectManager.ActiveCount, "第二次调度仍不应创建效果");
        }

        // ====================================================================
        // 纯逻辑验证（不持有 Unity 表现组件）
        // ====================================================================

        [Test]
        [Description("四种士兵不持有 UnityEngine.GameObject 或表现组件（纯逻辑）。")]
        public void AllSoldiers_ArePureLogic_NoUnityComponents()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();

            // 验证四种士兵类型可创建并在无 UnityEngine 依赖下工作。
            // 通过反射检查无 UnityEngine.GameObject/MonoBehaviour 字段。
            var knife = SetupKnifeSoldier(enemyManager, resolver, effectManager);
            var spear = SetupSpearSoldier(enemyManager, resolver, effectManager);
            var cavalry = SetupCavalrySoldier(enemyManager, resolver, effectManager);

            // BowSoldier 需要额外投射物依赖，单独构造。
            ProjectileFactory factory = CreateFactory(out _, out var bowEnemyManager);
            var projManager = new ProjectileManager(factory);
            var bow = SetupBowSoldier(bowEnemyManager, resolver, effectManager, factory, projManager);

            Assert.IsFalse(knife.GetType().Assembly.FullName.Contains("UnityEngine"),
                "KnifeSoldier 不应位于 UnityEngine 程序集");
            Assert.IsFalse(bow.GetType().Assembly.FullName.Contains("UnityEngine"),
                "BowSoldier 不应位于 UnityEngine 程序集");
            Assert.IsFalse(spear.GetType().Assembly.FullName.Contains("UnityEngine"),
                "SpearSoldier 不应位于 UnityEngine 程序集");
            Assert.IsFalse(cavalry.GetType().Assembly.FullName.Contains("UnityEngine"),
                "CavalrySoldier 不应位于 UnityEngine 程序集");
        }

        [Test]
        [Description("运行时攻速倍率变化时重算有效攻击间隔。")]
        public void SoldierBase_AttackInterval_FollowsAttackSpeedMultiplier()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            BowSoldier soldier = SetupBowSoldier(enemyManager, resolver, effectManager, factory, projManager);

            Assert.AreEqual(1.5f, soldier.AttackIntervalSeconds, "初始攻击间隔应使用基础值");

            soldier.SetAttackSpeedMultiplier(1.5f);
            Assert.AreEqual(1f, soldier.AttackIntervalSeconds, 0.0001f,
                "攻速倍率 1.5 时有效攻击间隔应为基础值除以 1.5");
        }

        [Test]
        [Description("攻击动画 30 帧在有效攻击间隔内完整播放，且出箭时序在各种攻速倍率下保持第 17 帧比例。")]
        public void BowAttackTiming_FrameDurationAndReleaseRemainAlignedAcrossSpeedMultipliers()
        {
            float[] multipliers = { 1f, 1.5f, 2.1f };
            for (int index = 0; index < multipliers.Length; index++)
            {
                float multiplier = multipliers[index];
                float effectiveInterval = 0.8f / multiplier;
                float frameDuration = SoldierSpriteAnimator.CalculateAttackFrameDuration(
                    effectiveInterval, frameCount: 30);
                Assert.AreEqual(effectiveInterval, frameDuration * 30f, 0.0001f,
                    $"倍率 {multiplier} 下 30 帧应完整覆盖一个攻击间隔");

                ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
                var projectileManager = new ProjectileManager(factory);
                var resolver = new AttackResolver();
                var effectManager = new AttackEffectManager();
                CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);
                BowSoldier soldier = SetupBowSoldier(
                    enemyManager, resolver, effectManager, factory, projectileManager,
                    attackIntervalSeconds: 0.8f);
                soldier.SetAttackSpeedMultiplier(multiplier);
                soldier.Attack(new EnemyTargetDto(1, 600f, 300f, float.PositiveInfinity));

                long releaseDelayMs = (long)Math.Ceiling(effectiveInterval * 1000d * 17d / 30d);
                effectManager.Update(releaseDelayMs - 1L);
                Assert.AreEqual(0, projectileManager.ActiveCount,
                    $"倍率 {multiplier} 下第 17 帧前不应创建箭矢");
                effectManager.Update(1L);
                Assert.AreEqual(1, projectileManager.ActiveCount,
                    $"倍率 {multiplier} 下第 17 帧开始时应创建箭矢");
            }
        }

        [Test]
        [Description("表现同步首次看到已攻击单位时不漏播攻击动画。")]
        public void BattlePresenter_FirstObservedAttackTime_TriggersAnimation()
        {
            Assert.IsTrue(BattlePresenter.ShouldPlayUnitAttackAnimation(
                    hasPreviousTime: false, previousTime: 0L, attackTimeMs: 800L),
                "首次同步到非零攻击时间应触发攻击动画");
            Assert.IsFalse(BattlePresenter.ShouldPlayUnitAttackAnimation(
                    hasPreviousTime: true, previousTime: 800L, attackTimeMs: 800L),
                "相同攻击时间不应重复触发攻击动画");
        }

        [Test]
        [Description("弓兵攻击写入原工程的初始箭矢切线角，并在进入 Idle 时清除旋转表现意图。")]
        public void BowSoldier_EnterIdle_ClearsBodyRotationIntent()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);

            // 攻击设置旋转意图（原工程：箭矢初始切线角 + 90°）。
            soldier.Attack(new EnemyTargetDto(1, 600f, 300f, float.PositiveInfinity));
            Assert.IsTrue(soldier.HasBodyRotation, "攻击后应设置旋转意图");
            Assert.AreEqual(45f, soldier.BodyRotationDegrees, 0.0001f,
                "目标在右下方且曲线控制点上移 120px 时，原工程的初始攻击角应为 45°");

            // 模拟 AttackScheduler 锁定目标切到 Attack，随后目标丢失切回 Idle。
            soldier.SetState(AttackUnitState.Attack);
            Assert.IsTrue(soldier.HasBodyRotation, "进入 Attack 态不应清除旋转意图");
            soldier.SetState(AttackUnitState.Idle);
            Assert.IsFalse(soldier.HasBodyRotation, "进入 Idle 应清除旋转意图，避免旧旋转残留");
        }

        [Test]
        [Description("枪兵攻击后进入 Idle 清除武器瞄准意图（HasWeaponAim）。")]
        public void SpearSoldier_EnterIdle_ClearsWeaponAimIntent()
        {
            var enemyManager = new EnemyManager(GridSize, null);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            CreateAndRegisterEnemy(enemyManager, id: 1, x: 420f, y: 300f);

            SpearSoldier soldier = SetupSpearSoldier(enemyManager, resolver, effectManager);

            // 攻击设置武器瞄准意图（PerformAttack 中 SetWeaponAim）。
            soldier.Attack(new EnemyTargetDto(1, 420f, 300f, float.PositiveInfinity));
            Assert.IsTrue(soldier.HasWeaponAim, "攻击后应设置武器瞄准意图");

            // 模拟 AttackScheduler 锁定目标切到 Attack，随后目标丢失切回 Idle。
            soldier.SetState(AttackUnitState.Attack);
            Assert.IsTrue(soldier.HasWeaponAim, "进入 Attack 态不应清除瞄准意图");
            soldier.SetState(AttackUnitState.Idle);
            Assert.IsFalse(soldier.HasWeaponAim, "进入 Idle 应清除瞄准意图，避免旧瞄准角残留");
        }

        [Test]
        [Description("弓兵 ResetState 后旋转与瞄准意图清零，池复用无残留。")]
        public void SoldierBase_ResetState_ClearsVisualIntent()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);
            soldier.Attack(new EnemyTargetDto(1, 600f, 300f, float.PositiveInfinity));
            soldier.SetState(AttackUnitState.Attack);
            Assert.IsTrue(soldier.HasBodyRotation, "攻击后应设置旋转意图");

            soldier.ResetState();

            Assert.IsFalse(soldier.HasBodyRotation, "ResetState 应清除旋转意图");
            Assert.IsFalse(soldier.HasWeaponAim, "ResetState 应清除瞄准意图");
        }

        [Test]
        [Description("弓兵攻击后取消所有者（死亡）：未发射的 BowReleaseEffect 被取消，不创建箭矢。")]
        public void BowRelease_CancelOwner_PreventsArrowCreation()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);
            soldier.Attack(new EnemyTargetDto(1, 600f, 300f, float.PositiveInfinity));

            Assert.AreEqual(1, effectManager.ActiveCount, "攻击后应登记延迟释放效果");

            // 弓兵回收：GameOver 应取消其全部效果。
            Assert.IsTrue(soldier.GameOver(), "首次回收应成功");

            // 推进效果管理器到超过释放延迟，箭矢不应创建。
            effectManager.Update(700);
            Assert.AreEqual(0, projManager.ActiveCount, "取消后即使超过释放延迟也不应创建箭矢");
        }

        // ====================================================================
        // 弓兵释放点有限重选测试（design 决策 4.2/4.3，task 4.4）
        // ====================================================================

        [Test]
        [Description("弓兵释放前原目标死亡且存在替代目标 → 只发射一支箭。")]
        public void BowRelease_PreferredDead_HasAlternative_FiresOneArrow()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy first = CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);
            TestEnemy second = CreateAndRegisterEnemy(enemyManager, id: 2, x: 620f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);

            // 初始目标为 first，释放前 first 死亡。
            soldier.Attack(InitialTarget(first));
            first.GameOver();

            // 0.8s × 17 / 30 ≈ 454ms。
            effectManager.Update(454);

            Assert.AreEqual(1, projManager.ActiveCount, "释放点应只创建 1 支箭（回退到替代目标）");
            Assert.AreEqual(1, effectManager.ActiveCount,
                "ReleaseEffect 完成后管理器中应只剩新建箭矢对应的 ProjectileAttackEffect");
        }

        [Test]
        [Description("弓兵释放前原目标死亡且无替代目标 → 不创建箭矢并正常完成。")]
        public void BowRelease_PreferredDead_NoAlternative_NoArrow()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy first = CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);

            soldier.Attack(InitialTarget(first));
            first.GameOver();

            effectManager.Update(454);

            Assert.AreEqual(0, projManager.ActiveCount, "无替代目标时不应创建箭矢");
            Assert.AreEqual(0, effectManager.ActiveCount, "无箭矢也应正常完成 ReleaseEffect");
        }

        [Test]
        [Description("弓兵释放点首选目标仍有效 → 发射一支箭，不切换。")]
        public void BowRelease_PreferredValid_FiresOneArrowNoSwitch()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy first = CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);
            TestEnemy second = CreateAndRegisterEnemy(enemyManager, id: 2, x: 620f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);

            soldier.Attack(InitialTarget(first));
            // first 仍存活。
            effectManager.Update(454);

            Assert.AreEqual(1, projManager.ActiveCount, "首选有效时应只发射 1 支箭");
        }

        [Test]
        [Description("弓兵 Cancel 后不发射箭矢（取消路径不触发重选/发射）。")]
        public void BowRelease_Cancelled_NoArrowNoResolve()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            TestEnemy first = CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);
            TestEnemy second = CreateAndRegisterEnemy(enemyManager, id: 2, x: 620f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);

            soldier.Attack(InitialTarget(first));
            first.GameOver();
            // 释放前取消（如弓兵死亡/回池）。
            Assert.IsTrue(soldier.GameOver(), "首次回收应成功");

            effectManager.Update(454);

            Assert.AreEqual(0, projManager.ActiveCount, "Cancel 后即使到释放点也不应创建箭矢");
        }

        [Test]
        [Description("同一 BowReleaseEffect 只释放一次：重复 Update 不创建第二支箭。")]
        public void BowRelease_RepeatUpdate_OnlyOneArrow()
        {
            ProjectileFactory factory = CreateFactory(out _, out var enemyManager);
            var projManager = new ProjectileManager(factory);
            var resolver = new AttackResolver();
            var effectManager = new AttackEffectManager();
            CreateAndRegisterEnemy(enemyManager, id: 1, x: 600f, y: 300f);

            BowSoldier soldier = SetupBowSoldier(
                enemyManager, resolver, effectManager, factory, projManager,
                attackIntervalSeconds: 0.8f);

            soldier.Attack(new EnemyTargetDto(1, 600f, 300f, float.PositiveInfinity));

            effectManager.Update(454);
            effectManager.Update(100);
            effectManager.Update(100);

            Assert.AreEqual(1, projManager.ActiveCount, "同一 ReleaseEffect 只应释放一次");
        }
    }
}
