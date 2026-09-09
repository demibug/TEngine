using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Launcher;
using TEngine;

namespace Procedure
{
    /// <summary>
    /// 流程 => 游戏启动。
    /// <remarks>Yield 后仍验证当前流程代际，避免旧流程隐藏新流程的启动 UI。</remarks>
    /// </summary>
    public class ProcedureStartGame : ProcedureBase
    {
        public override bool UseNativeDialog { get; }

        private StartupAttempt _currentAttempt;
        private int _nextAttemptId;

        protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            _currentAttempt?.Invalidate();
            _currentAttempt?.Dispose();
            _currentAttempt = new StartupAttempt(++_nextAttemptId);
            HideLauncherAfterYield(_currentAttempt).Forget();
        }

        protected override void OnLeave(IFsm<IProcedureModule> procedureOwner, bool isShutdown)
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

        private async UniTaskVoid HideLauncherAfterYield(StartupAttempt attempt)
        {
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, attempt.Token);
                if (!ModuleSystem.IsRunning || !ReferenceEquals(_currentAttempt, attempt) || !attempt.IsRunning)
                {
                    return;
                }

                LauncherMgr.HideAllUI();
                attempt.TrySucceed();
            }
            catch (OperationCanceledException)
            {
                // 离开流程后的旧 Yield 不得影响新的启动 UI。
            }
        }
    }
}
