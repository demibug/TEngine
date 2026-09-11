using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;

namespace Procedure
{
    /// <summary>
    /// 单进程两阶段更新会话。主包独占 release、不可逆闸门、注入/装载记录和成功检查点。
    /// </summary>
    internal static class TwoStageUpdateCoordinator
    {
        private const int MaxEntryBytes = UpdateReleaseEntryValidator.MaximumEntryBytes;
        private const int MaxDescriptorBytes = 1024 * 1024;

        private static readonly HashSet<string> InjectedMetadata = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Assembly> LoadedAssemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        private static UpdateSetting _setting;
        private static UpdateReleaseDescriptor _release;
        private static string _primaryHostUrl;
        private static string _fallbackHostUrl;
        private static string _sessionId;
        private static bool _prepared;
        private static bool _irreversible;
        private static bool _updaterInvocationStarted;
        private static bool _resourcesReady;
        private static bool _shutdown;
        private static CancellationTokenSource _sessionCancellation;

        public static bool IsEnabled => Settings.UpdateSetting != null && Settings.UpdateSetting.EnableTwoStageUpdate;
        public static bool IsPrepared => _prepared && !_shutdown;
        public static bool IsIrreversible => _irreversible;
        public static bool ResourcesReady => _resourcesReady;
        public static UpdateReleaseDescriptor Release => _release;
        public static string PackageVersion => _release?.PackageVersion;
        public static string ReleaseId => _release?.ReleaseId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
            _setting = null;
            _release = null;
            _primaryHostUrl = null;
            _fallbackHostUrl = null;
            _sessionId = null;
            _prepared = false;
            _irreversible = false;
            _updaterInvocationStarted = false;
            _resourcesReady = false;
            _shutdown = false;
            InjectedMetadata.Clear();
            LoadedAssemblies.Clear();
        }

        public static async UniTask PrepareAsync(UpdateSetting setting, IResourceModule resourceModule, CancellationToken cancellationToken)
        {
            if (_prepared)
            {
                throw new InvalidOperationException("Two-stage update session is already prepared.");
            }

            if (_irreversible)
            {
                throw new InvalidOperationException("A two-stage update already crossed the irreversible load boundary in this process.");
            }

            ValidateRuntimeConfiguration(setting, resourceModule);

            string json;
            string selectedReleaseId = null;
            if (resourceModule.PlayMode == EPlayMode.EditorSimulateMode)
            {
                json = setting.EditorSimulateReleaseDescriptorJson;
                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new InvalidOperationException(
                        "EditorSimulate two-stage mode requires EditorSimulateReleaseDescriptorJson for logical verification.");
                }
            }
            else
            {
                switch (setting.TwoStageReleaseSourceMode)
                {
                    case TwoStageReleaseSourceMode.DirectDescriptor:
                        json = await DownloadDescriptorAsync(
                            setting.TwoStageReleaseDescriptorUrl,
                            setting,
                            cancellationToken,
                            rejectRedirect: false);
                        break;

                    case TwoStageReleaseSourceMode.FixedEntry:
                        UpdateReleaseEntry entry = await DownloadEntryAsync(
                            setting.TwoStageReleaseDescriptorUrl,
                            setting,
                            cancellationToken);
                        selectedReleaseId = entry.ReleaseId;
                        string descriptorUrl = TwoStageReleaseUrl.BuildDescriptorUrl(
                            setting.TwoStageReleaseDescriptorUrl,
                            selectedReleaseId,
                            setting.AllowInsecureLoopbackHttp);
                        json = await DownloadDescriptorAsync(
                            descriptorUrl,
                            setting,
                            cancellationToken,
                            rejectRedirect: true);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Two-stage release source mode '{(int)setting.TwoStageReleaseSourceMode}' is not supported.");
                }
            }

            UpdateReleaseDescriptor descriptor;
            try
            {
                descriptor = JsonUtility.FromJson<UpdateReleaseDescriptor>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Release descriptor JSON is invalid.", exception);
            }

