using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Cysharp.Threading.Tasks;
using FairyGUI;
using GameFUI;
using GameFUI.Tests.EditMode;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEditor;
using UnityEngine.TestTools;
using UIBattle;
using YooAsset;

namespace GameFUI.Tests.PlayMode
{
    /// <summary>
    /// 任务 7.4：在 YooAsset Editor 模式（EditorSimulateMode）通过实际 Collector 与资源模块
    /// 验证描述、贴图、依赖包的规范 location，并验证首屏不得出现异步占位闪烁。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>本文件 diff 边界归属（任务 8.6 复核用）</b>：
    /// <list type="bullet">
    /// <item>场景 a/b（Collector/CheckLocationValid 验证）属任务 7.4 原有产出；</item>
    /// <item>场景 c（<c>ShowAsync_RealYooAsset_EditorSimulateMode_ClosesLoop</c>）与
    ///   <c>CreateRealResourceModuleForEditorSimulateMode</c> 属任务 8.4 新增，
    ///   8.4 新增改动 = 场景 c 闭环测试 + <see cref="PlayModeTestHarness.SetupWithRealYooAsset"/> 装配入口；</item>
    /// <item><c>GameFUI.Tests.asmdef</c> 的 GameLogic 引用（GUID:6e76b075）属任务 8.5，
    ///   GameFUI.Editor 引用（GUID:e8cdc169）属任务 8.2，二者均非 8.4 所需（8.4 仅依赖
    ///   TEngine.Runtime/GameFUI/FairyGUI/UniTask/YooAsset，asmdef 中已存在）；</item>
    /// <item><see cref="PlayModeTestHarness"/> 的 UGUI 模块装配方法（SetupUGUIModule 等）属任务 8.5，
    ///   本文件不涉及。</item>
    /// </list>
    /// asmdef 为 JSON 无法内嵌注释，<see cref="PlayModeTestHarness"/> 在 8.4/8.5 间共享，
    /// 二者的 diff 边界标注留待任务 8.6 统一处理。
    /// </para>
    /// </remarks>
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
    /// <b>真实 YooAsset ShowAsync 闭环验证（任务 8.4）</b>：反射实例化真实
    /// <c>TEngine.ResourceModule</c>（internal sealed，EditorSimulateMode 初始化 DefaultPackage），
    /// 通过公开 <see cref="FUI.RegisterModule(IResourceModule, FUIOptions)"/> 注册真实资源能力
    /// （FUIModule 内部包装为 <see cref="YooAssetFUIResourceProvider"/>），由测试 owner 注册最终测试
    /// Window/Widget 后 FreezeBindings，再 await <see cref="FUI.ShowAsync{T}"/> 完成纵向闭环。
    /// 断言 <c>UIBattle_fui</c>、<c>UICommon_fui</c>、<c>UICommon_atlas0</c> 真实加载就绪
    /// （PackageRecord.State==Ready、handle 为 <see cref="YooAssetHandleWrapper"/> 且 IsDone、
    /// AssetObject 为真实 Texture2D 非空）、窗口 Open/onStage/visible、依赖包通过规范 location Acquire。
    /// <b>不得仅以 <see cref="ResourcePackage.CheckLocationValid"/> 或内存 provider 替代真实加载</b>
    /// （任务 8.4 明令禁止）。
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>真实加载链路（任务 8.4）</b>：本测试不再用内存 provider 验"首屏无闪烁"，而是走生产同构的
    /// 真实链路：真实 IResourceModule -&gt; <see cref="YooAssetFUIResourceProvider"/> -&gt; YooAsset
    /// EditorSimulateMode -&gt; 实际 Collector 模拟构建产物。ShowAsync 的资源就绪屏障（design.md 决策8）
    /// 在真实异步加载下成立，即 await 返回时描述、图集、依赖包均已真实就绪，首屏不出现异步占位闪烁。
    /// 7.4 此前用内存 provider 完成屏障的写法已被本测试替换为真实 YooAsset 闭环
    /// （7.4 重新勾选依据即本测试通过）。
    /// </para>
    /// <para>
    /// <b>复用证据</b>：
    /// <list type="bullet">
    /// <item>复用 <see cref="PlayModeTestHarness.SetupWithRealYooAsset"/>（8.4 新增，真实 IResourceModule 装配入口，
    ///   与 <see cref="PlayModeTestHarness.SetupForShowAsync"/> 唯一差异是资源能力来源）；</item>
    /// <item>复用 <see cref="PlayModeTestHarness.Cleanup"/>（7.1 产出，全局清理）；</item>
    /// <item>复用 <see cref="PlayModeTestHarness.CreateTypeBasedCreator"/>（7.1 产出，非空 creator 构造）；</item>
    /// <item>复用 <see cref="YooAssetFUIResourceProvider"/>（4.3 产出，包装 IResourceModule 的真实 provider）；</item>
    /// <item>复用 <see cref="YooAssetHandleWrapper"/>（4.3 产出，真实 AssetHandle 包装，用于断言真实加载链路）；</item>
    /// <item>复用 <see cref="PackageLoader.FindRecord"/>（4.4 产出，internal 查询包记录）；</item>
    /// <item>复用 <see cref="PackageRecord"/> / <see cref="PackageRecord.AssetHandles"/> / <see cref="PackageRecord.DescHandle"/>
    ///   （4.4 产出，public 字段查询资源就绪状态）；</item>
    /// <item>复用 <see cref="FUIModule._windowEntries"/>（5.4 产出，internal 查询窗口条目状态）；</item>
    /// <item>复用 <see cref="TestBattleStartPanel"/>（3.6 产出，最终测试业务窗口，含结构契约查询入口
    ///   <see cref="TestBattleStartPanel.HasGComponentButtonWidgetCoexistence"/>）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 边界约束：本测试不修改任何 Runtime/Resource .cs 文件，不修改其他 PlayMode/EditMode 测试文件，
    /// 不修改 YooAsset Collector 配置，不修改 CSV，不依赖 GameLogic/GamePlay/GameBattle，不创建或修改 BattleModule。
    /// 任务 8.4 允许在 <see cref="PlayModeTestHarness"/> 新增真实资源装配入口（<see cref="PlayModeTestHarness.SetupWithRealYooAsset"/>），
    /// 已在该文件中添加；GameFUI.Tests.asmdef 已引用 TEngine.Runtime + YooAsset，真实 IResourceModule 可访问，无需改 asmdef。
    /// 通过反射访问 YooAsset.Editor / TEngine.Runtime 内部类型只读取 Collector 配置与运行结果，不写入。
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
            // 诊断：记录 SetUp 执行（用于 batchmode 下确认测试是否被执行）。
            DiagWrite(GetDiagPath(), $"SetUp 开始 {DateTime.Now:HH:mm:ss.fff}");

