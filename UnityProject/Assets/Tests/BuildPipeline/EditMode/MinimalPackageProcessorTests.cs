using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace TEngine.BuildPipelineTests
{
    /// <summary>
    /// 最小包处理器测试：隔离临时目录模拟 StreamingAssets/yoo 下的双包结构，验证范围限定与先校验后删除。
    /// </summary>
    public sealed class MinimalPackageProcessorTests
    {
        private string _yooRoot;
        private string _defaultPkgDir;
        private string _otherPkgDir;

        [SetUp]
        public void SetUp()
        {
            _yooRoot = Path.Combine(Path.GetTempPath(), "te-bp-yoo-" + Path.GetRandomFileName());
            _defaultPkgDir = Path.Combine(_yooRoot, "DefaultPackage");
            _otherPkgDir = Path.Combine(_yooRoot, "OtherPackage");
            Directory.CreateDirectory(_defaultPkgDir);
            Directory.CreateDirectory(_otherPkgDir); // 模拟另一个包，证明其完全不受影响
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_yooRoot))
                Directory.Delete(_yooRoot, true);
        }

        private string WriteBundle(string pkgDir, string fileName, string content = "bundle-data")
        {
            string path = Path.Combine(pkgDir, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        private static string SerializeReport(string packageName, string version, BuildTarget target,
            params (string fileName, string[] tags)[] bundles)
        {
            var report = new YooAsset.Editor.BuildReport
            {
                Summary = new ReportSummary
                {
                    BuildPackageName = packageName,
                    BuildPackageVersion = version,
                    BuildTarget = target,
                },
                BundleInfos = new List<ReportBundleInfo>(
                    bundles.Select(b => new ReportBundleInfo { FileName = b.fileName, Tags = b.tags })),
            };
            return JsonUtility.ToJson(report);
        }

        private void WriteBuiltinDefaultPackageFiles()
        {
            // 内置目录中的清单/版本/哈希文件，绝不能被删除
            File.WriteAllText(Path.Combine(_defaultPkgDir, "DefaultPackage_v1.bytes"), "manifest");
            File.WriteAllText(Path.Combine(_defaultPkgDir, "DefaultPackage_v1.hash"), "hash");
            File.WriteAllText(Path.Combine(_defaultPkgDir, "DefaultPackage.version"), "v1");
        }

        [Test]
        public void RetainTags_DeleteOnlyNonRetainedBundles_KeepManifestsAndOtherPackage()
        {
            string a = WriteBundle(_defaultPkgDir, "scene_a_111.bundle");
            string b = WriteBundle(_defaultPkgDir, "ui_b_222.bundle");
            string c = WriteBundle(_otherPkgDir, "other_c_333.bundle");
            WriteBuiltinDefaultPackageFiles();

            string json = SerializeReport("DefaultPackage", "v1", BuildTarget.Android,
                ("scene_a_111.bundle", null), ("ui_b_222.bundle", new[] { "base" }));

            var plan = MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "base", _defaultPkgDir);

            CollectionAssert.AreEqual(new[] { a }, plan.DeleteFiles, "只应删除未带保留 Tag 的 bundle");
            CollectionAssert.AreEqual(new[] { b }, plan.RetainFiles);
            Assert.AreEqual(2, plan.TotalBundleFiles);

            MinimalPackageProcessor.ExecutePlan(plan);

            Assert.IsFalse(File.Exists(a));
            Assert.IsTrue(File.Exists(b), "带保留 Tag 的 bundle 不能删除");
            Assert.IsTrue(File.Exists(Path.Combine(_defaultPkgDir, "DefaultPackage_v1.bytes")), "清单文件不能删除");
            Assert.IsTrue(File.Exists(Path.Combine(_defaultPkgDir, "DefaultPackage_v1.hash")));
            Assert.IsTrue(File.Exists(Path.Combine(_defaultPkgDir, "DefaultPackage.version")));
            Assert.IsTrue(File.Exists(c), "其他包文件必须完全不受影响");
        }

        [Test]
        public void EmptyRetainTags_DeletesAllBundlesInPackageOnly()
        {
            string a = WriteBundle(_defaultPkgDir, "scene_a_111.bundle");
            string b = WriteBundle(_defaultPkgDir, "ui_b_222.bundle");
            string c = WriteBundle(_otherPkgDir, "other_c_333.bundle");
            WriteBuiltinDefaultPackageFiles();

            string json = SerializeReport("DefaultPackage", "v1", BuildTarget.Android,
                ("scene_a_111.bundle", null), ("ui_b_222.bundle", null));

            var plan = MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir);
            MinimalPackageProcessor.ExecutePlan(plan);

            Assert.IsFalse(File.Exists(a));
            Assert.IsFalse(File.Exists(b));
            Assert.IsTrue(File.Exists(c), "其他包不受空 Tag 删除影响");
            Assert.IsTrue(File.Exists(Path.Combine(_defaultPkgDir, "DefaultPackage_v1.bytes")));
        }

        [Test]
        public void SubDirectoryBundle_InScopeAndDeleted()
        {
            string subDir = Path.Combine(_defaultPkgDir, "nested");
            Directory.CreateDirectory(subDir);
            string nested = WriteBundle(subDir, "nested_444.bundle");

            string json = SerializeReport("DefaultPackage", "v1", BuildTarget.Android, ("nested_444.bundle", null));
            var plan = MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir);
            MinimalPackageProcessor.ExecutePlan(plan);

            Assert.IsFalse(File.Exists(nested));
        }

        [Test]
        public void MissingVersionFieldMismatch_FailsAndDeletesNothing()
        {
            string a = WriteBundle(_defaultPkgDir, "scene_a_111.bundle");
            // 报告版本与本次输入不一致：旧产物不能掩盖本次输出
            string json = SerializeReport("DefaultPackage", "old-version", BuildTarget.Android, ("scene_a_111.bundle", null));

            Assert.Throws<BuildExecutionException>(() =>
                MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir));
            Assert.IsTrue(File.Exists(a), "校验失败不得删除任何文件");
        }

        [Test]
        public void PackageNameMismatch_Fails()
        {
            WriteBundle(_defaultPkgDir, "scene_a_111.bundle");
            string json = SerializeReport("OtherPackage", "v1", BuildTarget.Android, ("scene_a_111.bundle", null));

            var ex = Assert.Throws<BuildExecutionException>(() =>
                MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir));
            StringAssert.Contains("包名不匹配", ex.Message);
        }

        [Test]
        public void TargetMismatch_Fails()
        {
            WriteBundle(_defaultPkgDir, "scene_a_111.bundle");
            string json = SerializeReport("DefaultPackage", "v1", BuildTarget.iOS, ("scene_a_111.bundle", null));

            var ex = Assert.Throws<BuildExecutionException>(() =>
                MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir));
            StringAssert.Contains("构建目标", ex.Message);
        }

        [Test]
        public void CorruptReport_FailsAndDeletesNothing()
        {
            string a = WriteBundle(_defaultPkgDir, "scene_a_111.bundle");

            Assert.Throws<BuildExecutionException>(() =>
                MinimalPackageProcessor.CreatePlan("{bad json", "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir));
            Assert.IsTrue(File.Exists(a));
        }

        [Test]
        public void NullReportJson_Fails()
        {
            WriteBundle(_defaultPkgDir, "scene_a_111.bundle");
            Assert.Throws<BuildExecutionException>(() =>
                MinimalPackageProcessor.CreatePlan(null, "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir));
        }

        [Test]
        public void EmptyBundleInfos_Fails()
        {
            WriteBundle(_defaultPkgDir, "scene_a_111.bundle");
            string json = SerializeReport("DefaultPackage", "v1", BuildTarget.Android);

            Assert.Throws<BuildExecutionException>(() =>
                MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir));
        }

        [Test]
        public void MissingBuiltinDirectory_Fails()
        {
            string json = SerializeReport("DefaultPackage", "v1", BuildTarget.Android, ("scene_a_111.bundle", null));
            string missingDir = Path.Combine(_yooRoot, "NotExists");

            Assert.Throws<BuildExecutionException>(() =>
                MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", missingDir));
        }

        [Test]
        public void MissingPackageDirectory_DoesNotTouchOtherPackages()
        {
            // 期望包目录不存在 → 失败；OtherPackage 完全不变
            WriteBundle(_otherPkgDir, "other_c_333.bundle");
            string json = SerializeReport("DefaultPackage", "v1", BuildTarget.Android, ("scene_a_111.bundle", null));
            string wrongDir = Path.Combine(_yooRoot, "WrongPackage");

            Assert.Throws<BuildExecutionException>(() =>
                MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", wrongDir));
            Assert.IsTrue(File.Exists(Path.Combine(_otherPkgDir, "other_c_333.bundle")));
        }

        [Test]
        public void DeleteFailure_ThrowsOutOfExecutePlan()
        {
            // Windows 下锁定文件使 File.Delete 抛 IOException，验证删除失败向上传播（停止下游）
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                Assert.Ignore("仅 Windows 平台可稳定模拟文件锁定删除失败");

            string a = WriteBundle(_defaultPkgDir, "scene_a_111.bundle");
            string json = SerializeReport("DefaultPackage", "v1", BuildTarget.Android, ("scene_a_111.bundle", null));
            var plan = MinimalPackageProcessor.CreatePlan(json, "DefaultPackage", "v1", BuildTarget.Android, "", _defaultPkgDir);

            using (var stream = File.Open(a, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => MinimalPackageProcessor.ExecutePlan(plan), "删除失败必须向上传播");
            }

            Assert.IsTrue(File.Exists(a));
        }
    }
}
