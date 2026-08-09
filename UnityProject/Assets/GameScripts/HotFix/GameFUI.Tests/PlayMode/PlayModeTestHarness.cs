using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using GameFUI.Tests.EditMode;
using GameLogic;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UIBattle;

namespace GameFUI.Tests.PlayMode
{
    /// <summary>
    /// PlayMode 测试 harness：为 Editor PlayMode 纵向验收（任务 7.x）提供统一的 GameFUI 模块装配入口。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策10 与 spec fairygui-window-runtime / Scenario: 测试 owner 注册 UIBattle。
    /// <para>
    /// 本 harness 严格遵循 design.md 决策10 的装配流程：
    /// <code>
    /// FUI.RegisterModule(editorResource, options)
    ///   -&gt; 注册 UICommon 基础 Binder（UICommonBinder.BindAll()，当前为空实现，由生产组合根或测试装配方调用；
    ///        本 change 中 FUIModule 不在内部自动绑定 UICommon Binder，需由装配方显式调用——见
    ///        <see cref="CreateProviderWithUIBattleAndUICommon"/> 的 UICommon 预设说明）
    ///   -&gt; 测试 owner 调用 UIBattleBinder（通过 TestFUIOwner.RegisterUIBattle）
    ///   -&gt; 注册最终测试 Widget/Window 与 attach/detach（TestFUIOwner.RegisterUIBattle 已完成）
    ///   -&gt; FUI.Module.FreezeBindings()
    ///   -&gt; FUI.ShowAsync&lt;最终测试窗口&gt;()
    /// </code>
    /// </para>
    /// <para>
    /// <b>显式注册资源能力</b>：使用 <see cref="InMemoryFUIResourceProvider"/>（4.3 产出，通过
    /// <c>InternalsVisibleTo("GameFUI.Tests")</c> 暴露）注入可控内存资源能力，预设 UIBattle 与 UICommon
    /// 包描述资源及外部图集资源。内存 provider 与真实 <see cref="TEngine.IResourceModule"/> 同构，
    /// 支持 <see cref="IFUIResourceProvider.LoadAssetAsyncHandle{TAsset}"/>、
    /// <see cref="IFUIResourceProvider.CheckLocationValid"/> 与 <see cref="IFUIResourceProvider.HasAsset"/>，
    /// 使包加载在 Editor PlayMode 内完整闭环，无需依赖真实 YooAsset 初始化或生产 GameLogic 组合根。
    /// </para>
    /// <para>
    /// <b>显式注册选项</b>：通过 <see cref="FUIOptions"/> 显式传入包卸载策略与延迟时间。
    /// 默认使用 <see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>，与 design.md 决策8 首版允许的策略一致；
    /// 调用方可按场景需要传入 <see cref="FUIPackageUnloadPolicy.Delayed"/> 验证 6.4 的延迟卸载策略。
    /// </para>
    /// <para>
    /// <b>调用 UIBattle Binder</b>：由 <see cref="TestFUIOwner.RegisterUIBattle"/> 在注册阶段先调用
    /// <c>UIBattleBinder.BindAll()</c>，把生成类型 URL 注册到全局 <c>UIObjectFactory</c>，
    /// 再注册最终测试 Widget 与 Window 覆盖生成类型（spec：测试 owner 注册 UIBattle——先调用 Binder，
    /// 再注册业务 Widget 和 Window）。本 harness 不重复调用 Binder，避免破坏每包唯一 owner 契约。
    /// </para>
    /// <para>
    /// <b>由测试 owner 注册最终测试 Window/Widget 后冻结 Registry</b>：装配流程的注册阶段由
    /// <see cref="TestFUIOwner.RegisterUIBattle"/> 完成本包（UIBattle）的 owner 域注册，
    /// 包括最终测试 Widget（<see cref="TestBattleStartWidget"/>）与最终测试 Window（<see cref="TestBattleStartPanel"/>）。
    /// 注册完成后由本 harness 调用 <see cref="FUIModule.FreezeBindings"/> 冻结注册表，
    /// 冻结后新增或冲突注册直接报错（design.md 决策2；spec：重复或冲突注册）。
    /// </para>
    /// <para>
    /// <b>不得依赖生产 GameLogic 组合根</b>：本 harness 不调用 <c>ModuleSystem.RegisterModule</c>，
    /// 不引用 GameLogic/GamePlay/GameBattle 命名空间，不创建 BattleModule 或业务 Module。
    /// 资源能力通过 internal 测试入口 <see cref="FUI.RegisterModuleForTesting"/> 直接注入内存 provider，
    /// 绕过 <see cref="TEngine.IResourceModule"/> 包装，避免需要真实 YooAsset 初始化
    /// （design.md 决策1：仅通过 internal 测试入口为测试程序集注入可控失败/取消的内存 provider）。
    /// </para>
    /// <para>
    /// <b>复用证据</b>：
    /// <list type="bullet">
    /// <item>复用 <see cref="InMemoryFUIResourceProvider"/>（4.3 产出，通过 InternalsVisibleTo 暴露）；</item>
    /// <item>复用磁盘上真实发布的 <c>UIBattle_fui.bytes</c> 与 <c>UICommon_fui.bytes</c> 描述资源
    ///   （1.1 核对的规范资源清单），通过 <see cref="AssetDatabase.LoadAssetAtPath"/> 加载，
    ///   保留二进制字节完整性（与 <c>WindowLifecycleTests.CreateProviderWithLifecyclePackages</c> 一致）；</item>
    /// <item>复用 <see cref="TestFUIOwner"/>（3.6 产出，owner 注册入口，含 UIBattleBinder 调用）；</item>
    /// <item>复用 <see cref="TestBattleStartPanel"/> / <see cref="TestBattleStartWidget"/>（3.6 产出，最终测试业务类型）；</item>
    /// <item>复用 <see cref="FUI.RegisterModuleForTesting"/>（5.1 产出的 internal 测试入口）；</item>
    /// <item>复用 <see cref="FUIModule.BindingRegistry"/> / <see cref="FUIModule.FreezeBindings"/> 等 internal/public API。</item>
    /// </list>
    /// 装配模式与 EditMode <c>WindowLifecycleTests.SetupModuleWithLifecycleWindow</c> 一致，
    /// 保证 PlayMode 与 EditMode 使用完全相同的注册契约（design.md Risks：测试 owner 与未来真实业务 Module
    /// 存在装配差异 → 测试 owner 使用与未来业务 owner 完全相同的 Binder、Descriptor、Freeze 和 Show API）。
    /// </para>
    /// <para>
    /// <b>资源隔离</b>：每次 <see cref="Setup"/> 前调用方应先调用 <see cref="Cleanup"/> 清空 FairyGUI 全局包注册表、
    /// <see cref="PackageLoader"/> 静态注册表与 FUI 门面静态缓存，避免跨测试残留。
    /// <see cref="Cleanup"/> 幂等，可在 SetUp/TearDown 中重复调用。
    /// </para>
    /// <para>
    /// 边界约束：本 harness 不修改任何 Runtime/Resource .cs 文件，只通过公开/internal API 访问被测对象。
    /// 不依赖 GameLogic/GamePlay/GameBattle，不创建或修改 BattleModule。
    /// </para>
    /// </remarks>
    public static class PlayModeTestHarness
    {
        /// <summary>
        /// UIBattle 包名常量，与生成类型 <see cref="UI_BattleStartPanel"/> 的 <c>PkgName</c> 一致。
        /// </summary>
        /// <remarks>
        /// 用于定位真实描述资源与外部图集资源的 location 前缀（spec fairygui-package-loading：
        /// 包名与资源 location 使用统一规则——描述 location 为 <c>{PackageName}_fui</c>，
        /// 外部资源 location 使用包名前缀 + 文件名）。
        /// </remarks>
        public const string UIBattlePkg = "UIBattle";

