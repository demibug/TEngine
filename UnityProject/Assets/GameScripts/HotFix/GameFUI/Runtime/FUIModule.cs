using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using TEngine;

namespace GameFUI
{
    /// <summary>
    /// GameFUI 模块实现：组合绑定注册表、最窄资源 provider 与包加载器，对外实现 <see cref="IFUIModule"/>。
    /// </summary>
    /// <remarks>
    /// 设计依据：design.md 决策1。
    /// <para>
    /// 公开注册形态固定为 <c>FUI.RegisterModule(IResourceModule, FUIOptions)</c>，由 <see cref="FUI"/> 门面
    /// 在进入 <c>ModuleSystem.RegisterModule</c> 前完成重复注册保护并构造本模块实例。本 change 不修改生产
    /// GameLogic 组合根，因此 <see cref="FUI.RegisterModule"/> 不实际调用 <c>ModuleSystem.RegisterModule</c>，
    /// 而是直接创建本实例并存储到 <c>FUI._module</c>；生产组合根的 ModuleSystem 集成由后续 change 负责。
    /// </para>
    /// <para>
    /// 本模块继承 TEngine <see cref="Module"/> 基类（<c>Assets/TEngine/Runtime/Core/Module.cs</c>），
    /// 签名为 <c>public abstract class Module</c>，含 <c>virtual int Priority</c>、<c>abstract void OnInit()</c>
    /// 与 <c>abstract void Shutdown()</c>。本模块同时实现 <see cref="IFUIModule"/>（任务 3.3 固定的公开接口）。
    /// Module 基类的 <see cref="Shutdown"/> 与 <see cref="IFUIModule.Shutdown"/> 共用同一实现。
    /// </para>
    /// <para>
    /// 资源 provider 包装：本模块在内部把公开传入的 <see cref="IResourceModule"/> 包装为最窄资源 provider
    /// <see cref="YooAssetFUIResourceProvider"/>（design.md 决策1：FUIModule 在内部把公开传入的 IResourceModule
    /// 包装成最窄资源 provider）。仅通过 internal 测试入口为测试程序集注入可控失败/取消的内存 provider
    /// （<see cref="InMemoryFUIResourceProvider"/>），不增加第二个公开注册重载。
    /// </para>
    /// <para>
    /// 显式注册入口：owner 在初始化阶段通过 <see cref="RegisterDescriptor"/> 显式注册窗口/Widget 描述到
    /// <see cref="BindingRegistry"/>。<see cref="FUIBindingRegistry"/> 提供 <see cref="FUIBindingRegistry.GetRegisteredUrls"/>
    /// 只读查询方法（任务 5.1 返工修复），供 <see cref="FreezeBindings"/> 获取全部已注册 URL 并调用
    /// <see cref="FUIObjectFactoryIntegration.InstallPackageItemExtensions"/> 安装只捕获 URL 的无状态 creator。
    /// 这样无论 owner 走 <see cref="RegisterDescriptor"/> 还是直接走 <see cref="FUIBindingRegistry.Register"/>
    /// （如 design.md 决策10 装配流程中的 <c>TestFUIOwner.RegisterUIBattle</c>），冻结时都能获取到全部 URL。
    /// </para>
    /// <para>
    /// 边界约束：本类型不 using 且不反向依赖 GameLogic/GamePlay/GameBattle 命名空间。
    /// </para>
    /// </remarks>
    public sealed class FUIModule : Module, IFUIModule, IFUIWindowStateProvider
    {
        /// <summary>
        /// 绑定注册表实例，是运行时创建受管理对象的唯一注册来源（design.md 决策2）。
        /// </summary>
        private readonly FUIBindingRegistry _registry;

        /// <summary>
        /// 最窄资源 provider，由公开传入的 <see cref="IResourceModule"/> 包装而来，
        /// 或由 internal 测试入口直接注入内存 provider。
        /// </summary>
        private readonly IFUIResourceProvider _resourceProvider;

        /// <summary>
        /// 模块注册选项。为 null 时使用默认策略（KeepUntilShutdown）。
        /// </summary>
        private readonly FUIOptions _options;

        /// <summary>
        /// 模块是否已 Shutdown。Shutdown 后任何公开操作应失败，不得留下后续回调
        /// （spec：模块退出完整清理）。
        /// </summary>
        private bool _isShutdown;

        /// <summary>
        /// GRoot 下的固定层级容器集合（Background、Normal、Popup、Guide、Tips、System）。
        /// <para>由 <see cref="FreezeBindings"/> 在初始化阶段创建，由 <see cref="Shutdown"/> 释放。
        /// 后续任务 5.3 在此基础上建立 Full/Safe 子容器，5.11 在此基础上做层内排序与全屏遮挡
        /// （design.md 决策7）。</para>
        /// </summary>
        private FUILayerContainer _layerContainer;

        /// <summary>
        /// 模块 lifetime token source。驱动全部共享加载任务的生命周期，<see cref="Shutdown"/> 时取消。
        /// </summary>
        /// <remarks>
        /// 设计依据：design.md 决策4 与 spec“Show 公开接口和完成边界稳定”。
        /// <para>
        /// 无调用方取消令牌的 <c>ShowAsync</c> 重载使用本 token 驱动框架工作；带调用方令牌的重载只取消该调用方的等待，
        /// 不得取消其他调用方共享的包加载或对象创建（spec：带取消令牌打开窗口——令牌 SHALL 只取消该调用方的等待）。
        /// 因此共享加载任务只绑定本模块 lifetime token，不绑定单个调用方令牌。
        /// </para>
        /// <para>
        /// <see cref="Shutdown"/> 调用 <see cref="CancellationTokenSource.Cancel"/> 取消本 token，
        /// 使所有进行中的共享加载任务收到取消；重复 Cancel 幂等。Shutdown 后本字段被 Dispose 并置 null。
        /// </para>
        /// <para>
        /// 使用 <see cref="CancellationTokenSource"/> 而非裸 <see cref="CancellationToken"/>，是因为需要在模块内部
        /// 控制取消时机；对外通过 <see cref="ModuleLifetimeToken"/> 暴露只读令牌。
        /// </para>
        /// </remarks>
        private CancellationTokenSource _lifetimeCts;

        /// <summary>
        /// 模块 lifetime token。绑定到 <see cref="_lifetimeCts"/>，<see cref="Shutdown"/> 时被取消。
        /// </summary>
        /// <remarks>
        /// 供共享加载任务（<see cref="PackageLoader.AcquireAsync"/>）绑定生命周期：模块退出时所有共享加载任务收到取消，
        /// 不得留下后续回调（spec：模块退出完整清理——所有操作 SHALL 结束）。
        /// 本属性为 internal，供同程序集的 Show/Close 实现与测试程序集诊断使用。
        /// </remarks>
        internal CancellationToken ModuleLifetimeToken
        {
            get
            {
                // Shutdown 后 _lifetimeCts 被置 null，此时返回一个永远已取消的令牌，
                // 使任何迟到的共享加载任务立即收到取消，避免悬挂回调。
                if (_lifetimeCts == null)
                {
                    return new CancellationToken(true);
                }

                return _lifetimeCts.Token;
            }
        }

        /// <summary>
        /// 按窗口类型索引的运行时跟踪条目集合。
        /// </summary>
        /// <remarks>
        /// 设计依据：design.md 决策4——FUIModule 为每个窗口类型保存一个 <see cref="WindowEntry"/>，
        /// 包含注册描述、当前状态、实例、创建任务、包租约、打开请求队列、当前打开 CTS 和递增的 operation version。
        /// <para>
        /// 索引键为最终业务窗口类型 <see cref="Type"/>，与 <see cref="FUIBindingRegistry"/> 按 TargetType 查询描述一致。
        /// 同一类型在全模块内只有一个 <see cref="WindowEntry"/>；实例终态 <see cref="FUIWindowState.Disposed"/> 后，
        /// 下一次 Show 由后续 5.x 任务重建新 entry 回到 <see cref="FUIWindowState.Absent"/>。
        /// </para>
        /// <para>
        /// 本字段为 internal，供后续 5.x Show/Hide/Close/查询任务在同一程序集内访问；
        /// 测试程序集通过 InternalsVisibleTo 验证状态转换与诊断。
        /// </para>
        /// </remarks>
        internal readonly Dictionary<Type, WindowEntry> _windowEntries = new Dictionary<Type, WindowEntry>();

        /// <summary>
        /// 构造 FUIModule，注入已包装好的资源 provider 与选项。
        /// </summary>
        /// <param name="resourceProvider">最窄资源 provider，不得为 null。</param>
        /// <param name="options">注册选项，为 null 时使用默认策略（KeepUntilShutdown）。</param>
        /// <remarks>
        /// 本构造函数为 internal，仅供 <see cref="FUI.RegisterModule"/> 与 internal 测试入口调用。
        /// 公开注册形态固定为 <c>FUI.RegisterModule(IResourceModule, FUIOptions)</c>，不增加第二个公开重载
        /// （design.md 决策1）。
        /// </remarks>
        internal FUIModule(IFUIResourceProvider resourceProvider, FUIOptions options)
        {
            // provider 由调用方保证非空（FUI.RegisterModule / 测试入口负责校验并包装）。
            _resourceProvider = resourceProvider;
            _options = options ?? new FUIOptions();
            _registry = new FUIBindingRegistry();

            // 6.4 装配阶段接线：把公开 FUIOptions 的卸载策略与延迟时间传给 PackageLoader.Configure，
            // 使生产代码中 FUIOptions{UnloadPolicy=Delayed} 实际得到 Delayed 行为。此前 FUIModule 只存储
            // _options 不应用，PackageLoader 只能保持默认 KeepUntilShutdown（PackageBaselineTests 注释曾承认此降级）。
            // _options 已完成 null 合并（null options → 默认 KeepUntilShutdown + 5s），此处一定非 null；
            // 传默认值时与 PackageLoader 已有默认值一致，不改变默认 KeepUntilShutdown 行为。
            // Configure 只设置 PackageLoader 静态配置字段（Volatile 读写），无加载/卸载副作用，
            // 适合在构造阶段一次性完成；早于 FreezeBindings 与任何 ShowAsync，配置在包加载前生效。
            // 测试（PackageBaselineTests）在装配后通过 InternalsVisibleTo 显式调 Configure 覆盖，
            // 后写胜出（Volatile.Write），本接线不破坏测试的显式覆盖做法。
            PackageLoader.Configure(_options.UnloadPolicy, _options.UnloadDelaySeconds);

            // 创建模块 lifetime token source：驱动全部共享加载任务的生命周期（design.md 决策4）。
            // Shutdown 时取消，使所有进行中的共享加载任务收到取消，不得留下后续回调。
            _lifetimeCts = new CancellationTokenSource();
        }

        /// <summary>
        /// 获取本模块持有的绑定注册表（internal，供测试程序集与装配方在冻结前注册描述）。
        /// </summary>
        /// <remarks>
        /// owner 通过本访问器取得 <see cref="FUIBindingRegistry"/> 并调用其 <c>Register</c>；
        /// 装配方也可改用 <see cref="RegisterDescriptor"/>。任务 5.1 返工后，<see cref="FreezeBindings"/>
        /// 通过 <see cref="FUIBindingRegistry.GetRegisteredUrls"/> 从注册表获取全部已注册 URL，
        /// 两条注册路径对 creator 安装等效。
        /// 本访问器仅供测试程序集（通过 InternalsVisibleTo）与同程序集使用，不对外公开。
        /// </remarks>
        internal FUIBindingRegistry BindingRegistry => _registry;

        /// <summary>
        /// 获取本模块持有的最窄资源 provider（internal，仅供测试程序集诊断与验证注入）。
        /// </summary>
        internal IFUIResourceProvider ResourceProvider => _resourceProvider;

        /// <summary>
        /// 获取本模块注册选项（internal，供测试程序集验证装配参数）。
        /// </summary>
        internal FUIOptions Options => _options;

        /// <summary>
        /// 获取 GRoot 下的固定层级容器集合（internal）。
        /// <para>层容器在 <see cref="FreezeBindings"/> 阶段创建完成；访问本属性前必须先完成冻结。
        /// 供后续任务 5.3（Full/Safe 子容器）与 5.11（层内排序与全屏遮挡）在同一程序集或测试程序集扩展使用。</para>
        /// </summary>
        internal FUILayerContainer LayerContainer => _layerContainer;

        /// <summary>
        /// 注册一个窗口或 Widget 描述到绑定注册表。
        /// </summary>
        /// <param name="descriptor">受管理对象描述，字段不可变。</param>
        /// <remarks>
        /// 本方法为 internal，供装配方（测试 harness / 未来生产组合根）在冻结前显式注册。
        /// 它转发到 <see cref="FUIBindingRegistry.Register"/>。
        /// 注册阶段只完成绑定和同步描述写入，不得创建或显示 FairyGUI 对象。
        /// <para>
        /// URL 不再由本方法收集：任务 5.1 返工后，<see cref="FreezeBindings"/> 直接通过
        /// <see cref="FUIBindingRegistry.GetRegisteredUrls"/> 从注册表获取全部已注册 URL，
        /// 不再依赖本方法维护的旁路列表。这保证 owner 无论走本方法还是直接走
        /// <see cref="FUIBindingRegistry.Register"/>（design.md 决策10 装配流程），
        /// 冻结时 creator 安装都能覆盖全部 URL。
        /// </para>
        /// </remarks>
        /// <exception cref="FUIException">描述字段非法、与已注册描述冲突或在冻结后注册。</exception>
        internal void RegisterDescriptor(FUIDescriptor descriptor)
        {
            // 转发到注册表。URL 不再在此收集：FreezeBindings 通过 registry.GetRegisteredUrls() 获取。
            _registry.Register(descriptor);
        }

        /// <summary>
        /// 模块初始化。由 TEngine <c>ModuleSystem</c> 在 <c>RegisterModule</c> 时调用 OnInit。
        /// </summary>
        /// <remarks>
        /// 本 change 不修改生产 GameLogic 组合根，<see cref="FUI.RegisterModule"/> 不实际调用
        /// <c>ModuleSystem.RegisterModule</c>，故本方法在当前装配路径下不会被 ModuleSystem 触发。
        /// 预留给后续 change 接管生产组合根时使用：届时 ModuleSystem.RegisterModule 会调用本方法，
        /// 本方法保持空实现，模块实际能力由 <see cref="FUI.RegisterModule"/> 构造阶段与
        /// <see cref="FreezeBindings"/> 完成装配。
        /// </remarks>
        public override void OnInit()
        {
            // 当前阶段无框架级初始化工作；资源 provider 与 Registry 已在构造阶段就绪。
        }

        /// <summary>
        /// 冻结绑定注册表并安装全局无状态 creator，同时在 GRoot 下建立固定层级容器。
        /// </summary>
        /// <remarks>
        /// 所有 owner 完成显式注册后由装配方调用（design.md 决策2）。
        /// 本方法执行三步：
        /// 1. 创建 <see cref="FUILayerContainer"/>：在 GRoot 下按 <see cref="FUILayer"/> 顺序建立
        ///    Background、Normal、Popup、Guide、Tips、System 六个固定层级容器（design.md 决策7），
        ///    使后续 Show 的窗口只挂载到所属层级容器，不在 GRoot 顶层与其他系统对象混合排序；
        /// 2. <see cref="FUIBindingRegistry.Freeze"/>：冻结后任何新增或冲突注册直接抛 <see cref="FUIException"/>；
        /// 3. <see cref="FUIObjectFactoryIntegration.InstallPackageItemExtensions"/>：为已注册的全部组件 URL
        ///    安装只捕获 URL 的无状态 creator，使 FairyGUI 创建受管理对象时查询当前活动 Registry 的描述，
        ///    返回最末端业务类型而非生成基类（spec：业务类型覆盖生成类型）。
        /// 首次创建任何受管理对象前必须完成冻结与 creator 安装。
        /// <para>
        /// URL 来源（任务 5.1 返工修复）：通过 <see cref="FUIBindingRegistry.GetRegisteredUrls"/> 从注册表获取
        /// 全部已注册 URL，不再依赖本模块在 <see cref="RegisterDescriptor"/> 中旁路收集的列表。
        /// 这保证 design.md 决策10 装配流程中 owner 直接调用 <see cref="FUIBindingRegistry.Register"/>
        /// （如 <c>TestFUIOwner.RegisterUIBattle</c>）注册的 URL 也能被安装为全局 creator。
        /// </para>
        /// </remarks>
        public void FreezeBindings()
        {
            ThrowIfShutdown();

            // 1. 创建固定层级容器：在 GRoot 下建立六个固定层，窗口只挂载到所属层级容器（design.md 决策7）。
            //    层级容器在 FreezeBindings 阶段创建，保证后续 Show 前已就绪；GRoot.inst 在装配阶段可用。
            if (_layerContainer == null)
            {
                _layerContainer = new FUILayerContainer();
                _layerContainer.Create();
            }

            // 2. 冻结注册表：冻结后新增注册直接报错，运行期只读查询生效（IsActive 为 true）。
            _registry.Freeze();

            // 3. 安装全局无状态 creator：creator 只捕获 URL，不捕获 Registry 实例或可释放运行时对象，
            //    被调用时通过 FUIObjectFactoryIntegration.ActiveRegistry 静态访问器查询当前活动 Registry。
            //    URL 列表直接从注册表获取（GetRegisteredUrls），覆盖所有注册路径
            //    （RegisterDescriptor 与直接 Register），保证装配流程一致性。
            IReadOnlyCollection<string> urls = _registry.GetRegisteredUrls();
            FUIObjectFactoryIntegration.InstallPackageItemExtensions(_registry, urls);

            // 4. 6.5：将本模块注册为 PackageLoader 的全局窗口状态查询接口（IFUIWindowStateProvider）。
            //    使包卸载前置检查（含 Delayed 延迟卸载路径）能查询存活窗口、缓存窗口、创建任务与上层依赖，
            //    补齐 6.4 在延迟卸载路径中以 null 传入 windowStateProvider 留下的窗口约束缺口
            //    （spec：包租约控制缓存和卸载——只有不存在存活或缓存对象、创建任务、上层依赖和待完成资源操作时，
            //      包才可在延迟窗口结束后卸载；design.md 决策9：最终卸载必须……没有存活或缓存窗口、
            //      没有创建任务、没有上层依赖）。
            //    注册时机在 FreezeBindings 而非构造函数：构造阶段 _windowEntries 尚未使用，但注册本身
            //    只设置 PackageLoader 静态字段，无副作用；放在 FreezeBindings 保证与模块装配流程一致，
            //    且 Shutdown 时成对清空（见 Shutdown 步骤 1.5）。
            PackageLoader.SetWindowStateProvider(this);
        }

