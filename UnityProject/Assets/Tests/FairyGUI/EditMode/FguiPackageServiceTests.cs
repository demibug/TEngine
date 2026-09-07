using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using NUnit.Framework;
using TEngine.FairyGUIIntegration;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameLogic.FairyGUI.Tests
{
    public sealed class FguiPackageServiceTests
    {
        private const string CatalogPath = "Assets/AssetRaw/FGUI/FguiPackageCatalog.asset";

        [Test]
        public void Catalog_IsCompleteAndAcyclic()
        {
            FguiPackageCatalog catalog = AssetDatabase.LoadAssetAtPath<FguiPackageCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.ValidateCatalog(), Is.Empty);
            Assert.That(catalog.Packages.Count, Is.EqualTo(3));
        }

        [Test]
        public void Catalog_RejectsCyclesDuplicateIdentityAndDuplicateExternalKeys()
        {
            FguiPackageCatalog catalog = ScriptableObject.CreateInstance<FguiPackageCatalog>();
            try
            {
                JsonUtility.FromJsonOverwrite(
                    "{\"packages\":[" +
                    "{\"key\":\"A\",\"packageId\":\"same\",\"packageName\":\"Same\",\"yooAssetPackageName\":\"DefaultPackage\",\"descriptionAddress\":\"a.bytes\",\"assetNamePrefix\":\"A\",\"dependencies\":[\"B\"],\"assets\":[]}," +
                    "{\"key\":\"B\",\"packageId\":\"same\",\"packageName\":\"Same\",\"yooAssetPackageName\":\"DefaultPackage\",\"descriptionAddress\":\"b.bytes\",\"assetNamePrefix\":\"B\",\"dependencies\":[\"A\"],\"assets\":[]}]," +
                    "\"externalAssets\":[" +
                    "{\"key\":\"icon\",\"address\":\"a.png\",\"yooAssetPackageName\":\"DefaultPackage\",\"kind\":1}," +
                    "{\"key\":\"icon\",\"address\":\"b.png\",\"yooAssetPackageName\":\"DefaultPackage\",\"kind\":1}]}",
                    catalog);

                string errors = string.Join("\n", catalog.ValidateCatalog());
                StringAssert.Contains("duplicate FairyGUI id", errors);
                StringAssert.Contains("duplicate FairyGUI name", errors);
                StringAssert.Contains("dependency cycle", errors);
                StringAssert.Contains("Duplicate external FairyGUI asset key", errors);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        [UnityTest]
        public IEnumerator SharedDependency_IsReleasedOnlyAfterBothOwnersClose()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FguiPackageCatalog catalog = AssetDatabase.LoadAssetAtPath<FguiPackageCatalog>(CatalogPath);
                var provider = new AssetDatabaseProvider();
                var service = new FguiPackageService(catalog, provider, 5);
                FguiPackageLease first = null;
                FguiPackageLease second = null;
                try
                {
                    first = await service.AcquireAsync("BundleUsage", CancellationToken.None);
                    second = await service.AcquireAsync("ModalWaiting", CancellationToken.None);
                    Assert.That(service.GetReferenceCount("Common"), Is.EqualTo(2));
                    Assert.That(UIPackage.GetByName("Bag"), Is.Not.Null);

                    first.Dispose();
                    first = null;
                    Assert.That(service.GetReferenceCount("Common"), Is.EqualTo(1));
                    Assert.That(UIPackage.GetByName("Bag"), Is.Not.Null);

                    second.Dispose();
                    second = null;
                    Assert.That(service.GetReferenceCount("Common"), Is.Zero);
                    Assert.That(UIPackage.GetByName("Bag"), Is.Null);
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                }
                finally
                {
                    first?.Dispose();
                    second?.Dispose();
                    service.Shutdown();
                }
            });
        }

        [UnityTest]
        public IEnumerator SdkGlobalCleanup_BeforeLeaseRelease_IsIdempotent()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FguiPackageCatalog catalog = AssetDatabase.LoadAssetAtPath<FguiPackageCatalog>(CatalogPath);
                var provider = new AssetDatabaseProvider();
                var service = new FguiPackageService(catalog, provider, 5);
                FguiPackageLease lease = null;
                try
                {
                    lease = await service.AcquireAsync("BundleUsage", CancellationToken.None);
                    Assert.That(provider.ActiveLeaseCount, Is.GreaterThan(0));

                    UIPackage.RemoveAllPackages();
                    Assert.DoesNotThrow(() => lease.Dispose());
                    lease = null;

                    Assert.That(service.GetReferenceCount("BundleUsage"), Is.Zero);
                    Assert.That(service.GetReferenceCount("Common"), Is.Zero);
                    Assert.That(provider.ActiveLeaseCount, Is.Zero,
                        "SDK package cleanup must not skip releasing YooAsset-backed leases.");
                }
                finally
                {
                    lease?.Dispose();
                    service.Shutdown();
                }
            });
        }

        [UnityTest]
        public IEnumerator FailedLoad_RollsBackAndCanRetry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FguiPackageCatalog catalog = AssetDatabase.LoadAssetAtPath<FguiPackageCatalog>(CatalogPath);
                var provider = new AssetDatabaseProvider("FGUI/Packages/BundleUsage/BundleUsage_atlas0.png");
                var service = new FguiPackageService(catalog, provider, 5);
                try
                {
                    bool failed = false;
                    try
                    {
                        await service.AcquireAsync("BundleUsage", CancellationToken.None);
                    }
                    catch (FguiLoadException)
                    {
                        failed = true;
                    }
                    Assert.That(failed, Is.True);
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                    Assert.That(UIPackage.GetByName("BundleUsage"), Is.Null);

                    using FguiPackageLease retry = await service.AcquireAsync("BundleUsage", CancellationToken.None);
                    Assert.That(retry.Package.name, Is.EqualTo("BundleUsage"));
                }
                finally
                {
                    service.Shutdown();
                }
            });
        }

        [UnityTest]
        public IEnumerator CancelingOneWaiter_DoesNotCancelSharedLoad()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FguiPackageCatalog catalog = AssetDatabase.LoadAssetAtPath<FguiPackageCatalog>(CatalogPath);
                var provider = new AssetDatabaseProvider(delayFrames: 2);
                var service = new FguiPackageService(catalog, provider, 5);
                using var firstCts = new CancellationTokenSource();
                try
                {
                    UniTask<FguiPackageLease> firstTask = service.AcquireAsync("BundleUsage", firstCts.Token);
                    UniTask<FguiPackageLease> secondTask = service.AcquireAsync("BundleUsage", CancellationToken.None);
                    await UniTask.Yield();
                    firstCts.Cancel();

                    bool canceled = false;
                    try { await firstTask; }
                    catch (OperationCanceledException) { canceled = true; }
                    Assert.That(canceled, Is.True);

                    using FguiPackageLease second = await secondTask;
                    Assert.That(second.Package.name, Is.EqualTo("BundleUsage"));
                    Assert.That(service.GetReferenceCount("BundleUsage"), Is.EqualTo(1));
                }
                finally
                {
                    service.Shutdown();
                }
            });
        }


        [UnityTest]
        public IEnumerator ExternalLoader_RejectsLateReplacement_AndReleasesAfterDispose()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FguiPackageCatalog catalog = ScriptableObject.CreateInstance<FguiPackageCatalog>();
                JsonUtility.FromJsonOverwrite(
                    "{\"packages\":[],\"externalAssets\":[" +
                    "{\"key\":\"icon-a\",\"address\":\"a\",\"yooAssetPackageName\":\"DefaultPackage\",\"kind\":1}," +
                    "{\"key\":\"icon-b\",\"address\":\"b\",\"yooAssetPackageName\":\"DefaultPackage\",\"kind\":1}]}",
                    catalog);
                var provider = new DelayedTextureProvider();
                FguiExternalLoader loader = null;
                FguiExternalLoader disposedWhileLoading = null;
                try
                {
                    FguiExternalLoader.Configure(provider, catalog);
                    loader = new FguiExternalLoader { url = "asset://icon-a" };
                    await UniTask.Yield();
                    loader.url = "asset://icon-b";
                    await UniTask.DelayFrame(6);

                    Assert.That(loader.texture, Is.Not.Null);
                    Assert.That(loader.texture.nativeTexture, Is.SameAs(provider.TextureB));
                    Assert.That(provider.Releases["a"], Is.EqualTo(1),
                        "The late A result must be released instead of replacing B.");
                    Assert.That(provider.ActiveLeaseCount, Is.EqualTo(1));

                    loader.Dispose();
                    loader = null;
                    Assert.That(provider.Releases["b"], Is.EqualTo(1));
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);

                    disposedWhileLoading = new FguiExternalLoader { url = "asset://icon-a" };
                    disposedWhileLoading.Dispose();
                    disposedWhileLoading = null;
                    await UniTask.DelayFrame(6);
                    Assert.That(provider.Releases["a"], Is.EqualTo(2),
                        "A result arriving after loader disposal must be released exactly once.");
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                }
                finally
                {
                    loader?.Dispose();
                    disposedWhileLoading?.Dispose();
                    FguiExternalLoader.ResetConfiguration();
                    UnityEngine.Object.DestroyImmediate(provider.TextureA);
                    UnityEngine.Object.DestroyImmediate(provider.TextureB);
                    UnityEngine.Object.DestroyImmediate(catalog);
                }
            });
        }


        private sealed class AssetDatabaseProvider : IFguiResourceProvider
        {
            private readonly string _failOnceAddress;
            private readonly int _delayFrames;
            private bool _failed;
            private int _active;

            public AssetDatabaseProvider(string failOnceAddress = null, int delayFrames = 0)
            {
                _failOnceAddress = failOnceAddress;
                _delayFrames = delayFrames;
            }

            public int ActiveLeaseCount => _active;

            public async UniTask<FguiAssetLease> LoadAsync(string address, Type assetType,
                string yooAssetPackageName, CancellationToken cancellationToken)
            {
                for (int i = 0; i < _delayFrames; i++)
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                if (!_failed && address == _failOnceAddress)
                {
                    _failed = true;
                    throw new FguiLoadException("fake-provider", null, address, "Injected first-attempt failure.");
                }

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath("Assets/AssetRaw/" + address, assetType);
                if (asset == null)
                    throw new FguiLoadException("fake-provider", null, address, "AssetDatabase lookup failed.");
                Interlocked.Increment(ref _active);
                return new FguiAssetLease(asset, () => Interlocked.Decrement(ref _active));
            }
        }

        private sealed class DelayedTextureProvider : IFguiResourceProvider
        {
            private int _active;

            public DelayedTextureProvider()
            {
                TextureA = new Texture2D(2, 2) { name = "late-a" };
                TextureB = new Texture2D(2, 2) { name = "current-b" };
                Releases = new Dictionary<string, int>
                {
                    ["a"] = 0,
                    ["b"] = 0
                };
            }

            public Texture2D TextureA { get; }
            public Texture2D TextureB { get; }
            public Dictionary<string, int> Releases { get; }
            public int ActiveLeaseCount => _active;

            public async UniTask<FguiAssetLease> LoadAsync(string address, Type assetType,
                string yooAssetPackageName, CancellationToken cancellationToken)
            {
                int frames = address == "a" ? 4 : 1;
                for (int i = 0; i < frames; i++)
                    await UniTask.Yield(); // Deliberately ignore cancellation to model unavoidable late IO completion.

                Texture2D texture = address == "a" ? TextureA : TextureB;
                Interlocked.Increment(ref _active);
                return new FguiAssetLease(texture, () =>
                {
                    Releases[address]++;
                    Interlocked.Decrement(ref _active);
                });
            }
        }
    }
}
