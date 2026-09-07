# 候选设计清单

本清单只用于后续选择性复刻讨论，不批准任何 API、迁移或改造。优先级表示研究/决策优先级，不是实施排期；没有性能测量，不把任何候选描述为更快。

## S03-C001 — 保留 CURRENT 的类型栈，借鉴 OLD 的职责分层

- 设计：继续以 CURRENT 的 UIWindow 类型、UIModule 栈、WindowAttribute、UIWidget 父子关系作为主模型；只借鉴 OLD 将“管理器持有、窗口生命周期、组件生命周期、业务模块调用方”分开的职责边界，不移植 FairyGUI 的 PanelMgr/IPanel。
- 解决的问题：避免新窗口同时承担资源加载、导航、业务状态、子组件管理和全局清理，建立可复查的层级边界。
- 证据：[S03-E006][S03-E007][S03-E017][S03-E018][S03-E020]。
- CURRENT 等价能力：已有 UIModule、UIWindow、UIWidget、WindowAttribute 和 UIBase hooks，类型栈与父子组件能力已存在。[S03-E017][S03-E018][S03-E020]
- OLD 可借鉴局部：OLD 的 IPanel 契约把 Layer/Priority/Option/OnRemoveBefore/Dispose 显式分开，BaseView 不进入 Panel 栈；这些是职责参考，不是 API 复刻目标。[S03-E006][S03-E007]
- 收益：新窗口的扩展位置、表现状态和管理器职责更容易复查；避免引入第二套 UI 运行时。
- 代价：需要在当前约定/文档中明确 OnCreate、OnRefresh、OnSetVisible、OnDestroy 的使用规则；旧项目已有的返回过滤、缓存和扩展接口不能直接获得。
- 适用条件：CURRENT 继续使用 UGUI/Canvas，窗口以类型创建，组件需要挂入父窗口。
- 适配冲突：OLD 依赖 FairyGUI GObject stage/Dispose/URL 扩展，CURRENT 依赖 Unity GameObject/Canvas；不能把旧的 onRemovedFromStage 清理链直接套入当前窗口。[S03-E013][S03-E019]
- 跨领域依赖：02 需要确认资源实例销毁后的实际引用释放；04 需要确认 GameEvent 的统一订阅约定；06 需要确认生成绑定 hook 的稳定输入。
- 建议：保留当前实现；把旧设计作为职责和审查清单借鉴。优先级：高。

## S03-C002 — 为 CURRENT 窗口补充生命周期级异步取消边界

- 设计：研究并在后续选择性引入“窗口实例拥有一个 linked CancellationToken，Close/Destroy 时取消”的局部机制；加载、延迟刷新、子组件异步创建必须显式接收并使用该 token。不要复制 OLD 的 FairyGUI 代码。
- 解决的问题：处理异步打开中 Close、场景/模块释放和迟到资源回调，减少窗口销毁后继续执行 UI 初始化的机会。
- 证据：[S03-E014][S03-E019][S03-E023]。
- CURRENT 等价能力：IUIResourceLoader 和 IResourceModule 的异步接口已经接受 CancellationToken；UIWidget 的异步创建也从其 GameObject 获取销毁 token，但 UIWindow.InternalLoad 没有传入 token。[S03-E018][S03-E023]
- OLD 可借鉴局部：BasePanel_CancellationToken 在 OnClose 中 Cancel/Dispose/null；它证明“接口存在”仍要求任务主动使用 token。[S03-E010][S03-E014]
- 收益：可以明确 close 与 load 的竞态策略；调用方能区分取消、失败和正常完成。
- 代价：需要改变 UIWindow 的内部调用签名和错误传播方式；UniTaskVoid 的 fire-and-forget 调用必须决定异常观察和取消日志策略。
- 适用条件：窗口确实有异步加载/刷新任务，且资源模块的 token 取消语义在 02 中得到确认。
- 适配冲突：CURRENT Close 已直接 Destroy GameObject；token 取消与迟到 Handle_Completed 的兜底销毁要定义先后，不能假设底层 handle 一定会停止。[S03-E019][S03-E023]
- 跨领域依赖：02 的资源句柄/实例所有权；04 的异步工具和取消异常约定；01 的模块/场景释放时序。
- 建议：借鉴局部机制，先进一步验证；优先级：高。

