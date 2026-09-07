using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using TEngine.FairyGUIIntegration;
using UnityEngine;

namespace GameLogic
{
    public sealed class FguiPackageService
    {
        private enum EntryState
        {
            Idle,
            Loading,
            Ready
        }

        private sealed class Entry
        {
            public FguiCatalogPackage Catalog;
            public EntryState State;
            public int WaiterCount;
            public int OwnerCount;
            public CancellationTokenSource LoadCts;
            public UniTaskCompletionSource<UIPackage> Completion;
            public UIPackage Package;
            public FguiAssetLease DescriptionLease;
            public readonly List<FguiAssetLease> AssetLeases = new List<FguiAssetLease>();
            public readonly List<FguiPackageLease> DependencyLeases = new List<FguiPackageLease>();
            public readonly Dictionary<string, FguiAssetLease> Assets =
                new Dictionary<string, FguiAssetLease>(StringComparer.Ordinal);
        }

        private readonly FguiPackageCatalog _catalog;
        private readonly IFguiResourceProvider _resources;
        private readonly float _timeoutSeconds;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool _shutdown;

        public FguiPackageService(FguiPackageCatalog catalog, IFguiResourceProvider resources, float timeoutSeconds)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _timeoutSeconds = Mathf.Max(1f, timeoutSeconds);

            List<string> errors = catalog.ValidateCatalog();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            foreach (FguiCatalogPackage package in catalog.Packages)
                _entries.Add(package.Key, new Entry { Catalog = package });
        }

        public int ActiveResourceLeaseCount => _resources.ActiveLeaseCount;

        public int GetReferenceCount(string packageKey)
        {
            return _entries.TryGetValue(packageKey, out Entry entry) ? entry.OwnerCount : 0;
        }

        public async UniTask<FguiPackageLease> AcquireAsync(string packageKey, CancellationToken cancellationToken)
        {
            if (_shutdown)
                throw new ObjectDisposedException(nameof(FguiPackageService));
            cancellationToken.ThrowIfCancellationRequested();
            if (!_entries.TryGetValue(packageKey, out Entry entry))
                throw new FguiLoadException("catalog", packageKey, null, $"Unknown FairyGUI package '{packageKey}'.");

            if (entry.State == EntryState.Ready)
            {
                entry.OwnerCount++;
                return new FguiPackageLease(this, packageKey, entry.Package);
            }

            if (entry.State == EntryState.Idle)
                BeginLoad(entry);

            UniTaskCompletionSource<UIPackage> completion = entry.Completion;
            entry.WaiterCount++;
            try
            {
                UIPackage package = await completion.Task.AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                entry.OwnerCount++;
                return new FguiPackageLease(this, packageKey, package);
            }
            finally
            {
                entry.WaiterCount--;
                if (entry.State == EntryState.Loading && entry.WaiterCount == 0 && entry.OwnerCount == 0)
                    entry.LoadCts?.Cancel();
                else if (entry.State == EntryState.Ready && entry.WaiterCount == 0 && entry.OwnerCount == 0)
                    UnloadReadyEntry(entry);
            }
        }

        public void Shutdown()
        {
            if (_shutdown)
                return;
            _shutdown = true;

            foreach (Entry entry in _entries.Values)
                entry.LoadCts?.Cancel();

            List<string> order = BuildTopologicalOrder();
            for (int i = order.Count - 1; i >= 0; i--)
                ForceUnload(_entries[order[i]]);
        }

        internal void Release(string packageKey)
        {
            if (!_entries.TryGetValue(packageKey, out Entry entry) || entry.OwnerCount == 0)
                return;
            entry.OwnerCount--;
            if (entry.OwnerCount == 0 && entry.WaiterCount == 0 && entry.State == EntryState.Ready)
                UnloadReadyEntry(entry);
        }

        private void BeginLoad(Entry entry)
        {
            entry.State = EntryState.Loading;
            var loadCts = new CancellationTokenSource();
            var completion = new UniTaskCompletionSource<UIPackage>();
            entry.LoadCts = loadCts;
            entry.Completion = completion;
            LoadEntryAsync(entry, loadCts, completion).Forget();
        }

