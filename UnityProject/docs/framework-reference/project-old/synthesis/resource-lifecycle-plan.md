# 第 3 批：资源生命周期可靠性执行契约

状态：规划完成，未实施。工程：E:/MyWork/MyFramework/TEngine/UnityProject/。
依据：[候选 G05/G06/G08](candidates.md)、[资源生命周期核验](architecture.md)。用户确认前批完成；本计划不代替前批 REVIEW_PASS。

## Role

你是主要 slave 实施者，独立完成编码、调试与验证。先读取 AGENT.md、CLAUDE.md、tengine-dev 的资源与热更规范。
不调用其他 agent，不调用 master-planner。源码优先于过时 reference。

## Objective

修复现有资源加载、实例引用和 PRELOAD 的局部所有权闭环：失败后能重试，取消不影响其他调用者，未交付资源有释放责任，已交付资源按 owner 配对归还。
保持 UGUI 与已接入的 FGUI 兼容。不创建统一资源中心、全局任务中心或新公共 lease API。

## Existing architecture / 源码事实

路径均相对当前工程根：
- Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs：GetCacheKey 用 package/location 拼接；LoadAssetAsync 两类任务入口、LoadGameObjectAsync、回调入口共同访问 _assetPool 和 _assetLoadingList。
- TryWaitingLoading 忽略调用者 token，使用单个 TimeoutController；超时 catch 后可继续访问仍在加载的 key。缓存 Spawn 后 Yield 没有取消归还。
- 任务加载仅 SuppressCancellationThrow，其他异常可能跳过 _assetLoadingList.Remove；成功路径未统一核验 Status/非空/类型。回调路径的失败 handle 也缺完整释放。
- ResourceModule.Pool.cs：UnloadAsset 按 target Unspawn。ResourceModule.AssetObject.cs：AssetObject 持有 YooAsset handle，普通池释放时 Dispose。引用归还与池最终释放是两个时间点。
- Reference/AssetsReference.cs：实例携带源资产引用，OnDestroy 归还；当前 module 引用为 static，清理字段未在外部调用前摘除，实例化和绑定失败也没有统一补偿。
- Assets/GameScripts/Procedure/ProcedurePreload.cs：按 PRELOAD / WEBGL_PRELOAD 地址加载，只登记 bool，不保存或归还成功资源；失败亦标完成。重入/晚回调没有代际隔离。
- UGUI UIWindow.InternalCreate 使用 LoadGameObjectAsync。ResourceExtComponent 有自己的一层请求/资源池管理；其 setter 之前标记 transfer 不足以单独证明泄漏。本批不重写该层。
- FGUI 使用的裸 handle 桥接与通用资产池是不同所有权路径，不得接入自动 UnloadAsset。

上述为静态控制流，不是实际泄漏数量或性能测量。下面为本批选定的新契约。

## Implementation contract

### 1. 公开结果与兼容性

保留 IResourceModule 的现有签名、同步/任务/回调入口和包默认值。
任务接口返回非空才表示已交付一份持有权；正常资源不存在、底层加载失败、类型不符返回 null 并保留原因日志。调用者取消返回 null（沿用已有语义），不新增强制 OperationCanceledException。
非法参数与非预期实现异常仍可抛出，但必须先完成内部收尾；不把所有异常转换成 null。
因此旧 API 不提供类型化失败/取消结果，取消原因由调用者 token 判断。不得宣称本批已实现新的 Result API。

回调接口每次请求只产生一个加载终态：成功或失败；Action<T> 失败通知一次 null。没有成功接收者时不保留无人认领的 spawn。
成功回调进入前完成资源登记和持有权转移。业务回调抛错不触发第二次“加载失败”，不擅自撤销已交付资源；异常保持可见，async void/UniTaskVoid 通过项目现有异常通道报告。
进度回调抛错须被观察、报告并停止该进度观察者，不得卡住加载或产生未结束的进度任务。

### 2. 同 key 等待与取消

采用现有“一个请求加载，其余等待后再检查缓存”策略，不引入共享 UniTask 结果中心：
- 等待必须感知各自 token；取消等待者不得清除加载者的标记、释放其 handle 或改变其他等待者。
- 加载者失败/取消时释放自己尚未移交的 handle，在 finally 清除自己拥有的标记。其他调用者重新竞争加载资格，允许重新发起加载，不强制共享同一失败结果。
- 加载标记必须原子地检查/占有（主线程下无 await 间隙），只能由 owner 清除。醒来后循环重查；超时不得绕过标记继续加载。
- 去掉跨请求共享 TimeoutController。若保留编辑器 60 秒诊断，用请求独立 token 并结束该请求，不影响 key owner。无 token 且底层从未终止的生产请求不承诺超时，本批不新增全局超时政策。
- 已取消 token 在缓存检查前就结束；缓存 Spawn 后若 Yield/交付前取消，立即归还这一份 spawn。加载完成与取消竞态以最终交付点为界：交付前观测到取消则不交付，交付后取消不自动回收调用者资产。
- 晚到完成事件不能再次登记/通知。按本地 YooAsset-UniTask 适配器的实际释放协议选择唯一 handle owner，不叠加自动与手动释放。
- 保持主线程资源访问约束，不承诺多线程安全。

