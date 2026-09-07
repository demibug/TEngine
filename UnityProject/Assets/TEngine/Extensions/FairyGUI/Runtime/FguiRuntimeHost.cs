using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UguiImage = UnityEngine.UI.Image;

namespace TEngine.FairyGUIIntegration
{
    /// <summary>
    /// Presentation boundary used by the hotfix window module. The production implementation owns FairyGUI Stage;
    /// tests can provide an in-memory layer host without invoking Play Mode-only Unity lifecycle APIs.
    /// </summary>
    public interface IFguiRuntimeHost
    {
        bool IsSuspended { get; }
        GComponent GetLayer(FguiLayer layer);
        void SetModalActive(bool active);
        IDisposable SuspendPresentation();
        void Shutdown();
    }

    public enum FguiLayer
    {
        Bottom,
        UI,
        Top,
        Tips,
        System
    }

    public sealed class FguiRuntimeHost : MonoBehaviour, IFguiRuntimeHost
    {
        private readonly Dictionary<FguiLayer, GComponent> _layers = new Dictionary<FguiLayer, GComponent>();
        private FguiSettings _settings;
        private GameObject _shieldObject;
        private FguiInputBridge _shield;
        private bool _ownsStage;
        private bool _stageReady;
        private int _previousStageLayer;
        private int _previousStageCullingMask;
        private float _previousStageCameraDepth;
        private bool _previousStageCameraEnabled;
        private bool _previousStageCameraBehaviourEnabled;
        private StageEngine _stageEngine;
        private StageCamera _stageCameraBehaviour;
        private bool _initialized;
        private bool _shutdown;
        private bool _destroying;
        private int _modalCount;
        private int _suspendCount;
        private Rect _lastSafeArea;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Action _shutdownAction;

        public static FguiRuntimeHost Create(FguiSettings settings, Action shutdownAction)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var hostObject = new GameObject("[TEngine.FairyGUI]");
            if (Application.isPlaying)
                DontDestroyOnLoad(hostObject);
            FguiRuntimeHost host = hostObject.AddComponent<FguiRuntimeHost>();
            try
            {
                host.Initialize(settings, shutdownAction);
                return host;
            }
            catch
            {
                host.Shutdown();
                throw;
            }
        }

        public bool IsSuspended => _suspendCount > 0;

        public GComponent GetLayer(FguiLayer layer)
        {
            if (!_initialized || _shutdown)
                throw new InvalidOperationException("FairyGUI runtime host is not active.");
            return _layers[layer];
        }

        public void SetModalActive(bool active)
        {
            int previousCount = _modalCount;
            _modalCount = Mathf.Max(0, _modalCount + (active ? 1 : -1));
            if (previousCount == 0 && _modalCount > 0)
                _shield?.CancelUguiInteraction();
        }

        public IDisposable SuspendPresentation()
        {
            if (_shutdown)
                return FguiPresentationLease.Empty;

            _suspendCount++;
            ApplyPresentationState();
            return new FguiPresentationLease(this);
        }

        public void Shutdown()
        {
            if (_shutdown)
                return;
            _shutdown = true;

            RootModule.BeforeShutdown -= HandleBeforeShutdown;
            if (_shield != null)
                _shield.Enabled = false;

            foreach (GComponent layer in _layers.Values)
            {
                try
                {
                    if (layer != null && !layer.isDisposed)
                        layer.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Failed to dispose FairyGUI runtime layer: {exception}");
                }
            }
            _layers.Clear();

            if (_shieldObject != null)
                DestroyObject(_shieldObject);
            _shieldObject = null;
            _shield = null;

            if (_ownsStage && _stageReady)
            {
                if (_stageEngine != null)
                    _stageEngine.enabled = false;
                if (StageCamera.main != null)
                    StageCamera.main.enabled = false;
                if (_stageCameraBehaviour != null)
                    _stageCameraBehaviour.enabled = false;
            }
            else if (_stageReady)
            {
                Stage.inst.layer = _previousStageLayer;
                if (StageCamera.main != null)
                {
                    StageCamera.main.cullingMask = _previousStageCullingMask;
                    StageCamera.main.depth = _previousStageCameraDepth;
                    StageCamera.main.enabled = _previousStageCameraEnabled;
                }
                if (_stageCameraBehaviour != null)
                    _stageCameraBehaviour.enabled = _previousStageCameraBehaviourEnabled;
            }

            _shutdownAction = null;
            _initialized = false;
            if (!_destroying)
                DestroyObject(gameObject);
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(target);
            else
#endif
                Object.Destroy(target);
        }

