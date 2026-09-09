namespace TEngine
{
    internal partial class ResourceModule
    {
        private IObjectPool<AssetObject> _assetPool;
        
        /// <summary>
        /// 获取或设置资源对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        public float AssetAutoReleaseInterval
        {
            get => _assetPool != null ? _assetPool.AutoReleaseInterval : 0f;
            set
            {
                if (_assetPool != null)
                    _assetPool.AutoReleaseInterval = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池的容量。
        /// </summary>
        public int AssetCapacity
        {
            get => _assetPool != null ? _assetPool.Capacity : 0;
            set
            {
                if (_assetPool != null)
                    _assetPool.Capacity = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池对象过期秒数。
        /// </summary>
        public float AssetExpireTime
        {
            get => _assetPool != null ? _assetPool.ExpireTime : 0f;
            set
            {
                if (_assetPool != null)
                    _assetPool.ExpireTime = value;
            }
        }

        /// <summary>
        /// 获取或设置资源对象池的优先级。
        /// </summary>
        public int AssetPriority
        {
            get => _assetPool != null ? _assetPool.Priority : 0;
            set
            {
                if (_assetPool != null)
                    _assetPool.Priority = value;
            }
        }
        
        /// <summary>
        /// 卸载资源。
        /// </summary>
        /// <remarks>保持“一份成功加载对应一次归还”契约：调用一次 UnloadAsset 归还一份 spawn。
        /// 它无法识别同对象的不同调用者，不实现“同对象只释放一次”。</remarks>
        /// <param name="asset">要卸载的资源。</param>
        public void UnloadAsset(object asset)
        {
            if (asset == null)
            {
                Log.Warning("UnloadAsset called with a null asset.");
                return;
            }

            if (!_poolClosed && _assetPool != null)
            {
                try
                {
                    _assetPool.Unspawn(asset);
                    if (ModuleSystem.IsShuttingDown)
                    {
                        _shutdownAssetUnspawnCount++;
                    }
                }
                catch (System.Exception exception)
                {
                    Log.Error("UnloadAsset failed during cleanup: {0}", exception);
                }
            }
        }
        
        /// <summary>
        /// 设置对象池管理器。
        /// </summary>
        /// <param name="objectPoolModule">对象池管理器。</param>
        public void SetObjectPoolModule(IObjectPoolModule objectPoolModule)
        {
            EnsureAcceptingRequests();
            if (objectPoolModule == null)
            {
                throw new GameFrameworkException("Object pool manager is invalid.");
            }
            _assetPool = objectPoolModule.CreateMultiSpawnObjectPool<AssetObject>("Asset Pool");
        }

        /// <summary>
        /// 获取资源池对象信息（诊断用途）。
        /// </summary>
        internal ObjectInfo[] GetAssetPoolObjectInfos()
        {
            return (_assetPool as ObjectPoolBase)?.GetAllObjectInfos();
        }

        /// <summary>
        /// 仅释放资源池中未使用的条目（诊断用途，不触发 YooAsset 层回收）。
        /// </summary>
        internal void ReleaseUnusedPoolAssetsForDiagnostics()
        {
            if (!_poolClosed)
                _assetPool?.ReleaseAllUnused();
        }
    }
}
