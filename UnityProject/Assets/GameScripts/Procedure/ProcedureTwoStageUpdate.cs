using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
#if ENABLE_HYBRIDCLR
using HybridCLR;
#endif
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    /// <summary>
    /// 主包拥有的两阶段闸门：BOOTSTRAP 闭包 → metadata/updater → 可等待的热更第二阶段。
    /// </summary>
    public sealed class ProcedureTwoStageUpdate : ProcedureBase, IUpdateHost
    {
        private const float DefaultLoadTimeoutSeconds = 60f;

        public override bool UseNativeDialog => true;

        private ProcedureOwner _procedureOwner;
        private StartupAttempt _currentAttempt;
        private int _nextAttemptId;
        private ResourceDownloaderOperation _downloader;
        private UpdateSessionContext _activeContext;
        private CancellationTokenSource _contextCancellation;
        private float _lastProgressTime;
        private long _lastProgressBytes;
        private string _lastDownloadError;

        public float LoadTimeoutSeconds { get; set; } = DefaultLoadTimeoutSeconds;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            _procedureOwner = procedureOwner;
            StartAttempt();
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            try
            {
                base.OnLeave(procedureOwner, isShutdown);
            }
            finally
            {
                ReleaseDownloader(_downloader, cancelIfRunning: true);
                _downloader = null;
                _contextCancellation?.Cancel();
                _contextCancellation?.Dispose();
                _contextCancellation = null;
                _activeContext = null;

                StartupAttempt attempt = _currentAttempt;
                _currentAttempt = null;
                attempt?.Invalidate();
                attempt?.Dispose();
            }
        }

        private void StartAttempt()
        {
            StartupAttempt previous = _currentAttempt;
            _currentAttempt = null;
            previous?.Invalidate();
            previous?.Dispose();

            ReleaseDownloader(_downloader, cancelIfRunning: true);
            _downloader = null;
            _contextCancellation?.Cancel();
            _contextCancellation?.Dispose();
            _contextCancellation = null;
            _activeContext = null;

            StartupAttempt attempt = new StartupAttempt(++_nextAttemptId);
            _currentAttempt = attempt;
            RunTwoStageAsync(attempt).Forget();
        }

        private async UniTaskVoid RunTwoStageAsync(StartupAttempt attempt)
        {
            try
            {
                EnsureCurrentRunning(attempt);
                ValidateBootstrapAssetClassification();
                LauncherMgr.ShowUI<LoadUpdateUI>("正在获取启动更新器...");
                await DownloadBootstrapAsync(attempt, attempt.Token);
                EnsureCurrentRunning(attempt);

                UniTask loadTask = LoadMetadataAndUpdaterAsync(attempt, attempt.Token);
                await loadTask.Timeout(
                    TimeSpan.FromSeconds(Math.Max(0.01f, LoadTimeoutSeconds)),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update);
                EnsureCurrentRunning(attempt);

                _activeContext = TwoStageUpdateCoordinator.CreateContext(attempt.Token, out _contextCancellation);
                UpdateStageResult result = await InvokeUpdaterAsync(_activeContext);
                EnsureCurrentRunning(attempt);
                ValidateUpdaterResult(result, _activeContext);
                VerifyResourcesActuallyReady(_activeContext);
                TwoStageUpdateCoordinator.MarkResourcesReady();

                if (!attempt.TrySucceed())
                {
                    return;
                }

                ChangeState<ProcedurePreload>(_procedureOwner);
            }
            catch (OperationCanceledException exception)
            {
                if (IsCurrentRunning(attempt))
                {
                    FailAttempt(attempt, "两阶段更新已取消", exception);
                }
            }
            catch (TimeoutException exception)
            {
                FailAttempt(attempt, "更新器装载阶段超时", exception);
            }
            catch (Exception exception)
            {
                FailAttempt(attempt, "两阶段更新", exception);
            }
        }

        private async UniTask DownloadBootstrapAsync(StartupAttempt attempt, CancellationToken cancellationToken)
        {
            string tag = Settings.UpdateSetting.BootstrapTag;
            ResourceDownloaderOperation downloader = _resourceModule.CreateResourceDownloaderByTags(new[] { tag });
            if (downloader == null)
            {
                throw new InvalidOperationException("BOOTSTRAP downloader is null.");
            }

            _downloader = downloader;
            if (downloader.TotalDownloadCount == 0)
            {
                ReleaseDownloader(downloader, cancelIfRunning: false);
                _downloader = null;
                return;
            }

            LauncherMgr.ShowUI<LoadUpdateUI>(
                $"正在下载启动更新器：{downloader.TotalDownloadCount} 个文件，" +
                $"{downloader.TotalDownloadBytes / 1048576f:F1} MB");
            await RunDownloaderAsync(downloader, attempt, cancellationToken, "BOOTSTRAP");
            if (downloader.Status != EOperationStatus.Succeed)
            {
                throw new InvalidOperationException(
                    $"BOOTSTRAP download failed: {_lastDownloadError ?? downloader.Error ?? "unknown error"}");
            }

            ReleaseDownloader(downloader, cancelIfRunning: false);
            _downloader = null;
        }

        private async UniTask LoadMetadataAndUpdaterAsync(StartupAttempt attempt, CancellationToken cancellationToken)
        {
            UpdateSetting setting = Settings.UpdateSetting;
            TwoStageUpdateCoordinator.MarkIrreversible("metadata injection / GameUpdater Assembly.Load");

            if (_resourceModule.PlayMode == EPlayMode.EditorSimulateMode)
            {
                Assembly updater = FindLoadedAssembly(setting.BootstrapAssemblyName);
                if (updater == null)
                {
                    throw new InvalidOperationException(
                        $"EditorSimulate could not find loaded bootstrap assembly '{setting.BootstrapAssemblyName}'.");
                }

                TwoStageUpdateCoordinator.RecordLoadedAssembly(setting.BootstrapAssemblyName, updater);
                return;
            }

#if !ENABLE_HYBRIDCLR
            throw new InvalidOperationException("Two-stage Player cannot inject metadata without ENABLE_HYBRIDCLR.");
#else
            foreach (string metadataName in setting.AOTMetaAssemblies)
            {
                EnsureCurrentRunning(attempt);
                if (TwoStageUpdateCoordinator.IsMetadataInjected(metadataName))
                {
                    throw new InvalidOperationException($"Metadata '{metadataName}' was already injected.");
                }

                UpdateArtifactDescriptor artifact = TwoStageUpdateCoordinator.GetArtifact(metadataName);
                TextAsset textAsset = null;
                try
                {
                    textAsset = await _resourceModule.LoadAssetAsync<TextAsset>(artifact.Address, cancellationToken);
                    EnsureCurrentRunning(attempt);
                    if (textAsset == null)
                    {
                        throw new InvalidOperationException($"Metadata asset '{artifact.Address}' returned null.");
                    }

                    TwoStageUpdateCoordinator.ValidateArtifactBytes(artifact, textAsset.bytes);
                    LoadImageErrorCode code = RuntimeApi.LoadMetadataForAOTAssembly(
                        textAsset.bytes,
                        HomologousImageMode.SuperSet);
                    if (code != LoadImageErrorCode.OK)
                    {
                        throw new InvalidOperationException(
                            $"LoadMetadataForAOTAssembly failed for '{metadataName}' with '{code}'.");
                    }

                    TwoStageUpdateCoordinator.RecordMetadataInjected(metadataName);
                }
                finally
                {
                    if (textAsset != null)
                    {
                        _resourceModule.UnloadAsset(textAsset);
                    }
                }
            }

            UpdateArtifactDescriptor updaterArtifact = TwoStageUpdateCoordinator.GetArtifact(setting.BootstrapAssemblyName);
            TextAsset updaterAsset = null;
            try
            {
                updaterAsset = await _resourceModule.LoadAssetAsync<TextAsset>(updaterArtifact.Address, cancellationToken);
                EnsureCurrentRunning(attempt);
                if (updaterAsset == null)
                {
                    throw new InvalidOperationException($"Updater asset '{updaterArtifact.Address}' returned null.");
                }

                TwoStageUpdateCoordinator.ValidateArtifactBytes(updaterArtifact, updaterAsset.bytes);
                Assembly updaterAssembly;
                try
                {
                    updaterAssembly = Assembly.Load(updaterAsset.bytes);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Assembly.Load failed for bootstrap '{setting.BootstrapAssemblyName}'.",
                        exception);
                }

                TwoStageUpdateCoordinator.RecordLoadedAssembly(setting.BootstrapAssemblyName, updaterAssembly);
            }
            finally
            {
                if (updaterAsset != null)
                {
                    _resourceModule.UnloadAsset(updaterAsset);
                }
            }
