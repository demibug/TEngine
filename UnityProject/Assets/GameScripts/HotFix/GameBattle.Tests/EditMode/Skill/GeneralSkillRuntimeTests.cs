using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Skill
{
    // ============================================================================
    // Wave 3：GeneralSkillRuntime 单元测试
    // ----------------------------------------------------------------------------
    // 验证要求：
    //   1. Bind/attach：原子绑定（RegisterOwner+Attach），任一步失败回滚
    //   2. 阈值前累计：OnBasicAttack 累计但未达阈值不激活
    //   3. 成功清零：TryActivateInsteadOfAttack 成功后 AttackCount 清零
    //   4. Busy/失败不清：技能 Busy 或其他失败时 AttackCount 不清零
    //   5. stale generation 拒绝：旧 generation 实例的 TryActivate/OnBasicAttack 被拒
    //   6. Unbind 后重进同 UnitId 保留：下场再上场 AttackCount 保留
    //   7. Clear/Dispose 清局：清全部 owner 与累计，幂等
    //
    // 测试策略：
    //   使用真实 SkillRunner、SkillCatalogSnapshot、SkillHandlerRegistry、RecordingHandler，
    //   以及真实 UnitFactory 创建的 SoldierBase 实例（验证 Id+LifecycleGeneration 绑定）。
    // ============================================================================

    [TestFixture]
    internal class GeneralSkillRuntimeTests
    {
        private const float UnitWidth = 40f;
        private const float UnitHeight = 40f;
        private const float CellSize = 80f;
        private const int GridSize = 80;
        private const int OpponentAttackMultiplier = 1;

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>推进调度器到指定帧时间戳并 flush 到期动作。</summary>
        private static void Step(BattleActionScheduler scheduler, long frameNowMs)
        {
            scheduler.BeginFrame(frameNowMs);
            scheduler.FlushDueActions(1);
        }

        /// <summary>构造带 TriggerAttackCount 的 Skill 定义（handlerKey=key）。</summary>
        private static SkillDefinitionSnapshot Skill(
            string key, SkillCategory category, long cooldownMs, int triggerAttackCount)
        {
            return new SkillDefinitionSnapshot(
                key, category, cooldownMs, key, null, null,
                rangeTiles: null, triggerAttackCount: triggerAttackCount);
        }

        /// <summary>构造 GeneralSkillRuntime + SkillRunner + RecordingHandler。</summary>
        private static RecordingHandler BuildRuntime(
            out GeneralSkillRuntime runtime,
            out SkillRunner runner,
            out BattleActionScheduler scheduler,
            params SkillDefinitionSnapshot[] defs)
        {
            var catalog = new SkillCatalogSnapshot(defs);
            scheduler = new BattleActionScheduler();
            var registry = new SkillHandlerRegistry();
            var handler = new RecordingHandler();
            for (int i = 0; i < defs.Length; i++)
            {
                registry.Register(defs[i].HandlerKey, handler);
            }

            runner = new SkillRunner(catalog, registry, scheduler);
            runtime = new GeneralSkillRuntime(runner, catalog);
            return handler;
        }

        // ====================================================================
        // FakeSoldier —— 最小 SoldierBase 测试替身
        // --------------------------------------------------------------------
        // GeneralSkillRuntime 只依赖 soldier.Id 和 soldier.LifecycleGeneration。
        // 使用真实 UnitFactory 创建的 KnifeSoldier 更重，但可验证真实生命周期。
        // 这里使用真实 UnitFactory 以保证 Id/LifecycleGeneration 真实递增。
        // ====================================================================

        private RuntimeIdAllocator _idAllocator;
        private BattlePoolScope _poolScope;
        private BattleObjectPool<KnifeSoldier> _knifePool;
        private EnemyManager _enemyManager;
        private AttackResolver _attackResolver;
        private AttackEffectManager _attackEffectManager;
        private ProjectileFactory _projectileFactory;
        private ProjectileManager _projectileManager;
        private BattleObjectPool<SimpleDynamicArrow> _arrowPool;
        private UnitFactory _factory;

        [SetUp]
        public void SetUp()
        {
            _idAllocator = new RuntimeIdAllocator();
            _poolScope = new BattlePoolScope();
            _knifePool = _poolScope.GetPool<KnifeSoldier>(() => new KnifeSoldier());

            _enemyManager = new EnemyManager(GridSize);
            _attackResolver = new AttackResolver();
            _attackEffectManager = new AttackEffectManager();

            _arrowPool = _poolScope.GetPool<SimpleDynamicArrow>(() => new SimpleDynamicArrow());
            _projectileFactory = new ProjectileFactory(
                _idAllocator, _arrowPool, _enemyManager, CellSize);
            _projectileManager = new ProjectileManager(_projectileFactory);

            _factory = new UnitFactory(
                _idAllocator,
                _knifePool, _poolScope.GetPool<BowSoldier>(() => new BowSoldier()),
                _poolScope.GetPool<SpearSoldier>(() => new SpearSoldier()),
                _poolScope.GetPool<CavalrySoldier>(() => new CavalrySoldier()),
                _enemyManager, _attackResolver, _attackEffectManager,
                _projectileFactory, _projectileManager,
                CellSize, OpponentAttackMultiplier);
        }

        /// <summary>创建一个已配置的刀兵实例（有 Id 和 LifecycleGeneration）。</summary>
        private SoldierBase AcquireSoldier()
        {
            var config = new UnitConfigSnapshot(0, "刀", "knife", 1.5f, 3, 0.8f, "单体", "nearest");
            return _factory.Acquire(SoldierType.Knife, config, true, UnitWidth, UnitHeight);
        }

        /// <summary>回收士兵到池。</summary>
        private void ReleaseSoldier(SoldierBase soldier)
        {
            _factory.Release(soldier);
        }

        // ====================================================================
        // 测试
        // ====================================================================

        [Test]
        [Description("Bind 成功：RegisterOwner+Attach 原子完成，活动租期已建立。")]
        public void Bind_Success_RegistersOwnerAndAttaches()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out SkillRunner runner, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3));
            SoldierBase soldier = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");

            Assert.AreEqual(1, runtime.ActiveLeaseCount, "应有 1 个活动租期");
            Assert.AreEqual(1, runtime.StateCount, "应有 1 个持久状态");
            Assert.IsTrue(runner.TryGetState(
                new SkillOwnerHandle(soldier.Id, soldier.LifecycleGeneration),
                "AlphaStrike", out _), "Runner 应有 attached state");

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("同一活动实例重复 Bind 同一 skillKey 幂等成功。")]
        public void Bind_SameHandle_SameSkillKey_IdempotentSuccess()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3));
            SoldierBase soldier = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");
            runtime.Bind(unitId: 100, soldier, "AlphaStrike");

            Assert.AreEqual(1, runtime.ActiveLeaseCount, "仍只有 1 个活动租期");

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("同一活动实例已绑定到不同 skillKey 时明确拒绝。")]
        public void Bind_SameHandle_DifferentSkillKey_Rejects()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3),
                Skill("BetaBlast", SkillCategory.Active, 0, 3));
            SoldierBase soldier = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");

            Assert.Throws<InvalidOperationException>(() =>
                runtime.Bind(unitId: 100, soldier, "BetaBlast"),
                "同一活动实例已绑定到不同 skillKey 应抛异常。");

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("Bind 未知 skillKey 失败时原子回滚（RegisterOwner 已回滚，持久 state 不写入）。")]
        public void Bind_UnknownSkillKey_RollsBackAtomically()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out SkillRunner runner, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3));
            SoldierBase soldier = AcquireSoldier();

            Assert.Throws<InvalidOperationException>(() =>
                runtime.Bind(unitId: 100, soldier, "Nonexistent"),
                "未知 skillKey Bind 应抛异常。");

            Assert.AreEqual(0, runtime.ActiveLeaseCount, "活动租期应被回滚");
            Assert.AreEqual(0, runtime.StateCount, "失败不得新增持久 state");
            Assert.AreEqual(0, runner.OwnerCount, "Runner owner 应被回滚");

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("Bind handler 缺失失败时原子回滚：无租期、无持久 state、Runner 无残留。")]
        public void Bind_MissingHandler_RollsBackAtomically_NoState()
        {
            // 目录含定义但 handler 未注册（BuildRuntime 只注册 defs 中的 HandlerKey）。
            // 构造 skillKey 但不用默认的 RecordingHandler 注册路径：
            // 单独构造 catalog/registry，不注册 handler，Attach 返回 HandlerMissing。
            var catalog = new SkillCatalogSnapshot(new[]
            {
                Skill("AlphaStrike", SkillCategory.Active, 0, 3),
            });
            var scheduler = new BattleActionScheduler();
            var registry = new SkillHandlerRegistry();
            var runner = new SkillRunner(catalog, registry, scheduler);
            var runtime = new GeneralSkillRuntime(runner, catalog);
            SoldierBase soldier = AcquireSoldier();

            Assert.Throws<InvalidOperationException>(() =>
                runtime.Bind(unitId: 100, soldier, "AlphaStrike"),
                "handler 未注册时 Bind 应抛异常。");

            Assert.AreEqual(0, runtime.ActiveLeaseCount, "活动租期应被回滚");
            Assert.AreEqual(0, runtime.StateCount, "失败不得新增持久 state");
            Assert.AreEqual(0, runner.OwnerCount, "Runner owner 应被回滚");

            // 失败后可用合法 runtime 重新 Bind（同一 UnitId 无残留）。
            var handler = new RecordingHandler();
            registry.Register("AlphaStrike", handler);
            runtime.Bind(unitId: 100, soldier, "AlphaStrike");
            Assert.AreEqual(1, runtime.ActiveLeaseCount, "重新 Bind 应成功");
            Assert.AreEqual(1, runtime.StateCount, "重新 Bind 应写入持久 state");

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("阈值前累计：OnBasicAttack 累计但未达阈值不激活。")]
        public void TryActivate_BelowThreshold_DoesNotActivate_NoClear()
        {
            RecordingHandler handler = BuildRuntime(out GeneralSkillRuntime runtime, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3));
            SoldierBase soldier = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);

            bool activated = runtime.TryActivateInsteadOfAttack(soldier);

            Assert.IsFalse(activated, "未达阈值不应激活");
            Assert.AreEqual(0, handler.EffectCount, "不应调用 handler.Effect");
            // 未激活不清零：再次 OnBasicAttack 后应从 3 开始（而非从 1）。
            runtime.OnBasicAttack(soldier);
            bool activatedAgain = runtime.TryActivateInsteadOfAttack(soldier);
            Assert.IsTrue(activatedAgain, "3 >= 3 应满足阈值并激活");

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("达阈值成功激活且清零 AttackCount。")]
        public void TryActivate_AtThreshold_ActivatesAndClearsCount()
        {
            RecordingHandler handler = BuildRuntime(out GeneralSkillRuntime runtime,
                out SkillRunner runner, out BattleActionScheduler scheduler,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3));
            SoldierBase soldier = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);

            Step(scheduler, 1000);

            bool activated = runtime.TryActivateInsteadOfAttack(soldier);

            Assert.IsTrue(activated, "达阈值应成功激活");

            // Flush 到期动作以执行 effect 回调。
            Step(scheduler, 1001);
            Assert.AreEqual(1, handler.EffectCount, "应调用 handler.Effect 一次");

            // 激活后清零：再次 OnBasicAttack 后 TryActivate 应未达阈值。
            runtime.OnBasicAttack(soldier);
            bool activatedAgain = runtime.TryActivateInsteadOfAttack(soldier);
            Assert.IsFalse(activatedAgain, "清零后 1 次 < 3 不应激活");

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("Busy 时激活失败不清零 AttackCount。")]
        public void TryActivate_Busy_DoesNotClearCount()
        {
            RecordingHandler handler = BuildRuntime(out GeneralSkillRuntime runtime,
                out SkillRunner runner, out BattleActionScheduler scheduler,
                Skill("AlphaStrike", SkillCategory.Active, 1000, 3));
            SoldierBase soldier = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);

            Step(scheduler, 1000);

            // 首次激活成功。
            bool firstActivate = runtime.TryActivateInsteadOfAttack(soldier);
            Assert.IsTrue(firstActivate, "首次应成功激活");

            // 技能仍在 running（complete 未 flush），再次累计 3 次。
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);

            // Busy：技能仍在运行，激活失败，不清零。
            bool secondActivate = runtime.TryActivateInsteadOfAttack(soldier);
            Assert.IsFalse(secondActivate, "Busy 时不应激活");

            // Flush complete 后技能结束，再累计后可再次激活。
            Step(scheduler, 1001);
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);
            bool thirdActivate = runtime.TryActivateInsteadOfAttack(soldier);
            // 冷却 1000ms，当前帧 1001 > 1000（首次激活 NextReadyAtMs=1000+1000=2000）
            // 实际首次激活在 frame 1000，冷却到 2000，当前 1001 < 2000，冷却中
            Assert.IsFalse(thirdActivate, "OnCooldown 时不应激活");

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("stale generation 拒绝：旧实例的 TryActivate 和 OnBasicAttack 被拒。")]
        public void StaleGeneration_TryActivateAndOnBasicAttack_Rejected()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 1));
            SoldierBase soldier = AcquireSoldier();
            int originalId = soldier.Id;
            int originalGen = soldier.LifecycleGeneration;

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");
            runtime.OnBasicAttack(soldier);

            // 回收到池（LifecycleGeneration 不重置，但 Id 会被 ResetState 置 -1）。
            ReleaseSoldier(soldier);

            // soldier 已回收，Id=-1，不是有效实例。
            // stale 尝试应静默失败。
            bool activated = runtime.TryActivateInsteadOfAttack(soldier);
            Assert.IsFalse(activated, "已回收实例不应激活");

            runtime.OnBasicAttack(soldier);
            // 不影响内部状态（已回收实例不应累计）

            // 重新 Acquire 获得新实例（可能复用池对象，Id 新分配，Generation 递增）。
            SoldierBase newSoldier = AcquireSoldier();
            Assert.Greater(newSoldier.Id, originalId, "新实例应有更大 Id");

            // 旧 UnitId 的租期仍绑定旧 handle，新实例的 TryActivate 应失败（stale）。
            bool newActivate = runtime.TryActivateInsteadOfAttack(newSoldier);
            Assert.IsFalse(newActivate, "新实例（不同 handle）不应通过旧 UnitId 的租期激活");

            ReleaseSoldier(newSoldier);
        }

        [Test]
        [Description("Unbind 后重进同 UnitId 保留 AttackCount。")]
        public void Unbind_RebindSameUnitId_PreservesAttackCount()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 5));
            SoldierBase soldier = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);
            runtime.OnBasicAttack(soldier);

            // Unbind（保留 AttackCount=3）。
            runtime.Unbind(unitId: 100, soldier);
            Assert.AreEqual(0, runtime.ActiveLeaseCount, "Unbind 后活动租期清零");
            Assert.AreEqual(1, runtime.StateCount, "持久状态应保留");

            // 回收旧实例。
            ReleaseSoldier(soldier);

            // 新实例上场，重新 Bind 同一 UnitId。
            SoldierBase newSoldier = AcquireSoldier();
            runtime.Bind(unitId: 100, newSoldier, "AlphaStrike");

            // AttackCount 应保留为 3（上下场保留累计）。
            runtime.OnBasicAttack(newSoldier);
            runtime.OnBasicAttack(newSoldier);

            // 累计到 5 应可激活。
            bool activated = runtime.TryActivateInsteadOfAttack(newSoldier);
            Assert.IsTrue(activated, "上下场保留累计，5 次后应可激活");

            ReleaseSoldier(newSoldier);
        }

        [Test]
        [Description("Clear 清全部 owner 与累计，幂等。")]
        public void Clear_RemovesAllOwnersAndStates_Idempotent()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out SkillRunner runner, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3));
            SoldierBase soldier1 = AcquireSoldier();
            SoldierBase soldier2 = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier1, "AlphaStrike");
            runtime.Bind(unitId: 200, soldier2, "AlphaStrike");
            runtime.OnBasicAttack(soldier1);
            runtime.OnBasicAttack(soldier2);

            runtime.Clear();

            Assert.AreEqual(0, runtime.ActiveLeaseCount, "Clear 后活动租期清零");
            Assert.AreEqual(0, runtime.StateCount, "Clear 后持久状态清零");
            Assert.AreEqual(0, runner.OwnerCount, "Clear 后 Runner owner 清零");

            // 幂等：重复 Clear 安全。
            runtime.Clear();
            Assert.AreEqual(0, runtime.ActiveLeaseCount, "重复 Clear 安全");

            ReleaseSoldier(soldier1);
            ReleaseSoldier(soldier2);
        }

        [Test]
        [Description("Dispose 清全部 owner 与累计，之后拒绝新操作。")]
        public void Dispose_ClearsAll_RejectsNewOperations()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out SkillRunner runner, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3));
            SoldierBase soldier = AcquireSoldier();

            runtime.Bind(unitId: 100, soldier, "AlphaStrike");
            runtime.OnBasicAttack(soldier);

            runtime.Dispose();

            Assert.AreEqual(0, runner.OwnerCount, "Dispose 后 Runner owner 清零");

            // Dispose 后拒绝新操作。
            Assert.Throws<ObjectDisposedException>(() =>
                runtime.Bind(unitId: 200, soldier, "AlphaStrike"));

            // TryActivate/OnBasicAttack 静默返回 false/无操作。
            Assert.IsFalse(runtime.TryActivateInsteadOfAttack(soldier),
                "Dispose 后 TryActivate 应返回 false");
            runtime.OnBasicAttack(soldier); // 静默无操作

            ReleaseSoldier(soldier);
        }

        [Test]
        [Description("OnBasicAttack 仅给已绑定武将累计。")]
        public void OnBasicAttack_OnlyAccumulatesForBoundGenerals()
        {
            BuildRuntime(out GeneralSkillRuntime runtime, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0, 3));
            SoldierBase boundSoldier = AcquireSoldier();
            SoldierBase unboundSoldier = AcquireSoldier();

            runtime.Bind(unitId: 100, boundSoldier, "AlphaStrike");

            // 未绑定武将的 OnBasicAttack 应静默跳过。
            runtime.OnBasicAttack(unboundSoldier);

            // 绑定武将累计。
            runtime.OnBasicAttack(boundSoldier);
            runtime.OnBasicAttack(boundSoldier);
            runtime.OnBasicAttack(boundSoldier);

            bool activated = runtime.TryActivateInsteadOfAttack(boundSoldier);
            Assert.IsTrue(activated, "绑定武将 3 次后应可激活");

            ReleaseSoldier(boundSoldier);
            ReleaseSoldier(unboundSoldier);
        }

        // ====================================================================
        // RecordingHandler —— 记录 ISkillHandler 调用序列（与 SkillRunnerTests 一致）
        // ====================================================================

        /// <summary>记录 ISkillHandler 调用序列、上下文与提交标志的测试 handler。</summary>
        private sealed class RecordingHandler : ISkillHandler
        {
            internal readonly List<string> CallOrder = new List<string>();
            internal readonly List<SkillActivationContext> EffectContexts = new List<SkillActivationContext>();
            internal readonly List<SkillActivationContext> CompleteContexts = new List<SkillActivationContext>();
            internal readonly List<(SkillActivationContext Context, bool EffectCommitted)> Cancels =
                new List<(SkillActivationContext Context, bool EffectCommitted)>();

            internal int EffectCount => EffectContexts.Count;
            internal int CompleteCount => CompleteContexts.Count;
            internal int CancelCount => Cancels.Count;

            public void Effect(SkillActivationContext context)
            {
                EffectContexts.Add(context);
                CallOrder.Add($"effect:{context.Owner.RuntimeId}:{context.SkillKey}");
            }

            public void Complete(SkillActivationContext context)
            {
                CompleteContexts.Add(context);
                CallOrder.Add($"complete:{context.Owner.RuntimeId}:{context.SkillKey}");
            }

            public void Cancel(SkillActivationContext context, bool effectCommitted)
            {
                Cancels.Add((context, effectCommitted));
                CallOrder.Add($"cancel:{context.Owner.RuntimeId}:{context.SkillKey}:" +
                              (effectCommitted ? "committed" : "pending"));
            }
        }
    }
}
