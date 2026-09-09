# 第 5A 批：启动成功与失败终态

状态：2026-09-08 规划完成，未实施。工程：E:/MyWork/MyFramework/TEngine/UnityProject/。
第 5 批拆为 5A 启动终态、5B 退出协调，本提示词只实施 5A；5B 尚未批准实施。
用户确认前批完成；本轮不重复其独立验收。

## Role

你是主要 slave 实施者，负责探索、编码、调试及验证。先读 AGENT.md、CLAUDE.md、tengine-dev 热更/模块/资源规范。
不调用其他 agent，不调用 master-planner，不自动进入 5B。

## Objective

资源初始化和必要程序集/metadata 全部成功、主入口校验并正常返回后，才进入 ProcedureStartGame。
失败、取消、超时必须有终态，不永久等待、不提前进入游戏、不由晚回调重复启动。
成功不代表 GameApp 内部所有 fire-and-forget 业务初始化完成；保持同步 Entrance(object[]) 契约。

## Existing architecture / 当前证据

路径相对工程根：
- Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs：Start 设置资源模块配置后调用 Initialize。
- 同模块 ResourceModule.cs：Initialize 创建默认包、资产池；InitPackage 对 Processing/Succeed 的已有包返回 null。ProcedureInitPackage 随后访问 Status，重试也没有代际隔离。
- Assets/GameScripts/Procedure/ProcedureInitPackage.cs：失败显示重试/退出，异步完成后直接跳转，没有离开/重复重试保护。
- Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs：AllAssemblyLoadComplete 先 ChangeState<ProcedureStartGame>，后校验主程序集/类型/方法，再 Invoke。
- 同文件 DLL null/throw、末次 metadata null、metadata 返回码及计数共同决定状态；TextAsset 归还不完全在 finally，已有 failure 计数未统一阻止启动。
- Assets/GameScripts/Procedure/ProcedureStartGame.cs：进入后 Yield 再隐藏 Launcher。
- Assets/GameScripts/HotFix/GameLogic/GameApp.cs：Entrance 同步注册并启动业务，FGUI 初始化自行观察异常；本批不将可选 FGUI 失败升级成全局启动失败。
- RootModule.OnDestroy 与 ModuleSystem.Shutdown、SingletonSystem.Release 的关闭协调尚需另批处理。资源模块 Shutdown 当前为空，不在本批顺手补齐。

## Implementation contract

### 1. 资源 bootstrap 与包初始化

增加最小资源 bootstrap 等待接口：IResourceModule.WaitUntilInitializedAsync(CancellationToken cancellationToken = default)，对应 Initialize 的完成，不等同包/manifest ready。
Initialize 成功才完成等待源；失败保存原异常并完成失败；已成功重复调用不重复创建池；失败后本运行会话不自动重建部分初始化资源。Driver 初始化前参数配置异常也必须能被等待侧观察，使用模块内部失败入口或等价最小机制。
ProcedureInitPackage 等待 bootstrap，60 秒不受 timeScale 影响的阶段超时；外部 token 只取消当前等待，不取消共享 bootstrap。Driver 缺失不得无限等待。
DefaultPackage 已存在时也确保 YooAssets 默认包指向配置的包。保留资源配置来源，不手改场景/Prefab 执行顺序来掩盖时序依赖。

InitPackage 保留现有返回类型。相同包和相同初始化参数的并发调用共享可多等待操作，成功重复请求得到有效成功操作，不返回 null。
缓存当前初始化任务/操作；失败后重试依据本地 YooAsset 允许的实际初始化生命周期实现，不在旧操作 Processing 时再次 InitializeAsync。
needInitMainFest 等参数不同的冲突请求明确报错，不静默忽略差异。manifest 完成仍按原调用契约处理，不把 bootstrap ready 当 manifest ready。

### 2. 流程代际与失败

每次进入 ProcedureInitPackage / ProcedureLoadAssembly 建立唯一 attempt，至少有 Running/Succeeded/Failed/Cancelled；只允许一次终态提交。
OnLeave 使 attempt 失效并取消等待；每个 await、回调、重试按钮执行前验证当前 attempt。旧完成不得跳转、弹新提示或写新状态。
包失败保留原重试/退出交互；重复点击合并为一次有效重试。等待超时不假定底层操作已停止，重试必须先处理旧包操作仍运行的实际状态。
显示失败信息包含阶段、包/程序集名和底层原因；弹窗一次，UI 提示失败本身不能破坏终态。
不用新的启动服务总线/全局状态管理器，采用 Procedure 内部上下文与少量辅助类型。

### 3. DLL 与 metadata

移除“完成计数足够即成功”的歧义。所有配置必需项成功才允许进入入口校验；配置重复项去重并验证，主程序集必须满足配置约束。
明确空配置、热更禁用、EditorSimulate、已加载程序集分支，保持原合法模式，不要求 Editor 加载不适用的 AOT metadata。
TextAsset 无论 null、加载异常、Assembly.Load 失败、metadata 返回错误、attempt 取消，均正确结束请求；成功拿到的资源每份在 finally 归还一次。
按请求的配置名称关联加载结果，验证实际 assembly identity；不只依赖 TextAsset.name 选择主程序集。
metadata 返回码按本地 HybridCLR 枚举与实现判断；只有源码确认的成功/合法已加载结果可继续，不把所有非抛异常结果当成功。
启动阶段整体使用 60 秒非缩放超时（测试可注入缩短），失败/超时后不再发起后续加载。晚到资产仅收尾，不再执行 Assembly.Load/metadata 注入。
程序集加载和 metadata 注入不可回滚。热更阶段失败或 Entrance 抛错后，不提供本进程内重新加载 DLL 的“重试”；提示重新启动或退出，停在失败终态。不能通过清空列表假装回滚 CLR。
无需强制中断同步 Assembly.Load/Entrance；超时可终止异步等待，无法抢占同步阻塞代码，交付中说明该限制。

