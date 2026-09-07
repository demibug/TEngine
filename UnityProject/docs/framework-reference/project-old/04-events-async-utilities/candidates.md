# S04 候选设计

## 1. 说明

以下候选均未批准实施，也不构成未来生产 API。建议优先级只表示后续验证价值，不表示性能收益；本轮没有做性能测量。每个候选都先回答 CURRENT 是否已有等价能力，再判断是保留、局部借鉴、进一步验证还是不建议引入。

## 2. 候选总览

| 编号 | 方向 | CURRENT 等价能力 | 建议 | 优先级 |
|---|---|---|---|---|
| S04-C001 | 事件异常安全与分发状态恢复 | 有 GameEvent/EventDelegateData，但异常后状态恢复不足 | 局部借鉴并优先验证 | A |
| S04-C002 | 订阅的 owner/作用域句柄 | 有 GameEventMgr 局部记录，但 UIBase 有 pooled 字段风险 | 局部借鉴并优先验证 | A |
| S04-C003 | 计时器 owner、取消和 SystemTimer 退出 | 有 TimerModule/int id/延迟删除 | 局部借鉴，先修订契约 | B |
| S04-C004 | 统一任务作用域与取消/异常策略 | 有显式 CancellationToken 和资源 finally；无全局任务中心 | 按 reload 需求进一步验证 | B |
| S04-C005 | 通用 FSM | CURRENT 已有 FsmModule/Procedure | 保留当前实现，验证边界 | B |
| S04-C006 | 池对象代际、异常归还和重置审计 | CURRENT MemoryPool/ObjectPool 已较完整 | 保留当前实现，优先做边界修复/测试 | A |
| S04-C007 | 显式排队通知通道 | 未找到与 OLD NotifyDispatcher 同构的 CURRENT 通道 | 进一步验证，不建议直接复制双通道 | C |

## 3. S04-C001：让事件分发在异常后恢复可用

### 解决的问题

CURRENT EventDelegateData.Callback 直接调用 handler，正常返回后才 CheckModify。一个 handler 抛异常会跳过剩余 handler 和待处理变更，留下 _isExecute=true；OLD EventDispatcher 虽然逐 callback 捕获异常并继续，但它也存在 dispatcher 被回调 Dispose 时提前 return 的缺口。因此目标不是“照搬 OLD”，而是明确 CURRENT 的异常后状态不变量（S04-E004、S04-E019）。

### 设计选项

在 EventDelegateData 内部用 try/finally 保证 CheckModify 至少执行一次；再由上层选择一种明确策略：

1. 隔离策略：捕获每个 handler 异常、记录事件 ID/handler，并继续后续 handler。
2. 传播策略：先在 finally 恢复分发状态和应用增删，再把一个聚合异常或首个异常交给调用者。
3. 配置策略：开发环境隔离并报警，生产环境保留传播/上报；必须写明不同环境的契约。

对分发中 Add/Remove 应补测试：本次是否执行新增、删除自身、删除后续 handler、同 handler 重复 Add、嵌套同事件 Send，以及 callback 中 Clear/Shutdown。

### CURRENT 对照、收益与代价

- 当前等价能力：EventDelegateData 已有分发中增删队列，但当前循环不检查待删除列表，移除后续 handler 仍可能执行；同一事件的嵌套 Send 也没有重入保护。因此可复用其列表结构，但不能把现有语义当作异常安全契约（S04-E019）。
- OLD 可借鉴部分：逐 callback 异常隔离、回调后统一清理；不能直接借鉴其同类型 IsDispatch 拒绝或所有旧池逻辑，因为这些会改变现有语义（S04-E004）。
- 收益：异常后事件对象不再必然停留在 execute 状态；后续 handler/订阅变更的行为可测试、可记录。
- 代价：传播/隔离选择会改变调用方错误处理；日志或聚合异常可能增加诊断成本；finally 本身不能恢复已执行 handler 的业务副作用。
- 约束：必须确认 GameEvent.Send 的所有重载、生成 wrapper 和热更边界；不能只修一个参数数量的 Callback 重载。
- 建议：A，局部借鉴并优先验证。不要以“旧工程每回调吞异常”为默认答案。

## 4. S04-C002：用 owner/作用域约束订阅生命周期

