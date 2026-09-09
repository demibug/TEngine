using System;
using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEngine.TestTools;
using YooAsset;

namespace TEngine.StartupLifecycleTests
{
    /// <summary>
    /// 真实 YooAsset 包初始化协议测试。
    /// </summary>
    public sealed class StartupLifecyclePlayModeTests
    {
        private const string EditorPlayModeKey = "EditorPlayMode";

        // 同一 Play 会话中 ShutdownLifecycle 套件会把 ModuleSystem 留在 Stopped；
        // 本程序集无 InternalsVisibleTo，只能反射框架的显式会话重置点恢复 Running。
        private static readonly MethodInfo ResetForNewSessionMethod =
            typeof(ModuleSystem).GetMethod("ResetForNewSession", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void ResetSessionSetUp()
        {
            ResetForNewSession();
        }

        [TearDown]
        public void ResetSessionTearDown()
        {
            if (ModuleSystem.IsRunning)
            {
                ModuleSystem.Shutdown();
            }

            ResetForNewSession();
        }

        private static void ResetForNewSession()
        {
            Assert.IsNotNull(ResetForNewSessionMethod, "测试必须能调用框架的显式会话重置点。");
            ResetForNewSessionMethod.Invoke(null, null);
        }

        [UnityTest]
        public IEnumerator UnscaledTimeout_FiresWhenTimeScaleIsZero()
        {
            float previousTimeScale = UnityEngine.Time.timeScale;
            UnityEngine.Time.timeScale = 0f;
            try
            {
                Exception observed = null;
                UniTask delayed = UniTask.Delay(
                    TimeSpan.FromMilliseconds(100),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update);
                yield return delayed.Timeout(
                    TimeSpan.FromMilliseconds(10),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update).ToCoroutine(exception => observed = exception);

                Assert.IsInstanceOf<TimeoutException>(observed,
                    "启动超时必须使用非缩放时间，不能被 timeScale=0 卡住。");
            }
            finally
            {
                UnityEngine.Time.timeScale = previousTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator InitPackage_MergesConcurrentRequests_ReturnsCompletedOperation_AndRetriesAfterFailure()
        {
            IResourceModule module = ModuleSystem.GetModule<IResourceModule>();
            Assert.IsNotNull(module);
            module.Initialize();

            Type editorPrefsType = Type.GetType("UnityEditor.EditorPrefs, UnityEditor.CoreModule") ??
                                   Type.GetType("UnityEditor.EditorPrefs, UnityEditor");
            Assert.IsNotNull(editorPrefsType, "该真实包协议测试必须在 Unity Editor 中运行。");

            MethodInfo getInt = editorPrefsType.GetMethod(
                "GetInt", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(int) }, null);
            MethodInfo setInt = editorPrefsType.GetMethod(
                "SetInt", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(int) }, null);
            Assert.IsNotNull(getInt);
            Assert.IsNotNull(setInt);

            int previousPlayMode = (int)getInt.Invoke(null, new object[] { EditorPlayModeKey, 0 });
            // EditorSimulateBuildPipeline 只能为收集器中已配置的包生成模拟清单；
            // 本项目收集器里只有 DefaultPackage 有收集规则，随机包名必然失败。
            string retryPackageName = "DefaultPackage";
            try
            {
                // 先让同一包的第一次请求在配置阶段失败（CustomPlayMode 不被支持），
                // 验证失败上下文被移除而非永久占住包。此阶段在 touching YooAsset 前抛出，
                // 不会产生 Error 日志。
                setInt.Invoke(null, new object[] { EditorPlayModeKey, (int)EPlayMode.CustomPlayMode });
                UniTask<InitializationOperation> failedTask = module.InitPackage(retryPackageName, true);
                Exception failure = null;
                yield return failedTask.ToCoroutine(null, exception => failure = exception);
                Assert.IsInstanceOf<NotSupportedException>(failure);

                // 切到可用模式后重试；DefaultPackage 会由 EditorSimulateBuildPipeline 生成真实模拟清单。
                setInt.Invoke(null, new object[] { EditorPlayModeKey, (int)EPlayMode.EditorSimulateMode });
                UniTask<InitializationOperation> firstTask = module.InitPackage(retryPackageName, true);
                UniTask<InitializationOperation> mergedTask = module.InitPackage(retryPackageName, true);

                InitializationOperation firstOperation = null;
                InitializationOperation mergedOperation = null;
                Exception firstFailure = null;
                Exception mergedFailure = null;
                yield return firstTask.ToCoroutine(
                    operation => firstOperation = operation,
                    exception => firstFailure = exception);
                yield return mergedTask.ToCoroutine(
                    operation => mergedOperation = operation,
                    exception => mergedFailure = exception);

                Assert.IsNull(firstFailure);
                Assert.IsNull(mergedFailure);
                Assert.IsNotNull(firstOperation);
                Assert.AreSame(firstOperation, mergedOperation,
                    "同包同参数的并发请求必须共享同一个完成结果。");
                Assert.AreEqual(EOperationStatus.Succeed, firstOperation.Status);

                InitializationOperation repeatedOperation = null;
                Exception repeatedFailure = null;
                yield return module.InitPackage(retryPackageName, true).ToCoroutine(
                    operation => repeatedOperation = operation,
                    exception => repeatedFailure = exception);
                Assert.IsNull(repeatedFailure);
                Assert.AreSame(firstOperation, repeatedOperation,
                    "成功后的重复调用必须返回有效操作，不能返回 null 或再次初始化。");

                Exception conflict = null;
                yield return module.InitPackage(retryPackageName, false).ToCoroutine(
                    operation => Assert.Fail("不同 needInitMainFest 参数不应复用已存在的包请求。"),
                    exception => conflict = exception);
                Assert.IsInstanceOf<InvalidOperationException>(conflict,
                    "同包不同参数必须显式冲突，不能静默重置或返回 null。");
            }
            finally
            {
                setInt.Invoke(null, new object[] { EditorPlayModeKey, previousPlayMode });
            }
        }
    }
}
