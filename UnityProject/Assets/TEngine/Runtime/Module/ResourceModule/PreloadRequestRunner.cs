using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 预加载请求执行器（可回收缓存预热）。
    /// <remarks>
    /// - 先建立去重请求清单，再发起加载；重叠地址只加载一次。
    /// - 每次成功回调先配对归还预加载自己的 spawn；资产留在现有池中，按正常容量/过期策略回收。
    /// - 使用代际隔离晚回调：离开或再次进入后，旧成功仍归还资源，但不得更新新流程状态或触发跳转。
    /// - 成功、失败分别记状态，进度统计终态项，不依赖字典遍历 break。
    /// - 保留“预热尽力而为，全部终态后继续”的启动政策，失败明确记日志。
    /// </remarks>
    /// </summary>
    public sealed class PreloadRequestRunner
    {
        private readonly IResourceModule _resourceModule;
        private readonly Action _allCompletedCallback;

        private readonly Dictionary<string, bool> _terminalFlags = new Dictionary<string, bool>();
        private readonly HashSet<string> _failedLocations = new HashSet<string>();

        private int _generation;
        private bool _allCompletedNotified;

        /// <summary>
        /// 创建预加载请求执行器。
        /// </summary>
        /// <param name="resourceModule">资源模块。</param>
        /// <param name="allCompletedCallback">全部请求到达终态后的回调（仅当前代际触发一次）。</param>
        public PreloadRequestRunner(IResourceModule resourceModule, Action allCompletedCallback = null)
        {
            _resourceModule = resourceModule ?? throw new ArgumentNullException(nameof(resourceModule));
            _allCompletedCallback = allCompletedCallback;
        }

        /// <summary>
        /// 当前代际。
        /// </summary>
        public int Generation => _generation;

        /// <summary>
        /// 是否没有任何请求（空预热）。
        /// </summary>
        public bool IsEmpty => _terminalFlags.Count == 0;

        /// <summary>
        /// 失败的地址集合（仅记录当前代际）。
        /// </summary>
        public IReadOnlyCollection<string> FailedLocations => _failedLocations;

        /// <summary>
        /// 终态进度（0~1）。空清单视为 1。
        /// </summary>
        public float Progress
        {
            get
            {
                if (_terminalFlags.Count <= 0)
                {
                    return 1f;
                }

                int completed = 0;
                foreach (var flag in _terminalFlags.Values)
                {
                    if (flag)
                    {
                        completed++;
                    }
                }

                return (float)completed / _terminalFlags.Count;
            }
        }

        /// <summary>
        /// 是否全部请求到达终态。空清单视为已完成。
        /// </summary>
        public bool AllTerminal
        {
            get
            {
                if (_terminalFlags.Count <= 0)
                {
                    return true;
                }

                foreach (var flag in _terminalFlags.Values)
                {
                    if (!flag)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// 开始一代预热：去重后发起加载。
        /// </summary>
        /// <param name="addresses">要预热的地址集合（可重复，内部去重）。</param>
        public void Begin(IEnumerable<string> addresses)
        {
            _generation++;
            _terminalFlags.Clear();
            _failedLocations.Clear();
            _allCompletedNotified = false;

            if (addresses == null || !ModuleSystem.IsRunning)
            {
                return;
            }

            // 先建立去重请求清单，再发起加载。
            HashSet<string> deduped = new HashSet<string>();
            foreach (var address in addresses)
            {
                if (!string.IsNullOrEmpty(address))
                {
                    deduped.Add(address);
                }
            }

            foreach (var address in deduped)
            {
                _terminalFlags[address] = false;
            }

            foreach (var address in deduped)
            {
                StartRequest(address);
            }
        }

        /// <summary>
        /// 使当前代际失效（离开流程）。
        /// <remarks>晚到的旧回调仍会配对归还资源，但不再更新本执行器状态。</remarks>
        /// </summary>
        public void Invalidate()
        {
            _generation++;
        }

        private void StartRequest(string location)
        {
            int generation = _generation;
            LoadRequestAsync(location, generation).Forget();
        }

        private async UniTask LoadRequestAsync(string location, int generation)
        {
            UnityEngine.Object asset;
            try
            {
                AssetInfo assetInfo = _resourceModule.GetAssetInfo(location);
                Type assetType = assetInfo?.AssetType ?? typeof(UnityEngine.Object);
                asset = await _resourceModule.LoadAssetAsync(location, assetType, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // 同步或异步加载异常都必须收敛为本地址的失败终态，避免 ProcedurePreload 永久等待。
                OnPreloadFailure(generation, location, ex.ToString());
                return;
            }

            if (asset == null)
            {
                OnPreloadFailure(generation, location, "Resource loading returned null.");
                return;
            }

            OnPreloadSuccess(generation, location, asset);
        }

        private void OnPreloadSuccess(int generation, string location, object asset)
        {
            // 每次成功回调先配对归还预加载自己的 spawn（无论是否当前代际）；归还异常记录但不中断。
            if (asset != null)
            {
                try
                {
                    _resourceModule.UnloadAsset(asset);
                }
                catch (Exception ex)
                {
                    LogErrorSafely($"Preload '{location}' return spawn failed: {ex.Message}");
                }
            }

            if (generation != _generation || !ModuleSystem.IsRunning)
            {
                // 晚到成功：资源已归还，不污染新流程状态。
                LogWarningSafely($"Preload '{location}' succeeded after preload context changed; reference returned, state ignored.");
                return;
            }

            Log.Debug("Success preload asset from '{0}'.", location);
            MarkTerminal(location, failed: false);
        }

        private void OnPreloadFailure(int generation, string location, string errorMessage)
        {
            if (generation != _generation || !ModuleSystem.IsRunning)
            {
                LogWarningSafely($"Preload '{location}' failed after preload context changed: {errorMessage}");
                return;
            }

            Log.Warning("Can not preload asset from '{0}' with error message '{1}'.", location, errorMessage);
            MarkTerminal(location, failed: true);
        }

        private void MarkTerminal(string location, bool failed)
        {
            if (failed)
            {
                _failedLocations.Add(location);
            }

            _terminalFlags[location] = true;

            if (!_allCompletedNotified && AllTerminal)
            {
                _allCompletedNotified = true;
                _allCompletedCallback?.Invoke();
            }
        }

        private static void LogErrorSafely(string message)
        {
            try
            {
                Log.Error(message);
            }
            catch
            {
                try { UnityEngine.Debug.LogError(message); }
                catch { }
            }
        }

        private static void LogWarningSafely(string message)
        {
            try
            {
                Log.Warning(message);
            }
            catch
            {
                try { UnityEngine.Debug.LogWarning(message); }
                catch { }
            }
        }
    }
}
