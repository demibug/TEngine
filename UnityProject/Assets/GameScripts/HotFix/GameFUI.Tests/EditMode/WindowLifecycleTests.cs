using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UIBattle;

namespace GameFUI.Tests.EditMode
{
    /// <summary>
    /// FUIModule 窗口生命周期的 EditMode 单元测试，覆盖任务 5.12 的全部场景：
    /// 生命周期次数（OnCreate 一次、OnOpen/OnRefresh/OnClose 按每轮打开）、
    /// 并发不同参数 Show（同类型并发 Show 合并，各自刷新）、
    /// 调用方取消隔离（单调用方取消不传播到共享加载）、
    /// 加载期间 Close（加载期间 Close 使加载操作过期并回滚）、
    /// Close/Show 交叠（OnClose 内重入 ShowAsync 构造真实 Closing 期间交叠，等待重开不抛异常；Close 后 Show 等价场景作为回归覆盖）、
    /// 失败重试（加载失败后可重新 Show）、
    /// 层级（窗口挂载到正确层级）、
    /// 安全区（Safe 子容器对齐 Screen.safeArea）、
    /// 全屏遮挡（全屏窗口遮挡下层）。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - design.md 决策4/5/7；
    /// - spec fairygui-window-runtime 全部 Requirement。
    ///
    /// 测试装配：使用内存资源 provider 与测试 owner（<see cref="TestFUIOwner"/>）、
    /// 测试窗口（<see cref="LifecycleTestWindow"/>）完成装配。内存 provider 预设
    /// UIBattle 描述资源与 UICommon 描述资源（含 Atlas），使包加载在 EditMode 内完整成功。
    ///
    /// 复用证据：
    /// - 复用 <see cref="InMemoryFUIResourceProvider"/>（4.3 产出，通过 InternalsVisibleTo 暴露）；
    /// - 复用磁盘上真实发布的 <c>UIBattle_fui.bytes</c> 与 <c>UICommon_fui.bytes</c> 描述资源
    ///   （1.1 核对的规范资源清单），通过 <see cref="AssetDatabase.LoadAssetAtPath"/> 加载，
    ///   保留二进制字节完整性；
    /// - 复用 <see cref="TestFUIOwner.OwnerType"/>（3.6 产出，owner 类型标识）；
    /// - 复用 <see cref="FUI.RegisterModuleForTesting"/>（5.1 产出的 internal 测试入口）；
    /// - 复用 <see cref="FUIModule.BindingRegistry"/> / <see cref="FUIModule.LayerContainer"/> 等 internal 访问器。
    ///
    /// 资源隔离：每个测试在 <see cref="SetUp"/> 中重新注册模块并清空 FairyGUI 全局状态；
    /// <see cref="TearDown"/> 执行模块 Shutdown 与全局清理，避免跨测试残留。
    ///
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，只通过公开/internal API 访问被测对象。
    /// 本测试不依赖 GameLogic/GamePlay/GameBattle，不创建或修改 BattleModule。
    /// </remarks>
    [TestFixture]
    public class WindowLifecycleTests
    {
        /// <summary>
        /// 测试窗口使用的包名常量，与生成类型 <see cref="UI_BattleStartPanel"/> 的 PkgName 一致。
        /// </summary>
        private const string UIBattlePkg = "UIBattle";

        /// <summary>
        /// UICommon 共享依赖包名常量。
        /// </summary>
        private const string UICommonPkg = "UICommon";

        /// <summary>
        /// 每个测试前的状态基线重置：清空 FairyGUI 全局包注册表与 PackageLoader 注册表，
        /// 重新注册 GameFUI 模块，完成测试 owner 注册与冻结。
        /// </summary>
        /// <remarks>
        /// 装配流程遵循 design.md 决策10：
        /// FUI.RegisterModuleForTesting -> 测试 owner 注册 UIBattle -> FreezeBindings。
        /// 注意：UIBattle 包无外部依赖（测试使用内存描述无依赖声明），UICommon 作为共享包
        /// 在需要时预设；本组测试默认只加载 UIBattle，不强制加载 UICommon。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            // 清空 FairyGUI 全局状态与 PackageLoader 静态注册表，避免跨测试残留。
            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();

            // 清空 FUI 门面静态缓存（防止上一测试残留模块实例）。
            // 若上一测试异常未完成 Shutdown，此处强制清理使新测试能重新注册。
            FUI.ClearModuleForShutdown();

            // 清空 FUIObjectFactoryIntegration 的活动 Registry 静态引用，
            // 防止上一测试异常退出导致 InstallPackageItemExtensions 拒绝新 Registry。
            FUIObjectFactoryIntegration.ClearActiveRegistry();

            // 重置测试窗口的静态生命周期计数器，使每个测试从 0 开始统计。
            LifecycleTestWindow.ResetCounters();

            // 确保 FairyGUI Stage/GRoot 已初始化（EditMode 下需要主动触发）。
            // GRoot.inst getter 会在首次访问时调用 Stage.Instantiate() 创建 Stage 与 GRoot。
            _ = GRoot.inst;
        }

