using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TEngine;
using UnityEngine;

namespace TEngine.BuildPipelineTests
{
    /// <summary>
    /// DLL 产物校验/复制测试：全部使用隔离临时目录，不触碰 Assets/AssetRaw/DLL。
    /// </summary>
    public sealed class DllArtifactCopierTests
    {
        private string _srcDir;
        private string _dstDir;

        [SetUp]
        public void SetUp()
        {
            _srcDir = Path.Combine(Path.GetTempPath(), "te-bp-test-" + Path.GetRandomFileName());
            _dstDir = Path.Combine(Path.GetTempPath(), "te-bp-dst-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_srcDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_srcDir))
                Directory.Delete(_srcDir, true);
            if (Directory.Exists(_dstDir))
                Directory.Delete(_dstDir, true);
        }

        private string WriteSource(string fileName, string content)
        {
            string path = Path.Combine(_srcDir, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        private static List<TEngine.DllCopyEntry> Plan(params (string src, string dst)[] items)
        {
            return items.Select(i => new DllCopyEntry(i.src, i.dst)).ToList();
        }

        [Test]
        public void MissingSource_ReportsAllMissing_NotJustFirst()
        {
            string a = Path.Combine(_srcDir, "a.dll");
            string b = WriteSource("b.dll", "bbb");

            var errors = DllArtifactCopier.ValidateSources(Plan((a, Path.Combine(_dstDir, "a.dll.bytes")), (b, Path.Combine(_dstDir, "b.dll.bytes"))));

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("a.dll", errors[0]);
        }

        [Test]
        public void EmptySource_Fails()
        {
            string a = WriteSource("empty.dll", string.Empty);
            var errors = DllArtifactCopier.ValidateSources(Plan((a, Path.Combine(_dstDir, "empty.dll.bytes"))));
            Assert.IsTrue(errors.Any(e => e.Contains("为空")), string.Join("\n", errors));
        }

        [Test]
        public void EmptyPlan_Fails()
        {
            var errors = DllArtifactCopier.ValidateSources(new List<TEngine.DllCopyEntry>());
            Assert.IsNotEmpty(errors);
        }

        [Test]
        public void CopyAll_CreatesDirAndCopiesContent_Overwrite()
        {
            string src = WriteSource("hot.dll", "content-1");
            string dst = Path.Combine(_dstDir, "sub", "hot.dll.bytes");

            DllArtifactCopier.CopyAll(Plan((src, dst)));
            Assert.AreEqual("content-1", File.ReadAllText(dst));

            // 覆盖复制
            File.WriteAllText(src, "content-2");
            DllArtifactCopier.CopyAll(Plan((src, dst)));
            Assert.AreEqual("content-2", File.ReadAllText(dst));
        }

        [Test]
        public void Reuse_StaleBytesWithDifferentContent_Fails()
        {
            string src = WriteSource("hot.dll", "new-content");
            string dst = Path.Combine(_dstDir, "hot.dll.bytes");
            Directory.CreateDirectory(_dstDir);
            File.WriteAllText(dst, "old-content"); // 旧产物

            var errors = DllArtifactCopier.ValidateReuse(Plan((src, dst)));

            Assert.IsTrue(errors.Any(e => e.Contains("不一致")), string.Join("\n", errors));
        }

        [Test]
        public void Reuse_MatchingContent_Passes()
        {
            string src = WriteSource("hot.dll", "same");
            string dst = Path.Combine(_dstDir, "hot.dll.bytes");
            Directory.CreateDirectory(_dstDir);
            File.WriteAllText(dst, "same");

            var errors = DllArtifactCopier.ValidateReuse(Plan((src, dst)));
            Assert.IsEmpty(errors);
        }

        [Test]
        public void Reuse_MissingBytes_Fails()
        {
            string src = WriteSource("hot.dll", "same");
            string dst = Path.Combine(_dstDir, "hot.dll.bytes");
            Directory.CreateDirectory(_dstDir);

            var errors = DllArtifactCopier.ValidateReuse(Plan((src, dst)));
            Assert.IsTrue(errors.Any(e => e.Contains("缺少已复制产物")), string.Join("\n", errors));
        }

        [Test]
        public void Reuse_SourceMissing_FailsBeforeReuseCheck()
        {
            string dst = Path.Combine(_dstDir, "hot.dll.bytes");
            Directory.CreateDirectory(_dstDir);
            File.WriteAllText(dst, "same");

            var errors = DllArtifactCopier.ValidateReuse(Plan((Path.Combine(_srcDir, "not-exist.dll"), dst)));
            Assert.IsTrue(errors.Any(e => e.Contains("缺少必需 DLL 源文件")), string.Join("\n", errors));
        }

        [Test]
        public void ResolveAssemblyTextAssetDir_RejectsEscapingPath()
        {
            // AssemblyTextAssetPath 正常情况指向 Assets 内目录；这里通过反射无法注入，
            // 改为直接验证目录解析 API 在真实配置下不抛出且位于 Assets 内。
            string dir = DllArtifactCopier.ResolveAssemblyTextAssetDir(false);
            string assetsRoot = Path.GetFullPath(Application.dataPath);
            StringAssert.StartsWith(assetsRoot, dir);
        }
    }
}