        private void Initialize(FguiSettings settings, Action shutdownAction)
        {
            _settings = settings;
            _shutdownAction = shutdownAction;
            if (settings.DesignWidth <= 0 || settings.DesignHeight <= 0)
                throw new InvalidOperationException("FairyGUI design resolution must be positive.");
            int renderLayer = LayerMask.NameToLayer(settings.RenderLayerName);
            if (renderLayer < 0)
                throw new InvalidOperationException(
                    $"FairyGUI render layer '{settings.RenderLayerName}' is not defined in TagManager.");
            GameObject existingStageCamera = GameObject.Find(StageCamera.Name);
            _ownsStage = existingStageCamera == null ||
                         existingStageCamera.GetComponent<FguiOwnedStageMarker>() != null;

            GRoot root = GRoot.inst;
            _stageReady = true;
            Stage stage = Stage.inst;
            _stageEngine = stage.gameObject.GetComponent<StageEngine>();
            _previousStageLayer = stage.layer;
            stage.layer = renderLayer;
            stage.gameObject.SetActive(true);
            if (StageCamera.main != null)
            {
                if (existingStageCamera == null)
                    StageCamera.main.gameObject.AddComponent<FguiOwnedStageMarker>();
                _stageCameraBehaviour = StageCamera.main.GetComponent<StageCamera>();
                _previousStageCameraBehaviourEnabled = _stageCameraBehaviour != null && _stageCameraBehaviour.enabled;
                if (_stageCameraBehaviour != null)
                    _stageCameraBehaviour.enabled = true;
                if (_stageEngine != null && _ownsStage)
                    _stageEngine.enabled = true;
                _previousStageCullingMask = StageCamera.main.cullingMask;
                _previousStageCameraDepth = StageCamera.main.depth;
                _previousStageCameraEnabled = StageCamera.main.enabled;
                StageCamera.main.gameObject.SetActive(true);
                StageCamera.main.enabled = true;
                StageCamera.main.depth = settings.StageCameraDepth;
                StageCamera.main.cullingMask = 1 << renderLayer;
            }

            root.SetContentScaleFactor(settings.DesignWidth, settings.DesignHeight, settings.ScreenMatchMode);
            foreach (FguiLayer layerId in Enum.GetValues(typeof(FguiLayer)))
            {
                var layer = new GComponent { name = "TEngine." + layerId, touchable = true };
                layer.opaque = false;
                root.AddChild(layer);
                _layers.Add(layerId, layer);
            }

            CreateInputShield();
            RootModule.BeforeShutdown += HandleBeforeShutdown;
            _initialized = true;
            UpdateSafeArea(true);
            ApplyPresentationState();
        }

        private void Update()
        {
            if (_initialized && !_shutdown)
                UpdateSafeArea(false);
        }

        private void OnDestroy()
        {
            _destroying = true;
            Shutdown();
        }

        private void HandleBeforeShutdown()
        {
            try
            {
                _shutdownAction?.Invoke();
            }
            finally
            {
                Shutdown();
            }
        }

