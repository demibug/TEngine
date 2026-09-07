using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.UI.FGUI.Gen;

namespace GameLogic.UI.FGUI.Imp
{
    public sealed class ModalWaitingFguiWindow : FguiWindow
    {
        public UI_ModalWaitingMain TypedView => (UI_ModalWaitingMain)View;

        protected override UniTask OnCreateAsync(object userData, CancellationToken cancellationToken)
        {
            View.Center();
            return UniTask.CompletedTask;
        }
    }
}
