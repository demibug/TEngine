using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TEngine;
using UnityEngine;

namespace TEngine.BuildPipelineTests
{
    /// <summary>
    /// AB 输出核验与输出根解析测试：使用隔离临时目录，文件名全部来自 YooAsset 工具方法。
    /// </summary>
    public sealed class AssetBundleOutputVerificationTests
    {
        private string _outputDir;

        [SetUp]
        public void SetUp()
        {
            _outputDir = Path.Combine(Path.GetTempPath(), "te-bp-about-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_outputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_outputDir))
                Directory.Delete(_outputDir, true);
        }

        private void WriteAllRequiredFiles(string version)
        {
            string pkg = ReleaseTools.PackageName;
            string[] files =
            {
                YooAsset.YooAssetSettingsData.GetBuildReportFileName(pkg, version),
                YooAsset.YooAssetSettingsData.GetManifestBinaryFileName(pkg, version),
                YooAsset.YooAssetSettingsData.GetManifestJsonFileName(pkg, version),
                YooAsset.YooAssetSettingsData.GetPackageHashFileName(pkg, version),
                YooAsset.YooAssetSettingsData.GetPackageVersionFileName(pkg),
            };
            foreach (var file in files)
                File.WriteAllText(Path.Combine(_outputDir, file), "data");
        }

        [Test]
        public void AllRequiredFilesPresent_Passes()
        {
            WriteAllRequiredFiles("v1");
            var missing = ReleaseTools.VerifyAssetBundleOutputs(_outputDir, "v1");
            Assert.IsEmpty(missing);
        }

        [Test]
        public void MissingVersionFile_Fails()
        {
            WriteAllRequiredFiles("v1");
            File.Delete(Path.Combine(_outputDir, YooAsset.YooAssetSettingsData.GetPackageVersionFileName(ReleaseTools.PackageName)));

            var missing = ReleaseTools.VerifyAssetBundleOutputs(_outputDir, "v1");
            Assert.AreEqual(1, missing.Count);
            StringAssert.Contains("版本文件", missing[0]);
        }

        [Test]
        public void EmptyManifestFile_Fails()
        {
            WriteAllRequiredFiles("v1");
            File.WriteAllText(Path.Combine(_outputDir, YooAsset.YooAssetSettingsData.GetManifestBinaryFileName(ReleaseTools.PackageName, "v1")), string.Empty);

            var missing = ReleaseTools.VerifyAssetBundleOutputs(_outputDir, "v1");
            Assert.IsTrue(missing.Any(m => m.Contains("为空")), string.Join("\n", missing));
        }

        [Test]
        public void OutputDirectoryMissing_Fails()
        {
            var missing = ReleaseTools.VerifyAssetBundleOutputs(Path.Combine(_outputDir, "not-exists"), "v1");
            Assert.AreEqual(1, missing.Count);
            StringAssert.Contains("输出目录不存在", missing[0]);
        }

        [Test]
        public void ResolveOutputRoot_RelativePath_BecomesProjectRootedFullPath()
        {
            var config = new BuildConfig { OutputRoot = "./Builds/Test/" };
            string resolved = ReleaseTools.ResolveOutputRoot(config);
            Assert.IsTrue(Path.IsPathRooted(resolved));
            StringAssert.EndsWith("Builds/Test", resolved.Replace('\\', '/').TrimEnd('/'));
        }

        [Test]
        public void ResolveOutputRoot_AbsolutePath_KeptAsIs()
        {
            string absolute = Path.Combine(Path.GetTempPath(), "te-abs-out");
            var config = new BuildConfig { OutputRoot = absolute };
            string resolved = ReleaseTools.ResolveOutputRoot(config);
            Assert.AreEqual(Path.GetFullPath(absolute).Replace('\\', '/'), resolved);
        }
    }
}
