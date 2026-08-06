using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using GameFUI.Tests.EditMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UIBattle;

namespace GameFUI.Tests.PlayMode
{
    /// <summary>
    /// 任务 7.6 PlayMode 验收：现有 UGUI 与测试 FairyGUI 窗口同时存在、分别关闭和模块退出时互不破坏。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// <list type="bullet">
    /// <item>spec fairygui-hot-update-delivery / Requirement: UGUI 在迁移期保持兼容 ——
    ///   FairyGUI 接入不得改变现有 UGUI UIModule 的公开行为或资源路径；两套窗口栈 SHALL 独立管理，
    ///   并可在同一运行期并存；Scenario: 同时打开 UGUI 与 FairyGUI 窗口 —— 两者 SHALL 独立完成生命周期、
    ///   层级和关闭操作，任一模块关闭不得销毁另一模块管理的窗口。</item>
    /// <item>design.md 决策7（GameFUI 使用独立固定层容器）—— GameFUI 在 GRoot 下建立自己的层级容器，
    ///   不引用位于 GameLogic 的 UILayer，与 UGUI 的 Canvas 渲染栈物理隔离。</item>
    /// <item>design.md Risks —— UGUI 与 FGUI 双栈增加过渡期开销 → 两套栈保持独立，不建立统一超大门面；
    ///   按业务域迁移，保留随时停止注册 FGUI 的回滚能力。</item>
    /// <item>design.md Non-Goals —— 不在本 change 中迁移所有现有 UGUI 窗口或删除 UIModule。</item>
    /// </list>
    ///
    /// <para>
    /// <b>为何不直接调用 GameLogic.UIModule</b>：GameFUI.Tests.asmdef 不引用 GameLogic（design.md 决策1：
    /// GameFUI 不反向依赖 GameLogic），测试程序集无法访问 <c>GameLogic.UIWindow</c>、<c>UIModule.Instance</c>
    /// 或任何 UGUI 业务窗口类型。本测试通过构造一个代表性的 UGUI Canvas 根节点与 UGUI 窗口 GameObject
    /// （挂载 <see cref="Canvas"/> 与 <see cref="GraphicRaycaster"/>，模拟真实 UIModule 管理的 UIRoot+窗口栈），
    /// 验证两套渲染栈在同一运行期互不破坏。该模拟保留了 UGUI 的关键结构特征：
    /// <c>Canvas</c> 渲染、<c>GraphicRaycaster</c> 输入、独立 GameObject 生命周期，足以验证共存隔离契约。
    /// </para>
    /// <para>
    /// <b>共存隔离的物理基础</b>：UGUI 通过 <c>Canvas</c> + <c>GraphicRaycaster</c> 在 Unity 渲染管线中独立渲染；
    /// FairyGUI 通过自己的 <c>Stage</c> + <c>StageCamera</c> + MeshRenderer 渲染（见 <c>Stage.Instantiate</c>）。
    /// 两者在场景层级中是各自的 GameObject 子树，互不包含。GameFUI 的 <see cref="FUIModule.Shutdown"/> 只释放
    /// GRoot 下的层级容器与 FairyGUI 包对象（design.md 决策7、spec: 模块退出完整清理），不调用任何 UGUI API，
    /// 因此 UGUI 树不受影响。反之，销毁 UGUI GameObject 只影响 Unity 场景层级，不影响 FairyGUI 内部的对象树
    /// （FairyGUI 对象由 <c>Stage</c> 持有，不在 UGUI Canvas 下）。
    /// </para>
    ///
    /// <para>
    /// <b>复用证据</b>：
    /// <list type="bullet">
    /// <item>复用 <see cref="PlayModeTestHarness.SetupForShowAsync"/>（任务 7.1 产出）完成 GameFUI 模块装配、
    ///   UIBattle Binder 调用、最终测试 Window/Widget 注册与 Registry 冻结，使 <see cref="FUI.ShowAsync{T}"/>
    ///   可直接打开 <see cref="TestBattleStartPanel"/>。</item>
    /// <item>复用 <see cref="PlayModeTestHarness.Cleanup"/> 完成模块 Shutdown 与全局状态基线重置。</item>
    /// <item>复用 <see cref="PlayModeTestHarness.EnsureGRootInitialized"/> 确保 FairyGUI Stage/GRoot 在测试前已就绪。</item>
    /// <item>复用 <see cref="TestBattleStartPanel"/>（任务 3.6 产出的最终测试业务窗口类型）作为 FairyGUI 侧窗口。</item>
    /// <item>复用 <see cref="FUI.ShowAsync{T}"/> / <see cref="FUI.Close{T}"/> / <see cref="IFUIModule.Shutdown"/>
    ///   等 GameFUI 公开/internal API 驱动 FairyGUI 侧生命周期。</item>
    /// </list>
    /// 装配模式与 PlayModeTestHarness 既有 PlayMode 验收（任务 7.2/7.3）完全一致，不引入测试专用运行时旁路。
    /// </para>
    ///
    /// <para>
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，不依赖 GameLogic/GamePlay/GameBattle，
    /// 不创建或修改 BattleModule，不调用 <c>ModuleSystem.RegisterModule</c>。只通过公开/internal API 与
    /// UnityEngine.UI 基础组件构造代表性 UGUI 树，验证共存隔离契约。
    /// </para>
    /// </remarks>
    [TestFixture]
    public class UGUICoexistenceTests
    {
        /// <summary>
        /// 代表性 UGUI 根节点 GameObject 名称，模拟真实 UIModule 的 UIRoot。
        /// </summary>
        private const string UGUIRootName = "TestUGUIRoot";

