using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using YooAsset.Editor;

namespace TEngine
{
    /// <summary>
    /// 最小包处理计划：先完整校验报告与目录，再形成删除清单；校验失败不得开始删除。
    /// </summary>
    public sealed class MinimalPackagePlan
    {
        /// <summary>本次构建包（DefaultPackage）的内置目录（完整路径）。</summary>
        public string BuiltinPackageDirectory;

        /// <summary>待删除的 .bundle 文件完整路径。</summary>
        public List<string> DeleteFiles = new List<string>();

        /// <summary>按保留 Tag 匹配的 .bundle 文件完整路径。</summary>
        public List<string> RetainFiles = new List<string>();

        /// <summary>内置目录内扫描到的 .bundle 总数。</summary>
        public int TotalBundleFiles;
    }

    /// <summary>
    /// 最小包处理器：仅处理本次构建包（DefaultPackage）的内置目录，不清理其他包或整个 StreamingAssets 根。
    /// </summary>
    public static class MinimalPackageProcessor
    {
        /// <summary>
        /// 由构建报告生成删除计划。任何校验失败都抛出异常（此时不应删除任何文件）。
        /// </summary>
        /// <param name="reportJson">本次构建报告 JSON。</param>
        /// <param name="expectedPackageName">期望包名（本次构建包名）。</param>
        /// <param name="expectedPackageVersion">期望版本（本次输入版本）。</param>
        /// <param name="expectedTarget">期望构建目标（NoTarget 表示不校验目标）。</param>
        /// <param name="retainTagsRaw">保留 Tag 原始串（逗号分隔，空表示删除所有 .bundle）。</param>
        /// <param name="builtinPackageDirectory">本次包的内置目录（完整路径）。</param>
        public static MinimalPackagePlan CreatePlan(string reportJson, string expectedPackageName, string expectedPackageVersion,
            BuildTarget expectedTarget, string retainTagsRaw, string builtinPackageDirectory)
        {
            if (string.IsNullOrWhiteSpace(reportJson))
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 构建报告内容为空");
            if (string.IsNullOrEmpty(expectedPackageName))
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 期望包名为空");
            if (string.IsNullOrEmpty(expectedPackageVersion))
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 期望版本为空");
            if (string.IsNullOrWhiteSpace(builtinPackageDirectory))
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 内置包目录为空");
            if (!Directory.Exists(builtinPackageDirectory))
                throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 内置包目录不存在: {builtinPackageDirectory}，请确认本次构建的内置文件拷贝选项");

            // 1. 解析报告（解析失败不得删除）
            YooAsset.Editor.BuildReport report;
            try
            {
                report = YooAsset.Editor.BuildReport.Deserialize(reportJson);
            }
            catch (Exception e)
            {
                throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 解析构建报告失败: {e.Message}", e);
            }

            if (report == null)
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 构建报告反序列化结果为空");

            // 2. 校验报告字段（包名/版本/目标/BundleInfos/文件名）
            var summary = report.Summary;
            if (summary == null)
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 报告缺少 Summary，报告不完整");

            if (string.IsNullOrEmpty(summary.BuildPackageName))
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 报告缺少包名字段 (Summary.BuildPackageName)");
            if (summary.BuildPackageName != expectedPackageName)
                throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 报告包名不匹配: 报告={summary.BuildPackageName} 期望={expectedPackageName}，拒绝处理其他包");

            if (string.IsNullOrEmpty(summary.BuildPackageVersion))
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 报告缺少版本字段 (Summary.BuildPackageVersion)");
            if (summary.BuildPackageVersion != expectedPackageVersion)
                throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 报告版本与本次输入不匹配: 报告={summary.BuildPackageVersion} 期望={expectedPackageVersion}，旧产物不能掩盖本次输出");

            if (summary.BuildTarget == BuildTarget.NoTarget)
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 报告缺少构建目标字段 (Summary.BuildTarget)");
            if (expectedTarget != BuildTarget.NoTarget && summary.BuildTarget != expectedTarget)
                throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 报告构建目标与本次请求不匹配: 报告={summary.BuildTarget} 期望={expectedTarget}");

            if (report.BundleInfos == null || report.BundleInfos.Count == 0)
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 报告 BundleInfos 为空，无法确认 bundle 清单");
            foreach (var bundleInfo in report.BundleInfos)
            {
                if (bundleInfo == null || string.IsNullOrEmpty(bundleInfo.FileName))
                    throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 报告 BundleInfos 存在空 FileName，报告不完整");
            }

            // 3. 由报告形成保留集合（保留现有 tag 匹配语义；空 tag = 删除全部非保留 .bundle）
            string[] retainTagArray = ParseRetainTags(retainTagsRaw);
            var retainFileNames = new HashSet<string>(StringComparer.Ordinal);
            if (retainTagArray.Length > 0)
            {
                foreach (var bundleInfo in report.BundleInfos)
                {
                    if (bundleInfo.Tags != null && HasTag(bundleInfo.Tags, retainTagArray))
                        retainFileNames.Add(bundleInfo.FileName);
                }
            }

            // 4. 扫描内置包目录（仅本次 DefaultPackage 目录，不跟随目录链接，拒绝逃逸路径）
            string root = Path.GetFullPath(builtinPackageDirectory).TrimEnd('/', '\\');
            var plan = new MinimalPackagePlan { BuiltinPackageDirectory = root };

            foreach (string file in EnumerateBundleFiles(root))
            {
                string full = Path.GetFullPath(file);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new BuildExecutionException(BuildStage.MinimalPackage, $"[最小包] 拒绝处理越出内置包目录的路径: {full}");

                plan.TotalBundleFiles++;
                string fileName = Path.GetFileName(full);
                if (retainFileNames.Contains(fileName))
                {
                    plan.RetainFiles.Add(full);
                }
                else
                {
                    plan.DeleteFiles.Add(full);
                }
            }

            return plan;
        }

