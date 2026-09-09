using System;
using System.Collections.Generic;
using TEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GameLogic
{
    public interface ISingleton
    {
        /// <summary>
        /// 激活接口，通常用于在某个时机手动实例化
        /// </summary>
        void Active();

        /// <summary>
        /// 释放接口
        /// </summary>
        void Release();
    }

    public interface IUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnUpdate();
    }

    public interface IFixedUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnFixedUpdate();
    }

    public interface ILateUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnLateUpdate();
    }

    public interface IDrawGizmos
    {
        void OnDrawGizmos();
    }
    
    public interface IDrawGizmosSelected
    {
        void OnDrawGizmosSelected();
    }

    /// <summary>
    /// 让统一关闭器在 Unity 延迟 Destroy 之前摘除 MonoBehaviour 单例的静态引用。
    /// </summary>
    internal interface ISingletonInstanceResetter
    {
        void ClearInstanceForShutdown();
    }

    /// <summary>
    /// 框架中的全局对象与Unity场景依赖相关的DontDestroyOnLoad需要统一管理，方便重启游戏时清除工作
    /// </summary>
    public static class SingletonSystem
    {
        private static IUpdateDriver _updateDriver;
        private static readonly List<ISingleton> _singletons = new List<ISingleton>();
        private static readonly List<IUpdate> _updates = new List<IUpdate>();
        private static readonly List<IFixedUpdate> _fixedUpdates = new List<IFixedUpdate>();
        private static readonly List<ILateUpdate> _lateUpdates = new List<ILateUpdate>();
#if UNITY_EDITOR
        private static readonly List<IDrawGizmos> _drawGizmos = new List<IDrawGizmos>();
        private static readonly List<IDrawGizmosSelected> _drawGizmosSelecteds = new List<IDrawGizmosSelected>();
#endif
        
        private static readonly Dictionary<string, GameObject> _gameObjects = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, object> _gameObjectOwners = new Dictionary<string, object>();
        private static readonly List<Action> _sessionResetters = new List<Action>();
        private static readonly HashSet<Action> _sessionResetterSet = new HashSet<Action>();
        private static bool _isReleasing;
        private static bool _shutdownHookRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            var resetterSnapshot = new List<Action>(_sessionResetters);
            foreach (Action resetter in resetterSnapshot)
            {
                try
                {
                    resetter();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Singleton session reset failed: {0}", exception);
                }
            }

            _sessionResetters.Clear();
            _sessionResetterSet.Clear();
            _gameObjects.Clear();
            _gameObjectOwners.Clear();
            _singletons.Clear();
            _updates.Clear();
            _fixedUpdates.Clear();
            _lateUpdates.Clear();
#if UNITY_EDITOR
            _drawGizmos.Clear();
            _drawGizmosSelecteds.Clear();
#endif
            _updateDriver = null;
            _isInit = false;
            _isReleasing = false;
            _shutdownHookRegistered = false;
        }

        internal static void RegisterSessionResetter(Action resetter)
        {
            if (resetter != null && _sessionResetterSet.Add(resetter))
            {
                _sessionResetters.Add(resetter);
            }
        }

        public static void Retain(ISingleton singleton)
        {
            EnsureCanRegister();
            if (singleton == null)
            {
                throw new GameFrameworkException("Singleton is invalid.");
            }

            CheckInit();
            EnsureShutdownHook();

            if (_singletons.Contains(singleton))
            {
                return;
            }

            _singletons.Add(singleton);

            BuildLifeCycle(singleton);
        }

        public static void Retain(GameObject go, object singleton)
        {
            EnsureCanRegister();
            if (go == null || singleton == null)
            {
                throw new GameFrameworkException("Singleton GameObject or instance is invalid.");
            }

            CheckInit();
            EnsureShutdownHook();

            if (_gameObjects.TryAdd(go.name, go))
            {
                _gameObjectOwners[go.name] = singleton;
                if (Application.isPlaying)
                {
                    Object.DontDestroyOnLoad(go);
                }

                BuildLifeCycle(singleton);
            }
        }

        private static void BuildLifeCycle(object singleton)
        {
            Type iUpdate = typeof(IUpdate);
            bool needUpdate = iUpdate.IsInstanceOfType(singleton);
            if (needUpdate && singleton is IUpdate update)
            {
                _updates.Add(update);
            }

            Type iFixedUpdate = typeof(IFixedUpdate);
            bool needFixedUpdate = iFixedUpdate.IsInstanceOfType(singleton);
            if (needFixedUpdate && singleton is IFixedUpdate fixedUpdate)
            {
                _fixedUpdates.Add(fixedUpdate);
            }

            Type iLateUpdate = typeof(ILateUpdate);
            bool needLateUpdate = iLateUpdate.IsInstanceOfType(singleton);
            if (needLateUpdate && singleton is ILateUpdate lateUpdate)
            {
                _lateUpdates.Add(lateUpdate);
            }

#if UNITY_EDITOR
            Type iDrawGizmos = typeof(IDrawGizmos);
            bool needDrawGizmos = iDrawGizmos.IsInstanceOfType(singleton);
            if (needDrawGizmos && singleton is IDrawGizmos drawGizmos)
            {
                _drawGizmos.Add(drawGizmos);
            }
            
            Type iDrawGizmosSelected = typeof(IDrawGizmosSelected);
            bool needDrawGizmosSelected = iDrawGizmosSelected.IsInstanceOfType(singleton);
            if (needDrawGizmosSelected && singleton is IDrawGizmosSelected drawGizmosSelected)
            {
                _drawGizmosSelecteds.Add(drawGizmosSelected);
            }
#endif
        }

        public static void Release(GameObject go, object singleton)
        {
            if (go == null || _gameObjects == null)
            {
                return;
            }

            string name = go.name;
            if (_gameObjects.TryGetValue(name, out GameObject registered) &&
                ReferenceEquals(registered, go))
            {
                _gameObjects.Remove(name);
                _gameObjectOwners.Remove(name);
                ReleaseLifeCycle(singleton);
                try
                {
                    Object.Destroy(go);
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Singleton GameObject destroy failed: {0}", exception);
                }
            }
        }

        public static void Release(ISingleton singleton)
        {
            if (_singletons != null && _singletons.Contains(singleton))
            {
                _singletons.Remove(singleton);
                ReleaseLifeCycle(singleton);
            }
        }
        
        private static void ReleaseLifeCycle(object singleton)
        {
            Type iUpdate = typeof(IUpdate);
            bool needUpdate = iUpdate.IsInstanceOfType(singleton);
            if (needUpdate && singleton is IUpdate update)
            {
                if (_updates.Contains(update))
                {
                    _updates.Remove(update);
                }
            }

            Type iFixedUpdate = typeof(IFixedUpdate);
            bool needFixedUpdate = iFixedUpdate.IsInstanceOfType(singleton);
            if (needFixedUpdate && singleton is IFixedUpdate fixedUpdate)
            {
                if (_fixedUpdates.Contains(fixedUpdate))
                {
                    _fixedUpdates.Remove(fixedUpdate);
                }
            }

            Type iLateUpdate = typeof(ILateUpdate);
            bool needLateUpdate = iLateUpdate.IsInstanceOfType(singleton);
            if (needLateUpdate && singleton is ILateUpdate lateUpdate)
            {
                if (_lateUpdates.Contains(lateUpdate))
                {
                    _lateUpdates.Remove(lateUpdate);
                }
            }

#if UNITY_EDITOR
            Type iDrawGizmos = typeof(IDrawGizmos);
            bool needDrawGizmos = iDrawGizmos.IsInstanceOfType(singleton);
            if (needDrawGizmos && singleton is IDrawGizmos drawGizmos)
            {
                if (_drawGizmos.Contains(drawGizmos))
                {
                    _drawGizmos.Remove(drawGizmos);
                }
            }
            
            Type iDrawGizmosSelected = typeof(IDrawGizmosSelected);
            bool needDrawGizmosSelected = iDrawGizmosSelected.IsInstanceOfType(singleton);
            if (needDrawGizmosSelected && singleton is IDrawGizmosSelected drawGizmosSelected)
            {
                if (_drawGizmosSelecteds.Contains(drawGizmosSelected))
                {
                    _drawGizmosSelecteds.Remove(drawGizmosSelected);
                }
            }
#endif
        }

        public static void Release()
        {
            if (_isReleasing)
            {
                return;
            }

            _isReleasing = true;
            try
            {
                var gameObjectSnapshot = new List<KeyValuePair<GameObject, object>>(_gameObjectOwners.Count);
                foreach (KeyValuePair<string, GameObject> pair in _gameObjects)
                {
                    _gameObjectOwners.TryGetValue(pair.Key, out object owner);
                    gameObjectSnapshot.Add(new KeyValuePair<GameObject, object>(pair.Value, owner));
                }

                _gameObjects.Clear();
                _gameObjectOwners.Clear();
                _updates.Clear();
                _fixedUpdates.Clear();
                _lateUpdates.Clear();
#if UNITY_EDITOR
                _drawGizmos.Clear();
                _drawGizmosSelecteds.Clear();
#endif

                foreach (KeyValuePair<GameObject, object> entry in gameObjectSnapshot)
                {
                    ClearInstanceReference(entry.Value);
                    if (entry.Key == null)
                    {
                        continue;
                    }

                    try
                    {
                        Object.Destroy(entry.Key);
                    }
                    catch (Exception exception)
                    {
                        LogErrorSafely("Singleton GameObject destroy failed: {0}", exception);
                    }
                }

                var singletonSnapshot = new List<ISingleton>(_singletons);
                _singletons.Clear();
                for (int i = singletonSnapshot.Count - 1; i >= 0; i--)
                {
                    ISingleton singleton = singletonSnapshot[i];
                    if (singleton == null)
                    {
                        continue;
                    }

                    try
                    {
                        singleton.Release();
                    }
                    catch (Exception exception)
                    {
                        LogErrorSafely("Singleton release failed: {0}", exception);
                    }
                    finally
                    {
                        ClearInstanceReference(singleton);
                    }
                }

                DeInit();
                try
                {
                    Resources.UnloadUnusedAssets();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Singleton resource unload failed: {0}", exception);
                }
            }
            finally
            {
                _gameObjects.Clear();
                _gameObjectOwners.Clear();
                _singletons.Clear();
                _updates.Clear();
                _fixedUpdates.Clear();
                _lateUpdates.Clear();
#if UNITY_EDITOR
                _drawGizmos.Clear();
                _drawGizmosSelecteds.Clear();
#endif
                _sessionResetters.Clear();
                _sessionResetterSet.Clear();
                RootModule.BeforeShutdown -= Release;
                _shutdownHookRegistered = false;
                DeInit();
                _isReleasing = false;
            }
        }

        private static void EnsureCanRegister()
        {
            if (!ModuleSystem.IsRunning)
            {
                throw new GameFrameworkException("Can not register a singleton while the module system is shutting down.");
            }
        }

        private static void EnsureShutdownHook()
        {
            if (_shutdownHookRegistered || !ModuleSystem.IsRunning)
            {
                return;
            }

            RootModule.BeforeShutdown += Release;
            _shutdownHookRegistered = true;
        }

        private static void ClearInstanceReference(object singleton)
        {
            if (singleton is ISingletonInstanceResetter resetter)
            {
                try
                {
                    resetter.ClearInstanceForShutdown();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Singleton static reference cleanup failed: {0}", exception);
                }
            }
        }

        public static GameObject GetGameObject(string name)
        {
            GameObject go = null;
            if (_gameObjects != null)
            {
                _gameObjects.TryGetValue(name, out go);
            }

            return go;
        }

        internal static bool ContainsKey(string name)
        {
            if (_gameObjects != null)
            {
                return _gameObjects.ContainsKey(name);
            }

            return false;
        }

        public static void Restart()
        {
            if (!ModuleSystem.IsRunning)
            {
                throw new GameFrameworkException("Can not restart while the module system is shutting down.");
            }

            if (Camera.main != null)
            {
                Camera.main.gameObject.SetActive(false);
            }

            Release();
            SceneManager.LoadScene(0);
        }

        internal static ISingleton GetSingleton(string name)
        {
            for (int i = 0; i < _singletons.Count; ++i)
            {
                if (_singletons[i].ToString() == name)
                {
                    return _singletons[i];
                }
            }

            return null;
        }

        #region 生命周期

        private static bool _isInit = false;

        private static void CheckInit()
        {
            if (_isInit == true)
            {
                return;
            }

            IUpdateDriver updateDriver = null;
            bool updateAdded = false;
            bool fixedUpdateAdded = false;
            bool lateUpdateAdded = false;
#if UNITY_EDITOR
            bool drawGizmosAdded = false;
            bool drawGizmosSelectedAdded = false;
#endif

            try
            {
                updateDriver = _updateDriver ?? ModuleSystem.GetModule<IUpdateDriver>();
                updateDriver.AddUpdateListener(OnUpdate);
                updateAdded = true;
                updateDriver.AddFixedUpdateListener(OnFixedUpdate);
                fixedUpdateAdded = true;
                updateDriver.AddLateUpdateListener(OnLateUpdate);
                lateUpdateAdded = true;
#if UNITY_EDITOR
                updateDriver.AddOnDrawGizmosListener(OnDrawGizmos);
                drawGizmosAdded = true;
                updateDriver.AddOnDrawGizmosSelectedListener(OnDrawGizmosSelected);
                drawGizmosSelectedAdded = true;
#endif
                _updateDriver = updateDriver;
                _isInit = true;
            }
            catch
            {
                if (updateAdded)
                {
                    try { updateDriver.RemoveUpdateListener(OnUpdate); } catch (Exception exception) { LogErrorSafely("Singleton update listener rollback failed: {0}", exception); }
                }

                if (fixedUpdateAdded)
                {
                    try { updateDriver.RemoveFixedUpdateListener(OnFixedUpdate); } catch (Exception exception) { LogErrorSafely("Singleton fixed listener rollback failed: {0}", exception); }
                }

                if (lateUpdateAdded)
                {
                    try { updateDriver.RemoveLateUpdateListener(OnLateUpdate); } catch (Exception exception) { LogErrorSafely("Singleton late listener rollback failed: {0}", exception); }
                }
#if UNITY_EDITOR
                if (drawGizmosAdded)
                {
                    try { updateDriver.RemoveOnDrawGizmosListener(OnDrawGizmos); } catch (Exception exception) { LogErrorSafely("Singleton gizmo listener rollback failed: {0}", exception); }
                }

                if (drawGizmosSelectedAdded)
                {
                    try { updateDriver.RemoveOnDrawGizmosSelectedListener(OnDrawGizmosSelected); } catch (Exception exception) { LogErrorSafely("Singleton selected gizmo listener rollback failed: {0}", exception); }
                }
#endif
                _updateDriver = null;
                _isInit = false;
                throw;
            }
        }
        
        private static void DeInit()
        {
            if (_isInit == false)
            {
                return;
            }

            _isInit = false;

            IUpdateDriver updateDriver = _updateDriver ?? ModuleSystem.TryGetExistingModule<IUpdateDriver>();
            if (updateDriver == null)
            {
                _updateDriver = null;
                return;
            }

            try
            {
                updateDriver.RemoveUpdateListener(OnUpdate);
            }
            catch (Exception exception)
            {
                LogErrorSafely("Singleton update listener removal failed: {0}", exception);
            }

            try
            {
                updateDriver.RemoveFixedUpdateListener(OnFixedUpdate);
            }
            catch (Exception exception)
            {
                LogErrorSafely("Singleton fixed listener removal failed: {0}", exception);
            }

            try
            {
                updateDriver.RemoveLateUpdateListener(OnLateUpdate);
            }
            catch (Exception exception)
            {
                LogErrorSafely("Singleton late listener removal failed: {0}", exception);
            }
#if UNITY_EDITOR
            try
            {
                updateDriver.RemoveOnDrawGizmosListener(OnDrawGizmos);
            }
            catch (Exception exception)
            {
                LogErrorSafely("Singleton gizmo listener removal failed: {0}", exception);
            }

            try
            {
                updateDriver.RemoveOnDrawGizmosSelectedListener(OnDrawGizmosSelected);
            }
            catch (Exception exception)
            {
                LogErrorSafely("Singleton selected gizmo listener removal failed: {0}", exception);
            }
#endif
            _updateDriver = null;
        }

        private static void OnUpdate()
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            var snapshot = new List<IUpdate>(_updates);
            foreach (var update in snapshot)
            {
                if (!ModuleSystem.IsRunning)
                {
                    break;
                }

                try
                {
                    update.OnUpdate();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Singleton update failed: {0}", exception);
                }
            }
        }

        private static void OnFixedUpdate()
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            var snapshot = new List<IFixedUpdate>(_fixedUpdates);
            foreach (var fixedUpdate in snapshot)
            {
                if (!ModuleSystem.IsRunning)
                {
                    break;
                }

                try
                {
                    fixedUpdate.OnFixedUpdate();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Singleton fixed update failed: {0}", exception);
                }
            }
        }

        private static void OnLateUpdate()
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            var snapshot = new List<ILateUpdate>(_lateUpdates);
            foreach (var lateUpdate in snapshot)
            {
                if (!ModuleSystem.IsRunning)
                {
                    break;
                }

                try
                {
                    lateUpdate.OnLateUpdate();
                }
                catch (Exception exception)
                {
                    LogErrorSafely("Singleton late update failed: {0}", exception);
                }
            }
        }

        private static void OnDrawGizmos()
        {
#if UNITY_EDITOR
            foreach (var drawGizmo in _drawGizmos)
            {
                drawGizmo.OnDrawGizmos();
            }
#endif
        }
            
        private static void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            foreach (var drawGizmosSelected in _drawGizmosSelecteds)
            {
                drawGizmosSelected.OnDrawGizmosSelected();
            }
#endif
        }

        private static void LogErrorSafely(string format, Exception exception)
        {
            try { Log.Error(format, exception); }
            catch { }
        }
        #endregion
    }
}
