using System.Reflection;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI.Tests.EditMode;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameFUI.Tests.PlayMode
{
    /// <summary>
    /// GameFUI 屏幕适配的生产装配回归测试。
    /// </summary>
    [TestFixture]
    public sealed class ScreenAdaptationTests
    {
        private ScreenStateScope _screen;

        [SetUp]
        public void SetUp()
        {
            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();
            FUI.ClearModuleForShutdown();
            FUIObjectFactoryIntegration.ClearActiveRegistry();

            _screen = new ScreenStateScope();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                FUI.Module.Shutdown();
            }
            catch (FUIException)
            {
                // 模块未注册或已关闭时无需重复处理。
            }

            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();
            FUI.ClearModuleForShutdown();
            FUIObjectFactoryIntegration.ClearActiveRegistry();

            _screen?.Dispose();
            _screen = null;
        }

        /// <summary>
        /// 生产默认装配必须在创建层容器前把 720×1280 设计窗口缩放到竖屏视口内。
        /// 当前缺少设计分辨率配置时，缩放因子保持 1，以下用例会以窗口右侧或底部越界复现裁切。
        /// </summary>
        [TestCase(375, 667)]
        [TestCase(390, 844)]
        [TestCase(720, 1280)]
        [TestCase(768, 1024)]
        [TestCase(1080, 2400)]
        public void FreezeBindings_DefaultProductionOptions_FitsDesignWindowInPortraitViewport(
            int screenWidth,
            int screenHeight)
        {
            _screen.SetViewport(screenWidth, screenHeight);

            FUI.RegisterModuleForTesting(new InMemoryFUIResourceProvider());
            FUIModule module = (FUIModule)FUI.Module;
            module.FreezeBindings();

            float expectedScale = System.Math.Min(
                (float)screenWidth / 720f,
                (float)screenHeight / 1280f);

            Assert.AreEqual(expectedScale, GRoot.contentScaleFactor, 0.0001f,
                "FreezeBindings 应应用 720×1280 设计分辨率；保持 scaleFactor=1 会裁切设计窗口。");
            Assert.GreaterOrEqual(GRoot.inst.width + 0.01f, 720f,
                "缩放后的 GRoot 逻辑宽度应容纳 720 设计宽度。");
            Assert.GreaterOrEqual(GRoot.inst.height + 0.01f, 1280f,
                "缩放后的 GRoot 逻辑高度应容纳 1280 设计高度。");

            UIContentScaler scaler = Stage.inst.gameObject.GetComponent<UIContentScaler>();
            Assert.AreEqual(UIContentScaler.ScaleMode.ScaleWithScreenSize, scaler.scaleMode,
                "FreezeBindings 应启用按屏幕尺寸缩放模式。");
            Assert.AreEqual(720, scaler.designResolutionX, "scaler 应在显示树创建前收到设计宽度。");
            Assert.AreEqual(1280, scaler.designResolutionY, "scaler 应在显示树创建前收到设计高度。");
            Assert.AreEqual(UIContentScaler.ScreenMatchMode.MatchWidthOrHeight, scaler.screenMatchMode,
                "scaler 应使用默认的 MatchWidthOrHeight。");

            GComponent firstLayer = module.LayerContainer.GetLayer(FUILayer.Background);
            Assert.AreEqual(GRoot.inst.width, firstLayer.width, 0.01f,
                "首个层容器创建时应已经使用缩放后的 GRoot 宽度。");
            Assert.AreEqual(GRoot.inst.height, firstLayer.height, 0.01f,
                "首个层容器创建时应已经使用缩放后的 GRoot 高度。");
            Assert.AreEqual(0, module._windowEntries.Count,
                "FreezeBindings 只建立已缩放的层容器，不应提前创建受管理窗口。");
        }

        /// <summary>
        /// 非正设计宽高必须在创建任何 GameFUI 显示树前失败，并报告具体配置字段。
        /// </summary>
        [TestCase("DesignWidth", 0)]
        [TestCase("DesignWidth", -1)]
        [TestCase("DesignHeight", 0)]
        [TestCase("DesignHeight", -1)]
        public void FreezeBindings_InvalidDesignSize_FailsBeforeCreatingDisplayTree(
            string fieldName,
            int invalidValue)
        {
            FUIOptions options = new FUIOptions();
            PropertyInfo property = typeof(FUIOptions).GetProperty(fieldName);
            Assert.IsNotNull(property, $"FUIOptions 应声明 {fieldName} 配置字段。");
            property.SetValue(options, invalidValue);

            FUI.RegisterModuleForTesting(new InMemoryFUIResourceProvider(), options);
            FUIModule module = (FUIModule)FUI.Module;

            FUIException exception = Assert.Throws<FUIException>(() => module.FreezeBindings());

            StringAssert.Contains(fieldName, exception.Message,
                "非法设计尺寸的错误应包含具体配置字段，便于定位组合根配置。");
            Assert.IsNull(module.LayerContainer,
                "设计尺寸校验失败前不得创建 Full/Safe 层容器。");
            Assert.AreEqual(0, module._windowEntries.Count,
                "设计尺寸校验失败前不得创建受管理窗口条目。");
        }

        /// <summary>
        /// Full/Safe 全屏窗口在首次打开、显隐、缓存重开和容器变化后始终跟随目标容器，
        /// 最终释放时不留下显示对象或尺寸关系回调。
        /// </summary>
        [TestCase(FUISafeAreaMode.Full)]
        [TestCase(FUISafeAreaMode.Safe)]
        public async UniTask FullScreenWindow_FollowsTargetContainerAcrossLifecycle(
            FUISafeAreaMode safeAreaMode)
        {
            _screen.SetViewport(375, 667);
            FUIModule module = PlayModeTestHarness.SetupForShowAsync(
                options: new FUIOptions(),
                fullScreen: true,
                cacheMode: FUICacheMode.Cache,
                safeAreaMode: safeAreaMode);

            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();
            GComponent container = module.LayerContainer.GetSubContainer(
                FUILayer.Normal,
                safeAreaMode);

            AssertFullScreenLayout(window, container, "首次打开");

            module.Hide<TestBattleStartPanel>();
            TestBattleStartPanel shownWindow = await module.ShowAsync<TestBattleStartPanel>();
            Assert.AreSame(window, shownWindow, "Hide/Show 应复用同一打开域窗口实例。");
            AssertFullScreenLayout(window, container, "Hide/Show");

            container.SetSize(container.width + 37f, container.height + 53f);
            AssertFullScreenLayout(window, container, "容器尺寸变化");

            module.Close<TestBattleStartPanel>();
            Assert.IsNull(window.parent, "缓存关闭后窗口应移出显示树。");
            Assert.IsFalse(window.isDisposed, "缓存关闭不应释放窗口实例。");

            container.SetSize(container.width + 19f, container.height + 29f);
            TestBattleStartPanel reopenedWindow = await module.ShowAsync<TestBattleStartPanel>();
            Assert.AreSame(window, reopenedWindow, "Cache 重开应复用同一窗口实例。");
            AssertFullScreenLayout(window, container, "Cache 重开");

            module.Close<TestBattleStartPanel>();
            module.Close<TestBattleStartPanel>();
            Assert.IsTrue(window.isDisposed, "Cached 窗口再次 Close 后应最终释放。");
            Assert.IsNull(window.parent, "最终释放后不得残留显示对象。");
            Assert.IsTrue(window.relations.isEmpty, "最终释放后不得残留容器尺寸关系回调。");
        }

        /// <summary>
        /// 非全屏 Popup 必须保留 FairyGUI 资源声明的 720×1280 尺寸和根位置，
        /// 不得被框架添加全屏 Size Relation。
        /// </summary>
        [Test]
        public async UniTask NonFullScreenPopup_PreservesResourceLayoutWithoutSizeRelation()
        {
            _screen.SetViewport(390, 844);
            FUIModule module = PlayModeTestHarness.SetupForShowAsync(
                options: new FUIOptions(),
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                layer: FUILayer.Popup);

            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();
            GComponent container = module.LayerContainer.GetSubContainer(
                FUILayer.Popup,
                FUISafeAreaMode.Full);

            Assert.AreSame(container, window.parent, "非全屏窗口应挂载到描述指定的 Popup/Full 子容器。");
            Assert.AreEqual(720f, window.width, 0.01f, "非全屏窗口应保留资源设计宽度。");
            Assert.AreEqual(1280f, window.height, 0.01f, "非全屏窗口应保留资源设计高度。");
            Assert.AreEqual(0f, window.x, 0.01f, "非全屏窗口应保留资源根 X 位置。");
            Assert.AreEqual(0f, window.y, 0.01f, "非全屏窗口应保留资源根 Y 位置。");
            Assert.IsFalse(window.relations.Contains(container),
                "非全屏窗口不得获得针对层容器的全屏 Size Relation。");

            container.SetSize(container.width + 41f, container.height + 67f);
            Assert.AreEqual(720f, window.width, 0.01f, "容器变化不得拉伸非全屏窗口宽度。");
            Assert.AreEqual(1280f, window.height, 0.01f, "容器变化不得拉伸非全屏窗口高度。");
        }

        /// <summary>
        /// 平台左下角像素安全区必须转换为 FairyGUI 左上角逻辑坐标；空安全区回退完整根区域。
        /// </summary>
        [Test]
        public void SafeAreaInput_ConvertsPixelsAndFallsBackForEmptyRect()
        {
            _screen.SetViewport(375, 667);
            GRoot.inst.SetContentScaleFactor(
                720,
                1280,
                UIContentScaler.ScreenMatchMode.MatchWidthOrHeight);
            _screen.SafeArea = new Rect(20f, 31f, 335f, 601f);

            using (FUILayerContainer layers = new FUILayerContainer(() => _screen.SafeArea))
            {
                layers.Create();
                GComponent safe = layers.GetSubContainer(FUILayer.Normal, FUISafeAreaMode.Safe);
                float scale = GRoot.contentScaleFactor;

                Assert.AreEqual(_screen.SafeArea.x / scale, safe.x, 0.01f,
                    "安全区 X 应从屏幕像素转换为 FairyGUI 逻辑坐标。");
                Assert.AreEqual(
                    GRoot.inst.height - _screen.SafeArea.yMax / scale,
                    safe.y,
                    0.01f,
                    "安全区 Y 应从左下角屏幕坐标翻转到左上角 UI 坐标。");
                Assert.AreEqual(_screen.SafeArea.width / scale, safe.width, 0.01f,
                    "安全区宽度应按当前 contentScaleFactor 转换。");
                Assert.AreEqual(_screen.SafeArea.height / scale, safe.height, 0.01f,
                    "安全区高度应按当前 contentScaleFactor 转换。");

                _screen.SafeArea = Rect.zero;
                layers.ApplySafeAreaToAll();
                Assert.AreEqual(0f, safe.x, 0.01f, "空安全区应回退到根区域原点 X。");
                Assert.AreEqual(0f, safe.y, 0.01f, "空安全区应回退到根区域原点 Y。");
                Assert.AreEqual(GRoot.inst.width, safe.width, 0.01f, "空安全区应回退完整根宽度。");
                Assert.AreEqual(GRoot.inst.height, safe.height, 0.01f, "空安全区应回退完整根高度。");
            }
        }

        /// <summary>
        /// Stage resize 回调必须延迟到根尺寸与缩放稳定后，再用同一状态重算 Full/Safe 容器。
        /// </summary>
        [Test]
        public async UniTask StageResize_DeferredApplyUsesStableRootAndScale()
        {
            _screen.SetViewport(375, 667);
            GRoot.inst.SetContentScaleFactor(
                720,
                1280,
                UIContentScaler.ScreenMatchMode.MatchWidthOrHeight);
            _screen.SafeArea = new Rect(10f, 20f, 355f, 627f);

            using (FUILayerContainer layers = new FUILayerContainer(() => _screen.SafeArea))
            {
                layers.Create();

                _screen.SetStageSize(390, 844);
                _screen.SafeArea = new Rect(18f, 42f, 354f, 760f);
                Stage.inst.onStageResized.Call();

                // 模拟 Stage.HandleScreenSizeChanged 在事件派发后更新 scaler 与 GRoot。
                GRoot.inst.SetContentScaleFactor(
                    720,
                    1280,
                    UIContentScaler.ScreenMatchMode.MatchWidthOrHeight);
                await UniTask.NextFrame();

                GComponent full = layers.GetSubContainer(FUILayer.Normal, FUISafeAreaMode.Full);
                GComponent safe = layers.GetSubContainer(FUILayer.Normal, FUISafeAreaMode.Safe);
                float stableScale = GRoot.contentScaleFactor;

                Assert.AreEqual(GRoot.inst.width, full.width, 0.01f,
                    "Stage resize 后 Full 容器应覆盖稳定后的完整根宽度。");
                Assert.AreEqual(GRoot.inst.height, full.height, 0.01f,
                    "Stage resize 后 Full 容器应覆盖稳定后的完整根高度。");
                Assert.AreEqual(_screen.SafeArea.x / stableScale, safe.x, 0.01f,
                    "Safe X 应使用更新后的 contentScaleFactor。");
                Assert.AreEqual(GRoot.inst.height - _screen.SafeArea.yMax / stableScale, safe.y, 0.01f,
                    "Safe Y 应同时使用更新后的根高度与 contentScaleFactor。");
                Assert.AreEqual(_screen.SafeArea.width / stableScale, safe.width, 0.01f,
                    "Safe 宽度应使用稳定后的 contentScaleFactor。");
                Assert.AreEqual(_screen.SafeArea.height / stableScale, safe.height, 0.01f,
                    "Safe 高度应使用稳定后的 contentScaleFactor。");
            }
        }

        /// <summary>
        /// 同为竖屏的窗口尺寸变化应更新根缩放、层容器、全屏窗口与安全区，且不重建业务窗口。
        /// </summary>
        [Test]
        public async UniTask PortraitResize_UpdatesManagedWindowWithoutRecreation()
        {
            _screen.SetViewport(375, 667);
            _screen.SafeArea = new Rect(12f, 28f, 351f, 611f);
            FUIModule module = PlayModeTestHarness.SetupForShowAsync(
                options: new FUIOptions(),
                fullScreen: true,
                cacheMode: FUICacheMode.Cache,
                safeAreaMode: FUISafeAreaMode.Safe,
                safeAreaProvider: () => _screen.SafeArea);

            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();
            GComponent safe = module.LayerContainer.GetSubContainer(
                FUILayer.Normal,
                FUISafeAreaMode.Safe);
            AssertFullScreenLayout(window, safe, "尺寸变化前");

            _screen.SetStageSize(390, 844);
            _screen.SafeArea = new Rect(18f, 44f, 354f, 756f);
            Stage.inst.onStageResized.Call();
            GRoot.inst.SetContentScaleFactor(
                720,
                1280,
                UIContentScaler.ScreenMatchMode.MatchWidthOrHeight);
            await UniTask.NextFrame();

            float scale = GRoot.contentScaleFactor;
            GComponent layer = module.LayerContainer.GetLayer(FUILayer.Normal);
            Assert.AreEqual(GRoot.inst.width, layer.width, 0.01f, "层容器应更新为新根宽度。");
            Assert.AreEqual(GRoot.inst.height, layer.height, 0.01f, "层容器应更新为新根高度。");
            Assert.AreEqual(_screen.SafeArea.width / scale, safe.width, 0.01f,
                "Safe 容器应更新为新安全区逻辑宽度。");
            Assert.AreEqual(_screen.SafeArea.height / scale, safe.height, 0.01f,
                "Safe 容器应更新为新安全区逻辑高度。");
            Assert.AreSame(window, module.GetWindow<TestBattleStartPanel>(),
                "竖屏尺寸变化不得重建业务窗口。");
            AssertFullScreenLayout(window, safe, "尺寸变化后");
        }

        /// <summary>
        /// 生产组合根必须显式声明约定适配值，且公开 GameFUI 注册形态保持不变。
        /// </summary>
        [Test]
        public void ProductionComposition_UsesDeclaredOptionsAndKeepsPublicRegistrationShape()
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/GameScripts/HotFix/GameLogic/Module/HotFixModules.cs");
            Assert.IsNotNull(script, "应能读取生产组合根 HotFixModules 源码。");

            string source = script.text;
            StringAssert.IsMatch(@"DesignWidth\s*=\s*720", source,
                "HotFixModules 应显式传入 720 设计宽度。");
            StringAssert.IsMatch(@"DesignHeight\s*=\s*1280", source,
                "HotFixModules 应显式传入 1280 设计高度。");
            StringAssert.IsMatch(
                @"ScreenMatchMode\s*=\s*FUIScreenMatchMode\.MatchWidthOrHeight",
                source,
                "HotFixModules 应显式传入 MatchWidthOrHeight。");

            MethodInfo registerMethod = typeof(FUI).GetMethod(
                "RegisterModule",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(TEngine.IResourceModule), typeof(FUIOptions) },
                modifiers: null);

            Assert.IsNotNull(registerMethod,
                "公开 FUI.RegisterModule(IResourceModule, FUIOptions) 形态必须保持不变。");
            Assert.AreEqual(typeof(void), registerMethod.ReturnType, "公开注册方法仍应返回 void。");
        }

        private static void AssertFullScreenLayout(
            TestBattleStartPanel window,
            GComponent container,
            string stage)
        {
            Assert.AreSame(container, window.parent, $"{stage}：窗口应挂载到目标子容器。");
            Assert.AreEqual(0f, window.x, 0.01f, $"{stage}：窗口 X 应为容器原点。");
            Assert.AreEqual(0f, window.y, 0.01f, $"{stage}：窗口 Y 应为容器原点。");
            Assert.AreEqual(container.width, window.width, 0.01f, $"{stage}：窗口宽度应跟随容器。");
            Assert.AreEqual(container.height, window.height, 0.01f, $"{stage}：窗口高度应跟随容器。");
            Assert.IsTrue(window.relations.Contains(container), $"{stage}：窗口应保持目标容器关系。");
        }

        /// <summary>
        /// 可恢复屏幕夹具，统一管理 FairyGUI 的全局 Stage、GRoot、scaler 与安全区测试输入。
        /// </summary>
        private sealed class ScreenStateScope : System.IDisposable
        {
            private readonly UIContentScaler _scaler;
            private readonly UIContentScaler.ScaleMode _scaleMode;
            private readonly UIContentScaler.ScreenMatchMode _screenMatchMode;
            private readonly int _designResolutionX;
            private readonly int _designResolutionY;
            private readonly float _constantScaleFactor;
            private readonly float _scaleFactor;
            private readonly int _scaleLevel;
            private readonly float _stageWidth;
            private readonly float _stageHeight;
            private readonly float _rootWidth;
            private readonly float _rootHeight;
            private readonly float _rootScaleX;
            private readonly float _rootScaleY;
            private bool _disposed;

            public ScreenStateScope()
            {
                GRoot root = GRoot.inst;
                _scaler = Stage.inst.gameObject.GetComponent<UIContentScaler>();
                _scaleMode = _scaler.scaleMode;
                _screenMatchMode = _scaler.screenMatchMode;
                _designResolutionX = _scaler.designResolutionX;
                _designResolutionY = _scaler.designResolutionY;
                _constantScaleFactor = _scaler.constantScaleFactor;
                _scaleFactor = UIContentScaler.scaleFactor;
                _scaleLevel = UIContentScaler.scaleLevel;
                _stageWidth = Stage.inst.width;
                _stageHeight = Stage.inst.height;
                _rootWidth = root.width;
                _rootHeight = root.height;
                _rootScaleX = root.scaleX;
                _rootScaleY = root.scaleY;
                OriginalSafeArea = Screen.safeArea;
                SafeArea = OriginalSafeArea;
            }

            public Rect OriginalSafeArea { get; }

            public Rect SafeArea { get; set; }

            public void SetViewport(int width, int height)
            {
                Stage.inst.SetSize(width, height);
                GRoot.inst.SetContentScaleFactor(1f);
            }

            public void SetStageSize(int width, int height)
            {
                Stage.inst.SetSize(width, height);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Stage.inst.SetSize(_stageWidth, _stageHeight);

                _scaler.scaleMode = _scaleMode;
                _scaler.screenMatchMode = _screenMatchMode;
                _scaler.designResolutionX = _designResolutionX;
                _scaler.designResolutionY = _designResolutionY;
                _scaler.constantScaleFactor = _constantScaleFactor;
                UIContentScaler.scaleFactor = _scaleFactor;
                UIContentScaler.scaleLevel = _scaleLevel;

                GRoot root = GRoot.inst;
                root.SetSize(_rootWidth, _rootHeight);
                root.SetScale(_rootScaleX, _rootScaleY);
                SafeArea = OriginalSafeArea;
            }
        }
    }
}
