using System.Collections.Generic;
using System;
using TEngine;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameLogic
{
    public abstract class UIWidget : UIBase
    {
        /// <summary>
        /// 窗口组件的实例资源对象。
        /// </summary>
        public override GameObject gameObject { protected set; get; }

        /// <summary>
        /// 窗口组件矩阵位置组件。
        /// </summary>
        public override RectTransform rectTransform { protected set; get; }
        
        /// <summary>
        /// 窗口位置组件。
        /// </summary>
        public override Transform transform { protected set; get; }

        /// <summary>
        /// 窗口组件名称。
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public string name { protected set; get; } = string.Empty;

        private bool _destroyStarted;

        /// <summary>
        /// UI类型。
        /// </summary>
        public override UIType Type => UIType.Widget;

        /// <summary>
        /// 所属的窗口。
        /// </summary>
        public UIWindow OwnerWindow
        {
            get
            {
                var parentUI = base._parent;
                while (parentUI != null)
                {
                    if (parentUI.Type == UIType.Window)
                    {
                        return parentUI as UIWindow;
                    }

                    parentUI = parentUI.Parent;
                }

                return null;
            }
        }
        
        /// <summary>
        /// 窗口可见性。
        /// </summary>
        public bool Visible
        {
            get => gameObject != null && gameObject.activeSelf;

            set
            {
                if (gameObject == null || _destroyStarted)
                {
                    return;
                }

                gameObject.SetActive(value);
                OnSetVisible(value);
            }
        }

        internal bool InternalUpdate()
        {
            if (!IsPrepare)
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
            if (listNextUpdateChild is not { Count: > 0 })
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

        #region Create

        /// <summary>
        /// 创建窗口内嵌的界面。
        /// </summary>
        /// <param name="parentUI">父节点UI。</param>
        /// <param name="widgetRoot">组件根节点。</param>
        /// <param name="visible">是否可见。</param>
        /// <returns></returns>
        public bool Create(UIBase parentUI, GameObject widgetRoot, bool visible = true)
        {
            return CreateImp(parentUI, widgetRoot, false, visible);
        }

        /// <summary>
        /// 根据资源名创建
        /// </summary>
        /// <param name="resPath"></param>
        /// <param name="parentUI"></param>
        /// <param name="parentTrans"></param>
        /// <param name="visible"></param>
        /// <returns></returns>
        public bool CreateByPath(string resPath, UIBase parentUI, Transform parentTrans = null, bool visible = true)
        {
            if (parentUI == null) throw new ArgumentNullException(nameof(parentUI));
            parentUI.ThrowIfWidgetOwnerInvalid();
            GameObject goInst = UIModule.Resource.LoadGameObject(resPath, parent: parentTrans);
            if (goInst == null)
            {
                return false;
            }

            if (!CreateOwned(parentUI, goInst, false, visible))
            {
                return false;
            }

            goInst.transform.localScale = Vector3.one;
            goInst.transform.localPosition = Vector3.zero;
            return true;
        }

        /// <summary>
        /// 根据prefab或者模版来创建新的 widget。
        /// <remarks>存在父物体得资源故不需要异步加载。</remarks>
        /// </summary>
        /// <param name="parentUI">父物体UI。</param>
        /// <param name="goPrefab">实例化预制体。</param>
        /// <param name="parentTrans">实例化父节点。</param>
        /// <param name="visible">是否可见。</param>
        /// <returns>是否创建成功。</returns>
        public bool CreateByPrefab(UIBase parentUI, GameObject goPrefab, Transform parentTrans, bool visible = true)
        {
            if (parentUI == null) throw new ArgumentNullException(nameof(parentUI));
            parentUI.ThrowIfWidgetOwnerInvalid();
            if (parentTrans == null)
            {
                parentTrans = parentUI.rectTransform;
            }

            return CreateOwned(parentUI, Object.Instantiate(goPrefab, parentTrans), true, visible);
        }

        private bool CreateOwned(UIBase parentUI, GameObject instance, bool bindGo, bool visible)
        {
            bool created = false;
            try
            {
                created = CreateImp(parentUI, instance, bindGo, visible);
                return created;
            }
            finally
            {
                if (!created && instance != null)
                    Object.Destroy(instance);
            }
        }

        private bool CreateImp(UIBase parentUI, GameObject widgetRoot, bool bindGo, bool visible = true)
        {
            if (!ModuleSystem.IsRunning || parentUI == null || parentUI.IsLifecycleInvalid)
            {
                throw new ObjectDisposedException(parentUI?.GetType().Name ?? nameof(UIBase),
                    "The UI owner is closing and cannot create a widget.");
            }

            if (!CreateBase(widgetRoot, bindGo))
            {
                return false;
            }

            RestChildCanvas(parentUI);
            _parent = parentUI;
            Parent.ListChild.Add(this);
            Parent.SetUpdateDirty();
            try
            {
                Inject();
                ScriptGenerator();
                BindMemberProperty();
                RegisterEvent();
                OnCreate();
                OnRefresh();
                IsPrepare = true;

                if (!visible)
                {
                    gameObject.SetActive(false);
                }
                else
                {
                    if (!gameObject.activeSelf)
                    {
                        gameObject.SetActive(true);
                    }
                }

                return true;
            }
            catch
            {
                try
                {
                    _parent?.ListChild.Remove(this);
                    InternalDestroy();
                }
                catch (Exception cleanupException)
                {
                    Log.Warning($"UI widget '{GetType().Name}' cleanup after create failure raised an exception: {cleanupException}");
                }

                throw;
            }
        }

        protected bool CreateBase(GameObject go, bool bindGo)
        {
            if (go == null)
            {
                return false;
            }

            name = GetType().Name;
            transform = go.GetComponent<Transform>();
            rectTransform = transform as RectTransform;
            gameObject = go;
            Log.Assert(rectTransform != null, $"{go.name} ui base element need to be RectTransform");
            return true;
        }

        protected void RestChildCanvas(UIBase parentUI)
        {
            if (parentUI == null || parentUI.gameObject == null)
            {
                return;
            }

            Canvas parentCanvas = parentUI.gameObject.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                return;
            }

            if (gameObject != null)
            {
                var listCanvas = gameObject.GetComponentsInChildren<Canvas>(true);
                for (var index = 0; index < listCanvas.Length; index++)
                {
                    var childCanvas = listCanvas[index];
                    childCanvas.sortingOrder = parentCanvas.sortingOrder + childCanvas.sortingOrder % UIModule.WINDOW_DEEP;
                }
            }
        }

        #endregion

        #region Destroy

        /// <summary>
        /// 组件被销毁调用。
        /// <remarks>请勿手动调用！</remarks>
        /// </summary>
        protected internal void OnDestroyWidget()
        {
            InternalDestroy();
        }

        internal void InternalDestroy()
        {
            if (_destroyStarted)
            {
                return;
            }

            _destroyStarted = true;
            BlockUIEvents();
            Parent?.SetUpdateDirty();

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
                    if (gameObject != null)
                    {
                        Object.Destroy(gameObject);
                    }
                }
                catch (Exception exception)
                {
                    firstException ??= exception;
                }
                finally
                {
                    IsPrepare = false;
                    _parent = null;
                    gameObject = null;
                    transform = null;
                    rectTransform = null;
                }
            }

            if (firstException != null)
            {
                throw firstException;
            }
        }

        /// <summary>
        /// 主动销毁组件。
        /// </summary>
        public void Destroy()
        {
            if (_parent != null)
            {
                _parent.ListChild.Remove(this);
            }

            InternalDestroy();
        }

        #endregion
    }
}
