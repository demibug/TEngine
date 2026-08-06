using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace GameFUI
{
    /// <summary>
    /// GameFUI 资源句柄抽象。
    /// </summary>
    /// <remarks>
    /// TEngine 的 <see cref="AssetHandle"/> 是 sealed 类型且其构造函数为 internal，
    /// GameFUI 程序集无法直接实例化或派生，因此定义此最窄句柄抽象用于：
    /// 1. 真实实现 <see cref="YooAssetFUIResourceProvider"/> 内部包装 <see cref="AssetHandle"/>；
    /// 2. 测试用内存实现可注入可控的内存句柄，支持失败与取消。
    /// 句柄持有人通过 <see cref="Dispose"/> 释放底层资源所有权。
    /// </remarks>
    public interface IFUIAssetHandle : IDisposable
    {
        /// <summary>
        /// 获取已加载的资源对象。未完成或已释放时返回 null。
        /// </summary>
        UnityEngine.Object AssetObject { get; }

        /// <summary>
        /// 获取资源对象并按指定类型转换。未完成、类型不匹配或已释放时返回 null。
        /// </summary>
        /// <typeparam name="TAsset">资源类型。</typeparam>
        /// <returns>资源对象。</returns>
        TAsset GetAssetObject<TAsset>() where TAsset : UnityEngine.Object;

        /// <summary>
        /// 是否已完成加载（含成功与失败）。
        /// </summary>
        bool IsDone { get; }

        /// <summary>
        /// 最近一次错误信息；无错误时为空字符串。
        /// </summary>
        string LastError { get; }
    }

    /// <summary>
    /// GameFUI 最窄内部资源 provider。
    /// </summary>
    /// <remarks>
    /// 本接口只覆盖 GameFUI 包加载实际使用的能力：handle 加载、location 校验与诊断。
    /// 它包装公开传入的 <see cref="TEngine.IResourceModule"/>，不反向依赖 GameLogic/GamePlay/GameBattle。
    /// 设计依据：design.md 决策1 与决策8。Acquire 流程使用
    /// <see cref="LoadAssetAsyncHandle"/> 加载 <c>{PackageName}_fui</c> 描述文件与外部资源，
    /// 并通过返回的 <see cref="IFUIAssetHandle"/> 统一持有所有权，由 PackageRecord 在最终释放时 Dispose。
    /// </remarks>
    public interface IFUIResourceProvider
    {
        /// <summary>
        /// 异步加载资源并返回可持有的资源句柄。
        /// </summary>
        /// <remarks>
        /// 该方法对应 <see cref="TEngine.IResourceModule.LoadAssetAsyncHandle{T}"/> 的最窄包装，
        /// 用于加载 <c>{PackageName}_fui</c> 描述文件（TextAsset）与包内贴图、音频等外部资源。
        /// 返回的句柄由调用方持有并负责 <see cref="IFUIAssetHandle.Dispose"/>。
        /// </remarks>
        /// <param name="location">资源定位地址。</param>
        /// <param name="cancellationToken">取消令牌；响应取消时抛出 <see cref="OperationCanceledException"/>。</param>
        /// <param name="packageName">资源包名称，空字符串使用默认包。</param>
        /// <typeparam name="TAsset">资源类型。</typeparam>
        /// <returns>可等待的资源句柄。</returns>
        UniTask<IFUIAssetHandle> LoadAssetAsyncHandle<TAsset>(string location, CancellationToken cancellationToken = default, string packageName = "") where TAsset : UnityEngine.Object;

        /// <summary>
        /// 校验资源定位地址是否有效。
        /// </summary>
        /// <remarks>
        /// 对应 <see cref="TEngine.IResourceModule.CheckLocationValid"/>。用于构建前或加载前诊断，
        /// 避免把寻址错误推迟到首屏构造阶段。
        /// </remarks>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称，空字符串使用默认包。</param>
        /// <returns>地址有效返回 true。</returns>
        bool CheckLocationValid(string location, string packageName = "");

        /// <summary>
        /// 检查资源是否存在，用于加载前诊断。
        /// </summary>
        /// <remarks>
        /// 对应 <see cref="TEngine.IResourceModule.HasAsset"/> 的诊断能力。
        /// 当 YooAsset 的 handle 加载已能覆盖失败语义时，调用方可不强制使用本方法。
        /// </remarks>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称，空字符串使用默认包。</param>
        /// <returns>资源存在返回 true。</returns>
        bool HasAsset(string location, string packageName = "");
    }

    /// <summary>
    /// 测试用内存资源 provider，支持预设资源、可控失败与可控取消。
    /// </summary>
    /// <remarks>
    /// 仅通过 internal 测试入口为测试程序集注入可控失败/取消的内存 provider（design.md 决策1）。
    /// 由于 <see cref="AssetHandle"/> 是 sealed 且构造函数为 internal，
    /// 内存实现使用 <see cref="InMemoryAssetHandle"/> 提供与真实句柄同构的测试句柄。
    /// 待 3.6 创建 GameFUI.Tests.asmdef 时通过 InternalsVisibleTo 暴露本类型，
    /// 或在 AssemblyInfo.cs 中统一配置；当前阶段先以 internal 定义，不影响生产程序集。
    /// 本类型不得反向依赖 GameLogic/GamePlay/GameBattle。
    /// </remarks>
    internal sealed class InMemoryFUIResourceProvider : IFUIResourceProvider
    {
        /// <summary>
        /// 预设的资源表：location -> 资源对象。
        /// </summary>
        private readonly Dictionary<string, UnityEngine.Object> _assets = new Dictionary<string, UnityEngine.Object>();

        /// <summary>
        /// 需要模拟加载失败的 location 集合。
        /// </summary>
        private readonly HashSet<string> _failLocations = new HashSet<string>();

        /// <summary>
        /// 加载延迟（毫秒），用于测试取消与并发时序；0 表示立即完成。
        /// </summary>
        private readonly int _loadDelayMs;

        /// <summary>
        /// 构造内存 provider。
        /// </summary>
        /// <param name="loadDelayMs">加载延迟毫秒数，用于模拟异步时序与取消窗口。</param>
        public InMemoryFUIResourceProvider(int loadDelayMs = 0)
        {
            _loadDelayMs = loadDelayMs;
        }

        /// <summary>
        /// 预设一个内存资源。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="asset">资源对象。</param>
        public void SetAsset(string location, UnityEngine.Object asset)
        {
            _assets[location] = asset;
        }

        /// <summary>
        /// 标记指定 location 加载失败。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        public void MarkLoadFailure(string location)
        {
            _failLocations.Add(location);
        }

        /// <summary>
        /// 标记多个 location 加载失败。
        /// </summary>
        /// <param name="locations">资源定位地址集合。</param>
        public void MarkLoadFailures(IEnumerable<string> locations)
        {
            foreach (var location in locations)
            {
                _failLocations.Add(location);
            }
        }

        /// <inheritdoc/>
        public async UniTask<IFUIAssetHandle> LoadAssetAsyncHandle<TAsset>(string location, CancellationToken cancellationToken = default, string packageName = "") where TAsset : UnityEngine.Object
        {
            // 模拟异步延迟，使取消测试具备时序窗口。
            if (_loadDelayMs > 0)
            {
                await UniTask.Delay(_loadDelayMs, cancellationToken: cancellationToken);
            }
            else
            {
                // 即使无延迟也响应取消，保证取消语义即时生效。
                cancellationToken.ThrowIfCancellationRequested();
            }

            // 可控失败：命中失败集合时抛出异常。
            if (_failLocations.Contains(location))
            {
                throw new InvalidOperationException($"[InMemoryFUIResourceProvider] 模拟加载失败：{location}");
            }

            // 资源缺失：未预设时抛出异常，与真实 provider 的加载失败语义对齐。
            if (!_assets.TryGetValue(location, out var asset) || asset == null)
            {
                throw new InvalidOperationException($"[InMemoryFUIResourceProvider] 未预设资源：{location}");
            }

            return new InMemoryAssetHandle(asset);
        }

        /// <inheritdoc/>
        public bool CheckLocationValid(string location, string packageName = "")
        {
            return !string.IsNullOrEmpty(location);
        }

        /// <inheritdoc/>
        public bool HasAsset(string location, string packageName = "")
        {
            return _assets.ContainsKey(location) && !_failLocations.Contains(location);
        }
    }

    /// <summary>
    /// 内存资源句柄，供 <see cref="InMemoryFUIResourceProvider"/> 返回。
    /// </summary>
    /// <remarks>
    /// 与真实 <see cref="AssetHandle"/> 同构的最窄句柄，供测试程序集持有与释放。
    /// internal 可见，待测试程序集通过 InternalsVisibleTo 访问。
    /// </remarks>
    internal sealed class InMemoryAssetHandle : IFUIAssetHandle
    {
        private UnityEngine.Object _asset;
        private bool _disposed;

        /// <summary>
        /// 构造内存句柄。
        /// </summary>
        /// <param name="asset">资源对象。</param>
        public InMemoryAssetHandle(UnityEngine.Object asset)
        {
            _asset = asset;
        }

        /// <inheritdoc/>
        public UnityEngine.Object AssetObject => _disposed ? null : _asset;

        /// <inheritdoc/>
        public bool IsDone => true;

        /// <inheritdoc/>
        public string LastError => string.Empty;

        /// <inheritdoc/>
        public TAsset GetAssetObject<TAsset>() where TAsset : UnityEngine.Object
        {
            return _disposed ? null : _asset as TAsset;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _asset = null;
        }
    }
}