        /// <summary>
        /// 每个测试后的状态基线重置，与 <see cref="SetUp"/> 对称，确保即使测试中途失败
        /// 也不会残留全局状态污染后续测试。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            // 若模块仍注册，执行 Shutdown 完整清理。
            // FUI.Module getter 在 _module 为 null 时抛 FUIException，用 try/catch 保护。
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
                // 其他异常（如 FairyGUI 对象已释放）不阻塞 TearDown 清理。
            }

            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();
            FUI.ClearModuleForShutdown();
        }

        // ============================================================
        // 任务 8.1：FUI 查询门面——未注册、Loading、Open、Cached、Disposed
        // ============================================================

        /// <summary>
        /// 模块未注册时，查询门面应明确失败，且不得隐式创建模块。
        /// </summary>
        [Test]
        [Description("FUI 查询门面：未注册时抛出明确异常且不隐式注册模块。")]
        public void FacadeQuery_Unregistered_ThrowsWithoutImplicitRegistration()
        {
            Assert.Throws<FUIException>(() => FUI.GetWindow<LifecycleTestWindow>(),
                "未注册时 GetWindow 应通过 Module getter 抛出明确异常。");
            Assert.Throws<FUIException>(() => FUI.HasWindow<LifecycleTestWindow>(),
                "未注册时 HasWindow 应通过 Module getter 抛出明确异常。");
            Assert.Throws<FUIException>(() => { _ = FUI.Module; },
                "查询失败后模块仍应保持未注册，不能隐式创建默认实例。");
        }

        /// <summary>
        /// 查询门面应覆盖 Loading、Open 与 Cached，并始终返回同一个已注册模块管理的状态。
        /// </summary>
        [Test]
        [Description("FUI 查询门面：Loading、Open、Cached 状态只读转发且不创建额外窗口。")]
        public async UniTask FacadeQuery_LoadingOpenCached_ForwardsWithoutImplicitCreation()
        {
            FUIModule module = SetupModuleWithLifecycleWindow(
                loadDelayMs: 100,
                cacheMode: FUICacheMode.Cache);

            UniTask<LifecycleTestWindow> showTask = FUI.ShowAsync<LifecycleTestWindow>();

            Assert.AreSame(module, FUI.Module, "查询应继续使用显式注册的同一模块实例。");
            Assert.AreEqual(FUIWindowState.Loading, GetEntryState(module, typeof(LifecycleTestWindow)),
                "延迟加载期间窗口条目应处于 Loading。");
            Assert.IsTrue(FUI.HasWindow<LifecycleTestWindow>(),
                "Loading 条目应被视为存在，以便调用方观察正在进行的窗口操作。");
            Assert.IsNull(FUI.GetWindow<LifecycleTestWindow>(),
                "Loading 早期尚未构造实例时 GetWindow 应返回 null，且不得隐式创建窗口。");

            LifecycleTestWindow window = await showTask;

            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "Show 完成后窗口应处于 Open。");
            Assert.IsTrue(FUI.HasWindow<LifecycleTestWindow>(), "Open 窗口应存在。");
            Assert.AreSame(window, FUI.GetWindow<LifecycleTestWindow>(),
                "Open 状态应返回当前模块管理的同一窗口实例。");

            FUI.Close<LifecycleTestWindow>();

            Assert.AreEqual(FUIWindowState.Cached, GetEntryState(module, typeof(LifecycleTestWindow)),
                "显式 Cache 窗口关闭后应处于 Cached。");
            Assert.IsTrue(FUI.HasWindow<LifecycleTestWindow>(), "Cached 窗口仍应存在。");
            Assert.AreSame(window, FUI.GetWindow<LifecycleTestWindow>(),
                "Cached 状态应返回保留的同一窗口实例。");
            Assert.AreEqual(1, module._windowEntries.Count,
                "查询门面不得创建额外窗口条目。");
        }

        /// <summary>
        /// 默认不缓存窗口关闭并进入 Disposed 后，查询门面应返回不存在。
        /// </summary>
        [Test]
        [Description("FUI 查询门面：Disposed 状态返回不存在且不隐式重建窗口。")]
        public async UniTask FacadeQuery_Disposed_ReturnsMissingWithoutImplicitRecreation()
        {
            FUIModule module = SetupModuleWithLifecycleWindow();
            LifecycleTestWindow window = await FUI.ShowAsync<LifecycleTestWindow>();

            FUI.Close(window);

            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(LifecycleTestWindow)),
                "默认 CacheMode=None 的窗口关闭后应处于 Disposed。");
            Assert.IsFalse(FUI.HasWindow<LifecycleTestWindow>(), "Disposed 窗口不应视为存在。");
            Assert.IsNull(FUI.GetWindow<LifecycleTestWindow>(),
                "Disposed 状态查询应返回 null，且不得隐式创建新实例。");
            Assert.AreEqual(1, LifecycleTestWindow.OnCreateCount,
                "Disposed 后查询不得触发第二次窗口创建。");
        }

        // ============================================================
        // 场景 a：生命周期次数——OnCreate 一次，OnOpen/OnRefresh/OnClose 按每轮打开
        // ============================================================

        /// <summary>
        /// 生命周期次数：首次打开并关闭后，OnCreate 执行一次，OnOpen/OnRefresh/OnClose 各执行一次。
        /// 再次打开（新实例）后，OnCreate 再执行一次（新实例），OnOpen/OnRefresh/OnClose 再各执行一次。
        /// </summary>
        /// <remarks>
        /// spec "窗口生命周期次数确定"——OnCreate 和 OnDispose 在单个实例上各执行一次，
        /// OnOpen、OnRefresh、OnClose 按每轮打开执行。
        /// design.md 决策5：首次创建 OnCreate 一次；每轮打开 OnOpen -> OnRefresh；每轮关闭 OnClose。
        /// </remarks>
        [Test]
        [Description("生命周期次数：OnCreate 一次，OnOpen/OnRefresh/OnClose 按每轮打开各一次。")]
        public async UniTask LifecycleCounts_OnCreateOnce_OnOpenOnRefreshOnClosePerRound()
        {
            // 安排：装配模块与注册测试窗口。
            FUIModule module = SetupModuleWithLifecycleWindow();

            // 执行：首次 Show 并 Close。
            LifecycleTestWindow window1 = await module.ShowAsync<LifecycleTestWindow>();
            module.Close<LifecycleTestWindow>();

            // 断言：首轮打开 OnCreate=1, OnOpen=1, OnRefresh=1, OnClose=1。
            Assert.AreEqual(1, LifecycleTestWindow.OnCreateCount, "首次打开后 OnCreate 应执行一次。");
            Assert.AreEqual(1, LifecycleTestWindow.OnOpenCount, "首次打开后 OnOpen 应执行一次。");
            Assert.AreEqual(1, LifecycleTestWindow.OnRefreshCount, "首次打开后 OnRefresh 应执行一次。");
            Assert.AreEqual(1, LifecycleTestWindow.OnCloseCount, "首次关闭后 OnClose 应执行一次。");

            // 执行：再次 Show（新实例，因默认 CacheMode=None 已 Dispose）。
            LifecycleTestWindow window2 = await module.ShowAsync<LifecycleTestWindow>();
            module.Close<LifecycleTestWindow>();

            // 断言：第二轮打开累计 OnCreate=2（新实例），OnOpen=2，OnRefresh=2，OnClose=2。
            Assert.AreEqual(2, LifecycleTestWindow.OnCreateCount, "第二次打开新实例后 OnCreate 累计应执行两次。");
            Assert.AreEqual(2, LifecycleTestWindow.OnOpenCount, "第二次打开后 OnOpen 累计应执行两次。");
            Assert.AreEqual(2, LifecycleTestWindow.OnRefreshCount, "第二次打开后 OnRefresh 累计应执行两次。");
            Assert.AreEqual(2, LifecycleTestWindow.OnCloseCount, "第二次关闭后 OnClose 累计应执行两次。");

            // 验证实例不同（默认 Close 即 Dispose，第二次 Show 创建新实例）。
            Assert.AreNotSame(window1, window2, "默认 CacheMode=None 下两次 Show 应创建不同实例。");
        }

        // ============================================================
        // 场景 b：并发不同参数 Show——同类型并发 Show 合并，各自刷新
        // ============================================================

        /// <summary>
        /// 并发不同参数 Show：两个调用方以不同参数并发 Show 同一窗口类型，
        /// 系统只创建一个实例，并按请求顺序对该实例各执行一次刷新。
        /// </summary>
        /// <remarks>
        /// spec "同类型并发打开串行收敛"——两个调用在窗口尚未创建时以不同参数并发 Show 同一窗口类型，
        /// 系统 SHALL 只创建一个实例，并按请求顺序对该实例各执行一次刷新。
        /// design.md 决策4：同类型请求只合并包加载和实例创建；每个未取消的 Show 请求仍进入 FIFO 刷新队列。
        /// </remarks>
        [Test]
        [Description("并发不同参数 Show：同类型并发 Show 合并加载与创建，各自执行一次刷新。")]
        public async UniTask ConcurrentShow_DifferentArgs_MergesAndRefreshesEach()
        {
            // 安排：装配模块。
            FUIModule module = SetupModuleWithLifecycleWindow();

            // 执行：并发发起两个 Show，参数不同。
            UniTask<LifecycleTestWindow> task1 = module.ShowAsync<LifecycleTestWindow>("arg1");
            UniTask<LifecycleTestWindow> task2 = module.ShowAsync<LifecycleTestWindow>("arg2");
            (LifecycleTestWindow window1, LifecycleTestWindow window2) = await UniTask.WhenAll(task1, task2);

            // 断言：两个调用方获得同一实例。
            Assert.IsNotNull(window1, "调用方 1 应获得非空窗口。");
            Assert.IsNotNull(window2, "调用方 2 应获得非空窗口。");
            Assert.AreSame(window1, window2, "并发 Show 同类型应合并为同一实例。");

            // 断言：OnCreate 只执行一次（实例只创建一次）。
            Assert.AreEqual(1, LifecycleTestWindow.OnCreateCount, "并发 Show 应只创建一个实例，OnCreate 只执行一次。");

            // 断言：OnRefresh 执行两次（每个有效请求各一次），OnOpen 只执行一次（每轮打开一次）。
            Assert.AreEqual(1, LifecycleTestWindow.OnOpenCount, "并发 Show 共享同一轮打开，OnOpen 只执行一次。");
            Assert.AreEqual(2, LifecycleTestWindow.OnRefreshCount, "两个并发请求应各执行一次 OnRefresh。");

            // 断言：最终 UserData 由最后一个刷新请求决定（spec：最终显示状态由最后一个完成刷新请求决定）。
            // 注意：两个请求按入队顺序刷新，最终 UserData 为 "arg2"。
            Assert.AreEqual("arg2", window1.UserData, "最终 UserData 应由最后一个刷新请求决定。");
        }

        // ============================================================
        // 场景 c：调用方取消隔离——单调用方取消不传播到共享加载
        // ============================================================

        /// <summary>
        /// 调用方取消隔离：调用方 A 与 B 并发 Show 同一窗口类型，A 取消自己的等待，
        /// B 的 Show 应正常完成，共享加载不被取消。
        /// </summary>
        /// <remarks>
        /// spec "Show 操作可等待且错误明确"——调用方取消 SHALL 以 OperationCanceledException 表达，
        /// 且不得取消仍被其他请求共享的包加载。
        /// design.md 决策4：共享加载只受模块 lifetime token 控制，调用方令牌只取消该调用方的等待。
        /// </remarks>
        [Test]
        [Description("调用方取消隔离：单调用方取消不传播到共享加载，其他调用方正常完成。")]
        public async UniTask CallerCancellation_DoesNotPropagateToSharedLoad()
        {
            // 安排：装配模块（使用延迟 provider 使取消窗口存在）。
            FUIModule module = SetupModuleWithLifecycleWindow(loadDelayMs: 50);

            // 执行：调用方 A 带取消令牌，调用方 B 无取消令牌。
            CancellationTokenSource ctsA = new CancellationTokenSource();
            UniTask<LifecycleTestWindow> taskA = module.ShowAsync<LifecycleTestWindow>(ctsA.Token, "argA");
            UniTask<LifecycleTestWindow> taskB = module.ShowAsync<LifecycleTestWindow>("argB");

            // A 在加载完成前取消。
            ctsA.Cancel();

            // A 应收到 OperationCanceledException。
            bool aCancelled = false;
            try
            {
                await taskA;
            }
            catch (OperationCanceledException)
            {
                aCancelled = true;
            }

            // B 应正常完成。
            LifecycleTestWindow windowB = await taskB;

            // 断言。
            Assert.IsTrue(aCancelled, "调用方 A 取消应抛 OperationCanceledException。");
            Assert.IsNotNull(windowB, "调用方 B 应正常完成并获得窗口。");
            Assert.AreEqual(1, LifecycleTestWindow.OnCreateCount, "共享加载不应被取消，实例应创建一次。");

            ctsA.Dispose();
        }

        // ============================================================
        // 场景 d：加载期间 Close——加载期间 Close 使加载操作过期并回滚
        // ============================================================

        /// <summary>
        /// 加载期间 Close：窗口仍在加载时收到 Close，旧打开操作 SHALL 失效，
        /// 完成后的过期对象不得闪现或执行过期回调，并 SHALL 释放该操作取得的资源租约。
        /// </summary>
        /// <remarks>
        /// spec "加载期间关闭"——窗口仍在加载时收到 Close，
        /// 旧打开操作 SHALL 失效，完成后的过期对象不得闪现或执行过期回调，
        /// 并 SHALL 释放该操作取得的资源租约。
        /// design.md 决策4：Close 在任何非终态递增 version，使旧操作完成后只能回滚。
        /// </remarks>
        [Test]
        [Description("加载期间 Close：加载期间 Close 使操作版本过期，完成后回滚不闪现。")]
        public async UniTask CloseDuringLoad_MakesOperationStale_AndRollsBack()
        {
            // 安排：装配模块（使用延迟 provider 使 Close 在加载期间生效）。
            FUIModule module = SetupModuleWithLifecycleWindow(loadDelayMs: 100);

            // 执行：发起 Show（不 await），在加载期间 Close。
            UniTask<LifecycleTestWindow> showTask = module.ShowAsync<LifecycleTestWindow>();

            // Close 在加载期间执行（递增 operation version）。
            module.Close<LifecycleTestWindow>();

            // Show 应因版本过期而抛 OperationCanceledException 或 FUIException。
            bool operationStale = false;
            try
            {
                await showTask;
            }
            catch (OperationCanceledException)
            {
                operationStale = true;
            }
            catch (FUIException)
            {
                // 加载期间 Close 也可能以 FUIException 表达，取决于回滚路径。
                operationStale = true;
            }

            // 断言：Show 因版本过期而失败。
            Assert.IsTrue(operationStale, "加载期间 Close 后 Show 应因版本过期而失败。");

            // 断言：过期对象未执行 OnOpen/OnRefresh（不闪现）。
            Assert.AreEqual(0, LifecycleTestWindow.OnOpenCount, "过期操作不应执行 OnOpen。");
            Assert.AreEqual(0, LifecycleTestWindow.OnRefreshCount, "过期操作不应执行 OnRefresh。");

            // 断言：entry 状态已回滚（不在 Open 状态）。
            // 通过 internal _windowEntries 访问（InternalsVisibleTo）。
            Assert.IsTrue(module._windowEntries.TryGetValue(typeof(LifecycleTestWindow), out WindowEntry entry),
                "应存在窗口条目。");
            Assert.AreNotEqual(FUIWindowState.Open, entry.State, "过期操作回滚后不应处于 Open 状态。");
        }

        // ============================================================
        // 场景 e：Close/Show 交叠——Close 后 Show 从 Cached/Absent 重开，不复活旧打开域
        // ============================================================

        /// <summary>
        /// Close/Show 交叠（默认 None 缓存）：Close 完成后再 Show 应从新 Absent entry 创建新实例，
        /// 不复活旧打开域，不抛防御性 FUIException，且租约无泄漏。
        /// </summary>
        /// <remarks>
        /// spec "窗口状态转换受控" / "Closing 期间再次 Show"——新 Show SHALL 等待本轮 Close 完成后再
        /// 从 Cached 或 Absent 重新打开，不得中途复活旧打开域。
        /// design.md 决策4：Closing 期间的新 Show 排在 Close 之后，不复活旧 Open 域。
        ///
        /// 可达性说明（任务 8.3）：Closing 状态通过 OnClose 回调内重入 module.ShowAsync 可达
        /// （CloseEntryCore 在 :885 TransitionTo(Closing) 后于 :896 调用 InvokeOnClose，
        /// 业务 OnClose 内调用 ShowAsync 时 entry 处于 Closing）。真实交叠测试见
        /// <see cref="ClosingShowOverlap_None_WaitsAndReopensFromAbsent"/>。
        /// 本测试作为回归覆盖：Close 完成后立即 Show 从 Absent 重新打开，验证不抛异常 +
        /// 旧打开域不复活（新实例、OnCreate 再次执行）+ 租约无泄漏（引用计数归零后重新获取）。
        /// </remarks>
        [Test]
        [Description("Close/Show 交叠：Close 后 Show 从新 Absent 重开，不复活旧域，租约无泄漏。")]
        public async UniTask ShowAfterClose_DoesNotReviveOldOpenDomain()
        {
            // 安排：装配模块（默认 CacheMode=None）。
            FUIModule module = SetupModuleWithLifecycleWindow();

            // 执行：首次 Show 并 Close（同步推进到 Disposed）。
            LifecycleTestWindow window1 = await module.ShowAsync<LifecycleTestWindow>();
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "首次 Show 后应处于 Open。");

            // 断言：首次 Show 后租约已获取（UIBattle 引用 1，UICommon 依赖引用 1）。
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "首次 Show 后 UIBattle 引用计数应为 1。");
            Assert.AreEqual(1, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "首次 Show 后 UICommon 引用计数应为 1。");

            module.Close<LifecycleTestWindow>();

            // Close 后状态应为 Disposed（默认 CacheMode=None）。
            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(LifecycleTestWindow)),
                "默认 CacheMode=None Close 后应处于 Disposed。");

            // 断言：Close 释放实例后租约归零（无泄漏）。
            Assert.AreEqual(0, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "Close 后 UIBattle 引用计数应归零（无租约泄漏）。");
            Assert.AreEqual(0, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "Close 后 UICommon 引用计数应归零（无租约泄漏）。");

            // 执行：再次 Show——应从新 Absent entry 创建新实例，不抛异常，不复活旧打开域。
            LifecycleTestWindow window2 = await module.ShowAsync<LifecycleTestWindow>();

            // 断言：新实例不同（不复活旧实例）。
            Assert.AreNotSame(window1, window2, "Close 后再 Show 应创建新实例，不复活旧打开域。");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "再次 Show 后应处于 Open。");

            // 断言：OnCreate 执行两次（两个不同实例各一次），证明不复活旧打开域。
            Assert.AreEqual(2, LifecycleTestWindow.OnCreateCount, "两个不同实例应各执行一次 OnCreate。");

            // 断言：再次 Show 后重新获取租约（引用计数恢复为 1，新租约不复用旧 lease）。
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "再次 Show 后 UIBattle 引用计数应恢复为 1（新租约）。");
            Assert.AreEqual(1, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "再次 Show 后 UICommon 引用计数应恢复为 1（新租约）。");

            // 执行：再次 Close，验证最终无泄漏。
            module.Close<LifecycleTestWindow>();
            Assert.AreEqual(0, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "再次 Close 后 UIBattle 引用计数应归零（无租约泄漏）。");
            Assert.AreEqual(0, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "再次 Close 后 UICommon 引用计数应归零（无租约泄漏）。");
        }

        /// <summary>
        /// Close/Show 交叠（Cache 模式）：Close(Cached) 后再 Show 从 Cached 重新打开，
        /// 验证不抛异常、旧打开域不复活（OnCreate 不重复、新 OpenCts/事件域）、租约无泄漏、FIFO 顺序保留。
        /// </summary>
        /// <remarks>
        /// spec "Closing 期间再次 Show"——新 Show SHALL 等待本轮 Close 完成后再从 Cached 或 Absent 重新打开，
        /// 不得中途复活旧打开域。
        /// spec "缓存窗口重新打开"——SHALL 不重复执行 OnCreate，但 SHALL 重新建立打开取消域和事件域，
        /// 并再次执行 OnOpen 与 OnRefresh。
        /// design.md 决策4：Closing 期间的新 Show 排在 Close 之后，不复活旧 Open 域。
        ///
        /// 可达性说明（任务 8.3）：Closing 状态通过 OnClose 回调内重入 module.ShowAsync 可达。
        /// 真实交叠测试见 <see cref="ClosingShowOverlap_Cache_WaitsAndReopensFromCached"/>。
        /// 本测试作为回归覆盖：Close(Cached) 后立即 Show 从 Cached 重新打开，
        /// 验证不抛异常 + 旧打开域不复活（OnCreate 不重复）+ 新打开域建立（OnOpen/OnRefresh 按新轮执行）+
        /// 租约无泄漏（Cached 保留计数、最终释放归零）+ FIFO（重开请求刷新参数生效）。
        /// </remarks>
        [Test]
        [Description("Close/Show 交叠等价：Cache 模式 Close 后 Show 从 Cached 重开，不复活旧域，无泄漏。")]
        public async UniTask ShowAfterClose_Cached_ReopensWithoutLeakOrDomainRevival()
        {
            // 安排：装配 Cache 模式窗口。
            FUIModule module = SetupModuleWithLifecycleWindow(cacheMode: FUICacheMode.Cache);

            // 执行：首次 Show。
            LifecycleTestWindow window1 = await module.ShowAsync<LifecycleTestWindow>("first");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "首次 Show 后应处于 Open。");
            Assert.AreEqual("first", window1.UserData, "首个请求的 UserData 应生效。");

            // 断言：首次 Show 后租约已获取。
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "首次 Show 后 UIBattle 引用计数应为 1。");
            Assert.AreEqual(1, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "首次 Show 后 UICommon 引用计数应为 1。");

            // 执行：Close（Cache 模式 -> Cached，保留实例与租约）。
            module.Close<LifecycleTestWindow>();
            Assert.AreEqual(FUIWindowState.Cached, GetEntryState(module, typeof(LifecycleTestWindow)),
                "Cache 模式 Close 后应处于 Cached。");

            // 断言：Cached 保留租约（引用计数仍为 1，无泄漏）。
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "Cached 状态 UIBattle 引用计数应保持 1。");
            Assert.AreEqual(1, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "Cached 状态 UICommon 引用计数应保持 1。");

            // 执行：再次 Show——从 Cached 重新打开，不抛异常，不复活旧打开域。
            // 传入不同参数验证 FIFO 与刷新语义：重开请求的 UserData 应由其刷新参数决定。
            LifecycleTestWindow window2 = await module.ShowAsync<LifecycleTestWindow>("reopened");

            // 断言：Cached 重开复用同一实例（不创建新实例，旧打开域不复活）。
            Assert.AreSame(window1, window2, "Cache 模式重开应复用同一实例，不创建新实例。");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "重开后应处于 Open。");

            // 断言：OnCreate 只执行一次（实例未重建，旧打开域不复活）。
            Assert.AreEqual(1, LifecycleTestWindow.OnCreateCount, "Cached 重开不应重复 OnCreate。");
            // 断言：OnOpen 执行两次（每轮打开各一次，重新建立打开域）。
            Assert.AreEqual(2, LifecycleTestWindow.OnOpenCount, "两轮打开应各执行一次 OnOpen。");
            // 断言：OnRefresh 执行两次（首次 Show 与重开 Show 各一次，FIFO）。
            Assert.AreEqual(2, LifecycleTestWindow.OnRefreshCount, "两个有效请求应各执行一次 OnRefresh。");
            // 断言：OnClose 执行一次（仅首轮 Close，重开后尚未 Close）。
            Assert.AreEqual(1, LifecycleTestWindow.OnCloseCount, "首轮 Close 后 OnClose 应执行一次。");

            // 断言：FIFO——重开请求的刷新参数生效（最终 UserData 为 "reopened"）。
            Assert.AreEqual("reopened", window1.UserData, "重开请求的 UserData 应由其刷新参数决定。");

            // 断言：重开后租约仍保持（Cached 重开不重新获取租约，计数仍为 1）。
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "重开后 UIBattle 引用计数应保持 1（Cached 重开不重新获取租约）。");

            // 执行：最终 Close（Cached -> Disposed，释放实例与租约）。
            module.Close<LifecycleTestWindow>();
            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(LifecycleTestWindow)),
                "最终 Close 后应处于 Disposed。");

            // 断言：最终释放后租约归零（无泄漏）。
            Assert.AreEqual(0, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "最终释放后 UIBattle 引用计数应为 0（无租约泄漏）。");
            Assert.AreEqual(0, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "最终释放后 UICommon 引用计数应为 0（无租约泄漏）。");
        }

        // ============================================================
        // 场景 e2：真实 Closing 期间 Show 交叠（任务 8.3 返工——公开 API 重入构造）
        // ============================================================

        /// <summary>
        /// 真实 Closing 期间 Show 交叠（None 模式）：OnClose 回调内通过公开 API ShowAsync 重入，
        /// 此时 entry 处于 Closing。ShowAsync 不抛异常，等待 Close 完成后从新 Absent entry 重开。
        /// 验证：await 返回窗口实例、FIFO 顺序（重开请求 args 生效）、旧 Open 域不复活（新实例 OnCreate 再执行）、
        /// 租约无泄漏（Close 释放后重开获取，归零→1→最终归零）。
        /// </summary>
        /// <remarks>
        /// spec "Closing 期间再次 Show"——新 Show SHALL 等待本轮 Close 完成后再从 Cached 或 Absent 重新打开，
        /// 不得中途复活旧打开域。不得对该有效请求抛防御性 FUIException。
        ///
        /// 真实交叠构造（任务 8.3 硬要求③）：测试窗口的 OnClose 回调内调用 module.ShowAsync<T>()（公开 API）。
        /// 此时 CloseEntryCore 正在执行栈上、entry 处于 Closing（:885 TransitionTo(Closing) 之后）。
        /// ShowAsyncCore 重入执行读到 entry.State==Closing，命中等待重开分支。
        /// 这是通过公开 API 构造的真实交叠，不是"Close 后 Show"等价场景。
        /// </remarks>
        [Test]
        [Description("真实 Closing 期间 Show 交叠（None）：OnClose 内 ShowAsync 等待重开，不抛异常，无泄漏。")]
        public async UniTask ClosingShowOverlap_None_WaitsAndReopensFromAbsent()
        {
            // 安排：装配模块（默认 CacheMode=None）。
            FUIModule module = SetupModuleWithLifecycleWindow();

            // 首次 Show，获取初始窗口实例与初始 OpenCancellationToken。
            LifecycleTestWindow window1 = await module.ShowAsync<LifecycleTestWindow>("initial");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "首次 Show 后应处于 Open。");
            CancellationToken oldOpenToken = window1.OpenCancellationToken;
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "首次 Show 后 UIBattle 引用计数应为 1。");

            // 记录重入 Show 的待决任务与参数。
            UniTask<LifecycleTestWindow> reopenTask = default;
            const string reopenArg = "reopenArg";

            // 设置 OnClose 钩子：在 OnClose 内通过公开 API 构造重入 Show（entry 处于 Closing）。
            LifecycleTestWindow.OnCloseHook = _ =>
            {
                // 此时 CloseEntryCore 正在执行栈上，entry.State == Closing。
                // ShowAsyncCore 重入命中 Closing 分支，注册 pending reopen，返回挂起 UniTask。
                // 不抛异常（任务 8.3 硬要求①②）。
                reopenTask = FUI.ShowAsync<LifecycleTestWindow>(reopenArg);
            };

            // 执行：Close 触发 OnClose -> 重入 ShowAsync（交叠发生在 Closing 期间）。
            module.Close<LifecycleTestWindow>();

            // Close 返回后，None 模式的 pending reopen 通过 UniTaskVoid 异步重建。
            // await reopenTask 驱动 PlayerLoop 完成异步重开。
            LifecycleTestWindow window2 = await reopenTask;

            // 断言：ShowAsync 成功完成（不抛异常），返回窗口实例。
            Assert.IsNotNull(window2, "Closing 期间 Show 的 await 应返回重开后的窗口实例，不抛异常。");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "重开后应处于 Open。");

            // 断言：旧 Open 域不复活——None 模式重开创建新实例（OnCreate 再执行一次）。
            Assert.AreNotSame(window1, window2, "None 模式重开应创建新实例，不复活旧实例。");
            Assert.AreEqual(2, LifecycleTestWindow.OnCreateCount,
                "两个不同实例应各执行一次 OnCreate（旧域不复活）。");
            Assert.AreEqual(2, LifecycleTestWindow.OnOpenCount,
                "两轮打开应各执行一次 OnOpen（新 Open 域建立）。");
            Assert.AreEqual(2, LifecycleTestWindow.OnRefreshCount,
                "两个有效请求应各执行一次 OnRefresh（FIFO 顺序保留）。");
            Assert.AreEqual(1, LifecycleTestWindow.OnCloseCount,
                "仅首轮 Close 执行一次 OnClose。");

            // 断言：FIFO 顺序——重开请求的 args 生效（最终 UserData 为 reopenArg）。
            Assert.AreEqual(reopenArg, window2.UserData,
                "重开请求的 args 应在 OnRefresh 中生效（FIFO 顺序保留）。");

            // 断言：旧 OpenCts 不复活——新实例有新的 OpenCancellationToken。
            Assert.AreNotEqual(oldOpenToken, window2.OpenCancellationToken,
                "重开应创建新 OpenCts，不复用旧 Open 域的取消令牌。");

            // 断言：租约无泄漏——重开后引用计数恢复为 1（新租约，旧 lease 已释放）。
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "重开后 UIBattle 引用计数应恢复为 1（新租约）。");
            Assert.AreEqual(1, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "重开后 UICommon 引用计数应恢复为 1（新租约）。");

            // 清除 OnClose 钩子，避免最终 Close 再次触发重入 Show。
            LifecycleTestWindow.OnCloseHook = null;

            // 执行：最终 Close，验证无泄漏。
            module.Close<LifecycleTestWindow>();
            Assert.AreEqual(0, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "最终 Close 后 UIBattle 引用计数应归零（无租约泄漏）。");
            Assert.AreEqual(0, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "最终 Close 后 UICommon 引用计数应归零（无租约泄漏）。");
        }

        /// <summary>
        /// 真实 Closing 期间 Show 交叠（Cache 模式）：OnClose 回调内通过公开 API ShowAsync 重入，
        /// 此时 entry 处于 Closing。ShowAsync 不抛异常，等待 Close 完成后从 Cached 同步重开。
        /// 验证：await 返回窗口实例、FIFO 顺序（重开请求 args 生效）、旧 Open 域不复活（OnCreate 不重复、新 OpenCts）、
        /// 租约无泄漏（Cached 保留计数、最终释放归零）。
        /// </summary>
        /// <remarks>
        /// spec "Closing 期间再次 Show" + "缓存窗口重新打开"——SHALL 不重复执行 OnCreate，
        /// 但 SHALL 重新建立打开取消域和事件域，并再次执行 OnOpen 与 OnRefresh。
        ///
        /// 真实交叠构造（任务 8.3 硬要求③）：测试窗口的 OnClose 回调内调用 module.ShowAsync<T>()（公开 API）。
        /// CloseEntryCore 在 InvokeOnClose 返回后检测 pending reopen，同步从 Cached 重开（ExecuteOpenLifecycle）。
        /// </remarks>
        [Test]
        [Description("真实 Closing 期间 Show 交叠（Cache）：OnClose 内 ShowAsync 等待同步重开，不抛异常，无泄漏。")]
        public async UniTask ClosingShowOverlap_Cache_WaitsAndReopensFromCached()
        {
            // 安排：装配 Cache 模式窗口。
            FUIModule module = SetupModuleWithLifecycleWindow(cacheMode: FUICacheMode.Cache);

            // 首次 Show。
            LifecycleTestWindow window1 = await module.ShowAsync<LifecycleTestWindow>("initial");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "首次 Show 后应处于 Open。");
            CancellationToken oldOpenToken = window1.OpenCancellationToken;
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "首次 Show 后 UIBattle 引用计数应为 1。");

            // 记录重入 Show 的待决任务与参数。
            UniTask<LifecycleTestWindow> reopenTask = default;
            const string reopenArg = "reopenArg";

            // 设置 OnClose 钩子：在 OnClose 内通过公开 API 构造重入 Show（entry 处于 Closing）。
            LifecycleTestWindow.OnCloseHook = _ =>
            {
                // 此时 CloseEntryCore 正在执行栈上，entry.State == Closing。
                // ShowAsyncCore 重入命中 Closing 分支，注册 pending reopen，返回挂起 UniTask。
                reopenTask = FUI.ShowAsync<LifecycleTestWindow>(reopenArg);
            };

            // 执行：Close 触发 OnClose -> 重入 ShowAsync（交叠发生在 Closing 期间）。
            module.Close<LifecycleTestWindow>();

            // Cache 模式的 pending reopen 由 CloseEntryCore 同步处理（ExecuteOpenLifecycle）。
            // tcs 已 resolve，await reopenTask 在 UniTask 调度上恢复。
            LifecycleTestWindow window2 = await reopenTask;

            // 断言：ShowAsync 成功完成（不抛异常），返回窗口实例。
            Assert.IsNotNull(window2, "Closing 期间 Show 的 await 应返回重开后的窗口实例，不抛异常。");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTestWindow)),
                "重开后应处于 Open。");

            // 断言：旧 Open 域不复活——Cache 模式复用同一实例（OnCreate 不重复），但新 OpenCts/事件域。
            Assert.AreSame(window1, window2, "Cache 模式重开应复用同一实例，不创建新实例。");
            Assert.AreEqual(1, LifecycleTestWindow.OnCreateCount,
                "Cache 模式重开不应重复 OnCreate（旧实例生命周期不复活）。");
            Assert.AreEqual(2, LifecycleTestWindow.OnOpenCount,
                "两轮打开应各执行一次 OnOpen（新 Open 域建立）。");
            Assert.AreEqual(2, LifecycleTestWindow.OnRefreshCount,
                "两个有效请求应各执行一次 OnRefresh（FIFO 顺序保留）。");
            Assert.AreEqual(1, LifecycleTestWindow.OnCloseCount,
                "仅首轮 Close 执行一次 OnClose。");

            // 断言：FIFO 顺序——重开请求的 args 生效（最终 UserData 为 reopenArg）。
            Assert.AreEqual(reopenArg, window2.UserData,
                "重开请求的 args 应在 OnRefresh 中生效（FIFO 顺序保留）。");

            // 断言：旧 OpenCts 不复活——重开创建新 OpenCancellationToken。
            Assert.AreNotEqual(oldOpenToken, window2.OpenCancellationToken,
                "重开应创建新 OpenCts，不复用旧 Open 域的取消令牌。");

            // 断言：租约无泄漏——Cached 保留计数（重开不重新获取，计数仍为 1）。
            Assert.AreEqual(1, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "重开后 UIBattle 引用计数应保持 1（Cached 重开不重新获取租约）。");

            // 清除 OnClose 钩子，避免最终 Close 再次触发重入 Show。
            LifecycleTestWindow.OnCloseHook = null;

            // 执行：最终 Close（Cached -> Disposed，释放实例与租约）。
            module.Close<LifecycleTestWindow>();
            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(LifecycleTestWindow)),
                "最终 Close 后应处于 Disposed。");
            Assert.AreEqual(0, PackageLoader.FindRecord(UIBattlePkg).ReferenceCount,
                "最终释放后 UIBattle 引用计数应为 0（无租约泄漏）。");
            Assert.AreEqual(0, PackageLoader.FindRecord(UICommonPkg).ReferenceCount,
                "最终释放后 UICommon 引用计数应为 0（无租约泄漏）。");
        }

        // ============================================================
        // 场景 f：失败重试——加载失败后可重新 Show
        // ============================================================

        /// <summary>
        /// 失败重试：一次加载失败并完成回滚后再次 Show，系统 SHALL 发起新的有效打开操作，
        /// 且不得复用失败操作的实例、事件或资源租约。
        /// </summary>
        /// <remarks>
        /// spec "窗口状态转换受控" / "加载失败后重试"——一次加载失败并完成回滚后再次 Show，
        /// 系统 SHALL 发起新的有效打开操作，且不得复用失败操作的实例、事件或资源租约。
        /// design.md 决策4：失败或取消 SHALL 回滚到可重试的 Absent 状态。
        /// </remarks>
        [Test]
        [Description("失败重试：加载失败后可重新 Show，新操作不复用失败操作的实例。")]
        public async UniTask FailureRetry_CanShowAgainAfterLoadFailure()
        {
            // 安排：装配模块，预设描述加载失败。
            InMemoryFUIResourceProvider provider = CreateProviderWithLifecyclePackages();
            provider.MarkLoadFailure(UIBattlePkg + "_fui");
            FUI.RegisterModuleForTesting(provider);
            FUIModule module = (FUIModule)FUI.Module;
            UIBattleBinder.BindAll();
            RegisterCustomWidget(
                module,
                typeof(TestBattleStartWidget),
                UI_BattleStartWidget.URL,
                UI_BattleStartWidget.PkgName,
                UI_BattleStartWidget.ResName);
            RegisterLifecycleWindow(module);
            module.FreezeBindings();

            // 执行：首次 Show 应失败（描述加载失败）。
            bool firstFailed = false;
            try
            {
                await module.ShowAsync<LifecycleTestWindow>();
            }
            catch (FUIException)
            {
                firstFailed = true;
            }

            Assert.IsTrue(firstFailed, "首次 Show 应因描述加载失败而抛 FUIException。");

            // 断言：失败后 entry 回滚到 Absent（可重试）。
            Assert.AreEqual(FUIWindowState.Absent, GetEntryState(module, typeof(LifecycleTestWindow)),
                "加载失败后应回滚到 Absent（可重试）。");

            // 执行：解除失败标记，再次 Show 应成功。
            // 重新装配 provider（原 provider 已标记失败，需重新创建干净 provider 并重新注册模块）。
            module.Shutdown();
            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();
            FUI.ClearModuleForShutdown();

            InMemoryFUIResourceProvider cleanProvider = CreateProviderWithLifecyclePackages();
            FUI.RegisterModuleForTesting(cleanProvider);
            FUIModule cleanModule = (FUIModule)FUI.Module;
            UIBattleBinder.BindAll();
            RegisterCustomWidget(
                cleanModule,
                typeof(TestBattleStartWidget),
                UI_BattleStartWidget.URL,
                UI_BattleStartWidget.PkgName,
                UI_BattleStartWidget.ResName);
            RegisterLifecycleWindow(cleanModule);
            cleanModule.FreezeBindings();

            LifecycleTestWindow window = await cleanModule.ShowAsync<LifecycleTestWindow>();

            // 断言：重试成功，新实例创建。
            Assert.IsNotNull(window, "失败重试后应成功创建窗口。");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(cleanModule, typeof(LifecycleTestWindow)),
                "重试成功后应处于 Open。");
            Assert.AreEqual(1, LifecycleTestWindow.OnCreateCount, "重试成功后新实例应执行一次 OnCreate。");
        }

        // ============================================================
        // 场景 g：层级——窗口挂载到正确层级
        // ============================================================

        /// <summary>
        /// 层级：窗口按描述的 Layer 挂载到对应层级容器的子容器中。
        /// </summary>
        /// <remarks>
        /// spec "独立层级和全屏遮挡"——系统 SHALL 使用 GameFUI 自有的固定层级集合，
        /// 每层对应稳定容器，窗口只在所属容器内调整 child index。
        /// design.md 决策7：每层在 GRoot 下有稳定容器，窗口只在所属容器内调整 child index。
        /// </remarks>
        [Test]
        [Description("层级：窗口挂载到正确层级的子容器中。")]
        public async UniTask Layer_WindowMountedToCorrectLayerSubContainer()
        {
            // 安排：装配模块，注册 Normal 层窗口。
            FUIModule module = SetupModuleWithLifecycleWindow();

            // 执行：Show 窗口。
            LifecycleTestWindow window = await module.ShowAsync<LifecycleTestWindow>();

            // 断言：窗口的 parent 应为 Normal 层的 Full 子容器（描述 SafeAreaMode=Full）。
            GComponent expectedContainer = module.LayerContainer.GetSubContainer(FUILayer.Normal, FUISafeAreaMode.Full);
            Assert.AreSame(expectedContainer, window.parent,
                "窗口应挂载到 Normal 层 Full 子容器。");

            // 执行：Close 后再验证窗口已从显示树移除。
            module.Close<LifecycleTestWindow>();
            Assert.IsNull(window.parent, "Close 后窗口应从显示树移除。");
        }

        // ============================================================
        // 场景 h：安全区——Safe 子容器对齐 Screen.safeArea
        // ============================================================

        /// <summary>
        /// 安全区：Safe 子容器对齐 <c>Screen.safeArea</c>，Full 子容器铺满层级容器。
        /// </summary>
        /// <remarks>
        /// spec "独立层级和全屏遮挡" / design.md 决策7：每层可包含 Full 和 Safe 两个子容器；
        /// 需要安全区的窗口进入 Safe 容器，其尺寸和坐标在分辨率、方向或 safeArea 变化时重算。
        /// GRoot 本身不缩成安全区。
        /// </remarks>
        [Test]
        [Description("安全区：Safe 子容器对齐 Screen.safeArea，Full 子容器铺满。")]
        public void SafeArea_SafeSubContainerAlignedToScreenSafeArea()
        {
            // 安排：装配模块（只需 FreezeBindings 创建层容器，不需要 Show）。
            FUIModule module = SetupModuleWithLifecycleWindow();

            // 获取 Normal 层的 Full 与 Safe 子容器。
            GComponent fullContainer = module.LayerContainer.GetSubContainer(FUILayer.Normal, FUISafeAreaMode.Full);
            GComponent safeContainer = module.LayerContainer.GetSubContainer(FUILayer.Normal, FUISafeAreaMode.Safe);

            Assert.IsNotNull(fullContainer, "Full 子容器应存在。");
            Assert.IsNotNull(safeContainer, "Safe 子容器应存在。");

            // 断言：Full 子容器铺满 GRoot（与层级容器同尺寸）。
            GRoot root = GRoot.inst;
            Assert.AreEqual(root.width, fullContainer.width, 1f, "Full 子容器宽度应铺满 GRoot。");
            Assert.AreEqual(root.height, fullContainer.height, 1f, "Full 子容器高度应铺满 GRoot。");

            // 断言：Safe 子容器对齐 Screen.safeArea。
            // 在 Editor 默认环境下 Screen.safeArea 通常等于屏幕全尺寸，
            // 因此 Safe 子容器尺寸应接近 GRoot 尺寸（允许浮点误差）。
            float scaleFactor = GRoot.contentScaleFactor;
            if (scaleFactor <= 0f)
            {
                scaleFactor = 1f;
            }

            Rect safeArea = Screen.safeArea;
            float expectedSafeWidth = safeArea.width / scaleFactor;
            float expectedSafeHeight = safeArea.height / scaleFactor;

            Assert.AreEqual(expectedSafeWidth, safeContainer.width, 2f,
                "Safe 子容器宽度应对齐 Screen.safeArea 宽度。");
            Assert.AreEqual(expectedSafeHeight, safeContainer.height, 2f,
                "Safe 子容器高度应对齐 Screen.safeArea 高度。");
        }

        // ============================================================
        // 场景 i：全屏遮挡——全屏窗口遮挡下层
        // ============================================================

        /// <summary>
        /// 全屏遮挡：全屏窗口在更高层级打开后，下层层级容器的 visible/touchable 被设为 false，
        /// 但下层窗口保留 Stage 归属（不移出 Stage）。关闭全屏窗口后下层恢复可见。
        /// </summary>
        /// <remarks>
        /// spec "独立层级和全屏遮挡"——全屏窗口 SHALL 按窗口栈把被遮挡下层窗口的 visible 和 touchable
        /// 同时设为 false，但 SHALL 保留这些窗口的 Stage 归属；关闭后 SHALL 恢复仍有效的下层窗口。
        /// design.md 决策7：全屏遮挡通过窗口栈从顶向下把被遮挡下层窗口的 visible 和 touchable 同时设为 false，
        /// 但不移出 Stage。
        ///
        /// 实现说明：由于两个窗口类型不能共享同一 URL（注册唯一性），本测试注册一个 Popup 层全屏窗口
        /// （<see cref="FullScreenTestWindow"/>），覆盖 BattleStartPanel URL。
        /// 先 Show 一个 Normal 层普通窗口（<see cref="LifecycleTestWindow"/>），Close 它，
        /// 然后改为注册 FullScreenTestWindow——但 Freeze 后不能注册。
        /// 因此本测试采用单窗口方案：注册一个 Popup 层全屏窗口，打开后验证 Normal 层级容器被遮挡。
        /// 下层"窗口保留 Stage 归属"通过验证 Normal 层级容器未从 GRoot 移除来覆盖
        /// （<see cref="FUILayerContainer.ApplyFullScreenOcclusion"/> 只切换 visible/touchable，不移出 GRoot）。
        /// </remarks>
        [Test]
        [Description("全屏遮挡：全屏窗口遮挡下层（visible/touchable false），关闭后恢复。")]
        public async UniTask FullScreenOcclusion_OccludesLowerLayer_RestoresOnClose()
        {
            // 安排：装配模块，注册 Popup 层全屏窗口。
            FUIModule module = SetupModuleWithFullScreenWindow();

            // 获取 Normal 层级容器，验证初始可见。
            GComponent normalLayer = module.LayerContainer.GetLayer(FUILayer.Normal);
            Assert.IsTrue(normalLayer.visible, "Normal 层初始应可见。");
            Assert.IsTrue(normalLayer.touchable, "Normal 层初始应可交互。");

            // 断言：Normal 层级容器在 GRoot 中（保留 Stage 归属）。
            Assert.IsNotNull(normalLayer.parent, "Normal 层级容器应在 GRoot 中。");

            // 执行：打开 Popup 层全屏窗口。
            FullScreenTestWindow highWindow = await module.ShowAsync<FullScreenTestWindow>();

            // 断言：Normal 层被遮挡（visible=false, touchable=false）。
            Assert.IsFalse(normalLayer.visible, "全屏窗口在上层打开后 Normal 层应被遮挡（visible=false）。");
            Assert.IsFalse(normalLayer.touchable, "全屏窗口在上层打开后 Normal 层应被遮挡（touchable=false）。");

            // 断言：Normal 层级容器仍保留 Stage 归属（未从 GRoot 移除）。
            Assert.IsNotNull(normalLayer.parent, "被遮挡的 Normal 层级容器应保留 Stage 归属（不移出 GRoot）。");

            // 执行：关闭全屏窗口。
            module.Close<FullScreenTestWindow>();

            // 断言：Normal 层恢复可见。
            Assert.IsTrue(normalLayer.visible, "关闭全屏窗口后 Normal 层应恢复可见。");
            Assert.IsTrue(normalLayer.touchable, "关闭全屏窗口后 Normal 层应恢复可交互。");
        }

        // ============================================================
        // 辅助方法与测试类型
        // ============================================================

        /// <summary>
        /// 装配模块并注册 <see cref="LifecycleTestWindow"/>，使用延迟可配的内存 provider。
        /// </summary>
        /// <param name="loadDelayMs">加载延迟毫秒数，用于并发/取消/加载期间 Close 测试。</param>
        /// <returns>已完成 FreezeBindings 的 <see cref="FUIModule"/>。</returns>
        /// <remarks>
        /// 本方法不调用 <see cref="TestFUIOwner.RegisterUIBattle"/>，因为后者注册的 Widget/Window
        /// 描述的 Creator 为 null，而全局无状态 creator 在被调用时需要 descriptor.Creator 非空。
        /// 本方法直接调用 <see cref="UIBattleBinder.BindAll"/> 注册生成类型到全局工厂，
        /// 然后注册 <see cref="LifecycleTestWindow"/> 覆盖 Window URL，并注册
        /// <see cref="TestBattleStartWidget"/> 覆盖 Widget URL，两者均提供非 null Creator。
        /// </remarks>
        private static FUIModule SetupModuleWithLifecycleWindow(
            int loadDelayMs = 0,
            FUICacheMode cacheMode = FUICacheMode.None)
        {
            InMemoryFUIResourceProvider provider = CreateProviderWithLifecyclePackages(loadDelayMs);
            FUI.RegisterModuleForTesting(provider);
            FUIModule module = (FUIModule)FUI.Module;

            // 1. 本包生成 Binder：注册生成类型到全局 UIObjectFactory。
            UIBattleBinder.BindAll();

            // 2. 注册最终测试 Widget（覆盖生成类型，Creator 非空）。
            RegisterCustomWidget(
                module,
                typeof(TestBattleStartWidget),
                UI_BattleStartWidget.URL,
                UI_BattleStartWidget.PkgName,
                UI_BattleStartWidget.ResName);

            // 3. 注册最终测试 Window（覆盖生成类型，Creator 非空）。
            RegisterLifecycleWindow(module, cacheMode);

            module.FreezeBindings();
            return module;
        }

        /// <summary>
        /// 装配模块并注册一个 Popup 层全屏测试窗口（<see cref="FullScreenTestWindow"/>），
        /// 用于全屏遮挡测试。
        /// </summary>
        /// <returns>已完成 FreezeBindings 的 <see cref="FUIModule"/>。</returns>
        private static FUIModule SetupModuleWithFullScreenWindow()
        {
            InMemoryFUIResourceProvider provider = CreateProviderWithLifecyclePackages();
            FUI.RegisterModuleForTesting(provider);
            FUIModule module = (FUIModule)FUI.Module;

            // 1. 本包生成 Binder。
            UIBattleBinder.BindAll();

            // 2. 注册最终测试 Widget。
            RegisterCustomWidget(
                module,
                typeof(TestBattleStartWidget),
                UI_BattleStartWidget.URL,
                UI_BattleStartWidget.PkgName,
                UI_BattleStartWidget.ResName);

            // 3. 注册全屏窗口为 Popup 层，覆盖 BattleStartPanel URL 的最终业务类型。
            RegisterCustomWindow(
                module,
                typeof(FullScreenTestWindow),
                UI_BattleStartPanel.URL,
                UI_BattleStartPanel.PkgName,
                UI_BattleStartPanel.ResName,
                FUILayer.Popup,
                fullScreen: true,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full);

            module.FreezeBindings();
            return module;
        }

        /// <summary>
        /// 注册 <see cref="LifecycleTestWindow"/> 到模块的绑定注册表，
        /// 覆盖 <see cref="UI_BattleStartPanel"/> 的生成类型。
        /// </summary>
        /// <param name="module">已注册但未冻结的模块。</param>
        private static void RegisterLifecycleWindow(
            FUIModule module,
            FUICacheMode cacheMode = FUICacheMode.None)
        {
            RegisterCustomWindow(
                module,
                typeof(LifecycleTestWindow),
                UI_BattleStartPanel.URL,
                UI_BattleStartPanel.PkgName,
                UI_BattleStartPanel.ResName,
                FUILayer.Normal,
                fullScreen: false,
                cacheMode: cacheMode,
                safeAreaMode: FUISafeAreaMode.Full);
        }

        /// <summary>
        /// 注册自定义窗口描述到模块的绑定注册表。
        /// </summary>
        /// <param name="module">已注册但未冻结的模块。</param>
        /// <param name="targetType">最终业务窗口类型。</param>
        /// <param name="url">组件 URL。</param>
        /// <param name="packageName">包名。</param>
        /// <param name="componentName">组件名。</param>
        /// <param name="layer">层级。</param>
        /// <param name="fullScreen">是否全屏。</param>
        /// <param name="cacheMode">缓存策略。</param>
        /// <param name="safeAreaMode">安全区策略。</param>
        /// <remarks>
        /// creator 使用 <see cref="Activator.CreateInstance"/> 构造最终业务类型，
        /// 与 <see cref="UIObjectFactory.SetPackageItemExtension(string, Type)"/> 的语义一致。
        /// 全局无状态 creator（<see cref="FUIObjectFactoryIntegration"/>)在被调用时查询 Registry 描述，
        /// 再通过 <see cref="FUIDescriptor.Creator"/> 创建实例。
        /// </remarks>
        private static void RegisterCustomWindow(
            FUIModule module,
            Type targetType,
            string url,
            string packageName,
            string componentName,
            FUILayer layer,
            bool fullScreen,
            FUICacheMode cacheMode,
            FUISafeAreaMode safeAreaMode)
        {
            module.BindingRegistry.Register(new FUIDescriptor(
                url: url,
                packageName: packageName,
                componentName: componentName,
                ownerType: TestFUIOwner.OwnerType,
                targetType: targetType,
                layer: layer,
                fullScreen: fullScreen,
                cacheMode: cacheMode,
                safeAreaMode: safeAreaMode,
                creator: CreateTypeBasedCreator(targetType)));
        }

        /// <summary>
        /// 注册自定义 Widget 描述到模块的绑定注册表。
        /// </summary>
        /// <param name="module">已注册但未冻结的模块。</param>
        /// <param name="targetType">最终业务 Widget 类型。</param>
        /// <param name="url">组件 URL。</param>
        /// <param name="packageName">包名。</param>
        /// <param name="componentName">组件名。</param>
        /// <remarks>
        /// Widget 通常使用 Normal 层、非全屏、不缓存、Full 安全区。
        /// creator 使用 <see cref="CreateTypeBasedCreator"/> 构造最终业务类型。
        /// </remarks>
        private static void RegisterCustomWidget(
            FUIModule module,
            Type targetType,
            string url,
            string packageName,
            string componentName)
        {
            module.BindingRegistry.Register(new FUIDescriptor(
                url: url,
                packageName: packageName,
                componentName: componentName,
                ownerType: TestFUIOwner.OwnerType,
                targetType: targetType,
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: CreateTypeBasedCreator(targetType)));
        }

        /// <summary>
        /// 创建基于 <see cref="Activator.CreateInstance"/> 的无状态 creator 委托。
        /// </summary>
        /// <param name="targetType">最终业务类型，必须有无参构造函数。</param>
        /// <returns>creator 委托，接收 URL 并创建实例。</returns>
        /// <remarks>
        /// 与 <see cref="UIObjectFactory.SetPackageItemExtension(string, Type)"/> 内部实现一致，
        /// 使用 <see cref="Activator.CreateInstance"/> 构造目标类型实例。
        /// creator 只捕获 targetType（Type 不可释放），不捕获 Registry 或其他可释放对象，
        /// 符合 design.md 决策2 的无状态 creator 约束。
        /// </remarks>
        private static Func<string, GComponent> CreateTypeBasedCreator(Type targetType)
        {
            return url => (GComponent)Activator.CreateInstance(targetType);
        }

        /// <summary>
        /// 创建已预设 UIBattle（含 Atlas）与 UICommon（含 Atlas）描述资源的内存 provider。
        /// </summary>
        /// <param name="loadDelayMs">加载延迟毫秒数，0 表示立即完成。</param>
        /// <returns>已预设描述资源的内存 provider。</returns>
        /// <remarks>
        /// 描述资源使用磁盘上真实发布的 <c>UIBattle_fui.bytes</c> 与 <c>UICommon_fui.bytes</c>，
        /// 因为 <see cref="FairyGuiDescBuilder"/> 构造的最小二进制只含 Atlas 项、不含组件项，
        /// 无法通过 <c>UIPackage.CreateObjectFromURL</c> 创建真实窗口实例。
        ///
        /// 关键约束：真实描述字节包含非 ASCII 字节（>= 0x80），不能通过
        /// <c>new TextAsset(Encoding.UTF8.GetString(bytes))</c> 构造，否则 UTF8 往返会破坏二进制结构。
        /// 本方法使用 <see cref="AssetDatabase.LoadAssetAtPath"/> 从磁盘直接加载 <see cref="TextAsset"/>，
        /// 确保 <c>TextAsset.bytes</c> 返回原始二进制字节。
        ///
        /// 外部资源（Atlas）使用 <see cref="Texture2D"/> 模拟（2x2 像素足以通过预载）。
        /// 包依赖：UIBattle 依赖 UICommon（共享图集），与真实项目结构一致。
        /// </remarks>
        private static InMemoryFUIResourceProvider CreateProviderWithLifecyclePackages(int loadDelayMs = 0)
        {
            InMemoryFUIResourceProvider provider = new InMemoryFUIResourceProvider(loadDelayMs);

            // 从 AssetDatabase 加载真实 TextAsset（保留二进制字节完整性）。
            TextAsset uiCommonDesc = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/AssetRaw/FUI/" + UICommonPkg + "_fui.bytes");
            TextAsset uiBattleDesc = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/AssetRaw/FUI/" + UIBattlePkg + "_fui.bytes");

            Assert.IsNotNull(uiCommonDesc, "应能从磁盘加载 UICommon_fui.bytes 描述资源。");
            Assert.IsNotNull(uiBattleDesc, "应能从磁盘加载 UIBattle_fui.bytes 描述资源。");

            // UICommon 包（无依赖，含 Atlas）。
            provider.SetAsset(UICommonPkg + "_fui", uiCommonDesc);
            provider.SetAsset(UICommonPkg + "_atlas0", CreateTexture2D(2, 2));

            // UIBattle 包（依赖 UICommon，无自己的 Atlas——使用 UICommon 的共享图集）。
            provider.SetAsset(UIBattlePkg + "_fui", uiBattleDesc);

            return provider;
        }

        /// <summary>
        /// 获取指定窗口类型的当前 <see cref="FUIWindowState"/>。
        /// </summary>
        /// <param name="module">模块实例。</param>
        /// <param name="windowType">窗口类型。</param>
        /// <returns>当前状态；找不到条目时返回 <see cref="FUIWindowState.Absent"/>。</returns>
        private static FUIWindowState GetEntryState(FUIModule module, Type windowType)
        {
            if (module._windowEntries.TryGetValue(windowType, out WindowEntry entry))
            {
                return entry.State;
            }

            return FUIWindowState.Absent;
        }

        /// <summary>
        /// 创建指定尺寸的 <see cref="Texture2D"/>（复用 PackageLoadingTests 的实现）。
        /// </summary>
        private static Texture2D CreateTexture2D(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        // ============================================================
        // 测试用窗口类型
        // ============================================================

        /// <summary>
        /// 生命周期计数测试窗口，继承生成类型 <see cref="UI_BattleStartPanel"/>。
        /// 通过静态计数器记录各生命周期回调执行次数，供测试断言。
        /// </summary>
        /// <remarks>
        /// design.md 决策5：OnCreate/OnDispose 各执行一次，OnOpen/OnRefresh/OnClose 按每轮打开执行。
        /// 本类型在 protected virtual 回调中递增静态计数器，不引入可变实例字段以保证跨实例累计统计。
        /// </remarks>
        public class LifecycleTestWindow : UI_BattleStartPanel
        {
            /// <summary>OnCreate 累计执行次数。</summary>
            public static int OnCreateCount;
            /// <summary>OnOpen 累计执行次数。</summary>
            public static int OnOpenCount;
            /// <summary>OnRefresh 累计执行次数。</summary>
            public static int OnRefreshCount;
            /// <summary>OnClose 累计执行次数。</summary>
            public static int OnCloseCount;
            /// <summary>OnHide 累计执行次数。</summary>
            public static int OnHideCount;
            /// <summary>OnDispose 累计执行次数。</summary>
            public static int OnDisposeCount;

            /// <summary>
            /// OnClose 回调钩子，供测试在 OnClose 内通过公开 API 构造重入 Show 交叠（任务 8.3 真实交叠测试）。
            /// 设置后，每次 OnClose 会调用此钩子；测试结束后应置 null 以免影响其他测试。
            /// 钩子内可调用 FUI.ShowAsync 触发 Closing 期间 Show 的重入场景。
            /// </summary>
            public static Action<LifecycleTestWindow> OnCloseHook;

            /// <summary>
            /// 重置所有静态计数器为 0，在每个测试的 SetUp 中调用。
            /// </summary>
            public static void ResetCounters()
            {
                OnCreateCount = 0;
                OnOpenCount = 0;
                OnRefreshCount = 0;
                OnCloseCount = 0;
                OnHideCount = 0;
                OnDisposeCount = 0;
                OnCloseHook = null;
            }

            /// <inheritdoc/>
            protected override void OnCreate()
            {
                base.OnCreate();
                OnCreateCount++;
            }

            /// <inheritdoc/>
            protected override void OnOpen()
            {
                base.OnOpen();
                OnOpenCount++;
            }

            /// <inheritdoc/>
            protected override void OnRefresh()
            {
                base.OnRefresh();
                OnRefreshCount++;
            }

            /// <inheritdoc/>
            protected override void OnHide()
            {
                base.OnHide();
                OnHideCount++;
            }

            /// <inheritdoc/>
            protected override void OnClose()
            {
                base.OnClose();
                OnCloseCount++;
                // 任务 8.3：在 OnClose 内通过公开 API 构造重入 Show 交叠。
                // 此时 CloseEntryCore 正在执行栈上、entry 处于 Closing，ShowAsyncCore 重入读到 Closing 状态。
                OnCloseHook?.Invoke(this);
            }

            /// <inheritdoc/>
            protected override void OnDispose()
            {
                base.OnDispose();
                OnDisposeCount++;
            }
        }

        /// <summary>
        /// 全屏遮挡测试窗口，继承生成类型 <see cref="UI_BattleStartPanel"/>。
        /// 用于全屏遮挡场景，注册为更高层级（如 Popup）的全屏窗口。
        /// </summary>
        public class FullScreenTestWindow : UI_BattleStartPanel
        {
        }
    }
}
