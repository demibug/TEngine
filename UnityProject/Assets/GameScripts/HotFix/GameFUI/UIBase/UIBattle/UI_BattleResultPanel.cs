/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace UIBattle
{
    public partial class UI_BattleResultPanel : GameFUI.FUIWindow
    {
        public Controller m_cResult;
        public GButton m_btnRestart;
        public GButton m_btnExit;
        public const string URL = "ui://56fffadnho215";
        public const string PkgName = "UIBattle";
        public const string ResName = "BattleResultPanel";

        public static UI_BattleResultPanel CreateInstance()
        {
            return (UI_BattleResultPanel)UIPackage.CreateObject("UIBattle", "BattleResultPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_cResult = GetControllerAt(0);
            m_btnRestart = (GButton)GetChildAt(0);
            m_btnExit = (GButton)GetChildAt(1);
        }
    }
}