        private async UniTaskVoid LoadEntryAsync(Entry entry, CancellationTokenSource loadCts,
            UniTaskCompletionSource<UIPackage> completion)
        {
            CancellationToken loadToken = loadCts.Token;
            using var timeoutCts = new CancellationTokenSource();
            using IDisposable timeoutRegistration = timeoutCts.CancelAfterSlim(TimeSpan.FromSeconds(_timeoutSeconds),
                DelayType.UnscaledDeltaTime);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(loadToken, timeoutCts.Token);
            CancellationToken token = linkedCts.Token;
            try
            {
                foreach (string dependency in entry.Catalog.Dependencies)
                    entry.DependencyLeases.Add(await AcquireAsync(dependency, token));

                entry.DescriptionLease = await _resources.LoadAsync(entry.Catalog.DescriptionAddress,
                    typeof(TextAsset), entry.Catalog.YooAssetPackageName, token);

                foreach (FguiCatalogAsset asset in entry.Catalog.Assets)
                {
                    Type assetType = ResolveAssetType(asset.Kind);
                    FguiAssetLease lease = await _resources.LoadAsync(asset.Address, assetType,
                        entry.Catalog.YooAssetPackageName, token);
                    entry.AssetLeases.Add(lease);
                    entry.Assets.Add(asset.LookupKey, lease);
                }

                token.ThrowIfCancellationRequested();
                CheckGlobalPackageConflict(entry);
                var desc = entry.DescriptionLease.Asset as TextAsset;
                if (desc == null || desc.bytes == null || desc.bytes.Length == 0)
                    throw new FguiLoadException("package-description", entry.Catalog.Key,
                        entry.Catalog.DescriptionAddress, "FairyGUI description is empty or has the wrong type.");

                entry.Package = UIPackage.AddPackage(desc.bytes, entry.Catalog.AssetNamePrefix,
                    (string name, string extension, Type type, out DestroyMethod destroyMethod) =>
                    {
                        destroyMethod = DestroyMethod.None;
                        string lookupKey = name + extension;
                        if (!entry.Assets.TryGetValue(lookupKey, out FguiAssetLease lease) || lease.Asset == null)
                            throw new FguiLoadException("package-resource", entry.Catalog.Key, lookupKey,
                                $"No preloaded asset is mapped for '{lookupKey}'.");
                        if (!IsCompatible(type, lease.Asset))
                            throw new FguiLoadException("package-resource-type", entry.Catalog.Key, lookupKey,
                                $"Preloaded asset '{lookupKey}' is {lease.Asset.GetType().Name}, requested {type.Name}.");
                        return lease.Asset;
                    });

                if (entry.Package == null)
                    throw new FguiLoadException("package-add", entry.Catalog.Key, entry.Catalog.DescriptionAddress,
                        "UIPackage.AddPackage returned null.");
                if (!string.Equals(entry.Package.id, entry.Catalog.PackageId, StringComparison.Ordinal) ||
                    !string.Equals(entry.Package.name, entry.Catalog.PackageName, StringComparison.Ordinal))
                    throw new FguiLoadException("package-identity", entry.Catalog.Key, entry.Catalog.DescriptionAddress,
                        $"Catalog expects {entry.Catalog.PackageName}/{entry.Catalog.PackageId}, descriptor contains " +
                        $"{entry.Package.name}/{entry.Package.id}.");

                ValidateDescriptorDependencies(entry);
                entry.Package.LoadAllAssets();
                token.ThrowIfCancellationRequested();
                entry.State = EntryState.Ready;
                completion.TrySetResult(entry.Package);
            }
            catch (OperationCanceledException exception)
            {
                RollbackLoadingEntry(entry);
                if (timeoutCts.IsCancellationRequested && !loadToken.IsCancellationRequested)
                    completion.TrySetException(new FguiTimeoutException(
                        $"FairyGUI package '{entry.Catalog.Key}' exceeded {_timeoutSeconds:0.##} seconds.", exception));
                else
                    completion.TrySetCanceled(loadToken);
            }
            catch (Exception exception)
            {
                RollbackLoadingEntry(entry);
                if (exception is FguiLoadException)
                    completion.TrySetException(exception);
                else
                    completion.TrySetException(new FguiLoadException("package-load", entry.Catalog.Key,
                        entry.Catalog.DescriptionAddress, $"Failed to load FairyGUI package '{entry.Catalog.Key}'.",
                        exception));
            }
            finally
            {
                loadCts.Dispose();
                if (ReferenceEquals(entry.LoadCts, loadCts))
                    entry.LoadCts = null;
            }
        }

