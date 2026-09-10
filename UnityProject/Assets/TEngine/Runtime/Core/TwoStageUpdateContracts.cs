using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 两阶段更新器与 AOT 宿主之间的稳定会话快照。
    /// </summary>
    public sealed class UpdateSessionContext
    {
        public UpdateSessionContext(
            string sessionId,
            int contractVersion,
            string basePlayerId,
            string platform,
            string channel,
            string packageName,
            string packageVersion,
            string releaseId,
            string manifestId,
            string manifestSha256,
            CancellationToken cancellationToken)
        {
            SessionId = sessionId;
            ContractVersion = contractVersion;
            BasePlayerId = basePlayerId;
            Platform = platform;
            Channel = channel;
            PackageName = packageName;
            PackageVersion = packageVersion;
            ReleaseId = releaseId;
            ManifestId = manifestId;
            ManifestSha256 = manifestSha256;
            CancellationToken = cancellationToken;
        }

        public string SessionId { get; }
        public int ContractVersion { get; }
        public string BasePlayerId { get; }
        public string Platform { get; }
        public string Channel { get; }
        public string PackageName { get; }
        public string PackageVersion { get; }
        public string ReleaseId { get; }
        public string ManifestId { get; }
        public string ManifestSha256 { get; }
        public CancellationToken CancellationToken { get; }
    }

    public enum UpdateStageStatus
    {
        ResourcesReady = 1,
        Failed = 2,
        Cancelled = 3,
    }

    /// <summary>
    /// GameUpdater 返回给主包的唯一阶段结果。
    /// </summary>
    public sealed class UpdateStageResult
    {
        public UpdateStageResult(UpdateStageStatus status, string sessionId, string releaseId, string error)
        {
            Status = status;
            SessionId = sessionId;
            ReleaseId = releaseId;
            Error = error ?? string.Empty;
        }

        public UpdateStageStatus Status { get; }
        public string SessionId { get; }
        public string ReleaseId { get; }
        public string Error { get; }

        public static UpdateStageResult Ready(UpdateSessionContext context)
        {
            return new UpdateStageResult(UpdateStageStatus.ResourcesReady, context.SessionId, context.ReleaseId, string.Empty);
        }

        public static UpdateStageResult Failed(UpdateSessionContext context, string error)
        {
            return new UpdateStageResult(UpdateStageStatus.Failed, context.SessionId, context.ReleaseId, error);
        }

        public static UpdateStageResult Cancelled(UpdateSessionContext context, string error)
        {
            return new UpdateStageResult(UpdateStageStatus.Cancelled, context.SessionId, context.ReleaseId, error);
        }
    }

    public enum UpdateDownloadStatus
    {
        Succeeded = 1,
        Failed = 2,
        Cancelled = 3,
    }

    public sealed class UpdateDownloadResult
    {
        public UpdateDownloadResult(UpdateDownloadStatus status, string error)
        {
            Status = status;
            Error = error ?? string.Empty;
        }

        public UpdateDownloadStatus Status { get; }
        public string Error { get; }
    }

    public enum UpdateRetryDecision
    {
        Retry = 1,
        Cancel = 2,
    }

    /// <summary>
    /// 热更更新器可见的窄宿主接口。资源模块和 Procedure 跳转均不越过该边界。
    /// </summary>
    public interface IUpdateHost
    {
        bool IsSessionCurrent(UpdateSessionContext context);
        void ReportStatus(UpdateSessionContext context, string message);
        UniTask<UpdateDownloadResult> DownloadRemainingResourcesAsync(UpdateSessionContext context);
        UniTask<UpdateRetryDecision> RequestRetryOrCancelAsync(UpdateSessionContext context, string error);
    }

    /// <summary>
    /// 由资源模块实现的窄接口：把 release 摘要绑定到 YooAsset 实际反序列化并激活的清单。
    /// </summary>
    public interface IPinnedManifestIntegrity
    {
        void ConfigurePinnedManifest(string packageName, string packageVersion, string manifestSha256);
        void VerifyPinnedManifestActivated(string packageName, string packageVersion, string manifestSha256);
    }

    /// <summary>
    /// release descriptor 中单个可装载程序集/metadata 资源的身份和内容摘要。
    /// 字段形式用于 Unity JsonUtility。
    /// </summary>
    [Serializable]
    public sealed class UpdateArtifactDescriptor
    {
        public string Name;
        public string Address;
        public string Sha256;
    }

    [Serializable]
    public sealed class UpdateReleaseDescriptor
    {
        public int ContractVersion;
        public string ReleaseId;
        public string BasePlayerId;
        public string Platform;
        public string Channel;
        public string PackageName;
        public string PackageVersion;
        public string ManifestId;
        public string ManifestSha256;
        public UpdateArtifactDescriptor BootstrapAssembly;
        public UpdateArtifactDescriptor[] BusinessAssemblies;
        public UpdateArtifactDescriptor[] MetadataAssemblies;
    }

    /// <summary>
    /// 主包期望值。远端 descriptor 只能声明与这些受信任配置一致的 release，不能改写宿主或本地路径。
    /// </summary>
    public sealed class UpdateReleaseValidationContext
    {
        public int ContractVersion;
        public string BasePlayerId;
        public string Platform;
        public string Channel;
        public string PackageName;
        public string ManifestId;
        public string BootstrapAssemblyName;
        public IReadOnlyList<string> HotUpdateAssemblies;
        public IReadOnlyList<string> MetadataAssemblies;
    }

    public static class UpdateReleaseDescriptorValidator
    {
        public static List<string> Validate(UpdateReleaseDescriptor descriptor, UpdateReleaseValidationContext expected)
        {
            List<string> errors = new List<string>();
            if (descriptor == null)
            {
                errors.Add("Release descriptor is null.");
                return errors;
            }

            if (expected == null)
            {
                errors.Add("Release validation context is null.");
                return errors;
            }

            RequireEqual(errors, "ContractVersion", expected.ContractVersion, descriptor.ContractVersion);
            RequireEqual(errors, "BasePlayerId", expected.BasePlayerId, descriptor.BasePlayerId);
            RequireEqual(errors, "Platform", expected.Platform, descriptor.Platform);
            RequireEqual(errors, "Channel", expected.Channel, descriptor.Channel);
            RequireEqual(errors, "PackageName", expected.PackageName, descriptor.PackageName);
            RequireEqual(errors, "ManifestId", expected.ManifestId, descriptor.ManifestId);

            ValidateSegment(errors, "ReleaseId", descriptor.ReleaseId);
            ValidateSegment(errors, "PackageVersion", descriptor.PackageVersion);
            ValidateSegment(errors, "ManifestId", descriptor.ManifestId);
            ValidateSha256(errors, "ManifestSha256", descriptor.ManifestSha256);

            HashSet<string> expectedHot = CreateExactNameSet(errors, expected.HotUpdateAssemblies, "HotUpdateAssemblies");
            HashSet<string> expectedMetadata = CreateExactNameSet(errors, expected.MetadataAssemblies, "MetadataAssemblies");
            if (string.IsNullOrWhiteSpace(expected.BootstrapAssemblyName))
            {
                errors.Add("BootstrapAssemblyName is empty.");
            }
            else if (!expectedHot.Contains(expected.BootstrapAssemblyName))
            {
                errors.Add($"Bootstrap assembly '{expected.BootstrapAssemblyName}' is not in HotUpdateAssemblies.");
            }

            HashSet<string> actualNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> actualAddresses = new HashSet<string>(StringComparer.Ordinal);
            ValidateArtifact(errors, descriptor.BootstrapAssembly, "BootstrapAssembly", actualNames, actualAddresses);
            if (descriptor.BootstrapAssembly != null &&
                !string.Equals(descriptor.BootstrapAssembly.Name, expected.BootstrapAssemblyName, StringComparison.Ordinal))
            {
                errors.Add($"Bootstrap assembly mismatch: expected '{expected.BootstrapAssemblyName}', actual '{descriptor.BootstrapAssembly.Name}'.");
            }

            ValidateArtifactArray(errors, descriptor.BusinessAssemblies, "BusinessAssemblies", actualNames, actualAddresses);
            ValidateArtifactArray(errors, descriptor.MetadataAssemblies, "MetadataAssemblies", actualNames, actualAddresses);

            HashSet<string> actualHot = new HashSet<string>(StringComparer.Ordinal);
            if (descriptor.BootstrapAssembly != null && !string.IsNullOrWhiteSpace(descriptor.BootstrapAssembly.Name))
            {
                actualHot.Add(descriptor.BootstrapAssembly.Name);
            }

            if (descriptor.BusinessAssemblies != null)
            {
                foreach (UpdateArtifactDescriptor artifact in descriptor.BusinessAssemblies)
                {
                    if (artifact != null && !string.IsNullOrWhiteSpace(artifact.Name))
                    {
                        actualHot.Add(artifact.Name);
                    }
                }
            }

            HashSet<string> actualMetadata = new HashSet<string>(StringComparer.Ordinal);
            if (descriptor.MetadataAssemblies != null)
            {
                foreach (UpdateArtifactDescriptor artifact in descriptor.MetadataAssemblies)
                {
                    if (artifact != null && !string.IsNullOrWhiteSpace(artifact.Name))
                    {
                        actualMetadata.Add(artifact.Name);
                    }
                }
            }

            RequireSameSet(errors, "hot-update assemblies", expectedHot, actualHot);
            RequireSameSet(errors, "metadata assemblies", expectedMetadata, actualMetadata);
            return errors;
        }

        public static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static bool HashMatches(byte[] bytes, string expectedSha256)
        {
            return IsSha256(expectedSha256) &&
                   string.Equals(ComputeSha256(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateArtifactArray(
            List<string> errors,
            UpdateArtifactDescriptor[] artifacts,
            string field,
            HashSet<string> names,
            HashSet<string> addresses)
        {
            if (artifacts == null || artifacts.Length == 0)
            {
                errors.Add($"{field} is empty.");
                return;
            }

            for (int i = 0; i < artifacts.Length; i++)
            {
                ValidateArtifact(errors, artifacts[i], $"{field}[{i}]", names, addresses);
            }
        }

        private static void ValidateArtifact(
            List<string> errors,
            UpdateArtifactDescriptor artifact,
            string field,
            HashSet<string> names,
            HashSet<string> addresses)
        {
            if (artifact == null)
            {
                errors.Add($"{field} is null.");
                return;
            }

            ValidateAssemblyName(errors, $"{field}.Name", artifact.Name);
            ValidateAddress(errors, $"{field}.Address", artifact.Address);
            ValidateSha256(errors, $"{field}.Sha256", artifact.Sha256);
            if (!string.IsNullOrWhiteSpace(artifact.Name) && !names.Add(artifact.Name))
            {
                errors.Add($"Duplicate artifact name '{artifact.Name}'.");
            }

            if (!string.IsNullOrWhiteSpace(artifact.Address) && !addresses.Add(artifact.Address))
            {
                errors.Add($"Duplicate artifact address '{artifact.Address}'.");
            }
        }

        private static HashSet<string> CreateExactNameSet(List<string> errors, IReadOnlyList<string> values, string field)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (values == null || values.Count == 0)
            {
                errors.Add($"{field} is empty.");
                return result;
            }

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                ValidateAssemblyName(errors, $"{field}[{i}]", value);
                if (!string.IsNullOrWhiteSpace(value) && !result.Add(value))
                {
                    errors.Add($"{field} contains duplicate '{value}'.");
                }
            }

            return result;
        }

        private static void RequireSameSet(List<string> errors, string field, HashSet<string> expected, HashSet<string> actual)
        {
            foreach (string value in expected)
            {
                if (!actual.Contains(value))
                {
                    errors.Add($"Release is missing required {field} entry '{value}'.");
                }
            }

            foreach (string value in actual)
            {
                if (!expected.Contains(value))
                {
                    errors.Add($"Release contains unexpected {field} entry '{value}'.");
                }
            }
        }

        private static void RequireEqual(List<string> errors, string field, int expected, int actual)
        {
            if (expected != actual)
            {
                errors.Add($"{field} mismatch: expected '{expected}', actual '{actual}'.");
            }
        }

        private static void RequireEqual(List<string> errors, string field, string expected, string actual)
        {
            if (string.IsNullOrWhiteSpace(expected) ||
                !string.Equals(expected, actual, StringComparison.Ordinal))
            {
                errors.Add($"{field} mismatch: expected '{expected ?? "<null>"}', actual '{actual ?? "<null>"}'.");
            }
        }

        private static void ValidateAssemblyName(List<string> errors, string field, string value)
        {
            ValidateSegment(errors, field, value);
            if (!string.IsNullOrWhiteSpace(value) && !value.EndsWith(".dll", StringComparison.Ordinal))
            {
                errors.Add($"{field} must end with '.dll': '{value}'.");
            }
        }

        private static void ValidateAddress(List<string> errors, string field, string value)
        {
            ValidateSegment(errors, field, value);
            if (!string.IsNullOrWhiteSpace(value) && value.IndexOf(':') >= 0)
            {
                errors.Add($"{field} must be a resource address, not a URI: '{value}'.");
            }
        }

        private static void ValidateSegment(List<string> errors, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{field} is empty.");
                return;
            }

            if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.Contains(".."))
            {
                errors.Add($"{field} contains an invalid path segment: '{value}'.");
            }
        }

        private static void ValidateSha256(List<string> errors, string field, string value)
        {
            if (!IsSha256(value))
            {
                errors.Add($"{field} must be a 64-character SHA-256 hex digest.");
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F';
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
