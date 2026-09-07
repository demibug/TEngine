using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.UI.FGUI.Gen;
using TEngine.FairyGUIIntegration;

namespace GameLogic.UI.FGUI.Imp
{
    public static class FguiSampleRegistration
    {
        public const string SettingsAddress = "FGUI/FguiSettings.asset";

        public static async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            await GameModule.FGUI.InitializeAsync(SettingsAddress, cancellationToken);

            FguiGeneratedBinder.BindAll();
            if (!GameModule.FGUI.IsRegistered<BundleUsageFguiWindow>())
            {
                GameModule.FGUI.Register<BundleUsageFguiWindow>(FguiWindowDescriptor.Create(
                    () => new BundleUsageFguiWindow(), "BundleUsage", "BundleUsage", "Main", FguiLayer.UI));
            }
            if (!GameModule.FGUI.IsRegistered<ModalWaitingFguiWindow>())
            {
                GameModule.FGUI.Register<ModalWaitingFguiWindow>(FguiWindowDescriptor.Create(
                    () => new ModalWaitingFguiWindow(), "ModalWaiting", "ModalWaiting", "Main", FguiLayer.Top,
                    modal: true));
            }
        }

        public static async UniTask<BundleUsageFguiWindow> ShowCoexistenceSampleAsync(
            CancellationToken cancellationToken = default)
        {
            await InitializeAsync(cancellationToken);
            return await GameModule.FGUI.ShowAsync<BundleUsageFguiWindow>("sample-icon", cancellationToken);
        }
    }
}
