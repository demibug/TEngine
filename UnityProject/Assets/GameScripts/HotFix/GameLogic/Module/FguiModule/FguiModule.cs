using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using TEngine;
using TEngine.FairyGUIIntegration;
using UnityEngine;

namespace GameLogic
{
    public sealed class FguiModule : Singleton<FguiModule>
    {
        private sealed class Creation
        {
            public FguiWindowDescriptor Descriptor;
            public object FirstUserData;
            public CancellationTokenSource Cts;
            public UniTaskCompletionSource<FguiWindow> Completion;
            public int WaiterCount;
        }

        private readonly Dictionary<Type, FguiWindowDescriptor> _descriptors =
            new Dictionary<Type, FguiWindowDescriptor>();
        private readonly Dictionary<Type, FguiWindow> _windows = new Dictionary<Type, FguiWindow>();
        private readonly Dictionary<Type, Creation> _creations = new Dictionary<Type, Creation>();

        private FguiSettings _settings;
        private FguiAssetLease _settingsLease;
        private IFguiResourceProvider _resourceProvider;
        private FguiPackageService _packages;
        private Func<FguiSettings, Action, IFguiRuntimeHost> _hostFactory =
            (settings, shutdownAction) => FguiRuntimeHost.Create(settings, shutdownAction);
        private IFguiRuntimeHost _host;
        private bool _externalLoaderConfigured;
        private bool _initialized;
        private bool _shutdown;

        public bool IsInitialized => _initialized && !_shutdown;
        public int ActiveResourceLeaseCount => _packages?.ActiveResourceLeaseCount ?? 0;

        public async UniTask InitializeAsync(string settingsAddress,
            CancellationToken cancellationToken = default, string yooAssetPackageName = "DefaultPackage")
        {
            EnsureCanStartWork();
            if (string.IsNullOrWhiteSpace(settingsAddress))
                throw new ArgumentException("Settings address cannot be empty.", nameof(settingsAddress));
            if (_initialized)
                return;

            _resourceProvider = new FguiResourceProvider(GameModule.Resource);
            FguiAssetLease lease = await _resourceProvider.LoadAsync(settingsAddress, typeof(FguiSettings),
                yooAssetPackageName, cancellationToken);
            try
            {
                await InitializeAsync((FguiSettings)lease.Asset, cancellationToken);
                if (_settingsLease == null)
                {
                    _settingsLease = lease;
                    lease = null;
                }
            }
            finally
            {
                try
                {
                    lease?.Dispose();
                }
                catch (Exception exception)
                {
                    LogWarningSafely($"FairyGUI settings lease cleanup failed after initialization attempt: {exception}");
                }
            }
        }

        /// <summary>
        /// Initializes the real runtime host. An alternate resource provider can be supplied for
        /// deterministic Play Mode tests; normal callers continue to use GameModule.Resource.
        /// The provider cannot be replaced while the module is initialized.
        /// </summary>
        public UniTask InitializeAsync(FguiSettings settings, CancellationToken cancellationToken = default,
            IFguiResourceProvider resourceProvider = null)
        {
            EnsureCanStartWork();
            cancellationToken.ThrowIfCancellationRequested();
            if (_initialized)
            {
                if (_settings != settings)
                    throw new InvalidOperationException("FairyGUI is already initialized with different settings.");
                if (resourceProvider != null && !ReferenceEquals(_resourceProvider, resourceProvider))
                    throw new InvalidOperationException("FairyGUI is already initialized with a different resource provider.");
                return UniTask.CompletedTask;
            }
            if (settings == null || settings.Catalog == null)
                throw new ArgumentException("FairyGUI settings and catalog are required.", nameof(settings));

            _shutdown = false;
            _settings = settings;
            _resourceProvider = resourceProvider ?? _resourceProvider ?? new FguiResourceProvider(GameModule.Resource);
            try
            {
                _packages = new FguiPackageService(settings.Catalog, _resourceProvider, settings.LoadTimeoutSeconds);
                _host = _hostFactory(settings, Shutdown);
                FguiExternalLoader.Configure(_resourceProvider, settings.Catalog);
                _externalLoaderConfigured = true;
                _initialized = true;
                return UniTask.CompletedTask;
            }
            catch
            {
                if (_externalLoaderConfigured)
                {
                    try
                    {
                        FguiExternalLoader.ResetConfiguration();
                    }
                    catch (Exception exception)
                    {
                        LogErrorSafely("FairyGUI external loader rollback failed: {0}", exception);
                    }
                    finally
                    {
                        _externalLoaderConfigured = false;
                    }
                }

                try
                {
                    _host?.Shutdown();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("FairyGUI host rollback failed: {0}", exception);
                }
                finally
                {
                    _host = null;
                }

                try
                {
                    _packages?.Shutdown();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("FairyGUI package rollback failed: {0}", exception);
                }
                finally
                {
                    _packages = null;
                }

                _settings = null;
                _initialized = false;
                throw;
            }
        }

