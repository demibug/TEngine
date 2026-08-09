/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace UIBattle
{
    public partial class UI_BattleHudPanel : GameFUI.FUIWindow
    {
        public GButton m_btnExit;
        public GButton m_btnRefresh;
        public const string URL = "ui://56fffadnmxwi3";
        public const string PkgName = "UIBattle";
        public const string ResName = "BattleHudPanel";

        public static UI_BattleHudPanel CreateInstance()
        {
            return (UI_BattleHudPanel)UIPackage.CreateObject("UIBattle", "BattleHudPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_btnExit = (GButton)GetChildAt(0);
            m_btnRefresh = (GButton)GetChildAt(1);
        }
    }
}