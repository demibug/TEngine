/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace UIBattle
{
    public partial class UI_BattleStartPanel : GComponent
    {
        public GButton m_btn;
        public const string URL = "ui://56fffadntdew0";

        public static UI_BattleStartPanel CreateInstance()
        {
            return (UI_BattleStartPanel)UIPackage.CreateObject("UIBattle", "BattleStartPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_btn = (GButton)GetChildAt(0);
        }
    }
}