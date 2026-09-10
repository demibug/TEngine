using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
using WeChatWASM;
#endif

namespace TEngine
{
    /// <summary>
    /// 资源管理器。
    /// </summary>
    internal sealed partial class ResourceModule : Module, IResourceModule, IUpdateModule, IPinnedManifestIntegrity
    {
        private PinnedManifestIntegrityVerifier _pinnedManifestVerifier;
        /// <summary>
        /// 默认资源包名称。
        /// </summary>
        public string DefaultPackageName { get; set; } = "DefaultPackage";

        /// <summary>
        /// 资源系统运行模式。
        /// </summary>
        public EPlayMode PlayMode { get; set; } = EPlayMode.OfflinePlayMode;

        public EncryptionType EncryptionType { get; set; } = EncryptionType.None;

        /// <summary>
        /// 设置异步系统参数，每帧执行消耗的最大时间切片（单位：毫秒）
        /// </summary>
        public long Milliseconds { get; set; } = 30;

        /// <summary>
        /// 自动释放资源引用计数为0的资源包
        /// </summary>
        public bool AutoUnloadBundleWhenUnused { get; set; } = false;

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        public override int Priority => 4;

        public override void OnInit()
        {
            _pinnedManifestVerifier = null;
            _stopping = false;
            _poolClosed = false;
            _shutdownComplete = false;
            _ownsYooAssets = false;
            _shutdownAssetUnspawnCount = 0;
            _shutdownHandleDisposeCount = 0;
            _lifetimeCancellation = new CancellationTokenSource();
        }

        /// <summary>
        /// 资源模块轮询。
        /// <remarks>补偿从未激活即销毁的已交付实例；这类实例不保证触发 AssetsReference.OnDestroy。</remarks>
        /// </summary>
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (_stopping || !ModuleSystem.IsRunning)
            {
                return;
            }

            AssetsReference.ReleaseDestroyedReferences();
        }

        public override void Shutdown()
        {
            // 直接关闭 ResourceModule 时也遵守同一所有权顺序；通常由 ModuleSystem 分阶段调用。
            BeginShutdown();
            ReleaseOwnedInstances();
            MarkAssetPoolClosed();
            FinalizeShutdown();
        }

        internal bool IsAcceptingRequests => !_stopping && !_shutdownComplete && ModuleSystem.IsRunning;

        /// <summary>
        /// 关闭阶段实际从资源对象池归还的 spawn 数量（测试/诊断用）。
        /// </summary>
        internal int ShutdownAssetUnspawnCount => _shutdownAssetUnspawnCount;

        /// <summary>
        /// 关闭阶段实际 Dispose 的资源池 handle 数量（测试/诊断用）。
        /// </summary>
        internal int ShutdownHandleDisposeCount => _shutdownHandleDisposeCount;

        private int _shutdownAssetUnspawnCount;
        private int _shutdownHandleDisposeCount;

        internal void RecordShutdownHandleDispose()
        {
            if (ModuleSystem.IsShuttingDown)
            {
                _shutdownHandleDisposeCount++;
            }
        }

        internal CancellationToken LifetimeToken => _lifetimeCancellation != null
            ? _lifetimeCancellation.Token
            : CancellationToken.None;

        internal void BeginShutdown()
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            try
            {
                _lifetimeCancellation?.Cancel();
            }
            catch (Exception exception)
            {
                LogErrorSafely("Resource lifetime cancellation failed: {0}", exception);
            }

