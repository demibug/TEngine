# 第 4 批：UGUI / FGUI 窗口生命周期执行契约

日期：2026-09-08。状态：规划完成，尚未实施。工程：E:/MyWork/MyFramework/TEngine/UnityProject/。
前置：用户确认资源批次完成。本轮只读取其 verification，不代替资源批次独立验收；其中记录的 FGUI 并发测试失败纳入本批复现。
来源：[G09–G11 候选](candidates.md)、[框架生命周期地图](architecture.md)。

## Role

你是主要 slave 实施者，独立完成编码、调试、测试。读取 AGENT.md、CLAUDE.md 和 tengine-dev 的 UI 生命周期/事件/资源规范，以实际代码为准。
不调用其他 agent、不调用 master-planner。完成后供用户手动发给新 master 审查。

## Objective

等待打开成功时窗口已经完成创建和刷新；重复打开共享正确的创建结果；加载中关闭、关闭后重开不会让旧任务复活窗口；清理过程中某个 hook 抛异常，仍释放其余框架持有资源。
保持 GameModule.UI 与 GameModule.FGUI 并存，不统一成一个新 UI 管理器。

## Existing architecture / 直接核验事实

以下路径相对工程根：
- Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs：ShowUIAwaitImp 在已有窗口时直接返回；首次创建轮询 IsLoadDone，60 秒后无论成功与否都返回对象。GetUIAsyncAwait / GetUIAsync 有同类超时返回问题。
- 同目录 UIWindow.cs：Handle_Completed 在组件校验及 InternalCreate / InternalRefresh 之前设置 IsLoadDone；null panel 直接返回。InternalDestroy 在 OnDestroy 等用户 hook 之后才设 IsDestroyed/移除计时器。
- UIBase.cs：RemoveAllUIEvent 归还池化 _eventMgr 后没有摘除字段，可能持有被其他 owner 复用的 manager。
- IUIResourceLoader.cs 已支持 CancellationToken；UIWindow.InternalLoad 尚未传递创建周期 token。
- Module/FguiModule/FguiModule.cs 已有 _creations、Completion、WaiterCount、超时和 package lease；ShowAsync 已有窗口分支刷新后未验证窗口仍属于当前注册项。
- 同文件创建路径在可见性 hook 完成前写入 _windows；失败清理和字典/模态状态需配套核对。FguiWindow.InternalDestroy 已有逐项清理，不应重写正确机制。
- Assets/Tests/FairyGUI/PlayMode/FguiWindowModuleTests.cs 的 WindowModule_MergesConcurrentShow_AndClosePreventsLateWindow，在资源批次 verification 中被记录为失败；本轮不认定原因或擅自归咎资源实现，实施时先独立复现。

## Implementation contract

### 1. UGUI 状态与成功边界

每次创建有独立状态/完成源与取消源，最少区分 Loading、Ready、Closing、Closed、Failed。
不用 IsLoadDone 轮询充当成功证明；保留既有字段/API，如需补充内部状态不改变外部签名。
Ready 仅在实例有效、必要组件校验、Inject/ScriptGenerator/BindMemberProperty/RegisterEvent/OnCreate、首次 OnRefresh、层级与可见性处理完成，且窗口仍为本次有效实例后提交。
窗口不必位于最顶层或实际可见（允许被全屏窗口遮挡）；成功表示自身生命周期已准备好，并非资源句柄完成。

ShowUIAsyncAwait：成功返回 Ready 对象；加载失败/创建或刷新异常抛出带窗口上下文的异常；关闭取消抛 OperationCanceledException；创建超时抛 TimeoutException。不再返回未准备/已销毁对象。
这是明确的行为修正；不要改成 null 吞错。资源层取消返回 null，UI 根据自己的周期 token 区分取消与加载失败。
沿用既有 60 秒创建上限但使用不受 Time.timeScale 影响的时间；覆盖创建和首次准备，超时必须结束本次任务并清理，不只是退出等待循环。
void Show 包装观察并记录真实失败，预期关闭取消不记录 Error；同步 Show 保留同步执行，不暗中改为异步。

GetUIAsyncAwait：找不到返回 null；找到 Loading 则等待该实例完成；外部 token 只取消本次等待，不关闭窗口。失败/关闭/超时按该实例终态传播，不转而等待同类型新实例。
GetUIAsync 回调最多一次，缺失或失败给 null，真实失败同时记录；禁止将已销毁对象交给回调。原缺失不回调的行为修正需写明并检查现有调用方。

