using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset.Editor;

namespace TEngine.BuildPipelineTests
{
    /// <summary>
    /// 构建编排核心测试：用注入的阶段替身验证“失败停止下游、目标不一致零副作用、状态映射、阶段顺序”。
    /// </summary>
    public sealed class ReleaseToolsStageTests
    {
        private List<string> _calls;
        private BuildStageImpl _impl;

        [SetUp]
        public void SetUp()
        {
            _calls = new List<string>();
            _impl = new BuildStageImpl
            {
                GetActiveTarget = () => BuildTarget.Android,
                IsHybridClrAvailable = () => true,
                CompileHotFixDll = config => _calls.Add("Dll"),
                ValidateReusedDll = config => _calls.Add("ReuseCheck"),
                BuildAssetBundle = config => { _calls.Add("AB"); return AbStageOutcome.Succeed("mock://ab-output"); },
                ProcessMinimalPackage = (config, result) => _calls.Add("Minimal"),
                BuildPlayer = (target, group, output) => { _calls.Add($"Player:{target}:{output}"); return PlayerStageOutcome.Succeed(output); },
            };
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
                PlayerOutputPath = "Build/Android/test.apk",
            };
        }

        [Test]
        public void PreflightFail_ZeroSideEffectStageCalls()
        {
            var config = CreateValidConfig();
            config.BuildTarget = BuildTarget.iOS; // 与激活平台不一致

            LogAssert.Expect(LogType.Error, new Regex(@"构建失败"));
var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Failed);
            Assert.AreEqual(BuildStage.Preflight, result.Stage);
            Assert.IsEmpty(_calls, "预检失败后不应调用任何有副作用阶段");
        }

        [Test]
        public void AbFailure_StopsMinimalAndPlayer()
        {
            var config = CreateValidConfig();
            config.MinimalPackage = true;
            config.BuildPlayer = true;
            _impl.BuildAssetBundle = config2 => { _calls.Add("AB"); return AbStageOutcome.Fail("模拟 AB 失败"); };

            LogAssert.Expect(LogType.Error, new Regex(@"构建失败"));
var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: true, _impl);

            Assert.IsTrue(result.Failed);
            Assert.AreEqual(BuildStage.AssetBundle, result.Stage);
            StringAssert.Contains("模拟 AB 失败", result.Error);
            CollectionAssert.AreEqual(new[] { "ReuseCheck", "AB" }, _calls, "AB 失败后不得进入最小包/Player");
            Assert.IsNull(result.OutputPackageDirectory);
        }

        [Test]
        public void AbThrows_StopsDownstreamAndCapturesException()
        {
            var config = CreateValidConfig();
            _impl.BuildAssetBundle = config2 => throw new InvalidOperationException("AB 炸了");

            LogAssert.Expect(LogType.Error, new Regex(@"构建失败"));
var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Failed);
            Assert.AreEqual(BuildStage.AssetBundle, result.Stage);
            Assert.IsNotNull(result.Exception);
            StringAssert.Contains("AB 炸了", result.Error);
            Assert.IsFalse(_calls.Contains("Minimal"));
        }

        [Test]
        public void MinimalPackageFailure_StopsPlayer()
        {
            var config = CreateValidConfig();
            config.MinimalPackage = true;
            config.BuildPlayer = true;
            _impl.ProcessMinimalPackage = (config2, result2) => throw new InvalidOperationException("删除失败");

            LogAssert.Expect(LogType.Error, new Regex(@"构建失败"));
var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: true, _impl);

            Assert.IsTrue(result.Failed);
            Assert.AreEqual(BuildStage.MinimalPackage, result.Stage);
            CollectionAssert.AreEqual(new[] { "ReuseCheck", "AB", }, _calls.Where(c => !c.StartsWith("Player")).ToList());
            Assert.IsFalse(_calls.Any(c => c.StartsWith("Player")), "最小包失败后不得构建 Player");
        }

        [Test]
        public void PlayerFailed_MapsFailedAtPlayerStage()
        {
            var config = CreateValidConfig();
            config.BuildPlayer = true;
            _impl.BuildPlayer = (target, group, output) => PlayerStageOutcome.Fail("Player 构建失败");

            LogAssert.Expect(LogType.Error, new Regex(@"构建失败"));
var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Failed);
            Assert.AreEqual(BuildStage.Player, result.Stage);
            StringAssert.Contains("Player 构建失败", result.Error);
        }

        [Test]
        public void PlayerCancelled_MapsCancelled()
        {
            var config = CreateValidConfig();
            config.BuildPlayer = true;
            _impl.BuildPlayer = (target, group, output) => PlayerStageOutcome.Cancelled("用户取消");

            var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Cancelled);
            Assert.AreEqual(BuildStage.Player, result.Stage);
            StringAssert.Contains("取消", result.Error);
        }

        [Test]
        public void PlayerNullOutcome_MapsFailed()
        {
            var config = CreateValidConfig();
            config.BuildPlayer = true;
            _impl.BuildPlayer = (target, group, output) => null;

            LogAssert.Expect(LogType.Error, new Regex(@"构建失败"));
var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Failed);
            Assert.AreEqual(BuildStage.Player, result.Stage);
        }

        [Test]
        public void PlayerUnknownOutcome_MapsFailed()
        {
            var config = CreateValidConfig();
            config.BuildPlayer = true;
            // 模拟 BuildPlayer 返回 Unknown 报告：默认阶段实现才做映射，这里直接验证替身返回 Failed 视作失败
            _impl.BuildPlayer = (target, group, output) => PlayerStageOutcome.Fail("Player 构建结果未知: Unknown");

            LogAssert.Expect(LogType.Error, new Regex(@"构建失败"));
var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Failed);
            Assert.AreEqual(BuildStage.Player, result.Stage);
        }

        [Test]
        public void SuccessPath_StageOrderAndCompleted()
        {
            var config = CreateValidConfig();
            config.BuildHotFixDll = true;
            config.MinimalPackage = true;
            config.BuildPlayer = true;

            var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: true, _impl);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(BuildStage.Completed, result.Stage);
            CollectionAssert.AreEqual(new[] { "Dll", "AB", "Minimal", "Player:Android:Build/Android/test.apk" }, _calls);
            Assert.AreEqual("mock://ab-output", result.OutputPackageDirectory);
            Assert.AreEqual("Build/Android/test.apk", result.PlayerOutputPath);
            Assert.AreEqual("test-1.0", result.PackageVersion);
            Assert.AreEqual(BuildTarget.Android, result.Target);
        }

        [Test]
        public void MinimalPackageDisabled_MinimalStageSkipped()
        {
            var config = CreateValidConfig();
            var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Succeeded);
            CollectionAssert.AreEqual(new[] { "ReuseCheck", "AB" }, _calls);
        }

        [Test]
        public void BuildHotFixDllFalse_ReuseValidationRunsInsteadOfCompile()
        {
            var config = CreateValidConfig();
            config.BuildHotFixDll = false;
            var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Succeeded);
            CollectionAssert.AreEqual(new[] { "ReuseCheck", "AB" }, _calls);
        }

        [Test]
        public void BuildHotFixDllFalse_HybridClrUnavailable_SkipsDllStage()
        {
            var config = CreateValidConfig();
            config.BuildHotFixDll = false;
            _impl.IsHybridClrAvailable = () => false;
            var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Succeeded);
            CollectionAssert.AreEqual(new[] { "AB" }, _calls);
        }

        [Test]
        public void ReuseValidationFailure_BlocksAb()
        {
            var config = CreateValidConfig();
            config.BuildHotFixDll = false;
            _impl.ValidateReusedDll = config2 => throw new BuildExecutionException(BuildStage.Dll, "复用产物与源不一致");

            LogAssert.Expect(LogType.Error, new Regex(@"构建失败"));
var result = ReleaseTools.ExecuteBuildWithResult(config, buildPlayer: false, _impl);

            Assert.IsTrue(result.Failed);
            Assert.AreEqual(BuildStage.Dll, result.Stage);
            StringAssert.Contains("不一致", result.Error);
            CollectionAssert.IsEmpty(_calls.Where(c => c == "AB"), "DLL 复用校验失败后不得进入 AB");
        }

        [Test]
        public void CreateBuildParameters_VersionAndTargetFlowThrough()
        {
            var config = CreateValidConfig();
            config.PackageVersion = "custom-9.9";
            config.EncryptionType = EncryptionType.FileOffSet;

            var parameters = ReleaseTools.CreateBuildParameters(config, "mock://out");

            Assert.AreEqual("custom-9.9", parameters.PackageVersion, "自定义版本必须到达 AB 参数");
            Assert.AreEqual(BuildTarget.Android, parameters.BuildTarget);
            Assert.AreEqual(ReleaseTools.PackageName, parameters.PackageName);
            Assert.AreEqual("mock://out", parameters.BuildOutputRoot);
            Assert.IsNotNull(parameters.EncryptionServices);
        }

        [Test]
        public void TryGetBuildTarget_UnknownPlatform_ReturnsFalse()
        {
            Assert.IsFalse(ReleaseTools.TryGetBuildTarget("BadPlatform", out var target));
            Assert.AreEqual(BuildTarget.NoTarget, target);
            Assert.IsTrue(ReleaseTools.TryGetBuildTarget("Android", out var android));
            Assert.AreEqual(BuildTarget.Android, android);
        }
    }
}
