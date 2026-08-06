using System;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using GameFUI.Tests.EditMode;
using NUnit.Framework;
using UnityEngine;
using UIBattle;

namespace GameFUI.Tests.PlayMode
{
    /// <summary>
    /// 任务 7.2 PlayMode 纵向验收：通过 <c>FUI.ShowAsync&lt;最终测试窗口&gt;()</c> 验证
    /// 返回最终类型、UICommon 图集在 Show 完成前已就绪、OwnerWindow 正确且 descriptor attach 已完成。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - design.md 决策10（Editor 测试装配流程）；
    /// - spec fairygui-window-runtime：
    ///   * Requirement: Show 公开接口和完成边界稳定——Show 成功 SHALL 表示包和依赖资源已就绪、
    ///     对象已构造、上下文与 Widget 已附加、同步创建/打开/刷新生命周期已执行且窗口处于 Open；
    ///   * Scenario: 直接打开最终测试业务窗口——调用方通过公开 Show 接口打开最终测试业务窗口，
    ///     系统 SHALL 通过已注册描述创建并返回最终测试业务类型；
    ///   * Requirement: 业务依赖通过可清理上下文附加——FUIModule SHALL 在对象构造完成后、
    ///     任何业务生命周期开始前调用 attach；
    ///   * Requirement: Widget 在生命周期前获得所属窗口——初始嵌套 Widget SHALL 在执行自身
    ///     OnCreate 前获得正确的 OwnerWindow。
    ///
    /// 测试装配：使用 <see cref="PlayModeTestHarness.SetupForShowAsync"/> 完成内存 provider 注入、
    /// UIBattleBinder 调用、最终测试 Widget/Window（带非空 creator）注册与 Registry 冻结。
    /// harness 不依赖生产 GameLogic 组合根，不创建 BattleModule。
    ///
    /// 复用证据：
    /// - 复用 <see cref="PlayModeTestHarness"/>（7.1 产出，含 SetupForShowAsync/Cleanup/EnsureGRootInitialized）；
    /// - 复用 <see cref="PlayModeTestHarness.SetupForShowAsync"/> 提供的带非空 creator 的最终测试 Window/Widget 注册
    ///   （harness 内调用 UIBattleBinder.BindAll 并以 CreateTypeBasedCreator 覆盖生成类型）；
    /// - 复用 <see cref="TestBattleStartPanel"/>（3.6 产出，最终测试业务窗口，含 TestWidget/HasGComponentButtonWidgetCoexistence）；
    /// - 复用 <see cref="TestBattleStartWidget"/>（3.6 产出，最终测试业务 Widget）；
    /// - 复用 <see cref="FUI.ShowAsync{T}"/> 公开入口（IFUIModule 5.1 产出）；
    /// - 复用 <see cref="PackageLoader.FindRecord"/> internal 静态方法（4.4 产出）与
    ///   <see cref="PackageRecord.State"/>/<see cref="PackageRecord.AssetHandles"/> internal 字段
    ///   （通过 InternalsVisibleTo("GameFUI.Tests") 暴露）验证 UICommon 图集就绪；
    /// - 复用 <see cref="FUIModule._windowEntries"/> internal 字段与 <see cref="WindowEntry"/> internal 类
    ///   （5.4 产出，通过 InternalsVisibleTo 暴露）验证 descriptor attach 完成；
    /// - 复用 <see cref="FUIWidget.IsAttached"/>/<see cref="FUIWidget.OwnerWindow"/> internal/public 属性
    ///   （6.1 产出）验证 OwnerWindow 正确与 attach 完成。
    ///
    /// 资源隔离：每个测试在 <see cref="SetUp"/> 中调用 <see cref="PlayModeTestHarness.Cleanup"/>
    /// 与 <see cref="PlayModeTestHarness.EnsureGRootInitialized"/>；<see cref="TearDown"/> 再调用
    /// <see cref="PlayModeTestHarness.Cleanup"/> 回到基线，避免跨测试残留。
    ///
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，只通过公开/internal API 访问被测对象。
    /// 不依赖 GameLogic/GamePlay/GameBattle，不创建或修改 BattleModule。
    /// </remarks>
    [TestFixture]
    public class ShowAsyncVerificationTests
    {
        /// <summary>
        /// 每个测试前的状态基线重置：清空全局状态并初始化 GRoot。
        /// </summary>
        /// <remarks>
        /// <see cref="PlayModeTestHarness.Cleanup"/> 幂等清空 FUI 模块、PackageLoader 注册表、
        /// FairyGUI 全局包注册表与 FUIObjectFactoryIntegration 活动 Registry。
        /// <see cref="PlayModeTestHarness.EnsureGRootInitialized"/> 保证 Stage/GRoot 在 EditMode/PlayMode 下可用，
        /// 与 <c>WindowLifecycleTests.SetUp</c> 的 GRoot 初始化对齐。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            PlayModeTestHarness.Cleanup();
            PlayModeTestHarness.EnsureGRootInitialized();
        }

