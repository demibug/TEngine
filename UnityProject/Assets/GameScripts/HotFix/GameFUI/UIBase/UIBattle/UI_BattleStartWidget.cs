/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace UIBattle
{
    public partial class UI_BattleStartWidget : GameFUI.FUIWidget
    {
        public GTextField m_title;
        public const string URL = "ui://56fffadntdew2";
        public const string PkgName = "UIBattle";
        public const string ResName = "BattleStartWidget";

        public static UI_BattleStartWidget CreateInstance()
        {
            return (UI_BattleStartWidget)UIPackage.CreateObject("UIBattle", "BattleStartWidget");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_title = (GTextField)GetChildAt(0);
        }
    }
}