# S06 证据索引

说明：`OLD` 指 `D:\Work\SAUnity\ProjectOld`，`CURRENT` 指 `E:\MyWork\MyFramework\TEngine\UnityProject`。外部工具路径以 `E:\MyWork\MyFramework\TEngine` 为基准单独注明。行号按本次读取到的工作树文本确认；二进制本体不以“反编译内容”作为证据，只引用其 `.meta` 或同目录源码。归属字段用于区分自写框架、第三方原始 API、适配器、生成模板和业务调用。

## 基线与路径

### S06-E001 — Unity 版本与仓库基线

- 项目：OLD / CURRENT
- 位置：`ProjectSettings/ProjectVersion.txt:1`；OLD 另有 `Tools/assembly` SVN 工作副本，CURRENT Git `HEAD=16afccb5df2a2a2efcb5003ecf9fbc0781c0170a`
- 符号/字段：`m_EditorVersion`
- 摘录：两边均为 `2022.3.62f2`；OLD 根目录无 Git 元数据，`Tools/assembly` 报 SVN revision `45216`。
- 支持结论：静态对比使用同一 Unity 主版本；旧工程没有可用于整库 diff 的根 Git 基线，文档以绝对路径和文件哈希复核。
- 性质：配置/验证；归属：工程环境。

### S06-E002 — 旧编辑器路径和代码生成路径配置

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/EditorFrameworkConfig.cs:15-28,71-74,102-124`
- 符号/字段：`EditorFrameworkConfig.FrameworkPath`、`ConfigCodePath`、`OutputPath`、`GenCodeFolder`、`Inst`
- 摘录：默认配置代码目录为 `Assets/Scripts/Game/Data/Config`，输出根为 `output`；`Inst` 从 `Assets/Resources/Editor/framework_cfg.asset` 加载。
- 支持结论：旧工具的路径不是调用方临时拼接的自由值，而由编辑器配置和固定相对根共同决定；缺少配置资产会直接抛出异常。
- 性质：定义/配置；归属：自写框架。

## 旧工程构建与收集

### S06-E003 — 旧一键入口串联生成、配置资产和构建

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/AssetBundle/AssetBundlePanel.cs:96-120,324-363`
- 符号/方法：`AssetBundlePanel.BuildAll`、`OnAction`
- 摘录：`BuildAll` 先设 `UseYooAsset=true`、生成版本号，调用 `GenCodeTool.GenCode()`、`Cfg2AssetsTool.EncodeAllCfg()`，再调用 `AssetBundleBuilder.Build()` 和 `CopyAssetBundle()`；`OnlyBuild` 明确跳过前两步。
- 支持结论：旧工程有一个可见的编辑器编排入口，但“完整构建”和“仅打包”是两个不同契约；菜单注释明确目标为 DefaultPackage + RawPackage。
- 性质：调用；归属：自写框架/编辑器入口。

### S06-E004 — 旧收集器构建前的预处理入口

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/AssetBundle/AssetBundleImporter.cs:244-370`
- 符号/方法：`AssetBundleImporter.Build`
- 摘录：按 `abType` 进入前置处理，随后刷新 AssetDatabase、读取/生成 `ab_mapping.asset`，解析 `output/ab.json` 并保存 `output/AssetBundleTarget.txt`；异常被记录到日志。
- 支持结论：收集/映射属于构建前阶段，且部分输出是编辑器资产或 output 目录中的中间契约；不是单纯调用第三方 BuildPipeline。
- 性质：调用/状态；归属：自写框架适配层。

### S06-E005 — 旧构建收尾和清理

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/AssetBundle/AssetBundleBuilder.cs:814-843`
- 符号/方法：`Build`、`Clear`
- 摘录：构建成功后调用 `EditorHelper.InvokeExportProcessor("OnPostExportAssetBundle")`，刷新资源并写成功日志；`Clear` 删除 `ab.json`、AssetBundle 输出、StreamingAssets 的 AB 目录和 AAB 目录。
- 支持结论：后置处理只在底层构建返回成功后触发；清理是路径定向删除而非全 StreamingAssets 清空，且保留 video/wwise 的原因写在源码注释中。
- 性质：调用/清理；归属：自写框架。

### S06-E006 — 旧 `[Export]` 处理器的业务耦合

- 项目：OLD
- 位置：`Assets/Editor/Export/ExportProcessor.cs:11-50,55-78`
- 符号/方法：`ExportProcessor.OnPreExportAssetBundle`、`OnPostExportAssetBundle`、`OnPreExportProject`
- 摘录：预处理按 `PackageMode` 调用 `MakeInterface.HybridCLRGenerateAll` 或 `ExportHybridCLRDLL`，之后 `Cfg2AssetsTool.EncodeAllCfg()` 和 `SoDataDecoder.SaveToAsset()`；Wwise 资源另行拷贝。
- 支持结论：旧构建阶段把框架构建、HybridCLR、配置资产化、SoData 和业务音频复制放在同一业务处理器中；这是真实的机器/业务耦合，不应误记为 YooAsset 自带保证。
- 性质：调用；归属：业务导出处理器/自写适配。

