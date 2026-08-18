using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Skill
{
    // ============================================================================
    // 任务 2.5/3.2-3.7/4.1-4.3：SkillRunner 最小生命周期、两节点时间线与取消清理测试
    // ----------------------------------------------------------------------------
    // 验证内容（design.md 决策 2/3/5 / specs/combat-skill-lifecycle/spec.md）：
    //   1. owner 注册/注销：无效 handle、同 handle 幂等、旧 generation 拒绝、新
    //      generation 稳定替换且旧租期状态不泄漏。
    //   2. Attach：未注册 owner/未知 key/Passive/未注册 handler 不创建 state；
    //      重复 attach 幂等保留同一 state。
    //   3. Activate：两阶段原子验证（非法 plan/Busy/冷却/时间加法溢出/调度器拒绝），
    //      失败零副作用且不消耗冷却；成功写 runVersion 与冷却并按 effect→complete
    //      顺序登记两节点。
    //   4. 时间线：effect 先于 complete、跨 fixed step 到期、同刻 FIFO、exactly-once、
    //      callback 用当下 FrameNowMs 建上下文。
    //   5. 取消/清理：effect 前/后取消、重复取消与重复清理、Detach/ClearOwner/
    //      Clear/Dispose、批量清理按 runtimeId/skillKey ordinal 稳定顺序、handler 抛
    //      异常时取消 completion 并结束 running。
    //   6. 池复用安全：相同 runtimeId 新 generation 与 cancel 后新 runVersion 下，
    //      每个 callback 重验 owner/key/version，迟到 callback no-op。
    // ============================================================================

    [TestFixture]
    internal class SkillRunnerTests
    {
        private const int RuntimeId = 7;

        // ====================================================================
        // 构造与依赖
        // ====================================================================

        [Test]
        [Description("Runner 三个依赖任一为 null 时构造抛 ArgumentNullException。")]
        public void Ctor_NullDependencies_Throw()
        {
            var scheduler = new BattleActionScheduler();
            var registry = new SkillHandlerRegistry();
            var catalog = SingleDef(Skill("AlphaStrike", SkillCategory.Active, 0));

            Assert.Throws<ArgumentNullException>(() => new SkillRunner(null, registry, scheduler));
            Assert.Throws<ArgumentNullException>(() => new SkillRunner(catalog, null, scheduler));
            Assert.Throws<ArgumentNullException>(() => new SkillRunner(catalog, registry, null));
        }

        // ====================================================================
        // owner 注册/注销
        // ====================================================================

        [Test]
        [Description("无效 owner 句柄（RuntimeId<=0 或 Generation<=0）注册被拒绝，不建立租期。")]
        public void RegisterOwner_InvalidHandle_IsRejected()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));

            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.RegisterOwner(new SkillOwnerHandle(0, 1)).Status);
            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.RegisterOwner(new SkillOwnerHandle(1, 0)).Status);
            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.RegisterOwner(new SkillOwnerHandle(0, 0)).Status);
            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.RegisterOwner(new SkillOwnerHandle(-3, 2)).Status);

            Assert.IsFalse(runner.TryGetState(new SkillOwnerHandle(1, 1), "AlphaStrike", out _),
                "无效句柄不应建立租期。");
        }

        [Test]
        [Description("同一句柄重复注册幂等成功，既有租期与 state 保持不变。")]
        public void RegisterOwner_SameHandle_IsIdempotentSuccess()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 3);

            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out _),
                "重复注册后租期仍有效。");
        }

        [Test]
        [Description("相同 runtimeId 的更小 generation 注册被拒绝，当前租期不受影响。")]
        public void RegisterOwner_StaleGeneration_IsRejectedAndCurrentUnchanged()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var current = new SkillOwnerHandle(RuntimeId, 2);
            var stale = new SkillOwnerHandle(RuntimeId, 1);

            Assert.IsTrue(runner.RegisterOwner(current).IsSuccess);
            Assert.IsTrue(runner.Attach(current, "AlphaStrike").IsSuccess);

            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.RegisterOwner(stale).Status,
                "旧 generation 注册应被拒绝。");
            Assert.IsTrue(runner.TryGetState(current, "AlphaStrike", out _),
                "当前租期不应被旧 generation 注册影响。");
        }

        [Test]
        [Description("相同 runtimeId 的更大 generation 注册替换旧租期：取消运行激活、清除 state，旧句柄后续操作 StaleOwner。")]
        public void RegisterOwner_NewerGeneration_SupersedesOldLeaseAndIsolatesOldHandle()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var oldHandle = new SkillOwnerHandle(RuntimeId, 1);
            var newHandle = new SkillOwnerHandle(RuntimeId, 2);

            Assert.IsTrue(runner.RegisterOwner(oldHandle).IsSuccess);
            Assert.IsTrue(runner.Attach(oldHandle, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(oldHandle, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Assert.IsTrue(runner.RegisterOwner(newHandle).IsSuccess, "新租期应被接受。");

            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.Attach(oldHandle, "AlphaStrike").Status,
                "旧句柄 attach 不得影响新租期。");
            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.Activate(oldHandle, "AlphaStrike", Plan(10, 20)).Status,
                "旧句柄激活不得影响新租期。");

            Assert.IsFalse(runner.TryGetState(newHandle, "AlphaStrike", out _),
                "旧租期 state 应被清除。");

            Assert.IsTrue(runner.Attach(newHandle, "AlphaStrike").IsSuccess,
                "新租期应可直接 attach（租期已替换）。");
            Assert.IsTrue(runner.TryGetState(newHandle, "AlphaStrike", out SkillStateSnapshot reattached));
            Assert.AreEqual(0L, reattached.RunVersion, "新租期应从零开始。");
        }

        [Test]
        [Description("RegisterOwner 新租期替换时，旧租期的运行激活只取消一次（effectCommitted 按当前提交状态）。")]
        public void RegisterOwner_NewerGeneration_CancelsRunningActivationOnce()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out BattleActionScheduler scheduler, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var oldHandle = new SkillOwnerHandle(RuntimeId, 1);
            var newHandle = new SkillOwnerHandle(RuntimeId, 2);

            Assert.IsTrue(runner.RegisterOwner(oldHandle).IsSuccess);
            Assert.IsTrue(runner.Attach(oldHandle, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(oldHandle, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Step(scheduler, 100); // effect 已提交
            Assert.IsTrue(runner.RegisterOwner(newHandle).IsSuccess);

            Assert.AreEqual(1, handler.CancelCount, "替换旧租期应取消运行中激活一次。");
            Assert.IsTrue(handler.Cancels[0].EffectCommitted, "effect 已提交，取消应报告 committed。");
            Assert.AreEqual(1, handler.EffectCount);

            Step(scheduler, 200);
            Assert.AreEqual(0, handler.CompleteCount, "旧租期的 complete 不得执行。");
        }

        [Test]
        [Description("RegisterOwner 新租期替换后，旧租期迟到 effect callback no-op（不调用 handler）。")]
        public void RegisterOwner_NewerGeneration_LateCallbackIsNoOp()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var oldHandle = new SkillOwnerHandle(RuntimeId, 1);
            var newHandle = new SkillOwnerHandle(RuntimeId, 2);

            Assert.IsTrue(runner.RegisterOwner(oldHandle).IsSuccess);
            Assert.IsTrue(runner.Attach(oldHandle, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(oldHandle, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Assert.IsTrue(runner.RegisterOwner(newHandle).IsSuccess);
            Step(runner.Scheduler, 100);
            Step(runner.Scheduler, 200);

            Assert.AreEqual(0, handler.EffectCount, "迟到 effect callback 不得调用 handler。");
            Assert.AreEqual(0, handler.CompleteCount, "迟到 complete callback 不得调用 handler。");
            Assert.AreEqual(1, handler.CancelCount, "替换时仅一次取消（effect 未提交）。");
            Assert.IsFalse(handler.Cancels[0].EffectCommitted, "effect 未提交，取消应报告 pending。");
        }

        [Test]
        [Description("UnregisterOwner 取消全部运行激活、清除 state 并移除租期。")]
        public void UnregisterOwner_RemovesLeaseAndStates()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);

            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Assert.IsTrue(runner.UnregisterOwner(owner).IsSuccess);

            Assert.AreEqual(1, handler.CancelCount, "注销应取消运行中激活一次。");
            Assert.IsFalse(runner.TryGetState(owner, "AlphaStrike", out _), "租期应已移除。");
            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.Attach(owner, "AlphaStrike").Status);
        }

        // ====================================================================
        // Attach
        // ====================================================================

        [Test]
        [Description("Attach 未注册 owner → StaleOwner，不创建任何 state。")]
        public void Attach_UnregisteredOwner_ReturnsStaleOwner()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));

            Assert.AreEqual(SkillOperationStatus.StaleOwner,
                runner.Attach(new SkillOwnerHandle(RuntimeId, 1), "AlphaStrike").Status);
            Assert.IsFalse(runner.TryGetState(new SkillOwnerHandle(RuntimeId, 1), "AlphaStrike", out _));
        }

        [Test]
        [Description("Attach 未知技能 key → UnknownSkillKey。")]
        public void Attach_UnknownSkillKey_ReturnsUnknownSkillKey()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.UnknownSkillKey,
                runner.Attach(owner, "NoSuchSkill").Status);
        }

        [Test]
        [Description("Attach Passive 技能 → UnsupportedCategory，不创建 state。")]
        public void Attach_Passive_ReturnsUnsupportedCategory()
        {
            SkillRunner runner = BuildRunner(Skill("StunPassive", SkillCategory.Passive, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.UnsupportedCategory,
                runner.Attach(owner, "StunPassive").Status);
            Assert.IsFalse(runner.TryGetState(owner, "StunPassive", out _),
                "Passive attach 不得创建 state。");
        }

        [Test]
        [Description("未知 SkillCategory 与 Passive 一样明确拒绝，不得创建 state 或调度工作。")]
        public void UnknownCategory_IsUnsupportedForAttachAndActivate()
        {
            SkillRunner runner = BuildRunner(Skill("Broken", (SkillCategory)99, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.UnsupportedCategory,
                runner.Attach(owner, "Broken").Status);
            Assert.AreEqual(SkillOperationStatus.UnsupportedCategory,
                runner.Activate(owner, "Broken", Plan(10, 20)).Status);
            Assert.IsFalse(runner.TryGetState(owner, "Broken", out _));
        }

        [Test]
        [Description("目录可含未注册 handler 的技能，但 attach 返回 HandlerMissing 且不创建 state。")]
        public void Attach_MissingHandler_ReturnsHandlerMissingWithoutState()
        {
            var def = Skill("Ghost", SkillCategory.Active, 0);
            SkillRunner runner = BuildRunner(def, registerHandler: false);
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.HandlerMissing,
                runner.Attach(owner, "Ghost").Status,
                "未注册 handler 的目录行可以存在但不得 attach。");
            Assert.IsFalse(runner.TryGetState(owner, "Ghost", out _),
                "HandlerMissing 不得创建任何 state。");
        }

        [Test]
        [Description("合法 Attach 创建 (owner,skillKey) 唯一 state。")]
        public void Attach_Success_CreatesState()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.IsFalse(state.IsRunning);
            Assert.AreEqual(0L, state.RunVersion);
            Assert.AreEqual(0L, state.NextReadyAtMs);
        }

        [Test]
        [Description("重复 attach 幂等成功并保留同一 state（不重复初始化）。")]
        public void Attach_Duplicate_IsIdempotentAndKeepsState()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(10, 20)).IsSuccess);

            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess, "重复 attach 应成功。");
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot after));
            Assert.AreEqual(1L, after.RunVersion, "重复 attach 不得重置 state。");
            Assert.AreEqual(0, handler.EffectCount, "attach 不得调用 handler。");
        }

        // ====================================================================
        // Activate：验证失败路径
        // ====================================================================

        [Test]
        [Description("Activate 未 attach 技能 → NotAttached。")]
        public void Activate_NotAttached_ReturnsNotAttached()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.NotAttached,
                runner.Activate(owner, "AlphaStrike", Plan(10, 20)).Status);
        }

        [Test]
        [Description("Activate 未知技能 key → UnknownSkillKey。")]
        public void Activate_UnknownSkillKey_ReturnsUnknownSkillKey()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.UnknownSkillKey,
                runner.Activate(owner, "NoSuchSkill", Plan(10, 20)).Status);
        }

        [Test]
        [Description("Activate Passive 技能 → UnsupportedCategory（不调度任何工作）。")]
        public void Activate_Passive_ReturnsUnsupportedCategory()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("StunPassive", SkillCategory.Passive, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.UnsupportedCategory,
                runner.Activate(owner, "StunPassive", Plan(10, 20)).Status);
            Step(runner.Scheduler, 1000);
            Assert.AreEqual(0, handler.EffectCount, "Passive 激活不得调度工作。");
        }

        [Test]
        [Description("非法激活计划（负延迟、complete<=effect）→ InvalidActivationPlan，零副作用且不消耗冷却。")]
        public void Activate_IllegalPlan_FailsWithZeroSideEffects()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 5000));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            var illegalPlans = new[]
            {
                Plan(-1, 0),
                Plan(0, -5),
                Plan(0, 0),
                Plan(200, 100),
                Plan(100, 100),
            };
            foreach (SkillActivationPlan plan in illegalPlans)
            {
                Assert.AreEqual(SkillOperationStatus.InvalidActivationPlan,
                    runner.Activate(owner, "AlphaStrike", plan).Status);
            }

            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.IsFalse(state.IsRunning, "非法计划不得进入 running。");
            Assert.AreEqual(0L, state.RunVersion, "非法计划不得递增 runVersion。");
            Assert.AreEqual(0L, state.NextReadyAtMs, "非法计划不得消耗冷却。");
            Assert.AreEqual(0, handler.EffectCount);
            Assert.AreEqual(0, handler.CompleteCount);
            Assert.AreEqual(0, handler.CancelCount);
        }

        [Test]
        [Description("Busy：同技能已有运行中激活 → Busy，且不替换既有时间线。")]
        public void Activate_WhileRunning_ReturnsBusy()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.Busy,
                runner.Activate(owner, "AlphaStrike", Plan(10, 20)).Status,
                "运行中再次激活应返回 Busy。");

            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.AreEqual(1L, state.RunVersion, "Busy 不得替换时间线。");

            Step(runner.Scheduler, 100);
            Assert.AreEqual(1, handler.EffectCount, "既有时间线的 effect 应正常执行。");
        }

        [Test]
        [Description("冷却：完成后同帧/同帧后激活 → OnCooldown；冷却就绪后可再次激活。")]
        public void Activate_OnCooldown_RejectsUntilReady()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 1000));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(50, 100)).IsSuccess);
            Step(runner.Scheduler, 100); // complete：运行结束，冷却未结束

            Assert.AreEqual(SkillOperationStatus.OnCooldown,
                runner.Activate(owner, "AlphaStrike", Plan(50, 100)).Status,
                "完成后同帧应处于冷却。");

            Step(runner.Scheduler, 999);
            Assert.AreEqual(SkillOperationStatus.OnCooldown,
                runner.Activate(owner, "AlphaStrike", Plan(50, 100)).Status,
                "冷却未结束时不应激活。");

            Step(runner.Scheduler, 1000);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(50, 100)).IsSuccess,
                "冷却就绪后应可再次激活。");
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.AreEqual(2L, state.RunVersion, "再次激活应递增 runVersion。");
        }

        [Test]
        [Description("调度器拒绝登记（冻结）→ InvalidState，激活零副作用且不消耗冷却。")]
        public void Activate_SchedulerRejects_RollsBackWithZeroSideEffects()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out BattleActionScheduler scheduler, out _,
                Skill("AlphaStrike", SkillCategory.Active, 5000));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            scheduler.Freeze();
            Assert.AreEqual(SkillOperationStatus.InvalidState,
                runner.Activate(owner, "AlphaStrike", Plan(10, 20)).Status,
                "调度器拒绝登记应返回 InvalidState。");

            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.IsFalse(state.IsRunning, "登记失败不得进入 running。");
            Assert.AreEqual(0L, state.RunVersion, "登记失败不得递增 runVersion。");
            Assert.AreEqual(0L, state.NextReadyAtMs, "登记失败不得消耗冷却。");
            Assert.AreEqual(0, handler.EffectCount);
            Assert.AreEqual(0, handler.CompleteCount);
        }

        // ====================================================================
        // Activate：成功路径与两节点时间线
        // ====================================================================
        [Test]
        [Description("成功激活写入 runVersion 与冷却，并保持 running/effectCommitted=false。")]
        public void Activate_Success_RecordsRunVersionAndCooldown()
        {
            BuildHandler(out SkillRunner runner, out BattleActionScheduler scheduler, out _,
                Skill("AlphaStrike", SkillCategory.Active, 1000));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            scheduler.BeginFrame(500);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.IsTrue(state.IsRunning);
            Assert.IsFalse(state.EffectCommitted);
            Assert.AreEqual(1L, state.RunVersion);
            Assert.AreEqual(1500L, state.NextReadyAtMs, "冷却应从激活时刻（500ms）起算。");
        }

        [Test]
        [Description("effect 先于 complete，且按 callback 当下 FrameNowMs 建上下文；跨 fixed step 到期执行。")]
        public void Activate_EffectBeforeComplete_AcrossFixedSteps()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(500, 1000)).IsSuccess);

            Step(runner.Scheduler, 500);
            Assert.AreEqual(1, handler.EffectCount, "effect 应在 500ms 到期。");
            Assert.AreEqual(0, handler.CompleteCount, "complete 未到期不应执行。");
            Assert.AreEqual(500L, handler.EffectContexts[0].BattleNowMs,
                "effect 上下文应使用 callback 当下的 FrameNowMs。");

            Step(runner.Scheduler, 1000);
            Assert.AreEqual(1, handler.CompleteCount, "complete 应在 1000ms 到期。");
            Assert.AreEqual(1000L, handler.CompleteContexts[0].BattleNowMs);
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.IsFalse(state.IsRunning, "complete 后运行应结束。");
            Assert.AreEqual(1L, state.RunVersion, "complete 不重置 runVersion。");
        }

        [Test]
        [Description("同刻 FIFO：两个技能同批 flush，按登记顺序 effect 先于各自 complete。")]
        public void Activate_SameTickFifo_TwoSkills()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0),
                Skill("BetaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "BetaStrike").IsSuccess);

            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 150)).IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "BetaStrike", Plan(100, 150)).IsSuccess);

            Step(runner.Scheduler, 150);

            CollectionAssert.AreEqual(new[]
            {
                "effect:7:AlphaStrike",
                "complete:7:AlphaStrike",
                "effect:7:BetaStrike",
                "complete:7:BetaStrike",
            }, handler.CallOrder, "同刻 flush 应按登记顺序 FIFO 执行。");
            Assert.AreEqual(150L, handler.EffectContexts[0].BattleNowMs,
                "effect 上下文时间戳应为 flush 当下 FrameNowMs。");
        }

        [Test]
        [Description("effect/complete 各自 exactly-once：重复 flush 不重复执行。")]
        public void Activate_ExactlyOnce_RepeatedFlush()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Step(runner.Scheduler, 100);
            Step(runner.Scheduler, 150);
            Step(runner.Scheduler, 200);
            Step(runner.Scheduler, 300);

            Assert.AreEqual(1, handler.EffectCount, "effect 应恰好执行一次。");
            Assert.AreEqual(1, handler.CompleteCount, "complete 应恰好执行一次。");
        }

        [Test]
        [Description("effect handler 抛异常：取消 completion、结束 running 并继续抛出，complete 不再执行。")]
        public void Activate_EffectHandlerThrows_CancelsCompletionAndEndsRunning()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            handler.ThrowOnEffect = true;
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Assert.Throws<InvalidOperationException>(() => Step(runner.Scheduler, 100),
                "handler 异常应继续抛出。");

            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.IsFalse(state.IsRunning, "effect 异常后运行应结束。");

            Step(runner.Scheduler, 200);
            Assert.AreEqual(0, handler.CompleteCount, "effect 异常应取消 completion。");
            Assert.AreEqual(SkillOperationStatus.NotRunning,
                runner.Cancel(owner, "AlphaStrike").Status,
                "effect 异常结束后不再有运行中激活。");
        }

        // ====================================================================
        // Cancel
        // ====================================================================

        [Test]
        [Description("取消未 attach 技能 → NotAttached。")]
        public void Cancel_NotAttached_ReturnsNotAttached()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.NotAttached,
                runner.Cancel(owner, "AlphaStrike").Status);
        }

        [Test]
        [Description("取消未运行激活 → NotRunning（重复取消无额外 handler 调用）。")]
        public void Cancel_NotRunning_ReturnsNotRunning()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            Assert.AreEqual(SkillOperationStatus.NotRunning,
                runner.Cancel(owner, "AlphaStrike").Status);
        }

        [Test]
        [Description("effect 前取消：handler 收到 effectCommitted=false，未执行回调被取消，冷却保留。")]
        public void Cancel_BeforeEffect_ReportsNotCommittedAndKeepsCooldown()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 1000));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            runner.Scheduler.BeginFrame(10);
            Assert.IsTrue(runner.Cancel(owner, "AlphaStrike").IsSuccess);

            Assert.AreEqual(1, handler.CancelCount);
            Assert.IsFalse(handler.Cancels[0].EffectCommitted, "effect 未提交应报告 pending。");
            Assert.AreEqual(10L, handler.Cancels[0].Context.BattleNowMs);

            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.IsFalse(state.IsRunning);
            Assert.AreEqual(1L, state.RunVersion, "取消保留 runVersion。");
            Assert.AreEqual(1000L, state.NextReadyAtMs, "取消保留冷却，不退款。");

            Step(runner.Scheduler, 200);
            Assert.AreEqual(0, handler.EffectCount, "取消后 effect 不得执行。");
            Assert.AreEqual(0, handler.CompleteCount, "取消后 complete 不得执行。");
        }

        [Test]
        [Description("effect 后取消：handler 收到 effectCommitted=true，complete 被取消，已提交外部效果不回滚。")]
        public void Cancel_AfterEffect_ReportsCommittedAndCancelsCompletion()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Step(runner.Scheduler, 100);
            Assert.AreEqual(1, handler.EffectCount);

            Assert.IsTrue(runner.Cancel(owner, "AlphaStrike").IsSuccess);

            Assert.AreEqual(1, handler.CancelCount);
            Assert.IsTrue(handler.Cancels[0].EffectCommitted, "effect 已提交应报告 committed。");

            Step(runner.Scheduler, 200);
            Assert.AreEqual(0, handler.CompleteCount, "取消后 complete 不得执行。");
        }

        [Test]
        [Description("重复取消：第二次返回 NotRunning，handler.Cancel 仅调用一次。")]
        public void Cancel_Twice_NoExtraHandlerCall()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Assert.IsTrue(runner.Cancel(owner, "AlphaStrike").IsSuccess);
            Assert.AreEqual(SkillOperationStatus.NotRunning,
                runner.Cancel(owner, "AlphaStrike").Status);
            Assert.AreEqual(1, handler.CancelCount, "重复取消不得再次调用 handler.Cancel。");
        }

        // ====================================================================
        // Detach / ClearOwner / Clear / Dispose
        // ====================================================================

        [Test]
        [Description("Detach 移除 state；运行中先取消一次再移除。")]
        public void Detach_RemovesState_AndCancelsRunningOnce()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            Step(runner.Scheduler, 100);
            Assert.IsTrue(runner.Detach(owner, "AlphaStrike").IsSuccess);

            Assert.AreEqual(1, handler.CancelCount, "运行中 detach 应先取消一次。");
            Assert.IsTrue(handler.Cancels[0].EffectCommitted);
            Assert.IsFalse(runner.TryGetState(owner, "AlphaStrike", out _), "detach 后 state 应移除。");

            Step(runner.Scheduler, 200);
            Assert.AreEqual(0, handler.CompleteCount, "detach 后 complete 不得执行。");
        }

        [Test]
        [Description("Detach 未 attach → NotAttached。")]
        public void Detach_NotAttached_ReturnsNotAttached()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.AreEqual(SkillOperationStatus.NotAttached,
                runner.Detach(owner, "AlphaStrike").Status);
        }

        [Test]
        [Description("ClearOwner 取消全部运行激活并清除 state，但保留租期（可免注册重新 attach）。")]
        public void ClearOwner_CancelsAllAndKeepsLease()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0),
                Skill("BetaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "BetaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "BetaStrike", Plan(100, 200)).IsSuccess);

            Step(runner.Scheduler, 100);
            Assert.IsTrue(runner.ClearOwner(owner).IsSuccess);

            Assert.AreEqual(2, handler.CancelCount, "两个运行中激活各取消一次。");
            Assert.IsFalse(runner.TryGetState(owner, "AlphaStrike", out _));
            Assert.IsFalse(runner.TryGetState(owner, "BetaStrike", out _));

            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess,
                "ClearOwner 后租期仍在，可直接重新 attach。");
        }

        [Test]
        [Description("Clear 清空全部 owner 的 state 与租期，重复调用幂等。")]
        public void Clear_EmptiesAllAndIsIdempotent()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var ownerA = new SkillOwnerHandle(RuntimeId, 1);
            var ownerB = new SkillOwnerHandle(RuntimeId + 1, 1);
            Assert.IsTrue(runner.RegisterOwner(ownerA).IsSuccess);
            Assert.IsTrue(runner.Attach(ownerA, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(ownerA, "AlphaStrike", Plan(100, 200)).IsSuccess);
            Assert.IsTrue(runner.RegisterOwner(ownerB).IsSuccess);
            Assert.IsTrue(runner.Attach(ownerB, "AlphaStrike").IsSuccess);

            Assert.IsTrue(runner.Clear().IsSuccess);
            Assert.IsTrue(runner.Clear().IsSuccess, "重复 Clear 应幂等成功。");

            Assert.AreEqual(1, handler.CancelCount, "仅运行中激活被取消一次。");
            Assert.IsFalse(runner.TryGetState(ownerA, "AlphaStrike", out _));
            Assert.IsFalse(runner.TryGetState(ownerB, "AlphaStrike", out _));

            Assert.IsTrue(runner.RegisterOwner(ownerA).IsSuccess, "Clear 后可重新注册。");
        }

        [Test]
        [Description("批量清理按 owner runtimeId、skillKey ordinal 稳定顺序调用 handler.Cancel。")]
        public void Clear_BatchCancelOrder_IsStable()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("A", SkillCategory.Active, 0),
                Skill("B", SkillCategory.Active, 0),
                Skill("C", SkillCategory.Active, 0));
            var ownerLow = new SkillOwnerHandle(3, 1);
            var ownerHigh = new SkillOwnerHandle(5, 1);
            foreach (var owner in new[] { ownerLow, ownerHigh })
            {
                Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            }

            Assert.IsTrue(runner.Attach(ownerLow, "B").IsSuccess);
            Assert.IsTrue(runner.Attach(ownerLow, "A").IsSuccess);
            Assert.IsTrue(runner.Attach(ownerHigh, "C").IsSuccess);
            Assert.IsTrue(runner.Activate(ownerLow, "B", Plan(100, 200)).IsSuccess);
            Assert.IsTrue(runner.Activate(ownerLow, "A", Plan(100, 200)).IsSuccess);
            Assert.IsTrue(runner.Activate(ownerHigh, "C", Plan(100, 200)).IsSuccess);

            Step(runner.Scheduler, 100);
            int effectCalls = handler.CallOrder.Count;
            Assert.IsTrue(runner.Clear().IsSuccess);

            Assert.AreEqual(3, handler.CallOrder.Count - effectCalls,
                "三个运行中激活应各取消一次。");
            CollectionAssert.AreEqual(new[]
            {
                "cancel:3:A:committed",
                "cancel:3:B:committed",
                "cancel:5:C:committed",
            }, handler.CallOrder.GetRange(effectCalls, 3),
                "批量清理应按 runtimeId、skillKey ordinal 稳定顺序。");
        }

        [Test]
        [Description("Dispose 清理全部状态；之后所有操作返回 Disposed，重复 Dispose 幂等。")]
        public void Dispose_RejectsOperationsAndIsIdempotent()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);

            runner.Dispose();
            runner.Dispose();
            Assert.AreEqual(1, handler.CancelCount, "Dispose 应取消运行中激活一次。");
            Assert.AreEqual(SkillOperationStatus.Disposed, runner.RegisterOwner(owner).Status);
            Assert.AreEqual(SkillOperationStatus.Disposed, runner.UnregisterOwner(owner).Status);
            Assert.AreEqual(SkillOperationStatus.Disposed, runner.Attach(owner, "AlphaStrike").Status);
            Assert.AreEqual(SkillOperationStatus.Disposed, runner.Activate(owner, "AlphaStrike", Plan(10, 20)).Status);
            Assert.AreEqual(SkillOperationStatus.Disposed, runner.Cancel(owner, "AlphaStrike").Status);
            Assert.AreEqual(SkillOperationStatus.Disposed, runner.Detach(owner, "AlphaStrike").Status);
            Assert.AreEqual(SkillOperationStatus.Disposed, runner.ClearOwner(owner).Status);
            Assert.AreEqual(SkillOperationStatus.Disposed, runner.Clear().Status);
            Assert.IsFalse(runner.TryGetState(owner, "AlphaStrike", out _), "Dispose 后查询应返回 false。");
        }

        // ====================================================================
        // TryGetState 生命周期
        // ====================================================================

        [Test]
        [Description("TryGetState 覆盖 attach→activate→effect→complete→detach 全生命周期。")]
        public void TryGetState_ReflectsLifecycle()
        {
            SkillRunner runner = BuildRunner(Skill("AlphaStrike", SkillCategory.Active, 500));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);

            Assert.IsFalse(runner.TryGetState(owner, "AlphaStrike", out _), "未 attach 应返回 false。");

            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot attached));
            Assert.IsFalse(attached.IsRunning);
            Assert.AreEqual(0L, attached.RunVersion);

            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot running));
            Assert.IsTrue(running.IsRunning);
            Assert.AreEqual(1L, running.RunVersion);
            Assert.IsFalse(running.EffectCommitted);
            Assert.AreEqual(500L, running.NextReadyAtMs);

            Step(runner.Scheduler, 100);
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot effected));
            Assert.IsTrue(effected.IsRunning);
            Assert.IsTrue(effected.EffectCommitted, "effect 提交后快照应反映。");

            Step(runner.Scheduler, 200);
            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot completed));
            Assert.IsFalse(completed.IsRunning, "complete 后运行结束。");
            Assert.AreEqual(1L, completed.RunVersion);
            Assert.AreEqual(500L, completed.NextReadyAtMs, "complete 保留冷却。");

            Assert.IsTrue(runner.Detach(owner, "AlphaStrike").IsSuccess);
            Assert.IsFalse(runner.TryGetState(owner, "AlphaStrike", out _), "detach 后应返回 false。");
        }

        // ====================================================================
        // cancel 后新 runVersion 与迟到 callback
        // ====================================================================

        [Test]
        [Description("cancel 后重新激活获得新 runVersion；旧版本 complete 永不执行，两代激活互不干扰。")]
        public void CancelThenReactivate_NewRunVersion_NoStaleCallback()
        {
            RecordingHandler handler = BuildHandler(out SkillRunner runner, out _, out _,
                Skill("AlphaStrike", SkillCategory.Active, 0));
            var owner = new SkillOwnerHandle(RuntimeId, 1);
            Assert.IsTrue(runner.RegisterOwner(owner).IsSuccess);
            Assert.IsTrue(runner.Attach(owner, "AlphaStrike").IsSuccess);

            // 第一代：effect 提交后取消。
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);
            Step(runner.Scheduler, 100);
            Assert.IsTrue(runner.Cancel(owner, "AlphaStrike").IsSuccess);

            // 第二代：新 runVersion=2，完整跑完。
            Assert.IsTrue(runner.Activate(owner, "AlphaStrike", Plan(100, 200)).IsSuccess);
            Step(runner.Scheduler, 200);
            Step(runner.Scheduler, 300);

            CollectionAssert.AreEqual(new[]
            {
                "effect:7:AlphaStrike",
                "cancel:7:AlphaStrike:committed",
                "effect:7:AlphaStrike",
                "complete:7:AlphaStrike",
            }, handler.CallOrder,
                "第一代 complete 必须被取消，第二代以新 runVersion 独立执行。");

            Assert.IsTrue(runner.TryGetState(owner, "AlphaStrike", out SkillStateSnapshot state));
            Assert.AreEqual(2L, state.RunVersion, "取消后新激活应递增 runVersion。");
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>推进调度器到指定帧时间戳并 flush 到期动作。</summary>
        private static void Step(BattleActionScheduler scheduler, long frameNowMs)
        {
            scheduler.BeginFrame(frameNowMs);
            scheduler.FlushDueActions(1);
        }

        /// <summary>构造单技能目录并注册 handler，返回 Runner（不注册 owner）。</summary>
        private static SkillRunner BuildRunner(
            SkillDefinitionSnapshot def, bool registerHandler = true)
            => BuildRunner(new[] { def }, registerHandler);

        /// <summary>构造目录并注册 handler，返回 Runner（不注册 owner）。</summary>
        private static SkillRunner BuildRunner(
            SkillDefinitionSnapshot[] defs, bool registerHandler = true)
        {
            BuildHandler(out SkillRunner runner, out _, out _, defs, registerHandler);
            return runner;
        }

        /// <summary>构造目录+handler+Runner，返回 RecordingHandler 并暴露 scheduler。</summary>
        private static RecordingHandler BuildHandler(
            out SkillRunner runner,
            out BattleActionScheduler scheduler,
            out SkillHandlerRegistry registry,
            params SkillDefinitionSnapshot[] defs)
            => BuildHandler(out runner, out scheduler, out registry, defs, registerHandler: true);

        /// <summary>构造目录+handler+Runner，返回 RecordingHandler 并暴露 scheduler。</summary>
        private static RecordingHandler BuildHandler(
            out SkillRunner runner,
            out BattleActionScheduler scheduler,
            out SkillHandlerRegistry registry,
            SkillDefinitionSnapshot[] defs,
            bool registerHandler)
        {
            var catalog = new SkillCatalogSnapshot(defs);
            scheduler = new BattleActionScheduler();
            registry = new SkillHandlerRegistry();
            var handler = new RecordingHandler();
            if (registerHandler)
            {
                for (int i = 0; i < defs.Length; i++)
                {
                    registry.Register(defs[i].HandlerKey, handler);
                }
            }

            runner = new SkillRunner(catalog, registry, scheduler);
            return handler;
        }

        /// <summary>构造合法 Skill 定义（handlerKey=key）。</summary>
        private static SkillDefinitionSnapshot Skill(
            string key, SkillCategory category, long cooldownMs)
        {
            return new SkillDefinitionSnapshot(key, category, cooldownMs, key, null, null);
        }

        /// <summary>构造单定义目录。</summary>
        private static SkillCatalogSnapshot SingleDef(SkillDefinitionSnapshot def)
            => new SkillCatalogSnapshot(new[] { def });

        /// <summary>构造激活计划。</summary>
        private static SkillActivationPlan Plan(long effectDelayMs, long completeDelayMs)
            => new SkillActivationPlan(effectDelayMs, completeDelayMs);

        /// <summary>记录 ISkillHandler 调用序列、上下文与提交标志的测试 handler。</summary>
        private sealed class RecordingHandler : ISkillHandler
        {
            internal readonly List<string> CallOrder = new List<string>();
            internal readonly List<SkillActivationContext> EffectContexts = new List<SkillActivationContext>();
            internal readonly List<SkillActivationContext> CompleteContexts = new List<SkillActivationContext>();
            internal readonly List<(SkillActivationContext Context, bool EffectCommitted)> Cancels =
                new List<(SkillActivationContext Context, bool EffectCommitted)>();

            /// <summary>Effect 抛异常开关（测试 handler 异常路径）。</summary>
            internal bool ThrowOnEffect;

            internal int EffectCount => EffectContexts.Count;
            internal int CompleteCount => CompleteContexts.Count;
            internal int CancelCount => Cancels.Count;

            public void Effect(SkillActivationContext context)
            {
                EffectContexts.Add(context);
                CallOrder.Add($"effect:{context.Owner.RuntimeId}:{context.SkillKey}");
                if (ThrowOnEffect)
                {
                    throw new InvalidOperationException("RecordingHandler effect 抛出测试异常。");
                }
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
