using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TEngine;
using UnityEditor;

namespace TEngine.BuildPipelineTests
{
    public sealed class TwoStageReleaseIdRegistryTests
    {
        private string _root;
        private UpdateSetting _setting;
        private bool _enabled;
        private string _basePlayerId;
        private string _channel;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "TEngine-ReleaseIdRegistry-" + Guid.NewGuid().ToString("N"));
            _setting = Settings.UpdateSetting;
            _enabled = _setting.EnableTwoStageUpdate;
            _basePlayerId = _setting.BasePlayerId;
            _channel = _setting.Channel;
            _setting.EnableTwoStageUpdate = true;
            _setting.BasePlayerId = "player-registry-test";
            _setting.Channel = "channel-registry-test";
        }

        [TearDown]
        public void TearDown()
        {
            _setting.EnableTwoStageUpdate = _enabled;
            _setting.BasePlayerId = _basePlayerId;
            _setting.Channel = _channel;
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [TestCase(true, "v1")]
        [TestCase(false, "v2")]
        public void CompletedReleaseIdReuse_FailsBeforeOldArtifactsChange(bool clearBuildCache, string nextVersion)
        {
            BuildConfig first = CreateConfig("release-fixed", "v1");
            TwoStageReleaseBuilder.ReserveReleaseId(first);

            string oldOutput = Path.Combine(_root, "old-release");
            Directory.CreateDirectory(oldOutput);
            string descriptor = Write(oldOutput, "descriptor.json", new byte[] { 1, 2, 3 });
            string manifest = Write(oldOutput, "manifest.bytes", new byte[] { 4, 5, 6 });
            string bundle = Write(oldOutput, "content.bundle", new byte[] { 7, 8, 9 });
            Complete(first, descriptor);
            byte[] descriptorBefore = File.ReadAllBytes(descriptor);
            byte[] manifestBefore = File.ReadAllBytes(manifest);
            byte[] bundleBefore = File.ReadAllBytes(bundle);

            BuildConfig reuse = CreateConfig("release-fixed", nextVersion);
            reuse.ClearBuildCache = clearBuildCache;

            BuildExecutionException exception = Assert.Throws<BuildExecutionException>(
                () => TwoStageReleaseBuilder.ValidateReleaseIdAvailable(reuse));
            StringAssert.Contains("已被占用", exception.Message);
            CollectionAssert.AreEqual(descriptorBefore, File.ReadAllBytes(descriptor));
            CollectionAssert.AreEqual(manifestBefore, File.ReadAllBytes(manifest));
            CollectionAssert.AreEqual(bundleBefore, File.ReadAllBytes(bundle));
        }

        [Test]
        public void NewReleaseId_CanBeReservedIndependentlyOfPackageVersion()
        {
            BuildConfig first = CreateConfig("release-a", "v1");
            TwoStageReleaseBuilder.ReserveReleaseId(first);

            BuildConfig second = CreateConfig("release-b", "v1");
            Assert.DoesNotThrow(() => TwoStageReleaseBuilder.ReserveReleaseId(second));
            Assert.IsTrue(File.Exists(TwoStageReleaseBuilder.GetReleaseIdRecordPath(second)));
        }

        private BuildConfig CreateConfig(string releaseId, string packageVersion)
        {
            return new BuildConfig
            {
                BuildTarget = BuildTarget.Android,
                OutputRoot = _root,
                ReleaseId = releaseId,
                PackageVersion = packageVersion,
            };
        }

        private static string Write(string directory, string name, byte[] bytes)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void Complete(BuildConfig config, string descriptorPath)
        {
            MethodInfo method = typeof(TwoStageReleaseBuilder).GetMethod(
                "CompleteReleaseIdReservation", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            try
            {
                method.Invoke(null, new object[] { config, descriptorPath });
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }
    }
}