            // Stage 会调用 DontDestroyOnLoad，只能在 Play Mode 中初始化。
            if (Application.isPlaying)
            {
                PlayModeTestHarness.EnsureGRootInitialized();
            }

            // 清空 GameFUI 与 FairyGUI 全局状态基线（幂等）。
            PlayModeTestHarness.Cleanup();

            DiagWrite(GetDiagPath(), $"SetUp 完成 {DateTime.Now:HH:mm:ss.fff}");
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
        // 场景 c：真实 YooAsset ShowAsync 闭环验证（任务 8.4）
        // ============================================================

        /// <summary>
        /// 真实 YooAsset 闭环验证（任务 8.4）：在 EditorSimulateMode 下用真实 IResourceModule + 真实
        /// <see cref="FUI.ShowAsync{T}"/> 完成 UIBattle 窗口纵向闭环，断言描述、贴图、依赖包真实加载就绪
        /// 且首屏无异步占位闪烁。不得仅以 <see cref="ResourcePackage.CheckLocationValid"/> 或内存 provider 替代。
        /// </summary>
        /// <remarks>
        /// 任务 8.4：7.4 此前用内存 provider 验"首屏无闪烁"，真实 YooAsset 加载下的 ShowAsync 闭环未直接测试。
        /// 本测试改用真实 IResourceModule（反射实例化 <c>TEngine.ResourceModule</c>，EditorSimulateMode 初始化
        /// DefaultPackage），通过公开 <see cref="FUI.RegisterModule(IResourceModule, FUIOptions)"/> 注册
        /// （由 FUIModule 内部包装为 <see cref="YooAssetFUIResourceProvider"/>），由测试 owner 注册最终测试
        /// Window/Widget 后 FreezeBindings，再 await <see cref="FUI.ShowAsync{T}"/> 完成纵向闭环。
        /// <para>
        /// spec fairygui-hot-update-delivery / Scenario: YooAsset Editor 模式寻址——
        /// Editor 测试通过实际 Collector 和资源模块加载 UIBattle 与 UICommon，
        /// 系统 SHALL 使用规范逻辑包名和 location，且结果 SHALL 与内存资源适配器测试的类型和生命周期契约一致。
        /// </para>
        /// <para>
        /// spec fairygui-package-loading / Requirement: Acquire 成功代表资源已经可用于首屏构造——
        /// 包 Acquire 接口 SHALL 是真实可等待操作。成功返回时，目标包及依赖的描述均已注册，
        /// 窗口首屏可能使用的外部资源已经完成加载，并可由 FairyGUI 在对象构造期间同步取得。
        /// </para>
        /// <para>
        /// 验证项（在 await <see cref="FUI.ShowAsync{T}"/> 返回后立即断言，不得出现"先显示占位再异步填充"的闪烁）：
        /// <list type="bullet">
        /// <item>真实加载证据：UICommon_atlas0 的 handle 为 <see cref="YooAssetHandleWrapper"/>（真实 YooAsset 句柄包装），
        ///   而非 <c>InMemoryAssetHandle</c>——证明走真实 YooAsset 加载链路而非内存 provider；</item>
        /// <item>UIBattle/UICommon 包记录状态均为 <see cref="PackageLoadState.Ready"/>，
        ///   DescHandle 非空且为 <see cref="YooAssetHandleWrapper"/>（描述文件真实加载就绪）；</item>
        /// <item>UICommon_atlas0 handle <see cref="IFUIAssetHandle.IsDone"/> 且 AssetObject 为真实
        ///   <see cref="Texture2D"/>（贴图真实加载就绪，非内存占位）；</item>
        /// <item>窗口状态 <see cref="FUIWindowState.Open"/>、onStage、visible（首屏就绪，无占位闪烁）；</item>
        /// <item>UIBattle DependencyLeases 包含 UICommon（依赖包通过规范 location 真实 Acquire）；</item>
        /// <item>窗口内受管理 Widget 与原生 Button 均构造完成（内容就绪非占位骨架）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 装配使用 <see cref="PlayModeTestHarness.SetupWithRealYooAsset"/>（真实 IResourceModule + 公开
        /// <see cref="FUI.RegisterModule(IResourceModule, FUIOptions)"/>），复用 UIBattleBinder/TestFUIOwner
        /// owner 契约与 <see cref="PlayModeTestHarness.CreateTypeBasedCreator"/>，与
        /// <see cref="PlayModeTestHarness.SetupForShowAsync"/> 唯一差异是资源能力来源（真实 IResourceModule 而非内存 provider）。
        /// </para>
        /// </remarks>
        [UnityTest]
        [Description("真实 YooAsset 闭环：EditorSimulateMode + 真实 IResourceModule + FUI.ShowAsync 完成首屏就绪验收。")]
        [Timeout(30000)] // EditorSimulateMode 模拟构建 + 真实加载可能耗时，30 秒超时防止挂死（参考场景 b）。
        public IEnumerator ShowAsync_RealYooAsset_EditorSimulateMode_ClosesLoop()
        {
            // GameFUI.Tests 是 Editor-only 测试程序集，由 EditMode Test Runner 发现；
            // 进入 Play Mode 后再初始化 FairyGUI Stage 并执行真实纵向闭环。
            yield return new EnterPlayMode();

            PlayModeTestHarness.EnsureGRootInitialized();
            PlayModeTestHarness.Cleanup();

            yield return RunRealYooAssetEditorSimulateModeAsync().ToCoroutine();

            PlayModeTestHarness.Cleanup();
            yield return new ExitPlayMode();
        }

