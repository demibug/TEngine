using System.Collections.Generic;
using YooAsset;

namespace TEngine
{
    internal partial class ResourceModule
    {
        /// <summary>
        /// 资源对象。
        /// <remarks>登记成功后由资源池唯一持有 handle；正常池回收时释放 handle 一次。
        /// 池 shutdown 场景的全局释放责任不在本类型（见资源生命周期批次规划）。</remarks>
        /// </summary>
        private sealed class AssetObject : ObjectBase
        {
            private HandleBase _assetHandle = null;
            private ResourceModule _resourceModule;

            public static AssetObject Create(string name, object target, object assetHandle, ResourceModule resourceModule)
            {
                if (assetHandle == null)
                {
                    throw new GameFrameworkException("Resource is invalid.");
                }

                if (resourceModule == null)
                {
                    throw new GameFrameworkException("Resource Manager is invalid.");
                }

                if (assetHandle is not HandleBase handleBase)
                {
                    throw new GameFrameworkException($"Unsupported handle type: {assetHandle.GetType()}.");
                }

                AssetObject assetObject = MemoryPool.Acquire<AssetObject>();
                try
                {
                    assetObject.Initialize(name, target);
                    assetObject._assetHandle = handleBase;
                    assetObject._resourceModule = resourceModule;
                    return assetObject;
                }
                catch
                {
                    // Initialize 失败时对象尚未进入资源池，及时归还 MemoryPool；外层仍持有原 handle。
                    MemoryPool.Release(assetObject);
                    throw;
                }
            }

            public override void Clear()
            {
                base.Clear();
                _assetHandle = null;
                _resourceModule = null;
            }

            protected internal override void OnUnspawn()
            {
                base.OnUnspawn();
            }

            protected internal override void Release(bool isShutdown)
            {
                // shutdown 分支同样必须释放 handle。HandleBase.Dispose 在 YooAsset 已销毁后
                // 会安全地变成 no-op，但跳过它会把 Provider/Bundle 引用遗留到池 wrapper 中。
                HandleBase handle = _assetHandle;
                _assetHandle = null;
                if (handle is { IsValid: true })
                {
                    try
                    {
                        handle.Dispose();
                        if (isShutdown)
                        {
                            _resourceModule?.RecordShutdownHandleDispose();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        LogErrorSafely($"Release asset handle failed for '{Name}': {ex.Message}");
                    }
                }
            }
        }
    }
}
