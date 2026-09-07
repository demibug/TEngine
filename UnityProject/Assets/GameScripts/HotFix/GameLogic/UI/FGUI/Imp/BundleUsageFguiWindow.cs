using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameLogic.UI.FGUI.Gen;

namespace GameLogic.UI.FGUI.Imp
{
    public sealed class BundleUsageFguiWindow : FguiWindow
    {
        private GLoader _externalIcon;

        public UI_BundleUsageMain TypedView => (UI_BundleUsageMain)View;

        protected override UniTask OnCreateAsync(object userData, CancellationToken cancellationToken)
        {
            View.SetSize(View.parent.width, View.parent.height);
            View.AddRelation(View.parent, RelationType.Size);
            _externalIcon = new GLoader
            {
                name = "sampleExternalIcon",
                fill = FillType.Scale,
                align = AlignType.Center,
                verticalAlign = VertAlignType.Middle
            };
            _externalIcon.SetSize(96, 96);
            _externalIcon.SetXY(24, 24);
            View.AddChild(_externalIcon);
            SetExternalIcon(userData as string ?? "sample-icon");
            return UniTask.CompletedTask;
        }

        public void SetExternalIcon(string catalogKey)
        {
            if (_externalIcon != null)
                _externalIcon.url = "asset://" + catalogKey;
        }

        protected override void OnDestroy()
        {
            _externalIcon = null;
        }
    }
}