        /// <summary>
        /// 执行真实 YooAsset 纵向验收逻辑，由 PlayMode UnityTest 和诊断入口共同复用。
        /// </summary>
        private static async UniTask RunRealYooAssetEditorSimulateModeAsync()
        {
            // 诊断文件：Unity batchmode 下测试日志不输出到 -logFile，需手动写入诊断文件以获取失败详情。
            string diagPath = GetDiagPath();
            DiagWrite(diagPath, $"=== 测试开始 {DateTime.Now:HH:mm:ss.fff} ===");

            try
            {
                // 1. 创建真实 IResourceModule（反射实例化 ResourceModule，EditorSimulateMode 初始化 DefaultPackage）。
                //    走真实加载链路：IResourceModule -> YooAssetFUIResourceProvider -> YooAsset EditorSimulateMode。
                DiagWrite(diagPath, "步骤1: 创建真实 IResourceModule...");
                IResourceModule resourceModule = await CreateRealResourceModuleForEditorSimulateMode();
                DiagWrite(diagPath, $"步骤1完成: resourceModule={resourceModule?.GetType().Name}, DefaultPackageName={resourceModule?.DefaultPackageName}");

                // 2. 通过公开 FUI.RegisterModule(IResourceModule) 装配模块（真实资源能力，非内存 provider）。
                //    SetupWithRealYooAsset 内部调用 FUI.RegisterModule(IResourceModule, FUIOptions)（公开入口），
                //    FUIModule 把 IResourceModule 包装为 YooAssetFUIResourceProvider。
                DiagWrite(diagPath, "步骤2: SetupWithRealYooAsset...");
                FUIModule module = PlayModeTestHarness.SetupWithRealYooAsset(resourceModule);
                DiagWrite(diagPath, $"步骤2完成: module={module?.GetType().Name}");

                // 3. 通过真实 FUI.ShowAsync 完成纵向闭环（门面静态入口，转发到已注册模块）。
                DiagWrite(diagPath, "步骤3: FUI.ShowAsync<TestBattleStartPanel>...");
                TestBattleStartPanel window = await FUI.ShowAsync<TestBattleStartPanel>();
                DiagWrite(diagPath, $"步骤3完成: window={window?.GetType().Name}, onStage={window?.onStage}, visible={window?.visible}");

                // 4. 断言返回值非空且为最终业务类型。
                Assert.IsNotNull(window, "FUI.ShowAsync 应返回非空最终业务窗口。");
                Assert.IsInstanceOf<TestBattleStartPanel>(window, "FUI.ShowAsync 应返回最终测试业务类型 TestBattleStartPanel。");
                DiagWrite(diagPath, "断言4通过: 返回值非空且为最终业务类型。");

                // 5. 断言窗口状态为 Open（首屏就绪，非 Loading/Opening——后者表示资源未就绪，会出现占位闪烁）。
                Assert.IsTrue(module._windowEntries.TryGetValue(typeof(TestBattleStartPanel), out WindowEntry entry),
                    "FUI.ShowAsync 完成后应存在 TestBattleStartPanel 的 WindowEntry。");
                DiagWrite(diagPath, $"断言5: entry.State={entry.State}");
                Assert.AreEqual(FUIWindowState.Open, entry.State,
                    $"FUI.ShowAsync 返回时窗口状态应为 Open，实际：{entry.State}。" +
                    "非 Open 状态意味着资源未就绪，会出现占位闪烁。");

                // 6. 断言窗口已挂载到 Stage 且可见——首屏不出现"先空后填充"。
                Assert.IsTrue(window.onStage,
                    "FUI.ShowAsync 返回时窗口应已 onStage。若未 onStage，说明窗口尚未被 AddChild 到层级容器，会出现延迟显示。");
                Assert.IsTrue(window.visible,
                    "FUI.ShowAsync 返回时窗口应 visible=true。若 visible=false，首屏不可见，违背首屏就绪语义。");
                DiagWrite(diagPath, "断言6通过: 窗口 onStage 且 visible。");

                // 7. 断言包记录已 Ready——真实资源加载在 Show 完成前已全部就绪。
                PackageRecord uiBattleRecord = PackageLoader.FindRecord(PlayModeTestHarness.UIBattlePkg);
                Assert.IsNotNull(uiBattleRecord, "FUI.ShowAsync 完成后应存在 UIBattle 的 PackageRecord。");
                DiagWrite(diagPath, $"断言7: uiBattleRecord.State={uiBattleRecord.State}");
                Assert.AreEqual(PackageLoadState.Ready, uiBattleRecord.State,
                    $"UIBattle 包记录状态应为 Ready，实际：{uiBattleRecord.State}。" +
                    "非 Ready 表示描述或外部资源未就绪，会出现占位闪烁。");

                PackageRecord uiCommonRecord = PackageLoader.FindRecord(PlayModeTestHarness.UICommonPkg);
                Assert.IsNotNull(uiCommonRecord, "FUI.ShowAsync 完成后应存在 UICommon 依赖包记录（UIBattle 依赖 UICommon）。");
                DiagWrite(diagPath, $"断言7b: uiCommonRecord.State={uiCommonRecord.State}");
                Assert.AreEqual(PackageLoadState.Ready, uiCommonRecord.State,
                    $"UICommon 依赖包记录状态应为 Ready，实际：{uiCommonRecord.State}。依赖未就绪会导致窗口内容缺失或闪烁。");

                // 8. 真实加载证据：断言 atlas handle 为 YooAssetHandleWrapper（真实 YooAsset 句柄），而非 InMemoryAssetHandle。
                //    这证明包加载走真实 IResourceModule -> YooAssetFUIResourceProvider -> YooAsset 链路，非内存 provider。
                //    任务 8.4 明令禁止仅以内存 provider 替代真实加载。
                Assert.IsTrue(uiCommonRecord.AssetHandles.ContainsKey(UICommonAtlasLocation),
                    $"UICommon 包的 AssetHandles 应包含贴图 location '{UICommonAtlasLocation}'。" +
                    "图集未预载会导致首屏贴图迟到，出现白屏闪烁（design.md 决策8：资源就绪屏障与懒加载解耦）。");
                IFUIAssetHandle atlasHandle = uiCommonRecord.AssetHandles[UICommonAtlasLocation];
                DiagWrite(diagPath, $"断言8: atlasHandle类型={atlasHandle?.GetType().Name}, IsDone={atlasHandle?.IsDone}");
                Assert.IsInstanceOf<YooAssetHandleWrapper>(atlasHandle,
                    "UICommon_atlas0 的 handle 应为 YooAssetHandleWrapper（真实 YooAsset 句柄包装），" +
                    "而非 InMemoryAssetHandle。若为内存 handle，说明未走真实 YooAsset 加载链路" +
                    "（任务 8.4 禁止内存 provider 替代真实加载）。");

                // 9. 断言 atlas handle 真实就绪：IsDone 且 AssetObject 为真实 Texture2D（非内存占位）。
                Assert.IsNotNull(atlasHandle, "UICommon_atlas0 的 handle 不应为 null。");
                Assert.IsTrue(atlasHandle.IsDone,
                    "UICommon_atlas0 的 handle 应处于 IsDone 状态。未完成表示贴图仍在异步加载，会出现占位闪烁。");
                Assert.IsNotNull(atlasHandle.AssetObject,
                    "UICommon_atlas0 的 AssetObject 应非空（真实 Texture2D 已加载）。空对象会导致贴图无法渲染。");
                Assert.IsInstanceOf<Texture2D>(atlasHandle.AssetObject,
                    "UICommon_atlas0 的 AssetObject 应为 Texture2D（真实贴图资源），实际类型：" +
                    (atlasHandle.AssetObject == null ? "null" : atlasHandle.AssetObject.GetType().Name) + "。");
                DiagWrite(diagPath, "断言9通过: atlas handle 真实就绪。");

                // 10. 断言描述文件 handle 已真实加载（DescHandle 非空，且为真实 YooAsset 句柄）。
                Assert.IsNotNull(uiBattleRecord.DescHandle,
                    "UIBattle 包的描述文件 handle 应已加载（非空）。描述未就绪会导致 CreateObjectFromURL 失败或占位。");
                Assert.IsInstanceOf<YooAssetHandleWrapper>(uiBattleRecord.DescHandle,
                    "UIBattle DescHandle 应为 YooAssetHandleWrapper（真实 YooAsset 句柄）。");
                Assert.IsNotNull(uiCommonRecord.DescHandle,
                    "UICommon 包的描述文件 handle 应已加载（非空）。依赖描述未就绪会导致依赖包加载失败。");
                DiagWrite(diagPath, "断言10通过: 描述文件 handle 真实加载。");

                // 11. 断言依赖包通过规范 location 真实 Acquire：UIBattle DependencyLeases 包含 UICommon 的租约，
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
                    "UIBattle 的 DependencyLeases 应包含 UICommon 的租约。依赖包通过规范 location（UICommon_fui）" +
                    "真实 Acquire 后，其 PackageName 应为 UICommon（spec：包名作为逻辑身份）。");
                DiagWrite(diagPath, "断言11通过: 依赖包通过规范 location 真实 Acquire。");

                // 12. 断言窗口内容已构造完成（受管理 Widget 与原生 Button 均非空）——结构契约证明非占位。
                Assert.IsTrue(window.HasGComponentButtonWidgetCoexistence(),
                    "FUI.ShowAsync 返回时窗口内受管理 Widget 与原生 Button 应均已构造且非空。" +
                    "若为空，说明 Widget 树 Attach 或构造未完成，窗口显示为占位骨架。");
                Assert.IsNotNull(window.TestWidget, "TestWidget 应非空（受管理 Widget 已 Attach）。");
                Assert.IsNotNull(window.NativeButton, "NativeButton 应非空（原生 Button 已绑定）。");
                Assert.IsTrue(window.TestWidget.onStage,
                    "受管理 Widget 应已 onStage。Widget 未挂载会导致内容缺失。");
                Assert.IsTrue(window.NativeButton.onStage,
                    "原生 Button 应已 onStage。Button 未挂载会导致内容缺失。");
                DiagWrite(diagPath, "断言12通过: 窗口内容构造完成。");

                DiagWrite(diagPath, $"=== 测试全部通过 {DateTime.Now:HH:mm:ss.fff} ===");
            }
            catch (Exception ex)
            {
                // 捕获异常并写入诊断文件，便于 batchmode 下定位失败原因。
                DiagWrite(diagPath, $"!!! 测试失败: {ex.GetType().Name}: {ex.Message}");
                DiagWrite(diagPath, $"!!! 堆栈:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    DiagWrite(diagPath, $"!!! 内部异常: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    DiagWrite(diagPath, $"!!! 内部异常堆栈:\n{ex.InnerException.StackTrace}");
                }
                throw;
            }
        }

        /// <summary>
        /// 获取诊断文件路径（基于 Application.dataPath 构建绝对路径，避免工作目录不确定性）。
        /// </summary>
        private static string GetDiagPath()
        {
            // 使用硬编码项目路径作为兜底，确保 batchmode 下路径可用。
            try
            {
                // Application.dataPath 返回 项目路径/Assets，向上取项目根目录下的 Temp。
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.Combine(projectRoot, "Temp", "8.4-test-diagnostic.log");
            }
            catch
            {
                return @"E:\MyWork\MyTD\TEngine\UnityProject\Temp\8.4-test-diagnostic.log";
            }
        }

        /// <summary>
        /// 写入诊断信息到指定文件（追加模式），用于 Unity batchmode 下获取测试执行详情。
        /// </summary>
        /// <param name="path">诊断文件路径。</param>
        /// <param name="message">诊断消息。</param>
        private static void DiagWrite(string path, string message)
        {
            try
            {
                // 同时写入主路径和兜底路径，确保至少一个能成功。
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");

                // 兜底路径：如果主路径与硬编码路径不同，也写入硬编码路径。
                string fallback = @"E:\MyWork\MyTD\TEngine\UnityProject\Temp\8.4-test-diagnostic.log";
                if (path != fallback)
                {
                    string fallbackDir = Path.GetDirectoryName(fallback);
                    if (!string.IsNullOrEmpty(fallbackDir) && !Directory.Exists(fallbackDir))
                    {
                        Directory.CreateDirectory(fallbackDir);
                    }
                    File.AppendAllText(fallback, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                }
            }
            catch
            {
                // 诊断写入失败不影响测试执行。
            }
        }

        // ============================================================
        // -executeMethod 入口：绕过 Unity Test Framework batchmode bug 直接运行测试
        // ============================================================

        /// <summary>
        /// 诊断：Unity Test Framework 1.6.0 在 batchmode 下 EditMode 测试不执行（框架 bug），
        /// 用 -executeMethod 直接调用测试逻辑，结果写入 Temp/8.4-execute-result.txt。
        /// 用法：Unity.exe -batchmode -projectPath ... -executeMethod GameFUI.Tests.PlayMode.YooAssetEditorVerificationTests.ExecuteTest8_4
        /// </summary>
        public static void ExecuteTest8_4()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string resultPath = Path.Combine(projectRoot, "Temp", "8.4-execute-result.txt");
            ExecuteTest8_4Async(resultPath).Forget(OnExecuteTest8_4Failed);
        }

        private static bool s_test8_4_done;
        private static int s_test8_4_exitCode;

        private static async UniTask ExecuteTest8_4Async(string resultPath)
        {
            s_test8_4_done = false;
            s_test8_4_exitCode = 2;

            // 注册 update 回调，测试完成后退出 Unity
            EditorApplication.update += () =>
            {
                if (s_test8_4_done)
                {
                    EditorApplication.Exit(s_test8_4_exitCode);
                }
            };

            try
            {
                // 写入开始标记
                string startMessage = $"STARTED {DateTime.Now:HH:mm:ss.fff}";
                Debug.Log($"[GameFUI 8.4] {startMessage}，结果文件：{resultPath}");
                TryWriteExecuteResult(resultPath, startMessage, true);

                // 初始化 GRoot（SetUp 等价）
                PlayModeTestHarness.EnsureGRootInitialized();
                PlayModeTestHarness.Cleanup();

                // 创建测试实例并直接调用测试方法（绕过 NUnit/TestFramework）
                await RunRealYooAssetEditorSimulateModeAsync();

                // 测试通过
                string passMessage = $"PASSED {DateTime.Now:HH:mm:ss.fff}";
                Debug.Log($"[GameFUI 8.4] {passMessage}");
                TryWriteExecuteResult(resultPath, passMessage, false);
                s_test8_4_exitCode = 0;
            }
            catch (Exception ex)
            {
                // 测试失败，写入完整异常信息
                Debug.LogException(ex);
                string failMessage =
                    $"FAILED {DateTime.Now:HH:mm:ss.fff}\n{ex.GetType().Name}: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}";
                if (ex.InnerException != null)
                {
                    failMessage +=
                        $"\nInnerException: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}";
                }
                Debug.LogError($"[GameFUI 8.4] {failMessage}");
                TryWriteExecuteResult(resultPath, failMessage, false);
                s_test8_4_exitCode = 2;
            }
            finally
            {
                // TearDown 等价：清理全局状态
                try
                {
                    PlayModeTestHarness.Cleanup();
                }
                catch
                {
                    // 清理失败不影响结果
                }
                s_test8_4_done = true;
            }
        }

        /// <summary>
        /// 记录 executeMethod 验收结果。诊断文件写入失败不得覆盖真实测试结果。
        /// </summary>
        private static void TryWriteExecuteResult(string resultPath, string message, bool overwrite)
        {
            try
            {
                string directory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (overwrite)
                {
                    File.WriteAllText(resultPath, message + "\n");
                }
                else
                {
                    File.AppendAllText(resultPath, message + "\n");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameFUI 8.4] 写入诊断文件失败：{resultPath}\n{ex}");
            }
        }

        /// <summary>
        /// 捕获 executeMethod 入口自身未观察到的异常，确保 Unity 能以失败码退出。
        /// </summary>
        private static void OnExecuteTest8_4Failed(Exception exception)
        {
            Debug.LogException(exception);
            s_test8_4_exitCode = 2;
            s_test8_4_done = true;
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

        /// <summary>
        /// 创建真实 <see cref="IResourceModule"/>（反射实例化 TEngine.Runtime 的 internal ResourceModule），
        /// 并在 YooAsset EditorSimulateMode 下初始化 DefaultPackage（使用实际 Collector 模拟构建结果）。
        /// </summary>
        /// <returns>已初始化 DefaultPackage 的真实 IResourceModule 实例。</returns>
        /// <remarks>
        /// <para>
        /// <b>为何反射实例化</b>：<c>TEngine.ResourceModule</c> 为 internal sealed，GameFUI.Tests 不直接引用其类型，
        /// 故通过反射实例化。GameFUI.Tests.asmdef 已引用 TEngine.Runtime（含 ResourceModule），真实 IResourceModule 可访问。
        /// </para>
        /// <para>
        /// <b>为何跳过 ResourceModule.Initialize()</b>：Initialize() 内部调用
        /// <c>ModuleSystem.GetModule&lt;IObjectPoolModule&gt;()</c> 设置对象池，需要完整 ModuleSystem 基础设施。
        /// <see cref="YooAssetFUIResourceProvider"/> 实际只使用 IResourceModule 的三个方法：
        /// <see cref="IResourceModule.LoadAssetAsyncHandle{T}"/>、<see cref="IResourceModule.CheckLocationValid"/>、
        /// <see cref="IResourceModule.HasAsset"/>。这三者在 ResourceModule 中均路由到 YooAssets 静态/ResourcePackage
        /// 全局注册表（不经过对象池），因此跳过 Initialize() 不影响 GameFUI 包加载链路。
        /// </para>
        /// <para>
        /// <b>为何调用真实 InitPackage</b>：仅调用 <c>package.InitializeAsync</c> 只会创建 Editor 文件系统，
        /// 不保证激活资源清单；<see cref="IResourceModule.InitPackage"/> 在
        /// <c>needInitMainFest=true</c> 时还会请求版本并更新 Manifest，确保后续真实 AssetHandle 加载可用。
        /// 测试临时把 EditorPlayMode 设为 EditorSimulateMode，并在初始化结束后恢复原值。
        /// </para>
        /// <para>
        /// <b>幂等性</b>：YooAssets.Initialize 与 package 创建/初始化均幂等。若场景 b 已初始化 DefaultPackage，
        /// 本方法复用之（跳过重复初始化），仅补设默认包。
        /// </para>
        /// </remarks>
        private static async UniTask<IResourceModule> CreateRealResourceModuleForEditorSimulateMode()
        {
            // 1. 初始化 YooAssets 全局系统（幂等）。
            InitializeYooAssetsForEditorMode();

            // 2. 创建或复用 DefaultPackage（EditorSimulateMode）。
            ResourcePackage package = YooAssets.TryGetPackage(DefaultPackageName);
            if (package == null)
            {
                package = YooAssets.CreatePackage(DefaultPackageName);
            }

            // 设为默认包：ResourceModule.CheckLocationValid/LoadAssetAsyncHandle 在 packageName 为空时
            // 路由到 YooAssets 静态接口（使用默认包），必须设置才能使真实 IResourceModule 的默认包路径可用。
            YooAssets.SetDefaultPackage(package);

            // 3. 反射实例化 TEngine.ResourceModule（internal sealed），不调用 Initialize()。
            Type resourceModuleType = LoadTEngineRuntimeResourceModuleType();
            Assert.IsNotNull(resourceModuleType,
                "应能从已加载程序集反射获取 TEngine.ResourceModule 类型（TEngine.Runtime 程序集）。");

            object instance = Activator.CreateInstance(resourceModuleType, nonPublic: true);
            Assert.IsNotNull(instance, "应能反射实例化 TEngine.ResourceModule（默认无参构造，internal 可见）。");

            IResourceModule resourceModule = (IResourceModule)instance;
            resourceModule.DefaultPackageName = DefaultPackageName;

            // 4. 复用真实资源模块初始化路径，并请求/激活 EditorSimulateMode 构建清单。
            const string editorPlayModeKey = "EditorPlayMode";
            bool hadEditorPlayMode = EditorPrefs.HasKey(editorPlayModeKey);
            int previousEditorPlayMode = EditorPrefs.GetInt(editorPlayModeKey, (int)EPlayMode.EditorSimulateMode);
            EditorPrefs.SetInt(editorPlayModeKey, (int)EPlayMode.EditorSimulateMode);

            try
            {
                InitializationOperation initOp = await resourceModule.InitPackage(
                    DefaultPackageName,
                    needInitMainFest: true);

                Assert.IsNotNull(initOp, "ResourceModule.InitPackage 应返回初始化操作。");
                Assert.AreEqual(EOperationStatus.Succeed, initOp.Status,
                    $"DefaultPackage 在 EditorSimulateMode 初始化应成功，错误：{initOp.Error}。");
            }
            finally
            {
                if (hadEditorPlayMode)
                {
                    EditorPrefs.SetInt(editorPlayModeKey, previousEditorPlayMode);
                }
                else
                {
                    EditorPrefs.DeleteKey(editorPlayModeKey);
                }
            }

            return resourceModule;
        }

        /// <summary>
        /// 从已加载程序集反射获取 <c>TEngine.ResourceModule</c> 类型（internal sealed，位于 TEngine.Runtime 程序集）。
        /// </summary>
        /// <returns>ResourceModule 类型；未找到返回 null。</returns>
        /// <remarks>
        /// 遍历已加载程序集按全名查找，避免硬编码程序集名（与 <see cref="LoadYooAssetEditorAssembly"/> 的兜底策略一致）。
        /// </remarks>
        private static Type LoadTEngineRuntimeResourceModuleType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("TEngine.ResourceModule");
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
