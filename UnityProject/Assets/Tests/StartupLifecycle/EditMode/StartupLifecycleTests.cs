using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEngine.TestTools;
using Regex = System.Text.RegularExpressions.Regex;

namespace TEngine.StartupLifecycleTests
{
    public sealed class StartupAttemptTests
    {
        private const BindingFlags AnyInstanceMember =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void Attempt_TerminalStateCanBeSubmittedOnlyOnce()
        {
            Type attemptType = GetApplicationType("Procedure.StartupAttempt");
            object attempt = Activator.CreateInstance(
                attemptType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { 7 },
                null);
            try
            {
                Assert.IsTrue((bool)InvokeInstance(attempt, "TrySucceed"));
                Assert.IsFalse((bool)InvokeInstance(attempt, "TryFail"));
                Assert.AreEqual("Succeeded", GetProperty(attempt, "State").ToString());
                Assert.IsFalse((bool)GetProperty(attempt, "IsRunning"));
            }
            finally
            {
                InvokeInstance(attempt, "Dispose");
            }
        }

        [Test]
        public void Attempt_InvalidateMakesLateCallbackNonActionable()
        {
            Type attemptType = GetApplicationType("Procedure.StartupAttempt");
            object attempt = Activator.CreateInstance(
                attemptType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { 8 },
                null);
            try
            {
                CancellationToken token = (CancellationToken)GetProperty(attempt, "Token");
                InvokeInstance(attempt, "Invalidate");

                Assert.IsTrue(token.IsCancellationRequested);
                Assert.AreEqual("Cancelled", GetProperty(attempt, "State").ToString());
                Assert.IsFalse((bool)GetProperty(attempt, "IsRunning"));
                Assert.IsFalse((bool)InvokeInstance(attempt, "TrySucceed"), "晚完成不得再次提交成功终态。");
            }
            finally
            {
                InvokeInstance(attempt, "Dispose");
            }
        }

        private static object InvokeInstance(object instance, string methodName)
        {
            return instance.GetType().GetMethod(methodName, AnyInstanceMember).Invoke(instance, null);
        }

        private static object GetProperty(object instance, string propertyName)
        {
            return instance.GetType().GetProperty(propertyName, AnyInstanceMember).GetValue(instance, null);
        }

        private static Type GetApplicationType(string typeName)
        {
            Assembly applicationAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
                {
                    applicationAssembly = assembly;
                    break;
                }
            }

            Assert.IsNotNull(applicationAssembly,
                "应用层 Assembly-CSharp 尚未加载；该测试必须在 Unity 应用程序集存在时运行。");
            Type type = applicationAssembly.GetType(typeName, false);
            Assert.IsNotNull(type, $"应用层类型 '{typeName}' 未找到。");
            return type;
        }
    }

    public sealed class StartupEntryContractTests
    {
        private static class ValidGameApp
        {
            public static void Entrance(object[] objects) { }
        }

        private static class MissingGameApp
        {
            public static void Start(object[] objects) { }
        }

        private static class InvalidSignatureGameApp
        {
            public static int Entrance(object[] objects) => 0;
        }

        private static class AmbiguousGameApp
        {
            public static void Entrance(object[] objects) { }
            public static void Entrance(string value) { }
        }

        [Test]
        public void ValidEntrance_IsFoundExactly()
        {
            bool result = TryFindEntrance(typeof(ValidGameApp), out MethodInfo method, out string error);

            Assert.IsTrue(result, error);
            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(void), method.ReturnType);
            Assert.AreEqual(new[] { typeof(object[]) },
                Array.ConvertAll(method.GetParameters(), parameter => parameter.ParameterType));
        }

        [TestCase(typeof(MissingGameApp))]
        [TestCase(typeof(InvalidSignatureGameApp))]
        [TestCase(typeof(AmbiguousGameApp))]
        public void InvalidEntrance_IsRejected(Type appType)
        {
            bool result = TryFindEntrance(appType, out MethodInfo method, out string error);

            Assert.IsFalse(result);
            Assert.IsNull(method);
            Assert.IsNotEmpty(error);
        }

        private static bool TryFindEntrance(Type appType, out MethodInfo method, out string error)
        {
            Type contractType = GetApplicationType("Procedure.StartupEntryContract");
            MethodInfo contractMethod = contractType.GetMethod(
                "TryFindEntrance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            object[] arguments = { appType, null, null };
            bool result = (bool)contractMethod.Invoke(null, arguments);
            method = (MethodInfo)arguments[1];
            error = (string)arguments[2];
            return result;
        }

        private static Type GetApplicationType(string typeName)
        {
            Assembly applicationAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal))
                {
                    applicationAssembly = assembly;
                    break;
                }
            }

            Assert.IsNotNull(applicationAssembly,
                "应用层 Assembly-CSharp 尚未加载；该测试必须在 Unity 应用程序集存在时运行。");
            Type type = applicationAssembly.GetType(typeName, false);
            Assert.IsNotNull(type, $"应用层类型 '{typeName}' 未找到。");
            return type;
        }
    }

    public sealed class ResourceBootstrapTests
    {
        [Test]
        public void InvalidDriverObjectPoolConfiguration_FailsBootstrapBeforeCompletion()
        {
            ResourceModule module = new ResourceModule();
            module.ConfigureObjectPoolForInitialization(
                assetAutoReleaseInterval: 60f,
                assetCapacity: -1,
                assetExpireTime: 60f,
                assetPriority: 0,
                forceUnloadUnusedAssetsAction: null);

            LogAssert.Expect(LogType.Error, new Regex("ResourceModule initialization failed.*"));

            Exception thrown = null;
            try
            {
                module.Initialize();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            Assert.IsNotNull(thrown);

            Exception observed = null;
            try
            {
                module.WaitUntilInitializedAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            Assert.AreSame(thrown, observed,
                "Driver 配置失败必须在 bootstrap 完成前进入共享失败终态。");
        }

        [Test]
        public void BootstrapFailure_CompletesSharedWaitWithOriginalException()
        {
            ResourceModule module = new ResourceModule();
            var expected = new InvalidOperationException("injected bootstrap failure");
            LogAssert.Expect(LogType.Error, new Regex("ResourceModule initialization failed: .*"));
            module.FailInitialization(expected);

            Exception observed = null;
            try
            {
                module.WaitUntilInitializedAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            Assert.AreSame(expected, observed, "等待方必须观察 bootstrap 保存的原始异常。");
        }

        [Test]
        public void BootstrapWaitCancellation_DoesNotCancelSharedBootstrap()
        {
            ResourceModule module = new ResourceModule();
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                UniTask wait = module.WaitUntilInitializedAsync(cancellation.Token);
                cancellation.Cancel();

                Assert.Throws<OperationCanceledException>(() => wait.GetAwaiter().GetResult());

                var expected = new InvalidOperationException("shared bootstrap still owns completion");
                LogAssert.Expect(LogType.Error, new Regex("ResourceModule initialization failed: .*"));
                module.FailInitialization(expected);

                Exception observed = null;
                try
                {
                    module.WaitUntilInitializedAsync().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.AreSame(expected, observed,
                    "等待者取消不能把共享 bootstrap 误标记为取消；后续仍应观察真实失败。");
            }
        }
    }
}