        public void Register<TWindow>(FguiWindowDescriptor descriptor) where TWindow : FguiWindow
        {
            EnsureCanStartWork();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (descriptor.WindowType != typeof(TWindow))
                throw new ArgumentException($"Descriptor type {descriptor.WindowType} does not match {typeof(TWindow)}.");
            if (_descriptors.ContainsKey(typeof(TWindow)))
                throw new InvalidOperationException($"FairyGUI window '{typeof(TWindow).Name}' is already registered.");
            descriptor.BindGeneratedTypes?.Invoke();
            _descriptors.Add(typeof(TWindow), descriptor);
        }

        public async UniTask<TWindow> ShowAsync<TWindow>(object userData = null,
            CancellationToken cancellationToken = default) where TWindow : FguiWindow
        {
            EnsureInitialized();
            Type type = typeof(TWindow);
            cancellationToken.ThrowIfCancellationRequested();

            if (_windows.TryGetValue(type, out FguiWindow existing))
            {
                try
                {
                    CancellationToken lifetimeToken = existing.LifecycleToken;
                    ShowExisting(existing);
                    await AwaitRefreshAsync(existing.EnqueueRefreshAsync(userData, cancellationToken,
                        exception => HandleRefreshFailure(type, existing, exception)), lifetimeToken,
                        cancellationToken);
                    EnsureCurrentWindow(type, existing);
                    BringToFront(existing);
                    EnsureCurrentWindow(type, existing);
                    return (TWindow)existing;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Exception failure = NormalizeFguiException(existing, "window-refresh", exception);
                    if (IsCurrentWindow(type, existing))
                        CloseWindow(type, existing);
                    throw failure;
                }
            }

            if (!_creations.TryGetValue(type, out Creation creation))
            {
                if (!_descriptors.TryGetValue(type, out FguiWindowDescriptor descriptor))
                    throw new InvalidOperationException($"FairyGUI window '{type.Name}' is not registered.");
                creation = new Creation
                {
                    Descriptor = descriptor,
                    FirstUserData = userData,
                    Cts = new CancellationTokenSource(),
                    Completion = new UniTaskCompletionSource<FguiWindow>(),
                    WaiterCount = 1
                };
                _creations.Add(type, creation);
                CreateWindowAsync(creation).Forget();
            }
            else
            {
                creation.WaiterCount++;
            }

            UniTaskCompletionSource<FguiWindow> completion = creation.Completion;
            try
            {
                FguiWindow result = await completion.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureCurrentWindow(type, result);
                return (TWindow)result;
            }
            finally
            {
                creation.WaiterCount--;
                if (creation.WaiterCount == 0 && IsCurrentCreation(type, creation))
                    CancelCreation(creation);
            }
        }

        public void Hide<TWindow>() where TWindow : FguiWindow
        {
            if (_windows.TryGetValue(typeof(TWindow), out FguiWindow window))
            {
                SetModalEngaged(window, false);
                window.InternalSetVisible(false);
            }
        }

        public void Close<TWindow>() where TWindow : FguiWindow
        {
            Close(typeof(TWindow));
        }

        public bool IsOpen<TWindow>() where TWindow : FguiWindow => _windows.ContainsKey(typeof(TWindow));

        public bool IsRegistered<TWindow>() where TWindow : FguiWindow =>
            _descriptors.ContainsKey(typeof(TWindow));

