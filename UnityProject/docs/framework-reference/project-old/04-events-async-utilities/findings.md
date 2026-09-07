# 深入发现：事件、异步与通用运行时机制

## 1. 先给结论

以下结论只针对本轮读到的代码，不把目录名、旧文档或类名当作可达性证明。

| 结论 | 证据 |
|---|---|
| OLD 的跨模块通知不是单一同步事件：模块内部事件立即执行；模块中心的通知先入双缓冲操作队列；面向非模块调用方的同名监听又可由 ModuleMgr 立即派发。 | S04-E006、S04-E007 |
| OLD 的 EventDispatcher 对分发中增删有明确策略：新增追加到当前列表尾部、删除标记后延迟移除、同类型嵌套分发拒绝；每个回调异常被捕获并继续后续回调。 | S04-E003、S04-E004 |
| CURRENT 的 EventDelegateData 也延迟应用分发中增删，但只在正常返回时调用 CheckModify；回调抛异常会使执行标志和待处理变更停留在中间状态。它没有同事件重入保护，分发中移除的后续 handler 仍可能在当前循环执行。没有代码证明它具有主线程或线程安全保障。 | S04-E019 |
| OLD 具有按对象/组批量解绑和模块自带 listener group；CURRENT 的 GameEventMgr 以委托列表实现局部所有权，但 UIBase 归还池后没有把字段置空，存在后续错误复用的静态风险。 | S04-E005、S04-E008、S04-E020 |
| OLD 的任务取消中心提供全局、模块、面板层级；实际可达证据最明确的是启动注册和面板 linked CTS。CURRENT 没找到同构的全局任务管理器，资源异步链采用调用方 token、替换取消和 finally 清理，但 transfer 标志在 SetAsset 前设置，用户回调抛异常时仍有池对象/资源引用清理缺口；组件销毁路径只 Cancel、不等待任务。 | S04-E010、S04-E011、S04-E023、S04-E028 |
| OLD 的 FairyGUI.Timers 由隐藏 GameObject 的 Unity Update 驱动，默认不捕获回调异常；CURRENT TimerModule 由 ModuleSystem Update 驱动，取消是标记后延迟删除，普通 timer 回调也没有 try/catch。 | S04-E013、S04-E021、S04-E022 |
| OLD 没有在 framework 范围发现统一通用 FSM，读到的是 game 业务自己的两套 FSM；CURRENT 有 FsmModule/Fsm/Procedure 的通用链路，并由 ModuleSystem 统一更新和关闭；但 RootModule 的关闭调用受 `!UNITY_EDITOR` 条件限制，FSM 清理回调异常时 map/池归还也可能中断。 | S04-E016、S04-E024、S04-E029、S04-E030 |
| CURRENT MemoryPool/ObjectPool 已有 Clear 重置、开发期 double-release 检查、spawn count/locked 释放条件；但 Acquire/创建中途失败和 Release 回调抛异常时没有统一的 finally 保护。 | S04-E025、S04-E026 |

这些结论的候选设计见 [candidates.md](candidates.md)。候选不是实施批准。

## 2. 机制分层与归属

### 2.1 OLD 的归属

- ZeroFramework.EventDispatcher、NotifyDispatcher、CallbackVo、UniTaskManager、AsyncTodo、RunTimer、ModuleMgr/BaseModule 属于项目自研框架源码。Assets/Scripts/framework/Framework.asmdef 的程序集名为 dls.framework；通用公共目录另有 dls.framework.common。这是程序集边界证据，不等于每个类都已被启动路径使用（S04-E002）。
- FairyGUI.Timers 位于 Assets/Scripts/framework/FairyGUI/Scripts/Utils，命名空间为 FairyGUI，源码带 FairyGUI 风格接口和项目扩展文件。它在本工程树中是可读源码分支，但第三方原始能力、项目 fork 和业务调用必须分开记录（S04-E013）。
- GEvent<T>/GEvent 是轻量委托包装，不是 EventDispatcher 的同一实现；它在业务对象上有大量实际 Do 调用，但只提供单个多播 Action 和 Proxy（S04-E009）。
- 两套 FSM 位于 Assets/Scripts/game，命名空间分别是 PlayerOp 和 KSBattle；它们是业务实现，不应被描述成 OLD framework 通用设施（S04-E016）。

### 2.2 CURRENT 的归属

- TEngine.Runtime 提供 GameEvent、TimerModule、FsmModule、MemoryPool、ObjectPoolModule、ModuleSystem 等自研运行时；GameLogic 是业务/热更程序集引用方（S04-E017）。
- GameEvent 的 EventInterfaceAttribute、Roslyn analyzer DLL 和 GameEventHelper.Init() 表明存在生成式适配路径；生成后的包装源文件没有出现在当前资产树，因而只能确认入口和配置，不能静态确认完整生成结果或所有调用方可达（S04-E017）。
- YooAsset 句柄属于第三方资源能力；CURRENT 的 AssetObject 是 ObjectPool/MemoryPool 与 YooAsset 之间的适配层，不把句柄释放策略误归为 YooAsset 默认行为（S04-E027）。

