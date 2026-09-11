using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;
using YooAsset.Editor;

namespace TEngine.BuildPipelineTests
{
    /// <summary>
    /// 固定入口本地发布事务测试。测试只使用临时目录，不触碰工程构建产物。
    /// </summary>
    public sealed class TwoStageReleasePublisherTests
    {
        private UpdateSetting _setting;
        private UpdateSettingScope _settingScope;
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _setting = Settings.UpdateSetting;
            Assert.IsNotNull(_setting, "测试工程必须存在 TEngine UpdateSetting 资产。");
            _settingScope = new UpdateSettingScope(_setting);
            _temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "TEngine-TwoStagePublisher-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            _settingScope?.Dispose();
            if (!string.IsNullOrEmpty(_temporaryRoot) && Directory.Exists(_temporaryRoot))
                Directory.Delete(_temporaryRoot, true);
        }

        [UnityTest]
        public IEnumerator PublishTwoReleases_UpdatesEntryAndKeepsOldRelease()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot first = CreateSnapshot("release-a", "version-a", publishRoot);
                string firstSource = CreateSource(first, "a");
                TwoStageReleasePublishResult firstResult = await PublishAsync(first, firstSource);

                Assert.IsTrue(firstResult.Succeeded, firstResult.Error);
                Assert.IsTrue(Directory.Exists(firstResult.ReleaseDirectory));
                string oldEntry = File.ReadAllText(firstResult.EntryPath, Encoding.UTF8);

                TwoStageBuildSnapshot second = CreateSnapshot("release-b", "version-b", publishRoot);
                string secondSource = CreateSource(second, "b");
                TwoStageReleasePublishResult secondResult = await PublishAsync(second, secondSource);

