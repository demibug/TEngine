using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Skill
{
    // ============================================================================
    // 任务 2.4/2.5：SkillHandlerRegistry —— 严格 handlerKey 注册与查询测试
    // ----------------------------------------------------------------------------
    // 验证内容（design.md 决策 1/4 / specs/combat-skill-lifecycle/spec.md）：
    //   1. 拒绝空 key、null handler 与重复注册（严格注册，不做覆盖）。
    //   2. 未注册查询返回 false（不抛异常、不 fallback）。
    //   3. handler 契约接收不可变上下文（Recording handler 验证三个调用点签名）。
    // ============================================================================

    [TestFixture]
    internal class SkillHandlerRegistryTests
    {
        [Test]
        [Description("注册后按 handlerKey 精确查询命中同一实例。")]
        public void RegisterAndLookup_ReturnsExactHandler()
        {
            var registry = new SkillHandlerRegistry();
            var handler = new RecordingHandler();

            registry.Register("SoulCapture", handler);

            Assert.IsTrue(registry.TryGet("SoulCapture", out ISkillHandler found));
            Assert.AreSame(handler, found);
        }

        [Test]
        [Description("重复注册被拒绝且不覆盖首个 handler。")]
        public void DuplicateRegistration_IsRejectedWithoutReplacingFirstHandler()
        {
            var registry = new SkillHandlerRegistry();
            var first = new RecordingHandler();
            registry.Register("SoulCapture", first);

            Assert.Throws<InvalidOperationException>(
                () => registry.Register("SoulCapture", new RecordingHandler()));
            Assert.IsTrue(registry.TryGet("SoulCapture", out ISkillHandler found));
            Assert.AreSame(first, found);
        }

        [Test]
        [Description("空 key 与 null handler 均被拒绝。")]
        public void EmptyKeyAndNullHandler_AreRejected()
        {
            var registry = new SkillHandlerRegistry();

            Assert.Throws<ArgumentException>(() => registry.Register("", new RecordingHandler()));
            Assert.Throws<ArgumentException>(() => registry.Register(null, new RecordingHandler()));
            Assert.Throws<ArgumentNullException>(() => registry.Register("SoulCapture", null));
        }

        [Test]
        [Description("未注册查询返回 false，不抛异常、不 fallback。")]
        public void MissingLookup_ReturnsFalseWithoutFallback()
        {
            var registry = new SkillHandlerRegistry();

            Assert.IsFalse(registry.TryGet("NotRegistered", out ISkillHandler missing));
            Assert.IsNull(missing);
        }

        [Test]
        [Description("Recording handler 验证 ISkillHandler 三调用点：Effect、Complete 与带提交状态的 Cancel。")]
        public void RecordingHandler_ReceivesImmutableContext()
        {
            var registry = new SkillHandlerRegistry();
            var handler = new RecordingHandler();
            var owner = new SkillOwnerHandle(runtimeId: 1, generation: 2);
            var context = new SkillActivationContext(
                owner, "SoulCapture", battleNowMs: 1234);
            registry.Register("SoulCapture", handler);

            Assert.IsTrue(registry.TryGet("SoulCapture", out ISkillHandler found));

            found.Effect(context);
            found.Complete(context);
            found.Cancel(context, effectCommitted: true);

            Assert.AreEqual(1, handler.EffectCount);
            Assert.AreEqual(1, handler.CompleteCount);
            Assert.AreEqual(1, handler.CancelCount);
            Assert.AreSame(context, handler.LastContext);
            Assert.AreEqual(owner, handler.LastOwner, "上下文应保留 owner 句柄");
            Assert.AreEqual("SoulCapture", handler.LastContext.SkillKey);
            Assert.AreEqual(1234L, handler.LastContext.BattleNowMs);
            Assert.IsTrue(handler.CancelEffectCommitted, "Cancel 应携带 effectCommitted=true");
        }

        [Test]
        [Description("激活计划允许承载非法延迟，由 Activate 统一校验。")]
        public void SkillActivationPlan_AllowsIllegalDelays()
        {
            var plan = new SkillActivationPlan(
                effectDelayMs: -1, completeDelayMs: 0);

            Assert.AreEqual(-1, plan.EffectDelayMs, "非法 effect 偏移允许承载");
            Assert.AreEqual(0, plan.CompleteDelayMs);
        }

        // ====================================================================
        // 测试辅助
        // ====================================================================

        /// <summary>记录 ISkillHandler 调用次数与上下文的测试 handler。</summary>
        private sealed class RecordingHandler : ISkillHandler
        {
            internal int EffectCount { get; private set; }
            internal int CompleteCount { get; private set; }
            internal int CancelCount { get; private set; }
            internal SkillActivationContext LastContext { get; private set; }
            internal SkillOwnerHandle LastOwner { get; private set; }
            internal bool CancelEffectCommitted { get; private set; }

            public void Effect(SkillActivationContext context)
            {
                EffectCount++;
                LastContext = context;
                LastOwner = context.Owner;
            }

            public void Complete(SkillActivationContext context)
            {
                CompleteCount++;
                LastContext = context;
                LastOwner = context.Owner;
            }

            public void Cancel(SkillActivationContext context, bool effectCommitted)
            {
                CancelCount++;
                LastContext = context;
                LastOwner = context.Owner;
                CancelEffectCommitted = effectCommitted;
            }
        }
    }
}
