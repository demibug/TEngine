using System;

namespace TEngine
{
    /// <summary>
    /// 两阶段固定入口及资源目录的 URL 协议辅助类。
    /// </summary>
    public static class TwoStageReleaseUrl
    {
        public const string EntryFileName = "current.json";
        public const string ReleaseDirectoryName = "releases";

        private const string DescriptorFilePrefix = "TwoStageRelease_";
        private const string DescriptorFileSuffix = ".json";
        private const string EntryPathSuffix = "/" + EntryFileName;

        /// <summary>
        /// 校验两阶段下载使用的可信 URL 协议。
        /// </summary>
        public static bool TryValidateTrustedUrl(
            string value,
            bool allowInsecureLoopbackHttp,
            out string error)
        {
            error = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
            {
                error = $"URL 无效：'{value ?? "<null>"}'。";
                return false;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                uri.IsLoopback && allowInsecureLoopbackHttp)
            {
                return true;
            }

            error =
                $"URL 必须使用 HTTPS；仅显式允许的 loopback HTTP 可用于开发：'{value ?? "<null>"}'。";
            return false;
        }

        /// <summary>
        /// 校验固定入口 URL，不允许用户信息、查询参数、fragment，且路径必须以 /current.json 结尾。
        /// </summary>
        public static bool TryValidateFixedEntryUrl(
            string value,
            bool allowInsecureLoopbackHttp,
            out string error)
        {
            if (!TryCreateCleanUrl(value, allowInsecureLoopbackHttp, out Uri uri, out error))
            {
                return false;
            }

            string path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            if (!string.Equals(path, EntryFileName, StringComparison.Ordinal) &&
                !path.EndsWith(EntryPathSuffix, StringComparison.Ordinal))
            {
                error = $"固定入口 URL 路径必须以 '/{EntryFileName}' 结尾：'{value}'。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 校验固定资源根 URL，不允许用户信息、查询参数或 fragment。
        /// </summary>
        public static bool TryValidateFixedResourceRootUrl(
            string value,
            bool allowInsecureLoopbackHttp,
            out string error)
        {
            return TryCreateCleanUrl(value, allowInsecureLoopbackHttp, out _, out error);
        }

        /// <summary>
        /// 规范化固定资源根，并去掉末尾斜杠。
        /// </summary>
        public static string NormalizeFixedResourceRoot(
            string value,
            bool allowInsecureLoopbackHttp)
        {
            ValidateFixedResourceRootUrl(value, allowInsecureLoopbackHttp);
            Uri uri = new Uri(value, UriKind.Absolute);
            string authority = GetOriginalAuthority(value);
            string path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');
            return string.IsNullOrEmpty(path) ? authority : authority + "/" + path;
        }

        /// <summary>
        /// 根据固定入口原始配置地址构造不可变 descriptor 地址。
        /// </summary>
        public static string BuildDescriptorUrl(
            string fixedEntryUrl,
            string releaseId,
            bool allowInsecureLoopbackHttp)
        {
            ValidateFixedEntryUrl(fixedEntryUrl, allowInsecureLoopbackHttp);
            EnsureValidReleaseId(releaseId);

            Uri entryUri = new Uri(fixedEntryUrl, UriKind.Absolute);
            string escapedPath = entryUri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            string basePath = escapedPath.Substring(0, escapedPath.Length - EntryFileName.Length).TrimEnd('/');
            string authority = GetOriginalAuthority(fixedEntryUrl);
            string escapedReleaseId = Uri.EscapeDataString(releaseId);
            string descriptorName = DescriptorFilePrefix + escapedReleaseId + DescriptorFileSuffix;
            string identityPath = string.IsNullOrEmpty(basePath) ? string.Empty : "/" + basePath;
            return authority + identityPath + "/" + ReleaseDirectoryName + "/" + escapedReleaseId + "/" + descriptorName;
        }

        /// <summary>
        /// 根据固定资源根构造当前 release 的资源目录。
        /// </summary>
        public static string BuildReleaseRootUrl(
            string fixedResourceRootUrl,
            string releaseId,
            bool allowInsecureLoopbackHttp)
        {
            string root = NormalizeFixedResourceRoot(fixedResourceRootUrl, allowInsecureLoopbackHttp);
            EnsureValidReleaseId(releaseId);
            return root + "/" + ReleaseDirectoryName + "/" + Uri.EscapeDataString(releaseId);
        }

        /// <summary>
        /// 在已规范化资源根后追加一个资源文件名。
        /// </summary>
        public static string CombineFileUrl(string normalizedRoot, string fileName)
        {
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                throw new ArgumentException("资源 URL 根目录为空。", nameof(normalizedRoot));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("资源文件名为空。", nameof(fileName));
            }

            return normalizedRoot.TrimEnd('/') + "/" + Uri.EscapeDataString(fileName);
        }

        /// <summary>
        /// 为固定入口请求追加每次不同的缓存击穿参数，不改变原始配置 URL。
        /// </summary>
        public static string AppendCacheBust(string fixedEntryUrl, string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce))
            {
                throw new ArgumentException("缓存校验随机值为空。", nameof(nonce));
            }

            if (!Uri.TryCreate(fixedEntryUrl, UriKind.Absolute, out Uri uri))
            {
                throw new ArgumentException($"固定入口 URL 无效：'{fixedEntryUrl}'。", nameof(fixedEntryUrl));
            }

            return fixedEntryUrl.Trim() + "?ts=" + Uri.EscapeDataString(nonce);
        }

        public static void ValidateTrustedUrl(string value, bool allowInsecureLoopbackHttp)
        {
            if (!TryValidateTrustedUrl(value, allowInsecureLoopbackHttp, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        public static void ValidateFixedEntryUrl(string value, bool allowInsecureLoopbackHttp)
        {
            if (!TryValidateFixedEntryUrl(value, allowInsecureLoopbackHttp, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        public static void ValidateFixedResourceRootUrl(string value, bool allowInsecureLoopbackHttp)
        {
            if (!TryValidateFixedResourceRootUrl(value, allowInsecureLoopbackHttp, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        private static bool TryCreateCleanUrl(
            string value,
            bool allowInsecureLoopbackHttp,
            out Uri uri,
            out string error)
        {
            uri = null;
            if (!TryValidateTrustedUrl(value, allowInsecureLoopbackHttp, out error) ||
                !Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                error = $"固定入口/资源 URL 不允许用户信息：'{value}'。";
                return false;
            }

            if (!string.IsNullOrEmpty(uri.Query))
            {
                error = $"固定入口/资源 URL 不允许查询参数：'{value}'。";
                return false;
            }

            if (!string.IsNullOrEmpty(uri.Fragment))
            {
                error = $"固定入口/资源 URL 不允许 fragment：'{value}'。";
                return false;
            }

            return true;
        }

        private static void EnsureValidReleaseId(string releaseId)
        {
            if (!UpdateReleaseEntryValidator.IsValidReleaseId(releaseId))
            {
                throw new ArgumentException(
                    "固定入口 ReleaseId 必须为 1～128 个 ASCII 字母、数字、短横线或下划线。",
                    nameof(releaseId));
            }
        }

        private static string GetOriginalAuthority(string value)
        {
            string trimmed = value.Trim();
            int schemeSeparator = trimmed.IndexOf("://", StringComparison.Ordinal);
            int pathStart = trimmed.IndexOf('/', schemeSeparator + 3);
            return pathStart < 0 ? trimmed : trimmed.Substring(0, pathStart);
        }
    }
}
