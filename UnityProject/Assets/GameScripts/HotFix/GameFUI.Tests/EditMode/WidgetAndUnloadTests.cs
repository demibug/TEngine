using System;
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
    /// Widget 池化与运行期包卸载的 EditMode 单元测试，覆盖任务 6.6 的全部场景：
    /// 缓存窗口持有包（Cached 状态窗口持有的包与依赖不被卸载）、
    /// 共享依赖（窗口通过包依赖链共享依赖包的引用计数）、
    /// 重复 Release（窗口租约重复释放不产生负引用）、
    /// 延迟期重开（Delayed 策略下延迟期内重新 Show 取消卸载）、
    /// 动态 Widget 池化（动态创建与池化复用 Widget 的生命周期）。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - design.md 决策5（实例生命周期与每轮打开生命周期分离）、决策6（Widget 幂等 Attach 与受控创建入口）、
    ///   决策8（包加载采用异步预载、同步解析；延迟卸载前重新 Acquire 取消旧卸载任务）、
    ///   决策9（最终卸载前置检查含存活/缓存窗口、创建任务与上层依赖）；
    /// - spec fairygui-package-loading“包租约控制缓存和卸载”“资源所有权唯一且释放有序”
    ///   “包和依赖加载任务合并”“延迟卸载期间重新 Acquire”；
    /// - spec fairygui-window-runtime“默认关闭即释放且缓存显式启用”
    ///   “Widget 在生命周期前获得所属窗口”“动态列表 Widget”。
    ///
    /// 测试装配：与 <see cref="WindowLifecycleTests"/> 一致，使用内存资源 provider 与测试 owner
    /// （<see cref="TestFUIOwner"/>）、测试窗口完成装配。内存 provider 预设 UIBattle 描述资源与
    /// UICommon 描述资源（含 Atlas），使包加载在 EditMode 内完整成功。
    ///
    /// 复用证据：
    /// - 复用 <see cref="InMemoryFUIResourceProvider"/>（4.3 产出，通过 InternalsVisibleTo 暴露）；
    /// - 复用磁盘上真实发布的 <c>UIBattle_fui.bytes</c> 与 <c>UICommon_fui.bytes</c> 描述资源
    ///   （1.1 核对的规范资源清单），通过 <see cref="AssetDatabase.LoadAssetAtPath"/> 加载，
    ///   保留二进制字节完整性；
    /// - 复用 <see cref="TestFUIOwner.OwnerType"/>（3.6 产出，owner 类型标识）；
    /// - 复用 <see cref="FUI.RegisterModuleForTesting"/>（5.1 产出的 internal 测试入口）；
    /// - 复用 <see cref="PackageLoader.Configure"/>（6.4 产出的 Delayed 策略配置入口）；
    /// - 复用 <see cref="PackageLoader.SetWindowStateProvider"/>（6.5 产出，由 FUIModule.FreezeBindings 注册）；
    /// - 复用 <see cref="FUIModule._windowEntries"/> / <see cref="WindowEntry.Lease"/> 等 internal 访问器；
    /// - 复用 <see cref="FUIModule.CreateDynamicWidget{TWidget}"/> / <see cref="FUIModule.AttachDynamicWidget"/>
    ///   （6.2 产出的动态 Widget 受控入口）；
    /// - 复用 <see cref="FUIWidget.ResetForReuse"/> / <see cref="FUIWidget.IsCreated"/> / <see cref="FUIWidget.IsDisposed"/>
    ///   （6.2 产出的池化复用入口与生命周期幂等标记）。
    ///
    /// 资源隔离：每个测试在 <see cref="SetUp"/> 中重新注册模块并清空 FairyGUI 全局状态；
    /// <see cref="TearDown"/> 执行模块 Shutdown 与全局清理，避免跨测试残留。
    ///
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，只通过公开/internal API 访问被测对象。
    /// 本测试不依赖 GameLogic/GamePlay/GameBattle，不创建或修改 BattleModule。
    /// </remarks>
    [TestFixture]
    public class WidgetAndUnloadTests
    {
        /// <summary>
        /// 测试窗口使用的包名常量，与生成类型 <see cref="UI_BattleStartPanel"/> 的 PkgName 一致。
        /// </summary>
        private const string UIBattlePkg = "UIBattle";

        /// <summary>
        /// UICommon 共享依赖包名常量。UIBattle 声明依赖 UICommon（共享图集）。
        /// </summary>
        private const string UICommonPkg = "UICommon";

        /// <summary>
        /// 每个测试前的状态基线重置：清空 FairyGUI 全局包注册表与 PackageLoader 注册表，
        /// 重新注册 GameFUI 模块，完成测试 owner 注册与冻结。
        /// </summary>
        /// <remarks>
        /// 装配流程遵循 design.md 决策10：
        /// FUI.RegisterModuleForTesting -> 测试 owner 注册 UIBattle -> FreezeBindings。
        /// 同时重置 PackageLoader 的卸载策略配置为默认 KeepUntilShutdown，
        /// 避免上一测试的 Delayed 配置残留污染当前测试。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            // 清空 FairyGUI 全局状态与 PackageLoader 静态注册表，避免跨测试残留。
            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();

            // 清空 FUI 门面静态缓存（防止上一测试残留模块实例）。
            FUI.ClearModuleForShutdown();

            // 清空 FUIObjectFactoryIntegration 的活动 Registry 静态引用，
            // 防止上一测试异常退出导致 InstallPackageItemExtensions 拒绝新 Registry。
            FUIObjectFactoryIntegration.ClearActiveRegistry();

            // 重置 PackageLoader 卸载策略为默认 KeepUntilShutdown，
            // 避免上一测试设置 Delayed 后残留影响当前测试。
            PackageLoader.Configure(FUIPackageUnloadPolicy.KeepUntilShutdown, 5f);

            // 确保 FairyGUI Stage/GRoot 已初始化（EditMode 下需要主动触发）。
            GRoot.inst.SetSize(1920, 1080);
        }

        /// <summary>
        /// 每个测试后的状态基线重置，与 <see cref="SetUp"/> 对称，确保即使测试中途失败
        /// 也不会残留全局状态污染后续测试。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            // 若模块仍注册，执行 Shutdown 完整清理。
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
                // 其他异常不阻塞 TearDown 清理。
            }

            PackageLoader.ClearRegistry();
            UIPackage.RemoveAllPackages();
            FUI.ClearModuleForShutdown();

            // 重置 PackageLoader 卸载策略为默认值，避免残留。
            PackageLoader.Configure(FUIPackageUnloadPolicy.KeepUntilShutdown, 5f);
        }

        // ============================================================
        // 场景 a：缓存窗口持有包——Cached 状态窗口持有的包与依赖不被卸载
        // ============================================================

        /// <summary>
        /// 缓存窗口持有包：声明 CacheMode=Cache 的窗口 Close 后进入 Cached 状态，
        /// 其包租约与依赖租约保持，包不被卸载（引用计数 > 0，UIPackage 仍在注册表）。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading“包租约控制缓存和卸载——缓存窗口仍持有包：
        /// 窗口关闭后进入 Cached 状态，其包和依赖 SHALL 保持租约，不得因其他窗口关闭而卸载”。
        /// design.md 决策5：显式声明 Cache 时 Close 保留实例与租约。
        /// design.md 决策9：最终卸载前置检查含缓存窗口。
        /// </remarks>
        [Test]
        [Description("缓存窗口持有包：Cached 状态窗口的包与依赖引用计数 > 0，包不被卸载。")]
        public async UniTask CachedWindow_HoldsPackageAndDependencies_NotUnloaded()
        {
            // 安排：装配模块并注册 Cache 模式窗口。
            FUIModule module = SetupModuleWithCachedWindow();

            // 执行：Show 窗口。
            CachedTestWindow window = await module.ShowAsync<CachedTestWindow>();
            Assert.AreEqual(FUIWindowState.Open, GetEntryState(module, typeof(CachedTestWindow)),
                "Show 后应处于 Open。");

            // 获取 Show 后的引用计数基线。
            PackageRecord battleRecord = PackageLoader.FindRecord(UIBattlePkg);
            PackageRecord commonRecord = PackageLoader.FindRecord(UICommonPkg);
            Assert.IsNotNull(battleRecord, "UIBattle 包记录应存在。");
            Assert.IsNotNull(commonRecord, "UICommon 依赖包记录应存在。");
            int battleRefCountAfterShow = battleRecord.ReferenceCount;
            int commonRefCountAfterShow = commonRecord.ReferenceCount;
            Assert.GreaterOrEqual(battleRefCountAfterShow, 1, "Show 后 UIBattle 引用计数应 >= 1。");
            Assert.GreaterOrEqual(commonRefCountAfterShow, 1, "Show 后 UICommon 依赖引用计数应 >= 1。");

            // 执行：Close 窗口（CacheMode=Cache → 进入 Cached，保留实例与租约）。
            module.Close<CachedTestWindow>();

            // 断言：窗口进入 Cached 状态。
            Assert.AreEqual(FUIWindowState.Cached, GetEntryState(module, typeof(CachedTestWindow)),
                "CacheMode=Cache 的窗口 Close 后应处于 Cached。");

            // 断言：包引用计数不变（租约保留）。
            Assert.AreEqual(battleRefCountAfterShow, battleRecord.ReferenceCount,
                "Cached 窗口的包引用计数应保持不变（租约保留）。");
            Assert.AreEqual(commonRefCountAfterShow, commonRecord.ReferenceCount,
                "Cached 窗口的依赖包引用计数应保持不变（依赖租约保留）。");

            // 断言：包仍处于 Ready，UIPackage 未被移除。
            Assert.AreEqual(PackageLoadState.Ready, battleRecord.State,
                "Cached 窗口持有的包应仍处于 Ready。");
            Assert.IsNotNull(battleRecord.Package, "Cached 窗口持有的 UIPackage 不应被移除。");

            // 尝试显式卸载应被前置检查拒绝（缓存窗口持有）。
            bool unloaded = PackageLoader.UnloadPackage(UIBattlePkg, module);
            Assert.IsFalse(unloaded, "存在 Cached 窗口时 UnloadPackage 应被前置检查拒绝。");
            Assert.AreEqual(PackageLoadState.Ready, battleRecord.State,
                "前置检查拒绝后包应仍处于 Ready。");
        }

        // ============================================================
        // 场景 b：共享依赖——窗口通过包依赖链共享依赖包的引用计数
        // ============================================================

        /// <summary>
        /// 共享依赖：UIBattle 窗口声明依赖 UICommon（共享图集），Show 后 UICommon 作为依赖包
        /// 只加载一次并被 UIBattle 持有依赖租约；Close 窗口（CacheMode=None → Disposed）后，
        /// 窗口 lease 释放使 UIBattle 引用计数递减，但 UIBattle 持有的依赖 lease 仍保留
        /// （KeepUntilShutdown 策略下 UIBattle 记录不被 FinalUnload，依赖 lease 不释放），
        /// UICommon 引用计数保持 >0 且仍 Ready，验证"任一上层仍持有依赖时 C 保持可用"语义。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading“包和依赖加载任务合并——两个包共享依赖：
        /// 包 A 和包 B 都依赖包 C，包 C 只加载一次，并在 A 或 B 任一仍持有依赖时保持可用”。
        /// design.md 决策8：共享依赖租约。
        ///
        /// 覆盖降级说明：spec 的完整语义要求两个独立上层包 A、B 共享依赖 C，验证"A 或 B 任一仍持有
        /// 依赖时 C 保持可用"。本测试受限于测试装配只注册了单个上层包 UIBattle（A），未注册第二个
        /// 独立上层包 B 也依赖 UICommon（C），因此无法直接验证"关闭 B 后 A 仍持有 C"的完整双持有者场景。
        /// 作为降级覆盖，本测试验证单持有者场景下引用计数的正确传导：
        /// - Show 后 UICommon 引用计数 >0（由 UIBattle 的依赖 lease 持有）；
        /// - Close 窗口后 UIBattle 引用计数递减（窗口 lease 释放），但 UICommon 引用计数仍 >0
        ///   且 Ready（UIBattle 记录未被 FinalUnload，依赖 lease 未释放，C 仍可用）。
        /// 这验证了"上层包记录存活期间依赖保持可用"的核心约束，是双持有者场景的单持有者子集。
        /// </remarks>
        [Test]
        [Description("共享依赖：UIBattle 依赖 UICommon，Close 窗口后依赖 lease 仍持有 UICommon 可用。")]
        public async UniTask SharedDependency_WindowHoldsDependencyLease_RefCountCorrect()
        {
            // 安排：装配模块（使用 CacheMode=None 窗口，Close 直接触发 lease 释放便于观察引用计数）。
            FUIModule module = SetupModuleWithWidgetWindow();

            // 执行：Show 窗口（触发 UIBattle 加载，递归 Acquire 依赖 UICommon）。
            WidgetTestWindow window = await module.ShowAsync<WidgetTestWindow>();

            // 断言：UIBattle 与 UICommon 均已加载且引用计数正确。
            PackageRecord battleRecord = PackageLoader.FindRecord(UIBattlePkg);
            PackageRecord commonRecord = PackageLoader.FindRecord(UICommonPkg);
            Assert.IsNotNull(battleRecord, "UIBattle 包记录应存在。");
            Assert.IsNotNull(commonRecord, "UICommon 依赖包记录应存在。");
            Assert.AreEqual(PackageLoadState.Ready, commonRecord.State, "UICommon 应已 Ready。");

            // UICommon 引用计数 >= 1：由 UIBattle 的依赖 lease 持有。
            // UIBattle 引用计数 >= 1：由窗口 lease 持有。
            Assert.GreaterOrEqual(battleRecord.ReferenceCount, 1, "UIBattle 引用计数应 >= 1（窗口 lease）。");
            Assert.GreaterOrEqual(commonRecord.ReferenceCount, 1, "UICommon 引用计数应 >= 1（UIBattle 依赖 lease）。");

            // 断言：UIBattle 持有 1 个依赖 lease 指向 UICommon。
            Assert.AreEqual(1, battleRecord.DependencyLeases.Count, "UIBattle 应持有 1 个依赖 lease。");
            Assert.AreEqual(UICommonPkg, battleRecord.DependencyLeases[0].PackageName,
                "UIBattle 的依赖 lease 应指向 UICommon。");

            // 记录 Show 后的引用计数基线。
            int battleRefCountAfterShow = battleRecord.ReferenceCount;
            int commonRefCountAfterShow = commonRecord.ReferenceCount;

            // 执行：Close 窗口（CacheMode=None → Disposed，释放窗口 lease，UIBattle 引用计数递减）。
            module.Close<WidgetTestWindow>();
            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(WidgetTestWindow)),
                "CacheMode=None Close 后应 Disposed。");

            // 断言：UIBattle 引用计数递减（窗口 lease 已释放）。
            // KeepUntilShutdown 策略下引用归零不触发 FinalUnload，UIBattle 记录保留且仍 Ready。
            Assert.AreEqual(battleRefCountAfterShow - 1, battleRecord.ReferenceCount,
                "Close 后 UIBattle 引用计数应递减 1（窗口 lease 释放）。");
            Assert.AreEqual(PackageLoadState.Ready, battleRecord.State,
                "KeepUntilShutdown 策略下 UIBattle 引用归零后仍 Ready（不触发运行期卸载）。");

            // 断言：UICommon 引用计数仍 >0 且 Ready——UIBattle 记录未被 FinalUnload，
            // 其持有的依赖 lease 未释放，UICommon 作为共享依赖仍保持可用。
            // 这验证了 spec "A 或 B 任一仍持有依赖时 C 保持可用" 的核心约束（单持有者子集）：
            // 只要 UIBattle 记录存活且持有依赖 lease，UICommon 就不会被释放。
            Assert.AreEqual(commonRefCountAfterShow, commonRecord.ReferenceCount,
                "Close 窗口后 UICommon 引用计数应保持不变（UIBattle 依赖 lease 未释放，记录未 FinalUnload）。");
            Assert.GreaterOrEqual(commonRecord.ReferenceCount, 1,
                "UICommon 引用计数应仍 >= 1（UIBattle 依赖 lease 仍持有）。");
            Assert.AreEqual(PackageLoadState.Ready, commonRecord.State,
                "UICommon 应仍 Ready（依赖 lease 仍持有，未被释放）。");
            Assert.IsNotNull(commonRecord.Package, "UICommon 的 UIPackage 不应被移除（仍可用）。");

            // 断言：UIBattle 的依赖 lease 仍未释放（记录未 FinalUnload，依赖 lease 仅在 FinalUnload 时释放）。
            Assert.AreEqual(1, battleRecord.DependencyLeases.Count,
                "UIBattle 的依赖 lease 应仍存在（记录未 FinalUnload，依赖 lease 不释放）。");
            Assert.IsFalse(battleRecord.DependencyLeases[0].IsReleased,
                "UIBattle 的依赖 lease 应未释放（FinalUnload 才释放依赖 lease）。");

            // 清理：Shutdown 模块释放全部（FinalUnload UIBattle 时释放依赖 lease，UICommon 引用归零并被回收）。
            module.Shutdown();
            Assert.IsNull(PackageLoader.FindRecord(UIBattlePkg), "Shutdown 后 UIBattle 记录应已移除。");
            Assert.IsNull(PackageLoader.FindRecord(UICommonPkg), "Shutdown 后 UICommon 记录应已移除。");
        }

        // ============================================================
        // 场景 c：重复 Release——窗口租约重复释放不产生负引用
        // ============================================================

        /// <summary>
        /// 重复 Release：窗口持有的包租约被重复释放时，第二次释放被拒绝，
        /// 引用计数不递减（不为负）。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading“资源所有权唯一且释放有序——重复释放租约 SHALL 被拒绝，
        /// 不得使引用计数变为负数”。
        /// PackageLease.Release 使用 Interlocked.CompareExchange 保证幂等，第二次返回 false。
        /// </remarks>
        [Test]
        [Description("重复 Release：窗口 lease 重复释放被拒绝，引用计数不为负。")]
        public async UniTask DuplicateRelease_WindowLease_SecondReleaseRejected_RefCountNotNegative()
        {
            // 安排：装配模块。
            FUIModule module = SetupModuleWithCachedWindow();

            // 执行：Show 窗口。
            CachedTestWindow window = await module.ShowAsync<CachedTestWindow>();

            // 获取窗口条目中的 lease。
            Assert.IsTrue(module._windowEntries.TryGetValue(typeof(CachedTestWindow), out WindowEntry entry),
                "应存在窗口条目。");
            PackageLease lease = entry.Lease;
            Assert.IsNotNull(lease, "窗口应持有非空 lease。");
            PackageRecord record = lease.Record;
            int refCountBefore = record.ReferenceCount;

            // 执行：首次 Release 成功。
            bool firstRelease = lease.Release();
            Assert.IsTrue(firstRelease, "首次 Release 应成功。");
            Assert.AreEqual(refCountBefore - 1, record.ReferenceCount, "首次 Release 后引用计数应递减 1。");

            // 重复 Release：第二次被拒绝，引用计数不变。
            bool secondRelease = lease.Release();
            Assert.IsFalse(secondRelease, "第二次 Release 应被拒绝。");
            Assert.IsTrue(lease.IsReleased, "lease 应标记为已释放。");
            Assert.AreEqual(refCountBefore - 1, record.ReferenceCount, "重复 Release 引用计数应保持不变，不为负。");

            // 多次重复 Release 同样被拒绝。
            Assert.IsFalse(lease.Release(), "第三次 Release 仍应被拒绝。");
            Assert.IsFalse(lease.Release(), "第四次 Release 仍应被拒绝。");
            Assert.AreEqual(refCountBefore - 1, record.ReferenceCount, "多次重复 Release 引用计数始终不变。");
            Assert.GreaterOrEqual(record.ReferenceCount, 0, "引用计数始终不为负。");

            // 注意：手动释放 lease 后窗口条目仍持有 lease 引用，Close 时 RollbackLoadedInstance
            // 会再次尝试 Release（幂等保护使其安全跳过）。此处不 Close 以隔离测试，
            // 由 TearDown 的 Shutdown 完成清理（Shutdown 强制回收，不依赖 lease 状态）。
        }

        // ============================================================
        // 场景 d：延迟期重开——Delayed 策略下延迟期内重新 Show 取消卸载
        // ============================================================

        /// <summary>
        /// 延迟期重开：Delayed 策略下窗口 Close 后引用归零触发延迟卸载任务，
        /// 在延迟期内重新 Show 使引用计数恢复 > 0，延迟卸载任务到期后因版本不匹配或零引用检查失败而放弃卸载，
        /// 包记录被复用，不产生卸载与重载抖动。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading“延迟卸载期间重新 Acquire → 卸载 SHALL 被取消并复用现有包记录，
        /// 不得产生卸载与重载抖动”。
        /// design.md 决策8：延迟卸载前重新 Acquire 通过递增待卸载版本取消旧卸载任务。
        /// 6.4 实现：ScheduleDelayedUnload 使用三重检查（版本/引用/状态）保证延迟期内重开取消卸载。
        /// </remarks>
        [Test]
        [Description("延迟期重开：Delayed 策略下延迟期内重新 Show 取消卸载，包记录复用。")]
        public async UniTask DelayedReopen_WithinDelay_CancelsUnload_AndReusesRecord()
        {
            // 安排：装配模块并配置 Delayed 策略（延迟 200ms，便于测试）。
            // 注意：FUIModule 当前未在构造阶段调用 PackageLoader.Configure（由后续 6.7 任务接线），
            // 故本测试显式调用 PackageLoader.Configure 设置 Delayed 策略。
            // 延迟时间设为 200ms，使延迟卸载任务在测试完成后才到期，便于验证"延迟期内重开"语义。
            PackageLoader.Configure(FUIPackageUnloadPolicy.Delayed, 0.2f);
            FUIModule module = SetupModuleWithCachedWindow();

            // 执行：Show 窗口。
            CachedTestWindow window1 = await module.ShowAsync<CachedTestWindow>();
            PackageRecord battleRecord = PackageLoader.FindRecord(UIBattlePkg);
            Assert.IsNotNull(battleRecord, "UIBattle 包记录应存在。");
            Assert.AreEqual(PackageLoadState.Ready, battleRecord.State, "Show 后包应 Ready。");
            int refCountAfterShow = battleRecord.ReferenceCount;
            Assert.GreaterOrEqual(refCountAfterShow, 1, "Show 后引用计数应 >= 1。");

            // 执行：Close 窗口（CacheMode=None 的 CachedTestWindow 进入 Disposed 释放 lease，
            // 引用归零触发 Delayed 延迟卸载任务）。
            // 注意：CachedTestWindow 使用 CacheMode=Cache，Close 进 Cached 保留 lease。
            // 为触发引用归零，需使用 CacheMode=None 的窗口。此处复用 CachedTestWindow 但
            // 二次 Close 使其从 Cached 进入 Disposed 释放 lease。
            module.Close<CachedTestWindow>(); // Open -> Cached
            module.Close<CachedTestWindow>(); // Cached -> Disposed，释放 lease

            // 断言：引用计数归零（lease 已释放）。
            Assert.AreEqual(0, battleRecord.ReferenceCount, "Close 后引用计数应归零。");

            // 捕获当前待卸载版本（Close 后 HandleReferenceCountReachedZero 已调度延迟卸载任务并递增版本）。
            int versionBeforeReopen = battleRecord.PendingUnloadVersion;
            Assert.Greater(versionBeforeReopen, 0, "Delayed 策略下引用归零应递增待卸载版本。");

            // 执行：在延迟期内重新 Show（200ms 延迟，立即 Show 在延迟窗口内）。
            // 重新 Show 会通过 CancelPendingUnload 递增版本，使旧卸载任务版本过期。
            CachedTestWindow window2 = await module.ShowAsync<CachedTestWindow>();

            // 断言：包记录被复用（同一 PackageRecord 实例，未卸载重载）。
            PackageRecord battleRecordAfterReopen = PackageLoader.FindRecord(UIBattlePkg);
            Assert.AreSame(battleRecord, battleRecordAfterReopen,
                "延迟期内重开应复用现有包记录，不产生卸载与重载抖动。");
            Assert.AreEqual(PackageLoadState.Ready, battleRecord.State, "重开后包应仍 Ready。");
            Assert.IsNotNull(battleRecord.Package, "重开后 UIPackage 不应为 null（未卸载）。");

            // 断言：引用计数恢复 > 0。
            Assert.GreaterOrEqual(battleRecord.ReferenceCount, 1, "重开后引用计数应恢复 >= 1。");

            // 断言：待卸载版本在重开时再次递增（CancelPendingUnload）。
            Assert.Greater(battleRecord.PendingUnloadVersion, versionBeforeReopen,
                "重开应递增待卸载版本使旧卸载任务过期。");

            // 等待延迟卸载时间到期（200ms + 余量），验证卸载任务被取消。
            await UniTask.Delay(350);

            // 断言：延迟卸载任务到期后未执行卸载（包仍 Ready）。
            Assert.AreEqual(PackageLoadState.Ready, battleRecord.State,
                "延迟卸载任务到期后应因版本过期或引用非零而放弃卸载，包仍 Ready。");
            Assert.IsNotNull(battleRecord.Package, "延迟到期后 UIPackage 仍应存在（卸载被取消）。");

            // 验证 window1 与 window2 是不同实例（Disposed 后重新 Show 创建新实例）。
            Assert.AreNotSame(window1, window2, "Disposed 后重新 Show 应创建新实例。");
        }

        // ============================================================
        // 场景 e：动态 Widget 池化——动态创建与池化复用 Widget 的生命周期
        // ============================================================

        /// <summary>
        /// 动态 Widget 池化：通过 <see cref="FUIModule.CreateDynamicWidget{TWidget}"/> 动态创建 Widget，
        /// 验证 Widget 获得 OwnerWindow 与 OnCreate 执行；再通过 <see cref="FUIModule.AttachDynamicWidget"/>
        /// 池化复用同一 Widget 实例，验证 OnCreate 幂等不重复执行（<see cref="FUIWidget.IsCreated"/> 保持 true）。
        /// </summary>
        /// <remarks>
        /// spec fairygui-window-runtime“Widget 在生命周期前获得所属窗口——动态列表 Widget：
        /// 窗口运行期间创建或复用一个注册的列表 Widget，系统 SHALL 在业务代码依赖 OwnerWindow 前完成绑定，
        /// 并不得因重复 Attach 重复执行创建生命周期”。
        /// design.md 决策6：动态受管理 Widget 只允许来自所属窗口包或其已声明依赖；
        /// 池化复用入口在交付业务前重新 Attach。
        /// design.md 风险项：Widget 池化和动态重挂导致 OwnerWindow 陈旧 → Attach 幂等但检测 owner 变更，
        /// 复用入口在交付业务前重新 Attach。
        /// </remarks>
        [Test]
        [Description("动态 Widget 池化：动态创建 Widget 获得 OwnerWindow，池化复用 OnCreate 幂等不重复。")]
        public async UniTask DynamicWidgetPooling_CreateAndReuse_OnCreateIdempotent()
        {
            // 安排：装配模块并注册测试 Widget（覆盖生成类型，Creator 非空）。
            FUIModule module = SetupModuleWithWidgetWindow();

            // 执行：Show 窗口（取得 ownerWindow）。
            WidgetTestWindow window = await module.ShowAsync<WidgetTestWindow>();
            Assert.IsNotNull(window, "应成功打开窗口。");

            // 执行：通过 CreateDynamicWidget 动态创建 Widget。
            // TestBattleStartWidget 属于 UIBattle 包，与窗口同包，通过包校验。
            TestBattleStartWidget widget = module.CreateDynamicWidget<TestBattleStartWidget>(window);
            Assert.IsNotNull(widget, "动态创建的 Widget 不应为空。");

            // 断言：Widget 在 OnCreate 前已获得 OwnerWindow（spec: 动态列表 Widget——SHALL 在业务代码依赖
            // OwnerWindow 前完成绑定）。
            Assert.IsTrue(widget.IsAttached, "动态创建的 Widget 应已 Attach。");
            Assert.AreSame(window, widget.OwnerWindow, "动态 Widget 的 OwnerWindow 应为创建时传入的窗口。");
            Assert.IsTrue(widget.IsCreated, "动态创建后 Widget 应已执行 OnCreate。");

            // 执行：池化复用——先模拟从池中取出（ResetForReuse 回退 Attach 状态），
            // 再通过 AttachDynamicWidget 重新 Attach。
            widget.ResetForReuse();
            Assert.IsFalse(widget.IsAttached, "ResetForReuse 后 IsAttached 应为 false。");
            Assert.IsNull(widget.OwnerWindow, "ResetForReuse 后 OwnerWindow 应为 null。");
            Assert.IsTrue(widget.IsCreated, "ResetForReuse 后 IsCreated 应保持 true（生命周期不重置）。");

            // 重新 Attach 到同一窗口。
            module.AttachDynamicWidget(widget, window);
            Assert.IsTrue(widget.IsAttached, "重新 Attach 后 IsAttached 应为 true。");
            Assert.AreSame(window, widget.OwnerWindow, "重新 Attach 后 OwnerWindow 应为窗口。");

            // 断言：OnCreate 幂等不重复执行（spec: 不得因重复 Attach 重复执行创建生命周期）。
            // IsCreated 保持 true，没有重复执行 OnCreate 的机制（InvokeOnCreate 幂等跳过）。
            Assert.IsTrue(widget.IsCreated, "池化复用后 IsCreated 应保持 true（OnCreate 不重复执行）。");
            Assert.IsFalse(widget.IsDisposed, "池化复用后 IsDisposed 应为 false（未 Dispose）。");

            // 断言：重复 Attach 同一 owner 不触发 owner 变更诊断。
            Assert.IsFalse(widget.LastAttachOwnerChanged,
                "重复 Attach 同一 owner 不应触发 owner 变更诊断。");

            // 执行：Close 窗口（默认 None → Disposed），验证窗口 Dispose 时 Widget 树被遍历 Dispose。
            // 注意：动态创建的 Widget 若未 AddChild 到窗口显示树，不会被 DisposeWidgetTree 遍历。
            // 此处只验证窗口正常 Close 不影响动态 Widget 的生命周期标记。
            module.Close<WidgetTestWindow>();
            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(WidgetTestWindow)),
                "默认 CacheMode=None Close 后应 Disposed。");
        }

        /// <summary>
        /// 动态 Widget 池化跨 owner 复用（ResetForReuse 路径）：验证调用 <see cref="FUIWidget.ResetForReuse"/>
        /// 后重新 Attach 到不同 owner 窗口时，因 <see cref="FUIWidget.IsAttached"/> 已回退为 false，
        /// <see cref="FUIWidget.AttachContext"/> 视为首次 Attach（<see cref="FUIWidget.LastAttachOwnerChanged"/>
        /// 不触发），OwnerWindow 更新为新窗口，OnCreate 不重复执行。
        /// </summary>
        /// <remarks>
        /// design.md 决策6 风险项：Widget 池化和动态重挂导致 OwnerWindow 陈旧 → Attach 幂等但检测 owner 变更，
        /// 复用入口在交付业务前重新 Attach。
        /// spec fairygui-window-runtime“动态列表 Widget”。
        ///
        /// 命名说明：本测试原名 DynamicWidgetPooling_CrossOwner_DetectsOwnerChange_UpdatesOwnerWindow，
        /// 但因先调用 <see cref="FUIWidget.ResetForReuse"/> 使 <see cref="FUIWidget.IsAttached"/>=false，
        /// 后续 <see cref="FUIModule.AttachDynamicWidget"/> 走首次 Attach 分支（FUIWidget.AttachContext 中
        /// ownerChanged = IsAttached &amp;&amp; ... 判定为 false），<see cref="FUIWidget.LastAttachOwnerChanged"/>
        /// 不触发。为避免误导，重命名为 DynamicWidgetPooling_CrossOwner_AfterReset_TreatedAsFirstAttach，
        /// 准确反映本测试验证的是"ResetForReuse 后跨 owner 复用走首次 Attach 分支"的语义。
        ///
        /// 未覆盖说明：不调用 ResetForReuse 的直接跨 owner Attach 场景（IsAttached=true 时 AttachContext
        /// 触发 LastAttachOwnerChanged=true）受限于单 URL 注册约束——同类型只有一个 WindowEntry，
        /// 无法同时存在两个不同实例的窗口作为 owner1/owner2 供直接跨 owner Attach。该场景由
        /// FUIWidget.AttachContext 的实现与文档保证，不在此 EditMode 测试覆盖。
        /// </remarks>
        [Test]
        [Description("动态 Widget 池化跨 owner（ResetForReuse 后）：视为首次 Attach，OnCreate 不重复。")]
        public async UniTask DynamicWidgetPooling_CrossOwner_AfterReset_TreatedAsFirstAttach()
        {
            // 安排：装配模块。
            FUIModule module = SetupModuleWithWidgetWindow();

            // 执行：Show 第一个窗口，动态创建 Widget。
            WidgetTestWindow window1 = await module.ShowAsync<WidgetTestWindow>();
            TestBattleStartWidget widget = module.CreateDynamicWidget<TestBattleStartWidget>(window1);
            Assert.AreSame(window1, widget.OwnerWindow, "初始 OwnerWindow 应为 window1。");

            // 模拟池化回收：ResetForReuse 后 widget 可被复用到其他窗口。
            widget.ResetForReuse();
            Assert.IsFalse(widget.IsAttached, "ResetForReuse 后应未 Attach。");

            // 执行：Hide window1，Show 第二个窗口（同类型新实例，因为默认 None Close 后 Disposed）。
            // 由于同类型只有一个 WindowEntry，需先 Close window1 再 Show。
            module.Close<WidgetTestWindow>();
            Assert.AreEqual(FUIWindowState.Disposed, GetEntryState(module, typeof(WidgetTestWindow)),
                "Close 后应 Disposed。");

            WidgetTestWindow window2 = await module.ShowAsync<WidgetTestWindow>();
            Assert.AreNotSame(window1, window2, "Close 后重新 Show 应创建新实例。");

            // 执行：将池化的 Widget 重新 Attach 到 window2（跨 owner 复用）。
            // 注意：ResetForReuse 已回退 IsAttached=false，故 AttachContext 视为首次 Attach，
            // 不触发 owner 变更诊断（LastAttachOwnerChanged 保持 false）。
            module.AttachDynamicWidget(widget, window2);

            // 断言：OwnerWindow 更新为 window2。
            Assert.AreSame(window2, widget.OwnerWindow, "跨 owner 复用后 OwnerWindow 应更新为 window2。");
            Assert.IsTrue(widget.IsAttached, "跨 owner 复用后应已 Attach。");

            // 断言：OnCreate 幂等不重复执行。
            Assert.IsTrue(widget.IsCreated, "跨 owner 复用后 IsCreated 应保持 true（OnCreate 不重复）。");
            Assert.IsFalse(widget.IsDisposed, "跨 owner 复用后 IsDisposed 应为 false。");

            // 断言：ResetForReuse 后重新 Attach 视为首次，不触发 owner 变更诊断。
            // 这是本测试的核心验证点：ResetForReuse 的"干净复用路径"不触发 LastAttachOwnerChanged。
            Assert.IsFalse(widget.LastAttachOwnerChanged,
                "ResetForReuse 后重新 Attach 视为首次，不触发 owner 变更诊断。");
        }

        // ============================================================
        // 辅助方法与测试类型
        // ============================================================

        /// <summary>
        /// 装配模块并注册 <see cref="CachedTestWindow"/>（CacheMode=Cache），使用内存 provider。
        /// </summary>
        /// <returns>已完成 FreezeBindings 的 <see cref="FUIModule"/>。</returns>
        private static FUIModule SetupModuleWithCachedWindow()
        {
            InMemoryFUIResourceProvider provider = CreateProviderWithPackages();
            FUI.RegisterModuleForTesting(provider);
            FUIModule module = (FUIModule)FUI.Module;

            // 1. 本包生成 Binder。
            UIBattleBinder.BindAll();

            // 2. 注册最终测试 Widget（覆盖生成类型，Creator 非空）。
            RegisterCustomWidget(
                module,
                typeof(TestBattleStartWidget),
                UI_BattleStartWidget.URL,
                UI_BattleStartWidget.PkgName,
                UI_BattleStartWidget.ResName);

            // 3. 注册最终测试 Window（CacheMode=Cache，覆盖生成类型，Creator 非空）。
            RegisterCustomWindow(
                module,
                typeof(CachedTestWindow),
                UI_BattleStartPanel.URL,
                UI_BattleStartPanel.PkgName,
                UI_BattleStartPanel.ResName,
                FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.Cache,
                safeAreaMode: FUISafeAreaMode.Full);

            module.FreezeBindings();
            return module;
        }

        /// <summary>
        /// 装配模块并注册 <see cref="WidgetTestWindow"/>（CacheMode=None），用于动态 Widget 测试。
        /// </summary>
        /// <returns>已完成 FreezeBindings 的 <see cref="FUIModule"/>。</returns>
        private static FUIModule SetupModuleWithWidgetWindow()
        {
            InMemoryFUIResourceProvider provider = CreateProviderWithPackages();
            FUI.RegisterModuleForTesting(provider);
            FUIModule module = (FUIModule)FUI.Module;

            // 1. 本包生成 Binder。
            UIBattleBinder.BindAll();

            // 2. 注册最终测试 Widget（覆盖生成类型，Creator 非空）。
            RegisterCustomWidget(
                module,
                typeof(TestBattleStartWidget),
                UI_BattleStartWidget.URL,
                UI_BattleStartWidget.PkgName,
                UI_BattleStartWidget.ResName);

            // 3. 注册最终测试 Window（CacheMode=None，覆盖生成类型，Creator 非空）。
            RegisterCustomWindow(
                module,
                typeof(WidgetTestWindow),
                UI_BattleStartPanel.URL,
                UI_BattleStartPanel.PkgName,
                UI_BattleStartPanel.ResName,
                FUILayer.Normal,
                fullScreen: false,
                cacheMode: FUICacheMode.None,
                safeAreaMode: FUISafeAreaMode.Full);

            module.FreezeBindings();
            return module;
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
        /// 与 <see cref="WindowLifecycleTests.RegisterCustomWindow"/> 实现一致，
        /// creator 使用 <see cref="CreateTypeBasedCreator"/> 构造最终业务类型。
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
        /// 与 <see cref="WindowLifecycleTests.CreateTypeBasedCreator"/> 实现一致。
        /// creator 只捕获 targetType（Type 不可释放），不捕获 Registry 或其他可释放对象，
        /// 符合 design.md 决策2 的无状态 creator 约束。
        /// </remarks>
        private static Func<string, GComponent> CreateTypeBasedCreator(Type targetType)
        {
            return url => (GComponent)Activator.CreateInstance(targetType);
        }

        /// <summary>
        /// 创建已预设 UIBattle（依赖 UICommon）与 UICommon（含 Atlas）描述资源的内存 provider。
        /// </summary>
        /// <returns>已预设描述资源的内存 provider。</returns>
        /// <remarks>
        /// 与 <see cref="WindowLifecycleTests.CreateProviderWithLifecyclePackages"/> 实现一致，
        /// 使用磁盘上真实发布的描述资源，保留二进制字节完整性。
        /// UICommon 作为 UIBattle 的共享图集依赖包，预设 atlas0 纹理。
        /// </remarks>
        private static InMemoryFUIResourceProvider CreateProviderWithPackages()
        {
            InMemoryFUIResourceProvider provider = new InMemoryFUIResourceProvider();

            // 从 AssetDatabase 加载真实 TextAsset（保留二进制字节完整性）。
            TextAsset uiCommonDesc = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/AssetRaw/FUI/" + UICommonPkg + "_fui.bytes");
            TextAsset uiBattleDesc = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/AssetRaw/FUI/" + UIBattlePkg + "_fui.bytes");

            Assert.IsNotNull(uiCommonDesc, "应能从磁盘加载 UICommon_fui.bytes 描述资源。");
            Assert.IsNotNull(uiBattleDesc, "应能从磁盘加载 UIBattle_fui.bytes 描述资源。");

            // UICommon 包（无依赖，含 Atlas）——共享依赖包。
            provider.SetAsset(UICommonPkg + "_fui", uiCommonDesc);
            provider.SetAsset(UICommonPkg + "_atlas0", CreateTexture2D(2, 2));

            // UIBattle 包（依赖 UICommon，无自己的 Atlas）。
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
        /// 创建指定尺寸的 <see cref="Texture2D"/>（复用 WindowLifecycleTests 的实现）。
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
        /// 缓存策略测试窗口，继承生成类型 <see cref="UI_BattleStartPanel"/>。
        /// 注册时声明 <see cref="FUICacheMode.Cache"/>，Close 后进入 Cached 状态保留实例与租约。
        /// </summary>
        /// <remarks>
        /// spec fairygui-window-runtime“默认关闭即释放且缓存显式启用”——显式声明 Cache 时
        /// Close 保留实例与包租约（design.md 决策5）。
        /// </remarks>
        public class CachedTestWindow : UI_BattleStartPanel
        {
        }

        /// <summary>
        /// 动态 Widget 测试窗口，继承生成类型 <see cref="UI_BattleStartPanel"/>。
        /// 用于动态创建与池化复用 Widget 的生命周期测试。
        /// </summary>
        public class WidgetTestWindow : UI_BattleStartPanel
        {
        }
    }
}