        /// <summary>
        /// UICommon 共享依赖包名常量。
        /// </summary>
        /// <remarks>
        /// UICommon 作为 UIBattle 的共享图集依赖包（design.md 决策2：共享包同样必须有唯一 owner）。
        /// 本 change 中 UICommon 的 <c>UICommonBinder.BindAll()</c> 为空实现（无受管理组件），
        /// 且 FUIModule 在构造/注册阶段不自动调用 UICommonBinder.BindAll()；由生产组合根或测试装配方按需显式调用。
        /// PlayMode 测试 harness 只预设 UICommon 描述资源使依赖加载成功，不调用其 Binder（空实现，无受管理组件可注册）。
        /// </remarks>
        public const string UICommonPkg = "UICommon";

        /// <summary>
        /// 装配 GameFUI 模块并完成 UIBattle 测试 owner 注册与 Registry 冻结，返回已就绪的 <see cref="FUIModule"/>。
        /// </summary>
        /// <param name="options">
        /// 注册选项，为 null 时使用默认策略（<see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>）。
        /// 调用方可传入 <see cref="FUIPackageUnloadPolicy.Delayed"/> 验证 6.4 的延迟卸载策略。
        /// </param>
        /// <param name="loadDelayMs">
        /// 内存 provider 加载延迟毫秒数，0 表示立即完成。用于模拟异步时序以验证并发 Show、调用方取消、
        /// 加载期间 Close 等场景（与 <c>WindowLifecycleTests</c> 的 loadDelayMs 参数语义一致）。
        /// </param>
        /// <returns>已完成 <see cref="FUIModule.FreezeBindings"/> 的模块实例，可直接调用 <see cref="FUI.ShowAsync{T}"/>。</returns>
        /// <remarks>
        /// 装配流程严格遵循 design.md 决策10：
        /// <list type="number">
        /// <item><see cref="Cleanup"/>：清空全局状态，避免上一测试残留（调用方通常在 SetUp 已调用，此处幂等保护）；</item>
        /// <item><see cref="CreateProviderWithUIBattleAndUICommon"/>：构造内存 provider 并预设 UIBattle/UICommon 描述与图集资源；</item>
        /// <item><see cref="FUI.RegisterModuleForTesting"/>：显式注册资源能力（内存 provider）与选项（FUIOptions）；</item>
        /// <item><see cref="TestFUIOwner.RegisterUIBattle"/>：由测试 owner 调用 UIBattleBinder 并注册最终测试 Widget/Window；</item>
        /// <item><see cref="FUIModule.FreezeBindings"/>：冻结 Registry，安装全局无状态 creator，建立层级容器。</item>
        /// </list>
        /// 冻结后调用方可通过 <c>FUI.ShowAsync&lt;TestBattleStartPanel&gt;()</c> 直接打开最终测试业务窗口
        /// （spec：直接打开最终测试业务窗口——测试 owner 完成注册且 Registry 已冻结后，调用方通过公开 Show 接口打开）。
        /// </remarks>
        /// <example>
        /// <code>
        /// // Editor PlayMode 测试典型用法：
        /// [SetUp] public void SetUp() { PlayModeTestHarness.Cleanup(); PlayModeTestHarness.EnsureGRootInitialized(); }
        /// [TearDown] public void TearDown() { PlayModeTestHarness.Cleanup(); }
        /// [Test] public async UniTask ShowUIBattle_ReturnsTestWindow()
        /// {
        ///     FUIModule module = PlayModeTestHarness.Setup();
        ///     TestBattleStartPanel window = await module.ShowAsync&lt;TestBattleStartPanel&gt;();
        ///     Assert.IsNotNull(window);
        ///     Assert.IsTrue(window.HasGComponentButtonWidgetCoexistence());
        /// }
        /// </code>
        /// </example>
        public static FUIModule Setup(FUIOptions options = null, int loadDelayMs = 0)
        {
            // 1. 幂等清空全局状态，避免上一测试残留污染装配。
            //    调用方通常在 SetUp 已调用 Cleanup，此处再调一次保证装配起点干净。
            Cleanup();

            // 2. 构造内存 provider，预设 UIBattle（依赖 UICommon）与 UICommon 包描述及外部图集资源。
            //    内存 provider 与真实 IResourceModule 同构，使包加载在 Editor PlayMode 内完整闭环。
            InMemoryFUIResourceProvider provider = CreateProviderWithUIBattleAndUICommon(loadDelayMs);

            // 3. 显式注册资源能力与选项：通过 internal 测试入口直接注入内存 provider，
            //    绕过 IResourceModule 包装，避免需要真实 YooAsset 初始化。
            //    重复注册保护由 FUI.RegisterModuleForTesting 内部完成（design.md 决策1）。
            //    RegisterModuleForTesting 构造 FUIModule 实例并存入 FUI._module（类型为 IFUIModule），
            //    此处取回后强转为 FUIModule 以访问 internal BindingRegistry。
            FUI.RegisterModuleForTesting(provider, options);
            FUIModule module = (FUIModule)FUI.Module;

            // 4. 由测试 owner 调用 UIBattleBinder 并注册最终测试 Widget/Window。
            //    TestFUIOwner.RegisterUIBattle 内部先调用 UIBattleBinder.BindAll()（生成类型 URL 注册到全局工厂），
            //    再注册 TestBattleStartWidget 与 TestBattleStartPanel 覆盖生成类型
            //    （spec：测试 owner 注册 UIBattle——先调用 Binder，再注册业务 Widget 和 Window）。
            //    本 harness 不重复调用 Binder，避免破坏每包唯一 owner 契约。
            //    BindingRegistry 为 internal，通过 InternalsVisibleTo("GameFUI.Tests") 暴露给测试程序集。
            TestFUIOwner.RegisterUIBattle(module.BindingRegistry);

            // 5. 冻结 Registry：冻结后新增注册直接报错；同时安装全局无状态 creator 并建立层级容器。
            //    冻结是首次创建任何受管理对象前的必要条件（design.md 决策2；spec：显式注册窗口和 Widget）。
            //    FreezeBindings 在 IFUIModule 公开接口上，可直接通过接口调用。
            module.FreezeBindings();

            return module;
        }