### 4. 入口与提交顺序

验证主程序集、GameApp 类型和唯一可调用 public static void Entrance(object[])；拒绝错误签名/歧义入口。
保持传参结构 object[] { hotfixAssemblyList }，反射 Invoke 的 arguments 为外层 object[] 包装；不变更热更 ABI。
入口调用前设置一次性调用闸门，避免 OnUpdate 重入；正常返回且 attempt 仍有效，才提交成功并 ChangeState<ProcedureStartGame>，整个会话不重复调用入口。
Invocation 异常保留内部原因，失败时不进入 StartGame、不隐藏启动错误 UI。
先调用入口再切流程是本批明确修正；检查现有 GameApp 同步路径，若实际业务必须依赖先进入 StartGame，报告 PLAN_CONFLICT，不擅自改业务。
GameApp 内自行启动的异步 UI/FGUI 操作不纳入本次成功定义，不声称等到了全部业务 ready。
ProcedureStartGame 的 Yield 后也检查自身仍有效，离开后不能隐藏其他流程的 Launcher。

## Relevant files / scope

允许修改：
- Assets/GameScripts/Procedure/ProcedureInitPackage.cs、ProcedureLoadAssembly.cs、ProcedureStartGame.cs 及同目录必要内部辅助类。
- Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs（仅 bootstrap/InitPackage）、IResourceModule.cs、ResourceModuleDriver.cs。
- Assets/Tests/StartupLifecycle/ 和必要测试 asmdef/meta；已有 IResourceModule 测试替身为新增接口做机械适配。
- docs/framework-reference/project-old/synthesis/startup-terminal-verification.md。

只读参考：GameApp、更新/预加载流程、YooAsset/HybridCLR、FGUI。
不修改资源加载/引用核心、UI/事件系统、业务代码、程序集名单、第三方、ModuleSystem、SingletonSystem、RootModule 的退出顺序。
保留当前工作树前批改动，不暂存、提交、恢复、清理。

## Implementation steps

1. 记录 git status/diff，核对本地 YooAsset 重复初始化和 HybridCLR 返回码；读取已有调用点及接口实现者。
2. 用可控加载/入口代理建立失败与晚完成测试；补齐 bootstrap/包等待语义。
3. 将程序集流程收敛为单次 attempt，finally 归还资源，校验入口后提交成功。
4. 完成针对性测试和验证记录，生成手动 master 交接并停止。

## Acceptance criteria / Verification

- Driver 尚未初始化时不调用 InitPackage；bootstrap 成功/异常/缺失超时各有明确结果。
- 包初始化 Processing 合并、已成功重复请求非 null；失败重试、重复点击和参数冲突有独立断言。
- DLL 返回 null/抛异常/损坏，metadata 最后一项 null/错误返回码/抛异常，均进入失败且不调用入口。
- 主程序集/类型/方法缺失、签名错误、入口抛错：无 StartGame 跳转，失败只报告一次。
- 全部成功：每项资源配对归还，入口一次且在 StartGame 之前；EditorSimulate/热更关闭分支正常。
- 流程离开后旧加载成功/失败、旧重试按钮触发均不影响新 attempt；晚成功资产仍归还。
- timeScale=0 超时有效；超时后旧完成不启动游戏；同步代码不可抢占的限制明确。
- 验证 ProcedureStartGame 离开后的晚 Yield 不隐藏新 UI。
- 针对性 EditMode/PlayMode 和 Unity 编译；至少真实流程生命周期验证。代理只能证明控制流，不能冒充 Player 的真实 HybridCLR 注入。
- 本地工具链具备时做隔离启动 smoke；未验证的真包/IL2CPP/metadata 条件分支明确记录。禁止测试破坏当前 CLR 会话或把测试程序集注入真实热更流程。
- 记录命令、用例数、日志、结果与未运行项；git diff --check、新增 meta、接口替身适配及修改范围核对。不要求全平台 Player 构建。
本规划轮未实施或执行测试。

## Handling unexpected repository reality

小差异自行解决；包失败重试若需销毁共享包/影响其他 owner，或必须改热更 ABI/业务入口/退出顺序，输出 ## PLAN_CONFLICT，提供具体源码和唯一需重议决策，不扩大范围。

## Completion requirements

留下完整实现和验证记录，注明异步业务不纳入 Entrance 成功及程序集不可回滚限制。结束输出 ## MASTER_REVIEW_HANDOFF，不自动实施 5B。

## Required master review handoff

包含 Role（直接查共享工作树、不重实现/委派）、原目标、bootstrap/包 ready 区别、attempt/资源归还/入口签名/先入口后跳转契约、实际文件与修改摘要、验证结果及限制。
重点审查假成功、计数等待不结束、重复初始化、旧回调复活、重复入口、资源遗漏和不可回滚操作的错误重试。
通过输出 ## REVIEW_PASS；实质问题输出 ## SLAVE_FIX_HANDOFF，仅含具体问题、原因、源码定位、修正与验证。