### S06-E007 — 旧反射式扩展发现机制

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/Common/EditorHelper.cs:849-884`
- 符号/方法：`InvokeExportProcessor`
- 摘录：遍历当前 AppDomain 的已加载程序集，筛选带 `ExportAttribute` 的类型，再用静态、公开/非公开方法标志调用指定方法。
- 支持结论：扩展点按“当前已加载程序集 + 类型属性 + 方法名”发现；源码未提供多处理器排序、异常隔离或唯一实现保证，因此定义存在不等于实际被调用。
- 性质：定义/调用；归属：自写框架反射适配层。

### S06-E008 — 旧 DefaultPackage 收集规则

- 项目：OLD
- 位置：`Assets/Resources/AssetBundleCollectorSetting.asset:15-40`
- 符号/字段：`Packages[DefaultPackage]`、`AppConfig` group
- 摘录：DefaultPackage 使用 `SilentNormalIgnoreRule`，`Assets/GameRes/AppConfig` 采用 `AddressByFileName`、`PackByUserData`、`CollectByUserData`。
- 支持结论：包名、忽略规则、路径、地址规则、打包规则和筛选规则共同构成旧资源构建输入；仅复制包名不能复刻其收集语义。
- 性质：配置；归属：自写项目配置 + YooAsset 配置格式。

### S06-E009 — 旧 RawPackage 及热更原始文件规则

- 项目：OLD
- 位置：`Assets/Resources/AssetBundleCollectorSetting.asset:774-829`
- 符号/字段：`Packages[RawPackage]`、`HotUpdate`、`Video`、`WWise`
- 摘录：RawPackage 开启 `IncludeAssetGUID`，使用 `RawFileIgnoreRule`；热更使用 `PreserveExtensionPackRule`，视频和 Wwise 也在 RawPackage 中单独收集。
- 支持结论：旧工程的 RawPackage 不是 DefaultPackage 的别名，而是包含不同 Ignore/Pack/资源类型契约的独立包。
- 性质：配置；归属：自写项目配置/适配规则。

### S06-E010 — 旧 YooAsset 双包编排与参数

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/AssetBundle/YooAssetBuildHelper.cs:77-129,170-205,242-263,877-1025`
- 符号/方法：`BuildOptions.Default`、`BuildAssetBundles`、`BuildPackage`、`CreateBuildParameters`
- 摘录：默认包取 `YooAssetConfig.Instance.DefaultPackageName`；`BuildAssetBundles` 先 `BuildPackage(Default())` 再 `BuildPackage(Raw())`；参数包含 target、输出根、StreamingAssets 根、包名、版本和复制策略；完成后统一更新 `version_config.json`。
- 支持结论：旧构建输出由包名/平台/版本/管线/首包复制策略共同确定，并且双包成功后才更新版本配置；构建 helper 还保存最后一次错误、输出目录和版本。
- 性质：定义/调用；归属：自写 YooAsset 适配层。

## 旧配置、通用代码与程序集生成

### S06-E011 — 旧 CSV→配置资产入口和发布路径

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/Config/ConfigTool.cs:21-53,222-280`
- 符号/方法：`CreateConfigCode`、`EncodeAllCfg`、`EncodeCfgByDecoders`
- 摘录：选中的 `.csv` 可通过 `Assets/创建配置文件代码` 进入生成；批量编码时读取 `FrameworkConfig.GetCsvPath(resName)`，发布分支固定为 `FrameworkConfig.GetConfigPath(resName, true)`，即 `Assets/GameRes/GameConfig/<name>.asset` 形态。
- 支持结论：旧配置链把 CSV 读取、代码生成和 `.asset` 编码分为两个入口，但一键构建会把配置资产化纳入打包前步骤；源码把 `releaseBuild` 最终硬置为 `true`，这是实际行为而非字段名推断。
- 性质：调用/路径；归属：自写框架。

### S06-E012 — 旧 CSV→三类 C# 文件和命名规则

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/Config/CfgCodeCreator.cs:47-118,121-182,371-391`
- 符号/方法：`Do`、`CreateClsFile`、`SaveFile`
- 摘录：解析 CSV 后按模板创建 data、define、`_Extend` 三个文件；配置名会派生 `FooCfgData`、`FooDao`、`FooDecoder`，输出根为 `EditorFrameworkConfig.ConfigCodePath + /<configName>`；扩展文件已存在时不重写，写文件异常只记录错误。
- 支持结论：旧生成边界是“生成定义/数据/扩展骨架，扩展文件尽量保留”；路径、类名和是否重写是运行时/业务代码可感知的隐式契约。
- 性质：定义/生成；归属：自写生成器/模板。

