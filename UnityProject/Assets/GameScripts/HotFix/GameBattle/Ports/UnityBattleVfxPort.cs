using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameBattle
{
    /// <summary>
    /// 将可选战斗特效挂到 BattleMap0 的 EffectRoot。
    /// </summary>
    internal sealed class UnityBattleVfxPort : IBattleVfxPort
    {
        private const string ArrowAddress = "Arrow";
        private const string MergeEffectAddress = "Sprites/Extracted/GameObject/soldier/mergeEff1";

        private readonly BattleMapBindings _bindings;
        private readonly Dictionary<string, string> _requiredPrefabs = new Dictionary<string, string>
        {
            { "projectile_arrow", ArrowAddress },
        };
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        private readonly List<AssetHandle> _assetHandles = new List<AssetHandle>();
        private readonly Dictionary<string, Stack<GameObject>> _pools =
            new Dictionary<string, Stack<GameObject>>();
        private readonly HashSet<GameObject> _activeInstances = new HashSet<GameObject>();

        private bool _preloaded;
        private Sprite _mergeEffectSprite;

        internal UnityBattleVfxPort(BattleMapBindings bindings)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        }

        /// <summary>
        /// 仅 Arrow 是当前可用且必需的预热项；未配置的命中特效不阻塞启动。
        /// </summary>
        public async UniTask PreloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_preloaded)
            {
                return;
            }

            try
            {
                IResourceModule resource = GetRequiredResourceModule();
                foreach (KeyValuePair<string, string> requiredPrefab in _requiredPrefabs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await LoadRequiredPrefabAsync(
                        resource,
                        requiredPrefab.Key,
                        requiredPrefab.Value,
                        cancellationToken);
                }

                // 合并特效：用已提取的 mergeEff1.png 静态贴图做短时效（无 Prefab 时的最小接入）。
                AssetHandle mergeHandle = resource.LoadAssetAsyncHandle<Sprite>(MergeEffectAddress);
                if (mergeHandle != null)
                {
                    _assetHandles.Add(mergeHandle);
                    await UniTask.WaitUntil(() => mergeHandle.IsDone, cancellationToken: cancellationToken);
                    if (mergeHandle.IsValid && mergeHandle.AssetObject is Sprite mergeSprite)
                    {
                        _mergeEffectSprite = mergeSprite;
                    }
                }

                _preloaded = true;
            }
            catch (OperationCanceledException)
            {
                ResetPreload();
                throw;
            }
            catch (BattlePresentationLoadException)
            {
                ResetPreload();
                throw;
            }
            catch (Exception ex)
            {
                ResetPreload();
                throw new BattlePresentationLoadException("preload", "<unknown>", ex);
            }
        }

        public void PlayHitEffect(string vfxId, float x, float y)
        {
            PlayOptionalVfx(vfxId, _bindings.LogicToWorld(x, y));
        }

        public void PlayDeathEffect(string vfxId, float x, float y)
        {
            PlayOptionalVfx(vfxId, _bindings.LogicToWorld(x, y));
        }

        public void PlayAttackEffect(string vfxId, float x, float y)
        {
            PlayOptionalVfx(vfxId, _bindings.LogicToWorld(x, y));
        }

        public void PlaySpawnEffect(string vfxId, int gridX, int gridY)
        {
            if (vfxId == "unit_merge" && _mergeEffectSprite != null)
            {
                PlayMergeSpriteEffect(_bindings.CellToWorld(gridX, gridY));
                return;
            }

            PlayOptionalVfx(vfxId, _bindings.CellToWorld(gridX, gridY));
        }

        /// <summary>
        /// 播放合并特效（mergeEff1.png 静态贴图，短暂显示后自动回收）。
        /// 原工程没有可加载的 unit_merge Prefab，此处用已提取贴图做最小接入。
        /// </summary>
        private void PlayMergeSpriteEffect(Vector3 worldPosition)
        {
            var go = new GameObject("MergeEffect");
            go.transform.SetParent(_bindings.EffectRoot, false);
            go.transform.position = worldPosition;
            go.transform.localScale = Vector3.one;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _mergeEffectSprite;
            renderer.sortingOrder = 20;

            // 短暂显示后自动销毁（无 Coroutine，用 Update 计时）。
            go.AddComponent<AutoDestroyVfx>().Run(durationSeconds: 1f);
        }

        public void Clear()
        {
            foreach (GameObject instance in _activeInstances)
            {
                Destroy(instance);
            }
            _activeInstances.Clear();

            foreach (Stack<GameObject> pool in _pools.Values)
            {
                while (pool.Count > 0)
                {
                    Destroy(pool.Pop());
                }
            }
            _pools.Clear();
            ResetPreload();
        }

        private static IResourceModule GetRequiredResourceModule()
        {
            try
            {
                IResourceModule resource = ModuleSystem.GetModule<IResourceModule>();
                if (resource != null)
                {
                    return resource;
                }
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("resource-module", "<resource-module>", ex);
            }

            throw new BattlePresentationLoadException(
                "resource-module",
                "<resource-module>",
                new InvalidOperationException("IResourceModule 未初始化"));
        }

        private async UniTask LoadRequiredPrefabAsync(
            IResourceModule resource,
            string assetKey,
            string address,
            CancellationToken cancellationToken)
        {
            AssetHandle handle;
            try
            {
                handle = resource.LoadAssetAsyncHandle<GameObject>(address);
                if (handle == null)
                {
                    throw new InvalidOperationException("资源模块返回空 AssetHandle");
                }

                _assetHandles.Add(handle);
                await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("load", address, ex);
            }

            if (!handle.IsValid)
            {
                throw new BattlePresentationLoadException(
                    "validate-handle",
                    address,
                    new InvalidOperationException("AssetHandle 无效"));
            }

            if (!(handle.AssetObject is GameObject prefab) || prefab == null)
            {
                throw new BattlePresentationLoadException(
                    "validate-asset",
                    address,
                    new InvalidCastException("AssetObject 不是 GameObject"));
            }

            ValidateInstantiation(address, prefab);

            _prefabs.Add(assetKey, prefab);
        }

        private void ValidateInstantiation(string address, GameObject prefab)
        {
            GameObject probe = null;
            try
            {
                probe = UnityEngine.Object.Instantiate(prefab, _bindings.EffectRoot);
                if (probe == null)
                {
                    throw new InvalidOperationException("Instantiate 返回空对象");
                }

                probe.SetActive(false);
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("instantiate-validate", address, ex);
            }
            finally
            {
                Destroy(probe);
            }
        }

        private void PlayOptionalVfx(string vfxId, Vector3 worldPosition)
        {
            if (string.IsNullOrEmpty(vfxId) || !_prefabs.TryGetValue(vfxId, out GameObject prefab) || prefab == null)
            {
                return;
            }

            try
            {
                GameObject instance = AcquireFromPool(vfxId, prefab);
                instance.transform.position = worldPosition;
                _activeInstances.Add(instance);

                ParticleSystem particleSystem = instance.GetComponent<ParticleSystem>();
                if (particleSystem != null)
                {
                    particleSystem.Clear(true);
                    particleSystem.Play(true);
                }
            }
            catch (Exception ex)
            {
                throw new BattlePresentationLoadException("instantiate", _requiredPrefabs[vfxId], ex);
            }
        }

        private GameObject AcquireFromPool(string vfxId, GameObject prefab)
        {
            if (!_pools.TryGetValue(vfxId, out Stack<GameObject> pool))
            {
                pool = new Stack<GameObject>();
                _pools.Add(vfxId, pool);
            }

            GameObject instance = null;
            while (pool.Count > 0 && instance == null)
            {
                instance = pool.Pop();
            }

            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(prefab, _bindings.EffectRoot);
            }
            else
            {
                instance.transform.SetParent(_bindings.EffectRoot, false);
            }

            instance.SetActive(true);
            return instance;
        }

        private void ResetPreload()
        {
            for (int index = 0; index < _assetHandles.Count; index++)
            {
                AssetHandle handle = _assetHandles[index];
                if (handle != null && handle.IsValid)
                {
                    handle.Release();
                }
            }

            _assetHandles.Clear();
            _prefabs.Clear();
            _preloaded = false;
        }

        private static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// 短暂显示后自动销毁自身的 VFX 组件（无 Coroutine，用 Update 计时）。
    /// </summary>
    internal sealed class AutoDestroyVfx : MonoBehaviour
    {
        private float _remaining;

        /// <summary>启动自动销毁计时。</summary>
        internal void Run(float durationSeconds)
        {
            _remaining = durationSeconds > 0f ? durationSeconds : 1f;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
