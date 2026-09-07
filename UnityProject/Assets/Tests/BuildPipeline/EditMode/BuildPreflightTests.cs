using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using YooAsset.Editor;

namespace TEngine.BuildPipelineTests
{
    /// <summary>
    /// 构建前置校验测试：只验证无副作用检查本身，全部使用注入的场景列表，不读 EditorBuildSettings。
    /// </summary>
    public sealed class BuildPreflightTests
    {
        private const string SomeScene = "Assets/Scenes/main.unity";

        private static List<string> OneScene()
        {
            return new List<string> { SomeScene };
        }

        private static BuildConfig CreateValidConfig()
        {
            return new BuildConfig
            {
                BuildTarget = BuildTarget.Android,
                PackageVersion = "test-1.0",
                OutputRoot = "./Builds/",
                BuildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll,
                BuildHotFixDll = false,
                MinimalPackage = false,
                BuildPlayer = false,
                PlayerPlatform = BuildTarget.Android,
            };
        }

        [Test]
        public void ValidConfig_NoErrors()
        {
            var errors = BuildPreflight.Validate(CreateValidConfig(), playerRequested: false,
                BuildTarget.Android, hybridClrAvailable: true, OneScene);
            Assert.IsEmpty(errors);
        }

        [Test]
        public void NoTarget_Fails()
        {
            var config = CreateValidConfig();
            config.BuildTarget = BuildTarget.NoTarget;
            var errors = BuildPreflight.Validate(config, false, BuildTarget.Android, true, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("NoTarget")), string.Join("\n", errors));
        }

        [Test]
        public void EmptyVersion_Fails()
        {
            var config = CreateValidConfig();
            config.PackageVersion = "  ";
            var errors = BuildPreflight.Validate(config, false, BuildTarget.Android, true, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("版本号")), string.Join("\n", errors));
        }

        [Test]
        public void VersionWithInvalidFileNameChars_Fails()
        {
            var config = CreateValidConfig();
            config.PackageVersion = "1.0/2\\3";
            var errors = BuildPreflight.Validate(config, false, BuildTarget.Android, true, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("非法文件名字符")), string.Join("\n", errors));
        }

        [Test]
        public void ActiveTargetMismatch_FailsWithNoAutoSwitchHint()
        {
            var errors = BuildPreflight.Validate(CreateValidConfig(), false, BuildTarget.StandaloneWindows64, true, OneScene);
            Assert.IsNotEmpty(errors);
            Assert.IsTrue(errors.Any(e => e.Contains("不会自动切换平台")), string.Join("\n", errors));
        }

        [Test]
        public void PlayerPlatformMismatch_Fails()
        {
            var config = CreateValidConfig();
            config.PlayerPlatform = BuildTarget.iOS;
            var errors = BuildPreflight.Validate(config, playerRequested: true, BuildTarget.Android, true, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("PlayerPlatform") && e.Contains("不一致")), string.Join("\n", errors));
        }

        [Test]
        public void PlayerRequested_NoEnabledScenes_Fails()
        {
            var config = CreateValidConfig();
            var errors = BuildPreflight.Validate(config, playerRequested: true, BuildTarget.Android, true,
                () => new List<string>());
            Assert.IsTrue(errors.Any(e => e.Contains("场景")), string.Join("\n", errors));
        }

        [Test]
        public void MinimalPackageWithNoneCopyOption_Fails()
        {
            var config = CreateValidConfig();
            config.MinimalPackage = true;
            config.BuildinFileCopyOption = EBuildinFileCopyOption.None;
            var errors = BuildPreflight.Validate(config, false, BuildTarget.Android, true, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("最小包")), string.Join("\n", errors));
        }

        [Test]
        public void BuildHotFixDllRequested_HybridClrUnavailable_Fails()
        {
            var config = CreateValidConfig();
            config.BuildHotFixDll = true;
            var errors = BuildPreflight.Validate(config, false, BuildTarget.Android, hybridClrAvailable: false, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("HybridCLR 未启用")), string.Join("\n", errors));
        }

        [Test]
        public void BuildHotFixDllNotRequested_HybridClrUnavailable_NoError()
        {
            var config = CreateValidConfig();
            config.BuildHotFixDll = false;
            var errors = BuildPreflight.Validate(config, false, BuildTarget.Android, hybridClrAvailable: false, OneScene);
            Assert.IsEmpty(errors);
        }

        [Test]
        public void PlayerOnly_TargetMismatchActive_Fails()
        {
            var errors = BuildPreflight.ValidatePlayerOnly(BuildTarget.Android, "Build/Android/x.apk",
                BuildTarget.StandaloneWindows64, null, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("不会自动切换平台")), string.Join("\n", errors));
        }

        [Test]
        public void PlayerOnly_GroupMismatch_Fails()
        {
            var errors = BuildPreflight.ValidatePlayerOnly(BuildTarget.Android, "Build/Android/x.apk",
                BuildTarget.Android, BuildTargetGroup.Standalone, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("目标组")), string.Join("\n", errors));
        }

        [Test]
        public void PlayerOnly_EmptyOutputPath_Fails()
        {
            var errors = BuildPreflight.ValidatePlayerOnly(BuildTarget.Android, " ",
                BuildTarget.Android, null, OneScene);
            Assert.IsTrue(errors.Any(e => e.Contains("输出路径")), string.Join("\n", errors));
        }

        [Test]
        public void PlayerOnly_NoScenes_Fails()
        {
            var errors = BuildPreflight.ValidatePlayerOnly(BuildTarget.Android, "Build/Android/x.apk",
                BuildTarget.Android, null, () => new List<string>());
            Assert.IsTrue(errors.Any(e => e.Contains("场景")), string.Join("\n", errors));
        }

        [Test]
        public void PlayerOnly_Valid_NoErrors()
        {
            var errors = BuildPreflight.ValidatePlayerOnly(BuildTarget.Android, "Build/Android/x.apk",
                BuildTarget.Android, BuildTargetGroup.Android, OneScene);
            Assert.IsEmpty(errors);
        }

        [Test]
        public void NullConfig_Fails()
        {
            var errors = BuildPreflight.Validate(null, false, BuildTarget.Android, true, OneScene);
            Assert.IsNotEmpty(errors);
        }
    }
}