#endif
        }

        private async UniTask<UpdateStageResult> InvokeUpdaterAsync(UpdateSessionContext context)
        {
            Assembly assembly = TwoStageUpdateCoordinator.GetLoadedAssembly(Settings.UpdateSetting.BootstrapAssemblyName);
            if (assembly == null)
            {
                throw new InvalidOperationException("GameUpdater assembly was not recorded after loading.");
            }

            Type entryType = assembly.GetType("GameUpdater.Entry", false);
            if (!UpdateStageEntryContract.TryFindRunAsync(entryType, out MethodInfo method, out string error))
            {
                throw new InvalidOperationException(error);
            }

            TwoStageUpdateCoordinator.BeginUpdaterInvocation();
            object invocationResult;
            try
            {
                invocationResult = method.Invoke(null, new object[] { context, this });
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(
                    "GameUpdater.Entry.RunAsync threw before returning its task.",
                    exception.InnerException ?? exception);
            }

            if (!(invocationResult is UniTask<UpdateStageResult> task))
            {
                throw new InvalidOperationException("GameUpdater.Entry.RunAsync returned an invalid task value.");
            }

            try
            {
                return await task;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("GameUpdater.Entry.RunAsync task failed.", exception);
            }
        }

        private static void ValidateUpdaterResult(UpdateStageResult result, UpdateSessionContext context)
        {
            if (result == null)
            {
                throw new InvalidOperationException("GameUpdater returned null.");
            }

            if (!Enum.IsDefined(typeof(UpdateStageStatus), result.Status))
            {
                throw new InvalidOperationException($"GameUpdater returned unknown status '{result.Status}'.");
            }

            if (!string.Equals(result.SessionId, context.SessionId, StringComparison.Ordinal) ||
                !string.Equals(result.ReleaseId, context.ReleaseId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("GameUpdater returned a result for a different session/release.");
            }

            if (result.Status != UpdateStageStatus.ResourcesReady)
            {
                throw new InvalidOperationException(
                    $"GameUpdater did not make resources ready: status={result.Status}, error={result.Error}");
            }
        }

        private void VerifyResourcesActuallyReady(UpdateSessionContext context)
        {
            if (!IsSessionCurrent(context))
            {
                throw new OperationCanceledException(context.CancellationToken);
            }

            if (_resourceModule.PlayMode == EPlayMode.EditorSimulateMode)
            {
                if (!string.Equals(_resourceModule.PackageVersion, context.PackageVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"EditorSimulate logical version drift: expected '{context.PackageVersion}', session='{_resourceModule.PackageVersion}'.");
                }

                return;
            }

            string actualVersion = _resourceModule.GetPackageVersion(context.PackageName);
            if (!string.Equals(actualVersion, context.PackageVersion, StringComparison.Ordinal) ||
                !string.Equals(_resourceModule.PackageVersion, context.PackageVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Resource version drift: expected '{context.PackageVersion}', manifest='{actualVersion}', session='{_resourceModule.PackageVersion}'.");
            }

            ResourceDownloaderOperation verification = _resourceModule.CreateResourceDownloader(context.PackageName);
            if (verification == null || verification.TotalDownloadCount != 0)
            {
                throw new InvalidOperationException(
                    $"GameUpdater reported ready but {verification?.TotalDownloadCount ?? -1} required bundle(s) remain.");
            }
        }

        private void ValidateBootstrapAssetClassification()
        {
            string tag = Settings.UpdateSetting.BootstrapTag;
            AssetInfo[] taggedAssets = _resourceModule.GetAssetInfos(tag);
            HashSet<string> taggedAddresses = new HashSet<string>(StringComparer.Ordinal);
            if (taggedAssets != null)
            {
                foreach (AssetInfo asset in taggedAssets)
                {
                    if (asset != null)
                    {
                        taggedAddresses.Add(asset.Address);
                    }
                }
            }

            UpdateReleaseDescriptor release = TwoStageUpdateCoordinator.Release;
            RequireTagged(taggedAddresses, release.BootstrapAssembly, tag);
            foreach (UpdateArtifactDescriptor metadata in release.MetadataAssemblies)
            {
                RequireTagged(taggedAddresses, metadata, tag);
            }

            foreach (UpdateArtifactDescriptor business in release.BusinessAssemblies)
            {
                if (taggedAddresses.Contains(business.Address))
                {
                    throw new InvalidOperationException(
                        $"Business assembly '{business.Name}' must not be tagged '{tag}'.");
                }
            }
        }

        private static void RequireTagged(HashSet<string> taggedAddresses, UpdateArtifactDescriptor artifact, string tag)
        {
            if (artifact == null || !taggedAddresses.Contains(artifact.Address))
            {
                throw new InvalidOperationException(
                    $"Required bootstrap artifact '{artifact?.Name ?? "<null>"}' is not tagged '{tag}'.");
            }
        }

        public bool IsSessionCurrent(UpdateSessionContext context)
        {
            return ModuleSystem.IsRunning && ReferenceEquals(context, _activeContext) &&
                   IsCurrentRunning(_currentAttempt) && TwoStageUpdateCoordinator.IsCurrent(context);
        }

        public void ReportStatus(UpdateSessionContext context, string message)
        {
            if (!IsSessionCurrent(context))
            {
                return;
            }

            LauncherMgr.ShowUI<LoadUpdateUI>(message ?? string.Empty);
        }

        public async UniTask<UpdateDownloadResult> DownloadRemainingResourcesAsync(UpdateSessionContext context)
        {
            if (!IsSessionCurrent(context))
            {
                return new UpdateDownloadResult(UpdateDownloadStatus.Cancelled, "Session is no longer current.");
            }

            ResourceDownloaderOperation downloader = _resourceModule.CreateResourceDownloader(context.PackageName);
            if (downloader == null)
            {
                return new UpdateDownloadResult(UpdateDownloadStatus.Failed, "Host created a null resource downloader.");
            }

            _downloader = downloader;
            if (downloader.TotalDownloadCount == 0)
            {
                ReleaseDownloader(downloader, cancelIfRunning: false);
                _downloader = null;
                return new UpdateDownloadResult(UpdateDownloadStatus.Succeeded, string.Empty);
            }

            LauncherMgr.ShowUI<LoadUpdateUI>(
                $"第二阶段需更新 {downloader.TotalDownloadCount} 个文件，" +
                $"{downloader.TotalDownloadBytes / 1048576f:F1} MB");
            try
            {
                await RunDownloaderAsync(downloader, _currentAttempt, context.CancellationToken, "SECOND_STAGE");
                if (!IsSessionCurrent(context))
                {
                    return new UpdateDownloadResult(UpdateDownloadStatus.Cancelled, "Session ended during download.");
                }

                return downloader.Status == EOperationStatus.Succeed
                    ? new UpdateDownloadResult(UpdateDownloadStatus.Succeeded, string.Empty)
                    : new UpdateDownloadResult(
                        UpdateDownloadStatus.Failed,
                        _lastDownloadError ?? downloader.Error ?? "Resource download failed without an error callback.");
            }
            catch (OperationCanceledException)
            {
                return new UpdateDownloadResult(UpdateDownloadStatus.Cancelled, "Resource download was cancelled.");
            }
            catch (Exception exception)
            {
                return new UpdateDownloadResult(UpdateDownloadStatus.Failed, exception.ToString());
            }
            finally
            {
                ReleaseDownloader(downloader, cancelIfRunning: true);
                if (ReferenceEquals(_downloader, downloader))
                {
                    _downloader = null;
                }
            }
        }

        public async UniTask<UpdateRetryDecision> RequestRetryOrCancelAsync(UpdateSessionContext context, string error)
        {
            if (!IsSessionCurrent(context))
            {
                return UpdateRetryDecision.Cancel;
            }

            UniTaskCompletionSource<UpdateRetryDecision> completion = new UniTaskCompletionSource<UpdateRetryDecision>();
            using (context.CancellationToken.Register(() => completion.TrySetResult(UpdateRetryDecision.Cancel)))
            {
                LauncherMgr.ShowMessageBox(
                    $"第二阶段资源更新失败，只能重试当前 release '{context.ReleaseId}' 或退出。\n\n" +
                    $"<color=#FF0000>{error}</color>",
                    () => completion.TrySetResult(UpdateRetryDecision.Retry),
                    () => completion.TrySetResult(UpdateRetryDecision.Cancel));
                return await completion.Task;
            }
        }

        private async UniTask RunDownloaderAsync(
            ResourceDownloaderOperation downloader,
            StartupAttempt attempt,
            CancellationToken cancellationToken,
            string stage)
        {
            _lastDownloadError = null;
            _lastProgressBytes = 0;
            _lastProgressTime = Time.realtimeSinceStartup;
            downloader.DownloadErrorCallback = data =>
            {
                if (ReferenceEquals(_downloader, downloader) && IsCurrentRunning(attempt))
                {
                    _lastDownloadError = $"{data.FileName}: {data.ErrorInfo}";
                    _lastProgressTime = Time.realtimeSinceStartup;
                }
            };
            downloader.DownloadUpdateCallback = data =>
            {
                if (!ReferenceEquals(_downloader, downloader) || !IsCurrentRunning(attempt))
                {
                    return;
                }

                if (data.CurrentDownloadBytes != _lastProgressBytes)
                {
                    _lastProgressBytes = data.CurrentDownloadBytes;
                    _lastProgressTime = Time.realtimeSinceStartup;
                }

                float progress = data.TotalDownloadBytes <= 0
                    ? 1f
                    : (float)data.CurrentDownloadBytes / data.TotalDownloadBytes;
                LauncherMgr.RefreshProgress(progress);
                LauncherMgr.ShowUI<LoadUpdateUI>(
                    $"{stage}: {data.CurrentDownloadCount}/{data.TotalDownloadCount} " +
                    $"({progress * 100f:F1}%)");
            };

            using (cancellationToken.Register(() =>
                   {
                       if (!downloader.IsDone)
                       {
                           downloader.CancelDownload();
                       }
                   }))
            {
                downloader.BeginDownload();
                float noProgressTimeout = Math.Max(5f, Settings.UpdateSetting.TwoStageNoProgressTimeoutSeconds);
                while (!downloader.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(0.25f),
                        DelayType.UnscaledDeltaTime,
                        PlayerLoopTiming.Update,
                        cancellationToken);
                    if (Time.realtimeSinceStartup - _lastProgressTime > noProgressTimeout)
                    {
                        downloader.CancelDownload();
                        throw new TimeoutException(
                            $"{stage} made no byte progress for {noProgressTimeout:F0} seconds.");
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static Assembly FindLoadedAssembly(string configuredName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals($"{assembly.GetName().Name}.dll", configuredName, StringComparison.Ordinal))
                {
                    return assembly;
                }
            }

            return null;
        }

        private bool IsCurrentRunning(StartupAttempt attempt)
        {
            return ModuleSystem.IsRunning && attempt != null && ReferenceEquals(_currentAttempt, attempt) && attempt.IsRunning;
        }

        private void EnsureCurrentRunning(StartupAttempt attempt)
        {
            if (!IsCurrentRunning(attempt))
            {
                throw new OperationCanceledException(attempt?.Token ?? default(CancellationToken));
            }
        }

        private void FailAttempt(StartupAttempt attempt, string stage, Exception exception)
        {
            if (!IsCurrentRunning(attempt) || !attempt.TryFail())
            {
                return;
            }

            string message = exception?.ToString() ?? "Unknown error.";
            bool canRetry = !TwoStageUpdateCoordinator.IsIrreversible;
            string action = canRetry
                ? "尚未装载程序集，可重试同一固定 release 或退出。"
                : "已跨越程序集装载边界，本进程不能回退或重载，请退出后重新启动。";
            string userMessage = $"两阶段更新失败！阶段：{stage}；release：{TwoStageUpdateCoordinator.ReleaseId}\n\n" +
                                 $"<color=#FF0000>{message}</color>\n\n{action}";
            Log.Error(userMessage);
            LauncherMgr.ShowUI<LoadUpdateUI>("两阶段更新失败！");
            LauncherMgr.ShowMessageBox(
                userMessage,
                canRetry ? (Action)(() => Retry(attempt)) : Application.Quit,
                Application.Quit);
        }

        private void Retry(StartupAttempt failedAttempt)
        {
            if (!ReferenceEquals(_currentAttempt, failedAttempt) ||
                failedAttempt == null || failedAttempt.State != StartupAttempt.TerminalState.Failed ||
                TwoStageUpdateCoordinator.IsIrreversible)
            {
                return;
            }

            LauncherMgr.ShowUI<LoadUpdateUI>("正在重试启动更新器...");
            StartAttempt();
        }

        private static void ReleaseDownloader(ResourceDownloaderOperation downloader, bool cancelIfRunning)
        {
            if (downloader == null)
            {
                return;
            }

            downloader.DownloadErrorCallback = null;
            downloader.DownloadUpdateCallback = null;
            if (cancelIfRunning && !downloader.IsDone)
            {
                downloader.CancelDownload();
            }
        }
    }
}
