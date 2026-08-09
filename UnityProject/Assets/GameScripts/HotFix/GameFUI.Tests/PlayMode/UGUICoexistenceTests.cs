using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using GameFUI.Tests.EditMode;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UIBattle;

namespace GameFUI.Tests.PlayMode
{
    /// <summary>
    /// 共存验收用极简真实 UGUI 窗口（任务 8.5）。
    /// </summary>
    /// <remarks>
    /// 继承 GameLogic <see cref="UIWindow"/>，标记 <see cref="WindowAttribute"/>（<see cref="UILayer.UI"/>），
    /// 由真实 <see cref="UIModule"/> 公开入口 <see cref="UIModule.ShowUIAsyncAwait{T}"/> 打开，
    /// 经真实生命周期（InternalLoad → Handle_Completed → InternalCreate → OnRefresh）。
    /// 不设置 location，<see cref="UIModule"/> 以类型名作为资源地址，由测试资源加载器
    /// （<see cref="PlayModeTestHarness"/> 的 TestUGUIResourceLoader）提供面板 GameObject。
    /// 现有 GameLogic UIWindow 子类（LoginUI 等）为 internal，测试程序集不可访问，故在 Tests 内新建极简窗口。
    /// </remarks>
    [Window(UILayer.UI)]
    public class CoexistenceTestUGUIWindow : UIWindow
    {
    }

