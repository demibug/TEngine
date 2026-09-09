using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using SceneHandle = YooAsset.SceneHandle;
using YooAsset;

namespace TEngine
{
    internal class SceneModule : Module, ISceneModule
    {
        private string _currentMainSceneName = string.Empty;

        private SceneHandle _currentMainScene;

        private readonly Dictionary<string, SceneHandle> _subScenes = new Dictionary<string, SceneHandle>();

        private readonly HashSet<string> _handlingScene = new HashSet<string>();
        private bool _stopping;
        private int _lifetimeVersion;

        /// <summary>
        /// 当前主场景名称。
        /// </summary>
        public string CurrentMainSceneName => _currentMainSceneName;

        public override void OnInit()
        {
            _stopping = false;
            _lifetimeVersion++;
            _currentMainScene = null;
            _currentMainSceneName = SceneManager.GetSceneByBuildIndex(0).name;
        }

        public override void Shutdown()
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            _lifetimeVersion++;

            var sceneSnapshot = new List<SceneHandle>(_subScenes.Values);
            if (_currentMainScene != null)
            {
                sceneSnapshot.Add(_currentMainScene);
            }

            _subScenes.Clear();
            _handlingScene.Clear();
            _currentMainScene = null;
            _currentMainSceneName = string.Empty;