### 3. 缓存与 handle 所有权

资源身份由规范化包名和 location 组成，默认空包与 DefaultPackageName 等价，避免 package/location 简单拼接碰撞。
同一身份的不同请求类型不能导致错误 cast 后仍占引用：命中后验证可赋值性，不符归还本次 spawn，返回失败；不要简单按 Type 分池造成同一 target 多份池登记与 UnloadAsset 歧义。
同步入口与异步入口共享同一缓存语义：缓存未命中且同 key 已异步加载时，同步入口明确抛出冲突异常，不阻塞等待、不再注册第二份。这是为安全而明确的兼容性约束。

加载请求取得的 handle 在登记成功前由请求持有；失败、取消、登记异常均由请求收尾一次。登记成功后仅池持有 handle，调用方持有 spawn。
不得将 Failed、null AssetObject 或不兼容类型登记为成功对象。缓存命中也执行类型检查。
池释放前只有 Unspawn，不立即 Dispose 共享 handle。正常池回收时释放 handle 一次；不得修改 ObjectPool/MemoryPool 的全局计数政策。
保留 UnloadAsset 的“一份成功加载对应一次归还”契约。它无法识别同对象的不同调用者，不可实现成“同对象只释放一次”。违规多次手动归还不在本批承诺幂等。

### 4. GameObject 与 AssetsReference

每个成功返回的 LoadGameObject 实例拥有一份源 prefab spawn。
先确保资产已被池登记，再实例化并绑定；实例化失败、绑定失败或返回前取消时，销毁未交付实例并归还该份 spawn，不能既手动归还又由 OnDestroy 再归还。
AssetsReference 清理使用实例绑定时的 module；不允许 static module 被另一实例覆盖。保留无参绑定兼容路径，在绑定时解析并保存 owner，清理时不重新获取或创建模块。
清理前先摘除所持记录，重复清理不再归还；某一条归还异常仍尝试其余条目，记录异常。AssetsRefInfo 中每条合法登记代表一份持有，不能仅按对象去重。
保持克隆不继承原实例持有权的既有意图；覆盖 active/inactive prefab、已有 AssetsReference 和外部 Instantiate 克隆场景，不能克隆后双重释放。
不改全局退出顺序，ResourceModule.Shutdown/池 isShutdown 的全局责任留到第 5 批；不得宣称覆盖模块关闭后所有晚回调。

### 5. PRELOAD 策略（本次设计选择）

PRELOAD 是“预热可回收缓存”，不是应用全程常驻：
- 先建立去重请求清单，再发起加载；PRELOAD 与 WEBGL_PRELOAD 重叠地址只加载一次。
- 每次成功回调先配对归还预加载自己的 spawn；资产留在现有池中，按正常容量/过期策略回收。普通调用者另取自己的 spawn，不受预加载归还影响。
- 使用进入流程的代际/请求上下文隔离晚回调。离开或再次进入后，旧成功仍须归还资源，但不得更新新流程状态或触发跳转。
- 成功、失败分别记状态，进度统计终态项，不能依赖字典遇到第一个未完成项就 break。
- 保留原“预热尽力而为，全部终态后继续”的启动政策，失败明确记日志，不冒充成功；本批不新增重试 UI 或改变启动故障产品策略。
- OnLeave 等生命周期签名按现有 ProcedureBase/FSM 核对；不用跨热更边界的新服务。

这会取消隐式永久引用；需要常驻资源的业务必须另行明确 owner。本批不引入常驻资源清单。

## Relevant files / symbols 与允许范围

主要修改 ResourceModule.cs、ResourceModule.Pool.cs、ResourceModule.AssetObject.cs、Reference/AssetsReference.cs，以及必要的模块内部辅助文件。
IResourceModule 只补充行为/所有权文档；不新增/删除公开 API。
允许对 ProcedurePreload.cs 做上述 owner、代际与进度局部修正，不改其他启动流程。
测试放 Assets/Tests/ResourceLifecycle/，按需分 EditMode/PlayMode，附 asmdef/meta。
验证记录放 docs/framework-reference/project-old/synthesis/resource-lifecycle-verification.md。
ResourceExtComponent、AssetsSetHelper、UGUI、FGUI 桥接、裸 handle API、场景加载器和第三方源码只读核对，不扩大生产修改范围。

