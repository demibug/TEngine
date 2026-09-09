using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using TEngine;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameLogic
{
    public sealed class UIWindowLoadException : Exception
    {
        public string Stage { get; }
        public string WindowName { get; }

        public UIWindowLoadException(string stage, string windowName, string message,
            Exception innerException = null) : base(message, innerException)
        {
            Stage = stage;
            WindowName = windowName;
        }

        public override string ToString()
        {
            return $"{base.ToString()}\nUGUI stage={Stage}, window={WindowName}";
        }
    }

    public sealed class UIWindowTimeoutException : TimeoutException
    {
        public UIWindowTimeoutException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    internal enum UIWindowLifecycleState
    {
        Loading,
        Ready,
        Closing,
        Closed,
        Failed
    }

    public abstract class UIWindow : UIBase
    {
        #region Propreties

        private SetUISafeFitHelper _setUISafeFitHelper;

        private System.Action<UIWindow> _prepareCallback;

        private bool _isCreate = false;
        private CancellationTokenSource _lifecycleCancellation;
        private UniTaskCompletionSource<UIWindow> _completion;
        private UIWindowLifecycleState _lifecycleState = UIWindowLifecycleState.Loading;
        private Exception _failure;
        private bool _cleanupStarted;
        private ITimerModule _hideTimerOwner;

        private GameObject _panel;

        private Canvas _canvas;
        
        public Canvas Canvas => _canvas;

        private Canvas[] _childCanvas;

        private GraphicRaycaster _raycaster;
        
        public GraphicRaycaster GraphicRaycaster => _raycaster;

        private GraphicRaycaster[] _childRaycaster;

        public override UIType Type => UIType.Window;

        /// <summary>
        /// 窗口位置组件。
        /// </summary>
        public override Transform transform => _panel.transform;
        
        /// <summary>
        /// 窗口矩阵位置组件。
        /// </summary>
        public override RectTransform rectTransform => _panel.transform as RectTransform;

        /// <summary>
        /// 窗口的实例资源对象。
        /// </summary>
        public override GameObject gameObject => _panel;

        /// <summary>
        /// 窗口名称。
        /// </summary>
        public string WindowName { private set; get; }

        /// <summary>
        /// 窗口层级。
        /// </summary>
        public int WindowLayer { private set; get; }

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public string AssetName { private set; get; }

        /// <summary>
        /// 是否为全屏窗口。
        /// </summary>
        public virtual bool FullScreen { private set; get; } = false;

        /// <summary>
        /// 是内部资源无需AB加载。
        /// </summary>
        public bool FromResources { private set; get; }
        
        /// <summary>
        /// 隐藏窗口关闭时间。
        /// </summary>
        public int HideTimeToClose { get; set; }
        
        public int HideTimerId { get; set; }

        /// <summary>
        /// 窗口深度值。
        /// </summary>
        public int Depth
        {
            get
            {
                if (_canvas != null)
                {
                    return _canvas.sortingOrder;
                }
                else
                {
                    return 0;
                }
            }

            set
            {
                if (_canvas != null)
                {
                    if (_canvas.sortingOrder == value)
                    {
                        return;
                    }

                    var oldOrder = _canvas.sortingOrder;
                    // 设置父类
                    _canvas.sortingOrder = value;
                    // 设置子类
                    // int depth = value;
                    for (int i = 0; i < _childCanvas.Length; i++)
                    {
                        var canvas = _childCanvas[i];
                        if (canvas != _canvas)
                        {
                            // depth += 5; //注意递增值
                            // canvas.sortingOrder = depth;
                            canvas.sortingOrder = value + (canvas.sortingOrder - oldOrder);
                        }
                    }


                    // 虚函数
                    if (Visible)
                    {
                        _OnSortDepth();
                    }
                    else
                    {
                        _isSortingOrderDirty = true;
                    }
                }
            }
        }

        /// <summary>
        /// 窗口可见性。
        /// </summary>
        public bool Visible
        {
            get
            {
                if (_canvas != null)
                {
                    return _canvas.gameObject.layer == UIModule.WINDOW_SHOW_LAYER;
                }
                else
                {
                    return false;
                }
            }

            set
            {
                if (_canvas != null)
                {
                    int setLayer = value ? UIModule.WINDOW_SHOW_LAYER : UIModule.WINDOW_HIDE_LAYER;
                    if (_canvas.gameObject.layer == setLayer)
                        return;

                    // 显示设置
                    _canvas.gameObject.layer = setLayer;
                    for (int i = 0; i < _childCanvas.Length; i++)
                    {
                        _childCanvas[i].gameObject.layer = setLayer;
                    }

                    if (value && _isCreate)
                    {
                        _isSortingOrderDirty = false;
                        _OnSortDepth();
                    }

                    // 交互设置
                    Interactable = value;

                    // 虚函数
                    if (_isCreate)
                    {
                        OnSetVisible(value);
                    }
                }
            }
        }

        /// <summary>
        /// 窗口交互性。
        /// </summary>
        private bool Interactable
        {
            get
            {
                if (_raycaster != null)
                {
                    return _raycaster.enabled;
                }
                else
                {
                    return false;
                }
            }

            set
            {
                if (_raycaster != null)
                {
                    _raycaster.enabled = value;
                    for (int i = 0; i < _childRaycaster.Length; i++)
                    {
                        _childRaycaster[i].enabled = value;
                    }
                }
            }
        }

        /// <summary>
        /// 是否加载完毕。
        /// </summary>
        internal bool IsLoadDone = false;
        
        /// <summary>
        /// UI是否销毁。
        /// </summary>
        internal bool IsDestroyed = false;

        internal bool IsLoading => _lifecycleState == UIWindowLifecycleState.Loading;
        internal bool IsReady => _lifecycleState == UIWindowLifecycleState.Ready;
        internal bool IsCreated => _isCreate;
        internal bool IsClosingOrDestroyed => _lifecycleState == UIWindowLifecycleState.Closing ||
                                               _lifecycleState == UIWindowLifecycleState.Closed || IsDestroyed;
        internal bool IsLifecycleActive => _lifecycleState == UIWindowLifecycleState.Loading ||
                                           _lifecycleState == UIWindowLifecycleState.Ready;
        internal CancellationToken LifecycleToken => _lifecycleCancellation?.Token ?? CancellationToken.None;
        internal UniTask<UIWindow> CompletionTask => _completion?.Task ?? UniTask.FromCanceled<UIWindow>();
        
        /// <summary>
        /// UI是否隐藏标志位。
        /// </summary>
        public bool IsHide { internal set; get; } = false;

        #endregion

        public void Init(string name, int layer, bool fullScreen, string assetName, bool fromResources, int hideTimeToClose)
        {
            ResetUIEventLifecycle();
            CancelHideToCloseTimer();
            _lifecycleCancellation?.Dispose();
            _lifecycleCancellation = new CancellationTokenSource();
            _completion = new UniTaskCompletionSource<UIWindow>();
            _lifecycleState = UIWindowLifecycleState.Loading;
            _failure = null;
            _cleanupStarted = false;
            _isCreate = false;
            IsLoadDone = false;
            IsDestroyed = false;
            IsPrepare = false;
            IsHide = false;
            HideTimerId = 0;
            _hideTimerOwner = null;

            WindowName = name;
            WindowLayer = layer;
            FullScreen = fullScreen;
            AssetName = assetName;
            FromResources = fromResources;
            HideTimeToClose = hideTimeToClose;
        }

        #region 刘海屏适配

        /// <summary>
        /// 移动设备屏幕适配
        /// </summary>
        /// <param name="fitRect">适配的RectTransform对象</param>
        /// <param name="liuHaiFit">是否开启刘海屏顶部适配</param>
        /// <param name="topSpacing">刘海屏顶部适配偏移高度</param>
        /// <param name="bottomFit">是否开启刘海屏底部适配</param>
        /// <param name="bottomSpacing">刘海屏底部适配偏移高度</param>
        public void SetUIFit(RectTransform fitRect, bool liuHaiFit = true, float topSpacing = 0, bool bottomFit = true, float bottomSpacing = 0)
        {
            if (_setUISafeFitHelper == null)
            {
                _setUISafeFitHelper = new SetUISafeFitHelper(fitRect, liuHaiFit, topSpacing, bottomFit, bottomSpacing);
            }
            _setUISafeFitHelper?.SetUIFit();
        }

        /// <summary>
        /// rectTransform不受m_curRect适配影响
        /// </summary>
        /// <param name="rect"></param>
        public void SetUINotFit(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            _setUISafeFitHelper?.SetUINotFit(rect);
        }

        /// <summary>
        /// 设置某一个节点不受指定RectTransform的影响
        /// </summary>
        /// <param name="rect">设置的RectTransform</param>
        /// <param name="refRect">依赖的RectTransform</param>
        public void SetUINotFit(RectTransform rect, RectTransform refRect)
        {
            if (rect == null || refRect == null)
            {
                return;
            }
            if (_setUISafeFitHelper == null)
            {
                _setUISafeFitHelper = new SetUISafeFitHelper();
            }
            _setUISafeFitHelper?.SetUINotFit(rect, refRect);
        }

        #endregion

        internal void TryInvoke(System.Action<UIWindow> prepareCallback, System.Object[] userDatas)
        {
            CancelHideToCloseTimer();
            base._userDatas = userDatas;
            if (IsReady)
            {
                prepareCallback?.Invoke(this);
            }
        }

        internal void InternalSetUserDatas(System.Object[] userDatas)
        {
            CancelHideToCloseTimer();
            this._userDatas = userDatas;
        }

        internal async UniTask InternalLoadAsync(string location, Action<UIWindow> prepareCallback)
        {
            _prepareCallback = prepareCallback;
            CancellationToken lifecycleToken = LifecycleToken;
            if (lifecycleToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(lifecycleToken);
            }

            using var timeoutCts = new CancellationTokenSource();
            float timeoutSeconds = Math.Max(0.001f, UIModule.CreationTimeoutSeconds);
            using IDisposable timeoutRegistration = timeoutCts.CancelAfterSlim(
                TimeSpan.FromSeconds(timeoutSeconds), DelayType.UnscaledDeltaTime);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(lifecycleToken, timeoutCts.Token);
            CancellationToken loadToken = linkedCts.Token;

            GameObject panel = null;
            try
            {
                if (!FromResources)
                {
                    UniTask<GameObject> resourceTask = UIModule.Resource.LoadGameObjectAsync(
                        location, parent: UIModule.UIRoot, cancellationToken: loadToken);
                    var relay = new ResourceLoadRelay(resourceTask);
                    relay.Observe().Forget();
                    try
                    {
                        panel = await relay.Completion.Task.AttachExternalCancellation(loadToken);
                        relay.Claim(panel);
                    }
                    catch
                    {
                        relay.Abandon();
                        throw;
                    }
                }
                else
                {
                    panel = Object.Instantiate(Resources.Load<GameObject>(location), UIModule.UIRoot);
                }

                loadToken.ThrowIfCancellationRequested();
                if (timeoutCts.IsCancellationRequested)
                {
                    throw new UIWindowTimeoutException(
                        $"UGUI window '{WindowName}' exceeded {timeoutSeconds:0.###} seconds while loading.");
                }

                Handle_Completed(panel);
                loadToken.ThrowIfCancellationRequested();
                if (timeoutCts.IsCancellationRequested)
                {
                    throw new UIWindowTimeoutException(
                        $"UGUI window '{WindowName}' exceeded {timeoutSeconds:0.###} seconds while preparing.");
                }

                _completion?.TrySetResult(this);
                panel = null;
            }
            catch (OperationCanceledException exception)
            {
                DestroyUnownedPanel(panel);
                if (IsClosingOrDestroyed || lifecycleToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(lifecycleToken);
                }

                if (timeoutCts.IsCancellationRequested)
                {
                    throw new UIWindowTimeoutException(
                        $"UGUI window '{WindowName}' exceeded {timeoutSeconds:0.###} seconds while loading or preparing.",
                        exception);
                }

                throw new UIWindowLoadException("resource-load", WindowName,
                    $"UGUI window '{WindowName}' resource loading was canceled unexpectedly.", exception);
            }
            catch
            {
                DestroyUnownedPanel(panel);
                throw;
            }
            finally
            {
                _prepareCallback = null;
            }
        }

        internal void InternalLoadSync(string location, Action<UIWindow> prepareCallback)
        {
            _prepareCallback = prepareCallback;
            GameObject panel = null;
            try
            {
                if (LifecycleToken.IsCancellationRequested || IsClosingOrDestroyed)
                {
                    throw new OperationCanceledException(LifecycleToken);
                }

                panel = FromResources
                    ? Object.Instantiate(Resources.Load<GameObject>(location), UIModule.UIRoot)
                    : UIModule.Resource.LoadGameObject(location, parent: UIModule.UIRoot);
                Handle_Completed(panel);
                _completion?.TrySetResult(this);
                panel = null;
            }
            catch
            {
                DestroyUnownedPanel(panel);
                throw;
            }
            finally
            {
                _prepareCallback = null;
            }
        }

        private void DestroyUnownedPanel(GameObject panel)
        {
            if (panel != null && !ReferenceEquals(_panel, panel))
            {
                try { Object.Destroy(panel); }
                catch (Exception exception)
                {
                    Log.Warning($"UGUI window '{WindowName}' failed to destroy an unowned panel: {exception}");
                }
            }
        }

        internal void InternalCreate()
        {
            if (!IsLifecycleActive)
            {
                throw new OperationCanceledException(LifecycleToken);
            }

            if (_isCreate == false)
            {
                _isCreate = true;
                Inject();
                ScriptGenerator();
                BindMemberProperty();
                RegisterEvent();
                OnCreate();
            }
        }

        internal void InternalRefresh()
        {
            if (!IsLifecycleActive)
            {
                throw new OperationCanceledException(LifecycleToken);
            }

            OnRefresh();
        }

        internal bool InternalUpdate()
        {
            if (!IsPrepare || !Visible)
            {
                return false;
            }

            List<UIWidget> listNextUpdateChild = null;
            if (ListChild != null && ListChild.Count > 0)
            {
                listNextUpdateChild = _listUpdateChild;
                var updateListValid = _updateListValid;
                List<UIWidget> listChild = null;
                if (!updateListValid)
                {
                    if (listNextUpdateChild == null)
                    {
                        listNextUpdateChild = new List<UIWidget>();
                        _listUpdateChild = listNextUpdateChild;
                    }
                    else
                    {
                        listNextUpdateChild.Clear();
                    }

                    listChild = ListChild;
                }
                else
                {
                    listChild = listNextUpdateChild;
                }

                for (int i = 0; i < listChild.Count; i++)
                {
                    var uiWidget = listChild[i];

                    if (uiWidget == null)
                    {
                        continue;
                    }

                    var needValid = uiWidget.InternalUpdate();

                    if (!updateListValid && needValid)
                    {
                        listNextUpdateChild.Add(uiWidget);
                    }
                }

                if (!updateListValid)
                {
                    _updateListValid = true;
                }
            }

            bool needUpdate = false;
            if (listNextUpdateChild == null || listNextUpdateChild.Count <= 0)
            {
                _hasOverrideUpdate = true;
                OnUpdate();
                needUpdate = _hasOverrideUpdate;
            }
            else
            {
                OnUpdate();
                needUpdate = true;
            }

            return needUpdate;
        }

        internal void RecordFailure(Exception exception)
        {
            if (exception == null || _lifecycleState == UIWindowLifecycleState.Closing ||
                _lifecycleState == UIWindowLifecycleState.Closed)
            {
                return;
            }

            _failure ??= exception;
            _lifecycleState = UIWindowLifecycleState.Failed;
            IsPrepare = false;
            IsLoadDone = false;
        }

        internal bool BeginClose()
        {
            if (_lifecycleState == UIWindowLifecycleState.Closing ||
                _lifecycleState == UIWindowLifecycleState.Closed)
            {
                return false;
            }

            _lifecycleState = UIWindowLifecycleState.Closing;
            IsDestroyed = true;
            IsPrepare = false;
            IsLoadDone = false;
            BlockUIEvents();
            _prepareCallback = null;
            CancelHideToCloseTimer();

            try
            {
                _lifecycleCancellation?.Cancel();
            }
            catch (Exception exception)
            {
                Log.Warning($"UI window '{WindowName}' lifecycle cancellation failed: {exception}");
            }

            return true;
        }

        internal void InternalDestroy(bool isShutDown = false)
        {
            if (_cleanupStarted)
            {
                return;
            }

            BeginClose();
            _cleanupStarted = true;
            _isCreate = false;

            Exception firstException = null;
            try
            {
                try
                {
                    RemoveAllUIEvent();
                }
                catch (Exception exception)
                {
                    firstException = exception;
                }

                List<UIWidget> children = new List<UIWidget>(ListChild);
                ListChild.Clear();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        children[i]?.InternalDestroy();
                    }
                    catch (Exception exception)
                    {
                        firstException ??= exception;
                    }
                }

                try
                {
                    OnDestroy();
                }
                catch (Exception exception)
                {
                    firstException ??= exception;
                }
            }
            finally
            {
                try
                {
                    if (_panel != null)
                    {
                        Object.Destroy(_panel);
                    }
                }
                catch (Exception exception)
                {
                    firstException ??= exception;
                }
                finally
                {
                    _panel = null;
                    _canvas = null;
                    _childCanvas = null;
                    _raycaster = null;
                    _childRaycaster = null;
                    _lifecycleState = UIWindowLifecycleState.Closed;
                    _lifecycleCancellation?.Dispose();
                    _lifecycleCancellation = null;
                    if (_failure != null)
                    {
                        _completion?.TrySetException(_failure);
                    }
                    else
                    {
                        _completion?.TrySetCanceled();
                    }
                }
            }

            if (firstException != null)
            {
                Log.Warning($"UI window '{WindowName}' cleanup hook failed after complete cleanup: {firstException}");
            }
        }

        /// <summary>
        /// 处理资源加载完成回调。
        /// </summary>
        /// <param name="panel">面板资源实例。</param>
        private void Handle_Completed(GameObject panel)
        {
            if (panel == null)
            {
                if (IsClosingOrDestroyed)
                {
                    throw new OperationCanceledException(LifecycleToken);
                }

                throw new UIWindowLoadException("resource-load", WindowName,
                    $"UGUI window '{WindowName}' resource loader returned a null instance.");
            }

            if (!IsLifecycleActive)
            {
                Object.Destroy(panel);
                throw new OperationCanceledException(LifecycleToken);
            }
            
            panel.name = GetType().Name;
            _panel = panel;
            _panel.transform.localPosition = Vector3.zero;

            // 获取组件
            _canvas = _panel.GetComponent<Canvas>();
            if (_canvas == null)
            {
                throw new Exception($"Not found {nameof(Canvas)} in panel {WindowName}");
            }

            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 0;
            _canvas.sortingLayerName = "Default";

            // 获取组件
            _raycaster = _panel.GetComponent<GraphicRaycaster>();
            _childCanvas = _panel.GetComponentsInChildren<Canvas>(true);
            _childRaycaster = _panel.GetComponentsInChildren<GraphicRaycaster>(true);

            // 通知UI管理器
            _prepareCallback?.Invoke(this);

            if (!IsLifecycleActive)
            {
                throw new OperationCanceledException(LifecycleToken);
            }

            _lifecycleState = UIWindowLifecycleState.Ready;
            IsPrepare = true;
            IsLoadDone = true;
        }
        
        protected virtual void Hide()
        {
            UIModule.Instance.HideUI(this.GetType());
        }

        protected virtual void Close()
        {
            UIModule.Instance.CloseUI(this.GetType());
        }
        
        internal void CancelHideToCloseTimer()
        {
            IsHide = false;
            if (HideTimerId > 0)
            {
                try
                {
                    _hideTimerOwner?.RemoveTimer(HideTimerId);
                }
                catch (Exception exception)
                {
                    Log.Warning($"UI window '{WindowName}' hide timer cleanup failed: {exception}");
                }

                HideTimerId = 0;
                _hideTimerOwner = null;
            }
        }

        internal void SetHideTimer(ITimerModule timerModule, int timerId)
        {
            _hideTimerOwner = timerModule;
            HideTimerId = timerId;
        }

        private sealed class ResourceLoadRelay
        {
            private readonly UniTask<GameObject> _resourceTask;
            private bool _abandoned;
            private bool _finished;
            private bool _claimed;
            private GameObject _result;

            public ResourceLoadRelay(UniTask<GameObject> resourceTask)
            {
                _resourceTask = resourceTask;
                Completion = new UniTaskCompletionSource<GameObject>();
            }

            public UniTaskCompletionSource<GameObject> Completion { get; }

            public async UniTaskVoid Observe()
            {
                try
                {
                    GameObject panel = await _resourceTask;
                    if (_abandoned)
                    {
                        DestroyLate(panel);
                        return;
                    }

                    _result = panel;
                    _finished = true;
                    Completion.TrySetResult(panel);
                }
                catch (Exception exception)
                {
                    _finished = true;
                    if (!_abandoned)
                    {
                        Completion.TrySetException(exception);
                    }
                }
            }

            public void Claim(GameObject panel)
            {
                if (_claimed)
                {
                    return;
                }

                _claimed = true;
                if (ReferenceEquals(_result, panel))
                {
                    _result = null;
                }
            }

            public void Abandon()
            {
                _abandoned = true;
                if (_finished && !_claimed)
                {
                    _claimed = true;
                    GameObject panel = _result;
                    _result = null;
                    DestroyLate(panel);
                }
            }

            private static void DestroyLate(GameObject panel)
            {
                if (panel != null)
                {
                    Object.Destroy(panel);
                }
            }
        }
    }
}