            foreach (SceneHandle scene in sceneSnapshot)
            {
                if (scene == null)
                {
                    continue;
                }

                try
                {
                    if (scene.IsValid)
                    {
                        // UnloadSceneOperation can wait for a scene that is still loading;
                        // do not leave the owner handle behind merely because SceneObject is
                        // not valid yet.
                        scene.UnloadAsync();
                    }
                }
                catch (Exception exception)
                {
                    try { Log.Error("Scene shutdown failed: {0}", exception); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 加载场景。
        /// </summary>
        /// <param name="location">场景的定位地址</param>
        /// <param name="sceneMode">场景加载模式</param>
        /// <param name="suspendLoad">加载完毕时是否主动挂起</param>
        /// <param name="priority">优先级</param>
        /// <param name="gcCollect">加载主场景是否回收垃圾。</param>
        /// <param name="progressCallBack">加载进度回调。</param>
        public async UniTask<Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            bool gcCollect = true, Action<float> progressCallBack = null)
        {
            if (!CanAcceptNewWork())
            {
                return default;
            }

            int lifetimeVersion = _lifetimeVersion;
            if (!_handlingScene.Add(location))
            {
                Log.Error($"Could not load scene while loading. Scene: {location}");
                return default;
            }

            try
            {
                if (sceneMode == LoadSceneMode.Additive)
                {
                    if (_subScenes.TryGetValue(location, out SceneHandle subScene))
                    {
                        throw new Exception($"Could not load subScene while already loaded. Scene: {location}");
                    }

                    subScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

                    //Fix 这里前置，subScene.IsDone在UnSupendLoad之后才会是true
                    _subScenes.Add(location, subScene);

                    if (progressCallBack != null)
                    {
                        while (!subScene.IsDone && subScene.IsValid)
                        {
                            if (!IsCurrentLifetime(lifetimeVersion))
                            {
                                return default;
                            }

                            progressCallBack.Invoke(subScene.Progress);
                            await UniTask.Yield();
                        }
                    }
                    else
                    {
                        await subScene.ToUniTask();
                    }

                    if (!IsCurrentLifetime(lifetimeVersion))
                    {
                        return default;
                    }

                    return subScene.SceneObject;
                }
                else
                {
                    if (_currentMainScene is { IsDone: false })
                    {
                        throw new Exception($"Could not load MainScene while loading. CurrentMainScene: {_currentMainSceneName}.");
                    }

                    _currentMainSceneName = location;

                    _currentMainScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

                    if (progressCallBack != null)
                    {
                        while (!_currentMainScene.IsDone && _currentMainScene.IsValid)
                        {
                            if (!IsCurrentLifetime(lifetimeVersion))
                            {
                                return default;
                            }

                            progressCallBack.Invoke(_currentMainScene.Progress);
                            await UniTask.Yield();
                        }
                    }
                    else
                    {
                        await _currentMainScene.ToUniTask();
                    }

                    if (!IsCurrentLifetime(lifetimeVersion))
                    {
                        return default;
                    }
#if UNITY_EDITOR&&EditorFixedMaterialShader
                    Utility.MaterialHelper.WaitGetRootGameObjects(_currentMainScene).Forget();
#endif
                    ModuleSystem.TryGetExistingModule<IResourceModule>()?.ForceUnloadUnusedAssets(gcCollect);

                    return _currentMainScene.SceneObject;
                }
            }
            finally
            {
                _handlingScene.Remove(location);
            }
        }

        /// <summary>
        /// 加载场景。
        /// </summary>
        /// <param name="location">场景的定位地址</param>
        /// <param name="sceneMode">场景加载模式</param>
        /// <param name="suspendLoad">加载完毕时是否主动挂起</param>
        /// <param name="priority">优先级</param>
        /// <param name="callBack">加载回调。</param>
        /// <param name="gcCollect">加载主场景是否回收垃圾。</param>
        /// <param name="progressCallBack">加载进度回调。</param>
        public void LoadScene(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            Action<Scene> callBack = null,
            bool gcCollect = true, Action<float> progressCallBack = null)
        {
            if (!CanAcceptNewWork())
            {
                return;
            }

            int lifetimeVersion = _lifetimeVersion;
            if (!_handlingScene.Add(location))
            {
                Log.Error($"Could not load scene while loading. Scene: {location}");
                return;
            }

            try
            {
                if (sceneMode == LoadSceneMode.Additive)
                {
                    if (_subScenes.TryGetValue(location, out SceneHandle subScene))
                    {
                        Log.Warning($"Could not load subScene while already loaded. Scene: {location}");
                        return;
                    }

                    subScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

                    subScene.Completed += handle =>
                    {
                        _handlingScene.Remove(location);
                        if (!IsCurrentLifetime(lifetimeVersion))
                        {
                            return;
                        }

                        callBack?.Invoke(handle.SceneObject);
                    };

                    if (progressCallBack != null)
                    {
                        InvokeProgress(subScene, progressCallBack).Forget();
                    }

                    _subScenes.Add(location, subScene);
                }
                else
                {
                    if (_currentMainScene is { IsDone: false })
                    {
                        Log.Warning($"Could not load MainScene while loading. CurrentMainScene: {_currentMainSceneName}.");
                        return;
                    }

                    _currentMainSceneName = location;

                    _currentMainScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

                    _currentMainScene.Completed += handle =>
                    {
                        _handlingScene.Remove(location);
                        if (!IsCurrentLifetime(lifetimeVersion))
                        {
                            return;
                        }

                        callBack?.Invoke(handle.SceneObject);
                    };

                    if (progressCallBack != null)
                    {
                        InvokeProgress(_currentMainScene, progressCallBack).Forget();
                    }
#if UNITY_EDITOR&&EditorFixedMaterialShader
                    Utility.MaterialHelper.WaitGetRootGameObjects(_currentMainScene).Forget();
#endif
                    ModuleSystem.TryGetExistingModule<IResourceModule>()?.ForceUnloadUnusedAssets(gcCollect);
                }
            }
            catch
            {
                _handlingScene.Remove(location);
                throw;
            }
        }

        private async UniTaskVoid InvokeProgress(SceneHandle sceneHandle, Action<float> progress)
        {
            if (sceneHandle == null)
            {
                return;
            }

            int lifetimeVersion = _lifetimeVersion;
            while (IsCurrentLifetime(lifetimeVersion) && !sceneHandle.IsDone && sceneHandle.IsValid)
            {
                await UniTask.Yield();

                if (IsCurrentLifetime(lifetimeVersion))
                {
                    progress?.Invoke(sceneHandle.Progress);
                }
            }
        }

        /// <summary>
        /// 激活场景（当同时存在多个场景时用于切换激活场景）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否操作成功。</returns>
        public bool ActivateScene(string location)
        {
            if (!CanAcceptNewWork())
            {
                return false;
            }

            if (_currentMainSceneName.Equals(location))
            {
                if (_currentMainScene != null)
                {
                    return _currentMainScene.ActivateScene();
                }

                return false;
            }

            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                return subScene.ActivateScene();
            }

            Log.Warning($"IsMainScene invalid location:{location}");
            return false;
        }

        /// <summary>
        /// 解除场景加载挂起操作。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否操作成功。</returns>
        public bool UnSuspend(string location)
        {
            if (!CanAcceptNewWork())
            {
                return false;
            }

            if (_currentMainSceneName.Equals(location))
            {
                if (_currentMainScene != null)
                {
                    return _currentMainScene.UnSuspend();
                }

                return false;
            }

            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                return subScene.UnSuspend();
            }

            Log.Warning($"IsMainScene invalid location:{location}");
            return false;
        }

