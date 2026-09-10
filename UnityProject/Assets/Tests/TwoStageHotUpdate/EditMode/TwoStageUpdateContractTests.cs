using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using YooAsset;

namespace TEngine.TwoStageHotUpdateTests
{
    public sealed class TwoStageUpdateContractTests
    {
        private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Test]
        public void ValidDescriptor_PassesExactContractValidation()
        {
            List<string> errors = UpdateReleaseDescriptorValidator.Validate(CreateDescriptor(), CreateExpected());
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void WrongBasePlayerAndTraversalAddress_AreRejected()
        {
            UpdateReleaseDescriptor descriptor = CreateDescriptor();
            descriptor.BasePlayerId = "wrong-player";
            descriptor.BootstrapAssembly.Address = "../GameUpdater.dll";

            List<string> errors = UpdateReleaseDescriptorValidator.Validate(descriptor, CreateExpected());

            Assert.IsTrue(errors.Exists(error => error.Contains("BasePlayerId")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(error => error.Contains("invalid path")), string.Join("\n", errors));
        }

        [Test]
        public void MissingBusinessAssembly_IsRejected()
        {
            UpdateReleaseDescriptor descriptor = CreateDescriptor();
            descriptor.BusinessAssemblies = new[] { descriptor.BusinessAssemblies[0] };

            List<string> errors = UpdateReleaseDescriptorValidator.Validate(descriptor, CreateExpected());

            Assert.IsTrue(errors.Exists(error => error.Contains("GameLogic.dll")), string.Join("\n", errors));
        }

        [Test]
        public void WrongContractPlatformAndMissingDigest_AreRejected()
        {
            UpdateReleaseDescriptor descriptor = CreateDescriptor();
            descriptor.ContractVersion = 2;
            descriptor.Platform = "Android";
            descriptor.ManifestSha256 = string.Empty;

            List<string> errors = UpdateReleaseDescriptorValidator.Validate(descriptor, CreateExpected());

            Assert.IsTrue(errors.Exists(error => error.Contains("ContractVersion")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(error => error.Contains("Platform")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(error => error.Contains("ManifestSha256")), string.Join("\n", errors));
        }

        [Test]
        public void WrongManifestId_IsRejectedAgainstYooAssetFileName()
        {
            UpdateReleaseDescriptor descriptor = CreateDescriptor();
            descriptor.ManifestId = "manifest.bytes";

            List<string> errors = UpdateReleaseDescriptorValidator.Validate(descriptor, CreateExpected());

            Assert.IsTrue(errors.Exists(error => error.Contains("ManifestId")), string.Join("\n", errors));
        }

        [TestCase("same-version different cache")]
        [TestCase("remote changed after descriptor check")]
        [TestCase("primary fallback content mismatch")]
        public void ActualManifestBytesMismatch_IsRejectedBeforeActivation(string scenario)
        {
            byte[] expected = { 1, 2, 3, 4 };
            var verifier = new PinnedManifestIntegrityVerifier(
                "DefaultPackage", "v1", UpdateReleaseDescriptorValidator.ComputeSha256(expected));
            IManifestRestoreServices restore = verifier;

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => restore.RestoreManifest(new byte[] { 9, 2, 3, 4 }), scenario);
            StringAssert.Contains("SHA-256 mismatch", exception.Message);
            Assert.Throws<InvalidOperationException>(() => verifier.VerifyActivated(
                "DefaultPackage", "v1", UpdateReleaseDescriptorValidator.ComputeSha256(expected)));
        }

        [Test]
        public void ExistingActiveManifestWithoutVerifiedBytes_IsRejected()
        {
            byte[] expected = { 1, 2, 3, 4 };
            string digest = UpdateReleaseDescriptorValidator.ComputeSha256(expected);
            var verifier = new PinnedManifestIntegrityVerifier("DefaultPackage", "v1", digest);

            Assert.Throws<InvalidOperationException>(
                () => verifier.VerifyActivated("DefaultPackage", "v1", digest));
        }

        [Test]
        public void MatchingActualManifestBytes_AuthorizeActivation()
        {
            byte[] expected = { 1, 2, 3, 4 };
            string digest = UpdateReleaseDescriptorValidator.ComputeSha256(expected);
            var verifier = new PinnedManifestIntegrityVerifier("DefaultPackage", "v1", digest);

            byte[] restored = ((IManifestRestoreServices)verifier).RestoreManifest(expected);

            Assert.AreSame(expected, restored);
            Assert.DoesNotThrow(() => verifier.VerifyActivated("DefaultPackage", "v1", digest));
        }

        [Test]
        public void DuplicateArtifactAddress_IsRejected()
        {
            UpdateReleaseDescriptor descriptor = CreateDescriptor();
            descriptor.BusinessAssemblies[0].Address = descriptor.BootstrapAssembly.Address;

            List<string> errors = UpdateReleaseDescriptorValidator.Validate(descriptor, CreateExpected());

            Assert.IsTrue(errors.Exists(error => error.Contains("Duplicate artifact address")), string.Join("\n", errors));
        }

        [Test]
        public void Sha256Comparison_DetectsCorruption()
        {
            byte[] bytes = { 1, 2, 3, 4 };
            string hash = UpdateReleaseDescriptorValidator.ComputeSha256(bytes);
            Assert.IsTrue(UpdateReleaseDescriptorValidator.HashMatches(bytes, hash));
            bytes[0] = 9;
            Assert.IsFalse(UpdateReleaseDescriptorValidator.HashMatches(bytes, hash));
        }

        [Test]
        public void UpdaterEntry_HasExactReflectionContract()
        {
            Assembly applicationAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "Assembly-CSharp")
                {
                    applicationAssembly = assembly;
                    break;
                }
            }

            Assert.IsNotNull(applicationAssembly);
            Type contract = applicationAssembly.GetType("Procedure.UpdateStageEntryContract", true);
            MethodInfo method = contract.GetMethod("TryFindRunAsync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object[] args = { typeof(GameUpdater.Entry), null, null };
            bool valid = (bool)method.Invoke(null, args);
            Assert.IsTrue(valid, args[2] as string);
            Assert.IsNotNull(args[1]);
        }

        [Test]
        public void Updater_RetriesSameSessionThenReturnsReady()
        {
            UpdateSessionContext context = CreateContext();
            FakeHost host = new FakeHost(
                new UpdateDownloadResult(UpdateDownloadStatus.Failed, "injected"),
                new UpdateDownloadResult(UpdateDownloadStatus.Succeeded, string.Empty));

            UpdateStageResult result = GameUpdater.Entry.RunAsync(context, host).GetAwaiter().GetResult();

            Assert.AreEqual(UpdateStageStatus.ResourcesReady, result.Status);
            Assert.AreEqual(2, host.DownloadCalls);
            Assert.AreEqual(1, host.RetryCalls);
            Assert.AreEqual(context.SessionId, result.SessionId);
            Assert.AreEqual(context.ReleaseId, result.ReleaseId);
        }

        [Test]
        public void Updater_SilentFailedTerminalStillReturnsFailedResult()
        {
            UpdateSessionContext context = CreateContext();
            FakeHost host = new FakeHost(new UpdateDownloadResult[] { null });

            UpdateStageResult result = GameUpdater.Entry.RunAsync(context, host).GetAwaiter().GetResult();

            Assert.AreEqual(UpdateStageStatus.Failed, result.Status);
            StringAssert.Contains("null download result", result.Error);
            Assert.AreEqual(1, host.DownloadCalls);
        }

        [Test]
        public void Updater_CancelDecisionDoesNotStartAnotherDownload()
        {
            UpdateSessionContext context = CreateContext();
            FakeHost host = new FakeHost(
                UpdateRetryDecision.Cancel,
                new UpdateDownloadResult(UpdateDownloadStatus.Failed, "injected"));

            UpdateStageResult result = GameUpdater.Entry.RunAsync(context, host).GetAwaiter().GetResult();

            Assert.AreEqual(UpdateStageStatus.Cancelled, result.Status);
            Assert.AreEqual(1, host.DownloadCalls);
            Assert.AreEqual(1, host.RetryCalls);
        }

        [Test]
        public void Updater_UnknownDownloadStatusIsRejected()
        {
            UpdateSessionContext context = CreateContext();
            FakeHost host = new FakeHost(
                new UpdateDownloadResult((UpdateDownloadStatus)999, "injected"));

            UpdateStageResult result = GameUpdater.Entry.RunAsync(context, host).GetAwaiter().GetResult();

            Assert.AreEqual(UpdateStageStatus.Failed, result.Status);
            StringAssert.Contains("Unknown download result", result.Error);
        }

        [Test]
        public void ReflectionContract_RejectsWrongSignature()
        {
            Assembly applicationAssembly = FindApplicationAssembly();
            Assert.IsNotNull(applicationAssembly);
            Type contract = applicationAssembly.GetType("Procedure.UpdateStageEntryContract", true);
            MethodInfo method = contract.GetMethod("TryFindRunAsync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object[] args = { typeof(BadUpdaterEntry), null, null };

            bool valid = (bool)method.Invoke(null, args);

            Assert.IsFalse(valid);
            StringAssert.Contains("exactly one", args[2] as string);
        }

        [Test]
        public void EntryCheckpoint_CorruptionIsRejected()
        {
            Assembly applicationAssembly = FindApplicationAssembly();
            Assert.IsNotNull(applicationAssembly);
            Type coordinator = applicationAssembly.GetType("Procedure.TwoStageUpdateCoordinator", true);
            Type checkpointType = coordinator.GetNestedType(
                "EntryCompletedCheckpoint",
                BindingFlags.NonPublic);
            MethodInfo create = checkpointType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            MethodInfo validate = coordinator.GetMethod(
                "TryValidateEntryCompletedCheckpointJson",
                BindingFlags.NonPublic | BindingFlags.Static);
            UpdateReleaseDescriptor release = CreateDescriptor();
            string json = JsonUtility.ToJson(create.Invoke(null, new object[] { release }), true);
            object[] validArgs = { json, release, null };
            Assert.IsTrue((bool)validate.Invoke(null, validArgs), validArgs[2] as string);

            string corrupted = json.Replace("release-1", "release-corrupted");
            object[] corruptArgs = { corrupted, release, null };
            Assert.IsFalse((bool)validate.Invoke(null, corruptArgs));
        }

        [Test]
        public void AtomicCheckpointWrite_FailureReturnsFalseAndCleansTemporaryFile()
        {
            Assembly applicationAssembly = FindApplicationAssembly();
            Assert.IsNotNull(applicationAssembly);
            Type coordinator = applicationAssembly.GetType("Procedure.TwoStageUpdateCoordinator", true);
            MethodInfo write = coordinator.GetMethod(
                "TryWriteTextAtomically",
                BindingFlags.NonPublic | BindingFlags.Static);
            string directoryTarget = Path.Combine(
                Path.GetTempPath(),
                "TEngine-TwoStage-WriteFailure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directoryTarget);
            try
            {
                object[] args = { directoryTarget, "checkpoint", null };
                Assert.IsFalse((bool)write.Invoke(null, args));
                Assert.IsEmpty(Directory.GetFiles(
                    Path.GetDirectoryName(directoryTarget),
                    Path.GetFileName(directoryTarget) + ".tmp-*"));
                Assert.IsNotEmpty(args[2] as string);
            }
            finally
            {
                Directory.Delete(directoryTarget, true);
                foreach (string temporary in Directory.GetFiles(
                             Path.GetDirectoryName(directoryTarget),
                             Path.GetFileName(directoryTarget) + ".tmp-*"))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static UpdateSessionContext CreateContext()
        {
            return new UpdateSessionContext(
                "session-1", 1, "player-1", "Windows64", "test", "DefaultPackage", "v1", "release-1",
                "manifest.bytes", Hash, CancellationToken.None);
        }

        private static Assembly FindApplicationAssembly()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "Assembly-CSharp")
                {
                    return assembly;
                }
            }

            return null;
        }

        private static UpdateReleaseValidationContext CreateExpected()
        {
            return new UpdateReleaseValidationContext
            {
                ContractVersion = 1,
                BasePlayerId = "player-1",
                Platform = "Windows64",
                Channel = "test",
                PackageName = "DefaultPackage",
                ManifestId = "PackageManifest_DefaultPackage_v1.bytes",
                BootstrapAssemblyName = "GameUpdater.dll",
                HotUpdateAssemblies = new[] { "GameUpdater.dll", "GameProto.dll", "GameLogic.dll" },
                MetadataAssemblies = new[] { "mscorlib.dll", "TEngine.Runtime.dll" },
            };
        }

        private static UpdateReleaseDescriptor CreateDescriptor()
        {
            return new UpdateReleaseDescriptor
            {
                ContractVersion = 1,
                ReleaseId = "release-1",
                BasePlayerId = "player-1",
                Platform = "Windows64",
                Channel = "test",
                PackageName = "DefaultPackage",
                PackageVersion = "v1",
                ManifestId = "PackageManifest_DefaultPackage_v1.bytes",
                ManifestSha256 = Hash,
                BootstrapAssembly = Artifact("GameUpdater.dll"),
                BusinessAssemblies = new[] { Artifact("GameProto.dll"), Artifact("GameLogic.dll") },
                MetadataAssemblies = new[] { Artifact("mscorlib.dll"), Artifact("TEngine.Runtime.dll") },
            };
        }

        private static UpdateArtifactDescriptor Artifact(string name)
        {
            return new UpdateArtifactDescriptor { Name = name, Address = name, Sha256 = Hash };
        }

        private sealed class FakeHost : IUpdateHost
        {
            private readonly Queue<UpdateDownloadResult> _results;
            private readonly UpdateRetryDecision _retryDecision;

            public FakeHost(params UpdateDownloadResult[] results)
                : this(UpdateRetryDecision.Retry, results)
            {
            }

            public FakeHost(UpdateRetryDecision retryDecision, params UpdateDownloadResult[] results)
            {
                _results = new Queue<UpdateDownloadResult>(results);
                _retryDecision = retryDecision;
            }

            public int DownloadCalls { get; private set; }
            public int RetryCalls { get; private set; }

            public bool IsSessionCurrent(UpdateSessionContext context)
            {
                return !context.CancellationToken.IsCancellationRequested;
            }

            public void ReportStatus(UpdateSessionContext context, string message)
            {
            }

            public UniTask<UpdateDownloadResult> DownloadRemainingResourcesAsync(UpdateSessionContext context)
            {
                DownloadCalls++;
                return UniTask.FromResult(_results.Dequeue());
            }

            public UniTask<UpdateRetryDecision> RequestRetryOrCancelAsync(UpdateSessionContext context, string error)
            {
                RetryCalls++;
                return UniTask.FromResult(_retryDecision);
            }
        }

        private static class BadUpdaterEntry
        {
            public static void RunAsync(UpdateSessionContext context, IUpdateHost host)
            {
            }
        }
    }
}