        /// <summary>
        /// 装配 GameFUI 模块并使用默认 KeepUntilShutdown 策略，返回已就绪的 <see cref="FUIModule"/>。
        /// </summary>
        /// <returns>已完成 FreezeBindings 的模块实例。</returns>
        /// <remarks>
        /// 便捷重载，等价于 <c>Setup(options: null, loadDelayMs: 0)</c>。
        /// 用于不需要自定义卸载策略或加载延迟的常规 PlayMode 验收场景（如任务 7.2 的基本 Show 验证）。
        /// </remarks>
        public static FUIModule Setup()
        {
            return Setup(null, 0);
        }

        /// <summary>
        /// 装配 GameFUI 模块并使用指定的延迟卸载策略，用于验证 6.4 的 Delayed 卸载策略。
        /// </summary>
        /// <param name="unloadPolicy">包卸载策略。</param>
        /// <param name="unloadDelaySeconds">Delayed 策略下零引用后的延迟卸载时间（秒）。</param>
        /// <param name="loadDelayMs">内存 provider 加载延迟毫秒数，用于异步时序模拟。</param>
        /// <returns>已完成 FreezeBindings 的模块实例。</returns>
        /// <remarks>
        /// 用于任务 7.5 的 KeepUntilShutdown 与 Delayed 策略对比验收
        /// （spec fairygui-package-loading：包租约控制缓存和卸载；design.md 决策8）。
        /// </remarks>
        public static FUIModule SetupWithUnloadPolicy(
            FUIPackageUnloadPolicy unloadPolicy,
            float unloadDelaySeconds = 5f,
            int loadDelayMs = 0)
        {
            FUIOptions options = new FUIOptions
            {
                UnloadPolicy = unloadPolicy,
                UnloadDelaySeconds = unloadDelaySeconds,
            };
            return Setup(options, loadDelayMs);
        }

