using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using YooAsset;

namespace GameLogic
{
    public interface IFguiResourceProvider
    {
        int ActiveLeaseCount { get; }
        UniTask<FguiAssetLease> LoadAsync(string address, Type assetType, string yooAssetPackageName,
            CancellationToken cancellationToken);
    }

    public sealed class FguiResourceProvider : IFguiResourceProvider
    {
        private readonly IResourceModule _resourceModule;
        private int _activeLeaseCount;

        public FguiResourceProvider(IResourceModule resourceModule)
        {
            _resourceModule = resourceModule ?? throw new ArgumentNullException(nameof(resourceModule));
        }

        public int ActiveLeaseCount => _activeLeaseCount;

        public async UniTask<FguiAssetLease> LoadAsync(string address, Type assetType, string yooAssetPackageName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("Asset address cannot be empty.", nameof(address));
            if (assetType == null)
                throw new ArgumentNullException(nameof(assetType));

            cancellationToken.ThrowIfCancellationRequested();
            AssetHandle handle = null;
            try
            {
                handle = _resourceModule.LoadAssetAsyncHandle(address, assetType, yooAssetPackageName ?? string.Empty);
                if (handle == null)
                    throw new FguiLoadException("resource-handle", null, address, "YooAsset returned a null handle.");

                while (!handle.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();

                if (handle.Status != EOperationStatus.Succeed || handle.AssetObject == null)
                    throw new FguiLoadException("resource-load", null, address,
                        $"YooAsset failed to load '{address}': {handle.LastError}");
                if (!assetType.IsInstanceOfType(handle.AssetObject))
                    throw new FguiLoadException("resource-type", null, address,
                        $"Asset '{address}' is {handle.AssetObject.GetType().Name}, expected {assetType.Name}.");

                Interlocked.Increment(ref _activeLeaseCount);
                FguiAssetLease lease = new FguiAssetLease(handle, handle.AssetObject,
                    () => Interlocked.Decrement(ref _activeLeaseCount));
                handle = null;
                return lease;
            }
            finally
            {
                handle?.Dispose();
            }
        }
    }

    public sealed class FguiAssetLease : IDisposable
    {
        private Action _release;

        internal FguiAssetLease(AssetHandle handle, UnityEngine.Object asset, Action onDisposed)
        {
            Asset = asset;
            _release = () =>
            {
                try { handle.Dispose(); }
                finally { onDisposed?.Invoke(); }
            };
        }

        /// <summary>Creates a lease for an alternate provider (primarily deterministic tests).</summary>
        public FguiAssetLease(UnityEngine.Object asset, Action release)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public UnityEngine.Object Asset { get; private set; }
        public bool IsDisposed => _release == null;

        public void Dispose()
        {
            Action release = Interlocked.Exchange(ref _release, null);
            if (release == null)
                return;

            Asset = null;
            release.Invoke();
        }
    }
}