            _initializationCompletionSource.TrySetCanceled();
            var contextSnapshot = new List<PackageInitializationContext>(_packageInitializationContexts.Values);
            foreach (PackageInitializationContext context in contextSnapshot)
            {
                try
                {
                    context.ActiveCompletion?.TrySetCanceled();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Resource package waiter cancellation failed: {0}", exception);
                }
            }
        }

        internal void ReleaseOwnedInstances()
        {
            AssetsReference.ReleaseOwnedReferences(this);
        }

        internal void MarkAssetPoolClosed()
        {
            _poolClosed = true;
        }

        internal void FinalizeShutdown()
        {
            if (_shutdownComplete)
            {
                return;
            }

            _shutdownComplete = true;
            _stopping = true;
            try
            {
                try
                {
                    _lifetimeCancellation?.Cancel();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Resource lifetime cancellation failed during finalization: {0}", exception);
                }

                _forceUnloadUnusedAssetsAction = null;
                _pendingForceUnloadUnusedAssetsAction = null;
                _assetLoadingList.Clear();
                _packageInitializationContexts.Clear();
                PackageMap.Clear();
                DefaultPackage = null;
                Downloader = null;
                _hasPendingObjectPoolConfiguration = false;

                if (_ownsYooAssets && YooAssets.Initialized)
                {
                    YooAssets.Destroy();
                }
            }
            finally
            {
                _ownsYooAssets = false;
                _assetPool = null;
                try
                {
                    _lifetimeCancellation?.Dispose();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Resource lifetime cancellation disposal failed: {0}", exception);
                }

                _lifetimeCancellation = null;
            }
        }

        private void EnsureAcceptingRequests()
        {
            if (!IsAcceptingRequests)
            {
                throw new GameFrameworkException("Resource module is shutting down and cannot accept new requests.");
            }
        }

        /// <summary>
        /// 资源服务器地址。
        /// </summary>
        public string HostServerURL { get; set; }

        public string FallbackHostServerURL { get; set; }

        /// <summary>
        /// WebGL：加载资源方式
        /// </summary>
        public LoadResWayWebGL LoadResWayWebGL { get; set; }

        private string _applicableGameVersion;

        private int _internalResourceVersion;

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public string ApplicableGameVersion => _applicableGameVersion;

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public int InternalResourceVersion => _internalResourceVersion;

        /// <summary>
        /// 当前最新的包裹版本。
        /// </summary>
        public string PackageVersion { set; get; }

        public int DownloadingMaxNum { get; set; }

        public int FailedTryAgain { get; set; }

        /// <summary>
        /// 是否支持边玩边下载。
        /// </summary>
        public bool UpdatableWhilePlaying { get; set; }

        #region internal

        /// <summary>
        /// 默认资源包。
        /// </summary>
        internal ResourcePackage DefaultPackage { private set; get; }

        /// <summary>
        /// 资源包列表。
        /// </summary>
        private Dictionary<string, ResourcePackage> PackageMap { get; } = new Dictionary<string, ResourcePackage>();

        private enum InitializationState
        {
            NotStarted,
            Initializing,
            Succeeded,
            Failed,
        }

        private sealed class PackageInitializationParameters : IEquatable<PackageInitializationParameters>
        {
            public bool NeedInitMainFest;
            public EPlayMode PlayMode;
            public EncryptionType EncryptionType;
            public bool AutoUnloadBundleWhenUnused;
            public LoadResWayWebGL LoadResWayWebGL;
            public string HostServerURL;
            public string FallbackHostServerURL;

            public bool Equals(PackageInitializationParameters other)
            {
                if (other == null)
                {
                    return false;
                }

                return NeedInitMainFest == other.NeedInitMainFest &&
                       PlayMode == other.PlayMode &&
                       EncryptionType == other.EncryptionType &&
                       AutoUnloadBundleWhenUnused == other.AutoUnloadBundleWhenUnused &&
                       LoadResWayWebGL == other.LoadResWayWebGL &&
                       string.Equals(HostServerURL, other.HostServerURL, StringComparison.Ordinal) &&
                       string.Equals(FallbackHostServerURL, other.FallbackHostServerURL, StringComparison.Ordinal);
            }
        }

        private sealed class PackageInitializationContext
        {
            public string PackageName;
            public ResourcePackage Package;
            public PackageInitializationParameters Parameters;
            public InitializationOperation InitializationOperation;
            public bool ManifestReady;
            public UniTaskCompletionSource<InitializationOperation> ActiveCompletion;
        }

        private InitializationState _initializationState;
        private Exception _initializationException;
        private readonly UniTaskCompletionSource _initializationCompletionSource = new UniTaskCompletionSource();
        private readonly Dictionary<string, PackageInitializationContext> _packageInitializationContexts =
            new Dictionary<string, PackageInitializationContext>();
        private bool _hasPendingObjectPoolConfiguration;
        private float _pendingAssetAutoReleaseInterval;
        private int _pendingAssetCapacity;
        private float _pendingAssetExpireTime;
        private int _pendingAssetPriority;
        private Action<bool> _pendingForceUnloadUnusedAssetsAction;

        /// <summary>
        /// 资源请求生命周期。关闭第一阶段取消等待者；池和 YooAsset 驱动仍按统一阶段延后关闭。
        /// </summary>
        private CancellationTokenSource _lifetimeCancellation;
        private bool _stopping;
        private bool _poolClosed;
        private bool _shutdownComplete;
        private bool _ownsYooAssets;

        /// <summary>
        /// 资源信息列表。
        /// </summary>
        private readonly Dictionary<string, AssetInfo> _assetInfoMap = new Dictionary<string, AssetInfo>();

        #endregion

        public void Initialize()
        {
            EnsureAcceptingRequests();

            if (_initializationState == InitializationState.Succeeded)
            {
                // 重复调用只重新确认默认包指向，不重复创建 YooAsset 驱动或资源池。
                if (DefaultPackage != null)
                {
                    YooAssets.SetDefaultPackage(DefaultPackage);
                }

                return;
            }

            if (_initializationState == InitializationState.Failed)
            {
                Log.Error($"ResourceModule initialization already failed: {_initializationException}");
                return;
            }

            if (_initializationState == InitializationState.Initializing)
            {
                return;
            }

            _initializationState = InitializationState.Initializing;

            try
            {
                // 初始化资源系统
                _ownsYooAssets = !YooAssets.Initialized;
                YooAssets.Initialize(new ResourceLogger());
                YooAssets.SetOperationSystemMaxTimeSlice(Milliseconds);

                // 创建默认的资源包。包已经存在时也必须重新设置 YooAsset 默认包指针。
                string packageName = DefaultPackageName;
                if (string.IsNullOrEmpty(packageName))
                {
                    throw new InvalidOperationException("Resource package name is null or empty.");
                }

                var defaultPackage = YooAssets.TryGetPackage(packageName);
                if (defaultPackage == null)
                {
                    defaultPackage = YooAssets.CreatePackage(packageName);
                }

                YooAssets.SetDefaultPackage(defaultPackage);
                DefaultPackage = defaultPackage;

                IObjectPoolModule objectPoolManager = ModuleSystem.GetModule<IObjectPoolModule>();
                SetObjectPoolModule(objectPoolManager);
                ApplyPendingObjectPoolConfiguration();

                _initializationState = InitializationState.Succeeded;
                _initializationCompletionSource.TrySetResult();
            }
            catch (Exception exception)
            {
                FailInitialization(exception);
                throw;
            }
        }

        /// <summary>
        /// 在 bootstrap 完成前登记 Driver 的对象池参数，确保参数校验属于同一初始化终态。
        /// </summary>
        internal void ConfigureObjectPoolForInitialization(
            float assetAutoReleaseInterval,
            int assetCapacity,
            float assetExpireTime,
            int assetPriority,
            Action<bool> forceUnloadUnusedAssetsAction)
        {
            EnsureAcceptingRequests();
            _pendingAssetAutoReleaseInterval = assetAutoReleaseInterval;
            _pendingAssetCapacity = assetCapacity;
            _pendingAssetExpireTime = assetExpireTime;
            _pendingAssetPriority = assetPriority;
            _pendingForceUnloadUnusedAssetsAction = forceUnloadUnusedAssetsAction;
            _hasPendingObjectPoolConfiguration = true;
        }

        private void ApplyPendingObjectPoolConfiguration()
        {
            if (!_hasPendingObjectPoolConfiguration)
            {
                return;
            }

            AssetAutoReleaseInterval = _pendingAssetAutoReleaseInterval;
            AssetCapacity = _pendingAssetCapacity;
            AssetExpireTime = _pendingAssetExpireTime;
            AssetPriority = _pendingAssetPriority;
            SetForceUnloadUnusedAssetsAction(_pendingForceUnloadUnusedAssetsAction);
            _hasPendingObjectPoolConfiguration = false;
        }

        /// <summary>
        /// 等待资源模块 bootstrap 完成。
        /// <remarks>调用者取消只取消当前等待者，不会取消共享的 bootstrap。</remarks>
        /// </summary>
        public UniTask WaitUntilInitializedAsync(CancellationToken cancellationToken = default)
        {
            if (_stopping || !ModuleSystem.IsRunning)
            {
                return UniTask.FromCanceled();
            }

            if (_initializationState == InitializationState.Succeeded)
            {
                return UniTask.CompletedTask;
            }

            UniTask initializationTask = _initializationCompletionSource.Task;
            return cancellationToken.CanBeCanceled
                ? initializationTask.AttachExternalCancellation(cancellationToken)
                : initializationTask;
        }

        /// <summary>
        /// 记录驱动器在参数配置或 Initialize 前失败的 bootstrap 终态。
        /// </summary>
        internal void FailInitialization(Exception exception)
        {
            if (_initializationState == InitializationState.Succeeded ||
                _initializationState == InitializationState.Failed)
            {
                return;
            }

            _initializationException = exception ?? new InvalidOperationException("ResourceModule initialization failed.");
            _initializationState = InitializationState.Failed;
            _initializationCompletionSource.TrySetException(_initializationException);
            Log.Error($"ResourceModule initialization failed: {_initializationException}");
        }

        public UniTask<InitializationOperation> InitPackage(string packageName, bool needInitMainFest = false)
        {
            if (!IsAcceptingRequests)
            {
                return UniTask.FromCanceled<InitializationOperation>();
            }

            if (string.IsNullOrEmpty(packageName))
            {
                return UniTask.FromException<InitializationOperation>(
                    new ArgumentException("Resource package name is null or empty.", nameof(packageName)));
            }

            if (_initializationState != InitializationState.Succeeded)
            {
                return UniTask.FromException<InitializationOperation>(
                    _initializationException ?? new InvalidOperationException("ResourceModule is not initialized."));
            }

            PackageInitializationParameters parameters = CapturePackageInitializationParameters(needInitMainFest);
            if (_packageInitializationContexts.TryGetValue(packageName, out PackageInitializationContext context))
            {
                if (!context.Parameters.Equals(parameters))
                {
                    return UniTask.FromException<InitializationOperation>(
                        new InvalidOperationException(
                            $"Conflicting initialization parameters for package '{packageName}'. " +
                            "The existing request must finish before a request with different parameters is allowed."));
                }

                if (context.ActiveCompletion != null)
                {
                    return context.ActiveCompletion.Task;
                }

                if (context.InitializationOperation != null &&
                    context.InitializationOperation.Status == EOperationStatus.Succeed &&
                    (!context.Parameters.NeedInitMainFest || context.ManifestReady))
                {
                    // 成功重复调用返回有效的已完成操作，而不是 null。
                    return UniTask.FromResult(context.InitializationOperation);
                }

                // 包初始化已成功但 manifest 请求失败：复用同一个 YooAsset 包，
                // 只重试 manifest，不再次调用 InitializeAsync。
                return BeginPackageInitialization(context);
            }

            ResourcePackage package = YooAssets.TryGetPackage(packageName);
            if (package == null)
            {
                package = YooAssets.CreatePackage(packageName);
            }

            context = new PackageInitializationContext
            {
                PackageName = packageName,
                Package = package,
                Parameters = parameters,
            };
            PackageMap[packageName] = package;
            _packageInitializationContexts.Add(packageName, context);

            return BeginPackageInitialization(context);
        }

        private PackageInitializationParameters CapturePackageInitializationParameters(bool needInitMainFest)
        {
#if UNITY_EDITOR
            EPlayMode playMode = (EPlayMode)UnityEditor.EditorPrefs.GetInt("EditorPlayMode");
            Log.Warning($"Editor Module Used :{playMode}");
#else
            EPlayMode playMode = PlayMode;
#endif

            return new PackageInitializationParameters
            {
                NeedInitMainFest = needInitMainFest,
                PlayMode = playMode,
                EncryptionType = EncryptionType,
                AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused,
                LoadResWayWebGL = LoadResWayWebGL,
                HostServerURL = HostServerURL,
                FallbackHostServerURL = FallbackHostServerURL,
            };
        }

        private UniTask<InitializationOperation> BeginPackageInitialization(PackageInitializationContext context)
        {
            UniTaskCompletionSource<InitializationOperation> completionSource =
                new UniTaskCompletionSource<InitializationOperation>();
            context.ActiveCompletion = completionSource;
            InitializePackageAsync(context, completionSource).Forget();
            return completionSource.Task;
        }

        private async UniTaskVoid InitializePackageAsync(
            PackageInitializationContext context,
            UniTaskCompletionSource<InitializationOperation> completionSource)
        {
            using var linkedLifetime = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken);
            CancellationToken lifetimeToken = linkedLifetime.Token;
            try
            {
                lifetimeToken.ThrowIfCancellationRequested();
                if (context.InitializationOperation == null)
                {
                    context.InitializationOperation = CreatePackageInitializationOperation(context);
                    if (context.InitializationOperation == null)
                    {
                        throw new InvalidOperationException(
                            $"YooAsset returned a null initialization operation for package '{context.PackageName}'.");
                    }

                    await context.InitializationOperation.ToUniTask().AttachExternalCancellation(lifetimeToken);
                    Log.Info($"Init resource package version : {context.InitializationOperation.Status}");
                }

                if (context.InitializationOperation.Status != EOperationStatus.Succeed)
                {
                    throw new GameFrameworkException(
                        $"Init resource package '{context.PackageName}' failed: {context.InitializationOperation.Error}");
                }

                if (context.Parameters.NeedInitMainFest && !context.ManifestReady)
                {
                    await InitializePackageManifestAsync(context, lifetimeToken);
                    context.ManifestReady = true;
                }

                lifetimeToken.ThrowIfCancellationRequested();

                context.ActiveCompletion = null;
                completionSource.TrySetResult(context.InitializationOperation);
            }
            catch (OperationCanceledException) when (!IsAcceptingRequests || lifetimeToken.IsCancellationRequested)
            {
                _packageInitializationContexts.Remove(context.PackageName);
                context.ActiveCompletion = null;
                completionSource.TrySetCanceled();
            }
            catch (Exception exception)
            {
                bool packageInitializationSucceeded = context.InitializationOperation != null &&
                    context.InitializationOperation.Status == EOperationStatus.Succeed;

                if (!packageInitializationSucceeded)
                {
                    // YooAsset 失败操作允许下一次 InitializeAsync 重试；删除缓存的 active context，
                    // 但保留 PackageMap 中的包对象供 YooAsset 的失败重置协议使用。
                    _packageInitializationContexts.Remove(context.PackageName);
                }
                else
                {
                    // 初始化已成功且不可回滚；只允许下次重试 manifest。
                    context.ManifestReady = false;
                }

                context.ActiveCompletion = null;
                completionSource.TrySetException(exception);
            }
        }

        private InitializationOperation CreatePackageInitializationOperation(PackageInitializationContext context)
        {
            PackageInitializationParameters parameters = context.Parameters;
            ResourcePackage package = context.Package;

            // 编辑器下的模拟模式
            if (parameters.PlayMode == EPlayMode.EditorSimulateMode)
            {
                var buildResult = EditorSimulateModeHelper.SimulateBuild(context.PackageName);
                var packageRoot = buildResult.PackageRootDirectory;
                var createParameters = new EditorSimulateModeParameters
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot),
                    AutoUnloadBundleWhenUnused = parameters.AutoUnloadBundleWhenUnused,
                };
                return package.InitializeAsync(createParameters);
            }

            IDecryptionServices decryptionServices = CreateDecryptionServices(parameters.EncryptionType);

            // 单机运行模式
            if (parameters.PlayMode == EPlayMode.OfflinePlayMode)
            {
                var createParameters = new OfflinePlayModeParameters
                {
                    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices),
                    AutoUnloadBundleWhenUnused = parameters.AutoUnloadBundleWhenUnused,
                };
                return package.InitializeAsync(createParameters);
            }

            // 联机运行模式
            if (parameters.PlayMode == EPlayMode.HostPlayMode)
            {
                IRemoteServices remoteServices = new RemoteServices(parameters.HostServerURL, parameters.FallbackHostServerURL);
                var createParameters = new HostPlayModeParameters
                {
                    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices),
                    CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, decryptionServices),
                    AutoUnloadBundleWhenUnused = parameters.AutoUnloadBundleWhenUnused,
                };
                if (_pinnedManifestVerifier != null)
                {
                    createParameters.BuildinFileSystemParameters.AddParameter(
                        FileSystemParametersDefine.MANIFEST_SERVICES, _pinnedManifestVerifier);
                    createParameters.CacheFileSystemParameters.AddParameter(
                        FileSystemParametersDefine.MANIFEST_SERVICES, _pinnedManifestVerifier);
                }
                return package.InitializeAsync(createParameters);
            }

            // WebGL运行模式
            if (parameters.PlayMode == EPlayMode.WebPlayMode)
            {
                var createParameters = new WebPlayModeParameters();
                IWebDecryptionServices webDecryptionServices = CreateWebDecryptionServices(parameters.EncryptionType);
                IRemoteServices remoteServices = new RemoteServices(parameters.HostServerURL, parameters.FallbackHostServerURL);
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
                Log.Info("=======================WEIXINMINIGAME=======================");
                // 注意：如果有子目录，请修改此处！
                string packageRoot = $"{WeChatWASM.WX.env.USER_DATA_PATH}/__GAME_FILE_CACHE";
                createParameters.WebServerFileSystemParameters = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteServices, webDecryptionServices);