## Implementation steps

1. 记录 git status/diff，直接读相关新增文件，保留前批构建、事件及 FGUI 变更。核对本地 ToUniTask 的失败/取消/释放行为和池 Register/Unspawn 异常边界。
2. 用可控制完成/失败/取消的轻量测试接缝复现状态卡住与引用差额，再收敛加载核心，让同步、任务和回调路径遵守同一登记规则。
3. 修复实例绑定/清理与 PRELOAD owner；检查现有 UGUI/FGUI 调用兼容性，不迁移其实现。
4. 运行针对性测试和真实最小样本，记录结果，完成手动审查交接。

## Acceptance criteria / Verification

必须通过行为测试核对：
- 底层失败、抛异常、注册异常后标记清除、未转移 handle 释放；同 key 再次加载可成功。
- 同 key A/B/C 等待：B 取消不影响 A/C；A 取消后 C 可重新加载；成功者每人仅一份 spawn，取消者没有持有。
- 预取消、等待中取消、缓存 Yield 期间取消、完成同帧取消、晚到完成的交付与计数符合契约。
- 不同包/路径含分隔符的 key 不碰撞；默认包显式/隐式共享；类型不匹配无错误交付或引用残留。
- 同步入口撞上异步未完成时明确失败，不发生双登记。
- 各回调入口成功/失败只通知一次；业务成功回调抛异常不触发 failure，已移交资源仍由接收方归还。
- 两个实例共享 prefab：销毁一个不释放另一个的持有；实例化/绑定失败及取消无遗留；重复清理与克隆不重复归还；一条清理异常不跳过其余条目。
- PRELOAD 标签重复、部分失败、离开后完成、离开再进入都无遗留 spawn 或旧流程污染；预热资源可以正常池回收。
- 裸 handle FGUI 桥接不受池自动释放影响；SetSprite 消费 null/取消的既有路径不回归。

测试用计数区分请求 handle、池持有、spawn、实例，不能只断言“没有抛错”。至少包括真实 Unity GameObject 生命周期 PlayMode 用例；不要仅用 mock 代替 Destroy/Awake 行为。
运行相关程序集编译、针对性 EditMode/PlayMode；能隔离时用最小 YooAsset EditorSimulate 资产验证加载/销毁/回收。
PRELOAD 在 EditorSimulate 被跳过，不能把普通模拟模式启动当成预加载测试；通过抽出的请求处理逻辑和可控制回调验证，真实包模式未跑就明确记录。
记录实际命令/入口、用例数、结果、日志和未运行项；不要求全量游戏、真机全平台或性能测试。git diff --check，新增 meta 与修改范围核对。
本规划轮只读源码和写文档，没有运行上述验收。

## Constraints

不暂存、提交、恢复或清理已有变更；旧工程只读。无业务迁移、无性能提升声明。
不改对象池全局算法、全局退出、场景卸载、窗口状态机、Sprite 请求层或 FGUI SDK。
旧 UnloadAsset 的 owner 识别限制与 PRELOAD 非常驻策略必须写入交付摘要，不能用过强的“释放全局幂等”描述。

## Handling unexpected repository reality

小差异自行核对调整。若本地 YooAsset 释放语义、池登记协议或已存在的常驻资源依赖与契约产生重大冲突，停止相关修改并输出 ## PLAN_CONFLICT，给出具体路径/符号、证据及唯一需要重议的决策，不调用 agent。

## Completion requirements

留下完整实现、测试与验证记录，简述变化、结果及限制，不贴大 diff，不自动进入 UI 批次。最后输出 ## MASTER_REVIEW_HANDOFF。

## Required master review handoff

自包含提示词必须包括：
- Role：最终正确性审查者，直接从共享工作树 git status/diff 和新增文件开始，不重实现、不调用 agent。
- Original objective：失败可恢复，取消隔离，实例/预热引用配对。
- Important implementation contract：保留 null 取消兼容、owner 标记、唯一 handle 转移、同步冲突、回调终态、实例清理、PRELOAD 策略与范围限制。
- Implementation summary、Changed files：实际实现和本批全部文件，区分已有变更。
- Tests/checks already run：命令/入口、结果与未运行项；Known deviations or concerns：真实限制，没有则 none。
- Review priorities：等待死锁、取消误伤、重复登记/释放、异常清理、晚回调、同资产多 owner、PRELOAD 重入、UGUI/FGUI 兼容和未覆盖真实生命周期。
- Required review output：正确输出 ## REVIEW_PASS；需修复输出 ## SLAVE_FIX_HANDOFF，仅包含具体问题、原因、路径/符号、修正与验证，不生成大范围第二方案。