### S06-E013 — 旧生成配置的运行时资产读取

- 项目：OLD
- 位置：`Assets/Scripts/framework/Library/ZeroFramework/Config/BaseDao.cs:199-268,463-496`
- 符号/方法：`LoadConfig`、`AssetReader.Read`
- 摘录：非编辑器或开启 YooAsset 时选 `AssetReader`；`GetConfigPath` 委托到 `FrameworkConfig.GetConfigPath`，最后调用 `LoadMgr.Inst.LoadAsset<BaseCfgData<TConfig>>(path)`；加载器不可用或数据为空会返回明确错误状态。
- 支持结论：`.asset` 生成物的运行时消费者是 DAO→LoadMgr 链，不是生成器本身；编辑器/Windows 还存在 CSV 回退分支，不能把所有平台的读取方式混为一谈。
- 性质：调用/状态；归属：自写运行时框架。

### S06-E014 — 旧通用代码生成协议

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/Common/GenCodeTool.cs:10-50,52-133`
- 符号/方法：`GenCode`、`GenCodeByType`
- 摘录：菜单 `Tools/Framework/CodeGenerator/生成代码` 反射查找 `IGG.Framework.ICodeGenerator`；provider 必须有无参构造、`Get` 和 `WriteCode`；列表每 1000 项拆成 `AddItem_N`，输出包在 `#if !UNITY_EDITOR` 下并写入 `Assets/<GenCodeFolder>/<namespace>/<class>.cs`。
- 支持结论：协议的输入是 provider 运行时返回的对象列表，输出是非编辑器代码；分段阈值是 Windows 方法体限制的规避策略，不能当成性能测量结论。
- 性质：定义/调用；归属：自写框架。

### S06-E015 — 旧通用生成器实际 provider 未证实

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/Panel/UIBindProviderUtil.cs:297-328`；`Assets/Scripts/game/Module/GameDebug/Command/CmdMethodProvider.cs:17-20`
- 符号/代码：`UIBindProvider` 使用示例、`CmdMethodProvider`
- 摘录：UIBind provider 位于块注释内；CmdMethodProvider 的 `:ICodeGenerator<CmdMethodVo>` 也被注释，并说明暂时改用反射执行。
- 支持结论：已确认通用入口和协议，未确认旧工程当前编译/加载域中存在可执行 provider；“一键调用 GenCodeTool”不等于本次会有生成文件。
- 性质：定义/验证；归属：自写工具示例/业务工具；状态：待核验。

### S06-E016 — 旧命令行 JSON/Base64 输入契约

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/Export/CommandHelper.cs:24-119,300-372`
- 符号/方法：`GetValue`、`InitFromCommandLine`
- 摘录：BatchMode 从 `-executeParams` 读取 Base64 UTF-8 JSON；非 BatchMode 读取 `./output/unity_params.json`；初始化会强制 `UseYooAsset=true`，将 `BundleVersion` 写入 `YooAssetConfig.AppVersion` 和 `FrameworkConfig.MainVersion`，最后保存配置。
- 支持结论：旧 CLI 的输入不仅是 Unity 参数，还会改变工程资产配置；同名版本字段和输出路径必须保持一致，否则会出现“命令参数已读但构建仍使用旧版本”的风险。
- 性质：定义/调用；归属：自写工具适配层。

### S06-E017 — 旧 Python→Unity 执行与结果协议

- 项目：OLD
- 位置：`Tools/make/plugin/base.py:196-261`
- 符号/方法：`RunUnity`
- 摘录：运行前删除 `output/unity_result.json`，把 JSON 参数写入 `unity_params.json` 并 Base64 后拼入 `-executeParams`；命令包含 `-batchmode -quit -projectPath -executeMethod -logFile -buildTarget`；成功后解析结果 JSON，失败则解析 Unity 日志。
- 支持结论：旧自动化工具定义了输入、日志、结果文件和退出判定四件套；删除结果文件和解析成功标志是调用者侧的状态管理，不是 Unity 菜单天然提供的语义。
- 性质：调用/协议；归属：自写 Python 工具。