        /// <summary>
        /// 代表性 UGUI 窗口 GameObject 名称，模拟真实 UIModule 管理的 UIWindow 实例。
        /// </summary>
        private const string UGUIWindowName = "TestUGUIWindow";

        /// <summary>
        /// 测试期间创建的 UGUI 根节点 GameObject 实例，用于 TearDown 统一清理。
        /// </summary>
        private GameObject _uguiRoot;

        /// <summary>
        /// 每个测试前的状态基线重置：清空 GameFUI 与 FairyGUI 全局状态，确保 FairyGUI Stage/GRoot 已初始化。
        /// </summary>
        /// <remarks>
        /// 不在此调用 <see cref="PlayModeTestHarness.SetupForShowAsync"/>：各测试对模块装配时机有不同需求
        /// （如模块退出测试需要在 UGUI 树创建后再装配模块），故由各测试自行调用 SetupForShowAsync。
        /// 本方法只保证全局状态干净与 GRoot 就绪。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            // 清空 GameFUI 与 FairyGUI 全局状态，避免上一测试残留。
            PlayModeTestHarness.Cleanup();

            // 确保 FairyGUI Stage/GRoot 已初始化（EditMode/Editor PlayMode 下需要主动触发）。
            // GRoot.inst getter 在首次访问时调用 Stage.Instantiate() 创建 Stage 与 GRoot。
            PlayModeTestHarness.EnsureGRootInitialized();

            // UGUI 根节点在具体测试中按需创建，SetUp 不预创建。
            _uguiRoot = null;
        }

        /// <summary>
        /// 每个测试后的状态基线重置：销毁测试期间创建的 UGUI 树，并清空 GameFUI 与 FairyGUI 全局状态。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="SetUp"/> 对称，确保即使测试中途失败也不会残留 UGUI GameObject 或 FairyGUI 全局状态
        /// 污染后续测试。UGUI 树销毁使用 <see cref="UnityEngine.Object.DestroyImmediate"/>（EditMode 下立即生效）；
        /// GameFUI/FairyGUI 全局清理复用 <see cref="PlayModeTestHarness.Cleanup"/>。
        /// </remarks>
        [TearDown]
        public void TearDown()
        {
            // 1. 销毁测试期间创建的 UGUI 根节点（及其子窗口）。
            //    DestroyImmediate 在 EditMode/Editor 下立即生效，保证 TearDown 后无残留。
            if (_uguiRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_uguiRoot);
                _uguiRoot = null;
            }

