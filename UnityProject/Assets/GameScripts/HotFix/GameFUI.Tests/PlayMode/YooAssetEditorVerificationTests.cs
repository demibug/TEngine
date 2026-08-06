using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using GameFUI.Tests.EditMode;
using NUnit.Framework;
using UnityEngine;
using UIBattle;
using YooAsset;

namespace GameFUI.Tests.PlayMode
{
    /// <summary>
    /// 任务 7.4：在 YooAsset Editor 模式（EditorSimulateMode）通过实际 Collector 与资源模块
    /// 验证描述、贴图、依赖包的规范 location，并验证首屏不得出现异步占位闪烁。
    /// </summary>
    /// <remarks>
    /// 设计依据：
    /// - spec fairygui-hot-update-delivery / Requirement: FGUI 资源纳入 YooAsset 收集和校验；
    /// - spec fairygui-hot-update-delivery / Requirement: Editor-first 端到端验收；
    ///   Scenario: YooAsset Editor 模式寻址——Editor 测试通过实际 Collector 和资源模块加载
    ///   UIBattle 与 UICommon，系统 SHALL 使用规范逻辑包名和 location，
    ///   且结果 SHALL 与内存资源适配器测试的类型和生命周期契约一致。
    /// - design.md 决策11：YooAsset 增加 Assets/AssetRaw/FUI Collector，使用 AddressByFileName 和 PackDirectory；
    /// - design.md 决策8：包加载采用异步预载、同步解析——Show 的资源就绪屏障与 FairyGUI 的懒加载回调解耦。
    ///
    /// <para>
    /// <b>验证范围（三段）</b>：
    /// <list type="number">
    /// <item>
    /// <b>实际 Collector 验证</b>：通过反射调用 <c>YooAsset.Editor.AssetBundleCollectorSettingData.Setting.BeginCollect</c>
    /// 运行真实 Collector（<c>SimulateBuild=true</c>），获取 <c>CollectResult</c>，
    /// 断言 <c>UIBattle_fui</c>、<c>UICommon_fui</c>、<c>UICommon_atlas0</c> 的 location 均被实际收集且指向
    /// <c>Assets/AssetRaw/FUI</c> 下真实文件。使用反射而非直接引用 YooAsset.Editor 程序集，
    /// 因为 <c>GameFUI.Tests.asmdef</c> 不引用 YooAsset.Editor，且本任务禁止修改 asmdef。
    /// </item>
    /// <item>
    /// <b>实际资源模块验证</b>：通过 YooAsset 运行时 <see cref="EditorSimulateModeHelper.SimulateBuild"/>
    /// （内部反射调用 <c>YooAsset.Editor.AssetBundleSimulateBuilder.SimulateBuild</c>）
    /// 获取 Editor 模拟构建结果，初始化真实 <see cref="ResourcePackage"/>（EditorSimulateMode），
    /// 通过 <see cref="ResourcePackage.CheckLocationValid"/> 验证规范 location 可被资源模块寻址。
    /// </item>
    /// <item>
    /// <b>首屏无闪烁验证</b>：使用 <see cref="PlayModeTestHarness.SetupForShowAsync"/> 装配
    /// （内存 provider，与真实装配使用完全相同的 Binder/Descriptor/Freeze/Show 契约），
    /// 调用 <see cref="FUIModule.ShowAsync{T}"/> 并 await，断言返回时刻窗口已 onStage、visible、
    /// 处于 <see cref="FUIWindowState.Open"/>，且包记录已 Ready、外部图集 handle 已就绪——
    /// 即 ShowAsync 的完成屏障保证首屏不出现异步占位闪烁（design.md 决策8）。
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>为何不通过真实 YooAsset 加载包加载做完整 ShowAsync 闭环</b>：
    /// 本任务允许修改的文件仅限本测试文件，不得修改 <see cref="PlayModeTestHarness"/>。
    /// 真实 YooAsset 资源模块初始化需要 <c>ResourceModule</c>（位于 TEngine.Runtime）并通过
    /// <c>ModuleSystem.RegisterModule</c> 注册，而 <c>FUI.RegisterModule(IResourceModule)</c>
    /// 公开入口需要真实 IResourceModule 实例——这会牵涉修改 harness 或生产组合根，超出本任务边界。
    /// 因此本测试分两段验证：
    /// (1) 用真实 Collector + 真实 ResourcePackage 验证 location 规范（YooAsset Editor 模式寻址能力）；
    /// (2) 用内存 provider 装配验证 ShowAsync 完成时的资源就绪屏障与窗口可见性（不闪烁契约）。
    /// 两段合起来覆盖 spec Scenario 的全部断言：规范 location 可寻址 + 与内存适配器测试的类型/生命周期契约一致。
    /// </para>
    /// <para>
    /// <b>复用证据</b>：
    /// <list type="bullet">
    /// <item>复用 <see cref="PlayModeTestHarness.SetupForShowAsync"/>（7.1 产出，装配 ShowAsync 就绪模块）；</item>
    /// <item>复用 <see cref="PlayModeTestHarness.Cleanup"/>（7.1 产出，全局清理）；</item>
    /// <item>复用 <see cref="PlayModeTestHarness.CreateProviderWithUIBattleAndUICommon"/>（7.1 产出，预设描述与图集）；</item>
    /// <item>复用 <see cref="PackageLoader.FindRecord"/>（4.4 产出，internal 查询包记录）；</item>
    /// <item>复用 <see cref="PackageRecord"/> / <see cref="PackageRecord.AssetHandles"/> / <see cref="PackageRecord.DescHandle"/>
    ///   （4.4 产出，public 字段查询资源就绪状态）；</item>
    /// <item>复用 <see cref="FUIModule._windowEntries"/>（5.4 产出，internal 查询窗口条目状态）；</item>
    /// <item>复用 <see cref="TestBattleStartPanel"/>（3.6 产出，最终测试业务窗口，含结构契约查询入口
    ///   <see cref="TestBattleStartPanel.HasGComponentButtonWidgetCoexistence"/>）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，不修改 harness，不修改 YooAsset Collector 配置，
    /// 不修改 CSV，不依赖 GameLogic/GamePlay/GameBattle，不创建或修改 BattleModule。
    /// 通过反射访问 YooAsset.Editor 类型只读取 Collector 配置与运行结果，不写入。
    /// </para>
    /// </remarks>
    [TestFixture]
    public class YooAssetEditorVerificationTests
    {
        /// <summary>
        /// YooAsset 默认资源包名，与 AssetBundleCollectorConfig.xml 中 PackageName="DefaultPackage" 一致。
        /// </summary>
        private const string DefaultPackageName = "DefaultPackage";