## 3. OLD 事件与跨模块通知

### 3.1 EventDispatcher：接口、状态与所有权

EventDispatcher 以字符串为键维护 Dictionary<string, EventVo>；监听项是 CallbackVo，记录回调、优先级、自动移除、可取消、组名和参数包装。添加时通过回调 hash 查重，非分发期间按 level 排序；泛型重载把业务 listener 包在一个 Action<CallbackVo> 中，并在包装层检查参数为空与运行时类型（S04-E003）。

它提供三种明显的生命周期入口：

1. 精确回调解绑：RemoveEventListener(type, callback)。
2. 按目标对象解绑：扫描 callback.Target == o 的项。
3. 按 group 批量解绑：匹配 group，或用 all 清理全部。

按对象/组解绑会先取得 ListPool 临时列表，再调用私有按 type/id 移除；分发期间的私有移除不是立刻改 List，而是给 CallbackVo 标记 TobeRemove。这使面板、模块可以用 owner/group 作为批量清理边界，而不需要保存所有精确回调引用（S04-E003、S04-E005）。

CallbackVo.Dispose 具备清空回调、目标、参数和状态的钩子，并把 ParamVo 归还池；但在本轮围绕 EventDispatcher 的搜索中没有找到 CallbackVo.Dispose 的实际调用闭环。因此“定义了 pooled callback 的清理动作”是代码确认，“每次解绑都会执行该 Dispose”不是已证明事实（S04-E005）。

### 3.2 分发中增删、重入和异常

OLD EventDispatcher 的 DispatchEvent 先检查事件不存在和同类型 IsDispatch。同类型嵌套分发直接记录错误并返回。正常分发时：

- IsDispatch = true；
- 以 i < eventList.Count 遍历，所以回调期间新增项会追加并可能在本次分发尾部执行；
- 删除项只标记，当前循环跳过，循环后反向真实删除；
- 自动移除也走同一标记；
- PreventDefault 只在 callback 可取消时生效，并会终止后续项；
- 每个回调外围都有 try/catch，异常只记录日志，不阻止后续 handler。

这套行为是代码直接写出的分发契约，不是从注释推导（S04-E004、S04-E005）。

但它不是无缺口的“事务分发”：

- 如果某个回调中 Dispose 整个 dispatcher，代码会提前 return，跳过尾部清理和 IsDispatch = false；这是静态可见的异常路径，是否在产品运行中触发待验证。
- IEventParam 在每个 callback 后都会尝试归还池，而同一 data 可能被多个 listener 看到；若归还后对象马上被复用，后续 listener 观察到的内容存在风险。这是由调用顺序推断的设计风险，不是本轮运行测试结论。
- EventParam<T> 要求实现 Reset，但池的 Release 只把对象放回队列，本轮未见统一调用 Reset；“回池不自动重置业务字段”是代码确认（S04-E004、S04-E005）。

### 3.3 NotifyDispatcher：排队的跨模块层

NotifyDispatcher 不直接把 Register/Unregister/SendNotify 改入 notify map，而是把操作写入当前操作列表；Update 时先切换到另一列表，再执行旧列表。这样在处理一个批次时新注册、注销或发送会进入下一批，不会改变当前操作批次的遍历对象（S04-E006）。

处理 Send 操作时，handler 按列表顺序调用；每个 handler 有独立 try/catch，异常记录后继续；发送完成回调也独立捕获。若数据实现 IRecyclable，则该操作处理后调用 Return。这里的“异常隔离”强于 CURRENT EventDelegateData 的当前实现，但池对象的具体 Reset/Return 语义仍由 IRecyclable 实现决定（S04-E006）。

ModuleMgr 的两个更新阶段说明了时间语义：

- LateUpdate 先调用 m_dispatcher.Update()，再调用各模块 LateUpdate；
- BaseModule.SendNotify 先立即派发模块自己的 m_dispatcher，再把通知交给中心；
- ModuleMgr.SendNotify 会把通知排入中心队列，同时对面向非模块调用方的 _notifyDispatcher 立即 DispatchEvent。

因此同名通知可以拥有“模块 handler 下一次中心 Update 才处理”和“外部 EventDispatcher listener 当前调用栈立即处理”两种可观察时机。这里不是简单的同步/异步二选一，而是两条依赖不同 owner 的通道（S04-E006、S04-E007）。

### 3.4 完整的实际订阅—解绑闭环

