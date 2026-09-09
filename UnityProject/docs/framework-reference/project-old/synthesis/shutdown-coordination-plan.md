# 第 5B 批：退出协调与重复清理

日期：2026-09-09。状态：规划完成，未实施。工程 E:/MyWork/MyFramework/TEngine/UnityProject/。
用户确认 5A 完成；本轮不代替其独立验收。依据：[架构地图](architecture.md)、[候选清单](candidates.md)。

## Role

你是主要 slave 实施者，独立完成编码、调试、测试。先读 AGENT.md、CLAUDE.md 和 tengine-dev 模块/资源/UI/热更规范，源码优先。
不调用其他 agent，不调用 master-planner。只实施本批，结束给出手动 master 交接。

## Objective

显式退出、Root 销毁、正常应用退出均经过同一会话的关闭协议：先阻止新工作、停止启动流程，再释放业务 owner 和 UI，最后释放池与资源系统。
重复/重入退出不重复释放；某个清理 hook 抛异常不能跳过其他 owner；晚回调不能重建模块、UI 或资源登记。
不引入 DI 容器、通用生命周期图或自动重启系统。

## Existing architecture / 已核验事实

以下路径相对工程根：
- Assets/TEngine/Runtime/Module/RootModule.cs：OnDestroy 派发 BeforeShutdown，但仅非 Editor 调用 ModuleSystem.Shutdown。
- Assets/TEngine/Runtime/Core/ModuleSystem.cs：按 _modules 逆序 Shutdown，无逐项异常隔离；GetModule 缺失时可创建模块。
- Assets/GameScripts/HotFix/GameLogic/GameApp.cs：Entrance 用 Utility.Unity.AddDestroyListener(Release)，Release 调 SingletonSystem.Release，执行时机依赖 UpdateDriver 对象销毁。
- 同目录 GameModule.cs：Shutdown 清缓存并调用 _fgui.Shutdown，但不是完整根退出协调。
- SingletonSystem/SingletonSystem.cs：Release 先 Destroy 持有 GameObject，再逆序释放单例；某 Release 异常会跳过后续对象、列表清理与 DeInit。
- Module/FguiModule/FguiModule.cs 与 Assets/TEngine/Extensions/FairyGUI/Runtime/FguiRuntimeHost.cs：已有幂等关闭和 Root.BeforeShutdown 订阅。
- ResourceModule 优先级 4、ObjectPoolModule 优先级 6，模块逆序关闭时资源早于池；资源 Shutdown 当前为空。AssetObject.Release(isShutdown) 只在非 shutdown Dispose handle。
- ResourceModule/Reference/AssetsReference.cs 已有实例 owner 与静态追踪，含未激活实例补偿；不能等待 Unity 延迟 OnDestroy 后才归还。
- Packages/YooAsset/Runtime/YooAssets.cs：Destroy 会终止操作并调用 AssetBundle.UnloadAllAssetBundles(true)。因此只能在本框架全会话最后阶段调用，不能拿它当普通窗口/单例释放操作。

## Implementation contract

### 会话状态与统一入口

保留 ModuleSystem.Shutdown、RootModule 既有公开入口和 BeforeShutdown 订阅兼容性；它们委派同一个会话关闭路径，不能两套回调分别协调。
会话有 Running/ShuttingDown/Stopped，执行任何用户清理前切 ShuttingDown，重复或重入 Shutdown 直接返回。模块更新立即停止。
退出中允许取得尚未关闭的已存在依赖以清理，但禁止创建/注册新模块；已关闭模块不得由旧缓存重新用于新工作。新增只读 TryGetExistingModule<T>（或同等最小内部机制），清理不用 GetModule 隐式创建。
GameModule、SingletonSystem 与 UGUI/FGUI 新建入口在全局退出时拒绝新工作，沿用各 API 的失败/取消呈现；不能仅依赖清空缓存防止重建。
正常启动与单独关闭某个 UI 不触发全局门禁。单独 SingletonSystem.Release 的现有局部语义保持，不能因为释放一个业务层就销毁 YooAssets。
本批不实现业务热重启。编辑器下一次独立 Play 会话需有显式静态重置点（包括关闭 Domain Reload 时），仅能在新会话开始重置；旧晚回调持有旧代际，不得混入新会话。不要用懒 Get 自动重开会话。
Root 所属会话确认后才可关闭；重复 Root/旧 Root 延迟 OnDestroy 不得关闭新会话。

### 冻结的关闭顺序