        /// <summary>
        /// 是否为主场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否主场景。</returns>
        public bool IsMainScene(string location)
        {
            if (!CanAcceptNewWork())
            {
                return false;
            }

            // 获取当前激活的场景  
            Scene currentScene = SceneManager.GetActiveScene();

            if (_currentMainSceneName.Equals(location))
            {
                if (_currentMainScene == null)
                {
                    return false;
                }

                // 判断当前场景是否是主场景  
                if (currentScene.name == _currentMainScene.SceneName)
                {
                    return true;
                }

                return _currentMainScene.SceneName == currentScene.name;
            }

            // 判断当前场景是否是主场景  
            if (currentScene.name == _currentMainScene?.SceneName)
            {
                return true;
            }

            Log.Warning($"IsMainScene invalid location:{location}");
            return false;
        }

        /// <summary>
        /// 异步卸载子场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public async UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null)
        {
            if (!CanAcceptNewWork())
            {
                return false;
            }

            int lifetimeVersion = _lifetimeVersion;
            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                if (subScene.SceneObject == default)
                {
                    Log.Error($"Could not unload Scene while not loaded. Scene: {location}");
                    return false;
                }

                if (!_handlingScene.Add(location))
                {
                    Log.Warning($"Could not unload Scene while loading. Scene: {location}");
                    return false;
                }

                var unloadOperation = subScene.UnloadAsync();

                try
                {
                    if (progressCallBack != null)
                    {
                        while (!unloadOperation.IsDone && unloadOperation.Status != EOperationStatus.Failed)
                        {
                            if (!IsCurrentLifetime(lifetimeVersion))
                            {
                                return false;
                            }

                            progressCallBack.Invoke(unloadOperation.Progress);
                            await UniTask.Yield();
                        }
                    }
                    else
                    {
                        await unloadOperation.ToUniTask();
                    }

                    if (!IsCurrentLifetime(lifetimeVersion))
                    {
                        return false;
                    }

                    _subScenes.Remove(location);

                    return true;
                }
                finally
                {
                    _handlingScene.Remove(location);
                }
            }

            Log.Warning($"UnloadAsync invalid location:{location}");
            return false;
        }

        /// <summary>
        /// 异步卸载子场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="callBack">卸载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public void Unload(string location, Action callBack = null, Action<float> progressCallBack = null)
        {
            if (!CanAcceptNewWork())
            {
                return;
            }

            int lifetimeVersion = _lifetimeVersion;
            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                if (subScene.SceneObject == default)
                {
                    Log.Error($"Could not unload Scene while not loaded. Scene: {location}");
                    return;
                }

                if (!_handlingScene.Add(location))
                {
                    Log.Warning($"Could not unload Scene while loading. Scene: {location}");
                    return;
                }

                try
                {
                    var unloadOperation = subScene.UnloadAsync();
                    unloadOperation.Completed += @base =>
                    {
                        _handlingScene.Remove(location);
                        if (!IsCurrentLifetime(lifetimeVersion))
                        {
                            return;
                        }

                        _subScenes.Remove(location);
                        callBack?.Invoke();
                    };

                    if (progressCallBack != null)
                    {
                        InvokeProgress(subScene, progressCallBack).Forget();
                    }
                }
                catch
                {
                    _handlingScene.Remove(location);
                    throw;
                }

                return;
            }

            Log.Warning($"UnloadAsync invalid location:{location}");
        }

        /// <summary>
        /// 是否包含场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否包含场景。</returns>
        public bool IsContainScene(string location)
        {
            if (!CanAcceptNewWork())
            {
                return false;
            }

            if (_currentMainSceneName.Equals(location))
            {
                return true;
            }

            return _subScenes.TryGetValue(location, out var _);
        }

        private bool CanAcceptNewWork()
        {
            return !_stopping && ModuleSystem.IsRunning;
        }

        private bool IsCurrentLifetime(int lifetimeVersion)
        {
            return CanAcceptNewWork() && lifetimeVersion == _lifetimeVersion;
        }
    }
}
