using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using YooAsset;

namespace TEngine.ResourceLifecycleTests
{
    /// <summary>
    /// 可控回调的假资源模块：记录请求与归还，用于验证 PreloadRequestRunner 行为。
    /// </summary>
    internal sealed class FakeResourceModule : IResourceModule
    {
        public sealed class PendingRequest
        {
            public string Location;
            public LoadAssetSuccessCallback Success;
            public LoadAssetFailureCallback Failure;
            public UniTaskCompletionSource<UnityEngine.Object> Completion;

            public void CompleteSuccess(object asset, float duration = 0.1f)
            {
                if (Completion != null)
                {
                    Completion.TrySetResult((UnityEngine.Object)asset);
                    return;
                }

                Success?.Invoke(Location, asset, duration, null);
            }

            public void CompleteFailure(string message, LoadResourceStatus status = LoadResourceStatus.NotReady)
            {
                if (Completion != null)
                {
                    Completion.TrySetResult(null);
                    return;
                }

                Failure?.Invoke(Location, status, message, null);
            }

            public void CompleteException(Exception exception)
            {
                Completion.TrySetException(exception);
            }
        }

        public readonly List<PendingRequest> Requests = new List<PendingRequest>();
        public readonly List<object> UnloadedAssets = new List<object>();
        public int UnloadCallCount => UnloadedAssets.Count;

        /// <summary>注入点：对该地址的 LoadAssetAsync 同步抛错（验证单地址异常不卡死预热）。</summary>
        public string ThrowOnLoadAddress;

        public void LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (!string.IsNullOrEmpty(ThrowOnLoadAddress) && location == ThrowOnLoadAddress)
            {
                throw new GameFrameworkException($"injected sync failure for '{location}'");
            }

            var request = new PendingRequest { Location = location };
            Requests.Add(request);

            if (loadAssetCallbacks != null)
            {
                request.Success = loadAssetCallbacks.LoadAssetSuccessCallback;
                request.Failure = loadAssetCallbacks.LoadAssetFailureCallback;
            }
        }

        public void UnloadAsset(object asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            UnloadedAssets.Add(asset);
        }

        public void CompleteRequest(int index, object asset) => Requests[index].CompleteSuccess(asset);
        public void FailRequest(int index, string message) => Requests[index].CompleteFailure(message);
        public void ThrowRequestAsync(int index, Exception exception) => Requests[index].CompleteException(exception);

