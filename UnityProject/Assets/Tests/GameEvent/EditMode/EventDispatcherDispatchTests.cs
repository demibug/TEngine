using System;
using System.Collections.Generic;
using NUnit.Framework;
using TEngine;

namespace TEngine.GameEventTests
{
    /// <summary>
    /// EventDispatcher 同步分发可靠性测试：使用独立实例，覆盖异常传播恢复、同事件嵌套、顺序增删与 0-6 参数重载。
    /// </summary>
    public sealed class EventDispatcherDispatchTests
    {
        private const int EventId = 90001;
        private const int EventIdNested = 90002;
        private const int EventIdCrossX = 90003;
        private const int EventIdCrossY = 90004;

        private EventDispatcher _dispatcher;

        [SetUp]
        public void SetUp()
        {
            // 重复注册/缺失移除按日志惯例走 Log.Fatal（DefaultLogHelper 下会抛异常），
            // 测试期间静默日志，使断言不依赖日志宏与日志实现。
            GameFrameworkLog.SetLogHelper(null);
            _dispatcher = new EventDispatcher();
        }

        [TearDown]
        public void TearDown()
        {
            _dispatcher = null;
            GameFrameworkLog.SetLogHelper(new DefaultLogHelper());
        }

        [Test]
        public void Send_FirstListenerThrows_PropagatesSkipsRestAndRecovers()
        {
            var calls = new List<string>();
            Action thrower = () =>
            {
                calls.Add("A");
                throw new InvalidOperationException("boom");
            };
            Action after = () => calls.Add("B");

            Assert.IsTrue(_dispatcher.AddEventListener(EventId, thrower));
            Assert.IsTrue(_dispatcher.AddEventListener(EventId, after));

            var ex = Assert.Throws<InvalidOperationException>(() => _dispatcher.Send(EventId));
            Assert.AreEqual("boom", ex.Message);
            Assert.AreEqual(new[] { "A" }, calls);

            _dispatcher.RemoveEventListener(EventId, thrower);
            calls.Clear();
            Assert.DoesNotThrow(() => _dispatcher.Send(EventId));
            Assert.AreEqual(new[] { "B" }, calls);
        }