### 2. 重复打开与刷新

UGUI 同类型 Loading 时只创建一次；所有 await 调用者等待同一个可多等待完成源（不可多次 await 单消费 UniTask）。
保留 UGUI 的最新 userDatas 更新意图：Loading 期间最后一次 Show 数据用于首次刷新，不为每个等待者重复创建/注册事件。
Ready 时每次 Show 仍按当前行为置顶并刷新，不重新执行 OnCreate/RegisterEvent。刷新 hook 失败时该次请求失败，并关闭此实例以避免部分刷新状态残留。
同步 Show 遇到同类型 Loading 明确抛冲突异常，不假装同步打开成功；检查现有调用点并记录兼容性变化。
用户 hook 中 Close、CloseAll 或关闭后重新 Show 必须受实例身份保护；旧执行栈不得清理/弹出新窗口。hook 返回后必须复核状态才能进入下一步或返回成功。

FGUI 保留现有首个创建者 userData 语义及“取消一个等待者不伤其他等待者；无人等待可取消创建”，不强行改成 UGUI 的最新数据规则。
已存在窗口的异步刷新按调用顺序串行执行，每个调用各有结果，不并发运行同窗 OnRefreshAsync；等待者 token 与窗口生命周期联合取消。刷新失败关闭该实例，取消调用者本身不自动关闭已存在窗口。
刷新结束、显示及返回前检查同一实例仍有效；关闭后不能继续 BringToFront 或返回成功。排队中已取消请求不得调用 hook。
不创建通用队列系统，仅实现本模块同窗刷新协调。

### 3. 关闭、晚到任务和失败回收

Close 在任何可重入 hook 之前标记失效并按实例身份移出注册/栈，使立刻重开可创建新实例。
关闭 Loading：立即结束其所有等待者并取消创建；即使资源加载器不响应 token，晚到实例也只能被销毁，不能进入 OnCreate/OnRefresh/注册表。
FGUI 同样立即摘除旧 creation；重开不得接到旧取消完成源。旧 finally 不删除新 creation。
资源/异步 hook 无法被强行终止：要求传入生命周期 token，并在每次 await 后检查。框架不得在失效后启动下一 hook 或提交成功；不承诺阻止业务忽略 token 后自行访问对象。
FGUI 可见性 hook 抛错或重入关闭时，字典、模态计数、View 和 package lease 必须一起收尾；只有当前实例可移除对应注册项。
关闭和超时均只清理本代对象；不遗留没有等待者且无 owner 的实例或包租约。

### 4. 清理与所有权

UGUI 关闭前摘除 _prepareCallback、计时器 id、事件 manager 等待释放字段；重复关闭/清理不重复归还。
RemoveAllUIEvent 使用局部变量归还并先置 _eventMgr=null；关闭中的 owner 不允许通过 hook 再注册新 UI 事件/子项。
逐项清理事件、子 widget、OnDestroy、实例、自动关闭 timer；一项抛异常仍尝试其余项，异常可见且栈/可见性最终一致。
覆盖 CloseUI、CloseAll、指定层关闭、隐藏到期关闭及已有 Shutdown 的局部清理入口；不调整全局模块退出顺序。已不存在的 timer/module 不为清理重新创建。
Hide 保持保留窗口与事件的既有语义，不等同于 Close；重新显示应取消旧自动关闭 timer。
不要新加全局 timer/task registry；只清理当前框架确实登记的任务和计时器，未纳管业务任务由业务配合 token。

LoadGameObject 返回的实例只 Destroy，由 AssetsReference 归还 prefab 引用；UI 不再手动 UnloadAsset 同一份引用。
FGUI View、package lease、Lifetime 的既有唯一 owner 保留，不让通用资产池接管。
GameEvent 保留本轮分发快照语义：解绑不撤回已进入的分发，不修改事件核心；不宣称关闭能阻止当前同步事件中的所有业务回调。

## Relevant files / symbols 与范围

