# S04 证据索引

## 1. 使用说明

本索引只摘录必要短语，不复制整份源码。行号以本轮调查时工作区文件的文本行号为准；如果源码后续发生变动，应先重新核对 SHA-256 或 SVN 工作副本。证据性质使用“定义”“调用”“配置”“范围检查”标记。

相对路径均相对于各自工程根目录：

- OLD：D:\Work\SAUnity\ProjectOld
- CURRENT：E:\MyWork\MyFramework\TEngine\UnityProject

## 2. 基线与程序集

### S04-E001

- 所属工程：OLD / CURRENT
- 相对路径：ProjectSettings/ProjectVersion.txt
- 类/方法/配置字段：m_EditorVersion、m_EditorVersionWithRevision
- 核验行号：1-2（两工程均相同）
- 短摘录：m_EditorVersion: 2022.3.62f2；m_EditorVersionWithRevision: 2022.3.62f2 (7670c08855a9)
- 支持结论：两个工程调查基线均为 Unity 2022.3.62f2，同一 Unity revision。
- 性质：配置证据。

补充版本范围检查：OLD 根目录没有 Git 元数据，也不是根级 SVN working copy；Assets/Scripts/framework 与 Assets/Scripts/game 的 SVN info 均显示 revision 10480。根级 revision 不可取得，见 verification.md。

### S04-E002

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Framework.asmdef；Assets/Scripts/framework/Library/Common/FrameworkCommon.asmdef；Assets/Scripts/game/Game.asmdef
- 类/方法/配置字段：程序集 name、references
- 核验行号：Framework.asmdef:2-20；FrameworkCommon.asmdef:2-6；Game.asmdef:2-56
- 短摘录：dls.framework、dls.framework.common、dls.game
- 支持结论：ZeroFramework 与 game 业务有程序集边界；不能仅凭目录名把 game 内 FSM 当成 framework 通用设施。
- 性质：配置证据。

### S04-E003

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Event/EventDispatcher.cs
- 类/方法/配置字段：EventDispatcher.AddEventListener；RemoveEventListener；RemoveEventListenerByObject；RemoveEventListenerByGroup
- 核验行号：75-114；132-205；213-299
- 短摘录：按 callback hash 查重；group 可覆盖；按 object/group 收集后移除。
- 支持结论：OLD EventDispatcher 以字符串事件和 CallbackVo 保存订阅，支持精确、按对象、按 group 的解绑，并有优先级/自动移除参数。
- 性质：定义证据。

### S04-E004

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Event/EventDispatcher.cs
- 类/方法/配置字段：EventDispatcher.DispatchEvent
- 核验行号：350-458
- 短摘录：IsDispatch 防同类型嵌套；i < eventList.Count；TobeRemove；每个 callback try/catch；Disposed 时提前 return。
- 支持结论：OLD 分发中新增项可追加到当前列表尾部，删除延迟标记，异常按回调隔离；dispatcher 在回调中 Dispose 存在提前返回路径。
- 性质：定义证据。

### S04-E005

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Event/CallbackVo.cs；Assets/Scripts/framework/Library/ZeroFramework/Event/EventDispatcher.cs
- 类/方法/配置字段：CallbackVo.PreventDefault、MarkRemove、OnDispose；IEventParam、EventParam<T>、EventParamMgr
- 核验行号：CallbackVo.cs:134-178；EventDispatcher.cs:403-408、608-706
- 短摘录：PreventDefault 仅在 Cancelable 时生效；OnDispose 调 ParamVo.Put；IEventParam 要求 Reset；Dispatch 后 EventParamMgr.Release。
- 支持结论：OLD 具备 callback 状态和参数池的清理接口；但本轮未找到 CallbackVo.Dispose 从解绑路径被调用的闭环，且未见 EventParam pool 统一调用 Reset。
- 性质：定义/调用证据。