                Assert.IsTrue(secondResult.Succeeded, secondResult.Error);
                Assert.IsTrue(Directory.Exists(firstResult.ReleaseDirectory));
                Assert.AreNotEqual(oldEntry, File.ReadAllText(secondResult.EntryPath, Encoding.UTF8));
                Assert.IsTrue(UpdateReleaseEntryValidator.TryParse(
                    File.ReadAllText(secondResult.EntryPath, Encoding.UTF8),
                    out UpdateReleaseEntry entry,
                    out string error), error);
                Assert.AreEqual("release-b", entry.ReleaseId);
            });
        }

        [UnityTest]
        public IEnumerator PublishSameReleaseWithIdenticalFiles_ReusesDirectory()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot snapshot = CreateSnapshot("release-reuse", "version-reuse", publishRoot);
                string source = CreateSource(snapshot, "same");

                TwoStageReleasePublishResult first = await PublishAsync(snapshot, source);
                TwoStageReleasePublishResult second = await PublishAsync(snapshot, source);

                Assert.IsTrue(first.Succeeded, first.Error);
                Assert.IsTrue(second.Succeeded, second.Error);
                Assert.IsTrue(second.ReusedExistingRelease);
            });
        }

        [UnityTest]
        public IEnumerator PublishSameReleaseWithDifferentFiles_FailsAndKeepsCurrentEntry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot snapshot = CreateSnapshot("release-immutable", "version-immutable", publishRoot);
                string firstSource = CreateSource(snapshot, "first");
                TwoStageReleasePublishResult first = await PublishAsync(snapshot, firstSource);
                string oldEntry = File.ReadAllText(first.EntryPath, Encoding.UTF8);

                string differentSource = CreateSource(snapshot, "different");
                TwoStageReleasePublishResult second = await PublishAsync(snapshot, differentSource);

                Assert.IsTrue(first.Succeeded, first.Error);
                Assert.IsTrue(second.Failed, "同一 ReleaseId 的不同文件集/内容必须拒绝覆盖。");
                Assert.AreEqual(oldEntry, File.ReadAllText(first.EntryPath, Encoding.UTF8));
                StringAssert.Contains("不同", second.Error);
            });
        }

        [UnityTest]
        public IEnumerator PublishCancelledBeforeCommit_DoesNotCreateReleaseOrEntry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot snapshot = CreateSnapshot("release-cancel", "version-cancel", publishRoot);
                string source = CreateSource(snapshot, "cancel");

                TwoStageReleasePublishResult result = await TwoStageReleasePublisher.PublishAsync(
                    snapshot,
                    CreateSuccessfulBuild(snapshot, source),
                    new CancellationToken(true));

                Assert.IsTrue(result.Cancelled, result.Error);
                Assert.IsFalse(File.Exists(result.EntryPath));
                Assert.IsFalse(Directory.Exists(result.ReleaseDirectory));
            });
        }

        [Test]
        public void PublishPreflight_RejectsBuildOutputInsidePublishRoot()
        {
            string publishRoot = Path.Combine(_temporaryRoot, "build-output");
            TwoStageBuildSnapshot snapshot = CreateSnapshot("release-path", "version-path", publishRoot);

            List<string> errors = TwoStageReleasePublisher.ValidateRequest(snapshot);

            Assert.IsTrue(
                errors.Any(error => error.Contains("不能相同或互相包含")),
                string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator ConcurrentPublishWithIdentityLock_FailsWithoutChangingCurrentEntry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot first = CreateSnapshot("release-lock-a", "version-lock-a", publishRoot);
                string firstSource = CreateSource(first, "lock-a");
                TwoStageReleasePublishResult firstResult = await PublishAsync(first, firstSource);
                string oldEntry = File.ReadAllText(firstResult.EntryPath, Encoding.UTF8);

                TwoStageBuildSnapshot second = CreateSnapshot("release-lock-b", "version-lock-b", publishRoot);
                string secondSource = CreateSource(second, "lock-b");
                string lockPath = Path.Combine(firstResult.PublishDirectory, ".two-stage-publish.lock");
                using (FileStream heldLock = new FileStream(
                           lockPath,
                           FileMode.OpenOrCreate,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                    TwoStageReleasePublishResult result = await PublishAsync(second, secondSource);
                    Assert.IsTrue(result.Failed);
                    StringAssert.Contains("并发", result.Error);
                }

                Assert.AreEqual(oldEntry, File.ReadAllText(firstResult.EntryPath, Encoding.UTF8));
                Assert.IsFalse(Directory.Exists(GetReleaseDirectory(second)));
            });
        }

        [UnityTest]
        public IEnumerator CopyInterrupted_KeepsOldEntryAndHistory()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot first = CreateSnapshot("release-copy-old", "version-copy-old", publishRoot);
                TwoStageReleasePublishResult firstResult = await PublishAsync(first, CreateSource(first, "old"));
                string oldEntry = File.ReadAllText(firstResult.EntryPath, Encoding.UTF8);

                TwoStageBuildSnapshot second = CreateSnapshot("release-copy-new", "version-copy-new", publishRoot);
                int copiedFiles = 0;
                TwoStageReleasePublisherTestHooks hooks = new TwoStageReleasePublisherTestHooks
                {
                    AfterFileCopied = _ =>
                    {
                        copiedFiles++;
                        throw new IOException("模拟复制中断");
                    },
                };

                TwoStageReleasePublishResult result = await PublishAsync(
                    second, CreateSource(second, "new"), CancellationToken.None, hooks);

                Assert.IsTrue(result.Failed, result.Error);
                Assert.AreEqual(1, copiedFiles);
                Assert.AreEqual(oldEntry, File.ReadAllText(firstResult.EntryPath, Encoding.UTF8));
                Assert.IsTrue(Directory.Exists(firstResult.ReleaseDirectory));
                Assert.IsFalse(Directory.Exists(GetReleaseDirectory(second)));
            });
        }

        [UnityTest]
        public IEnumerator EntryReplacementFailure_KeepsOldEntryAndPublishedHistory()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot first = CreateSnapshot("release-entry-old", "version-entry-old", publishRoot);
                TwoStageReleasePublishResult firstResult = await PublishAsync(first, CreateSource(first, "old"));
                string oldEntry = File.ReadAllText(firstResult.EntryPath, Encoding.UTF8);

                TwoStageBuildSnapshot second = CreateSnapshot("release-entry-new", "version-entry-new", publishRoot);
                TwoStageReleasePublisherTestHooks hooks = new TwoStageReleasePublisherTestHooks
                {
                    CommitCurrentEntry = (_, __) => throw new IOException("模拟入口原子替换失败"),
                };

                TwoStageReleasePublishResult result = await PublishAsync(
                    second, CreateSource(second, "new"), CancellationToken.None, hooks);

                Assert.IsTrue(result.Failed, result.Error);
                Assert.AreEqual(oldEntry, File.ReadAllText(firstResult.EntryPath, Encoding.UTF8));
                Assert.IsTrue(Directory.Exists(firstResult.ReleaseDirectory));
                Assert.IsTrue(Directory.Exists(result.ReleaseDirectory), "入口失败后应保留已准备好的完整 release。");
            });
        }

        [UnityTest]
        public IEnumerator CancelImmediatelyBeforeCommit_KeepsOldEntryAndHistory()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot first = CreateSnapshot("release-cancel-old", "version-cancel-old", publishRoot);
                TwoStageReleasePublishResult firstResult = await PublishAsync(first, CreateSource(first, "old"));
                string oldEntry = File.ReadAllText(firstResult.EntryPath, Encoding.UTF8);

                TwoStageBuildSnapshot second = CreateSnapshot("release-cancel-new", "version-cancel-new", publishRoot);
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    TwoStageReleasePublisherTestHooks hooks = new TwoStageReleasePublisherTestHooks
                    {
                        BeforeEntryCommit = cancellation.Cancel,
                    };
                    TwoStageReleasePublishResult result = await PublishAsync(
                        second, CreateSource(second, "new"), cancellation.Token, hooks);

                    Assert.IsTrue(result.Cancelled, result.Error);
                    Assert.AreEqual(oldEntry, File.ReadAllText(firstResult.EntryPath, Encoding.UTF8));
                    Assert.IsTrue(Directory.Exists(firstResult.ReleaseDirectory));
                }
            });
        }

        [UnityTest]
        public IEnumerator CancelAfterCommit_ReturnsSuccessAndKeepsCommittedEntry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot snapshot = CreateSnapshot("release-committed", "version-committed", publishRoot);
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    TwoStageReleasePublisherTestHooks hooks = new TwoStageReleasePublisherTestHooks
                    {
                        AfterEntryCommit = cancellation.Cancel,
                    };
                    TwoStageReleasePublishResult result = await PublishAsync(
                        snapshot, CreateSource(snapshot, "committed"), cancellation.Token, hooks);

                    Assert.IsTrue(result.Succeeded, result.Error);
                    Assert.IsTrue(cancellation.IsCancellationRequested);
                    Assert.IsTrue(UpdateReleaseEntryValidator.TryParse(
                        File.ReadAllText(result.EntryPath, Encoding.UTF8),
                        out UpdateReleaseEntry entry,
                        out string error), error);
                    Assert.AreEqual(snapshot.Config.ReleaseId, entry.ReleaseId);
                    Assert.IsTrue(Directory.Exists(result.ReleaseDirectory));
                }
            });
        }

        [UnityTest]
        public IEnumerator BuildFailure_DoesNotInvokePublisher()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot snapshot = CreateSnapshot("release-build-fail", "version-build-fail", publishRoot);
                int publisherCalls = 0;
                BuildConfig invalidConfig = new BuildConfig
                {
                    BuildTarget = snapshot.Config.BuildTarget,
                    PackageVersion = snapshot.Config.PackageVersion,
                    ReleaseId = snapshot.Config.ReleaseId,
                    OutputRoot = string.Empty,
                    BuildHotFixDll = false,
                    PlayerPlatform = snapshot.Config.PlayerPlatform,
                };
                TwoStageBuildSnapshot invalidSnapshot = TwoStageBuildSnapshot.Capture(invalidConfig, publishRoot);

                LogAssert.Expect(LogType.Error, new Regex("构建失败"));
                TwoStageBuildAndPublishResult result = await TwoStageBuildAndPublish.ExecuteAsync(
                    invalidSnapshot,
                    buildPlayer: false,
                    cancellationToken: CancellationToken.None,
                    implementation: BuildStageImpl.CreateDefault(),
                    publisher: (currentSnapshot, buildResult, token) =>
                    {
                        publisherCalls++;
                        return UniTask.FromResult(new TwoStageReleasePublishResult());
                    });

                Assert.IsNotNull(result.BuildResult);
                Assert.IsFalse(result.BuildResult.Succeeded);
                Assert.AreEqual(0, publisherCalls);
                Assert.IsNotNull(result.PublishResult);
                Assert.AreEqual(TwoStageReleasePublishStatus.NotRun, result.PublishResult.Status);
            });
        }

        [UnityTest]
        public IEnumerator PlayerFailure_DoesNotInvokePublisher()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot snapshot = CreateSnapshot("release-player-fail", "version-player-fail", publishRoot);
                BuildStageImpl implementation = CreateBuildStageImplementation();
                implementation.BuildPlayer = (target, group, output) => PlayerStageOutcome.Fail("模拟 Player 失败");
                CallCounter publisherCalls = new CallCounter();

                LogAssert.Expect(LogType.Error, new Regex("构建失败"));
                TwoStageBuildAndPublishResult result = await ExecuteWithInjectedPublisherAsync(
                    snapshot,
                    buildPlayer: true,
                    implementation,
                    publisherCalls);

                Assert.IsTrue(result.BuildResult.Failed);
                Assert.AreEqual(BuildStage.Player, result.BuildResult.Stage);
                Assert.AreEqual(0, publisherCalls.Value);
                Assert.AreEqual(TwoStageReleasePublishStatus.NotRun, result.PublishResult.Status);
            });
        }

        [UnityTest]
        public IEnumerator FinalAotVerificationFailure_DoesNotInvokePublisher()
        {
            return UniTask.ToCoroutine(async () =>
            {
                string publishRoot = Path.Combine(_temporaryRoot, "publish");
                TwoStageBuildSnapshot snapshot = CreateSnapshot("release-aot-fail", "version-aot-fail", publishRoot);
                BuildStageImpl implementation = CreateBuildStageImplementation();
                implementation.BuildPlayer = (target, group, output) => PlayerStageOutcome.Succeed(output);
                CallCounter publisherCalls = new CallCounter();

                LogAssert.Expect(LogType.Error, new Regex("构建失败"));
                TwoStageBuildAndPublishResult result = await ExecuteWithInjectedPublisherAsync(
                    snapshot,
                    buildPlayer: true,
                    implementation,
                    publisherCalls);

                Assert.IsTrue(result.BuildResult.Failed);
                Assert.AreEqual(BuildStage.Player, result.BuildResult.Stage);
                Assert.AreEqual(0, publisherCalls.Value);
                Assert.AreEqual(TwoStageReleasePublishStatus.NotRun, result.PublishResult.Status);
            });
        }

        [Test]
        public void ReleaseBuilderTemporaryName_IsShortAndSameDirectory()
        {
            string destination = Path.Combine(_temporaryRoot, new string('x', 180) + ".json");
            MethodInfo method = typeof(TwoStageReleaseBuilder).GetMethod(
                "CreateShortTemporaryPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            string temporary = (string)method.Invoke(null, new object[] { destination });
            Assert.AreEqual(
                Path.GetFullPath(Path.GetDirectoryName(destination)),
                Path.GetFullPath(Path.GetDirectoryName(temporary)));
            Assert.Less(Path.GetFileName(temporary).Length, 64);
            Assert.IsFalse(Path.GetFileName(temporary).Contains(new string('x', 20)));
        }

        private TwoStageBuildSnapshot CreateSnapshot(string releaseId, string packageVersion, string publishRoot)
        {
            BuildConfig config = new BuildConfig
            {
                BuildTarget = BuildTarget.Android,
                PackageVersion = packageVersion,
                ReleaseId = releaseId,
                OutputRoot = Path.Combine(_temporaryRoot, "build-output"),
                BuildHotFixDll = false,
                BuildPlayer = false,
                PlayerPlatform = BuildTarget.Android,
                PlayerOutputPath = Path.Combine(_temporaryRoot, "player-output.apk"),
                BuildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll,
            };
            return TwoStageBuildSnapshot.Capture(config, publishRoot);
        }

        private string CreateSource(TwoStageBuildSnapshot snapshot, string contentSuffix)
        {
            string source = Path.Combine(
                _temporaryRoot,
                "source-" + snapshot.Config.ReleaseId + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(source);

            byte[] manifest = Encoding.UTF8.GetBytes("manifest-" + contentSuffix);
            File.WriteAllBytes(Path.Combine(source, snapshot.CreateValidationContext().ManifestId), manifest);
            File.WriteAllText(Path.Combine(source, "bundle-" + contentSuffix + ".bundle"), "bundle-" + contentSuffix,
                Encoding.UTF8);
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(Path.Combine(source, "nested", "other.txt"), "other-" + contentSuffix, Encoding.UTF8);

            UpdateReleaseValidationContext expected = snapshot.CreateValidationContext();
            UpdateReleaseDescriptor descriptor = new UpdateReleaseDescriptor
            {
                ContractVersion = expected.ContractVersion,
                ReleaseId = snapshot.Config.ReleaseId,
                BasePlayerId = expected.BasePlayerId,
                Platform = expected.Platform,
                Channel = expected.Channel,
                PackageName = expected.PackageName,
                PackageVersion = snapshot.Config.PackageVersion,
                ManifestId = expected.ManifestId,
                ManifestSha256 = UpdateReleaseDescriptorValidator.ComputeSha256(manifest),
                BootstrapAssembly = CreateArtifact(expected.BootstrapAssemblyName),
                BusinessAssemblies = expected.HotUpdateAssemblies
                    .Where(name => !string.Equals(name, expected.BootstrapAssemblyName, StringComparison.Ordinal))
                    .Select(CreateArtifact)
                    .ToArray(),
                MetadataAssemblies = expected.MetadataAssemblies.Select(CreateArtifact).ToArray(),
            };
            File.WriteAllText(
                Path.Combine(source, "TwoStageRelease_" + snapshot.Config.ReleaseId + ".json"),
                JsonUtility.ToJson(descriptor, true),
                new UTF8Encoding(false));
            return source;
        }

        private static UpdateArtifactDescriptor CreateArtifact(string name)
        {
            return new UpdateArtifactDescriptor
            {
                Name = name,
                Address = name,
                Sha256 = new string('a', 64),
            };
        }

        private static BuildExecutionResult CreateSuccessfulBuild(
            TwoStageBuildSnapshot snapshot,
            string sourceDirectory)
        {
            return new BuildExecutionResult
            {
                Status = BuildStatus.Succeeded,
                Stage = BuildStage.Completed,
                Target = snapshot.Config.BuildTarget,
                PackageVersion = snapshot.Config.PackageVersion,
                OutputPackageDirectory = sourceDirectory,
            };
        }

        private BuildStageImpl CreateBuildStageImplementation()
        {
            return new BuildStageImpl
            {
                GetActiveTarget = () => BuildTarget.Android,
                IsHybridClrAvailable = () => true,
                ValidateReusedDll = config => { },
                BuildAssetBundle = config => AbStageOutcome.Succeed(
                    Path.Combine(_temporaryRoot, "injected-ab-output")),
            };
        }

        private static UniTask<TwoStageBuildAndPublishResult> ExecuteWithInjectedPublisherAsync(
            TwoStageBuildSnapshot snapshot,
            bool buildPlayer,
            BuildStageImpl implementation,
            CallCounter publisherCalls)
        {
            return TwoStageBuildAndPublish.ExecuteAsync(
                snapshot,
                buildPlayer,
                CancellationToken.None,
                implementation,
                (currentSnapshot, buildResult, token) =>
                {
                    publisherCalls.Value++;
                    return UniTask.FromResult(new TwoStageReleasePublishResult());
                });
        }

        private static UniTask<TwoStageReleasePublishResult> PublishAsync(
            TwoStageBuildSnapshot snapshot,
            string sourceDirectory)
        {
            return TwoStageReleasePublisher.PublishAsync(
                snapshot,
                CreateSuccessfulBuild(snapshot, sourceDirectory),
                CancellationToken.None);
        }

        private static UniTask<TwoStageReleasePublishResult> PublishAsync(
            TwoStageBuildSnapshot snapshot,
            string sourceDirectory,
            CancellationToken cancellationToken,
            TwoStageReleasePublisherTestHooks testHooks)
        {
            return TwoStageReleasePublisher.PublishAsync(
                snapshot,
                CreateSuccessfulBuild(snapshot, sourceDirectory),
                cancellationToken,
                testHooks);
        }

        private static string GetReleaseDirectory(TwoStageBuildSnapshot snapshot)
        {
            return Path.Combine(
                TwoStageReleasePublisher.GetPublishDirectory(snapshot),
                "releases",
                snapshot.Config.ReleaseId);
        }

        private sealed class CallCounter
        {
            public int Value;
        }

        private sealed class UpdateSettingScope : IDisposable
        {
            private readonly UpdateSetting _setting;
            private readonly bool _enabled;
            private readonly TwoStageReleaseSourceMode _sourceMode;
            private readonly int _contractVersion;
            private readonly string _basePlayerId;
            private readonly string _channel;
            private readonly string _descriptorUrl;
            private readonly string _hostUrl;
            private readonly string _fallbackUrl;
            private readonly UpdateStyle _updateStyle;
            private readonly string _logicMainDllName;
            private readonly string _bootstrapAssemblyName;
            private readonly string _bootstrapTextAssetPath;
            private readonly string _assemblyTextAssetPath;
            private readonly string _bootstrapTag;
            private readonly bool _allowInsecureLoopbackHttp;
            private readonly List<string> _hotUpdateAssemblies;
            private readonly List<string> _metadataAssemblies;

            public UpdateSettingScope(UpdateSetting setting)
            {
                _setting = setting;
                _enabled = setting.EnableTwoStageUpdate;
                _sourceMode = setting.TwoStageReleaseSourceMode;
                _contractVersion = setting.TwoStageContractVersion;
                _basePlayerId = setting.BasePlayerId;
                _channel = setting.Channel;
                _descriptorUrl = setting.TwoStageReleaseDescriptorUrl;
                _hostUrl = setting.TwoStageHostServerUrl;
                _fallbackUrl = setting.TwoStageFallbackHostServerUrl;
                _updateStyle = setting.UpdateStyle;
                _logicMainDllName = setting.LogicMainDllName;
                _bootstrapAssemblyName = setting.BootstrapAssemblyName;
                _bootstrapTextAssetPath = setting.BootstrapTextAssetPath;
                _assemblyTextAssetPath = setting.AssemblyTextAssetPath;
                _bootstrapTag = setting.BootstrapTag;
                _allowInsecureLoopbackHttp = setting.AllowInsecureLoopbackHttp;
                _hotUpdateAssemblies = new List<string>(setting.HotUpdateAssemblies ?? new List<string>());
                _metadataAssemblies = new List<string>(setting.AOTMetaAssemblies ?? new List<string>());

                setting.EnableTwoStageUpdate = true;
                setting.TwoStageReleaseSourceMode = TwoStageReleaseSourceMode.FixedEntry;
                setting.TwoStageContractVersion = 1;
                setting.UpdateStyle = UpdateStyle.Force;
                setting.BasePlayerId = "publisher-player";
                setting.Channel = "publisher-channel";
                setting.LogicMainDllName = "GameLogic.dll";
                setting.BootstrapAssemblyName = "GameUpdater.dll";
                setting.BootstrapTextAssetPath = "AssetRaw/Bootstrap/DLL";
                setting.AssemblyTextAssetPath = "AssetRaw/DLL";
                setting.BootstrapTag = "BOOTSTRAP";
                setting.AllowInsecureLoopbackHttp = false;
                setting.TwoStageReleaseDescriptorUrl =
                    "https://example.invalid/publisher/publisher-player/Android/publisher-channel/DefaultPackage/current.json";
                setting.TwoStageHostServerUrl =
                    "https://assets.example.invalid/publisher/publisher-player/Android/publisher-channel/DefaultPackage";
                setting.TwoStageFallbackHostServerUrl =
                    "https://fallback.example.invalid/publisher/publisher-player/Android/publisher-channel/DefaultPackage";
                setting.HotUpdateAssemblies = new List<string>
                {
                    "GameUpdater.dll",
                    "GameProto.dll",
                    "GameLogic.dll",
                };
                setting.AOTMetaAssemblies = new List<string>
                {
                    "mscorlib.dll",
                    "TEngine.Runtime.dll",
                };
            }

            public void Dispose()
            {
                _setting.EnableTwoStageUpdate = _enabled;
                _setting.TwoStageReleaseSourceMode = _sourceMode;
                _setting.TwoStageContractVersion = _contractVersion;
                _setting.BasePlayerId = _basePlayerId;
                _setting.Channel = _channel;
                _setting.TwoStageReleaseDescriptorUrl = _descriptorUrl;
                _setting.TwoStageHostServerUrl = _hostUrl;
                _setting.TwoStageFallbackHostServerUrl = _fallbackUrl;
                _setting.UpdateStyle = _updateStyle;
                _setting.LogicMainDllName = _logicMainDllName;
                _setting.BootstrapAssemblyName = _bootstrapAssemblyName;
                _setting.BootstrapTextAssetPath = _bootstrapTextAssetPath;
                _setting.AssemblyTextAssetPath = _assemblyTextAssetPath;
                _setting.BootstrapTag = _bootstrapTag;
                _setting.AllowInsecureLoopbackHttp = _allowInsecureLoopbackHttp;
                _setting.HotUpdateAssemblies = _hotUpdateAssemblies;
                _setting.AOTMetaAssemblies = _metadataAssemblies;
            }
        }
    }
}