        /// <summary>
        /// 装配 GameFUI 模块并使用带无状态 creator 的最终测试 Window/Widget 注册，
        /// 使 <see cref="FUI.ShowAsync{T}"/> 能成功创建最终业务类型实例。
        /// </summary>
        /// <param name="options">注册选项，为 null 时使用默认策略。</param>
        /// <param name="loadDelayMs">内存 provider 加载延迟毫秒数。</param>
        /// <returns>已完成 FreezeBindings 的模块实例。</returns>
        /// <remarks>
        /// 本方法是 <see cref="Setup(FUIOptions, int)"/> 的 ShowAsync 就绪变体。
        /// <para>
        /// <b>为何不使用 <see cref="TestFUIOwner.RegisterUIBattle"/></b>：
        /// <see cref="TestFUIOwner.RegisterUIBattle"/> 注册的描述使用 <c>creator: null</c>，
        /// 适用于 Registry/绑定契约验证（任务 3.6/3.7），但 <see cref="FUIObjectFactoryIntegration"/>
        /// 安装的全局无状态 creator 在被调用时会查询 Registry 描述并执行 <c>descriptor.Creator(url)</c>，
        /// null creator 会导致 <see cref="FUI.ShowAsync{T}"/> 在 CreateObjectFromURL 阶段抛 NRE。
        /// 因此 ShowAsync 就绪路径需要非空 creator。
        /// </para>
        /// <para>
        /// <see cref="TestFUIOwner"/> 为 EditMode 程序集的测试类型，不在本 harness 可修改范围内
        /// （本 change 禁止修改 EditMode 测试文件），且其 <c>RegisterUIBattle</c> 无带非空 creator 的重载。
        /// 故本方法不能通过 <see cref="TestFUIOwner.RegisterUIBattle"/> 完成注册，需在 harness 内自行实现
        /// 带非空 creator 的等价注册入口。注册内容与 owner 契约与 <see cref="TestFUIOwner.RegisterUIBattle"/>
        /// 完全一致（同 owner 类型、同 URL、同包名、同顺序），唯一差异是提供非空 creator 使 ShowAsync 闭环可用。
        /// </para>
        /// <para>
        /// 本方法遵循与 <c>WindowLifecycleTests.SetupModuleWithLifecycleWindow</c> 相同的模式：
        /// 先调用 <c>UIBattleBinder.BindAll()</c>（与 <see cref="TestFUIOwner.RegisterUIBattle"/> 内部一致），
        /// 再使用 <see cref="CreateTypeBasedCreator"/> 注册带非空 creator 的最终测试 Widget（<see cref="TestBattleStartWidget"/>）
        /// 与最终测试 Window（<see cref="TestBattleStartPanel"/>），覆盖生成类型，使创建结果为最终业务类型
        /// （spec：业务类型覆盖生成类型）。
        /// </para>
        /// <para>
        /// 用于任务 7.2+ 的 ShowAsync 纵向验收（spec：直接打开最终测试业务窗口——调用方通过公开 Show 接口打开）。
        /// </para>
        /// </remarks>
        public static FUIModule SetupForShowAsync(
            FUIOptions options = null,
            int loadDelayMs = 0,
            bool fullScreen = false,
            FUICacheMode cacheMode = FUICacheMode.None,
            FUISafeAreaMode safeAreaMode = FUISafeAreaMode.Full,
            FUILayer layer = FUILayer.Normal,
            Func<Rect> safeAreaProvider = null)
        {
            // 1. 幂等清空全局状态。
            Cleanup();

            // 2. 构造内存 provider，预设 UIBattle/UICommon 描述与图集资源。
            //    使用 AssetDatabase 加载真实 .bytes 描述（含组件项），使 CreateObjectFromURL 可创建真实实例。
            InMemoryFUIResourceProvider provider = CreateProviderWithUIBattleAndUICommon(loadDelayMs);

            // 3. 显式注册资源能力与选项。
            FUI.RegisterModuleForTesting(provider, options, safeAreaProvider);
            FUIModule module = (FUIModule)FUI.Module;

            // 4. 调用 UIBattle Binder：注册生成类型 URL 到全局 UIObjectFactory。
            //    不能直接调用 TestFUIOwner.RegisterUIBattle：它使用 creator:null，会导致 ShowAsync 抛 NRE
            //    （详见方法 docstring）。TestFUIOwner 不在可修改范围内且无带非空 creator 的重载，
            //    故在 harness 内实现等价注册（同 owner、同 URL、同顺序，唯一差异为非空 creator）。
            //    第一步 Binder 与 TestFUIOwner.RegisterUIBattle 内部第一步一致。
            UIBattleBinder.BindAll();

            // 5. 注册最终测试 Widget（覆盖生成类型，Creator 非空）。
            //    绑定顺序与 TestFUIOwner.RegisterUIBattle 一致：先 Widget 再 Window。
            module.BindingRegistry.Register(new FUIDescriptor(
                url: UI_BattleStartWidget.URL,
                packageName: UI_BattleStartWidget.PkgName,
                componentName: UI_BattleStartWidget.ResName,
                ownerType: TestFUIOwner.OwnerType,
                targetType: typeof(TestBattleStartWidget),
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: CreateTypeBasedCreator(typeof(TestBattleStartWidget))));

            // 6. 注册最终测试 Window（覆盖生成类型，Creator 非空）。
            module.BindingRegistry.Register(new FUIDescriptor(
                url: UI_BattleStartPanel.URL,
                packageName: UI_BattleStartPanel.PkgName,
                componentName: UI_BattleStartPanel.ResName,
                ownerType: TestFUIOwner.OwnerType,
                targetType: typeof(TestBattleStartPanel),
                layer: layer,
                fullScreen: fullScreen,
                cacheMode: cacheMode,
                safeAreaMode: safeAreaMode,
                creator: CreateTypeBasedCreator(typeof(TestBattleStartPanel))));

            // 7. 冻结 Registry：安装全局无状态 creator 并建立层级容器。
            module.FreezeBindings();

            return module;
        }