本轮选取 TimelineMgr 的通知订阅，因为它同时能看到业务入口、框架转发和关闭路径：

~~~mermaid
flowchart LR
    A[CommonModule 初始化] -->|RegisterNotify| B[TimelineMgr]
    B -->|AddNotifyListener 六类通知| C[ModuleMgr]
    C -->|AddEventListener| D[ModuleMgr._notifyDispatcher]
    E[Timeline/其他调用方 SendNotify] --> C
    C -->|立即派发外部监听| D
    C -->|SendNotify 入队模块通知| F[NotifyDispatcher]
    F -->|LateUpdate.Update| G[模块 HandleNotify]
    H[CommonModule.OnDispose] -->|UnRegisterNotify| B
    B -->|RemoveNotifyListener 六次| C
    C -->|RemoveEventListener| D
~~~

具体链条是：

1. CommonModule.cs:242 调用 TimelineMgr.Inst.RegisterNotify()。
2. TimelineMgr.RegisterNotify:113-125 注册 TimelineFinish、CityMove、TimelineEvent、TimelineOperationFinish、CurGuideFinish、NoviceGuideIdChanged 六项。
3. ModuleMgr.AddNotifyListener:332-335 直接委托 _notifyDispatcher.AddEventListener，默认没有 group。
4. 发送路径由 ModuleMgr.SendNotify:389-404 同时进入中心队列，并立即调用外部事件 dispatcher。
5. CommonModule.cs:632 在 OnDispose 调用 TimelineMgr.Inst.UnRegisterNotify()；TimelineMgr 的六个 RemoveNotifyListener 再回到 EventDispatcher 的精确解绑。

这个闭环证明了“该实例的注册有对应注销”，也暴露了所有权是由 CommonModule 的生命周期驱动，而不是由 TimelineMgr 自己持有一个可复用 subscription handle。若 CommonModule 关闭未走到该分支，或 TimelineMgr 被单独替换，静态代码没有第二层兜底（S04-E008）。

### 3.5 GEvent 的简易事件边界

GEvent<T> 和无参 GEvent 用一个 Action 保存多播回调，Add 通过先减后加避免同一个 delegate 重复，Proxy 暴露写操作；Do 只设置一个 m_doing 标志后调用 Action。它没有 EventDispatcher 的逐 handler 遍历、按组解绑、优先级、自动移除或异常隔离（S04-E009）。

泛型版本 Remove 会正确将 HasListener 设为 m_action != null；无参版本的 Remove 源码却将其写成 m_action == null，与属性含义相反。两者 Do 都没有 try/finally：回调抛异常时 m_doing 可能永久保持 true。这些是静态缺陷/风险记录，不代表本轮已在运行时复现（S04-E009）。

## 4. CURRENT GameEvent 与事件所有权

### 4.1 核心接口和生成式入口

CURRENT 的低层 EventDispatcher 以 int 为 key，GameEvent 再提供 int 与 string（RuntimeId 转换）的静态重载；EventMgr 还维护 Type 到生成式接口 wrapper 的映射。GameEvent.Shutdown 通过 EventMgr.Init 清空 wrapper map 和 dispatcher 表，但本轮未找到它被 ModuleSystem.Shutdown 或 RootModule.OnDestroy 调用，因此这是可手动调用的清理能力，不是已证实的全局关闭闭环（S04-E017、S04-E018、S04-E029）。

当前 GameApp 在入口调用 GameEventHelper.Init()，业务接口 ILoginUI 带 EventInterface(EEventGroup.GroupUI)。但 GameEventHelper 具体源文件和生成的 wrapper 不在 Assets 源码中；因此“生成机制被配置并被入口调用”已确认，“运行后能取得 ILoginUI wrapper 且所有事件 ID 正确注册”仍待 Unity 编译/运行验证。GameEvent.Get<ILoginUI>() 在当前 GameApp 中是注释示例，不当作实际业务调用证据（S04-E017）。

### 4.2 EventDelegateData 的分发策略

EventDelegateData 有 _listExist、_addList、_deleteList、_isExecute 和 _dirty。执行期间 Add 放入 _addList，Remove 放入 _deleteList；正常返回时 CheckModify 先加后删。因此与 OLD 不同：

- 当前分发期间新增 handler 不参加本次循环；
- 删除 handler 也延迟到本次循环结束才从实际列表移除；
- 当前实现没有同类型嵌套分发拒绝逻辑；嵌套 Send 会再次进入同一个 Callback，形成递归调用；
- 循环体不检查 `_deleteList`，因此回调中移除的后续 handler 仍可能执行本次循环；CheckModify 按“先加后删”应用，分发期间 Add/Remove 同一 handler 的结果受该顺序影响；
- 低层 EventDelegateData 没有 lock、线程检查或主线程断言。