### 解决的问题

OLD 支持按对象和 group 移除，Timeline/面板能在 owner 退出时批量解绑；CURRENT GameEventMgr 也保存 eventType 与 Delegate 并在 Clear 中解绑，但 UIBase 在 Release 后没有把 _eventMgr 置空。对象再次创建或池复用时，旧 owner 仍可能持有已经归还的 manager 引用（S04-E003、S04-E008、S04-E020）。

### 设计选项

优先验证而非立即扩展 API：

- 最小修复方向：所有 pooled manager 归还后清空 owner 字段，并让重复 Clear/RemoveAll 幂等。
- 作用域方向：为一个 owner 返回可 Dispose 的 subscription scope/handle，scope Dispose 只清理由它创建的 handler。
- 组方向：保留当前 GameEventMgr 的局部列表，再增加显式 owner group；不把全局 GameEvent 表的事件移除交给业务自行扫描。

Handle 必须具有重复 Dispose 安全、事件分发中 Dispose 的明确时机、池回收后的失效语义；否则只是把 stale reference 从 manager 换成 handle。

### CURRENT 对照、收益与代价

- 当前等价能力：GameEventMgr 已有局部所有权列表，UIWindow.InternalDestroy 已调用 RemoveAllUIEvent；先修字段和复用验证，可能已解决最主要风险（S04-E020）。
- OLD 可借鉴部分：RemoveEventListenerByObject、RemoveEventListenerByGroup 的 owner/group 组织方式（S04-E003、S04-E011）。
- 收益：减少“忘记保存原委托”以及关闭后晚到回调的风险；让 owner 生命周期可在审查时直接追踪。
- 代价：生成式接口、UIBase 和现有 AddUIEvent 的 API 兼容；scope 与 MemoryPool 的生命周期需要协同，不能让 handle 跨回池继续有效。
- 约束：必须先做 UIWindow 重复 InternalCreate/InternalDestroy、池交叉借用和分发中销毁测试；必须明确全局 GameEvent.Shutdown 与局部 scope 的先后顺序。当前 ModuleSystem.Shutdown 未发现调用 GameEvent.Shutdown，不能假定全局事件已纳入关闭链（S04-E029）。
- 建议：A，局部借鉴并优先验证。若最小的 _eventMgr = null 已足够，不应为凑 API 引入另一套全局订阅系统。

## 5. S04-C003：把计时器取消变成可验证的 owner 作用域

### 解决的问题

CURRENT TimerModule 的普通 timer 使用 int id，RemoveTimer 只设置 isNeedRemove，调用方自己保存并取消；Handler 没有异常隔离。AddSystemTimer 还只 Stop，不 Dispose、不解绑 Elapsed、不清 _ticker，本类也没有回主线程代码。OLD FairyGUI.Timers 有 delegate.Target 的 RemoveObject 和 deferred add/remove，但默认回调异常也逃逸，且它的 singleton 退出闭环未证实（S04-E013、S04-E021）。

### 设计选项

在不改变现有 AddTimer 兼容入口的前提下，后续可验证：

- 增加带 owner/generation 的 timer scope，使 owner 退出时批量标记 timer；
- 让 Remove 幂等，并定义“回调已经开始时取消是否阻止本次回调”；
- 在 Update 中用 try/finally 确保一次性 timer 的删除，另行定义异常传播或记录策略；
- 对 System.Timers.Timer 明确 Stop、Elapsed -= callback、Dispose、列表清空及线程转发策略；如果没有真实调用方，应考虑收窄或隐藏该 API。

### CURRENT 对照、收益与代价

- 当前等价能力：TimerModule 已有 scaled/unscaled list、id、Pause/Resume/Restart、Shutdown 清空和延迟删除；UI HideTimerId 是真实调用（S04-E021、S04-E022）。
- OLD 可借鉴部分：RemoveObject 的 owner 批量清理，以及 pending/active 分离；不直接复制 FairyGUI 的参数池或静态 singleton（S04-E013）。
- 收益：减少 UI/模块退出后的晚到回调；让 SystemTimer 的资源和线程边界可审查。
- 代价：timer id 到 handle 的迁移、回调异常语义和关闭顺序会影响 UI/资源/网络调用方；owner 绑定若过强会限制跨 owner 的合法 timer。
- 约束：需要跨模块确认主线程入口；不能把 System.Timers.Elapsed 当成 Unity Update 回调；需要运行时测试 callback 中 Remove、Add、Shutdown。
- 建议：B，局部借鉴并先修订契约；没有调用证据的 SystemTimer 不建议直接扩展。

