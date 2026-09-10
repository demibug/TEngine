using System.Collections;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;
using UnityEngine;
using YooAsset;
using ProcedureOwner = TEngine.IFsm<TEngine.IProcedureModule>;

namespace Procedure
{
    public class ProcedureInitResources : ProcedureBase
    {
        private bool _initResourcesComplete = false;

        public override bool UseNativeDialog => true;

        private ProcedureOwner _procedureOwner;

        private StartupAttempt _twoStageAttempt;
        private int _nextTwoStageAttemptId;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            _procedureOwner = procedureOwner;

            base.OnEnter(procedureOwner);

            _initResourcesComplete = false;

            LauncherMgr.ShowUI<LoadUpdateUI>("初始化资源中...");

            if (Settings.UpdateSetting != null && Settings.UpdateSetting.EnableTwoStageUpdate)
            {
                StartTwoStageManifestAttempt();
                return;
            }

            // 注意：使用单机模式并初始化资源前，需要先构建 AssetBundle 并复制到 StreamingAssets 中，否则会产生 HTTP 404 错误
            Utility.Unity.StartCoroutine(InitResources(procedureOwner));
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            try
            {
                base.OnLeave(procedureOwner, isShutdown);
            }
            finally
            {
                StartupAttempt attempt = _twoStageAttempt;
                _twoStageAttempt = null;
                attempt?.Invalidate();
                attempt?.Dispose();
            }
        }

        private void ChangeToCreateDownloaderState(ProcedureOwner procedureOwner)
        {
            if (ModuleSystem.IsRunning)
            {
                ChangeState<ProcedureCreateDownloader>(procedureOwner);
            }
        }

        protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (!_initResourcesComplete)
            {
                // 初始化资源未完成则继续等待
                return;
            }

            if (Settings.UpdateSetting != null && Settings.UpdateSetting.EnableTwoStageUpdate)
            {
                ChangeState<ProcedureTwoStageUpdate>(procedureOwner);
                return;
            }

            if (_resourceModule.PlayMode == EPlayMode.HostPlayMode || _resourceModule.PlayMode == EPlayMode.WebPlayMode)
            {
                //线上最新版本operation.PackageVersion
                Log.Debug($"Updated package Version : from {_resourceModule.GetPackageVersion()} to {_resourceModule.PackageVersion}");
                //注意：保存资源版本号作为下次默认启动的版本!
                // 如果当前是WebGL或者是边玩边下载直接进入预加载阶段。
                if (_resourceModule.PlayMode == EPlayMode.WebPlayMode ||
                    _resourceModule.UpdatableWhilePlaying)
                {
                    // 边玩边下载还可以拓展首包支持。
                    ChangeToPreloadState(procedureOwner);
                    return;
                }

                ChangeToCreateDownloaderState(procedureOwner);
                return;
            }

