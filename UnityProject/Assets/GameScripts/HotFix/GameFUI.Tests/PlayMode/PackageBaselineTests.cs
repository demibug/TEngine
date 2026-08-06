using System;
using System.Collections.Generic;
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
    /// 任务 7.5 PlayMode 验收：对比操作前后 PackageRecord、YooAsset handle 和内存基线，
    /// 验证 KeepUntilShutdown 与 Delayed 策略，以及并发/失败/Shutdown 后无负引用和过期回调。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - design.md 决策8（包加载采用异步预载、同步解析；KeepUntilShutdown 与 Delayed 策略）；
    /// - design.md 决策9（包失败回滚使用本次操作账本；最终卸载前置检查与固定顺序）；
    /// - spec fairygui-package-loading：
    ///   * Requirement: 资源所有权唯一且释放有序——handle 由包记录统一持有，释放顺序固定；
    ///   * Requirement: 失败和取消执行原子回滚——本次新增引用与资源回滚，不影响共享持有者；
    ///   * Requirement: 包租约控制缓存和卸载——延迟期内重新 Acquire 取消卸载；
    ///   * Requirement: 模块 Shutdown 回收全部包资源——Shutdown 后回到启动前基线。
    ///
    /// 测试装配：复用 <see cref="PlayModeTestHarness"/> 的公开构建块完成内存 provider 构造、
    /// UIBattle Binder 调用、Registry 冻结与全局清理。对需要自定义卸载策略或可控失败的场景，
    /// 本测试文件内提供 <see cref="SetupWithWindow"/> 自行装配，复用 harness 的公开构建块，
    /// 不修改 harness（任务约束：禁止修改 PlayModeTestHarness.cs）。
    ///
    /// <para>
    /// <b>关键约束：PackageLoader.Configure 由测试显式调用</b>。
    /// 当前 <see cref="FUIModule"/> 尚未在装配阶段调用 <see cref="PackageLoader.Configure"/>
    /// （由同批次 6.7 任务负责接线），故 <see cref="PlayModeTestHarness.SetupWithUnloadPolicy"/>
    /// 创建的 <see cref="FUIOptions"/> 不会实际改变 <see cref="PackageLoader.UnloadPolicy"/>。
    /// 本测试通过 InternalsVisibleTo 直接调用 <see cref="PackageLoader.Configure"/> 验证 Delayed 策略，
    /// 这是 7.5 任务在 6.7 接线完成前的临时验证路径；6.7 完成后可改用 harness 的
    /// <see cref="PlayModeTestHarness.SetupWithUnloadPolicy"/> 间接装配。
    /// </para>
    ///
    /// <para>
    /// <b>策略重置</b>：每个测试的 <see cref="SetUp"/> 与 <see cref="TearDown"/> 调用
    /// <see cref="ResetUnloadPolicy"/> 将 <see cref="PackageLoader.UnloadPolicy"/> 恢复为
    /// <see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>，避免 Delayed 策略跨测试残留。
    /// <see cref="PlayModeTestHarness.Cleanup"/> 不重置策略（harness 不修改运行时代码），
    /// 故由本测试自行负责重置。
    /// </para>
    ///
    /// 复用证据：
    /// - 复用 <see cref="PlayModeTestHarness.Cleanup"/>（7.1 产出，全局清理）；
    /// - 复用 <see cref="PlayModeTestHarness.CreateProviderWithUIBattleAndUICommon"/>（7.1 产出，内存 provider 构造）；
    /// - 复用 <see cref="PlayModeTestHarness.CreateTypeBasedCreator"/>（7.1 产出，无状态 creator）；
    /// - 复用 <see cref="PlayModeTestHarness.EnsureGRootInitialized"/>（7.1 产出，GRoot 初始化）；
    /// - 复用 <see cref="TestBattleStartPanel"/> / <see cref="TestBattleStartWidget"/>（3.6 产出，最终测试业务类型）；
    /// - 复用 <see cref="TestFUIOwner.OwnerType"/>（3.6 产出，owner 类型标识）；
    /// - 复用 <see cref="FUI.RegisterModuleForTesting"/>（5.1 产出的 internal 测试入口）；
    /// - 复用 <see cref="FUIModule.BindingRegistry"/> / <see cref="FUIModule._windowEntries"/> 等 internal 访问器；
    /// - 复用 <see cref="PackageLoader.FindRecord"/> / <see cref="PackageLoader.Configure"/> /
    ///   <see cref="PackageLoader.UnloadPolicy"/> / <see cref="PackageLoader.UnloadDelayMs"/>
    ///   等 internal 静态成员（通过 InternalsVisibleTo("GameFUI.Tests") 暴露）；
    /// - 复用 <see cref="InMemoryFUIResourceProvider.MarkLoadFailure"/>（4.3 产出的可控失败入口）。
    ///
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，只通过公开/internal API 访问被测对象。
    /// 不依赖 GameLogic/GamePlay/GameBattle，不创建或修改 BattleModule。
    /// </remarks>
    [TestFixture]
    public class PackageBaselineTests
    {
        // ============================================================
        // 常量
        // ============================================================

        /// <summary>
        /// Delayed 策略测试使用的延迟卸载时间（毫秒），足够短使测试快速完成，足够长使重开窗口能在延迟期内执行。
        /// </summary>
        private const int TestUnloadDelayMs = 200;

        /// <summary>
        /// Delayed 策略测试使用的延迟卸载时间（秒），传给 <see cref="PackageLoader.Configure"/>。
        /// </summary>
        private const float TestUnloadDelaySeconds = 0.2f;

        /// <summary>
        /// 等待延迟卸载完成的安全余量（毫秒），确保延迟任务在 UniTask 调度器上完成执行。
        /// </summary>
        private const int DelaySafetyMarginMs = 150;

        // ============================================================
        // SetUp / TearDown
        // ============================================================

        /// <summary>
        /// 每个测试前的状态基线重置：清空全局状态、初始化 GRoot、恢复默认卸载策略。
        /// </summary>
        /// <remarks>
        /// <see cref="ResetUnloadPolicy"/> 将 <see cref="PackageLoader.UnloadPolicy"/> 恢复为
        /// <see cref="FUIPackageUnloadPolicy.KeepUntilShutdown"/>，避免上一测试设置的 Delayed 策略残留。
        /// <see cref="PlayModeTestHarness.Cleanup"/> 幂等清空 FairyGUI 全局包注册表、PackageLoader 静态注册表、
        /// FUI 门面静态缓存与活动 Registry 静态引用。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            PlayModeTestHarness.Cleanup();
            PlayModeTestHarness.EnsureGRootInitialized();
            ResetUnloadPolicy();
        }

        /// <summary>
        /// 每个测试后的状态基线重置，与 <see cref="SetUp"/> 对称，确保即使测试中途失败
        /// 也不会残留全局状态或 Delayed 策略污染后续测试。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            PlayModeTestHarness.Cleanup();
            ResetUnloadPolicy();
        }

        // ============================================================
        // 场景 a：操作前后 PackageRecord 状态对比
        // ============================================================

        /// <summary>
        /// 操作前后 PackageRecord 状态对比：Show 前 UIBattle/UICommon 包不存在记录，
        /// Show 后两者处于 Ready 且引用计数为 1，Close 后引用计数归零但包仍 Ready（KeepUntilShutdown），
        /// Shutdown 后记录进入 Disposed 终态且从注册表移除。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 资源所有权唯一且释放有序——
        /// 由 YooAsset 加载的描述与外部资源 SHALL 由包记录统一持有。
        /// spec / Requirement: 模块 Shutdown 回收全部包资源——Shutdown 完成后回到启动前基线。
        /// design.md 决策8：每个包对应一个 PackageRecord，包含状态、引用计数、handle 表。
        /// design.md 决策9：最终卸载顺序固定。
        /// </remarks>
        [Test]
        [Description("PackageRecord 状态对比：Absent -> Ready -> Ready(零引用) -> Disposed。")]
        public async UniTask PackageRecord_StateTransitions_ShowCloseShutdown()
        {
            // 安排：装配模块（默认 KeepUntilShutdown 策略）。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);

            // 基线：Show 前包记录不存在。
            Assert.IsNull(PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg),
                "Show 前 UIBattle 包记录不应存在。");
            Assert.IsNull(PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg),
                "Show 前 UICommon 包记录不应存在。");
            Assert.AreEqual(0, UIPackage.GetPackages().Count,
                "Show 前 FairyGUI 全局包注册表应为空。");

            // 执行：Show 窗口。
            BaselineTrackingWindow window = await module.ShowAsync<BaselineTrackingWindow>();

            // 断言：Show 后两个包均处于 Ready，引用计数 >= 1。
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            PackageRecord uiCommonRecord = PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg);
            Assert.IsNotNull(uiBattleRecord, "Show 后 UIBattle 包记录应存在。");
            Assert.IsNotNull(uiCommonRecord, "Show 后 UICommon 包记录应存在。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "Show 后 UIBattle 应处于 Ready。");
            Assert.AreEqual(PackageLoadState.Ready, uiCommonRecord.State,
                "Show 后 UICommon 应处于 Ready。");
            Assert.GreaterOrEqual(uiBattleRecord.ReferenceCount, 1,
                "Show 后 UIBattle 引用计数应 >= 1（窗口 lease）。");
            Assert.GreaterOrEqual(uiCommonRecord.ReferenceCount, 1,
                "Show 后 UICommon 引用计数应 >= 1（UIBattle 依赖 lease）。");
            Assert.AreEqual(2, UIPackage.GetPackages().Count,
                "Show 后 FairyGUI 全局包注册表应有 2 个包（UIBattle + UICommon）。");

            // 执行：Close 窗口（默认 CacheMode=None，最终释放 lease）。
            module.Close<BaselineTrackingWindow>();

            // 断言：Close 后引用计数归零，但包仍处于 Ready（KeepUntilShutdown 策略不运行期卸载）。
            Assert.AreEqual(0, uiBattleRecord.ReferenceCount,
                "Close 后 UIBattle 引用计数应归零。");
            Assert.AreEqual(0, uiCommonRecord.ReferenceCount,
                "Close 后 UICommon 引用计数应归零。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "KeepUntilShutdown 策略下 Close 后 UIBattle 仍应处于 Ready（保留到 Shutdown）。");
            Assert.AreEqual(PackageLoadState.Ready, uiCommonRecord.State,
                "KeepUntilShutdown 策略下 Close 后 UICommon 仍应处于 Ready。");
            Assert.AreEqual(2, UIPackage.GetPackages().Count,
                "KeepUntilShutdown 策略下 Close 后全局包注册表仍应有 2 个包。");

            // 执行：Shutdown。
            module.Shutdown();

            // 断言：Shutdown 后记录进入 Disposed 终态，从注册表移除，全局包表清空。
            Assert.AreEqual(PackageLoadState.Disposed, uiBattleRecord.State,
                "Shutdown 后 UIBattle 应处于 Disposed。");
            Assert.AreEqual(PackageLoadState.Disposed, uiCommonRecord.State,
                "Shutdown 后 UICommon 应处于 Disposed。");
            Assert.IsNull(PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg),
                "Shutdown 后 UIBattle 包记录应从注册表移除。");
            Assert.IsNull(PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg),
                "Shutdown 后 UICommon 包记录应从注册表移除。");
            Assert.AreEqual(0, UIPackage.GetPackages().Count,
                "Shutdown 后 FairyGUI 全局包注册表应为空。");
        }

        // ============================================================
        // 场景 b：YooAsset handle 数量对比
        // ============================================================

        /// <summary>
        /// YooAsset handle 数量对比：Show 前各包 handle 表为空，Show 后 UIBattle 持有描述 handle，
        /// UICommon 持有描述 handle 与 atlas0 外部资源 handle，Close 后 handle 仍保留（KeepUntilShutdown），
        /// Shutdown 后全部 handle 清空。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 资源所有权唯一且释放有序——
        /// 由 YooAsset 加载的描述、贴图、音频 SHALL 由包记录统一持有其 AssetHandle；
        /// 释放 SHALL 先移除 FairyGUI 包，再释放本包持有的 handle，最后递减依赖租约。
        /// design.md 决策8：外部资源 AssetHandle 表按 location 索引，描述 handle 单独持有。
        /// </remarks>
        [Test]
        [Description("YooAsset handle 数量对比：Show 后持有 handle，Shutdown 后清空。")]
        public async UniTask YooAssetHandleCount_ShowThenShutdown()
        {
            // 安排：装配模块。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);

            // 执行：Show 窗口。
            BaselineTrackingWindow window = await module.ShowAsync<BaselineTrackingWindow>();

            // 断言：UIBattle 持有描述 handle，无外部资源 handle（UIBattle 无独立 atlas）。
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            Assert.IsNotNull(uiBattleRecord.DescHandle, "Show 后 UIBattle 应持有描述 handle。");
            Assert.AreEqual(0, uiBattleRecord.AssetHandles.Count,
                "UIBattle 无独立 atlas，AssetHandles 应为空。");

            // 断言：UICommon 持有描述 handle 与 atlas0 外部资源 handle。
            PackageRecord uiCommonRecord = PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg);
            Assert.IsNotNull(uiCommonRecord.DescHandle, "Show 后 UICommon 应持有描述 handle。");
            Assert.AreEqual(1, uiCommonRecord.AssetHandles.Count,
                "UICommon 应持有 1 个外部资源 handle（atlas0）。");
            Assert.IsTrue(uiCommonRecord.AssetHandles.ContainsKey(PlayModeTestHarness.UICommonPkg + "_atlas0"),
                "UICommon AssetHandles 应包含 atlas0 location。");

            // 记录 handle 总数基线。
            int totalHandlesBeforeClose = TotalHandleCount(uiBattleRecord) + TotalHandleCount(uiCommonRecord);
            Assert.GreaterOrEqual(totalHandlesBeforeClose, 3,
                "Show 后 handle 总数应 >= 3（UIBattle 描述 + UICommon 描述 + UICommon atlas0）。");

            // 执行：Close（KeepUntilShutdown 下 handle 保留）。
            module.Close<BaselineTrackingWindow>();

            // 断言：Close 后 handle 仍保留（KeepUntilShutdown 不释放 handle）。
            Assert.IsNotNull(uiBattleRecord.DescHandle, "Close 后 UIBattle 描述 handle 应保留。");
            Assert.IsNotNull(uiCommonRecord.DescHandle, "Close 后 UICommon 描述 handle 应保留。");
            Assert.AreEqual(1, uiCommonRecord.AssetHandles.Count,
                "Close 后 UICommon atlas0 handle 应保留。");

            // 执行：Shutdown。
            module.Shutdown();

            // 断言：Shutdown 后全部 handle 清空。
            Assert.IsNull(uiBattleRecord.DescHandle, "Shutdown 后 UIBattle 描述 handle 应已清空。");
            Assert.IsNull(uiCommonRecord.DescHandle, "Shutdown 后 UICommon 描述 handle 应已清空。");
            Assert.AreEqual(0, uiBattleRecord.AssetHandles.Count,
                "Shutdown 后 UIBattle AssetHandles 应清空。");
            Assert.AreEqual(0, uiCommonRecord.AssetHandles.Count,
                "Shutdown 后 UICommon AssetHandles 应清空。");
        }

        // ============================================================
        // 场景 c：内存基线对比（UIPackage 全局注册表 + PackageLoader 注册表）
        // ============================================================

        /// <summary>
        /// 内存基线对比：操作前 UIPackage 全局注册表与 PackageLoader 注册表为空，
        /// Show 后包含 2 个包，Close 后仍包含 2 个包（KeepUntilShutdown），
        /// Shutdown 后回到空基线。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 模块 Shutdown 回收全部包资源——
        /// Shutdown 完成后所有模块持有的包、依赖引用和 handle SHALL 回到启动前基线。
        /// design.md 决策8/9。
        /// </remarks>
        [Test]
        [Description("内存基线对比：空 -> 2 包 -> 2 包（Close） -> 空（Shutdown）。")]
        public async UniTask MemoryBaseline_Empty_Show_Close_Shutdown()
        {
            // 安排：装配模块。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);

            // 基线：操作前为空。
            Assert.AreEqual(0, UIPackage.GetPackages().Count, "操作前全局包注册表应为空。");
            Assert.IsNull(PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg), "操作前 UIBattle 记录应为空。");

            // 执行：Show。
            BaselineTrackingWindow window = await module.ShowAsync<BaselineTrackingWindow>();

            // 断言：Show 后 2 个包。
            Assert.AreEqual(2, UIPackage.GetPackages().Count, "Show 后全局包注册表应有 2 个包。");
            Assert.IsNotNull(PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg), "Show 后 UIBattle 记录应存在。");
            Assert.IsNotNull(PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg), "Show 后 UICommon 记录应存在。");

            // 执行：Close。
            module.Close<BaselineTrackingWindow>();

            // 断言：Close 后仍 2 个包（KeepUntilShutdown）。
            Assert.AreEqual(2, UIPackage.GetPackages().Count, "Close 后全局包注册表仍应有 2 个包。");

            // 执行：Shutdown。
            module.Shutdown();

            // 断言：Shutdown 后回到空基线。
            Assert.AreEqual(0, UIPackage.GetPackages().Count, "Shutdown 后全局包注册表应为空。");
            Assert.IsNull(PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg), "Shutdown 后 UIBattle 记录应为空。");
            Assert.IsNull(PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg), "Shutdown 后 UICommon 记录应为空。");
        }

        // ============================================================
        // 场景 d：KeepUntilShutdown 策略——包在 Shutdown 前不卸载
        // ============================================================

        /// <summary>
        /// KeepUntilShutdown 策略：窗口 Close 后引用计数归零，但包 SHALL 保留到模块 Shutdown，
        /// 运行期不卸载。再 Show 同一类型时复用已 Ready 的包记录，不重新加载。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 包租约控制缓存和卸载——
        /// 只有不存在存活或缓存对象、创建任务、上层依赖和待完成资源操作时，包才可在延迟窗口结束后卸载。
        /// design.md 决策8：首个实现允许 KeepUntilShutdown 卸载策略，包保留到模块 Shutdown。
        /// </remarks>
        [Test]
        [Description("KeepUntilShutdown：Close 后包仍 Ready，再 Show 复用记录，Shutdown 后释放。")]
        public async UniTask KeepUntilShutdown_PackageRetained_UntilShutdown()
        {
            // 安排：装配模块（默认 KeepUntilShutdown）。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);
            Assert.AreEqual(FUIPackageUnloadPolicy.KeepUntilShutdown, PackageLoader.UnloadPolicy,
                "默认策略应为 KeepUntilShutdown。");

            // 执行：首次 Show 后 Close。
            BaselineTrackingWindow window1 = await module.ShowAsync<BaselineTrackingWindow>();
            module.Close<BaselineTrackingWindow>();

            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            Assert.AreEqual(0, uiBattleRecord.ReferenceCount, "Close 后引用计数应归零。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "KeepUntilShutdown 下 Close 后包应仍 Ready。");

            // 执行：再次 Show（复用已 Ready 的包记录，不重新加载）。
            BaselineTrackingWindow window2 = await module.ShowAsync<BaselineTrackingWindow>();

            // 断言：包记录被复用，引用计数再次 >= 1。
            Assert.AreSame(uiBattleRecord, PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg),
                "再 Show 应复用同一 PackageRecord（KeepUntilShutdown 保留）。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "再 Show 后包应仍 Ready（复用）。");
            Assert.GreaterOrEqual(uiBattleRecord.ReferenceCount, 1,
                "再 Show 后引用计数应 >= 1。");

            // 断言：未触发重新加载（SharedLoadTask 为 null，说明复用了已 Ready 的记录）。
            Assert.IsNull(uiBattleRecord.SharedLoadTask,
                "复用已 Ready 记录不应创建新的 SharedLoadTask。");

            // 清理：Close 后 Shutdown。
            module.Close<BaselineTrackingWindow>();
            module.Shutdown();
        }

        // ============================================================
        // 场景 e：Delayed 策略——延迟期后卸载
        // ============================================================

        /// <summary>
        /// Delayed 策略：窗口 Close 后引用计数归零，延迟期到期后包进入 Disposed 终态，
        /// handle 清空，从注册表移除。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 包租约控制缓存和卸载——
        /// 包引用归零后进入延迟卸载窗口，到期后卸载。
        /// design.md 决策8：Delayed 策略下零引用后启动延迟卸载任务，到期后执行最终释放。
        /// </remarks>
        [Test]
        [Description("Delayed：Close 后延迟期到期，包进入 Disposed，handle 清空。")]
        public async UniTask Delayed_PackageUnloaded_AfterDelayExpires()
        {
            // 安排：装配模块并配置 Delayed 策略。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);
            PackageLoader.Configure(FUIPackageUnloadPolicy.Delayed, TestUnloadDelaySeconds);
            Assert.AreEqual(FUIPackageUnloadPolicy.Delayed, PackageLoader.UnloadPolicy,
                "配置后策略应为 Delayed。");

            // 执行：Show 后 Close（触发延迟卸载任务）。
            BaselineTrackingWindow window = await module.ShowAsync<BaselineTrackingWindow>();
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            PackageRecord uiCommonRecord = PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg);

            // 断言：Show 后引用计数 >= 1。
            Assert.GreaterOrEqual(uiBattleRecord.ReferenceCount, 1, "Show 后 UIBattle 引用应 >= 1。");

            // 执行 Close：引用计数归零，触发 Delayed 延迟卸载任务。
            module.Close<BaselineTrackingWindow>();

            // 断言：Close 后立即检查，包仍处于 Ready（延迟期未到期）。
            Assert.AreEqual(0, uiBattleRecord.ReferenceCount, "Close 后引用应归零。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "Close 后延迟期内包应仍 Ready。");

            // 等待延迟期到期 + 安全余量。
            await UniTask.Delay(TestUnloadDelayMs + DelaySafetyMarginMs);

            // 断言：延迟期到期后，UIBattle 包进入 Disposed（UIBattle 无窗口引用，引用为零）。
            // 注意：UICommon 作为 UIBattle 的依赖，在 UIBattle 释放依赖 lease 后引用也归零，
            // 同样触发延迟卸载。两个包的延迟任务独立调度，但都在延迟期后完成。
            Assert.AreEqual(PackageLoadState.Disposed, uiBattleRecord.State,
                "Delayed 延迟期到期后 UIBattle 应进入 Disposed。");
            Assert.IsNull(uiBattleRecord.DescHandle,
                "Delayed 卸载后 UIBattle 描述 handle 应已清空。");
            Assert.AreEqual(0, uiBattleRecord.AssetHandles.Count,
                "Delayed 卸载后 UIBattle AssetHandles 应清空。");

            // UICommon 作为依赖，UIBattle 卸载时释放依赖 lease，UICommon 引用归零后也触发延迟卸载。
            // 等待额外延迟期使 UICommon 延迟任务完成。
            await UniTask.Delay(TestUnloadDelayMs + DelaySafetyMarginMs);
            Assert.AreEqual(PackageLoadState.Disposed, uiCommonRecord.State,
                "Delayed 延迟期到期后 UICommon 应进入 Disposed。");

            // 清理：Shutdown（幂等，包已 Disposed，Shutdown 强制回收残留）。
            module.Shutdown();
        }

        // ============================================================
        // 场景 f：Delayed 策略——延迟期内重开取消卸载
        // ============================================================

        /// <summary>
        /// Delayed 策略延迟期内重新 Acquire：窗口 Close 后进入延迟卸载窗口，
        /// 到期前再次 Show 同一类型 SHALL 取消卸载并复用现有包记录，不产生卸载与重载抖动。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 包租约控制缓存和卸载 / Scenario: 延迟卸载期间重新 Acquire——
        /// 卸载 SHALL 被取消并复用现有包记录，不得产生卸载与重载抖动。
        /// design.md 决策8：延迟卸载前重新 Acquire 通过递增待卸载版本取消旧卸载任务。
        /// </remarks>
        [Test]
        [Description("Delayed：延迟期内重开取消卸载，复用包记录，无抖动。")]
        public async UniTask Delayed_ReopenDuringDelay_CancelsUnload()
        {
            // 安排：装配模块并配置 Delayed 策略。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);
            PackageLoader.Configure(FUIPackageUnloadPolicy.Delayed, TestUnloadDelaySeconds);

            // 执行：首次 Show 后 Close（触发延迟卸载任务）。
            BaselineTrackingWindow window1 = await module.ShowAsync<BaselineTrackingWindow>();
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            module.Close<BaselineTrackingWindow>();

            // 断言：Close 后引用归零，包仍 Ready（延迟期内）。
            Assert.AreEqual(0, uiBattleRecord.ReferenceCount, "Close 后引用应归零。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State, "延迟期内包应仍 Ready。");

            // 记录待卸载版本（延迟任务启动时应已递增）。
            int versionBeforeReopen = uiBattleRecord.PendingUnloadVersion;

            // 执行：在延迟期内再次 Show（应取消卸载并复用记录）。
            // 等待短暂时间但不超过延迟期，确保仍处于延迟窗口内。
            await UniTask.Delay(TestUnloadDelayMs / 2);
            BaselineTrackingWindow window2 = await module.ShowAsync<BaselineTrackingWindow>();

            // 断言：包记录被复用（未卸载），状态仍为 Ready。
            Assert.AreSame(uiBattleRecord, PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg),
                "延迟期内重开应复用同一 PackageRecord。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "延迟期内重开后包应仍 Ready（卸载被取消）。");
            Assert.GreaterOrEqual(uiBattleRecord.ReferenceCount, 1,
                "重开后引用计数应 >= 1。");

            // 断言：待卸载版本在重开时被递增，使旧卸载任务过期。
            Assert.Greater(uiBattleRecord.PendingUnloadVersion, versionBeforeReopen,
                "重开应递增 PendingUnloadVersion 使旧卸载任务过期。");

            // 等待超过原始延迟期，验证旧卸载任务未执行（包仍 Ready）。
            await UniTask.Delay(TestUnloadDelayMs + DelaySafetyMarginMs);
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "旧卸载任务应被取消，包仍 Ready。");

            // 清理：Close 后 Shutdown。
            module.Close<BaselineTrackingWindow>();
            module.Shutdown();
        }

        // ============================================================
        // 场景 g：并发场景无负引用
        // ============================================================

        /// <summary>
        /// 并发场景无负引用：两个并发 Show 同一窗口类型合并加载，只创建一个实例，
        /// Close 后引用计数从 1 归零，不为负数。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 包和依赖加载任务合并 / Scenario: 两个窗口同时请求同一包——
        /// 系统 SHALL 只执行一次加载，并向两个调用方返回独立租约。
        /// spec / Requirement: 资源所有权唯一且释放有序——重复释放租约 SHALL 被拒绝，不得使引用计数变为负数。
        /// design.md 决策8：同包任务合并。
        /// </remarks>
        [Test]
        [Description("并发 Show 合并加载，Close 后引用计数归零，不为负。")]
        public async UniTask ConcurrentShow_NoNegativeReference()
        {
            // 安排：装配模块（使用延迟 provider 使并发窗口存在）。
            FUIModule module = SetupWithWindow(
                typeof(BaselineTrackingWindow), FUICacheMode.None, loadDelayMs: 50);

            // 执行：两个并发 Show 同一类型（合并加载，只创建一个实例）。
            UniTask<BaselineTrackingWindow> taskA = module.ShowAsync<BaselineTrackingWindow>("argA");
            UniTask<BaselineTrackingWindow> taskB = module.ShowAsync<BaselineTrackingWindow>("argB");

            BaselineTrackingWindow windowA = await taskA;
            BaselineTrackingWindow windowB = await taskB;

            // 断言：同一实例（合并创建）。
            Assert.AreSame(windowA, windowB, "并发 Show 同一类型应返回同一实例。");

            // 断言：包引用计数为 1（窗口只持有一个 lease，并发 Show 合并创建）。
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            Assert.AreEqual(1, uiBattleRecord.ReferenceCount,
                "并发 Show 合并后 UIBattle 引用计数应为 1（一个窗口 lease）。");

            // 执行：Close（释放 lease，引用归零）。
            module.Close<BaselineTrackingWindow>();

            // 断言：引用计数归零，不为负。
            Assert.AreEqual(0, uiBattleRecord.ReferenceCount,
                "Close 后引用计数应归零。");
            Assert.GreaterOrEqual(uiBattleRecord.ReferenceCount, 0,
                "引用计数不得为负。");

            // 清理：Shutdown。
            module.Shutdown();
        }

        // ============================================================
        // 场景 h：失败场景无负引用
        // ============================================================

        /// <summary>
        /// 失败场景无负引用：加载失败时引用计数回滚到 0，不为负数；
        /// 失败后包记录不持有任何 handle 或 UIPackage（干净回滚）。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 失败和取消执行原子回滚——
        /// 包加载任一步骤失败时，系统 SHALL 回滚本次操作新增的引用和资源，且不得影响已持有的共享记录。
        /// design.md 决策9：包失败回滚使用本次操作账本。
        /// </remarks>
        [Test]
        [Description("加载失败后引用计数为 0，handle 清空，不为负。")]
        public async UniTask LoadFailure_NoNegativeReference()
        {
            // 安排：装配模块（provider 预设了 UIBattle/UICommon 资源）。
            // 装配阶段不触发包加载（ShowAsync 才加载），故可在装配后标记失败。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);

            // 获取内部 provider 并标记 UIBattle 描述资源加载失败。
            // ResourceProvider 为 internal，通过 InternalsVisibleTo 访问；
            // InMemoryFUIResourceProvider 为 internal，同样通过 InternalsVisibleTo 访问。
            InMemoryFUIResourceProvider provider =
                (InMemoryFUIResourceProvider)module.ResourceProvider;
            provider.MarkLoadFailure(PlayModeTestHarness.UIBattlePkg + "_fui");

            // 执行：Show 应抛出异常（描述资源加载失败）。
            // InMemoryFUIResourceProvider 模拟加载失败抛出 InvalidOperationException，
            // PackageLoader.AcquireAsync 向上传播；ExecuteLoadAndOpenAsync 回滚本次资源后重新抛出。
            bool loadFailed = false;
            try
            {
                await module.ShowAsync<BaselineTrackingWindow>();
            }
            catch (FUIException)
            {
                loadFailed = true;
            }
            catch (InvalidOperationException)
            {
                // provider 抛出的原始异常可能直接传播，取决于异常包装路径。
                loadFailed = true;
            }

            // 断言：加载失败。
            Assert.IsTrue(loadFailed, "描述资源加载失败应导致 Show 抛出异常。");

            // 断言：失败后包记录引用计数为 0（回滚成功），不为负。
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            Assert.IsNotNull(uiBattleRecord, "失败后 UIBattle 记录仍应存在（用于诊断）。");
            Assert.AreEqual(0, uiBattleRecord.ReferenceCount,
                "失败回滚后引用计数应为 0。");
            Assert.GreaterOrEqual(uiBattleRecord.ReferenceCount, 0,
                "引用计数不得为负。");

            // 断言：失败后不持有任何 handle 或 UIPackage（干净回滚）。
            Assert.IsNull(uiBattleRecord.DescHandle, "失败回滚后描述 handle 应已清空。");
            Assert.AreEqual(0, uiBattleRecord.AssetHandles.Count,
                "失败回滚后 AssetHandles 应清空。");
            Assert.IsNull(uiBattleRecord.Package,
                "失败回滚后 UIPackage 应为 null。");
            Assert.AreNotEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "失败后不应处于 Ready。");

            // 清理：Shutdown。
            module.Shutdown();
        }

        // ============================================================
        // 场景 i：Shutdown 后无过期回调
        // ============================================================

        /// <summary>
        /// Shutdown 后无过期回调：Delayed 策略下触发延迟卸载任务后立即 Shutdown，
        /// 延迟任务到期后 SHALL 不执行任何过期回调（包已被 Shutdown 强制回收，延迟任务因版本/状态检查中止），
        /// 不抛出异常，包状态保持 Disposed。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 模块 Shutdown 回收全部包资源——
        /// 无论运行期延迟卸载是否启用，模块 Shutdown SHALL 停止新的 Acquire，等待或失效化进行中的资源操作。
        /// design.md 决策8：UnloadAllForShutdown 递增各记录 PendingUnloadVersion 使延迟任务过期。
        /// design.md 决策9：FinalUnload 重入保护使延迟任务在 Shutdown 后不双重释放。
        /// </remarks>
        [Test]
        [Description("Shutdown 后延迟任务到期不执行过期回调，包保持 Disposed。")]
        public async UniTask Shutdown_NoStaleCallbacks_AfterDelayedTaskExpires()
        {
            // 安排：装配模块并配置 Delayed 策略。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);
            PackageLoader.Configure(FUIPackageUnloadPolicy.Delayed, TestUnloadDelaySeconds);

            // 执行：Show 后 Close（触发延迟卸载任务），立即 Shutdown（不等延迟期）。
            BaselineTrackingWindow window = await module.ShowAsync<BaselineTrackingWindow>();
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            module.Close<BaselineTrackingWindow>();

            // 断言：Close 后延迟期内包仍 Ready。
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                "Close 后延迟期内包应仍 Ready。");

            // 执行：立即 Shutdown（延迟任务仍在等待中）。
            module.Shutdown();

            // 断言：Shutdown 后包已被强制回收，进入 Disposed。
            Assert.AreEqual(PackageLoadState.Disposed, uiBattleRecord.State,
                "Shutdown 后包应进入 Disposed。");
            Assert.IsNull(uiBattleRecord.DescHandle, "Shutdown 后描述 handle 应已清空。");
            Assert.AreEqual(0, uiBattleRecord.AssetHandles.Count, "Shutdown 后 handle 表应清空。");

            // 等待延迟期到期 + 安全余量，验证延迟任务不执行任何过期回调。
            // 延迟任务到期后应因版本不匹配（Shutdown 递增了版本）和状态检查（Disposed）中止，
            // 不抛异常、不修改状态、不双重释放。
            await UniTask.Delay(TestUnloadDelayMs + DelaySafetyMarginMs);

            // 断言：包状态仍为 Disposed（延迟任务未改变状态，未双重释放）。
            Assert.AreEqual(PackageLoadState.Disposed, uiBattleRecord.State,
                "延迟任务到期后包状态应仍为 Disposed（无过期回调）。");
            Assert.IsNull(uiBattleRecord.DescHandle,
                "延迟任务到期后描述 handle 应仍为 null（未双重释放）。");
            Assert.AreEqual(0, uiBattleRecord.AssetHandles.Count,
                "延迟任务到期后 handle 表应仍为空。");

            // 断言：FUI.Module getter 抛异常（模块已 Shutdown）。
            Assert.Throws<FUIException>(() =>
            {
                IFUIModule _ = FUI.Module;
            }, "Shutdown 后 FUI.Module getter 应抛 FUIException。");
        }

        // ============================================================
        // 场景 j：重复 Release 租约无负引用（直接通过 PackageLoader 验证）
        // ============================================================

        /// <summary>
        /// 重复 Release 租约无负引用：同一 lease 被重复 Release 时，第二次返回 false，
        /// 引用计数保持为 0，不为负数。窗口 Close 后再 Close（已 Disposed）也不产生负引用。
        /// </summary>
        /// <remarks>
        /// spec fairygui-package-loading / Requirement: 资源所有权唯一且释放有序 / Scenario: 重复释放租约——
        /// 系统 SHALL 拒绝第二次释放并报告诊断信息，不得使引用计数变为负数。
        /// design.md 决策8：PackageLease.Release 幂等保护。
        /// </remarks>
        [Test]
        [Description("重复 Release 租约被拒绝，引用计数保持 0，不为负。")]
        public async UniTask DuplicateRelease_NoNegativeReference()
        {
            // 安排：装配模块。
            FUIModule module = SetupWithWindow(typeof(BaselineTrackingWindow), FUICacheMode.None);

            // 执行：Show 后 Close。
            BaselineTrackingWindow window = await module.ShowAsync<BaselineTrackingWindow>();
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            Assert.AreEqual(1, uiBattleRecord.ReferenceCount, "Show 后引用应为 1。");

            module.Close<BaselineTrackingWindow>();

            // 断言：Close 后引用归零。
            Assert.AreEqual(0, uiBattleRecord.ReferenceCount, "Close 后引用应归零。");

            // 执行：再次 Close（窗口已 Disposed，不应再次释放 lease）。
            Assert.DoesNotThrow(() => module.Close<BaselineTrackingWindow>(),
                "已 Disposed 窗口再次 Close 不应抛异常。");

            // 断言：引用计数仍为 0，不为负。
            Assert.AreEqual(0, uiBattleRecord.ReferenceCount,
                "再次 Close 后引用计数应仍为 0，不为负。");
            Assert.GreaterOrEqual(uiBattleRecord.ReferenceCount, 0,
                "引用计数不得为负。");

            // 清理：Shutdown。
            module.Shutdown();
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
        /// 本方法复用 <see cref="PlayModeTestHarness"/> 的公开构建块，不修改 harness。
        /// 装配流程与 <see cref="PlayModeTestHarness.SetupForShowAsync"/> 一致，唯一差异是允许自定义窗口类型与 CacheMode。
        /// 注册顺序与 spec "测试 owner 注册 UIBattle" 一致：先 Binder，再 Widget，再 Window。
        /// </remarks>
        private static FUIModule SetupWithWindow(
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
        /// 将 <see cref="PackageLoader.UnloadPolicy"/> 恢复为默认的 KeepUntilShutdown 策略，
        /// 避免 Delayed 策略跨测试残留。
        /// </summary>
        /// <remarks>
        /// <see cref="PlayModeTestHarness.Cleanup"/> 不重置 PackageLoader 的策略配置
        /// （harness 不修改运行时代码），故由本测试自行负责重置。
        /// 每个测试的 SetUp 与 TearDown 调用本方法保证策略基线一致。
        /// </remarks>
        private static void ResetUnloadPolicy()
        {
            PackageLoader.Configure(FUIPackageUnloadPolicy.KeepUntilShutdown, 5f);
        }

        /// <summary>
        /// 计算指定包记录持有的 handle 总数（描述 handle + 外部资源 handle 表）。
        /// </summary>
        /// <param name="record">包记录。</param>
        /// <returns>handle 总数。</returns>
        private static int TotalHandleCount(PackageRecord record)
        {
            int count = record.AssetHandles.Count;
            if (record.DescHandle != null)
            {
                count++;
            }
            return count;
        }

        // ============================================================
        // 测试用窗口类型
        // ============================================================

        /// <summary>
        /// 基线追踪测试窗口，继承 <see cref="TestBattleStartPanel"/>。
        /// 用于任务 7.5 的 PackageRecord/handle/基线对比验收，不引入额外生命周期计数逻辑
        /// （生命周期次数已由 <see cref="PlayModeLifecycleTests"/> 覆盖）。
        /// </summary>
        public class BaselineTrackingWindow : TestBattleStartPanel
        {
            // 本类型仅作为最终业务窗口类型用于 ShowAsync，不添加额外字段或计数器。
            // 生命周期行为完全继承自 TestBattleStartPanel，保证与 7.2/7.3 验收一致的装配契约。
        }
    }
}
