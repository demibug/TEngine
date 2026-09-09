using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;

namespace TEngine.ResourceLifecycleTests
{
    /// <summary>
    /// 资源生命周期 PlayMode 测试：基于 YooAsset EditorSimulate 与真实 GameObject 生命周期。
    /// 用计数增量核对引用，而不是只断言“没有抛错”。
    /// </summary>
    public sealed class ResourceLifecyclePlayModeTests
    {
        /// <summary>Assets/AssetRaw/Actor/Cube.prefab 的资源地址（AddressByFileName）。</summary>
        private const string CubeLocation = "Cube";

        private static IResourceModule _module;
        private static MonoBehaviour _yooDriver;
        private static bool _bootstrapped;
        private static bool _moduleInitialized;
        private static bool _packageInitialized;

        private readonly List<GameObject> _trackedInstances = new List<GameObject>();
        private readonly List<UnityEngine.Object> _trackedSpawns = new List<UnityEngine.Object>();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // 真正的引导在 UnitySetUp 中执行（需要逐帧等待）。
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!_bootstrapped)
            {
                yield return Bootstrap();
                _bootstrapped = true;
            }

            if (_yooDriver != null)
            {
                _yooDriver.enabled = true;
            }

            // 释放上一用例遗留的 0-spawn 缓存条目，保证各用例从“无 Cube 缓存条目”开始。
            Inner().ReleaseUnusedPoolAssetsForDiagnostics();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_yooDriver != null)
            {
                _yooDriver.enabled = true;
            }

            foreach (var instance in _trackedInstances)
            {
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }

            _trackedInstances.Clear();

            foreach (var spawn in _trackedSpawns)
            {
                if (spawn != null)
                {
                    _module.UnloadAsset(spawn);
                }
            }

            _trackedSpawns.Clear();
            yield return null; // 让 OnDestroy 归还落地
            Inner().ReleaseUnusedPoolAssetsForDiagnostics();
        }

        private static IEnumerator Bootstrap()
        {
            // 分阶段幂等引导：任一阶段失败重试时跳过已完成阶段。
            if (!_moduleInitialized)
            {
                // 模块与对象池（不依赖 GameEntry 引导）。
                _module = ModuleSystem.GetModule<IResourceModule>();
                _module.Initialize();
                _module.AssetAutoReleaseInterval = float.MaxValue;
                _module.AssetExpireTime = float.MaxValue;
                _module.AssetCapacity = int.MaxValue;
                _moduleInitialized = true;
            }

            if (!_packageInitialized)
            {
                // YooAsset EditorSimulate 初始化（独立于 InitPackage 的 EditorPrefs 读取）。
                ResourcePackage package = YooAssets.TryGetPackage("DefaultPackage") ?? YooAssets.CreatePackage("DefaultPackage");
                YooAssets.SetDefaultPackage(package);
                var buildResult = EditorSimulateModeHelper.SimulateBuild("DefaultPackage");
                var createParameters = new EditorSimulateModeParameters();
                createParameters.EditorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory);
                InitializationOperation initOperation = package.InitializeAsync(createParameters);
                while (!initOperation.IsDone)
                {
                    yield return null;
                }

                Assert.AreEqual(EOperationStatus.Succeed, initOperation.Status, "EditorSimulate 初始化必须成功。");

                // 与 ProcedureInitResources 一致：请求版本并激活清单，否则无法加载。
                var versionOperation = package.RequestPackageVersionAsync();
                while (!versionOperation.IsDone)
                {
                    yield return null;
                }

                Assert.AreEqual(EOperationStatus.Succeed, versionOperation.Status, "请求模拟版本必须成功。");

                var manifestOperation = package.UpdatePackageManifestAsync(versionOperation.PackageVersion);
                while (!manifestOperation.IsDone)
                {
                    yield return null;
                }

                Assert.AreEqual(EOperationStatus.Succeed, manifestOperation.Status, "激活模拟清单必须成功。");
                _packageInitialized = true;
            }

            _yooDriver = FindYooAssetsDriver();
            Assert.IsNotNull(_yooDriver, "YooAssets.Initialize 后必须存在 YooAssetsDriver。");
        }

        private static ResourceModule Inner() => (ResourceModule)_module;

        private static MonoBehaviour FindYooAssetsDriver()
        {
            // YooAssetsDriver 为 internal 类型，无法按类型查找；其宿主 GameObject 名称唯一。
            GameObject host = GameObject.Find("[YooAssets]");
            return host != null ? host.GetComponentInChildren<MonoBehaviour>(true) : null;
        }

        /// <summary>资源定位地址 → 资源池缓存键（与生产 GetCacheKey 保持一致）。</summary>
        private static string KeyOf(string location) => Inner().GetCacheKey(location);

        private static int SpawnCountOf(string location)
        {
            string cacheKey = KeyOf(location);
            ObjectInfo[] infos = Inner().GetAssetPoolObjectInfos();
            ObjectInfo info = infos?.FirstOrDefault(i => i.Name == cacheKey) ?? default;
            return info.Name == null ? 0 : info.SpawnCount;
        }

        private static bool MarkerContains(string location) => Inner()._assetLoadingList.Contains(KeyOf(location));

        /// <summary>
        /// 强制冷却 Cube 的 provider（销毁零引用 provider），确保下一次加载真实经历异步过程。
        /// </summary>
        private static IEnumerator ForceColdProvider()
        {
            Inner().ReleaseUnusedPoolAssetsForDiagnostics();
            ResourcePackage package = YooAssets.GetPackage("DefaultPackage");
            var unloadOperation = package.UnloadUnusedAssetsAsync();
            while (!unloadOperation.IsDone)
            {
                yield return null;
            }
        }

        private static bool PoolHasKey(string location)
        {
            string cacheKey = KeyOf(location);
            ObjectInfo[] infos = Inner().GetAssetPoolObjectInfos();
            return infos != null && infos.Any(i => i.Name == cacheKey);
        }

        private GameObject Track(GameObject instance)
        {
            _trackedInstances.Add(instance);
            return instance;
        }

        private static IEnumerator AwaitGO(UniTask<GameObject> task, Action<GameObject> onResult)
        {
            yield return task.ToCoroutine(onResult, ex => Assert.Fail($"Unexpected exception: {ex.Message}"));
        }

        private static IEnumerator AwaitObj(UniTask<UnityEngine.Object> task, Action<UnityEngine.Object> onResult)
        {
            yield return task.ToCoroutine(onResult, ex => Assert.Fail($"Unexpected exception: {ex.Message}"));
        }

        private static IEnumerator Await(UniTask task)
        {
            Exception fault = null;
            yield return task.ToCoroutine(ex => fault = ex);
            Assert.IsNull(fault, $"Unexpected exception: {fault?.Message}");
        }

        // ------------------------------------------------------------------
        // 1. 成功交付：一次成功加载对应一份持有；销毁实例配对归还。
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator LoadGameObjectAsync_Success_DeliversOneReference_AndDestroyRefunds()
        {
            int before = SpawnCountOf(CubeLocation);

            GameObject instance = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { instance = value; Track(value); });

            Assert.IsNotNull(instance);
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation), "一次成功加载必须恰好对应一份 spawn。");

            UnityEngine.Object.Destroy(instance);
            _trackedInstances.Remove(instance);
            yield return null;

            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "实例销毁必须配对归还 spawn。");
        }

        [UnityTest]
        public IEnumerator TwoInstancesSharePrefab_DestroyOneDoesNotReleaseTheOther()
        {
            int before = SpawnCountOf(CubeLocation);

            GameObject a = null, b = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { a = value; Track(value); });
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { b = value; Track(value); });

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(before + 2, SpawnCountOf(CubeLocation), "两个实例共享 prefab，各自持有一份 spawn。");

            UnityEngine.Object.Destroy(a);
            _trackedInstances.Remove(a);
            yield return null;

            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation), "销毁一个实例不得释放另一个实例的持有。");
            Assert.IsNotNull(b, "另一个实例必须存活。");

            UnityEngine.Object.Destroy(b);
            _trackedInstances.Remove(b);
            yield return null;

            Assert.AreEqual(before, SpawnCountOf(CubeLocation));
        }

        // ------------------------------------------------------------------
        // 2. 多调用者取消隔离。
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MultiCaller_WaiterCancel_Isolated()
        {
            int before = SpawnCountOf(CubeLocation);
            yield return ForceColdProvider();

            var ctsA = new CancellationTokenSource();
            UniTask<GameObject> taskA = _module.LoadGameObjectAsync(CubeLocation, cancellationToken: ctsA.Token);

            // 同帧断言：taskA 已同步执行到首个 await，标记必须已领取。
            Assert.IsTrue(MarkerContains(CubeLocation), "A 应持有加载标记。");

            _yooDriver.enabled = false; // 冻结 YooAsset：A 保持加载中

            var ctsB = new CancellationTokenSource();
            UniTask<GameObject> taskB = _module.LoadGameObjectAsync(CubeLocation, cancellationToken: ctsB.Token);

            var ctsC = new CancellationTokenSource();
            UniTask<GameObject> taskC = _module.LoadGameObjectAsync(CubeLocation, cancellationToken: ctsC.Token);
            yield return null; // B、C 进入等待

            ctsB.Cancel();

            GameObject b = null;
            yield return AwaitGO(taskB, value => b = value);
            Assert.IsNull(b, "等待者取消返回 null。");
            Assert.IsTrue(MarkerContains(CubeLocation), "B 取消不得清除 A 的加载标记。");

            _yooDriver.enabled = true;

            GameObject a = null;
            yield return AwaitGO(taskA, value => { a = value; Track(value); });
            Assert.IsNotNull(a, "B 取消不影响 A。");

            GameObject c = null;
            yield return AwaitGO(taskC, value => { c = value; Track(value); });
            Assert.IsNotNull(c, "另一个等待者 C 不受 B 取消影响。");
            Assert.AreEqual(before + 2, SpawnCountOf(CubeLocation), "A、C 各一份 spawn。");

            UnityEngine.Object.Destroy(a);
            UnityEngine.Object.Destroy(c);
            _trackedInstances.Clear();
            yield return null;
            Assert.AreEqual(before, SpawnCountOf(CubeLocation));
        }

        [UnityTest]
        public IEnumerator OwnerCancel_MidLoad_ClearsMarker_AndRetrySucceeds()
        {
            int before = SpawnCountOf(CubeLocation);
            yield return ForceColdProvider();

            var cts = new CancellationTokenSource();
            UniTask<GameObject> taskA = _module.LoadGameObjectAsync(CubeLocation, cancellationToken: cts.Token);

            // 同帧断言：标记已领取，随后冻结保持加载中。
            Assert.IsTrue(MarkerContains(CubeLocation));
            _yooDriver.enabled = false;

            cts.Cancel();

            GameObject a = null;
            yield return AwaitGO(taskA, value => a = value);
            Assert.IsNull(a, "owner 交付前取消返回 null。");
            Assert.IsFalse(MarkerContains(CubeLocation), "owner 取消后必须清除自己拥有的标记。");
            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "未交付资源不得遗留持有。");

            _yooDriver.enabled = true;

            GameObject c = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { c = value; Track(value); });
            Assert.IsNotNull(c, "失败/取消后同 key 再次加载可成功。");
        }

        [UnityTest]
        public IEnumerator PreCanceledToken_EndsBeforeCacheCheck()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            GameObject result = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation, cancellationToken: cts.Token), value => result = value);

            Assert.IsNull(result);
            Assert.IsFalse(MarkerContains(CubeLocation), "预取消不得注册加载标记。");
        }

        [UnityTest]
        public IEnumerator CacheSpawn_CancelBeforeDelivery_RefundsSpawn()
        {
            // 先填充缓存（load → destroy → 条目保留、spawn 归零）。
            int before = SpawnCountOf(CubeLocation);

            GameObject first = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { first = value; Track(value); });
            UnityEngine.Object.Destroy(first);
            _trackedInstances.Remove(first);
            yield return null;
            Assert.AreEqual(before, SpawnCountOf(CubeLocation));

            // 第二次加载：命中缓存领取 spawn → 同帧取消 → 交付前取消。
            var cts = new CancellationTokenSource();
            UniTask<GameObject> task = _module.LoadGameObjectAsync(CubeLocation, cancellationToken: cts.Token);
            cts.Cancel(); // 同帧取消（spawn 已领取，交付尚未发生）

            GameObject result = null;
            yield return AwaitGO(task, value => result = value);
            Assert.IsNull(result, "交付前取消不交付。");
            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "缓存 Yield/交付前取消必须立即归还这一份 spawn。");
        }

        // ------------------------------------------------------------------
        // 3. 同步入口与异步冲突。
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SyncLoad_ConflictWithInFlightAsync_Throws_ThenSucceedsAfterAsync()
        {
            int before = SpawnCountOf(CubeLocation);
            yield return ForceColdProvider();

            UniTask<GameObject> taskA = _module.LoadGameObjectAsync(CubeLocation);

            // 同帧：A 已持有标记并挂起，冻结保持加载中。
            _yooDriver.enabled = false;

            Assert.Throws<GameFrameworkException>(
                () => _module.LoadAsset<GameObject>(CubeLocation),
                "同步入口撞上未完成异步加载必须明确失败。");
            Assert.Throws<GameFrameworkException>(
                () => _module.LoadGameObject(CubeLocation),
                "同步 LoadGameObject 撞上未完成异步加载必须明确失败。");
            Assert.IsTrue(MarkerContains(CubeLocation), "冲突后不得发生双登记（标记仍只有一份）。");

            _yooDriver.enabled = true;

            GameObject a = null;
            yield return AwaitGO(taskA, value => { a = value; Track(value); });
            Assert.IsNotNull(a, "冲突异常不得影响异步加载者。");

            GameObject sync = Track(_module.LoadGameObject(CubeLocation));
            Assert.IsNotNull(sync, "异步完成后同步入口走缓存。");
            Assert.AreEqual(before + 2, SpawnCountOf(CubeLocation));
        }

        // ------------------------------------------------------------------
        // 4. 失败与类型安全。
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NonexistentLocation_ReturnsNull_AndRemainsRetryable()
        {
            const string missing = "Definitely_Not_Exist_XYZ";

            LogAssert.Expect(LogType.Error, new Regex("Could not found location"));

            UnityEngine.Object result = null;
            yield return AwaitObj(_module.LoadAssetAsync(missing, typeof(GameObject)), value => result = value);
            Assert.IsNull(result, "正常资源不存在返回 null。");
            Assert.IsFalse(MarkerContains(missing), "失败后标记必须清除。");

            LogAssert.Expect(LogType.Error, new Regex("Could not found location"));
            UnityEngine.Object retry = null;
            yield return AwaitObj(_module.LoadAssetAsync(missing, typeof(GameObject)), value => retry = value);
            Assert.IsNull(retry, "失败后再次加载不得卡死或抛出未收尾异常。");
            Assert.IsFalse(MarkerContains(missing));
        }

        [UnityTest]
        public IEnumerator CachedTypeMismatch_NoWrongDelivery_NoReferenceResidue()
        {
            int before = SpawnCountOf(CubeLocation);

            // 注意：LoadAsset 返回的是源资产（prefab 本体），只能 UnloadAsset 归还，不能 Destroy。
            GameObject seed = null;
            yield return AwaitGO(_module.LoadAssetAsync<GameObject>(CubeLocation), value => { seed = value; _trackedSpawns.Add(value); });
            Assert.IsNotNull(seed);
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation));

            LogAssert.Expect(LogType.Error, new Regex("not assignable"));

            // 错误类型请求：缓存命中但类型不符 → 归还本次 spawn → 失败。
            Texture wrong = null;
            UniTask<Texture> wrongTask = _module.LoadAssetAsync<Texture>(CubeLocation);
            yield return wrongTask.ToCoroutine(value => wrong = value, ex => Assert.Fail($"Unexpected exception: {ex.Message}"));
            Assert.IsNull(wrong, "类型不符不得错误交付。");
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation), "类型不符不得残留引用（本次 spawn 已归还）。");

            GameObject again = null;
            yield return AwaitGO(_module.LoadAssetAsync<GameObject>(CubeLocation), value => { again = value; _trackedSpawns.Add(value); });
            Assert.IsNotNull(again, "正确类型仍可正常领取。");
            Assert.AreEqual(before + 2, SpawnCountOf(CubeLocation));
        }

        // ------------------------------------------------------------------
        // 5. 回调入口：单终态与回调异常可见。
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator CallbackApi_SuccessNotifiesOnce_NoFailure()
        {
            int before = SpawnCountOf(CubeLocation);

            int successCount = 0;
            int failureCount = 0;
            GameObject delivered = null;
            var callbacks = new LoadAssetCallbacks(
                (name, asset, duration, userData) => { successCount++; delivered = asset as GameObject; },
                (name, status, message, userData) => failureCount++);

            _module.LoadAssetAsync(CubeLocation, typeof(GameObject), 0, callbacks, null);
            yield return null;
            yield return null;

            Assert.AreEqual(1, successCount, "成功只通知一次。");
            Assert.AreEqual(0, failureCount, "不得产生失败通知。");
            Assert.IsNotNull(delivered);

            // 回调交付的是源资产：接收方负责归还，避免污染后续用例。
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation));
            _trackedSpawns.Add(delivered);
            _module.UnloadAsset(delivered);
            Assert.AreEqual(before, SpawnCountOf(CubeLocation));
            _trackedSpawns.Remove(delivered);
        }

        [UnityTest]
        public IEnumerator CallbackApi_FailureNotifiesOnce()
        {
            const string missing = "Definitely_Not_Exist_Callback";
            int successCount = 0;
            int failureCount = 0;
            var callbacks = new LoadAssetCallbacks(
                (name, asset, duration, userData) => successCount++,
                (name, status, message, userData) => failureCount++);

            LogAssert.Expect(LogType.Error, new Regex("Could not found location"));

            _module.LoadAssetAsync(missing, typeof(GameObject), 0, callbacks, null);
            yield return null;
            yield return null;

            Assert.AreEqual(0, successCount);
            Assert.AreEqual(1, failureCount, "失败只通知一次。");
            Assert.IsFalse(MarkerContains(missing), "失败后标记清除。");
        }

        [UnityTest]
        public IEnumerator CallbackApi_BusinessThrow_DoesNotTriggerFailure_DeliveredAssetRefundable()
        {
            int before = SpawnCountOf(CubeLocation);

            int successCount = 0;
            int failureCount = 0;
            GameObject delivered = null;
            var callbacks = new LoadAssetCallbacks(
                (name, asset, duration, userData) =>
                {
                    successCount++;
                    delivered = asset as GameObject;
                    throw new InvalidOperationException("business error");
                },
                (name, status, message, userData) => failureCount++);

            LogAssert.Expect(LogType.Exception, new Regex("business error"));

            _module.LoadAssetAsync(CubeLocation, typeof(GameObject), 0, callbacks, null);
            yield return null;
            yield return null;

            Assert.AreEqual(1, successCount, "成功回调恰好一次（即使抛错）。");
            Assert.AreEqual(0, failureCount, "业务成功回调抛错不得触发第二次“加载失败”。");
            Assert.IsNotNull(delivered, "已交付资源不因回调异常被撤销。");

            // 回调收到的是源资产（prefab 本体）：已移交资源仍由接收方（本测试）UnloadAsset 归还。
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation));
            _trackedSpawns.Add(delivered);
            _module.UnloadAsset(delivered);
            Assert.AreEqual(before, SpawnCountOf(CubeLocation));
            _trackedSpawns.Remove(delivered);
        }

        [UnityTest]
        public IEnumerator ProgressCallbackThrow_StopsObserver_WithoutBlockingLoad()
        {
            int before = SpawnCountOf(CubeLocation);

            int progressCalls = 0;
            int successCount = 0;
            int failureCount = 0;
            GameObject delivered = null;
            var callbacks = new LoadAssetCallbacks(
                (name, asset, duration, userData) => { successCount++; delivered = asset as GameObject; },
                (name, status, message, userData) => failureCount++,
                (name, progress, userData) =>
                {
                    progressCalls++;
                    throw new InvalidOperationException("progress boom");
                });

            // 进度回调抛错被观察并以 Error 记录，观察者随即停止。
            LogAssert.Expect(LogType.Error, new Regex("progress boom"));

            yield return ForceColdProvider();

            // 先启动加载，再冻结驱动保证进度观察者至少执行一次（加载保持未完成状态）。
            _module.LoadAssetAsync(CubeLocation, typeof(GameObject), 0, callbacks, null);
            _yooDriver.enabled = false;
            yield return null;
            yield return null;
            _yooDriver.enabled = true;

            // 有界等待加载终态（观察者已停止，加载不应被卡死）。
            int settleFrames = 0;
            while (successCount == 0 && failureCount == 0 && settleFrames < 30)
            {
                settleFrames++;
                yield return null;
            }

            Assert.AreEqual(1, successCount, "进度回调抛错不得卡住加载。");
            Assert.AreEqual(0, failureCount);
            Assert.AreEqual(1, progressCalls, "进度观察者抛错后必须停止，不得继续轮询。");

            Assert.IsNotNull(delivered);
            _trackedSpawns.Add(delivered);
            _module.UnloadAsset(delivered);
            _trackedSpawns.Remove(delivered);
        }

        // ------------------------------------------------------------------
        // 6. 克隆与 AssetsReference 清理。
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ExternalClone_DoesNotInheritOwnership_NoDoubleRelease()
        {
            int before = SpawnCountOf(CubeLocation);

            GameObject original = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { original = value; Track(value); });
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation));

            GameObject clone = Track(UnityEngine.Object.Instantiate(original));
            yield return null; // 克隆 Awake 摘除拷贝的引用记录
            Assert.IsNotNull(clone.GetComponent<AssetsReference>(), "克隆保留组件但不继承持有权。");

            UnityEngine.Object.Destroy(clone);
            _trackedInstances.Remove(clone);
            yield return null;
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation), "克隆销毁不得归还原实例的 spawn。");

            Assert.IsNotNull(original, "原实例存活。");
            UnityEngine.Object.Destroy(original);
            _trackedInstances.Remove(original);
            yield return null;
            Assert.AreEqual(before, SpawnCountOf(CubeLocation));
        }

        [UnityTest]
        public IEnumerator AssetsReference_ForeignEntryRelease_ThrowsButContinuesOthers()
        {
            int before = SpawnCountOf(CubeLocation);

            GameObject instance = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { instance = value; Track(value); });
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation));

            // 附加一个不在池中的“外来”持有：清理时应记录异常但继续其余条目。
            // ResourceModule.UnloadAsset 内部捕获归还异常并记日志（不再传播到 AssetsReference 的 catch）。
            GameObject foreign = new GameObject("ForeignAssetObject");
            Track(foreign);
            instance.GetComponent<AssetsReference>().Ref<UnityEngine.Object>(foreign, _module);

            LogAssert.Expect(LogType.Error, new Regex("UnloadAsset failed during cleanup"));

            UnityEngine.Object.Destroy(instance);
            _trackedInstances.Remove(instance);
            yield return null;

            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "一条归还异常不得跳过其余条目（源 prefab 仍须归还）。");
        }

        // ------------------------------------------------------------------
        // 7. PRELOAD：真实配对归还 + 可正常池回收。
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator PreloadRunner_RealModule_PairedReturn_AndPoolRecyclable()
        {
            int before = SpawnCountOf(CubeLocation);
            bool hadKeyBefore = PoolHasKey(CubeLocation);

            var runner = new PreloadRequestRunner(_module);
            runner.Begin(new[] { CubeLocation, CubeLocation }); // 重复地址去重
            Assert.AreEqual(1, runner.Generation, "Begin 推进代际。");

            while (!runner.AllTerminal)
            {
                yield return null;
            }

            Assert.AreEqual(1f, runner.Progress, 0.0001f);
            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "预热成功后必须配对归还预加载引用。");

            // 资产留在池中，可正常池回收：归还后释放未使用条目。
            Inner().ReleaseUnusedPoolAssetsForDiagnostics();
            if (!hadKeyBefore)
            {
                Assert.IsFalse(PoolHasKey(CubeLocation), "归还后的预热资源可正常池回收。");
            }
        }

        // ------------------------------------------------------------------
        // 8. SLAVE_FIX 补充：无接收者交付、未激活克隆/实例、手动补偿、null 类型、异常可见。
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NullCallbackLoad_NoHeldDelivery_ColdAndCacheHit()
        {
            int before = SpawnCountOf(CubeLocation);
            yield return ForceColdProvider();

            // 冷加载 + null 回调：无成功接收者 → 不发起持有性交付（资产仅预热登记）。
            _module.LoadAsset<GameObject>(CubeLocation, (Action<GameObject>)null);
            yield return null;
            yield return null;
            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "冷加载无接收者不得遗留 spawn。");

            // 缓存命中路径 + null 回调：同样不得遗留持有。
            _module.LoadAsset<GameObject>(CubeLocation, (Action<GameObject>)null);
            yield return null;
            yield return null;
            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "缓存命中无接收者不得遗留 spawn。");
        }

        [UnityTest]
        public IEnumerator InactiveClone_RebindBeforeActivation_NoDoubleReturn()
        {
            // 原始实例：source 绑定 + 资产列表绑定，各持有一份 spawn；外加本测试自持一份。
            GameObject original = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { original = value; Track(value); });

            GameObject listedAsset = null;
            yield return AwaitGO(_module.LoadAssetAsync<GameObject>(CubeLocation), value => listedAsset = value);
            original.GetComponent<AssetsReference>().Ref<UnityEngine.Object>(listedAsset, _module);

            GameObject independentlyOwnedAsset = null;
            yield return AwaitGO(_module.LoadAssetAsync<GameObject>(CubeLocation), value =>
            {
                independentlyOwnedAsset = value;
                _trackedSpawns.Add(value);
            });

            int total = SpawnCountOf(CubeLocation);
            Assert.AreEqual(3, total, "source 绑定 + 列表绑定 + 本测试自持 = 3 份 spawn。");

            // 先让 prefab 处于 inactive，再克隆；这样克隆在 Ref 前确实尚未执行 Awake。
            original.SetActive(false);
            GameObject clone = Track(UnityEngine.Object.Instantiate(original));
            Assert.IsFalse(clone.activeSelf, "inactive prefab 的克隆在绑定前必须保持未激活。");
            original.SetActive(true);

            // 激活前重新绑定：绑定入口必须主动摘除复制残留，只保留重绑定的新持有。
            GameObject reboundAsset = null;
            yield return AwaitGO(_module.LoadAssetAsync<GameObject>(CubeLocation), value => reboundAsset = value);
            clone.GetComponent<AssetsReference>().Ref(reboundAsset, _module);

            UnityEngine.Object.Destroy(clone); // 从未激活即销毁
            _trackedInstances.Remove(clone);
            yield return null;

            // 未激活即销毁的实例不保证收到 OnDestroy，归还依赖 ResourceModule.Update
            // 的补偿扫描；本套件无 GameEntry 引导，必须手动泵一帧（同 DeliveredInactiveDestroy）。
            ModuleSystem.Update(0.016f, 0.016f);

            Assert.AreEqual(total, SpawnCountOf(CubeLocation),
                "未激活克隆激活前重绑定后销毁：只归还重绑定的新持有，复制的残留记录不得重复归还。");

            Assert.IsNotNull(independentlyOwnedAsset, "独立持有的资产应留给本测试在 TearDown 中归还。");
        }

        [UnityTest]
        public IEnumerator DeliveredInactiveDestroy_RefundsOnce_AndManualCompensation_NoDouble()
        {
            // 1. 从 inactive prefab 实例化：克隆不会执行 Awake，交付后未激活即销毁也不得遗留持有。
            GameObject inactivePrefab = null;
            yield return AwaitGO(_module.LoadAssetAsync<GameObject>(CubeLocation), value =>
            {
                inactivePrefab = value;
                _trackedSpawns.Add(value);
            });
            inactivePrefab.SetActive(false);

            GameObject instance = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { instance = value; Track(value); });
            Assert.IsFalse(instance.activeSelf, "inactive prefab 交付的实例必须保持未激活。");
            int withInstance = SpawnCountOf(CubeLocation);

            UnityEngine.Object.Destroy(instance);
            _trackedInstances.Remove(instance);
            yield return null;

            // 本套件无 GameEntry 引导，生产环境由 RootModule.Update 每帧驱动的
            // ModuleSystem.Update 在这里必须手动泵一帧：未激活即销毁实例的
            // 补偿扫描位于 ResourceModule.Update（AssetsReference 不保证收到 OnDestroy）。
            ModuleSystem.Update(0.016f, 0.016f);

            Assert.AreEqual(withInstance - 1, SpawnCountOf(CubeLocation), "未激活实例销毁必须归还，不得遗留 spawn。");

            inactivePrefab.SetActive(true);
            _module.UnloadAsset(inactivePrefab);
            _trackedSpawns.Remove(inactivePrefab);

            // 2. 直接调用绑定后取消补偿路径，验证它也不依赖 OnDestroy。
            GameObject cancellationPrefab = null;
            yield return AwaitGO(_module.LoadAssetAsync<GameObject>(CubeLocation), value => cancellationPrefab = value);
            cancellationPrefab.SetActive(false);
            int beforeCancellation = SpawnCountOf(CubeLocation);
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            GameObject canceledInstance = null;
            yield return AwaitGO(Inner().InstantiateAndDeliverAsync(cancellationPrefab, null, cancellation.Token), value => canceledInstance = value);
            Assert.IsNull(canceledInstance, "绑定后取消不得交付 inactive 实例。");
            Assert.AreEqual(beforeCancellation - 1, SpawnCountOf(CubeLocation), "绑定后取消必须由 owner 归还 prefab spawn 一次。");
            cancellationPrefab.SetActive(true);

            // 3. 手动补偿原语：摘除绑定 + 手动归还 → 全程恰好一次（OnDestroy 不再重复归还）。
            GameObject second = null;
            yield return AwaitGO(_module.LoadGameObjectAsync(CubeLocation), value => { second = value; Track(value); });
            int before2 = SpawnCountOf(CubeLocation);

            second.GetComponent<AssetsReference>().DetachWithoutRelease();
            UnityEngine.Object.Destroy(second);
            _trackedInstances.Remove(second);

            // 手动归还这一份 spawn（生产取消路径的等价操作）：
            // DetachWithoutRelease 摘除后 second 的 source spawn 无人跟踪，须由调用方补偿归还；
            // 再领取一份引用后归还两份——一份是本次领取的，一份是摘除后的孤儿持有。
            GameObject prefabAsset = null;
            yield return AwaitGO(_module.LoadAssetAsync<GameObject>(CubeLocation), value => { prefabAsset = value; _trackedSpawns.Add(value); });
            _module.UnloadAsset(prefabAsset);
            _trackedSpawns.Remove(prefabAsset);
            _module.UnloadAsset(prefabAsset);
            yield return null;

            Assert.AreEqual(before2 - 1, SpawnCountOf(CubeLocation), "摘除+手动归还必须恰好一次（不得双重归还或遗留）。");
        }

        [UnityTest]
        public IEnumerator UnexpectedException_PropagatesWithCleanup_Visible()
        {
            // 注入非预期异常：CheckLocationValid 内不存在的包 → NullReferenceException，必须保持可见。
            LogAssert.Expect(LogType.Error, new Regex("Can not found resource package"));

            Exception fault = null;
            UniTask<GameObject> task = _module.LoadAssetAsync<GameObject>(CubeLocation, CancellationToken.None, "NoSuchPkg_XYZ");
            yield return task.ToCoroutine(null, ex => fault = ex);

            Assert.IsNotNull(fault, "非预期异常必须传播保持可见，不得转换为 null。");
            Assert.IsFalse(MarkerContains(CubeLocation), "异常路径必须已收尾（无加载标记残留）。");
        }

        [UnityTest]
        public IEnumerator NullTypeNormalized_And_SyncColdWrongType_NoResidue()
        {
            int before = SpawnCountOf(CubeLocation);

            // null 类型 → 规范化为 Object → 正常交付，不得先领 spawn 后抛异常泄漏。
            UnityEngine.Object delivered = null;
            yield return AwaitObj(_module.LoadAssetAsync(CubeLocation, null), value => delivered = value);
            Assert.IsNotNull(delivered, "null 类型规范化后应正常交付。");
            Assert.AreEqual(before + 1, SpawnCountOf(CubeLocation));
            _module.UnloadAsset(delivered);
            yield return null;
            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "null 类型交付不得泄漏。");
            _trackedSpawns.Remove(delivered);

            // 同步冷加载错误类型：登记前完成类型校验 → null 且无登记残留。
            // 第一部分遗留的 0-spawn Cube 条目会使命中池缓存（日志变为 Cached asset ...），
            // 先冷却走真实冷加载路径，才能覆盖"加载失败"分支而非"缓存类型不匹配"分支。
            yield return ForceColdProvider();
            // 冷加载类型不匹配产生两条 Error：YooAsset 内部日志（经 ResourceLogger 重定向）
            // 与 TEngine 包装日志（"Load asset ... failed"），两条都必须被 Expect 覆盖。
            LogAssert.Expect(LogType.Error, new Regex("Failed to load asset object"));
            LogAssert.Expect(LogType.Error, new Regex("Load asset 'Cube' failed"));
            Material wrong = _module.LoadAsset<Material>(CubeLocation);
            Assert.IsNull(wrong, "同步冷加载错误类型不得登记或交付。");
            Assert.AreEqual(before, SpawnCountOf(CubeLocation), "同步冷加载错误类型不得残留池对象。");

            // 恢复 Cube provider 健康（同步失败 provider 会驻留，防止污染后续用例）。
            yield return ForceColdProvider();
        }
    }
}
