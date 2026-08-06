using NUnit.Framework;

namespace GameBattle.Tests.EditMode
{
    /// <summary>
    /// BattleModuleState 合法迁移表测试（task 2.5）。
    /// </summary>
    /// <remarks>
    /// <para>验证迁移规则（决策 0.7 + specs/battle-runtime-lifecycle/spec.md）：</para>
    /// <list type="bullet">
    /// <item>Idle → Entering → Running → Settling（正常生命周期）</item>
    /// <item>Settling → Restarting → Entering（重开循环）</item>
    /// <item>活动状态（Entering/Running/Settling/Restarting）→ Exiting → Idle（退出）</item>
    /// <item>Idle → Exiting（空闲幂等退出）</item>
    /// <item>任意状态 → Faulted（意外失败）</item>
    /// <item>Faulted → Idle（清理后恢复）</item>
    /// </list>
    /// <para>同时验证关键禁止迁移：跳过阶段、从 Faulted 直接进入活动状态、
    /// 从 Exiting 跳到非 Idle 状态等。</para>
    /// </remarks>
    [TestFixture]
    internal class BattleModuleStateTests
    {
        // ====================================================================
        // 允许的迁移测试
        // ====================================================================

        [Test]
        [Description("Idle → Entering：开始战斗时从空闲进入加载。")]
        public void Idle_To_Entering_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Idle, BattleModuleState.Entering),
                "Idle → Entering 是 Start 命令的合法迁移。");
        }

        [Test]
        [Description("Entering → Running：加载完成进入运行。")]
        public void Entering_To_Running_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Entering, BattleModuleState.Running),
                "Entering → Running 是加载完成的合法迁移。");
        }

        [Test]
        [Description("Running → Settling：结果冻结后进入结算。")]
        public void Running_To_Settling_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Running, BattleModuleState.Settling),
                "Running → Settling 是结果冻结的合法迁移。");
        }

        [Test]
        [Description("Settling → Restarting：结算后请求再来一局。")]
        public void Settling_To_Restarting_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Settling, BattleModuleState.Restarting),
                "Settling → Restarting 是 Restart 命令的合法迁移。");
        }

        [Test]
        [Description("Restarting → Entering：重开时销毁旧运行时并创建新运行时。")]
        public void Restarting_To_Entering_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Restarting, BattleModuleState.Entering),
                "Restarting → Entering 是新局加载的合法迁移。");
        }

        [Test]
        [Description("Entering → Exiting：加载中收到退出请求。")]
        public void Entering_To_Exiting_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Entering, BattleModuleState.Exiting),
                "活动状态 Entering → Exiting 是合法迁移。");
        }

        [Test]
        [Description("Running → Exiting：运行中收到退出请求。")]
        public void Running_To_Exiting_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Running, BattleModuleState.Exiting),
                "活动状态 Running → Exiting 是合法迁移。");
        }

        [Test]
        [Description("Settling → Exiting：结算中收到退出请求。")]
        public void Settling_To_Exiting_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Settling, BattleModuleState.Exiting),
                "活动状态 Settling → Exiting 是合法迁移。");
        }

        [Test]
        [Description("Restarting → Exiting：重开中收到退出请求。")]
        public void Restarting_To_Exiting_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Restarting, BattleModuleState.Exiting),
                "活动状态 Restarting → Exiting 是合法迁移。");
        }

        [Test]
        [Description("Idle → Exiting：空闲状态下退出（幂等，无活动运行时）。")]
        public void Idle_To_Exiting_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Idle, BattleModuleState.Exiting),
                "Idle → Exiting 是空闲退出的合法迁移（Exit 幂等）。");
        }

        [Test]
        [Description("Exiting → Idle：退出完成回到空闲。")]
        public void Exiting_To_Idle_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Exiting, BattleModuleState.Idle),
                "Exiting → Idle 是退出完成的合法迁移。");
        }

        [Test]
        [Description("Idle → Faulted：空闲状态意外失败。")]
        public void Idle_To_Faulted_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Idle, BattleModuleState.Faulted),
                "任意状态 → Faulted：Idle → Faulted 是合法迁移。");
        }

        [Test]
        [Description("Entering → Faulted：加载中意外失败。")]
        public void Entering_To_Faulted_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Entering, BattleModuleState.Faulted),
                "任意状态 → Faulted：Entering → Faulted 是合法迁移。");
        }

        [Test]
        [Description("Running → Faulted：运行中意外失败。")]
        public void Running_To_Faulted_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Running, BattleModuleState.Faulted),
                "任意状态 → Faulted：Running → Faulted 是合法迁移。");
        }

        [Test]
        [Description("Settling → Faulted：结算中意外失败。")]
        public void Settling_To_Faulted_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Settling, BattleModuleState.Faulted),
                "任意状态 → Faulted：Settling → Faulted 是合法迁移。");
        }

        [Test]
        [Description("Restarting → Faulted：重开中意外失败。")]
        public void Restarting_To_Faulted_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Restarting, BattleModuleState.Faulted),
                "任意状态 → Faulted：Restarting → Faulted 是合法迁移。");
        }

        [Test]
        [Description("Exiting → Faulted：退出过程中意外失败。")]
        public void Exiting_To_Faulted_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Exiting, BattleModuleState.Faulted),
                "任意状态 → Faulted：Exiting → Faulted 是合法迁移。");
        }

        [Test]
        [Description("Faulted → Idle：清理完成后从故障恢复到空闲。")]
        public void Faulted_To_Idle_IsAllowed()
        {
            Assert.IsTrue(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Faulted, BattleModuleState.Idle),
                "Faulted → Idle 是清理后恢复的合法迁移。");
        }

        // ====================================================================
        // 关键禁止迁移测试
        // ====================================================================

        [Test]
        [Description("Idle → Running 禁止：不能跳过 Entering 直接运行。")]
        public void Idle_To_Running_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Idle, BattleModuleState.Running),
                "Idle → Running 禁止：必须经过 Entering 加载阶段。");
        }

        [Test]
        [Description("Idle → Settling 禁止：不能跳过加载和运行直接结算。")]
        public void Idle_To_Settling_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Idle, BattleModuleState.Settling),
                "Idle → Settling 禁止：必须经过 Entering → Running。");
        }

        [Test]
        [Description("Idle → Restarting 禁止：空闲状态不能直接重开。")]
        public void Idle_To_Restarting_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Idle, BattleModuleState.Restarting),
                "Idle → Restarting 禁止：Restart 只允许 Settling 状态。");
        }

        [Test]
        [Description("Settling → Idle 禁止：不能从结算直接跳到空闲，必须经过 Exiting。")]
        public void Settling_To_Idle_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Settling, BattleModuleState.Idle),
                "Settling → Idle 禁止：必须经过 Exiting → Idle。");
        }

        [Test]
        [Description("Settling → Running 禁止：不能从结算跳回运行。")]
        public void Settling_To_Running_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Settling, BattleModuleState.Running),
                "Settling → Running 禁止：结算后不能直接回到运行。");
        }

        [Test]
        [Description("Settling → Entering 禁止：不能从结算直接进入加载（必须经过 Restarting）。")]
        public void Settling_To_Entering_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Settling, BattleModuleState.Entering),
                "Settling → Entering 禁止：重开必须经过 Restarting。");
        }

        [Test]
        [Description("Running → Entering 禁止：不能从运行跳回加载。")]
        public void Running_To_Entering_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Running, BattleModuleState.Entering),
                "Running → Entering 禁止：不能从运行跳回加载。");
        }

        [Test]
        [Description("Running → Restarting 禁止：运行中不能直接重开（Restart 只允许 Settling）。")]
        public void Running_To_Restarting_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Running, BattleModuleState.Restarting),
                "Running → Restarting 禁止：Restart 只允许 Settling 状态。");
        }

        [Test]
        [Description("Entering → Settling 禁止：不能跳过 Running 直接结算。")]
        public void Entering_To_Settling_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Entering, BattleModuleState.Settling),
                "Entering → Settling 禁止：必须经过 Running。");
        }

        [Test]
        [Description("Entering → Restarting 禁止：加载中不能重开。")]
        public void Entering_To_Restarting_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Entering, BattleModuleState.Restarting),
                "Entering → Restarting 禁止：Restart 只允许 Settling 状态。");
        }

        [Test]
        [Description("Exiting → Entering 禁止：退出中不能直接开始新战斗。")]
        public void Exiting_To_Entering_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Exiting, BattleModuleState.Entering),
                "Exiting → Entering 禁止：退出中不能开始新战斗。");
        }

        [Test]
        [Description("Exiting → Running 禁止：退出中不能跳到运行。")]
        public void Exiting_To_Running_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Exiting, BattleModuleState.Running),
                "Exiting → Running 禁止：退出中不能跳到运行。");
        }

        [Test]
        [Description("Exiting → Settling 禁止：退出中不能跳到结算。")]
        public void Exiting_To_Settling_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Exiting, BattleModuleState.Settling),
                "Exiting → Settling 禁止：退出中不能跳到结算。");
        }

        [Test]
        [Description("Exiting → Restarting 禁止：退出中不能跳到重开。")]
        public void Exiting_To_Restarting_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Exiting, BattleModuleState.Restarting),
                "Exiting → Restarting 禁止：退出中不能跳到重开。");
        }

        [Test]
        [Description("Faulted → Entering 禁止：故障状态必须先清理回到 Idle 才能开始新战斗。")]
        public void Faulted_To_Entering_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Faulted, BattleModuleState.Entering),
                "Faulted → Entering 禁止：必须先 Faulted → Idle 清理后才能开始。");
        }

        [Test]
        [Description("Faulted → Running 禁止：故障状态不能直接跳到运行。")]
        public void Faulted_To_Running_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Faulted, BattleModuleState.Running),
                "Faulted → Running 禁止：必须先清理回到 Idle。");
        }

        [Test]
        [Description("Faulted → Settling 禁止：故障状态不能直接跳到结算。")]
        public void Faulted_To_Settling_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Faulted, BattleModuleState.Settling),
                "Faulted → Settling 禁止：必须先清理回到 Idle。");
        }

        [Test]
        [Description("Faulted → Restarting 禁止：故障状态不能直接重开。")]
        public void Faulted_To_Restarting_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Faulted, BattleModuleState.Restarting),
                "Faulted → Restarting 禁止：必须先清理回到 Idle。");
        }

        [Test]
        [Description("Faulted → Exiting 禁止：故障状态必须先清理回到 Idle，再从 Idle → Exiting。")]
        public void Faulted_To_Exiting_IsForbidden()
        {
            Assert.IsFalse(
                BattleModuleStateTransitions.CanTransition(BattleModuleState.Faulted, BattleModuleState.Exiting),
                "Faulted → Exiting 禁止：必须先清理回到 Idle。");
        }

        // ====================================================================
        // 自迁移禁止测试
        // ====================================================================

        [Test]
        [Description("自迁移一律禁止：状态机不靠自迁移表达幂等。")]
        public void SelfTransitions_AreForbidden()
        {
            // 所有状态的自迁移都应禁止。
            var allStates = new[]
            {
                BattleModuleState.Idle,
                BattleModuleState.Entering,
                BattleModuleState.Running,
                BattleModuleState.Settling,
                BattleModuleState.Restarting,
                BattleModuleState.Exiting,
                BattleModuleState.Faulted,
            };

            foreach (var state in allStates)
            {
                Assert.IsFalse(
                    BattleModuleStateTransitions.CanTransition(state, state),
                    $"{state} → {state} 自迁移应禁止。");
            }
        }

        // ====================================================================
        // 扩展方法测试
        // ====================================================================

        [Test]
        [Description("扩展方法 CanTransitionTo 与静态方法 CanTransition 结果一致。")]
        public void ExtensionMethod_Matches_StaticMethod()
        {
            // 抽查几条典型迁移，确认扩展方法与静态方法行为一致。
            Assert.IsTrue(
                BattleModuleState.Idle.CanTransitionTo(BattleModuleState.Entering),
                "扩展方法：Idle → Entering 应允许。");
            Assert.IsFalse(
                BattleModuleState.Idle.CanTransitionTo(BattleModuleState.Running),
                "扩展方法：Idle → Running 应禁止。");
            Assert.IsTrue(
                BattleModuleState.Faulted.CanTransitionTo(BattleModuleState.Idle),
                "扩展方法：Faulted → Idle 应允许。");
            Assert.IsFalse(
                BattleModuleState.Faulted.CanTransitionTo(BattleModuleState.Entering),
                "扩展方法：Faulted → Entering 应禁止。");
        }
    }
}