            if (descriptor == null)
            {
                throw new InvalidOperationException("Release descriptor JSON is null.");
            }

            if (resourceModule.PlayMode != EPlayMode.EditorSimulateMode &&
                setting.TwoStageReleaseSourceMode == TwoStageReleaseSourceMode.FixedEntry &&
                !string.Equals(descriptor.ReleaseId, selectedReleaseId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"固定入口 ReleaseId 与 descriptor 不一致：入口为 '{selectedReleaseId}'，descriptor 为 '{descriptor.ReleaseId ?? "<null>"}'。");
            }

            UpdateReleaseValidationContext expected = new UpdateReleaseValidationContext
            {
                ContractVersion = setting.TwoStageContractVersion,
                BasePlayerId = setting.BasePlayerId,
                Platform = UpdateSetting.GetPlatformName(),
                Channel = setting.Channel,
                PackageName = resourceModule.DefaultPackageName,
                ManifestId = YooAssetSettingsData.GetManifestBinaryFileName(
                    resourceModule.DefaultPackageName, descriptor.PackageVersion),
                BootstrapAssemblyName = setting.BootstrapAssemblyName,
                HotUpdateAssemblies = setting.HotUpdateAssemblies,
                MetadataAssemblies = setting.AOTMetaAssemblies,
            };
            List<string> errors = UpdateReleaseDescriptorValidator.Validate(descriptor, expected);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Release descriptor validation failed:\n" + string.Join("\n", errors));
            }

            if (!(resourceModule is IPinnedManifestIntegrity manifestIntegrity))
                throw new InvalidOperationException(
                    "Resource module does not support binding the release digest to the active manifest.");

            manifestIntegrity.ConfigurePinnedManifest(
                descriptor.PackageName, descriptor.PackageVersion, descriptor.ManifestSha256);

            string primaryHostUrl = null;
            string fallbackHostUrl = null;
            if (resourceModule.PlayMode == EPlayMode.HostPlayMode)
            {
                // RemoteServices captures these strings during InitPackage. They are pinned here, before that call.
                if (setting.TwoStageReleaseSourceMode == TwoStageReleaseSourceMode.FixedEntry)
                {
                    primaryHostUrl = TwoStageReleaseUrl.BuildReleaseRootUrl(
                        setting.TwoStageHostServerUrl,
                        descriptor.ReleaseId,
                        setting.AllowInsecureLoopbackHttp);
                    fallbackHostUrl = TwoStageReleaseUrl.BuildReleaseRootUrl(
                        setting.TwoStageFallbackHostServerUrl,
                        descriptor.ReleaseId,
                        setting.AllowInsecureLoopbackHttp);
                }
                else
                {
                    primaryHostUrl = NormalizeTrustedUrl(setting.TwoStageHostServerUrl);
                    fallbackHostUrl = NormalizeTrustedUrl(setting.TwoStageFallbackHostServerUrl);
                }

                resourceModule.SetRemoteServicesUrl(primaryHostUrl, fallbackHostUrl);
            }

            _setting = setting;
            _release = descriptor;
            _primaryHostUrl = primaryHostUrl;
            _fallbackHostUrl = fallbackHostUrl;
            _sessionId = Guid.NewGuid().ToString("N");
            _sessionCancellation = new CancellationTokenSource();
            _prepared = true;
            RootModule.BeforeShutdown += OnBeforeShutdown;
        }

        public static UpdateSessionContext CreateContext(CancellationToken token, out CancellationTokenSource linkedCancellation)
        {
            EnsurePrepared();
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _sessionCancellation.Token);
            return CreateContext(linkedCancellation.Token);
        }

        public static bool IsCurrent(UpdateSessionContext context)
        {
            return context != null && IsPrepared &&
                   string.Equals(context.SessionId, _sessionId, StringComparison.Ordinal) &&
                   string.Equals(context.ReleaseId, _release.ReleaseId, StringComparison.Ordinal) &&
                   string.Equals(context.PackageVersion, _release.PackageVersion, StringComparison.Ordinal) &&
                   context.ContractVersion == _release.ContractVersion &&
                   !context.CancellationToken.IsCancellationRequested;
        }

        public static UpdateArtifactDescriptor GetArtifact(string assemblyName)
        {
            EnsurePrepared();
            if (_release.BootstrapAssembly != null &&
                string.Equals(_release.BootstrapAssembly.Name, assemblyName, StringComparison.Ordinal))
            {
                return _release.BootstrapAssembly;
            }

            UpdateArtifactDescriptor artifact = FindArtifact(_release.BusinessAssemblies, assemblyName);
            if (artifact != null)
            {
                return artifact;
            }

            artifact = FindArtifact(_release.MetadataAssemblies, assemblyName);
            if (artifact != null)
            {
                return artifact;
            }

            throw new InvalidOperationException($"Release does not declare required artifact '{assemblyName}'.");
        }

        public static void ValidateArtifactBytes(UpdateArtifactDescriptor artifact, byte[] bytes)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            if (!UpdateReleaseDescriptorValidator.HashMatches(bytes, artifact.Sha256))
            {
                throw new InvalidOperationException($"Artifact SHA-256 mismatch for '{artifact.Name}' at '{artifact.Address}'.");
            }
        }

        public static void MarkIrreversible(string reason)
        {
            EnsurePrepared();
            if (_irreversible)
            {
                throw new InvalidOperationException($"Irreversible update boundary was already crossed; duplicate operation '{reason}'.");
            }

            // Set before the operation: metadata injection / Assembly.Load may have side effects even when it throws.
            _irreversible = true;
            Log.Info($"Two-stage update crossed irreversible boundary: {reason}");
        }

        public static bool IsMetadataInjected(string assemblyName)
        {
            return InjectedMetadata.Contains(assemblyName);
        }

        public static void RecordMetadataInjected(string assemblyName)
        {
            if (!InjectedMetadata.Add(assemblyName))
            {
                throw new InvalidOperationException($"Metadata '{assemblyName}' was injected more than once in this session.");
            }
        }

        public static Assembly GetLoadedAssembly(string assemblyName)
        {
            LoadedAssemblies.TryGetValue(assemblyName, out Assembly assembly);
            return assembly;
        }

        public static void RecordLoadedAssembly(string configuredName, Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            string actual = $"{assembly.GetName().Name}.dll";
            if (!string.Equals(configuredName, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Configured assembly '{configuredName}' resolved to identity '{actual}'.");
            }

            if (LoadedAssemblies.ContainsKey(configuredName))
            {
                throw new InvalidOperationException($"Assembly '{configuredName}' was loaded more than once in this session.");
            }

            LoadedAssemblies.Add(configuredName, assembly);
        }

        public static void BeginUpdaterInvocation()
        {
            EnsurePrepared();
            if (_updaterInvocationStarted)
            {
                throw new InvalidOperationException("GameUpdater.Entry.RunAsync was already invoked in this process.");
            }

            _updaterInvocationStarted = true;
        }

        public static void MarkResourcesReady()
        {
            EnsurePrepared();
            if (_resourcesReady)
            {
                throw new InvalidOperationException("Two-stage resources were already marked ready.");
            }

            _resourcesReady = true;
        }

        /// <summary>
        /// 探测所有可达的固定主/备源；任一可达源内容与 descriptor 不符即拒绝。
        /// 此检查只负责发现主备不一致，最终信任仍由实际 YooAsset 反序列化入口校验建立。
        /// </summary>
        public static async UniTask VerifyPinnedManifestSourcesAsync(CancellationToken cancellationToken)
        {
            EnsurePrepared();
            string[] urls = string.Equals(_primaryHostUrl, _fallbackHostUrl, StringComparison.Ordinal)
                ? new[] { CombineTrustedUrl(_primaryHostUrl, _release.ManifestId) }
                : new[]
                {
                    CombineTrustedUrl(_primaryHostUrl, _release.ManifestId),
                    CombineTrustedUrl(_fallbackHostUrl, _release.ManifestId),
                };
            int reachable = 0;
            List<Exception> failures = new List<Exception>();
            foreach (string url in urls)
            {
                try
                {
                    byte[] bytes = await DownloadBytesAsync(
                        url,
                        _setting,
                        cancellationToken,
                        maxBytes: 0,
                        appendCacheBust: false,
                        rejectRedirect: false,
                        responseName: "固定清单");
                    reachable++;
                    if (!UpdateReleaseDescriptorValidator.HashMatches(bytes, _release.ManifestSha256))
                        throw new InvalidOperationException(
                            $"Pinned manifest source SHA-256 mismatch at '{url}'.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.StartsWith("Pinned manifest source SHA-256 mismatch", StringComparison.Ordinal))
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(new InvalidOperationException(
                        $"Pinned manifest source is unavailable: '{url}'.", exception));
                }
            }

            if (reachable == 0)
                throw new AggregateException("No pinned manifest source was reachable.", failures);
        }

        /// <summary>
        /// 在业务入口提交前再次确认会话未漂移且 Host 全包没有待下载资源。
        /// </summary>
        public static void VerifyReadyForEntry(IResourceModule resourceModule)
        {
            EnsurePrepared();
            if (!_resourcesReady)
            {
                throw new InvalidOperationException("Two-stage resources have not reached the ready state.");
            }

            if (resourceModule == null)
            {
                throw new ArgumentNullException(nameof(resourceModule));
            }

            if (!string.Equals(resourceModule.PackageVersion, _release.PackageVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Package session version drift before entry: expected '{_release.PackageVersion}', actual '{resourceModule.PackageVersion}'.");
            }

            if (resourceModule.PlayMode == EPlayMode.EditorSimulateMode)
            {
                return;
            }

            string actualVersion = resourceModule.GetPackageVersion(_release.PackageName);
            if (!string.Equals(actualVersion, _release.PackageVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Loaded manifest drift before entry: expected '{_release.PackageVersion}', actual '{actualVersion}'.");
            }

            ResourceDownloaderOperation verification = resourceModule.CreateResourceDownloader(_release.PackageName);
            if (verification == null || verification.TotalDownloadCount != 0)
            {
                throw new InvalidOperationException(
                    $"Resources drifted before entry; {verification?.TotalDownloadCount ?? -1} required bundle(s) remain.");
            }
        }

        public static bool TryWriteEntryCompletedCheckpoint(out string error)
        {
            error = null;
            try
            {
                EnsurePrepared();
                if (!_resourcesReady)
                {
                    throw new InvalidOperationException("Cannot write EntryCompleted before resources are ready.");
                }

                EntryCompletedCheckpoint checkpoint = EntryCompletedCheckpoint.Create(_release);
                string json = JsonUtility.ToJson(checkpoint, true);
                if (!EntryCompletedCheckpoint.TryValidate(json, _release, out string validationError))
                {
                    throw new InvalidOperationException(
                        $"Generated EntryCompleted checkpoint failed self-validation: {validationError}");
                }

                string directory = Path.Combine(Application.persistentDataPath, "two-stage-entry");
                Directory.CreateDirectory(directory);
                string key = UpdateReleaseDescriptorValidator.ComputeSha256(
                    Encoding.UTF8.GetBytes($"{_release.BasePlayerId}|{_release.Platform}|{_release.Channel}|{_release.PackageName}"));
                string destination = Path.Combine(directory, key + ".json");
                if (!TryWriteTextAtomically(destination, json, out error))
                {
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                return false;
            }
        }

        internal static bool TryValidateEntryCompletedCheckpointJson(
            string json,
            UpdateReleaseDescriptor release,
            out string error)
        {
            return EntryCompletedCheckpoint.TryValidate(json, release, out error);
        }

        internal static bool TryWriteTextAtomically(string destination, string contents, out string error)
        {
            error = null;
            string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, contents, new UTF8Encoding(false));
                if (File.Exists(destination))
                {
                    File.Replace(temporary, destination, null);
                }
                else
                {
                    File.Move(temporary, destination);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (Exception cleanupException)
                {
                    error = string.IsNullOrEmpty(error)
                        ? cleanupException.ToString()
                        : error + Environment.NewLine + cleanupException;
                }
            }
        }

        public static List<string> GetBusinessAssemblyNames()
        {
            EnsurePrepared();
            List<string> result = new List<string>();
            foreach (UpdateArtifactDescriptor artifact in _release.BusinessAssemblies)
            {
                result.Add(artifact.Name);
            }

            return result;
        }

        private static UpdateSessionContext CreateContext(CancellationToken cancellationToken)
        {
            return new UpdateSessionContext(
                _sessionId,
                _release.ContractVersion,
                _release.BasePlayerId,
                _release.Platform,
                _release.Channel,
                _release.PackageName,
                _release.PackageVersion,
                _release.ReleaseId,
                _release.ManifestId,
                _release.ManifestSha256,
                cancellationToken);
        }

        private static async UniTask<UpdateReleaseEntry> DownloadEntryAsync(
            string url,
            UpdateSetting setting,
            CancellationToken cancellationToken)
        {
            byte[] bytes = await DownloadBytesAsync(
                url,
                setting,
                cancellationToken,
                MaxEntryBytes,
                appendCacheBust: true,
                rejectRedirect: true,
                responseName: "固定入口");

            string json = DecodeUtf8(bytes, "固定入口");
            if (!UpdateReleaseEntryValidator.TryParse(json, out UpdateReleaseEntry entry, out string error))
            {
                throw new InvalidOperationException($"固定入口校验失败：{error}");
            }

            return entry;
        }

        private static async UniTask<string> DownloadDescriptorAsync(
            string url,
            UpdateSetting setting,
            CancellationToken cancellationToken,
            bool rejectRedirect)
        {
            byte[] bytes = await DownloadBytesAsync(
                url,
                setting,
                cancellationToken,
                MaxDescriptorBytes,
                appendCacheBust: false,
                rejectRedirect: rejectRedirect,
                responseName: "Release descriptor");

            // 旧 DirectDescriptor 路径保持原有 UTF-8 容错解码；固定 descriptor 则使用严格文本校验。
            string json = rejectRedirect
                ? DecodeUtf8(bytes, "Release descriptor")
                : Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Release descriptor response is empty.");
            }

            return json;
        }

        private static async UniTask<byte[]> DownloadBytesAsync(
            string url,
            UpdateSetting setting,
            CancellationToken cancellationToken,
            int maxBytes,
            bool appendCacheBust,
            bool rejectRedirect,
            string responseName)
        {
            ValidateTrustedUrl(url, setting);
            string requestUrl = appendCacheBust
                ? TwoStageReleaseUrl.AppendCacheBust(url, Guid.NewGuid().ToString("N"))
                : url;
            using (UnityWebRequest request = UnityWebRequest.Get(requestUrl))
            {
                request.timeout = 60;
                if (rejectRedirect)
                {
                    request.redirectLimit = 0;
                }

                if (appendCacheBust)
                {
                    request.SetRequestHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                    request.SetRequestHeader("Pragma", "no-cache");
                }

                LimitedDownloadHandler limitedHandler = null;
                if (maxBytes > 0)
                {
                    limitedHandler = new LimitedDownloadHandler(maxBytes);
                    request.downloadHandler = limitedHandler;
                }

                Exception requestException = null;
                try
                {
                    await request.SendWebRequest().WithCancellation(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnityWebRequestException exception)
                {
                    // UniTask 会在 HTTP、网络或数据处理失败时于 await 处抛出，先保存异常再按请求状态分类。
                    requestException = exception;
                }

                if (limitedHandler != null && limitedHandler.ExceededLimit)
                {
                    throw new InvalidOperationException(
                        $"{responseName} 超过 {maxBytes} 字节上限，已终止请求：'{url}'。");
                }

                if (rejectRedirect && request.responseCode >= 300 && request.responseCode < 400)
                {
                    throw new InvalidOperationException(
                        $"{responseName} 不允许重定向：url='{url}', code={request.responseCode}。");
                }

                if (request.result != UnityWebRequest.Result.Success || requestException != null)
                {
                    throw new InvalidOperationException(
                        $"{responseName}请求失败：url='{url}', code={request.responseCode}, error='{request.error}'。",
                        requestException);
                }

                byte[] bytes = limitedHandler != null
                    ? limitedHandler.GetReceivedBytes()
                    : request.downloadHandler?.data;
                if (bytes == null || bytes.Length == 0)
                {
                    throw new InvalidOperationException($"{responseName}响应为空：url='{url}'。");
                }

                return bytes;
            }
        }

        private static string DecodeUtf8(byte[] bytes, string responseName)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException($"{responseName}不是有效的 UTF-8 文本。", exception);
            }
        }

        private static void ValidateRuntimeConfiguration(UpdateSetting setting, IResourceModule resourceModule)
        {
            if (setting == null)
            {
                throw new InvalidOperationException("UpdateSetting is missing.");
            }

            if (!Enum.IsDefined(typeof(TwoStageReleaseSourceMode), setting.TwoStageReleaseSourceMode))
            {
                throw new InvalidOperationException(
                    $"Two-stage release source mode '{(int)setting.TwoStageReleaseSourceMode}' is not supported.");
            }

            if (!setting.EnableTwoStageUpdate)
            {
                throw new InvalidOperationException("Two-stage update is disabled.");
            }

            if (resourceModule == null)
            {
                throw new InvalidOperationException("Resource module is missing.");
            }

            if (resourceModule.PlayMode != EPlayMode.HostPlayMode &&
                resourceModule.PlayMode != EPlayMode.EditorSimulateMode)
            {
                throw new InvalidOperationException(
                    $"Two-stage update supports only HostPlayMode (or EditorSimulate logical verification), actual '{resourceModule.PlayMode}'.");
            }

            if (setting.UpdateStyle != UpdateStyle.Force)
            {
                throw new InvalidOperationException("Two-stage update v1 requires UpdateStyle.Force.");
            }

            if (resourceModule.UpdatableWhilePlaying)
            {
                throw new InvalidOperationException("Two-stage update v1 does not support updatable-while-playing mode.");
            }

#if !UNITY_EDITOR && !ENABLE_HYBRIDCLR
            throw new InvalidOperationException("Two-stage Host Player requires ENABLE_HYBRIDCLR.");
#endif

            if (setting.TwoStageContractVersion <= 0 || string.IsNullOrWhiteSpace(setting.BasePlayerId) ||
                string.IsNullOrWhiteSpace(setting.Channel) || string.IsNullOrWhiteSpace(setting.BootstrapAssemblyName) ||
                string.IsNullOrWhiteSpace(setting.BootstrapTextAssetPath) || string.IsNullOrWhiteSpace(setting.BootstrapTag))
            {
                throw new InvalidOperationException("Two-stage update has incomplete contract/base/channel/bootstrap configuration.");
            }

            if (!string.Equals(setting.BootstrapAssemblyName, "GameUpdater.dll", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Two-stage update v1 requires GameUpdater.dll as the bootstrap assembly.");
            }

            if (string.IsNullOrWhiteSpace(setting.LogicMainDllName) ||
                setting.HotUpdateAssemblies == null ||
                !setting.HotUpdateAssemblies.Contains(setting.LogicMainDllName) ||
                string.Equals(setting.LogicMainDllName, setting.BootstrapAssemblyName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Two-stage update requires a business LogicMainDllName in HotUpdateAssemblies.");
            }

            if (string.Equals(setting.AssemblyTextAssetPath, setting.BootstrapTextAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Business and bootstrap DLL asset paths must be different.");
            }

            if (resourceModule.PlayMode == EPlayMode.HostPlayMode)
            {
                if (setting.TwoStageReleaseSourceMode == TwoStageReleaseSourceMode.FixedEntry)
                {
                    TwoStageReleaseUrl.ValidateFixedEntryUrl(
                        setting.TwoStageReleaseDescriptorUrl,
                        setting.AllowInsecureLoopbackHttp);
                    TwoStageReleaseUrl.ValidateFixedResourceRootUrl(
                        setting.TwoStageHostServerUrl,
                        setting.AllowInsecureLoopbackHttp);
                    TwoStageReleaseUrl.ValidateFixedResourceRootUrl(
                        setting.TwoStageFallbackHostServerUrl,
                        setting.AllowInsecureLoopbackHttp);
                }
                else
                {
                    ValidateTrustedUrl(setting.TwoStageReleaseDescriptorUrl, setting);
                    ValidateTrustedUrl(setting.TwoStageHostServerUrl, setting);
                    ValidateTrustedUrl(setting.TwoStageFallbackHostServerUrl, setting);
                }
            }
        }

        private static void ValidateTrustedUrl(string value, UpdateSetting setting)
        {
            TwoStageReleaseUrl.ValidateTrustedUrl(
                value,
                setting != null && setting.AllowInsecureLoopbackHttp);
        }

        private static string NormalizeTrustedUrl(string value)
        {
            return TwoStageReleaseUrl.NormalizeLegacyRoot(value);
        }

        private static string CombineTrustedUrl(string root, string fileName)
        {
            string normalizedRoot = NormalizeTrustedUrl(root);
            ValidateTrustedUrl(normalizedRoot, _setting);
            return TwoStageReleaseUrl.CombineLegacyFileUrl(normalizedRoot, fileName);
        }

        private static UpdateArtifactDescriptor FindArtifact(UpdateArtifactDescriptor[] artifacts, string name)
        {
            if (artifacts == null)
            {
                return null;
            }

            foreach (UpdateArtifactDescriptor artifact in artifacts)
            {
                if (artifact != null && string.Equals(artifact.Name, name, StringComparison.Ordinal))
                {
                    return artifact;
                }
            }

            return null;
        }

        private static void EnsurePrepared()
        {
            if (!IsPrepared || _release == null || _setting == null)
            {
                throw new InvalidOperationException("Two-stage update session is not prepared or has shut down.");
            }
        }

        private static void OnBeforeShutdown()
        {
            _shutdown = true;
            try
            {
                _sessionCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _sessionCancellation?.Dispose();
                _sessionCancellation = null;
            }
        }

        /// <summary>
        /// 受限接收入口响应，超过上限时立即停止 UnityWebRequest 的数据接收。
        /// </summary>
        private sealed class LimitedDownloadHandler : DownloadHandlerScript
        {
            private readonly int _maxBytes;
            private readonly MemoryStream _buffer;

            public LimitedDownloadHandler(int maxBytes)
            {
                _maxBytes = maxBytes;
                _buffer = new MemoryStream(Math.Min(maxBytes, 4096));
            }

            public bool ExceededLimit { get; private set; }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength < 0 || dataLength > data.Length)
                {
                    ExceededLimit = true;
                    return false;
                }

                if (_buffer.Length > _maxBytes - dataLength)
                {
                    ExceededLimit = true;
                    return false;
                }

                if (dataLength > 0)
                {
                    _buffer.Write(data, 0, dataLength);
                }

                return true;
            }

            public byte[] GetReceivedBytes()
            {
                return _buffer.ToArray();
            }
        }

        [Serializable]
        private sealed class EntryCompletedCheckpoint
        {
            public int SchemaVersion;
            public string ReleaseId;
            public string BasePlayerId;
            public string Platform;
            public string Channel;
            public string PackageName;
            public string PackageVersion;
            public string ManifestSha256;
            public string PayloadSha256;

            public static EntryCompletedCheckpoint Create(UpdateReleaseDescriptor release)
            {
                EntryCompletedCheckpoint checkpoint = new EntryCompletedCheckpoint
                {
                    SchemaVersion = 1,
                    ReleaseId = release.ReleaseId,
                    BasePlayerId = release.BasePlayerId,
                    Platform = release.Platform,
                    Channel = release.Channel,
                    PackageName = release.PackageName,
                    PackageVersion = release.PackageVersion,
                    ManifestSha256 = release.ManifestSha256,
                };
                checkpoint.PayloadSha256 = UpdateReleaseDescriptorValidator.ComputeSha256(
                    Encoding.UTF8.GetBytes(checkpoint.GetCanonicalPayload()));
                return checkpoint;
            }

            public static bool TryValidate(
                string json,
                UpdateReleaseDescriptor release,
                out string error)
            {
                error = null;
                if (release == null)
                {
                    error = "Release is null.";
                    return false;
                }

                EntryCompletedCheckpoint checkpoint;
                try
                {
                    checkpoint = JsonUtility.FromJson<EntryCompletedCheckpoint>(json);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }

                if (checkpoint == null || checkpoint.SchemaVersion != 1 ||
                    !string.Equals(checkpoint.ReleaseId, release.ReleaseId, StringComparison.Ordinal) ||
                    !string.Equals(checkpoint.BasePlayerId, release.BasePlayerId, StringComparison.Ordinal) ||
                    !string.Equals(checkpoint.Platform, release.Platform, StringComparison.Ordinal) ||
                    !string.Equals(checkpoint.Channel, release.Channel, StringComparison.Ordinal) ||
                    !string.Equals(checkpoint.PackageName, release.PackageName, StringComparison.Ordinal) ||
                    !string.Equals(checkpoint.PackageVersion, release.PackageVersion, StringComparison.Ordinal) ||
                    !string.Equals(checkpoint.ManifestSha256, release.ManifestSha256, StringComparison.Ordinal))
                {
                    error = "Checkpoint identity does not match the fixed release.";
                    return false;
                }

                string actualPayloadSha256 = UpdateReleaseDescriptorValidator.ComputeSha256(
                    Encoding.UTF8.GetBytes(checkpoint.GetCanonicalPayload()));
                if (!string.Equals(actualPayloadSha256, checkpoint.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Checkpoint payload hash is invalid.";
                    return false;
                }

                return true;
            }

            private string GetCanonicalPayload()
            {
                return $"{SchemaVersion}|{ReleaseId}|{BasePlayerId}|{Platform}|{Channel}|{PackageName}|{PackageVersion}|{ManifestSha256}";
            }
        }
    }

    /// <summary>
    /// GameUpdater 入口的精确反射 ABI。
    /// </summary>
    internal static class UpdateStageEntryContract
    {
        public static bool TryFindRunAsync(Type entryType, out MethodInfo method, out string error)
        {
            method = null;
            error = null;
            if (entryType == null)
            {
                error = "GameUpdater.Entry type is missing.";
                return false;
            }

            MethodInfo[] candidates = entryType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            int named = 0;
            int matches = 0;
            foreach (MethodInfo candidate in candidates)
            {
                if (!string.Equals(candidate.Name, "RunAsync", StringComparison.Ordinal))
                {
                    continue;
                }

                named++;
                ParameterInfo[] parameters = candidate.GetParameters();
                if (!candidate.IsGenericMethod &&
                    candidate.ReturnType == typeof(UniTask<UpdateStageResult>) &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(UpdateSessionContext) &&
                    parameters[1].ParameterType == typeof(IUpdateHost))
                {
                    method = candidate;
                    matches++;
                }
            }

            if (named == 1 && matches == 1)
            {
                return true;
            }

            method = null;
            error = "GameUpdater.Entry must declare exactly one public static UniTask<UpdateStageResult> RunAsync(UpdateSessionContext, IUpdateHost).";
            return false;
        }
    }
}