### S04-E006

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Event/NotifyDispatcher.cs
- 类/方法/配置字段：Register、Unregister、SendNotify、Update
- 核验行号：18-71；73-162
- 短摘录：m_listOpA/m_listOpB 双缓冲；Update 先切换当前列表；handler 与完成 callback 分别 try/catch；IRecyclable.Return。
- 支持结论：OLD 跨模块通知的注册、注销、发送先排队，Update 批量应用；处理期间新操作进入另一列表，handler 异常不会阻断同批后续 handler。
- 性质：定义证据。

### S04-E007

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Module/ModuleMgr.cs；Assets/Scripts/framework/Library/ZeroFramework/Module/BaseModule.cs
- 类/方法/配置字段：ModuleMgr.Update/LateUpdate/AddNotifyListener/SendNotify；BaseModule.SendNotify/RegisterNotify/UnregisterAllNotify
- 核验行号：ModuleMgr.cs:270-312、332-359、381-405；BaseModule.cs:431-502
- 短摘录：LateUpdate 先 m_dispatcher.Update；BaseModule 先本地 DispatchEvent 再 m_center.SendNotify；ModuleMgr.SendNotify 另行立即 dispatch 外部监听。
- 支持结论：OLD 存在本地立即事件、模块中心排队通知、外部立即通知三种相邻时序；不能把 NotifyDispatcher 简化成普通同步事件。
- 性质：定义/调用证据。

### S04-E008

- 所属工程：OLD
- 相对路径：Assets/Scripts/game/Module/Common/CommonModule.cs；Assets/Scripts/game/Managers/TimelineMgr.cs
- 类/方法/配置字段：CommonModule.OnInit/OnDispose；TimelineMgr.RegisterNotify/UnRegisterNotify
- 核验行号：CommonModule.cs:240-245、624-634；TimelineMgr.cs:113-125、186-197
- 短摘录：CommonModule 调 RegisterNotify；TimelineMgr 注册六类通知；OnDispose 调 UnRegisterNotify 并逐项 RemoveNotifyListener。
- 支持结论：TimelineMgr 有一个可复查的真实订阅到解绑闭环，实际生命周期 owner 是 CommonModule。
- 性质：调用证据。

### S04-E009

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Event/GEvent.cs
- 类/方法/配置字段：GEvent<T>.Do/Add/Remove；GEvent.Do/Add/Remove
- 核验行号：1-117；166-235
- 短摘录：单个 m_action 多播；Do 只设置 m_doing 前后标志；无参 Remove 将 HasListener 设为 m_action == null。
- 支持结论：GEvent 是轻量委托包装，缺少 EventDispatcher 的逐 handler 异常隔离和 owner/group 生命周期；无参 Remove 与 Do 存在静态缺陷/风险。
- 性质：定义证据。

## 3. OLD 异步、计时器、局部池和 FSM

### S04-E010

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Manager/UniTaskManager.cs；Assets/Scripts/game/Module/Launch/GameLaunch.cs
- 类/方法/配置字段：UniTaskManager.Inst/Initialize/GetModuleToken/CreateTimeoutTokenSource/CancelAllTasks/RunAndForget；GameLaunch.InitLaunchLoader
- 核验行号：UniTaskManager.cs:20-75、106-196、288-418；GameLaunch.cs:325-359（UniTaskManager 在 355）
- 短摘录：_globalCts、_moduleCtsDict；CancelAllTasks 后创建新的 global CTS；RunAndForget 捕获取消并记录非取消异常。
- 支持结论：OLD 有全局/模块 token、reload 取消和 Fire-and-Forget 异常策略；启动器把 manager 注册进管理系统。
- 性质：定义/调用证据。

