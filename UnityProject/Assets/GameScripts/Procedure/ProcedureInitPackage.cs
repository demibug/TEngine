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
    /// <summary>
    /// 流程 => 初始化 Package。
    /// <remarks>
    /// 资源模块 bootstrap 与 YooAsset 包初始化分开等待；每次进入流程建立独立 attempt，
    /// 离开后的旧等待只能收尾，不能切换流程或再次显示失败 UI。
    /// </remarks>
    /// </summary>
    public class ProcedureInitPackage : ProcedureBase
    {
        private const float DefaultInitializationTimeoutSeconds = 60f;

        public override bool UseNativeDialog { get; }

        private ProcedureOwner _procedureOwner;
        private StartupAttempt _currentAttempt;
        private int _nextAttemptId;

        /// <summary>
        /// 启动阶段超时（非缩放时间）。保留可注入属性以便生命周期测试缩短等待。
        /// </summary>
        public float InitializationTimeoutSeconds { get; set; } = DefaultInitializationTimeoutSeconds;

        protected override void OnEnter(ProcedureOwner procedureOwner)
        {
            base.OnEnter(procedureOwner);
            _procedureOwner = procedureOwner;
            StartAttempt();
        }

        protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
        {
            try
            {
                base.OnLeave(procedureOwner, isShutdown);
            }
            finally
            {
                StartupAttempt attempt = _currentAttempt;
                _currentAttempt = null;
                attempt?.Invalidate();
                attempt?.Dispose();
            }
        }

        private void StartAttempt()
        {
            StartupAttempt previousAttempt = _currentAttempt;
            _currentAttempt = null;
            previousAttempt?.Invalidate();
            previousAttempt?.Dispose();

            StartupAttempt attempt = new StartupAttempt(++_nextAttemptId);
            _currentAttempt = attempt;
            InitializePackage(attempt).Forget();
        }

        private async UniTaskVoid InitializePackage(StartupAttempt attempt)
        {
            using (CancellationTokenSource timeoutCancellation =
                   CancellationTokenSource.CreateLinkedTokenSource(attempt.Token))
            {
                try
                {
                    UniTask packageTask = InitializePackageCore(attempt, timeoutCancellation.Token);
                    await packageTask.Timeout(
                        TimeSpan.FromSeconds(Math.Max(0.01f, InitializationTimeoutSeconds)),
                        DelayType.UnscaledDeltaTime,
                        PlayerLoopTiming.Update,
                        timeoutCancellation);
                }
                catch (TimeoutException exception)
                {
                    FailAttempt(attempt, "资源包初始化阶段超时", exception);
                }
                catch (OperationCanceledException)
                {
                    // 流程离开或 attempt 失效属于正常取消，不弹出新的错误 UI。
                    if (IsCurrentRunning(attempt))
                    {
                        attempt.TryCancel();
                    }
                }
                catch (Exception exception)
                {
                    FailAttempt(attempt, "资源包初始化阶段", exception);
                }
            }
        }

        private async UniTask InitializePackageCore(StartupAttempt attempt, CancellationToken cancellationToken)
        {
            if (_resourceModule == null)
            {
                throw new InvalidOperationException("Resource module is missing.");
            }

            // 只取消当前等待者；ResourceModule 内部的 bootstrap completion source 仍由 Driver 共享。
            await _resourceModule.WaitUntilInitializedAsync(cancellationToken);
            EnsureCurrentRunning(attempt);

            UniTask<InitializationOperation> packageTask =
                _resourceModule.InitPackage(_resourceModule.DefaultPackageName);
            InitializationOperation initializationOperation =
                await packageTask.AttachExternalCancellation(cancellationToken);

            EnsureCurrentRunning(attempt);
            if (initializationOperation == null)
            {
                throw new InvalidOperationException(
                    $"Resource package '{_resourceModule.DefaultPackageName}' returned a null initialization operation.");
            }

            if (initializationOperation.Status != EOperationStatus.Succeed)
            {
                throw new GameFrameworkException(
                    $"Resource package '{_resourceModule.DefaultPackageName}' failed: {initializationOperation.Error}");
            }

            // 资源包已经成功且当前 attempt 仍有效后，才进入下一资源流程。
            LoadText.Instance.InitConfigData(null);
            EnsureCurrentRunning(attempt);

            EPlayMode playMode = _resourceModule.PlayMode;
            if (playMode != EPlayMode.EditorSimulateMode &&
                playMode != EPlayMode.OfflinePlayMode &&
                playMode != EPlayMode.HostPlayMode &&
                playMode != EPlayMode.WebPlayMode)
            {
                throw new InvalidOperationException($"Unknown resource play mode: {playMode}.");
            }

            if (playMode == EPlayMode.HostPlayMode || playMode == EPlayMode.WebPlayMode)
            {
                LauncherMgr.ShowUI<LoadUpdateUI>();
            }

            if (!attempt.TrySucceed())
            {
                return;
            }

            ChangeState<ProcedureInitResources>(_procedureOwner);
        }

        private bool IsCurrentRunning(StartupAttempt attempt)
        {
            return ModuleSystem.IsRunning && ReferenceEquals(_currentAttempt, attempt) && attempt != null && attempt.IsRunning;
        }

        private void EnsureCurrentRunning(StartupAttempt attempt)
        {
            if (!IsCurrentRunning(attempt))
            {
                throw new OperationCanceledException(attempt?.Token ?? default(CancellationToken));
            }
        }

        private void FailAttempt(StartupAttempt attempt, string stage, Exception exception)
        {
            if (!IsCurrentRunning(attempt) || !attempt.TryFail())
            {
                return;
            }

            string message = exception?.ToString() ?? "Unknown error.";
            if (message.Contains("PackageManifest_DefaultPackage.version Error : HTTP/1.1 404 Not Found", StringComparison.Ordinal))
            {
                message = "请检查 StreamingAssets/package/DefaultPackage/PackageManifest_DefaultPackage.version 是否存在。";
            }

            string packageName = _resourceModule?.DefaultPackageName ?? "<unknown>";
            string userMessage = $"资源初始化失败！阶段：{stage}；包：{packageName}\n\n" +
                                 $"<color=#FF0000>原因：{message}</color>";
            Log.Error($"{userMessage}");
            LauncherMgr.ShowUI<LoadUpdateUI>("资源初始化失败！");
            LauncherMgr.ShowMessageBox(userMessage, () => Retry(attempt), Application.Quit);
        }

        private void Retry(StartupAttempt failedAttempt)
        {
            // 旧弹窗重复点击、旧代际晚回调和离开后的按钮都必须失效；只有当前失败终态允许一次重试。
            if (!ReferenceEquals(_currentAttempt, failedAttempt) ||
                failedAttempt == null || failedAttempt.State != StartupAttempt.TerminalState.Failed)
            {
                return;
            }

            LauncherMgr.ShowUI<LoadUpdateUI>("重新初始化资源中...");
            StartAttempt();
        }
    }
}