        public void CloseAll()
        {
            var creations = new List<Creation>(_creations.Values);
            _creations.Clear();
            foreach (Creation creation in creations)
            {
                try
                {
                    CancelCreation(creation);
                }
                catch (Exception exception)
                {
                    LogWarningSafely($"FairyGUI creation cleanup failed: {exception}");
                }
            }

            var types = new List<Type>(_windows.Keys);
            for (int i = types.Count - 1; i >= 0; i--)
            {
                try
                {
                    Close(types[i]);
                }
                catch (Exception exception)
                {
                    LogErrorSafely("FairyGUI window shutdown failed: {0}", exception);
                }
            }
        }

        public IDisposable SuspendPresentation()
        {
            EnsureInitialized();
            return _host.SuspendPresentation();
        }

        /// <summary>
        /// Keeps a package dependency closure resident until the returned lease is disposed.
        /// </summary>
        public UniTask<FguiPackageLease> PinPackageAsync(string packageKey,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            return _packages.AcquireAsync(packageKey, cancellationToken);
        }

        public void Shutdown()
        {
            if (_shutdown)
                return;
            _shutdown = true;

            try
            {
                CloseAll();
            }
            catch (Exception exception)
            {
                LogErrorSafely("FairyGUI windows shutdown failed: {0}", exception);
            }

            try
            {
                _packages?.Shutdown();
            }
            catch (Exception exception)
            {
                LogErrorSafely("FairyGUI package shutdown failed: {0}", exception);
            }
            finally
            {
                _packages = null;
            }

            try
            {
                if (_externalLoaderConfigured)
                {
                    FguiExternalLoader.ResetConfiguration();
                    _externalLoaderConfigured = false;
                }
            }
            catch (Exception exception)
            {
                LogErrorSafely("FairyGUI external loader cleanup failed: {0}", exception);
            }
            finally
            {
                _externalLoaderConfigured = false;
            }

            try
            {
                _host?.Shutdown();
            }
            catch (Exception exception)
            {
                LogErrorSafely("FairyGUI host shutdown failed: {0}", exception);
            }
            finally
            {
                _host = null;
            }

            try
            {
                _settingsLease?.Dispose();
            }
            catch (Exception exception)
            {
                LogErrorSafely("FairyGUI settings lease cleanup failed: {0}", exception);
            }
            finally
            {
                _settingsLease = null;
                _settings = null;
                _initialized = false;
            }
        }

        protected override void OnRelease()
        {
            Shutdown();
            base.OnRelease();
        }

        private async UniTaskVoid CreateWindowAsync(Creation creation)
        {
            Type windowType = creation.Descriptor.WindowType;
            FguiPackageLease packageLease = null;
            FguiWindow window = null;
            GComponent view = null;
            FguiSettings settings = _settings;
            FguiPackageService packages = _packages;
            IFguiRuntimeHost host = _host;
            float timeoutSeconds = settings.LoadTimeoutSeconds;
            using var timeoutCts = new CancellationTokenSource();
            using IDisposable timeoutRegistration = timeoutCts.CancelAfterSlim(
                TimeSpan.FromSeconds(timeoutSeconds), DelayType.UnscaledDeltaTime);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(creation.Cts.Token, timeoutCts.Token);
            CancellationToken token = linkedCts.Token;
            try
            {
                packageLease = await AcquirePackageAsync(packages, creation.Descriptor.PackageKey, token);
                EnsureCurrentCreation(windowType, creation, token);

                GObject createdObject = UIPackage.CreateObject(creation.Descriptor.PackageName,
                    creation.Descriptor.ComponentName);
                view = createdObject as GComponent;
                if (view == null)
                {
                    createdObject?.Dispose();
                    throw new FguiLoadException("window-view", creation.Descriptor.PackageKey,
                        creation.Descriptor.ComponentName, "FairyGUI component is missing or is not a GComponent.");
                }

                window = creation.Descriptor.Factory();
                if (window == null || window.GetType() != windowType)
                    throw new FguiLoadException("window-factory", creation.Descriptor.PackageKey, null,
                        $"Factory did not create the registered type '{windowType.Name}'.");
                window.InternalAttach(view, packageLease);
                packageLease = null;
                host.GetLayer(creation.Descriptor.Layer).AddChild(view);
                view = null;

                await window.InternalCreateAsync(creation.FirstUserData, token);
                EnsureCurrentCreation(windowType, creation, token);
                _windows.Add(windowType, window);
                window.InternalSetVisible(true);
                EnsureCurrentWindow(windowType, window);
                SetModalEngaged(window, creation.Descriptor.Modal);
                EnsureCurrentWindow(windowType, window);
                RemoveCreation(windowType, creation);
                creation.Completion.TrySetResult(window);
            }
            catch (OperationCanceledException exception)
            {
                RemoveCreation(windowType, creation);
                CleanupFailedWindow(windowType, window, view, packageLease);
                if (timeoutCts.IsCancellationRequested && !creation.Cts.IsCancellationRequested)
                    creation.Completion.TrySetException(new FguiTimeoutException(
                        $"FairyGUI window '{windowType.Name}' exceeded {timeoutSeconds:0.##} seconds.",
                        exception));
                else
                    creation.Completion.TrySetCanceled(creation.Cts.Token);
            }
            catch (Exception exception)
            {
                RemoveCreation(windowType, creation);
                CleanupFailedWindow(windowType, window, view, packageLease);
                if (exception is FguiLoadException || exception is FguiTimeoutException)
                    creation.Completion.TrySetException(exception);
                else
                    creation.Completion.TrySetException(new FguiLoadException("window-create",
                        creation.Descriptor.PackageKey, creation.Descriptor.ComponentName,
                        $"Failed to create FairyGUI window '{windowType.Name}'.", exception));
            }
            finally
            {
                creation.Cts.Dispose();
            }
        }