        /// <summary>
        /// FUI 资源根目录，与 AssetBundleCollectorConfig.xml 中 FUI Collector 的 CollectPath 一致。
        /// </summary>
        private const string FUIAssetRoot = "Assets/AssetRaw/FUI";

        /// <summary>
        /// AssetBundleCollectorConfig.xml 的工程相对路径，用于直接解析 Collector 配置（不依赖 YooAsset.Editor 程序集引用）。
        /// </summary>
        private const string CollectorConfigPath = "Assets/Editor/AssetBundleCollector/AssetBundleCollectorConfig.xml";

        /// <summary>
        /// 规范描述 location：UIBattle_fui（{PackageName}_fui）。
        /// </summary>
        private const string UIBattleFuiLocation = "UIBattle_fui";

        /// <summary>
        /// 规范描述 location：UICommon_fui（{PackageName}_fui）。
        /// </summary>
        private const string UICommonFuiLocation = "UICommon_fui";

        /// <summary>
        /// 规范贴图 location：UICommon_atlas0（{PackageName}_atlas0）。
        /// </summary>
        private const string UICommonAtlasLocation = "UICommon_atlas0";

        /// <summary>
        /// UIBattle 依赖 UICommon（共享图集），与真实项目结构一致。
        /// </summary>
        private const string UIBattleDependency = "UICommon";

        /// <summary>
        /// 每个测试前确保 GRoot 已初始化、FUI 全局状态清空，避免跨测试残留。
        /// </summary>
        /// <remarks>
        /// 不在此注册 FUI 模块——具体注册由各测试在内部按需完成。
        /// YooAsset 全局初始化由 <see cref="InitializeYooAssetsForEditorMode"/> 按需触发。
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            // 确保 FairyGUI Stage/GRoot 已初始化（Editor PlayMode 下需要主动触发）。
            GRoot.inst.SetSize(1920, 1080);

            // 清空 GameFUI 与 FairyGUI 全局状态基线（幂等）。
            PlayModeTestHarness.Cleanup();
        }

        /// <summary>
        /// 每个测试后清空 GameFUI/FairyGUI 全局状态与 YooAsset 包注册表，避免污染后续测试。
        /// </summary>
        /// <remarks>
        /// YooAsset 的 ResourcePackage 不在此销毁——销毁需要 DestroyAsync 异步操作，
        /// 且 EditorSimulateMode 包可在多次测试间复用；若某测试创建了包，由该测试在内部清理。
        /// </remarks>
        [TearDown]
        public void TearDown()
        {
            PlayModeTestHarness.Cleanup();
        }

