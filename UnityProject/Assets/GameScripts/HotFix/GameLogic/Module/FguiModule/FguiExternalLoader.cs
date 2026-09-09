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
    public sealed class FguiExternalLoader : GLoader
    {
        private static IFguiResourceProvider _resources;
        private static FguiPackageCatalog _catalog;

        private readonly Dictionary<NTexture, FguiAssetLease> _leases =
            new Dictionary<NTexture, FguiAssetLease>();
        private CancellationTokenSource _loadCts;
        private int _generation;

        public static void Configure(IFguiResourceProvider resources, FguiPackageCatalog catalog)
        {
            if (!ModuleSystem.IsRunning)
                throw new ObjectDisposedException(nameof(FguiExternalLoader), "FairyGUI external loader cannot be configured while the module system is not running.");

            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            UIObjectFactory.SetLoaderExtension(typeof(FguiExternalLoader));
        }

        public static void ResetConfiguration()
        {
            _resources = null;
            _catalog = null;
            UIObjectFactory.SetLoaderExtension((UIObjectFactory.GLoaderCreator)null);
        }

        protected override void LoadExternal()
        {
            CancelCurrentLoad();
            int generation = ++_generation;
            if (!ModuleSystem.IsRunning)
                return;

            string requestedUrl = url;
            if (!requestedUrl.StartsWith("asset://", StringComparison.Ordinal))
            {
                LogWarningSafely($"Unsupported FairyGUI external URL '{requestedUrl}'. Only asset:// is allowed.");
                onExternalLoadFailed();
                return;
            }

            string key = requestedUrl.Substring("asset://".Length);
            if (_catalog == null || _resources == null || !_catalog.TryGetExternal(key, out FguiExternalAsset asset) ||
                asset.Kind != FguiAssetKind.Texture2D)
            {
                LogWarningSafely($"FairyGUI external texture '{key}' is missing from the catalog.");
                onExternalLoadFailed();
                return;
            }

            _loadCts = new CancellationTokenSource();
            LoadTextureAsync(generation, requestedUrl, asset, _loadCts.Token).Forget();
        }

        protected override void FreeExternal(NTexture texture)
        {
            if (texture == null)
                return;
            if (_leases.TryGetValue(texture, out FguiAssetLease lease))
            {
                _leases.Remove(texture);
                try { texture.Dispose(); }
                finally { lease.Dispose(); }
            }
        }

        public override void Dispose()
        {
            CancelCurrentLoad();
            _generation++;
            try
            {
                base.Dispose();
            }
            finally
            {
                foreach (FguiAssetLease lease in _leases.Values)
                {
                    try { lease.Dispose(); }
                    catch (Exception exception)
                    {
                        LogWarningSafely($"Failed to release FairyGUI external texture lease: {exception}");
                    }
                }
                _leases.Clear();
            }
        }

        private async UniTaskVoid LoadTextureAsync(int generation, string requestedUrl, FguiExternalAsset asset,
            CancellationToken cancellationToken)
        {
            FguiAssetLease lease = null;
            try
            {
                lease = await _resources.LoadAsync(asset.Address, typeof(Texture2D), asset.YooAssetPackageName,
                    cancellationToken);
                if (!ModuleSystem.IsRunning || generation != _generation || url != requestedUrl || isDisposed ||
                    cancellationToken.IsCancellationRequested)
                    return;

                var texture = new NTexture((Texture2D)lease.Asset) { destroyMethod = DestroyMethod.None };
                _leases.Add(texture, lease);
                lease = null;
                onExternalLoadSuccess(texture);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (ModuleSystem.IsRunning && generation == _generation && url == requestedUrl && !isDisposed)
                {
                    LogWarningSafely($"Failed to load FairyGUI external texture '{requestedUrl}': {exception}");
                    onExternalLoadFailed();
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
                    LogWarningSafely($"Failed to release late FairyGUI external texture lease: {exception}");
                }
            }
        }

        private void CancelCurrentLoad()
        {
            CancellationTokenSource loadCts = _loadCts;
            _loadCts = null;
            if (loadCts == null)
                return;

            try
            {
                loadCts.Cancel();
            }
            catch (Exception exception)
            {
                LogWarningSafely($"Failed to cancel FairyGUI external texture load: {exception}");
            }
            finally
            {
                try { loadCts.Dispose(); }
                catch (Exception exception) { LogWarningSafely($"Failed to dispose FairyGUI external texture load: {exception}"); }
            }
        }

        private static void LogWarningSafely(string message)
        {
            try { Log.Warning(message); }
            catch { }
        }
    }
}