#else
                Log.Info("=======================UNITY_WEBGL=======================");
                if (parameters.LoadResWayWebGL == LoadResWayWebGL.Remote)
                {
                    createParameters.WebRemoteFileSystemParameters = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(remoteServices, webDecryptionServices);
                }

                createParameters.WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(webDecryptionServices);
#endif
                createParameters.AutoUnloadBundleWhenUnused = parameters.AutoUnloadBundleWhenUnused;
                return package.InitializeAsync(createParameters);
            }

            throw new NotSupportedException($"Unsupported resource play mode: {parameters.PlayMode}.");
        }

        private async UniTask InitializePackageManifestAsync(PackageInitializationContext context, CancellationToken cancellationToken)
        {
            ResourcePackage package = context.Package;
            var requestPackageVersionOperation = package.RequestPackageVersionAsync();
            await requestPackageVersionOperation.ToUniTask().AttachExternalCancellation(cancellationToken);
            if (requestPackageVersionOperation.Status != EOperationStatus.Succeed)
            {
                throw new GameFrameworkException(
                    $"Request package version failed for '{context.PackageName}': {requestPackageVersionOperation.Error}");
            }

            var updatePackageManifestOperation = package.UpdatePackageManifestAsync(requestPackageVersionOperation.PackageVersion);
            await updatePackageManifestOperation.ToUniTask().AttachExternalCancellation(cancellationToken);
            if (updatePackageManifestOperation.Status != EOperationStatus.Succeed)
            {
                throw new GameFrameworkException(
                    $"Update package manifest failed for '{context.PackageName}': {updatePackageManifestOperation.Error}");
            }
        }

        /// <summary>
        /// 创建解密服务。
        /// </summary>
        private IDecryptionServices CreateDecryptionServices(EncryptionType encryptionType)
        {
            return encryptionType switch
            {
                EncryptionType.FileOffSet => new FileOffsetDecryption(),
                EncryptionType.FileStream => new FileStreamDecryption(),
                _ => null
            };
        }

        /// <summary>
        /// 创建Web解密服务。
        /// </summary>
        private IWebDecryptionServices CreateWebDecryptionServices(EncryptionType encryptionType)
        {
            return encryptionType switch
            {
                EncryptionType.FileOffSet => new FileOffsetWebDecryption(),
                EncryptionType.FileStream => new FileStreamWebDecryption(),
                _ => null
            };
        }

        /// <summary>
        /// 获取当前资源包版本。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源包版本。</returns>
        public string GetPackageVersion(string customPackageName = "")
        {
            EnsureAcceptingRequests();
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            if (package == null)
            {
                return string.Empty;
            }

            return package.GetPackageVersion();
        }

        /// <summary>
        /// 异步更新最新包的版本。
        /// </summary>
        /// <param name="appendTimeTicks">请求URL是否需要带时间戳。</param>
        /// <param name="timeout">超时时间。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>请求远端包裹的最新版本操作句柄。</returns>
        public RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60,
            string customPackageName = "")
        {
            EnsureAcceptingRequests();
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.RequestPackageVersionAsync(appendTimeTicks, timeout);
        }

        public void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
        {
            HostServerURL = defaultHostServer;
            FallbackHostServerURL = fallbackHostServer;
        }

        /// <summary>
        /// 向网络端请求并更新清单
        /// </summary>
        /// <param name="packageVersion">更新的包裹版本</param>
        /// <param name="timeout">超时时间（默认值：60秒）</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public UpdatePackageManifestOperation UpdatePackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            EnsureAcceptingRequests();
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(this.DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.UpdatePackageManifestAsync(packageVersion, timeout);
        }

        void IPinnedManifestIntegrity.ConfigurePinnedManifest(
            string packageName, string packageVersion, string manifestSha256)
        {
            if (_packageInitializationContexts.ContainsKey(packageName))
                throw new InvalidOperationException(
                    $"Pinned manifest integrity must be configured before package '{packageName}' initialization.");

            _pinnedManifestVerifier = new PinnedManifestIntegrityVerifier(
                packageName, packageVersion, manifestSha256);
        }

        void IPinnedManifestIntegrity.VerifyPinnedManifestActivated(
            string packageName, string packageVersion, string manifestSha256)
        {
            if (_pinnedManifestVerifier == null)
                throw new InvalidOperationException("Pinned manifest integrity verifier is not configured.");

            _pinnedManifestVerifier.VerifyActivated(packageName, packageVersion, manifestSha256);
        }

        /// <summary>
        /// 资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        public ResourceDownloaderOperation Downloader { get; set; }

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "")
        {
            EnsureAcceptingRequests();
            ResourcePackage package;
            if (string.IsNullOrEmpty(customPackageName))
            {
                package = YooAssets.GetPackage(this.DefaultPackageName);
            }
            else
            {
                package = YooAssets.GetPackage(customPackageName);
            }

            Downloader = package.CreateResourceDownloader(DownloadingMaxNum, FailedTryAgain);
            return Downloader;
        }

        /// <summary>
        /// 按标签创建差量下载器。保留单一 ResourceModule owner，并让调用阶段拥有回调绑定与终态收尾。
        /// </summary>
        public ResourceDownloaderOperation CreateResourceDownloaderByTags(string[] tags, string customPackageName = "")
        {
            EnsureAcceptingRequests();
            if (tags == null || tags.Length == 0)
            {
                throw new ArgumentException("Download tags are empty.", nameof(tags));
            }

            string packageName = string.IsNullOrEmpty(customPackageName) ? DefaultPackageName : customPackageName;
            ResourcePackage package = YooAssets.GetPackage(packageName);
            if (package == null)
            {
                throw new InvalidOperationException($"Resource package '{packageName}' is not initialized.");
            }

            Downloader = package.CreateResourceDownloader(tags, DownloadingMaxNum, FailedTryAgain);
            return Downloader;
        }

        /// <summary>
        /// 清理包裹未使用的缓存文件。
        /// </summary>
        /// <param name="clearMode">文件清理方式。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public ClearCacheFilesOperation ClearCacheFilesAsync(
            EFileClearMode clearMode = EFileClearMode.ClearUnusedBundleFiles,
            string customPackageName = "")
        {
            EnsureAcceptingRequests();
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.ClearCacheFilesAsync(clearMode);
        }

        /// <summary>
        /// 清理沙盒路径。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        public void ClearAllBundleFiles(string customPackageName = "")
            => ClearCacheFilesAsync(EFileClearMode.ClearAllBundleFiles, customPackageName);

        #region 资源回收

        public void OnLowMemory()
        {
            if (!IsAcceptingRequests)
            {
                return;
            }

            Log.Warning("Low memory reported...");
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

        /// <summary>
        /// 低内存回调保护。
        /// </summary>
        /// <param name="action">低内存行为。</param>
        public void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            if (!IsAcceptingRequests)
            {
                return;
            }

            _forceUnloadUnusedAssetsAction = action;
        }

        /// <summary>
        /// 资源回收（卸载引用计数为零的资源）。
        /// </summary>
        public void UnloadUnusedAssets()
        {
            if (!IsAcceptingRequests || _assetPool == null)
            {
                return;
            }

            _assetPool.ReleaseAllUnused();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeed })
                {
                    package.UnloadUnusedAssetsAsync();
                }
            }
        }

        /// <summary>
        /// 强制回收所有资源。
        /// </summary>
        public void ForceUnloadAllAssets()
        {
            if (!IsAcceptingRequests)
            {
                return;
            }

#if UNITY_WEBGL
            Log.Warning($"WebGL not support invoke {nameof(ForceUnloadAllAssets)}");
			return;
#else

            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeed })
                {
                    package.UnloadAllAssetsAsync();
                }
            }