最大差异是回调异常路径：Callback 方法直接调用 action，之后才调用 CheckModify，没有 try/finally。若某 handler 抛异常，剩余 handler 不会继续执行，_isExecute 也不会被复位，待添加/待删除列表不会应用；后续操作可能持续进入待处理列表。这是从控制流直接得到的静态结论，但异常后调用者是否继续使用同一事件对象需要运行验证（S04-E019）。

此外，Add 的重复检查只查看 _listExist，没有查 _addList；同一次分发中重复添加同一个委托可能排入多次。上述嵌套调用和分发中取消的控制流是静态可确认的，具体业务是否依赖这些边界仍需运行验证（S04-E019）。

### 4.3 GameEventMgr 与 UI 生命周期

GameEventMgr 是 IMemory 对象，内部保存 eventType/Delegate 两个平行列表。每次 AddEvent 只有在全局 GameEvent 添加成功后才记录；Clear 遍历记录并调用 GameEvent.RemoveEventListener，再清空列表（S04-E020）。

UIBase 的 EventMgr 属性按需从 MemoryPool.Acquire<GameEventMgr>() 获取；UIWindow.InternalCreate 在 _isCreate 为 false 时调用 RegisterEvent；InternalDestroy 将 _isCreate=false 后调用 RemoveAllUIEvent。RemoveAllUIEvent 只 MemoryPool.Release(_eventMgr)，没有 _eventMgr = null（S04-E020）。

代码确认的生命周期是“UI 创建 → 局部 manager 记录委托 → UI 销毁 → Clear 解绑并归还池”。静态风险是“销毁后 UIBase 仍持有已归还的 manager 引用”：如果同一 UIWindow 再次创建，或池把该 manager 分配给其他 owner，旧 UIBase 访问属性时可能操作不属于自己的委托列表。是否真实发生取决于 UI 对象复用和重新创建方式，不能仅凭源码声称已经泄漏；它是 S04-C002 的验证重点。

## 5. 异步任务、取消与异常

### 5.1 OLD UniTaskManager

OLD 的 UniTaskManager 由 _globalCts、模块名到 CTS 的字典、可选 debug tracked task 表组成，启动时初始化全局 CTS。GetModuleToken 通过 linked CTS 创建模块 token；CreateTimeoutTokenSource 再挂接 global token 和 CancelAfterSlim（S04-E010）。

CancelAllTasks 的流程是：遍历并 Cancel/Dispose 各模块 CTS，清空模块字典；Cancel/Dispose global CTS；创建新的 global CTS；若开启 debug tracking，则打印并清空 tracked tasks。它建立了“reload 使旧任务失效、新任务拿到新 global token”的生命周期边界，但没有从静态代码证明所有异步调用都使用这些 token（S04-E010）。

RunAndForget 的内部实现 finally 注销 tracked task；OperationCanceledException 被视为正常取消，其他异常只 LogError，不再向调用者抛出。这是一个明确的 Fire-and-Forget 异常策略，代价是调用方不能通过 await 得知失败（S04-E010）。

启动路径在 GameLaunch.InitLaunchLoader 中把 UniTaskManager.Inst 注册为 manager；面板扩展用 CreateLinkedTokenSource() 把面板 CTS 链接到全局 token。BasePanel.RemoveToStageHandler 在 OnClose、Do onClosed 后移除 timer、对象监听和组监听；BasePanel.OnClose 默认调用 CancelPanelTasks，CTS Cancel/Dispose/null（S04-E010、S04-E011）。

### 5.2 OLD 的帧队列和延迟任务

AsyncTodo 是另一条轻量异步机制：Do 只是把 CmdVo 加入列表；Update 取得一个 ParamVo，按顺序执行每个 callback，每个 callback 单独 try/catch，完成后归还 ParamVo 并清空命令。NetworkBase 的真实调用是构造 AsyncTodo、Connect/Close 时 Do、每帧 Update；它是“延后一帧/下一次 owner Update 执行”的队列，不是 UniTask 调度器（S04-E012）。

DelayTodo 则把固定/重置时间语义包在 FairyGUI.Timers 上：Do 注册一次性 timer，Cancel 用相同的 TimeHandler 移除，触发时创建 DelayTodoParam、调用 GEvent，再归还 ParamVo。ReqMapInfoContext 既有 new DelayTodo，也有 Do/Cancel 调用，证明该 wrapper 不是孤立定义（S04-E014）。

### 5.3 CURRENT 真实任务/池清理链：资源设置

CURRENT 没有在本轮找到 OLD UniTaskManager 同构的全局任务中心。最完整的实际任务链是 ResourceExtComponent.SetAssetByResources，但它是“有 finally 的局部链”，不是所有异常路径都已安全提交：