        private async UniTask<FguiPackageLease> AcquirePackageAsync(FguiPackageService packages,
            string packageKey, CancellationToken token)
        {
            while (true)
            {
                try
                {
                    return await packages.AcquireAsync(packageKey, token);
                }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    // A just-closed creation may have canceled the package service's shared load.
                    // Let that load publish Idle before this new creation starts a fresh generation.
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
            }
        }

        private void CleanupFailedWindow(Type windowType, FguiWindow window, GComponent unattachedView,
            FguiPackageLease packageLease)
        {
            try
            {
                if (window != null)
                {
                    RemoveWindowIfCurrent(windowType, window);
                    window.BeginDestroy();
                    window.InternalDestroy();
                }
            }
            catch (Exception exception)
            {
                LogWarningSafely($"Failed FairyGUI window cleanup raised an exception: {exception}");
            }
            finally
            {
                try
                {
                    if (unattachedView != null && !unattachedView.isDisposed)
                        unattachedView.Dispose();
                }
                catch (Exception exception)
                {
                    LogWarningSafely($"Failed to dispose unattached FairyGUI view: {exception}");
                }

                try { packageLease?.Dispose(); }
                catch (Exception exception)
                {
                    LogWarningSafely($"Failed to release FairyGUI package lease: {exception}");
                }
            }
        }

        private void Close(Type windowType)
        {
            if (_creations.TryGetValue(windowType, out Creation creation))
            {
                _creations.Remove(windowType);
                CancelCreation(creation);
            }

            if (!_windows.TryGetValue(windowType, out FguiWindow window))
                return;
            CloseWindow(windowType, window);
        }

        private bool IsCurrentCreation(Type windowType, Creation creation)
        {
            return _creations.TryGetValue(windowType, out Creation current) && ReferenceEquals(current, creation);
        }

        private void RemoveCreation(Type windowType, Creation creation)
        {
            if (IsCurrentCreation(windowType, creation))
                _creations.Remove(windowType);
        }

        private void ShowExisting(FguiWindow window)
        {
            EnsureCurrentWindow(window.GetType(), window);
            window.InternalSetVisible(true);
            EnsureCurrentWindow(window.GetType(), window);
            FguiWindowDescriptor descriptor = _descriptors[window.GetType()];
            SetModalEngaged(window, descriptor.Modal);
            EnsureCurrentWindow(window.GetType(), window);
        }

        private static void BringToFront(FguiWindow window)
        {
            window.View?.parent?.SetChildIndex(window.View, window.View.parent.numChildren - 1);
        }

