using System;
using NUnit.Framework;
using TEngine;

namespace GameLogic.FairyGUI.Tests
{
    /// <summary>
    /// FguiLifetimeScope 的 GameEvent 注册与 Dispose 回归：仅覆盖事件解绑语义，不依赖 FairyGUI 图形资源。
    /// </summary>
    public sealed class FguiLifetimeScopeEventTests
    {
        private const int EventId = 92001;

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
        public void Dispose_AfterRegistration_StopsReceiving()
        {
            int calls = 0;
            using (var scope = new FguiLifetimeScope())
            {
                scope.AddUIEvent(EventId, (Action)(() => calls++));

                GameEvent.Send(EventId);
                Assert.AreEqual(1, calls);

                scope.Dispose();
                GameEvent.Send(EventId);
                Assert.AreEqual(1, calls);
            }
        }

        [Test]
        public void Dispose_DuringDispatch_CurrentCallbackStillRuns_NextDoesNot()
        {
            var scope = new FguiLifetimeScope();
            int calls = 0;

            scope.AddUIEvent(EventId, (Action)(() =>
            {
                calls++;
                scope.Dispose();
            }));

            GameEvent.Send(EventId);
            Assert.AreEqual(1, calls);

            GameEvent.Send(EventId);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Dispose_Twice_NoException()
        {
            var scope = new FguiLifetimeScope();
            scope.AddUIEvent(EventId, (Action)(() => { }));

            Assert.DoesNotThrow(() =>
            {
                scope.Dispose();
                scope.Dispose();
            });

            GameEvent.Send(EventId);
        }
    }
}