    /// <summary>
    /// 任务 8.5 PlayMode 验收：真实 UGUI <see cref="UIModule"/>/<see cref="UIWindow"/> 与测试 FairyGUI 窗口
    /// 同时运行、分别关闭、<see cref="FUIModule.Shutdown"/> 与 UGUI 模块退出时互不销毁。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// <list type="bullet">
    /// <item>spec fairygui-hot-update-delivery / Requirement: UGUI 在迁移期保持兼容 ——
    ///   FairyGUI 接入不得改变现有 UGUI <see cref="UIModule"/> 的公开行为或资源路径；两套窗口栈 SHALL 独立管理，
    ///   并可在同一运行期并存；Scenario: 同时打开 UGUI 与 FairyGUI 窗口 —— 两者 SHALL 独立完成生命周期、
    ///   层级和关闭操作，任一模块关闭不得销毁另一模块管理的窗口。</item>
    /// <item>design.md 决策7（GameFUI 使用独立固定层容器）—— GameFUI 在 GRoot 下建立自己的层级容器，
    ///   不引用位于 GameLogic 的 UILayer，与 UGUI 的 Canvas 渲染栈物理隔离。</item>
    /// <item>design.md Risks —— UGUI 与 FGUI 双栈增加过渡期开销 → 两套栈保持独立，不建立统一超大门面；
    ///   按业务域迁移，保留随时停止注册 FGUI 的回滚能力。</item>
    /// <item>design.md Non-Goals —— 不在本 change 中迁移所有现有 UGUI 窗口或删除 <see cref="UIModule"/>。</item>
    /// </list>
    /// <para>
    /// <b>任务 8.5 改造点</b>：7.6 此前用代表性 UGUI 树（手工 Canvas+GraphicRaycaster）验共存，非真实
    /// <see cref="UIModule"/>/<see cref="UIWindow"/>。本测试改为由专用验收测试装配（<see cref="PlayModeTestHarness.SetupUGUIModule"/>）
    /// 装配真实 <see cref="UIModule"/> 单例，通过现有 UGUI 公开入口（<see cref="UIModule.ShowUIAsyncAwait{T}"/>、
    /// <see cref="UIModule.CloseUI{T}"/>、<c>Release</c>）打开与关闭真实 <see cref="UIWindow"/>，并与测试 FairyGUI
    /// 窗口同时运行。GameFUI.Runtime 不反向依赖 GameLogic（约束仅限 Runtime）；本测试程序集新增 GameLogic 引用
    /// 不违反该约束，且未修改 GameLogic 源码或 GameLogic.asmdef。
    /// </para>
    /// <para>
    /// <b>共存隔离的物理基础</b>：UGUI 通过 <c>Canvas</c> + <c>GraphicRaycaster</c> 在 Unity 渲染管线中独立渲染；
    /// FairyGUI 通过自己的 <c>Stage</c> + <c>StageCamera</c> + MeshRenderer 渲染。两者在场景层级中是各自的
    /// GameObject 子树，互不包含。GameFUI 的 <see cref="FUIModule.Shutdown"/> 只释放 GRoot 下的层级容器与
    /// FairyGUI 包对象，不调用任何 UGUI API；反之 <see cref="UIModule"/>.<c>Release</c> 只关闭 UGUI 窗口栈与
    /// UIRoot，不影响 FairyGUI 内部对象树（由 Stage 持有，不在 UGUI Canvas 下）。
    /// </para>
    /// <para>
    /// <b>复用证据</b>：
    /// <list type="bullet">
    /// <item>复用 <see cref="PlayModeTestHarness.SetupForShowAsync"/>（任务 7.1 产出）完成 GameFUI 模块装配、
    ///   UIBattle Binder 调用、最终测试 Window/Widget 注册与 Registry 冻结，使 <see cref="FUI.ShowAsync{T}"/>
    ///   可直接打开 <see cref="TestBattleStartPanel"/>。</item>
    /// <item>复用 <see cref="PlayModeTestHarness.Cleanup"/> 完成 GameFUI 模块 Shutdown 与全局状态基线重置。</item>
    /// <item>复用 <see cref="PlayModeTestHarness.EnsureGRootInitialized"/> 确保 FairyGUI Stage/GRoot 在测试前已就绪。</item>
    /// <item>复用 <see cref="PlayModeTestHarness.SetupUGUIModule"/> / <see cref="PlayModeTestHarness.ReleaseUGUIModule"/>
    ///   （任务 8.5 新增）装配真实 <see cref="UIModule"/> 单例与 UIRoot 场景前置、注入测试资源加载器，
    ///   使测试经 <see cref="UIModule"/> 公开入口驱动真实 <see cref="UIWindow"/> 生命周期。</item>
    /// <item>复用 <see cref="TestBattleStartPanel"/>（任务 3.6 产出）作为 FairyGUI 侧窗口。</item>
    /// <item>复用 <see cref="FUI.ShowAsync{T}"/> / <see cref="FUI.Close{T}"/> / <see cref="IFUIModule.Shutdown"/>
    ///   等 GameFUI 公开/internal API 驱动 FairyGUI 侧生命周期。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，不修改 GameLogic 源码或 GameLogic.asmdef，
    /// 不创建或修改 BattleModule，不调用 <c>ModuleSystem.RegisterModule</c>。UGUI 侧通过 <see cref="UIModule"/>
    /// 公开入口与公开注入点 <see cref="UIModule.Resource"/> 装配，FairyGUI 侧通过 GameFUI 公开/internal API 装配。
    /// </para>
    /// </remarks>
    [TestFixture]
    public class UGUICoexistenceTests
    {
        /// <summary>
        /// 每个测试前的状态基线重置：清空 GameFUI 与 FairyGUI 全局状态，确保 FairyGUI Stage/GRoot 已初始化。
        /// </summary>
        /// <remarks>
        /// 不在此装配 UIModule 或 GameFUI 模块：各测试对两侧装配时机与顺序有不同需求，由各测试自行调用
        /// <see cref="PlayModeTestHarness.SetupUGUIModule"/> 与 <see cref="PlayModeTestHarness.SetupForShowAsync"/>。
        /// 本方法只保证全局状态干净与 GRoot 就绪。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            // 清空 GameFUI 与 FairyGUI 全局状态，避免上一测试残留。
            PlayModeTestHarness.Cleanup();

            // 确保 FairyGUI Stage/GRoot 已初始化（EditMode/Editor PlayMode 下需要主动触发）。
            PlayModeTestHarness.EnsureGRootInitialized();
        }

