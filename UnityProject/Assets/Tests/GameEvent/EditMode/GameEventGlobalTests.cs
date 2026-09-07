using System;
using System.Collections.Generic;
using NUnit.Framework;
using TEngine;

namespace TEngine.GameEventTests
{
    /// <summary>
    /// 全局 GameEvent / GameEventMgr / Shutdown 语义测试。
    /// <remarks>全局 GameEvent 为静态共享状态，每个用例使用独立事件 id，TearDown 统一 Shutdown 清理，不并行运行。</remarks>
    /// </summary>
    public sealed class GameEventGlobalTests
    {
        private const int EventId = 91001;
        private const int EventIdArg = 91002;
        private const int EventIdArgString = 91003;

        [SetUp]
        public void SetUp()
        {
            GameFrameworkLog.SetLogHelper(null);
        }

        [TearDown]
        public void TearDown()
        {
            GameEvent.Shutdown();
            GameFrameworkLog.SetLogHelper(new DefaultLogHelper());
        }

        [Test]
        public void GameEventMgr_ClearDuringDispatch_NoResidueAndRepeatClearSafe()
        {
            var mgr = new GameEventMgr();
            var calls = new List<string>();
            Action pending = () => calls.Add("H2");
            bool first = true;

            Action h = () =>
            {
                calls.Add("H");
                if (!first)
                {
                    return;
                }

                first = false;
                mgr.AddEvent(EventId, pending);
                mgr.Clear();
            };

            mgr.AddEvent(EventId, h);

            GameEvent.Send(EventId);
            Assert.AreEqual(new[] { "H" }, calls);

            calls.Clear();
            GameEvent.Send(EventId);
            Assert.IsEmpty(calls);

            Assert.DoesNotThrow(() => mgr.Clear());
            calls.Clear();
            GameEvent.Send(EventId);
            Assert.IsEmpty(calls);
        }

        [Test]
        public void GameEventMgr_ClearWithoutDispatch_StopsReceivingAndRepeatClearSafe()
        {
            var mgr = new GameEventMgr();
            int calls = 0;
            Action h = () => calls++;

            mgr.AddEvent(EventId, h);
            GameEvent.Send(EventId);
            Assert.AreEqual(1, calls);

            mgr.Clear();
            GameEvent.Send(EventId);
            Assert.AreEqual(1, calls);

            Assert.DoesNotThrow(() => mgr.Clear());
            GameEvent.Send(EventId);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ShutdownDuringCallback_ReregisteredEventOnlyNewListenerFires()
        {
            var calls = new List<string>();
            Action added = () => calls.Add("N");

            Action a = () =>
            {
                calls.Add("A");
                GameEvent.Shutdown();
                Assert.IsTrue(GameEvent.AddEventListener(EventId, added));
            };

            GameEvent.AddEventListener(EventId, a);

            GameEvent.Send(EventId);
            Assert.AreEqual(new[] { "A" }, calls);

            calls.Clear();
            GameEvent.Send(EventId);
            Assert.AreEqual(new[] { "N" }, calls);
        }

        [Test]
        public void ShutdownDuringCallback_PendingChangeDoesNotRevive()
        {
            var calls = new List<string>();
            Action pendingOld = () => calls.Add("M");
            Action added = () => calls.Add("N");
            bool first = true;

            Action a = () =>
            {
                calls.Add("A");
                if (!first)
                {
                    return;
                }

                first = false;
                GameEvent.AddEventListener(EventId, pendingOld);
                GameEvent.Shutdown();
                GameEvent.AddEventListener(EventId, added);
            };

            GameEvent.AddEventListener(EventId, a);

            GameEvent.Send(EventId);
            Assert.AreEqual(new[] { "A" }, calls);

            calls.Clear();
            GameEvent.Send(EventId);
            Assert.AreEqual(new[] { "N" }, calls);

            calls.Clear();
            GameEvent.Send(EventId);
            Assert.AreEqual(new[] { "N" }, calls);
        }

        [Test]
        public void GameEvent_ThrowThenContinue_Recovers()
        {
            var calls = new List<string>();
            Action thrower = () =>
            {
                calls.Add("A");
                throw new InvalidOperationException("boom");
            };
            Action after = () => calls.Add("B");

            GameEvent.AddEventListener(EventId, thrower);
            GameEvent.AddEventListener(EventId, after);

            Assert.Throws<InvalidOperationException>(() => GameEvent.Send(EventId));
            Assert.AreEqual(new[] { "A" }, calls);

            GameEvent.RemoveEventListener(EventId, thrower);
            calls.Clear();
            GameEvent.Send(EventId);
            Assert.AreEqual(new[] { "B" }, calls);
        }

        [Test]
        public void GameEvent_IntAndStringEntries_PassArguments()
        {
            int gotInt = 0;
            string gotString = null;
            int gotA = 0;
            string gotB = null;

            GameEvent.AddEventListener<int>(EventIdArg, v => gotInt = v);
            GameEvent.AddEventListener<string>(EventIdArgString, v => gotString = v);
            GameEvent.AddEventListener<int, string>(EventIdArg, (a, b) =>
            {
                gotA = a;
                gotB = b;
            });

            GameEvent.Send(EventIdArg, 42);
            Assert.AreEqual(42, gotInt);

            GameEvent.Send(EventIdArgString, "hello");
            Assert.AreEqual("hello", gotString);

            GameEvent.Send(EventIdArg, 7, "seven");
            Assert.AreEqual(7, gotA);
            Assert.AreEqual("seven", gotB);
        }
    }
}
