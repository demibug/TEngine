using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
#if ENABLE_HYBRIDCLR
using HybridCLR.Editor;
#endif
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TEngine
{
    /// <summary>
    /// 验证真实构建报告中的 Bootstrap 隔离，并生成不可变的本地 release descriptor。
    /// </summary>
    public static class TwoStageReleaseBuilder
    {
        public static string GenerateAndVerify(BuildConfig config, string outputPackageDirectory)
        {
            UpdateSetting setting = Settings.UpdateSetting;
            if (setting == null || !setting.EnableTwoStageUpdate)
                return null;

            if (config == null)
                throw new BuildExecutionException(BuildStage.AssetBundle, "生成两阶段 release 时 BuildConfig 为空");

            string reportPath = Path.Combine(
                outputPackageDirectory,
                YooAssetSettingsData.GetBuildReportFileName(ReleaseTools.PackageName, config.PackageVersion));
            if (!File.Exists(reportPath))
                throw new BuildExecutionException(BuildStage.AssetBundle, $"缺少两阶段构建报告: {reportPath}");

            BuildReport report;
            try
            {
                report = JsonUtility.FromJson<BuildReport>(File.ReadAllText(reportPath));
            }
            catch (Exception exception)
            {
                throw new BuildExecutionException(BuildStage.AssetBundle, "两阶段构建报告 JSON 无效", exception);
            }

            VerifyBootstrapBundle(report, config, setting);

            string businessDir = DllArtifactCopier.ResolveAssemblyTextAssetDir(false);
            string bootstrapDir = DllArtifactCopier.ResolveBootstrapTextAssetDir(false);
            string manifestName = YooAssetSettingsData.GetManifestBinaryFileName(
                ReleaseTools.PackageName,
                config.PackageVersion);
            string manifestPath = Path.Combine(outputPackageDirectory, manifestName);
            List<string> businessNames = setting.HotUpdateAssemblies
                .Where(name => !string.Equals(name, setting.BootstrapAssemblyName, StringComparison.Ordinal))
                .ToList();

            UpdateReleaseDescriptor descriptor = new UpdateReleaseDescriptor
            {
                ContractVersion = setting.TwoStageContractVersion,
                ReleaseId = config.ReleaseId,
                BasePlayerId = setting.BasePlayerId,
                Platform = GetPlatformName(config.BuildTarget),
                Channel = setting.Channel,
                PackageName = ReleaseTools.PackageName,
                PackageVersion = config.PackageVersion,
                ManifestId = manifestName,
                ManifestSha256 = ComputeFileSha256(manifestPath),
                BootstrapAssembly = CreateArtifact(setting.BootstrapAssemblyName, bootstrapDir, setting),
                BusinessAssemblies = businessNames.Select(name => CreateArtifact(name, businessDir, setting)).ToArray(),
                MetadataAssemblies = setting.AOTMetaAssemblies
                    .Select(name => CreateArtifact(name, bootstrapDir, setting))
                    .ToArray(),
            };

            UpdateReleaseValidationContext expected = new UpdateReleaseValidationContext
            {
                ContractVersion = setting.TwoStageContractVersion,
                BasePlayerId = setting.BasePlayerId,
                Platform = GetPlatformName(config.BuildTarget),
                Channel = setting.Channel,
                PackageName = ReleaseTools.PackageName,
                ManifestId = manifestName,
                BootstrapAssemblyName = setting.BootstrapAssemblyName,
                HotUpdateAssemblies = setting.HotUpdateAssemblies,
                MetadataAssemblies = setting.AOTMetaAssemblies,
            };
            List<string> validationErrors = UpdateReleaseDescriptorValidator.Validate(descriptor, expected);
            if (validationErrors.Count > 0)
                throw new BuildExecutionException(
                    BuildStage.AssetBundle,
                    "生成的两阶段 release descriptor 未通过自校验:\n" + string.Join("\n", validationErrors));

            string json = JsonUtility.ToJson(descriptor, true);
            string outputPath = GetDescriptorOutputPath(outputPackageDirectory, config.ReleaseId);
            WriteImmutable(outputPath, json);
            CompleteReleaseIdReservation(config, outputPath);
            Debug.Log($"[TwoStageRelease] descriptor: {outputPath}");
            return outputPath;
        }

        /// <summary>
        /// Player 构建后重新比对最终 strip 目录，防止 AB 前 metadata 与最终基础 Player 不一致。
        /// </summary>
        public static void VerifyFinalAotMetadata(BuildConfig config, string outputPackageDirectory)
        {
            UpdateSetting setting = Settings.UpdateSetting;
            if (setting == null || !setting.EnableTwoStageUpdate)
                return;

#if !ENABLE_HYBRIDCLR
            throw new BuildExecutionException(BuildStage.Player, "两阶段 Player 构建完成，但当前编辑器未启用 HybridCLR，无法核对最终 AOT strip");
#else
            string descriptorPath = GetDescriptorOutputPath(outputPackageDirectory, config.ReleaseId);
            if (!File.Exists(descriptorPath))
                throw new BuildExecutionException(BuildStage.Player, $"Player 后复核缺少 release descriptor: {descriptorPath}");

            UpdateReleaseDescriptor descriptor = JsonUtility.FromJson<UpdateReleaseDescriptor>(File.ReadAllText(descriptorPath));
            string strippedDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(config.BuildTarget);
            List<string> errors = new List<string>();
            foreach (UpdateArtifactDescriptor metadata in descriptor.MetadataAssemblies ?? Array.Empty<UpdateArtifactDescriptor>())
            {
                string finalPath = Path.Combine(strippedDir, metadata.Name);
                if (!File.Exists(finalPath))
                {
                    errors.Add($"最终 Player strip 缺少 metadata 源: {finalPath}");
                    continue;
                }

                string actual = ComputeFileSha256(finalPath);
                if (!string.Equals(actual, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"最终 Player strip 已变化，metadata descriptor 过期: {metadata.Name}");
            }

            if (errors.Count > 0)
                throw new BuildExecutionException(
                    BuildStage.Player,
                    "最终 Player 的 AOT strip 与 release metadata 不匹配；必须重新复制 DLL、重建 AB 并复核:\n" +
                    string.Join("\n", errors));
#endif
        }

        public static string GetDescriptorOutputPath(string outputPackageDirectory, string releaseId)
        {
            return Path.Combine(outputPackageDirectory, $"TwoStageRelease_{releaseId}.json");
        }

        [Serializable]
        private sealed class ReleaseIdRecord
        {
            public int SchemaVersion = 1;
            public string State;
            public string ReleaseId;
            public string BasePlayerId;
            public string Platform;
            public string Channel;
            public string PackageName;
            public string PackageVersion;
            public string DescriptorPath;
            public string CreatedUtc;
            public string CompletedUtc;
        }

        /// <summary>
        /// 身份记录位于原始 OutputRoot 下，独立于 PackageVersion 和 YooAsset BuildOutputRoot 清理范围。
        /// </summary>
        public static string GetReleaseIdRecordPath(BuildConfig config)
        {
            UpdateSetting setting = Settings.UpdateSetting;
            EnsureSafeIdentitySegment(config?.ReleaseId, "ReleaseId");
            EnsureSafeIdentitySegment(setting?.BasePlayerId, "BasePlayerId");
            EnsureSafeIdentitySegment(setting?.Channel, "Channel");
            string root = config.OutputRoot;
            if (!Path.IsPathRooted(root))
                root = Path.Combine(Application.dataPath, "..", root);
            return Path.GetFullPath(Path.Combine(root, "TwoStageReleaseRegistry",
                setting.BasePlayerId, GetPlatformName(config.BuildTarget), setting.Channel,
                ReleaseTools.PackageName, config.ReleaseId + ".json"));
        }

        public static void ValidateReleaseIdAvailable(BuildConfig config)
        {
            UpdateSetting setting = Settings.UpdateSetting;
            if (setting == null || !setting.EnableTwoStageUpdate)
                return;
            string path = GetReleaseIdRecordPath(config);
            if (File.Exists(path))
                throw new BuildExecutionException(BuildStage.Preflight,
                    $"两阶段 ReleaseId 已被占用，必须生成新 ID: {config.ReleaseId} ({path})");

            // 兼容修复前已成功生成 descriptor、但尚无 registry 的 release。
            string legacyReleaseRoot = ReleaseTools.ResolveOutputRoot(config);
            if (Directory.Exists(legacyReleaseRoot) && Directory.EnumerateFiles(
                    legacyReleaseRoot, $"TwoStageRelease_{config.ReleaseId}.json",
                    SearchOption.AllDirectories).Any())
                throw new BuildExecutionException(BuildStage.Preflight,
                    $"两阶段 ReleaseId 已存在历史 descriptor，必须生成新 ID: {config.ReleaseId} ({legacyReleaseRoot})");
        }

        /// <summary>
        /// 在 DLL、清缓存或输出写入前原子占用 ID。失败的构建也保留占用，避免覆盖部分产物。
        /// </summary>
        public static void ReserveReleaseId(BuildConfig config)
        {
            UpdateSetting setting = Settings.UpdateSetting;
            if (setting == null || !setting.EnableTwoStageUpdate)
                return;
            ValidateReleaseIdAvailable(config);
            string path = GetReleaseIdRecordPath(config);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var record = new ReleaseIdRecord
            {
                State = "Reserved",
                ReleaseId = config.ReleaseId,
                BasePlayerId = setting.BasePlayerId,
                Platform = GetPlatformName(config.BuildTarget),
                Channel = setting.Channel,
                PackageName = ReleaseTools.PackageName,
                PackageVersion = config.PackageVersion,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
            };
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(record, true));
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(bytes, 0, bytes.Length);
                config.TwoStageReleaseIdReserved = true;
            }
            catch (IOException exception)
            {
                throw new BuildExecutionException(BuildStage.Preflight,
                    $"两阶段 ReleaseId 已被并发占用，必须生成新 ID: {config.ReleaseId}", exception);
            }
        }

        private static void CompleteReleaseIdReservation(BuildConfig config, string descriptorPath)
        {
            string path = GetReleaseIdRecordPath(config);
            if (!config.TwoStageReleaseIdReserved || !File.Exists(path))
                throw new BuildExecutionException(BuildStage.AssetBundle,
                    $"两阶段 ReleaseId 缺少构建前保留记录: {path}");
            ReleaseIdRecord record = JsonUtility.FromJson<ReleaseIdRecord>(File.ReadAllText(path));
            if (record == null || !string.Equals(record.State, "Reserved", StringComparison.Ordinal) ||
                !string.Equals(record.ReleaseId, config.ReleaseId, StringComparison.Ordinal))
                throw new BuildExecutionException(BuildStage.AssetBundle,
                    $"两阶段 ReleaseId 保留记录无效: {path}");
            record.State = "Completed";
            record.DescriptorPath = Path.GetFullPath(descriptorPath);
            record.CompletedUtc = DateTime.UtcNow.ToString("O");
            WriteRecordReplacement(path, JsonUtility.ToJson(record, true));
        }

        private static void WriteRecordReplacement(string path, string json)
        {
            string temporary = CreateShortTemporaryPath(path);
            bool temporaryCreated = false;
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (FileStream stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    temporaryCreated = true;
                    stream.Write(bytes, 0, bytes.Length);
                }
                File.Replace(temporary, path, null);
            }
            finally
            {
                if (temporaryCreated && File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        /// <summary>
        /// 在目标文件同目录创建短临时文件名，避免完整目标路径叠加长后缀触发 Windows MAX_PATH。
        /// </summary>
        private static string CreateShortTemporaryPath(string destination)
        {
            string directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(directory))
                throw new IOException($"临时文件目标目录为空: {destination}");

            for (int i = 0; i < 8; i++)
            {
                string temporary = Path.Combine(
                    directory,
                    ".ts-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
                if (!File.Exists(temporary) && !Directory.Exists(temporary))
                    return temporary;
            }

            throw new IOException($"无法为目标创建不冲突的临时文件名: {destination}");
        }

        private static void EnsureSafeIdentitySegment(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
                value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.Contains("..") ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new BuildExecutionException(BuildStage.Preflight,
                    $"两阶段 {field} 必须是安全的单一目录段");
        }

        private static void VerifyBootstrapBundle(BuildReport report, BuildConfig config, UpdateSetting setting)
        {
            if (report?.Summary == null || report.BundleInfos == null)
                throw new BuildExecutionException(BuildStage.AssetBundle, "两阶段构建报告缺少 Summary/BundleInfos");
            if (!string.Equals(report.Summary.BuildPackageName, ReleaseTools.PackageName, StringComparison.Ordinal) ||
                !string.Equals(report.Summary.BuildPackageVersion, config.PackageVersion, StringComparison.Ordinal) ||
                report.Summary.BuildTarget != config.BuildTarget)
                throw new BuildExecutionException(BuildStage.AssetBundle, "两阶段构建报告的包名/版本/目标与本次构建不一致");

            string bootstrapRoot = NormalizeAssetPath("Assets/" + setting.BootstrapTextAssetPath.Trim('/', '\\')) + "/";
            string businessRoot = NormalizeAssetPath("Assets/" + setting.AssemblyTextAssetPath.Trim('/', '\\')) + "/";
            HashSet<string> requiredBootstrap = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                bootstrapRoot + setting.BootstrapAssemblyName + setting.AssemblyTextAssetExtension,
            };
            foreach (string metadata in setting.AOTMetaAssemblies)
                requiredBootstrap.Add(bootstrapRoot + metadata + setting.AssemblyTextAssetExtension);

            HashSet<string> businessAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string hotUpdate in setting.HotUpdateAssemblies)
            {
                if (!string.Equals(hotUpdate, setting.BootstrapAssemblyName, StringComparison.Ordinal))
                    businessAssets.Add(businessRoot + hotUpdate + setting.AssemblyTextAssetExtension);
            }

            HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> bootstrapBundles = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, ReportBundleInfo> bundlesByName = new Dictionary<string, ReportBundleInfo>(StringComparer.Ordinal);
            foreach (ReportBundleInfo bundle in report.BundleInfos)
            {
                if (bundle == null || string.IsNullOrWhiteSpace(bundle.BundleName))
                    throw new BuildExecutionException(BuildStage.AssetBundle, "两阶段构建报告包含空 bundle/BundleName");
                if (!bundlesByName.TryAdd(bundle.BundleName, bundle))
                    throw new BuildExecutionException(BuildStage.AssetBundle, $"两阶段构建报告包含重复 bundle: {bundle.BundleName}");
            }

            foreach (ReportBundleInfo bundle in report.BundleInfos)
            {
                IEnumerable<YooAsset.Editor.AssetInfo> assets = bundle.BundleContents ?? new List<YooAsset.Editor.AssetInfo>();
                HashSet<string> contents = new HashSet<string>(
                    assets.Where(asset => asset != null).Select(asset => NormalizeAssetPath(asset.AssetPath)),
                    StringComparer.OrdinalIgnoreCase);
                bool containsBootstrap = contents.Any(requiredBootstrap.Contains);
                if (!containsBootstrap)
                    continue;

                bootstrapBundles.Add(bundle.BundleName ?? bundle.FileName ?? "<unnamed>");
                if (bundle.Tags == null || !bundle.Tags.Contains(setting.BootstrapTag))
                    throw new BuildExecutionException(
                        BuildStage.AssetBundle,
                        $"Bootstrap bundle '{bundle.BundleName}' 缺少标签 '{setting.BootstrapTag}'");
                if (contents.Any(businessAssets.Contains))
                    throw new BuildExecutionException(
                        BuildStage.AssetBundle,
                        $"Bootstrap bundle '{bundle.BundleName}' 错误捆绑了业务 DLL");
                found.UnionWith(contents.Where(requiredBootstrap.Contains));
            }

            List<string> missing = requiredBootstrap.Where(path => !found.Contains(path)).ToList();
            if (missing.Count > 0)
                throw new BuildExecutionException(
                    BuildStage.AssetBundle,
                    "Bootstrap bundle 缺少 updater/metadata:\n" + string.Join("\n", missing));
            if (bootstrapBundles.Count != 1)
                throw new BuildExecutionException(
                    BuildStage.AssetBundle,
                    $"Bootstrap updater/metadata 必须由独立 PackDirectory 形成一个 bundle，实际 {bootstrapBundles.Count} 个");

            VerifyBootstrapDependencyClosure(bootstrapBundles.Single(), bundlesByName, businessAssets);
        }

        private static void VerifyBootstrapDependencyClosure(
            string bootstrapBundleName,
            IReadOnlyDictionary<string, ReportBundleInfo> bundlesByName,
            HashSet<string> businessAssets)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            Stack<string> pending = new Stack<string>();
            pending.Push(bootstrapBundleName);
            while (pending.Count > 0)
            {
                string bundleName = pending.Pop();
                if (!visited.Add(bundleName))
                    continue;
                if (!bundlesByName.TryGetValue(bundleName, out ReportBundleInfo bundle))
                    throw new BuildExecutionException(
                        BuildStage.AssetBundle,
                        $"Bootstrap 依赖闭包引用了报告中不存在的 bundle: {bundleName}");

                IEnumerable<YooAsset.Editor.AssetInfo> assets =
                    bundle.BundleContents ?? new List<YooAsset.Editor.AssetInfo>();
                string bundledBusiness = assets
                    .Where(asset => asset != null)
                    .Select(asset => NormalizeAssetPath(asset.AssetPath))
                    .FirstOrDefault(businessAssets.Contains);
                if (!string.IsNullOrEmpty(bundledBusiness))
                    throw new BuildExecutionException(
                        BuildStage.AssetBundle,
                        $"Bootstrap 依赖闭包 bundle '{bundleName}' 包含业务 DLL: {bundledBusiness}");

                foreach (string dependency in bundle.DependBundles ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(dependency))
                        throw new BuildExecutionException(
                            BuildStage.AssetBundle,
                            $"Bootstrap 依赖闭包 bundle '{bundleName}' 包含空依赖名");
                    pending.Push(dependency);
                }
            }
        }

        private static UpdateArtifactDescriptor CreateArtifact(string name, string directory, UpdateSetting setting)
        {
            string path = Path.Combine(directory, name + setting.AssemblyTextAssetExtension);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new BuildExecutionException(BuildStage.AssetBundle, $"release 缺少 DLL/metadata 产物: {path}");
            return new UpdateArtifactDescriptor
            {
                Name = name,
                Address = name,
                Sha256 = ComputeFileSha256(path),
            };
        }

        private static string ComputeFileSha256(string path)
        {
            if (!File.Exists(path))
                throw new BuildExecutionException(BuildStage.AssetBundle, $"摘要源文件不存在: {path}");
            return UpdateReleaseDescriptorValidator.ComputeSha256(File.ReadAllBytes(path));
        }

        private static void WriteImmutable(string path, string json)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (File.Exists(path))
            {
                byte[] existing = File.ReadAllBytes(path);
                if (!existing.SequenceEqual(bytes))
                    throw new BuildExecutionException(
                        BuildStage.AssetBundle,
                        $"同一 ReleaseId 已存在不同内容，拒绝覆盖: {path}");
                return;
            }

            string temporary = CreateShortTemporaryPath(path);
            bool temporaryCreated = false;
            try
            {
                using (FileStream stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    temporaryCreated = true;
                    stream.Write(bytes, 0, bytes.Length);
                }
                File.Move(temporary, path);
            }
            finally
            {
                if (temporaryCreated && File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        public static string GetPlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64: return "Windows64";
                case BuildTarget.StandaloneOSX: return "MacOS";
                case BuildTarget.StandaloneLinux64: return "Linux64";
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "IOS";
                case BuildTarget.PS5: return "PS5";
                default:
                    throw new BuildExecutionException(BuildStage.Preflight, $"两阶段更新不支持构建目标 '{target}'");
            }
        }
    }
}