        /// <summary>
        /// 模块退出清理：取消进行中的打开操作、释放窗口与包租约、清理本地描述与活动 Registry、清空静态模块缓存。
        /// </summary>
        /// <remarks>
        /// 实现依据：spec“模块退出完整清理”与 design.md 决策8/9。
        /// 顺序固定为：
        /// 0. 取消模块 lifetime token（<see cref="_lifetimeCts"/>）：使所有进行中的共享加载任务收到取消，
        ///    阻止新的加载回调进入半完成状态（design.md 决策4：共享加载只受模块 lifetime token 控制）。
        ///    本步最先执行，保证后续包回收不会与迟到的加载回调竞争；
        /// 1. 清理窗口条目（<see cref="_windowEntries"/>）：取消每个条目的打开 CTS、清空刷新队列与共享创建任务，
        ///    使进行中的打开操作失效（spec：模块退出——所有操作 SHALL 结束，所有窗口实例、事件域、取消域 SHALL 被清理）。
        ///    窗口实例与包租约的最终释放由后续 5.x 任务在本步内补充，本任务只清理 CTS/队列/任务引用与字典本身；
        /// 1.5 6.5：清空 <see cref="PackageLoader"/> 的全局窗口状态查询接口注册（<see cref="PackageLoader.ClearWindowStateProvider"/>），
        ///    使任何进行中的 Delayed 延迟卸载任务不查询本模块，避免跨模块残留；
        /// 2. <see cref="PackageLoader.UnloadAllForShutdown"/>：按 KeepUntilShutdown 策略强制回收全部已注册包，
        ///    移除 FairyGUI 包注册、Dispose handle、释放依赖租约，使包/依赖引用/handle 回到启动前基线；
        /// 3. <see cref="FUIBindingRegistry.Shutdown"/>：清空本地描述、owner 映射，标记非活动，
        ///    使迟到的全局 creator 查询因 Registry 非活动而明确失败；
        /// 4. <see cref="FUIObjectFactoryIntegration.ClearActiveRegistry"/>：清空活动 Registry 静态引用，
        ///    不调用全局 <c>UIObjectFactory.Clear()</c>，以免清除其他 FairyGUI 扩展；
        /// 5. 标记本模块已 Shutdown，并清空 <see cref="FUI"/> 门面的静态模块缓存；
        /// 6. 释放 <see cref="FUILayerContainer"/>：从 GRoot 移除并 Dispose 六个固定层级容器，
        ///    使显示树回到创建前基线（design.md 决策7）。窗口实例的释放由后续 5.x Shutdown 流程在包回收前完成，
        ///    本步只清理层级容器本身；
        /// 7. Dispose <see cref="_lifetimeCts"/> 并置 null，释放取消令牌源资源。
        /// 本方法同时满足 <see cref="Module.Shutdown"/> 与 <see cref="IFUIModule.Shutdown"/> 契约。
        /// </remarks>
        public override void Shutdown()
        {
            if (_isShutdown)
            {
                // 重复 Shutdown 幂等，直接返回。
                return;
            }

            _isShutdown = true;

            // 0. 取消模块 lifetime token：使所有进行中的共享加载任务收到取消，
            //    阻止新的加载回调进入半完成状态（design.md 决策4）。本步最先执行，
            //    保证后续包回收不会与迟到的加载回调竞争。Cancel 幂等，不会抛异常。
            if (_lifetimeCts != null)
            {
                _lifetimeCts.Cancel();
            }

            // 1. 清理窗口条目：取消每个条目的打开 CTS、清空刷新队列与共享创建任务引用，
            //    使进行中的打开操作失效（spec：模块退出——所有操作 SHALL 结束）。
            //    窗口实例与包租约的最终释放顺序由后续 5.x 任务在本步内补充（OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease），
            //    本任务先清理 CTS/队列/任务引用与字典本身，避免迟到回调持有过期 entry。
            ClearWindowEntriesForShutdown();

            // 1.5 6.5：清空 PackageLoader 的全局窗口状态查询接口注册。
            //   在强制回收包之前清空，使任何进行中的 Delayed 延迟卸载任务在到期时不会查询本模块
            //   （UnloadAllForShutdown 为强制回收，不查询 CanUnload；清空避免跨模块残留）。
            //   清空后 CanUnload 回退到 null 视为无窗口约束，与 6.4 行为一致，不影响强制 Shutdown 路径。
            PackageLoader.ClearWindowStateProvider();

            // 2. 强制回收全部已注册包（KeepUntilShutdown 策略）。
            PackageLoader.UnloadAllForShutdown();

            // 3. 清空本地绑定注册表，标记非活动。
            _registry.Shutdown();

            // 4. 清空活动 Registry 静态引用，使迟到的全局 creator 明确失败；不调用 UIObjectFactory.Clear()。
            FUIObjectFactoryIntegration.ClearActiveRegistry();

            // 5. 清空 FUI 门面的静态模块缓存，使后续 Module getter 抛“尚未注册”异常，
            //    避免残留实例被访问（design.md 决策1：Shutdown 清空静态缓存）。
            FUI.ClearModuleForShutdown();

            // 6. 释放固定层级容器：从 GRoot 移除并 Dispose，使显示树回到创建前基线（design.md 决策7）。
            if (_layerContainer != null)
            {
                _layerContainer.Dispose();
                _layerContainer = null;
            }

            // 7. 释放模块 lifetime token source，释放取消令牌源资源。
            //    Dispose 后 Token 访问返回已取消令牌（见 ModuleLifetimeToken getter），保证迟到的共享加载任务立即收到取消。
            if (_lifetimeCts != null)
            {
                _lifetimeCts.Dispose();
                _lifetimeCts = null;
            }
        }

        /// <summary>
        /// 模块 Shutdown 时清理全部窗口条目：取消打开 CTS、清空刷新队列与共享创建任务引用，并释放窗口实例与包租约。
        /// </summary>
        /// <remarks>
        /// 本方法在 <see cref="PackageLoader.UnloadAllForShutdown"/> 之前执行，使进行中的打开操作失效，
        /// 避免迟到回调持有过期 entry 或执行过期生命周期（spec：模块退出完整清理）。
        /// <para>
        /// 窗口实例与包租约的最终释放顺序由任务 5.10 补充：对每个已 attach 的存活实例执行完整释放顺序
        /// （Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease），
        /// 对未 attach 的实例只 Dispose GObject + Release lease。释放判断依据
        /// <see cref="WindowEntry.IsInstanceLifecycleAttached"/>。
        /// </para>
        /// <para>
        /// spec: 模块退出完整清理——所有窗口实例、事件域、取消域和租约 SHALL 被清理且不得留下后续回调。
        /// </para>
        /// <para>
        /// 本方法幂等：多次调用不抛异常，已清理的 entry 字段为 null 时跳过。
        /// </para>
        /// </remarks>
        private void ClearWindowEntriesForShutdown()
        {
            foreach (KeyValuePair<Type, WindowEntry> pair in _windowEntries)
            {
                WindowEntry entry = pair.Value;
                if (entry == null)
                {
                    continue;
                }

                // 取消打开 CTS：使窗口自行启动的业务异步任务收到取消（design.md 决策5：Close 时取消 Open Token）。
                // Shutdown 时额外 Dispose CTS，释放取消令牌源资源（资源卫生）。
                CancellationTokenSource openCts = entry.OpenCts;
                if (openCts != null)
                {
                    try
                    {
                        openCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // CTS 已被 Dispose，忽略取消异常，继续清理其他字段。
                    }

                    try
                    {
                        openCts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // CTS 已被 Dispose，忽略 Dispose 异常。
                    }

                    entry.OpenCts = null;
                }

                // 清空刷新队列：未处理的刷新请求在 Shutdown 后不再执行（spec：所有操作 SHALL 结束）。
                entry.PendingRefreshArgs.Clear();

                // 清空共享创建任务引用：使后续 await 的调用方在任务完成时通过 operation version 检测到过期并回滚，
                // 不执行过期 OnOpen/OnRefresh（design.md 决策4）。任务本身不被 Cancel，由调用方在 await 后自检版本。
                entry.SharedCreateTask = null;

                // 释放窗口实例与包租约（任务 5.10 补充完整释放顺序）。
                // 对已 attach 的实例执行完整释放顺序（Dispose Widgets -> OnDispose -> Descriptor.Detach ->
                // Dispose GObject -> Release lease），对未 attach 的实例只 Dispose GObject + Release lease。
                // 释放顺序由 RollbackLoadedInstance 根据 IsInstanceLifecycleAttached 自动选择。
                if (entry.Window != null || entry.Lease != null)
                {
                    RollbackLoadedInstance(entry);
                }
            }

            _windowEntries.Clear();
        }

        /// <summary>
        /// 打开窗口（无调用方取消令牌）。
        /// <para>使用模块 lifetime token 驱动框架加载与创建工作；成功表示包和依赖资源已就绪、
        /// 最终类型已构造、同步 OnCreate/OnOpen/OnRefresh 已完成且窗口处于 Open
        /// （spec: Show 公开接口和完成边界稳定；design.md 决策4）。</para>
        /// <para>同类型并发 Show 合并加载与实例创建：首个请求建立 <see cref="WindowEntry.SharedCreateTask"/>，
        /// 后续请求 await 同一任务，完成后各自进入 <see cref="WindowEntry.PendingRefreshArgs"/> FIFO 队列
        /// 获得一次刷新机会（spec: 同类型并发打开串行收敛）。</para>
        /// <para>每个有效请求先更新 <c>UserDatas/UserData</c> 再同步执行 <c>OnRefresh</c>
        /// （spec: 窗口生命周期次数确定——每个刷新请求执行前 SHALL 更新 UserDatas 和 UserData）。</para>
        /// </summary>
        /// <typeparam name="T">最终业务窗口类型，必须为 <see cref="FUIWindow"/> 子类且已注册。</typeparam>
        /// <param name="args">零个或多个用户参数，由框架在每个有效刷新请求执行前更新到窗口的 UserDatas/UserData。</param>
        /// <returns>已处于 Open 状态的非空业务窗口实例。</returns>
        /// <exception cref="OperationCanceledException">模块 lifetime 被取消（如 Shutdown）。</exception>
        /// <exception cref="FUIException">注册缺失、包资源、对象构造或生命周期失败。</exception>
        public UniTask<T> ShowAsync<T>(params object[] args) where T : FUIWindow
        {
            // 无调用方令牌重载：使用模块 lifetime token 驱动框架工作（spec: 无取消令牌打开窗口）。
            return ShowAsyncCore<T>(ModuleLifetimeToken, args);
        }

        /// <summary>
        /// 打开窗口（带调用方取消令牌）。
        /// <para>令牌只取消该调用方的等待，不得取消其他调用方共享的包加载或对象创建
        /// （spec: Show 操作可等待且错误明确；design.md 决策4）。</para>
        /// <para>共享加载任务只绑定模块 lifetime token；调用方令牌通过
        /// <see cref="UniTaskExtensions.AttachExternalCancellation"/> 只在外部取消调用方的等待，
        /// 不传播到共享加载任务（spec: 带取消令牌打开窗口——令牌 SHALL 只取消该调用方的等待）。</para>
        /// </summary>
        /// <typeparam name="T">最终业务窗口类型，必须为 <see cref="FUIWindow"/> 子类且已注册。</typeparam>
        /// <param name="cancellationToken">调用方取消令牌，只影响本次等待。</param>
        /// <param name="args">零个或多个用户参数。</param>
        /// <returns>已处于 Open 状态的非空业务窗口实例。</returns>
        /// <exception cref="OperationCanceledException">调用方或模块 lifetime 被取消。</exception>
        /// <exception cref="FUIException">注册缺失、包资源、对象构造或生命周期失败。</exception>
        public UniTask<T> ShowAsync<T>(CancellationToken cancellationToken, params object[] args) where T : FUIWindow
        {
            // 带调用方令牌重载：共享加载使用模块 lifetime token，调用方令牌只取消本次等待。
            // 通过 AttachExternalCancellation 实现：调用方取消时抛 OperationCanceledException，
            // 但不取消底层共享加载任务（spec: 令牌 SHALL 只取消该调用方的等待）。
            UniTask<T> core = ShowAsyncCore<T>(ModuleLifetimeToken, args);
            return core.AttachExternalCancellation(cancellationToken);
        }

        /// <summary>
        /// 隐藏指定类型的窗口。
        /// <para>Hide 不结束本轮打开域：只将状态从 Open 转为 Hidden，同步执行 OnHide 并改变显示与输入状态，
        /// 不取消 Open Token、不清事件、不执行 OnClose、不释放实例（spec: Hide 不结束打开域；design.md 决策5）。</para>
        /// <para>不递增 operation version：Hide 不是 Close，不使旧操作过期，Open 域继续有效，
        /// 后续 Show 可直接从 Hidden 回到 Open 并恢复显示（spec: 再次显示不重新建立打开域）。</para>
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <exception cref="FUIException">模块已 Shutdown、窗口未注册、未创建或不在可隐藏的状态。</exception>
        public void Hide<T>() where T : FUIWindow
        {
            ThrowIfShutdown();

            Type windowType = typeof(T);

            // 未注册的窗口类型无法隐藏：给出明确诊断（包含窗口类型上下文）。
            if (!_registry.TryGetDescriptor(windowType, out FUIDescriptor descriptor))
            {
                throw new FUIException(
                    $"Hide 失败：窗口类型未注册或已 Shutdown：{windowType.FullName}。" +
                    "请先完成 owner 注册并 FreezeBindings。");
            }

            // 未创建条目或处于 Absent/Disposed 终态时，Hide 为幂等空操作。
            if (!_windowEntries.TryGetValue(windowType, out WindowEntry entry) || entry == null)
            {
                return;
            }

            if (entry.State == FUIWindowState.Absent || entry.State == FUIWindowState.Disposed)
            {
                // 无存活实例，Hide 幂等返回。
                return;
            }

            HideEntryCore(entry, descriptor);
        }

        /// <summary>
        /// 隐藏指定窗口实例。
        /// <para>通过实例反查 <see cref="WindowEntry"/>，再执行与按类型隐藏相同的隐藏流程。
        /// 找不到对应条目时抛 <see cref="FUIException"/>（包含实例类型上下文）。</para>
        /// </summary>
        /// <param name="window">要隐藏的窗口实例，不得为 null。</param>
        /// <exception cref="FUIException">模块已 Shutdown、实例为 null 或找不到对应条目。</exception>
        public void Hide(FUIWindow window)
        {
            ThrowIfShutdown();

            if (window == null)
            {
                throw new FUIException("Hide 失败：window 实例不能为空。");
            }

            Type windowType = window.GetType();

            // 通过实例类型反查条目。
            if (!_windowEntries.TryGetValue(windowType, out WindowEntry entry) || entry == null)
            {
                throw new FUIException(
                    $"Hide 失败：找不到窗口实例对应的运行时条目：类型={windowType.FullName}。" +
                    "可能原因：实例未通过 FUIModule 创建或已 Shutdown。");
            }

            // 防御性校验：传入实例与条目持有的实例不一致，给出诊断（避免隐藏错误实例）。
            if (!ReferenceEquals(entry.Window, window))
            {
                throw new FUIException(
                    $"Hide 失败：传入实例与当前条目实例不一致：类型={windowType.FullName}, " +
                    $"URL={entry.Descriptor.URL}, 包名={entry.Descriptor.PackageName}, " +
                    $"当前操作版本={entry.OperationVersion}。" +
                    "可能原因：实例已被回滚或替换。");
            }

            // Absent/Disposed 终态幂等返回。
            if (entry.State == FUIWindowState.Absent || entry.State == FUIWindowState.Disposed)
            {
                return;
            }

            HideEntryCore(entry, entry.Descriptor);
        }