~~~mermaid
sequenceDiagram
    participant Caller as 调用方
    participant R as ResourceExtComponent
    participant Pool as MemoryPool
    participant AssetPool as AssetItem/ObjectPool
    participant Yoo as IResourceModule/YooAsset
    Caller->>R: SetAssetByResources(target, callerToken)
    R->>R: CreateLinkedTokenSource(callerToken)
    R->>Pool: Acquire LoadingState
    R->>R: ReplaceLoadingState(target, state)
    R->>R: await 等待同位置加载 + linkedToken
    alt 缓存命中
        R->>AssetPool: Spawn(location)
        R->>R: SetAsset; transfer=true
    else 缓存未命中
        R->>Yoo: LoadAssetAsync(location, token)
        R->>AssetPool: Register(AssetItemObject.Create(...), true)
        R->>R: SetAsset; transfer=true
    end
    R->>R: catch cancel/exception
    R->>R: finally detach/remove marker/unload未注册资源
    R->>Pool: Release setAssetObject if未转移
    R->>Pool: Release LoadingState -> Clear -> Dispose CTS
~~~

SetSpriteExtensions.cs:20、32 将实际的 Sprite 设置调用转入该方法，因此这不是只有定义没有调用方的孤立链。正常完成或异常进入 finally 时，代码会解除 target 到 loadingState 的映射、移除重复加载标记、对没有转入池的 loadedResource 调用 UnloadAsset、按 transfer 标志归还 setAssetObject、归还 LoadingState。可是 transfer 标志在 `SetAsset` 之前设置，而 `SetAsset` 会调用用户提供的回调；该回调抛异常时，finally 可能跳过 setAssetObject 归还，已注册资源也可能保持 spawn 状态。LoadingState.Clear 又会 Dispose linked CTS。ReplaceLoadingState 会取消同一 target 的旧状态，因此“新请求替换旧请求”有显式取消语义（S04-E023、S04-E028）。

边界仍然存在：

- ProcedureSetting.StartProcedure() 是 UniTaskVoid，GameEntry.Awake 直接 Forget()，没有传入取消 token 或显式异常回调；资源链的 finally 不能推导为所有 CURRENT 任务都有同等清理。
- OperationCanceledException 只有在 linked token 已取消时才按正常取消分支处理；其他异常统一 LogError，但都会进入 finally。
- ResourceExtComponent.OnDestroy 只清空 loading-state 映射并 Cancel 每个 CTS，没有等待任务、直接 Release LoadingState 或确认 finally 已完成；关闭时的最终释放依赖任务后续继续执行（S04-E028）。
- 当前没有静态证据证明普通业务任务都由统一 owner 注册、可枚举、可在模块关闭时取消（S04-E023）。

## 6. 计时器与退出路径

### 6.1 OLD FairyGUI.Timers

Timers 是项目树中的 FairyGUI 源码分支。它维护 active _items、待加入 _toAdd、待删除 _toRemove 和 _pool。Add 对同一 callback 会重置已有计时器；新 timer 先进 _toAdd，下一次 Update 才并入 active。Remove 对 active 只标记 deleted，对 pending 直接移除并回池；RemoveObject 通过 delegate.Target 批量清理一个 owner（S04-E013）。

计时器回池时只把 callback 置 null，没有清空 param；GetFromPool 重置 deleted/elapsed。因此 callback 引用会释放，但参数对象可能被池条目继续持有，这是静态可见的引用保留风险。默认 catchCallbackExceptions=false：回调异常直接离开 Update；只有打开开关时才捕获异常并把 timer 标记删除。回调执行之后，deleted 条目才会从 active 移除并回池，最后才合并 _toAdd（S04-E013）。

TimersEngine 的 MonoBehaviour.Update 调用 Timers.inst.Update，普通路径因此由 Unity 主线程驱动。它没有在本轮读到的源码中提供静态 singleton 销毁/引擎解绑闭环；这项生命周期属于待验证项（S04-E013）。

面板关闭路径是一个真实的 owner 清理样例：BasePanel.RemoveToStageHandler 调用 OnClose/onClosed，随后 Timers.inst.RemoveObject(this)、按对象移除模块事件、按组移除监听。它把 timer、事件和 UI stage 清理放在同一个 owner 退出点，但 OnClose 中重新打开其他面板的注释也承认存在重入风险（S04-E011）。

### 6.2 CURRENT TimerModule

普通 timer 的创建—取消—执行链如下：

