/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace UICommon
{
    public partial class UI_BtnNormal : GButton
    {
        public GImage m_bg;
        public const string URL = "ui://5w9iycrvtdew1";
        public const string PkgName = "UICommon";
        public const string ResName = "BtnNormal";

        public static UI_BtnNormal CreateInstance()
        {
            return (UI_BtnNormal)UIPackage.CreateObject("UICommon", "BtnNormal");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_bg = (GImage)GetChildAt(0);
        }
    }
}