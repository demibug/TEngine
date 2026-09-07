# S04 开放问题与待验证项

## 1. 说明

本文件把“代码没有证明”的内容公开列出。每项包含查过的位置、当前推断、下一步验证方法和是否阻塞后续决策。阻塞只表示会影响某个候选的批准，不表示本轮文档无法交付。

## 2. 生成式事件路径

### OQ-S04-001：GameEventHelper 的生成结果是否实际进入 GameLogic 程序集？

- 当前状态：待验证，局部阻塞 S04-C001/S04-C002 的最终 API 设计。
- 已确认：GameApp.cs:27 调用 GameEventHelper.Init；ILoginUI.cs:5 有 EventInterface；SourceGenerator.dll.meta 和 GameEventAnalyzer.dll.meta 带 RoslynAnalyzer；Assets 中未找到 GameEventHelper.cs 或生成 wrapper 源文件（S04-E017）。
- 推断：Unity 编译阶段可能把 analyzer/source generator 结果注入编译单元，但静态资产树不能证明生成成功、程序集顺序或热更可达。
- 下一步：在 CURRENT Unity 2022.3.62f2 工程中只做一次受控编辑器编译，检查编译诊断和生成源/程序集反射结果；不得把生成产物复制入本调查目录作为源码依据。
- 是否阻塞：阻塞生成式 wrapper 的实施决定；不阻塞对低层 EventDelegateData 的静态分析。

### OQ-S04-002：CURRENT 业务层是否已有完整的 GameEvent Add/Remove 实际闭环？

- 当前状态：未证实。
- 已查位置：Assets 下的 GameEvent、GameLogic、Procedure、HotFix 源码；搜索到 GameEventMgr 框架调用、GameApp 的入口/注释示例和 ProcedurePreload 的 Send，但没有足够业务层精确 Add/Remove 对。
- 推断：UIBase 的 RegisterEvent 是潜在调用入口，但具体窗口是否覆盖并执行没有在本轮逐个运行确认。
- 下一步：以一个实际 UIWindow 类型为样本，从 UIModule 打开、InternalCreate、RegisterEvent 到 InternalDestroy、RemoveAllUIEvent 做运行日志或受控断点；检查生成接口调用是否进入同一全局 GameEvent 表。
- 是否阻塞：阻塞“替换/扩展现有订阅 API”的范围判断；不阻塞记录当前框架定义。

## 3. 事件异常、重入与参数池

### OQ-S04-003：EventDelegateData 异常后的中间状态是否会被后续使用？

- 当前状态：静态风险已确认，运行影响待验证。
- 已查位置：EventDelegateData.cs:32-96、101-134 及所有参数数量的 Callback 重载（S04-E019）。当前 Callback 只遍历 `_listExist`，不检查 `_deleteList`；分发中移除后续 handler 仍可能执行，同一事件嵌套 Send 会递归进入同一个 Callback。
- 推断：handler 抛异常后 `_isExecute` 保持 true，后续 Add/Remove 继续进 pending list；若事件对象随后被 Send，则可能持续不应用修改。嵌套 Send 还会让外层和内层共享同一组待处理列表，最终顺序依赖回调时序。
- 下一步：添加临时测试 harness 或编辑器单元测试，覆盖 handler 抛异常、下一次 Add/Remove/Send、Clear/Shutdown、嵌套 Send；测试完成后删除测试改动，不纳入本任务文档目录以外。
- 是否阻塞：阻塞 S04-C001 的“隔离还是传播”选择；不阻塞报告缺少 finally 的代码事实。

### OQ-S04-004：OLD EventParam 的回收时机是否造成实际 payload 复用？

- 当前状态：待验证。
- 已确认：EventDispatcher 每个 callback 后调用 EventParamMgr.Release；EventParam<T> 要求 Reset，但本轮未见 pool Release 调 Reset（S04-E004、S04-E005）。
- 推断：同一 pooled payload 被多个 listener 使用时，若 Release 后立即再分配，后续 listener 可能观察到变化；实际是否有重入分配需要调用样本。
- 下一步：查找实际构造 EventParam<T> 的业务调用，记录一个 dispatch 内 listener 数量和是否发生嵌套 Acquire；再以 Debug 标记跟踪对象 identity。
- 是否阻塞：阻塞直接复刻 OLD 参数池；不阻塞保留“未证明 Reset/所有权”的风险结论。

### OQ-S04-005：OLD CallbackVo.Dispose 是否由真实解绑路径调用？

