using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// UI管理模块。
    /// </summary>
    public sealed partial class UIModule : Singleton<UIModule>, IUpdate
    {
        // 核心字段
        private static Transform _instanceRoot = null;          // UI根节点变换组件
        private bool _enableErrorLog = true;                    // 是否启用错误日志
        private Camera _uiCamera = null;                        // UI专用摄像机
        private readonly List<UIWindow> _uiStack = new List<UIWindow>(128); // 窗口堆栈
        private ErrorLogger _errorLogger;                       // 错误日志记录器
        private bool _shuttingDown;

        // 常量定义
        public const int LAYER_DEEP = 2000; 
        public const int WINDOW_DEEP = 100;
        public const int WINDOW_HIDE_LAYER = 2; // Ignore Raycast
        public const int WINDOW_SHOW_LAYER = 5; // UI

        // 资源加载接口
        public static IUIResourceLoader Resource;

        // Tests may shorten this value through the internal test hook; production keeps the
        // historical sixty-second creation budget.
        internal static float CreationTimeoutSeconds = 60f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            _instanceRoot = null;
            Resource = null;
        }
        
        /// <summary>
        /// UI根节点访问属性
        /// </summary>
        public static Transform UIRoot => _instanceRoot;

        /// <summary>
        /// UI摄像机访问属性
        /// </summary>
        public Camera UICamera => _uiCamera;
        
        /// <summary>
        /// 模块初始化（自动调用）。
        /// 1. 查找场景中的UIRoot
        /// 2. 初始化资源加载器
        /// 3. 配置错误日志系统
        /// </summary>
        protected override void OnInit()
        {
            _shuttingDown = false;
            var uiRoot = GameObject.Find("UIRoot");
            if (uiRoot != null)
            {
                _instanceRoot = uiRoot.GetComponentInChildren<Canvas>()?.transform;
                _uiCamera = uiRoot.GetComponentInChildren<Camera>();
            }
            else
            {
                Log.Fatal("UIRoot not found !");
                return;
            }
            
            Resource = new UIResourceLoader();

            UnityEngine.Object.DontDestroyOnLoad(_instanceRoot.parent != null ? _instanceRoot.parent : _instanceRoot);

            _instanceRoot.gameObject.layer = LayerMask.NameToLayer("UI");

            if (Debugger.Instance != null)
            {
                switch (Debugger.Instance.ActiveWindowType)
                {
                    case DebuggerActiveWindowType.AlwaysOpen:
                        _enableErrorLog = true;
                        break;

                    case DebuggerActiveWindowType.OnlyOpenWhenDevelopment:
                        _enableErrorLog = Debug.isDebugBuild;
                        break;

                    case DebuggerActiveWindowType.OnlyOpenInEditor:
                        _enableErrorLog = Application.isEditor;
                        break;

                    default:
                        _enableErrorLog = false;
                        break;
                }
                if (_enableErrorLog)
                {
                    _errorLogger = new ErrorLogger(this);
                }   
            }
        }

        /// <summary>
        /// 模块释放（自动调用）。
        /// 1. 清理错误日志系统
        /// 2. 关闭所有窗口
        /// 3. 销毁UI根节点
        /// </summary>
        protected override void OnRelease()
        {
            _shuttingDown = true;
            try
            {
                if (_errorLogger != null)
                {
                    _errorLogger.Dispose();
                }
            }
            catch (Exception exception)
            {
                LogErrorSafely("UI error logger shutdown failed: {0}", exception);
            }
            finally
            {
                _errorLogger = null;
            }

            try
            {
                CloseAll(isShutDown: true);
            }
            finally
            {
                try
                {
                    if (_instanceRoot != null && _instanceRoot.parent != null)
                    {
                        UnityEngine.Object.Destroy(_instanceRoot.parent.gameObject);
                    }
                }
                catch (Exception exception)
                {
                    LogErrorSafely("UI root shutdown failed: {0}", exception);
                }

                Resource = null;
                _instanceRoot = null;
                _uiCamera = null;
            }
        }

        #region 设置安全区域

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）。
        /// </summary>
        /// <param name="safeRect">安全区域矩形（基于屏幕像素坐标）。</param>
        public static void ApplyScreenSafeRect(Rect safeRect)
        {
            CanvasScaler scaler = UIRoot.GetComponentInParent<CanvasScaler>();
            if (scaler == null)
            {
                Log.Error($"Not found {nameof(CanvasScaler)} !");
                return;
            }

            // Convert safe area rectangle from absolute pixels to UGUI coordinates
            float rateX = scaler.referenceResolution.x / Screen.width;
            float rateY = scaler.referenceResolution.y / Screen.height;
            float posX = (int)(safeRect.position.x * rateX);
            float posY = (int)(safeRect.position.y * rateY);
            float width = (int)(safeRect.size.x * rateX);
            float height = (int)(safeRect.size.y * rateY);

            float offsetMaxX = scaler.referenceResolution.x - width - posX;
            float offsetMaxY = scaler.referenceResolution.y - height - posY;

            // 注意：安全区坐标系的原点为左下角	
            var rectTrans = UIRoot.transform as RectTransform;
            if (rectTrans != null)
            {
                rectTrans.offsetMin = new Vector2(posX, posY); //锚框状态下的屏幕左下角偏移向量
                rectTrans.offsetMax = new Vector2(-offsetMaxX, -offsetMaxY); //锚框状态下的屏幕右上角偏移向量
            }
        }

        /// <summary>
        /// 模拟IPhoneX异形屏
        /// </summary>
        public static void SimulateIPhoneXNotchScreen()
        {
            Rect rect;
            if (Screen.height > Screen.width)
            {
                // 竖屏Portrait
                float deviceWidth = 1125;
                float deviceHeight = 2436;
                rect = new Rect(0f / deviceWidth, 102f / deviceHeight, 1125f / deviceWidth, 2202f / deviceHeight);
            }
            else
            {
                // 横屏Landscape
                float deviceWidth = 2436;
                float deviceHeight = 1125;
                rect = new Rect(132f / deviceWidth, 63f / deviceHeight, 2172f / deviceWidth, 1062f / deviceHeight);
            }

            Rect safeArea = new Rect(Screen.width * rect.x, Screen.height * rect.y, Screen.width * rect.width, Screen.height * rect.height);
            ApplyScreenSafeRect(safeArea);
        }

        #endregion

        /// <summary>
        /// 获取所有层级下顶部的窗口名称。
        /// </summary>
        public string GetTopWindow()
        {
            if (_uiStack.Count == 0)
            {
                return string.Empty;
            }

            UIWindow topWindow = _uiStack[^1];
            return topWindow.WindowName;
        }

        /// <summary>
        /// 获取指定层级下顶部的窗口名称。
        /// </summary>
        public string GetTopWindow(int layer)
        {
            UIWindow lastOne = null;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack[i].WindowLayer == layer)
                    lastOne = _uiStack[i];
            }

            if (lastOne == null)
                return string.Empty;

            return lastOne.WindowName;
        }

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        public bool IsAnyLoading()
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                var window = _uiStack[i];
                if (window.IsLoading)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <typeparam name="T">界面类型。</typeparam>
        /// <returns>是否存在。</returns>
        public bool HasWindow<T>()
        {
            return HasWindow(typeof(T));
        }

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <returns>是否存在。</returns>
        public bool HasWindow(Type type)
        {
            return IsContains(type.FullName);
        }

       /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="userDatas">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUIAsync<T>(params System.Object[] userDatas) where T : UIWindow , new()
        {
            if (!CanOpenWindows())
            {
                return;
            }

            ShowUIAsyncImp(typeof(T), userDatas).Forget(HandleFireAndForgetException);
        }

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="type">界面类型。</param>
        /// <param name="userDatas">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUIAsync(Type type, params System.Object[] userDatas)
        {
            if (!CanOpenWindows())
            {
                return;
            }

            ShowUIAsyncImp(type, userDatas).Forget(HandleFireAndForgetException);
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <typeparam name="T">窗口类。</typeparam>
        /// <param name="userDatas">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUI<T>(params System.Object[] userDatas) where T : UIWindow , new()
        {
            EnsureCanOpenWindows();
            ShowUIImp<T>(false, userDatas);
        }
        
        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        /// <param name="userDatas">用户自定义数据。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public async UniTask<T> ShowUIAsyncAwait<T>(params System.Object[] userDatas) where T : UIWindow , new()
        {
            EnsureCanOpenWindows();
            return await ShowUIAsyncImp(typeof(T), userDatas) as T;
        }

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        /// <param name="type"></param>
        /// <param name="userDatas"></param>
        /// <returns>打开窗口操作句柄。</returns>
        public void ShowUI(Type type, params System.Object[] userDatas)
        {
            EnsureCanOpenWindows();
            ShowUIImp(type, false, userDatas);
        }

        private void ShowUIImp(Type type, bool isAsync, params System.Object[] userDatas)
        {
            EnsureCanOpenWindows();
            if (isAsync)
            {
                ShowUIAsyncImp(type, userDatas).Forget(HandleFireAndForgetException);
                return;
            }

            ShowUISyncImp(type, userDatas);
        }
        
        private void ShowUIImp<T>(bool isAsync, params System.Object[] userDatas) where T : UIWindow , new()
        {
            EnsureCanOpenWindows();
            if (isAsync)
            {
                ShowUIAsyncImp(typeof(T), userDatas).Forget(HandleFireAndForgetException);
                return;
            }

            ShowUISyncImp(typeof(T), userDatas);
        }

        private void ShowUISyncImp(Type type, params System.Object[] userDatas)
        {
            EnsureCanOpenWindows();
            UIWindow window = GetWindow(type.FullName);
            if (window != null)
            {
                if (window.IsLoading)
                {
                    throw new GameFrameworkException(
                        $"Cannot synchronously show UI window '{type.FullName}' while it is loading.");
                }

                if (!window.IsReady)
                {
                    CloseWindow(window);
                    window = null;
                }
                else
                {
                    window.InternalSetUserDatas(userDatas);
                    BringWindowToFront(window);
                    try
                    {
                        OnWindowPrepare(window);
                        EnsureCurrentReady(window);
                    }
                    catch (Exception exception)
                    {
                        Exception failure = NormalizeWindowException(window, "prepare", exception);
                        if (!IsExpectedWindowCancellation(window, exception))
                        {
                            FailAndClose(window, failure);
                            throw failure;
                        }

                        throw;
                    }
                    return;
                }
            }

            window = CreateInstance(type);
            Push(window);
            window.InternalSetUserDatas(userDatas);
            try
            {
                window.InternalLoadSync(window.AssetName, OnWindowPrepare);
                EnsureCurrentReady(window);
            }
            catch (Exception exception)
            {
                Exception failure = NormalizeWindowException(window, "create", exception);
                if (!IsExpectedWindowCancellation(window, exception))
                {
                    FailAndClose(window, failure);
                    throw failure;
                }

                throw;
            }
        }

        private async UniTask<UIWindow> ShowUIAsyncImp(Type type, params System.Object[] userDatas)
        {
            EnsureCanOpenWindows();
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            UIWindow window = GetWindow(type.FullName);
            if (window != null)
            {
                if (window.IsLoading)
                {
                    window.InternalSetUserDatas(userDatas);
                    return await WaitForReady(window);
                }

                if (window.IsReady)
                {
                    window.InternalSetUserDatas(userDatas);
                    try
                    {
                        BringWindowToFront(window);
                        OnWindowPrepare(window);
                        EnsureCurrentReady(window);
                        return window;
                    }
                    catch (Exception exception)
                    {
                        if (IsExpectedWindowCancellation(window, exception))
                        {
                            throw;
                        }

                        Exception failure = NormalizeWindowException(window, "prepare", exception);
                        FailAndClose(window, failure);
                        throw failure;
                    }
                }

                CloseWindow(window);
            }

            window = CreateInstance(type);
            Push(window);
            window.InternalSetUserDatas(userDatas);
            StartAsyncLoad(window);
            return await WaitForReady(window);
        }

        private async UniTask<UIWindow> WaitForReady(UIWindow window)
        {
            UIWindow result = await window.CompletionTask;
            EnsureCurrentReady(window);
            return result;
        }

        private void StartAsyncLoad(UIWindow window)
        {
            LoadWindowAsync(window).Forget(exception => HandleWindowLoadFailure(window, exception));
        }

        private async UniTask LoadWindowAsync(UIWindow window)
        {
            await window.InternalLoadAsync(window.AssetName, OnWindowPrepare);
        }

        private void HandleWindowLoadFailure(UIWindow window, Exception exception)
        {
            if (window == null || !IsCurrentWindow(window) || IsExpectedWindowCancellation(window, exception))
            {
                return;
            }

            Exception failure = NormalizeWindowException(window, "create", exception);
            FailAndClose(window, failure);
        }

        private void HandleFireAndForgetException(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return;
            }

            Log.Error($"UGUI window show failed: {exception}");
        }

        private void FailAndClose(UIWindow window, Exception exception)
        {
            if (window == null || !IsCurrentWindow(window))
            {
                return;
            }

            window.RecordFailure(exception);
            CloseWindow(window);
        }

        private static Exception NormalizeWindowException(UIWindow window, string stage, Exception exception)
        {
            if (exception is UIWindowLoadException || exception is UIWindowTimeoutException)
            {
                return exception;
            }

            if (exception is OperationCanceledException && window.IsClosingOrDestroyed)
            {
                return exception;
            }

            return new UIWindowLoadException(stage, window.WindowName,
                $"Failed to {stage} UGUI window '{window.WindowName}'.", exception);
        }

        private static bool IsExpectedWindowCancellation(UIWindow window, Exception exception)
        {
            return exception is OperationCanceledException && window.IsClosingOrDestroyed;
        }

        private void EnsureCurrentReady(UIWindow window)
        {
            if (!IsCurrentWindow(window) || !window.IsReady || window.IsDestroyed)
            {
                throw new OperationCanceledException();
            }
        }

        private void BringWindowToFront(UIWindow window)
        {
            if (!IsCurrentWindow(window))
            {
                throw new OperationCanceledException();
            }

            Pop(window);
            Push(window);
        }

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型</typeparam>
        public void CloseUI<T>() where T : UIWindow
        {
            CloseUI(typeof(T));
        }

        public void CloseUI(Type type)
        {
            if (type == null)
            {
                return;
            }

            CloseWindow(GetWindow(type.FullName));
        }
        
        public void HideUI<T>() where T : UIWindow
        {
            HideUI(typeof(T));
        }

        public void HideUI(Type type)
        {
            if (type == null)
            {
                return;
            }

            UIWindow window = GetWindow(type.FullName);
            if (window == null)
            {
                return;
            }

            if (window.HideTimeToClose <= 0)
            {
                CloseWindow(window);
                return;
            }

            window.CancelHideToCloseTimer();
            window.Visible = false;
            window.IsHide = true;
            if (!IsCurrentWindow(window))
            {
                return;
            }

            ITimerModule timerModule = GameModule.Timer;
            int timerId = timerModule.AddTimer((arg) =>
            {
                if (IsCurrentWindow(window))
                {
                    CloseWindow(window);
                }
            }, window.HideTimeToClose);
            window.SetHideTimer(timerModule, timerId);

            if (window.FullScreen)
            {
                SafeRefreshPresentation();
            }
        }

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        public void CloseAll(bool isShutDown = false)
        {
            UIWindow[] windows = _uiStack.ToArray();
            _uiStack.Clear();
            for (int i = 0; i < windows.Length; i++)
            {
                try
                {
                    windows[i]?.BeginClose();
                }
                catch (Exception exception)
                {
                    LogWarningSafely($"UI window close preparation failed: {exception}");
                }
            }

            SafeRefreshPresentation();
            for (int i = 0; i < windows.Length; i++)
            {
                try
                {
                    windows[i].InternalDestroy(isShutDown);
                }
                catch (Exception exception)
                {
                    LogWarningSafely($"UI window '{windows[i]?.WindowName}' cleanup raised an exception: {exception}");
                }
            }
        }

        /// <summary>
        /// 关闭所有窗口除了。
        /// </summary>
        public void CloseAllWithOut(UIWindow withOut)
        {
            CloseWindowsExcept(window => window == withOut);
        }

        private void CloseWindowsExcept(Func<UIWindow, bool> keep)
        {
            var windows = new List<UIWindow>();
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                if (keep(window))
                {
                    continue;
                }

                _uiStack.RemoveAt(i);
                windows.Add(window);
            }

            for (int i = 0; i < windows.Count; i++)
            {
                try
                {
                    windows[i]?.BeginClose();
                }
                catch (Exception exception)
                {
                    LogWarningSafely($"UI window close preparation failed: {exception}");
                }
            }

            SafeRefreshPresentation();
            for (int i = 0; i < windows.Count; i++)
            {
                try
                {
                    windows[i].InternalDestroy();
                }
                catch (Exception exception)
                {
                    LogWarningSafely($"UI window '{windows[i]?.WindowName}' cleanup raised an exception: {exception}");
                }
            }
        }

        /// <summary>
        /// 关闭所有窗口除了。
        /// </summary>
        public void CloseAllWithOut<T>() where T : UIWindow
        {
            CloseWindowsExcept(window => window.GetType() == typeof(T));
        }

        private void OnWindowPrepare(UIWindow window)
        {
            EnsureCurrentWindow(window);
            if (!window.IsCreated)
            {
                window.InternalCreate();
            }
            EnsureCurrentWindow(window);
            window.InternalRefresh();
            EnsureCurrentWindow(window);
            OnSortWindowDepth(window.WindowLayer);
            EnsureCurrentWindow(window);
            OnSetWindowVisible();
            EnsureCurrentWindow(window);
        }

        private void OnSortWindowDepth(int layer)
        {
            int depth = layer * LAYER_DEEP;
            UIWindow[] windows = _uiStack.ToArray();
            for (int i = 0; i < windows.Length; i++)
            {
                UIWindow window = windows[i];
                if (IsCurrentWindow(window) && window.WindowLayer == layer)
                {
                    window.Depth = depth;
                    depth += WINDOW_DEEP;
                }
            }
        }

        private void OnSetWindowVisible()
        {
            bool isHideNext = false;
            UIWindow[] windows = _uiStack.ToArray();
            for (int i = windows.Length - 1; i >= 0; i--)
            {
                UIWindow window = windows[i];
                if (!IsCurrentWindow(window))
                {
                    continue;
                }

                if (isHideNext == false)
                {
                    if (window.IsHide)
                    {
                        continue;
                    }
                    window.Visible = true;
                    if (IsCurrentWindow(window) && window.IsCreated && window.FullScreen)
                    {
                        isHideNext = true;
                    }
                }
                else
                {
                    if (IsCurrentWindow(window))
                    {
                        window.Visible = false;
                    }
                }
            }
        }

        private void CloseWindow(UIWindow window, bool isShutDown = false)
        {
            if (window == null)
            {
                return;
            }

            int layer = window.WindowLayer;
            // Cancellation callbacks can synchronously reopen this window type.
            Pop(window);
            if (!window.BeginClose())
            {
                return;
            }
            try
            {
                OnSortWindowDepth(layer);
            }
            catch (Exception exception)
            {
                LogWarningSafely($"UI window presentation cleanup failed after closing '{window.WindowName}': {exception}");
            }

            try
            {
                OnSetWindowVisible();
            }
            catch (Exception exception)
            {
                LogWarningSafely($"UI window visibility cleanup failed after closing '{window.WindowName}': {exception}");
            }

            try
            {
                window.InternalDestroy(isShutDown);
            }
            catch (Exception exception)
            {
                LogWarningSafely($"UI window '{window.WindowName}' cleanup raised an exception: {exception}");
            }
        }

        private void SafeRefreshPresentation()
        {
            try
            {
                OnSetWindowVisible();
            }
            catch (Exception exception)
            {
                LogWarningSafely($"UI window presentation refresh failed during cleanup: {exception}");
            }
        }

        private void EnsureCurrentWindow(UIWindow window)
        {
            if (!IsCurrentWindow(window) || !window.IsLifecycleActive)
            {
                throw new OperationCanceledException(window?.LifecycleToken ?? CancellationToken.None);
            }
        }

        private bool IsCurrentWindow(UIWindow window)
        {
            if (window == null || window.IsClosingOrDestroyed)
            {
                return false;
            }

            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (ReferenceEquals(_uiStack[i], window))
                {
                    return true;
                }
            }

            return false;
        }

        private UIWindow CreateInstance<T>() where T : UIWindow , new()
        {
            EnsureCanOpenWindows();
            Type type = typeof(T);
            UIWindow window = new T();
            WindowAttribute attribute = Attribute.GetCustomAttribute(type, typeof(WindowAttribute)) as WindowAttribute;

            if (window == null)
                throw new GameFrameworkException($"Window {type.FullName} create instance failed.");

            if (attribute != null)
            {
                string assetName = string.IsNullOrEmpty(attribute.Location) ? type.Name : attribute.Location;
                window.Init(type.FullName, attribute.WindowLayer, attribute.FullScreen, assetName, attribute.FromResources, attribute.HideTimeToClose);
            }
            else
            {
                window.Init(type.FullName, (int)UILayer.UI, fullScreen: window.FullScreen, assetName: type.Name, fromResources: false, hideTimeToClose: 10);
            }

            return window;
        }

        private UIWindow CreateInstance(Type type)
        {
            EnsureCanOpenWindows();
            UIWindow window = Activator.CreateInstance(type) as UIWindow;
            WindowAttribute attribute = Attribute.GetCustomAttribute(type, typeof(WindowAttribute)) as WindowAttribute;

            if (window == null)
                throw new GameFrameworkException($"Window {type.FullName} create instance failed.");

            if (attribute != null)
            {
                string assetName = string.IsNullOrEmpty(attribute.Location) ? type.Name : attribute.Location;
                window.Init(type.FullName, attribute.WindowLayer, attribute.FullScreen, assetName, attribute.FromResources, attribute.HideTimeToClose);
            }
            else
            {
                window.Init(type.FullName, (int)UILayer.UI, fullScreen: window.FullScreen, assetName: type.Name, fromResources: false, hideTimeToClose: 10);
            }

            return window;
        }
        
        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <returns>打开窗口操作句柄。</returns>
        public async UniTask<T> GetUIAsyncAwait<T>(CancellationToken cancellationToken = default) where T : UIWindow
        {
            string windowName = typeof(T).FullName;
            var window = GetWindow(windowName);
            if (window == null)
            {
                return null;
            }
            
            var ret = window as T;

            if (ret == null)
            {
                return null;
            }

            if (ret.IsReady)
            {
                EnsureCurrentReady(ret);
                return ret;
            }

            UIWindow result = await ret.CompletionTask.AttachExternalCancellation(cancellationToken);
            EnsureCurrentReady(result);
            return result as T;
        }

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        /// <param name="callback">回调。</param>
        /// <returns>打开窗口操作句柄。</returns>
        public void GetUIAsync<T>(Action<T> callback) where T : UIWindow
        {
            string windowName = typeof(T).FullName;
            var window = GetWindow(windowName);
            if (window == null)
            {
                InvokeGetCallback(callback, null);
                return;
            }

            var ret = window as T;
            
            if (ret == null)
            {
                InvokeGetCallback(callback, null);
                return;
            }

            GetUIAsyncImp(ret, callback).Forget(exception =>
            {
                if (exception is not OperationCanceledException)
                {
                    Log.Error($"UGUI window get failed: {exception}");
                }

                InvokeGetCallback(callback, null);
            });

            async UniTask GetUIAsyncImp(UIWindow target, Action<T> ctx)
            {
                T result = null;
                Exception failure = null;
                try
                {
                    UIWindow completed = target.IsReady
                        ? target
                        : await target.CompletionTask;
                    if (IsCurrentWindow(completed) && completed.IsReady && !completed.IsDestroyed)
                    {
                        result = completed as T;
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (failure != null && failure is not OperationCanceledException)
                {
                    Log.Error($"UGUI window get failed: {failure}");
                }

                InvokeGetCallback(ctx, result);
            }
        }

        private static void InvokeGetCallback<T>(Action<T> callback, T value) where T : UIWindow
        {
            try
            {
                callback?.Invoke(value);
            }
            catch (Exception exception)
            {
                Log.Error($"UGUI window get callback failed: {exception}");
            }
        }

        private UIWindow GetWindow(string windowName)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (window.WindowName == windowName)
                {
                    return window;
                }
            }

            return null;
        }

        private bool IsContains(string windowName)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                if (window.WindowName == windowName)
                {
                    return true;
                }
            }

            return false;
        }

        private void Push(UIWindow window)
        {
            // 如果已经存在
            if (IsContains(window.WindowName))
            {
                throw new GameFrameworkException($"Window {window.WindowName} is exist.");
            }

            // 获取插入到所属层级的位置
            int insertIndex = -1;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (window.WindowLayer == _uiStack[i].WindowLayer)
                {
                    insertIndex = i + 1;
                }
            }

            // 如果没有所属层级，找到相邻层级
            if (insertIndex == -1)
            {
                for (int i = 0; i < _uiStack.Count; i++)
                {
                    if (window.WindowLayer > _uiStack[i].WindowLayer)
                    {
                        insertIndex = i + 1;
                    }
                }
            }

            // 如果是空栈或没有找到插入位置
            if (insertIndex == -1)
            {
                insertIndex = 0;
            }

            // 最后插入到堆栈
            _uiStack.Insert(insertIndex, window);
        }

        private void Pop(UIWindow window)
        {
            // 从堆栈里移除
            _uiStack.Remove(window);
        }

        private bool CanOpenWindows()
        {
            return !_shuttingDown && ModuleSystem.IsRunning;
        }

        private void EnsureCanOpenWindows()
        {
            if (!CanOpenWindows())
            {
                throw new ObjectDisposedException(nameof(UIModule), "UI module is shutting down and cannot create a window.");
            }
        }

        public void OnUpdate()
        {
            if (_uiStack == null || !ModuleSystem.IsRunning)
            {
                return;
            }

            int count = _uiStack.Count;
            for (int i = 0; i < _uiStack.Count; i++)
            {
                if (_uiStack.Count != count)
                {
                    break;
                }

                var window = _uiStack[i];
                window.InternalUpdate();
            }
        }

        private static void LogErrorSafely(string format, Exception exception)
        {
            try { Log.Error(format, exception); }
            catch { }
        }

        private static void LogWarningSafely(string message)
        {
            try { Log.Warning(message); }
            catch { }
        }
    }
}
