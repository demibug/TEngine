/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace UIBattle
{
    public partial class UI_BattleStartPanel : GameFUI.FUIWindow
    {
        public Controller m_cState;
        public GButton m_btn;
        public UI_BattleStartWidget m_widgetStart;
        public const string URL = "ui://56fffadntdew0";
        public const string PkgName = "UIBattle";
        public const string ResName = "BattleStartPanel";

        public static UI_BattleStartPanel CreateInstance()
        {
            return (UI_BattleStartPanel)UIPackage.CreateObject("UIBattle", "BattleStartPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_cState = GetControllerAt(0);
            m_btn = (GButton)GetChildAt(0);
            m_widgetStart = (UI_BattleStartWidget)GetChildAt(1);
        }
    }
}