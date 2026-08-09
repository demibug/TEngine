using GameCommon.Battle;
using UIBattle;

namespace GameBattle
{
    /// <summary>
    /// 战斗开始组件，只消费不可变装载信息并更新显示。
    /// </summary>
    internal sealed class BattleStartWidget : UI_BattleStartWidget
    {
        /// <summary>
        /// 根据本局装载信息刷新标题，不回写战斗规则状态。
        /// </summary>
        internal void Refresh(BattleLoadoutDto loadout)
        {
            if (m_title != null)
            {
                m_title.text = $"战斗地图 {loadout.MapId + 1}";
            }
        }

        /// <summary>
        /// 显示战斗进入中的状态。
        /// </summary>
        internal void ShowStarting()
        {
            if (m_title != null)
            {
                m_title.text = "正在进入战斗...";
            }
        }

        /// <summary>
        /// 显示可重试的进入失败提示。
        /// </summary>
        internal void ShowStartFailed()
        {
            if (m_title != null)
            {
                m_title.text = "进入失败，请重试";
            }
        }
    }
}