- 当前状态：缺失证据。
- 已查位置：CallbackVo.cs:165-178、EventDispatcher 的精确/按对象/按组移除、EventDispatcher.OnDispose；在本轮 Event/ZeroFramework 范围未找到 CallbackVo.Dispose 调用（S04-E003、S04-E005）。
- 推断：CallbackVo 的清理 hook 可能依赖其他 owner 或最终 GC，不应写成“解绑自动归还 ParamVo”。
- 下一步：对 OLD 全部源码只搜索 CallbackVo.Dispose/Dispose() 的调用接收者，排除无关第三方；如果仍无结果，保留为设计缺口。
- 是否阻塞：阻塞把该清理链当作可复刻保障；不阻塞现有事件生命周期分析。

## 4. 计时器和线程

### OQ-S04-006：FairyGUI.Timers 的第三方来源与 singleton 退出方式是什么？

- 当前状态：待确认归属细节。
- 已确认：源码在 Assets/Scripts/framework/FairyGUI/Scripts/Utils，namespace FairyGUI，包含项目扩展；本轮读到 TimersEngine.Update，但未读到清理 singleton 的调用闭环（S04-E013）。
- 推断：工程可能维护了第三方源码 fork；无法据此宣称这是 ZeroFramework 自研计时器。
- 下一步：核对 OLD LocalPackages、FairyGUI 版本文件、许可证/包清单和项目初始化销毁场景；只读检查，不更新依赖。
- 是否阻塞：阻塞第三方归属和许可审查；不阻塞记录其当前源码行为。

### OQ-S04-007：CURRENT AddSystemTimer 是否有调用方，回调是否跨线程触碰 Unity 对象？

- 当前状态：未找到实际调用闭环，线程行为待运行确认。
- 已确认：TimerModule.cs:439-449 创建 System.Timers.Timer 并挂 Elapsed，452-461 只 Stop；本类没有回 Unity 主线程的代码（S04-E021）。
- 推断：Elapsed 可能在运行时线程池线程执行；如果 callback 操作 Unity API，将有线程约束风险，但本轮不以静态代码断言实际线程。
- 下一步：搜索全部 CURRENT 源码的 AddSystemTimer 调用；若存在，记录 callback 所在线程并验证 Shutdown 后是否仍触发；若无调用，评估收窄该 API。
- 是否阻塞：阻塞 S04-C003 对 SystemTimer 的处理；不阻塞普通 TimerModule 结论。

### OQ-S04-008：CURRENT TimerModule 回调异常后 timer list 和后续模块更新如何表现？

- 当前状态：待运行验证。
- 已确认：TimerModule.cs:341-387 和 unscaled 重载直接调用 Handler，无 try/catch；删除一次性 timer 的代码在回调之后（S04-E021）。
- 推断：回调抛异常可能跳过反向移除，导致一次性 timer 留在列表或打断当前 ModuleSystem.Update。
- 下一步：用一次性与循环 timer 分别注入抛异常 handler，记录列表计数、下一帧是否再次触发、后续模块是否更新。
- 是否阻塞：阻塞 S04-C003 的异常策略；不阻塞“当前代码没有异常隔离”事实。

## 5. 异步任务与模块关闭

### OQ-S04-009：CURRENT 是否需要全局 reload 任务中心？

- 当前状态：需求未证实。
- 已确认：OLD UniTaskManager 有 global/module CTS 和 CancelAllTasks；CURRENT 资源链使用显式 token/linked CTS/finally，ProcedureSetting.StartProcedure().Forget 无 token；本轮未找到 CURRENT 同构 manager（S04-E010、S04-E023）。
- 推断：若热更重载只需按 owner 取消，显式 scope 可能足够；若要全局 reload，缺少中心会留下跨模块任务。
- 下一步：和启动、热更、资源、UI 调查合并，列出所有跨场景/重载 task owner，再决定局部 scope 或全局 CTS。
- 是否阻塞：阻塞 S04-C004 的引入范围；不阻塞资源链的局部结论。

### OQ-S04-010：UniTaskVoid/Forget 任务失败是否有统一观察者？

