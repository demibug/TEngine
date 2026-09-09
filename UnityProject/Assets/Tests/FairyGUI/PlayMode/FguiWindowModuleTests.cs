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

                    TestWindow.ThrowOnVisible = true;
                    bool visibleHookFailed = false;
                    try { await module.ShowAsync<TestWindow>("bad-visible-hook"); }
                    catch (FguiLoadException exception)
                    {
                        visibleHookFailed = exception.Stage == "window-create";
                    }
                    finally { TestWindow.ThrowOnVisible = false; }
                    Assert.That(visibleHookFailed, Is.True);
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

        [UnityTest]
        public IEnumerator WindowModule_SerializesExistingRefresh_AndCloseCancelsStaleRefresh()
        {
            return UniTask.ToCoroutine(async () =>
            {
                FguiSettings settings = AssetDatabase.LoadAssetAtPath<FguiSettings>(
                    "Assets/AssetRaw/FGUI/FguiSettings.asset");
                var provider = new AssetDatabaseProvider(delayFrames: 1);
                Assert.That(Application.isPlaying, Is.True);
                Assert.That(FguiModule.IsValid, Is.False,
                    "Run in the Test Runner's isolated Play Mode scene.");
                var module = FguiModule.Instance;

                try
                {
                    await module.InitializeAsync(settings, resourceProvider: provider);
                    RefreshRaceWindow.Reset();
                    if (!module.IsRegistered<RefreshRaceWindow>())
                    {
                        module.Register<RefreshRaceWindow>(FguiWindowDescriptor.Create(() => new RefreshRaceWindow(),
                            "BundleUsage", "BundleUsage", "Main", FguiLayer.UI));
                    }

                    RefreshRaceWindow window = await module.ShowAsync<RefreshRaceWindow>("create");
                    RefreshRaceWindow.BlockNextRefresh();
                    UniTask<RefreshRaceWindow> firstRefresh = module.ShowAsync<RefreshRaceWindow>("first");
                    await UniTask.Yield();
                    UniTask<RefreshRaceWindow> secondRefresh = module.ShowAsync<RefreshRaceWindow>("second");
                    await UniTask.Yield();
                    Assert.That(RefreshRaceWindow.ActiveRefreshCount, Is.EqualTo(1));
                    Assert.That(RefreshRaceWindow.MaxConcurrentRefreshCount, Is.EqualTo(1));

                    using var queuedCts = new CancellationTokenSource();
                    UniTask<RefreshRaceWindow> canceledQueued = module.ShowAsync<RefreshRaceWindow>("canceled-queued", queuedCts.Token);
                    queuedCts.Cancel();
                    RefreshRaceWindow.ReleaseBlockedRefresh();
                    Assert.That(await firstRefresh, Is.SameAs(window));
                    Assert.That(await secondRefresh, Is.SameAs(window));
                    bool queueCanceled = false;
                    try { await canceledQueued; }
                    catch (OperationCanceledException) { queueCanceled = true; }
                    Assert.That(queueCanceled, Is.True);
                    Assert.That(RefreshRaceWindow.LastRefreshData, Is.EqualTo("second"));
                    Assert.That(RefreshRaceWindow.MaxConcurrentRefreshCount, Is.EqualTo(1));

                    RefreshRaceWindow.BlockNextRefresh();
                    UniTask<RefreshRaceWindow> closingRefresh = module.ShowAsync<RefreshRaceWindow>("closing");
                    await UniTask.Yield();
                    module.Close<RefreshRaceWindow>();
                    RefreshRaceWindow.ReleaseBlockedRefresh();
                    bool canceled = false;
                    try { await closingRefresh; }
                    catch (OperationCanceledException) { canceled = true; }
                    Assert.That(canceled, Is.True);
                    Assert.That(module.IsOpen<RefreshRaceWindow>(), Is.False);
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                }
                finally
                {
                    module.Release();
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                }
            });
        }

        [UnityTest]
        public IEnumerator CanceledHookEntry_SkipsHooksAndReleasesOwnership()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var settings = AssetDatabase.LoadAssetAtPath<FguiSettings>("Assets/AssetRaw/FGUI/FguiSettings.asset");
                var provider = new AssetDatabaseProvider();
                var module = FguiModule.Instance;
                try
                {
                    await module.InitializeAsync(settings, resourceProvider: provider);
                    TestWindow.Reset();
                    using var creationCts = new CancellationTokenSource();
                    TestWindow canceledWindow = null;
                    module.Register<TestWindow>(FguiWindowDescriptor.Create(() =>
                    {
                        canceledWindow = new TestWindow();
                        creationCts.Cancel();
                        module.Close<TestWindow>();
                        return canceledWindow;
                    }, "BundleUsage", "BundleUsage", "Main", FguiLayer.UI));
                    bool canceled = false;
                    try { await module.ShowAsync<TestWindow>("canceled-create", creationCts.Token); }
                    catch (OperationCanceledException) { canceled = true; }
                    Assert.That(canceled, Is.True);
                    Assert.That(TestWindow.CreateCount, Is.Zero);
                    Assert.That(TestWindow.LastRefreshData, Is.Null);
                    Assert.That(canceledWindow.View, Is.Null);
                    Assert.That(canceledWindow.Lifetime, Is.Null);
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                }
                finally { module.Release(); }

                module = FguiModule.Instance;
                try
                {
                    await module.InitializeAsync(settings, resourceProvider: provider);
                    TestWindow.Reset();
                    module.Register<TestWindow>(FguiWindowDescriptor.Create(() => new TestWindow(),
                        "BundleUsage", "BundleUsage", "Main", FguiLayer.UI));
                    var window = await module.ShowAsync<TestWindow>("initial");
                    var lifetimeToken = window.Lifetime.Token;
                    using var callerCts = new CancellationTokenSource();
                    callerCts.Cancel();
                    // Invoke the entry boundary directly: cancellation has occurred after
                    // the queue's precheck, but before the user hook is entered.
                    var refreshEntry = typeof(FguiWindow).GetMethod("InternalRefreshAsync",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    bool canceled = false;
                    try { await (UniTask)refreshEntry.Invoke(window, new object[] { "canceled", callerCts.Token }); }
                    catch (OperationCanceledException) { canceled = true; }
                    Assert.That(canceled, Is.True);
                    Assert.That(TestWindow.LastRefreshData, Is.EqualTo("initial"));
                    var lifetimeCts = (CancellationTokenSource)typeof(FguiLifetimeScope).GetField("_cancellation",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(window.Lifetime);
                    lifetimeCts.Cancel();
                    canceled = false;
                    try { await (UniTask)refreshEntry.Invoke(window, new object[] { "lifetime-canceled", CancellationToken.None }); }
                    catch (OperationCanceledException) { canceled = true; }
                    Assert.That(canceled, Is.True);
                    Assert.That(TestWindow.LastRefreshData, Is.EqualTo("initial"));
                    module.Close<TestWindow>();
                    Assert.That(lifetimeToken.IsCancellationRequested, Is.True);
                    Assert.That(window.View, Is.Null);
                    Assert.That(window.Lifetime, Is.Null);
                    Assert.That(provider.ActiveLeaseCount, Is.Zero);
                }
                finally { module.Release(); }
            });
        }

        private sealed class TestWindow : FguiWindow
        {
            public static int CreateCount { get; private set; }
            public static object LastRefreshData { get; private set; }
            public static bool ThrowOnCreate { get; set; }
            public static bool ThrowOnDestroy { get; set; }
            public static bool ThrowOnVisible { get; set; }

            public static void Reset()
            {
                CreateCount = 0;
                LastRefreshData = null;
                ThrowOnCreate = false;
                ThrowOnDestroy = false;
                ThrowOnVisible = false;
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

            protected override void OnSetVisible(bool visible)
            {
                if (visible && ThrowOnVisible)
                    throw new InvalidOperationException("Injected window visibility failure.");
            }
        }

        private sealed class RefreshRaceWindow : FguiWindow
        {
            private static UniTaskCompletionSource _blockedRefresh;
            private static bool _blockNext;

            public static int ActiveRefreshCount { get; private set; }
            public static int MaxConcurrentRefreshCount { get; private set; }
            public static object LastRefreshData { get; private set; }

            public static void Reset()
            {
                _blockedRefresh = null;
                _blockNext = false;
                ActiveRefreshCount = 0;
                MaxConcurrentRefreshCount = 0;
                LastRefreshData = null;
            }

            public static void BlockNextRefresh()
            {
                _blockNext = true;
                _blockedRefresh = new UniTaskCompletionSource();
            }

            public static void ReleaseBlockedRefresh()
            {
                _blockedRefresh?.TrySetResult();
            }

            protected override async UniTask OnRefreshAsync(object userData, CancellationToken cancellationToken)
            {
                ActiveRefreshCount++;
                MaxConcurrentRefreshCount = Math.Max(MaxConcurrentRefreshCount, ActiveRefreshCount);
                try
                {
                    if (_blockNext)
                    {
                        _blockNext = false;
                        await _blockedRefresh.Task.AttachExternalCancellation(cancellationToken);
                    }

                    LastRefreshData = userData;
                }
                finally
                {
                    ActiveRefreshCount--;
                }
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