### S04-E011

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel_CancellationToken.cs；Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel.cs
- 类/方法/配置字段：PanelCancellationToken、CancelPanelTasks、RemoveToStageHandler、OnClose
- 核验行号：BasePanel_CancellationToken.cs:16-54；BasePanel.cs:280-314、368-371
- 短摘录：面板 CTS 链接全局 token；CancelPanelTasks Cancel/Dispose/null；关闭后 RemoveObject、RemoveEventListenerByObject、RemoveEventListenerByGroup。
- 支持结论：OLD 面板有任务、timer、事件的共同退出点；事件/timer 清理在 OnClose/onClosed 之后继续执行。
- 性质：定义/调用证据。

### S04-E012

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Utils/AsyncTodo.cs；Assets/Scripts/framework/Library/ZeroFramework/Network/NetworkBase.cs
- 类/方法/配置字段：AsyncTodo.Do/Update；NetworkBase 构造/Connect/Close/Update
- 核验行号：AsyncTodo.cs:14-57；NetworkBase.cs:86、150、188、300、368（搜索定位）
- 短摘录：Do 加 CmdVo；Update 逐项 try/catch，最后 ParamVo.Put 并清空 m_cmds。
- 支持结论：AsyncTodo 是 owner Update 驱动的帧队列，回调异常逐项隔离，不是 UniTask 全局调度器。
- 性质：定义/调用证据。

### S04-E013

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/FairyGUI/Scripts/Utils/Timers.cs
- 类/方法/配置字段：Timers._items/_toAdd/_toRemove/_pool；Add/Remove/RemoveObject/GetFromPool/ReturnToPool/Update；TimersEngine.Update
- 核验行号：13-25；94-123；167-236；238-316；339-344
- 短摘录：catchCallbackExceptions=false；pending add/remove；RemoveObject 比较 callback.Target；TimersEngine.Update 调 Timers.inst.Update。
- 支持结论：FairyGUI.Timers 是 Unity Update 驱动的第三方源码分支，支持 owner-like delegate.Target 清理和对象池；默认回调异常逃逸，回池不清 param。
- 性质：定义证据；归属为第三方源码分支。

### S04-E014

- 所属工程：OLD
- 相对路径：Assets/Scripts/game/Utils/DelayTodo.cs；Assets/Scripts/game/Data/Cache/WorldMap/ReqMapInfoContext.cs
- 类/方法/配置字段：DelayTodo.Do/Cancel/TimeHandler；LowViewReq/TopViewReq/LowViewReqCancel
- 核验行号：DelayTodo.cs:25-94；ReqMapInfoContext.cs:28-30、244-259
- 短摘录：Do 调 Timers.inst.Add(..., 1, TimeHandler)；Cancel 调 Timers.inst.Remove(TimeHandler)；调用方有 Do 与 Cancel。
- 支持结论：DelayTodo 的固定时间/重置时间语义有真实业务使用，且取消依赖相同 callback identity。
- 性质：定义/调用证据。

### S04-E015

- 所属工程：OLD
- 相对路径：Assets/Scripts/framework/Library/Common/Utils/RunTimer.cs；Assets/Scripts/framework/Library/ZeroFramework/Module/ModuleGroup.cs
- 类/方法/配置字段：RunTimer.Start/Stop/Get/Put；ModuleGroup.InitAsync
- 核验行号：RunTimer.cs:12-95；ModuleGroup.cs:34-52
- 短摘录：Stack<Stopwatch>；Stop 后 Put 并置空；InitAsync 在每次 InitOne 后检查 timer.Time。
- 支持结论：OLD 有一个实际用于限制模块初始化帧预算的 Stopwatch 池；它要求调用方配对 Start/Stop，异常中途退出没有统一 finally。
- 性质：定义/调用证据。

### S04-E016