            ChangeToPreloadState(procedureOwner);
        }

        private void StartTwoStageManifestAttempt()
        {
            StartupAttempt previous = _twoStageAttempt;
            _twoStageAttempt = null;
            previous?.Invalidate();
            previous?.Dispose();

            StartupAttempt attempt = new StartupAttempt(++_nextTwoStageAttemptId);
            _twoStageAttempt = attempt;
            InitializeTwoStageManifestAsync(attempt).Forget();
        }

        private async UniTaskVoid InitializeTwoStageManifestAsync(StartupAttempt attempt)
        {
            try
            {
                if (!IsCurrentTwoStageAttempt(attempt) || !TwoStageUpdateCoordinator.IsPrepared)
                {
                    throw new OperationCanceledException(attempt.Token);
                }

                string packageVersion = TwoStageUpdateCoordinator.PackageVersion;
                _resourceModule.PackageVersion = packageVersion;
                LauncherMgr.ShowUI<LoadUpdateUI>($"固定 release 清单：{packageVersion}");

                if (_resourceModule.PlayMode != EPlayMode.EditorSimulateMode)
                {
                    await TwoStageUpdateCoordinator.VerifyPinnedManifestSourcesAsync(attempt.Token);
                    if (!IsCurrentTwoStageAttempt(attempt))
                        throw new OperationCanceledException(attempt.Token);

                    UpdatePackageManifestOperation operation =
                        _resourceModule.UpdatePackageManifestAsync(packageVersion);
                    await operation.ToUniTask().AttachExternalCancellation(attempt.Token);
                    if (!IsCurrentTwoStageAttempt(attempt))
                    {
                        throw new OperationCanceledException(attempt.Token);
                    }

                    if (operation.Status != EOperationStatus.Succeed)
                    {
                        throw new InvalidOperationException(
                            $"Fixed manifest '{packageVersion}' failed: {operation.Error}");
                    }

                    string actual = _resourceModule.GetPackageVersion();
                    if (!string.Equals(actual, packageVersion, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Manifest version drift after update: expected '{packageVersion}', actual '{actual}'.");
                    }

                    if (!(_resourceModule is IPinnedManifestIntegrity manifestIntegrity))
                        throw new InvalidOperationException(
                            "Resource module cannot verify the active manifest byte identity.");

                    manifestIntegrity.VerifyPinnedManifestActivated(
                        TwoStageUpdateCoordinator.Release.PackageName,
                        packageVersion,
                        TwoStageUpdateCoordinator.Release.ManifestSha256);
                }

                if (!attempt.TrySucceed())
                {
                    return;
                }

                _initResourcesComplete = true;
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentTwoStageAttempt(attempt))
                {
                    attempt.TryCancel();
                }
            }
            catch (Exception exception)
            {
                FailTwoStageManifestAttempt(attempt, exception);
            }
        }

        private bool IsCurrentTwoStageAttempt(StartupAttempt attempt)
        {
            return ModuleSystem.IsRunning && attempt != null &&
                   ReferenceEquals(_twoStageAttempt, attempt) && attempt.IsRunning;
        }

        private void FailTwoStageManifestAttempt(StartupAttempt attempt, Exception exception)
        {
            if (!IsCurrentTwoStageAttempt(attempt) || !attempt.TryFail())
            {
                return;
            }

            string releaseId = TwoStageUpdateCoordinator.ReleaseId ?? "<unknown>";
            string userMessage = $"固定 release 清单更新失败：{releaseId}\n\n" +
                                 $"<color=#FF0000>{exception}</color>\n\n" +
                                 "尚未装载程序集，可重试同一 release 或退出。";
            Log.Error(userMessage);
            LauncherMgr.ShowMessageBox(
                userMessage,
                () =>
                {
                    if (ReferenceEquals(_twoStageAttempt, attempt) &&
                        attempt.State == StartupAttempt.TerminalState.Failed)
                    {
                        StartTwoStageManifestAttempt();
                    }
                },
                Application.Quit);
        }

        //// <summary>
        /// 初始化资源流程。
        /// <remarks>YooAsset 需要保持编辑器、单机、联机模式流程一致。</remarks>
        private IEnumerator InitResources(ProcedureOwner procedureOwner)
        {
            if (!ModuleSystem.IsRunning)
            {
                yield break;
            }

            Log.Info("更新资源清单！！！");
            LauncherMgr.ShowUI<LoadUpdateUI>($"更新清单文件...");

            // 1. 获取资源清单的版本信息
            var operation1 = _resourceModule.RequestPackageVersionAsync();
            yield return operation1;
            if (!ModuleSystem.IsRunning)
            {
                yield break;
            }

            if (operation1.Status != EOperationStatus.Succeed)
            {
                OnInitResourcesError(procedureOwner, operation1.Error);
                yield break;
            }

            var packageVersion = operation1.PackageVersion;
            _resourceModule.PackageVersion = packageVersion;

            if (Utility.PlayerPrefs.HasKey("GAME_VERSION"))
            {
                Utility.PlayerPrefs.SetString("GAME_VERSION", _resourceModule.PackageVersion);
            }

            Log.Info($"Init resource package version : {packageVersion}");

            // 2. 传入的版本信息更新资源清单
            var operation2 = _resourceModule.UpdatePackageManifestAsync(packageVersion);
            yield return operation2;
            if (!ModuleSystem.IsRunning)
            {
                yield break;
            }

            if (operation2.Status != EOperationStatus.Succeed)
            {
                OnInitResourcesError(procedureOwner, operation2.Error);
                yield break;
            }

            _initResourcesComplete = true;
        }

        private void ChangeToPreloadState(ProcedureOwner procedureOwner)
        {
            if (ModuleSystem.IsRunning)
            {
                ChangeState<ProcedurePreload>(procedureOwner);
            }
        }

        private void OnInitResourcesError(ProcedureOwner procedureOwner, string message)
        {
            if (!ModuleSystem.IsRunning)
            {
                return;
            }

            // 检查设备网络连接状态。
            if (_resourceModule.PlayMode == EPlayMode.HostPlayMode)
            {
                if (!IsNeedUpdate())
                {
                    return;
                }
                else
                {
                    Log.Error(message);
                    LauncherMgr.ShowMessageBox($"获取远程版本失败！点击确认重试\n <color=#FF0000>{message}</color>"
                    , () =>
                    {
                        if (ModuleSystem.IsRunning)
                        {
                            Utility.Unity.StartCoroutine(InitResources(procedureOwner));
                        }
                    }
                    ,Application.Quit);
                    return;
                }
            }

            Log.Error(message);
            LauncherMgr.ShowMessageBox($"初始化资源失败！点击确认重试 \n <color=#FF0000>{message}</color>"
                ,() =>
                {
                    if (ModuleSystem.IsRunning)
                    {
                        Utility.Unity.StartCoroutine(InitResources(procedureOwner));
                    }
                }, Application.Quit);
        }

        private bool IsNeedUpdate()
        {
            if (!ModuleSystem.IsRunning)
            {
                return false;
            }

            // 如果不能联网且当前游戏非强制(不更新可以进入游戏。)
            if (Settings.UpdateSetting.UpdateStyle == UpdateStyle.Optional && !_resourceModule.UpdatableWhilePlaying)
            {
                // 获取上次成功记录的版本
                string packageVersion = Utility.PlayerPrefs.GetString("GAME_VERSION", string.Empty);
                if (string.IsNullOrEmpty(packageVersion))
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Net_UnReachable);
                    LauncherMgr.ShowMessageBox("没有找到本地版本记录，需要更新资源！",
                        () =>
                        {
                            if (ModuleSystem.IsRunning)
                            {
                                Utility.Unity.StartCoroutine(InitResources(_procedureOwner));
                            }
                        },
                        Application.Quit);
                    return false;
                }

                _resourceModule.PackageVersion = packageVersion;

                if (Settings.UpdateSetting.UpdateNotice == UpdateNotice.Notice)
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Notice);
                    LauncherMgr.ShowMessageBox($"更新失败，检测到可选资源更新，推荐完成更新提升游戏体验！ \\n \\n 确定再试一次，取消进入游戏",
                        () =>
                        {
                            if (ModuleSystem.IsRunning)
                            {
                                Utility.Unity.StartCoroutine(InitResources(_procedureOwner));
                            }
                        },
                        () =>
                        {
                            if (ModuleSystem.IsRunning)
                            {
                                ChangeState<ProcedurePreload>(_procedureOwner);
                            }
                        });
                }
                else
                {
                    ChangeState<ProcedurePreload>(_procedureOwner);
                }

                return false;
            }

            return true;
        }
    }
}