1. 停止模块/单例更新和新工作；资源进入 stopping，取消其 bootstrap/包/资产等待者，禁止新登记，但暂时允许既有 owner 归还引用。
2. 关闭现有 Procedure/FSM 的活动流程，使 5A attempt 和 PRELOAD 失效，旧按钮/回调不得再次启动。按实例只执行一次；后续模块阶段不重复 Shutdown。
3. 派发 BeforeShutdown，并完成热更层退出（GameApp 注册命名方法，显式退出和销毁兜底共同命中同一幂等 Release）。关闭 UGUI/FGUI、取消其任务，释放单例。使用框架事件/回调跨热更边界，TEngine.Runtime 不反向引用 GameLogic。
4. 关闭其余资源使用模块（如音频/计时器等），保留既有相对逆序；资源模块和对象池暂缓。每个模块逐项异常隔离；清理不能取回已关闭依赖。
5. 资源模块在资产池仍有效时，对本模块拥有的 AssetsReference 追踪快照主动归还并摘除记录，包括未激活/已 Destroy 尚未 OnDestroy 的实例；随后旧 OnDestroy 只能无操作。仅处理本 owner，禁止全场景无差别扫描销毁。
6. 对象池统一关闭，AssetObject 的正常与 shutdown 分支都按唯一 owner 释放其有效 handle；清空对象池登记与缓存。
7. 资源模块完成 pending handle/包操作收尾和缓存摘除，再调用 YooAssets.Destroy（仅当该会话负责初始化/拥有该全局系统），清空包/bootstrap/加载状态。不能在 Unity 已销毁 YooAsset driver 的兜底路径再次假定 provider 可用；先核对本地有效性协议。
8. 清 GameEvent 注册、模块/单例残留缓存，最后清 MemoryPool 与 Marshal 缓存；发布 Stopped。异常汇总可见，但即使某项失败，仍尝试后续阶段并结束状态，不能永久停在 ShuttingDown。

可以用明确的内部阶段方法实现，不为此引入通用插件式拓扑系统；不要仅改 Priority 数值掩盖资源“先停止、最后释放”的两阶段需求。
BeforeShutdown 订阅之间不应依赖注册顺序；FGUI host 与 GameApp 两路重复关闭要幂等。注册清单按快照处理，本次退出中新增订阅不执行并阻止其留入下一会话。

### 资源与晚回调

资源停止时，每个尚未移交 handle 有唯一 owner；取消、完成、池登记、全局销毁竞态不能双重 Dispose。
任务等待及回调入口需结束；保留第 3 批 null 取消语义和回调终态约定。新的裸 handle 申请也拒绝，已发给 FGUI 的 handle 由其 lease owner 先释放，不用普通 UnloadAsset 代替。
不把请求 cancellation 等同物理 IO 立即结束；晚完成只能释放本代持有，不得注册到已清空/新会话的池或唤醒旧业务。
清理前摘字段、后调用外部释放；finally 完成 owner/状态表清理。无法协作取消的业务 hook 不保证被强制中断，框架必须阻止其后续提交。
真实进程被操作系统强杀不保证执行清理，本契约覆盖正常 Unity 生命周期，不宣称强杀恢复。

### 异常、实例和单例

SingletonSystem.Release 按快照逐项执行，先摘登记/停止驱动再调用外部 Release；允许 Release 自行注销而不破坏遍历。每个单例与 GameObject 最多清理一次，DeInit 对未 Init/已销毁 driver 安全。
单例清理异常、某事件订阅者异常、某池对象 Release 异常都记录具体 owner 后继续，禁止只在最外层 catch 导致内部剩余项被跳过。
ObjectPool/FSM/MemoryPool 如内部循环仍可被单个清理异常打断，允许只修 shutdown/clear 循环的隔离和摘除，不改正常运行的池计数/调度算法。
Unity Object.Destroy 是延迟销毁，不能把返回当 owner 已清理；由上述主动引用归还桥接。运行时不得用 DestroyImmediate 规避顺序问题。
业务缓存如 GameModule._ui/_fgui、GameApp 订阅和 _hotfixAssembly 引用应摘除；程序集本身不可能卸载，不声称可回滚 CLR。
Restart 入口需要调用点审查：不得绕过全局门禁复活 Stopped 会话；本批不新增重新加载 DLL 方案。若现有实际用户流程必须完整同进程 Restart，报 PLAN_CONFLICT 明确需要额外契约。