## 6. S04-C004：统一任务作用域，但保留显式取消和错误可见性

### 解决的问题

OLD UniTaskManager 解决 global/module reload 取消，并给 RunAndForget 规定取消吞掉、其他异常记录；面板再链接 global token。CURRENT 资源异步链已经用 caller token、linked CTS、替换取消和 finally 清理，但 Procedure 启动直接 UniTaskVoid.Forget，没有统一 owner 或取消入口（S04-E010、S04-E011、S04-E023）。

### 设计选项

可先定义概念而不立即新增全局 singleton：

- 模块/UI/资源组件各持有一个 task scope 或 CTS；
- 新任务必须显式接收 scope.Token；
- scope 关闭时 Cancel，再等待或观察任务完成；
- Fire-and-Forget 必须统一异常回调/上报，不把所有异常静默吞掉；
- 只有确实存在 reload 全局边界时，再把各 scope 链接到一个全局 CTS。

CURRENT ResourceExtComponent 的 finally 可作为“局部清理结构”的样例，但不能直接视为所有失败路径安全：资源转移标记早于 SetAsset 回调，回调抛异常时可能跳过池对象归还；组件 OnDestroy 也只 Cancel、不等待任务完成或直接释放 LoadingState（S04-E023、S04-E028）。

### CURRENT 对照、收益与代价

- 当前等价能力：显式 CancellationToken、GetCancellationTokenOnDestroy、资源链 linked CTS；没有发现 OLD UniTaskManager 同构的全局任务表（S04-E023）。
- OLD 可借鉴部分：模块 token、全局 reload token、debug tracked task；OLD 实际 wrapper 调用覆盖率和异常策略需要先确认，不能默认全量接入（S04-E010）。
- 收益：模块关闭或请求替换时有统一取消边界，减少“任务仍持有旧 owner”。
- 代价：token 传播会增加调用方负担；Cancel 与等待的顺序可能改变业务时序；tracked task 字典和全局 singleton 带来额外状态。
- 跨领域依赖：资源、UI、流程、网络关闭顺序；需和启动/热更/资源 slave 汇总确认，不在本任务实现。
- 建议：B，进一步验证。若只需要资源/界面局部退出，保留显式 token；不建议仅因 OLD 有 manager 就复制全局中心。

## 7. S04-C005：保留 CURRENT 通用 FSM，集中验证失败和重入

### 解决的问题

OLD framework 没有统一 FSM，PlayerOp 和 KSBattle 是两套业务状态机；CURRENT 已由 FsmModule/Fsm/ProcedureModule 提供通用 owner、Update、Destroy 和池清理链。因此“复刻旧 FSM”不能解决 CURRENT 的缺口，真正的问题是 CURRENT Fsm.Create 中途失败和状态回调重入（S04-E016、S04-E024）。

### 设计选项

- 保留 IFsmModule/FsmState API，补充 Create 失败时的回收/部分初始化策略。
- 明确 OnInit、OnEnter、OnLeave、OnDestroy 抛异常时 FSM 的状态不变量。
- 明确 OnLeave/OnEnter 中 ChangeState 的是否允许、排队还是拒绝；必要时加开发期重入断言。
- 对 Procedure 流程做 Create、Start、Shutdown、Restart 的最小状态测试。

### CURRENT 对照、收益与代价

- 当前等价能力：CURRENT 已是 framework 级通用实现，ProcedureSetting 有真实启动和关闭链；不需要引入 OLD 的业务 FSM。需要单独验证 DestroyFsm 在状态清理回调抛异常时是否仍能从 map 移除并归还池（S04-E024、S04-E030）。
- OLD 可借鉴部分：PlayerOp 的 state lock 只可作为“是否允许重入”的一种局部策略，不能直接套到所有 FSM（S04-E016）。
- 收益：保持当前调用方和模块优先级不变，降低跨域迁移风险。
- 代价：需要设计异常/重入契约和测试；状态回调是业务扩展点，框架无法替业务回滚副作用。
- 建议：B，保留当前实现，进一步验证；不建议引入 OLD 业务 FSM。

