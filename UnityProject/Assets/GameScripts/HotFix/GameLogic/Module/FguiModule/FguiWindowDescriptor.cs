using System;
using TEngine.FairyGUIIntegration;

namespace GameLogic
{
    public sealed class FguiWindowDescriptor
    {
        public Type WindowType { get; }
        public Func<FguiWindow> Factory { get; }
        public string PackageKey { get; }
        public string PackageName { get; }
        public string ComponentName { get; }
        public FguiLayer Layer { get; }
        public bool Modal { get; }
        public Action BindGeneratedTypes { get; }

        public FguiWindowDescriptor(Type windowType, Func<FguiWindow> factory, string packageKey,
            string packageName, string componentName, FguiLayer layer = FguiLayer.UI, bool modal = false,
            Action bindGeneratedTypes = null)
        {
            WindowType = windowType ?? throw new ArgumentNullException(nameof(windowType));
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            PackageKey = packageKey ?? throw new ArgumentNullException(nameof(packageKey));
            PackageName = packageName ?? throw new ArgumentNullException(nameof(packageName));
            ComponentName = componentName ?? throw new ArgumentNullException(nameof(componentName));
            Layer = layer;
            Modal = modal;
            BindGeneratedTypes = bindGeneratedTypes;
        }

        public static FguiWindowDescriptor Create<TWindow>(Func<TWindow> factory, string packageKey,
            string packageName, string componentName, FguiLayer layer = FguiLayer.UI, bool modal = false,
            Action bindGeneratedTypes = null) where TWindow : FguiWindow
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            return new FguiWindowDescriptor(typeof(TWindow), () => factory(), packageKey, packageName,
                componentName, layer, modal, bindGeneratedTypes);
        }
    }
}