        /// <summary>
        /// 每个测试后的状态基线重置：释放真实 UIModule 与 UIRoot，并清空 GameFUI 与 FairyGUI 全局状态。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="SetUp"/> 对称。先释放 UGUI 侧（<see cref="PlayModeTestHarness.ReleaseUGUIModule"/>，
        /// 幂等），再清空 GameFUI/FairyGUI 全局状态（<see cref="PlayModeTestHarness.Cleanup"/>），
        /// 确保即使测试中途失败也不残留 UIModule、UIRoot GameObject 或 FairyGUI 全局状态污染后续测试。
        /// </remarks>
        [TearDown]
        public void TearDown()
        {
            // 1. 释放真实 UIModule 并销毁 UIRoot（幂等：已释放则跳过）。
            PlayModeTestHarness.ReleaseUGUIModule();

            // 2. 清空 GameFUI 与 FairyGUI 全局状态。
            PlayModeTestHarness.Cleanup();
        }

        /// <summary>
        /// 验证真实 UGUI 窗口与 FairyGUI 窗口能在同一运行期同时存在，互不影响显示状态。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口）：
        /// <list type="bullet">
        /// <item>UGUI 侧通过 <see cref="UIModule.ShowUIAsyncAwait{T}"/> 公开入口打开真实
        ///   <see cref="CoexistenceTestUGUIWindow"/>，实例非空、面板已创建并挂载在 UIModule 窗口栈中；</item>
        /// <item>FairyGUI 侧通过 <see cref="FUI.ShowAsync{T}"/> 打开 <see cref="TestBattleStartPanel"/>，
        ///   实例非空、onStage=true 且 isDisposed=false；</item>
        /// <item>两套窗口栈的 GameObject 子树互不包含。</item>
        /// </list>
        /// 两者同时存在是共存隔离契约的基线，后续关闭/退出测试在此基础上验证互不破坏。
        /// </remarks>
        [Test]
        public async UniTask UGUIAndFairyGUIWindow_Coexist_Simultaneously()
        {
            // 1. 装配真实 UIModule 并通过公开入口 ShowUIAsyncAwait 打开真实 UIWindow。
            UIModule uiModule = PlayModeTestHarness.SetupUGUIModule();
            CoexistenceTestUGUIWindow uguiWindow = await uiModule.ShowUIAsyncAwait<CoexistenceTestUGUIWindow>();

            // 2. 装配 GameFUI 模块并打开 FairyGUI 测试窗口。
            FUIModule fuiModule = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await fuiModule.ShowAsync<TestBattleStartPanel>();

            // 3. 断言 UGUI 侧：真实 UIWindow 实例非空、面板已创建、挂载在 UIModule 窗口栈中。
            Assert.IsNotNull(uguiWindow, "真实 UIWindow 实例应非空（ShowUIAsyncAwait 返回）。");
            Assert.IsNotNull(uguiWindow.gameObject, "UIWindow 面板 GameObject 应已创建（Handle_Completed 完成）。");
            Assert.IsNotNull(uguiWindow.Canvas, "UIWindow 面板应带 Canvas 组件（Handle_Completed 读取）。");
            Assert.IsTrue(uiModule.HasWindow<CoexistenceTestUGUIWindow>(),
                "UIWindow 应已挂载在 UIModule 窗口栈中（HasWindow=true）。");

            // 4. 断言 FairyGUI 侧：窗口实例非空、已挂载到 Stage 且未释放。
            Assert.IsNotNull(fuiWindow, "FairyGUI 窗口实例应非空（ShowAsync 返回）。");
            Assert.IsTrue(fuiWindow.onStage, "FairyGUI 窗口应已挂载到 Stage（onStage=true）。");
            Assert.IsFalse(fuiWindow.isDisposed, "FairyGUI 窗口应未被释放（isDisposed=false）。");

            // 5. 断言共存隔离：UGUI 面板 GameObject 与 FairyGUI displayObject 是各自独立的 GameObject。
            Assert.AreNotEqual(uguiWindow.gameObject, fuiWindow.displayObject.cachedTransform,
                "UGUI 面板 GameObject 与 FairyGUI displayObject 不应等同。");
            Assert.IsNotNull(fuiWindow.parent, "FairyGUI 窗口应有 parent（GameFUI 层级容器）。");
        }