- 所属工程：OLD
- 相对路径：Assets/Scripts/game/Module/PlayerOp/OpStates/FiniteStateMachine.cs；Assets/Scripts/game/Module/PlayerOp/OpStates/PlayerOperation.cs；Assets/Scripts/game/Module/KSBattle/Fsm/Core/BaseFsm.cs
- 类/方法/配置字段：FiniteStateMachine<T>.ChangeState/Update/Dispose；PlayerOperation 构造/Dispose；BaseFsm.Update/SetInitialState/Dispose/Pause/Resume
- 核验行号：FiniteStateMachine.cs:8-56、93-180；PlayerOperation.cs:65-71、278-289；BaseFsm.cs:10-68、140-185、224-273
- 短摘录：PlayerOp 有 _stateLockCount；KSBattle 按字符串状态、Pause/Resume；两者都在 game 目录自行 Dispose。
- 支持结论：OLD framework 范围未发现统一通用 FSM；存在两套业务 FSM，语义和所有权不一致。
- 性质：定义/调用/范围检查证据。

## 4. CURRENT 事件与生成路径

### S04-E017

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/TEngine.Runtime.asmdef；Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef；Assets/TEngine/Runtime/Core/GameEvent/SourceGenerator.dll.meta；Assets/TEngine/Runtime/Core/GameEvent/GameEventAnalyzer.dll.meta；Assets/GameScripts/HotFix/GameLogic/GameApp.cs；Assets/GameScripts/HotFix/GameLogic/IEvent/ILoginUI.cs
- 类/方法/配置字段：程序集 name/references；RoslynAnalyzer label/platformData；GameApp.Entrance；EventInterfaceAttribute
- 核验行号：TEngine.Runtime.asmdef:2-16；GameLogic.asmdef:2-30；两个 meta:1-28；GameApp.cs:27、38；ILoginUI.cs:5
- 短摘录：GameEventHelper.Init()；ILoginUI 带 EventInterface；GameEvent.Get<ILoginUI>() 为注释；生成器源文件未在 Assets 中找到。
- 支持结论：CURRENT 的生成式事件接口有配置和入口，但生成源码/运行后 wrapper 可达性待 Unity 编译验证。
- 性质：配置/调用/范围检查证据。

### S04-E018

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Core/GameEvent/EventDispatcher.cs；Assets/TEngine/Runtime/Core/GameEvent/GameEvent.cs；Assets/TEngine/Runtime/Core/GameEvent/EventMgr.cs
- 类/方法/配置字段：EventDispatcher.Add/Remove；GameEvent int/string Add/Remove；EventMgr.RegWrapInterface/Init
- 核验行号：EventDispatcher.cs:14-54；GameEvent.cs:28-42、125-150、212-239、292-317、596-598；EventMgr.cs:47-86
- 短摘录：_eventTable Dictionary<int, EventDelegateData>；string eventType 经 RuntimeId；Init 清 wrapper map 和 dispatcher。
- 支持结论：CURRENT GameEvent 是 int 核心、string 适配和 Type wrapper 三层；Shutdown/Init 可清空全局事件表。
- 性质：定义证据。

### S04-E019

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs
- 类/方法/配置字段：AddHandler/RmvHandler/CheckModify/Callback
- 核验行号：12-16、32-96、101-134（其他参数数量重载同样模式）
- 短摘录：执行期间写 _addList/_deleteList；Callback 直接 action(arg)，正常返回后才 CheckModify；循环只遍历 _listExist，不检查 _deleteList；GameEvent.Send 没有同事件重入保护。
- 支持结论：CURRENT 增删延迟到当前分发之后；新增不参加当前循环；分发中移除的后续 handler 仍可能执行本次循环；同事件嵌套会递归进入 Callback；回调异常会跳过 CheckModify 并留下 _isExecute 状态，且 Add 查重不看 _addList。
- 性质：定义证据。

### S04-E020

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Core/GameEvent/GameEventMgr.cs；Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs；Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs
- 类/方法/配置字段：GameEventMgr.Clear/AddEvent；UIBase.EventMgr/RemoveAllUIEvent；UIWindow.InternalCreate/InternalDestroy
- 核验行号：GameEventMgr.cs:9-70；UIBase.cs:284-330；UIWindow.cs:338-348、427-457
- 短摘录：Clear 遍历平行列表调用 GameEvent.RemoveEventListener；RemoveAllUIEvent 只 MemoryPool.Release(_eventMgr)，没有置 null。
- 支持结论：CURRENT UI 有局部事件 manager 的真实创建—清除—回池链，但 UIBase 字段保留已回池引用的风险需要运行验证。
- 性质：定义/调用证据。

