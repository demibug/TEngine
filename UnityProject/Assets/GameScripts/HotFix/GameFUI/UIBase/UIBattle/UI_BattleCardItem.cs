/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace UIBattle
{
    public partial class UI_BattleCardItem : GButton
    {
        public Controller m_cState;
        public const string URL = "ui://56fffadnmxwi4";
        public const string PkgName = "UIBattle";
        public const string ResName = "BattleCardItem";

        public static UI_BattleCardItem CreateInstance()
        {
            return (UI_BattleCardItem)UIPackage.CreateObject("UIBattle", "BattleCardItem");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_cState = GetControllerAt(0);
        }
    }
}