        /// <summary>
        /// 使用真实 <see cref="IResourceModule"/> 装配 GameFUI 模块，通过公开
        /// <see cref="FUI.RegisterModule(IResourceModule, FUIOptions)"/> 注册真实资源能力
        /// （由 FUIModule 内部包装为 <see cref="YooAssetFUIResourceProvider"/>），并完成 UIBattle 测试
        /// owner 注册与 Registry 冻结，返回可直接调用 <see cref="FUI.ShowAsync{T}"/> 的模块实例。
        /// </summary>
        /// <param name="resourceModule">
        /// 真实资源模块（如 YooAsset EditorSimulateMode 初始化后的 ResourceModule 实例），不得为 null。
        /// </param>
        /// <param name="options">注册选项，为 null 时使用默认策略（KeepUntilShutdown）。</param>
        /// <returns>已完成 <see cref="FUIModule.FreezeBindings"/> 的模块实例。</returns>
        /// <remarks>
        /// <para>
        /// <b>与 <see cref="SetupForShowAsync"/> 的唯一差异</b>：资源能力来源不同。
        /// <see cref="SetupForShowAsync"/> 通过 internal <see cref="FUI.RegisterModuleForTesting"/> 注入内存 provider，
        /// 而本方法通过公开 <see cref="FUI.RegisterModule(IResourceModule, FUIOptions)"/> 注入真实 IResourceModule，
        /// 由 FUIModule 内部包装为 <see cref="YooAssetFUIResourceProvider"/>（design.md 决策1）。
        /// Binder、Descriptor 注册、Freeze 流程与 <see cref="SetupForShowAsync"/> 完全一致，复用
        /// <see cref="CreateTypeBasedCreator"/> 与 <c>UIBattleBinder.BindAll()</c>。
        /// </para>
        /// <para>
        /// 用于任务 8.4：在真实 YooAsset EditorSimulateMode 下完成 ShowAsync 纵向闭环验收
        /// （spec fairygui-hot-update-delivery / Scenario: YooAsset Editor 模式寻址——
        /// 结果 SHALL 与内存资源适配器测试的类型和生命周期契约一致）。
        /// 本方法不得用 <see cref="InMemoryFUIResourceProvider"/> 或 CheckLocationValid 替代真实加载
        /// （任务 8.4 明令禁止）。
        /// </para>
        /// </remarks>
        public static FUIModule SetupWithRealYooAsset(IResourceModule resourceModule, FUIOptions options = null)
        {
            // 1. 幂等清空全局状态，避免上一测试残留污染装配。
            Cleanup();

            // 2. 通过公开 RegisterModule(IResourceModule) 注册真实资源能力。
            //    FUIModule 内部把 IResourceModule 包装为 YooAssetFUIResourceProvider（design.md 决策1）。
            //    重复注册保护由 FUI.RegisterModule 内部完成（design.md 决策1）。
            //    此处走生产同构的公开注册入口，而非 internal RegisterModuleForTesting，
            //    使资源加载链路为 真实 IResourceModule -> YooAssetFUIResourceProvider -> YooAsset，
            //    而非内存 provider。
            FUI.RegisterModule(resourceModule, options);
            FUIModule module = (FUIModule)FUI.Module;

            // 3. 调用 UIBattle Binder：注册生成类型 URL 到全局 UIObjectFactory。
            //    与 SetupForShowAsync 第一步一致，不重复调用 Binder 避免破坏每包唯一 owner 契约。
            UIBattleBinder.BindAll();

            // 4. 注册最终测试 Widget（覆盖生成类型，Creator 非空）。
            //    绑定顺序与 SetupForShowAsync / TestFUIOwner.RegisterUIBattle 一致：先 Widget 再 Window。
            //    Creator 非空是 ShowAsync 闭环的必要条件（null creator 会在 CreateObjectFromURL 阶段抛 NRE）。
            module.BindingRegistry.Register(new FUIDescriptor(
                url: UI_BattleStartWidget.URL,
                packageName: UI_BattleStartWidget.PkgName,
                componentName: UI_BattleStartWidget.ResName,
                ownerType: TestFUIOwner.OwnerType,
                targetType: typeof(TestBattleStartWidget),
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: CreateTypeBasedCreator(typeof(TestBattleStartWidget))));

            // 5. 注册最终测试 Window（覆盖生成类型，Creator 非空）。
            module.BindingRegistry.Register(new FUIDescriptor(
                url: UI_BattleStartPanel.URL,
                packageName: UI_BattleStartPanel.PkgName,
                componentName: UI_BattleStartPanel.ResName,
                ownerType: TestFUIOwner.OwnerType,
                targetType: typeof(TestBattleStartPanel),
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: CreateTypeBasedCreator(typeof(TestBattleStartPanel))));

            // 6. 冻结 Registry：安装全局无状态 creator 并建立层级容器。
            module.FreezeBindings();

            return module;
        }

        /// <summary>
        /// 创建基于 <see cref="Activator.CreateInstance"/> 的无状态 creator 委托。
        /// </summary>
        /// <param name="targetType">最终业务类型，必须有无参构造函数。</param>
        /// <returns>creator 委托，接收 URL 并创建实例。</returns>
        /// <remarks>
        /// 复用 <c>WindowLifecycleTests.CreateTypeBasedCreator</c> 的实现，与
        /// <see cref="UIObjectFactory.SetPackageItemExtension(string, System.Type)"/> 内部实现一致。
        /// creator 只捕获 targetType（<see cref="Type"/> 不可释放），不捕获 Registry 或其他可释放对象，
        /// 符合 design.md 决策2 的无状态 creator 约束。
        /// </remarks>
        public static Func<string, GComponent> CreateTypeBasedCreator(Type targetType)
        {
            return url => (GComponent)Activator.CreateInstance(targetType);
        }

        /// <summary>
        /// 清空 GameFUI 与 FairyGUI 全局状态，使测试回到启动前基线。
        /// </summary>
        /// <remarks>
        /// 本方法幂等，可在 SetUp/TearDown 中重复调用。清理顺序固定为：
        /// <list type="number">
        /// <item>若模块仍注册，执行 <see cref="FUIModule.Shutdown"/> 完整清理（取消打开操作、释放窗口与包租约、
        ///   清空 Registry 与活动 Registry 静态引用、清空 FUI 门面静态缓存、释放层级容器）；</item>
        /// <item><see cref="FUIObjectFactoryIntegration.ClearActiveRegistry"/>：防御性清空活动 Registry 静态引用，
        ///   与 EditMode <c>WindowLifecycleTests.SetUp</c> 对齐，防止上一测试异常退出导致
        ///   <c>InstallPackageItemExtensions</c> 拒绝新 Registry（Shutdown 已调用，此处幂等保护）；</item>
        /// <item><see cref="PackageLoader.ClearRegistry"/>：清空 PackageLoader 静态注册表；</item>
        /// <item><see cref="UIPackage.RemoveAllPackages"/>：清空 FairyGUI 全局包注册表；</item>
        /// <item><see cref="FUI.ClearModuleForShutdown"/>：清空 FUI 门面静态模块缓存（防御性，使后续 Setup 能重新注册）。</item>
        /// </list>
        /// <para>
        /// spec fairygui-window-runtime / 模块退出完整清理：模块退出 SHALL 取消所有进行中的打开操作，
        /// 按反向顺序关闭并释放窗口，执行 detach，清理本地描述、owner、活动 Registry 和静态模块缓存，
        /// 并把所持包租约交还资源管理能力。
        /// </para>
        /// <para>
        /// Shutdown 异常被吞掉以保证清理不中断（如 FairyGUI 对象已释放或模块未注册）。
        /// </para>
        /// </remarks>
        public static void Cleanup()
        {
            // 1. 若模块仍注册，执行 Shutdown 完整清理。
            //    FUI.Module getter 在 _module 为 null 时抛 FUIException，用 try/catch 保护。
            try
            {
                FUI.Module.Shutdown();
            }
            catch (FUIException)
            {
                // 模块未注册或已 Shutdown，忽略。
            }
            catch (Exception)
            {
                // 其他异常（如 FairyGUI 对象已释放）不阻塞清理。
            }

            // 2. 防御性清空 FUIObjectFactoryIntegration 的活动 Registry 静态引用，
            //    与 EditMode WindowLifecycleTests.SetUp 对齐，防止上一测试异常退出导致
            //    InstallPackageItemExtensions 拒绝新 Registry（Shutdown 已调用，此处幂等保护）。
            FUIObjectFactoryIntegration.ClearActiveRegistry();

            // 3. 清空 PackageLoader 静态注册表（跨测试残留保护）。
            PackageLoader.ClearRegistry();

            // 4. 清空 FairyGUI 全局包注册表（跨测试残留保护）。
            UIPackage.RemoveAllPackages();

            // 5. 清空 FUI 门面静态模块缓存，使后续 Setup 能重新注册（防御性，Shutdown 已调用，此处幂等）。
            FUI.ClearModuleForShutdown();
        }

