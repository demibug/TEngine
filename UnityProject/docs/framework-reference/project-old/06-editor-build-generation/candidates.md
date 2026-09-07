# S06 候选复刻方案（未批准）

本文件只提出候选，不代表已决定实施、已获得架构批准或已修改当前工程。每个候选都同时列出旧机制、当前等价物、收益、成本、前置条件、跨域影响和暂缓理由；没有性能收益承诺，除非后续有测量数据。[S06-E003][S06-E020]

## S06-C001：显式阶段编排与阶段结果协议

### 要解决的问题

旧工程通过菜单顺序和 [Export] 反射把代码、配置、HybridCLR、收集、构建和收尾串起来；当前主链只显式编排 DLL、AssetBundle、最小包和 Player，Luban/UI/事件生成不在同一结果协议内；当前 BuildWithConfig 还没有结构化返回结果，调用窗口会在返回后无条件记录“构建完成”。[S06-E003][S06-E006][S06-E007][S06-E020][S06-E022][S06-E026][S06-E028]

### 候选设计

引入一个只描述本次执行的 BuildExecutionContext，至少包含 target、package/version、输出根、生成物清单和是否构建 Player。阶段采用显式有序列表，例如：

1. 输入和工具版本检查；
2. 生成物准备；
3. DLL/AOT 资产准备；
4. 收集/构建每个 package；
5. 内置资源复制或最小包处理；
6. 产物校验和结果汇总。

每个阶段返回状态、日志位置、产物路径和可恢复/不可恢复错误；成功后才能放行下一个阶段。旧 [Export] 处理器可以先作为兼容阶段挂入，但不再以“扫描到哪个程序集”为顺序来源。[S06-E007][S06-E010]

### 当前等价物

当前的 ReleaseTools.BuildWithConfig、BuildInternalWithConfig 和 BuildPipelineWindow 是最接近的骨架；旧工程的 AssetBundleBuilder.Build/ExportProcessor 可作为迁移输入，而不是当前实现。[S06-E005][S06-E020][S06-E022]

### 收益、成本与约束

- 收益：入口顺序、失败边界和输出物可被日志/CI/审查共同理解；能明确区分“生成未执行”“生成失败”“包构建失败”。
- 成本：需要把现有静态方法、外部脚本和第三方 BuildResult 适配成统一结果；还要定义旧菜单兼容层。
- 前置条件：先确定哪些生成器必须进入发布构建，哪些仍是开发者手动工具；定义结果文件/日志位置和退出码。
- 跨域影响：直接影响 YooAsset 包清单、HybridCLR DLL 复制、Luban 输出和运行时默认包初始化。[S06-E023][S06-E026][S06-E041]
- 主要冲突：旧 `BuildAll` 已出现入口显式 `EncodeAllCfg` 与 Export 前置处理再次 `EncodeAllCfg` 的重复写入；如果复刻时保留反射式 [Export] 与显式阶段同时写同一资产，可能重复生成或覆盖，必须定义唯一 owner。[S06-E003][S06-E006][S06-E007]

### 暂缓理由

若当前只要求开发者手动从 Unity 菜单分别执行，统一编排的收益未必抵得上迁移成本；应先用 C001 的“结果字段”要求审计一次现有入口，再决定是否实施。[S06-E020][S06-E036]

## S06-C002：统一 package/version/output manifest

### 要解决的问题

旧工程使用 DefaultPackage + RawPackage 和平台/版本路径公式；当前构建把包名固定为 DefaultPackage，收集器还出现 Other/Dlc 包，但运行时默认路由只明确了 DefaultPackage。当前 CLI 读取 packageVersion 后又没有传入兼容构建函数。[S06-E009][S06-E021][S06-E025][S06-E036][S06-E041]

### 候选设计

定义版本化的 PackageSpec/BuildManifest，至少记录 packageName、buildTarget、packageVersion、outputRoot、build-in root、copy option、collect rule profile、runtime route 和 artifact list。构建参数、收集器选择、版本配置、内置清单校验和运行时路由均从同一份执行上下文派生；若只允许 DefaultPackage，也应显式写成策略而不是硬编码字符串。[S06-E010][S06-E021]

### 当前等价物

当前 BuildConfig 已有目标、版本、输出根和复制策略字段；AssetBundleCollectorSetting.asset 保存多个 package；ResourceModule.DefaultPackageName 和 ProcedureInitPackage 提供运行时默认包入口。[S06-E022][S06-E025][S06-E041]

### 收益、成本与约束

- 收益：降低“构建输出目录、内置清单、热更/配置 location、运行时包名”互相漂移的概率；能审计 Other/Dlc 是否真的有消费入口。
- 成本：需要梳理 YooAsset 配置资产、ReleaseTools 参数、StreamingAssets 目录、远端版本配置和运行时包 API。
- 前置条件：确定是否要复刻旧 RawPackage；如果不需要，必须记录排除理由和替代的 RawFile/DefaultPackage 规则。[S06-E008][S06-E009]
- 跨域影响：资源模块、热更 DLL、Luban bytes、最小包和发布 CDN 都依赖 package/version/path 一致性。[S06-E023][S06-E026][S06-E041]
- 主要冲突：改变 packageName 或版本格式会破坏已有包缓存和服务器目录；应提供兼容读取或迁移窗口。