        /// <summary>
        /// 执行删除计划与空目录清理；任何删除/后处理异常直接向上传播（调用方必须停止下游 Player 构建）。
        /// <remarks>不承诺删除回滚：失败后输出需重新构建后才可使用。</remarks>
        /// </summary>
        public static void ExecutePlan(MinimalPackagePlan plan)
        {
            if (plan == null)
                throw new BuildExecutionException(BuildStage.MinimalPackage, "[最小包] 删除计划为空");

            foreach (string file in plan.DeleteFiles)
            {
                File.Delete(file);
            }

            CleanEmptyDirectories(plan.BuiltinPackageDirectory);
        }

        /// <summary>
        /// 深度优先枚举目录下所有 *.bundle 文件；不跟随目录链接（ReparsePoint），避免越界。
        /// </summary>
        private static IEnumerable<string> EnumerateBundleFiles(string rootDir)
        {
            var pending = new Stack<string>();
            pending.Push(rootDir);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                var attributes = File.GetAttributes(dir);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                foreach (string file in Directory.GetFiles(dir, "*.bundle"))
                    yield return file;

                foreach (string subDir in Directory.GetDirectories(dir))
                    pending.Push(subDir);
            }
        }

        private static void CleanEmptyDirectories(string rootPath)
        {
            if (!Directory.Exists(rootPath))
                return;

            foreach (string dir in Directory.GetDirectories(rootPath))
            {
                var attributes = File.GetAttributes(dir);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                CleanEmptyDirectories(dir);
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
        }

        private static bool HasTag(string[] bundleTags, string[] matchTags)
        {
            foreach (var matchTag in matchTags)
            {
                foreach (var bundleTag in bundleTags)
                {
                    if (bundleTag == matchTag)
                        return true;
                }
            }
            return false;
        }

        private static string[] ParseRetainTags(string retainTags)
        {
            if (string.IsNullOrWhiteSpace(retainTags))
                return Array.Empty<string>();

            return retainTags
                .Split(',', '，') // 支持中英文逗号
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();
        }
    }
}
