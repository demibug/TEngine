// AssetDatabase supplies real published packages for Editor Play Mode.
// This is not a replacement for the real YooAsset AssetBundle/Player smoke test.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine.FairyGUIIntegration;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameLogic.FairyGUI.PlayModeTests
{
    public sealed class FguiWindowModuleTests
    {
        [UnityTest]
        public IEnumerator WindowModule_MergesConcurrentShow_AndClosePreventsLateWindow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FguiSettings settings = AssetDatabase.LoadAssetAtPath<FguiSettings>(
                    "Assets/AssetRaw/FGUI/FguiSettings.asset");
                var provider = new AssetDatabaseProvider(delayFrames: 2);
                Assert.That(Application.isPlaying, Is.True);
                Assert.That(FguiModule.IsValid, Is.False,
                    "Run in the Test Runner's isolated Play Mode scene.");
                var module = FguiModule.Instance;

                try
                {
                    await module.InitializeAsync(settings, resourceProvider: provider);
                    TestWindow.Reset();
                    if (!module.IsRegistered<TestWindow>())
                    {
                        module.Register<TestWindow>(FguiWindowDescriptor.Create(() => new TestWindow(),
                            "BundleUsage", "BundleUsage", "Main", FguiLayer.UI));
                    }

                    using var firstCts = new CancellationTokenSource();
                    UniTask<TestWindow> canceledWait = module.ShowAsync<TestWindow>("first", firstCts.Token);
                    UniTask<TestWindow> survivingWait = module.ShowAsync<TestWindow>("second");
                    await UniTask.Yield();
                    firstCts.Cancel();
                    bool firstCanceled = false;
                    try { await canceledWait; }
                    catch (OperationCanceledException) { firstCanceled = true; }
                    Assert.That(firstCanceled, Is.True);

                    TestWindow window = await survivingWait;
                    Assert.That(window.View.GetChild("n1").asTextField.text, Is.Not.Empty,
                        "The real package text must construct using a live runtime font.");
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                    Assert.That(window, Is.SameAs(await module.ShowAsync<TestWindow>("refresh")));
                    Assert.That(TestWindow.CreateCount, Is.EqualTo(1));
                    Assert.That(TestWindow.LastRefreshData, Is.EqualTo("refresh"));

                    TestWindow.ThrowOnDestroy = true;
                    Assert.DoesNotThrow(() => module.Close<TestWindow>());
                    TestWindow.ThrowOnDestroy = false;
                    Assert.That(module.IsOpen<TestWindow>(), Is.False);
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);

                    TestWindow.ThrowOnCreate = true;
                    bool hookFailed = false;
                    try { await module.ShowAsync<TestWindow>("bad-hook"); }
                    catch (FguiLoadException exception)
                    {
                        hookFailed = exception.Stage == "window-create";
                    }
                    finally { TestWindow.ThrowOnCreate = false; }
                    Assert.That(hookFailed, Is.True);
                    Assert.That(module.IsOpen<TestWindow>(), Is.False);
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);

                    UniTask<TestWindow> closingLoad = module.ShowAsync<TestWindow>("closing");
                    await UniTask.Yield();
                    module.Close<TestWindow>();
                    UniTask<TestWindow> retryLoad = module.ShowAsync<TestWindow>("retry");
                    bool closeCanceled = false;
                    try { await closingLoad; }
                    catch (OperationCanceledException) { closeCanceled = true; }
                    Assert.That(closeCanceled, Is.True);
                    Assert.That(module.IsOpen<TestWindow>(), Is.False);

                    TestWindow retry = await retryLoad;
                    Assert.That(retry, Is.Not.Null);
                    module.Close<TestWindow>();
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                }
                finally
                {
                    module.Release();
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                }
            });
        }

        private sealed class TestWindow : FguiWindow
        {
            public static int CreateCount { get; private set; }
            public static object LastRefreshData { get; private set; }
            public static bool ThrowOnCreate { get; set; }
            public static bool ThrowOnDestroy { get; set; }

            public static void Reset()
            {
                CreateCount = 0;
                LastRefreshData = null;
                ThrowOnCreate = false;
                ThrowOnDestroy = false;
            }

            protected override UniTask OnCreateAsync(object userData, CancellationToken cancellationToken)
            {
                CreateCount++;
                if (ThrowOnCreate)
                    throw new InvalidOperationException("Injected window create failure.");
                return UniTask.CompletedTask;
            }

            protected override UniTask OnRefreshAsync(object userData, CancellationToken cancellationToken)
            {
                LastRefreshData = userData;
                return UniTask.CompletedTask;
            }

            protected override void OnDestroy()
            {
                if (ThrowOnDestroy)
                    throw new InvalidOperationException("Injected window destroy failure.");
            }
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

    }
}
#endif
