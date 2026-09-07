# S06 未决问题与下一步核验

本文件只记录静态调查后仍不能安全断言的内容。旧参考工程和关键源码均可访问，因此本次没有触发 PLAN_CONFLICT；未决项主要是“静态定义是否被当前 Unity/程序集/CLI 实际激活”，不是调查被核心不可用阻断。[S06-E001][S06-E034]

## A. 激活关系与实际可达性

### S06-Q001：旧 GenCodeTool 是否有可执行 provider？

- 已知：入口会反射查找 ICodeGenerator 并逐类型生成；UIBindProvider 位于注释示例中，CmdMethodProvider 也把接口实现注释掉。[S06-E014][S06-E015]
- 未知：旧工程最终编译程序集、条件编译符号或其他目录是否提供了未被本次限定搜索发现的 provider。
- 下一步：在允许打开 Unity 后，记录 Editor log 中 GenCodeTool 发现的类型数；同时按实际程序集过滤器检查所有 ICodeGenerator< 的非注释定义，确认生成目录和最终编译引用。
- 当前处理：不把旧一键入口的 GenCode 调用写成“必然有代码产出”。[S06-E015]

### S06-Q002：当前 Roslyn 事件生成器是否被 Unity 实际加载？

- 已知：当前工作区保存 SourceGenerator.dll、GameEventAnalyzer.dll，.meta 有 RoslynAnalyzer 标签；工具源码的输入/输出规则明确；业务源码使用 EventInterface 标记且 GameApp 调用 GameEventHelper。[S06-E033][S06-E034][S06-E035]
- 未知：Unity 当前编译是否加载 DLL；.meta 的 Editor enabled: 0 是否表示未启用；GameEventHelper 是否只存在于 Library 生成缓存、某个外部编译流程或未完成状态。
- 下一步：只做 Unity 导入/编译验证，检查 Roslyn analyzer/source generator 加载日志、编译生成源列表和 GameLogic 程序集引用；不要用 Library 缓存文件代替工作树证据。
- 当前处理：把“源码/工件存在”和“当前生成成功”分开。[S06-E034][S06-E035]

### S06-Q003：当前 Luban 输出是否由版本控制或构建流程拥有？

- 已知：脚本输出 GameProto/GameConfig C# 和 AssetRaw/Configs/bytes，并复制 ConfigSystem/ExternalTypeUtil；当前目标目录在工作树中不存在。[S06-E026][S06-E027]
- 未知：这些输出是否预期提交到 Git、是否由 CI 每次生成、当前 GameProto.asmdef 是否应该引用生成代码、运行时是否已有真正的 ConfigSystem 消费者。
- 下一步：在不执行脚本的前提下先确认项目约定/ignore/CI；获准执行后再做一次隔离分支转表，记录 exit code、文件清单、asmdef 编译结果和资源定位名。
- 当前处理：只记录模板定义的预期消费者，不声称生成物可运行。[S06-E027][S06-E028]

## B. 构建输入/输出契约

### S06-Q004：当前 CLI 的真实入口和版本参数是什么？

- 已知：CommandLineReader 注释示例写 BuildPackage；ReleaseTools 实际提供 BuildDll、BuildAssetBundle、AutomationBuildAndroid 等；BuildAssetBundle 读取 packageVersion 后调用未传入该值的 BuildInternal，BuildDll 的 DLL 构建调用被注释；BuildCLI Android 脚本调用 AutomationBuildAndroid。BuildWithConfig 的 DLL 前置使用 activeBuildTarget 而 AssetBundle 使用 config.BuildTarget，且没有结构化返回结果；BuildCLI 的 `path_define.bat` 还硬编码了另一工程路径和 Unity 版本。[S06-E020][S06-E021][S06-E022][S06-E023][S06-E036]
- 未知：实际 CI 是否调用另一份脚本、外部 wrapper 或菜单方法；`path_define.bat` 是待替换的本机模板还是实际入口；CustomArgs 中的多余参数是否只是历史遗留；兼容入口默认版本 1.0 是否仍被使用；调用方是否能感知 BuildWithConfig 的失败。
- 下一步：只读收集 CI/Jenkins 调用方和 Unity 执行日志，确认脚本路径/Unity 版本并核对目标平台是否先切换；如需验证，使用独立临时输出目录和明确的 packageVersion，不使用当前用户输出目录，同时记录 BuildWithConfig 的实际失败状态。
- 当前处理：把 CLI 标为契约风险，不在本调查中修复。[S06-E036]

### S06-Q005：当前 OtherPackage/Dlc1Package/Dlc2Package 是否实际启用？

