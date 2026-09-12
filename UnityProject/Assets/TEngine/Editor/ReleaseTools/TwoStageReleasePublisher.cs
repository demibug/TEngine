using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset.Editor;

[assembly: InternalsVisibleTo("TEngine.BuildPipeline.Tests")]

namespace TEngine
{
    /// <summary>
    /// 仅供发布事务测试注入确定性的故障点，正式发布不传入该对象。
    /// </summary>
    internal sealed class TwoStageReleasePublisherTestHooks
    {
        public Action<string> AfterFileCopied;
        public Action BeforeEntryCommit;
        public Action<string, string> CommitCurrentEntry;
        public Action AfterEntryCommit;
    }

    public enum TwoStageReleasePublishStatus
    {
        NotRun,
        Succeeded,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// 本地固定入口发布结果。
    /// </summary>
    public sealed class TwoStageReleasePublishResult
    {
        public TwoStageReleasePublishStatus Status { get; internal set; }
        public string PublishRoot { get; internal set; }
        public string PublishDirectory { get; internal set; }
        public string ReleaseDirectory { get; internal set; }
        public string EntryPath { get; internal set; }
        public string Error { get; internal set; }
        public Exception Exception { get; internal set; }
        public bool ReusedExistingRelease { get; internal set; }

        public bool Succeeded => Status == TwoStageReleasePublishStatus.Succeeded;
        public bool Failed => Status == TwoStageReleasePublishStatus.Failed;
        public bool Cancelled => Status == TwoStageReleasePublishStatus.Cancelled;

        internal static TwoStageReleasePublishResult CreateNotRun(string publishRoot, string error)
        {
            return new TwoStageReleasePublishResult
            {
                Status = TwoStageReleasePublishStatus.NotRun,
                PublishRoot = publishRoot,
                Error = error,
            };
        }

        internal static TwoStageReleasePublishResult CreateCancelled(string publishRoot, string error)
        {
            return new TwoStageReleasePublishResult
            {
                Status = TwoStageReleasePublishStatus.Cancelled,
                PublishRoot = publishRoot,
                Error = error,
            };
        }

        internal static TwoStageReleasePublishResult CreateFailed(
            string publishRoot,
            string error,
            Exception exception)
        {
            return new TwoStageReleasePublishResult
            {
                Status = TwoStageReleasePublishStatus.Failed,
                PublishRoot = publishRoot,
                Error = error,
                Exception = exception,
            };
        }
    }

    /// <summary>
    /// 把成功构建的 OutputPackageDirectory 发布为固定入口目录。
    /// </summary>
    public static class TwoStageReleasePublisher
    {
        private const int FileBufferSize = 64 * 1024;
        private const int DescriptorMaxBytes = 1024 * 1024;
        private const string LockFileName = ".two-stage-publish.lock";
        private const string TemporaryReleasePrefix = ".release-tmp-";
        private const string TemporaryEntryPrefix = ".current-tmp-";