        [Test]
        public void Send_ListenerAddsSameEventThenThrows_CommitsAddViaFinally()
        {
            var calls = new List<string>();
            Action pending = () => calls.Add("C");
            Action thrower = () =>
            {
                calls.Add("A");
                Assert.IsTrue(_dispatcher.AddEventListener(EventId, pending));
                throw new InvalidOperationException("boom");
            };

            _dispatcher.AddEventListener(EventId, thrower);

            Assert.Throws<InvalidOperationException>(() => _dispatcher.Send(EventId));
            Assert.AreEqual(new[] { "A" }, calls);

            _dispatcher.RemoveEventListener(EventId, thrower);
            calls.Clear();
            Assert.DoesNotThrow(() => _dispatcher.Send(EventId));
            Assert.AreEqual(new[] { "C" }, calls);

            calls.Clear();
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "C" }, calls);
        }

        [Test]
        public void RemoveDuringDispatch_CurrentStillInvoked_NextNot()
        {
            var calls = new List<string>();
            Action b = () => calls.Add("B");
            bool first = true;

            Action a = () =>
            {
                calls.Add("A");
                if (!first)
                {
                    return;
                }

                first = false;
                _dispatcher.RemoveEventListener(EventId, b);
            };

            _dispatcher.AddEventListener(EventId, a);
            _dispatcher.AddEventListener(EventId, b);

            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A", "B" }, calls);

            calls.Clear();
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A" }, calls);
        }

        [Test]
        public void AddDuringDispatch_CurrentAndNestedDoNotSeePending_NextSendDoes()
        {
            var calls = new List<string>();
            Action pending = () => calls.Add("C");
            bool first = true;

            Action a = () =>
            {
                calls.Add("A");
                if (!first)
                {
                    return;
                }

                first = false;
                _dispatcher.AddEventListener(EventId, pending);
                _dispatcher.Send(EventId);
            };

            _dispatcher.AddEventListener(EventId, a);

            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A", "A" }, calls);

            calls.Clear();
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A", "C" }, calls);
        }

        [Test]
        public void RemoveThenAddDuringDispatch_CurrentUnchanged_NextReorderedToTail()
        {
            var order = new List<string>();
            Action a = null;
            Action b = null;
            Action c = () => order.Add("C");
            bool first = true;

            a = () =>
            {
                order.Add("A");
                if (!first)
                {
                    return;
                }

                first = false;
                _dispatcher.RemoveEventListener(EventId, b);
                _dispatcher.AddEventListener(EventId, b);
            };
            b = () => order.Add("B");

            _dispatcher.AddEventListener(EventId, a);
            _dispatcher.AddEventListener(EventId, b);
            _dispatcher.AddEventListener(EventId, c);

            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A", "B", "C" }, order);

            order.Clear();
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A", "C", "B" }, order);
        }

        [Test]
        public void AddPendingTwice_SecondReturnsFalse_OnlyOneInstanceCommitted()
        {
            var calls = new List<string>();
            Action a = null;
            Action pending = () => calls.Add("C");
            bool first = true;

            a = () =>
            {
                calls.Add("A");
                if (!first)
                {
                    return;
                }

                first = false;
                Assert.IsTrue(_dispatcher.AddEventListener(EventId, pending));
                Assert.IsFalse(_dispatcher.AddEventListener(EventId, pending));
            };

            _dispatcher.AddEventListener(EventId, a);

            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A" }, calls);

            calls.Clear();
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A", "C" }, calls);

            calls.Clear();
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A", "C" }, calls);
        }

        [Test]
        public void AddThenRemovePendingDuringDispatch_EndsUpAbsent()
        {
            var calls = new List<string>();
            Action a = null;
            Action pending = () => calls.Add("C");
            bool first = true;

            a = () =>
            {
                calls.Add("A");
                if (!first)
                {
                    return;
                }

                first = false;
                _dispatcher.AddEventListener(EventId, pending);
                _dispatcher.RemoveEventListener(EventId, pending);
            };

            _dispatcher.AddEventListener(EventId, a);

            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A" }, calls);

            calls.Clear();
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A" }, calls);
        }

        [Test]
        public void NestedThrowsCaughtByOuter_OuterMutates_PendingCommitOnlyAtOuterExit()
        {
            var calls = new List<string>();
            bool bThrown = false;
            Action nestedPending = () => calls.Add("E");
            Action outerPending = () => calls.Add("D");
            Action b = () =>
            {
                calls.Add("B");
                if (bThrown)
                {
                    return;
                }

                bThrown = true;
                _dispatcher.AddEventListener(EventId, nestedPending);
                throw new InvalidOperationException("inner");
            };
            bool first = true;

            Action a = () =>
            {
                calls.Add("A");
                if (!first)
                {
                    return;
                }

                first = false;
                try
                {
                    _dispatcher.Send(EventId);
                }
                catch (InvalidOperationException)
                {
                }

                _dispatcher.AddEventListener(EventId, outerPending);
            };

            _dispatcher.AddEventListener(EventId, a);
            _dispatcher.AddEventListener(EventId, b);

            Assert.DoesNotThrow(() => _dispatcher.Send(EventId));
            Assert.AreEqual(new[] { "A", "A", "B", "B" }, calls);

            calls.Clear();
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "A", "B", "E", "D" }, calls);
        }

        [Test]
        public void CrossEventSend_IndependentDepthAndCommits()
        {
            var calls = new List<string>();
            bool xFirst = true;
            bool yFirst = true;
            Action xPending = () => calls.Add("X2");
            Action xHandler = null;
            Action yHandler = () =>
            {
                calls.Add("Y");
                if (!yFirst)
                {
                    return;
                }

                yFirst = false;
                _dispatcher.AddEventListener(EventIdCrossX, xPending);
            };

            xHandler = () =>
            {
                calls.Add("X");
                if (!xFirst)
                {
                    return;
                }

                xFirst = false;
                _dispatcher.Send(EventIdCrossY);
            };

            _dispatcher.AddEventListener(EventIdCrossX, xHandler);
            _dispatcher.AddEventListener(EventIdCrossY, yHandler);

            _dispatcher.Send(EventIdCrossX);
            Assert.AreEqual(new[] { "X", "Y" }, calls);

            calls.Clear();
            _dispatcher.Send(EventIdCrossX);
            Assert.AreEqual(new[] { "X", "X2" }, calls);

            calls.Clear();
            _dispatcher.Send(EventIdCrossY);
            Assert.AreEqual(new[] { "Y" }, calls);
        }

        [Test]
        public void RemoveMissingHandler_NonDispatch_NoThrowAndSystemUsable()
        {
            Assert.DoesNotThrow(() => _dispatcher.RemoveEventListener(EventId, (Action)(() => { })));

            var calls = new List<string>();
            Action h = () => calls.Add("H");
            Assert.IsTrue(_dispatcher.AddEventListener(EventId, h));
            _dispatcher.Send(EventId);
            Assert.AreEqual(new[] { "H" }, calls);
        }

        [Test]
        public void AddDuplicateHandler_NonDispatch_SecondReturnsFalse_SingleInvocation()
        {
            int calls = 0;
            Action h = () => calls++;

            Assert.IsTrue(_dispatcher.AddEventListener(EventId, h));
            Assert.IsFalse(_dispatcher.AddEventListener(EventId, h));

            _dispatcher.Send(EventId);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Send_ZeroArg_OverloadDispatch()
        {
            int calls = 0;
            _dispatcher.AddEventListener(EventId, (Action)(() => calls++));
            _dispatcher.Send(EventId);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Send_OneArg_PassesValue_AndRecoversAfterThrow()
        {
            var received = new List<int>();
            Action<int> thrower = v =>
            {
                received.Add(v);
                throw new InvalidOperationException("boom");
            };
            Action<int> after = v => received.Add(v * 2);

            _dispatcher.AddEventListener(EventId, thrower);
            _dispatcher.AddEventListener(EventId, after);

            Assert.Throws<InvalidOperationException>(() => _dispatcher.Send(EventId, 5));
            Assert.AreEqual(new[] { 5 }, received);

            _dispatcher.RemoveEventListener(EventId, thrower);
            received.Clear();
            _dispatcher.Send(EventId, 7);
            Assert.AreEqual(new[] { 14 }, received);
        }

        [Test]
        public void Send_TwoArgs_PassesValues_AndRecoversAfterThrow()
        {
            var received = new List<string>();
            Action<int, string> thrower = (a, b) =>
            {
                received.Add($"{a}-{b}");
                throw new InvalidOperationException("boom");
            };
            Action<int, string> after = (a, b) => received.Add($"{b}-{a}");

            _dispatcher.AddEventListener(EventId, thrower);
            _dispatcher.AddEventListener(EventId, after);

            Assert.Throws<InvalidOperationException>(() => _dispatcher.Send(EventId, 1, "one"));
            Assert.AreEqual(new[] { "1-one" }, received);

            _dispatcher.RemoveEventListener(EventId, thrower);
            received.Clear();
            _dispatcher.Send(EventId, 2, "two");
            Assert.AreEqual(new[] { "two-2" }, received);
        }

        [Test]
        public void Send_ThreeArgs_PassesValues_AndRecoversAfterThrow()
        {
            var received = new List<string>();
            Action<int, string, bool> thrower = (a, b, c) =>
            {
                received.Add($"{a}-{b}-{c}");
                throw new InvalidOperationException("boom");
            };
            Action<int, string, bool> after = (a, b, c) => received.Add($"{c}-{b}-{a}");

            _dispatcher.AddEventListener(EventId, thrower);
            _dispatcher.AddEventListener(EventId, after);

            Assert.Throws<InvalidOperationException>(() => _dispatcher.Send(EventId, 1, "one", true));
            Assert.AreEqual(new[] { "1-one-True" }, received);

            _dispatcher.RemoveEventListener(EventId, thrower);
            received.Clear();
            _dispatcher.Send(EventId, 2, "two", false);
            Assert.AreEqual(new[] { "False-two-2" }, received);
        }

        [Test]
        public void Send_FourArgs_PassesValues_AndRecoversAfterThrow()
        {
            var received = new List<string>();
            Action<int, string, bool, float> thrower = (a, b, c, d) =>
            {
                received.Add($"{a}-{b}-{c}-{d}");
                throw new InvalidOperationException("boom");
            };
            Action<int, string, bool, float> after = (a, b, c, d) => received.Add($"{d}-{c}-{b}-{a}");

            _dispatcher.AddEventListener(EventId, thrower);
            _dispatcher.AddEventListener(EventId, after);

            Assert.Throws<InvalidOperationException>(() => _dispatcher.Send(EventId, 1, "one", true, 0.5f));
            Assert.AreEqual(new[] { "1-one-True-0.5" }, received);

            _dispatcher.RemoveEventListener(EventId, thrower);
            received.Clear();
            _dispatcher.Send(EventId, 2, "two", false, 1.5f);
            Assert.AreEqual(new[] { "1.5-False-two-2" }, received);
        }

        [Test]
        public void Send_FiveArgs_PassesValues_AndRecoversAfterThrow()
        {
            var received = new List<string>();
            Action<int, string, bool, float, char> thrower = (a, b, c, d, e) =>
            {
                received.Add($"{a}-{b}-{c}-{d}-{e}");
                throw new InvalidOperationException("boom");
            };
            Action<int, string, bool, float, char> after = (a, b, c, d, e) => received.Add($"{e}-{d}-{c}-{b}-{a}");

            _dispatcher.AddEventListener(EventId, thrower);
            _dispatcher.AddEventListener(EventId, after);

            Assert.Throws<InvalidOperationException>(() => _dispatcher.Send(EventId, 1, "one", true, 0.5f, 'x'));
            Assert.AreEqual(new[] { "1-one-True-0.5-x" }, received);

            _dispatcher.RemoveEventListener(EventId, thrower);
            received.Clear();
            _dispatcher.Send(EventId, 2, "two", false, 1.5f, 'y');
            Assert.AreEqual(new[] { "y-1.5-False-two-2" }, received);
        }

        [Test]
        public void Send_SixArgs_PassesValues_AndRecoversAfterThrow()
        {
            var received = new List<string>();
            Action<int, string, bool, float, char, long> thrower = (a, b, c, d, e, f) =>
            {
                received.Add($"{a}-{b}-{c}-{d}-{e}-{f}");
                throw new InvalidOperationException("boom");
            };
            Action<int, string, bool, float, char, long> after = (a, b, c, d, e, f) => received.Add($"{f}-{e}-{d}-{c}-{b}-{a}");

            _dispatcher.AddEventListener(EventId, thrower);
            _dispatcher.AddEventListener(EventId, after);

            Assert.Throws<InvalidOperationException>(() => _dispatcher.Send(EventId, 1, "one", true, 0.5f, 'x', 9L));
            Assert.AreEqual(new[] { "1-one-True-0.5-x-9" }, received);

            _dispatcher.RemoveEventListener(EventId, thrower);
            received.Clear();
            _dispatcher.Send(EventId, 2, "two", false, 1.5f, 'y', 10L);
            Assert.AreEqual(new[] { "10-y-1.5-False-two-2" }, received);
        }

        [Test]
        public void Send_SixArgs_AllListenersFireInOrder_WithTypedSignatures()
        {
            var order = new List<string>();
            Action<int, string, bool, float, char, long> first = (a, b, c, d, e, f) => order.Add("S1");
            Action<int, string, bool, float, char, long> second = (a, b, c, d, e, f) => order.Add("S2");
            Action<int, string, bool, float, char, long> third = (a, b, c, d, e, f) => order.Add("S3");

            _dispatcher.AddEventListener(EventIdNested, first);
            _dispatcher.AddEventListener(EventIdNested, second);
            _dispatcher.AddEventListener(EventIdNested, third);

            _dispatcher.Send(EventIdNested, 1, "a", true, 0.1f, 'z', 1L);
            Assert.AreEqual(new[] { "S1", "S2", "S3" }, order);
        }

        [Test]
        public void Send_TypeMismatchedListener_SkippedWithoutBreaking()
        {
            int intCalls = 0;
            Action mismatched = () => intCalls++;
            Action<int> matched = v => intCalls += 10;

            _dispatcher.AddEventListener(EventId, mismatched);
            _dispatcher.AddEventListener(EventId, matched);

            _dispatcher.Send(EventId, 1);
            Assert.AreEqual(10, intCalls);
        }
    }
}