### 暂缓理由

若当前发布只使用 DefaultPackage，先做只读校验器比立即引入多包抽象更合适；没有确认 Other/Dlc/Raw 的实际业务需求前，不建议复制旧双包复杂度。[S06-E009][S06-E025]

## S06-C003：生成物 manifest 与 generated/hand-written 所有权

### 要解决的问题

旧配置生成器产出数据/定义/扩展三类文件并对扩展文件不重写；当前 Luban 产出 GameProto C# + bytes，UI 自动生成产出只读 _Gen.g.cs 和不覆盖的 partial 实现，事件生成器则通过 Roslyn 不落盘。[S06-E012][S06-E026][S06-E030][S06-E033]

### 候选设计

为每种生成器声明 GeneratorSpec：输入 schema/场景/接口、工具版本、输出路径、输出命名、是否落盘、是否可覆盖、运行时消费者、生成失败策略。每次生成同时输出机器可读的 manifest 或日志摘要，明确：

- generated：可删除、可覆盖；
- extension/implementation：工具不得覆盖；
- generated source：由编译器提供，不应要求工作树有 .g.cs；
- runtime data：必须能被收集器和运行时 location 找到。

对当前 UI 的混合形态，先标记现有业务文件的 owner，不自动删除。[S06-E030][S06-E032]

### 当前等价物

旧的 CfgCodeCreator partial/_Extend 约定、当前 ScriptGeneratorSetting 的 Gen/Imp 双路径、Luban 的代码/bytes 双输出、Roslyn AddSource 都是已有局部契约。[S06-E012][S06-E027][S06-E029][S06-E033]

### 收益、成本与约束

- 收益：减少重生成覆盖业务代码、生成物缺失却继续打包、把未激活工具误当成已启用工具等问题。
- 成本：需要为旧生成文件补 provenance，维护多种工具格式，并在 CI/Unity 导入时处理生成源与落盘文件两种模型。
- 前置条件：确认当前 Unity 是否启用 Roslyn DLL、Luban 输出是否由版本控制提交、UI 现有文件是否计划迁移。[S06-E030][S06-E034][S06-E035]
- 跨域影响：直接影响 GameProto/GameLogic asmdef、UI runtime 的 ScriptGenerator 生命周期、配置资源收集和热更程序集编译。[S06-E023][S06-E027][S06-E031]
- 主要冲突：旧生成路径与当前 Gen/Imp 路径不同；自动迁移可能造成 namespace、类名和 partial 配对改变。

### 暂缓理由

在生成产物尚未通过一次完整 Unity 编译前，先建立 manifest 规范和只读报告即可；不要以“目录目前不存在”作为自动清理依据。[S06-E026][S06-E030][S06-E034]

## S06-C004：外部工具的同步 I/O 与可判定结果

### 要解决的问题

旧 Python runner 会写入输入 JSON、Base64 传参、删除旧结果、等待 Unity 命令结束并解析结果；当前 Luban bridge 只调用 ShellHelper.RunByPath，异步转发 stdout/stderr，不等待退出码，也不返回生成物结果。[S06-E017][S06-E028]

### 候选设计

增加一个 ExternalToolInvocation 适配契约：明确 executable/script、workingDirectory、参数编码、环境变量、日志文件、超时/取消、exit code、预期输出路径和结果 JSON。调用者在 Unity 菜单中显示“已启动/成功/失败/输出缺失”状态；失败时不把半成品交给后续打包阶段。

不要求所有工具都采用旧 Base64 格式；关键是输入、退出、日志、产物校验四项可判定，并保留脚本原始输出。[S06-E016][S06-E017]

### 当前等价物

当前 LubanTools + ShellHelper.RunByPath 是唯一明显的外部生成桥；当前 BuildCLI/CommandLineReader 是另一套 Unity 命令入口。旧 Python 的 unity_result.json 可作为结果契约参考，不能直接复制旧路径。[S06-E017][S06-E028][S06-E036]

### 收益、成本与约束

- 收益：避免菜单已经返回但 Luban 仍在运行、Unity 继续打包旧 bytes、脚本失败只出日志等不可判定状态。
- 成本：需要改造 UI 进度显示和进程生命周期，并处理 Windows/macOS/Linux 的 shell 差异。
- 前置条件：确定 AI_MODE、pause、工作目录和 dotnet/Luban 路径在开发机与 CI 的统一规则。[S06-E026]
- 跨域影响：会改变 Configs/GameConfig 输出与 AssetRaw/Configs 收集时机，也会影响构建编排候选 C001。[S06-E025][S06-E026]
- 主要冲突：外部脚本自带 pause 或清理行为，统一 runner 若强行注入命令可能改变开发者体验和安全边界。

### 暂缓理由