        /// <summary>
        /// 发布器自身的无副作用参数预检。
        /// </summary>
        public static List<string> ValidateRequest(TwoStageBuildSnapshot snapshot)
        {
            List<string> errors = new List<string>();
            if (snapshot == null)
            {
                errors.Add("两阶段发布快照为空。");
                return errors;
            }

            if (!snapshot.TwoStageEnabled)
                errors.Add("本地固定入口发布要求启用两阶段更新。");

            if (snapshot.Config == null)
            {
                errors.Add("构建配置为空。");
                return errors;
            }

            if (!UpdateReleaseEntryValidator.IsValidReleaseId(snapshot.Config.ReleaseId))
                errors.Add("固定入口 ReleaseId 必须是 1～128 个 ASCII 字母、数字、短横线或下划线。");
            if (!IsSafePathSegment(snapshot.BasePlayerId))
                errors.Add("固定入口 BasePlayerId 必须是安全的单一目录段。");
            if (!IsSafePathSegment(snapshot.Channel))
                errors.Add("固定入口 Channel 必须是安全的单一目录段。");
            if (!IsSafePathSegment(snapshot.Platform))
                errors.Add("构建目标平台必须是安全的单一目录段。");
            if (!IsSafePathSegment(snapshot.PackageName))
                errors.Add("资源包名必须是安全的单一目录段。");
            if (string.IsNullOrWhiteSpace(snapshot.PublishRoot))
                errors.Add("本地发布根目录为空。");
            if (snapshot.ContractVersion <= 0)
                errors.Add("两阶段契约版本必须大于 0。");

            if (!TwoStageReleaseUrl.TryValidateFixedEntryUrl(
                    snapshot.FixedEntryUrl,
                    snapshot.AllowInsecureLoopbackHttp,
                    out string entryError))
            {
                errors.Add($"固定入口 URL 无效：{entryError}");
            }

            if (!TwoStageReleaseUrl.TryValidateFixedResourceRootUrl(
                    snapshot.PrimaryHostUrl,
                    snapshot.AllowInsecureLoopbackHttp,
                    out string primaryError))
            {
                errors.Add($"primary host URL 无效：{primaryError}");
            }

            if (!TwoStageReleaseUrl.TryValidateFixedResourceRootUrl(
                    snapshot.FallbackHostUrl,
                    snapshot.AllowInsecureLoopbackHttp,
                    out string fallbackError))
            {
                errors.Add($"fallback host URL 无效：{fallbackError}");
            }

            if (errors.Count == 0)
            {
                try
                {
                    string publishRoot = ResolvePublishRoot(snapshot.PublishRoot);
                    ValidateNoReparsePointsInPath(publishRoot);
                    if (File.Exists(publishRoot))
                        errors.Add("本地发布根目录当前是文件而不是目录。");

                    string publishDirectory = GetIdentityDirectory(snapshot, publishRoot);
                    if (!IsSameOrDescendant(publishRoot, publishDirectory))
                        errors.Add("本地发布身份目录越出了发布根目录。");
                    else if (File.Exists(publishDirectory))
                        errors.Add("本地发布身份目录当前是文件而不是目录。");

                    if (errors.Count == 0 && !string.IsNullOrWhiteSpace(snapshot.Config.OutputRoot))
                    {
                        string expectedSourceDirectory = ResolveExpectedSourceDirectory(snapshot);
                        string expectedReleaseDirectory = Path.Combine(
                            publishDirectory,
                            TwoStageReleaseUrl.ReleaseDirectoryName,
                            snapshot.Config.ReleaseId);
                        ValidateSourceAndDestinationPaths(
                            expectedSourceDirectory,
                            publishRoot,
                            publishDirectory,
                            expectedReleaseDirectory);
                    }

                    ValidateNoReparsePointsInPath(Path.Combine(publishDirectory, LockFileName));
                }
                catch (Exception exception)
                {
                    errors.Add($"本地发布根目录无效：{exception.Message}");
                }
            }

            return errors;
        }

        public static string GetPublishDirectory(TwoStageBuildSnapshot snapshot)
        {
            EnsureValidSnapshot(snapshot);
            return GetIdentityDirectory(snapshot);
        }

        public static string GetEntryPath(TwoStageBuildSnapshot snapshot)
        {
            string directory = GetPublishDirectory(snapshot);
            return Path.Combine(directory, TwoStageReleaseUrl.EntryFileName);
        }

        /// <summary>
        /// 发布事务：预检 → 异步复制/校验 → 原子改名 → 原子提交 current.json。
        /// </summary>
        public static UniTask<TwoStageReleasePublishResult> PublishAsync(
            TwoStageBuildSnapshot snapshot,
            BuildExecutionResult buildResult,
            CancellationToken cancellationToken)
        {
            return PublishAsync(snapshot, buildResult, cancellationToken, null);
        }