## S03-C003 — 明确异步打开状态机、去重和失败结果策略

- 设计：在后续设计中为每个窗口类型区分 NotCreated、Loading、Prepared、Visible、Hidden、Destroying/Failed 等状态，并明确重复 Show、Close during Loading、超时、null 资源和异常的结果。是否合并请求、拒绝重复请求或采用最后一次参数，需在 API 评审中选定。
- 解决的问题：让“重复打开”和“异步打开中关闭”拥有确定语义，不把一个布尔集合或轮询超时当成完整状态机。
- 证据：[S03-E008][S03-E009][S03-E019][S03-E020]。
- CURRENT 等价能力：CURRENT 按 Type.FullName 在栈中去重，加载中重复 Show 不会重新启动加载，但会覆盖 prepare callback；ShowUIAsync 返回 void，ShowUIAsyncAwait 轮询最多 60 秒。[S03-E019][S03-E020]
- OLD 对照：OLD 有 m_openingPanel 和 WaitOpenAsync，但 EnsureOpenAsync 仍直接触发 OpenPanelAsync；OpenPanelAsync 的 opening 标记在若干早返/异常路径前未 finally 移除。[S03-E008][S03-E009]
- 收益：后续调用者可以知道是否已打开、正在加载、已取消或失败；可避免重复初始化、孤儿回调和“超时但实际仍在加载”的歧义。
- 代价：会增加管理器状态、请求句柄和测试矩阵；参数合并策略可能改变已有调用方的表现。
- 适用条件：需要支持多个系统并发请求同一窗口、可取消打开或需要可靠 await 结果时。
- 适配冲突：CURRENT 当前公开的 ShowUIAsync 是 void；修改为可 await/可取消可能影响热更调用方。OLD UIAwaiter 的等待者列表也不能直接解决请求合并。[S03-E009][S03-E020]
- 跨领域依赖：02 资源加载失败/取消契约；04 UniTask 错误和取消处理；01 启动/场景切换。
- 建议：先进一步验证和设计，不直接引入 OLD 的 m_openingPanel 形态。优先级：高。

## S03-C004 — 缓存作为显式、可选择的窗口策略，而不是默认行为

- 设计：若后续有窗口确实需要保留实例，再研究 CURRENT 的 per-window opt-in cache：区分 Hide、Close、Detach/Cache、Dispose，提供显式清缓存入口和状态重置策略。默认仍 Close 即 Destroy。
- 解决的问题：在需要保留复杂表现状态时避免重复构建，同时不把所有窗口的资源/状态长期留在内存。
- 证据：[S03-E011][S03-E017][S03-E019][S03-E020]。
- CURRENT 等价能力：CURRENT 只有 HideTimeToClose 延迟销毁和 _uiStack 活动持有，没有 PanelInfo 式 detached 实例缓存；Launcher 字典也在 Close 时销毁。[S03-E020][S03-E022]
- OLD 可借鉴局部：CanCache 面板脱离层容器但保留 PanelInfo.Panel，ClearCachePanel/UnregPanelByName 才 Dispose；缓存方向状态也有单独字段。[S03-E011][S03-E016]
- 收益：缓存的所有权、退出条件和复用入口明确；可以按窗口而不是按全局管理器猜测生命周期。
- 代价：保留 GameObject 与 UI 状态会增加内存和状态污染风险；必须处理场景切换、方向变化、数据刷新和事件重订阅。
- 适用条件：经过测量确认实例构建/绑定成本或状态重建成本足以支持保留实例，且窗口能提供可靠 Reset/Refresh。
- 适配冲突：Unity GameObject Destroy 与 FairyGUI GObject Dispose 不同；不能以旧 CanCache flag 的名字或行为直接复刻。当前资源模块资产池也不代表窗口实例缓存。[S03-E011][S03-E023]
- 跨领域依赖：02 资源引用和卸载；01 场景切换；04 事件重订阅；表现层方向适配。
- 建议：不建议默认引入；在有测量和明确窗口需求时借鉴局部机制。优先级：中。

## S03-C005 — 在 CURRENT 补充最小导航策略，不移植整套 PanelOption

