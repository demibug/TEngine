using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using GameFUI.Tests.EditMode;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UIBattle;

namespace GameFUI.Tests.PlayMode
{
    /// <summary>
    /// Editor PlayMode 生命周期验收测试，覆盖任务 7.3 的全部场景：
    /// 首次/重复 Show 参数、调用取消、加载期间 Close、Hide、默认 Close、显式 Cache、Dispose、
    /// 重复模块注册保护和退出 Shutdown。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - design.md 决策4/5/7/10；
    /// - spec fairygui-window-runtime 全部 Requirement（生命周期次数确定、Show 公开接口和完成边界稳定、
    ///   窗口状态转换受控、默认关闭即释放且缓存显式启用、模块退出完整清理）。
    ///
    /// 测试装配：使用 <see cref="PlayModeTestHarness"/> 提供的公开方法完成内存 provider 构造、
    /// UIBattle Binder 调用、Registry 冻结与全局清理。对需要自定义 CacheMode 或自定义窗口类型的场景，
    /// 本测试文件内提供 <see cref="SetupWithCustomWindow"/> 自行装配，复用 harness 的公开构建块，
    /// 不修改 harness（任务约束：禁止修改 PlayModeTestHarness.cs）。
    ///
    /// 复用证据：
    /// - 复用 <see cref="PlayModeTestHarness.SetupForShowAsync"/>（7.1 产出，带非空 creator 的 ShowAsync 就绪装配）；
    /// - 复用 <see cref="PlayModeTestHarness.CreateProviderWithUIBattleAndUICommon"/>（7.1 产出，内存 provider 构造）；
    /// - 复用 <see cref="PlayModeTestHarness.CreateTypeBasedCreator"/>（7.1 产出，无状态 creator）；
    /// - 复用 <see cref="PlayModeTestHarness.Cleanup"/>（7.1 产出，全局清理）；
    /// - 复用 <see cref="PlayModeTestHarness.EnsureGRootInitialized"/>（7.1 产出，GRoot 初始化）；
    /// - 复用 <see cref="TestBattleStartPanel"/> / <see cref="TestBattleStartWidget"/>（3.6 产出，最终测试业务类型）；
    /// - 复用 <see cref="TestFUIOwner.OwnerType"/>（3.6 产出，owner 类型标识）；
    /// - 复用 <see cref="FUI.RegisterModuleForTesting"/>（5.1 产出的 internal 测试入口）；
    /// - 复用 <see cref="FUIModule.BindingRegistry"/> / <see cref="FUIModule._windowEntries"/> 等 internal 访问器。
    /// 装配模式与 EditMode <c>WindowLifecycleTests.SetupModuleWithLifecycleWindow</c> 一致，
    /// 保证 PlayMode 与 EditMode 使用完全相同的注册契约（design.md Risks：测试 owner 与未来真实业务 Module
    /// 存在装配差异 → 测试 owner 使用与未来业务 owner 完全相同的 Binder、Descriptor、Freeze 和 Show API）。
    ///
    /// 资源隔离：每个测试在 <see cref="SetUp"/> 中清空全局状态并初始化 GRoot；
    /// <see cref="TearDown"/> 执行 harness 清理，避免跨测试残留。
    ///
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，只通过公开/internal API 访问被测对象。
    /// 本测试不依赖 GameLogic/GamePlay/GameBattle，不创建或修改 BattleModule。
    /// </remarks>
    [TestFixture]
    public class PlayModeLifecycleTests
    {
        // ============================================================
        // SetUp / TearDown
        // ============================================================

        /// <summary>
        /// 每个测试前的状态基线重置：清空全局状态并初始化 GRoot。
        /// </summary>
        /// <remarks>
        /// <see cref="PlayModeTestHarness.Cleanup"/> 幂等清空 FairyGUI 全局包注册表、PackageLoader 静态注册表、
        /// FUI 门面静态缓存与活动 Registry 静态引用，避免跨测试残留。
        /// <see cref="PlayModeTestHarness.EnsureGRootInitialized"/> 确保 GRoot 在 EditMode/Editor PlayMode 下已就绪。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            PlayModeTestHarness.Cleanup();
            PlayModeTestHarness.EnsureGRootInitialized();
            LifecycleTrackingWindow.ResetCounters();
            CachedTrackingWindow.ResetCounters();
        }