        /// <summary>
        /// 隐藏条目核心流程：状态从 Open 转为 Hidden，改变显示与输入状态，保留 Open 域。
        /// </summary>
        /// <param name="entry">窗口条目，已校验为非终态。</param>
        /// <param name="descriptor">窗口描述。</param>
        /// <remarks>
        /// 实现依据：spec "Hide 不结束打开域" 与 design.md 决策5。
        /// <para>
        /// Hide 只做三件事（design.md 决策5：临时隐藏 OnHide -&gt; visible/touchable false，保留 open CTS 与事件域）：
        /// 1. 同步执行 OnHide（若 <see cref="FUIWindow"/> 暴露了 OnHide 的内部触发入口）；
        /// 2. 将 visible 与 touchable 同时设为 false，改变显示与输入状态
        ///    （与 design.md 决策7 全屏遮挡使用相同策略：visible/touchable false 但不移出 Stage）；
        /// 3. 状态从 Open 转为 Hidden（<see cref="WindowEntry.TransitionTo"/> 校验合法性）。
        /// </para>
        /// <para>
        /// Hide 明确不做的事（spec: Hide 不结束打开域）：
        /// - 不取消 <see cref="WindowEntry.OpenCts"/>（Open Token 继续有效，业务异步任务不被取消）；
        /// - 不执行 <see cref="FUIWindow.InvokeClearOpenEvents"/>（本轮事件域保留）；
        /// - 不执行 <see cref="FUIWindow.InvokeOnClose"/>（不触发关闭回调）；
        /// - 不 Dispose 实例、不释放包租约（实例与租约保留）；
        /// - 不从显示树移除窗口（只切换 visible/touchable，避免触发 onRemovedFromStage 干扰 Widget 生命周期信号，
        ///   与 design.md 决策6 一致：onAddedToStage/onRemovedFromStage 不作为唯一 Open/Close 信号）；
        /// - 不递增 operation version（Hide 不是 Close，不使旧操作过期；后续 Show 从 Hidden 直接回到 Open）。
        /// </para>
        /// <para>
        /// 合法源状态：只有 <see cref="FUIWindowState.Open"/> 可转换到 <see cref="FUIWindowState.Hidden"/>
        /// （<see cref="WindowEntry"/> 状态机已定义）。其他非终态（Loading/Opening/Closing/Cached）抛
        /// <see cref="FUIException"/>，避免在非法状态下执行隐藏。Hidden 状态再次 Hide 为幂等空操作。
        /// </para>
        /// <para>
        /// OnHide 回调执行说明：<see cref="FUIWindow"/> 已声明 <c>protected virtual OnHide</c>，
        /// 并提供 internal <see cref="FUIWindow.InvokeOnHide"/> 入口（与 OnCreate/OnOpen/OnRefresh/OnClose 等同模式）。
        /// 本方法在切换 visible/touchable=false 之后、TransitionTo(Hidden) 之前调用 <see cref="FUIWindow.InvokeOnHide"/>，
        /// 执行同步 OnHide 回调；OnHide 仅改变显示与输入状态，不结束本轮打开域（不取消 Open Token、不清事件）。
        /// </para>
        /// </remarks>
        private void HideEntryCore(WindowEntry entry, FUIDescriptor descriptor)
        {
            // Hidden 状态再次 Hide：幂等空操作，不重复切换显示状态。
            if (entry.State == FUIWindowState.Hidden)
            {
                return;
            }

            // 只有 Open 可转换到 Hidden（WindowEntry 状态机已定义 Open -> Hidden）。
            // 其他非终态（Loading/Opening/Closing/Cached）抛 FUIException，避免在非法状态下隐藏。
            if (entry.State != FUIWindowState.Open)
            {
                throw new FUIException(
                    $"Hide 失败：窗口 {entry.WindowType.FullName} 当前状态为 {entry.State}，" +
                    $"只有 Open 状态可 Hide。URL={descriptor.URL}, 包名={descriptor.PackageName}, " +
                    $"当前操作版本={entry.OperationVersion}。" +
                    "请先通过 ShowAsync 打开窗口后再 Hide。");
            }

            FUIWindow window = entry.Window;
            if (window == null)
            {
                throw new FUIException(
                    $"Hide 失败：窗口 {entry.WindowType.FullName} 实例为空但状态为 Open（数据不一致）。" +
                    $"URL={descriptor.URL}, 包名={descriptor.PackageName}。");
            }

            // 1. 改变显示与输入状态：visible/touchable 设为 false（design.md 决策5/7）。
            //    不移出 Stage，避免触发 onRemovedFromStage 干扰 Widget 生命周期信号（design.md 决策6）。
            //    FairyGUI 的 visible=false 会自动使对象 untouchable（见 GObject.visible 注释），
            //    这里同时显式设置 touchable=false 以保证语义清晰与全屏遮挡策略一致。
            try
            {
                window.visible = false;
                window.touchable = false;
            }
            catch (Exception)
            {
                // 显示状态切换异常不中断 Hide 流程，状态转换仍需执行以保证状态机一致。
            }

            // 2. 同步执行 OnHide 回调（design.md 决策5：临时隐藏 OnHide -> visible/touchable false，
            //    保留 open CTS 与事件域；spec "Hide 不结束打开域"：系统 SHALL 执行同步 OnHide 并改变显示与输入状态）。
            //    与已建立的 InvokeOnCreate/InvokeOnOpen/InvokeOnRefresh/InvokeClearOpenEvents/InvokeOnClose 同模式。
            //    OnHide 仅改变显示与输入状态，不结束本轮打开域（不取消 Open Token、不清事件）。
            window.InvokeOnHide();

            // 3. 状态从 Open 转为 Hidden（WindowEntry.TransitionTo 校验合法性）。
            FUIWindowState stateBeforeHide = entry.State;
            entry.TransitionTo(FUIWindowState.Hidden);
            LogWindowStateTransition(entry, stateBeforeHide);

            // 5.11：Hide 后统一重新计算全屏遮挡（design.md 决策7；spec: 关闭/隐藏顶部全屏窗口后恢复下层）。
            // Hidden 状态的全屏窗口不再参与遮挡，若它是唯一遮挡下层的全屏窗口，下层将恢复可见。
            RecomputeFullScreenOcclusion();
        }

        /// <summary>
        /// 隐藏窗口再次显示时恢复显示状态并从 Hidden 转回 Open（spec: Hide 不结束打开域——再次显示）。
        /// </summary>
        /// <param name="entry">窗口条目，已处于 <see cref="FUIWindowState.Hidden"/>。</param>
        /// <param name="descriptor">窗口描述。</param>
        /// <remarks>
        /// 实现依据：spec "Hide 不结束打开域"——"一个 Open 窗口被 Hide 后再次显示" 时，
        /// 系统 SHALL 恢复显示与输入状态，但 SHALL 不重新建立打开域、不重新执行 OnOpen 或重新注册事件。
        /// <para>
        /// Hide 保留了 OpenCts 与事件域（见 <see cref="HideEntryCore"/>），因此再次显示只需：
        /// 1. 恢复 visible/touchable 为 true（撤销 Hide 时设置的 false）；
        /// 2. 状态从 Hidden 转回 Open（<see cref="WindowEntry.TransitionTo"/> 校验 Hidden -&gt; Open 合法性）。
        /// </para>
        /// <para>
        /// 不调用 OnOpen / RegisterOpenEvents / 不创建新 OpenCts：
        /// 本轮打开域自首次 Show 建立后一直有效，Hide 只是临时隐藏显示，不结束打开域。
        /// 这与 Cached 再打开（<see cref="ExecuteOpenLifecycle"/>）不同：
        /// Cached 窗口已经 Close 过，打开域已清理，需要重新建立；Hidden 窗口未 Close，打开域仍有效。
        /// </para>
        /// <para>
        /// 窗口仍应保留在原层级容器中（Hide 没有移出 Stage），因此本方法不重新挂载。
        /// 后续 Show 请求的 OnRefresh 由 <see cref="ProcessRefreshQueue"/> 统一处理。
        /// </para>
        /// </remarks>
        private void RestoreVisibilityAndTransitionToOpen(WindowEntry entry, FUIDescriptor descriptor)
        {
            FUIWindow window = entry.Window;
            if (window == null)
            {
                throw new FUIException(
                    $"ShowAsync 失败：窗口 {entry.WindowType.FullName} 实例为空但状态为 Hidden（数据不一致）。" +
                    $"URL={descriptor.URL}, 包名={descriptor.PackageName}。");
            }

            // 1. 恢复显示与输入状态：visible/touchable 设为 true（撤销 Hide 时的 false）。
            try
            {
                window.visible = true;
                window.touchable = true;
            }
            catch (Exception)
            {
                // 显示状态切换异常不中断 Show 流程，状态转换仍需执行以保证状态机一致。
            }

            // 5.11：隐藏窗口再次显示时置顶到所属子容器末尾，保证重排后渲染在同级最顶层（层内排序）。
            BringWindowToTop(entry);

            // 2. 状态从 Hidden 转回 Open（WindowEntry.TransitionTo 校验 Hidden -> Open 合法性）。
            FUIWindowState stateBeforeRestore = entry.State;
            entry.TransitionTo(FUIWindowState.Open);
            LogWindowStateTransition(entry, stateBeforeRestore);

            // 5.11：再次显示进入 Open 后统一重新计算全屏遮挡。
            // 若本窗口为全屏窗口，恢复显示后应遮挡下层；若其他全屏窗口仍在 Open，保持现有遮挡关系。
            RecomputeFullScreenOcclusion();
        }

        /// <summary>
        /// 关闭指定类型的窗口。
        /// <para>在任何非终态递增 operation version，使旧操作完成后只能回滚，不能显示
        /// （design.md 决策4：Close 在任何非终态都会递增 version）。</para>
        /// <para>按缓存策略决定最终状态：默认 None 进入 Disposed 并释放实例与租约；
        /// 显式 Cache 进入 Cached 保留实例与租约（spec: 默认关闭即释放且缓存显式启用）。</para>
        /// <para>加载期间 Close 只递增 version 不转换状态（状态机不允许 Loading -&gt; Closing），
        /// 加载完成时由 <see cref="ExecuteLoadAndOpenAsync"/> 检测版本过期并回滚
        /// （spec: 加载期间 Close——旧打开操作 SHALL 失效）。</para>
        /// <para>每轮关闭同步执行 Cancel OpenCts -> ClearOpenEvents -> OnClose（5.8 实现）。
        /// 完整最终释放顺序（Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease）
        /// 由 <see cref="RollbackLoadedInstance"/> 在 Disposed 终态分支执行（5.10 实现）。</para>
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <exception cref="FUIException">模块已 Shutdown 或窗口未注册。</exception>
        public void Close<T>() where T : FUIWindow
        {
            ThrowIfShutdown();

            Type windowType = typeof(T);

            // 未注册的窗口类型无法关闭：给出明确诊断（包含窗口类型上下文）。
            if (!_registry.TryGetDescriptor(windowType, out FUIDescriptor descriptor))
            {
                throw new FUIException(
                    $"Close 失败：窗口类型未注册或已 Shutdown：{windowType.FullName}。" +
                    "请先完成 owner 注册并 FreezeBindings。");
            }

            // 未创建条目或处于 Absent/Disposed 终态时，Close 为幂等空操作，不递增 version。
            if (!_windowEntries.TryGetValue(windowType, out WindowEntry entry) || entry == null)
            {
                return;
            }

            if (entry.State == FUIWindowState.Absent || entry.State == FUIWindowState.Disposed)
            {
                // 无存活实例，Close 幂等返回。
                return;
            }

            CloseEntryCore(entry, descriptor);
        }

        /// <summary>
        /// 关闭指定窗口实例。
        /// <para>通过实例反查 <see cref="WindowEntry"/>，再执行与按类型关闭相同的关闭流程。
        /// 找不到对应条目时抛 <see cref="FUIException"/>（包含实例类型上下文）。</para>
        /// </summary>
        /// <param name="window">要关闭的窗口实例，不得为 null。</param>
        /// <exception cref="FUIException">模块已 Shutdown、实例为 null 或找不到对应条目。</exception>
        public void Close(FUIWindow window)
        {
            ThrowIfShutdown();

            if (window == null)
            {
                throw new FUIException("Close 失败：window 实例不能为空。");
            }

            Type windowType = window.GetType();

            // 通过实例类型反查条目。
            if (!_windowEntries.TryGetValue(windowType, out WindowEntry entry) || entry == null)
            {
                throw new FUIException(
                    $"Close 失败：找不到窗口实例对应的运行时条目：类型={windowType.FullName}。" +
                    "可能原因：实例未通过 FUIModule 创建或已 Shutdown。");
            }

            // 防御性校验：传入实例与条目持有的实例不一致，给出诊断（避免关闭错误实例）。
            if (!ReferenceEquals(entry.Window, window))
            {
                throw new FUIException(
                    $"Close 失败：传入实例与当前条目实例不一致：类型={windowType.FullName}, " +
                    $"URL={entry.Descriptor.URL}, 包名={entry.Descriptor.PackageName}, " +
                    $"当前操作版本={entry.OperationVersion}。" +
                    "可能原因：实例已被回滚或替换。");
            }

            // Absent/Disposed 终态幂等返回。
            if (entry.State == FUIWindowState.Absent || entry.State == FUIWindowState.Disposed)
            {
                return;
            }

            CloseEntryCore(entry, entry.Descriptor);
        }

        /// <summary>
        /// 关闭条目核心流程：递增 version、取消 Open 域、状态转换、清事件、执行 OnClose，按缓存策略释放或保留实例。
        /// </summary>
        /// <param name="entry">窗口条目，已校验为非终态。</param>
        /// <param name="descriptor">窗口描述。</param>
        /// <remarks>
        /// 顺序固定为（design.md 决策4/5）：
        /// 1. <see cref="WindowEntry.IncrementOperationVersion"/>：在任何非终态递增 version，
        ///    使正在进行的加载操作完成后通过 <see cref="WindowEntry.IsOperationStale"/> 检测到过期并回滚
        ///    （spec: 加载期间 Close——旧打开操作 SHALL 失效，完成后的过期对象不得闪现）。
        ///    本步最先执行，保证后续清理不会被迟到的加载回调覆盖；
        /// 2. 取消 OpenCts：使窗口自行启动的业务异步任务收到取消（design.md 决策5：Close 时取消 Open Token）。
        ///    Loading 状态下尚未创建 OpenCts，跳过；
        /// 3. 状态转换与资源释放：
        ///    - Loading/Opening：不转换到 Closing（状态机不允许），仅递增 version 即可。
        ///      加载/打开完成后由 <see cref="ExecuteLoadAndOpenAsync"/> 的版本校验回滚到 Absent；
        ///    - Open/Hidden：TransitionTo(Closing)，执行 ClearOpenEvents -> OnClose，从显示树移除，
        ///      按缓存策略 TransitionTo(Cached/Disposed)；
        ///    - Closing：防御性幂等返回（同步 Close 不应到达此状态）；
        ///    - Cached：最终释放，TransitionTo(Disposed) 并释放实例与租约；
        /// 4. Disposed 终态：通过 <see cref="RollbackLoadedInstance"/> 执行完整释放顺序
        ///    （Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease，
        ///    任务 5.10 实现）。实例已 attach（<see cref="WindowEntry.IsInstanceLifecycleAttached"/> 为 true），
        ///    故走完整释放路径。
        /// <para>
        /// design.md 决策5 每轮关闭顺序：Cancel open CTS -> ClearOpenEvents -> OnClose。
        /// Cancel 在第 2 步统一执行（含 Loading 跳过场景），ClearOpenEvents 与 OnClose 在 Open/Hidden 分支内执行。
        /// 完整的 OnDispose / Descriptor.Detach / Dispose Widgets 由 <see cref="RollbackLoadedInstance"/> 在
        /// Disposed/Cached 最终释放分支执行（任务 5.10）。
        /// </para>
        /// </remarks>
        private void CloseEntryCore(WindowEntry entry, FUIDescriptor descriptor)
        {
            FUIWindowState stateBeforeClose = entry.State;

            // 1. 递增 operation version（design.md 决策4：Close 在任何非终态都会递增 version）。
            //    使正在进行的加载操作完成后检测到版本过期并回滚，不能显示
            //    （spec: 加载期间 Close——旧打开操作 SHALL 失效）。
            //    版本递增最先执行，保证后续清理不会被迟到的加载回调覆盖。
            entry.IncrementOperationVersion();

            // 2. 取消 OpenCts：使窗口自行启动的业务异步任务收到取消（design.md 决策5）。
            //    Loading 状态下尚未创建 OpenCts，跳过。
            CancelAndDisposeOpenCts(entry);

            // 3. 状态转换与资源释放。
            if (stateBeforeClose == FUIWindowState.Loading || stateBeforeClose == FUIWindowState.Opening)
            {
                // 加载/打开期间 Close：不转换到 Closing（状态机不允许 Loading/Opening -> Closing）。
                // 仅递增 version（已在上一步完成），加载/打开完成后由 ExecuteLoadAndOpenAsync 的版本校验回滚到 Absent。
                // sharedTask 的合并等待方通过 TrySetException 收到取消，不会显示过期实例
                // （spec: 加载期间 Close——完成后的过期对象不得闪现或执行过期回调）。
                // 注意：不在此处清空 SharedCreateTask：加载器仍在运行并持有本地引用，
                // 清空会使期间到达的新 Show 在合并分支读到 null 导致 NRE。
                // SharedCreateTask 的清空由加载器 catch 块统一完成（幂等）。
                return;
            }

            if (stateBeforeClose == FUIWindowState.Open || stateBeforeClose == FUIWindowState.Hidden)
            {
                // Open/Hidden -> Closing -> Cached/Disposed。
                FUIWindowState stateBeforeClosing = entry.State;
                entry.TransitionTo(FUIWindowState.Closing);
                LogWindowStateTransition(entry, stateBeforeClosing);

                // 每轮关闭同步生命周期：ClearOpenEvents -> OnClose（design.md 决策5）。
                // OpenCts 已在第 2 步取消并释放，这里执行事件清理与 OnClose 回调。
                // spec: 默认关闭即释放且缓存显式启用——普通 Close SHALL 执行 OnClose。
                // spec: 缓存窗口关闭——SHALL 清理本轮打开域（含事件域）。
                FUIWindow windowToClose = entry.Window;
                if (windowToClose != null)
                {
                    windowToClose.InvokeClearOpenEvents();
                    windowToClose.InvokeOnClose();
                }

                // 从显示树移除窗口。
                RemoveFromDisplay(entry);

                // 按缓存策略决定最终状态（spec: 默认关闭即释放且缓存显式启用）。
                if (descriptor.CacheMode == FUICacheMode.Cache)
                {
                    // 缓存模式：保留实例与租约，进入 Cached。
                    // Cached 状态的再打开由 ShowAsyncCore 的 isCachedReopen 分支与 ExecuteOpenLifecycle 处理
                    // （Cached -> Opening -> Open，重新建立打开域但不重复 OnCreate）。
                    // Cached 状态的最终释放由 CloseEntryCore 的 Cached 分支处理（Cached -> Disposed）。
                    FUIWindowState stateBeforeCached = entry.State;
                    entry.TransitionTo(FUIWindowState.Cached);
                    LogWindowStateTransition(entry, stateBeforeCached);
                }
                else
                {
                    // 默认 None：最终释放实例与租约，进入 Disposed 终态。
                    FUIWindowState stateBeforeDisposed = entry.State;
                    entry.TransitionTo(FUIWindowState.Disposed);
                    LogWindowStateTransition(entry, stateBeforeDisposed);
                    RollbackLoadedInstance(entry);
                }

                // 5.11：Close 完成后统一重新计算全屏遮挡（design.md 决策7；spec: 关闭顶部全屏窗口后恢复下层）。
                // 关闭的窗口不再处于 Open，若它是遮挡下层的全屏窗口，下层将恢复可见。
                RecomputeFullScreenOcclusion();

                return;
            }

            if (stateBeforeClose == FUIWindowState.Closing)
            {
                // 同步 Close 不应到达 Closing 状态（前一次 Close 已同步推进到 Cached/Disposed）。
                // 防御性幂等返回，避免重复清理。
                return;
            }

            if (stateBeforeClose == FUIWindowState.Cached)
            {
                // Cached 状态的 Close 为最终释放：TransitionTo(Disposed) 并释放实例与租约。
                // Cached -> Disposed 是合法转换（WindowEntry 状态机已定义）。
                FUIWindowState stateBeforeFinalDispose = entry.State;
                entry.TransitionTo(FUIWindowState.Disposed);
                LogWindowStateTransition(entry, stateBeforeFinalDispose);
                RollbackLoadedInstance(entry);

                // 5.11：Cached 窗口最终释放后统一重新计算全屏遮挡（幂等，Cached 窗口本就不在显示树，通常无变化）。
                RecomputeFullScreenOcclusion();
                return;
            }
        }

