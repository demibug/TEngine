using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    /// <summary>
    /// 构建开始时冻结的两阶段发布快照。快照不持有可变的 UpdateSetting 引用。
    /// </summary>
    public sealed class TwoStageBuildSnapshot
    {
        private readonly BuildConfig _config;

        private TwoStageBuildSnapshot(
            BuildConfig config,
            string publishRoot,
            bool twoStageEnabled,
            int contractVersion,
            string basePlayerId,
            string platform,
            string channel,
            string packageName,
            string bootstrapAssemblyName,
            string fixedEntryUrl,
            string primaryHostUrl,
            string fallbackHostUrl,
            bool allowInsecureLoopbackHttp,
            IReadOnlyList<string> hotUpdateAssemblies,
            IReadOnlyList<string> metadataAssemblies)
        {
            _config = config;
            PublishRoot = publishRoot;
            TwoStageEnabled = twoStageEnabled;
            ContractVersion = contractVersion;
            BasePlayerId = basePlayerId;
            Platform = platform;
            Channel = channel;
            PackageName = packageName;
            BootstrapAssemblyName = bootstrapAssemblyName;
            FixedEntryUrl = fixedEntryUrl;
            PrimaryHostUrl = primaryHostUrl;
            FallbackHostUrl = fallbackHostUrl;
            AllowInsecureLoopbackHttp = allowInsecureLoopbackHttp;
            HotUpdateAssemblies = hotUpdateAssemblies;
            MetadataAssemblies = metadataAssemblies;
        }

        /// <summary>
        /// 返回配置副本，调用方不能通过结果对象修改编排期间冻结的配置。
        /// </summary>
        public BuildConfig Config => TwoStageBuildAndPublish.CloneBuildConfig(_config);
        public BuildConfig BuildConfig => Config;
        public string PublishRoot { get; }
        public bool TwoStageEnabled { get; }
        public int ContractVersion { get; }
        public string BasePlayerId { get; }
        public string Platform { get; }
        public string Channel { get; }
        public string PackageName { get; }
        public string BootstrapAssemblyName { get; }
        public string FixedEntryUrl { get; }
        public string PrimaryHostUrl { get; }
        public string FallbackHostUrl { get; }
        public bool AllowInsecureLoopbackHttp { get; }
        public IReadOnlyList<string> HotUpdateAssemblies { get; }
        public IReadOnlyList<string> MetadataAssemblies { get; }

        /// <summary>
        /// 从当前编辑器配置捕获不可变发布输入。
        /// </summary>
        public static TwoStageBuildSnapshot Capture(BuildConfig config, string publishRoot)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            UpdateSetting setting = Settings.UpdateSetting;
            string platform = string.Empty;
            try
            {
                platform = TwoStageReleaseBuilder.GetPlatformName(config.BuildTarget);
            }
            catch
            {
                // 目标平台错误交给发布预检返回，捕获快照本身不能掩盖这个错误。
            }

            return new TwoStageBuildSnapshot(
                TwoStageBuildAndPublish.CloneBuildConfig(config),
                publishRoot,
                setting != null && setting.EnableTwoStageUpdate,
                setting?.TwoStageContractVersion ?? 0,
                setting?.BasePlayerId,
                platform,
                setting?.Channel,
                ReleaseTools.PackageName,
                setting?.BootstrapAssemblyName,
                setting?.TwoStageReleaseEntryUrl,
                setting?.TwoStageHostServerUrl,
                setting?.TwoStageFallbackHostServerUrl,
                setting != null && setting.AllowInsecureLoopbackHttp,
                Copy(setting?.HotUpdateAssemblies),
                Copy(setting?.AOTMetaAssemblies));
        }

        public UpdateReleaseValidationContext CreateValidationContext()
        {
            return new UpdateReleaseValidationContext
            {
                ContractVersion = ContractVersion,
                BasePlayerId = BasePlayerId,
                Platform = Platform,
                Channel = Channel,
                PackageName = PackageName,
                ManifestId = YooAssetSettingsData.GetManifestBinaryFileName(
                    PackageName,
                    Config.PackageVersion),
                BootstrapAssemblyName = BootstrapAssemblyName,
                HotUpdateAssemblies = HotUpdateAssemblies,
                MetadataAssemblies = MetadataAssemblies,
            };
        }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> values)
        {
            return values == null ? null : new List<string>(values).AsReadOnly();
        }
    }

    /// <summary>
    /// 构建和本地发布的组合结果，分别暴露构建与发布状态。
    /// </summary>
    public sealed class TwoStageBuildAndPublishResult
    {
        public BuildExecutionResult BuildResult { get; internal set; }
        public TwoStageReleasePublishResult PublishResult { get; internal set; }

        public bool PublishSucceeded => PublishResult != null && PublishResult.Succeeded;
        public string PublishDirectory => PublishResult?.PublishDirectory;
        public string EntryPath => PublishResult?.EntryPath;
        public string Error
        {
            get
            {
                if (BuildResult == null)
                    return "构建结果为空。";
                if (!BuildResult.Succeeded)
                    return BuildResult.Error;
                return PublishResult?.Error;
            }
        }

        public bool Succeeded => BuildResult != null && BuildResult.Succeeded && PublishSucceeded;
    }

    /// <summary>
    /// 固定入口发布编排层。它只在原构建流程完整成功后调用发布器。
    /// </summary>
    public static class TwoStageBuildAndPublish
    {
        public static UniTask<TwoStageBuildAndPublishResult> ExecuteAsync(
            BuildConfig config,
            string publishRoot,
            bool buildPlayer,
            CancellationToken cancellationToken = default(CancellationToken),
            BuildStageImpl implementation = null)
        {
            return ExecuteCaptureAsync(config, publishRoot, buildPlayer, cancellationToken, implementation);
        }

        /// <summary>
        /// 测试和编辑器调用可直接使用已冻结快照，并注入发布器替身。
        /// </summary>
        public static UniTask<TwoStageBuildAndPublishResult> ExecuteAsync(
            TwoStageBuildSnapshot snapshot,
            bool buildPlayer,
            CancellationToken cancellationToken = default(CancellationToken),
            BuildStageImpl implementation = null,
            Func<TwoStageBuildSnapshot, BuildExecutionResult, CancellationToken,
                UniTask<TwoStageReleasePublishResult>> publisher = null)
        {
            return ExecuteSnapshotAsync(
                snapshot,
                buildPlayer,
                cancellationToken,
                implementation,
                publisher);
        }

        internal static BuildConfig CloneBuildConfig(BuildConfig source)
        {
            if (source == null)
                return null;

            return new BuildConfig
            {
                BuildTarget = source.BuildTarget,
                BuildPipeline = source.BuildPipeline,
                CompressOption = source.CompressOption,
                EncryptionType = source.EncryptionType,
                PackageVersion = source.PackageVersion,
                ReleaseId = source.ReleaseId,
                OutputRoot = source.OutputRoot,
                MinimalPackage = source.MinimalPackage,
                RetainTags = source.RetainTags,
                EnableSharePackRule = source.EnableSharePackRule,
                UseAssetDependencyDB = source.UseAssetDependencyDB,
                ClearBuildCache = source.ClearBuildCache,
                VerifyBuildingResult = source.VerifyBuildingResult,
                BuildinFileCopyOption = source.BuildinFileCopyOption,
                FileNameStyle = source.FileNameStyle,
                BuildHotFixDll = source.BuildHotFixDll,
                BuildPlayer = source.BuildPlayer,
                PlayerPlatform = source.PlayerPlatform,
                PlayerOutputPath = source.PlayerOutputPath,
            };
        }

        private static async UniTask<TwoStageBuildAndPublishResult> ExecuteCaptureAsync(
            BuildConfig config,
            string publishRoot,
            bool buildPlayer,
            CancellationToken cancellationToken,
            BuildStageImpl implementation)
        {
            TwoStageBuildSnapshot snapshot;
            try
            {
                snapshot = TwoStageBuildSnapshot.Capture(config, publishRoot);
            }
            catch (Exception exception)
            {
                return CreatePreflightFailure(
                    config,
                    $"捕获构建/发布快照失败：{exception.Message}",
                    exception);
            }

            return await ExecuteSnapshotAsync(
                snapshot,
                buildPlayer,
                cancellationToken,
                implementation,
                null);
        }

        private static async UniTask<TwoStageBuildAndPublishResult> ExecuteSnapshotAsync(
            TwoStageBuildSnapshot snapshot,
            bool buildPlayer,
            CancellationToken cancellationToken,
            BuildStageImpl implementation,
            Func<TwoStageBuildSnapshot, BuildExecutionResult, CancellationToken,
                UniTask<TwoStageReleasePublishResult>> publisher)
        {
            if (snapshot == null)
                return CreatePreflightFailure(null, "两阶段发布快照为空。", null);

            List<string> preflightErrors = TwoStageReleasePublisher.ValidateRequest(snapshot);
            if (preflightErrors.Count > 0)
            {
                return CreatePreflightFailure(
                    snapshot.Config,
                    string.Join("\n", preflightErrors),
                    null);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                BuildExecutionResult cancelled = CreateBuildResult(
                    snapshot.Config,
                    BuildStatus.Cancelled,
                    BuildStage.Preflight,
                    "发布前已取消。",
                    null);
                return new TwoStageBuildAndPublishResult
                {
                    BuildResult = cancelled,
                    PublishResult = TwoStageReleasePublishResult.CreateCancelled(
                        snapshot.PublishRoot,
                        "构建开始前已取消。"),
                };
            }

            BuildExecutionResult buildResult;
            try
            {
                BuildConfig buildConfig = CloneBuildConfig(snapshot.Config);
                buildResult = ReleaseTools.ExecuteBuildWithResult(
                    buildConfig,
                    buildPlayer,
                    implementation);
            }
            catch (Exception exception)
            {
                buildResult = CreateBuildResult(
                    snapshot.Config,
                    BuildStatus.Failed,
                    BuildStage.Preflight,
                    $"构建流程异常：{exception.Message}",
                    exception);
            }

            if (buildResult == null)
            {
                buildResult = CreateBuildResult(
                    snapshot.Config,
                    BuildStatus.Failed,
                    BuildStage.Preflight,
                    "构建流程返回空结果。",
                    null);
            }

            if (!buildResult.Succeeded)
            {
                return new TwoStageBuildAndPublishResult
                {
                    BuildResult = buildResult,
                    PublishResult = TwoStageReleasePublishResult.CreateNotRun(
                        snapshot.PublishRoot,
                        "构建未成功，未执行本地发布。"),
                };
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return new TwoStageBuildAndPublishResult
                {
                    BuildResult = buildResult,
                    PublishResult = TwoStageReleasePublishResult.CreateCancelled(
                        snapshot.PublishRoot,
                        "构建完成后、入口提交前已取消；未切换 current.json。"),
                };
            }

            TwoStageReleasePublishResult publishResult;
            try
            {
                Func<TwoStageBuildSnapshot, BuildExecutionResult, CancellationToken,
                    UniTask<TwoStageReleasePublishResult>> publish = publisher ??
                    ((currentSnapshot, currentBuild, token) =>
                        TwoStageReleasePublisher.PublishAsync(currentSnapshot, currentBuild, token));
                publishResult = await publish(snapshot, buildResult, cancellationToken);
                if (publishResult == null)
                {
                    publishResult = TwoStageReleasePublishResult.CreateFailed(
                        snapshot.PublishRoot,
                        "发布器返回空结果。",
                        null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                publishResult = TwoStageReleasePublishResult.CreateCancelled(
                    snapshot.PublishRoot,
                    "入口提交前已取消；未切换 current.json。");
            }
            catch (Exception exception)
            {
                publishResult = TwoStageReleasePublishResult.CreateFailed(
                    snapshot.PublishRoot,
                    $"本地发布异常：{exception.Message}",
                    exception);
            }

            return new TwoStageBuildAndPublishResult
            {
                BuildResult = buildResult,
                PublishResult = publishResult,
            };
        }

        private static TwoStageBuildAndPublishResult CreatePreflightFailure(
            BuildConfig config,
            string error,
            Exception exception)
        {
            BuildExecutionResult result = CreateBuildResult(
                config,
                BuildStatus.Failed,
                BuildStage.Preflight,
                error,
                exception);
            return new TwoStageBuildAndPublishResult
            {
                BuildResult = result,
                PublishResult = TwoStageReleasePublishResult.CreateNotRun(
                    null,
                    "发布预检失败，未执行本地发布。"),
            };
        }

        private static BuildExecutionResult CreateBuildResult(
            BuildConfig config,
            BuildStatus status,
            BuildStage stage,
            string error,
            Exception exception)
        {
            return new BuildExecutionResult
            {
                Status = status,
                Stage = stage,
                Target = config?.BuildTarget ?? BuildTarget.NoTarget,
                PackageVersion = config?.PackageVersion,
                Error = error,
                Exception = exception,
            };
        }
    }
}
