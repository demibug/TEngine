using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TEngine.ShutdownLifecycleTests
{
    /// <summary>
    /// 真实 Unity/YooAsset 退出验证。该用例故意在延迟 Destroy 落地前关闭框架，
    /// 证明实例引用由资源 owner 主动归还，随后 OnDestroy 不再访问已关闭的池。
    /// </summary>
    public sealed class ShutdownLifecyclePlayModeTests
    {
        private const string CubeLocation = "Cube";
        private static int _independentPlaySessionCount;
        private static int _firstSessionBeforeShutdownCount;
        private static int _controlledLateCallbackInvocationCount;
        private static bool _firstSessionLateRegistrationAttempted;
        private static RootModule _firstSessionRoot;
        private static IUpdateDriver _firstSessionCachedDriver;
        private static Action _controlledLateCallback;

        [UnityTest]
        [Order(-1000)]
        [Category("DomainReloadOff")]
        [Explicit("Run this same test in two separate Play sessions with Disable Domain Reload enabled.")]
        public IEnumerator IndependentPlaySession_StartsWithFreshShutdownState()
        {
#if UNITY_EDITOR
            Assert.IsTrue(EditorSettings.enterPlayModeOptionsEnabled,
                "必须启用 Enter Play Mode Options 才能验证 Domain Reload Off。");
            Assert.IsTrue(EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableDomainReload),
                "必须启用 Disable Domain Reload 才能验证跨会话静态隔离。");
#else
            Assert.Fail("该验证必须在 Unity Editor 的 Play Mode 中运行。");
#endif

            int sessionNumber = ++_independentPlaySessionCount;
            if (sessionNumber == 1)
            {
                yield return VerifyFirstIndependentPlaySession();
                yield break;
            }

            Assert.AreEqual(2, sessionNumber,
                "请在同一 Unity Editor 中只连续运行此显式测试两次；第二次必须观察到计数递增。");
            yield return VerifySecondIndependentPlaySession();
        }

        [UnityTest]
        [Category("UpdateDriverShutdown")]
        [Explicit("Each case shuts down the Play session; run each test in its own Play session.")]
        public IEnumerator UpdateDriver_UpdateListenerShutdownStopsRemainingListeners()
        {
            yield return VerifyUpdateDriverStopsRemainingListener(
                "Update", (driver, listener) => driver.AddUpdateListener(listener));
        }

        [UnityTest]
        [Category("UpdateDriverShutdown")]
        [Explicit("Each case shuts down the Play session; run each test in its own Play session.")]
        public IEnumerator UpdateDriver_FixedUpdateListenerShutdownStopsRemainingListeners()
        {
            yield return VerifyUpdateDriverStopsRemainingListener(
                "FixedUpdate", (driver, listener) => driver.AddFixedUpdateListener(listener));
        }

        [UnityTest]
        [Category("UpdateDriverShutdown")]
        [Explicit("Each case shuts down the Play session; run each test in its own Play session.")]
        public IEnumerator UpdateDriver_LateUpdateListenerShutdownStopsRemainingListeners()
        {
            yield return VerifyUpdateDriverStopsRemainingListener(
                "LateUpdate", (driver, listener) => driver.AddLateUpdateListener(listener));
        }

        [UnityTest]
        [Order(1000)]
        public IEnumerator Shutdown_ReturnsActiveAndPendingDestroyedInstancesBeforeDelayedDestroy()
        {
            Assert.AreEqual(ModuleSystemState.Running, ModuleSystem.State);

            var trace = new System.Collections.Generic.List<string>();
            GameObject rootObject = new GameObject("[ShutdownLifecycleRoot]");
            RootModule root = rootObject.AddComponent<RootModule>();
            rootObject.AddComponent<DestroyTraceProbe>().Configure(trace, "RootObject.OnDestroy");
            Assert.AreSame(root, RootModule.Instance, "测试必须由实际 RootModule 持有当前会话。");

            IUpdateDriver updateDriver = ModuleSystem.GetModule<IUpdateDriver>();
            GameObject updateDriverEntity = GetUpdateDriverEntity(updateDriver);
            Assert.IsNotNull(updateDriverEntity, "实际 UpdateDriver 宿主必须存在。");
            updateDriverEntity.AddComponent<DestroyTraceProbe>().Configure(trace, "UpdateDriver.OnDestroy");

            ResourceModule resource = (ResourceModule)ModuleSystem.GetModule<IResourceModule>();
            resource.PlayMode = EPlayMode.EditorSimulateMode;
            resource.DefaultPackageName = "DefaultPackage";
            resource.Initialize();
            yield return InitializeEditorSimulatePackage();

            GameObject activeInstance = null;
            GameObject pendingDestroyedInstance = null;
            yield return AwaitGameObject(resource.LoadGameObjectAsync(CubeLocation), instance => activeInstance = instance);
            yield return AwaitGameObject(resource.LoadGameObjectAsync(CubeLocation), instance => pendingDestroyedInstance = instance);

            Assert.IsNotNull(activeInstance);
            Assert.IsNotNull(pendingDestroyedInstance);
            pendingDestroyedInstance.SetActive(false);

            ModuleShutdownPhase phaseObservedBeforeReturn = ModuleShutdownPhase.None;
            int poolObjectCountObservedBeforeReturn = -1;
            int spawnCountObservedBeforeReturn = -1;
            int unspawnCountObservedBeforeReturn = -1;
            int handleDisposeCountObservedBeforeReturn = -1;
            bool delayedDestroyRequestedDuringShutdown = false;
            RootModule.BeforeShutdown += () =>
            {
                trace.Add("RootModule.BeforeShutdown");
                phaseObservedBeforeReturn = ModuleSystem.ShutdownPhase;
                ObjectInfo[] poolInfos = resource.GetAssetPoolObjectInfos();
                poolObjectCountObservedBeforeReturn = poolInfos == null ? 0 : poolInfos.Length;
                if (poolInfos != null)
                {
                    for (int i = 0; i < poolInfos.Length; i++)
                    {
                        if (poolInfos[i].Name == resource.GetCacheKey(CubeLocation))
                        {
                            spawnCountObservedBeforeReturn = poolInfos[i].SpawnCount;
                            break;
                        }
                    }
                }

                unspawnCountObservedBeforeReturn = resource.ShutdownAssetUnspawnCount;
                handleDisposeCountObservedBeforeReturn = resource.ShutdownHandleDisposeCount;

                // Destroy 仍是延迟的；在 RootModule.OnDestroy 触发的全局关闭期间标记
                // 未激活实例，验证 owner 主动归还先于真正的 Unity OnDestroy。
                UnityEngine.Object.Destroy(pendingDestroyedInstance);
                delayedDestroyRequestedDuringShutdown = true;
            };

            // 由实际 RootModule 的 Unity 销毁路径触发统一关闭，而不是直接调用协调器。
            UnityEngine.Object.Destroy(rootObject);
            Assert.AreEqual(ModuleSystemState.Running, ModuleSystem.State,
                "Unity Object.Destroy 应在当前帧延迟触发 RootModule.OnDestroy。");
            yield return null;

            for (int i = 0; i < 4 && !trace.Contains("UpdateDriver.OnDestroy"); i++)
            {
                yield return null;
            }

            Assert.AreEqual(ModuleSystemState.Stopped, ModuleSystem.State);
            Assert.AreEqual(ModuleShutdownPhase.Stopped, ModuleSystem.ShutdownPhase);
            Assert.IsNull(ModuleSystem.TryGetExistingModule<IResourceModule>());
            Assert.IsNull(resource.GetAssetPoolObjectInfos(), "对象池关闭后不得留下资源池索引。");
            Assert.IsFalse(YooAssets.Initialized, "本测试会话由资源模块初始化 YooAsset，最终收尾必须销毁它。");
            Assert.AreEqual(ModuleShutdownPhase.BeforeShutdown, phaseObservedBeforeReturn,
                "Root 销毁必须在统一 BeforeShutdown 阶段观察到资源池仍有效。");
            Assert.AreEqual(1, poolObjectCountObservedBeforeReturn,
                "两个实例应共享一个资源池对象条目。");
            Assert.AreEqual(2, spawnCountObservedBeforeReturn,
                "全局关闭开始时两个实例仍各持有一份 spawn，尚未依赖 OnDestroy 归还。");
            Assert.AreEqual(0, unspawnCountObservedBeforeReturn,
                "资源引用归还不得提前于 BeforeShutdown 观察点。");
            Assert.AreEqual(0, handleDisposeCountObservedBeforeReturn,
                "资源池 handle 不得提前于对象池关闭阶段释放。");
            Assert.IsTrue(delayedDestroyRequestedDuringShutdown);
            Assert.AreEqual(2, resource.ShutdownAssetUnspawnCount,
                "活动实例和未激活延迟销毁实例必须各归还一次 spawn。");
            Assert.AreEqual(1, resource.ShutdownHandleDisposeCount,
                "共享资源池条目的 YooAsset handle 必须只 Dispose 一次。");

            int unspawnCountAfterShutdown = resource.ShutdownAssetUnspawnCount;
            int handleDisposeCountAfterShutdown = resource.ShutdownHandleDisposeCount;

            int beforeUpdateDriverDestroy = trace.IndexOf("RootModule.BeforeShutdown");
            int updateDriverDestroy = trace.IndexOf("UpdateDriver.OnDestroy");
            Assert.GreaterOrEqual(beforeUpdateDriverDestroy, 0,
                "实际 RootModule 销毁必须经过 BeforeShutdown。");
            Assert.GreaterOrEqual(updateDriverDestroy, 0,
                "统一关闭必须最终销毁实际 UpdateDriver 宿主。");
            Assert.Less(beforeUpdateDriverDestroy, updateDriverDestroy,
                "RootModule 关闭协调必须先于 UpdateDriver 的延迟 OnDestroy。");
            Assert.IsTrue(trace.Contains("RootObject.OnDestroy"), "Root GameObject 必须实际落地销毁。");

            // Active instance 的 Unity 销毁和 pending inactive instance 的延迟 OnDestroy 都发生在
            // YooAsset/资源池收尾之后；旧组件只能 no-op，不能重复 Unspawn 或重建模块。
            UnityEngine.Object.Destroy(activeInstance);
            yield return null;
            Assert.IsTrue(activeInstance == null);
            Assert.IsTrue(pendingDestroyedInstance == null);
            Assert.AreEqual(unspawnCountAfterShutdown, resource.ShutdownAssetUnspawnCount,
                "延迟 OnDestroy 不得重复归还实例引用。");
            Assert.AreEqual(handleDisposeCountAfterShutdown, resource.ShutdownHandleDisposeCount,
                "延迟 OnDestroy 不得重复 Dispose YooAsset handle。");
            Assert.IsFalse(YooAssets.Initialized);
        }

        private static IEnumerator VerifyFirstIndependentPlaySession()
        {
            Assert.AreEqual(ModuleSystemState.Running, ModuleSystem.State);
            Assert.AreEqual(ModuleShutdownPhase.None, ModuleSystem.ShutdownPhase);
            Assert.IsEmpty(ModuleSystem.ShutdownErrors);

            _firstSessionBeforeShutdownCount = 0;
            _controlledLateCallbackInvocationCount = 0;
            _firstSessionLateRegistrationAttempted = false;
            _controlledLateCallback = ControlledLateCallback;

            GameObject rootObject = new GameObject("[ShutdownLifecycle.Session1Root]");
            RootModule root = rootObject.AddComponent<RootModule>();
            _firstSessionRoot = root;
            _firstSessionCachedDriver = ModuleSystem.GetModule<IUpdateDriver>();
            GameObject firstSessionDriverEntity = GetUpdateDriverEntity(_firstSessionCachedDriver);
            RootModule.BeforeShutdown += FirstSessionBeforeShutdown;

            List<string> unexpectedErrors = new List<string>();
            Application.LogCallback errorHandler = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    unexpectedErrors.Add(condition);
                }
            };
            Application.logMessageReceived += errorHandler;
            try
            {
                yield return null;
                Assert.AreSame(root, RootModule.Instance);

                // 真实 Root 销毁触发第一会话的关闭；回调内部再注册的 listener 必须被拒绝。
                UnityEngine.Object.Destroy(rootObject);
                for (int frame = 0; frame < 8 && (rootObject != null || firstSessionDriverEntity != null); frame++)
                {
                    yield return null;
                }

                Assert.IsTrue(rootObject == null, "第一会话 Root 必须完成延迟销毁。");
                Assert.IsTrue(firstSessionDriverEntity == null, "第一会话 UpdateDriver 宿主必须完成延迟销毁。");
                Assert.AreEqual(ModuleSystemState.Stopped, ModuleSystem.State);
                Assert.AreEqual(ModuleShutdownPhase.Stopped, ModuleSystem.ShutdownPhase);
                Assert.AreEqual(1, _firstSessionBeforeShutdownCount);
                Assert.IsTrue(_firstSessionLateRegistrationAttempted);
                Assert.AreEqual(0, _controlledLateCallbackInvocationCount,
                    "关闭期间新增的受控晚回调不得在第一会话执行。");
                Assert.IsEmpty(ModuleSystem.ShutdownErrors);
            }
            finally
            {
                Application.logMessageReceived -= errorHandler;
                if (rootObject != null)
                {
                    UnityEngine.Object.Destroy(rootObject);
                }
            }

            Assert.IsEmpty(unexpectedErrors,
                "第一会话 Root 关闭不得产生未预期的 Error/Exception/Assert 日志。");
            Debug.Log("[ShutdownLifecycle] independent Play session #1 left a rejected late callback and cached module reference.");
        }

        private static IEnumerator VerifySecondIndependentPlaySession()
        {
            Assert.AreEqual(ModuleSystemState.Running, ModuleSystem.State,
                "SubsystemRegistration 必须把第二会话恢复为 Running。");
            Assert.AreEqual(ModuleShutdownPhase.None, ModuleSystem.ShutdownPhase,
                "第二会话不得带入第一会话的关闭阶段。");
            Assert.IsEmpty(ModuleSystem.ShutdownErrors,
                "第二会话不得带入第一会话的关闭异常。");
            Assert.AreEqual(1, _firstSessionBeforeShutdownCount,
                "第二会话必须看到第一会话确实执行过 Root 关闭订阅。");
            Assert.IsTrue(_firstSessionLateRegistrationAttempted);
            Assert.AreEqual(0, _controlledLateCallbackInvocationCount,
                "第一会话关闭期间注册的晚回调不得泄漏到第二会话。");

            Assert.IsNull(ModuleSystem.TryGetExistingModule<IUpdateDriver>(),
                "第一会话的模块缓存不得在第二会话开始时可查询。");
            Assert.IsNull(RootModule.Instance,
                "第一会话的 Root 静态引用不得在第二会话开始时可查询。");
            Assert.IsTrue(_firstSessionRoot == null,
                "第一会话 Root 的 Unity 引用必须已落地销毁。");
            Assert.IsNotNull(_firstSessionCachedDriver,
                "第一会话必须实际留下过一个受控模块缓存引用供第二会话核对。");

            GameObject secondRootObject = new GameObject("[ShutdownLifecycle.Session2Root]");
            RootModule secondRoot = secondRootObject.AddComponent<RootModule>();
            try
            {
                yield return null;
                Assert.AreSame(secondRoot, RootModule.Instance);
                Assert.IsFalse(ReferenceEquals(_firstSessionRoot, secondRoot),
                    "第二会话不得复用第一会话的 Root 实例。");

                IUpdateDriver secondDriver = ModuleSystem.GetModule<IUpdateDriver>();
                GameObject secondSessionDriverEntity = GetUpdateDriverEntity(secondDriver);
                Assert.IsFalse(ReferenceEquals(_firstSessionCachedDriver, secondDriver),
                    "第二会话不得复用第一会话的模块实例。");

                int secondSessionSubscriptionCount = 0;
                RootModule.BeforeShutdown += () => secondSessionSubscriptionCount++;
                ModuleSystem.Shutdown();

                Assert.AreEqual(ModuleSystemState.Stopped, ModuleSystem.State);
                Assert.AreEqual(1, secondSessionSubscriptionCount,
                    "第二会话的当前订阅必须正常执行，证明事件表不是被旧状态污染。");
                Assert.AreEqual(1, _firstSessionBeforeShutdownCount,
                    "第一会话的关闭订阅不得在第二会话再次执行。");
                Assert.AreEqual(0, _controlledLateCallbackInvocationCount,
                    "第二会话关闭也不得执行第一会话遗留的受控晚回调。");

                for (int frame = 0; frame < 8 && secondSessionDriverEntity != null; frame++)
                {
                    yield return null;
                }

                Assert.IsTrue(secondSessionDriverEntity == null,
                    "第二会话 UpdateDriver 宿主必须完成延迟销毁。");
            }
            finally
            {
                UnityEngine.Object.Destroy(secondRootObject);
            }

            for (int frame = 0; frame < 8 && secondRootObject != null; frame++)
            {
                yield return null;
            }

            Assert.IsTrue(secondRootObject == null, "第二会话 Root 必须完成延迟销毁。");
            Assert.AreEqual(0, _controlledLateCallbackInvocationCount);
            Debug.Log("[ShutdownLifecycle] independent Play session #2 observed clean state and no cross-session callback.");
        }

        private static void FirstSessionBeforeShutdown()
        {
            _firstSessionBeforeShutdownCount++;
            _firstSessionLateRegistrationAttempted = true;
            RootModule.BeforeShutdown += _controlledLateCallback;
        }

        private static void ControlledLateCallback()
        {
            _controlledLateCallbackInvocationCount++;
        }

        private static IEnumerator VerifyUpdateDriverStopsRemainingListener(
            string updateName, Action<IUpdateDriver, Action> addListener)
        {
            Assert.AreEqual(ModuleSystemState.Running, ModuleSystem.State);

            IUpdateDriver driver = ModuleSystem.GetModule<IUpdateDriver>();
            GameObject entity = GetUpdateDriverEntity(driver);
            Assert.IsNotNull(entity, "UpdateDriver 必须创建实际 PlayMode 宿主。");

            bool armed = false;
            bool firstMounted = false;
            bool secondMounted = false;
            int firstInvocations = 0;
            int secondInvocations = 0;

            Action first = () =>
            {
                if (!armed)
                {
                    firstMounted = true;
                    return;
                }

                firstInvocations++;
                ModuleSystem.Shutdown();
            };
            Action second = () =>
            {
                if (!armed)
                {
                    secondMounted = true;
                    return;
                }

                secondInvocations++;
            };

            List<string> unexpectedErrors = new List<string>();
            Application.LogCallback errorHandler = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    unexpectedErrors.Add(condition);
                }
            };
            Application.logMessageReceived += errorHandler;
            try
            {
                addListener(driver, first);
                addListener(driver, second);

                // Add*Listener 通过 UniTask 延迟挂载；只有两个回调都实际被 Unity 入口触发过，
                // 才允许首监听执行 Shutdown，确保它们确实处于同一 invocation list。
                for (int frame = 0; frame < 120 && (!firstMounted || !secondMounted); frame++)
                {
                    yield return null;
                }

                Assert.IsTrue(firstMounted, $"{updateName} 首监听未完成异步挂载。");
                Assert.IsTrue(secondMounted, $"{updateName} 后续监听未完成异步挂载。");

                armed = true;
                for (int frame = 0; frame < 120 && firstInvocations == 0; frame++)
                {
                    yield return null;
                }

                Assert.AreEqual(1, firstInvocations,
                    $"{updateName} 首监听必须在真实 Unity 消息中执行一次。");
                Assert.AreEqual(0, secondInvocations,
                    $"{updateName} 首监听退出后不得执行后续监听。");
                Assert.AreEqual(ModuleSystemState.Stopped, ModuleSystem.State);

                // UpdateDriver.Shutdown 使用 Destroy；等待实际 OnDestroy 落地，避免再次制造
                // 非运行模式 Destroy 错误或把延迟销毁误当成同步完成。
                for (int frame = 0; frame < 8 && entity != null; frame++)
                {
                    yield return null;
                }

                Assert.IsTrue(entity == null, $"{updateName} 测试宿主必须完成延迟销毁。");
            }
            finally
            {
                Application.logMessageReceived -= errorHandler;
                if (ModuleSystem.IsRunning)
                {
                    ModuleSystem.Shutdown();
                }

                if (entity != null)
                {
                    UnityEngine.Object.Destroy(entity);
                }
            }

            Assert.IsEmpty(unexpectedErrors,
                $"{updateName} 监听短路和延迟销毁不得产生未预期的 Error/Exception/Assert 日志。");
        }

        private static GameObject GetUpdateDriverEntity(IUpdateDriver updateDriver)
        {
            FieldInfo entityField = updateDriver.GetType().GetField("_entity", BindingFlags.Instance | BindingFlags.NonPublic);
            return entityField?.GetValue(updateDriver) as GameObject;
        }

        private sealed class DestroyTraceProbe : MonoBehaviour
        {
            private System.Collections.Generic.List<string> _trace;
            private string _label;

            public void Configure(System.Collections.Generic.List<string> trace, string label)
            {
                _trace = trace;
                _label = label;
            }

            private void OnDestroy()
            {
                _trace?.Add(_label);
            }
        }

        private static IEnumerator InitializeEditorSimulatePackage()
        {
            ResourcePackage package = YooAssets.GetPackage("DefaultPackage");
            Assert.IsNotNull(package);

            // 全量运行时 ResourceLifecycle 套件可能已初始化该包；
            // YooAsset 禁止重复 InitializeAsync，已就绪则直接复用。
            if (package.InitializeStatus == EOperationStatus.Succeed)
            {
                yield break;
            }

            var buildResult = EditorSimulateModeHelper.SimulateBuild("DefaultPackage");
            var createParameters = new EditorSimulateModeParameters
            {
                EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory)
            };
            InitializationOperation initializeOperation = package.InitializeAsync(createParameters);
            while (!initializeOperation.IsDone)
            {
                yield return null;
            }

            Assert.AreEqual(EOperationStatus.Succeed, initializeOperation.Status, initializeOperation.Error);

            RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync();
            while (!versionOperation.IsDone)
            {
                yield return null;
            }

            Assert.AreEqual(EOperationStatus.Succeed, versionOperation.Status, versionOperation.Error);

            UpdatePackageManifestOperation manifestOperation =
                package.UpdatePackageManifestAsync(versionOperation.PackageVersion);
            while (!manifestOperation.IsDone)
            {
                yield return null;
            }

            Assert.AreEqual(EOperationStatus.Succeed, manifestOperation.Status, manifestOperation.Error);
        }

        private static IEnumerator AwaitGameObject(UniTask<GameObject> task, Action<GameObject> callback)
        {
            Exception error = null;
            yield return task.ToCoroutine(callback, exception => error = exception);
            Assert.IsNull(error, error?.ToString());
        }
    }
}