1. UIModule.HideUI 先取消旧 HideTimerId，设置不可见/隐藏，再调用 GameModule.Timer.AddTimer 注册一次性 CloseUI 闭包。
2. UIWindow.CancelHideToCloseTimer 调用 ITimerModule.RemoveTimer 并把 id 置零。
3. TimerModule.RemoveTimer 只设置 isNeedRemove，UpdateTimer 下一次遍历会先把尚未开始的该 timer 放入待删索引；如果回调已经开始，当前回调不会被中途打断。
4. 未被取消时，UpdateTimer 扣减 curTime；到期调用 Handler；一次性 timer 加入待删索引，循环 timer 重置 curTime。
5. RootModule.Update 调用 ModuleSystem.Update，TimerModule 作为 IUpdateModule 在该主循环中运行；非编辑器构建中 RootModule.OnDestroy 才调用 ModuleSystem.Shutdown，ModuleSystem 再逆序 Shutdown，TimerModule 清空普通 timer。

对应源码和调用行见 S04-E021、S04-E022。它证明普通 TimerModule 不是后台线程计时器，也证明取消不是立即从 List 删除，而是“请求删除、下一次 Update 生效”。

普通 timer 的 Handler 没有 try/catch。若 Handler 抛异常，反向移除代码可能无法执行，当前帧后续模块 Update 也可能被 Unity 调用链打断；具体 Unity 异常传播方式需运行验证，但框架自身没有异常隔离/清理 finally（S04-E021）。

TimerModule 另有 AddSystemTimer，创建 System.Timers.Timer，设置 AutoReset/Enabled 并挂 Elapsed 回调；DestroySystemTimer 只 Stop，没有取消事件订阅、Dispose 或清空 _ticker。该类没有把 Elapsed 转发回 Unity 主线程的代码，因此“普通 TimerModule 主线程”不能扩展成“SystemTimer 回调主线程安全”。回调线程实际表现和使用方数量仍待验证（S04-E021）。

## 7. 通用状态机

### 7.1 OLD：只有业务 FSM 实现

本轮在 OLD framework 范围没有找到与 ModuleMgr 同级的统一通用 FSM；读到的 FiniteStateMachine<T> 在 PlayerOp 目录，维护 owner、current/previous/next、global state 和 state lock count，PlayerOperation 构造时创建并设置初始/全局状态，Dispose 时释放 FSM。它是业务模块内的通用化代码，不是 ZeroFramework runtime API（S04-E016）。

KSBattle 的 BaseFsm 是另一套按名字索引的战斗业务 FSM：Update 调当前状态并检查转换，SetInitialState 只允许一次，支持 Pause/Resume，Dispose 调当前 OnLeave 后清状态表。两套 FSM 的状态标识、回调协议和所有权不同，不能简单视为同一个旧设计（S04-E016）。

PlayerOp FSM 的 _stateLockCount 能拒绝 ChangeState，确实提供了一个“回调期间暂不切换”的局部约束；KSBattle BaseFsm 与之没有同等 lock。旧工程没有统一的跨域重入策略。

### 7.2 CURRENT：FsmModule—Procedure 的实际链路

CURRENT 的通用 FSM 由 FsmModule 按 TypeNamePair 持有，Update 先复制 map 到临时列表再更新，Shutdown 逐个 fsm.Shutdown 后清 map。CreateFsm 检查 owner/重复名后调用 Fsm<T>.Create 并登记；DestroyFsm 调 Shutdown 再从 map 删除，因此清理回调抛异常时 map 删除也可能未执行（S04-E024、S04-E030）。

已有的实际启动链：

~~~mermaid
flowchart TD
    A[GameEntry.Awake] -->|Get IFsmModule| B[FsmModule]
    A -->|StartProcedure().Forget| C[ProcedureSetting]
    C -->|Get IProcedureModule| D[ProcedureModule]
    C -->|反射创建 procedures| C
    C -->|Initialize| D
    D -->|CreateFsm(this, procedures)| B
    B -->|Fsm.Create / MemoryPool.Acquire| E[Fsm<IProcedureModule>]
    C -->|await UniTask.Yield| C
    C -->|StartProcedure| E
    F[RootModule.Update] --> G[ModuleSystem.Update]
    G --> B
    B -->|Fsm.Update| E
    H[RootModule.OnDestroy (!UNITY_EDITOR)] --> G2[ModuleSystem.Shutdown]
    G2 --> D
    D -->|DestroyFsm| B
    B -->|Fsm.Shutdown| E
    E -->|MemoryPool.Release| I[Fsm.Clear: OnLeave/OnDestroy/清空]
~~~

Fsm<T>.Clear 会调用当前状态 OnLeave(true)，遍历状态 OnDestroy，清理 owner/name/state/data；Fsm.Shutdown 通过 MemoryPool.Release 触发 Clear。普通状态转换先 OnLeave(false)、重置时间、替换 current、再 OnEnter，没有状态转换锁或异常回滚（S04-E024）。

重要的静态缺口：