- 当前状态：未证实。
- 已查位置：GameEntry.cs:6-14、ProcedureSetting.cs:55-102、ResourceExtComponent.Resource.cs:76-153；资源链 catch/finally 明确，Procedure 启动没有显式错误回调（S04-E023）。
- 推断：Procedure 的异常可能由 UniTaskVoid/Forget 机制按库规则处理，而非由 ModuleSystem/ProcedureModule 接收；静态代码不能声称已安全上报。
- 下一步：在不改生产语义的临时 harness 中让 procedure 初始化或 Start 抛异常，确认日志、任务完成状态和 ModuleSystem 关闭行为。
- 是否阻塞：阻塞统一 Fire-and-Forget 规范；不阻塞报告现有调用路径。

## 6. FSM、对象池和资源句柄

### OQ-S04-011：CURRENT Fsm.Create 中途失败是否真的泄漏池对象？

- 当前状态：静态风险已确认，数量待测。
- 已确认：Fsm.Create 在 MemoryPool.Acquire 后逐个校验并调用 OnInit，异常分支没有 finally；Fsm.Shutdown 才通过 MemoryPool.Release（S04-E024）。
- 推断：输入 null/重复状态或 OnInit 抛异常时，已 Acquire 的 FSM 不回池，且已初始化状态可能未 OnDestroy。
- 下一步：用专门测试状态分别触发 null、重复 type、OnInit 异常，读取 MemoryPoolInfo 和状态回调计数。
- 是否阻塞：阻塞 S04-C005/C006 的修复优先级；不阻塞“CURRENT 有通用 FSM”结论。

### OQ-S04-012：状态回调中的 ChangeState 是否被业务依赖？

- 当前状态：待验证。
- 已确认：Fsm.ChangeState 先 OnLeave 再改 current 再 OnEnter，没有锁或回滚；OLD PlayerOp 另有 _stateLockCount（S04-E016、S04-E024）。
- 推断：CURRENT 允许回调重入，但具体嵌套顺序依赖业务回调。
- 下一步：搜索 CURRENT FsmState 的 OnLeave/OnEnter 是否直接 ChangeState；如有，建立最小事件序列测试。
- 是否阻塞：阻塞是否借鉴旧 state lock；不阻塞保留当前 API 对照。

### OQ-S04-013：AssetObject shutdown 分支的 YooAsset HandleBase 最终 owner 是谁？

- 当前状态：缺失跨模块证据。
- 已确认：AssetObject.Release(false) Dispose 有效 handle；Release(true) 不 Dispose；ResourceModule 通过 Asset Pool 管理对象；ModuleSystem 关闭最后 ClearAll（S04-E022、S04-E025、S04-E027）。
- 推断：shutdown 可能由资源模块整体回收负责，也可能留下句柄；仅凭 AssetObject 不能决定。
- 下一步：追踪 ResourceModule.Shutdown、YooAsset 资源包关闭和 AssetObject 调用顺序，必要时只做句柄计数/日志验证。
- 是否阻塞：阻塞资源池候选的 shutdown 处理；不阻塞普通 ObjectPool 清理分析。

### OQ-S04-014：CURRENT pooled UI/GameEventMgr 的 stale reference 是否会实际跨 owner 复用？

- 当前状态：静态风险已确认，运行影响待验证。
- 已确认：UIBase.EventMgr 按需 Acquire；RemoveAllUIEvent Release 后不置 null；GameEventMgr.Clear 会解绑并清列表（S04-E020）。
- 推断：只有同一 UIBase 再次访问属性或池把 manager 交给其他 UI 时才显现。
- 下一步：循环打开/关闭同一 UIWindow，并同时创建第二个 UI owner，记录 manager object identity 和全局 handler 列表。
- 是否阻塞：阻塞 S04-C002 是否只需最小修复；不阻塞 GameEventMgr 的当前清理事实。

## 7. 版本、范围与跨领域协调

### OQ-S04-015：OLD 根级 revision 能否取得？

- 当前状态：不能取得。
- 已查位置：OLD 根目录、Assets/Scripts/framework、Assets/Scripts/game、ILScriptProject/Core；根目录无 Git 工作树/根级 SVN working copy，两个带 .svn 的源码目录 revision 为 10480。
- 当前替代证据：调查日期、Unity 版本、关键 OLD 源码 SHA-256 已记录在 README.md、evidence.md、verification.md。
- 下一步：如果需要精确快照，要求只读提供对应 SVN 导出 revision 或归档清单；本轮不执行 svn update。
- 是否阻塞：不阻塞源码结论；阻塞精确历史版本归因。

### OQ-S04-016：ModuleSystem/RootModule 关闭顺序与其他领域的任务、资源、事件 owner 是否一致？