        /// <summary>
        /// 取消并释放窗口的 OpenCts，使窗口自行启动的业务异步任务收到取消。
        /// </summary>
        /// <param name="entry">窗口条目。</param>
        /// <remarks>
        /// design.md 决策5：Close 时取消 Open Token。本方法幂等，OpenCts 为 null 时跳过。
        /// 取消后 Dispose CTS 释放资源；异常被吞掉以保证清理不中断。
        /// </remarks>
        private void CancelAndDisposeOpenCts(WindowEntry entry)
        {
            CancellationTokenSource openCts = entry.OpenCts;
            if (openCts == null)
            {
                return;
            }

            try
            {
                openCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS 已被 Dispose，忽略取消异常。
            }

            try
            {
                openCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // CTS 已被 Dispose，忽略 Dispose 异常。
            }

            entry.OpenCts = null;
        }

        /// <summary>
        /// 从显示树移除窗口实例（若仍在显示树中）。
        /// </summary>
        /// <param name="entry">窗口条目。</param>
        /// <remarks>
        /// 移除异常被吞掉以保证 Close 流程不中断。
        /// 使用 RemoveChild(dispose:false) 仅移除引用，不在此处 Dispose GObject
        /// （Dispose 由 <see cref="RollbackLoadedInstance"/> 统一处理，避免重复释放）。
        /// <para>任务 6.3 验证：RemoveChild 只从显示树移除窗口，不触发状态转换——
        /// Close 的状态转换由调用方 <see cref="CloseEntryCore"/> 显式 TransitionTo 驱动。
        /// 虽然移除会触发 FairyGUI 的 <c>onRemovedFromStage</c>，但框架不订阅该事件作为 Close 信号
        /// （design.md 决策6：onAddedToStage/onRemovedFromStage 不作为唯一 Open/Close 信号）。</para>
        /// </remarks>
        private void RemoveFromDisplay(WindowEntry entry)
        {
            FUIWindow window = entry.Window;
            if (window == null)
            {
                return;
            }

            try
            {
                if (window.parent != null)
                {
                    window.parent.RemoveChild(window, false);
                }
            }
            catch (Exception)
            {
                // 移除异常不中断 Close 流程。
            }
        }

        /// <summary>
        /// 查询指定类型窗口的当前实例。
        /// <para>依据 <see cref="_windowEntries"/> 查询 T 对应的 <see cref="WindowEntry"/>，返回其当前窗口实例。
        /// 仅查询已注册实例，不隐式注册或创建（spec: getter 只返回已注册实例）。
        /// 存活状态（Loading/Opening/Open/Hidden/Closing）与缓存状态（Cached）下返回当前实例，
        /// 实例尚未构造完成（如 Loading 早期）时为 null；Absent/Disposed 终态返回 null。
        /// 类型不匹配时返回 null，不抛异常。模块 Shutdown 后字典已清空，自然返回 null。</para>
        /// <para>存活判断与 <see cref="HasWindow{T}"/> 完全一致。</para>
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>当前实例，未创建或已释放时为 null。</returns>
        public T GetWindow<T>() where T : FUIWindow
        {
            Type windowType = typeof(T);

            // 未注册条目：无实例，返回 null。模块 Shutdown 后 _windowEntries 已清空，同样走到这里返回 null。
            if (!_windowEntries.TryGetValue(windowType, out WindowEntry entry) || entry == null)
            {
                return null;
            }

            // Absent/Disposed 终态：无当前实例，返回 null。
            // 与 HasWindow<T> 使用一致的存活判断：Loading/Opening/Open/Hidden/Closing/Cached 视为存在当前实例。
            FUIWindowState state = entry.State;
            if (state == FUIWindowState.Absent || state == FUIWindowState.Disposed)
            {
                return null;
            }

            // 返回当前实例并转换为 T；实例尚未构造完成（如 Loading 早期）或类型不匹配时 as 返回 null，不抛异常。
            return entry.Window as T;
        }

        /// <summary>
        /// 查询指定类型窗口是否存在当前实例。
        /// <para>存活状态（Loading/Opening/Open/Hidden/Closing）与缓存状态（Cached）算存在；
        /// Absent/Disposed 终态算不存在。模块 Shutdown 后字典已清空，自然返回 false。</para>
        /// <para>存活判断与 <see cref="GetWindow{T}"/> 完全一致。</para>
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>存在当前实例返回 true，否则 false。</returns>
        public bool HasWindow<T>() where T : FUIWindow
        {
            Type windowType = typeof(T);

            if (!_windowEntries.TryGetValue(windowType, out WindowEntry entry) || entry == null)
            {
                return false;
            }

            // 与 GetWindow<T> 一致：Absent/Disposed 不算存在，其余状态视为存在当前实例。
            FUIWindowState state = entry.State;
            return state != FUIWindowState.Absent && state != FUIWindowState.Disposed;
        }

        /// <summary>
        /// Show 重载共享的核心实现：状态机驱动加载、创建、打开与刷新（design.md 决策4）。
        /// </summary>
        /// <typeparam name="T">最终业务窗口类型。</typeparam>
        /// <param name="lifetimeToken">模块 lifetime token，驱动共享加载任务生命周期。</param>
        /// <param name="args">用户参数，进入 FIFO 刷新队列后更新 UserDatas/UserData 并执行 OnRefresh。</param>
        /// <returns>已处于 Open 状态的非空业务窗口实例。</returns>
        /// <remarks>
        /// 本方法实现以下契约：
        /// <list type="bullet">
        /// <item>同类型加载/创建任务合并：首个请求建立 <see cref="WindowEntry.SharedCreateTask"/>，
        ///   后续请求 await 同一任务（spec: 同类型并发打开串行收敛——只创建一个实例）。</item>
        /// <item>FIFO 请求队列：每个有效请求按到达顺序进入 <see cref="WindowEntry.PendingRefreshArgs"/>，
        ///   首个请求驱动加载与打开，后续请求在窗口 Open 后各执行一次刷新
        ///   （spec: 有效请求 SHALL 按进入顺序执行刷新）。</item>
        /// <item>每个有效请求先更新 UserDatas/UserData 再同步刷新
        ///   （spec: 每个刷新请求执行前 SHALL 更新 UserDatas 和 UserData）。</item>
        /// <item>operation version 校验：加载完成后若版本过期（如期间收到 Close），回滚本次操作取得的资源
        ///   （spec: 加载期间 Close——旧打开操作 SHALL 失效）。</item>
        /// </list>
        /// 调用方令牌的取消语义由外层 <see cref="ShowAsync{T}(CancellationToken, object[])"/> 通过
        /// <see cref="UniTaskExtensions.AttachExternalCancellation"/> 注入；本核心方法只使用模块 lifetime token。
        /// </remarks>
        private async UniTask<T> ShowAsyncCore<T>(CancellationToken lifetimeToken, object[] args) where T : FUIWindow
        {
            ThrowIfShutdown();

            // FreezeBindings 完成校验：层容器为 null 表示尚未冻结（未安装 creator、未建立层级），
            // 此时不允许 Show，避免后续 ExecuteLoadAndOpenAsync/ExecuteOpenLifecycle 访问 _layerContainer 触发 NRE。
            if (_layerContainer == null)
            {
                throw new FUIException(
                    "ShowAsync 失败：GameFUI 模块尚未完成 FreezeBindings（层容器未创建）。" +
                    "请先调用 FUI.Module.FreezeBindings() 再打开窗口。");
            }

            Type windowType = typeof(T);

            // 从注册表按 TargetType 查询描述；未注册或已 Shutdown 时抛 FUIException。
            if (!_registry.TryGetDescriptor(windowType, out FUIDescriptor descriptor))
            {
                throw new FUIException(
                    $"ShowAsync 失败：窗口类型未注册或已 Shutdown：{windowType.FullName}。" +
                    "请先完成 owner 注册并 FreezeBindings。");
            }

            // 防御性校验：TargetType 必须是 FUIWindow 子类，避免注册阶段写入错误类型。
            if (!typeof(FUIWindow).IsAssignableFrom(windowType))
            {
                throw new FUIException(
                    $"ShowAsync 失败：类型 {windowType.FullName} 不是 FUIWindow 子类，无法作为窗口打开。");
            }

            // 获取或创建 WindowEntry。Disposed 终态后重建新 entry 回到 Absent
            // （spec: 窗口状态转换受控——Disposed 是终态，类型在下一次 Show 时从新 Absent entry 创建新实例）。
            WindowEntry entry = GetOrCreateEntry(windowType, descriptor);

            // 本次请求的用户参数进入 FIFO 刷新队列：即使本次请求在等待共享加载期间，
            // 后到的请求也会按到达顺序排在后面，保证刷新顺序与请求到达一致
            // （spec: 有效请求 SHALL 按进入顺序执行刷新）。
            // 复制一份避免调用方修改原数组影响队列中的快照。
            object[] argsSnapshot = args == null ? Array.Empty<object>() : (object[])args.Clone();
            entry.PendingRefreshArgs.Enqueue(argsSnapshot);

            // 捕获当前 operation version 快照（不递增）：本操作完成后若版本已变化，说明期间收到 Close 等操作，
            // 本操作为过期操作，只能回滚（spec: 加载期间 Close——旧打开操作 SHALL 失效）。
            // 注意：design.md 决策4 明确"Close 在任何非终态都会递增 version，使旧操作完成后只能回滚"，
            // 递增责任在 Close/重开，而非每个 Show。Show 不递增 version，保证同类型并发 Show 共享同一快照，
            // loader 完成后版本校验通过，合并等待方拿到同一实例（spec: 同类型并发打开串行收敛）。
            // 若 Show 也递增，则第二个并发 Show 会使第一个 loader 的快照立即过期，两者互相取消，与 spec 冲突。
            long operationVersion = entry.OperationVersion;

            // 分流处理不同起始状态。
            // Absent: 本请求成为加载执行者，建立 SharedCreateTask 供并发请求合并。
            // Cached: 缓存窗口再打开，重新建立打开域（不重复 OnCreate）。
            // Loading/Opening: 已有并发请求在驱动，本请求 await 同一 SharedCreateTask 或等待状态推进。
            // Open: 实例已就绪，本请求只参与 FIFO 刷新。
            // Hidden: 隐藏窗口再次显示，恢复显示状态并转回 Open（不重新建立打开域，spec: Hide 不结束打开域）。
            // Closing: 等待本轮 Close 完成后再从 Cached/Absent 重新打开（spec: Closing 期间再次 Show）。

            bool isLoader = false;
            bool isCachedReopen = false;
            bool isHiddenRedispatch = false;
            UniTaskCompletionSource<FUIWindow> sharedTask = entry.SharedCreateTask;

            if (entry.State == FUIWindowState.Absent)
            {
                // 本请求成为加载执行者：创建 SharedCreateTask 供后续并发请求合并等待。
                FUIWindowState stateBeforeLoading = entry.State;
                entry.TransitionTo(FUIWindowState.Loading);
                LogWindowStateTransition(entry, stateBeforeLoading);
                sharedTask = new UniTaskCompletionSource<FUIWindow>();
                entry.SharedCreateTask = sharedTask;
                isLoader = true;
            }
            else if (entry.State == FUIWindowState.Cached)
            {
                // 缓存窗口再打开：重新建立打开域，不重复 OnCreate（spec: 缓存窗口重新打开）。
                // 直接进入 Opening，不创建 SharedCreateTask（实例已存在）。
                FUIWindowState stateBeforeReopen = entry.State;
                entry.TransitionTo(FUIWindowState.Opening);
                LogWindowStateTransition(entry, stateBeforeReopen);
                isCachedReopen = true;
            }
            else if (entry.State == FUIWindowState.Loading)
            {
                // 同类型加载合并：await 同一 SharedCreateTask，不重复加载
                // （spec: 同类型并发打开串行收敛——只创建一个实例）。
                // sharedTask 此时一定非 null（加载执行者已创建）。
            }
            else if (entry.State == FUIWindowState.Opening || entry.State == FUIWindowState.Open)
            {
                // 实例已就绪或正在打开：本请求只参与 FIFO 刷新，无需加载。
            }
            else if (entry.State == FUIWindowState.Hidden)
            {
                // 隐藏窗口再次显示（spec: Hide 不结束打开域——再次显示 SHALL 恢复显示与输入状态，
                // 但 SHALL 不重新建立打开域、不重新执行 OnOpen 或重新注册事件）。
                // Hide 保留了 OpenCts 与事件域，因此这里只需恢复显示状态并转回 Open。
                isHiddenRedispatch = true;
            }
            else if (entry.State == FUIWindowState.Closing)
            {
                // Closing 期间的新 Show 排在 Close 之后，不复活旧打开域
                // （spec: Closing 期间再次 Show——新 Show SHALL 等待本轮 Close 完成后再从 Cached 或 Absent 重新打开）。
                // 5.6 已实现同步 Close：Close 会同步推进到 Cached/Disposed，因此 Show 不应观察到 Closing 状态。
                // 此处为防御性占位：若因时序异常到达 Closing，抛出包含上下文的 FUIException，避免在未就绪状态下错误推进。
                throw new FUIException(
                    $"ShowAsync 失败：窗口 {windowType.FullName} 正在 Closing，无法在此期间 Show。" +
                    $"URL={descriptor.URL}, 包名={descriptor.PackageName}, " +
                    $"当前状态={entry.State}, 当前操作版本={entry.OperationVersion}。" +
                    "请等待 Close 完成后再重新 Show。");
            }

            FUIWindow window;

            if (isLoader)
            {
                // 加载执行者：执行包加载、实例创建与首次打开生命周期。
                window = await ExecuteLoadAndOpenAsync(entry, descriptor, lifetimeToken, operationVersion, sharedTask);
            }
            else if (isCachedReopen)
            {
                // Cached 再打开：执行每轮打开生命周期（不重复 OnCreate）。
                window = entry.Window;
                ExecuteOpenLifecycle(entry, descriptor);
            }
            else if (isHiddenRedispatch)
            {
                // 隐藏窗口再次显示：恢复显示状态并转回 Open（spec: Hide 不结束打开域）。
                // Hide 保留了 OpenCts 与事件域，这里不重新创建 OpenCts、不重新注册事件、不重新执行 OnOpen。
                // 只恢复 visible/touchable 并转换状态，之后由 ProcessRefreshQueue 统一处理刷新。
                window = entry.Window;
                RestoreVisibilityAndTransitionToOpen(entry, descriptor);
            }
            else if (entry.State == FUIWindowState.Loading)
            {
                // 合并等待方：await 共享创建任务，完成后各自参与 FIFO 刷新
                // （spec: 同类型并发打开串行收敛——按请求顺序对该实例各执行一次刷新）。
                // 共享加载只受模块 lifetime token 控制，调用方令牌不传播到此处
                // （spec: 调用方取消只取消自己的等待）。
                window = await sharedTask.Task;

                // 加载完成后检查 operation version：若期间收到 Close 使版本过期，
                // 本请求不再显示，只回滚本次不需要的资源（实例由加载执行者管理）
                // （spec: 加载期间 Close——旧打开操作 SHALL 失效，完成后的过期对象不得闪现或执行过期回调）。
                if (entry.IsOperationStale(operationVersion))
                {
                    throw new OperationCanceledException(
                        $"窗口 Show 请求因操作版本过期而取消：类型={windowType.FullName}, " +
                        $"URL={descriptor.URL}, 包名={descriptor.PackageName}, " +
                        $"操作版本快照={operationVersion}, 当前版本={entry.OperationVersion}。" +
                        "期间可能收到 Close 或重开。");
                }
            }
            else
            {
                // Opening/Open：实例已就绪，本请求只参与 FIFO 刷新。
                window = entry.Window;

                // 5.11：对已 Open 窗口再次 Show 时置顶到所属子容器末尾，实现层内排序（后打开的在上层）。
                // 只在 Open 状态置顶；Opening 状态下窗口尚未挂载完成，由 ExecuteLoadAndOpenAsync 完成后统一置顶。
                if (entry.State == FUIWindowState.Open)
                {
                    BringWindowToTop(entry);
                }
            }

            // 窗口已就绪：处理本请求在 FIFO 队列中的刷新机会。
            // 每个有效请求先更新 UserDatas/UserData 再同步 OnRefresh
            // （spec: 每个刷新请求执行前 SHALL 更新 UserDatas 和 UserData）。
            ProcessRefreshQueue(entry);

            // 校验窗口仍处于有效状态后返回。
            if (window == null)
            {
                throw new FUIException($"ShowAsync 失败：窗口 {windowType.FullName} 实例为空。");
            }

            if (entry.IsOperationStale(operationVersion))
            {
                // 刷新期间版本过期（如其他操作递增了版本），本次返回取消。
                throw new OperationCanceledException(
                    $"窗口 Show 请求在刷新后因操作版本过期而取消：类型={windowType.FullName}, " +
                    $"URL={descriptor.URL}, 包名={descriptor.PackageName}, " +
                    $"操作版本快照={operationVersion}, 当前版本={entry.OperationVersion}。");
            }

            return (T)window;
        }