        private async UniTask AwaitRefreshAsync(UniTask refresh, CancellationToken lifetimeToken,
            CancellationToken callerToken)
        {
            CancellationTokenSource linkedCts = null;
            try
            {
                CancellationToken waitToken = lifetimeToken;
                if (callerToken.CanBeCanceled || lifetimeToken.CanBeCanceled)
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, callerToken);
                    waitToken = linkedCts.Token;
                }

                if (waitToken.CanBeCanceled)
                    await refresh.AttachExternalCancellation(waitToken);
                else
                    await refresh;
            }
            finally
            {
                linkedCts?.Dispose();
            }
        }

        private void HandleRefreshFailure(Type windowType, FguiWindow window, Exception exception)
        {
            if (!IsCurrentWindow(windowType, window))
                return;

            CloseWindow(windowType, window);
        }

        private static Exception NormalizeFguiException(FguiWindow window, string stage, Exception exception)
        {
            if (exception is FguiLoadException || exception is FguiTimeoutException ||
                exception is OperationCanceledException)
            {
                return exception;
            }

            return new FguiLoadException(stage, null, window?.GetType().Name,
                $"Failed to {stage} FairyGUI window '{window?.GetType().Name}'.", exception);
        }

        private void EnsureCurrentCreation(Type windowType, Creation creation, CancellationToken token)
        {
            if (!IsCurrentCreation(windowType, creation) || token.IsCancellationRequested)
                throw new OperationCanceledException(token);
        }

        private void EnsureCurrentWindow(Type windowType, FguiWindow window)
        {
            if (!IsCurrentWindow(windowType, window) || window == null || window.IsDestroyed ||
                window.View == null || window.View.isDisposed)
            {
                throw new OperationCanceledException();
            }
        }

        private bool IsCurrentWindow(Type windowType, FguiWindow window)
        {
            return window != null && _windows.TryGetValue(windowType, out FguiWindow current) &&
                   ReferenceEquals(current, window);
        }

        private void RemoveWindowIfCurrent(Type windowType, FguiWindow window)
        {
            if (!IsCurrentWindow(windowType, window))
                return;

            window.BeginDestroy();
            _windows.Remove(windowType);
            SetModalEngagedSafely(window, false);
        }

        private void CloseWindow(Type windowType, FguiWindow window)
        {
            if (!IsCurrentWindow(windowType, window))
                return;

            window.BeginDestroy();
            _windows.Remove(windowType);
            SetModalEngagedSafely(window, false);
            window.InternalDestroy();
        }

        private void CancelCreation(Creation creation)
        {
            if (creation == null || creation.Cts == null || creation.Cts.IsCancellationRequested)
                return;

            try { creation.Cts.Cancel(); }
            catch (Exception exception)
            {
                LogWarningSafely($"FairyGUI creation cancellation failed: {exception}");
            }
        }

        private void SetModalEngagedSafely(FguiWindow window, bool engaged)
        {
            try { SetModalEngaged(window, engaged); }
            catch (Exception exception)
            {
                LogWarningSafely($"FairyGUI modal presentation cleanup failed: {exception}");
            }
        }

        private void SetModalEngaged(FguiWindow window, bool engaged)
        {
            if (window.ModalEngaged == engaged)
                return;
            window.ModalEngaged = engaged;
            _host?.SetModalActive(engaged);
        }

        private void EnsureInitialized()
        {
            if (!ModuleSystem.IsRunning)
                throw new ObjectDisposedException(nameof(FguiModule), "FairyGUI is shutting down and cannot accept new work.");

            if (!IsInitialized)
                throw new InvalidOperationException("FairyGUI is not initialized. Await InitializeAsync first.");
        }

        private void EnsureCanStartWork()
        {
            if (!ModuleSystem.IsRunning || _shutdown)
                throw new ObjectDisposedException(nameof(FguiModule), "FairyGUI is shutting down and cannot accept new work.");
        }

        private static void LogErrorSafely(string format, Exception exception)
        {
            try { Log.Error(format, exception); }
            catch { }
        }

        private static void LogWarningSafely(string message)
        {
            try { Log.Warning(message); }
            catch { }
        }
    }
}