## 5. CURRENT 计时器、模块更新和异步

### S04-E021

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs；Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs；Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs
- 类/方法/配置字段：AddTimer/RemoveTimer/UpdateTimer/AddSystemTimer/Shutdown；UIModule.HideUI；UIWindow.CancelHideToCloseTimer
- 核验行号：TimerModule.cs:40-57、236-255、341-435、437-471；UIModule.cs:393-414；UIWindow.cs:506、514-521
- 短摘录：RemoveTimer 设置 isNeedRemove；UpdateTimer 到期调用 Handler 后再移除一次性 timer；AddSystemTimer 挂 Elapsed；DestroySystemTimer 只 Stop。
- 支持结论：CURRENT 普通 timer 是 ModuleSystem Update 驱动、标记后删除；handler 没有 try/catch；System.Timer 没有本类内的 Dispose、解绑或回主线程代码。
- 性质：定义/调用证据。

### S04-E022

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/RootModule.cs；Assets/TEngine/Runtime/Core/ModuleSystem.cs
- 类/方法/配置字段：RootModule.Update/OnDestroy；ModuleSystem.Update/Shutdown/RegisterUpdate
- 核验行号：RootModule.cs:140-166；ModuleSystem.cs:29-60、143-206
- 短摘录：RootModule.Update 调 ModuleSystem.Update；`#if !UNITY_EDITOR` 下 OnDestroy 才调 Shutdown；Shutdown 逆序关闭模块并 MemoryPool.ClearAll；按 Priority 构造更新表。
- 支持结论：CURRENT 的普通 Timer/Fsm 等模块由 Unity RootModule 的主循环更新；非编辑器构建的 RootModule 销毁路径会进入 ModuleSystem.Shutdown，编辑器不通过该条件分支进入；这是调用路径，不是并发安全承诺。
- 性质：定义/调用证据。

### S04-E023

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs；Assets/TEngine/Runtime/Module/ResourceModule/Extension/Implement/SetSpriteExtensions.cs；Assets/GameScripts/GameEntry.cs；Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureSetting.cs
- 类/方法/配置字段：LoadingState.Cancel/Clear；SetAssetByResources；ReplaceLoadingState/OnDestroy；SetSpriteExtensions；GameEntry.Awake；StartProcedure
- 核验行号：ResourceExtComponent.Resource.cs:14-38、40-69、76-153、156-194；SetSpriteExtensions.cs:20、32；GameEntry.cs:6-14；ProcedureSetting.cs:55-102
- 短摘录：linked CTS；MemoryPool.Acquire LoadingState；替换旧 state 时 Cancel；finally detach/remove marker/unload/release；StartProcedure().Forget()；组件 OnDestroy 只 Cancel。
- 支持结论：CURRENT 资源异步链有实际的 token、替换取消和 finally 清理，但 transfer 标志在 SetAsset 前设置，用户回调异常时可能跳过池对象归还；OnDestroy 只取消状态、不等待任务；Procedure 启动是无 token 的 UniTaskVoid Fire-and-Forget。
- 性质：定义/调用证据。

## 6. CURRENT FSM 与池