- 当前状态：待跨领域汇总。
- 已确认：CURRENT `RootModule.OnDestroy` 只有在 `#if !UNITY_EDITOR` 分支才调用 `ModuleSystem.Shutdown`；非编辑器路径模块按链表逆序 Shutdown，最后 `MemoryPool.ClearAll`。`GameEvent.Shutdown` 目前只找到定义，未找到 `ModuleSystem.Shutdown` 调用；OLD ModuleMgr/UniTaskManager/PanelMgr 的顺序由启动器和各 manager 实现共同决定（S04-E010、S04-E022、S04-E029）。
- 推断：事件、timer、任务、资源池若没有同一关闭序列，可能出现晚到回调或句柄释放顺序问题；编辑器路径不能直接套用非编辑器的关闭结论。
- 下一步：与 01 启动、02 资源/场景、03 UI、05 网络/配置的文档交叉核对，只提出协调问题，不在本任务改动共享索引。
- 是否阻塞：阻塞跨领域候选的最终排序；不阻塞本领域调查交付。

### OQ-S04-017：ResourceExtComponent 的资源转移和销毁路径是否能覆盖回调异常？

- 当前状态：静态失败路径已确认，运行影响待验证。
- 已确认：`SetAssetByResources` 在调用 `SetAsset` 前即设置 `setAssetObjectTransferred = true`；`SetAsset` 会把对象加入链表后调用具体 `SetAssetObject`，`SetSpriteObject` 再执行用户回调。finally 只在 transfer 标记为 false 时归还对象；组件 `OnDestroy` 只清理映射并 Cancel，没有等待任务或直接释放 `LoadingState`（S04-E028）。
- 推断：如果用户回调抛异常，资源对象可能跳过池归还，已注册的资源/链表项也可能保持到后续清理；OnDestroy 的最终回收依赖任务延续到 finally。
- 下一步：用临时 harness 分别注入 SetAsset 回调异常、请求取消、组件销毁，记录 `_assetItemPool`、`_loadAssetObjectsLinkedList`、`LoadingState` 和 YooAsset handle 的最终状态；确认 finally 是否仍执行及执行顺序。
- 是否阻塞：阻塞 S04-C004 对资源任务 scope 的安全承诺和 S04-C006 的资源池关闭边界；不阻塞当前静态风险结论。

### OQ-S04-018：GameEvent.Shutdown 是否实际纳入全局关闭链？

- 当前状态：代码确认“存在手动能力”，但全局可达性未证实。
- 已确认：`GameEvent.Shutdown` 定义会清空全局事件表；`ModuleSystem.Shutdown` 只逆序关闭模块、清理模块表并调用 `MemoryPool.ClearAll`，当前源码范围搜索未找到对 `GameEvent.Shutdown` 的调用。`RootModule.OnDestroy` 的调用又受 `!UNITY_EDITOR` 条件控制（S04-E029）。
- 推断：不能把 `GameEvent.Shutdown` 写成所有运行环境都会执行的全局 owner 清理；实际可能由外部启动器、场景销毁或未搜索到的生成代码手动调用。
- 下一步：检查构建入口、编辑器退出/域重载回调、热更重载流程和所有程序集引用；运行一次非编辑器与编辑器退出/重载观察事件表、订阅和池状态。
- 是否阻塞：阻塞 S04-C002、S04-C004 的全局关闭排序；不阻塞局部 GameEventMgr 的解绑分析。

### OQ-S04-019：Fsm.DestroyFsm 的状态清理异常是否阻断 map 移除和池归还？

- 当前状态：静态失败路径已确认，运行影响待验证。
- 已确认：`FsmModule.InternalDestroyFsm` 先调用 `fsm.Shutdown()`，再从 `_fsmMap` 移除；`Fsm.Clear` 会调用当前状态 `OnLeave(true)` 和所有状态 `OnDestroy`，`Fsm.Shutdown` 最后通过 `MemoryPool.Release(this)` 归还（S04-E030）。
- 推断：任一状态清理回调抛异常都可能使 `_fsmMap.Remove` 或池归还不可达，形成已销毁 FSM 仍在 map 中或池计数不平衡。
- 下一步：用临时状态分别让 OnLeave、OnDestroy 抛异常，检查 FsmModule map、MemoryPool strict check、后续同 type 创建以及回调执行次数；验证是否存在外层统一异常捕获。
- 是否阻塞：阻塞 S04-C005/C006 的异常清理修复优先级；不阻塞保留 CURRENT 通用 FSM 的结论。
