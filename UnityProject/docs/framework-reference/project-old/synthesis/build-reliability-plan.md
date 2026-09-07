# 第 2 批：构建结果可信执行契约

状态：2026-09-07 规划完成，未实施。用户确认事件批次完成；其 verification.md 记录了 master 复核及 36/36 EditMode 通过，本轮只读取该记录，没有重复验收。
目标工程：E:/MyWork/MyFramework/TEngine/UnityProject/。研究来源：[G24 / G03](candidates.md)、[M-E018](architecture.md#m-e018)。
本批只收敛现有构建链，不引入构建插件系统、业务处理器或全局生成编排。

## Role

你是主要 slave 实施者，独立完成探索、编码、调试、测试。读取 AGENT.md、CLAUDE.md 和 tengine-dev 相关规范，不调用其他 agent 或 master-planner。

## Objective

菜单、构建窗口和现有 C# CLI 入口都能可信地区分成功、失败和取消；失败阶段停止下游。DLL 编译/复制、AB 和完整 Player 构建使用一致目标；缺必要产物不能被旧文件掩盖。

## Existing architecture / 核验依据

以下定位均相对工程根，按符号查找；本轮直接读取源码：
- Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs：BuildWithConfig 返回 void；AB 失败仅 log + return；BuildImp 仅记录 BuildSummary；ProcessMinimalPackage 缺报告/解析失败后返回，完整流程仍可继续 Player。
- 同文件 BuildDll 实际调用被注释；BuildAssetBundle 读取 packageVersion 后没有传给旧 BuildInternal；旧管线结果未向上传递。
- Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs：ExecuteBuild / ExecuteBuildPlayerOnly 在方法正常返回后无条件显示“构建完成”。
- Assets/TEngine/Editor/ReleaseTools/BuildConfig.cs：BuildTarget 与 PlayerPlatform 独立；默认热更编译开启。
- Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs：BuildWithConfig 调用无参 BuildAndCopyDlls；有参 CopyAOTHotUpdateDlls 又调用读取 activeBuildTarget 的无参复制方法。缺 AOT 文件只记录错误并 continue，Obfuz 分支也有缺文件跳过。
- ReleaseTools.ProcessMinimalPackage 扫描整个 StreamingAssets package 根的 *.bundle，未限定 DefaultPackage。
- 仓库根 BuildCLI/path_define.bat、build_android.bat 位于 UnityProject 上一级，仍有旧机器绝对路径。本批不修改仓库外脚本；交付可直接执行的正确 Unity 命令，不能宣称旧 bat 已修复。

这些是现状事实。下面“拒绝不一致目标、阶段结果”等是本次设计选择。

## Implementation contract

### 结果与入口

新增编辑器内轻量 BuildExecutionResult（与 Unity/YooAsset BuildResult 区分），至少包含 Status（Succeeded/Failed/Cancelled）、Stage、Error、Target、PackageVersion、已确认输出路径。
阶段为 Preflight、Dll、AssetBundle、MinimalPackage、Player、Completed；未执行阶段不冒充成功。保留异常详情供诊断；正常失败不依赖扫描日志判断。

为现有 void 公共方法保留兼容包装，内部增加返回结果的核心方法。包装失败/取消以构建异常向调用方暴露；窗口调用结果核心，准确显示结果并在 finally 解除日志订阅。菜单与 -executeMethod 的无参入口名称保留。
CLI 失败或取消必须导致 Unity 非零退出（顶层未捕获构建异常或经测试验证的 batchmode 出口）；普通编辑器不能被退出。成功仅在所有请求阶段完成后报告。
BuildDll 恢复真实编译/复制；BuildAssetBundle 必须将用户版本传入统一核心，保留其仅 AB 的入口范围，不暗中编译 DLL。旧 AB 路径默认加密等有效配置应显式映射，不能因合并入口静默改变。无效/缺少参数一律 Preflight 失败，不回落 1.0 或 NoTarget。

### 平台和模式

完整构建以 config.BuildTarget 为唯一目标，若请求 Player 则 PlayerPlatform 必须相等。
本批不自动切平台：activeBuildTarget 不等于请求目标时，在任何编译、复制、AB、删除操作之前失败，给出所需平台。CLI 文档通过 Unity -buildTarget 在启动时选择目标。
显式 target 贯穿 DLL 编译、AOT/热更源目录选择和复制，底层不再偷偷读取 activeBuildTarget。无参兼容入口只在入口捕获当前目标。
无效配置、非法输出/版本路径、Player 输出缺失、目标组不匹配等先检查。Player 场景使用启用的 EditorBuildSettings 场景，空集合失败。

保持“仅 Player”为独立模式：只承诺 Player 编译结果，可用于首次生成裁剪 AOT，不强制 DLL/AB 预检、不冒称发布包完整。完整构建缺 AOT 时停止并提示先准备该平台裁剪产物，本批不自动构建两遍 Player。

### DLL / AOT 产物

名单来自当前 UpdateSetting / HybridCLR / 条件 Obfuz 配置，不能硬编码旧项目名单。
启用 HybridCLR 时：
- 构建 DLL 后，先完整校验选定目标所有必需源文件存在且非空，再复制到 AssemblyTextAssetPath；缺任意 AOT、热更或选中的混淆 DLL 都失败，不能 continue。
- 目标目录按需创建；解析并验证配置路径，禁止越出预期 Assets 输出范围。复制异常立即失败，不执行 AB。
- BuildHotFixDll=false 表示复用而非忽略依赖：AB 前检查必需 .bytes 存在、非空，并与所选目标对应源文件内容一致；源缺失或不一致失败，要求重新准备。使用哈希/内容比较而非只看时间。
- 保留现有 Obfuz 选择规则，不能缺混淆文件时默默使用未混淆文件。
禁用 HybridCLR 且未请求 DLL 构建时不强制 DLL/AOT；显式请求 DLL 编译则报告不可用，不能空操作成功。

存在/内容一致只能证明与当前平台目录对应，不能证明裁剪 AOT 与未来 Player 完全匹配。本批不新增跨构建溯源系统，必须在交付限制中保留这一点。
不承诺文件复制事务回滚：中途失败可能留下部分文件，但本轮不得进入 AB；后续复用必须重新通过完整校验。

### AB / 最小包 / Player

AB 返回 Success=false 或异常均停止，不能执行最小包或 Player。输出包名保持 DefaultPackage，版本使用本次输入。第三方成功后核验本次输出目录、清单/版本等必需文件，具体文件名从已安装 YooAsset 源码工具方法取得，不能自行猜测。
开启 MinimalPackage 时，先完整解析校验报告、包名/版本/目标（报告有字段时）、BundleInfos、文件名以及内置目录，再形成删除清单；校验失败不得开始删除。
仅处理本次 DefaultPackage 内置目录，依据 YooAsset 实际路径公式；不得递归清理其他包或整个 StreamingAssets 根。拒绝逃逸路径，不跟随目录链接越界。
保留现有 tag 匹配与空 tag 删除非保留 bundle 的语义，不删除清单/版本文件。最小包与不生成新内置文件的复制选项组合先拒绝，避免处理旧内置包。
删除/后处理失败立即停止，不构建 Player；不承诺删除回滚，输出需重新构建后才可使用。
Player 根据 report.summary.result 判定 Succeeded/Failed/Cancelled，null 报告或 Unknown 视失败；不得仅靠 BuildPlayer 未抛异常判断成功。

## Relevant files / symbols 与范围

允许修改：
- Assets/TEngine/Editor/ReleaseTools/ 下现有工具及必要结果/预检辅助类。
- Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs。
- Assets/TEngine/Editor/Utility/CommandLineReader.cs：仅必要参数错误处理与正确入口示例。
- Assets/Tests/BuildPipeline/EditMode/ 及对应 asmdef/meta。
- docs/framework-reference/project-old/synthesis/build-reliability-verification.md 和必要测试结果文件。

只读参考：Packages 中 YooAsset/HybridCLR 实现、当前配置、运行时加载器、现有事件/FGUI 实现与测试。
不改第三方、运行时、生成器、配置名单、资源收集规则、上一级 BuildCLI 脚本或已有研究。保留本轮开始前全部变更，不暂存/提交/恢复/清理。

## Implementation steps

1. 记录 git status/diff，读取相关未跟踪文件，核对调用方与本地第三方结果/路径 API。
2. 建立结果核心与前置校验，让窗口、菜单、CLI 包装共享结果规则，消除双管线结果丢失。
3. 贯穿 target 和 DLL 产物校验，限制最小包操作范围，正确适配 AB 与 Player 结果。
4. 增加少量可注入阶段函数/文件系统临时目录测试接缝，不创建通用服务容器。完成故障测试及真实可用入口验证，写实施记录。

## Acceptance criteria / Verification

- AB 失败、抛异常：结果阶段准确，最小包/Player 调用次数为零，窗口不显示成功。
- Player Failed/Cancelled/null：分别映射预期结果；仅 Player 同样遵循。
- 配置目标不一致、active target 不符：所有有副作用阶段调用次数零。
- 显式平台贯穿编译/复制；缺 AOT、缺热更、缺混淆文件、空文件、旧 .bytes 内容不符均阻止 AB；目的目录遗留文件不能掩盖缺源。
- 禁用 HybridCLR 的跳过/显式请求行为符合契约；未实际编译的条件分支要明确标注。
- 最小包缺报告/坏报告/目录错误：不删文件、不运行 Player；双包夹具证明另一包完全不变；删除异常停止下游。
- CLI 缺参数/非法平台非零退出；自定义版本确实到达 AB 参数；BuildDll 不再空成功。
- 成功顺序及可选阶段跳过正确；日志订阅在成功、失败、异常后均解除。
- Unity 编辑器相关程序集编译、目标 EditMode 测试；文件操作测试只用隔离临时目录。
- 至少实测一次无效参数的 batchmode 非零退出；成功 AB/Player smoke 仅在本机工具链和产物具备时运行，写入明确测试输出目录，不上传发布。涉及现有 StreamingAssets 内容的真实构建先评估避免覆盖用户未提交资源，无法隔离就记录未运行。
- 记录实际命令、测试数量、结果、日志路径与未运行原因。mock 通过不能描述为真实 Android/iOS 构建通过。不要求全平台、全量游戏测试或性能测量。
- git diff --check，新增文件 meta 与修改范围核对。规划轮未运行上述测试。

## Constraints

本批不接入 Luban 自动生成、外部进程结果框架、发布上传或构建插件链。无性能提升承诺。
恢复当前 active target 的跨域机制、AOT 完整溯源与可重复构建另行规划；不能借本批重构启动/资源/UI。
需求是完整的当前阶段结果，不是构建全局事务或文件自动回滚。

## Handling unexpected repository reality

小差异自行修正并记录。若实际源目录/混淆产物协议使冻结契约无法成立，或必须越界修改生产模块，停止相关工作并输出 ## PLAN_CONFLICT，提供路径/符号、具体证据与需要重议的决策，不委派。

## Completion requirements

留下完整实现与针对性验证记录，简述改动和限制，不粘贴大 diff，不自动开始资源生命周期批次。
结束时输出 ## MASTER_REVIEW_HANDOFF，供用户手动发给新 master。

## Required master review handoff

必须包含：
- Role：高级正确性审查者，从共享工作树 git status/diff 和新增文件直接审查，不重实现、不调用 agent。
- Original objective：失败构建不可冒充成功，目标一致，缺必要产物停止，最小包不越界。
- Important implementation contract：本计划的结果传播、平台/模式、产物、最小包、CLI、兼容及范围规则。
- Implementation summary、Changed files：实际实现及所有本批文件，与基线变更区分。
- Tests/checks already run：命令、结果、未运行项；Known deviations or concerns：限制/偏差，没有则 none。
- Review priorities：假成功、失败后仍执行、平台混用、旧产物掩盖、误删、条件编译、CLI 退出码、缺少故障测试。
- Required review output：正确输出 ## REVIEW_PASS；具体问题输出 ## SLAVE_FIX_HANDOFF，仅列问题、原因、证据路径/符号、修正及验证，不重新设计大方案。