### S04-E024

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/FsmModule/FsmModule.cs；Assets/TEngine/Runtime/Module/FsmModule/Fsm.cs；Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureModule.cs；Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureSetting.cs
- 类/方法/配置字段：FsmModule.Update/Shutdown/CreateFsm/InternalDestroyFsm；Fsm<T>.Create/Clear/Update/Shutdown/ChangeState；ProcedureModule.Initialize/Shutdown；ProcedureSetting.StartProcedure
- 核验行号：FsmModule.cs:9-79、226-249、384-394；Fsm.cs:79-113、161-180、454-503；ProcedureModule.cs:60-108；ProcedureSetting.cs:55-102
- 短摘录：Fsm.Create 先 MemoryPool.Acquire；Clear OnLeave(true)/OnDestroy；Shutdown MemoryPool.Release；状态转换无 finally/lock。
- 支持结论：CURRENT 有 framework 级 FSM，并有 GameEntry → ProcedureSetting → ProcedureModule → FsmModule → MemoryPool → Update/Shutdown 的真实链路；创建中途异常和转换重入没有统一保护。
- 性质：定义/调用证据。

### S04-E025

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Core/MemoryPool/IMemory.cs；Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.cs；Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.MemoryCollection.cs；Assets/TEngine/Runtime/Core/MemoryPool/MemoryPoolSetting.cs
- 类/方法/配置字段：IMemory.Clear；MemoryPool.Acquire/Release/ClearAll；MemoryCollection.Acquire/Release；MemoryPoolSetting.m_EnableStrictCheck
- 核验行号：IMemory.cs:1-12；MemoryPool.cs:11-101；MemoryPool.MemoryCollection.cs:11-98；MemoryPoolSetting.cs:35-78
- 短摘录：Release 先 memory.Clear；strict 模式下检查 Queue.Contains；默认 OnlyEnableWhenDevelopment；开启 strict 会记录性能影响。
- 支持结论：CURRENT 内存池有统一 Clear 和可配置 double-release 检查，但没有 generation/owner handle，也不是所有构建默认严格检查。
- 性质：定义/配置证据。

### S04-E026

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectBase.cs；Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.Object.cs；Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.ObjectPool.cs
- 类/方法/配置字段：ObjectBase.Initialize/Clear/OnSpawn/OnUnspawn/Release；Object<T>.Create/Spawn/Unspawn/Release；ObjectPool.Register/Unspawn/ReleaseObject/Shutdown
- 核验行号：ObjectBase.cs:8-162；ObjectPoolModule.Object.cs:116-186；ObjectPoolModule.ObjectPool.cs:148-163、255-269、379-402、511-523
- 短摘录：ReleaseObject 拒绝 IsInUse/Locked；Unspawn 检查负 spawn count；Shutdown Release(true) 后回 MemoryPool。
- 支持结论：CURRENT 对象池以 spawn count、locked、capacity/expire 约束释放，wrapper 和目标对象分两层清理；Release 异常没有 finally 保护。
- 性质：定义证据。

### S04-E027

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs；Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.AssetObject.cs
- 类/方法/配置字段：SetObjectPoolModule/UnloadAsset；AssetObject.Create/Clear/Release
- 核验行号：ResourceModule.Pool.cs:47-66；ResourceModule.AssetObject.cs:11-61
- 短摘录：CreateMultiSpawnObjectPool<AssetObject>("Asset Pool")；非 shutdown Release Dispose HandleBase；Clear 清空句柄。
- 支持结论：资源模块实际使用 ObjectPool 管理 AssetObject；AssetObject 是 YooAsset handle 的适配层，shutdown 句柄最终归属需要结合整体关闭验证。
- 性质：调用/定义证据。

### S04-E028

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs；Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.cs；Assets/TEngine/Runtime/Module/ResourceModule/Extension/Implement/SetSpriteObject.cs
- 类/方法/配置字段：SetAssetByResources；ResourceExtComponent.SetAsset；SetSpriteObject.SetAsset；ResourceExtComponent.OnDestroy
- 核验行号：ResourceExtComponent.Resource.cs:119-153、187-196；ResourceExtComponent.cs:153-157；SetSpriteObject.cs:51-70
- 短摘录：SetAsset 前设置 setAssetObjectTransferred=true；SetAsset 先加入 linked list 再调用 ISetAssetObject.SetAsset；SetSpriteObject 会执行用户 callback；OnDestroy 只 Cancel loading states。
- 支持结论：CURRENT 资源链的 finally 依赖 transfer 标志判断归还；用户 SetAsset/callback 抛异常时可能跳过 setAssetObject 归还并保留已注册资源的 spawn 状态；组件销毁路径没有等待任务或直接释放 LoadingState。
- 性质：定义/调用/失败路径证据。