- Fsm.Create 先 Acquire，再逐个状态校验并调用 OnInit；若中途发现 null/重复状态或 OnInit 抛异常，源码没有 finally 归还已 Acquire 的 Fsm，可能留下池对象和部分初始化状态。
- OnLeave/OnEnter 可调用 ChangeState，源码没有防重入标志或事务回滚；回调中的嵌套转换顺序需要专门运行测试。
- ModuleSystem 负责按优先级更新与逆序关闭，但没有锁或线程断言；RootModule 的 OnDestroy → Shutdown 只在 `!UNITY_EDITOR` 编译分支存在。“由 Unity RootModule 驱动”是主线程调用路径证据，不是对外 API 的线程安全承诺（S04-E022、S04-E024、S04-E029）。

## 8. 内存池与对象池

### 8.1 OLD 的局部池

OLD 的池化不是一个统一对象池协议：

- EventParam<T> 有按类型的静态池，EventDispatcher 在 callback 后尝试 Release。
- CallbackVo 的 Dispose 回收 ParamVo，但本轮没有找到从 EventDispatcher 移除到 CallbackVo.Dispose 的闭环。
- FairyGUI.Timers 自己维护 Anymous_T 池。
- RunTimer 只池化 Stopwatch；Start 获取/Restart，Stop 停止、Put 并将字段置 null。ModuleGroup.InitAsync 以 RunTimer 限制一次初始化占用的帧时间。

这些池大多只重置部分字段，且所有权分散在调用方；不能把“有 pool 字段”理解成统一的 double-release 或 stale-reference 防护（S04-E005、S04-E013、S04-E015）。

### 8.2 CURRENT MemoryPool

CURRENT MemoryPool 以 IMemory.Clear 作为回收契约。MemoryCollection.Acquire 从 Queue 出队或 new；Release 先调用 memory.Clear，再在锁内检查 strict 模式下队列是否已经包含该对象，然后入队并更新计数。MemoryPoolSetting 默认是开发期开启 strict check，并明确记录 strict check 会影响性能（S04-E025）。

因此已存在的保障是：

- 回池统一调用 Clear；
- 开发/编辑器可按配置检查重复归还；
- 池统计记录 using/acquire/release/add/remove 数量。

未存在或未证明的保障是：

- 未看到 token/generation 句柄，无法从类型层阻止“旧 owner 持有已回池对象”；
- strict check 默认不是所有构建都开启；
- dictionary、业务对象字段和回调本身没有统一线程保护；
- Release 的 Clear 抛异常时不会进入队列，调用方是否能恢复由上层决定。

### 8.3 CURRENT ObjectPool 的资源拥有关系

ObjectBase 统一定义 Name/Target/Locked/Priority/LastUseTime、Initialize、Clear、OnSpawn、OnUnspawn 和抽象 Release(bool)。Object<T> 是 ObjectPool 内部的池化 wrapper：Create 从 MemoryPool 获取 wrapper 并记录目标/spawn count；Spawn 增加计数并调用目标 OnSpawn；Unspawn 调目标 OnUnspawn、减计数并拒绝负数；Release 先执行目标 Release，再把 wrapper 归还 MemoryPool（S04-E026）。

ObjectPool 只有在对象未使用、未锁定且 CustomCanReleaseFlag 为 true 时才真正 ReleaseObject；普通 Unspawn 只减少使用计数，容量/自动释放策略再决定何时释放。Shutdown 遍历 object map，调用 Release(true)、归还 wrapper、清理字典（S04-E026）。

这里有两个适配边界：

- Object<T>.Release 和 ObjectPool.ReleaseObject 都没有 finally；目标 Release 抛异常时 wrapper 可能不回池，map 删除顺序也可能留下不一致状态。
- ResourceModule 创建名为 Asset Pool 的多重 spawn pool；AssetObject.Create 从 MemoryPool 获取并绑定 YooAsset HandleBase，非 shutdown Release 会 Dispose handle，Clear 再清空句柄。shutdown 分支不 Dispose handle，说明资源句柄的最终 shutdown 所有权仍需结合资源模块整体关闭顺序确认（S04-E027）。

## 9. 任务、计时器、池与模块关闭的所有权表