        private void CreateInputShield()
        {
            _shieldObject = new GameObject("TEngine.FairyGUI.InputShield", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(UguiImage), typeof(FguiInputBridge));
            if (Application.isPlaying)
                DontDestroyOnLoad(_shieldObject);

            Canvas canvas = _shieldObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1;

            RectTransform rect = _shieldObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            UguiImage image = _shieldObject.GetComponent<UguiImage>();
            image.color = Color.clear;
            image.raycastTarget = true;

            _shield = _shieldObject.GetComponent<FguiInputBridge>();
            _shield.Configure(ShouldBlockUgui, _settings.ClearUguiSelectionOnCapture);
        }

        private bool ShouldBlockUgui(Vector2 screenPoint)
        {
            if (!_initialized || _shutdown || IsSuspended)
                return false;
            if (_modalCount > 0)
                return true;

            Vector2 stagePoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            DisplayObject hit = Stage.inst.HitTest(stagePoint, true);
            return hit != null && hit != Stage.inst;
        }

        private void UpdateSafeArea(bool force)
        {
            Rect safeArea = _settings.RespectSafeArea ? Screen.safeArea : new Rect(0, 0, Screen.width, Screen.height);
            if (!force && _lastScreenWidth == Screen.width && _lastScreenHeight == Screen.height &&
                _lastSafeArea == safeArea)
                return;

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            _lastSafeArea = safeArea;

            float scale = Mathf.Max(0.0001f, UIContentScaler.scaleFactor);
            float x = safeArea.xMin / scale;
            float y = (Screen.height - safeArea.yMax) / scale;
            float width = safeArea.width / scale;
            float height = safeArea.height / scale;
            foreach (GComponent layer in _layers.Values)
            {
                layer.SetXY(x, y);
                layer.SetSize(width, height);
            }
        }

        private void ResumePresentation()
        {
            if (_suspendCount == 0)
                return;
            _suspendCount--;
            ApplyPresentationState();
        }

        private void ApplyPresentationState()
        {
            bool visible = !_shutdown && !IsSuspended;
            foreach (GComponent layer in _layers.Values)
                layer.visible = visible;
            if (_shield != null)
                _shield.Enabled = visible;
            if (StageCamera.main != null)
                StageCamera.main.enabled = visible;
            if (_stageEngine != null && _ownsStage)
                _stageEngine.enabled = visible;
        }

        private sealed class FguiPresentationLease : IDisposable
        {
            public static readonly FguiPresentationLease Empty = new FguiPresentationLease(null);
            private FguiRuntimeHost _owner;

            public FguiPresentationLease(FguiRuntimeHost owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                FguiRuntimeHost owner = _owner;
                _owner = null;
                owner?.ResumePresentation();
            }
        }
    }

    public sealed class FguiOwnedStageMarker : MonoBehaviour { }

    public sealed class FguiInputBridge : MonoBehaviour, ICanvasRaycastFilter, IPointerDownHandler,
        IPointerUpHandler, IDragHandler, IScrollHandler
    {
        private Func<Vector2, bool> _predicate;
        private bool _clearSelection;

        public bool Enabled { get; set; }

        public void Configure(Func<Vector2, bool> predicate, bool clearSelection)
        {
            _predicate = predicate;
            _clearSelection = clearSelection;
            Enabled = true;
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            return Enabled && _predicate != null && _predicate(screenPoint);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_clearSelection && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        public void CancelUguiInteraction()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return;

            eventSystem.SetSelectedGameObject(null);
            BaseInputModule inputModule = eventSystem.currentInputModule;
            if (inputModule != null && inputModule.isActiveAndEnabled)
            {
                // StandaloneInputModule clears its retained pointer/drag data during deactivation.
                inputModule.DeactivateModule();
                inputModule.ActivateModule();
            }
            eventSystem.SetSelectedGameObject(null);
        }

        public void OnPointerUp(PointerEventData eventData) { }
        public void OnDrag(PointerEventData eventData) { }
        public void OnScroll(PointerEventData eventData) { }
    }
}
