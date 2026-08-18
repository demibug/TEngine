using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Runtime
{
    // ============================================================================
    // add-deterministic-buff-runtime task 1.2/1.3：BattleActionScheduler 到期动作
    // exactly-once 行为锁定
    // ----------------------------------------------------------------------------
    // 缺陷表征（task 1.2）：
    //   BattleActionScheduler.FlushDueActions 在执行循环中先调用 item.MarkExecuted()
    //   再调用 item.Invoke()；而 ScheduledAction.Invoke 的守卫是
    //   `if (Cancelled || Executed) return;`，因此 MarkExecuted 之后 Invoke 立即返回，
    //   到期 callback 永远不会执行（Buff duration 强依赖该公共能力，静默失效）。
    //
    // 本测试先建立表征基线：
    //   - 到期前不执行、到期边界（dueAtMs <= FrameNowMs）执行一次
    //   - 取消后不执行（含同一步 flush 内被前一回调取消）
    //   - 同到期时间按注册顺序 FIFO 执行
    //   - 同一回调恰好执行一次（重复 flush 不重复执行）
    //   - 冻结后不执行、冻结后拒绝新注册
    //
    // 修复（task 1.3）：最小调整执行顺序为先 Invoke 再 MarkExecuted，不改变
    // BattleSimulation 的 BattleUpdatePhase 顺序，不重写攻击生命周期，不新增
    // owner/group API。本测试为纯逻辑 EditMode，不接触 Scene/FUI/资源。
    // ============================================================================

    [TestFixture]
    internal class BattleActionSchedulerTests
    {
        /// <summary>
        /// 记录到期回调调用序列与次数的观察器。
        /// </summary>
        private sealed class CallbackRecorder
        {
            public readonly List<string> Order = new List<string>();
            public int Count;

            public void Record(string name)
            {
                Order.Add(name);
                Count++;
            }
        }

        // ────────────────────────── 到期前不执行 ──────────────────────────

        [Test]
        [Description("到期前 flush 不执行回调：dueAtMs(100) > FrameNowMs(0)，回调保持挂起。")]
        public void DueAction_NotDueYet_IsNotExecutedAndStaysPending()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            scheduler.Schedule(100, () => recorder.Record("due"));

            int executed = scheduler.FlushDueActions(80);

            Assert.AreEqual(0, executed, "未到期时不应执行任何回调。");
            Assert.AreEqual(0, recorder.Count, "未到期时回调不应被调用。");

            scheduler.BeginFrame(100);
            executed = scheduler.FlushDueActions(80);
            Assert.AreEqual(1, executed, "到期后应执行一次。");
            Assert.AreEqual(1, recorder.Count, "到期后回调应恰好执行一次。");
        }

        // ────────────────────────── 到期执行且 exactly-once ──────────────────────────

        [Test]
        [Description("核心缺陷表征：到期 flush 后回调应恰好执行一次；重复 flush 不重复执行。"
            + " 修复前 FlushDueActions 先 MarkExecuted 再 Invoke，Invoke 的 Executed 守卫跳过回调，"
            + " 实际执行为 0 次（到期动作静默丢失）。")]
        public void DueAction_CallbackExecutesExactlyOnce_AfterFlush()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            scheduler.Schedule(100, () => recorder.Record("due"));

            scheduler.BeginFrame(100);
            int executed = scheduler.FlushDueActions(80);

            Assert.AreEqual(1, executed, "到期 flush 应报告执行 1 个回调。");
            Assert.AreEqual(1, recorder.Count, "到期回调应恰好执行一次（exactly-once）。");

            // 第二次 flush 不应再次执行已到期动作。
            scheduler.BeginFrame(200);
            int again = scheduler.FlushDueActions(80);
            Assert.AreEqual(0, again, "已执行的到期动作不应被再次 flush。");
            Assert.AreEqual(1, recorder.Count, "重复 flush 不得重复执行回调。");
        }

        // ────────────────────────── 到期边界（含） ──────────────────────────

        [Test]
        [Description("到期边界含等于：dueAtMs == FrameNowMs 时执行（dueAtMs <= FrameNowMs）。")]
        public void DueAction_AtBoundaryTimestamp_IsExecuted()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            scheduler.Schedule(100, () => recorder.Record("boundary"));

            scheduler.BeginFrame(100);
            int executed = scheduler.FlushDueActions(80);

            Assert.AreEqual(1, executed, "到期时间等于当前帧时间戳时应执行。");
            Assert.AreEqual(1, recorder.Count, "边界回调应执行一次。");
        }

        // ────────────────────────── 取消后不执行 ──────────────────────────

        [Test]
        [Description("主动取消后到期不执行：Cancel(handle) 后即使到达 dueAtMs 也不调用回调。")]
        public void CancelledDueAction_IsNotExecuted()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            ScheduledActionHandle handle = scheduler.Schedule(100, () => recorder.Record("cancelled"));

            scheduler.Cancel(handle);
            scheduler.BeginFrame(100);
            int executed = scheduler.FlushDueActions(80);

            Assert.AreEqual(0, executed, "已取消的到期动作不应执行。");
            Assert.AreEqual(0, recorder.Count, "取消后回调不得被调用。");
        }

        [Test]
        [Description("同一步 flush 内前一回调取消后一回调：后一到期动作不得执行（FIFO 中取消生效）。")]
        public void Cancel_DuringFlush_SuppressesLaterSibling()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            ScheduledActionHandle secondHandle = null;
            scheduler.Schedule(100, () =>
            {
                recorder.Record("first");
                scheduler.Cancel(secondHandle);
            });
            secondHandle = scheduler.Schedule(100, () => recorder.Record("second"));

            scheduler.BeginFrame(100);
            int executed = scheduler.FlushDueActions(80);

            Assert.AreEqual(1, executed, "仅第一个到期动作应执行。");
            CollectionAssert.AreEqual(new[] { "first" }, recorder.Order,
                "被前一回调取消的到期动作不得执行。");
        }

        // ────────────────────────── 同到期时间 FIFO ──────────────────────────

        [Test]
        [Description("同到期时间按注册顺序 FIFO 执行：先注册的 A 先于后注册的 B。"
            + " 对应确定性要求（不依赖无序集合遍历顺序）。")]
        public void SameDueTime_ExecutesInRegistrationOrder_Fifo()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            scheduler.Schedule(100, () => recorder.Record("A"));
            scheduler.Schedule(100, () => recorder.Record("B"));
            scheduler.Schedule(100, () => recorder.Record("C"));

            scheduler.BeginFrame(100);
            int executed = scheduler.FlushDueActions(80);

            Assert.AreEqual(3, executed, "三个同到期动作应全部执行。");
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, recorder.Order,
                "同到期时间必须按注册顺序 FIFO 执行。");
        }

        [Test]
        [Description("混合到期时间：按注册顺序整体收集，到期者执行、未到期者保留；"
            + " 未到期动作的注册顺序在其后到期时仍然保持。")]
        public void MixedDueTimes_KeepRegistrationOrderWithinDueBatch()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            scheduler.Schedule(100, () => recorder.Record("A(100)"));
            scheduler.Schedule(300, () => recorder.Record("B(300)"));
            scheduler.Schedule(100, () => recorder.Record("C(100)"));

            scheduler.BeginFrame(100);
            int executed = scheduler.FlushDueActions(80);
            Assert.AreEqual(2, executed, "第一批应只执行 due<=100 的两个动作。");
            CollectionAssert.AreEqual(new[] { "A(100)", "C(100)" }, recorder.Order,
                "同批到期动作按注册顺序 FIFO 执行。");

            scheduler.BeginFrame(300);
            executed = scheduler.FlushDueActions(80);
            Assert.AreEqual(1, executed, "第二批应执行剩余的 B(300)。");
            CollectionAssert.AreEqual(new[] { "A(100)", "C(100)", "B(300)" }, recorder.Order,
                "后到期动作在其到期批次内按注册顺序执行。");
        }

        // ────────────────────────── 冻结语义 ──────────────────────────

        [Test]
        [Description("冻结后 flush 不执行任何到期动作；冻结后 Schedule 拒绝新注册返回 null。")]
        public void FrozenScheduler_DoesNotExecuteAndRejectsNewSchedule()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            scheduler.Schedule(100, () => recorder.Record("beforeFreeze"));

            scheduler.Freeze();
            Assert.IsTrue(scheduler.IsFrozen, "Freeze 后 IsFrozen 应为 true。");

            ScheduledActionHandle rejected = scheduler.Schedule(100, () => recorder.Record("afterFreeze"));
            Assert.IsNull(rejected, "冻结后不应接受新注册。");

            scheduler.BeginFrame(100);
            int executed = scheduler.FlushDueActions(80);
            Assert.AreEqual(0, executed, "冻结后 flush 不应执行到期动作。");
            Assert.AreEqual(0, recorder.Count, "冻结后回调不得被调用。");
        }

        // ────────────────────────── Clear 重置 ──────────────────────────

        [Test]
        [Description("Clear 清空待执行队列并可复用实例：旧到期动作不再执行，新局可重新注册。")]
        public void Clear_ResetsPendingAndCanReuseInstance()
        {
            var scheduler = new BattleActionScheduler();
            var recorder = new CallbackRecorder();
            scheduler.BeginFrame(0);
            scheduler.Schedule(100, () => recorder.Record("old"));

            scheduler.Clear();
            Assert.IsFalse(scheduler.IsFrozen, "Clear 后 IsFrozen 应为 false。");
            Assert.AreEqual(0, scheduler.FrameNowMs, "Clear 后时间戳应清零。");

            scheduler.BeginFrame(100);
            int executed = scheduler.FlushDueActions(80);
            Assert.AreEqual(0, executed, "Clear 后旧到期动作不应执行。");

            // 新局重新注册可正常到期执行。
            scheduler.BeginFrame(200);
            scheduler.Schedule(100, () => recorder.Record("new"));
            scheduler.BeginFrame(300);
            executed = scheduler.FlushDueActions(80);
            Assert.AreEqual(1, executed, "Clear 后新注册动作应可正常执行。");
            CollectionAssert.AreEqual(new[] { "new" }, recorder.Order,
                "新局只应执行新注册的到期动作。");
        }
    }
}