        /// <summary>
        /// 每个测试后的状态基线重置，与 <see cref="SetUp"/> 对称。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            PlayModeTestHarness.Cleanup();
        }

        // ============================================================
        // 场景 a：FUI.ShowAsync<最终测试窗口>() 返回最终类型（不是基类）
        // ============================================================

        /// <summary>
        /// 验证 <c>FUI.ShowAsync&lt;TestBattleStartPanel&gt;()</c> 返回的实例类型为最终业务类型
        /// <see cref="TestBattleStartPanel"/>，而非生成基类 <see cref="UI_BattleStartPanel"/> 或 <see cref="FUIWindow"/>。
        /// </summary>
        /// <remarks>
        /// spec fairygui-window-runtime / Scenario: 业务类型覆盖生成类型——创建结果 SHALL 是注册的最末端业务类型。
        /// spec / Scenario: 直接打开最终测试业务窗口——调用方通过公开 Show 接口打开最终测试业务窗口，
        /// 系统 SHALL 通过已注册描述创建并返回最终测试业务类型。
        /// design.md 决策2：后注册的最终 creator 覆盖生成类型。
        /// </remarks>
        [Test]
        [Description("ShowAsync<TestBattleStartPanel>() 返回最终业务类型 TestBattleStartPanel，非生成基类。")]
        public async UniTask ShowAsync_ReturnsFinalBusinessType_NotGeneratedBase()
        {
            // 安排：装配模块（含带非空 creator 的最终测试 Window/Widget 注册）。
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();

            // 执行：通过公开 Show 入口打开最终测试窗口。
            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();

            // 断言：返回非空，且类型为最终业务类型。
            Assert.IsNotNull(window, "ShowAsync 应返回非空窗口实例。");
            Assert.IsInstanceOf<TestBattleStartPanel>(window,
                "ShowAsync 应返回最终业务类型 TestBattleStartPanel，而非生成基类 UI_BattleStartPanel。");
            Assert.IsFalse(window is UI_BattleStartPanel && window.GetType() == typeof(UI_BattleStartPanel),
                "返回类型不得是生成基类 UI_BattleStartPanel 本身。");

            // 断言：通过 FUI 门面 ShowAsync 重载同样返回最终业务类型。
            TestBattleStartPanel windowViaFacade = await FUI.ShowAsync<TestBattleStartPanel>();
            Assert.IsNotNull(windowViaFacade, "FUI.ShowAsync 门面应返回非空窗口。");
            Assert.IsInstanceOf<TestBattleStartPanel>(windowViaFacade,
                "FUI.ShowAsync 门面应返回最终业务类型 TestBattleStartPanel。");
            // 已是 Open 状态，同类型再次 Show 返回同一实例（合并创建任务）。
            Assert.AreSame(window, windowViaFacade,
                "同类型 Open 状态下再次 Show 应返回同一实例（合并加载与创建）。");
        }

        // ============================================================
        // 场景 b：UICommon 图集在 Show 完成前已就绪
        // ============================================================

        /// <summary>
        /// 验证 Show 完成时，UICommon 共享依赖包（含 atlas0 图集）已处于 Ready 状态，
        /// 且其外部图集 handle 已写入 <see cref="PackageRecord.AssetHandles"/>。
        /// </summary>
        /// <remarks>
        /// spec fairygui-window-runtime / Requirement: Show 公开接口和完成边界稳定——
        /// Show 成功 SHALL 表示包和依赖资源已就绪。
        /// spec fairygui-package-loading / Requirement: 包加载采用异步预载、同步解析——
        /// Acquire 成功代表资源已经可用于首屏构造。
        /// design.md 决策8：Acquire 流程并发预载包内贴图等外部资源，确保 Acquire 成功前全部进入 Ready。
        /// design.md 决策2：UICommon 作为共享图集依赖包，由其 owner 绑定。
        ///
        /// 本测试通过 <see cref="PackageLoader.FindRecord"/> 查询 UICommon 包记录，
        /// 验证其 <see cref="PackageRecord.State"/> 为 <see cref="PackageLoadState.Ready"/>，
        /// 且 <see cref="PackageRecord.AssetHandles"/> 包含 atlas0 外部图集 handle。
        /// 这证明 Show 在返回前已完成依赖包的描述加载、AddPackage、外部资源预载全部步骤，
        /// 窗口首屏可同步取得真实图集纹理，无需依赖异步占位回填。
        /// </remarks>
        [Test]
        [Description("Show 完成时 UICommon 包处于 Ready，atlas0 图集 handle 已写入 AssetHandles。")]
        public async UniTask ShowAsync_UICommonAtlasReady_BeforeShowCompletes()
        {
            // 安排：装配模块（provider 预设 UICommon_fui 描述与 UICommon_atlas0 纹理）。
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();

            // 执行：Show 最终测试窗口。
            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();

            // 断言：窗口已创建（间接证明 UIBattle 包及其依赖 UICommon 已 Acquire 成功）。
            Assert.IsNotNull(window, "ShowAsync 应返回非空窗口实例。");

            // 断言：UICommon 包记录存在且处于 Ready。
            PackageRecord uiCommonRecord = PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg);
            Assert.IsNotNull(uiCommonRecord,
                "Show 完成后 UICommon 包记录应存在（UIBattle 声明依赖 UICommon）。");
            Assert.AreEqual(PackageLoadState.Ready, uiCommonRecord.State,
                "Show 完成时 UICommon 包应处于 Ready，确保图集在首屏前已就绪。");

            // 断言：UICommon 的 UIPackage 已注册到 FairyGUI 全局包表。
            // PackageRecord.Package 在 AddPackage 成功后设置；Ready 状态下必非空。
            Assert.IsNotNull(uiCommonRecord.Package,
                "Show 完成时 UICommon 的 UIPackage 应已注册（Package 字段非空）。");
            Assert.IsNotNull(UIPackage.GetByName(PlayModeTestHarness.UICommonPkg),
                "Show 完成时 UICommon 应可通过 UIPackage.GetByName 查询到（已 AddPackage）。");

            // 断言：UICommon 外部图集 handle 已写入 AssetHandles。
            // harness 通过 CreateProviderWithUIBattleAndUICommon 预设 UICommon_atlas0 纹理，
            // PackageLoader.PreloadExternalAssetsAsync 按 Path.GetFileNameWithoutExtension(item.file)
            // 映射为 location（即 "UICommon_atlas0"）写入 AssetHandles。
            Assert.IsTrue(
                uiCommonRecord.AssetHandles.ContainsKey(PlayModeTestHarness.UICommonPkg + "_atlas0"),
                "Show 完成时 UICommon 的 atlas0 图集 handle 应已写入 AssetHandles，确保首屏可同步取得纹理。");

            // 断言：UIBattle 主包同样处于 Ready（UIBattle 无独立 atlas，使用 UICommon 共享图集）。
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            Assert.IsNotNull(uiBattleRecord,
                "Show 完成后 UIBattle 包记录应存在。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "Show 完成时 UIBattle 包应处于 Ready。");
        }

        // ============================================================
        // 场景 c：OwnerWindow 正确
        // ============================================================

        /// <summary>
        /// 验证窗口内受管理 Widget 的 <see cref="FUIWidget.OwnerWindow"/> 在 Show 完成时已正确设置为窗口实例，
        /// 且与窗口自身引用一致。
        /// </summary>
        /// <remarks>
        /// spec fairygui-window-runtime / Requirement: Widget 在生命周期前获得所属窗口——
        /// 初始嵌套 Widget SHALL 在执行自身 OnCreate 前获得正确的 OwnerWindow。
        /// design.md 决策6：窗口 XML 构造完成后，FUIModule 遍历初始组件树，为所有 FUIWidget 执行幂等 Attach，
        /// 再统一启动 Widget 创建生命周期。业务不得在构造函数或 ConstructFromXML 中依赖 OwnerWindow。
        /// 任务 6.1：实现初始 Widget 树的幂等 Attach，并保证 OwnerWindow 在 Widget.OnCreate 前可用。
        ///
        /// 本测试在 Show 完成后通过 <see cref="TestBattleStartPanel.TestWidget"/> 访问受管理 Widget，
        /// 验证其 OwnerWindow 指向 Show 返回的窗口实例，证明 attach 顺序正确。
        /// </remarks>
        [Test]
        [Description("Show 完成时受管理 Widget 的 OwnerWindow 指向窗口实例，且 Widget IsAttached 为 true。")]
        public async UniTask ShowAsync_OwnerWindowCorrect_ForManagedWidget()
        {
            // 安排：装配模块。
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();

            // 执行：Show 最终测试窗口。
            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();

            // 断言：窗口非空，且包含受管理 Widget（共存结构契约）。
            Assert.IsNotNull(window, "ShowAsync 应返回非空窗口实例。");
            Assert.IsTrue(window.HasGComponentButtonWidgetCoexistence(),
                "窗口应包含普通 GComponent、原生 Button 与受管理 Widget 三者共存结构。");

            // 断言：受管理 Widget 的 OwnerWindow 指向 Show 返回的窗口实例。
            TestBattleStartWidget widget = window.TestWidget;
            Assert.IsNotNull(widget, "受管理 Widget 应非空（已由覆盖注册创建为最终业务类型）。");
            Assert.IsNotNull(widget.OwnerWindow,
                "Show 完成时受管理 Widget 的 OwnerWindow 应已设置（attach 在 OnCreate 前完成）。");
            Assert.AreSame(window, widget.OwnerWindow,
                "受管理 Widget 的 OwnerWindow 应与 Show 返回的窗口实例相同。");

            // 断言：Widget 已完成幂等 Attach（IsAttached 为 true）。
            Assert.IsTrue(widget.IsAttached,
                "Show 完成时受管理 Widget 应已 IsAttached（幂等 Attach 在 OnCreate 前执行）。");
        }

        // ============================================================
        // 场景 d：descriptor attach 已完成
        // ============================================================

        /// <summary>
        /// 验证 Show 完成时，窗口的实例生命周期 attach 链已完成：
        /// <c>AttachContext -> Descriptor.Attach -> AttachWidgetTree -> OnCreate</c> 全部执行，
        /// 窗口处于 Open 状态，<see cref="FUIWindow.IsCreated"/> 为 true。
        /// </summary>
        /// <remarks>
        /// spec fairygui-window-runtime / Requirement: 业务依赖通过可清理上下文附加——
        /// FUIModule SHALL 在对象构造完成后、任何业务生命周期开始前调用 attach。
        /// spec / Scenario: Attach 发生在 OnCreate 前——窗口 SHALL 在 OnCreate 前取得运行时上下文
        /// 和描述提供的业务依赖。
        /// design.md 决策5：首次创建 AttachContext -> Descriptor.Attach -> AttachWidgetTree -> OnCreate。
        ///
        /// 本测试通过 <see cref="WindowEntry.IsInstanceLifecycleAttached"/> internal 字段验证 attach 链完成，
        /// 通过 <see cref="FUIWindow.IsCreated"/> 验证 OnCreate 已执行，
        /// 通过 <see cref="WindowEntry.State"/> 验证窗口处于 Open。
        /// </remarks>
        [Test]
        [Description("Show 完成时 descriptor attach 链已完成，IsCreated 为 true，窗口处于 Open。")]
        public async UniTask ShowAsync_DescriptorAttachChainCompleted_BeforeShowReturns()
        {
            // 安排：装配模块。
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();

            // 执行：Show 最终测试窗口。
            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();

            // 断言：窗口非空。
            Assert.IsNotNull(window, "ShowAsync 应返回非空窗口实例。");

            // 断言：窗口的 OnCreate 已执行（IsCreated 为 true）。
            // IsCreated 在 InvokeOnCreate 中置 true，证明 attach 链已推进到 OnCreate。
            Assert.IsTrue(window.IsCreated,
                "Show 完成时窗口的 OnCreate 应已执行（IsCreated=true），证明 attach 链已完成。");

            // 断言：WindowEntry.IsInstanceLifecycleAttached 为 true。
            // 该字段在 OnCreate 完成后置 true（FUIModule.ExecuteLoadAndOpenAsync 第 6 步后），
            // 证明 AttachContext -> Descriptor.Attach -> AttachWidgetTree -> OnCreate 全部完成。
            Assert.IsTrue(
                module._windowEntries.TryGetValue(typeof(TestBattleStartPanel), out WindowEntry entry),
                "Show 完成后应存在 TestBattleStartPanel 的 WindowEntry。");
            Assert.IsNotNull(entry, "WindowEntry 应非空。");
            Assert.IsTrue(entry.IsInstanceLifecycleAttached,
                "Show 完成时 WindowEntry.IsInstanceLifecycleAttached 应为 true，" +
                "证明 AttachContext -> Descriptor.Attach -> AttachWidgetTree -> OnCreate 已全部完成。");

            // 断言：窗口状态为 Open。
            Assert.AreEqual(FUIWindowState.Open, entry.State,
                "Show 完成时窗口应处于 Open 状态。");

            // 断言：WindowEntry 的实例引用与 Show 返回的窗口一致。
            Assert.AreSame(window, entry.Window,
                "WindowEntry.Window 应与 Show 返回的窗口实例相同。");

            // 断言：WindowEntry 持有非空包租约（attach 完成后实例持有包租约）。
            Assert.IsNotNull(entry.Lease,
                "Show 完成时 WindowEntry 应持有非空包租约（attach 链完成后实例持有 UIBattle 包租约）。");
        }

        // ============================================================
        // 场景 e：综合验证——Show 完成边界四要素同时满足
        // ============================================================

        /// <summary>
        /// 综合验证 Show 完成边界的四要素同时满足：返回最终类型、UICommon 图集就绪、
        /// OwnerWindow 正确、descriptor attach 已完成。
        /// </summary>
        /// <remarks>
        /// spec fairygui-window-runtime / Requirement: Show 公开接口和完成边界稳定——
        /// Show 成功 SHALL 表示包和依赖资源已就绪、对象已构造、上下文与 Widget 已附加、
        /// 同步创建/打开/刷新生命周期已执行且窗口处于 Open。
        /// 本测试将四要素聚合在单次 Show 中验证，确保完成边界契约在 PlayMode 下整体成立。
        /// </remarks>
        [Test]
        [Description("Show 完成边界四要素同时满足：最终类型/UICommon 就绪/OwnerWindow/attach 完成。")]
        public async UniTask ShowAsync_CompletionBoundary_AllFourElementsSatisfied()
        {
            // 安排：装配模块。
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();

            // 执行：Show 最终测试窗口。
            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();

            // 断言 1：返回最终类型。
            Assert.IsNotNull(window, "ShowAsync 应返回非空窗口实例。");
            Assert.IsInstanceOf<TestBattleStartPanel>(window,
                "ShowAsync 应返回最终业务类型 TestBattleStartPanel。");

            // 断言 2：UICommon 图集就绪。
            PackageRecord uiCommonRecord = PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg);
            Assert.IsNotNull(uiCommonRecord, "UICommon 包记录应存在。");
            Assert.AreEqual(PackageLoadState.Ready, uiCommonRecord.State,
                "UICommon 包应处于 Ready。");
            Assert.IsTrue(
                uiCommonRecord.AssetHandles.ContainsKey(PlayModeTestHarness.UICommonPkg + "_atlas0"),
                "UICommon atlas0 图集 handle 应已写入 AssetHandles。");

            // 断言 3：OwnerWindow 正确。
            Assert.IsTrue(window.HasGComponentButtonWidgetCoexistence(),
                "窗口应包含共存结构（GComponent/Button/Widget）。");
            TestBattleStartWidget widget = window.TestWidget;
            Assert.IsNotNull(widget, "受管理 Widget 应非空。");
            Assert.AreSame(window, widget.OwnerWindow,
                "受管理 Widget 的 OwnerWindow 应指向窗口实例。");
            Assert.IsTrue(widget.IsAttached, "受管理 Widget 应已 IsAttached。");

            // 断言 4：descriptor attach 已完成。
            Assert.IsTrue(window.IsCreated, "窗口 IsCreated 应为 true。");
            Assert.IsTrue(
                module._windowEntries.TryGetValue(typeof(TestBattleStartPanel), out WindowEntry entry),
                "应存在 WindowEntry。");
            Assert.IsTrue(entry.IsInstanceLifecycleAttached,
                "WindowEntry.IsInstanceLifecycleAttached 应为 true。");
            Assert.AreEqual(FUIWindowState.Open, entry.State,
                "窗口应处于 Open。");
            Assert.IsNotNull(entry.Lease, "WindowEntry 应持有非空包租约。");
        }
    }
}
