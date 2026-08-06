using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using YooAsset;

namespace GameFUI
{
    /// <summary>
    /// 基于 YooAsset 的真实 GameFUI 资源 provider，包装显式注入的 <see cref="IResourceModule"/>。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策1。FUIModule 在内部把公开传入的 <see cref="IResourceModule"/>
    /// 包装成最窄资源 provider；本类型即该包装的真实实现。
    ///
    /// 依赖注入：必须通过构造函数注入 <see cref="IResourceModule"/>，禁止在内部调用
    /// <c>ModuleSystem.GetModule&lt;IResourceModule&gt;()</c>，以保留未来组合根对依赖组装的控制，
    /// 并避免 GameFUI 反向引用 GameModule（design.md 决策1）。
    ///
    /// 边界约束：本类型不得 using 且不得反向依赖 GameLogic/GamePlay/GameBattle 命名空间
    /// （GameFUI.asmdef 引用集合已保证，代码层面亦不引入相关 using）。
    ///
    /// 最窄能力：仅覆盖 GameFUI 实际使用的 handle 加载、location 校验与诊断能力，
    /// 不暴露 <see cref="IResourceModule"/> 的初始化、下载、版本等与包加载无关的接口。
    /// </remarks>
    public sealed class YooAssetFUIResourceProvider : IFUIResourceProvider
    {
        private readonly IResourceModule _resourceModule;

        /// <summary>
        /// 构造真实资源 provider。
        /// </summary>
        /// <param name="resourceModule">显式注入的资源模块，不得为 null。</param>
        /// <exception cref="ArgumentNullException">注入的资源模块为 null。</exception>
        public YooAssetFUIResourceProvider(IResourceModule resourceModule)
        {
            _resourceModule = resourceModule ?? throw new ArgumentNullException(nameof(resourceModule));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 调用 <see cref="IResourceModule.LoadAssetAsyncHandle{TAsset}"/> 获取真实 <see cref="AssetHandle"/>，
        /// 等待其完成（失败时抛出 <see cref="Exception"/>），并包装为 <see cref="YooAssetHandleWrapper"/> 返回。
        ///
        /// 等待方式：使用 <see cref="HandleBase.Task"/>（System.Threading.Tasks.Task）经
        /// <c>Task.AsUniTask()</c> 转为 UniTask，再用 <c>AttachExternalCancellation</c> 接入取消令牌。
        /// 此路径仅依赖 GameFUI.asmdef 显式引用的 UniTask 核心与 YooAsset 核心，
        /// 不依赖 UniTask.YooAsset 程序集的 <c>HandleBase.ToUniTask</c> 扩展（该扩展受
        /// UNITASK_YOOASSET_SUPPORT 条件编译且 GameFUI 未显式引用 UniTask.YooAsset）。
        ///
        /// 失败语义：<see cref="AssetHandle"/> 加载失败时，<see cref="HandleBase.LastError"/>
        /// 通过 Task 异常传播，调用方收到包装异常。
        /// 取消语义：取消令牌触发时抛出 <see cref="OperationCanceledException"/>。
        /// 资源所有权：等待失败或取消时释放本次新建的 handle，不产生无所有者 handle
        /// （spec：资源所有权唯一且释放有序）。
        /// </remarks>
        public async UniTask<IFUIAssetHandle> LoadAssetAsyncHandle<TAsset>(string location, CancellationToken cancellationToken = default, string packageName = "") where TAsset : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new ArgumentException("资源定位地址不能为空。", nameof(location));
            }

            AssetHandle handle = _resourceModule.LoadAssetAsyncHandle<TAsset>(location, packageName);
            YooAssetHandleWrapper wrapper = new YooAssetHandleWrapper(handle);

            try
            {
                // 等待 YooAsset handle 完成；失败时底层 Task 携带 LastError 异常。
                // AttachExternalCancellation 将 cancellationToken 接入取消：取消即抛出 OperationCanceledException。
                await handle.Task.AsUniTask().AttachExternalCancellation(cancellationToken);
                return wrapper;
            }
            catch
            {
                // 等待失败或取消时释放本次新建的 handle，避免无所有者 handle。
                wrapper.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public bool CheckLocationValid(string location, string packageName = "")
        {
            return _resourceModule.CheckLocationValid(location, packageName);
        }

        /// <inheritdoc/>
        public bool HasAsset(string location, string packageName = "")
        {
            // HasAssetResult：NotExist(0) 表示资源不存在，Valid 表示地址无效；
            // AssetOnline/AssetOnDisk/AssetOnFileSystem/BinaryOnDisk/BinaryOnFileSystem 均表示资源存在。
            HasAssetResult result = _resourceModule.HasAsset(location, packageName);
            return result != HasAssetResult.NotExist && result != HasAssetResult.Valid;
        }
    }

    /// <summary>
    /// 真实 <see cref="AssetHandle"/> 的最窄包装，实现 <see cref="IFUIAssetHandle"/>。
    /// </summary>
    /// <remarks>
    /// <see cref="AssetHandle"/> 是 sealed 类型且无法被 GameFUI 程序集派生，
    /// 故采用组合包装。所有权由本包装持有，<see cref="Dispose"/> 调用底层
    /// <see cref="AssetHandle.Release"/>，由 PackageRecord 在最终释放阶段统一触发
    /// （spec：资源所有权唯一且释放有序；design.md 决策9）。
    /// </remarks>
    internal sealed class YooAssetHandleWrapper : IFUIAssetHandle
    {
        private AssetHandle _handle;
        private bool _disposed;

        /// <summary>
        /// 构造包装。
        /// </summary>
        /// <param name="handle">真实 YooAsset 句柄，不得为 null。</param>
        public YooAssetHandleWrapper(AssetHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        /// <inheritdoc/>
        public UnityEngine.Object AssetObject
        {
            get
            {
                if (_disposed || _handle == null)
                {
                    return null;
                }

                return _handle.AssetObject;
            }
        }

        /// <inheritdoc/>
        public bool IsDone
        {
            get
            {
                if (_disposed || _handle == null)
                {
                    return true;
                }

                return _handle.IsDone;
            }
        }

        /// <inheritdoc/>
        public string LastError
        {
            get
            {
                if (_disposed || _handle == null)
                {
                    return string.Empty;
                }

                return _handle.LastError;
            }
        }

        /// <inheritdoc/>
        public TAsset GetAssetObject<TAsset>() where TAsset : UnityEngine.Object
        {
            if (_disposed || _handle == null)
            {
                return null;
            }

            return _handle.GetAssetObject<TAsset>();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_handle != null)
            {
                // AssetHandle.Release / Dispose 会递减引用计数并在归零时尝试卸载底层资源包。
                _handle.Release();
                _handle = null;
            }
        }
    }
}