## Relevant files / scope

允许按必要性修改：
- Runtime/Core/ModuleSystem.cs、Module/RootModule.cs、Module/UpdataDriver/UpdateDriver.cs，以及 Utility.Unity 的相关监听缓存清理。
- GameApp.cs、GameModule.cs、SingletonSystem/ 内退出与注册门禁。
- ResourceModule/ 内 stopping/Shutdown、handle owner、AssetsReference 主动收尾。
- ObjectPoolModule/、FsmModule/、ProcedureModule/、MemoryPool 仅退出异常隔离的必要路径。
- UIModule/FguiModule/FguiRuntimeHost 仅全局退出接入与晚任务门禁，不重写前批窗口/包生命周期。
- Assets/Tests/ShutdownLifecycle/ 及必要 asmdef/meta、已有测试接口的机械适配。
- docs/framework-reference/project-old/synthesis/shutdown-coordination-verification.md。

第三方 YooAsset/UniTask/HybridCLR 不修改；不改业务内容、资源配置、生成文件和发布脚本。保留前批全部变更，不暂存/提交/恢复/清理。

## Implementation steps

1. 记录 git status/diff，读取实际关闭/订阅链；列出会话 owner、模块顺序及本地 YooAsset 失效后 Release 协议。确认新 Play 静态重置入口。
2. 用可控模块/单例/资源 owner 建立异常和重入测试，接通统一退出状态及阶段。
3. 接入热更层、实例主动归还和资源两阶段关闭，修必要内部清理循环。
4. 验证真实退出和下一独立 Play 会话，不把测试注入成功当真实 Unity 销毁顺序已经证明；记录限制并交给新 master 审查。

## Acceptance criteria / Verification

- 显式 Shutdown、Root.Destroy、正常退出重复命中只执行一次，各阶段顺序可断言。
- 首个 BeforeShutdown、单例、模块、池对象抛异常，后续 owner 仍释放，最终 Stopped、缓存/订阅为空。
- 清理 hook 再次 Shutdown、注册模块/单例、打开 UI、加载资源不会复活；正常清理仍能使用尚未关闭的依赖。
- 5A 包/程序集等待、PRELOAD、UGUI 加载、FGUI 创建/刷新进行中退出：等待者有终态，晚回调无新 UI/登记。
- 活跃/未激活 prefab 实例退出归还一次；延迟 OnDestroy 不访问已关闭池；正常持有的池 handle 与失败 pending handle 各释放一次。
- FGUI lease 和模态状态清零，再关闭 YooAssets；不得调用一次全局 Destroy 后便忽略 owner 泄漏。
- 未进入 GameApp、资源 bootstrap 部分失败、只有部分模块存在时退出也能完成。
- 关闭 Domain Reload 的两次独立 Play：无旧订阅/缓存/代际污染；不要求同进程业务 Restart。
- 至少真实 Unity PlayMode 验证 Root/UpdateDriver 的销毁顺序变化与延迟 Destroy；至少一个真实 YooAsset EditorSimulate handle 的池 shutdown 验证。未跑真包/Player 正常退出明确列限制。
- 运行相关程序集编译、针对性 EditMode/PlayMode；关键前批 UI/启动/资源用例按受影响路径回归。记录命令、结果、计数、日志、未运行项。
- git diff --check、新增 meta、修改清单核对，不要求全平台或性能测试。

## Handling unexpected repository reality

小差异自行处理。若本地 YooAssets.Destroy 的全局行为会影响非本框架 owner，或不可中断操作无法完成所要求的终态，或必须实现业务 Restart 才兼容，停止相关实施并输出 ## PLAN_CONFLICT，给出路径/符号、复现及精确待决事项，不委派或绕过 owner。

## Completion requirements

留下完整实现、测试和验证记录，说明正常退出/编辑器会话/强杀的覆盖边界，不自动开始新一轮架构改造。最后输出 ## MASTER_REVIEW_HANDOFF。

## Required master review handoff

自包含列 Role（直接检查共享工作树，不重实现/委派）、原目标、冻结阶段顺序/门禁/所有权/异常契约、实际变更文件、已运行验证及限制。
审查优先级：资源释放先后、重复释放、晚回调复活、退出中创建模块、热更反向依赖、异常跳过内部 owner、编辑器跨会话污染。
通过输出 ## REVIEW_PASS；需修正输出 ## SLAVE_FIX_HANDOFF，仅含具体问题、原因、源码定位、修正和验证。