        /// <summary>
        /// 验证关闭 FairyGUI 窗口不影响真实 UGUI 窗口的显示与组件完整性。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口 ——
        /// 两者 SHALL 独立完成生命周期、层级和关闭操作）：
        /// <list type="bullet">
        /// <item>调用 <see cref="FUI.Close{T}"/> 关闭 FairyGUI 窗口后，FairyGUI 窗口被释放
        ///   （isDisposed=true、onStage=false）；</item>
        /// <item>真实 UGUI 窗口仍挂载在 UIModule 栈中、面板 GameObject 存活、Canvas/GraphicRaycaster 完好。</item>
        /// </list>
        /// 这验证 FairyGUI 窗口关闭只释放 FairyGUI 对象，不触碰 UIModule 管理的 UGUI 窗口栈。
        /// </remarks>
        [Test]
        public async UniTask CloseFairyGUIWindow_DoesNotDestroyUGUIWindow()
        {
            // 1. 装配两侧并打开窗口。
            UIModule uiModule = PlayModeTestHarness.SetupUGUIModule();
            CoexistenceTestUGUIWindow uguiWindow = await uiModule.ShowUIAsyncAwait<CoexistenceTestUGUIWindow>();
            FUIModule fuiModule = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await fuiModule.ShowAsync<TestBattleStartPanel>();

            // 记录 UGUI 窗口的关键组件引用，用于关闭后验证组件未被销毁。
            Canvas uguiCanvas = uguiWindow.Canvas;
            GraphicRaycaster uguiRaycaster = uguiWindow.GraphicRaycaster;

            // 2. 关闭 FairyGUI 窗口（默认 CacheMode=None，Close 后 Dispose 实例）。
            FUI.Close<TestBattleStartPanel>();

            // 3. 断言 FairyGUI 侧：窗口已释放并从 Stage 移除。
            Assert.IsTrue(fuiWindow.isDisposed, "FairyGUI 窗口应已被释放（isDisposed=true）。");
            Assert.IsFalse(fuiWindow.onStage, "FairyGUI 窗口应已从 Stage 移除（onStage=false）。");

            // 4. 断言 UGUI 侧：真实窗口与组件均完好，不受 FairyGUI 关闭影响。
            Assert.IsNotNull(uguiWindow.gameObject, "UGUI 面板 GameObject 不应被 FairyGUI 关闭操作销毁。");
            Assert.IsTrue(uiModule.HasWindow<CoexistenceTestUGUIWindow>(),
                "UGUI 窗口应仍挂载在 UIModule 栈中。");
            Assert.IsNotNull(uguiCanvas, "UGUI Canvas 组件应完好。");
            Assert.IsNotNull(uguiRaycaster, "UGUI GraphicRaycaster 组件应完好。");
        }