## 8. S04-C006：保留 CURRENT 池体系，审计代际和异常归还

### 解决的问题

CURRENT MemoryPool 已统一 IMemory.Clear、统计和可配置 strict double-release 检查；ObjectPool 再以 spawn count、Locked、capacity/expire 控制目标对象释放。OLD 的池更分散，EventParam、Timers、RunTimer 各自只重置部分字段。因此没有证据支持整体替换 CURRENT 池（S04-E005、S04-E013、S04-E015、S04-E025、S04-E026）。

### 设计选项

- 将“Acquire 后初始化失败必须归还”写成池化类型的审计规则；
- Object<T>.Release、ObjectPool.ReleaseObject、资源 AssetObject.Release 使用 try/finally 或明确失败状态；
- 对 pooled wrapper/manager 增加 generation 或 owner 状态，仅在确认现有调用方需要时引入；
- 继续在开发/测试环境启用 strict check，检查 Clear 是否清空委托、CTS、资源句柄、参数引用；
- 先修复/验证 UIBase 归还 GameEventMgr 后字段仍引用的问题，而不是新增复杂池协议。

### CURRENT 对照、收益与代价

- 当前等价能力：MemoryPool/ObjectPool 已覆盖 Fsm、GameEventMgr、LoadingState、AssetObject 等实际对象；ModuleSystem.Shutdown 最后调用 MemoryPool.ClearAll（S04-E023、S04-E024、S04-E025、S04-E027）。
- OLD 可借鉴部分：按用途使用小池、用 owner 清理临时对象；不借鉴其未清 param 或缺少统一 Reset 的部分（S04-E005、S04-E013）。
- 收益：降低错误复用、池 wrapper 遗留和异常中断造成的状态不一致。
- 代价：strict check 有明确运行成本提示；generation/owner 检查会增加状态和 API 复杂度；对 Unity/YooAsset 资源对象还要验证句柄所有权。
- 约束：MemoryPool.ClearAll 的关闭时机、热更对象生命周期、YooAsset HandleBase 释放策略必须协调；RootModule 进入 ModuleSystem.Shutdown 受 `!UNITY_EDITOR` 条件控制，不能把编辑器路径与非编辑器路径混为一谈（S04-E029）。
- 建议：A，保留当前实现并优先做边界修复/测试，不建议整体替换。

## 9. S04-C007：若需要顺序边界，单独定义排队通知

### 解决的问题

OLD NotifyDispatcher 用双缓冲把 Register/Unregister/SendNotify 延迟到 Update；CURRENT GameEvent 是直接 Send，ModuleSystem 只负责模块更新顺序。本轮没有找到 CURRENT 同构的跨模块排队通知总线。若将“需要下一帧处理”和“立即事件”混为一个 API，调用方很难判断观察时机（S04-E006、S04-E007、S04-E018）。

### 设计选项

如后续业务确实需要，可设计与 GameEvent 分开的 Queue/Notify 通道：

- 明确 enqueue 时机、处理阶段和模块优先级；
- 分发期间新增/删除在下一批生效；
- 明确每个 handler 异常是否隔离；
- pooled payload 只由发送批次归还一次，并定义 owner；
- 关闭时先停止入队，再清理 pending payload。

### CURRENT 对照、收益与代价

- 当前等价能力：ModuleSystem 的 Priority 提供更新顺序，GameEvent 提供立即事件；没有证据表明二者已经满足排队通知语义（S04-E022、S04-E018）。
- OLD 可借鉴部分：双缓冲操作列表和 handler 异常隔离（S04-E006）。
- 收益：为跨模块时序提供可命名的边界，避免在普通事件回调中修改模块注册表。
- 代价：增加延迟、队列内存、payload 所有权和关闭顺序复杂度；可能产生立即/排队两套可观测时机。
- 跨领域依赖：启动、场景、资源、网络模块对通知时序的依赖，需要联合验证。
- 建议：C，进一步验证；不建议直接复制 OLD 的“模块队列 + 外部立即派发”双路径，因为那会保留 OLD 的时序歧义。
