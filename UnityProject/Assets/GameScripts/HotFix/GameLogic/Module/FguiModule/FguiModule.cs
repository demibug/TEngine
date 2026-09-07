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
                lease?.Dispose();
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
                    FguiExternalLoader.ResetConfiguration();
                    _externalLoaderConfigured = false;
                }
                _host?.Shutdown();
                _host = null;
                _packages?.Shutdown();
                _packages = null;
                _settings = null;
                throw;
            }
        }

        public void Register<TWindow>(FguiWindowDescriptor descriptor) where TWindow : FguiWindow
        {
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
                ShowExisting(existing);
                await existing.InternalRefreshAsync(userData, cancellationToken);
                BringToFront(existing);
                return (TWindow)existing;
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
                    Completion = new UniTaskCompletionSource<FguiWindow>()
                };
                _creations.Add(type, creation);
                CreateWindowAsync(creation).Forget();
            }

            creation.WaiterCount++;
            UniTaskCompletionSource<FguiWindow> completion = creation.Completion;
            try
            {
                FguiWindow result = await completion.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return (TWindow)result;
            }
            finally
            {
                creation.WaiterCount--;
                if (creation.WaiterCount == 0 && IsCurrentCreation(type, creation))
                    creation.Cts.Cancel();
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
                creation.Cts.Cancel();

            var types = new List<Type>(_windows.Keys);
            for (int i = types.Count - 1; i >= 0; i--)
                Close(types[i]);
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

            CloseAll();
            _packages?.Shutdown();
            _packages = null;
            if (_externalLoaderConfigured)
            {
                FguiExternalLoader.ResetConfiguration();
                _externalLoaderConfigured = false;
            }
            _host?.Shutdown();
            _host = null;
            _settingsLease?.Dispose();
            _settingsLease = null;
            _settings = null;
            _initialized = false;
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
            float timeoutSeconds = _settings.LoadTimeoutSeconds;
            using var timeoutCts = new CancellationTokenSource();
            using IDisposable timeoutRegistration = timeoutCts.CancelAfterSlim(
                TimeSpan.FromSeconds(timeoutSeconds), DelayType.UnscaledDeltaTime);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(creation.Cts.Token, timeoutCts.Token);
            try
            {
                packageLease = await _packages.AcquireAsync(creation.Descriptor.PackageKey, linkedCts.Token);
                linkedCts.Token.ThrowIfCancellationRequested();

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
                _host.GetLayer(creation.Descriptor.Layer).AddChild(view);
                view = null;

                await window.InternalCreateAsync(creation.FirstUserData, linkedCts.Token);
                linkedCts.Token.ThrowIfCancellationRequested();
                _windows.Add(windowType, window);
                window.InternalSetVisible(true);
                SetModalEngaged(window, creation.Descriptor.Modal);
                RemoveCreation(windowType, creation);
                creation.Completion.TrySetResult(window);
            }
            catch (OperationCanceledException exception)
            {
                RemoveCreation(windowType, creation);
                CleanupFailedWindow(window, view, packageLease);
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
                CleanupFailedWindow(window, view, packageLease);
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

        private static void CleanupFailedWindow(FguiWindow window, GComponent unattachedView,
            FguiPackageLease packageLease)
        {
            try
            {
                if (window != null)
                    window.InternalDestroy();
                else if (unattachedView != null && !unattachedView.isDisposed)
                    unattachedView.Dispose();
            }
            catch (Exception exception)
            {
                Log.Warning($"Failed FairyGUI window cleanup raised an exception: {exception}");
            }
            finally
            {
                if (window == null)
                    packageLease?.Dispose();
            }
        }

        private void Close(Type windowType)
        {
            if (_creations.TryGetValue(windowType, out Creation creation))
            {
                _creations.Remove(windowType);
                creation.Cts.Cancel();
            }

            if (!_windows.TryGetValue(windowType, out FguiWindow window))
                return;
            _windows.Remove(windowType);
            SetModalEngaged(window, false);
            window.InternalDestroy();
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
            window.InternalSetVisible(true);
            FguiWindowDescriptor descriptor = _descriptors[window.GetType()];
            SetModalEngaged(window, descriptor.Modal);
        }

        private static void BringToFront(FguiWindow window)
        {
            window.View?.parent?.SetChildIndex(window.View, window.View.parent.numChildren - 1);
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
            if (!IsInitialized)
                throw new InvalidOperationException("FairyGUI is not initialized. Await InitializeAsync first.");
        }
    }
}