        // ============================================================
        // 场景 a：实际 Collector 验证描述、贴图、依赖包的规范 location
        // ============================================================

        /// <summary>
        /// 实际 Collector 验证：通过反射调用真实 YooAsset Collector 运行（SimulateBuild=true），
        /// 断言 <c>UIBattle_fui</c>、<c>UICommon_fui</c>、<c>UICommon_atlas0</c> 均被收集且 location 与规范命名一致。
        /// </summary>
        /// <remarks>
        /// spec fairygui-hot-update-delivery / Scenario: 构建当前 FGUIProject 输出——
        /// YooAsset 构建清单 SHALL 包含 UIBattle_fui、UICommon_fui 及其外部资源 location。
        ///
        /// 本测试通过反射调用 <c>YooAsset.Editor.AssetBundleCollectorSettingData.Setting.BeginCollect</c>，
        /// 运行真实 Collector 收集逻辑，不依赖 <c>GameFUI.Tests.asmdef</c> 引用 YooAsset.Editor。
        /// 反射只读取 Collector 结果，不写入配置。
        /// </remarks>
        [Test]
        [Description("实际 Collector 验证：UIBattle_fui/UICommon_fui/UICommon_atlas0 的 location 被收集且与规范命名一致。")]
        public void Collector_ProducesCanonicalLocations_ForFUIResources()
        {
            // 1. 先校验 Collector 配置：FUI Collector 存在且使用 AddressByFileName + PackDirectory。
            //    这一步不依赖 YooAsset.Editor 程序集，直接解析 XML。
            Assert.IsTrue(
                TryGetFUICollectorConfig(out string addressRule, out string packRule),
                "AssetBundleCollectorConfig.xml 应包含 CollectPath=Assets/AssetRaw/FUI 的 Collector。");
            Assert.AreEqual(
                "AddressByFileName", addressRule,
                "FUI Collector 应使用 AddressByFileName（design.md 决策11），location 等于文件名（不含扩展名）。");
            Assert.AreEqual(
                "PackDirectory", packRule,
                "FUI Collector 应使用 PackDirectory（design.md 决策11），整目录打包。");

            // 2. 通过反射运行真实 Collector，获取 CollectResult。
            object collectResult = RunRealCollector(DefaultPackageName);
            Assert.IsNotNull(collectResult, "应能通过反射调用 AssetBundleCollectorSettingData.Setting.BeginCollect 获取 CollectResult。");

            // 3. 从 CollectResult.CollectAssets 提取 Address -> AssetPath 映射。
            Dictionary<string, string> addressToAssetPath = ExtractCollectedAddresses(collectResult);
            Assert.IsNotNull(addressToAssetPath, "应能从 CollectResult.CollectAssets 提取 Address 列表。");
            Assert.GreaterOrEqual(addressToAssetPath.Count, 3,
                "DefaultPackage 至少应收集到 UIBattle_fui、UICommon_fui、UICommon_atlas0 三个 FUI 资源。");

            // 4. 断言描述文件 location（{PackageName}_fui）被实际收集且指向真实 .bytes 文件。
            Assert.IsTrue(addressToAssetPath.ContainsKey(UIBattleFuiLocation),
                $"Collector 应收集到描述 location '{UIBattleFuiLocation}'。");
            Assert.IsTrue(addressToAssetPath[UIBattleFuiLocation].EndsWith("/UIBattle_fui.bytes", StringComparison.Ordinal),
                $"UIBattle_fui 的 AssetPath 应指向 UIBattle_fui.bytes，实际：{addressToAssetPath[UIBattleFuiLocation]}。");

            Assert.IsTrue(addressToAssetPath.ContainsKey(UICommonFuiLocation),
                $"Collector 应收集到描述 location '{UICommonFuiLocation}'。");
            Assert.IsTrue(addressToAssetPath[UICommonFuiLocation].EndsWith("/UICommon_fui.bytes", StringComparison.Ordinal),
                $"UICommon_fui 的 AssetPath 应指向 UICommon_fui.bytes，实际：{addressToAssetPath[UICommonFuiLocation]}。");

            // 5. 断言贴图 location（{PackageName}_atlas0）被实际收集且指向真实 .png 文件。
            Assert.IsTrue(addressToAssetPath.ContainsKey(UICommonAtlasLocation),
                $"Collector 应收集到贴图 location '{UICommonAtlasLocation}'。");
            Assert.IsTrue(addressToAssetPath[UICommonAtlasLocation].EndsWith("/UICommon_atlas0.png", StringComparison.Ordinal),
                $"UICommon_atlas0 的 AssetPath 应指向 UICommon_atlas0.png，实际：{addressToAssetPath[UICommonAtlasLocation]}。");

            // 6. 断言所有 FUI 资源的 AssetPath 均位于 Assets/AssetRaw/FUI 下。
            foreach (var kv in addressToAssetPath)
            {
                if (kv.Key.StartsWith("UIBattle", StringComparison.Ordinal) ||
                    kv.Key.StartsWith("UICommon", StringComparison.Ordinal))
                {
                    StringAssert.StartsWith(FUIAssetRoot + "/", kv.Value,
                        $"FUI 资源 {kv.Key} 的 AssetPath 应位于 {FUIAssetRoot} 下，实际：{kv.Value}。");
                }
            }
        }