        /// <summary>
        /// 确保 FairyGUI Stage/GRoot 已初始化，供 PlayMode 测试使用。
        /// </summary>
        /// <remarks>
        /// GRoot.inst getter 在首次访问时调用 Stage.Instantiate() 创建 Stage 与 GRoot。
        /// 在 EditMode/Editor PlayMode 下需要主动触发 GRoot 初始化，否则 FreezeBindings 中
        /// 创建 <see cref="FUILayerContainer"/> 时访问 GRoot.inst 可能未就绪。
        /// 本方法只触发初始化，不设置逻辑尺寸；设计分辨率必须通过 <see cref="FUIOptions"/>
        /// 交给 <see cref="FUIModule.FreezeBindings"/> 应用，避免测试绕过生产初始化。
        /// </remarks>
        public static void EnsureGRootInitialized()
        {
            _ = GRoot.inst;
        }

        /// <summary>
        /// 构造已预设 UIBattle（依赖 UICommon）与 UICommon 包描述及外部图集资源的内存 provider。
        /// </summary>
        /// <param name="loadDelayMs">加载延迟毫秒数，0 表示立即完成。</param>
        /// <returns>已预设描述与图集资源的内存 provider。</returns>
        /// <remarks>
        /// 资源预设遵循 spec fairygui-package-loading 的命名规则：
        /// <list type="bullet">
        /// <item>描述 location：<c>{PackageName}_fui</c>（如 <c>UIBattle_fui</c>、<c>UICommon_fui</c>）；</item>
        /// <item>外部图集 location：包名前缀 + <c>_atlas0</c>（如 <c>UICommon_atlas0</c>）。</item>
        /// </list>
        /// <para>
        /// <b>关键约束：使用真实描述资源</b>。描述资源使用磁盘上真实发布的
        /// <c>UIBattle_fui.bytes</c> 与 <c>UICommon_fui.bytes</c>（1.1 核对的规范资源清单），
        /// 通过 <see cref="AssetDatabase.LoadAssetAtPath"/> 加载，保留二进制字节完整性。
        /// 不能使用 <see cref="FairyGuiDescBuilder"/> 构造的最小二进制描述：
        /// 它只含 Atlas 项、不含组件项，<c>UIPackage.CreateObjectFromURL</c> 无法从中创建真实窗口实例，
        /// 会导致 <see cref="FUI.ShowAsync{T}"/> 在创建对象阶段失败。
        /// </para>
        /// <para>
        /// 关键约束：真实描述字节包含非 ASCII 字节（&gt;= 0x80），不能通过
        /// <c>new TextAsset(Encoding.UTF8.GetString(bytes))</c> 构造，否则 UTF8 往返会破坏二进制结构。
        /// 本方法使用 <see cref="AssetDatabase.LoadAssetAtPath"/> 从磁盘直接加载 <see cref="TextAsset"/>，
        /// 确保 <c>TextAsset.bytes</c> 返回原始二进制字节。
        /// </para>
        /// <para>
        /// <b>外部图集资源</b>。UICommon 预设 <c>UICommon_atlas0</c> 纹理（2x2 像素足以通过预载）。
        /// UIBattle 无独立 atlas——使用 UICommon 的共享图集（与真实项目结构一致：
        /// <c>Assets/AssetRaw/FUI/</c> 下只有 <c>UICommon_atlas0.png</c>，无 <c>UIBattle_atlas0</c>），
        /// 因此不预设 <c>UIBattle_atlas0</c>。
        /// </para>
        /// <para>
        /// UIBattle 声明依赖 UICommon（共享图集），与真实项目结构一致
        /// （design.md 决策2：共享包同样必须有唯一 owner；spec：两个包共享依赖）。
        /// </para>
        /// <para>
        /// 本方法复用 <c>WindowLifecycleTests.CreateProviderWithLifecyclePackages</c> 的资源构造模式，
        /// 保证 PlayMode 与 EditMode 使用完全相同的资源预设契约。
        /// </para>
        /// </remarks>
        internal static InMemoryFUIResourceProvider CreateProviderWithUIBattleAndUICommon(int loadDelayMs = 0)
        {
            InMemoryFUIResourceProvider provider = new InMemoryFUIResourceProvider(loadDelayMs);

            // 从 AssetDatabase 加载真实 TextAsset（保留二进制字节完整性）。
            // 不能使用 FairyGuiDescBuilder 构造的最小二进制：它只含 Atlas 项、不含组件项，
            // UIPackage.CreateObjectFromURL 无法从中创建真实窗口实例。
            // 真实描述字节包含非 ASCII 字节（>= 0x80），不能用 new TextAsset(Encoding.UTF8.GetString(bytes)) 构造。
            TextAsset uiCommonDesc = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/AssetRaw/FUI/" + UICommonPkg + "_fui.bytes");
            TextAsset uiBattleDesc = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/AssetRaw/FUI/" + UIBattlePkg + "_fui.bytes");

            Assert.IsNotNull(uiCommonDesc, "应能从磁盘加载 UICommon_fui.bytes 描述资源。");
            Assert.IsNotNull(uiBattleDesc, "应能从磁盘加载 UIBattle_fui.bytes 描述资源。");

            // UICommon 包（无依赖，含 Atlas）——共享依赖包，先预设。
            provider.SetAsset(UICommonPkg + "_fui", uiCommonDesc);
            provider.SetAsset(UICommonPkg + "_atlas0", CreateTexture2D(2, 2));

            // UIBattle 包（依赖 UICommon，无独立 Atlas）——主包，后预设。
            // UIBattle 使用 UICommon 的共享图集，不预设 UIBattle_atlas0
            // （真实项目 Assets/AssetRaw/FUI/ 下无 UIBattle_atlas0）。
            provider.SetAsset(UIBattlePkg + "_fui", uiBattleDesc);

            return provider;
        }