        // —— 以下成员与本组测试无关 ——
        public string ApplicableGameVersion => string.Empty;
        public int InternalResourceVersion => 0;
        public EPlayMode PlayMode { get; set; }
        public EncryptionType EncryptionType { get; set; }
        public bool UpdatableWhilePlaying { get; set; }
        public int DownloadingMaxNum { get; set; }
        public int FailedTryAgain { get; set; }
        public void Initialize() { }
        public UniTask WaitUntilInitializedAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        public UniTask<InitializationOperation> InitPackage(string customPackageName, bool needInitMainFest = false) => UniTask.FromResult<InitializationOperation>(null);
        public string DefaultPackageName { get; set; } = "DefaultPackage";
        public long Milliseconds { get; set; }
        public bool AutoUnloadBundleWhenUnused { get; set; }
        public string HostServerURL { get; set; }
        public string FallbackHostServerURL { get; set; }
        public LoadResWayWebGL LoadResWayWebGL { get; set; }
        public float AssetAutoReleaseInterval { get; set; }
        public int AssetCapacity { get; set; }
        public float AssetExpireTime { get; set; }
        public int AssetPriority { get; set; }
        public void UnloadUnusedAssets() { }
        public void ForceUnloadAllAssets() { }
        public void ForceUnloadUnusedAssets(bool performGCCollect) { }
        public HasAssetResult HasAsset(string location, string packageName = "") => HasAssetResult.NotExist;
        public bool CheckLocationValid(string location, string packageName = "") => false;
        public AssetInfo[] GetAssetInfos(string resTag, string packageName = "") => Array.Empty<AssetInfo>();
        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "") => Array.Empty<AssetInfo>();
        public AssetInfo GetAssetInfo(string location, string packageName = "") => default;
        public void LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "") { }
        public T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object => null;
        public UnityEngine.Object LoadAsset(string location, Type assetType, string packageName = "") => null;
        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "") => null;
        public UniTaskVoid LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object
        {
            callback?.Invoke(null);
            return default;
        }
        public UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object => UniTask.FromResult<T>(null);
        public UniTask<UnityEngine.Object> LoadAssetAsync(string location, Type assetType, CancellationToken cancellationToken = default, string packageName = "")
        {
            if (!string.IsNullOrEmpty(ThrowOnLoadAddress) && location == ThrowOnLoadAddress)
            {
                throw new GameFrameworkException($"injected sync failure for '{location}'");
            }

            var request = new PendingRequest
            {
                Location = location,
                Completion = new UniTaskCompletionSource<UnityEngine.Object>()
            };
            Requests.Add(request);
            return request.Completion.Task;
        }
        public UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "") => UniTask.FromResult<GameObject>(null);
        public AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object => null;
        public AssetHandle LoadAssetSyncHandle(string location, Type type, string packageName = "") => null;
        public AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object => null;
        public AssetHandle LoadAssetAsyncHandle(string location, Type assetType, string packageName = "") => null;
        public ClearCacheFilesOperation ClearCacheFilesAsync(EFileClearMode clearMode = EFileClearMode.ClearUnusedBundleFiles, string customPackageName = "") => null;
        public void ClearAllBundleFiles(string customPackageName = "") { }
        public ResourceDownloaderOperation Downloader { get; set; }
        public ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "") => null;
        public string PackageVersion { get; set; }
        public string GetPackageVersion(string customPackageName = "") => string.Empty;
        public RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "") => null;
        public UpdatePackageManifestOperation UpdatePackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "") => null;
        public void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer) { }
        public void OnLowMemory() { }
        public void SetForceUnloadUnusedAssetsAction(Action<bool> action) { }
    }

    /// <summary>
    /// ResourceLifecycle EditMode 单测基类：显式接管框架会话状态。
    /// <remarks>
    /// EditMode 测试运行在编辑器主域，ModuleSystem 是跨测试共享的静态单例；
    /// PreloadRequestRunner 依赖 ModuleSystem.IsRunning 决定是否发起请求，
    /// 若域内残留非 Running 状态，Begin 会静默不发请求。这里在前后用框架的
    /// 显式会话重置点（ResetForNewSession）恢复 Running，保证纯单元测试可孤立复现。
    /// </remarks>
    /// </summary>
    public abstract class ResourceLifecycleTestBase
    {
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
            ResetForNewSession();
        }

        private static void ResetForNewSession()
        {
            Assert.IsNotNull(ResetForNewSessionMethod, "测试必须能调用框架的显式会话重置点。");
            ResetForNewSessionMethod.Invoke(null, null);
        }
    }

    /// <summary>
    /// PreloadRequestRunner 行为测试：去重、配对归还、终态进度、代际隔离、重入。
    /// </summary>
    public sealed class PreloadRequestRunnerTests : ResourceLifecycleTestBase
    {
        private FakeResourceModule _module;
        private int _allCompletedCount;
        private readonly List<UnityEngine.Object> _createdAssets = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            _module = new FakeResourceModule();
            _allCompletedCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object asset in _createdAssets)
            {
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }

            _createdAssets.Clear();
        }

        private PreloadRequestRunner CreateRunner()
        {
            return new PreloadRequestRunner(_module, () => _allCompletedCount++);
        }

        private GameObject CreateAsset()
        {
            GameObject asset = new GameObject("PreloadTestAsset");
            _createdAssets.Add(asset);
            return asset;
        }

        [Test]
        public void Begin_DedupesOverlappingAddresses_SingleRequestPerAddress()
        {
            var runner = CreateRunner();
            runner.Begin(new[] { "A", "B", "A", "B", "A" });

            Assert.AreEqual(2, _module.Requests.Count, "重叠地址只应发起一次加载。");
            Assert.AreEqual(1, runner.Generation, "Begin 推进一代。");
        }

        [Test]
        public void Success_ReturnsPreloadSpawn_PairedUnloadAndTerminalProgress()
        {
            var runner = CreateRunner();
            runner.Begin(new[] { "A", "B" });
            Assert.AreEqual(0f, runner.Progress);

            GameObject assetA = CreateAsset();
            GameObject assetB = CreateAsset();
            _module.CompleteRequest(0, assetA);

            Assert.AreEqual(1, _module.UnloadCallCount, "成功回调必须配对归还预加载自己的 spawn。");
            Assert.IsTrue(_module.UnloadedAssets.Contains(assetA));
            Assert.AreEqual(0.5f, runner.Progress, 0.0001f);
            Assert.IsFalse(runner.AllTerminal);
            Assert.AreEqual(0, _allCompletedCount);

            _module.CompleteRequest(1, assetB);
            Assert.AreEqual(2, _module.UnloadCallCount);
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
            Assert.IsTrue(runner.AllTerminal);
            Assert.AreEqual(1, _allCompletedCount, "全部终态回调只触发一次。");
        }

        [Test]
        public void Failure_MarkedTerminal_LogsAndContinues()
        {
            var runner = CreateRunner();
            runner.Begin(new[] { "A", "B" });

            _module.FailRequest(0, "boom");
            Assert.AreEqual(1, runner.FailedLocations.Count);
            Assert.IsTrue(runner.FailedLocations.Contains("A"));
            Assert.AreEqual(0.5f, runner.Progress, 0.0001f);
            Assert.AreEqual(0, _allCompletedCount);

            _module.CompleteRequest(1, CreateAsset());
            Assert.AreEqual(1, _allCompletedCount, "预热尽力而为：失败也是终态，全部终态后继续。");
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
        }

        [Test]
        public void LateSuccess_AfterInvalidate_StillReturnsReference_ButIgnoresState()
        {
            var runner = CreateRunner();
            runner.Begin(new[] { "A" });

            runner.Invalidate();
            GameObject asset = CreateAsset();
            _module.CompleteRequest(0, asset);

            Assert.AreEqual(1, _module.UnloadCallCount, "晚到成功仍须归还资源。");
            Assert.AreEqual(0f, runner.Progress, 0.0001f, "晚到成功不得更新新流程状态。");
            Assert.IsFalse(runner.AllTerminal);
            Assert.AreEqual(0, _allCompletedCount, "晚到成功不得触发跳转。");
        }

        [Test]
        public void Reentry_NewGeneration_IsolatedFromOldLateCallbacks()
        {
            var runner = CreateRunner();
            runner.Begin(new[] { "A" });

            // 离开再进入。
            runner.Invalidate();
            runner.Begin(new[] { "A" });
            Assert.AreEqual(3, runner.Generation, "Begin 与 Invalidate 各推进一代。");

            // 旧代际晚到成功。
            _module.CompleteRequest(0, CreateAsset());

            Assert.AreEqual(1, _module.UnloadCallCount);
            Assert.AreEqual(0f, runner.Progress, 0.0001f, "旧代晚回调不得污染新流程。");
            Assert.AreEqual(0, _allCompletedCount);

            // 新代际请求正常完成。
            _module.CompleteRequest(1, CreateAsset());
            Assert.AreEqual(2, _module.UnloadCallCount);
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
            Assert.IsTrue(runner.AllTerminal);
            Assert.AreEqual(1, _allCompletedCount);
        }

        [Test]
        public void Reentry_FailedThenRetried_FreshState()
        {
            var runner = CreateRunner();
            runner.Begin(new[] { "A" });
            _module.FailRequest(0, "boom");
            Assert.AreEqual(1, runner.FailedLocations.Count);

            runner.Invalidate();
            runner.Begin(new[] { "A" });

            Assert.AreEqual(0, runner.FailedLocations.Count, "重入后失败状态清零。");
            _module.CompleteRequest(1, CreateAsset());
            Assert.IsTrue(runner.AllTerminal);
        }

        [Test]
        public void StartRequest_SyncThrow_MarksFailedTerminal_ContinuesOthers()
        {
            // 单地址同步抛错：不得中断其余条目的发起，也不得让全部终态/完成回调卡死。
            _module.ThrowOnLoadAddress = "A";
            var runner = CreateRunner();
            runner.Begin(new[] { "A", "B" });

            Assert.AreEqual(1, _module.Requests.Count, "A 同步抛错不得中断 B 的发起（仅 B 发起成功）。");
            Assert.AreEqual(1, runner.FailedLocations.Count);
            Assert.IsTrue(runner.FailedLocations.Contains("A"));
            Assert.IsFalse(runner.AllTerminal, "B 仍在等待时，A 的同步抛错不能伪造全部完成。");
            Assert.AreEqual(0.5f, runner.Progress, 0.0001f);
            Assert.AreEqual(0, _allCompletedCount);

            _module.CompleteRequest(0, CreateAsset());
            Assert.IsTrue(runner.AllTerminal, "同步抛错记失败终态后，其他请求仍须完成并推动流程终态。");
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
            Assert.AreEqual(1, _allCompletedCount);
        }

        [Test]
        public void StartRequest_AsyncThrow_MarksFailedTerminal_ContinuesOthers()
        {
            var runner = CreateRunner();
            runner.Begin(new[] { "A", "B" });

            _module.ThrowRequestAsync(0, new InvalidOperationException("injected async failure"));

            Assert.AreEqual(1, runner.FailedLocations.Count);
            Assert.IsTrue(runner.FailedLocations.Contains("A"));
            Assert.IsFalse(runner.AllTerminal, "B 尚未完成时，异步异常只能结束 A。");
            Assert.AreEqual(0.5f, runner.Progress, 0.0001f);
            Assert.AreEqual(0, _allCompletedCount);

            _module.CompleteRequest(1, CreateAsset());

            Assert.IsTrue(runner.AllTerminal, "异步异常必须成为失败终态，不能让预加载永久等待。");
            Assert.AreEqual(1f, runner.Progress, 0.0001f);
            Assert.AreEqual(1, _allCompletedCount);
        }
    }

    public sealed class AssetsReferenceTests : ResourceLifecycleTestBase
    {
        [Test]
        public void MaterialBindings_RegisterOriginalAndUnloadEachRendererReference()
        {
            FakeResourceModule module = new FakeResourceModule();
            Shader shader = Shader.Find("Standard");
            Assert.IsNotNull(shader, "测试需要可创建 Material 的内置 Shader。");

            Material material = new Material(shader);
            GameObject spriteObject = new GameObject("SpriteMaterialOwner");
            GameObject meshObject = new GameObject("MeshMaterialOwner");

            try
            {
                SpriteRenderer spriteRenderer = spriteObject.AddComponent<SpriteRenderer>();
                MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
                spriteRenderer.sharedMaterial = material;
                meshRenderer.sharedMaterial = material;

                AssetsReference.Ref(material, spriteObject, module);
                AssetsReference.Ref(material, meshObject, module);

                UnityEngine.Object.DestroyImmediate(spriteObject);
                UnityEngine.Object.DestroyImmediate(meshObject);

                // EditMode 下普通 MonoBehaviour 不执行事件函数（OnDestroy 不会触发），
                // 生产环境由 ResourceModule.Update 轮询的补偿扫描归还；此处显式触发同一扫描。
                AssetsReference.ReleaseDestroyedReferences();

                Assert.AreEqual(2, module.UnloadCallCount,
                    "SpriteRenderer/MeshRenderer 的 Ref<T> 必须登记原始实例并各归还一份材质持有。");
                Assert.AreSame(material, module.UnloadedAssets[0]);
                Assert.AreSame(material, module.UnloadedAssets[1]);
            }
            finally
            {
                if (spriteObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(spriteObject);
                }

                if (meshObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(meshObject);
                }

                if (material != null)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        [Test]
        public void InactiveNeverActivatedReference_IsReleasedByFallbackSweep()
        {
            FakeResourceModule module = new FakeResourceModule();
            Shader shader = Shader.Find("Standard");
            Assert.IsNotNull(shader, "测试需要可创建 Material 的内置 Shader。");

            Material material = new Material(shader);
            GameObject owner = new GameObject("InactiveMaterialOwner");
            owner.SetActive(false);

            try
            {
                AssetsReference.Ref(material, owner, module);
                UnityEngine.Object.DestroyImmediate(owner);

                AssetsReference.ReleaseDestroyedReferences();

                Assert.AreEqual(1, module.UnloadCallCount,
                    "从未激活的绑定对象销毁后，补偿扫描必须恰好归还一次。");
            }
            finally
            {
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }

                if (material != null)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }
    }
}