允许修改：
- Assets/GameScripts/HotFix/GameLogic/Module/UIModule/ 的 UIModule.cs、UIWindow.cs、UIBase.cs、UIWidget.cs 及必要内部辅助类。
- IUIResourceLoader.cs 仅必要内部适配/注释，不破坏现有实现者签名。
- Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/ 的 FguiModule.cs、FguiWindow.cs、FguiLifetimeScope.cs：仅上述窗口生命周期与已复现问题所需局部修改。
- 新增 Assets/Tests/UILifecycle/ 与已有 Assets/Tests/FairyGUI/ 内的针对性测试及 asmdef/meta。
- docs/framework-reference/project-old/synthesis/ui-lifecycle-verification.md 与必要结果记录。

不改资源/事件/对象池核心、FGUI SDK、资源桥接/包管理算法、布局/生成文件、业务 UI、资源配置、RootModule 全局关闭顺序。
如果复现证明根因必须修改 FguiPackageService 或资源层，输出 PLAN_CONFLICT 给出最小证据，不偷偷扩大范围或靠修改测试期望掩盖失败。

## Implementation steps

1. 记录 git status/diff，读取本批调用链和测试；先复现 FGUI 已记录失败，区分测试错误与生产问题。
2. 添加 UGUI 加载/创建/关闭可控测试，建立每实例完成源和终态，接通全部 Show/Get/Close 入口。
3. 修复清理异常与 manager 归还；补齐 FGUI 并发、晚到完成、刷新关闭竞态。
4. 运行针对性验证并记录兼容变化、结果、未运行项，结束并生成手动审查交接。

## Acceptance criteria / Verification

UGUI 必测：
- 两个 await 同窗且资源未完成：均不提前返回，只加载/创建一次，首次刷新采用最后参数。
- null/异常加载、缺 Canvas、创建/刷新/显示 hook 抛错：等待者失败，实例/栈/事件/timer 无残留，再开可成功。
- Loading 关闭后立刻重开：旧等待者取消，旧资源晚到被销毁，不影响新窗口。
- 创建/刷新 hook 自己关闭或关闭后重开：不返回已销毁对象，不误删新对象。
- timeScale=0 时超时仍生效；Get 的取消不关闭窗口；Get 回调不收到半成品。
- 某子项/OnDestroy 清理抛错仍清理剩余项；重复关闭无双重归还。
- 池化 GameEventMgr 被另一个 UI 复用后，旧 UI 再清理不移除新 owner 的订阅。
- Hide/Show/自动关闭 timer 兼容，普通成功路径排序/全屏遮挡不回归。

FGUI 必测：
- 原 WindowModule_MergesConcurrentShow_AndClosePreventsLateWindow 通过；取消一个 waiter、关闭后立即重开、失败后重试均有独立断言。
- 已有窗口两个异步刷新串行；关闭/取消发生于等待或 hook await 时不返回失效窗口。
- 创建显示 hook 抛错、自关闭：无字典残留、模态计数错误或 package lease 泄漏。
- 保留现有 Lifetime 事件/timer/外部监听清理回归。

运行相关 Unity 编译、针对性 EditMode/PlayMode，真实 GameObject 与 FGUI View 生命周期不能全用 mock 替代。
计数验证实际实例、租约、事件和 timer，而非仅“无异常”。测试超时可用内部注入缩短，不让每例真实等待 60 秒。
记录实际命令/入口、用例数、日志、结果与未运行项；不要求 Player 构建、真机全平台或性能测试。
git diff --check、新增 meta、修改范围核对；保留资源批次未提交变更，不暂存/提交/恢复/清理。
本规划轮未运行上述测试。

## Handling unexpected repository reality

小差异自行解决。若必须改变冻结的兼容策略或修改范围外模块，输出 ## PLAN_CONFLICT，给出具体路径/符号、复现与需要重议的决策，不委派。

## Completion requirements

留下完整实现、验证记录和所有相关测试，不自动开始第 5 批启动退出协调。
结束输出 ## MASTER_REVIEW_HANDOFF，自包含地交给用户手动发往新 master。

## Required master review handoff

包含 Role（直接查共享工作树，不重实现/不委派）、Original objective、关键状态/成功/取消/重入/所有权契约、实际实现摘要、全部修改文件、测试命令与结果、已知限制和未运行项。
Review priorities：提前成功、等待不结束、关闭复活、旧任务误删新实例、清理异常、manager 复用、FGUI 模态/租约残留和 API 兼容。
正确输出 ## REVIEW_PASS；需修复输出 ## SLAVE_FIX_HANDOFF，仅列问题、原因、证据路径/符号、修正与验证，不形成新的大范围计划。