### S04-E029

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/RootModule.cs；Assets/TEngine/Runtime/Core/ModuleSystem.cs；Assets/TEngine/Runtime/Core/GameEvent/GameEvent.cs
- 类/方法/配置字段：RootModule.OnDestroy；ModuleSystem.Shutdown；GameEvent.Shutdown；范围搜索 GameEvent.Shutdown 调用
- 核验行号：RootModule.cs:162-166；ModuleSystem.cs:47-60；GameEvent.cs:596-599
- 短摘录：RootModule 的 ModuleSystem.Shutdown 位于 `#if !UNITY_EDITOR`；ModuleSystem.Shutdown 未调用 GameEvent.Shutdown；全工程源码搜索只命中 GameEvent.Shutdown 定义。
- 支持结论：CURRENT RootModule 销毁关闭链只在非编辑器编译分支可达；GameEvent.Shutdown 是可手动调用的全局清理能力，本轮未证明它属于 ModuleSystem 的实际关闭闭环。
- 性质：配置/定义/范围检查证据。

### S04-E030

- 所属工程：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/FsmModule/FsmModule.cs；Assets/TEngine/Runtime/Module/FsmModule/Fsm.cs
- 类/方法/配置字段：FsmModule.InternalDestroyFsm；Fsm<T>.Shutdown/Clear
- 核验行号：FsmModule.cs:384-390；Fsm.cs:161-180、468-471
- 短摘录：InternalDestroyFsm 先 fsm.Shutdown 再 Remove；Clear 先调用 OnLeave/OnDestroy；Shutdown 通过 MemoryPool.Release 触发 Clear。
- 支持结论：CURRENT FSM 销毁时，状态清理回调若抛异常，后续 map 删除或 MemoryPool 归还可能未执行；源码没有 finally 或异常隔离保护。
- 性质：定义/失败路径证据。

## 7. 证据文件 SHA-256（OLD 关键源码）

这些 hash 用于在 OLD 根级没有统一版本信息时复查本轮关键源码快照；不对源码做任何更新。

| 相对路径 | SHA-256 |
|---|---|
| Assets/Scripts/framework/Library/ZeroFramework/Event/EventDispatcher.cs | 8F7D5D3938BCAD0F9ADBA74CF64075DFF5E4F36A9F71F3F3A2E9574FD5834AB4 |
| Assets/Scripts/framework/Library/ZeroFramework/Event/NotifyDispatcher.cs | 7714E1C948577F1ACF42C09EE5126952E37BDE8D1B83FEF1074AF43975483845 |
| Assets/Scripts/framework/Library/ZeroFramework/Manager/UniTaskManager.cs | 5BF99AB3B48EB9952413FF9ED583B6E049916EE4637E3526DCC46DDF8BEE3EB8 |
| Assets/Scripts/framework/FairyGUI/Scripts/Utils/Timers.cs | F7C21A275D5913C9A30D5C4E1A9B86EEFEC5F97F7E10FD67AA012FB83577FD7C |
| Assets/Scripts/framework/Library/ZeroFramework/Module/ModuleMgr.cs | C7E944D94D40D70DEAA4E55443C2158A852264E9F500D5D82EF1ACE71F7CCFCB |
| Assets/Scripts/game/Managers/TimelineMgr.cs | C85C4315AF5D13F761557BC57C97485596D7349747457B94737B71A9CF248C1F |
| Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel_CancellationToken.cs | F51C4006D387B2C488765D358FDB5107503A253F1D504A7282FD455B02B5BAEE |