如果 Luban 只供人工菜单使用，先补一个独立“等待并检查输出”的验证命令即可；在未确认生成输出是否应提交前，不要把它强行接进主构建。[S06-E026][S06-E028]

## S06-C005：HybridCLR 产物预检与原子化复制

### 要解决的问题

旧工程在构建前检查本地 HybridCLR 安装、生成预处理文件并同步 patch；当前工程由 UpdateSetting 管清单、BuildDLLCommand 复制 AOT/热更 DLL，但 AOT 缺失只记录错误继续，热更源缺失可能直接由 File.Copy 抛异常；此外 BuildWithConfig 的 DLL 前置使用 activeBuildTarget，而资源包使用 config.BuildTarget，预检应覆盖这一目标一致性。[S06-E018][S06-E019][S06-E020][S06-E023][S06-E024]

### 候选设计

在构建阶段前增加只读 Preflight：逐项检查 HybridCLR 开关、hot update assembly、AOT metadata 源、目标目录、扩展名和收集器路径；预检结果按 assembly 记录。复制采用临时文件/完成标记或至少“全部源检查通过后再开始复制”的策略，构建结果 manifest 记录源文件与目标文件。

预检只负责确认与报告，不自动安装外部工具或修改 HybridCLRData；安装/patch 同步仍由明确的工具入口负责。[S06-E018][S06-E019]

### 当前等价物

当前 UpdateSettingEditor.ForceUpdateAssemblies、BuildDLLCommand.CopyAOTHotUpdateDlls、ProcedureLoadAssembly 已形成清单→物理复制→资源地址→加载链；运行时默认按程序集名地址加载，不能把物理复制目录直接当作 location。[S06-E023][S06-E024][S06-E041][S06-E043]

### 收益、成本与约束

- 收益：在 AssetBundle 构建之前暴露程序集清单漂移和源文件缺失；错误信息可直接指向 assembly 与路径。
- 成本：需要维护 HybridCLR API/平台差异、复制中断清理和历史 .bytes 残留策略。
- 前置条件：定义 AOT 缺失是阻断还是允许降级；确认启用/禁用 HybridCLR 时哪些目录必须为空或不收集。[S06-E023][S06-E024]
- 跨域影响：影响 DLL 资源收集、热更启动、最小包筛选和 Player 构建。[S06-E020][S06-E025][S06-E041]
- 主要冲突：旧工程有本地 SVN patch 源，当前工程的 HybridCLRData 来源和维护方式不同，不能直接复用旧同步器。[S06-E019][S06-E024]

### 暂缓理由

当前最小安全改动可能只是把缺失热更 DLL 转成带 assembly/path 的明确失败结果；在未确认构建平台和工具安装策略前，不建议复制旧工程的本地 patch 安装流程。[S06-E018][S06-E023]

## S06-C006：将反射式导出钩子收敛为有序、带类型的扩展点

### 要解决的问题

旧 Export 机制扫描所有已加载程序集中的 [Export] 类型，按方法名调用，没有源码证据保证多个处理器的排序或异常隔离；业务 ExportProcessor 又承载了 Wwise、HybridCLR、配置和 SoData 等多类副作用。[S06-E006][S06-E007]

### 候选设计

定义 typed BuildExtension 接口或阶段注册表：每个扩展声明阶段、顺序、输入依赖、输出 artifact、是否允许失败继续。保留旧 [Export] 适配器只用于兼容扫描，并在日志中报告实际发现的处理器和顺序；新扩展不得隐式扫描业务程序集。

### 当前等价物

当前没有等价的 [Export] 反射主链；ReleaseTools.BuildWithConfig 是可插入显式阶段的位置，BuildPipelineWindow 是人工入口。[S06-E020][S06-E022]

### 收益、成本与约束

- 收益：构建阶段顺序、失败策略和所有权可审计，减少同名静态方法或多程序集重复处理。
- 成本：需要迁移旧业务处理器，并处理旧菜单/第三方包仍依赖反射入口的兼容问题。
- 前置条件：列出所有 [Export] 类型及其实际被调用的方法，再决定哪些是框架扩展、哪些是业务发布步骤。[S06-E006][S06-E007]
- 跨域影响：直接影响 C001 编排、HybridCLR/SoData/音频构建和配置资产化；不能在没有业务 owner 的情况下删除旧钩子。[S06-E006]
- 主要冲突：若同时保留旧反射和新注册表，两边都修改相同资产会产生重复或顺序不确定。

### 暂缓理由

这是结构性迁移，风险高于单纯工具修补；如果当前只剩一个明确的 ReleaseTools 主链，可先不引入兼容层，待发现第二个实际扩展处理器时再启动。[S06-E007][S06-E020]

## 候选优先级建议（非批准结论）

在不改变当前行为的前提下，优先收集 C001/C002/C003 所需的只读 manifest 和 reachability 证据；C004/C005 属于较小范围的失败契约补强；C006 等确认实际存在多个旧扩展后再评审。这个排序只依据当前证据中的契约缺口，不代表收益/性能排序。[S06-E015][S06-E021][S06-E028][S06-E034][S06-E036]