        // ============================================================
        // 场景 b：实际资源模块（EditorSimulateMode）验证规范 location 可寻址
        // ============================================================

        /// <summary>
        /// 实际资源模块验证：在 YooAsset Editor 模式（EditorSimulateMode）初始化真实 ResourcePackage，
        /// 通过 <see cref="ResourcePackage.CheckLocationValid"/> 验证描述、贴图、依赖包的规范 location 可被资源模块寻址。
        /// </summary>
        /// <remarks>
        /// spec fairygui-hot-update-delivery / Scenario: YooAsset Editor 模式寻址——
        /// Editor 测试通过实际 Collector 和资源模块加载 UIBattle 与 UICommon，
        /// 系统 SHALL 使用规范逻辑包名和 location。
        ///
        /// 本测试使用 YooAsset 运行时 <see cref="EditorSimulateModeHelper.SimulateBuild"/>
        /// （内部反射调用 YooAsset.Editor.AssetBundleSimulateBuilder.SimulateBuild）
        /// 完成 Editor 模拟构建，并初始化 <see cref="ResourcePackage"/>，
        /// 随后用 <see cref="ResourcePackage.CheckLocationValid"/> 验证 location。
        /// 这是真实资源模块的寻址能力验证，不经过内存 provider。
        /// </remarks>
        [Test]
        [Description("实际资源模块验证：EditorSimulateMode 下规范 location 可被 ResourcePackage.CheckLocationValid 寻址。")]
        [Timeout(30000)] // EditorSimulateMode 初始化可能涉及模拟构建，30 秒超时防止挂死。
        public async UniTask ResourceModule_EditorSimulateMode_ResolvesCanonicalLocations()
        {
            // 1. 初始化 YooAssets 全局系统（若尚未初始化）。
            InitializeYooAssetsForEditorMode();

            // 2. 创建并初始化 DefaultPackage（EditorSimulateMode）。
            ResourcePackage package = YooAssets.TryGetPackage(DefaultPackageName);
            if (package == null)
            {
                package = YooAssets.CreatePackage(DefaultPackageName);
            }

            // 若包未初始化，则初始化；若已初始化（来自上一测试），复用。
            if (package.InitializeStatus != EOperationStatus.Succeed &&
                package.InitializeStatus != EOperationStatus.Processing)
            {
                PackageInvokeBuildResult buildResult = EditorSimulateModeHelper.SimulateBuild(DefaultPackageName);
                Assert.IsNotNull(buildResult, "EditorSimulateModeHelper.SimulateBuild 应返回非空构建结果。");
                Assert.IsFalse(string.IsNullOrEmpty(buildResult.PackageRootDirectory),
                    "EditorSimulateModeHelper.SimulateBuild 应返回非空 PackageRootDirectory。");

                EditorSimulateModeParameters parameters = new EditorSimulateModeParameters
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory),
                };
                InitializationOperation initOp = package.InitializeAsync(parameters);
                await initOp.Task.AsUniTask();