        /// <summary>
        /// 创建携带指定字节的 <see cref="TextAsset"/>，用于包描述资源。
        /// </summary>
        /// <param name="bytes">描述字节。</param>
        /// <returns>TextAsset 实例。</returns>
        /// <remarks>
        /// 复用 <c>PackageLoadingTests.CreateTextAsset</c> 的实现：将原始字节按 UTF8 解码为字符串构造 TextAsset。
        /// <see cref="FairyGuiDescBuilder"/> 保证描述字节全部为 ASCII（&lt; 0x80），UTF8 解码再编码无损。
        /// 嵌入的 0x00 字节由 C# string 与 TextAsset 保留，大端整数的高位 0x00 字节无损。
        /// </remarks>
        public static TextAsset CreateTextAsset(byte[] bytes)
        {
            string text = Encoding.UTF8.GetString(bytes);
            TextAsset asset = new TextAsset(text);
            asset.hideFlags = HideFlags.HideAndDontSave;
            return asset;
        }

        /// <summary>
        /// 创建指定尺寸的 <see cref="Texture2D"/>，用于外部图集资源。
        /// </summary>
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        /// <returns>Texture2D 实例。</returns>
        /// <remarks>
        /// 复用 <c>PackageLoadingTests.CreateTexture2D</c> 的实现。
        /// 使用 <see cref="HideFlags.HideAndDontSave"/> 避免资源被持久化或显示在Hierarchy中。
        /// </remarks>
        public static Texture2D CreateTexture2D(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        #region UGUI 模块装配（任务 8.5）

        // 测试用 UIRoot GameObject 名称，供 UIModule.OnInit 通过 GameObject.Find("UIRoot") 定位。
        private const string UGUIRootName = "UIRoot";

        // 测试期间创建的 UIRoot GameObject 实例，由 ReleaseUGUIModule 统一清理。
        private static GameObject _uguiRoot;

        /// <summary>
        /// 装配真实 <see cref="UIModule"/>：创建 UIRoot 场景前置、注入测试资源加载器、触发
        /// <see cref="UIModule.Instance"/> 初始化，返回可直接调用
        /// <see cref="UIModule.ShowUIAsyncAwait{T}"/> / <see cref="UIModule.CloseUI{T}"/> 等公开入口的模块实例。
        /// </summary>
        /// <returns>已就绪的真实 <see cref="UIModule"/> 单例。</returns>
        /// <remarks>
        /// <para>
        /// <b>任务 8.5 要求</b>：共存验收必须通过现有 UGUI 公开入口打开真实 <see cref="UIWindow"/>，
        /// 不得仅用手工 Canvas/GameObject 树代替现有 <see cref="UIModule"/>。本方法装配真实
        /// <see cref="UIModule"/> 单例与真实 <see cref="UIWindow"/> 生命周期，使测试经
        /// <c>UIModule.Instance.ShowUIAsyncAwait&lt;T&gt;()</c>、<c>CloseUI&lt;T&gt;()</c>、
        /// <c>Release()</c> 等公开入口驱动 UGUI 侧，而非手工构造 Canvas 树。
        /// </para>
        /// <para>
        /// <b>UIRoot 场景前置</b>：<see cref="UIModule.OnInit"/> 通过 <c>GameObject.Find("UIRoot")</c>
        /// 定位 UI 根节点，读取其 Canvas 作为窗口父节点（<c>_instanceRoot</c>）、读取其 Camera 作为
        /// UI 摄像机。本方法创建符合该契约的 UIRoot GameObject（含 Canvas+CanvasScaler+GraphicRaycaster
        /// 子节点与 Camera 子节点），使 OnInit 真实完成窗口栈初始化。
        /// </para>
        /// <para>
        /// <b>资源加载器注入</b>：<see cref="UIModule.Resource"/> 是 <c>public static IUIResourceLoader</c>
        /// 公开注入点（OnInit 默认写入 <c>new UIResourceLoader()</c>，其构造调用
        /// <c>ModuleSystem.GetModule&lt;IResourceModule&gt;()</c>）。生产环境由 ResourceModuleDriver
        /// 预注册 ResourceModule，但测试无该组合根，<c>ModuleSystem.GetModule&lt;IResourceModule&gt;()</c>
        /// 会因 ResourceModule（internal、无公开构造）无法经 <c>Activator.CreateInstance</c> 实例化而抛
        /// <c>MissingMethodException</c>，导致 OnInit 在 <c>Resource = new UIResourceLoader()</c> 处中断。
        /// 本方法对此做两重保护：
        /// </para>
        /// <list type="number">
        /// <item>先写入测试加载器（<see cref="TestUGUIResourceLoader"/>），使中断时
        ///   <c>Resource</c> 仍指向测试加载器（赋值右侧抛异常，<c>Resource</c> 未被覆盖）；</item>
        /// <item>用 try/catch 包裹 <see cref="UIModule.Instance"/> 首次访问，吞掉 OnInit 中断异常
        ///   （此时 <c>_instance</c> 与 <c>_instanceRoot</c> 已在抛出前就绪），再显式覆盖 <c>Resource</c>
        ///   为测试加载器，使后续 ShowUI 经测试加载器取得真实面板 GameObject。</item>
        /// </list>
        /// <para>
        /// 该注入与 <see cref="InMemoryFUIResourceProvider"/> 对 GameFUI 的注入对称：两侧均通过模块公开
        /// 注入点提供测试资源能力，再经真实公开入口驱动真实生命周期。本方法不修改 <see cref="UIModule"/>
        /// 或任何 GameLogic 源码，不修改 GameLogic.asmdef（仅在 GameFUI.Tests.asmdef 新增 GameLogic 引用）。
        /// </para>
        /// </remarks>
        public static UIModule SetupUGUIModule()
        {
            // 0. 幂等清理：释放可能残留的 UIModule 与 UIRoot，保证装配起点干净。
            ReleaseUGUIModule();

            // 1. 创建 UIRoot 场景前置（UIModule.OnInit 通过 GameObject.Find("UIRoot") 定位）。
            _uguiRoot = CreateUGUIRoot();

            // 2. 先写入测试资源加载器，使 OnInit 中断时 Resource 仍指向测试加载器。
            UIModule.Resource = new TestUGUIResourceLoader();

            // 3. 触发 UIModule.Instance 初始化。
            //    OnInit 找到 UIRoot 后，在 Resource = new UIResourceLoader() 处可能因 ModuleSystem 无法
            //    实例化 ResourceModule 而抛异常；此时 _instance 与 _instanceRoot 已就绪，吞掉异常继续。
            try
            {
                _ = UIModule.Instance;
            }
            catch (Exception)
            {
                // OnInit 在 UIResourceLoader 构造处中断：_instance 已创建、_instanceRoot 已设置，
                // 后续 DontDestroyOnLoad/layer/Debugger 配置未完成，但 ShowUI/CloseUI/Release 均可用。
                // Resource 因赋值右侧抛异常而未被覆盖，仍为步骤 2 写入的测试加载器。
            }

            // 4. 显式覆盖 Resource 为测试加载器：覆盖 OnInit 可能写入的真实 UIResourceLoader，
            //    或幂等保持步骤 2 的测试加载器，使 ShowUIAsyncAwait 经测试加载器取得真实面板。
            UIModule.Resource = new TestUGUIResourceLoader();

            return UIModule.Instance;
        }

        /// <summary>
        /// 释放真实 <see cref="UIModule"/> 并销毁 UIRoot，回到装配前基线。幂等，可在 SetUp/TearDown 重复调用。
        /// </summary>
        /// <remarks>
        /// 若 <see cref="UIModule"/> 仍有效，调用 <c>UIModule.Instance.Release()</c> 触发
        /// <see cref="UIModule.OnRelease"/>（关闭所有窗口并销毁 UIRoot），随后清空静态资源加载器引用并
        /// 销毁残留 UIRoot。使用 <see cref="UIModule.IsValid"/> 判断是否仍有效，避免误触发
        /// <see cref="UIModule.Instance"/> 重新初始化。
        /// </remarks>
        public static void ReleaseUGUIModule()
        {
            // 1. 若 UIModule 仍有效，触发 OnRelease：关闭所有窗口、销毁 UIRoot、清空 _instance。
            //    不使用 UIModule.Instance 判断（会触发重新初始化），而用 IsValid（只读 _instance != null）。
            if (UIModule.IsValid)
            {
                UIModule.Instance.Release();
            }

            // 2. 清空静态资源加载器引用，避免跨测试残留。
            UIModule.Resource = null;

            // 3. 销毁可能残留的 UIRoot（OnRelease 已销毁 UIRoot 时此处引用为 destroyed，
            //    Unity 重载 == 判为 null，跳过）。
            if (_uguiRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_uguiRoot);
                _uguiRoot = null;
            }
        }

