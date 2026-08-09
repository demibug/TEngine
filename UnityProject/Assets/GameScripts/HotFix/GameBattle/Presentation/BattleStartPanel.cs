using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFUI;
using GameCommon.Battle;
using TEngine;
using UIBattle;

namespace GameBattle
{
    /// <summary>
    /// 战斗业务窗口，只读取跨程序集不可变 DTO 刷新表现。
    /// </summary>
    internal sealed class BattleStartPanel : UI_BattleStartPanel
    {
        private BattleStartEntryArgs _entryArgs;
        private bool _isStarting;

        /// <inheritdoc />
        protected override void RegisterOpenEvents()
        {
            if (m_btn != null)
            {
                m_btn.onClick.Add(OnStartClicked);
            }
        }

        /// <inheritdoc />
        protected override void ClearOpenEvents()
        {
            if (m_btn != null)
            {
                m_btn.onClick.Remove(OnStartClicked);
            }

            _entryArgs = null;
            _isStarting = false;
        }

        /// <inheritdoc />
        protected override void OnRefresh()
        {
            _entryArgs = UserData as BattleStartEntryArgs;
            _isStarting = false;

            if (_entryArgs == null)
            {
                Log.Error("[BattleStartPanel] 入口参数缺失，无法开始战斗。");
                SetState("Error", false);
                return;
            }

            if (m_widgetStart is BattleStartWidget widget)
            {
                widget.Refresh(_entryArgs.Loadout);
            }

            SetState("Ready", true);
        }

        /// <summary>
        /// 接收 FairyGUI 开始按钮点击；异步任务自行处理全部预期失败和取消。
        /// </summary>
        private void OnStartClicked()
        {
            if (_isStarting || _entryArgs == null)
            {
                return;
            }

            StartBattleAsync().Forget();
        }

        /// <summary>
        /// 将本次点击转发给入口参数中的开始命令。
        /// </summary>
        private async UniTaskVoid StartBattleAsync()
        {
            BattleStartEntryArgs entryArgs = _entryArgs;
            if (entryArgs == null || _isStarting)
            {
                return;
            }

            _isStarting = true;
            SetState("Starting", false);
            if (m_widgetStart is BattleStartWidget widget)
            {
                widget.ShowStarting();
            }

            try
            {
                BattleOperationResult result = await entryArgs.StartAsync(
                    entryArgs.Loadout,
                    OpenCancellationToken);

                if (OpenCancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (result.IsSuccess)
                {
                    return;
                }

                ShowStartFailed(result.DiagnosticMessage);
            }
            catch (OperationCanceledException) when (OpenCancellationToken.IsCancellationRequested)
            {
                // 窗口关闭会取消本轮开始请求；BattleModule 会完成既有回滚。
            }
            catch (OperationCanceledException)
            {
                ShowStartFailed("请求取消");
            }
            catch (Exception ex)
            {
                Log.Error($"[BattleStartPanel] 请求开始战斗发生异常: {ex}");
                ShowStartFailed("请求异常");
            }
        }

        /// <summary>
        /// 恢复可重试状态并记录失败原因。
        /// </summary>
        private void ShowStartFailed(string reason)
        {
            if (OpenCancellationToken.IsCancellationRequested)
            {
                return;
            }

            _isStarting = false;
            Log.Warning($"[BattleStartPanel] 开始战斗失败: {reason}");
            SetState("Error", true);
            if (m_widgetStart is BattleStartWidget widget)
            {
                widget.ShowStartFailed();
            }
        }

        /// <summary>
        /// 同步控制器页面和按钮交互状态。
        /// </summary>
        private void SetState(string pageName, bool canStart)
        {
            if (m_cState != null)
            {
                m_cState.selectedPage = pageName;
            }

            if (m_btn != null)
            {
                m_btn.touchable = canStart;
                m_btn.grayed = !canStart;
            }
        }
    }

    /// <summary>
    /// 战斗入口窗口的单次打开参数。
    /// </summary>
    /// <remarks>
    /// 开始操作是带取消和结果的命令，不使用 GameEvent 表达。
    /// 委托仅由当前窗口实例持有，窗口关闭后随打开周期清理，避免全局静态订阅。
    /// </remarks>
    internal sealed class BattleStartEntryArgs
    {
        /// <summary>
        /// 本次入口对应的不可变装载信息。
        /// </summary>
        internal BattleLoadoutDto Loadout { get; }

        /// <summary>
        /// 请求开始战斗的异步命令。
        /// </summary>
        internal Func<BattleLoadoutDto, CancellationToken, UniTask<BattleOperationResult>> StartAsync { get; }

        internal BattleStartEntryArgs(
            BattleLoadoutDto loadout,
            Func<BattleLoadoutDto, CancellationToken, UniTask<BattleOperationResult>> startAsync)
        {
            Loadout = loadout;
            StartAsync = startAsync ?? throw new ArgumentNullException(nameof(startAsync));
        }
    }
}