- 设计：在需求确实存在时，为 UIModule 增加独立的“获取可返回顶部窗口/返回处理/批量关闭过滤”策略；层级和全屏遮挡继续使用现有 UILayer/FullScreen。不要把 OLD 的全部表现 flags 迁移成一个大枚举。
- 解决的问题：处理 CURRENT 在已有 GetTopWindow 栈查询之外，仍缺少 IBackHandler/DisBack/IgnoreBack 类返回过滤语义时的返回键和批量关闭规则。
- 证据：[S03-E012][S03-E020][S03-E021]。
- CURRENT 等价能力：已有 UILayer、Push、深度排序和全屏遮挡；缺少在本轮范围内可见的 IBackHandler、返回过滤和白名单批量关闭接口。[S03-E020][S03-E021]
- OLD 可借鉴局部：GetTopPanel 可忽略 Container 等选项，CloseTopPanel 支持 DisBack 与 IBackHandler；PanelOption 同时包含缓存、动画、模糊等多类 flags。[S03-E012]
- 收益：返回行为由框架统一解释，减少业务方遍历窗口栈；保持层级和导航职责清楚。
- 代价：需要定义隐藏窗口、加载中窗口、全屏遮挡和系统层窗口的返回规则；新增接口会影响调用方预期。
- 适用条件：产品交互需要统一系统返回键、返回栈或场景退出清理时。
- 适配冲突：CURRENT GetTopWindow 不跳过 IsHide；OLD DisBack/IgnoreBack 语义依赖 FairyGUI Panel 列表，不能直接照搬。[S03-E012][S03-E020]
- 跨领域依赖：输入/平台返回键、场景与流程模块；不依赖编辑器生成。
- 建议：按实际交互需求进一步验证；没有需求时保留 CURRENT。优先级：中。

## S03-C006 — 研究 UI 级资源/事件/计时器登记袋

- 设计：研究是否为 CURRENT UIBase 增加统一的生命周期登记能力，至少可登记外部事件取消、timer ID、异步任务/Disposable；Close/Destroy 时按所有权清理。实现时需要保持 GameEventMgr 的内存池语义和已有 OnDestroy hook。
- 解决的问题：避免 UI 只清理自身 GameEventMgr 和 HideTimerId，却遗漏业务在外部模块上注册的回调或其他 timer。
- 证据：[S03-E010][S03-E013][S03-E014][S03-E018][S03-E023]。
- CURRENT 等价能力：UI 自有 GameEventMgr 会被归还内存池；HideTimerId 有显式取消；通用 ITimerModule 没有 owner 批量清理，窗口异步加载也未统一登记。[S03-E018][S03-E019][S03-E023]
- OLD 可借鉴局部：BasePanel/BaseView 按对象清理 timer/模块事件，模块按 group 注销，AutoDispose 把 IDisposable 绑定到 GComponent。[S03-E007][S03-E010][S03-E013][S03-E015]
- 收益：窗口所有权检查可以从“业务自己记得清理”变成可审查的登记契约；降低回调持有已销毁 UI 的风险。
- 代价：登记袋本身需要幂等、顺序、异常隔离和重复注册策略；如果把所有业务订阅强行放入 UI，会扩大 UI 与事件/资源模块耦合。
- 适用条件：当前项目出现可复现的 UI 回调泄漏、timer 遗留或跨模块订阅难以审计时。
- 适配冲突：OLD 的 group/FGUI Disposable 不能在 CURRENT 直接复用；CURRENT 的 MemoryPool.Release 后未将 UIBase._eventMgr 置 null，若未来复用同一对象必须另行定义约束。[S03-E018]
- 跨领域依赖：04 通用事件；02 资源句柄；TEngine TimerModule；可能涉及热更程序集边界。
- 建议：先做泄漏样本和生命周期验证，再借鉴局部机制。优先级：中高。

## 未选为候选的方向

- 不建议整体迁移 OLD FairyGUI PanelMgr：两套对象树、资源包、stage 事件和生成代码模型不同，当前已有 UGUI 类型栈；没有证据表明整套迁移能解决当前问题或带来性能收益。[S03-E013][S03-E017][S03-E019]
- 不建议仅因为 OLD 有缓存就把 CURRENT Close 改为保留实例；缓存需要资源、方向、刷新和内存测量证据。[S03-E011][S03-E023]
- 不在本报告提出 editor binding generator 候选；该机制属于 06，本报告只保留运行时 ScriptGenerator/BindMemberProperty 接口边界。[S03-E003][S03-E018][S03-E022]