        /// <summary>
        /// 验证通过 UIModule 公开入口关闭真实 UGUI 窗口不影响 FairyGUI 窗口的显示与对象完整性。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口 ——
        /// 两者 SHALL 独立完成生命周期、层级和关闭操作）：
        /// <list type="bullet">
        /// <item>调用 <see cref="UIModule.CloseUI{T}"/> 关闭真实 UGUI 窗口后，UGUI 窗口从 UIModule 栈移除、
        ///   面板 GameObject 销毁；</item>
        /// <item>FairyGUI 窗口仍 onStage=true、isDisposed=false，parent 未变。</item>
        /// </list>
        /// 这验证 UGUI 窗口关闭只影响 UIModule 管理的 UGUI 窗口栈，不影响 FairyGUI 内部的对象树。
        /// </remarks>
        [Test]
        public async UniTask CloseUGUIWindow_DoesNotDestroyFairyGUIWindow()
        {
            // 1. 装配两侧并打开窗口。
            UIModule uiModule = PlayModeTestHarness.SetupUGUIModule();
            CoexistenceTestUGUIWindow uguiWindow = await uiModule.ShowUIAsyncAwait<CoexistenceTestUGUIWindow>();
            FUIModule fuiModule = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await fuiModule.ShowAsync<TestBattleStartPanel>();

            // 记录 FairyGUI 窗口的 parent，用于关闭后验证 parent 未变。
            GComponent fuiParentBefore = fuiWindow.parent;

            // 2. 通过 UIModule 公开入口 CloseUI 关闭真实 UGUI 窗口。
            uiModule.CloseUI<CoexistenceTestUGUIWindow>();

            // 3. 断言 UGUI 侧：窗口已从 UIModule 栈移除、面板已销毁（gameObject 置空）。
            Assert.IsFalse(uiModule.HasWindow<CoexistenceTestUGUIWindow>(),
                "UGUI 窗口应已从 UIModule 栈中移除。");
            Assert.IsNull(uguiWindow.gameObject, "UGUI 面板应已销毁（InternalDestroy 将 _panel 置空）。");

            // 4. 断言 FairyGUI 侧：窗口仍正常显示、未释放、parent 未变。
            Assert.IsFalse(fuiWindow.isDisposed, "FairyGUI 窗口不应因 UGUI 关闭而被释放。");
            Assert.IsTrue(fuiWindow.onStage, "FairyGUI 窗口应仍挂载在 Stage。");
            Assert.AreEqual(fuiParentBefore, fuiWindow.parent,
                "FairyGUI 窗口的 parent 不应因 UGUI 关闭而改变。");
        }

        /// <summary>
        /// 验证 GameFUI 模块退出（<see cref="FUIModule.Shutdown"/>）不破坏真实 UGUI 窗口。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口 ——
        /// 任一模块关闭不得销毁另一模块管理的窗口；spec fairygui-window-runtime / Requirement: 模块退出完整清理 ——
        /// 模块退出 SHALL 取消所有进行中的打开操作，按反向顺序关闭并释放窗口，执行 detach，清理本地描述、owner、
        /// 活动 Registry 和静态模块缓存，并把所持包租约交还资源管理能力）：
        /// <list type="bullet">
        /// <item>调用 <see cref="IFUIModule.Shutdown"/> 后，FairyGUI 窗口被释放（isDisposed=true、onStage=false）；</item>
        /// <item>真实 UGUI 窗口仍挂载在 UIModule 栈中、面板 GameObject 存活、Canvas/GraphicRaycaster 完好。</item>
        /// </list>
        /// 这验证 GameFUI 模块退出只清理 FairyGUI 侧资源，不调用任何 UGUI API，不破坏 UIModule 管理的 UGUI 窗口栈。
        /// </remarks>
        [Test]
        public async UniTask FUIModuleShutdown_DoesNotDestroyUGUIWindow()
        {
            // 1. 装配两侧并打开窗口。
            UIModule uiModule = PlayModeTestHarness.SetupUGUIModule();
            CoexistenceTestUGUIWindow uguiWindow = await uiModule.ShowUIAsyncAwait<CoexistenceTestUGUIWindow>();
            FUIModule fuiModule = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await fuiModule.ShowAsync<TestBattleStartPanel>();

            // 记录 UGUI 窗口的关键组件引用，用于 Shutdown 后验证组件未被销毁。
            Canvas uguiCanvas = uguiWindow.Canvas;
            GraphicRaycaster uguiRaycaster = uguiWindow.GraphicRaycaster;

            // 2. 调用 GameFUI 模块 Shutdown（模拟 GameFUI 模块退出）。
            //    Shutdown 取消打开操作、释放窗口与包租约、清空 Registry 与层级容器。
            fuiModule.Shutdown();

            // 3. 断言 FairyGUI 侧：窗口已释放并从 Stage 移除。
            Assert.IsTrue(fuiWindow.isDisposed, "FairyGUI 窗口应已被模块 Shutdown 释放。");
            Assert.IsFalse(fuiWindow.onStage, "FairyGUI 窗口应已从 Stage 移除。");

            // 4. 断言 UGUI 侧：真实窗口与组件均完好，不受 GameFUI 模块 Shutdown 影响。
            Assert.IsNotNull(uguiWindow.gameObject, "UGUI 面板 GameObject 不应被 GameFUI 模块 Shutdown 销毁。");
            Assert.IsTrue(uiModule.HasWindow<CoexistenceTestUGUIWindow>(),
                "UGUI 窗口应仍挂载在 UIModule 栈中。");
            Assert.IsNotNull(uguiCanvas, "UGUI Canvas 组件应完好。");
            Assert.IsNotNull(uguiRaycaster, "UGUI GraphicRaycaster 组件应完好。");
        }