- 已知：AssetBundleCollectorSetting 中存在这些 package 名；ReleaseTools 构建参数硬编码 DefaultPackage；ResourceModule/ProcedureInitPackage 的默认运行时路由也指向 DefaultPackage。[S06-E021][S06-E025][S06-E041]
- 未知：DLC 是否由其他未纳入本次范围的模块按包名显式加载，是否存在远端/StreamingAssets 预置清单。
- 下一步：限定搜索 GetPackage(、InitPackage(、包名字符串和远端配置；再检查各 package 的 group 是否有非空收集器。不要仅凭 asset 中出现的名字判断已运行。
- 当前处理：不建议在候选中直接恢复旧 RawPackage。[S06-E009][S06-E025]

### S06-Q006：当前是否需要旧 RawPackage 的语义？

- 已知：旧 RawPackage 对 HotUpdate/Video/WWise 使用 RawFileIgnoreRule、PreserveExtensionPackRule 和 IncludeAssetGUID；当前 DefaultPackage 收集 AssetRaw/DLL、Configs、UI/UIRaw，当前配置片段没有 RawPackage。[S06-E009][S06-E025][S06-E039]
- 未知：当前资源模块是否要求保留 .mp4/.bnk/.bytes 扩展、是否有外部 CDN/原始文件消费者。
- 下一步：分别追踪这些扩展名的 runtime location、加载 API、发布目录和包清单；如果没有消费者，记录为有意弃用而非迁移遗漏。
- 当前处理：不把“旧有双包”当作当前必须复刻的事实。[S06-E009][S06-E041]

## C. 生成文件所有权

### S06-Q007：当前 UI 文件是手写、旧生成还是当前生成器产物？

- 已知：设置默认 Gen/Imp 两个路径；自动生成 _Gen.g.cs 并设只读，partial 实现不覆盖；BattleMainUI 在业务文件内部保留生成区域，却不符合自动文件名。[S06-E029][S06-E030][S06-E032]
- 未知：现有 BattleMainUI 是否来自旧工具、人工整理过的生成代码，或当前工具的非自动模式；其他 UI 是否遵循同一规则。
- 下一步：检查文件历史/生成头、工具菜单调用记录和 UI prefab 绑定；迁移前建立 owner 清单，不做按文件名的批量删除。
- 当前处理：只把它标成混合所有权形态。[S06-E032]

### S06-Q008：旧配置生成器的 releaseBuild 强制值是否是有意策略？

- 已知：EncodeCfgByDecoders 先读取 Release，再把 releaseBuild 强制为 true，发布输出固定进入 GameRes/GameConfig。[S06-E011]
- 未知：这是临时调试残留还是正式发布约束；非发布 OutAssets 路径是否仍被任何工具消费。
- 下一步：查旧 CI/菜单调用和历史构建日志；如果复刻，先保留显式开关并在结果 manifest 中记录实际分支，不能按字段名猜。
- 当前处理：正文按源码实际行为记录，不为其补充不存在的设计意图。[S06-E011]

## D. 错误、清理与机器环境

### S06-Q009：当前热更 DLL 缺失应阻断还是允许继续？

- 已知：AOT 缺失只 LogError 后继续；热更 DLL 复制没有 File.Exists 守卫，可能由 File.Copy 异常中断；运行时默认以程序集名作为资源地址加载，物理 `AssetRaw/DLL/*.bytes` 通过 `AddressByFileName` 映射到该地址，并非直接按同一物理路径读取。[S06-E023][S06-E024][S06-E041][S06-E043]
- 未知：项目是否故意允许部分 AOT 元数据缺失；BuildWithConfig 的调用方是否会捕获 BuildDLLCommand 的异常。
- 下一步：先与运行时降级策略和平台清单 owner 确认，再设计 preflight；不要仅为统一日志而改变发布失败语义。
- 当前处理：作为 C005 的条件，不宣称现有行为是 bug。[S06-E023]

### S06-Q010：旧 HybridCLR patch 源与当前工程是否同一维护体系？

- 已知：旧 patch 同步器以 SVN 管理的 il2cpp_plus_repo 为唯一源并在 BuildPlayer 前同步；当前 HybridCLRSettings 只确认了热更/AOT 配置和输出根，没有确认同一 patch 管理器。[S06-E019][S06-E024]
- 未知：当前工程是否在其他根目录或外部 CI 中维护同类本地 patch。
- 下一步：只读检查当前工程外的 CI/工具说明和 HybridCLRData 标记；禁止直接复制旧 SVN 同步器或执行安装脚本。
- 当前处理：将旧同步器视为旧工程适配器，不列为当前既有机制。[S06-E019]

## 已搜索范围与排除范围

本次重点搜索了以下绝对路径：

- OLD：D:\Work\SAUnity\ProjectOld\Assets\Scripts\framework\Editor、Assets\Scripts\framework\Library、Assets\Editor\Export、Assets\Editor\YooAsset、Assets\Resources\AssetBundleCollectorSetting.asset、ProjectSettings\HybridCLRSettings.asset、Tools\make\plugin。
- CURRENT：E:\MyWork\MyFramework\TEngine\UnityProject\Assets\TEngine\Editor、Assets\Editor\UIScriptGenerator、Assets\GameScripts\Procedure、Assets\GameScripts\HotFix、Assets\Editor\AssetBundleCollector、ProjectSettings\HybridCLRSettings.asset、Packages\manifest.json。
- CURRENT 外部工具：E:\MyWork\MyFramework\TEngine\Configs\GameConfig、E:\MyWork\MyFramework\TEngine\Tools\GameEventSourceGenerator、E:\MyWork\MyFramework\TEngine\BuildCLI。

为避免把缓存/生成缓存误当源码，搜索排除了 UnityProject 的 Library、Temp、Logs、UserSettings、obj、bin 和大型运行输出目录；没有把 repowiki、.idea 历史或 MCP/工具缓存作为框架事实。[S06-E034][S06-E035]

## 推断与事实的分界

- 事实：某方法调用了另一个方法、某配置字段有某值、某目录规则存在。
- 推断：这些节点意图组成一条端到端链，或某输出应被某运行时消费；必须同时有消费者证据，且仍不等于成功运行。[S06-E040][S06-E041]
- 待核验：生成器是否被导入、CLI 是否被实际使用、包是否被远端发布、构建是否通过、路径是否有文件。

## 阻塞状态

本次没有阻塞：OLD 核心源码、CURRENT 关键源码和当前工程写入目录均可访问。不能执行 Unity/脚本只是任务明确的验证边界，不构成 PLAN_CONFLICT；后续若需要把静态链升级为运行时结论，必须另行获得执行条件。[S06-E001][S06-E034]