        /// <summary>
        /// 加载执行者：执行包加载、实例创建、首次打开生命周期，并向共享任务传播结果。
        /// </summary>
        /// <param name="entry">窗口条目，已处于 <see cref="FUIWindowState.Loading"/>。</param>
        /// <param name="descriptor">窗口描述。</param>
        /// <param name="lifetimeToken">模块 lifetime token，驱动共享加载任务生命周期。</param>
        /// <param name="operationVersion">本次操作的版本快照。</param>
        /// <param name="sharedTask">共享创建任务，成功时 TrySetResult，失败时 TrySetException。</param>
        /// <returns>已处于 Open 状态的窗口实例。</returns>
        /// <remarks>
        /// 顺序固定为（design.md 决策5）：
        /// 1. <see cref="PackageLoader.AcquireAsync"/> 获取包租约（共享加载只受 lifetimeToken 控制）；
        /// 2. <see cref="UIPackage.CreateObject"/> 构造最终业务类型实例（由全局无状态 creator 查询 Registry）；
        /// 3. 校验版本：若期间 Close 使版本过期，回滚本次取得的租约与实例，不显示；
        /// 4. AttachContext -> Descriptor.Attach -> AttachWidgetTree -> OnCreate（一次性同步实例生命周期，任务 5.7）；
        /// 5. TransitionTo(Opening)；
        /// 6. 创建 OpenCts、SetOpenCancellationToken（每轮 Open 创建事件域与 OpenCancellationToken，任务 5.7）；
        /// 7. 挂载到层容器（5.11 将补充层内排序与全屏遮挡）；
        /// 8. RegisterOpenEvents -> OnOpen（每轮打开同步生命周期，任务 5.8）；
        /// 9. TransitionTo(Open)；
        /// 10. TrySetResult 向合并等待方传播结果。
        /// 失败时 TrySetException 向所有合并等待方传播异常，并回滚本次取得的资源。
        /// </remarks>
        private async UniTask<FUIWindow> ExecuteLoadAndOpenAsync(
            WindowEntry entry,
            FUIDescriptor descriptor,
            CancellationToken lifetimeToken,
            long operationVersion,
            UniTaskCompletionSource<FUIWindow> sharedTask)
        {
            FUIWindow window = null;
            PackageLease lease = null;

            try
            {
                // 1. 包加载：使用模块 lifetime token，不绑定单个调用方令牌
                // （spec: 调用方取消只取消自己的等待；共享加载只受模块 lifetime token 控制）。
                lease = await PackageLoader.AcquireAsync(descriptor.PackageName, _resourceProvider, lifetimeToken);

                // 模块 Shutdown 期间 lifetimeToken 被取消时，AcquireAsync 抛 OperationCanceledException，
                // 由下方 catch 捕获并向合并等待方传播。

                // 2. 实例创建：通过全局无状态 creator 查询 Registry，返回最终业务类型
                // （spec: 业务类型覆盖生成类型；design.md 决策2）。
                // CreateObjectFromURL 会触发 UIObjectFactory 的 creator，creator 查询活动 Registry 的描述。
                GObject rawObject = UIPackage.CreateObjectFromURL(descriptor.URL);
                if (rawObject == null)
                {
                    throw new FUIException(
                        $"ShowAsync 失败：CreateObjectFromURL 返回 null：url={descriptor.URL}, " +
                        $"包名={descriptor.PackageName}, 组件名={descriptor.ComponentName}。" +
                        "可能原因：包未就绪、URL 未注册或描述错误。");
                }

                if (!(rawObject is FUIWindow typedWindow))
                {
                    // creator 返回的类型不是 FUIWindow 子类：释放本次创建的对象并报错。
                    rawObject.Dispose();
                    throw new FUIException(
                        $"ShowAsync 失败：创建对象不是 FUIWindow 子类：url={descriptor.URL}, " +
                        $"实际类型={rawObject.GetType().FullName}, 期望类型={descriptor.TargetType?.FullName}。");
                }

                window = typedWindow;
                entry.Window = window;
                entry.Lease = lease;

                // attach 前失败路径标记：在执行任何 attach 之前，IsInstanceLifecycleAttached 保持 false。
                // RollbackLoadedInstance 据此区分 attach 前失败（只 Dispose GObject + Release lease）
                // 与 attach 后失败（完整释放顺序）（任务 5.10）。
                entry.IsInstanceLifecycleAttached = false;

                // 3. 版本校验：加载期间若收到 Close 使版本过期，回滚本次取得的实例与租约，不显示
                // （spec: 加载期间 Close——旧打开操作 SHALL 失效，完成后的过期对象不得闪现或执行过期回调，
                //   并 SHALL 释放该操作取得的资源租约）。
                if (entry.IsOperationStale(operationVersion))
                {
                    RollbackLoadedInstance(entry);
                    // 版本过期属于操作取消语义，按 design.md 决策4 用 OperationCanceledException 表达，
                    // 消息包含窗口类型、URL、包名、操作版本快照与当前版本等诊断上下文。
                    throw new OperationCanceledException(
                        $"窗口加载操作因版本过期而取消：类型={entry.WindowType.FullName}, " +
                        $"URL={descriptor.URL}, 包名={descriptor.PackageName}, " +
                        $"操作版本快照={operationVersion}, 当前版本={entry.OperationVersion}。" +
                        "期间可能收到 Close 或重开。");
                }

                // 4. AttachContext：注入运行时上下文（design.md 决策5：AttachContext 是一次性实例生命周期第一步）。
                // 当前阶段上下文为 null，由后续任务细化为强类型上下文。
                // AttachContext 只设置 window.Context 字段，本身可重复调用但此路径只在首次创建时执行一次。
                window.AttachContext(null);

                // 可选 Descriptor.Attach：在 OnCreate 前附加业务依赖（design.md 决策5）。
                // spec: Attach 发生在 OnCreate 前——窗口 SHALL 在 OnCreate 前取得运行时上下文和描述提供的业务依赖。
                if (descriptor.Attach != null)
                {
                    descriptor.Attach(window, window.Context);
                }

                // 5. AttachWidgetTree：遍历窗口初始组件树，为所有 FUIWidget 执行幂等 Attach
                // （design.md 决策6：窗口 XML 构造完成后，FUIModule 遍历初始组件树，
                //   为所有 FUIWidget 执行幂等 Attach(owner, context)，再统一启动 Widget 创建生命周期）。
                // spec: Widget 在生命周期前获得所属窗口——初始嵌套 Widget SHALL 在执行自身 OnCreate 前获得正确的 OwnerWindow。
                // AttachWidgetTree 先为所有 Widget 完成 AttachContext，再统一触发 InvokeOnCreate，
                // 保证 Widget 的 OwnerWindow 在 OnCreate 前可用。
                AttachWidgetTree(window, window.Context);

                // 6. OnCreate：实例生命周期，执行一次（design.md 决策5：OnCreate 各执行一次）。
                // 通过 internal InvokeOnCreate 入口触发 protected virtual OnCreate（幂等保护在 FUIWindow 内部）。
                // OnCreate 在 AttachContext -> Descriptor.Attach -> AttachWidgetTree 之后执行，
                // 此时窗口与 Widget 均已取得上下文（spec: Attach 发生在 OnCreate 前生效）。
                window.InvokeOnCreate();

                // 实例生命周期 attach 完成：此后任何失败回滚都需按完整释放顺序执行
                // （Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease）。
                entry.IsInstanceLifecycleAttached = true;

                // 7. 进入 Opening。
                FUIWindowState stateBeforeOpening = entry.State;
                entry.TransitionTo(FUIWindowState.Opening);
                LogWindowStateTransition(entry, stateBeforeOpening);
                // 6.7 诊断：窗口包引用与 handle 数量——只在 Loading -> Opening 边界输出一次。
                LogWindowPackageDiagnostic(entry);

                // 8. 为每轮 Open 创建 OpenCancellationToken（design.md 决策5：每轮打开 Create open CTS）。
                // 每轮 Open 创建新的事件域（OpenCts）与 OpenCancellationToken。
                // RegisterOpenEvents 与 OnOpen 在第 10 步执行（5.8 实现）。
                CancellationTokenSource openCts = new CancellationTokenSource();
                entry.OpenCts = openCts;
                window.SetOpenCancellationToken(openCts.Token);

                // 9. 挂载到层容器：按描述的 Layer 与 SafeAreaMode 选择子容器（design.md 决策7）。
                // AddChild 把窗口追加到子容器末尾（最顶层），实现层内排序（后打开的在上层）。
                // 任务 6.3 验证：AddChild 只是显示树操作，不触发 onAddedToStage 作为 Open 信号——
                // 状态转换由下方第 11 步显式 TransitionTo(Open) 驱动，与 Stage 事件解耦
                // （design.md 决策6：onAddedToStage/onRemovedFromStage 不作为唯一 Open/Close 信号）。
                GComponent container = _layerContainer.GetSubContainer(descriptor.Layer, descriptor.SafeAreaMode);
                container.AddChild(window);
                // 5.11：挂载后显式置顶，保证即使 AddChild 因 sortingOrder 等机制未置末尾也能正确排序。
                BringWindowToTop(entry);

                // 10. 每轮打开同步生命周期：RegisterOpenEvents -> OnOpen（design.md 决策5）。
                // 在 TransitionTo(Open) 之前完成事件注册与 OnOpen，保证窗口进入 Open 时已注册本轮事件。
                // OnRefresh 不在此处调用：首个请求的 OnRefresh 由下方 ProcessRefreshQueue 统一处理
                // （与合并等待方的刷新请求一样，先 SetUserDatas 再 InvokeOnRefresh，保证刷新语义一致）。
                window.InvokeRegisterOpenEvents();
                window.InvokeOnOpen();

                // 11. 进入 Open。
                FUIWindowState stateBeforeOpen = entry.State;
                entry.TransitionTo(FUIWindowState.Open);
                LogWindowStateTransition(entry, stateBeforeOpen);

                // 5.11：窗口进入 Open 后统一重新计算全屏遮挡（design.md 决策7；spec: 打开全屏窗口——关闭下层渲染和输入）。
                // 若本窗口为全屏窗口，下层 visible/touchable 被设为 false 但保留 Stage 归属。
                RecomputeFullScreenOcclusion();

                // 12. 向合并等待方传播成功结果。
                sharedTask.TrySetResult(window);

                // 加载任务成功结束：清空 SharedCreateTask 引用，使下次 Show 从 Absent 重新创建。
                // 合并等待方已通过 await 捕获结果，清空字段不影响其 await。
                entry.SharedCreateTask = null;

                return window;
            }
            catch (Exception ex)
            {
                // 失败处理：回滚本次取得的资源，向合并等待方传播异常。
                // 回滚实例与租约（若已取得）。
                if (window != null || lease != null)
                {
                    RollbackLoadedInstance(entry);
                }

                // 加载/创建失败：状态回滚到 Absent（可重试）（spec: 加载失败后重试）。
                if (entry.State == FUIWindowState.Loading || entry.State == FUIWindowState.Opening)
                {
                    // TransitionTo 允许 Loading -> Absent 与 Opening -> Absent。
                    FUIWindowState stateBeforeRollback = entry.State;
                    entry.TransitionTo(FUIWindowState.Absent);
                    LogWindowStateTransition(entry, stateBeforeRollback);
                    // 6.7 诊断：窗口加载失败上下文——只在失败事件发生时输出一次。
                    LogWindowFailureContext(entry, ex?.Message ?? ex?.GetType().Name);
                }

                // 清空共享创建任务引用，使下次 Show 能创建新任务。
                entry.SharedCreateTask = null;

                // 向所有合并等待方传播异常。OperationCanceledException 由 TrySetException 内部转为取消态。
                sharedTask.TrySetException(ex);

                // 取消异常按原语义向上抛出，其余失败已包装为 FUIException。
                throw;
            }
        }

        /// <summary>
        /// 执行每轮打开生命周期（Opening -> Open），用于 Cached 再打开场景。
        /// </summary>
        /// <param name="entry">窗口条目，已处于 <see cref="FUIWindowState.Opening"/>。</param>
        /// <param name="descriptor">窗口描述。</param>
        /// <remarks>
        /// Cached 再打开不重复 OnCreate，但重新建立打开域与事件域，再次执行 RegisterOpenEvents -> OnOpen
        /// （spec: 缓存窗口重新打开——SHALL 重新建立打开取消域和事件域，并再次执行 OnOpen 与 OnRefresh）。
        /// 每轮 Open 创建新的 OpenCts 与 OpenCancellationToken，并执行 RegisterOpenEvents -> OnOpen（5.8 实现）。
        /// OnRefresh 由 ProcessRefreshQueue 统一处理，保证每个有效请求的刷新语义一致。
        /// </remarks>
        private void ExecuteOpenLifecycle(WindowEntry entry, FUIDescriptor descriptor)
        {
            FUIWindow window = entry.Window;
            if (window == null)
            {
                throw new FUIException(
                    $"ExecuteOpenLifecycle 失败：窗口 {entry.WindowType.FullName} 实例为空（Cached 状态不应为空）。");
            }

            // 每轮 Open 创建新的 OpenCts 与 OpenCancellationToken（design.md 决策5：每轮打开 Create open CTS）。
            // 任务 5.7：为每轮 Open 创建事件域（OpenCts）与 OpenCancellationToken。
            CancellationTokenSource openCts = new CancellationTokenSource();
            entry.OpenCts = openCts;
            window.SetOpenCancellationToken(openCts.Token);

            // 重新挂载到层容器（Cached 时可能已被移出显示树）。
            // 任务 6.3 验证：AddChild 只是显示树操作，不触发状态转换——状态由下方 TransitionTo(Open) 显式驱动。
            GComponent container = _layerContainer.GetSubContainer(descriptor.Layer, descriptor.SafeAreaMode);
            if (window.parent != container)
            {
                container.AddChild(window);
            }
            // 5.11：重新打开后显式置顶，保证缓存窗口重开后渲染在同级最顶层（层内排序）。
            BringWindowToTop(entry);

            // 每轮打开同步生命周期：RegisterOpenEvents -> OnOpen（design.md 决策5）。
            // spec: 缓存窗口重新打开——SHALL 重新建立打开取消域和事件域，并再次执行 OnOpen 与 OnRefresh。
            // OnRefresh 不在此处调用：首个请求的 OnRefresh 由下方 ProcessRefreshQueue 统一处理，
            // 与合并等待方的刷新请求一样，先 SetUserDatas 再 InvokeOnRefresh，保证刷新语义一致。
            window.InvokeRegisterOpenEvents();
            window.InvokeOnOpen();

            FUIWindowState stateBeforeCachedOpen = entry.State;
            entry.TransitionTo(FUIWindowState.Open);
            LogWindowStateTransition(entry, stateBeforeCachedOpen);

            // 5.11：缓存窗口重开进入 Open 后统一重新计算全屏遮挡（design.md 决策7）。
            RecomputeFullScreenOcclusion();
        }

        /// <summary>
        /// 遍历窗口初始组件树，为所有 <see cref="FUIWidget"/> 执行幂等 Attach，再统一触发 Widget OnCreate。
        /// </summary>
        /// <param name="window">刚构造完成的窗口实例。</param>
        /// <param name="context">运行时上下文，与窗口 Context 一致，可为 null。</param>
        /// <remarks>
        /// 设计依据：design.md 决策6——窗口 XML 构造完成后，FUIModule 遍历初始组件树，
        /// 为所有 FUIWidget 执行幂等 <c>Attach(owner, context)</c>，再统一启动 Widget 创建生命周期。
        /// <para>
        /// spec: Widget 在生命周期前获得所属窗口——初始嵌套 Widget SHALL 在执行自身 OnCreate 前获得正确的 OwnerWindow。
        /// </para>
        /// <para>
        /// 两阶段执行：
        /// 1. <see cref="CollectAndAttachWidgets"/>：深度优先遍历组件树，对每个 <see cref="FUIWidget"/> 调用
        ///    <see cref="FUIWidget.AttachContext"/>，设置 OwnerWindow 与 Context 并置位
        ///    <see cref="FUIWidget.IsAttached"/>（幂等，可重复调用只设置字段与标记，不重复触发 OnCreate）；
        /// 2. 统一对每个已 Attach 的 Widget 调用 <see cref="FUIWidget.InvokeOnCreate"/>（幂等，只执行一次 OnCreate）。
        /// </para>
        /// <para>
        /// 先全部 Attach 再统一 OnCreate，保证嵌套 Widget 的 OnCreate 执行时同层 Widget 也已 Attach，
        /// 避免子 Widget 在 OnCreate 中访问兄弟 Widget 时遇到未 Attach 的情况。
        /// </para>
        /// <para>
        /// 任务 6.1 幂等保证：初始 Widget 树在窗口创建时由本方法统一 Attach，owner 唯一确定；
        /// 重复以相同 owner 调用 <see cref="FUIWidget.AttachContext"/> 安全（只重置字段与 <see cref="FUIWidget.IsAttached"/> 标记），
        /// <see cref="FUIWidget.InvokeOnCreate"/> 受 <c>_isCreated</c> 保护不重复执行。owner 变更诊断由任务 6.2 负责。
        /// </para>
        /// <para>
        /// 遍历只处理 <see cref="GComponent"/> 子节点的 _children 集合，递归进入子 GComponent。
        /// 普通 FairyGUI 控件（GButton、GImage 等）不作为 Widget 处理，但其子节点仍会被遍历
        /// （spec: 普通 FairyGUI 组件不得被强制视为 Widget）。
        /// </para>
        /// <para>
        /// 动态 Widget（运行期创建/复用）的 Attach 由任务 6.2 提供受控入口；本方法只处理初始组件树。
        /// </para>
        /// </remarks>
        private void AttachWidgetTree(FUIWindow window, object context)
        {
            // 先收集并 Attach 所有初始 Widget，再统一触发 OnCreate（两阶段，保证同层 Widget 全部 Attach 后才 OnCreate）。
            List<FUIWidget> widgets = CollectAndAttachWidgets(window, window, context);

            // 统一触发 Widget OnCreate（幂等，已创建的跳过）。
            for (int i = 0; i < widgets.Count; i++)
            {
                widgets[i].InvokeOnCreate();
            }
        }