        internal static async UniTask<TwoStageReleasePublishResult> PublishAsync(
            TwoStageBuildSnapshot snapshot,
            BuildExecutionResult buildResult,
            CancellationToken cancellationToken,
            TwoStageReleasePublisherTestHooks testHooks)
        {
            TwoStageReleasePublishResult result = TwoStageReleasePublishResult.CreateFailed(null, null, null);
            string temporaryRelease = null;
            string temporaryEntry = null;
            bool temporaryReleaseCreated = false;
            bool releasePromoted = false;

            try
            {
                List<string> requestErrors = ValidateRequest(snapshot);
                if (requestErrors.Count > 0)
                {
                    result = TwoStageReleasePublishResult.CreateFailed(
                        snapshot?.PublishRoot,
                        string.Join("\n", requestErrors),
                        null);
                    return result;
                }

                if (buildResult == null || !buildResult.Succeeded)
                {
                    result = TwoStageReleasePublishResult.CreateNotRun(
                        snapshot.PublishRoot,
                        "构建未成功，发布器未执行。");
                    return result;
                }

                BuildConfig snapshotConfig = snapshot.Config;
                if (buildResult.Target != snapshotConfig.BuildTarget)
                    throw new InvalidOperationException(
                        $"构建目标与发布快照不一致：快照为 {snapshotConfig.BuildTarget}，结果为 {buildResult.Target}。");
                if (!string.Equals(buildResult.PackageVersion, snapshotConfig.PackageVersion, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"资源版本与发布快照不一致：快照为 '{snapshotConfig.PackageVersion}'，结果为 '{buildResult.PackageVersion ?? "<null>"}'。");

                string sourceDirectory = ResolveFullPath(buildResult.OutputPackageDirectory);
                string publishRoot = ResolvePublishRoot(snapshot.PublishRoot);
                string publishDirectory = GetIdentityDirectory(snapshot, publishRoot);
                string releaseDirectory = Path.Combine(
                    publishDirectory,
                    "releases",
                    snapshotConfig.ReleaseId);
                string entryPath = Path.Combine(publishDirectory, TwoStageReleaseUrl.EntryFileName);

                result.PublishRoot = publishRoot;
                result.PublishDirectory = publishDirectory;
                result.ReleaseDirectory = releaseDirectory;
                result.EntryPath = entryPath;

                ValidateSourceAndDestinationPaths(sourceDirectory, publishRoot, publishDirectory, releaseDirectory);
                if (cancellationToken.IsCancellationRequested)
                {
                    result = TwoStageReleasePublishResult.CreateCancelled(
                        publishRoot,
                        "发布预检完成前已取消；未切换 current.json。");
                    result.PublishDirectory = publishDirectory;
                    result.ReleaseDirectory = releaseDirectory;
                    result.EntryPath = entryPath;
                    return result;
                }

                if (!Directory.Exists(sourceDirectory))
                    throw new DirectoryNotFoundException($"构建输出目录不存在：{sourceDirectory}");

                // 在读取 descriptor 前先扫描整个源目录，避免先读取重解析点指向的文件。
                ValidateNoReparsePointsRecursively(sourceDirectory);
                UpdateReleaseDescriptor descriptor = await ReadAndValidateDescriptorAsync(
                    snapshot,
                    sourceDirectory,
                    cancellationToken);
                List<FileFingerprint> sourceFiles = await CreateFileManifestAsync(
                    sourceDirectory,
                    cancellationToken);

                Directory.CreateDirectory(publishDirectory);
                ValidateNoReparsePointsInPath(publishDirectory);
                if (Directory.Exists(entryPath))
                    throw new IOException($"固定入口路径是目录，拒绝覆盖：{entryPath}");
                if (File.Exists(entryPath) && IsReparsePoint(entryPath))
                    throw new IOException($"固定入口路径是重解析点，拒绝覆盖：{entryPath}");

                using (FileStream publishLock = AcquirePublishLock(publishDirectory))
                {
                    // 锁建立后再次检查目标，防止锁文件创建前目录状态发生变化。
                    ValidateSourceAndDestinationPaths(sourceDirectory, publishRoot, publishDirectory, releaseDirectory);
                    ValidateNoReparsePointsInPath(publishDirectory);
                    ValidateNoReparsePointsInPath(Path.Combine(publishDirectory, LockFileName));

                    if (File.Exists(releaseDirectory))
                        throw new IOException($"发布目标 release 路径不是目录：{releaseDirectory}");

                    if (Directory.Exists(releaseDirectory))
                    {
                        ValidateNoReparsePointsRecursively(releaseDirectory);
                        List<FileFingerprint> existingFiles = await CreateFileManifestAsync(
                            releaseDirectory,
                            cancellationToken);
                        string difference = CompareManifests(sourceFiles, existingFiles);
                        if (!string.IsNullOrEmpty(difference))
                            throw new IOException(
                                $"目标 release 已存在但文件集或内容不同，拒绝覆盖：{releaseDirectory}\n{difference}");

                        result.ReusedExistingRelease = true;
                    }
                    else
                    {
                        string releasesDirectory = Path.Combine(publishDirectory, "releases");
                        Directory.CreateDirectory(releasesDirectory);
                        ValidateNoReparsePointsInPath(releasesDirectory);

                        temporaryRelease = CreateUniqueDirectoryPath(
                            publishDirectory,
                            TemporaryReleasePrefix + snapshotConfig.ReleaseId + "-");
                        Directory.CreateDirectory(temporaryRelease);
                        temporaryReleaseCreated = true;
                        ValidateNoReparsePointsInPath(temporaryRelease);

                        await CopyFilesAsync(sourceFiles, temporaryRelease, cancellationToken, testHooks);
                        List<FileFingerprint> copiedFiles = await CreateFileManifestAsync(
                            temporaryRelease,
                            cancellationToken);
                        string difference = CompareManifests(sourceFiles, copiedFiles);
                        if (!string.IsNullOrEmpty(difference))
                            throw new IOException($"发布复制校验失败：{difference}");

                        cancellationToken.ThrowIfCancellationRequested();
                        Directory.Move(temporaryRelease, releaseDirectory);
                        releasePromoted = true;
                        temporaryReleaseCreated = false;
                    }

                    byte[] entryBytes = BuildEntryBytes(snapshotConfig.ReleaseId);
                    temporaryEntry = CreateUniqueFilePath(
                        publishDirectory,
                        TemporaryEntryPrefix,
                        ".json");
                    await WriteNewFileAsync(temporaryEntry, entryBytes, cancellationToken);

                    // 入口提交是发布事务的唯一上线动作；之后不再观察取消状态。
                    testHooks?.BeforeEntryCommit?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (testHooks?.CommitCurrentEntry != null)
                        testHooks.CommitCurrentEntry(temporaryEntry, entryPath);
                    else
                        CommitCurrentEntry(temporaryEntry, entryPath);
                    temporaryEntry = null;
                    testHooks?.AfterEntryCommit?.Invoke();

                    result.Status = TwoStageReleasePublishStatus.Succeeded;
                    result.Error = null;
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                result = TwoStageReleasePublishResult.CreateCancelled(
                    result.PublishRoot ?? snapshot?.PublishRoot,
                    "入口提交前已取消；未切换 current.json。");
                result.PublishDirectory = result.PublishDirectory ?? GetOptionalPublishDirectory(snapshot);
                result.ReleaseDirectory = result.ReleaseDirectory ?? GetOptionalReleaseDirectory(snapshot);
                result.EntryPath = result.EntryPath ?? GetOptionalEntryPath(snapshot);
                return result;
            }
            catch (Exception exception)
            {
                result = TwoStageReleasePublishResult.CreateFailed(
                    result.PublishRoot ?? snapshot?.PublishRoot,
                    exception.Message,
                    exception);
                result.PublishDirectory = result.PublishDirectory ?? GetOptionalPublishDirectory(snapshot);
                result.ReleaseDirectory = result.ReleaseDirectory ?? GetOptionalReleaseDirectory(snapshot);
                result.EntryPath = result.EntryPath ?? GetOptionalEntryPath(snapshot);
                return result;
            }
            finally
            {
                if (temporaryEntry != null)
                    TryDeleteFile(temporaryEntry);
                if (temporaryReleaseCreated && !releasePromoted && temporaryRelease != null)
                    TryDeleteDirectory(temporaryRelease);
            }
        }

        private static async UniTask<UpdateReleaseDescriptor> ReadAndValidateDescriptorAsync(
            TwoStageBuildSnapshot snapshot,
            string sourceDirectory,
            CancellationToken cancellationToken)
        {
            string descriptorPath = GetContainedPath(sourceDirectory,
                $"TwoStageRelease_{snapshot.Config.ReleaseId}.json");
            if (!File.Exists(descriptorPath))
                throw new FileNotFoundException($"构建输出缺少 release descriptor：{descriptorPath}", descriptorPath);

            byte[] descriptorBytes = await ReadFileBytesAsync(
                descriptorPath,
                DescriptorMaxBytes,
                cancellationToken);
            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(descriptorBytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("release descriptor 不是有效的 UTF-8 文本。", exception);
            }

            UpdateReleaseDescriptor descriptor;
            try
            {
                descriptor = JsonUtility.FromJson<UpdateReleaseDescriptor>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("release descriptor JSON 无效。", exception);
            }

            if (descriptor == null)
                throw new InvalidDataException("release descriptor JSON 为 null。");
            if (!string.Equals(descriptor.ReleaseId, snapshot.Config.ReleaseId, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"release descriptor ReleaseId 不匹配：期望 '{snapshot.Config.ReleaseId}'，实际 '{descriptor.ReleaseId ?? "<null>"}'。");
            if (!UpdateReleaseEntryValidator.IsValidReleaseId(descriptor.ReleaseId))
                throw new InvalidDataException("固定入口 descriptor 的 ReleaseId 不符合目录协议。");
            if (!string.Equals(descriptor.PackageVersion, snapshot.Config.PackageVersion, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"release descriptor PackageVersion 不匹配：期望 '{snapshot.Config.PackageVersion}'，实际 '{descriptor.PackageVersion ?? "<null>"}'。");

            List<string> validationErrors = UpdateReleaseDescriptorValidator.Validate(
                descriptor,
                snapshot.CreateValidationContext());
            if (validationErrors.Count > 0)
                throw new InvalidDataException(
                    "release descriptor 身份校验失败：\n" + string.Join("\n", validationErrors));

            string manifestPath = GetContainedPath(sourceDirectory, descriptor.ManifestId);
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException($"构建输出缺少 descriptor 指定的清单：{manifestPath}", manifestPath);

            string manifestSha256 = await ComputeFileSha256Async(manifestPath, cancellationToken);
            if (!string.Equals(manifestSha256, descriptor.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"清单 SHA-256 与 descriptor 不一致：{manifestPath}。");

            return descriptor;
        }

        private static byte[] BuildEntryBytes(string releaseId)
        {
            UpdateReleaseEntry entry = new UpdateReleaseEntry
            {
                EntryVersion = UpdateReleaseEntryValidator.SupportedEntryVersion,
                ReleaseId = releaseId,
            };
            string json = JsonUtility.ToJson(entry, true);
            if (!UpdateReleaseEntryValidator.TryParse(json, out _, out string error))
                throw new InvalidDataException($"生成的 current.json 未通过自校验：{error}");
            return new UTF8Encoding(false).GetBytes(json);
        }

        private static FileStream AcquirePublishLock(string publishDirectory)
        {
            string lockPath = Path.Combine(publishDirectory, LockFileName);
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException exception)
            {
                throw new IOException($"同一身份目录已有发布任务，拒绝并发发布：{lockPath}", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException($"无法取得发布锁：{lockPath}", exception);
            }
        }

        private static void CommitCurrentEntry(string temporaryEntry, string entryPath)
        {
            try
            {
                if (File.Exists(entryPath))
                {
                    // 不支持原子替换时直接失败，绝不能退化为删除旧入口后写入。
                    File.Replace(temporaryEntry, entryPath, null);
                }
                else
                {
                    // 同目录移动在同一文件系统内是原子提交；目标若在竞争中出现则让移动失败。
                    File.Move(temporaryEntry, entryPath);
                }
            }
            catch (Exception exception)
            {
                throw new IOException(
                    $"current.json 原子提交失败，旧入口保持不变：{entryPath}",
                    exception);
            }
        }

        private static async UniTask CopyFilesAsync(
            IReadOnlyList<FileFingerprint> files,
            string destinationRoot,
            CancellationToken cancellationToken,
            TwoStageReleasePublisherTestHooks testHooks)
        {
            foreach (FileFingerprint source in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = GetContainedPath(destinationRoot, source.RelativePath);
                string parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException($"临时发布目录出现重复目标文件：{destination}");
                await CopyFileAsync(source.FullPath, destination, cancellationToken);
                testHooks?.AfterFileCopied?.Invoke(source.RelativePath);
            }
        }

        private static async UniTask CopyFileAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            using (FileStream source = new FileStream(
                       sourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       FileBufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (FileStream destination = new FileStream(
                       destinationPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       FileBufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[FileBufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, read, cancellationToken);
                }

                await destination.FlushAsync(cancellationToken);
            }
        }

        private static async UniTask WriteNewFileAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       FileBufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }

        private static async UniTask<byte[]> ReadFileBytesAsync(
            string path,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length > maxBytes)
                throw new InvalidDataException($"文件超过 {maxBytes} 字节上限：{path}");

            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       FileBufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (MemoryStream buffer = new MemoryStream((int)Math.Min(info.Length, maxBytes)))
            {
                byte[] chunk = new byte[Math.Min(FileBufferSize, Math.Max(1, maxBytes))];
                int read;
                while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken)) > 0)
                {
                    if (buffer.Length > maxBytes - read)
                        throw new InvalidDataException($"文件超过 {maxBytes} 字节上限：{path}");
                    buffer.Write(chunk, 0, read);
                }

                return buffer.ToArray();
            }
        }

        private static async UniTask<List<FileFingerprint>> CreateFileManifestAsync(
            string root,
            CancellationToken cancellationToken)
        {
            ValidateNoReparsePointsInPath(root);
            List<string> paths = EnumerateFilesSafely(root);
            paths.Sort(StringComparer.Ordinal);
            List<FileFingerprint> files = new List<FileFingerprint>(paths.Count);
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info = new FileInfo(path);
                long lengthBefore = info.Length;
                string sha256 = await ComputeFileSha256Async(path, cancellationToken);
                long lengthAfter = new FileInfo(path).Length;
                if (lengthBefore != lengthAfter)
                    throw new IOException($"源文件在校验期间发生变化：{path}");

                files.Add(new FileFingerprint
                {
                    FullPath = path,
                    RelativePath = NormalizeRelativePath(Path.GetRelativePath(root, path)),
                    Length = lengthAfter,
                    Sha256 = sha256,
                });
            }

            return files;
        }

