using UnityEngine;

namespace TEngine.FairyGUIIntegration
{
    [CreateAssetMenu(fileName = "FguiSettings", menuName = "TEngine/FairyGUI/Settings")]
    public sealed class FguiSettings : ScriptableObject
    {
        [SerializeField] private FguiPackageCatalog catalog;
        [SerializeField] private int designWidth = 750;
        [SerializeField] private int designHeight = 1334;
        [SerializeField] private FairyGUI.UIContentScaler.ScreenMatchMode screenMatchMode =
            FairyGUI.UIContentScaler.ScreenMatchMode.MatchWidthOrHeight;
        [SerializeField] private string renderLayerName = "FairyGUI";
        [SerializeField] private float stageCameraDepth = 3f;
        [SerializeField] private float loadTimeoutSeconds = 30f;
        [SerializeField] private bool respectSafeArea = true;
        [SerializeField] private bool clearUguiSelectionOnCapture = true;

        public FguiPackageCatalog Catalog => catalog;
        public int DesignWidth => designWidth;
        public int DesignHeight => designHeight;
        public FairyGUI.UIContentScaler.ScreenMatchMode ScreenMatchMode => screenMatchMode;
        public string RenderLayerName => renderLayerName;
        public float StageCameraDepth => stageCameraDepth;
        public float LoadTimeoutSeconds => Mathf.Max(1f, loadTimeoutSeconds);
        public bool RespectSafeArea => respectSafeArea;
        public bool ClearUguiSelectionOnCapture => clearUguiSelectionOnCapture;
    }
}