        /// <summary>
        /// 深度优先遍历组件树，对每个 <see cref="FUIWidget"/> 执行幂等 AttachContext 并收集到列表。
        /// </summary>
        /// <param name="root">遍历根组件（通常是窗口自身）。</param>
        /// <param name="ownerWindow">Widget 的所属窗口。</param>
        /// <param name="context">运行时上下文。</param>
        /// <returns>本次遍历中已执行 AttachContext 的 Widget 列表（按深度优先顺序）。</returns>
        /// <remarks>
        /// 本方法只遍历 <see cref="GComponent"/> 的子节点并递归，不处理非 GComponent 的 GObject。
        /// AttachContext 为幂等字段设置（可重复调用），重复遍历不会导致副作用。
        /// </remarks>
        private List<FUIWidget> CollectAndAttachWidgets(GComponent root, FUIWindow ownerWindow, object context)
        {
            List<FUIWidget> widgets = new List<FUIWidget>();
            CollectAndAttachWidgetsRecursive(root, ownerWindow, context, widgets);
            return widgets;
        }

        /// <summary>
        /// 递归遍历组件树，对 FUIWidget 执行 AttachContext 并加入收集列表。
        /// </summary>
        /// <param name="container">当前遍历的容器节点。</param>
        /// <param name="ownerWindow">Widget 的所属窗口。</param>
        /// <param name="context">运行时上下文。</param>
        /// <param name="widgets">收集列表，每找到一个 Widget 即追加。</param>
        private void CollectAndAttachWidgetsRecursive(
            GComponent container,
            FUIWindow ownerWindow,
            object context,
            List<FUIWidget> widgets)
        {
            if (container == null)
            {
                return;
            }

            int count = container.numChildren;
            for (int i = 0; i < count; i++)
            {
                GObject child = container.GetChildAt(i);

                // 受管理 Widget：执行幂等 Attach 并收集。
                if (child is FUIWidget widget)
                {
                    widget.AttachContext(ownerWindow, context);
                    widgets.Add(widget);

                    // Widget 本身也是 GComponent 子类，递归遍历其子节点，
                    // 处理 Widget 内嵌套的更深层 Widget。
                    CollectAndAttachWidgetsRecursive(widget, ownerWindow, context, widgets);
                }
                else if (child is GComponent childContainer)
                {
                    // 普通 GComponent 子节点：递归遍历其子树，查找可能嵌套的 Widget。
                    CollectAndAttachWidgetsRecursive(childContainer, ownerWindow, context, widgets);
                }
            }
        }

        /// <summary>
        /// 处理 FIFO 刷新队列：按到达顺序对每个待处理请求先更新 UserDatas/UserData 再同步刷新。
        /// </summary>
        /// <param name="entry">窗口条目。</param>
        /// <remarks>
        /// spec: 每个刷新请求执行前 SHALL 更新 UserDatas 和 UserData；
        /// 有效请求 SHALL 按进入顺序执行刷新，最终显示状态由最后一个完成刷新请求决定。
        /// <para>
        /// 本方法处理队列中截至当前的全部待处理请求。并发 Show 的请求按入队顺序各执行一次刷新，
        /// 使最终显示状态由最后一个完成刷新请求决定（spec: 同类型并发打开串行收敛）。
        /// </para>
        /// <para>
        /// 窗口必须已处于 Open 或 Hidden 状态才执行刷新；其他状态（如正在 Closing）跳过刷新，
        /// 避免在非法状态下执行生命周期回调。
        /// </para>
        /// <para>
        /// 5.8 启用 OnRefresh 调用（通过 internal <see cref="FUIWindow.InvokeOnRefresh"/>）。
        /// </para>
        /// <para>
        /// 【5.8 复核结论】合并场景下"各执行一次刷新"语义保持不变：
        /// loader（加载执行者）在完成 OnOpen 后不单独调用 OnRefresh，而是与合并等待方一样，
        /// 通过本方法统一处理 FIFO 队列中的全部待处理请求（含 loader 自身入队的 args 与合并等待方入队的 args）。
        /// 这保证每个有效请求的 OnRefresh 与其自身的 UserDatas/UserData 严格配对：
        /// loader 的 args 在 ShowAsyncCore 入口处已入队，合并等待方的 args 在各自入口处已入队，
        /// 本方法按 FIFO 顺序逐个 Dequeue、SetUserDatas、InvokeOnRefresh，每个请求各执行一次刷新。
        /// 这与 spec"按请求顺序对该实例各执行一次刷新"一致，无需改为让每个合并等待方自行刷新。
        /// </para>
        /// </remarks>
        private void ProcessRefreshQueue(WindowEntry entry)
        {
            // 窗口必须已就绪才执行刷新。
            if (entry.State != FUIWindowState.Open && entry.State != FUIWindowState.Hidden)
            {
                return;
            }

            FUIWindow window = entry.Window;
            if (window == null)
            {
                return;
            }

            // FIFO：按入队顺序处理全部待处理请求。
            while (entry.PendingRefreshArgs.Count > 0)
            {
                object[] args = entry.PendingRefreshArgs.Dequeue();

                // 每个有效请求先更新 UserDatas/UserData
                // （spec: 每个刷新请求执行前 SHALL 更新 UserDatas 和 UserData）。
                // SetUserDatas 为 internal，同程序集可访问。
                window.SetUserDatas(args);

                // 同步刷新 OnRefresh（design.md 决策5：RegisterOpenEvents -> OnOpen -> OnRefresh）。
                // 通过 internal InvokeOnRefresh 触发 protected virtual OnRefresh。
                window.InvokeOnRefresh();
            }
        }

        /// <summary>
        /// 回滚加载执行者本次取得的实例与租约，不触碰其他调用方已持有的共享记录。
        /// </summary>
        /// <param name="entry">窗口条目。</param>
        /// <remarks>
        /// 根据 <see cref="WindowEntry.IsInstanceLifecycleAttached"/> 区分两条回滚路径（任务 5.10）：
        /// <list type="bullet">
        /// <item>attach 前失败（<see cref="WindowEntry.IsInstanceLifecycleAttached"/> 为 false）：
        ///   实例未执行任何生命周期回调（如包加载、实例构造或类型转换失败），
        ///   只需 Dispose GObject + Release lease，不调用 OnDispose / Descriptor.Detach / Dispose Widgets。</item>
        /// <item>attach 后失败或正常 Close/Shutdown 释放（<see cref="WindowEntry.IsInstanceLifecycleAttached"/> 为 true）：
        ///   实例已执行完整实例生命周期（AttachContext -> Descriptor.Attach -> AttachWidgetTree -> OnCreate），
        ///   按完整释放顺序执行（design.md 决策5）：
        ///   Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease。</item>
        /// </list>
        /// 本方法幂等：window 或 lease 为 null 时跳过对应步骤；所有步骤异常被吞掉以保证清理不中断。
        /// </remarks>
        private void RollbackLoadedInstance(WindowEntry entry)
        {
            FUIWindow window = entry.Window;
            PackageLease lease = entry.Lease;
            FUIDescriptor descriptor = entry.Descriptor;

            // 区分两条回滚路径：attach 前失败只做简单清理；attach 后失败按完整释放顺序。
            if (entry.IsInstanceLifecycleAttached)
            {
                // attach 后失败：按完整释放顺序执行（design.md 决策5）。
                // 顺序：Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease。
                DisposeWindowLifecycle(entry, window, descriptor);
            }
            else
            {
                // attach 前失败：实例未执行任何生命周期回调，只需 Dispose GObject + Release lease。
                // 不调用 OnDispose / Descriptor.Detach / Dispose Widgets，避免对未 attach 的实例执行回调。
                DisposeGObjectAndReleaseLease(window, lease);
            }

            // 清空 entry 引用，避免重复释放。
            entry.Window = null;
            entry.Lease = null;
            entry.IsInstanceLifecycleAttached = false;
        }

        /// <summary>
        /// 执行完整最终释放顺序：Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease。
        /// </summary>
        /// <param name="entry">窗口条目（用于访问上下文，本方法不修改 entry 字段，由调用方统一清空）。</param>
        /// <param name="window">窗口实例，可为 null（跳过前四步）。</param>
        /// <param name="descriptor">窗口描述，用于 Detach 回调。</param>
        /// <remarks>
        /// 实现依据：design.md 决策5 第118行——最终释放顺序固定为
        /// <c>Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease</c>。
        /// <para>
        /// 各步骤语义：
        /// <list type="number">
        /// <item>Dispose Widgets：遍历窗口组件树，对每个 <see cref="FUIWidget"/> 调用
        ///   <see cref="FUIWidget.InvokeOnDispose"/>（幂等，仅执行一次）。Widget 的 OnDispose 在窗口 OnDispose 之前执行，
        ///   保证业务在窗口 OnDispose 中访问 Widget 状态时 Widget 尚未释放。Widget GObject 的 Dispose 由窗口
        ///   GObject Dispose 顺带完成（FairyGUI GComponent.Dispose 递归 Dispose 子对象）。</item>
        /// <item>OnDispose：调用 <see cref="FUIWindow.InvokeOnDispose"/>（幂等，仅执行一次），
        ///   触发 protected virtual OnDispose 回调。</item>
        /// <item>Descriptor.Detach：若描述配置了 Detach 回调，调用它清理 Attach 阶段附加的业务依赖
        ///   （design.md 决策5：Descriptor.Detach 在 OnDispose 之后、Dispose GObject 之前）。</item>
        /// <item>Dispose GObject：从显示树移除并 Dispose 窗口的 FairyGUI GObject。</item>
        /// <item>Release lease：释放包租约（幂等，PackageLease.Release 已有重复释放保护）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 本方法用于：
        /// <list type="bullet">
        /// <item><see cref="RollbackLoadedInstance"/> 的 attach 后失败路径；</item>
        /// <item><see cref="CloseEntryCore"/> 的 Disposed/Cached 最终释放分支（通过 RollbackLoadedInstance 间接调用）；</item>
        /// <item><see cref="ClearWindowEntriesForShutdown"/> 的 Shutdown 释放（通过 RollbackLoadedInstance 间接调用）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 所有步骤异常被吞掉以保证清理不中断；window 为 null 时跳过前四步，直接释放 lease。
        /// </para>
        /// </remarks>
        private void DisposeWindowLifecycle(WindowEntry entry, FUIWindow window, FUIDescriptor descriptor)
        {
            // 1. Dispose Widgets：遍历窗口组件树，对每个 FUIWidget 调用 InvokeOnDispose（幂等）。
            //    Widget 的 OnDispose 在窗口 OnDispose 之前执行，保证业务在窗口 OnDispose 中访问 Widget 状态时 Widget 尚未释放。
            if (window != null)
            {
                DisposeWidgetTree(window);
            }

            // 2. OnDispose：调用窗口的 InvokeOnDispose（幂等，仅执行一次），触发 protected virtual OnDispose。
            if (window != null)
            {
                try
                {
                    window.InvokeOnDispose();
                }
                catch (Exception)
                {
                    // OnDispose 异常不中断释放流程，后续 Detach/Dispose/Release 仍需执行。
                }
            }

            // 3. Descriptor.Detach：若描述配置了 Detach 回调，调用它清理 Attach 阶段附加的业务依赖。
            //    Detach 在 OnDispose 之后、Dispose GObject 之前（design.md 决策5）。
            if (window != null && descriptor.Detach != null)
            {
                try
                {
                    descriptor.Detach(window, window.Context);
                }
                catch (Exception)
                {
                    // Detach 异常不中断释放流程，后续 Dispose/Release 仍需执行。
                }
            }

            // 4. Dispose GObject：从显示树移除并 Dispose 窗口的 FairyGUI GObject。
            DisposeGObjectAndReleaseLease(window, entry.Lease);
        }

        /// <summary>
        /// Dispose 窗口的 FairyGUI GObject 并释放包租约（不执行 OnDispose / Descriptor.Detach / Dispose Widgets）。
        /// </summary>
        /// <param name="window">窗口实例，可为 null（跳过 Dispose GObject）。</param>
        /// <param name="lease">包租约，可为 null（跳过释放）。</param>
        /// <remarks>
        /// 本方法只做 GObject Dispose 与 lease 释放，不执行任何生命周期回调。
        /// 用于 attach 前失败路径（实例未执行生命周期，无需回调），
        /// 也作为 <see cref="DisposeWindowLifecycle"/> 的最后两步共享实现。
        /// </remarks>
        private void DisposeGObjectAndReleaseLease(FUIWindow window, PackageLease lease)
        {
            // 释放 GObject（若仍在显示树中先移除）。
            if (window != null)
            {
                try
                {
                    if (window.parent != null)
                    {
                        window.parent.RemoveChild(window, false);
                    }
                }
                catch (Exception)
                {
                    // 移除异常不中断回滚。
                }

                try
                {
                    if (!window.isDisposed)
                    {
                        window.Dispose();
                    }
                }
                catch (Exception)
                {
                    // Dispose 异常不中断回滚。
                }
            }

            // 释放包租约。
            if (lease != null && !lease.IsReleased)
            {
                try
                {
                    lease.Release();
                }
                catch (Exception)
                {
                    // lease 释放异常不中断回滚。
                }
            }
        }

        /// <summary>
        /// 遍历窗口组件树，对每个 <see cref="FUIWidget"/> 调用 <see cref="FUIWidget.InvokeOnDispose"/>（幂等）。
        /// </summary>
        /// <param name="window">窗口实例，作为遍历根。</param>
        /// <remarks>
        /// 实现依据：design.md 决策5——最终释放顺序的第一步"Dispose Widgets"。
        /// <para>
        /// 与 <see cref="AttachWidgetTree"/> 对称：Attach 时遍历组件树为每个 Widget 执行 AttachContext + InvokeOnCreate，
        /// Dispose 时遍历组件树为每个 Widget 执行 InvokeOnDispose。两者使用相同的深度优先遍历顺序，
        /// 保证 Attach 与 Dispose 的 Widget 集合一致。
        /// </para>
        /// <para>
        /// Widget 的 InvokeOnDispose 为幂等（由 FUIWidget 内部 _isDisposed 标志保护），
        /// 重复遍历或多次释放不会重复执行业务回调。
        /// </para>
        /// <para>
        /// Widget GObject 的 Dispose 不由本方法处理：窗口 GObject Dispose 时（FairyGUI GComponent.Dispose 递归 Dispose 子对象）
        /// 会顺带 Dispose 所有子 Widget 的 GObject，无需框架单独处理，避免重复释放。
        /// </para>
        /// <para>
        /// 异常被吞掉以保证清理不中断：单个 Widget 的 OnDispose 异常不应阻止其他 Widget 或窗口的释放。
        /// </para>
        /// </remarks>
        private void DisposeWidgetTree(FUIWindow window)
        {
            if (window == null)
            {
                return;
            }

            try
            {
                DisposeWidgetTreeRecursive(window);
            }
            catch (Exception)
            {
                // 遍历异常不中断释放流程，后续 OnDispose/Descriptor.Detach/Dispose GObject/Release lease 仍需执行。
            }
        }

        /// <summary>
        /// 递归遍历组件树，对每个 <see cref="FUIWidget"/> 调用 <see cref="FUIWidget.InvokeOnDispose"/>。
        /// </summary>
        /// <param name="container">当前遍历的容器节点。</param>
        /// <remarks>
        /// 与 <see cref="CollectAndAttachWidgetsRecursive"/> 使用相同的遍历策略：
        /// 只处理 <see cref="GComponent"/> 的子节点并递归，不处理非 GComponent 的 GObject。
        /// 普通 FairyGUI 控件（GButton、GImage 等）不作为 Widget 处理，但其子节点仍会被遍历。
        /// </remarks>
        private void DisposeWidgetTreeRecursive(GComponent container)
        {
            if (container == null)
            {
                return;
            }

            int count = container.numChildren;
            for (int i = 0; i < count; i++)
            {
                GObject child = container.GetChildAt(i);

                // 受管理 Widget：调用 InvokeOnDispose（幂等，已释放的跳过）。
                if (child is FUIWidget widget)
                {
                    try
                    {
                        widget.InvokeOnDispose();
                    }
                    catch (Exception)
                    {
                        // 单个 Widget 的 OnDispose 异常不阻止其他 Widget 的释放。
                    }

                    // Widget 本身也是 GComponent 子类，递归遍历其子节点，
                    // 处理 Widget 内嵌套的更深层 Widget。
                    DisposeWidgetTreeRecursive(widget);
                }
                else if (child is GComponent childContainer)
                {
                    // 普通 GComponent 子节点：递归遍历其子树，查找可能嵌套的 Widget。
                    DisposeWidgetTreeRecursive(childContainer);
                }
            }
        }

        /// <summary>
        /// 获取或创建指定窗口类型的 <see cref="WindowEntry"/>。
        /// </summary>
        /// <param name="windowType">窗口类型。</param>
        /// <param name="descriptor">窗口描述。</param>
        /// <returns>已存在或新建的窗口条目。</returns>
        /// <remarks>
        /// Disposed 终态后重建新 entry 回到 Absent
        /// （spec: 窗口状态转换受控——Disposed 是终态，类型在下一次 Show 时从新 Absent entry 创建新实例）。
        /// </remarks>
        private WindowEntry GetOrCreateEntry(Type windowType, FUIDescriptor descriptor)
        {
            if (!_windowEntries.TryGetValue(windowType, out WindowEntry entry) || entry == null)
            {
                entry = new WindowEntry(windowType, descriptor);
                _windowEntries[windowType] = entry;
                return entry;
            }

            // Disposed 终态后重建新 entry。
            if (entry.State == FUIWindowState.Disposed)
            {
                entry = new WindowEntry(windowType, descriptor);
                _windowEntries[windowType] = entry;
            }

            return entry;
        }

