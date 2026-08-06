using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    /// <summary>
    /// 预加载流程
    /// </summary>
    public class ProcedurePreload : ProcedureBase
    {
        private float _progress = 0f;

        private readonly Dictionary<string, bool> _loadedFlag = new Dictionary<string, bool>();

        public override bool UseNativeDialog => true;

        private readonly bool _needProLoadConfig = true;

        private ProcedureOwner _procedureOwner;

        /// <summary>
        /// 预加载回调。
        /// </summary>
        private LoadAssetCallbacks m_PreLoadAssetCallbacks;

        protected override void OnInit(ProcedureOwner procedureOwner)
        {
            base.OnInit(procedureOwner);
            _procedureOwner = procedureOwner;
            m_PreLoadAssetCallbacks = new LoadAssetCallbacks(OnPreLoadAssetSuccess, OnPreLoadAssetFailure);
        }


        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);

            _loadedFlag.Clear();

            LauncherMgr.ShowUI<LoadUpdateUI>(Utility.Text.Format(LoadText.Instance.Label_Load_Load_Progress, 0));

            GameEvent.Send("UILoadUpdate.RefreshVersion");

            PreloadResources();
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            var totalCount = _loadedFlag.Count <= 0 ? 1 : _loadedFlag.Count;

            var loadCount = _loadedFlag.Count <= 0 ? 1 : 0;

            foreach (KeyValuePair<string, bool> loadedFlag in _loadedFlag)
            {
                if (!loadedFlag.Value)
                {
                    break;
                }
                else
                {
                    loadCount++;
                }
            }

            if (_loadedFlag.Count != 0)
            {
                LauncherMgr.ShowUI<LoadUpdateUI>(Utility.Text.Format(LoadText.Instance.Label_Load_Load_Progress, (float)loadCount / totalCount * 100));
            }
            else
            {
                LauncherMgr.RefreshProgress(_progress);

                string progressStr = $"{_progress * 100:f1}";

                if (Math.Abs(_progress - 1f) < 0.001f)
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Load_Complete);
                }
                else
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>(Utility.Text.Format(LoadText.Instance.Label_Load_Load_Progress, progressStr));
                }
            }

            if (loadCount < totalCount)
            {
                return;
            }

            ChangeProcedureToLoadAssembly();
        }


        private async UniTaskVoid SmoothValue(float value, float duration, Action callback = null)
        {
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                var result = Mathf.Lerp(0, value, time / duration);
                _progress = result;
                await UniTask.Yield();
            }

            _progress = value;
            callback?.Invoke();
        }

        private void PreloadResources()
        {
            if (_needProLoadConfig)
            {
                LoadAllConfig();
            }
        }

        // 时序契约（task 1.11 / battle-config-snapshot spec）：
        // 1. AssetBundleCollectorSetting.asset 的 Configs 组已打 PRELOAD 标签，
        //    本方法 GetAssetInfos("PRELOAD") 会取到 Assets/AssetRaw/Configs/bytes/ 下的
        //    battle_*.bytes / item_tbitem.bytes 等配置二进制，由 PreLoad 异步预加载到 YooAsset 缓存。
        // 2. ConfigSystem.Instance.Load() 的显式调用必须在热更域入口 GameApp.Entrance
        //    （GameEventHelper.Init() 之后、StartGameLogic() 之前）完成——本文件属主包
        //    Assembly-CSharp（Assets/GameScripts/Procedure/ 无 asmdef），不得直接引用热更程序集
        //    GameProto，故不在此处调用 ConfigSystem（避免主包→热更逆向依赖，违反热更边界）。
        // 3. GameApp.Entrance 由 ProcedureLoadAssembly 反射调用，时序晚于本流程，
        //    LoadByteBuf 的同步 LoadAsset<TextAsset> 届时命中 PRELOAD 缓存，不触发真实同步 IO。
        // 4. ConfigSystem.Tables getter 已硬化为未 Load 时抛 InvalidOperationException，
        //    任何子步懒加载会被立即发现，满足“BattleSimulation 子步不触发同步 IO”。
        private void LoadAllConfig()
        {
            if (_resourceModule.PlayMode == EPlayMode.EditorSimulateMode)
            {
                return;
            }

            AssetInfo[] assetInfos = _resourceModule.GetAssetInfos("PRELOAD");
            foreach (var assetInfo in assetInfos)
            {
                PreLoad(assetInfo.Address);
            }
#if UNITY_WEBGL
            AssetInfo[] webAssetInfos = _resourceModule.GetAssetInfos("WEBGL_PRELOAD");
            foreach (var assetInfo in webAssetInfos)
            {
                PreLoad(assetInfo.Address);
            }
#endif
            if (_loadedFlag.Count <= 0)
            {
                // SmoothValue(1, 1f, ChangeProcedureToLoadAssembly).Forget();
                return;
            }
        }

        private void PreLoad(string location)
        {
            _loadedFlag.Add(location, false);
            _resourceModule.LoadAssetAsync(location, 100, m_PreLoadAssetCallbacks, null);
        }

        private void OnPreLoadAssetFailure(string assetName, LoadResourceStatus status, string errormessage, object userdata)
        {
            Log.Warning("Can not preload asset from '{0}' with error message '{1}'.", assetName, errormessage);
            _loadedFlag[assetName] = true;
        }

        private void OnPreLoadAssetSuccess(string assetName, object asset, float duration, object userdata)
        {
            Log.Debug("Success preload asset from '{0}' duration '{1}'.", assetName, duration);
            _loadedFlag[assetName] = true;
        }

        private void ChangeProcedureToLoadAssembly()
        {
            ChangeState<ProcedureLoadAssembly>(_procedureOwner);
        }
    }
}
