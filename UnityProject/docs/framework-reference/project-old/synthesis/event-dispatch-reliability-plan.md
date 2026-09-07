# 第 1 批：TEngine 事件分发可靠性执行契约

日期：2026-09-07。状态：规划完成，待用户手动交给实施者；本轮未改生产代码。
基线 HEAD：`16afccb5df2a2a2efcb5003ecf9fbc0781c0170a`，实际工作树包含尚未提交的 FGUI 接入等变更。
来源：[G13 / S04-C001](candidates.md)、[M-E014](architecture.md#m-e014)。原研究修正仍独立跟踪，不将本计划视为整体研究 REVIEW_PASS。

## Role

你是主要 slave 实施者，负责本契约的编码、调试与验证。当前工程为
`E:/MyWork/MyFramework/TEngine/UnityProject/`，先读取适用仓库规范和 tengine-dev。
不得调用其他 agent、切换模型或调用 master-planner。只实施本批，完成后生成手动审查交接。

## Objective

保持 TEngine GameEvent 同步接口，使回调抛异常、嵌套分发及回调内增删监听之后，事件系统仍可继续正确分发和解绑。
FGUI 已接入，UGUI 与 FGUI 并存；本批修正二者使用的通用 GameEvent 核心，不重新接入 FGUI。

## Existing architecture 与源码依据

以下路径均相对上述当前工程根；符号为稳定定位，行号是规划时定位。

- `Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs:32` 的 AddHandler 只检查已生效列表，未检查待新增列表；RmvHandler 在 :57，CheckModify 在 :76。
- 同文件 :101–263 的 Callback 全部 0–6 参数重载，仅在正常尾部调用 CheckModify。回调异常会跳过恢复；布尔执行标记无法表达同事件嵌套深度。
- `Assets/TEngine/Runtime/Core/GameEvent/EventDispatcher.cs` 的 AddEventListener / RemoveEventListener / Send 将工作转发至每个事件的 EventDelegateData；ClearEventTable 清空字典。
- `Assets/TEngine/Runtime/Core/GameEvent/GameEvent.cs` 是全局入口；Shutdown 调用 `EventMgr.cs` 的 Init，清空接口表和事件表。
- `Assets/TEngine/Runtime/Core/GameEvent/GameEventMgr.cs` 的 AddEvent 仅在注册返回 true 时记录订阅；Clear 按记录解绑。
- `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiLifetimeScope.cs` 的 AddUIEvent / Dispose 使用 GameEventMgr；FairyGUI 原生 EventListener 是另一条调用链。
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs:324` 的 RemoveAllUIEvent 归还 GameEventMgr。UI owner 池化引用问题属于后续 UI 批次。

确认事实是上述控制流；下面的分发语义是本次明确选择的设计，不能反写成当前源码已经具备的事实。

## Implementation contract

1. 保留现有公开签名、同步调用方式与注册顺序。首个回调异常立即结束该次 Send，原异常直接向上传播，不吞掉、不包装、不继续执行余下回调。正常返回只代表同步分发完成。
2. 每个事件独立维护分发深度，所有 Callback 重载用 finally 恢复。只有该事件最外层分发退出时提交待变更列表；即使异常退出也提交，不做业务回滚。
3. 允许同事件同步嵌套 Send。整个同事件最外层调用及其嵌套调用使用稳定的已生效监听列表；新增、删除均在该树结束后可见。不同事件各自提交。调用者仍须避免无限递归，本批不引入深度上限。
4. 增删以“按调用顺序应用全部待操作后”的成员关系判定。委托相等采用正常 Delegate 相等语义。待新增重复注册返回 false，第一次返回 true；移除后再注册返回 true 并排到末尾；新增后移除最终不存在。
5. 无分发时增删立即生效。移除不存在监听不抛新异常；保留现有非分发缺失监听与重复注册的日志惯例，分发中重复移除可无操作。不要借机新增 null、参数签名或线程校验政策；保留现有不匹配 Action 类型被跳过的行为。
6. 保留已进入分发的监听可执行到本次结束的语义：A 回调移除 B 或 Dispose B 的 owner，B 仍可能在本次及尚未完成的嵌套分发被调用；后续最外层 Send 才不再调用。owner 的即时失效防护属于 UI 批次，不宣称 Dispose 能撤回已进入的分发。
7. Shutdown / Init 的清表语义不扩展：正在执行的旧事件对象可以完成当前调用；其 finally 不得把旧监听重新写回 dispatcher。清表后注册的新事件对象独立生效。
8. 不变更 payload 所有权，不释放调用方参数；不增加线程安全、异步队列或统一事件中心。本契约适用于现有同步单线程使用，不新增跨线程保证。

兼容性变化明确限于：嵌套时不再提前提交；重复的待新增会被拒绝；移除再新增按顺序生效。旧实现“统一先加后删”的结果不再作为契约。
其余公开重载不补齐、不重命名；尤其不要将 GameEvent.Send(eventType, Delegate) 擅自解释成直接调用传入委托。

推荐内部实现：分发深度 + 首次有效变更时懒拷贝的下一版本监听列表；外层 finally 替换已生效列表并清理待状态。
可使用等价实现，但无变更的 Send 不得新增列表拷贝、闭包或逐回调分配，不使用反射 / DynamicInvoke。没有测量不得声称性能提高。

## Relevant files / symbols 与允许范围

- 主要生产修改：`Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs`。
- `EventDispatcher.cs`、`GameEvent.cs` 可补充契约注释；若需要改变行为，必须先证明是上述契约所必需，不扩大公开 API。
- 新增 `Assets/Tests/GameEvent/EditMode/` 测试和对应 asmdef / meta；核心测试引用 `TEngine.Runtime`，通过公开 EventDispatcher 测试，避免为了测试扩大生产可见性。
- 如做 FguiLifetimeScope 回归，允许在 `Assets/Tests/FairyGUI/EditMode/` 新增独立测试文件，不修改现有 FGUI 实现。参考现有 GameLogic.FairyGUI.Tests.asmdef；若实际直接使用 TEngine 类型，按实际编译依赖补充测试程序集引用。
- 实施记录写 `docs/framework-reference/project-old/synthesis/event-dispatch-reliability-verification.md`。
- 不修改 SDK、FGUI/UGUI 生产实现、RootModule、构建/资源/启动模块、六份原研究及旧 handoffs。所有既有变更必须保留。

## Implementation steps

1. 记录 git status / diff，直接读取涉及的未跟踪测试和 asmdef；核对上述入口，确认事件核心没有与本计划冲突的新增改动。
2. 添加能复现异常后失效和嵌套提前提交的行为测试，再实现统一深度恢复及顺序增删。覆盖全部 0–6 参数 Callback，不能只修无参版本。
3. 检查 GameEventMgr 的 pending 注册后 Clear，以及现有 FGUI scope 的解绑调用兼容性。仅在目标边界内做回归，不重审 FGUI 包管理。
4. 运行针对性验证，记录结果和未运行原因，核对修改范围，提交手动 master 审查提示词。本批通过前不自动推进第 2 批。

## Acceptance criteria

| 场景 | 必须观察到的结果 |
| --- | --- |
| A 抛出指定异常，后面有 B | 同一异常向上传播，B 本次不执行；移除 A 后再次 Send 能调用 B |
| A 添加 C 后抛出 | finally 提交 C；后续移除 A 并 Send 能调用 C，系统不再卡在执行态 |
| A 删除 B | 当前 B 仍执行，下一次不执行 |
| A 添加 C，期间有受控同事件嵌套 | 当前及嵌套均不见 C；下次见 C，旧监听不重复、不漏掉 |
| 初始 A、B、C；A 一次性删除 B 再添加 B | 本次仍 A、B、C，下次 A、C、B |
| 待新增 C 两次 / 新增 C 后删除 C | 前者返回 true、false 且最终只有一份；后者最终没有 C |
| 内层抛错被外层回调捕获，之后外层继续修改 | 内层退出不提交；最外层退出后按序提交，后续分发正常 |
| 两个事件交叉 Send | 各自深度与提交互不污染 |
| GameEventMgr 分发中 AddEvent 后 Clear、重复 Clear | 下一次没有残留；重复清理不新增订阅或抛新异常 |
| 回调内 Shutdown 后重新注册同一事件 id | 后续仅新监听生效，旧待变更不会复活 |
| GameEvent 的 int/string 常用入口及 0–6 参数核心分发 | 参数原样传递，异常恢复规则一致，既有 API 可编译 |
| FguiLifetimeScope 的 GameEvent 注册/Dispose | 普通 Dispose 后不再接收后续 Send；分发中 Dispose 遵循第 6 条 |

测试中的增删和嵌套需用一次性标记控制，避免测试自身无限递归或在每次 Send 重复变更。

## Verification

- 优先 Unity EditMode 行为测试，核心测试使用独立 EventDispatcher，覆盖 0–6 参数成功与异常恢复；其他组合通过代表性重载覆盖，避免复制整套矩阵七遍。
- 全局 GameEvent 测试不并行运行，前后清理，保证无跨测试订阅污染；按实际日志等级匹配预期重复注册日志。
- FGUI scope 回归仅覆盖事件解绑，不需要图形资源或完整 UI 场景。
- 完成相关程序集 Unity 编译及上述测试，记录实际命令/入口、用例数、结果与日志位置。工具不可用则明确“未运行”，不能用静态阅读替代测试通过。
- 检查 git diff --check、新增未跟踪文件、API 签名及修改清单；不要求 Player 构建、全量游戏测试或性能 benchmark。
- 当前规划轮只做源码阅读和文档检查，以上测试均是实施者待执行项。

## Constraints

不暂存、不提交、不恢复、不清理已有变更。旧工程 `D:/Work/SAUnity/ProjectOld/` 只读。
无业务迁移、无第三方事件分发器替换、无网络/持久化格式变化。
将 UGUI owner 池化引用、FGUI 完整生命周期审查、跨线程事件、类型安全和全局退出顺序留给另行批准的工作。

## Handling unexpected repository reality

小幅路径或测试设施差异自行解决并记录。若源码证明冻结语义不可行、已有调用依赖必须破坏的行为，或必须修改范围外生产模块，停止相关实施并输出 `## PLAN_CONFLICT`，给出具体路径/符号、证据和需要重议的唯一决策，不委派、不扩大计划。

## Completion requirements

留下完整实现和测试，简述行为变化、验证及未解决限制，不贴巨大 diff。结束时输出 `## MASTER_REVIEW_HANDOFF`，供用户手动发给新的 master。

## Required master review handoff

交接必须自包含，包括：
- Role：最终正确性审查者；从共享工作树 git status、git diff 和新增文件开始，按本契约审查，不重实现、不调用 agent。
- Original objective：事件异常后可继续使用，嵌套与监听增删行为确定，UGUI/FGUI 共享入口兼容。
- Important implementation contract：复制上述异常传播、最外层提交、顺序成员关系、清表隔离、owner 延迟解绑及 API/范围约束。
- Implementation summary、Changed files：实际实现摘要及所有本批相关文件，区分基线已有变更。
- Tests/checks already run：实际命令或入口与结果；Known deviations or concerns：未执行项、偏差及风险，没有则写 none。
- Review priorities：异常路径、全部重载、嵌套与清理交错、重复注册、清表后复活、API 回归和测试缺口，忽略不影响正确性的排版。
- Required review output：满足契约输出 `## REVIEW_PASS`；需修复输出 `## SLAVE_FIX_HANDOFF`，仅含具体问题、原因、证据路径/符号、修正和验证，不启动其他批次。

