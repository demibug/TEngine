# Slave 04：事件、异步与通用运行时机制调查

## 1. 目的与结论边界

本文档记录对参考工程 ProjectOld 的事件、异步与通用运行时机制的静态源码调查，并用当前 TEngine 工程做针对性对照。目标是为后续“选择性复刻”提供可复查依据；本轮不修改框架源码、不迁移业务功能，也不冻结未来生产 API。

调查范围包括：

- 跨模块通知、事件分发、订阅与解绑、简易事件。
- UniTask/Coroutine 相关的任务取消、Fire-and-Forget、帧队列异步。
- 计时器、延迟任务、计时器回调退出路径。
- 通用状态机及其生命周期。
- 内存池、对象池、池对象重置和错误复用防护。
- 所有权、清理顺序、回调异常、重入/分发中修改、线程与主线程约束。

UI、资源、网络和启动代码只作为调用链锚点，不展开其领域内部设计。源码能证明的内容标为“代码确认”；从静态结构推导的行为标为“推断”；需要 Unity 编译、运行或包来源确认的内容标为“待验证”。

## 2. 调查基线

- 调查窗口：2026-09-06（主源码调查）至 2026-09-07（文档与范围最终核验），Asia/Shanghai。
- OLD 绝对根路径：D:\Work\SAUnity\ProjectOld
- CURRENT 绝对根路径：E:\MyWork\MyFramework\TEngine\UnityProject
- 两个工程的 ProjectSettings/ProjectVersion.txt 均为 Unity 2022.3.62f2，revision 7670c08855a9。
- OLD 根目录未发现 Git 工作树或根级 SVN 工作副本；Assets/Scripts/framework 与 Assets/Scripts/game 各自带 SVN 元数据，调查时 revision 均为 10480。相关路径、revision、文件 SHA-256 见 [evidence.md](evidence.md) 与 [verification.md](verification.md)。
- CURRENT 调查前分支为 framework。已知的其他变更包括 ProjectSettings/boot.config 删除、UserSettings/Layouts/default-2022.dwlt 未跟踪，以及其他 slave 的 handoff 文档；本任务只新增本目录文件。

## 3. 建议阅读顺序

1. [findings.md](findings.md)：先看机制结论、调用链、异常和生命周期表。
2. [evidence.md](evidence.md)：按 S04-E 编号回到源码、程序集和配置行号。
3. [candidates.md](candidates.md)：查看候选设计及 CURRENT 等价能力对照。
4. [open-questions.md](open-questions.md)：查看缺失证据和下一步验证方法。
5. [verification.md](verification.md)：查看本轮实际做过的范围与文档核验。

## 4. 覆盖清单

| 机制 | OLD | CURRENT | 覆盖状态 |
|---|---|---|---|
| 字符串/整数事件分发 | ZeroFramework EventDispatcher、GEvent | GameEvent、EventDispatcher、EventDelegateData | 已调查；异常与分发中修改均有差异（S04-E003、S04-E009、S04-E017、S04-E019） |
| 跨模块通知 | NotifyDispatcher、ModuleMgr、BaseModule | 未发现同构的排队通知总线；主要是 GameEvent 与 ModuleSystem | OLD 已调查；CURRENT 等价能力待进一步按实际调用确认（S04-E006、S04-E007、S04-E018、S04-E022） |
| 订阅所有权与解绑 | 按对象/组移除、模块组、面板关闭 | GameEventMgr 记录委托，UIBase 在销毁时归还池 | 已调查；发现一个 pooled manager 字段未置空的风险（S04-E003、S04-E008、S04-E020） |
| UniTask 与取消 | UniTaskManager 全局/模块 CTS、面板 CTS | 调用方显式 CancellationToken；资源加载有 linked CTS 与 finally，但转移标记和销毁等待仍有边界 | 已调查；CURRENT 没有发现全局任务管理器（S04-E010、S04-E011、S04-E023、S04-E028） |
| 帧队列异步 | AsyncTodo、NetworkBase 调用方 | 未发现同名机制 | OLD 已调查；CURRENT 未找到等价实现（S04-E006、S04-E012、S04-E014） |
| 延迟与计时器 | FairyGUI.Timers、DelayTodo、RunTimer | TimerModule、System.Timers.Timer 适配 | 已调查；System.Timer 的释放/线程边界待验证（S04-E013、S04-E014、S04-E021、S04-E022） |
| 通用状态机 | 未发现 framework 级统一 FSM；两个 game 业务 FSM | FsmModule、Fsm、ProcedureModule | 已调查；CURRENT 是更直接的通用能力（S04-E016、S04-E024、S04-E030） |
| 对象/内存池 | EventParam、CallbackVo/ParamVo、Timers、RunTimer 等局部池 | MemoryPool、ObjectPoolModule、资源对象池 | 已调查；重点检查 Clear、strict check、异常中断（S04-E005、S04-E015、S04-E025、S04-E026、S04-E027） |
| 主线程约束 | Unity Update 驱动的 Timers；无统一线程断言 | RootModule/ModuleSystem Update；System.Timer 无内部回主线程代码；RootModule 关闭分支受 `!UNITY_EDITOR` 条件控制 | 已调查；不能据静态代码证明全链路线程安全（S04-E013、S04-E021、S04-E022、S04-E029） |

未找到或未能证明的内容：

- OLD 根级统一版本号、根级 Git/SVN revision。
- CURRENT GameEventHelper 的生成源码和生成后的包装实现；只看到 analyzer DLL、属性、调用入口（S04-E017）。
- CURRENT 业务层完整的 GameEvent Add/Remove 实际调用闭环；框架/UI 封装存在，但搜索到的业务使用证据很少（S04-E020）。
- CURRENT `GameEvent.Shutdown` 虽有定义，但未找到 `ModuleSystem.Shutdown` 或 `RootModule` 对它的调用，不能据此证明全局事件表已纳入统一关闭链。
- CURRENT 资源异步回调抛异常时的池对象归还、已注册资源释放，以及 `ResourceExtComponent.OnDestroy` 的任务等待/直接清理只能由静态路径确认，尚无运行验证。
- CURRENT `Fsm.DestroyFsm` 在状态清理回调抛异常时是否仍完成 map 移除和池归还，尚无运行验证。
- 任何运行时异常、重入、线程、池复用测试结果。

## 5. 证据约定

所有关键结论和候选建议都引用 S04-E 编号。证据项同时记录工程、相对路径、类/方法/配置字段、核验行号、短摘录、证据性质（定义、调用、配置或范围检查）。候选编号为 S04-C001 起，候选均未批准实施。