        /// <summary>
        /// 每个测试后的状态基线重置，与 <see cref="SetUp"/> 对称，确保即使测试中途失败
        /// 也不会残留全局状态污染后续测试。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            PlayModeTestHarness.Cleanup();
        }

        // ============================================================
        // 场景 a：首次 Show 参数传递
        // ============================================================

        /// <summary>
        /// 首次 Show 参数传递：调用 <c>ShowAsync</c> 传入用户参数后，窗口的 <c>UserData</c> 应为首个参数，
        /// <c>OnCreate</c> 执行一次，<c>OnOpen</c> 执行一次，<c>OnRefresh</c> 执行一次且能读到参数。
        /// </summary>
        /// <remarks>
        /// spec "Show 公开接口和完成边界稳定"——无取消令牌打开窗口，系统 SHALL 使用模块 lifetime token 驱动框架加载，
        /// 并在成功后返回非空 Open 窗口。
        /// spec "窗口生命周期次数确定"——每个刷新请求执行前 SHALL 更新窗口的 UserDatas 和 UserData。
        /// design.md 决策5：首次创建 OnCreate 一次；每轮打开 OnOpen -> OnRefresh。
        /// </remarks>
        [Test]
        [Description("首次 Show 参数传递：UserData 正确设置，OnCreate/OnOpen/OnRefresh 各执行一次。")]
        public async UniTask FirstShow_PassesArgs_UpdatesUserData()
        {
            // 安排：装配模块并注册生命周期追踪窗口。
            FUIModule module = SetupWithCustomWindow(typeof(LifecycleTrackingWindow), FUICacheMode.None);

            // 执行：首次 Show，传入用户参数。
            LifecycleTrackingWindow window = await module.ShowAsync<LifecycleTrackingWindow>("first-arg", 42);

            // 断言：返回非空 Open 窗口。
            Assert.IsNotNull(window, "首次 Show 应返回非空窗口。");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTrackingWindow)),
                "首次 Show 后应处于 Open。");

            // 断言：UserData 为首个参数。
            Assert.AreEqual("first-arg", window.UserData, "首次 Show 后 UserData 应为传入的首个参数。");

            // 断言：生命周期计数。
            Assert.AreEqual(1, LifecycleTrackingWindow.OnCreateCount, "首次 Show 后 OnCreate 应执行一次。");
            Assert.AreEqual(1, LifecycleTrackingWindow.OnOpenCount, "首次 Show 后 OnOpen 应执行一次。");
            Assert.AreEqual(1, LifecycleTrackingWindow.OnRefreshCount, "首次 Show 后 OnRefresh 应执行一次。");
            Assert.AreEqual(2, LifecycleTrackingWindow.LastRefreshArgCount, "OnRefresh 应收到 2 个参数。");
        }

        // ============================================================
        // 场景 b：重复 Show 参数更新
        // ============================================================

        /// <summary>
        /// 重复 Show 参数更新：窗口已 Open 时再次 Show 同一类型并传入新参数，
        /// 系统应更新 <c>UserData</c> 并再执行一次 <c>OnRefresh</c>，但不重复 <c>OnCreate</c> 或 <c>OnOpen</c>。
        /// </summary>
        /// <remarks>
        /// spec "窗口生命周期次数确定"——OnCreate 和 OnDispose 在单个实例上各执行一次，
        /// OnOpen、OnRefresh 按每轮打开执行。同一轮打开内的重复 Show 只触发刷新，不重新打开。
        /// design.md 决策4：有效请求按进入顺序执行刷新，最终显示状态由最后一个完成刷新请求决定。
        /// </remarks>
        [Test]
        [Description("重复 Show 参数更新：不重复 OnCreate/OnOpen，只更新 UserData 并执行 OnRefresh。")]
        public async UniTask RepeatShow_UpdatesArgs_RefreshesWithoutReopen()
        {
            // 安排：装配模块并注册生命周期追踪窗口。
            FUIModule module = SetupWithCustomWindow(typeof(LifecycleTrackingWindow), FUICacheMode.None);

            // 执行：首次 Show。
            LifecycleTrackingWindow window1 = await module.ShowAsync<LifecycleTrackingWindow>("arg-1");

            // 执行：重复 Show，传入新参数。
            LifecycleTrackingWindow window2 = await module.ShowAsync<LifecycleTrackingWindow>("arg-2");

            // 断言：同一实例（不重复创建）。
            Assert.AreSame(window1, window2, "重复 Show 同一 Open 窗口应返回同一实例。");

            // 断言：UserData 更新为最新参数。
            Assert.AreEqual("arg-2", window2.UserData, "重复 Show 后 UserData 应更新为最新参数。");

            // 断言：OnCreate 仍只执行一次，OnOpen 仍只执行一次，OnRefresh 执行两次。
            Assert.AreEqual(1, LifecycleTrackingWindow.OnCreateCount, "重复 Show 不应再执行 OnCreate。");
            Assert.AreEqual(1, LifecycleTrackingWindow.OnOpenCount, "重复 Show 不应再执行 OnOpen。");
            Assert.AreEqual(2, LifecycleTrackingWindow.OnRefreshCount, "重复 Show 应再执行一次 OnRefresh。");
        }

        // ============================================================
        // 场景 c：调用取消
        // ============================================================

        /// <summary>
        /// 调用取消：调用方在窗口加载完成前取消自己的请求，该调用应抛 <c>OperationCanceledException</c>，
        /// 且不得取消仍被其他请求共享的包加载。
        /// </summary>
        /// <remarks>
        /// spec "Show 操作可等待且错误明确"——调用方取消 SHALL 以 OperationCanceledException 表达，
        /// 且不得取消仍被其他请求共享的包加载。
        /// design.md 决策4：共享加载只受模块 lifetime token 控制，调用方令牌只取消该调用方的等待。
        /// </remarks>
        [Test]
        [Description("调用取消：调用方取消只取消自己的等待，不传播到共享加载。")]
        public async UniTask CallerCancellation_OnlyCancelsOwnAwait()
        {
            // 安排：装配模块（使用延迟 provider 使取消窗口存在）。
            FUIModule module = SetupWithCustomWindow(
                typeof(LifecycleTrackingWindow), FUICacheMode.None, loadDelayMs: 50);

            // 执行：调用方 A 带取消令牌，调用方 B 无取消令牌。
            CancellationTokenSource ctsA = new CancellationTokenSource();
            UniTask<LifecycleTrackingWindow> taskA =
                module.ShowAsync<LifecycleTrackingWindow>(ctsA.Token, "argA");
            UniTask<LifecycleTrackingWindow> taskB =
                module.ShowAsync<LifecycleTrackingWindow>("argB");

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
            LifecycleTrackingWindow windowB = await taskB;

            // 断言。
            Assert.IsTrue(aCancelled, "调用方 A 取消应抛 OperationCanceledException。");
            Assert.IsNotNull(windowB, "调用方 B 应正常完成并获得窗口。");
            Assert.AreEqual(1, LifecycleTrackingWindow.OnCreateCount, "共享加载不应被取消，实例应创建一次。");

            ctsA.Dispose();
        }

        // ============================================================
        // 场景 d：加载期间 Close
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
        [Description("加载期间 Close：操作版本过期，完成后回滚不闪现。")]
        public async UniTask CloseDuringLoad_MakesOperationStale_RollsBack()
        {
            // 安排：装配模块（使用延迟 provider 使 Close 在加载期间生效）。
            FUIModule module = SetupWithCustomWindow(
                typeof(LifecycleTrackingWindow), FUICacheMode.None, loadDelayMs: 100);

            // 执行：发起 Show（不 await），在加载期间 Close。
            UniTask<LifecycleTrackingWindow> showTask = module.ShowAsync<LifecycleTrackingWindow>();

            // Close 在加载期间执行（递增 operation version）。
            module.Close<LifecycleTrackingWindow>();

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
            Assert.AreEqual(0, LifecycleTrackingWindow.OnOpenCount, "过期操作不应执行 OnOpen。");
            Assert.AreEqual(0, LifecycleTrackingWindow.OnRefreshCount, "过期操作不应执行 OnRefresh。");

            // 断言：entry 状态已回滚（不在 Open 状态）。
            Assert.IsTrue(module._windowEntries.TryGetValue(typeof(LifecycleTrackingWindow), out WindowEntry entry),
                "应存在窗口条目。");
            Assert.AreNotEqual(FUIWindowState.Open, entry.State, "过期操作回滚后不应处于 Open 状态。");
        }

        // ============================================================
        // 场景 e：Hide
        // ============================================================

        /// <summary>
        /// Hide 不结束打开域：一个 Open 窗口被 Hide 后，系统 SHALL 执行同步 OnHide 并改变显示与输入状态，
        /// 但 SHALL 不执行 OnClose、清理打开事件或取消 Open Token。再次显示时恢复可见但不重新执行 OnOpen。
        /// </summary>
        /// <remarks>
        /// spec "Hide 不结束打开域"——一个 Open 窗口被 Hide 后再次显示，
        /// 系统 SHALL 执行同步 OnHide 并改变显示与输入状态，但 SHALL 不执行 OnClose、清理打开事件或取消 Open Token。
        /// design.md 决策5：临时隐藏 OnHide -> visible/touchable false，保留 open CTS 与事件域。
        /// </remarks>
        [Test]
        [Description("Hide 不结束打开域：OnHide 执行，visible/touchable false，OnClose 不执行。")]
        public async UniTask Hide_DoesNotEndOpenDomain()
        {
            // 安排：装配模块并注册生命周期追踪窗口。
            FUIModule module = SetupWithCustomWindow(typeof(LifecycleTrackingWindow), FUICacheMode.None);

            // 执行：Show 后 Hide。
            LifecycleTrackingWindow window = await module.ShowAsync<LifecycleTrackingWindow>();
            Assert.IsTrue(window.visible, "Show 后窗口应可见。");
            Assert.IsTrue(window.touchable, "Show 后窗口应可交互。");

            module.Hide<LifecycleTrackingWindow>();

            // 断言：OnHide 执行一次。
            Assert.AreEqual(1, LifecycleTrackingWindow.OnHideCount, "Hide 应执行一次 OnHide。");

            // 断言：OnClose 未执行（Hide 不结束打开域）。
            Assert.AreEqual(0, LifecycleTrackingWindow.OnCloseCount, "Hide 不应执行 OnClose。");

            // 断言：visible/touchable 为 false。
            Assert.IsFalse(window.visible, "Hide 后窗口应不可见。");
            Assert.IsFalse(window.touchable, "Hide 后窗口应不可交互。");

            // 断言：状态为 Hidden。
            Assert.AreEqual(FUIWindowState.Hidden, GetEntryState(module, typeof(LifecycleTrackingWindow)),
                "Hide 后应处于 Hidden。");

            // 断言：窗口仍保留在显示树中（不移出 Stage）。
            Assert.IsNotNull(window.parent, "Hide 后窗口应保留 Stage 归属（不移出显示树）。");

            // 执行：再次 Show（从 Hidden 恢复显示）。
            LifecycleTrackingWindow window2 = await module.ShowAsync<LifecycleTrackingWindow>("refresh-after-hide");

            // 断言：同一实例。
            Assert.AreSame(window, window2, "Hidden 再次 Show 应返回同一实例。");

            // 断言：visible/touchable 恢复。
            Assert.IsTrue(window.visible, "再次 Show 后窗口应恢复可见。");
            Assert.IsTrue(window.touchable, "再次 Show 后窗口应恢复可交互。");

            // 断言：不重新执行 OnOpen（打开域未被结束）。
            Assert.AreEqual(1, LifecycleTrackingWindow.OnOpenCount, "Hidden 再次 Show 不应重新执行 OnOpen。");

            // 断言：OnRefresh 再次执行（刷新队列处理）。
            Assert.AreEqual(2, LifecycleTrackingWindow.OnRefreshCount, "Hidden 再次 Show 应再执行一次 OnRefresh。");

            // 断言：状态恢复为 Open。
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTrackingWindow)),
                "再次 Show 后应恢复为 Open。");
        }

        // ============================================================
        // 场景 f：默认 Close（Dispose）
        // ============================================================

        /// <summary>
        /// 默认 Close 即 Dispose：未声明缓存的窗口被关闭后，系统 SHALL 执行 OnClose、移出层容器、
        /// 执行 OnDispose 并释放窗口持有的包租约。默认 CacheMode=None 下 Close 后状态为 Disposed。
        /// </summary>
        /// <remarks>
        /// spec "默认关闭即释放且缓存显式启用"——未声明缓存的窗口被关闭，
        /// 系统 SHALL 执行 OnClose、移出层容器、执行 OnDispose 并释放窗口持有的包租约。
        /// design.md 决策5：默认 CacheMode 为 None，只有描述显式声明 Cache 时才在 Close 后保留实例和租约。
        /// </remarks>
        [Test]
        [Description("默认 Close 即 Dispose：OnClose 与 OnDispose 执行，实例释放，状态 Disposed。")]
        public async UniTask DefaultClose_DisposesInstance()
        {
            // 安排：装配模块并注册生命周期追踪窗口（默认 CacheMode=None）。
            FUIModule module = SetupWithCustomWindow(typeof(LifecycleTrackingWindow), FUICacheMode.None);

            // 执行：Show 后 Close。
            LifecycleTrackingWindow window = await module.ShowAsync<LifecycleTrackingWindow>();
            module.Close<LifecycleTrackingWindow>();

            // 断言：OnClose 执行一次。
            Assert.AreEqual(1, LifecycleTrackingWindow.OnCloseCount, "Close 应执行一次 OnClose。");

            // 断言：OnDispose 执行一次（默认 None 最终释放）。
            Assert.AreEqual(1, LifecycleTrackingWindow.OnDisposeCount, "默认 CacheMode=None Close 后应执行 OnDispose。");

            // 断言：窗口已从显示树移除。
            Assert.IsNull(window.parent, "Close 后窗口应从显示树移除。");

            // 断言：状态为 Disposed。
            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(LifecycleTrackingWindow)),
                "默认 CacheMode=None Close 后应处于 Disposed。");
        }

        // ============================================================
        // 场景 g：显式 Cache
        // ============================================================

        /// <summary>
        /// 显式 Cache：声明 Cache 的窗口被关闭后，系统 SHALL 清理本轮打开域并从显示树移除窗口，
        /// 但 SHALL 保留实例及其包租约直到最终 Dispose。Cached 再打开时不重复 OnCreate，但重新执行 OnOpen 与 OnRefresh。
        /// </summary>
        /// <remarks>
        /// spec "默认关闭即释放且缓存显式启用"——声明缓存的窗口被关闭，
        /// 系统 SHALL 清理本轮打开域并从显示树移除窗口，但 SHALL 保留实例及其包租钥直到最终 Dispose。
        /// spec "缓存窗口重新打开"——一个已关闭并缓存的窗口再次打开，
        /// 系统 SHALL 不重复执行 OnCreate，但 SHALL 重新建立打开取消域和事件域，并再次执行 OnOpen 与 OnRefresh。
        /// design.md 决策5：显式 Cache 在 Close 后保留实例和租约。
        /// </remarks>
        [Test]
        [Description("显式 Cache：Close 保留实例，再打开不重复 OnCreate 但重新 OnOpen/OnRefresh。")]
        public async UniTask ExplicitCache_PreservesInstance_OnReopenNoOnCreate()
        {
            // 安排：装配模块并注册缓存追踪窗口（CacheMode=Cache）。
            FUIModule module = SetupWithCustomWindow(typeof(CachedTrackingWindow), FUICacheMode.Cache);

            // 执行：首次 Show 后 Close。
            CachedTrackingWindow window1 = await module.ShowAsync<CachedTrackingWindow>("first");
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(CachedTrackingWindow)),
                "首次 Show 后应处于 Open。");

            module.Close<CachedTrackingWindow>();

            // 断言：OnClose 执行一次。
            Assert.AreEqual(1, CachedTrackingWindow.OnCloseCount, "Close 应执行一次 OnClose。");

            // 断言：OnDispose 未执行（Cache 保留实例）。
            Assert.AreEqual(0, CachedTrackingWindow.OnDisposeCount, "Cache 模式 Close 不应执行 OnDispose。");

            // 断言：状态为 Cached。
            Assert.AreEqual(FUIWindowState.Cached, GetEntryState(module, typeof(CachedTrackingWindow)),
                "Cache 模式 Close 后应处于 Cached。");

            // 断言：窗口已从显示树移除。
            Assert.IsNull(window1.parent, "Cache Close 后窗口应从显示树移除。");

            // 执行：再次 Show（从 Cached 重新打开）。
            CachedTrackingWindow window2 = await module.ShowAsync<CachedTrackingWindow>("second");

            // 断言：同一实例（Cache 保留）。
            Assert.AreSame(window1, window2, "Cached 再打开应返回同一实例。");

            // 断言：不重复 OnCreate。
            Assert.AreEqual(1, CachedTrackingWindow.OnCreateCount, "Cached 再打开不应重复 OnCreate。");

            // 断言：重新执行 OnOpen 与 OnRefresh。
            Assert.AreEqual(2, CachedTrackingWindow.OnOpenCount, "Cached 再打开应重新执行 OnOpen。");
            Assert.AreEqual(2, CachedTrackingWindow.OnRefreshCount, "Cached 再打开应重新执行 OnRefresh。");

            // 断言：状态恢复为 Open。
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(CachedTrackingWindow)),
                "Cached 再打开后应恢复为 Open。");

            // 断言：UserData 更新为最新参数。
            Assert.AreEqual("second", window2.UserData, "Cached 再打开后 UserData 应更新为最新参数。");
        }

        // ============================================================
        // 场景 h：Dispose（Cached 窗口最终释放）
        // ============================================================

        /// <summary>
        /// Dispose：Cached 窗口再次 Close 时最终释放，执行 OnDispose 并释放租约，状态进入 Disposed。
        /// </summary>
        /// <remarks>
        /// spec "默认关闭即释放且缓存显式启用"——缓存窗口最终 Dispose。
        /// spec "窗口生命周期次数确定"——OnDispose 在单个实例上各执行一次。
        /// design.md 决策5：最终释放顺序 Dispose Widgets -> OnDispose -> Descriptor.Detach -> Dispose GObject -> Release lease。
        /// </remarks>
        [Test]
        [Description("Dispose：Cached 窗口再次 Close 最终释放，OnDispose 执行，状态 Disposed。")]
        public async UniTask Dispose_CachedWindowFinalRelease()
        {
            // 安排：装配模块并注册缓存追踪窗口（CacheMode=Cache）。
            FUIModule module = SetupWithCustomWindow(typeof(CachedTrackingWindow), FUICacheMode.Cache);

            // 执行：首次 Show -> Close（进入 Cached） -> 再次 Close（最终释放）。
            CachedTrackingWindow window = await module.ShowAsync<CachedTrackingWindow>();
            module.Close<CachedTrackingWindow>();
            Assert.AreEqual(FUIWindowState.Cached, GetEntryState(module, typeof(CachedTrackingWindow)),
                "首次 Close 后应处于 Cached。");

            // 执行：再次 Close（Cached -> Disposed）。
            module.Close<CachedTrackingWindow>();

            // 断言：OnDispose 执行一次。
            Assert.AreEqual(1, CachedTrackingWindow.OnDisposeCount, "Cached 最终 Close 应执行 OnDispose。");

            // 断言：状态为 Disposed。
            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(CachedTrackingWindow)),
                "Cached 最终 Close 后应处于 Disposed。");

            // 断言：窗口已从显示树移除。
            Assert.IsNull(window.parent, "Dispose 后窗口应从显示树移除。");
        }

        // ============================================================
        // 场景 i：重复模块注册保护
        // ============================================================

        /// <summary>
        /// 重复模块注册保护：模块已注册时，第二次调用 <c>FUI.RegisterModuleForTesting</c> SHALL 抛 <c>FUIException</c>，
        /// 避免旧实例残留在更新队列。
        /// </summary>
        /// <remarks>
        /// spec "显式注册窗口和 Widget"——模块访问不得隐式创建或重复注册模块。
        /// design.md 决策1：第二次注册在进入 ModuleSystem 前被拒绝，避免旧实例残留在更新队列。
        /// </remarks>
        [Test]
        [Description("重复模块注册保护：第二次注册抛 FUIException。")]
        public void DuplicateModuleRegistration_ThrowsFUIException()
        {
            // 安排：首次注册模块（通过 harness 的 SetupForShowAsync 完成完整装配）。
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();

            // 执行与断言：第二次注册应抛 FUIException。
            InMemoryFUIResourceProvider secondProvider = PlayModeTestHarness.CreateProviderWithUIBattleAndUICommon();
            FUIException ex = Assert.Throws<FUIException>(() =>
                FUI.RegisterModuleForTesting(secondProvider));

            // 断言：异常消息包含已注册上下文。
            Assert.IsTrue(ex.Message.Contains("已注册"), "重复注册异常消息应包含已注册上下文。");

            // 断言：原模块仍可用（未被第二次注册破坏）。
            Assert.IsNotNull(FUI.Module, "重复注册失败后原模块应仍可访问。");
            Assert.AreSame(module, FUI.Module, "重复注册失败后原模块实例应不变。");
        }

        // ============================================================
        // 场景 j：退出 Shutdown
        // ============================================================

        /// <summary>
        /// 退出 Shutdown：模块在存在 Open 窗口时 Shutdown，所有操作 SHALL 结束，
        /// 所有窗口实例、事件域、取消域和租约 SHALL 被清理且不得留下后续回调。
        /// Shutdown 后 <c>FUI.Module</c> getter 应抛异常（静态缓存已清空）。
        /// </summary>
        /// <remarks>
        /// spec "模块退出完整清理"——模块退出 SHALL 取消所有进行中的打开操作，按反向顺序关闭并释放窗口，
        /// 执行 detach，清理本地描述、owner、活动 Registry 和静态模块缓存，并把所持包租约交还资源管理能力。
        /// 模块不得调用会清除其他 FairyGUI 扩展的全局 UIObjectFactory.Clear()。
        /// design.md 决策1/8：Shutdown 清空静态缓存；包回收按 KeepUntilShutdown 策略强制执行。
        /// </remarks>
        [Test]
        [Description("退出 Shutdown：Open 窗口被清理，模块缓存清空，Module getter 抛异常。")]
        public async UniTask Shutdown_ClearsAllWindowsEntriesAndModuleCache()
        {
            // 安排：装配模块并打开窗口。
            FUIModule module = SetupWithCustomWindow(typeof(LifecycleTrackingWindow), FUICacheMode.None);
            LifecycleTrackingWindow window = await module.ShowAsync<LifecycleTrackingWindow>();
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(LifecycleTrackingWindow)),
                "Show 后应处于 Open。");

            // 执行：Shutdown。
            module.Shutdown();

            // 断言：OnDispose 已执行（Shutdown 释放窗口实例）。
            Assert.AreEqual(1, LifecycleTrackingWindow.OnDisposeCount, "Shutdown 应触发窗口 OnDispose。");

            // 断言：窗口已从显示树移除。
            Assert.IsNull(window.parent, "Shutdown 后窗口应从显示树移除。");

            // 断言：FUI.Module getter 抛异常（静态缓存已清空）。
            Assert.Throws<FUIException>(() =>
            {
                IFUIModule _ = FUI.Module;
            }, "Shutdown 后 FUI.Module getter 应抛 FUIException。");

            // 断言：windowEntries 已清空。
            Assert.AreEqual(0, module._windowEntries.Count, "Shutdown 后 windowEntries 应被清空。");
        }

        // ============================================================
        // 场景 j2：Shutdown 期间存在 Loading 窗口
        // ============================================================

        /// <summary>
        /// Shutdown 期间存在 Loading 窗口：模块在同时存在 Open 窗口和 Loading 窗口时 Shutdown，
        /// 所有操作 SHALL 结束，所有窗口实例、事件域、取消域和租约 SHALL 被清理。
        /// </summary>
        /// <remarks>
        /// spec "模块退出完整清理" / "PlayMode 退出时仍有打开和加载窗口"——模块在同时存在 Open 窗口和 Loading 窗口时 Shutdown，
        /// 所有操作 SHALL 结束，所有窗口实例、事件域、取消域和租约 SHALL 被清理且不得留下后续回调。
        /// </remarks>
        [Test]
        [Description("Shutdown 期间存在 Loading 窗口：加载操作结束，无悬挂回调。")]
        public async UniTask Shutdown_WithLoadingWindow_ClearsAllOperations()
        {
            // 安排：装配模块（使用延迟 provider 使 Shutdown 时有 Loading 窗口）。
            FUIModule module = SetupWithCustomWindow(
                typeof(LifecycleTrackingWindow), FUICacheMode.None, loadDelayMs: 200);

            // 执行：发起 Show（不 await），在加载期间 Shutdown。
            UniTask<LifecycleTrackingWindow> showTask = module.ShowAsync<LifecycleTrackingWindow>();

            // 等待一小段时间确保进入 Loading 状态。
            await UniTask.Delay(50);

            // 断言：此时存在 Loading 条目。
            Assert.IsTrue(module._windowEntries.ContainsKey(typeof(LifecycleTrackingWindow)),
                "发起 Show 后应存在窗口条目。");

            // 执行：Shutdown。
            module.Shutdown();

            // 断言：Show 任务应因模块 lifetime token 取消而抛异常。
            bool showCancelled = false;
            try
            {
                await showTask;
            }
            catch (OperationCanceledException)
            {
                showCancelled = true;
            }
            catch (FUIException)
            {
                // Shutdown 后 Show 也可能以 FUIException 表达。
                showCancelled = true;
            }

            Assert.IsTrue(showCancelled, "Shutdown 后进行中的 Show 应被取消。");

            // 断言：windowEntries 已清空。
            Assert.AreEqual(0, module._windowEntries.Count, "Shutdown 后 windowEntries 应被清空。");

            // 断言：FUI.Module getter 抛异常。
            Assert.Throws<FUIException>(() =>
            {
                IFUIModule _ = FUI.Module;
            }, "Shutdown 后 FUI.Module getter 应抛 FUIException。");
        }

        // ============================================================
        // 辅助方法
        // ============================================================

        /// <summary>
        /// 装配模块并注册自定义窗口类型，使用指定的 CacheMode 与加载延迟。
        /// </summary>
        /// <param name="windowType">最终业务窗口类型，必须为 <see cref="FUIWindow"/> 子类。</param>
        /// <param name="cacheMode">缓存策略。</param>
        /// <param name="loadDelayMs">内存 provider 加载延迟毫秒数，用于异步时序模拟。</param>
        /// <returns>已完成 FreezeBindings 的 <see cref="FUIModule"/>。</returns>
        /// <remarks>
        /// 本方法复用 <see cref="PlayModeTestHarness"/> 的公开构建块（<see cref="PlayModeTestHarness.Cleanup"/>、
        /// <see cref="PlayModeTestHarness.CreateProviderWithUIBattleAndUICommon"/>、
        /// <see cref="PlayModeTestHarness.CreateTypeBasedCreator"/>），不修改 harness。
        /// 装配流程与 <see cref="PlayModeTestHarness.SetupForShowAsync"/> 一致，唯一差异是允许自定义窗口类型与 CacheMode。
        /// 注册顺序与 spec "测试 owner 注册 UIBattle" 一致：先 Binder，再 Widget，再 Window。
        /// </remarks>
        private static FUIModule SetupWithCustomWindow(
            Type windowType,
            FUICacheMode cacheMode,
            int loadDelayMs = 0)
        {
            // 1. 幂等清空全局状态。
            PlayModeTestHarness.Cleanup();

            // 2. 构造内存 provider，预设 UIBattle/UICommon 描述与图集资源。
            InMemoryFUIResourceProvider provider = PlayModeTestHarness.CreateProviderWithUIBattleAndUICommon(loadDelayMs);

            // 3. 显式注册资源能力与选项。
            FUI.RegisterModuleForTesting(provider);
            FUIModule module = (FUIModule)FUI.Module;

            // 4. 调用 UIBattle Binder：注册生成类型 URL 到全局 UIObjectFactory。
            UIBattleBinder.BindAll();

            // 5. 注册最终测试 Widget（覆盖生成类型，Creator 非空）。
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
                creator: PlayModeTestHarness.CreateTypeBasedCreator(typeof(TestBattleStartWidget))));

            // 6. 注册最终测试 Window（覆盖生成类型，Creator 非空，使用指定 CacheMode）。
            module.BindingRegistry.Register(new FUIDescriptor(
                url: UI_BattleStartPanel.URL,
                packageName: UI_BattleStartPanel.PkgName,
                componentName: UI_BattleStartPanel.ResName,
                ownerType: TestFUIOwner.OwnerType,
                targetType: windowType,
                layer: FUILayer.Normal,
                fullScreen: false,
                cacheMode: cacheMode,
                safeAreaMode: FUISafeAreaMode.Full,
                creator: PlayModeTestHarness.CreateTypeBasedCreator(windowType)));

            // 7. 冻结 Registry：安装全局无状态 creator 并建立层级容器。
            module.FreezeBindings();

            return module;
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

        // ============================================================
        // 测试用窗口类型
        // ============================================================

        /// <summary>
        /// 生命周期追踪测试窗口，继承 <see cref="TestBattleStartPanel"/>。
        /// 通过静态计数器记录各生命周期回调执行次数与刷新参数，供测试断言。
        /// </summary>
        /// <remarks>
        /// design.md 决策5：OnCreate/OnDispose 各执行一次，OnOpen/OnRefresh/OnClose/OnHide 按每轮打开执行。
        /// 本类型在 protected virtual 回调中递增静态计数器，不引入可变实例字段以保证跨实例累计统计。
        /// </remarks>
        public class LifecycleTrackingWindow : TestBattleStartPanel
        {
            /// <summary>OnCreate 累计执行次数。</summary>
            public static int OnCreateCount;
            /// <summary>OnOpen 累计执行次数。</summary>
            public static int OnOpenCount;
            /// <summary>OnRefresh 累计执行次数。</summary>
            public static int OnRefreshCount;
            /// <summary>OnHide 累计执行次数。</summary>
            public static int OnHideCount;
            /// <summary>OnClose 累计执行次数。</summary>
            public static int OnCloseCount;
            /// <summary>OnDispose 累计执行次数。</summary>
            public static int OnDisposeCount;
            /// <summary>最后一次 OnRefresh 收到的参数数量。</summary>
            public static int LastRefreshArgCount;

            /// <summary>
            /// 重置所有静态计数器为 0，在每个测试的 SetUp 中调用。
            /// </summary>
            public static void ResetCounters()
            {
                OnCreateCount = 0;
                OnOpenCount = 0;
                OnRefreshCount = 0;
                OnHideCount = 0;
                OnCloseCount = 0;
                OnDisposeCount = 0;
                LastRefreshArgCount = 0;
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
                LastRefreshArgCount = UserDatas?.Length ?? 0;
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
            }

            /// <inheritdoc/>
            protected override void OnDispose()
            {
                base.OnDispose();
                OnDisposeCount++;
            }
        }

        /// <summary>
        /// 缓存模式生命周期追踪测试窗口，继承 <see cref="TestBattleStartPanel"/>。
        /// 用于验证显式 Cache 场景下的生命周期次数与状态转换。
        /// </summary>
        public class CachedTrackingWindow : TestBattleStartPanel
        {
            /// <summary>OnCreate 累计执行次数。</summary>
            public static int OnCreateCount;
            /// <summary>OnOpen 累计执行次数。</summary>
            public static int OnOpenCount;
            /// <summary>OnRefresh 累计执行次数。</summary>
            public static int OnRefreshCount;
            /// <summary>OnClose 累计执行次数。</summary>
            public static int OnCloseCount;
            /// <summary>OnDispose 累计执行次数。</summary>
            public static int OnDisposeCount;

            /// <summary>
            /// 重置所有静态计数器为 0，在每个测试的 SetUp 中调用。
            /// </summary>
            public static void ResetCounters()
            {
                OnCreateCount = 0;
                OnOpenCount = 0;
                OnRefreshCount = 0;
                OnCloseCount = 0;
                OnDisposeCount = 0;
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
            protected override void OnClose()
            {
                base.OnClose();
                OnCloseCount++;
            }

            /// <inheritdoc/>
            protected override void OnDispose()
            {
                base.OnDispose();
                OnDisposeCount++;
            }
        }
    }
}