| 机制 | 创建者/持有者 | 正常退出 | 取消/异常路径 | 已知局限 |
|---|---|---|---|---|
| OLD EventDispatcher listener | dispatcher；模块/面板通过 callback、对象或 group 使用 | 精确 Remove、按对象/组 Remove、dispatcher Dispose | 分发中标记删除；回调异常隔离 | callbackVo.Dispose 调用闭环未证实；dispatcher 在回调中 Dispose 会提前 return |
| OLD NotifyDispatcher handler | ModuleMgr 的 notify map；BaseModule 以 notify 名注册 | Unregister 入队，下一次 Update 应用 | handler/完成回调异常继续；IRecyclable Return | 不同调用方可能观察到立即/排队两种时机 |
| OLD UniTask | 全局/模块 CTS；面板有 linked CTS | await 完成或 finally 取消 tracking | CancelAll/CancelModule；RunAndForget 吞取消、记录其他异常 | 全局 manager 的使用覆盖率未证实 |
| OLD Timers | static Timers + hidden GameObject；回调 target 可作 owner | one-shot 到期或 Remove | deleted 延迟移除；异常默认逃逸 | singleton 销毁、param 清理未证实 |
| CURRENT GameEventMgr | UIBase 按需 Acquire 的局部 manager | UIWindow.InternalDestroy → Clear → Release | GameEvent Remove；事件回调异常不清理 | UIBase Release 后字段未置空；GameEvent.Shutdown 仅有定义，未找到进入 ModuleSystem.Shutdown 的调用 |
| CURRENT TimerModule timer | 调用方持有 int id；TimerModule list | one-shot 到期后反向移除 | RemoveTimer 只标记，Shutdown 清空 | Handler 无 try/catch；无 owner 级自动绑定 |
| CURRENT resource async state | ResourceExtComponent 的 target→LoadingState 映射 | finally detach、资源转移或 unload、池归还 | 替换/销毁 Cancel，OCE 分支 | transfer 在 SetAsset 前设置；用户回调异常和 OnDestroy 只 Cancel 的最终清理仍有缺口 |
| CURRENT Fsm | FsmModule map；ProcedureModule 是实际 owner | DestroyFsm/ModuleSystem.Shutdown → Fsm.Clear | 创建中途异常无 finally；状态回调异常未隔离，可能阻断 map 删除/池归还 | ChangeState 无重入保护 |
| CURRENT ObjectPool wrapper | ObjectPool map + MemoryPool wrapper | Unspawn 后按 capacity/expire/Shutdown Release | in-use/locked 禁止释放 | Release 抛异常缺少 finally；shutdown 句柄策略需确认 |

表中的“局限”是源码可见的风险或证据缺口，不是运行时缺陷测试结果。

## 10. 线程与主线程约束

### 10.1 已确认的主线程调用路径

- OLD TimersEngine 是 MonoBehaviour.Update 调用 Timers.Update。
- CURRENT RootModule.Update 调用 ModuleSystem.Update，TimerModule、FsmModule 等 IUpdateModule 在该路径中执行；RootModule.OnDestroy 调 Shutdown 的代码仅在 `!UNITY_EDITOR` 分支编译。
- CURRENT GameEvent/EventDelegateData 没有把调用排队到 Update 的代码；直接调用 Send 就直接遍历委托，同事件嵌套会递归进入，分发中 Remove 不会跳过当前列表中的后续 handler。

这些路径支持“按常规 Unity 调用从主线程运行”的推断，但不是框架级线程安全保证（S04-E013、S04-E019、S04-E021、S04-E022）。

### 10.2 未确认/不应假设为主线程安全的路径

- CURRENT AddSystemTimer 使用 System.Timers.Timer.Elapsed，没有看到回主线程适配；实际 callback 线程需结合调用方和运行时验证。
- OLD RunTimer 和 CURRENT MemoryPool 的 lock 只保护池容器/Stopwatch 容器，不能保护事件回调、状态机状态或 Unity API。
- EventDispatcher、NotifyDispatcher、GameEvent 的 dictionary/list API 都没有统一线程断言或锁；从静态代码不能承诺并发 Add/Remove/Send 安全。

## 11. 对后续选择性复刻的直接含义

1. 最值得局部借鉴的是“订阅 owner/组”和“分发中修改延迟应用”，但异常策略必须先在 CURRENT 明确化；旧实现本身也有提前 return、池参数复用和简易 GEvent 缺陷（S04-E004、S04-E005、S04-E019）。
2. CURRENT 已有 TimerModule、FSM、MemoryPool/ObjectPool，不应因为 OLD 有类似工具就整体替换；优先验证 CURRENT 的异常安全和 owner 清理边界（S04-E021、S04-E024、S04-E025、S04-E026）。
3. OLD UniTaskManager 的全局取消适合解决 reload 边界，但实际调用覆盖率有限；CURRENT 资源链显示显式 token + finally 可以独立成立，是否引入全局层取决于跨模块 shutdown 需求，不是默认升级（S04-E010、S04-E023）。
4. NotifyDispatcher 的排队语义不能和 GameEvent 的立即语义混用；若后续需要跨模块顺序边界，应把“立即事件”和“排队通知”作为不同契约验证，而不是无条件复制双通道（S04-E006、S04-E007）。
5. GameEvent.Shutdown 具备清空能力但未找到进入 ModuleSystem.Shutdown 的调用；任何全局事件 owner/关闭顺序结论都必须先验证真实 teardown 入口（S04-E018、S04-E029）。