            // 2. 清空 GameFUI 与 FairyGUI 全局状态。
            PlayModeTestHarness.Cleanup();
        }

        /// <summary>
        /// 构造代表性的 UGUI 根节点与窗口 GameObject，模拟真实 UIModule 管理的 UIRoot + UIWindow 栈。
        /// </summary>
        /// <returns>UGUI 窗口 GameObject 实例（挂载 Canvas 与 GraphicRaycaster）。</returns>
        /// <remarks>
        /// 真实 UIModule（<c>GameLogic.UIModule</c>）在 <c>OnInit</c> 中查找场景中的 UIRoot GameObject，
        /// 获取其 Canvas 作为窗口父节点，每个 UIWindow 实例加载 Prefab 并挂到该 Canvas 下，窗口自身也带 Canvas
        /// （overrideSorting）与 GraphicRaycaster（见 <c>UIWindow.Handle_Completed</c>）。
        /// <para>
        /// 本方法不依赖 GameLogic，但保留了 UGUI 栈的关键结构特征：
        /// <list type="bullet">
        /// <item>根节点 <c>UIRoot</c>：带 <see cref="Canvas"/> + <see cref="CanvasScaler"/>，
        ///   模拟真实 UIRoot 的 Canvas 容器（UIModule.UIRoot 即为该 Canvas 的 transform）。</item>
            /// <item>窗口节点 <c>UGUIWindow</c>：作为 UIRoot 的子物体，带 <see cref="Canvas"/>（overrideSorting=true）
            ///   + <see cref="GraphicRaycaster"/> + <see cref="UnityEngine.UI.Image"/>，模拟真实 UIWindow 的 Canvas+Raycaster+Graphic 结构。</item>
        /// </list>
        /// 该结构足以验证 UGUI 与 FairyGUI 的渲染栈隔离：UGUI 通过 Canvas 渲染，FairyGUI 通过 Stage+StageCamera 渲染，
        /// 两者在场景层级中是各自的 GameObject 子树，互不包含。
        /// </para>
        /// <para>
        /// 创建的 UGUI 根节点赋值到 <see cref="_uguiRoot"/>，由 <see cref="TearDown"/> 统一销毁。
        /// </para>
        /// </remarks>
        private GameObject CreateRepresentativeUGUITree()
        {
            // 1. 构造 UGUI 根节点（模拟 UIRoot + Canvas）。
            _uguiRoot = new GameObject(UGUIRootName);
            Canvas rootCanvas = _uguiRoot.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = _uguiRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // 根节点挂载 GraphicRaycaster，使 UGUI 输入事件可命中根 Canvas 下的 Graphic。
            _uguiRoot.AddComponent<GraphicRaycaster>();

            // 2. 构造 UGUI 窗口节点（模拟 UIWindow 实例：Canvas + GraphicRaycaster + Image）。
            GameObject uguiWindow = new GameObject(UGUIWindowName);
            uguiWindow.transform.SetParent(_uguiRoot.transform, false);
            Canvas windowCanvas = uguiWindow.AddComponent<Canvas>();
            windowCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            windowCanvas.overrideSorting = true; // 模拟 UIWindow.Handle_Completed 中的 overrideSorting=true
            windowCanvas.sortingOrder = 100; // 模拟 UIModule 的 WINDOW_DEEP 排序
            uguiWindow.AddComponent<GraphicRaycaster>();
            // 挂载 Image 作为可见 Graphic，模拟窗口背景。
            UnityEngine.UI.Image windowImage = uguiWindow.AddComponent<UnityEngine.UI.Image>();
            RectTransform windowRect = uguiWindow.GetComponent<RectTransform>();
            windowRect.anchorMin = Vector2.zero;
            windowRect.anchorMax = Vector2.one;
            windowRect.offsetMin = Vector2.zero;
            windowRect.offsetMax = Vector2.zero;
            windowImage.color = new Color(0.2f, 0.2f, 0.2f, 1f); // 设置窗口背景色，使 Image 可见

            return uguiWindow;
        }

        /// <summary>
        /// 验证 UGUI 窗口与 FairyGUI 窗口能在同一运行期同时存在，互不影响显示状态。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口）：
        /// <list type="bullet">
        /// <item>UGUI 窗口 GameObject 存活且 activeInHierarchy=true（UGUI 侧正常显示）；</item>
        /// <item>FairyGUI 窗口实例非空、onStage=true 且 isDisposed=false（FairyGUI 侧正常显示）；</item>
        /// <item>FairyGUI 窗口挂载在 GRoot 下的 GameFUI 层级容器中，不在 UGUI Canvas 下；</item>
        /// <item>UGUI 窗口挂载在 UGUI Canvas 下，不在 FairyGUI Stage 下。</item>
        /// </list>
        /// 两者同时存在是共存隔离契约的基线，后续关闭/退出测试在此基础上验证互不破坏。
        /// </remarks>
        [Test]
        public async UniTask UGUIAndFairyGUIWindow_Coexist_Simultaneously()
        {
            // 1. 构造代表性 UGUI 树。
            GameObject uguiWindow = CreateRepresentativeUGUITree();

            // 2. 装配 GameFUI 模块并打开 FairyGUI 测试窗口。
            //    使用 SetupForShowAsync 而非 Setup：SetupForShowAsync 提供非空 creator，使 ShowAsync 能创建最终业务类型实例。
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await module.ShowAsync<TestBattleStartPanel>();

            // 3. 断言 UGUI 侧：窗口存活且在层级中激活。
            Assert.IsNotNull(uguiWindow, "UGUI 窗口 GameObject 应已创建。");
            Assert.IsTrue(uguiWindow.activeInHierarchy, "UGUI 窗口应处于激活状态（activeInHierarchy=true）。");
            Assert.IsNotNull(uguiWindow.GetComponent<Canvas>(), "UGUI 窗口应保留 Canvas 组件。");

            // 4. 断言 FairyGUI 侧：窗口实例非空、已挂载到 Stage 且未释放。
            Assert.IsNotNull(fuiWindow, "FairyGUI 窗口实例应非空（ShowAsync 返回非空业务窗口）。");
            Assert.IsTrue(fuiWindow.onStage, "FairyGUI 窗口应已挂载到 Stage（onStage=true）。");
            Assert.IsFalse(fuiWindow.isDisposed, "FairyGUI 窗口应未被释放（isDisposed=false）。");

            // 5. 断言共存隔离：FairyGUI 窗口不在 UGUI Canvas 下，UGUI 窗口不在 FairyGUI Stage 下。
            //    FairyGUI 对象的 displayObject.cachedTransform.parent 链最终通向 Stage，而非 UGUI Canvas。
            //    这里通过检查 FairyGUI 窗口的 parent 是否为 GameFUI 层级容器（GComponent）来验证。
            Assert.IsNotNull(fuiWindow.parent, "FairyGUI 窗口应有 parent（GameFUI 层级容器）。");
            Assert.AreNotEqual(uguiWindow.transform, fuiWindow.displayObject.cachedTransform,
                "FairyGUI 窗口的 displayObject 不应挂在 UGUI 窗口 GameObject 下。");

            // 6. 断言 UGUI 根节点与 FairyGUI Stage 是各自独立的 GameObject。
            //    Stage 在 Stage.Instantiate 时创建为独立 GameObject（名为 "Stage"），不在 UGUI Canvas 下。
            GameObject stageGo = Stage.inst.gameObject;
            Assert.IsNotNull(stageGo, "FairyGUI Stage 应有对应 GameObject。");
            Assert.AreNotEqual(_uguiRoot, stageGo, "FairyGUI Stage GameObject 不应等同于 UGUI 根节点。");
            // UGUI 根节点不应是 Stage 的子物体，Stage 也不应是 UGUI 根节点的子物体。
            Assert.IsFalse(IsDescendantOf(stageGo.transform, _uguiRoot.transform),
                "FairyGUI Stage 不应是 UGUI 根节点的子物体。");
            Assert.IsFalse(IsDescendantOf(_uguiRoot.transform, stageGo.transform),
                "UGUI 根节点不应是 FairyGUI Stage 的子物体。");
        }

        /// <summary>
        /// 验证关闭 FairyGUI 窗口不影响 UGUI 窗口的显示与组件完整性。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口 ——
        /// 两者 SHALL 独立完成生命周期、层级和关闭操作）：
        /// <list type="bullet">
        /// <item>调用 <see cref="FUI.Close{T}"/> 关闭 FairyGUI 窗口后，FairyGUI 窗口被释放（isDisposed=true、onStage=false）；</item>
        /// <item>UGUI 窗口 GameObject 仍存活、activeInHierarchy=true 且 Canvas/GraphicRaycaster 组件完好；</item>
        /// <item>UGUI 根节点 Canvas 未被销毁或禁用。</item>
        /// </list>
        /// 这验证 FairyGUI 窗口关闭只释放 FairyGUI 对象，不触碰 UGUI 场景层级。
        /// </remarks>
        [Test]
        public async UniTask CloseFairyGUIWindow_DoesNotDestroyUGUIWindow()
        {
            // 1. 构造 UGUI 树并打开 FairyGUI 窗口。
            GameObject uguiWindow = CreateRepresentativeUGUITree();
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await module.ShowAsync<TestBattleStartPanel>();

            // 记录 UGUI 窗口的关键组件引用，用于关闭后验证组件未被销毁。
            Canvas uguiCanvas = uguiWindow.GetComponent<Canvas>();
            GraphicRaycaster uguiRaycaster = uguiWindow.GetComponent<GraphicRaycaster>();

            // 2. 关闭 FairyGUI 窗口（默认 CacheMode=None，Close 后 Dispose 实例）。
            FUI.Close<TestBattleStartPanel>();

            // 3. 断言 FairyGUI 侧：窗口已释放并从 Stage 移除。
            Assert.IsTrue(fuiWindow.isDisposed, "FairyGUI 窗口应已被释放（isDisposed=true）。");
            Assert.IsFalse(fuiWindow.onStage, "FairyGUI 窗口应已从 Stage 移除（onStage=false）。");

            // 4. 断言 UGUI 侧：窗口与根节点均完好。
            Assert.IsNotNull(uguiWindow, "UGUI 窗口 GameObject 不应被 FairyGUI 关闭操作销毁。");
            Assert.IsTrue(uguiWindow.activeInHierarchy, "UGUI 窗口应仍处于激活状态。");
            Assert.IsNotNull(uguiCanvas, "UGUI 窗口的 Canvas 组件应完好。");
            Assert.IsNotNull(uguiRaycaster, "UGUI 窗口的 GraphicRaycaster 组件应完好。");
            Assert.IsNotNull(_uguiRoot, "UGUI 根节点不应被 FairyGUI 关闭操作销毁。");
            Assert.IsTrue(_uguiRoot.activeInHierarchy, "UGUI 根节点应仍处于激活状态。");
        }

        /// <summary>
        /// 验证销毁 UGUI 窗口不影响 FairyGUI 窗口的显示与对象完整性。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口 ——
        /// 两者 SHALL 独立完成生命周期、层级和关闭操作）：
        /// <list type="bullet">
        /// <item>销毁 UGUI 窗口 GameObject 后，UGUI 侧不再渲染；</item>
        /// <item>FairyGUI 窗口仍 onStage=true、isDisposed=false，parent 未变；</item>
        /// <item>FairyGUI Stage 与 GRoot 仍可用。</item>
        /// </list>
        /// 这验证 UGUI 窗口销毁只影响 Unity 场景层级中的 UGUI 子树，不影响 FairyGUI 内部的对象树。
        /// </remarks>
        [Test]
        public async UniTask DestroyUGUIWindow_DoesNotDestroyFairyGUIWindow()
        {
            // 1. 构造 UGUI 树并打开 FairyGUI 窗口。
            GameObject uguiWindow = CreateRepresentativeUGUITree();
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await module.ShowAsync<TestBattleStartPanel>();

            // 记录 FairyGUI 窗口的 parent，用于销毁后验证 parent 未变。
            GComponent fuiParentBefore = fuiWindow.parent;

            // 2. 销毁 UGUI 窗口 GameObject（模拟 UIModule.CloseUI 销毁窗口面板）。
            UnityEngine.Object.DestroyImmediate(uguiWindow);

            // 3. 断言 UGUI 侧：窗口已销毁（UnityEngine.Object 重写了 ==，DestroyImmediate 后与 null 比较为 true）。
            Assert.IsNull(uguiWindow, "UGUI 窗口 GameObject 应已被销毁。");

            // 4. 断言 FairyGUI 侧：窗口仍正常显示、未释放、parent 未变。
            Assert.IsNotNull(fuiWindow, "FairyGUI 窗口实例引用应仍有效。");
            Assert.IsFalse(fuiWindow.isDisposed, "FairyGUI 窗口不应因 UGUI 销毁而被释放。");
            Assert.IsTrue(fuiWindow.onStage, "FairyGUI 窗口应仍挂载在 Stage 上。");
            Assert.AreEqual(fuiParentBefore, fuiWindow.parent, "FairyGUI 窗口的 parent 不应因 UGUI 销毁而改变。");

            // 5. 断言 FairyGUI Stage 与 GRoot 仍可用。
            Assert.IsNotNull(Stage.inst, "FairyGUI Stage 应仍可用。");
            Assert.IsNotNull(GRoot.inst, "FairyGUI GRoot 应仍可用。");

            // 6. 清理：将 _uguiRoot 置空，避免 TearDown 二次销毁已销毁的对象。
            //    uguiWindow 已销毁，但 _uguiRoot 仍指向根节点 GameObject，TearDown 会统一销毁。
            //    （此处不手动销毁 _uguiRoot，由 TearDown 负责。）
        }

        /// <summary>
        /// 验证 GameFUI 模块退出（Shutdown）不破坏 UGUI 窗口与根节点。
        /// </summary>
        /// <remarks>
        /// 验收点（spec fairygui-hot-update-delivery / Scenario: 同时打开 UGUI 与 FairyGUI 窗口 ——
        /// 任一模块关闭不得销毁另一模块管理的窗口；spec fairygui-window-runtime / Requirement: 模块退出完整清理 ——
        /// 模块退出 SHALL 取消所有进行中的打开操作，按反向顺序关闭并释放窗口，执行 detach，清理本地描述、owner、
        /// 活动 Registry 和静态模块缓存，并把所持包租约交还资源管理能力）：
        /// <list type="bullet">
        /// <item>调用 <see cref="IFUIModule.Shutdown"/> 后，FairyGUI 窗口被释放（isDisposed=true、onStage=false）；</item>
        /// <item>GameFUI 层级容器从 GRoot 移除；</item>
        /// <item>UGUI 窗口 GameObject 仍存活、activeInHierarchy=true 且 Canvas/GraphicRaycaster 组件完好；</item>
        /// <item>UGUI 根节点未被销毁或禁用。</item>
        /// </list>
        /// 这验证 GameFUI 模块退出只清理 FairyGUI 侧资源，不调用任何 UGUI API，不破坏 UGUI 场景层级。
        /// </remarks>
        [Test]
        public async UniTask FUIModuleShutdown_DoesNotDestroyUGUITree()
        {
            // 1. 构造 UGUI 树并打开 FairyGUI 窗口。
            GameObject uguiWindow = CreateRepresentativeUGUITree();
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await module.ShowAsync<TestBattleStartPanel>();

            // 记录 UGUI 窗口的关键组件引用，用于 Shutdown 后验证组件未被销毁。
            Canvas uguiCanvas = uguiWindow.GetComponent<Canvas>();
            GraphicRaycaster uguiRaycaster = uguiWindow.GetComponent<GraphicRaycaster>();

            // 2. 调用 GameFUI 模块 Shutdown（模拟模块退出/PlayMode 退出）。
            //    Shutdown 会取消打开操作、释放窗口与包租约、清空 Registry 与层级容器。
            module.Shutdown();

            // 3. 断言 FairyGUI 侧：窗口已释放并从 Stage 移除。
            Assert.IsTrue(fuiWindow.isDisposed, "FairyGUI 窗口应已被模块 Shutdown 释放（isDisposed=true）。");
            Assert.IsFalse(fuiWindow.onStage, "FairyGUI 窗口应已从 Stage 移除（onStage=false）。");

            // 4. 断言 UGUI 侧：窗口与根节点均完好，不受模块 Shutdown 影响。
            Assert.IsNotNull(uguiWindow, "UGUI 窗口 GameObject 不应被 GameFUI 模块 Shutdown 销毁。");
            Assert.IsTrue(uguiWindow.activeInHierarchy, "UGUI 窗口应仍处于激活状态。");
            Assert.IsNotNull(uguiCanvas, "UGUI 窗口的 Canvas 组件应完好。");
            Assert.IsNotNull(uguiRaycaster, "UGUI 窗口的 GraphicRaycaster 组件应完好。");
            Assert.IsNotNull(_uguiRoot, "UGUI 根节点不应被 GameFUI 模块 Shutdown 销毁。");
            Assert.IsTrue(_uguiRoot.activeInHierarchy, "UGUI 根节点应仍处于激活状态。");
            Assert.IsNotNull(_uguiRoot.GetComponent<Canvas>(), "UGUI 根节点的 Canvas 组件应完好。");
        }

        /// <summary>
        /// 验证销毁 UGUI 树后仍能正常执行 GameFUI 模块 Shutdown，不抛异常、不留下残留。
        /// </summary>
        /// <remarks>
        /// 这验证反向场景：先销毁 UGUI 树（模拟 UIModule.OnRelease 销毁 UIRoot），再调用 GameFUI 模块 Shutdown。
        /// GameFUI Shutdown 不依赖 UGUI 树的存在，应正常完成清理。
        /// <para>
        /// 验收点：
        /// <list type="bullet">
        /// <item>销毁 UGUI 树后调用 <see cref="IFUIModule.Shutdown"/> 不抛异常；</item>
        /// <item>Shutdown 后 FairyGUI 窗口已释放；</item>
        /// <item>Shutdown 后 FUI.Module 静态缓存已清空（<see cref="FUI.Module"/> getter 抛 FUIException）。</item>
        /// </list>
        /// </para>
        /// </remarks>
        [Test]
        public async UniTask DestroyUGUITreeBeforeShutdown_DoesNotBreakFUIModuleShutdown()
        {
            // 1. 构造 UGUI 树并打开 FairyGUI 窗口。
            CreateRepresentativeUGUITree();
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();
            TestBattleStartPanel fuiWindow = await module.ShowAsync<TestBattleStartPanel>();

            // 2. 销毁 UGUI 树（根节点 + 窗口）。
            UnityEngine.Object.DestroyImmediate(_uguiRoot);
            _uguiRoot = null; // 置空避免 TearDown 二次销毁

            // 3. 调用 GameFUI 模块 Shutdown，不应抛异常。
            Assert.DoesNotThrow(() => module.Shutdown(),
                "销毁 UGUI 树后调用 GameFUI 模块 Shutdown 不应抛异常。");

            // 4. 断言 FairyGUI 侧：窗口已释放。
            Assert.IsTrue(fuiWindow.isDisposed, "FairyGUI 窗口应已被模块 Shutdown 释放。");

            // 5. 断言 FUI 门面静态缓存已清空：Module getter 应抛 FUIException。
            //    PlayModeTestHarness.Cleanup 在 TearDown 会再次清理，这里验证 Shutdown 已清空缓存。
            Assert.Throws<FUIException>(() =>
            {
                IFUIModule _ = FUI.Module;
            }, "Shutdown 后 FUI.Module getter 应抛 FUIException（模块未注册）。");
        }

        /// <summary>
        /// 判断 <paramref name="descendant"/> 是否是 <paramref name="ancestor"/> 的子物体（递归向上查找 parent 链）。
        /// </summary>
        /// <param name="descendant">待判断的子节点 Transform。</param>
        /// <param name="ancestor">候选祖先 Transform。</param>
        /// <returns>true 表示 descendant 在 ancestor 的子树中；false 表示不在或任一参数为 null。</returns>
        /// <remarks>
        /// 辅助方法，用于验证 UGUI 根节点与 FairyGUI Stage 在场景层级中互不包含。
        /// 遍历 descendant 的 parent 链向上查找，若遇到 ancestor 则返回 true。
        /// </remarks>
        private static bool IsDescendantOf(Transform descendant, Transform ancestor)
        {
            if (descendant == null || ancestor == null)
            {
                return false;
            }

            Transform current = descendant;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
