using System;
using FairyGUI;
using GameFUI;
using UIBattle;

namespace GameBattle
{
    /// <summary>
    /// UIBattle Package 的唯一业务 owner。
    /// </summary>
    internal static class BattleFuiOwner
    {
        /// <summary>
        /// 按生成 Binder、最终 Widget、最终 Window 的固定顺序注册。
        /// </summary>
        internal static void Register(FUIBindingRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            UIBattleBinder.BindAll();

            registry.Register(new FUIDescriptor(
                url: UI_BattleStartWidget.URL,
                packageName: UI_BattleStartWidget.PkgName,
                componentName: UI_BattleStartWidget.ResName,
                ownerType: typeof(BattleModule),
                targetType: typeof(BattleStartWidget),
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: CreateWidget));

            registry.Register(new FUIDescriptor(
                url: UI_BattleStartPanel.URL,
                packageName: UI_BattleStartPanel.PkgName,
                componentName: UI_BattleStartPanel.ResName,
                ownerType: typeof(BattleModule),
                targetType: typeof(BattleStartPanel),
                layer: FUILayer.Normal,
                fullScreen: true,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Safe,
                creator: CreateWindow));

            registry.Register(new FUIDescriptor(
                url: UI_BattleHudPanel.URL,
                packageName: UI_BattleHudPanel.PkgName,
                componentName: UI_BattleHudPanel.ResName,
                ownerType: typeof(BattleModule),
                targetType: typeof(BattleHudPanel),
                layer: FUILayer.Normal,
                fullScreen: true,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Safe,
                creator: CreateHudWindow));

            registry.Register(new FUIDescriptor(
                url: UI_BattleResultPanel.URL,
                packageName: UI_BattleResultPanel.PkgName,
                componentName: UI_BattleResultPanel.ResName,
                ownerType: typeof(BattleModule),
                targetType: typeof(BattleResultPanel),
                layer: FUILayer.Popup,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Safe,
                creator: CreateResultWindow));
        }

        private static GComponent CreateWidget(string url)
        {
            return new BattleStartWidget();
        }

        private static GComponent CreateWindow(string url)
        {
            return new BattleStartPanel();
        }

        private static GComponent CreateHudWindow(string url)
        {
            return new BattleHudPanel();
        }

        private static GComponent CreateResultWindow(string url)
        {
            return new BattleResultPanel();
        }
    }
}