        /// <summary>
        /// 重新计算并应用全屏遮挡：根据当前各层级是否有全屏窗口处于 Open 状态，
        /// 把被遮挡的下层容器 visible/touchable 设为 false，但保留 Stage 归属
        /// （design.md 决策7；spec: 独立层级和全屏遮挡）。
        /// </summary>
        /// <remarks>
        /// 遍历 <see cref="_windowEntries"/>，对每个处于 <see cref="FUIWindowState.Open"/> 状态
        /// 且描述 <see cref="FUIDescriptor.FullScreen"/> 为 true 的窗口，标记其所属层为有全屏窗口。
        /// 然后调用 <see cref="FUILayerContainer.ApplyFullScreenOcclusion"/> 应用遮挡。
        /// <para>
        /// Hidden 状态的全屏窗口不参与遮挡（已被 Hide 隐藏，不再遮挡下层）。
        /// 这保证 Hide 顶部全屏窗口后下层立恢可见（spec: 关闭顶部全屏窗口——SHALL 重新计算窗口栈，并恢复不再被其他全屏窗口遮挡的窗口）。
        /// </para>
        /// <para>
        /// 调用时机（任务 5.11）：Show 完成后、Hide 后、Close 完成后、层内重排后统一调用，
        /// 保证显示状态变化后遮挡关系一致。
        /// </para>
        /// <para>
        /// 本方法幂等，重复调用以最新状态为准。模块已 Shutdown 或层容器为 null 时直接返回。
        /// </para>
        /// </remarks>
        private void RecomputeFullScreenOcclusion()
        {
            if (_isShutdown || _layerContainer == null)
            {
                return;
            }

            bool[] fullScreenOpenByLayer = new bool[(int)FUILayer.System + 1];
            foreach (KeyValuePair<Type, WindowEntry> pair in _windowEntries)
            {
                WindowEntry entry = pair.Value;
                if (entry == null || entry.Descriptor.URL == null)
                {
                    continue;
                }

                // 只有 Open 状态的全屏窗口参与遮挡；Hidden 的全屏窗口已隐藏，不再遮挡下层。
                if (entry.State == FUIWindowState.Open && entry.Descriptor.FullScreen)
                {
                    int layerIndex = (int)entry.Descriptor.Layer;
                    if (layerIndex >= 0 && layerIndex < fullScreenOpenByLayer.Length)
                    {
                        fullScreenOpenByLayer[layerIndex] = true;
                    }
                }
            }

            _layerContainer.ApplyFullScreenOcclusion(fullScreenOpenByLayer);
        }

        /// <summary>
        /// 把指定窗口移到其所属子容器的最顶层，实现层内排序（design.md 决策7：窗口只在所属容器内调整 child index）。
        /// </summary>
        /// <param name="entry">窗口条目，应已挂载到层容器。</param>
        /// <remarks>
        /// 后打开/重新显示的窗口移到子容器末尾（最顶层），使其渲染在同级其他窗口之上。
        /// 窗口未挂载或已被移除时 BringToTopInContainer 内部安全跳过。
        /// </remarks>
        private void BringWindowToTop(WindowEntry entry)
        {
            if (_isShutdown || _layerContainer == null || entry == null || entry.Window == null)
            {
                return;
            }

            _layerContainer.BringToTopInContainer(
                entry.Window, entry.Descriptor.Layer, entry.Descriptor.SafeAreaMode);
        }

        /// <summary>
        /// 动态创建并 Attach 一个受管理 Widget 的受控入口（任务 6.2）。
        /// <para>窗口在运行期需要动态创建 Widget（如列表项、动态嵌套组件）时，必须通过本入口而非直接
        /// <c>UIPackage.CreateObject</c> 或 <c>AddChild</c>，以保证：
        /// <list type="bullet">
        /// <item>Widget 所属包校验：只允许来自窗口自身包或其已声明依赖包
        ///   （spec: 动态 Widget 来自未声明包——SHALL 拒绝创建并报告窗口包、Widget 包和缺失依赖）。</item>
        /// <item>受控 Attach：Widget 在业务生命周期前通过幂等 <see cref="FUIWidget.AttachContext"/> 获得正确
        ///   <see cref="FUIWidget.OwnerWindow"/> 与上下文（spec: 动态列表 Widget——SHALL 在业务代码依赖
        ///   OwnerWindow 前完成绑定）。</item>
        /// <item>owner 变更诊断：通过 <see cref="FUIWidget.LastAttachOwnerChanged"/> 暴露 owner 变更事实。</item>
        /// <item>不重复执行创建生命周期：<see cref="FUIWidget.InvokeOnCreate"/> 幂等保护
        ///   （spec: 不得因重复 Attach 重复执行创建生命周期）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// design.md 决策6：“动态受管理 Widget 只允许来自所属窗口包或其已声明依赖；其他包在本 change 中
        /// 直接拒绝，避免引入独立 Widget 租约模型。”本入口不创建独立包租约，复用窗口已持有的包租约。
        /// </para>
        /// </summary>
        /// <typeparam name="TWidget">受管理 Widget 业务类型，必须为 <see cref="FUIWidget"/> 子类且已注册。</typeparam>
        /// <param name="ownerWindow">所属窗口实例，不得为 null；必须是已通过 <see cref="ShowAsync{T}"/> 创建的受管理窗口。</param>
        /// <param name="context">运行时上下文，可为 null；通常传入 <see cref="FUIWindow.Context"/>。</param>
        /// <returns>已 Attach 且已执行 OnCreate 的受管理 Widget 实例。</returns>
        /// <exception cref="FUIException">模块已 Shutdown、owner 非 null 但非受管理窗口、Widget 类型未注册、
        /// Widget 所属包不是窗口自身包或声明依赖、或创建返回类型不匹配。</exception>
        internal TWidget CreateDynamicWidget<TWidget>(FUIWindow ownerWindow, object context = null) where TWidget : FUIWidget
        {
            ThrowIfShutdown();

            if (ownerWindow == null)
            {
                throw new FUIException("CreateDynamicWidget 失败：ownerWindow 不能为空。");
            }

            // 校验 owner 是受管理窗口：通过实例类型反查 WindowEntry，确认实例由 FUIModule 创建且仍存活。
            // 这避免业务传入自行构造的 GComponent 冒充 owner，确保 Widget Attach 到合法窗口。
            Type ownerType = ownerWindow.GetType();
            if (!_windowEntries.TryGetValue(ownerType, out WindowEntry ownerEntry) || ownerEntry == null)
            {
                throw new FUIException(
                    $"CreateDynamicWidget 失败：ownerWindow 不是受管理窗口或已释放：类型={ownerType.FullName}。" +
                    "owner 必须是通过 FUI.ShowAsync 创建的窗口实例。");
            }

            if (!ReferenceEquals(ownerEntry.Window, ownerWindow))
            {
                throw new FUIException(
                    $"CreateDynamicWidget 失败：ownerWindow 实例与当前条目不一致：类型={ownerType.FullName}, " +
                    $"URL={ownerEntry.Descriptor.URL}, 包名={ownerEntry.Descriptor.PackageName}。" +
                    "可能原因：实例已被回滚或替换。");
            }

            // 查询 Widget 类型对应的注册描述；未注册则无法创建受管理 Widget。
            Type widgetType = typeof(TWidget);
            if (!_registry.TryGetDescriptor(widgetType, out FUIDescriptor widgetDescriptor))
            {
                throw new FUIException(
                    $"CreateDynamicWidget 失败：Widget 类型未注册或已 Shutdown：{widgetType.FullName}。" +
                    "请先完成 owner 注册并 FreezeBindings。");
            }

            // 核心校验：Widget 所属包必须是 owner 窗口自身包或其已声明依赖包
            // （spec: 动态 Widget 来自未声明包——SHALL 拒绝创建并报告窗口包、Widget 包和缺失依赖；
            //   design.md 决策6：动态受管理 Widget 只允许来自所属窗口包或其已声明依赖）。
            ValidateDynamicWidgetPackageOwnership(
                ownerEntry, widgetDescriptor.PackageName, widgetDescriptor.URL);

            // 创建 Widget 实例：通过全局无状态 creator 查询 Registry，返回最终业务类型
            // （spec: 业务类型覆盖生成类型；design.md 决策2）。
            // CreateObjectFromURL 会触发 UIObjectFactory 的 creator，creator 查询活动 Registry 的描述。
            GObject rawObject = UIPackage.CreateObjectFromURL(widgetDescriptor.URL);
            if (rawObject == null)
            {
                throw new FUIException(
                    $"CreateDynamicWidget 失败：CreateObjectFromURL 返回 null：url={widgetDescriptor.URL}, " +
                    $"包名={widgetDescriptor.PackageName}, 组件名={widgetDescriptor.ComponentName}。" +
                    "可能原因：包未就绪、URL 未注册或描述错误。");
            }

            if (!(rawObject is TWidget typedWidget))
            {
                rawObject.Dispose();
                throw new FUIException(
                    $"CreateDynamicWidget 失败：创建对象不是 {widgetType.FullName}：url={widgetDescriptor.URL}, " +
                    $"实际类型={rawObject.GetType().FullName}。");
            }

            // 受控 Attach + 幂等 OnCreate：通过共享入口完成绑定，与初始 Widget 树保持一致语义。
            AttachAndStartWidget(typedWidget, ownerWindow, context);

            return typedWidget;
        }

        /// <summary>
        /// 池化复用 Widget 的受控 Attach 入口（任务 6.2）。
        /// <para>用于已创建的 Widget 实例（如从对象池取出的复用实例）重新 Attach 到 owner 窗口。
        /// 复用入口在交付业务前重新 Attach（design.md 决策6 风险项），保证 OwnerWindow 不陈旧。</para>
        /// <para>
        /// 本入口执行：
        /// <list type="bullet">
        /// <item>owner 窗口校验：必须是受管理窗口且实例一致。</item>
        /// <item>Widget 包校验：只允许窗口自身包或声明依赖包
        ///   （spec: 动态 Widget 来自未声明包——SHALL 拒绝创建并报告窗口包、Widget 包和缺失依赖）。</item>
        /// <item>受控 Attach：<see cref="FUIWidget.AttachContext"/> 幂等设置 OwnerWindow/Context，
        ///   owner 变更时通过 <see cref="FUIWidget.LastAttachOwnerChanged"/> 暴露诊断。</item>
        /// <item>幂等 OnCreate：<see cref="FUIWidget.InvokeOnCreate"/> 只执行一次
        ///   （spec: 不得因重复 Attach 重复执行创建生命周期）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 池化复用典型流程：业务从池中取出 Widget 后调用本入口重新 Attach；如需干净重置 Attach 状态
        /// （避免 owner 变更诊断标记），可在调用本入口前先调用 <see cref="FUIWidget.ResetForReuse"/>。
        /// </para>
        /// </summary>
        /// <param name="widget">待 Attach 的受管理 Widget 实例，不得为 null。</param>
        /// <param name="ownerWindow">所属窗口实例，不得为 null；必须是已通过 <see cref="ShowAsync{T}"/> 创建的受管理窗口。</param>
        /// <param name="context">运行时上下文，可为 null。</param>
        /// <exception cref="FUIException">模块已 Shutdown、参数为 null、owner 非受管理窗口、
        /// 或 Widget 所属包不是窗口自身包或声明依赖。</exception>
        internal void AttachDynamicWidget(FUIWidget widget, FUIWindow ownerWindow, object context = null)
        {
            ThrowIfShutdown();

            if (widget == null)
            {
                throw new FUIException("AttachDynamicWidget 失败：widget 不能为空。");
            }

            if (ownerWindow == null)
            {
                throw new FUIException("AttachDynamicWidget 失败：ownerWindow 不能为空。");
            }

            // 校验 owner 是受管理窗口。
            Type ownerType = ownerWindow.GetType();
            if (!_windowEntries.TryGetValue(ownerType, out WindowEntry ownerEntry) || ownerEntry == null)
            {
                throw new FUIException(
                    $"AttachDynamicWidget 失败：ownerWindow 不是受管理窗口或已释放：类型={ownerType.FullName}。" +
                    "owner 必须是通过 FUI.ShowAsync 创建的窗口实例。");
            }

            if (!ReferenceEquals(ownerEntry.Window, ownerWindow))
            {
                throw new FUIException(
                    $"AttachDynamicWidget 失败：ownerWindow 实例与当前条目不一致：类型={ownerType.FullName}, " +
                    $"URL={ownerEntry.Descriptor.URL}, 包名={ownerEntry.Descriptor.PackageName}。" +
                    "可能原因：实例已被回滚或替换。");
            }

            // 校验 Widget 所属包：只允许窗口自身包或声明依赖包。
            // 优先按实例运行时类型查询 Registry 取得注册描述；若未注册，则从 FairyGUI 运行时对象
            // 取得包名做最小校验（池化复用场景下 Widget 可能是生成基类实例）。
            (string widgetPackageName, string widgetUrl) = ResolveWidgetPackageInfo(widget);
            ValidateDynamicWidgetPackageOwnership(ownerEntry, widgetPackageName, widgetUrl);

            // 受控 Attach + 幂等 OnCreate。
            AttachAndStartWidget(widget, ownerWindow, context);
        }

        /// <summary>
        /// 校验动态 Widget 所属包是否为 owner 窗口自身包或其已声明依赖包。
        /// </summary>
        /// <param name="ownerEntry">owner 窗口条目。</param>
        /// <param name="widgetPackageName">Widget 所属包名。</param>
        /// <param name="widgetUrl">Widget 组件 URL，用于诊断上下文，可为 null。</param>
        /// <remarks>
        /// 实现依据：spec "动态 Widget 来自未声明包"——SHALL 拒绝创建并报告窗口包、Widget 包和缺失依赖；
        /// design.md 决策6：动态受管理 Widget 只允许来自所属窗口包或其已声明依赖，其他包直接拒绝。
        /// <para>
        /// 允许的包集合来源：
        /// <list type="bullet">
        /// <item>窗口自身包：<see cref="WindowEntry.Descriptor.PackageName"/>
        ///   （owner 窗口注册描述中的包名）。</item>
        /// <item>窗口包的声明依赖：通过 owner 窗口持有的 <see cref="PackageLease"/> 取得
        ///   <see cref="PackageRecord"/>，读取其 <see cref="UIPackage.dependencies"/> 中每个依赖的 "name" 字段。
        ///   这些是 FairyGUI 包描述中显式声明的依赖包名。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 本 change 不为动态 Widget 建立独立包租约（design.md 决策6：避免引入独立 Widget 租约模型），
        /// 因此只校验包归属，不增减引用计数。Widget 的包资源由 owner 窗口的包租约（含依赖）覆盖。
        /// </para>
        /// </remarks>
        /// <exception cref="FUIException">Widget 包不属于窗口自身包或声明依赖，消息包含窗口包、Widget 包与缺失依赖上下文。</exception>
        private void ValidateDynamicWidgetPackageOwnership(
            WindowEntry ownerEntry, string widgetPackageName, string widgetUrl)
        {
            string windowPackageName = ownerEntry.Descriptor.PackageName;

            // 快速通过：Widget 与窗口同包。
            if (!string.IsNullOrEmpty(widgetPackageName)
                && string.Equals(widgetPackageName, windowPackageName, StringComparison.Ordinal))
            {
                return;
            }

            // 构建窗口包的声明依赖包名集合：从 owner 窗口持有的 lease 取得 PackageRecord，
            // 读取 UIPackage.dependencies 中每个依赖的 "name" 字段。
            // 这些是 FairyGUI 包描述中显式声明的依赖，是 spec 定义的“已声明依赖”。
            HashSet<string> declaredDependencies = BuildDeclaredDependencySet(ownerEntry);

            if (declaredDependencies != null && declaredDependencies.Contains(widgetPackageName))
            {
                // Widget 包是窗口包的声明依赖：允许。
                return;
            }

            // 拒绝：报告窗口包、Widget 包与缺失依赖上下文
            // （spec: 动态 Widget 来自未声明包——SHALL 拒绝创建并报告窗口包、Widget 包和缺失依赖）。
            string declaredList = declaredDependencies != null && declaredDependencies.Count > 0
                ? string.Join(", ", declaredDependencies)
                : "<无>";
            throw new FUIException(
                $"动态 Widget 包校验失败：Widget 包 '{widgetPackageName}' 不是窗口包 '{windowPackageName}' 的自身包或声明依赖。" +
                $"窗口 URL={ownerEntry.Descriptor.URL}, Widget URL={widgetUrl ?? "<未知>"}, " +
                $"声明依赖=[{declaredList}]。" +
                "design.md 决策6：动态受管理 Widget 只允许来自所属窗口包或其已声明依赖，其他包直接拒绝，避免引入独立 Widget 租约模型。");
        }

