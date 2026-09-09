#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GameLogic.UILifecycle.PlayModeTests
{
    public sealed class UGUIWindowLifecycleTests
    {
        private const int EventId = 93101;
        private static readonly FieldInfo CreationTimeoutField = typeof(UIModule).GetField(
            "CreationTimeoutSeconds", BindingFlags.Static | BindingFlags.NonPublic);

        private GameObject _root;
        private UIModule _module;
        private ControlledLoader _loader;

        [SetUp]
        public void SetUp()
        {
            Assert.That(UIModule.IsValid, Is.False,
                "UGUI lifecycle tests require an isolated Play Mode scene.");

            _root = new GameObject("UIRoot");
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(_root.transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            _module = UIModule.Instance;
            _loader = new ControlledLoader();
            UIModule.Resource = _loader;
            LifecycleWindow.Reset();
            SetCreationTimeout(60f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            SetCreationTimeout(60f);
            if (_module != null && UIModule.IsValid)
                _module.Release();

            UIModule.Resource = null;
            GameEvent.Shutdown();
            if (_root != null)
                Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShowAwait_MergesLoadingCallsAndReturnsReadyWindow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UniTask<LifecycleWindow> first = _module.ShowUIAsyncAwait<LifecycleWindow>("first");
                await UniTask.Yield();
                Assert.Throws<GameFrameworkException>(() => _module.ShowUI<LifecycleWindow>("sync-conflict"));
                UniTask<LifecycleWindow> second = _module.ShowUIAsyncAwait<LifecycleWindow>("latest");
                await UniTask.Yield();

                Assert.That(_loader.AsyncRequests.Count, Is.EqualTo(1));
                Assert.That(_module.IsAnyLoading(), Is.True);
                GameObject panel = _loader.Complete(0);

                LifecycleWindow firstWindow = await first;
                LifecycleWindow secondWindow = await second;
                Assert.That(secondWindow, Is.SameAs(firstWindow));
                Assert.That(LifecycleWindow.CreateCount, Is.EqualTo(1));
                Assert.That(LifecycleWindow.LastRefreshData, Is.EqualTo("latest"));
                Assert.That(_module.IsAnyLoading(), Is.False);
                Assert.That(firstWindow.IsPrepare, Is.True);
                Assert.That(firstWindow.Canvas, Is.Not.Null);
                Assert.That(firstWindow.Visible, Is.True);

                _module.HideUI<LifecycleWindow>();
                Assert.That(firstWindow.Visible, Is.False);
                _module.ShowUI<LifecycleWindow>("reshow");
                Assert.That(firstWindow.Visible, Is.True);
                Assert.That(firstWindow.HideTimerId, Is.Zero);

                _module.CloseUI<LifecycleWindow>();
                _module.CloseUI<LifecycleWindow>();
                await UniTask.Yield();
                Assert.That(_module.HasWindow<LifecycleWindow>(), Is.False);
                Assert.That(LifecycleWindow.DestroyCount, Is.EqualTo(1));
                Assert.That(panel == null, Is.True, "The owned panel must be destroyed after close.");
            });
        }

        [UnityTest]
        public IEnumerator LoadingCloseAndImmediateReopen_DropsLateOldPanel()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UniTask<LifecycleWindow> closing = _module.ShowUIAsyncAwait<LifecycleWindow>("old");
                await UniTask.Yield();
                Assert.That(_loader.AsyncRequests.Count, Is.EqualTo(1));

                _module.CloseUI<LifecycleWindow>();
                bool canceled = false;
                try { await closing; }
                catch (OperationCanceledException) { canceled = true; }
                Assert.That(canceled, Is.True);
                Assert.That(_module.HasWindow<LifecycleWindow>(), Is.False);

                GameObject oldPanel = _loader.Complete(0);
                await UniTask.Yield();
                Assert.That(oldPanel == null, Is.True, "A late result from the closed generation must be destroyed.");
                Assert.That(LifecycleWindow.CreateCount, Is.Zero);

                UniTask<LifecycleWindow> reopened = _module.ShowUIAsyncAwait<LifecycleWindow>("new");
                await UniTask.Yield();
                Assert.That(_loader.AsyncRequests.Count, Is.EqualTo(2));
                GameObject newPanel = _loader.Complete(1);
                LifecycleWindow window = await reopened;
                Assert.That(window, Is.Not.Null);
                Assert.That(LifecycleWindow.CreateCount, Is.EqualTo(1));

                _module.CloseUI<LifecycleWindow>();
                await UniTask.Yield();
                Assert.That(newPanel == null, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator GetAwaitCancellationDoesNotClose_AndGetCallbackNeverReceivesHalfReadyWindow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UniTask<LifecycleWindow> show = _module.ShowUIAsyncAwait<LifecycleWindow>("get");
                await UniTask.Yield();

                using var waitCts = new CancellationTokenSource();
                UniTask<LifecycleWindow> get = _module.GetUIAsyncAwait<LifecycleWindow>(waitCts.Token);
                waitCts.Cancel();
                bool getCanceled = false;
                try { await get; }
                catch (OperationCanceledException) { getCanceled = true; }
                Assert.That(getCanceled, Is.True);
                Assert.That(_module.HasWindow<LifecycleWindow>(), Is.True,
                    "Canceling a Get wait must not close the existing loading instance.");

                int callbackCount = 0;
                LifecycleWindow callbackWindow = null;
                _module.GetUIAsync<LifecycleWindow>(window =>
                {
                    callbackCount++;
                    callbackWindow = window;
                });
                Assert.That(callbackCount, Is.Zero);

                GameObject panel = _loader.Complete(0);
                LifecycleWindow shown = await show;
                await UniTask.Yield();
                Assert.That(callbackCount, Is.EqualTo(1));
                Assert.That(callbackWindow, Is.SameAs(shown));

                _module.CloseUI<LifecycleWindow>();
                _module.GetUIAsync<LifecycleWindow>(window =>
                {
                    callbackCount++;
                    callbackWindow = window;
                });
                Assert.That(callbackCount, Is.EqualTo(2));
                Assert.That(callbackWindow, Is.Null);
                await UniTask.Yield();
                Assert.That(panel == null, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator FailureTimeoutSelfCloseAndCleanupExceptions_DoNotLeakLifecycleResources()
        {
            return UniTask.ToCoroutine(async () =>
            {
                UniTask<LifecycleWindow> nullLoad = _module.ShowUIAsyncAwait<LifecycleWindow>("null");
                await UniTask.Yield();
                _loader.CompleteNull(0);
                bool nullFailed = false;
                try { await nullLoad; }
                catch (UIWindowLoadException) { nullFailed = true; }
                Assert.That(nullFailed, Is.True);
                Assert.That(_module.HasWindow<LifecycleWindow>(), Is.False);

                UniTask<LifecycleWindow> exceptionLoad = _module.ShowUIAsyncAwait<LifecycleWindow>("exception");
                await UniTask.Yield();
                _loader.Fail(1, new InvalidOperationException("Injected loader failure."));
                bool exceptionFailed = false;
                try { await exceptionLoad; }
                catch (UIWindowLoadException) { exceptionFailed = true; }
                Assert.That(exceptionFailed, Is.True);
                Assert.That(_module.HasWindow<LifecycleWindow>(), Is.False);

                LifecycleWindow.ThrowOnCreate = true;
                UniTask<LifecycleWindow> failed = _module.ShowUIAsyncAwait<LifecycleWindow>("failed");
                await UniTask.Yield();
                GameObject failedPanel = _loader.Complete(2);
                bool loadFailed = false;
                try { await failed; }
                catch (UIWindowLoadException) { loadFailed = true; }
                Assert.That(loadFailed, Is.True);
                Assert.That(_module.HasWindow<LifecycleWindow>(), Is.False);
                await UniTask.Yield();
                Assert.That(failedPanel == null, Is.True);
                LifecycleWindow.ThrowOnCreate = false;

                SetCreationTimeout(0.02f);
                float previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                try
                {
                    UniTask<LifecycleWindow> timedOut = _module.ShowUIAsyncAwait<LifecycleWindow>("timeout");
                    await UniTask.Yield();
                    bool timeoutRaised = false;
                    try { await timedOut; }
                    catch (UIWindowTimeoutException) { timeoutRaised = true; }
                    Assert.That(timeoutRaised, Is.True);
                    Assert.That(_module.HasWindow<LifecycleWindow>(), Is.False);
                    GameObject lateTimeoutPanel = _loader.Complete(3);
                    await UniTask.Yield();
                    Assert.That(lateTimeoutPanel == null, Is.True);
                }
                finally
                {
                    Time.timeScale = previousTimeScale;
                }
                SetCreationTimeout(60f);

                LifecycleWindow.CloseFromOnCreate = true;
                UniTask<LifecycleWindow> selfClosing = _module.ShowUIAsyncAwait<LifecycleWindow>("self-close");
                await UniTask.Yield();
                GameObject selfClosingPanel = _loader.Complete(4);
                bool selfCloseCanceled = false;
                try { await selfClosing; }
                catch (OperationCanceledException) { selfCloseCanceled = true; }
                Assert.That(selfCloseCanceled, Is.True);
                Assert.That(_module.HasWindow<LifecycleWindow>(), Is.False);
                await UniTask.Yield();
                Assert.That(selfClosingPanel == null, Is.True);
                LifecycleWindow.CloseFromOnCreate = false;

                LifecycleWindow.CreateCleanupWidgets = true;
                LifecycleWindow.ThrowWidgetOnDestroy = true;
                LifecycleWindow.ThrowOnDestroy = true;
                _module.ShowUI<LifecycleWindow>("cleanup");
                Assert.DoesNotThrow(() => _module.CloseUI<LifecycleWindow>());
                Assert.That(LifecycleWindow.ThrowingWidgetDestroyCount, Is.EqualTo(1));
                Assert.That(LifecycleWindow.CountingWidgetDestroyCount, Is.EqualTo(1));
                LifecycleWindow.ThrowOnDestroy = false;
                LifecycleWindow.ThrowWidgetOnDestroy = false;
                LifecycleWindow.CreateCleanupWidgets = false;

                LifecycleWindow.EventCalls = 0;
                _module.ShowUI<LifecycleWindow>("event-first");
                GameEvent.Send(EventId);
                Assert.That(LifecycleWindow.EventCalls, Is.EqualTo(1));
                _module.CloseUI<LifecycleWindow>();
                _module.ShowUI<LifecycleWindow>("event-reused");
                GameEvent.Send(EventId);
                Assert.That(LifecycleWindow.EventCalls, Is.EqualTo(2),
                    "A pooled event manager must not retain the previous window's listener.");
                _module.CloseUI<LifecycleWindow>();
                GameEvent.Send(EventId);
                Assert.That(LifecycleWindow.EventCalls, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator CancellationCallback_ReopensBeforeOldLoadCompletes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var oldWait = _module.ShowUIAsyncAwait<LifecycleWindow>("old");
                await UniTask.Yield();
                LifecycleWindow replacement = null;
                using var registration = _loader.AsyncRequests[0].Token.Register(() =>
                {
                    _module.ShowUI<LifecycleWindow>("replacement");
                    _module.GetUIAsync<LifecycleWindow>(window => replacement = window);
                });
                _module.CloseUI<LifecycleWindow>();
                Assert.That(replacement, Is.Not.Null);
                bool canceled = false;
                try { await oldWait; }
                catch (OperationCanceledException) { canceled = true; }
                Assert.That(canceled, Is.True);
                GameObject late = _loader.Complete(0);
                await UniTask.Yield();
                Assert.That(late == null, Is.True);
                Assert.That(await _module.GetUIAsyncAwait<LifecycleWindow>(), Is.SameAs(replacement));
                Assert.That(LifecycleWindow.CreateCount, Is.EqualTo(1));
                Assert.That(LifecycleWindow.LastRefreshData, Is.EqualTo("replacement"));
            });
        }

        [UnityTest]
        public IEnumerator WidgetCreation_CloseRejectsAllocationAndDestroysLateLoaderResult()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _module.ShowUI<LifecycleWindow>();
                var owner = await _module.GetUIAsyncAwait<LifecycleWindow>();
                var pending = owner.CreateWidgetByPathAsync<TestWidget>(_root.transform, "widget");
                _module.CloseUI<LifecycleWindow>();
                int syncLoads = _loader.SyncLoads;
                Assert.Throws<ObjectDisposedException>(() => owner.CreateWidgetByPath<TestWidget>(_root.transform, "closed"));
                Assert.Throws<ObjectDisposedException>(() => new TestWidget().CreateByPath("closed", owner));
                Assert.That(_loader.SyncLoads, Is.EqualTo(syncLoads));
                int children = _root.transform.childCount;
                Assert.Throws<ObjectDisposedException>(() => owner.CreateWidgetByPrefab<TestWidget>(_root, _root.transform));
                Assert.That(_root.transform.childCount, Is.EqualTo(children));
                GameObject late = _loader.Complete(0);
                bool rejected = false;
                try { await pending; }
                catch (ObjectDisposedException) { rejected = true; }
                Assert.That(rejected, Is.True);
                await UniTask.Yield();
                Assert.That(late == null, Is.True);

                _module.ShowUI<LifecycleWindow>();
                owner = await _module.GetUIAsyncAwait<LifecycleWindow>();
                _loader.OnSyncLoad = () => _module.CloseUI<LifecycleWindow>();
                Assert.Throws<ObjectDisposedException>(() => new TestWidget().CreateByPath("reentrant", owner, _root.transform));
                GameObject abandoned = _loader.LastSyncInstance;
                await UniTask.Yield();
                Assert.That(abandoned == null, Is.True);
            });
        }

        private sealed class TestWidget : UIWidget { public TestWidget() { } }

        private static void SetCreationTimeout(float seconds)
        {
            Assert.That(CreationTimeoutField, Is.Not.Null);
            CreationTimeoutField.SetValue(null, seconds);
        }

        [Window(UILayer.UI, "UGUI.Lifecycle.Test")]
        private sealed class LifecycleWindow : UIWindow
        {
            public LifecycleWindow() { }

            public static int CreateCount { get; private set; }
            public static int DestroyCount { get; private set; }
            public static int EventCalls { get; set; }
            public static int ThrowingWidgetDestroyCount { get; private set; }
            public static int CountingWidgetDestroyCount { get; private set; }
            public static object LastRefreshData { get; private set; }
            public static bool ThrowOnCreate { get; set; }
            public static bool ThrowOnDestroy { get; set; }
            public static bool CloseFromOnCreate { get; set; }
            public static bool CreateCleanupWidgets { get; set; }
            public static bool ThrowWidgetOnDestroy { get; set; }

            public static void Reset()
            {
                CreateCount = 0;
                DestroyCount = 0;
                EventCalls = 0;
                ThrowingWidgetDestroyCount = 0;
                CountingWidgetDestroyCount = 0;
                LastRefreshData = null;
                ThrowOnCreate = false;
                ThrowOnDestroy = false;
                CloseFromOnCreate = false;
                CreateCleanupWidgets = false;
                ThrowWidgetOnDestroy = false;
            }

            protected override void OnCreate()
            {
                CreateCount++;
                AddUIEvent(EventId, HandleEvent);
                if (CloseFromOnCreate)
                    UIModule.Instance.CloseUI<LifecycleWindow>();
                if (ThrowOnCreate)
                    throw new InvalidOperationException("Injected UGUI create failure.");

                if (CreateCleanupWidgets)
                {
                    var throwingRoot = new GameObject("ThrowingWidget", typeof(RectTransform));
                    throwingRoot.transform.SetParent(rectTransform, false);
                    CreateWidget<ThrowingWidget>(throwingRoot);

                    var countingRoot = new GameObject("CountingWidget", typeof(RectTransform));
                    countingRoot.transform.SetParent(rectTransform, false);
                    CreateWidget<CountingWidget>(countingRoot);
                }
            }

            protected override void OnRefresh()
            {
                LastRefreshData = UserData;
            }

            protected override void OnDestroy()
            {
                DestroyCount++;
                if (ThrowOnDestroy)
                    throw new InvalidOperationException("Injected UGUI destroy failure.");
            }

            private static void HandleEvent()
            {
                EventCalls++;
            }

            private sealed class ThrowingWidget : UIWidget
            {
                public ThrowingWidget() { }

                protected override void OnDestroy()
                {
                    ThrowingWidgetDestroyCount++;
                    if (ThrowWidgetOnDestroy)
                        throw new InvalidOperationException("Injected widget destroy failure.");
                }
            }

            private sealed class CountingWidget : UIWidget
            {
                public CountingWidget() { }

                protected override void OnDestroy()
                {
                    CountingWidgetDestroyCount++;
                }
            }
        }

        private sealed class ControlledLoader : IUIResourceLoader
        {
            internal sealed class AsyncRequest
            {
                public string Location;
                public Transform Parent;
                public CancellationToken Token;
                public readonly UniTaskCompletionSource<GameObject> Completion =
                    new UniTaskCompletionSource<GameObject>();
            }

            public readonly List<AsyncRequest> AsyncRequests = new List<AsyncRequest>();
            public int SyncLoads;
            public Action OnSyncLoad;
            public GameObject LastSyncInstance;

            public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
            {
                SyncLoads++;
                LastSyncInstance = CreatePanel(parent);
                OnSyncLoad?.Invoke();
                return LastSyncInstance;
            }

            public UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null,
                CancellationToken cancellationToken = default, string packageName = "")
            {
                var request = new AsyncRequest { Location = location, Parent = parent, Token = cancellationToken };
                AsyncRequests.Add(request);
                return request.Completion.Task;
            }

            public GameObject Complete(int index)
            {
                AsyncRequest request = AsyncRequests[index];
                GameObject panel = CreatePanel(request.Parent);
                request.Completion.TrySetResult(panel);
                return panel;
            }

            public void CompleteNull(int index)
            {
                AsyncRequests[index].Completion.TrySetResult(null);
            }

            public void Fail(int index, Exception exception)
            {
                AsyncRequests[index].Completion.TrySetException(exception);
            }

            private static GameObject CreatePanel(Transform parent)
            {
                var panel = new GameObject("UGUI.Test.Panel", typeof(RectTransform), typeof(Canvas),
                    typeof(UnityEngine.UI.GraphicRaycaster));
                panel.transform.SetParent(parent, false);
                panel.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                return panel;
            }
        }
    }
}
#endif
