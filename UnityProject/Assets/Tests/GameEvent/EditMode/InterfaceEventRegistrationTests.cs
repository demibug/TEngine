using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using TEngine;
using GameEventTests.Registration;

namespace GameEventTests.Registration
{
    /// <summary>
    /// 注册链验证用事件接口：定义在测试程序集内，验证生成器对任意引用 TEngine.Runtime 的程序集生效
    /// （生成物 {接口}_Event/{接口}_Gen 输出到本命名空间，Registrar 输出到 TEngine 命名空间）。
    /// </summary>
    [EventInterface(EEventGroup.GroupLogic)]
    public interface IRegistrationProbe
    {
        void OnProbe();

        void OnProbeWithData(int code, string message);
    }
}

namespace TEngine.GameEventTests
{
    /// <summary>
    /// GameEventHelper 注册链运行时测试：Init 全量注册 / RegisterAssembly 动态注册 /
    /// Shutdown 后重新 Init 闭环 / 主线程与一次性状态校验。
    /// <remarks>全局 GameEvent 为静态共享状态，每个用例 SetUp/TearDown 统一 Shutdown 清理，不并行运行。</remarks>
    /// </summary>
    public sealed class InterfaceEventRegistrationTests
    {
        [SetUp]
        public void SetUp()
        {
            // ShutdownLifecycle 等其他测试可能把 ModuleSystem 置为 Stopped，且 EditMode 域内没有
            // RuntimeInitializeOnLoadMethod 回调来恢复；GameEventHelper 入口要求运行态，
            // 这里防御性恢复，消除测试执行顺序依赖。
            EnsureModuleSystemRunning();

            // 先清表并重置 Init 一次性状态（Shutdown 内联动 GameEventHelper.OnEventSystemReset）。
            GameEvent.Shutdown();
            GameFrameworkLog.SetLogHelper(null);
        }

        /// <summary>
        /// 将 ModuleSystem 恢复为 Running（仅测试域使用：反射写私有静态状态字段）。
        /// </summary>
        private static void EnsureModuleSystemRunning()
        {
            if (ModuleSystem.IsRunning)
            {
                return;
            }

            var stateField = typeof(ModuleSystem).GetField("_state",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (stateField == null)
            {
                throw new InvalidOperationException("ModuleSystem._state 字段不存在，测试基础设施需要同步更新。");
            }

            stateField.SetValue(null, Enum.Parse(stateField.FieldType, nameof(ModuleSystemState.Running)));
        }

        [TearDown]
        public void TearDown()
        {
            GameEvent.Shutdown();
            GameFrameworkLog.SetLogHelper(new DefaultLogHelper());
        }

        [Test]
        public void Init_RegistersTestAssemblyInterface()
        {
            TEngine.GameEventHelper.Init();
            Assert.IsNotNull(GameEvent.Get<IRegistrationProbe>());
        }

        [Test]
        public void Init_GeneratedInterfaceSendsAndDispatches()
        {
            TEngine.GameEventHelper.Init();

            int receivedCode = 0;
            string receivedMessage = null;
            GameEvent.AddEventListener<int, string>(
                IRegistrationProbe_Event.OnProbeWithData,
                (code, message) =>
                {
                    receivedCode = code;
                    receivedMessage = message;
                });

            GameEvent.Get<IRegistrationProbe>().OnProbeWithData(7, "ok");

            Assert.AreEqual(7, receivedCode);
            Assert.AreEqual("ok", receivedMessage);
        }

        [Test]
        public void Init_Shutdown_Init_ClosedLoop()
        {
            TEngine.GameEventHelper.Init();
            Assert.IsNotNull(GameEvent.Get<IRegistrationProbe>());

            GameEvent.Shutdown();
            Assert.IsNull(GameEvent.Get<IRegistrationProbe>());

            // Shutdown 重置 Init 一次性状态后，重新 Init 必须恢复注册。
            TEngine.GameEventHelper.Init();
            Assert.IsNotNull(GameEvent.Get<IRegistrationProbe>());
        }

        [Test]
        public void RegisterAssembly_BeforeInit_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() =>
                TEngine.GameEventHelper.RegisterAssembly(typeof(InterfaceEventRegistrationTests).Assembly));
        }

        [Test]
        public void Init_Twice_ThrowsInvalidOperation()
        {
            TEngine.GameEventHelper.Init();
            Assert.Throws<InvalidOperationException>(() => TEngine.GameEventHelper.Init());
        }

        [Test]
        public void Init_FromBackgroundThread_ThrowsInvalidOperation()
        {
            Exception caught = null;
            var thread = new Thread(() =>
            {
                try
                {
                    TEngine.GameEventHelper.Init();
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            });

            thread.Start();
            thread.Join();

            Assert.IsInstanceOf<InvalidOperationException>(caught);
        }

        [Test]
        public void RegisterAssembly_AfterInit_IsIdempotent()
        {
            TEngine.GameEventHelper.Init();
            // 运行期重复注册同一程序集：RegWrapInterface 幂等覆盖写，不应抛异常且注册仍可用。
            Assert.DoesNotThrow(() =>
                TEngine.GameEventHelper.RegisterAssembly(typeof(InterfaceEventRegistrationTests).Assembly));
            Assert.IsNotNull(GameEvent.Get<IRegistrationProbe>());
        }
    }
}