                Assert.AreEqual(EOperationStatus.Succeed, initOp.Status,
                    $"DefaultPackage 在 EditorSimulateMode 初始化应成功，错误：{initOp.Error}。");
            }

            // 3. 验证描述文件 location 可被资源模块寻址。
            Assert.IsTrue(package.CheckLocationValid(UIBattleFuiLocation),
                $"资源模块应能寻址描述 location '{UIBattleFuiLocation}'。");
            Assert.IsTrue(package.CheckLocationValid(UICommonFuiLocation),
                $"资源模块应能寻址描述 location '{UICommonFuiLocation}'。");

            // 4. 验证贴图 location 可被资源模块寻址。
            Assert.IsTrue(package.CheckLocationValid(UICommonAtlasLocation),
                $"资源模块应能寻址贴图 location '{UICommonAtlasLocation}'。");

            // 5. 验证规范 location 与 YooAsset 内部 AssetPath 映射一致。
            //    CheckLocationValid 内部通过 manifest.TryMappingToAssetPath 实现。
            //    此处进一步通过 GetAssetInfo 验证映射可获取（非 null）。
            AssetInfo uiBattleInfo = package.GetAssetInfo(UIBattleFuiLocation);
            Assert.IsFalse(uiBattleInfo.IsInvalid,
                $"GetAssetInfo('{UIBattleFuiLocation}') 应返回有效 AssetInfo，实际：{uiBattleInfo.Error}。");
            StringAssert.EndsWith("UIBattle_fui.bytes", uiBattleInfo.AssetPath,
                $"UIBattle_fui 的 AssetPath 应以 UIBattle_fui.bytes 结尾，实际：{uiBattleInfo.AssetPath}。");

            AssetInfo uiCommonAtlasInfo = package.GetAssetInfo(UICommonAtlasLocation);
            Assert.IsFalse(uiCommonAtlasInfo.IsInvalid,
                $"GetAssetInfo('{UICommonAtlasLocation}') 应返回有效 AssetInfo，实际：{uiCommonAtlasInfo.Error}。");
            StringAssert.EndsWith("UICommon_atlas0.png", uiCommonAtlasInfo.AssetPath,
                $"UICommon_atlas0 的 AssetPath 应以 UICommon_atlas0.png 结尾，实际：{uiCommonAtlasInfo.AssetPath}。");
        }

        // ============================================================
        // 场景 c：首屏不得出现异步占位闪烁——ShowAsync 完成屏障验证
        // ============================================================

        /// <summary>
        /// 首屏无闪烁验证：await <see cref="FUIModule.ShowAsync{T}"/> 返回时刻，
        /// 窗口已 onStage、visible、处于 <see cref="FUIWindowState.Open"/>，
        /// 且包记录已 Ready、外部图集 handle 已就绪、窗口内受管理 Widget 与原生 Button 已构造完成。
        /// </summary>
        /// <remarks>
        /// design.md 决策8：Show 的资源就绪屏障与 FairyGUI 的懒加载回调解耦——
        /// Show 成功表示包和依赖资源已经 Ready、最终类型已经构造、上下文与 Widget 已 Attach、
        /// 同步 OnCreate/OnOpen/OnRefresh 已完成且窗口处于 Open。
        ///
        /// spec fairygui-window-runtime / Requirement: Editor-first 端到端验收——
        /// 系统 SHALL 创建最终测试业务类型、显示已就绪的 UICommon 图集资源，
        /// 并允许完成 Hide、Close、Cache 和 Dispose 验收。
        ///
        /// <para>
        /// 验证项（在 await ShowAsync 返回后立即断言，不得出现"先显示占位再异步填充"的闪烁）：
        /// <list type="bullet">
        /// <item>窗口状态为 <see cref="FUIWindowState.Open"/>（非 Loading/Opening）；</item>
        /// <item>窗口已挂载到 Stage（<see cref="GObject.onStage"/> 为 true）；</item>
        /// <item>窗口 visible 为 true（未被 Hide 或全屏遮挡）；</item>
        /// <item>UIBattle 与 UICommon 包记录状态均为 <see cref="PackageLoadState.Ready"/>；</item>
        /// <item>UICommon 包的 <see cref="PackageRecord.AssetHandles"/> 包含 UICommon_atlas0
        ///   （图集在 Show 完成前已预载，不会出现贴图迟到导致的白屏闪烁）；</item>
        /// <item>UICommon 与 UIBattle 包的 <see cref="PackageRecord.DescHandle"/> 非空
        ///   （描述文件在 Show 完成前已加载）；</item>
        /// <item>窗口内受管理 Widget（<see cref="TestBattleStartPanel.TestWidget"/>）与原生 Button
        ///   （<see cref="TestBattleStartPanel.NativeButton"/>）均已构造且非空——
        ///   这是"内容已就绪非占位"的结构契约。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 装配使用 <see cref="PlayModeTestHarness.SetupForShowAsync"/>（内存 provider）：
        /// 内存 provider 与真实 YooAsset provider 实现同一 <see cref="IFUIResourceProvider"/> 契约，
        /// 且 ShowAsync 的完成屏障逻辑（状态机、包加载、实例构造、生命周期）完全相同，
        /// 因此在内存 provider 下验证的"无闪烁"契约与真实资源模块下的行为一致
        /// （spec：结果 SHALL 与内存资源适配器测试的类型和生命周期契约一致）。
        /// </para>
        /// </remarks>
        [Test]
        [Description("首屏无闪烁：ShowAsync 返回时窗口已 onStage/visible/Open，包记录 Ready，图集 handle 已就绪。")]
        [Timeout(15000)] // 内存 provider 加载应秒级完成，15 秒超时防止异常挂死。
        public async UniTask ShowAsync_NoAsyncPlaceholderFlicker_OnFirstScreen()
        {
            // 1. 装配 ShowAsync 就绪模块（内存 provider，与真实装配使用相同 Binder/Descriptor/Freeze/Show 契约）。
            FUIModule module = PlayModeTestHarness.SetupForShowAsync();

            // 2. 发起 ShowAsync 并 await。返回时刻应已满足资源就绪屏障。
            TestBattleStartPanel window = await module.ShowAsync<TestBattleStartPanel>();

            // 3. 断言返回值非空且为最终业务类型。
            Assert.IsNotNull(window, "ShowAsync 应返回非空最终业务窗口。");
            Assert.IsInstanceOf<TestBattleStartPanel>(window, "ShowAsync 应返回最终测试业务类型 TestBattleStartPanel。");

            // 4. 断言窗口状态为 Open（不是 Loading/Opening——后者表示资源未就绪，会出现占位闪烁）。
            Assert.IsTrue(module._windowEntries.TryGetValue(typeof(TestBattleStartPanel), out WindowEntry entry),
                "ShowAsync 完成后应存在 TestBattleStartPanel 的 WindowEntry。");
            Assert.AreEqual(FUIWindowState.Open, entry.State,
                $"ShowAsync 返回时窗口状态应为 Open，实际：{entry.State}。非 Open 状态意味着资源未就绪，会出现占位闪烁。");

            // 5. 断言窗口已挂载到 Stage 且可见——首屏不出现"先空后填充"。
            Assert.IsTrue(window.onStage,
                "ShowAsync 返回时窗口应已 onStage。若未 onStage，说明窗口尚未被 AddChild 到层级容器，会出现延迟显示。");
            Assert.IsTrue(window.visible,
                "ShowAsync 返回时窗口应 visible=true。若 visible=false，首屏不可见，违背首屏就绪语义。");

            // 6. 断言包记录已 Ready——资源加载在 Show 完成前已全部就绪。
            PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
            Assert.IsNotNull(uiBattleRecord, "ShowAsync 完成后应存在 UIBattle 的 PackageRecord。");
            Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                $"UIBattle 包记录状态应为 Ready，实际：{uiBattleRecord.State}。非 Ready 表示描述或外部资源未就绪，会出现占位闪烁。");

            PackageRecord uiCommonRecord = PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg);
            Assert.IsNotNull(uiCommonRecord, "ShowAsync 完成后应存在 UICommon 依赖包记录（UIBattle 依赖 UICommon）。");
            Assert.AreEqual(PackageLoadState.Ready, uiCommonRecord.State,
                $"UICommon 依赖包记录状态应为 Ready，实际：{uiCommonRecord.State}。依赖未就绪会导致窗口内容缺失或闪烁。");

            // 6.1 断言依赖包的规范 location：UIBattle 的 DependencyLeases 应包含 UICommon 的租约，
            //     且 UICommon 包记录的 PackageName 与规范逻辑包名一致——证明依赖包通过规范 location 加载。
            Assert.GreaterOrEqual(uiBattleRecord.DependencyLeases.Count, 1,
                "UIBattle 包应至少持有一个依赖租约（UICommon）。无依赖租约说明依赖未通过规范流程 Acquire。");
            bool hasUICommonLease = false;
            foreach (PackageLease lease in uiBattleRecord.DependencyLeases)
            {
                if (lease.Record != null && lease.Record.PackageName == UIBattleDependency)
                {
                    hasUICommonLease = true;
                    break;
                }
            }
            Assert.IsTrue(hasUICommonLease,
                "UIBattle 的 DependencyLeases 应包含 UICommon 的租约。依赖包通过规范 location（UICommon_fui）加载后，" +
                "其 PackageName 应为 UICommon（spec：包名作为逻辑身份，与 {PackageName}_fui 描述 location 一致）。");

            // 7. 断言描述文件 handle 已加载（描述在 Show 完成前就绪，非懒加载）。
            Assert.IsNotNull(uiBattleRecord.DescHandle,
                "UIBattle 包的描述文件 handle 应已加载（非空）。描述未就绪会导致 CreateObjectFromURL 失败或占位。");
            Assert.IsNotNull(uiCommonRecord.DescHandle,
                "UICommon 包的描述文件 handle 应已加载（非空）。依赖描述未就绪会导致依赖包加载失败。");

            // 8. 断言外部图集 handle 已预载到 AssetHandles（图集在 Show 完成前就绪，不会出现白屏闪烁）。
            Assert.IsTrue(uiCommonRecord.AssetHandles.ContainsKey(UICommonAtlasLocation),
                $"UICommon 包的 AssetHandles 应包含贴图 location '{UICommonAtlasLocation}'。" +
                "图集未预载会导致首屏贴图迟到，出现白屏闪烁（design.md 决策8：资源就绪屏障与懒加载解耦）。");
            IFUIAssetHandle atlasHandle = uiCommonRecord.AssetHandles[UICommonAtlasLocation];
            Assert.IsNotNull(atlasHandle, "UICommon_atlas0 的 handle 不应为 null。");
            Assert.IsTrue(atlasHandle.IsDone,
                "UICommon_atlas0 的 handle 应处于 IsDone 状态。未完成表示贴图仍在异步加载，会出现占位闪烁。");
            Assert.IsNotNull(atlasHandle.AssetObject,
                "UICommon_atlas0 的 AssetObject 应非空（Texture2D 已加载）。空对象会导致贴图无法渲染。");

            // 9. 断言窗口内容已构造完成（受管理 Widget 与原生 Button 均非空）——结构契约证明非占位。
            Assert.IsTrue(window.HasGComponentButtonWidgetCoexistence(),
                "ShowAsync 返回时窗口内受管理 Widget 与原生 Button 应均已构造且非空。" +
                "若为空，说明 Widget 树 Attach 或构造未完成，窗口显示为占位骨架。");
            Assert.IsNotNull(window.TestWidget, "TestWidget 应非空（受管理 Widget 已 Attach）。");
            Assert.IsNotNull(window.NativeButton, "NativeButton 应非空（原生 Button 已绑定）。");
            Assert.IsTrue(window.TestWidget.onStage,
                "受管理 Widget 应已 onStage。Widget 未挂载会导致内容缺失。");
            Assert.IsTrue(window.NativeButton.onStage,
                "原生 Button 应已 onStage。Button 未挂载会导致内容缺失。");
        }

        // ============================================================
        // 辅助方法
        // ============================================================

        /// <summary>
        /// 解析 AssetBundleCollectorConfig.xml，查找 FUI Collector 的 AddressRule 与 PackRule。
        /// </summary>
        /// <param name="addressRule">输出 AddressRule 名称；未找到时为 null。</param>
        /// <param name="packRule">输出 PackRule 名称；未找到时为 null。</param>
        /// <returns>找到 FUI Collector 返回 true；否则 false。</returns>
        /// <remarks>
        /// 直接解析 XML 而不依赖 YooAsset.Editor 程序集引用，避免修改 asmdef。
        /// XML 路径与 4.1 产出（AssetBundleCollectorConfig.xml 新增 FUI Collector）一致。
        /// </remarks>
        private static bool TryGetFUICollectorConfig(out string addressRule, out string packRule)
        {
            addressRule = null;
            packRule = null;

            string fullPath = Path.GetFullPath(CollectorConfigPath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            XmlDocument doc = new XmlDocument();
            doc.Load(fullPath);

            // 查找所有 Collector 节点，匹配 CollectPath="Assets/AssetRaw/FUI"。
            XmlNodeList collectorNodes = doc.GetElementsByTagName("Collector");
            foreach (XmlNode node in collectorNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null)
                {
                    continue;
                }

                string collectPath = element.GetAttribute("CollectPath");
                if (string.Equals(collectPath, FUIAssetRoot, StringComparison.Ordinal))
                {
                    addressRule = element.GetAttribute("AddressRule");
                    packRule = element.GetAttribute("PackRule");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 通过反射调用 YooAsset.Editor.AssetBundleCollectorSettingData.Setting.BeginCollect，
        /// 运行真实 Collector 收集逻辑（SimulateBuild=true），返回 CollectResult 实例。
        /// </summary>
        /// <param name="packageName">资源包名。</param>
        /// <returns>CollectResult 实例；反射失败或调用异常时返回 null。</returns>
        /// <remarks>
        /// 由于 GameFUI.Tests.asmdef 不引用 YooAsset.Editor 程序集，且本任务禁止修改 asmdef，
        /// 此处通过反射访问 YooAsset.Editor 类型。反射只读取 Collector 结果，不写入配置。
        ///
        /// 调用链：AssetBundleCollectorSettingData.Setting.BeginCollect(packageName, simulateBuild, useAssetDependencyDB)
        /// 返回 CollectResult，其 CollectAssets 字段为 List&lt;CollectAssetInfo&gt;，
        /// 每个 CollectAssetInfo 有 Address（location）与 AssetInfo（含 AssetPath）。
        /// </remarks>
        private static object RunRealCollector(string packageName)
        {
            // 加载 YooAsset.Editor 程序集。
            Assembly editorAssembly = LoadYooAssetEditorAssembly();
            if (editorAssembly == null)
            {
                return null;
            }

            // 获取 AssetBundleCollectorSettingData 类型。
            Type settingDataType = editorAssembly.GetType("YooAsset.Editor.AssetBundleCollectorSettingData");
            if (settingDataType == null)
            {
                return null;
            }

            // 获取静态 Setting 属性。
            PropertyInfo settingProperty = settingDataType.GetProperty("Setting", BindingFlags.Public | BindingFlags.Static);
            if (settingProperty == null)
            {
                return null;
            }

            object setting = settingProperty.GetValue(null);
            if (setting == null)
            {
                return null;
            }

            // 获取 BeginCollect 方法（签名：BeginCollect(string, bool, bool)）。
            MethodInfo beginCollectMethod = setting.GetType().GetMethod(
                "BeginCollect",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string), typeof(bool), typeof(bool) },
                null);

            if (beginCollectMethod == null)
            {
                return null;
            }

            // 调用 BeginCollect(packageName, simulateBuild=true, useAssetDependencyDB=true)。
            return beginCollectMethod.Invoke(setting, new object[] { packageName, true, true });
        }

        /// <summary>
        /// 从 CollectResult 实例提取 Address -> AssetPath 映射。
        /// </summary>
        /// <param name="collectResult">CollectResult 实例（来自 RunRealCollector）。</param>
        /// <returns>Address 到 AssetPath 的字典；反射失败返回 null。</returns>
        /// <remarks>
        /// CollectResult.CollectAssets 为 List&lt;CollectAssetInfo&gt;；
        /// CollectAssetInfo.Address 为 location 字符串；
        /// CollectAssetInfo.AssetInfo.AssetPath 为资源路径。
        /// </remarks>
        private static Dictionary<string, string> ExtractCollectedAddresses(object collectResult)
        {
            if (collectResult == null)
            {
                return null;
            }

            PropertyInfo collectAssetsProperty = collectResult.GetType().GetProperty("CollectAssets");
            if (collectAssetsProperty == null)
            {
                return null;
            }

            object collectAssets = collectAssetsProperty.GetValue(collectResult);
            if (collectAssets == null)
            {
                return null;
            }

            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (object item in (System.Collections.IEnumerable)collectAssets)
            {
                PropertyInfo addressProperty = item.GetType().GetProperty("Address");
                PropertyInfo assetInfoProperty = item.GetType().GetProperty("AssetInfo");

                if (addressProperty == null || assetInfoProperty == null)
                {
                    continue;
                }

                string address = addressProperty.GetValue(item) as string;
                object assetInfo = assetInfoProperty.GetValue(item);
                if (address == null || assetInfo == null)
                {
                    continue;
                }

                FieldInfo assetPathField = assetInfo.GetType().GetField("AssetPath");
                if (assetPathField == null)
                {
                    continue;
                }

                string assetPath = assetPathField.GetValue(assetInfo) as string;
                if (assetPath == null)
                {
                    continue;
                }

                result[address] = assetPath;
            }

            return result;
        }

        /// <summary>
        /// 加载 YooAsset.Editor 程序集，优先从已加载程序集中查找，否则按名称加载。
        /// </summary>
        /// <returns>YooAsset.Editor 程序集；失败返回 null。</returns>
        private static Assembly LoadYooAssetEditorAssembly()
        {
            // 优先从已加载程序集中查找（避免重复加载）。
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in loadedAssemblies)
            {
                if (assembly.GetName().Name == "YooAsset.Editor")
                {
                    return assembly;
                }
            }

            // 未加载时按名称加载（Editor 程序集通常已被 Unity 加载，此处兜底）。
            try
            {
                return Assembly.Load("YooAsset.Editor");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 初始化 YooAssets 全局系统（若尚未初始化），用于 EditorSimulateMode 包创建。
        /// </summary>
        /// <remarks>
        /// YooAssets.Initialize 是幂等的（已初始化时只输出警告），因此可安全重复调用。
        /// 不在此销毁全局系统——销毁会影响其他测试与生产代码复用 YooAssets 静态状态。
        /// </remarks>
        private static void InitializeYooAssetsForEditorMode()
        {
            if (!YooAssets.Initialized)
            {
                YooAssets.Initialize();
            }
        }
    }
}