        private static Type ResolveAssetType(FguiAssetKind kind)
        {
            switch (kind)
            {
                case FguiAssetKind.TextAsset: return typeof(TextAsset);
                case FguiAssetKind.Texture2D: return typeof(Texture2D);
                case FguiAssetKind.AudioClip: return typeof(AudioClip);
                case FguiAssetKind.Font: return typeof(Font);
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static bool IsCompatible(Type requestedType, UnityEngine.Object asset)
        {
            if (requestedType == typeof(Texture))
                return asset is Texture;
            return requestedType.IsInstanceOfType(asset);
        }

        private static void CheckGlobalPackageConflict(Entry entry)
        {
            UIPackage byId = UIPackage.GetById(entry.Catalog.PackageId);
            UIPackage byName = UIPackage.GetByName(entry.Catalog.PackageName);
            if (byId != null || byName != null)
                throw new FguiLoadException("package-conflict", entry.Catalog.Key, null,
                    $"FairyGUI package id/name is already registered: {entry.Catalog.PackageId}/{entry.Catalog.PackageName}.");
        }

        private static void ValidateDescriptorDependencies(Entry entry)
        {
            Dictionary<string, string>[] descriptorDependencies = entry.Package.dependencies;
            if (descriptorDependencies == null)
                return;

            foreach (Dictionary<string, string> dependency in descriptorDependencies)
            {
                if (!dependency.TryGetValue("id", out string id) && !dependency.TryGetValue("name", out _))
                    continue;
                bool found = false;
                foreach (FguiPackageLease lease in entry.DependencyLeases)
                {
                    if (lease.Package.id == id ||
                        dependency.TryGetValue("name", out string name) && lease.Package.name == name)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    throw new FguiLoadException("package-dependency", entry.Catalog.Key, id,
                        $"Descriptor dependency '{id}' is not declared in the catalog.");
            }
        }

        private static void RemovePackageIfPresent(Entry entry)
        {
            UIPackage package = entry.Package;
            if (package == null)
                return;

            // FairyGUI clears its global package registry from StageEngine.OnApplicationQuit before
            // Unity destroys TEngine's hotfix listener. Treat that SDK-owned cleanup as already done,
            // while still releasing the YooAsset leases below.
            UIPackage registeredById = UIPackage.GetById(package.id);
            UIPackage registeredByName = UIPackage.GetByName(package.name);
            string registeredKey = ReferenceEquals(registeredById, package)
                ? package.id
                : ReferenceEquals(registeredByName, package)
                    ? package.name
                    : null;
            entry.Package = null;
            if (registeredKey == null)
                return;

            try
            {
                UIPackage.RemovePackage(registeredKey);
            }
            catch (Exception exception)
            {
                TEngine.Log.Warning($"Failed to remove FairyGUI package '{entry.Catalog.Key}': {exception}");
            }
        }

        private static void DisposeAssets(Entry entry)
        {
            for (int i = entry.AssetLeases.Count - 1; i >= 0; i--)
            {
                try { entry.AssetLeases[i].Dispose(); }
                catch (Exception exception)
                {
                    TEngine.Log.Warning($"Failed to release FairyGUI asset lease: {exception}");
                }
            }
            entry.AssetLeases.Clear();
            entry.Assets.Clear();
            try { entry.DescriptionLease?.Dispose(); }
            catch (Exception exception)
            {
                TEngine.Log.Warning($"Failed to release FairyGUI package description lease: {exception}");
            }
            entry.DescriptionLease = null;
        }

        private static void DisposeDependencies(Entry entry)
        {
            for (int i = entry.DependencyLeases.Count - 1; i >= 0; i--)
            {
                try { entry.DependencyLeases[i].Dispose(); }
                catch (Exception exception)
                {
                    TEngine.Log.Warning($"Failed to release FairyGUI dependency lease: {exception}");
                }
            }
            entry.DependencyLeases.Clear();
        }

        private void RollbackLoadingEntry(Entry entry)
        {
            RemovePackageIfPresent(entry);
            DisposeAssets(entry);
            DisposeDependencies(entry);
            entry.State = EntryState.Idle;
        }

        private void UnloadReadyEntry(Entry entry)
        {
            RemovePackageIfPresent(entry);
            DisposeAssets(entry);
            entry.State = EntryState.Idle;
            DisposeDependencies(entry);
        }

        private void ForceUnload(Entry entry)
        {
            entry.OwnerCount = 0;
            entry.WaiterCount = 0;
            RemovePackageIfPresent(entry);
            DisposeAssets(entry);
            entry.State = EntryState.Idle;
            DisposeDependencies(entry);
        }

        private List<string> BuildTopologicalOrder()
        {
            var order = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in _entries.Keys)
                Visit(key, visited, order);
            return order;
        }

        private void Visit(string key, HashSet<string> visited, List<string> order)
        {
            if (!visited.Add(key))
                return;
            foreach (string dependency in _entries[key].Catalog.Dependencies)
                Visit(dependency, visited, order);
            order.Add(key);
        }
    }

    public sealed class FguiPackageLease : IDisposable
    {
        private FguiPackageService _owner;
        private string _packageKey;

        internal FguiPackageLease(FguiPackageService owner, string packageKey, UIPackage package)
        {
            _owner = owner;
            _packageKey = packageKey;
            Package = package;
        }

        public UIPackage Package { get; private set; }

        public void Dispose()
        {
            FguiPackageService owner = Interlocked.Exchange(ref _owner, null);
            string key = _packageKey;
            _packageKey = null;
            Package = null;
            owner?.Release(key);
        }
    }
}
