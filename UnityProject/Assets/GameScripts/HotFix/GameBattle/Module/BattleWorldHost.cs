using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace GameBattle
{
    /// <summary>
    /// 持有单场景战斗世界的模块级运行时根节点。
    /// </summary>
    internal sealed class BattleWorldHost
    {
        internal const string WORLD_ROOT_NAME = "BattleWorldRoot";
        internal const string MAP_ADDRESS = "BattleMap0";

        private readonly Func<Transform, CancellationToken, UniTask<GameObject>> _loadOverride;
        private IResourceModule _resourceModule;
        private GameObject _worldRootObject;
        private GameObject _mapInstance;
        private AssetsReference _mapResourceReference;
        private BattleMapBindings _bindings;
        private AsyncLazy<GameObject> _worldLoadTask;
        private Texture2D _placementTexture;
        private Sprite _placementSprite;
        private CameraSnapshot _cameraSnapshot;
        private bool _hasCameraSnapshot;

        internal BattleWorldHost()
        {
        }

        internal BattleWorldHost(
            Func<Transform, CancellationToken, UniTask<GameObject>> loadOverride)
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
        /// 加载或复用 BattleMap0 实例；并发调用共享同一个加载任务。
        /// </summary>
        internal async UniTask<GameObject> EnsureWorldAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Transform root = EnsureRoot();
            if (_mapInstance != null)
            {
                return _mapInstance;
            }

            if (_worldLoadTask == null)
            {
                _worldLoadTask = UniTask.Lazy(
                    () => LoadWorldAsync(root, cancellationToken));
            }

            return await _worldLoadTask.Task.AttachExternalCancellation(cancellationToken);
        }

        internal void ActivateWorld()
        {
            if (_worldRootObject == null || _mapInstance == null || _bindings == null)
            {
                throw new InvalidOperationException("BattleMap0 尚未完成加载与绑定。");
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
        /// 在原生战斗场景中显示当前阵营可放置的棋盘格。
        /// </summary>
        internal void ShowPlacementSlots(MapData map, bool playerSide)
        {
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
                    if (!map.IsBuildableForSide(playerSide, x, y))
                    {
                        continue;
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

        /// <summary>
        /// 完整释放模块级地图实例、自动资源引用和运行时根节点。
        /// </summary>
        internal void Release(bool destroyImmediate = false)
        {
            RestoreCamera();
            HideWorld();
            ClearDynamicRoots(destroyImmediate);

            if (_mapInstance != null)
            {
                _mapInstance.transform.SetParent(null, false);
                DestroyInstance(_mapInstance, destroyImmediate);
            }

            _mapInstance = null;
            _mapResourceReference = null;
            _bindings = null;
            _worldLoadTask = null;
            _resourceModule = null;

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

        private async UniTask<GameObject> LoadWorldAsync(
            Transform root,
            CancellationToken cancellationToken)
        {
            GameObject instance = null;
            try
            {
                instance = _loadOverride == null
                    ? await LoadFromResourceModuleAsync(root, cancellationToken)
                    : await _loadOverride(root, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
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
                GameObject loadedInstance = instance;
                instance = null;
                return loadedInstance;
            }
            finally
            {
                // 实例在写入模块级字段前发生取消或校验异常时，所有权仍归本方法。
                // 必须立即销毁，以触发 AssetsReference 自动归还资源引用，避免重试留下第二张地图。
                if (instance != null && _mapInstance == null)
                {
                    DestroyInstance(instance);
                }

                if (_mapInstance == null)
                {
                    _worldLoadTask = null;
                }
            }
        }

        private async UniTask<GameObject> LoadFromResourceModuleAsync(
            Transform root,
            CancellationToken cancellationToken)
        {
            if (_resourceModule == null)
            {
                _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            }

            if (_resourceModule == null)
            {
                throw new InvalidOperationException("IResourceModule 尚未初始化。");
            }

            return await _resourceModule.LoadGameObjectAsync(
                MAP_ADDRESS,
                root,
                cancellationToken);
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
}