        /// <summary>
        /// 构建 owner 窗口包的声明依赖包名集合。
        /// </summary>
        /// <param name="ownerEntry">owner 窗口条目。</param>
        /// <returns>声明依赖包名集合；若窗口包未加载（lease/package 为 null）返回 null。</returns>
        /// <remarks>
        /// 来源为 <see cref="UIPackage.dependencies"/>：每个依赖字典含 "id" 与 "name" 键，
        /// 以 "name" 作为依赖包逻辑名（与 <see cref="PackageLoader.AcquireDependenciesAsync"/> 一致）。
        /// <para>
        /// 本方法只读不修改任何状态；每次调用构造新集合，调用方安全持有。
        /// 若窗口包 lease 为 null 或 package 为 null（如窗口正在加载中），返回 null 表示无法判定依赖，
        /// 由调用方 <see cref="ValidateDynamicWidgetPackageOwnership"/> 据此拒绝（安全失败）。
        /// </para>
        /// </remarks>
        private HashSet<string> BuildDeclaredDependencySet(WindowEntry ownerEntry)
        {
            PackageLease lease = ownerEntry.Lease;
            if (lease == null)
            {
                // 窗口尚未完成包加载（lease 未交付）：无法判定依赖，返回 null 使校验安全失败。
                return null;
            }

            PackageRecord record = lease.Record;
            if (record == null)
            {
                return null;
            }

            UIPackage package = record.Package;
            if (package == null)
            {
                return null;
            }

            // FairyGUI dependencies API：Dictionary<string,string>[]，每项含 "id" 与 "name"。
            Dictionary<string, string>[] deps = package.dependencies;
            if (deps == null || deps.Length == 0)
            {
                // 无声明依赖：返回空集合（非 null），使调用方校验逻辑一致。
                return new HashSet<string>(StringComparer.Ordinal);
            }

            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < deps.Length; i++)
            {
                Dictionary<string, string> dep = deps[i];
                if (dep == null)
                {
                    continue;
                }

                if (dep.TryGetValue("name", out string depName) && !string.IsNullOrEmpty(depName))
                {
                    set.Add(depName);
                }
            }

            return set;
        }

        /// <summary>
        /// 解析 Widget 实例的包名与 URL，用于动态 Attach 的包校验。
        /// </summary>
        /// <param name="widget">Widget 实例。</param>
        /// <returns>(包名, URL) 元组；包名可能为 null（无法确定时由校验安全失败）。</returns>
        /// <remarks>
        /// 解析顺序：
        /// <list type="number">
        /// <item>优先按实例运行时类型查询 Registry（覆盖最终业务类型与生成基类，二者均可能注册），
        ///   命中则返回注册描述的 PackageName 与 URL。</item>
        /// <item>未注册类型（如池化复用的生成基类实例）：从 FairyGUI 运行时对象取得包名与组件资源名。
        ///   <c>packageItem.name</c> 为组件资源名；包名通过 <c>packageItem.owner.name</c> 取得。</item>
        /// <item>均无法取得时返回 (null, null)，由 <see cref="ValidateDynamicWidgetPackageOwnership"/> 拒绝（安全失败）。</item>
        /// </list>
        /// </remarks>
        private (string packageName, string url) ResolveWidgetPackageInfo(FUIWidget widget)
        {
            Type widgetType = widget.GetType();

            // 优先按实例运行时类型查询 Registry。
            if (_registry.TryGetDescriptor(widgetType, out FUIDescriptor desc))
            {
                return (desc.PackageName, desc.URL);
            }

            // 未注册类型：尝试从 FairyGUI 运行时对象取得包名与组件资源名。
            // packageItem.name 为组件资源名；包名通过 packageItem.owner.name 取得。
            // 若无法取得，返回 (null, null)，由校验安全失败。
            string pkgName = null;
            string resName = null;
            try
            {
                FairyGUI.PackageItem item = widget.packageItem;
                if (item != null)
                {
                    resName = item.name;
                    if (item.owner != null)
                    {
                        pkgName = item.owner.name;
                    }
                }
            }
            catch
            {
                // 取 packageItem 失败时忽略，pkgName 保持 null，校验将安全失败。
            }

            string url = (pkgName != null && resName != null) ? $"ui://{pkgName}/{resName}" : null;
            return (pkgName, url);
        }

        /// <summary>
        /// 受控 Attach 共享实现：幂等 <see cref="FUIWidget.AttachContext"/> + 幂等 <see cref="FUIWidget.InvokeOnCreate"/>。
        /// </summary>
        /// <param name="widget">Widget 实例。</param>
        /// <param name="ownerWindow">所属窗口。</param>
        /// <param name="context">运行时上下文，可为 null。</param>
        /// <remarks>
        /// 供 <see cref="CreateDynamicWidget{TWidget}"/> 与 <see cref="AttachDynamicWidget"/> 共享。
        /// <para>
        /// AttachContext 幂等：重复以相同 owner 调用只重置字段，不重复触发 OnCreate
        /// （spec: 动态列表 Widget——不得因重复 Attach 重复执行创建生命周期）。
        /// owner 变更时通过 <see cref="FUIWidget.LastAttachOwnerChanged"/> 暴露诊断
        /// （design.md 决策6 风险项：检测 owner 变更，复用入口在交付业务前重新 Attach）。
        /// </para>
        /// <para>
        /// InvokeOnCreate 幂等：已执行过 OnCreate 的实例跳过，保证单个实例只执行一次创建生命周期
        /// （design.md 决策5/6）。池化复用场景下 OnCreate 不重复执行。
        /// </para>
        /// </remarks>
        private void AttachAndStartWidget(FUIWidget widget, FUIWindow ownerWindow, object context)
        {
            // 1. 幂等 Attach：设置 OwnerWindow/Context/IsAttached，检测 owner 变更并设置诊断标记。
            widget.AttachContext(ownerWindow, context);

            // 2. 幂等 OnCreate：已创建的实例跳过，保证不重复执行创建生命周期。
            //    初始 Widget 树与动态入口共用此幂等保护（spec: 不得因重复 Attach 重复执行创建生命周期）。
            widget.InvokeOnCreate();
        }

        /// <summary>
        /// 查询是否存在存活窗口、缓存窗口、创建任务或上层依赖引用指定包（6.5 实现）。
        /// </summary>
        /// <param name="packageName">逻辑包名。</param>
        /// <returns>true 表示存在存活/缓存窗口、创建任务或上层依赖引用该包，不应卸载；false 表示可卸载。</returns>
        /// <remarks>
        /// 实现依据：design.md 决策9“最终卸载必须……没有存活或缓存窗口、没有创建任务、没有上层依赖”，
        /// 以及 spec“包租约控制缓存和卸载——只有不存在存活或缓存对象、创建任务、上层依赖和待完成资源操作时，
        /// 包才可在延迟窗口结束后卸载”。
        /// <para>
        /// 本方法由 <see cref="PackageLoader.CanUnload"/> 在包卸载前置检查中调用（通过
        /// <see cref="PackageLoader.WindowStateProvider"/> 全局注册），使 Delayed 延迟卸载路径与
        /// 显式 <see cref="PackageLoader.UnloadPackage"/> 调用都能查询窗口状态。
        /// </para>
        /// <para>
        /// 检查范围（统一纳入卸载前置检查）：
        /// <list type="bullet">
        /// <item>存活窗口（Open/Hidden）：窗口持有包租约且仍在显示或临时隐藏，包不可卸载</item>
        /// <item>缓存窗口（Cached）：窗口 Close 后显式缓存，仍持有包租约，包不可卸载
        ///   （spec：缓存窗口仍持有包——其包和依赖 SHALL 保持租约）</item>
        /// <item>创建任务（Loading/Opening）：窗口正在加载或打开中，包租约即将或已经持有，包不可卸载
        ///   （design.md 决策9：没有创建任务）</item>
        /// <item>上层依赖：其他包依赖该包，且使用那些上层包的窗口存活/缓存/创建中，
        ///   则该包也不可卸载（design.md 决策9：没有上层依赖）。
        ///   上层依赖通过窗口所属包的 <see cref="UIPackage.dependencies"/> 递归判定</item>
        /// </list>
        /// </para>
        /// <para>
        /// 与引用计数的关系：窗口通过 <see cref="PackageLease"/> 持有包引用，存活/缓存窗口的 lease
        /// 已贡献引用计数，故 <see cref="PackageRecord.ReferenceCount"/> != 0 时本方法通常不会被调用
        /// （CanUnload 先检查引用为零）。但创建任务期间（Loading 状态）lease 尚未交付，
        /// 引用计数可能暂时为零；此时本方法的创建任务检查提供关键保护，避免加载中的包被误卸载。
        /// 上层依赖检查同样是对引用计数的双重保护：上层包的 lease 持有依赖 lease 贡献引用计数，
        /// 但在依赖包 lease 交付时序窗口内，显式检查更安全。
        /// </para>
        /// <para>
        /// 性能：本方法在引用计数已归零的延迟卸载路径调用，频率低；遍历 _windowEntries 与
        /// 依赖集合的复杂度为 O(窗口数 × 依赖深度)，在常规 UI 规模下可接受。
        /// </para>
        /// <para>
        /// 边界约束：本方法不修改任何状态，只读查询；不反向依赖 GameLogic/GamePlay/GameBattle。
        /// </para>
        /// </remarks>
        public bool HasActiveOrCachedWindow(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return false;
            }

            // 遍历全部窗口条目，检查是否有存活/缓存/创建中的窗口直接或间接引用目标包。
            foreach (KeyValuePair<Type, WindowEntry> pair in _windowEntries)
            {
                WindowEntry entry = pair.Value;
                if (entry == null || entry.Descriptor.URL == null)
                {
                    continue;
                }

                // 判定该窗口是否处于“占用包”的状态：存活（Open/Hidden）、缓存（Cached）、创建任务（Loading/Opening）。
                // Closing 状态由 Close 流程同步推进到 Cached/Disposed，短暂存在期间也视为占用（保守保护）。
                if (!IsWindowHoldingPackage(entry))
                {
                    continue;
                }

                // 直接引用：窗口所属包就是目标包。
                if (string.Equals(entry.Descriptor.PackageName, packageName, StringComparison.Ordinal))
                {
                    return true;
                }

                // 上层依赖：窗口所属包的声明依赖链中包含目标包。
                // 例如窗口使用包 B，包 B 依赖包 A；若窗口存活/缓存/创建中，则包 A 也不可卸载。
                if (WindowPackageDependsOn(entry, packageName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判定窗口条目是否处于占用包资源的状态（存活/缓存/创建任务）。
        /// </summary>
        /// <param name="entry">窗口条目。</param>
        /// <returns>true 表示窗口存活、缓存或正在创建中，占用包资源。</returns>
        /// <remarks>
        /// 存活窗口（Open/Hidden）：持有 lease 且在显示或临时隐藏。
        /// 缓存窗口（Cached）：Close 后显式缓存，仍持有 lease（spec：缓存窗口仍持有包）。
        /// 创建任务（Loading/Opening）：正在加载或打开中，lease 即将或已经交付（design.md 决策9：没有创建任务）。
        /// Closing 状态保守视为占用：Close 流程同步推进到 Cached/Disposed，但异步路径下可能短暂处于 Closing。
        /// Absent/Disposed 终态不占用：无实例或已最终释放，不持有 lease。
        /// </remarks>
        private bool IsWindowHoldingPackage(WindowEntry entry)
        {
            FUIWindowState state = entry.State;
            return state == FUIWindowState.Open
                || state == FUIWindowState.Hidden
                || state == FUIWindowState.Cached
                || state == FUIWindowState.Loading
                || state == FUIWindowState.Opening
                || state == FUIWindowState.Closing;
        }

        /// <summary>
        /// 判定窗口所属包的声明依赖链中是否包含目标包（上层依赖检查）。
        /// </summary>
        /// <param name="entry">窗口条目，已确认处于占用包资源的状态。</param>
        /// <param name="targetPackageName">目标包名（待判定是否可卸载的包）。</param>
        /// <returns>true 表示窗口所属包直接或间接依赖目标包。</returns>
        /// <remarks>
        /// 依赖来源：<see cref="UIPackage.dependencies"/>，每个依赖字典含 "id" 与 "name" 键，
        /// 以 "name" 作为依赖包逻辑名（与 <see cref="PackageLoader.AcquireDependenciesAsync"/> 一致）。
        /// <para>
        /// 递归遍历：窗口包 → 其依赖包 → 依赖的依赖……，检测目标包是否出现在依赖链中。
        /// 使用已访问集合防止依赖环导致无限递归（依赖环在加载阶段已诊断，但此处防御性保护）。
        /// </para>
        /// <para>
        /// lease/package 为 null 时（如窗口正在 Loading 中 lease 尚未交付）返回 false：
        /// 此时窗口所属包尚未完成加载，依赖关系无法判定；引用计数与创建任务状态检查已提供保护。
        /// </para>
        /// </remarks>
        private bool WindowPackageDependsOn(WindowEntry entry, string targetPackageName)
        {
            PackageLease lease = entry.Lease;
            if (lease == null)
            {
                // 窗口尚未完成包加载（lease 未交付）：依赖关系无法判定，返回 false。
                // 此时若目标包是窗口包的依赖，加载流程会递归 Acquire 目标包使其引用计数 > 0，
                // CanUnload 的引用零检查会拦截卸载；本方法的上层依赖检查是对此的双重保护。
                return false;
            }

            PackageRecord record = lease.Record;
            if (record == null)
            {
                return false;
            }

            UIPackage package = record.Package;
            if (package == null)
            {
                return false;
            }

            // 递归检查依赖链，使用已访问集合防止依赖环导致无限递归。
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            return PackageDependsOnRecursive(package, targetPackageName, visited);
        }

        /// <summary>
        /// 递归检查指定包的声明依赖链中是否包含目标包。
        /// </summary>
        /// <param name="package">当前检查的 FairyGUI 包。</param>
        /// <param name="targetPackageName">目标包名。</param>
        /// <param name="visited">已访问包名集合，防止依赖环导致无限递归。</param>
        /// <returns>true 表示当前包直接或间接依赖目标包。</returns>
        private bool PackageDependsOnRecursive(UIPackage package, string targetPackageName, HashSet<string> visited)
        {
            if (package == null)
            {
                return false;
            }

            // FairyGUI dependencies API：Dictionary<string,string>[]，每项含 "id" 与 "name"。
            Dictionary<string, string>[] deps = package.dependencies;
            if (deps == null || deps.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < deps.Length; i++)
            {
                Dictionary<string, string> dep = deps[i];
                if (dep == null)
                {
                    continue;
                }

                if (!dep.TryGetValue("name", out string depName) || string.IsNullOrEmpty(depName))
                {
                    continue;
                }

                // 直接依赖命中目标包。
                if (string.Equals(depName, targetPackageName, StringComparison.Ordinal))
                {
                    return true;
                }

                // 防止依赖环导致无限递归：已访问的包不再递归。
                if (!visited.Add(depName))
                {
                    continue;
                }

                // 递归检查依赖的依赖：通过 PackageLoader 查找依赖包的 PackageRecord 取得 UIPackage。
                PackageRecord depRecord = PackageLoader.FindRecord(depName);
                if (depRecord != null && depRecord.Package != null)
                {
                    if (PackageDependsOnRecursive(depRecord.Package, targetPackageName, visited))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 模块已 Shutdown 时抛 <see cref="FUIException"/>，阻止 Shutdown 后的公开操作。
        /// </summary>
        private void ThrowIfShutdown()
        {
            if (_isShutdown)
            {
                throw new FUIException("GameFUI 模块已 Shutdown，禁止后续操作。");
            }
        }

        /// <summary>
        /// 输出窗口状态变化诊断日志（任务 6.7）。
        /// </summary>
        /// <param name="entry">窗口条目。</param>
        /// <param name="previous">转换前的状态。</param>
        /// <remarks>
        /// 任务 6.7 要求覆盖窗口状态变化诊断点，并避免每帧无状态变化时重复输出。
        /// 本方法只在显式状态转换发生时调用一次（紧跟 <see cref="WindowEntry.TransitionTo"/> 之后），
        /// 不在任何 Update/每帧轮询路径中调用，故天然满足“避免每帧重复输出”要求。
        /// 日志包含窗口类型、URL、包名、前后状态、当前操作版本，便于排查状态机异常。
        /// </remarks>
        private void LogWindowStateTransition(WindowEntry entry, FUIWindowState previous)
        {
            if (entry == null)
            {
                return;
            }

            Log.Info(
                "[GameFUI] 窗口状态变化：type={0}, {1} -> {2}, url={3}, pkg={4}, opVer={5}",
                entry.WindowType?.FullName, previous, entry.State,
                entry.Descriptor.URL, entry.Descriptor.PackageName, entry.OperationVersion);
        }

        /// <summary>
        /// 输出包引用与 handle 诊断日志（任务 6.7），在窗口加载完成时调用。
        /// </summary>
        /// <param name="entry">窗口条目，已持有 lease。</param>
        /// <remarks>
        /// 任务 6.7 要求覆盖包引用、依赖链、handle 数量、加载耗时诊断点。
        /// 本方法只在窗口加载成功（Loading -> Opening）边界调用一次，不重复输出。
        /// </remarks>
        private void LogWindowPackageDiagnostic(WindowEntry entry)
        {
            if (entry == null || entry.Lease == null)
            {
                return;
            }

            PackageRecord record = entry.Lease.Record;
            if (record == null)
            {
                return;
            }

            Log.Info(
                "[GameFUI] 窗口包诊断：type={0}, pkg={1}, refCount={2}, handles={3}, depChain=[{4}], loadMs={5}",
                entry.WindowType?.FullName, record.PackageName, record.ReferenceCount,
                record.AssetHandles.Count, record.BuildDependencyChainText(), record.LoadDurationMs);
        }

        /// <summary>
        /// 输出窗口加载失败上下文诊断日志（任务 6.7）。
        /// </summary>
        /// <param name="entry">窗口条目。</param>
        /// <param name="failureContext">失败上下文（异常消息或描述）。</param>
        /// <remarks>
        /// 任务 6.7 要求覆盖失败上下文诊断点。本方法只在加载失败事件发生时调用一次，
        /// 日志包含窗口类型、URL、包名、状态与失败上下文，便于定位加载失败原因。
        /// </remarks>
        private void LogWindowFailureContext(WindowEntry entry, string failureContext)
        {
            if (entry == null)
            {
                return;
            }

            Log.Error(
                "[GameFUI] 窗口加载失败：type={0}, state={1}, url={2}, pkg={3}, opVer={4}, failure={5}",
                entry.WindowType?.FullName, entry.State, entry.Descriptor.URL,
                entry.Descriptor.PackageName, entry.OperationVersion,
                failureContext ?? "<未知>");
        }
    }
}