        private static async UniTask<string> ComputeFileSha256Async(
            string path,
            CancellationToken cancellationToken)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       FileBufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[FileBufferSize];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    sha256.TransformBlock(buffer, 0, read, buffer, 0);
                }

                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static List<string> EnumerateFilesSafely(string root)
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"目录不存在：{root}");

            List<string> files = new List<string>();
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                DirectoryInfo directoryInfo = new DirectoryInfo(directory);
                EnsureNotReparsePoint(directoryInfo.FullName, directoryInfo.Attributes);

                foreach (FileInfo file in directoryInfo.GetFiles())
                {
                    EnsureNotReparsePoint(file.FullName, file.Attributes);
                    files.Add(file.FullName);
                }

                foreach (DirectoryInfo child in directoryInfo.GetDirectories())
                {
                    EnsureNotReparsePoint(child.FullName, child.Attributes);
                    pending.Push(child.FullName);
                }
            }

            return files;
        }

        private static string CompareManifests(
            IReadOnlyList<FileFingerprint> expected,
            IReadOnlyList<FileFingerprint> actual)
        {
            if (expected.Count != actual.Count)
                return $"文件数量不同：源 {expected.Count}，目标 {actual.Count}。";

            Dictionary<string, FileFingerprint> actualByPath = new Dictionary<string, FileFingerprint>(StringComparer.Ordinal);
            foreach (FileFingerprint file in actual)
            {
                if (!actualByPath.TryAdd(file.RelativePath, file))
                    return $"目标存在重复相对路径：{file.RelativePath}。";
            }

            foreach (FileFingerprint file in expected)
            {
                if (!actualByPath.TryGetValue(file.RelativePath, out FileFingerprint other))
                    return $"目标缺少文件：{file.RelativePath}。";
                if (file.Length != other.Length ||
                    !string.Equals(file.Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return $"文件内容不同：{file.RelativePath}。";
                }
            }

            return null;
        }

        private static void ValidateSourceAndDestinationPaths(
            string sourceDirectory,
            string publishRoot,
            string publishDirectory,
            string releaseDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Path.IsPathRooted(sourceDirectory))
                throw new InvalidOperationException("构建输出目录必须能解析为绝对路径。");
            if (string.IsNullOrWhiteSpace(publishRoot) || !Path.IsPathRooted(publishRoot))
                throw new InvalidOperationException("本地发布根目录必须能解析为绝对路径。");
            if (IsSameOrDescendant(publishRoot, sourceDirectory) ||
                IsSameOrDescendant(sourceDirectory, publishRoot))
            {
                throw new InvalidOperationException(
                    $"构建输出目录与发布根目录不能相同或互相包含：source='{sourceDirectory}', publishRoot='{publishRoot}'。");
            }

            if (!IsSameOrDescendant(publishRoot, publishDirectory) ||
                !IsSameOrDescendant(publishRoot, releaseDirectory))
            {
                throw new InvalidOperationException("发布目标路径越出了本地发布根目录。");
            }

            ValidateNoReparsePointsInPath(sourceDirectory);
            ValidateNoReparsePointsInPath(publishRoot);
        }

        private static void ValidateNoReparsePointsRecursively(string root)
        {
            EnumerateFilesSafely(root);
        }

        private static void ValidateNoReparsePointsInPath(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    EnsureNotReparsePoint(current, attributes);
                }

                string parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }
        }

        private static void EnsureNotReparsePoint(string path, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"发布源或目标包含重解析点，拒绝继续：{path}");
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static string GetContainedPath(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidOperationException($"相对文件路径无效：{relativePath}");

            string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(root, normalized));
            if (!IsSameOrDescendant(Path.GetFullPath(root), candidate) ||
                string.Equals(Path.GetFullPath(root), candidate, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"文件路径越出根目录：{relativePath}");
            }

            return candidate;
        }

        private static bool IsSameOrDescendant(string parent, string candidate)
        {
            string normalizedParent = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedParent, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                return true;

            string prefix = normalizedParent + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("构建输出目录为空。");
            return Path.GetFullPath(path);
        }

        private static string ResolvePublishRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("本地发布根目录为空。");

            string root = Path.IsPathRooted(path)
                ? path
                : Path.Combine(Application.dataPath, "..", path);
            root = Path.GetFullPath(root);
            if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetPathRoot(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("本地发布根目录不能直接使用磁盘根目录。");
            }

            return root;
        }

        private static string ResolveExpectedSourceDirectory(TwoStageBuildSnapshot snapshot)
        {
            BuildConfig config = snapshot.Config;
            string outputRoot = config.OutputRoot;
            if (!Path.IsPathRooted(outputRoot))
                outputRoot = Path.Combine(Application.dataPath, "..", outputRoot);

            return Path.GetFullPath(Path.Combine(
                outputRoot,
                "TwoStageReleases",
                snapshot.BasePlayerId,
                snapshot.Platform,
                snapshot.Channel,
                snapshot.PackageName,
                config.ReleaseId));
        }

        private static string GetIdentityDirectory(TwoStageBuildSnapshot snapshot)
        {
            return GetIdentityDirectory(snapshot, ResolvePublishRoot(snapshot.PublishRoot));
        }

        private static string GetIdentityDirectory(TwoStageBuildSnapshot snapshot, string publishRoot)
        {
            return Path.GetFullPath(Path.Combine(
                publishRoot,
                snapshot.BasePlayerId,
                snapshot.Platform,
                snapshot.Channel,
                snapshot.PackageName));
        }

        private static string CreateUniqueDirectoryPath(string parent, string prefix)
        {
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(parent, prefix + Guid.NewGuid().ToString("N").Substring(0, 12));
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                    return candidate;
            }

            throw new IOException($"无法创建不冲突的临时发布目录：{parent}");
        }

        private static string CreateUniqueFilePath(string parent, string prefix, string suffix)
        {
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(parent, prefix + Guid.NewGuid().ToString("N").Substring(0, 12) + suffix);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                    return candidate;
            }

            throw new IOException($"无法创建不冲突的临时入口文件：{parent}");
        }

        private static void EnsureValidSnapshot(TwoStageBuildSnapshot snapshot)
        {
            List<string> errors = ValidateRequest(snapshot);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
        }

        private static bool IsSafePathSegment(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !Path.IsPathRooted(value) &&
                   value.IndexOf('/') < 0 &&
                   value.IndexOf('\\') < 0 &&
                   !value.Contains("..") &&
                   value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static string NormalizeRelativePath(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized == ".." ||
                normalized.StartsWith("../", StringComparison.Ordinal) ||
                Path.IsPathRooted(normalized))
            {
                throw new InvalidOperationException($"源文件相对路径无效：{path}");
            }

            return normalized;
        }

        private static string GetOptionalPublishDirectory(TwoStageBuildSnapshot snapshot)
        {
            try { return GetPublishDirectory(snapshot); }
            catch { return null; }
        }

        private static string GetOptionalReleaseDirectory(TwoStageBuildSnapshot snapshot)
        {
            try
            {
                return Path.Combine(GetPublishDirectory(snapshot), "releases", snapshot.Config.ReleaseId);
            }
            catch { return null; }
        }

        private static string GetOptionalEntryPath(TwoStageBuildSnapshot snapshot)
        {
            try { return GetEntryPath(snapshot); }
            catch { return null; }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"清理本次发布临时入口失败（不影响旧入口）：{path}，{exception.Message}");
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"清理本次发布临时目录失败（不影响旧入口）：{path}，{exception.Message}");
            }
        }

        private sealed class FileFingerprint
        {
            public string FullPath;
            public string RelativePath;
            public long Length;
            public string Sha256;
        }
    }
}