        /// <summary>
        /// 创建符合 <see cref="UIModule.OnInit"/> 契约的 UIRoot GameObject：名为 "UIRoot"，
        /// 含 Canvas（+CanvasScaler+GraphicRaycaster）子节点与 Camera 子节点。
        /// </summary>
        /// <returns>UIRoot GameObject 实例。</returns>
        private static GameObject CreateUGUIRoot()
        {
            GameObject uiRoot = new GameObject(UGUIRootName);

            // UI 摄像机子节点（UIModule.OnInit 通过 GetComponentInChildren<Camera> 读取）。
            GameObject cameraGo = new GameObject("UICamera");
            cameraGo.transform.SetParent(uiRoot.transform, false);
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Depth;
            camera.orthographic = true;

            // Canvas 子节点（UIModule.OnInit 通过 GetComponentInChildren<Canvas> 读取为 _instanceRoot）。
            GameObject canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(uiRoot.transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            return uiRoot;
        }

        /// <summary>
        /// 测试用 UI 资源加载器：为 <see cref="UIModule.ShowUIAsyncAwait{T}"/> 提供带 Canvas 与
        /// GraphicRaycaster 的真实面板 GameObject，满足 <see cref="UIWindow"/> 的 Handle_Completed
        /// 对面板组件的要求，使真实 UIWindow 生命周期完整执行。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="InMemoryFUIResourceProvider"/> 对称：通过模块公开注入点
        /// （<see cref="UIModule.Resource"/>）提供测试资源能力，不伪造 <see cref="UIModule"/> 本身。
        /// 加载器忽略 location，统一构造符合 UIWindow 契约的面板：Canvas（Handle_Completed 读取并设置
        /// overrideSorting）+ GraphicRaycaster（Handle_Completed 读取）+ 全屏 RectTransform。
        /// </remarks>
        private sealed class TestUGUIResourceLoader : IUIResourceLoader
        {
            /// <summary>
            /// 同步加载（实例化）面板 GameObject。
            /// </summary>
            public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
            {
                return CreatePanel(parent);
            }

            /// <summary>
            /// 异步加载（实例化）面板 GameObject，立即完成。
            /// </summary>
            public UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null,
                CancellationToken cancellationToken = default, string packageName = "")
            {
                return UniTask.FromResult(CreatePanel(parent));
            }

            /// <summary>
            /// 构造带 Canvas 与 GraphicRaycaster 的面板 GameObject，挂到指定父节点下。
            /// </summary>
            private static GameObject CreatePanel(Transform parent)
            {
                GameObject panel = new GameObject("UGUIPanel");
                if (parent != null)
                {
                    panel.transform.SetParent(parent, false);
                }

                Canvas canvas = panel.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                panel.AddComponent<GraphicRaycaster>();

                RectTransform rect = panel.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                return panel;
            }
        }

        #endregion
    }
}