#endif
        }

        public void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            if (!IsAcceptingRequests)
            {
                return;
            }

            _forceUnloadUnusedAssetsAction?.Invoke(performGCCollect);
        }

        #endregion

        #region Public Methods

        #region 获取资源信息

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        public bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            EnsureAcceptingRequests();
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.IsNeedDownloadFromRemote(location);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.IsNeedDownloadFromRemote(location);
            }
        }

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="assetInfo">资源信息。</param>
        /// <param name="packageName">资源包名称。</param>
        public bool IsNeedDownloadFromRemote(AssetInfo assetInfo, string packageName = "")
        {
            EnsureAcceptingRequests();
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.IsNeedDownloadFromRemote(assetInfo);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.IsNeedDownloadFromRemote(assetInfo);
            }
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tag">资源标签。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string tag, string packageName = "")
        {
            EnsureAcceptingRequests();
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfos(tag);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.GetAssetInfos(tag);
            }
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tags">资源标签列表。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
        {
            EnsureAcceptingRequests();
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfos(tags);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.GetAssetInfos(tags);
            }
        }

        /// <summary>
        /// 获取资源信息。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息。</returns>
        public AssetInfo GetAssetInfo(string location, string packageName = "")
        {
            EnsureAcceptingRequests();
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            // 空包与默认包等价：统一使用无碰撞缓存键，两种调用形式共享同一份信息缓存。
            string key = GetCacheKey(location, packageName);
            if (_assetInfoMap.TryGetValue(key, out AssetInfo assetInfo))
            {
                return assetInfo;
            }

            if (string.IsNullOrEmpty(packageName))
            {
                assetInfo = YooAssets.GetAssetInfo(location);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                if (package == null)
                {
                    throw new GameFrameworkException($"The package does not exist. Package Name :{packageName}");
                }

                assetInfo = package.GetAssetInfo(location);
            }

            _assetInfoMap[key] = assetInfo;
            return assetInfo;
        }

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>检查资源是否存在的结果。</returns>
        public HasAssetResult HasAsset(string location, string packageName = "")
        {
            EnsureAcceptingRequests();
            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            AssetInfo assetInfo = GetAssetInfo(location, packageName);

            if (!CheckLocationValid(location, packageName))
            {
                return HasAssetResult.Valid;
            }

            if (assetInfo == null)
            {
                return HasAssetResult.NotExist;
            }

            if (IsNeedDownloadFromRemote(assetInfo))
            {
                return HasAssetResult.AssetOnline;
            }

            return HasAssetResult.AssetOnDisk;
        }

        /// <summary>
        /// 检查资源定位地址是否有效。
        /// </summary>
        /// <param name="location">资源的定位地址</param>
        /// <param name="packageName">资源包名称。</param>
        public bool CheckLocationValid(string location, string packageName = "")
        {
            if (!IsAcceptingRequests)
            {
                return false;
            }

            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.CheckLocationValid(location);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.CheckLocationValid(location);
            }
        }

        #endregion

        #region 资源加载

        #region 获取资源句柄

        /// <summary>
        /// 获取同步资源句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源句柄。</returns>
        private AssetHandle GetHandleSync<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return GetHandleSync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleSync(string location, Type assetType, string packageName = "")
        {
            assetType ??= typeof(UnityEngine.Object);

            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetSync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync(location, assetType);
        }

        /// <summary>
        /// 获取异步资源句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源句柄。</returns>
        private AssetHandle GetHandleAsync<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return GetHandleAsync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleAsync(string location, Type assetType, string packageName = "")
        {
            assetType ??= typeof(UnityEngine.Object);

            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetAsync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync(location, assetType);
        }

        #endregion

        /// <summary>
        /// 正在加载的资源列表。
        /// <remarks>主线程访问；键只能由领取它的请求移除。</remarks>
        /// </summary>
        internal readonly HashSet<string> _assetLoadingList = new HashSet<string>();

        #region 加载核心辅助

        /// <summary>
        /// 构建非默认资源包的缓存Key。
        /// <remarks>使用长度前缀避免 packageName/location 简单拼接碰撞（location 可含任意分隔符）。</remarks>
        /// </summary>
        internal static string BuildPackageCacheKey(string packageName, string location)
        {
            return $"{packageName.Length}:{packageName}/{location}";
        }

        /// <summary>
        /// 获取资源定位地址的缓存Key。
        /// <remarks>
        /// 所有包使用同一可唯一反解的无碰撞编码：默认包（空包与 DefaultPackageName 等价）为
        /// "0:{location}"，自定义包为 "{len}:{pkg}/{location}"。默认包 location 以任意字符串开头
        /// 都会获得 "0:" 前缀，自定义包长度前缀至少为 "1"，两者互不碰撞。
        /// </remarks>
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源定位地址的缓存Key。</returns>
        internal string GetCacheKey(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName) || packageName.Equals(DefaultPackageName))
            {
                return $"0:{location}";
            }

            return BuildPackageCacheKey(packageName, location);
        }

        /// <summary>
        /// 原子领取加载标记（主线程下无 await 间隙）。
        /// </summary>
        /// <returns>true 表示本请求成为该 key 的加载者。</returns>
        private bool TryEnterLoading(string assetObjectKey)
        {
            return _assetLoadingList.Add(assetObjectKey);
        }

        /// <summary>
        /// 清除加载标记。
        /// <remarks>只能由领取该标记的加载者在收尾时调用。</remarks>
        /// </summary>
        private void ExitLoading(string assetObjectKey)
        {
            _assetLoadingList.Remove(assetObjectKey);
        }

        /// <summary>
        /// 等待同 key 的加载结束。
        /// </summary>
        /// <returns>true 表示可以继续循环竞争；false 表示本请求应结束（调用者取消，或编辑器诊断超时）。</returns>
        private async UniTask<bool> WaitLoadingKeyAsync(string assetObjectKey, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!_assetLoadingList.Contains(assetObjectKey))
            {
                return true;
            }

            try
            {
#if UNITY_EDITOR
                // 编辑器诊断：每个请求独立 60 秒超时，仅结束本请求，不影响 key owner。
                using (CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    try
                    {
                        await UniTask.WaitUntil(() => !_assetLoadingList.Contains(assetObjectKey), cancellationToken: linkedCts.Token);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        Log.Error($"LoadAssetAsync Waiting {assetObjectKey} timeout(60s), request aborted.");
                        return false;
                    }
                }
#else
                if (_assetLoadingList.Contains(assetObjectKey))
                {
                    await UniTask.WaitUntil(() => !_assetLoadingList.Contains(assetObjectKey), cancellationToken: cancellationToken);
                }
#endif
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 调用者取消等待：不影响 key owner 与其他等待者。
                return false;
            }

            return !cancellationToken.IsCancellationRequested;
        }

        /// <summary>
        /// 尝试从缓存领取一份校验通过的资产引用。
        /// </summary>
        /// <returns>true 表示已触碰缓存条目；target 为 null 表示命中但类型不符（spawn 已归还，本次请求应失败返回）。</returns>
        private bool TrySpawnCacheAsset(string assetObjectKey, Type assetType, out UnityEngine.Object target)
        {
            target = null;
            assetType ??= typeof(UnityEngine.Object);

            if (!IsAcceptingRequests || _poolClosed || _assetPool == null)
            {
                return false;
            }

            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject == null)
            {
                return false;
            }

            if (!(assetObject.Target is UnityEngine.Object unityObject)
                || unityObject == null
                || !assetType.IsInstanceOfType(unityObject))
            {
                ReturnSpawn(assetObject.Target);
                Log.Error($"Cached asset '{assetObjectKey}' type '{assetObject.Target?.GetType().FullName}' is not assignable to '{assetType.FullName}'.");
                return true;
            }

            target = unityObject;
            return true;
        }

        /// <summary>
        /// 归还一份已领取的 spawn。
        /// </summary>
        private void ReturnSpawn(object target)
        {
            if (target == null || _poolClosed || _assetPool == null)
            {
                return;
            }

            try
            {
                _assetPool.Unspawn(target);
            }
            catch (Exception ex)
            {
                LogErrorSafely($"Return spawn failed for '{target.GetType().FullName}': {ex.Message}");
            }
        }

        /// <summary>
        /// 安全释放仍由请求持有的 handle（登记成功前）。
        /// </summary>
        private static void SafeDisposeHandle(AssetHandle handle)
        {
            if (handle is { IsValid: true })
            {
                try
                {
                    handle.Dispose();
                }
                catch (Exception ex)
                {
                    LogErrorSafely($"Dispose asset handle failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 等待资源句柄完成，并只归一化已确认的取消或底层失败。
        /// <remarks>
        /// YooAsset 的 UniTask 适配器可能把 Failed 状态表现为异常，也可能只设置 Status；
        /// 只有句柄明确处于 Failed 时才吞掉适配器异常，其他异常必须继续向调用方传播。
        /// </remarks>
        /// <returns>true 表示调用者取消；false 表示句柄已完成或已确认底层失败。</returns>
        private static async UniTask<bool> AwaitAssetHandleAsync(AssetHandle handle, CancellationToken cancellationToken)
        {
            try
            {
                await handle.ToUniTask().AttachExternalCancellation(cancellationToken);
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception) when (handle != null && handle.Status == EOperationStatus.Failed)
            {
                // 已确认是 YooAsset 底层失败；调用方随后统一读取 Status/LastError 并走失败收尾。
                return false;
            }
        }

        /// <summary>
        /// 将加载成功的资产登记进资源池。
        /// <remarks>登记成功后仅池持有 handle，调用方持有 spawn；登记异常时由本请求释放 handle 后传播。</remarks>
        /// </summary>
        private AssetObject RegisterLoadedAsset(string assetObjectKey, UnityEngine.Object target, ref AssetHandle handle)
        {
            EnsureAcceptingRequests();
            if (_poolClosed || _assetPool == null)
            {
                throw new OperationCanceledException("Resource object pool is closed.");
            }

            if (handle == null)
            {
                throw new GameFrameworkException("Asset handle is invalid.");
            }

            AssetObject assetObject = null;
            try
            {
                assetObject = AssetObject.Create(assetObjectKey, target, handle, this);

                // AssetObject 已接管 handle；失败时由 assetObject.Release(false) 负责释放，
                // 外层 finally 不再拥有它，避免 Dispose 二次执行。
                handle = null;
                _assetPool.Register(assetObject, true);
                return assetObject;
            }
            catch
            {
                if (assetObject != null)
                {
                    try
                    {
                        assetObject.Release(false);
                    }
                    finally
                    {
                        MemoryPool.Release(assetObject);
                    }
                }

                throw;
            }
        }

        /// <summary>
        /// 领取一份资产的持有引用（缓存命中或发起新加载）。
        /// </summary>
        /// <returns>非 null 表示已交付一份持有（交付点已通过取消检查）；null 表示请求结束（取消/失败/诊断超时），内部状态已收尾。</returns>
        private async UniTask<UnityEngine.Object> AcquireAssetReferenceAsync(string location, Type assetType, string packageName, CancellationToken cancellationToken)
        {
            if (!IsAcceptingRequests)
            {
                return null;
            }

            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            // null 类型规范化为任意资产类型，避免先领取 spawn 后在校验处抛异常造成泄漏。
            assetType ??= typeof(UnityEngine.Object);

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, LifetimeToken);
            CancellationToken requestToken = linkedCancellation.Token;

            // 已取消的 token 在缓存检查前就结束。
            if (requestToken.IsCancellationRequested)
            {
                return null;
            }

            if (!IsAcceptingRequests)
            {
                return null;
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                return null;
            }

            string assetObjectKey = GetCacheKey(location, packageName);

            while (true)
            {
                // 缓存命中（含类型校验；不符时本次 spawn 已归还并返回失败）。
                if (!IsAcceptingRequests || requestToken.IsCancellationRequested)
                {
                    return null;
                }

                if (TrySpawnCacheAsset(assetObjectKey, assetType, out UnityEngine.Object cachedTarget))
                {
                    if (cachedTarget == null)
                    {
                        return null;
                    }

                    // 兼容既有同帧节奏：下一帧交付；交付前取消则立即归还这一份 spawn。
                    await UniTask.Yield();
                    if (!IsAcceptingRequests || requestToken.IsCancellationRequested)
                    {
                        ReturnSpawn(cachedTarget);
                        return null;
                    }

                    return cachedTarget;
                }

                // 原子领取加载标记；未领取到则等待后循环重查。
                if (TryEnterLoading(assetObjectKey))
                {
                    break;
                }

                if (!await WaitLoadingKeyAsync(assetObjectKey, requestToken))
                {
                    // 取消或编辑器诊断超时：结束本请求，不绕过标记继续加载。
                    return null;
                }
            }

            // 本请求为该 key 的加载者（owner）：唯一负责清除标记；handle 在登记成功前由本请求持有。
            AssetHandle handle = null;
            bool pooled = false;
            try
            {
                handle = GetHandleAsync(location, assetType, packageName);

                // 只把调用者取消或句柄明确 Failed 的适配器结果归一化；其他异常在 finally 后继续传播。
                bool canceled = await AwaitAssetHandleAsync(handle, requestToken);
                if (canceled)
                {
                    return null;
                }

                UnityEngine.Object loaded = handle.AssetObject;
                if (loaded == null || handle.Status == EOperationStatus.Failed)
                {
                    Log.Error($"Load asset '{location}' failed. status:{handle.Status} error:{handle.LastError}");
                    return null;
                }

                if (!assetType.IsInstanceOfType(loaded))
                {
                    Log.Error($"Loaded asset '{location}' type '{loaded.GetType().FullName}' is not assignable to '{assetType.FullName}'.");
                    return null;
                }

                if (!IsAcceptingRequests || requestToken.IsCancellationRequested)
                {
                    return null;
                }

                AssetObject assetObject = RegisterLoadedAsset(assetObjectKey, loaded, ref handle);
                pooled = true;

                // 交付点：交付前观测到取消则不交付（归还本次 spawn，资产留在池中可正常回收）。
                if (!IsAcceptingRequests || requestToken.IsCancellationRequested)
                {
                    ReturnSpawn(assetObject.Target);
                    return null;
                }

                return assetObject.Target as UnityEngine.Object;
            }
            finally
            {
                // owner 收尾：释放尚未移交的 handle（双保险），并清除自己拥有的标记；异常在收尾后继续传播。
                if (!pooled)
                {
                    SafeDisposeHandle(handle);
                }

                ExitLoading(assetObjectKey);
            }
        }

        /// <summary>
        /// 实例化并绑定源资产引用。
        /// </summary>
        /// <returns>非 null 表示已交付实例；null 表示交付前取消（实例已销毁、spawn 已一次性补偿归还）。</returns>
        private void ReleaseBoundInstanceAndDestroy(GameObject instance, UnityEngine.Object prefab)
        {
            if (instance != null)
            {
                AssetsReference reference = instance.GetComponent<AssetsReference>();
                if (reference != null)
                {
                    // 绑定成功后由 owner 归还 source spawn；OnDestroy 仅看到已摘除记录。
                    reference.ReleaseAndDestroy();
                    return;
                }

                UnityEngine.Object.Destroy(instance);
            }

            // 绑定未成功或实例没有 owner 时没有组件可补偿，资源 spawn 由此路径归还。
            ReturnSpawn(prefab);
        }

        internal UniTask<GameObject> InstantiateAndDeliverAsync(UnityEngine.Object prefab, Transform parent, CancellationToken cancellationToken)
        {
            if (!IsAcceptingRequests || cancellationToken.IsCancellationRequested)
            {
                ReturnSpawn(prefab);
                return UniTask.FromResult<GameObject>(null);
            }

            GameObject instance;
            try
            {
                instance = AssetsReference.Instantiate(prefab as GameObject, parent, this).gameObject;
            }
            catch
            {
                // 实例化/绑定失败：AssetsReference.Instantiate 内部已销毁未绑定实例；
                // 该份 spawn 无人认领（不存在 OnDestroy 归还），在此手动一次性归还。
                ReturnSpawn(prefab);
                throw;
            }

            // 返回前取消：不依赖 OnDestroy 的一次性补偿——先摘除绑定防止双重归还，
            // 再销毁未交付实例，由 owner 归还这一份 spawn（未激活实例销毁不保证触发 OnDestroy）。
            if (!IsAcceptingRequests || cancellationToken.IsCancellationRequested)
            {
                ReleaseBoundInstanceAndDestroy(instance, prefab);
                return UniTask.FromResult<GameObject>(null);
            }

            return UniTask.FromResult(instance);
        }

        #endregion

        public T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAsset(location, typeof(T), packageName) as T;
        }

        public UnityEngine.Object LoadAsset(string location, Type assetType, string packageName = "")
        {
            EnsureAcceptingRequests();

            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            // null 类型规范化为任意资产类型，避免先领取 spawn 后在校验处抛异常造成泄漏。
            assetType ??= typeof(UnityEngine.Object);

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                return null;
            }

            string assetObjectKey = GetCacheKey(location, packageName);

            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject != null)
            {
                if (assetObject.Target is UnityEngine.Object unityObject
                    && unityObject != null
                    && assetType.IsInstanceOfType(unityObject))
                {
                    return unityObject;
                }

                ReturnSpawn(assetObject.Target);
                Log.Error($"Cached asset '{assetObjectKey}' type '{assetObject.Target?.GetType().FullName}' is not assignable to '{assetType.FullName}'.");
                return null;
            }

            // 同步入口撞上未完成的同 key 异步加载：明确失败，不阻塞等待、不注册第二份。
            if (_assetLoadingList.Contains(assetObjectKey))
            {
                throw new GameFrameworkException($"Sync load '{location}' conflicts with an in-flight async load. Await LoadAssetAsync instead.");
            }

            AssetHandle handle = null;
            try
            {
                handle = GetHandleSync(location, assetType, packageName);

                UnityEngine.Object loaded = handle.AssetObject;
                if (loaded == null || handle.Status == EOperationStatus.Failed)
                {
                    Log.Error($"Load asset '{location}' failed. status:{handle.Status} error:{handle.LastError}");
                    return null;
                }

                // 登记前完成类型校验，不得把不兼容类型登记为成功对象。
                if (!assetType.IsInstanceOfType(loaded))
                {
                    Log.Error($"Loaded asset '{location}' type '{loaded.GetType().FullName}' is not assignable to '{assetType.FullName}'.");
                    return null;
                }

                RegisterLoadedAsset(assetObjectKey, loaded, ref handle);
                return loaded;
            }
            finally
            {
                SafeDisposeHandle(handle);
            }
        }

        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            EnsureAcceptingRequests();

            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                return null;
            }

            string assetObjectKey = GetCacheKey(location, packageName);

            AssetObject assetObject = _assetPool.Spawn(assetObjectKey);
            if (assetObject == null)
            {
                // 同步入口撞上未完成的同 key 异步加载：明确失败，不阻塞等待、不注册第二份。
                if (_assetLoadingList.Contains(assetObjectKey))
                {
                    throw new GameFrameworkException($"Sync load '{location}' conflicts with an in-flight async load. Await LoadGameObjectAsync instead.");
                }

                AssetHandle handle = null;
                try
                {
                    handle = GetHandleSync<GameObject>(location, packageName: packageName);

                    UnityEngine.Object loaded = handle.AssetObject;
                    if (loaded == null || handle.Status == EOperationStatus.Failed)
                    {
                        Log.Error($"Load asset '{location}' failed. status:{handle.Status} error:{handle.LastError}");
                        return null;
                    }

                    // 登记前完成 GameObject 类型校验，不得把不兼容类型登记为成功对象。
                    if (!(loaded is GameObject))
                    {
                        Log.Error($"Loaded asset '{location}' type '{loaded.GetType().FullName}' is not assignable to 'UnityEngine.GameObject'.");
                        return null;
                    }

                    assetObject = RegisterLoadedAsset(assetObjectKey, loaded, ref handle);
                }
                finally
                {
                    SafeDisposeHandle(handle);
                }
            }

            GameObject prefab = assetObject.Target as GameObject;
            if (prefab == null)
            {
                ReturnSpawn(assetObject.Target);
                Log.Error($"Cached asset '{assetObjectKey}' type '{assetObject.Target?.GetType().FullName}' is not assignable to 'UnityEngine.GameObject'.");
                return null;
            }

            GameObject gameObject = null;
            try
            {
                gameObject = AssetsReference.Instantiate(prefab, parent, this).gameObject;

                // 绑定已成功：后处理异常时统一一次性补偿——销毁实例并由 owner 归还 source spawn。
                // 不依赖 OnDestroy，因为未激活实例的销毁回调并不保证触发。
#if UNITY_EDITOR&&EditorFixedMaterialShader
                if (PlayMode!=EPlayMode.EditorSimulateMode)
                {
                    Utility.MaterialHelper.FixedMaterialShader_All(gameObject.transform);
                }
#endif
                return gameObject;
            }
            catch
            {
                if (gameObject != null)
                {
                    // 绑定成功后的异常由 AssetsReference owner 归还 source spawn 一次。
                    ReleaseBoundInstanceAndDestroy(gameObject, prefab);
                }
                else
                {
                    // 实例化/绑定失败：AssetsReference.Instantiate 内部已销毁未绑定实例，
                    // 该份 spawn 无人认领（不存在 OnDestroy 归还），在此手动一次性归还。
                    ReturnSpawn(prefab);
                }

                throw;
            }
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="callback">回调函数。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        public async UniTaskVoid LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object
        {
            if (!IsAcceptingRequests)
            {
                return;
            }

            if (string.IsNullOrEmpty(location))
            {
                Log.Error("Asset name is invalid.");
                callback?.Invoke(null);
                return;
            }

            if (!CheckLocationValid(location, packageName))
            {
                Log.Error($"Could not found location [{location}].");
                callback?.Invoke(null);
                return;
            }

            UnityEngine.Object delivered = await AcquireAssetReferenceAsync(location, typeof(T), packageName, CancellationToken.None);

            if (delivered == null)
            {
                if (!IsAcceptingRequests)
                {
                    return;
                }

                // 失败通知一次 null。
                callback?.Invoke(null);
                return;
            }

            if (!IsAcceptingRequests)
            {
                ReturnSpawn(delivered);
                return;
            }

            if (callback == null)
            {
                // 无成功接收者：不发起持有性交付，立即归还这一份 spawn（资产已由池登记，可正常回收）。
                ReturnSpawn(delivered);
                return;
            }

            // 成功回调进入前已完成资源登记和持有权转移；业务回调抛错不撤销已交付资源，异常经 UniTaskVoid 通道保持可见。
            callback.Invoke(delivered as T);
        }

        public async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object
        {
            return await LoadAssetAsync(location, typeof(T), cancellationToken, packageName) as T;
        }

        public async UniTask<UnityEngine.Object> LoadAssetAsync(string location, Type assetType, CancellationToken cancellationToken = default, string packageName = "")
        {
            // null 类型规范化为任意资产类型。
            return await AcquireAssetReferenceAsync(location, assetType ?? typeof(UnityEngine.Object), packageName, cancellationToken);
        }

        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
        {
            if (!IsAcceptingRequests)
            {
                return null;
            }

            UnityEngine.Object prefab = await AcquireAssetReferenceAsync(location, typeof(GameObject), packageName, cancellationToken);
            if (prefab == null)
            {
                return null;
            }

            GameObject gameObject = await InstantiateAndDeliverAsync(prefab, parent, cancellationToken);
            if (gameObject == null)
            {
                return null;
            }

            try
            {
                // 绑定已成功：后处理异常时统一一次性补偿——销毁实例并由 owner 归还 source spawn。
                // 不依赖 OnDestroy，因为未激活实例的销毁回调并不保证触发。
#if UNITY_EDITOR&&EditorFixedMaterialShader
                if (PlayMode!=EPlayMode.EditorSimulateMode)
                {
                    Utility.MaterialHelper.FixedMaterialShader_All(gameObject.transform);
                }
#endif
                return gameObject;
            }
            catch
            {
                // 绑定成功后的异常由 AssetsReference owner 归还 source spawn 一次。
                ReleaseBoundInstanceAndDestroy(gameObject, prefab);
                throw;
            }
        }

        #endregion

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="assetType">要加载资源的类型。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        public void LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (!IsAcceptingRequests)
            {
                return;
            }

            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                string errorMessage = Utility.Text.Format("Could not found location [{0}].", location);
                Log.Error(errorMessage);
                loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, LoadResourceStatus.NotExist, errorMessage, userData);
                return;
            }

            // null 类型规范化为任意资产类型。
            LoadAssetWithCallbacksAsync(location, assetType ?? typeof(UnityEngine.Object), loadAssetCallbacks, userData, packageName).Forget();
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        public void LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (!IsAcceptingRequests)
            {
                return;
            }

            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameFrameworkException("Load asset callbacks is invalid.");
            }

            if (!CheckLocationValid(location, packageName))
            {
                string errorMessage = Utility.Text.Format("Could not found location [{0}].", location);
                Log.Error(errorMessage);
                loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, LoadResourceStatus.NotExist, errorMessage, userData);
                return;
            }

            AssetInfo assetInfo = GetAssetInfo(location, packageName);
            Type assetType = assetInfo.AssetType;
            if (assetType == null)
            {
                assetType = typeof(UnityEngine.Object);
            }

            LoadAssetWithCallbacksAsync(location, assetType, loadAssetCallbacks, userData, packageName).Forget();
        }

        /// <summary>
        /// 回调加载核心：每次请求只产生一个加载终态（成功或失败）。
        /// <remarks>成功回调进入前完成资源登记和持有权转移；业务回调抛错不触发第二次“加载失败”，
        /// 不擅自撤销已交付资源，异常保持可见（async void 经项目现有异常通道报告）。</remarks>
        /// </summary>
        private async UniTaskVoid LoadAssetWithCallbacksAsync(string location, Type assetType, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName)
        {
            CancellationToken lifetimeToken = LifetimeToken;
            assetType ??= typeof(UnityEngine.Object);
            string assetObjectKey = GetCacheKey(location, packageName);
            float beginTime = Time.time;

            while (true)
            {
                if (!IsAcceptingRequests || lifetimeToken.IsCancellationRequested)
                {
                    return;
                }

                if (TrySpawnCacheAsset(assetObjectKey, assetType, out UnityEngine.Object cachedTarget))
                {
                    if (cachedTarget == null)
                    {
                        NotifyLoadAssetFailure(loadAssetCallbacks, location, LoadResourceStatus.NotExist,
                            Utility.Text.Format("Can not load asset '{0}' because cached type mismatch.", location), userData);
                        return;
                    }

                    if (loadAssetCallbacks.LoadAssetSuccessCallback == null)
                    {
                        // 没有成功接收者时只完成预热，不向调用方转移持有权。
                        ReturnSpawn(cachedTarget);
                        return;
                    }

                    await UniTask.Yield();

                    if (!IsAcceptingRequests || lifetimeToken.IsCancellationRequested)
                    {
                        ReturnSpawn(cachedTarget);
                        return;
                    }

                    loadAssetCallbacks.LoadAssetSuccessCallback(location, cachedTarget, Time.time - beginTime, userData);
                    return;
                }

                if (TryEnterLoading(assetObjectKey))
                {
                    break;
                }

                if (!await WaitLoadingKeyAsync(assetObjectKey, lifetimeToken))
                {
                    // 编辑器诊断超时：结束本请求（一次失败终态），不影响 key owner。
                    NotifyLoadAssetFailure(loadAssetCallbacks, location, LoadResourceStatus.NotReady,
                        Utility.Text.Format("Load asset '{0}' waiting timeout.", location), userData);
                    return;
                }
            }

            AssetHandle handle = null;
            bool pooled = false;
            try
            {
                handle = GetHandleAsync(location, assetType, packageName);

                if (loadAssetCallbacks.LoadAssetUpdateCallback != null)
                {
                    ObserveProgress(location, handle, loadAssetCallbacks.LoadAssetUpdateCallback, userData).Forget();
                }

                // 加载失败以 null/Failed 状态检测；Failed 适配器异常由 AwaitAssetHandleAsync 归一化，
                // 其余异常（非预期）在 finally 收尾后传播，不再转换为失败终态。
                bool canceled = await AwaitAssetHandleAsync(handle, lifetimeToken);
                if (canceled)
                {
                    return;
                }

                UnityEngine.Object loaded = handle.AssetObject;
                if (loaded == null || handle.Status == EOperationStatus.Failed)
                {
                    NotifyLoadAssetFailure(loadAssetCallbacks, location, LoadResourceStatus.NotReady,
                        Utility.Text.Format("Can not load asset '{0}'. status:{1} error:{2}", location, handle.Status, handle.LastError), userData);
                    return;
                }

                if (!assetType.IsInstanceOfType(loaded))
                {
                    NotifyLoadAssetFailure(loadAssetCallbacks, location, LoadResourceStatus.NotReady,
                        Utility.Text.Format("Loaded asset '{0}' type '{1}' is not assignable to '{2}'.", location, loaded.GetType().FullName, assetType.FullName), userData);
                    return;
                }

                if (!IsAcceptingRequests || lifetimeToken.IsCancellationRequested)
                {
                    return;
                }

                RegisterLoadedAsset(assetObjectKey, loaded, ref handle);
                pooled = true;

                if (loadAssetCallbacks.LoadAssetSuccessCallback == null)
                {
                    // 无成功接收者：资产可留在池中作为预热结果，但本请求不留下 spawn。
                    ReturnSpawn(loaded);
                    return;
                }

                if (!IsAcceptingRequests || lifetimeToken.IsCancellationRequested)
                {
                    ReturnSpawn(loaded);
                    return;
                }

                if (IsAcceptingRequests && !lifetimeToken.IsCancellationRequested)
                {
                    loadAssetCallbacks.LoadAssetSuccessCallback(location, loaded, Time.time - beginTime, userData);
                }
            }
            catch (OperationCanceledException) when (!IsAcceptingRequests || lifetimeToken.IsCancellationRequested)
            {
                // 退出取消是正常终态；finally 仍释放未入池 handle 并清除 loading owner。
            }
            finally
            {
                if (!pooled)
                {
                    SafeDisposeHandle(handle);
                }

                ExitLoading(assetObjectKey);
            }
        }

        private static void NotifyLoadAssetFailure(LoadAssetCallbacks loadAssetCallbacks, string location, LoadResourceStatus status, string errorMessage, object userData)
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            if (loadAssetCallbacks.LoadAssetFailureCallback != null)
            {
                loadAssetCallbacks.LoadAssetFailureCallback(location, status, errorMessage, userData);
                return;
            }

            // 无失败接收者时保持异常可见（async void 经项目现有异常通道报告）。
            throw new GameFrameworkException(errorMessage);
        }

        /// <summary>
        /// 进度观察者：进度回调抛错须被观察、报告并停止该进度观察者，不卡住加载或产生未结束的进度任务。
        /// </summary>
        private async UniTaskVoid ObserveProgress(string location, AssetHandle assetHandle, LoadAssetUpdateCallback loadAssetUpdateCallback, object userData)
        {
            CancellationToken lifetimeToken = LifetimeToken;
            while (IsAcceptingRequests && !lifetimeToken.IsCancellationRequested && assetHandle is { IsValid: true, IsDone: false })
            {
                await UniTask.Yield();

                if (!IsAcceptingRequests || lifetimeToken.IsCancellationRequested || assetHandle is not { IsValid: true })
                {
                    break;
                }

                try
                {
                    loadAssetUpdateCallback.Invoke(location, assetHandle.Progress, userData);
                }
                catch (Exception ex)
                {
                    LogErrorSafely($"Load asset progress callback for '{location}' threw and the observer stopped: {ex.Message}");
                    break;
                }
            }
        }

        /// <summary>
        /// 获取同步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源操作句柄。</returns>
        public AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetSyncHandle(location, typeof(T), packageName);
        }

        public AssetHandle LoadAssetSyncHandle(string location, System.Type type, string packageName = "")
        {
            EnsureAcceptingRequests();
            type ??= typeof(UnityEngine.Object);

            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetSync(location, type);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync(location, type);
        }

        /// <summary>
        /// 获取异步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源操作句柄。</returns>
        public AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetAsyncHandle(location, typeof(T), packageName);
        }

        public AssetHandle LoadAssetAsyncHandle(string location, Type assetType, string packageName = "")
        {
            EnsureAcceptingRequests();
            assetType ??= typeof(UnityEngine.Object);

            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetAsync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync(location, assetType);
        }

        #endregion

        #region 设置下载系统参数，自定义下载请求

        /// <summary>
        /// 设置下载系统参数，自定义下载请求。
        /// </summary>
        /// <param name="downloadSystemUnityWebRequest">自定义下载器的请求委托。<see cref="UnityWebRequestDelegate"/></param>
        public void SetDownloadSystemUnityWebRequest(UnityWebRequestDelegate downloadSystemUnityWebRequest)
        {
            EnsureAcceptingRequests();
            YooAssets.SetDownloadSystemUnityWebRequest(downloadSystemUnityWebRequest);
        }

        public UnityEngine.Networking.UnityWebRequest CustomWebRequester(string url)
        {
            var request = new UnityEngine.Networking.UnityWebRequest(url, UnityEngine.Networking.UnityWebRequest.kHttpVerbGET);
            var authorization = GetAuthorization("Admin", "12345");
            request.SetRequestHeader("AUTHORIZATION", authorization);
            return request;
        }

        private string GetAuthorization(string userName, string password)
        {
            string auth = $"{userName}:{password}";
            var bytes = System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(auth);
            return $"Basic {Convert.ToBase64String(bytes)}";
        }

        private static void LogErrorSafely(string message)
        {
            try { Log.Error(message); }
            catch { }
        }

        private static void LogErrorSafely(string format, Exception exception)
        {
            try { Log.Error(format, exception); }
            catch { }
        }

        #endregion
    }
}
