using System;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameBattle
{
    /// <summary>
    /// 持有单场景战斗世界的模块级运行时根节点。
    /// </summary>
    internal sealed class BattleWorldHost
    {
        internal const string WORLD_ROOT_NAME = "BattleWorldRoot";
        internal const string MAP_ADDRESS = "BattleMap0";

        private readonly Func<Transform, UniTask<GameObject>> _loadOverride;
        private IResourceModule _resourceModule;
        private GameObject _worldRootObject;
        private GameObject _mapInstance;
        private AssetsReference _mapResourceReference;
        private BattleMapBindings _bindings;
        private AsyncLazy<GameObject> _worldLoadTask;
        private string _currentResourceAddress;
        private Texture2D _placementTexture;
        private Sprite _placementSprite;
        private AssetHandle _spaceTileHandle;
        private Sprite _spaceTileSprite;
        private int _spaceTileMapIndex = -1;
        private CameraSnapshot _cameraSnapshot;
        private bool _hasCameraSnapshot;

        /// <summary>地图加载代数。Release 或地图实例替换时递增；携带旧代数的迟到加载必须销毁实例且不得提交。</summary>
        private int _loadGeneration;

        internal BattleWorldHost()
        {
        }

        internal BattleWorldHost(Func<Transform, UniTask<GameObject>> loadOverride)
        {
            _loadOverride = loadOverride ?? throw new ArgumentNullException(nameof(loadOverride));
        }

        public Transform WorldRoot => _worldRootObject == null
            ? null
            : _worldRootObject.transform;

        public bool IsCreated => _worldRootObject != null;

        public GameObject MapInstance => _mapInstance;

        /// <summary>
        /// LoadGameObjectAsync 附加的自动资源引用，随地图实例销毁而释放。
        /// </summary>
        public AssetsReference MapResourceReference => _mapResourceReference;

        public BattleMapBindings Bindings => _bindings;

        /// <summary>
        /// 创建或复用非激活的运行时战斗根节点。
        /// </summary>
        internal Transform EnsureRoot()
        {
            if (_worldRootObject == null)
            {
                _worldRootObject = new GameObject(WORLD_ROOT_NAME)
                {
                    hideFlags = HideFlags.DontSave,
                };
            }

            _worldRootObject.SetActive(false);
            return _worldRootObject.transform;
        }

        /// <summary>
        /// 加载或复用 BattleMap0 实例（兼容 map0 数据入口）；并发调用共享同一个加载任务。
        /// </summary>
        internal async UniTask<GameObject> EnsureWorldAsync()
        {
            Transform root = EnsureRoot();
            if (_mapInstance != null)
            {
                return _mapInstance;
            }

            if (_worldLoadTask == null)
            {
                int generation = _loadGeneration;
                _worldLoadTask = UniTask.Lazy(() => LoadWorldAsync(root, generation));
            }

            return await _worldLoadTask.Task;
        }

        /// <summary>
        /// 按地图运行快照的 ResourceAddress 加载或复用世界实例，并按当前 MapData 重建绑定。
        /// </summary>
        /// <param name="map">当前地图运行快照（尺寸、cell 宽高、双路端点、资源地址）。</param>
        /// <returns>已就绪并完成当前地图绑定的世界实例。</returns>
        /// <remarks>
        /// <para><b>地址复用/替换（design.md 决策 4）：</b></para>
        /// <list type="bullet">
        /// <item>地址相同：复用已加载实例，仅按当前 MapData 重建绑定。</item>
        /// <item>地址不同：先清理动态根与当前绑定，Destroy 旧实例（AssetsReference 自动归还引用），
        /// 再异步加载新实例。</item>
        /// <item>加载或绑定失败：finally 销毁孤儿实例并清空加载任务，允许重试。</item>
        /// <item>加载代（<see cref="_loadGeneration"/>）：Release 或地图实例替换时递增。迟到的
        /// GameObject 加载若携带旧代数，必须 Destroy 实例且不得提交/赋值。</item>
        /// </list>
        /// </remarks>
        internal async UniTask<GameObject> EnsureWorldForMapAsync(MapData map)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            string address = map.ResourceAddress;
            if (string.IsNullOrEmpty(address))
            {
                // 寻址无效：由入口处理器映射为 AssetMissing。
                throw new BattleMapResourceAddressException(
                    "地图运行快照 ResourceAddress 为空，无法加载世界",
                    address ?? string.Empty);
            }

            Transform root = EnsureRoot();

            // 若存在仍未提交实例的在途加载任务，先等待其收敛（可能属于另一地址 A）。
            // 结束后按当前地址走单一替换流程，避免把地址 A 的任务/实例当作地址 B；
            // 在途任务失败时其 finally 已对称清理，此处继续加载目标地址即可。
            if (_mapInstance == null && _worldLoadTask != null)
            {
                try
                {
                    await _worldLoadTask.Task;
                }
                catch (Exception)
                {
                    // 已清理，继续按当前地址替换加载。
                }
            }

            // 地址不同：先清理动态根和当前绑定，销毁旧实例，再加载新实例。
            if (_mapInstance != null && !string.Equals(
                    _currentResourceAddress, address, StringComparison.Ordinal))
            {
                ClearDynamicRoots();
                DestroyMapInstance();
            }

            if (_mapInstance == null)
            {
                if (_worldLoadTask == null)
                {
                    int generation = _loadGeneration;
                    _worldLoadTask = UniTask.Lazy(() => LoadWorldForMapAsync(root, address, generation));
                }

                GameObject loaded = await _worldLoadTask.Task;
                if (_mapInstance == null)
                {
                    throw new BattleMapLoadException(
                        $"资源加载未提交有效实例：{address}",
                        address);
                }

                _currentResourceAddress = address;
            }

            // 复用或新加载后，统一按当前地图重建绑定（Bindings 不再作为跨局地图状态缓存）。
            BattleMapBindingResult bindingResult =
                BattleMapBindings.TryCreate(_mapInstance.transform, map);
            if (!bindingResult.IsValid)
            {
                // 绑定失败：销毁孤儿实例并清空加载状态，允许重试。
                // 缺节点映射 AssetMissing，重复/几何无效映射 PartialInitializationFailed。
                bool isMissingNode = bindingResult.MissingPaths.Count > 0;
                string nodePath = isMissingNode
                    ? bindingResult.MissingPaths[0]
                    : (bindingResult.DuplicatePaths.Count > 0
                        ? bindingResult.DuplicatePaths[0]
                        : (bindingResult.InvalidPaths.Count > 0
                            ? bindingResult.InvalidPaths[0]
                            : string.Empty));
                string diagnostic = bindingResult.DiagnosticMessage;
                DestroyMapInstance();
                throw new BattleMapBindingException(
                    $"{address} 节点绑定失败：{diagnostic}",
                    address,
                    nodePath,
                    isMissingNode);
            }

            _bindings = bindingResult.Bindings;
            _currentResourceAddress = address;
            return _mapInstance;
        }

        internal void ActivateWorld()
        {
            if (_worldRootObject == null || _mapInstance == null || _bindings == null)
            {
                throw new InvalidOperationException("战斗世界尚未完成加载与绑定。");
            }

            _worldRootObject.SetActive(true);
        }

        internal void ApplyBattleCamera()
        {
            ApplyBattleCamera(Camera.main);
        }

        internal void ApplyBattleCamera(Camera camera)
        {
            if (camera == null)
            {
                throw new InvalidOperationException("main.unity 中未找到 MainCamera。");
            }

            if (camera.gameObject.name == FairyGUI.StageCamera.Name
                || camera.GetComponent<FairyGUI.StageCamera>() != null)
            {
                throw new InvalidOperationException("不得使用或修改 FairyGUI StageCamera。");
            }

            if (!_hasCameraSnapshot)
            {
                _cameraSnapshot = new CameraSnapshot(camera);
                _hasCameraSnapshot = true;
            }
            else if (_cameraSnapshot.Camera != camera)
            {
                throw new InvalidOperationException("战斗期间 MainCamera 实例发生变化。");
            }

            camera.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, -10f),
                Quaternion.identity);
            camera.orthographic = true;
            camera.orthographicSize = camera.aspect > 0f
                ? 4f / camera.aspect
                : 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }

        internal void RestoreCamera()
        {
            if (!_hasCameraSnapshot)
            {
                return;
            }

            _cameraSnapshot.Restore();
            _cameraSnapshot = default;
            _hasCameraSnapshot = false;
        }

        internal void HideWorld()
        {
            if (_worldRootObject != null)
            {
                _worldRootObject.SetActive(false);
            }
        }

        internal void ClearDynamicRoots(bool destroyImmediate = false)
        {
            if (_bindings == null)
            {
                return;
            }

            ClearChildren(_bindings.EnemyRoot, destroyImmediate);
            ClearChildren(_bindings.SoldierRoot, destroyImmediate);
            ClearChildren(_bindings.ProjectileRoot, destroyImmediate);
            ClearChildren(_bindings.EffectRoot, destroyImmediate);
            ClearChildren(_bindings.UnitSlotRoot, destroyImmediate);
        }

        /// <summary>
        /// 清空当前单局地图绑定（退出/回滚时调用；地图实例与资源引用保留以便地址复用）。
        /// <para>必须在该局动态根清理完成后调用（动态根经绑定节点定位）。</para>
        /// </summary>
        internal void ClearBindings()
        {
            _bindings = null;
        }

        /// <summary>
        /// 在原生战斗场景中显示当前阵营可放置的棋盘格。
        /// </summary>
        internal void ShowPlacementSlots(BattleMapState mapState, bool playerSide)
        {
            MapData map = mapState?.Template;
            if (_bindings == null || map == null)
            {
                return;
            }

            ClearChildren(_bindings.UnitSlotRoot, destroyImmediate: false);
            EnsurePlacementSprite();

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    GridPosition position = new GridPosition(x, y);
                    if (!mapState.IsBuildableForSide(playerSide, position))
                    {
                        continue;
                    }

                    if (mapState.IsOpened(position) && _spaceTileSprite != null)
                    {
                        GameObject openedTile = new GameObject($"OpenedTile_{x}_{y}")
                        {
                            hideFlags = HideFlags.DontSave,
                        };
                        openedTile.transform.SetParent(_bindings.UnitSlotRoot, false);
                        openedTile.transform.position = _bindings.CellToWorld(x, y, 0.01f);
                        SpriteRenderer tileRenderer = openedTile.AddComponent<SpriteRenderer>();
                        tileRenderer.sprite = _spaceTileSprite;
                        tileRenderer.sortingOrder = 7;
                    }

                    GameObject slot = new GameObject($"PlayerSlot_{x}_{y}")
                    {
                        hideFlags = HideFlags.DontSave,
                    };
                    slot.transform.SetParent(_bindings.UnitSlotRoot, false);
                    slot.transform.position = _bindings.CellToWorld(x, y, 0.02f);
                    slot.transform.localScale = new Vector3(0.86f, 0.86f, 1f);

                    SpriteRenderer renderer = slot.AddComponent<SpriteRenderer>();
                    renderer.sprite = _placementSprite;
                    renderer.color = new Color(0.12f, 0.85f, 0.95f, 0.32f);
                    renderer.sortingOrder = 8;
                }
            }
        }

        /// <summary>预加载当前地图的已开垦地块贴图，句柄由世界宿主统一持有和释放。</summary>
        internal async UniTask PreloadTilePresentationAsync(MapData map)
        {
            if (map == null || _spaceTileMapIndex == map.MapIndex && _spaceTileSprite != null)
            {
                return;
            }

            _spaceTileHandle?.Release();
            _spaceTileHandle = null;
            _spaceTileSprite = null;
            _spaceTileMapIndex = -1;

            if (_resourceModule == null)
            {
                _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            }

            string address = $"Sprites/Extracted/Map/space_{map.MapIndex}";
            if (_resourceModule == null || !_resourceModule.CheckLocationValid(address))
            {
                Log.Warning($"[BattleWorldHost] 开垦地块贴图不可用：{address}");
                return;
            }

            AssetHandle handle = null;
            try
            {
                handle = _resourceModule.LoadAssetAsyncHandle<Sprite>(address);
                await handle.Task.AsUniTask();
                Sprite sprite = handle.AssetObject as Sprite;
                if (sprite == null)
                {
                    handle.Release();
                    Log.Warning($"[BattleWorldHost] 开垦地块贴图加载结果不是 Sprite：{address}");
                    return;
                }

                _spaceTileHandle = handle;
                _spaceTileSprite = sprite;
                _spaceTileMapIndex = map.MapIndex;
            }
            catch (Exception ex)
            {
                handle?.Release();
                Log.Warning($"[BattleWorldHost] 开垦地块贴图加载失败：{address}, {ex.Message}");
            }
        }

        /// <summary>
        /// 完整释放模块级地图实例、自动资源引用和运行时根节点。
        /// </summary>
        internal void Release(bool destroyImmediate = false)
        {
            RestoreCamera();
            HideWorld();
            ClearDynamicRoots(destroyImmediate);
            _loadGeneration++;

            if (_mapInstance != null)
            {
                _mapInstance.transform.SetParent(null, false);
                DestroyInstance(_mapInstance, destroyImmediate);
            }

            _mapInstance = null;
            _mapResourceReference = null;
            _bindings = null;
            _worldLoadTask = null;
            _currentResourceAddress = null;
            _resourceModule = null;

            _spaceTileHandle?.Release();
            _spaceTileHandle = null;
            _spaceTileSprite = null;
            _spaceTileMapIndex = -1;

            DestroyUnityObject(_placementSprite, destroyImmediate);
            DestroyUnityObject(_placementTexture, destroyImmediate);
            _placementSprite = null;
            _placementTexture = null;

            if (_worldRootObject != null)
            {
                DestroyInstance(_worldRootObject, destroyImmediate);
                _worldRootObject = null;
            }
        }

        private async UniTask<GameObject> LoadWorldAsync(Transform root, int generation)
        {
            GameObject instance = null;
            try
            {
                instance = _loadOverride == null
                    ? await LoadFromResourceModuleAsync(root, MAP_ADDRESS)
                    : await _loadOverride(root);
                if (generation != _loadGeneration)
                {
                    if (instance != null)
                    {
                        DestroyInstance(instance);
                    }

                    instance = null;
                    throw new BattleMapLoadException(
                        $"战斗地图资源加载已失效（地图已被替换或释放）：{MAP_ADDRESS}",
                        MAP_ADDRESS);
                }

                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"资源加载未返回有效实例：{MAP_ADDRESS}");
                }

                BattleMapBindingResult bindingResult =
                    BattleMapBindings.TryCreate(instance.transform);
                if (!bindingResult.IsValid)
                {
                    DestroyInstance(instance);
                    instance = null;
                    throw new InvalidOperationException(
                        $"{MAP_ADDRESS} 节点绑定失败：{bindingResult.DiagnosticMessage}");
                }

                _mapInstance = instance;
                _mapResourceReference = instance.GetComponent<AssetsReference>();
                _bindings = bindingResult.Bindings;
                _currentResourceAddress = MAP_ADDRESS;
                GameObject loadedInstance = instance;
                instance = null;
                return loadedInstance;
            }
            finally
            {
                // 实例在写入模块级字段前发生校验异常时，所有权仍归本方法。
                // 必须立即销毁，以触发 AssetsReference 自动归还资源引用，避免重试留下第二张地图。
                if (instance != null && _mapInstance == null)
                {
                    DestroyInstance(instance);
                }

                // 只允许本代加载清理共享任务。旧代加载可能在 Release 后迟到收尾，
                // 此时新一代加载已经写入 _worldLoadTask，不能被旧 finally 覆盖。
                if (generation == _loadGeneration && _mapInstance == null)
                {
                    _worldLoadTask = null;
                    _currentResourceAddress = null;
                }
            }
        }

        private async UniTask<GameObject> LoadWorldForMapAsync(
            Transform root,
            string address,
            int generation)
        {
            GameObject instance = null;
            try
            {
                try
                {
                    instance = _loadOverride == null
                        ? await LoadFromResourceModuleAsync(root, address)
                        : await _loadOverride(root);
                }
                catch (BattleMapResourceAddressException)
                {
                    // YooAsset 寻址无效属于资源缺失，保留结构化异常供入口映射 AssetMissing。
                    throw;
                }
                catch (Exception ex)
                {
                    // 地址有效但加载失败：转换为结构化加载异常（映射 AssetLoadFailed）。
                    throw new BattleMapLoadException(
                        $"战斗地图资源加载失败：{address}（{ex.GetType().Name}: {ex.Message}）",
                        address,
                        ex);
                }

                // 迟到的加载（Release 或地图替换已递增代数）：实例已失效，必须销毁且不得提交。
                if (generation != _loadGeneration)
                {
                    if (instance != null)
                    {
                        DestroyInstance(instance);
                    }

                    instance = null;
                    throw new BattleMapLoadException(
                        $"战斗地图资源加载已失效（地图已被替换或释放）：{address}",
                        address);
                }

                if (instance == null)
                {
                    throw new BattleMapLoadException(
                        $"资源加载未返回有效实例：{address}",
                        address);
                }

                _mapInstance = instance;
                _mapResourceReference = instance.GetComponent<AssetsReference>();
                _currentResourceAddress = address;
                GameObject loadedInstance = instance;
                instance = null;
                return loadedInstance;
            }
            finally
            {
                // 加载或校验失败时所有权仍归本方法：立即销毁孤儿并清空加载状态，允许重试。
                if (instance != null && _mapInstance == null)
                {
                    DestroyInstance(instance);
                }

                // 只清理本代状态，避免迟到的旧加载把新一代在途任务清空。
                if (generation == _loadGeneration && _mapInstance == null)
                {
                    _worldLoadTask = null;
                    _currentResourceAddress = null;
                }
            }
        }

        /// <summary>销毁当前地图实例并清空绑定、引用与加载状态（地址替换/绑定失败用）。</summary>
        private void DestroyMapInstance()
        {
            _loadGeneration++;

            if (_mapInstance != null)
            {
                _mapInstance.transform.SetParent(null, false);
                DestroyInstance(_mapInstance);
            }

            _mapInstance = null;
            _mapResourceReference = null;
            _bindings = null;
            _worldLoadTask = null;
            _currentResourceAddress = null;
        }

        private async UniTask<GameObject> LoadFromResourceModuleAsync(
            Transform root,
            string location)
        {
            if (_resourceModule == null)
            {
                _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            }

            if (_resourceModule == null)
            {
                throw new InvalidOperationException("IResourceModule 尚未初始化。");
            }

            if (!_resourceModule.CheckLocationValid(location))
            {
                throw new BattleMapResourceAddressException(
                    $"战斗地图资源地址未被 YooAsset 收集或不存在：{location}",
                    location);
            }

            return await _resourceModule.LoadGameObjectAsync(location, root);
        }

        private static void ClearChildren(Transform root, bool destroyImmediate)
        {
            while (root != null && root.childCount > 0)
            {
                Transform child = root.GetChild(root.childCount - 1);
                child.SetParent(null, false);
                DestroyInstance(child.gameObject, destroyImmediate);
            }
        }

        private void EnsurePlacementSprite()
        {
            if (_placementSprite != null)
            {
                return;
            }

            _placementTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "BattlePlacementSlotTexture",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _placementTexture.SetPixel(0, 0, Color.white);
            _placementTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _placementSprite = Sprite.Create(
                _placementTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            _placementSprite.name = "BattlePlacementSlotSprite";
            _placementSprite.hideFlags = HideFlags.DontSave;
        }

        private static void DestroyInstance(GameObject instance, bool destroyImmediate = false)
        {
            if (instance == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (destroyImmediate || !Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                return;
            }
#endif
            UnityEngine.Object.Destroy(instance);
        }

        private static void DestroyUnityObject(
            UnityEngine.Object instance,
            bool destroyImmediate = false)
        {
            if (instance == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (destroyImmediate || !Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                return;
            }
#endif
            UnityEngine.Object.Destroy(instance);
        }

        private readonly struct CameraSnapshot
        {
            public readonly Camera Camera;
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly bool _orthographic;
            private readonly float _orthographicSize;
            private readonly CameraClearFlags _clearFlags;
            private readonly Color _backgroundColor;

            public CameraSnapshot(Camera camera)
            {
                Camera = camera;
                _position = camera.transform.position;
                _rotation = camera.transform.rotation;
                _orthographic = camera.orthographic;
                _orthographicSize = camera.orthographicSize;
                _clearFlags = camera.clearFlags;
                _backgroundColor = camera.backgroundColor;
            }

            public void Restore()
            {
                if (Camera == null)
                {
                    return;
                }

                Camera.transform.SetPositionAndRotation(_position, _rotation);
                Camera.orthographic = _orthographic;
                Camera.orthographicSize = _orthographicSize;
                Camera.clearFlags = _clearFlags;
                Camera.backgroundColor = _backgroundColor;
            }
        }
    }

    /// <summary>
    /// 战斗地图资源寻址无效（地址为空、未被 YooAsset 收集或不存在）的结构化异常。
    /// 由 <see cref="BattleWorldHost"/> 在加载前抛出，入口处理器映射为
    /// <see cref="BattleErrorCode.AssetMissing"/>（design.md 决策 6）。
    /// </summary>
    internal sealed class BattleMapResourceAddressException : Exception
    {
        /// <summary>无法解析到有效资源的地址。</summary>
        public string ResourceAddress { get; }

        public BattleMapResourceAddressException(string message, string resourceAddress)
            : base(message)
        {
            ResourceAddress = resourceAddress ?? string.Empty;
        }
    }

    /// <summary>
    /// 战斗地图资源地址有效但加载失败的结构化异常。
    /// 由 <see cref="BattleWorldHost"/> 在资源加载期间抛出，入口处理器映射为
    /// <see cref="BattleErrorCode.AssetLoadFailed"/>（design.md 决策 6）。
    /// </summary>
    internal sealed class BattleMapLoadException : Exception
    {
        /// <summary>本次加载的资源地址。</summary>
        public string ResourceAddress { get; }

        public BattleMapLoadException(
            string message,
            string resourceAddress,
            Exception innerException = null)
            : base(message, innerException)
        {
            ResourceAddress = resourceAddress ?? string.Empty;
        }
    }

    /// <summary>
    /// 战斗地图节点/运行几何绑定失败的结构化异常。
    /// 由 <see cref="BattleWorldHost"/> 在绑定阶段抛出，入口处理器按 <see cref="IsMissingNode"/>
    /// 映射为 <see cref="BattleErrorCode.AssetMissing"/>（缺节点）或
    /// <see cref="BattleErrorCode.PartialInitializationFailed"/>（重复/几何无效），
    /// 失败阶段固定为 <see cref="BattleFailureStage.MapBinding"/>（design.md 决策 6）。
    /// </summary>
    internal sealed class BattleMapBindingException : Exception
    {
        /// <summary>绑定失败涉及的地图资源地址。</summary>
        public string ResourceAddress { get; }

        /// <summary>首个失败节点路径（缺节点/重复/几何无效）。</summary>
        public string NodePath { get; }

        /// <summary>true 表示缺节点（映射 AssetMissing）；false 表示重复/几何无效（映射 PartialInitializationFailed）。</summary>
        public bool IsMissingNode { get; }

        public BattleMapBindingException(
            string message,
            string resourceAddress,
            string nodePath,
            bool isMissingNode,
            Exception innerException = null)
            : base(message, innerException)
        {
            ResourceAddress = resourceAddress ?? string.Empty;
            NodePath = nodePath ?? string.Empty;
            IsMissingNode = isMissingNode;
        }
    }
}