        /// <summary>
        /// 验证 UGUI 模块退出（<see cref="UIModule"/> Release）不破坏 FairyGUI 窗口。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口 ——
        /// 任一模块关闭不得销毁另一模块管理的窗口）：
        /// <list type="bullet">
        /// <item>调用 <c>UIModule.Instance.Release()</c>（触发 <see cref="UIModule.OnRelease"/>：关闭所有 UGUI 窗口、
        ///   销毁 UIRoot、清空单例）后，UIModule 已释放（IsValid=false）；</item>
        /// <item>FairyGUI 窗口仍 onStage=true、isDisposed=false，parent 未变；FairyGUI Stage 与 GRoot 仍可用。</item>
        /// </list>
        /// 这验证 UIModule 退出只关闭 UGUI 窗口栈与 UIRoot，不影响 FairyGUI 内部对象树（由 Stage 持有，不在 UGUI Canvas 下）。
        /// </remarks>
        [Test]
        public async UniTask UGUIModuleRelease_DoesNotDestroyFairyGUIWindow()
        {
            // 1. 装配两侧并打开窗口。
            UIModule uiModule = PlayModeTestHarness.SetupUGUIModule();
            await uiModule.ShowUIAsyncAwait<CoexistenceTestUGUIWindow>();
            FUIModule fuiModule = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await fuiModule.ShowAsync<TestBattleStartPanel>();

            // 记录 FairyGUI 窗口的 parent，用于 UGUI 模块退出后验证 parent 未变。
            GComponent fuiParentBefore = fuiWindow.parent;

            // 2. 通过 UIModule.Release 公开入口模拟 UGUI 模块退出
            //    （OnRelease：CloseAll 关闭所有 UGUI 窗口、销毁 UIRoot、清空 _instance）。
            uiModule.Release();

            // 3. 断言 UGUI 侧：UIModule 已释放（IsValid=false，不触发重新初始化）。
            Assert.IsFalse(UIModule.IsValid, "UIModule 应已释放（IsValid=false）。");

            // 4. 断言 FairyGUI 侧：窗口仍正常显示、未释放、parent 未变，Stage 与 GRoot 仍可用。
            Assert.IsFalse(fuiWindow.isDisposed, "FairyGUI 窗口不应因 UGUI 模块退出而被释放。");
            Assert.IsTrue(fuiWindow.onStage, "FairyGUI 窗口应仍挂载在 Stage。");
            Assert.AreEqual(fuiParentBefore, fuiWindow.parent,
                "FairyGUI 窗口的 parent 不应因 UGUI 模块退出而改变。");
            Assert.IsNotNull(Stage.inst, "FairyGUI Stage 应仍可用。");
            Assert.IsNotNull(GRoot.inst, "FairyGUI GRoot 应仍可用。");
        }
    }
}
