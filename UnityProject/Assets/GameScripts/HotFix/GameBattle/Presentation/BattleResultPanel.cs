using System;
using Cysharp.Threading.Tasks;
using GameCommon.Battle;
using GameFUI;
using TEngine;
using UIBattle;

namespace GameBattle
{
    /// <summary>
    /// 战斗结算业务窗口。
    /// </summary>
    internal sealed class BattleResultPanel : UI_BattleResultPanel
    {
        private BattleResultEntryArgs _entryArgs;
        private bool _isConfirming;

        /// <inheritdoc />
        protected override void RegisterOpenEvents()
        {
            if (m_btnExit != null)
            {
                m_btnExit.onClick.Add(OnExitClicked);
            }
        }

        /// <inheritdoc />
        protected override void ClearOpenEvents()
        {
            if (m_btnExit != null)
            {
                m_btnExit.onClick.Remove(OnExitClicked);
            }

            _entryArgs = null;
            _isConfirming = false;
        }

        /// <inheritdoc />
        protected override void OnRefresh()
        {
            _entryArgs = UserData as BattleResultEntryArgs;
            _isConfirming = false;

            if (_entryArgs == null)
            {
                Log.Error("[BattleResultPanel] 结算参数缺失，无法退出当前战斗。");
                return;
            }

            if (m_cResult != null)
            {
                m_cResult.selectedPage = _entryArgs.Result.IsWin ? "Win" : "Lose";
            }

            // 本期仅提供“确定”退出语义，不实现 Restart。
            if (m_btnRestart != null)
            {
                m_btnRestart.visible = false;
                m_btnRestart.touchable = false;
            }

            if (m_btnExit != null)
            {
                m_btnExit.title = "确定";
                m_btnExit.touchable = true;
                m_btnExit.grayed = false;
            }
        }

        private void OnExitClicked()
        {
            if (_isConfirming || _entryArgs == null)
            {
                return;
            }

            ExitBattleAsync().Forget();
        }

        private async UniTaskVoid ExitBattleAsync()
        {
            BattleResultEntryArgs entryArgs = _entryArgs;
            if (entryArgs == null || _isConfirming)
            {
                return;
            }

            _isConfirming = true;
            SetConfirmEnabled(false);

            try
            {
                BattleOperationResult result = await entryArgs.ExitAsync();
                if (!ReferenceEquals(_entryArgs, entryArgs))
                {
                    return;
                }

                if (result.IsSuccess)
                {
                    // 成功退出：HUD 由退出 handler 关闭，结算面板自行关闭，
                    // 避免残留在开始界面。
                    FUI.Close<BattleResultPanel>();
                    return;
                }

                Log.Warning($"[BattleResultPanel] 退出战斗失败: {result}");
                RestoreConfirmState(entryArgs);
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(_entryArgs, entryArgs))
                {
                    return;
                }

                Log.Error($"[BattleResultPanel] 退出战斗发生异常: {ex}");
                RestoreConfirmState(entryArgs);
            }
        }

        private void SetConfirmEnabled(bool enabled)
        {
            if (m_btnExit == null)
            {
                return;
            }

            m_btnExit.touchable = enabled;
            m_btnExit.grayed = !enabled;
        }

        private void RestoreConfirmState(BattleResultEntryArgs entryArgs)
        {
            if (!ReferenceEquals(_entryArgs, entryArgs))
            {
                return;
            }

            _isConfirming = false;
            SetConfirmEnabled(true);
        }
    }

    /// <summary>
    /// 战斗结算窗口的单次打开参数。
    /// </summary>
    internal sealed class BattleResultEntryArgs
    {
        /// <summary>已冻结的不可变战斗结果 DTO。</summary>
        internal BattleResultDto Result { get; }

        /// <summary>退出当前战斗的异步命令。</summary>
        internal Func<UniTask<BattleOperationResult>> ExitAsync { get; }

        internal BattleResultEntryArgs(
            BattleResultDto result,
            Func<UniTask<BattleOperationResult>> exitAsync)
        {
            Result = result;
            ExitAsync = exitAsync ?? throw new ArgumentNullException(nameof(exitAsync));
        }
    }
}
