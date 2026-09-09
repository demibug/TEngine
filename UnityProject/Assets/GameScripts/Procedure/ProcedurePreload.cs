using System.Collections.Generic;
using Launcher;
using TEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    /// <summary>
    /// 预加载流程
    /// </summary>
    public class ProcedurePreload : ProcedureBase
    {
        /// <summary>
        /// 预热执行器：代际隔离晚回调；成功后配对归还预加载引用；进度统计终态项。
        /// </summary>
        private PreloadRequestRunner _preloadRunner;

        public override bool UseNativeDialog => true;

        private readonly bool _needProLoadConfig = true;

        private ProcedureOwner _procedureOwner;

        protected override void OnInit(ProcedureOwner procedureOwner)
        {
            base.OnInit(procedureOwner);
            _procedureOwner = procedureOwner;
        }

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            base.OnEnter(procedureOwner);

            // 每次进入建立新代际：旧代际晚回调只归还资源，不更新新流程状态或触发跳转。
            _preloadRunner = new PreloadRequestRunner(_resourceModule);

            LauncherMgr.ShowUI<LoadUpdateUI>(Utility.Text.Format(LoadText.Instance.Label_Load_Load_Progress, 0));

            GameEvent.Send("UILoadUpdate.RefreshVersion");

            PreloadResources();
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            try
            {
                base.OnLeave(procedureOwner, isShutdown);
            }
            finally
            {
                // 离开即失效当前代际；晚到的旧终态仍归还资源。
                _preloadRunner?.Invalidate();
                _preloadRunner = null;
            }
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (_preloadRunner == null || _preloadRunner.IsEmpty)
            {
                // 无预热请求（如 EditorSimulate 跳过预热）：保持“预热尽力而为，全部终态后继续”。
                LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Load_Complete);
                ChangeProcedureToLoadAssembly();
                return;
            }

            // 进度统计终态项，不依赖字典遇到第一个未完成项就 break。
            LauncherMgr.ShowUI<LoadUpdateUI>(Utility.Text.Format(LoadText.Instance.Label_Load_Load_Progress, _preloadRunner.Progress * 100));

            if (!_preloadRunner.AllTerminal)
            {
                return;
            }

            ChangeProcedureToLoadAssembly();
        }

        private void PreloadResources()
        {
            if (_needProLoadConfig)
            {
                LoadAllConfig();
            }
        }

        private void LoadAllConfig()
        {
            if (_resourceModule.PlayMode == EPlayMode.EditorSimulateMode)
            {
                return;
            }

            // 先建立去重请求清单，再发起加载；PRELOAD 与 WEBGL_PRELOAD 重叠地址只加载一次。
            List<string> addresses = new List<string>();
            AssetInfo[] assetInfos = _resourceModule.GetAssetInfos("PRELOAD");
            foreach (var assetInfo in assetInfos)
            {
                addresses.Add(assetInfo.Address);
            }

#if UNITY_WEBGL
            AssetInfo[] webAssetInfos = _resourceModule.GetAssetInfos("WEBGL_PRELOAD");
            foreach (var assetInfo in webAssetInfos)
            {
                addresses.Add(assetInfo.Address);
            }
#endif
            if (addresses.Count <= 0)
            {
                return;
            }

            _preloadRunner.Begin(addresses);
        }

        private void ChangeProcedureToLoadAssembly()
        {
            if (ModuleSystem.IsRunning)
            {
                ChangeState<ProcedureLoadAssembly>(_procedureOwner);
            }
        }
    }
}