### S06-E018 — 旧 HybridCLR 安装、预处理和 DLL 导出入口

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/Export/MakeInterface.cs:90-127,160-246`
- 符号/方法：`BuildProject`、`BuildHotUpdate`、`HybridCLRInstall`、`HybridCLRGenerateAll`、`ExportHybridCLRDLL`
- 摘录：构建入口先关闭 Cache Server、执行本地 HybridCLR 安装，再进入 `ExportProject`；安装源固定为 `HybridCLRData/il2cpp_plus_repo/libil2cpp`，缺少 `hybridclr` 子目录会抛错；Generate/Export 失败记录后重新抛出。
- 支持结论：旧热更构建将工具链安装状态、预处理生成和 DLL 导出纳入入口前置条件，并明确依赖本地提交源；它不是单纯把一个 DLL 拷贝到资源目录。
- 性质：调用/失败；归属：自写构建适配器 + HybridCLR 第三方 API。

### S06-E019 — 旧 HybridCLR 本地 patch 同步三层兜底

- 项目：OLD
- 位置：`Assets/Editor/HybridCLR/HybridCLRPatchSync.cs:1-27,47-72,74-153`
- 符号/方法：`HybridCLRPatchSync.SyncAll`、`HybridCLRPatchPreBuild.OnPreprocessBuild`
- 摘录：以 `HybridCLRData/il2cpp_plus_repo` 为唯一源，按 marker 复制到本地 IL2CPP 副本；Editor 启动 delayCall、菜单和 `IPreprocessBuildWithReport` 最晚回调都可触发同步，源缺 marker 记为错误。
- 支持结论：这是自写的外部源管理适配器，拥有“源/目标/marker/时机”契约；它不保证自动清理目标，也不应被描述为 HybridCLR 自带行为。
- 性质：定义/调用；归属：自写适配器。

## 当前工程构建、热更与生成

### S06-E020 — 当前菜单和 BuildWithConfig 编排

- 项目：CURRENT
- 位置：`Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:68-110,119-158`
- 符号/方法：`BuildCurrentPlatformAB`、`AutomationBuildAndroid`、`BuildWithConfig`
- 摘录：菜单入口创建 `BuildConfig`，可选先调用 `BuildDLLCommand.BuildAndCopyDlls()`，再刷新 AssetDatabase、执行 AssetBundle、可选最小包和 Player。
- 支持结论：当前主构建链是参数对象驱动的单入口，但它没有在此处调用旧 `GenCodeTool`、`Cfg2AssetsTool` 或当前 `LubanTools`；代码生成是独立菜单契约。
- 性质：调用；归属：自写当前框架。

### S06-E021 — 当前 YooAsset 参数和 DefaultPackage 硬编码

- 项目：CURRENT
- 位置：`Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:165-214,216-269`
- 符号/方法：`BuildInternalWithConfig`、`BuildInternal`
- 摘录：构建输出根可相对项目根归一化，内置文件根来自 StreamingAssets；`PackageName = "DefaultPackage"`，版本来自 `BuildConfig.PackageVersion`（旧兼容函数默认 `"1.0"`），最终直接 `pipeline.Run`。
- 支持结论：当前构建的可变输入主要是 BuildConfig，但包名仍固定；CLI 旧兼容函数和参数化入口不是同一条完整契约，调用者必须确认版本是否真正进入 `BuildParameters`。
- 性质：定义/调用；归属：自写 YooAsset 适配层。

### S06-E022 — 当前 BuildConfig 和 EditorPrefs 持久化

- 项目：CURRENT
- 位置：`Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs:8-47`；`Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs:86-116,468-512,571-636`
- 符号/字段：`BuildConfig`、`BuildPipelineWindow.LoadSettings/SaveSettings`
- 摘录：配置对象包含目标、管线、压缩、版本、输出根、最小包、依赖数据库、复制策略、DLL 和 Player 字段；窗口用 `TEngine_BP_*` 的 `EditorPrefs` 键加载/保存。
- 支持结论：当前编辑器 UI 的构建配置不是 ScriptableObject 资产，而是用户机器级 EditorPrefs；这会影响可复现性、团队共享和 CLI 与窗口之间的输入边界。
- 性质：定义/状态；归属：自写当前编辑器工具。

### S06-E023 — 当前 HybridCLR DLL/AOT 复制边界

- 项目：CURRENT
- 位置：`Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs:86-107,136-173`
- 符号/方法：`BuildAndCopyDlls`、`CopyAOTHotUpdateDlls`、`CopyAOTAssembliesToAssetPath`、`CopyHotUpdateAssembliesToAssetPath`
- 摘录：先编译再复制 AOT 元数据和热更 DLL；目标目录为 `Application.dataPath + UpdateSetting.AssemblyTextAssetPath`，文件统一追加 `.bytes`；AOT 缺失只 `LogError` 后继续，热更 DLL 直接 `File.Copy`。
- 支持结论：当前运行时程序集资产边界明确为 `Assets/AssetRaw/DLL/*.dll.bytes`（按默认设置），但缺失 AOT 与缺失热更 DLL 的失败语义不一致；热更源文件不存在时可能由复制抛异常。
- 性质：调用/失败；归属：自写当前适配器 + HybridCLR API。

### S06-E024 — 当前 UpdateSetting→HybridCLRSettings 同步

- 项目：CURRENT
- 位置：`Assets/TEngine/Runtime/Core/UpdateSetting.cs:71-90`；`Assets/TEngine/Editor/Utility/UpdateSettingEditor.cs:27-104`
- 符号/字段：`HotUpdateAssemblies`、`AOTMetaAssemblies`、`ForceUpdateAssemblies`
- 摘录：UpdateSetting 使用带 `.dll` 的 `GameProto.dll`、`GameLogic.dll` 和 AOT 列表；编辑器同步时剥离扩展名写入 HybridCLR hotUpdateAssemblies，并保存 HybridCLR 设置。
- 支持结论：当前以 UpdateSetting 作为面向项目的程序集清单，以 HybridCLRSettings 作为工具配置副本；二者有自动同步但仍有手动字段和资源路径约束。
- 性质：定义/调用；归属：自写当前配置适配器。

### S06-E025 — 当前收集器与旧 RawPackage 的差异

- 项目：CURRENT
- 位置：`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset:15-204`
- 符号/字段：`Packages`、`DefaultPackage` groups
- 摘录：当前 DefaultPackage 使用 `NormalIgnoreRule`、地址化和 `Assets/AssetRaw/{Actor,Audios,Configs,DLL,Effects,Fonts,Materials,Scenes,UI,UIRaw}` 收集；同一资产中还能看到 Other/Dlc 包名，但未看到旧式 `RawPackage`。
- 支持结论：当前 DLL、配置 bytes 和 UI raw 都进入 AssetRaw/DefaultPackage 的收集模型；不能直接把旧 RawPackage 的 PreserveExtension/IncludeAssetGUID 契约假定为当前行为。
- 性质：配置；归属：当前项目配置/YooAsset 格式。

### S06-E026 — 当前 Luban schema、输入和输出契约

- 项目：CURRENT（外部工具基准 `E:\MyWork\MyFramework\TEngine\Configs\GameConfig`）
- 位置：`Configs/GameConfig/luban.conf:1-21`；`Configs/GameConfig/gen_code_bin_to_project_lazyload.bat:4-21`
- 符号/字段：`schemaFiles`、`targets.client`、`DATA_OUTPATH`、`CODE_OUTPATH`
- 摘录：schema 使用 `Defines`、`Datas/__tables__.xlsx`、`__beans__.xlsx`、`__enums__.xlsx`；client target 的 topModule 为 `GameConfig`；代码输出到 `UnityProject/Assets/GameScripts/HotFix/GameProto/GameConfig/`，二进制输出到 `UnityProject/Assets/AssetRaw/Configs/bytes/`。
- 支持结论：当前配置生成的事实源是仓库外置 Luban 配置/批处理，输出分为 GameProto C# 和 AssetRaw bytes 两条目录；它没有被当前 ReleaseTools 主构建自动调用的源码证据。
- 性质：配置/调用；归属：第三方 Luban + 自写项目脚本。

### S06-E027 — 当前 Luban 自定义模板的运行时消费者契约

- 项目：CURRENT（外部工具基准）
- 位置：`Configs/GameConfig/CustomTemplate/ConfigSystem.cs:9-56`；`CustomTemplate/CustomTemplate_Client_LazyLoad/cs-bin/tables.sbn:14-48`
- 符号/方法：`ConfigSystem.LoadByteBuf`、模板 `Tables` 属性
- 摘录：`ConfigSystem` 从 TEngine `IResourceModule` 加载 `TextAsset` 并包装为 `ByteBuf`；lazy 模板的每个表第一次访问时调用 `defaultLoader(output_data_file)`，随后 `ResolveRef`。
- 支持结论：当前 Luban 方案将生成类型/表管理与资源模块连接，表是按访问惰性加载；生成物缺失、location 不匹配或资源模块不可用的错误处理不在模板中被统一包装。
- 性质：定义/生成模板；归属：自写模板/当前运行时接口。

### S06-E028 — 当前 Luban 菜单与外部进程 I/O

- 项目：CURRENT
- 位置：`Assets/TEngine/Editor/LubanTools/LubanTools.cs:8-18`；`Assets/TEngine/Editor/Utility/ShellHelper.cs:107-153`
- 符号/方法：`LubanTools.ZhuanXiaoYi`、`ShellHelper.RunByPath`
- 摘录：Windows 菜单拼接 `Application.dataPath + "/../../Configs/GameConfig/gen_code_bin_to_project_lazyload.bat"` 后启动进程；`RunByPath` 异步读取输出、记录 PID，但不等待退出码，也不返回生成结果。
- 支持结论：当前转表入口是“启动外部脚本”的桥接，不是有成功/失败结果的同步构建阶段；脚本失败、输出不完整和 Unity Refresh 的先后关系需要调用方额外验证。
- 性质：调用/协议；归属：自写当前编辑器适配器。

### S06-E029 — 当前 UI 生成设置和输出分层

- 项目：CURRENT
- 位置：`Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs:8-53,100-147`
- 符号/字段：`genCodePath`、`impCodePath`、`Namespace`、`ScriptGenerateRule`
- 摘录：默认生成路径为 `Assets/GameScripts/HotFix/GameLogic/UI/Gen`，实现路径为 `Assets/GameScripts/HotFix/GameLogic/UI`；规则由 `m_go`、`m_item`、`m_rect` 等前缀映射到组件类型。
- 支持结论：当前 UI 工具已经显式区分可重生成绑定目录和实现目录，并把命名规则/命名空间放入 ScriptableObject；这比仅靠约定扫描更容易形成可审计边界。
- 性质：定义/配置；归属：自写当前编辑器工具。

### S06-E030 — 当前 UI 自动生成文件保护与 partial 边界

- 项目：CURRENT
- 位置：`Assets/Editor/UIScriptGenerator/ScriptAutoGenerator.cs:89-94,215-247,367-436`
- 符号/方法：`GenerateCSharpScript`、`GenerateImpCSharpScript`
- 摘录：自动文件名为 `<name>_Gen.g.cs`，写入后设为只读；再次生成会先解除只读并删除；实现文件写入前若已存在则警告并跳过，生成类使用 `partial`。
- 支持结论：当前 UI 生成器定义了“工具文件可覆盖、实现文件不覆盖”的所有权约束；但工作树中未发现配置的 `UI/Gen` 目录，不能据此证明某个现有 UI 文件就是该工具当前产物。
- 性质：定义/生成；归属：自写当前生成器；状态：部分待核验。

### S06-E031 — 当前 UI 运行时消费生成代码的顺序

- 项目：CURRENT
- 位置：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs:314-349,427-458`
- 符号/方法：`InternalCreate`、`InternalDestroy`
- 摘录：创建顺序为 `Inject → ScriptGenerator → BindMemberProperty → RegisterEvent → OnCreate`；销毁时先移除 UI 事件、销毁子项、清回调，再销毁面板对象。
- 支持结论：UI 生成代码若实现 `ScriptGenerator`/绑定或事件注册，必须满足这个生命周期顺序；生成器的输出不只是静态字段，实际会进入 UI runtime 生命周期。
- 性质：调用/生命周期；归属：自写当前运行时。

### S06-E032 — 当前 UI 现有脚本的生成/手写混合形态

- 项目：CURRENT
- 位置：`Assets/GameScripts/HotFix/GameLogic/UI/BattleMainUI/BattleMainUI.cs:7-27`
- 符号/类型：`BattleMainUI`
- 摘录：类中保留“脚本工具生成的代码”区域，包含字段和 `ScriptGenerator` 查找路径；事件区域为空，文件名不是工具约定的 `_Gen.g.cs`。
- 支持结论：当前至少存在“手写类承载生成区域”的形态；它不能直接作为自动生成目录/只读文件契约的证明，复刻时需先决定是否迁移到 partial 双文件。
- 性质：业务调用/生成边界；归属：当前业务脚本 + 自写工具产物形态。

### S06-E033 — 当前 Roslyn 事件生成器的输入/输出规则

- 项目：CURRENT（源码在 `E:\MyWork\MyFramework\TEngine\Tools\GameEventSourceGenerator`）
- 位置：`Tools/GameEventSourceGenerator/SourceGenerator/Generator/EventInterfaceGenerator.cs:9-74,76-106,229-313`
- 符号/方法：`EventInterfaceGenerator.Execute`、`GenerateGameEventHelper`、`GenerateImplementationClass`
- 摘录：扫描带 `EventInterface` 标记的接口，生成 `<Interface>_Event.g.cs`、`<Interface>_Gen.g.cs`、`GameEventHelper.g.cs` 和 `EventCenter.g.cs`；生成的 `_Gen` 构造函数注册包装接口，方法把参数转发给 dispatcher。
- 支持结论：当前事件生成器的源级契约是“接口标记→事件 ID/包装器/Helper/Center”，输出由 Roslyn `AddSource` 提供，不需要把生成文件写入工作树。
- 性质：定义/生成；归属：自写 Roslyn 生成器。

### S06-E034 — 当前 Roslyn 工件存在但导入状态需核验

- 项目：CURRENT
- 位置：`Assets/TEngine/Runtime/Core/GameEvent/SourceGenerator.dll.meta:1-26`；同目录 `SourceGenerator.dll`、`GameEventAnalyzer.dll`
- 符号/字段：`.meta` 的 `labels`、`PluginImporter.platformData`
- 摘录：两个 DLL 物理存在且 `.meta` 标记 `RoslynAnalyzer`；SourceGenerator 的 Editor 平台字段显示 `enabled: 0`。
- 支持结论：当前工作区确实保存了 Roslyn 工具工件，但仅凭文件和标签不能确认 Unity 当前编译是否加载它；应把“源码和工件存在”与“生成输出实际激活”分开。
- 性质：配置/验证；归属：自写生成器工件；状态：待核验。

### S06-E035 — 当前事件消费者依赖生成符号

- 项目：CURRENT
- 位置：`Assets/GameScripts/HotFix/GameLogic/IEvent/ILoginUI.cs:3-11`；`Assets/GameScripts/HotFix/GameLogic/GameApp.cs:17-40`
- 符号/类型：`ILoginUI`、`GameApp.Entrance`
- 摘录：`ILoginUI` 带 `[EventInterface(EEventGroup.GroupUI)]`；热更入口第一步调用 `GameEventHelper.Init()`，之后显示 `BattleMainUI`。
- 支持结论：业务源码对事件生成结果有编译/运行依赖；但本次没有执行 Unity 编译，也没有在非缓存工作树中找到生成的 `GameEventHelper.g.cs`，最终可达性仍待核验。
- 性质：定义/调用；归属：当前业务调用 + 生成器消费者。

### S06-E036 — 当前命令行入口与实际实现不一致

- 项目：CURRENT
- 位置：`Assets/TEngine/Editor/Utility/CommandLineReader.cs:5-22`；`Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:22-62,216-250`；`BuildCLI/path_define.bat:3-6`、`BuildCLI/build_android.bat:3-6`
- 符号/方法：`ReleaseTools.BuildDll`、`BuildAssetBundle`、`AutomationBuildAndroid`
- 摘录：注释示例写 `TEngine.ReleaseTools.BuildPackage`；实际 Android 脚本调用 `AutomationBuildAndroid`；`BuildAssetBundle` 读取 `packageVersion` 后调用未传该参数的 `BuildInternal(target, outputRoot)`，`BuildDll` 的实际构建调用被注释；`path_define.bat` 还硬编码 `G:/github/TEngine/UnityProject` 和 Unity `2021.3.20f1c1`。
- 支持结论：当前 CLI 同时存在“文档示例契约、旧兼容入口、菜单入口”三套形态，脚本还带有与当前工程路径/Unity 版本不一致的本机环境；不能把存在 `GetCustomArgument` 或脚本文件当成端到端 CLI 已可用的证明。
- 性质：定义/调用/验证；归属：自写当前工具；状态：待核验。

### S06-E037 — 旧资产配置与菜单运行时覆盖

- 项目：OLD
- 位置：`Assets/Resources/framework_cfg.asset:15-18,36-67,69-77`
- 符号/字段：`ConfigRoot`、`PatchScriptRoot`、版本字段、`UseYooAsset`、`ResPath`、`AssemblyNames`、`LogicAssemblyNames`
- 摘录：资产里保存 `ConfigRoot: GameConfig`、`PatchScriptRoot: Patch`、`MainVersion: 3.0.8`、`UseYooAsset: 0`，程序集清单仍包含 `dls.*` 与 `Assembly-CSharp`。
- 支持结论：旧工程的磁盘配置呈现出版本/程序集迁移中的状态；不能单独以资产快照判断当前菜单路径，需同时看菜单对字段的写入和调用方。
- 性质：配置；归属：旧项目配置；状态：需结合调用确认。

### S06-E038 — 旧包输出路径公式

- 项目：OLD
- 位置：`Assets/Scripts/framework/Editor/Common/ConfigHelper.cs:53-75,103-136`
- 符号/属性：`AssetBundleDir`、`GetAssetBundleDir`、`AssetBundleListPath`、`AssetBundleFilterPath`
- 摘录：YooAsset 包路径由默认构建输出根、平台、包名、`YooAssetAbVersion` 拼成；中间列表为 `output/ab.json`、`output/ab_realbuild.json`，过滤配置为 `Assets/Resources/Editor/ab_filter.asset`。
- 支持结论：旧构建的物理输出和收集输入都存在固定命名公式；包名、平台、版本任一变化都会改变消费路径。
- 性质：定义/路径；归属：自写框架适配层。

### S06-E039 — 旧自定义 Pack/Ignore 规则含业务命名契约

- 项目：OLD
- 位置：`Assets/Editor/YooAsset/Editor/CustomPackRules.cs:10-76,290-325,332-359,534-589`；`Assets/Scripts/framework/Editor/AssetBundle/SilentNormalIgnoreRule.cs:7-45`
- 符号/类型：`EffectsPackRule`、`ConfigPackRule`、`UIPackRule`、`ExtensionPackRule`、`PreserveExtensionPackRule`、`SilentNormalIgnoreRule`
- 摘录：特效按 `e_map/e_ui/hero/troop` 等文件名映射 bundle；配置按 `/Common/World/Map/` 分组；UI `.png/.bytes` 产生 `ui_*`；原始文件规则保留扩展名；Silent ignore 静默跳过 DefaultAsset。
- 支持结论：旧包名并非只由目录推导，文件名正则、扩展名、资产类型和异常/警告策略都参与构建；复刻必须视为项目适配规则而非第三方默认行为。
- 性质：定义；归属：自写项目适配规则。

### S06-E040 — 旧 GameConfig 资产确实进入收集器

- 项目：OLD
- 位置：`Assets/Resources/AssetBundleCollectorSetting.asset:41-77`
- 符号/字段：`GameConfig` group、`CollectPath`
- 摘录：收集器包含 `Assets/GameRes/GameConfig/Common`、`Map`、根目录和 `extra_map`，均使用 `AddressByFileName` + `PackByUserData` + `CollectByUserData`，并带 `Game;PreDownload` 标签。
- 支持结论：旧 CSV→`.asset` 的输出路径与 DefaultPackage 的收集输入相接；这使“配置资产化→资源包→DAO 读取”成为可回查的静态端到端链路。
- 性质：配置；归属：旧项目配置/YooAsset 格式。

### S06-E041 — 当前 DefaultPackage 的运行时消费

- 项目：CURRENT
- 位置：`Assets/GameScripts/Procedure/ProcedureInitPackage.cs:31-64,92-109`；`Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:17-29,119-138,658-672,692-705`
- 符号/方法：`ProcedureInitPackage.InitPackage`、`ResourceModule.Initialize`、`LoadAsset`
- 摘录：启动过程调用 `_resourceModule.InitPackage(_resourceModule.DefaultPackageName)`；默认包名为 `DefaultPackage`，初始化时创建/设置 YooAssets 默认包；资源加载最终走默认包或指定包的 `LoadAssetAsync`/`LoadAsset`。
- 支持结论：当前构建把包名写入 `DefaultPackage` 后，运行时有对应的初始化/加载消费者；失败提示还把内置清单契约具体化为 `StreamingAssets/package/DefaultPackage/PackageManifest_DefaultPackage.version`。
- 性质：调用/生命周期；归属：自写当前运行时 + YooAsset API。

### S06-E042 — 旧业务代码实际读取生成配置

- 项目：OLD
- 位置：`Assets/Scripts/game/Managers/BuffMgr.cs:544-554`
- 符号/方法：`BuffMgr.SetByBuffId`
- 摘录：方法直接调用 `BuffDao.Inst.GetCfg(id)`，为空时记录错误并返回 `false`，成功时把配置传给 `SetByBuffCfg`。
- 支持结论：旧配置生成/资产读取链有真实业务消费者，不止停留在生成器或框架定义层。
- 性质：调用；归属：业务调用。

### S06-E043 — 当前热更程序集的资源地址与物理路径映射

- 项目：CURRENT
- 位置：`Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs:21,75-93,230-251`；`Packages/YooAsset/Editor/AssetBundleCollector/DefaultRules/DefaultAddressRule.cs:15-22`
- 符号/方法：`_enableAddressable`、`LoadAssembly`、`LoadMetadataForAOTAssembly`、`AddressByFileName`
- 摘录：`_enableAddressable` 默认值为 `true`；加载热更程序集和 AOT metadata 时默认使用程序集名作为资源定位地址，只有关闭 Addressable 时才拼接 `Assets/<AssemblyTextAssetPath>/<dll>.bytes`；`AddressByFileName` 使用 `Path.GetFileNameWithoutExtension`，所以 `GameLogic.dll.bytes` 的默认地址为 `GameLogic.dll`。
- 支持结论：当前构建复制目录是物理产物边界，运行时默认走资源地址；两者通过收集器地址规则相接，不能描述成运行时直接按同一物理路径读取。
- 性质：定义/调用/映射；归属：自写运行时 + YooAsset 地址规则。

## 证据使用规则

1. 证据只说明相应文本/配置确实存在；调用链是否被 Unity 当前程序集、条件编译、场景或启动流程实际触发，必须另有调用/加载证据。
2. `S06-E015`、`S06-E030`、`S06-E034`、`S06-E035`、`S06-E036` 已明确标为待核验；正文不得把它们升级成已激活事实。
3. 业务专属路径、包名、程序集名、Wwise/SoData 和 UI 类名均属于项目耦合证据，不代表框架通用保证。
4. `S06-E043` 只证明当前源码中的地址/路径分支和默认地址规则；它不替代 Unity 导入、资源构建或运行时加载验证。
