# 去重候选设计总表

**候选仅供下一轮规划，不构成批准实施。** 六份输入共 37 个候选全部保留编号，按主要问题归入 24 项；每个原编号只有一个主归属，交界通过依赖关联。不同研究中的 P0/P1、A/B、高/中并不具有统一含义，下面按当前证据和依赖重新给出研究顺序。

顺序 0：本轮已可收敛的保留/证据判断；顺序 1：最值得先形成明确小契约的现有机制；顺序 2：需求成立后再规划；顺序 3：无需求或测量时暂缓。顺序不是工期或实施排期。侵入范围表示若将来选择改造的预估，未经试验不承诺性能收益。F01–F06 见 [审查修复项](review.md#fixes)。

| 汇总项 | 原候选（主归属） | 当前等价能力与证据 | 收益与代价 | 前置依赖 | 侵入范围 | 主要风险 | 是否试验与建议顺序 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| G01 启动 ready 与失败终态 | [S01-C001](../01-startup-modules-hotfix/candidates.md)、[S01-C002](../01-startup-modules-hotfix/candidates.md)、[S01-C003](../01-startup-modules-hotfix/candidates.md) | ModuleSystem/Procedure/ResourceModuleDriver；[M-E004](architecture.md#m-e004)、[M-E006](architecture.md#m-e006) | 收益：启动失败可定位；代价：重定义完成位、失败与入口提交时点 | 先修 F01/F02；确定缺包、metadata 错误的产品行为 | Procedure 与资源初始化调用；中 | 误把 null、异常、已加载 assembly 合为同一终态 | 需要：DLL null/throw、metadata 末次 null、入口无效；顺序 1 |
| G02 全局退出责任 | [S01-C004](../01-startup-modules-hotfix/candidates.md)、[S01-C009](../01-startup-modules-hotfix/candidates.md) | ModuleSystem、SingletonSystem、GameApp destroy listener、GameModule facade；[M-E005](architecture.md#m-e005) | 收益：明确谁终止谁；代价：梳理跨对象退出幂等性 | 先决定是否支持热重启及 Editor 重入 | Root/UpdateDriver/Singleton/UI 交界；高 | 调整顺序可能影响第三方对象与资源 shutdown | 需要：两种销毁顺序、重复 shutdown；顺序 1，范围先限定 |
| G03 发布清单与运行时消费一致 | [S01-C005](../01-startup-modules-hotfix/candidates.md)、[S01-C007](../01-startup-modules-hotfix/candidates.md)、[S02-C004](../02-resources-scenes/candidates.md)、[S06-C002](../06-editor-build-generation/candidates.md)、[S06-C005](../06-editor-build-generation/candidates.md) | UpdateSetting、HybridCLRSettings、collector、ResourceModule 命名包、BuildDLLCommand；[M-E002](architecture.md#m-e002)、[M-E018](architecture.md#m-e018)、[M-E019](architecture.md#m-e019) | 收益：平台、包、版本、DLL/AOT 产物可核对；代价：统一输入及复制失败协议 | 确定真实发布入口、目标平台、包划分与版本来源 | 编辑器/脚本/配置，运行时仅消费约束；中 | active target 与配置不同；把 RawPackage 当必需或照抄旧 DLL 列表 | 需要：目标不一致、缺 AOT/热更产物、物理名到 address；顺序 1 |
| G04 旧入口证据归档 | [S01-C006](../01-startup-modules-hotfix/candidates.md)、[S01-C008](../01-startup-modules-hotfix/candidates.md) | 当前入口已由 GameEntry/Procedure 装配；[M-E003](architecture.md#m-e003)、[M-E004](architecture.md#m-e004) | 收益：防止复制空入口；代价：只整理入口状态 | 新调用证据出现才重开可达性核验 | 仅研究文档；低 | 把桥接日志/示例类型误当初始化成功 | 无需新增运行试验，若主张新入口启用则需验证；顺序 0，本轮已收敛 |
| G05 资源实例与预加载 owner | [S02-C001](../02-resources-scenes/candidates.md) | AssetsReference、AssetObject、多 Spawn 池；[M-E007](architecture.md#m-e007)、[M-E008](architecture.md#m-e008)、[M-E009](architecture.md#m-e009) | 收益：实例/资产/handle 三层计数明确；代价：调用方持有与归还契约 | 先修 F02；确定预加载是否常驻 | ResourceModule/实例创建和预加载调用；中 | 复制旧双减计数；将 Unspawn 等同 Dispose | 需要：双释放、池满销毁、PRELOAD 后平衡普通 load/unload；顺序 1 |
| G06 同 key 异步请求与取消 | [S02-C003](../02-resources-scenes/candidates.md) | cache key、loading marker、TryWaitingLoading、ResourceExt token/代数；[M-E010](architecture.md#m-e010)、[M-E015](architecture.md#m-e015) | 收益：每个等待者得到明确结果；代价：区分共享请求与独立 owner | 先确认有效 UniTask 绑定；明确取消是否终止共享底层请求 | 资源异步重载与调用方；中至高 | 失败遗留 marker、取消他人请求、重复 Spawn/释放 | 需要：命中/未命中、多个等待者、一人取消、handle 失败；顺序 1 |
| G07 场景状态与句柄 owner | [S02-C002](../02-resources-scenes/candidates.md) | SceneModule 主/additive 容器与 ResourceModule；[M-E011](architecture.md#m-e011) | 收益：状态、句柄、切换终态一致；代价：定义失败回滚和切换取消 | 需要当前真实场景使用场景，先勿新增业务调用 | SceneModule 及未来调用方；中 | 旧 ReleaseSceneRef 与当前 API 都存在未闭合路径 | 需要：加载失败、重复卸载、切换中退出；顺序 2，需求成立后 |
| G08 资源诊断 | [S02-C005](../02-resources-scenes/candidates.md) | 现有资产池计数/资源句柄可用于诊断；[M-E007](architecture.md#m-e007)、[M-E008](architecture.md#m-e008)、[M-E009](architecture.md#m-e009) | 收益：定位 owner 和引用差额；代价：开发态采样、输出维护 | 先统一计数含义，诊断不得补偿未知计数 | 开发诊断及少量资源观测点；低至中 | 诊断本身保留对象或误将计数当实际内存 | 需要：已知分配/释放样本；顺序 1，可作为 G05/G06 试验观测 |
| G09 保留 UGUI 类型栈并清晰分层 | [S03-C001](../03-ui-framework/candidates.md) | UIModule/UIWindow/UIWidget 和生成绑定；[M-E013](architecture.md#m-e013) | 收益：职责易审查；代价：若现有类已足够则无需改造 | 以当前窗口样本判定职责缺口 | UI 局部；低 | 为了复刻引入 FairyGUI/第二套窗口身份 | 无需迁移试验；若重构则验证现有窗口行为；顺序 0，保留 |
| G10 窗口异步终态与生命周期取消 | [S03-C002](../03-ui-framework/candidates.md)、[S03-C003](../03-ui-framework/candidates.md) | 类型去重、TryInvoke、IsLoadDone/IsPrepare、late panel 销毁；[M-E012](architecture.md#m-e012)、[M-E013](architecture.md#m-e013) | 收益：await 能表达明确成功/失败/取消；代价：修改现有调用约定 | 先修 F03；确定已存在窗口刷新、ready 定义、超时时钟 | UIModule/UIWindow 和直接 await 调用方；中 | 取消窗口后资源仍在加载；已有窗口立即返回；hook 抛错中断清理 | 需要：首次/重复加载、timeScale=0、缺 Canvas、加载中关闭；顺序 1 |
| G11 UI owner 下的订阅与计时器 | [S03-C006](../03-ui-framework/candidates.md)、[S04-C002](../04-events-async-utilities/candidates.md)、[S04-C003](../04-events-async-utilities/candidates.md) | GameEventMgr、UIBase 事件记录、UIWindow timer id、TimerModule；[M-E013](architecture.md#m-e013)、[M-E014](architecture.md#m-e014) | 收益：集中核对局部清理；代价：登记/解绑协议，避免过度抽象 | 先证明现有局部修正是否足够；SystemTimer 线程策略另判 | UI owner 与事件/timer 接口；中 | 池化 manager 陈旧引用；把延迟取消视为已停止；混入 SystemTimer 线程 | 需要：destroy 后再访问、owner 复用、回调中取消；顺序 1 |
| G12 模块任务作用域 | [S04-C004](../04-events-async-utilities/candidates.md) | 显式 CancellationToken、linked CTS、ResourceExt finally；[M-E012](architecture.md#m-e012)、[M-E015](architecture.md#m-e015) | 收益：任务随 owner 终止且异常可见；代价：任务登记与结束协议 | 必须先确认 reload/重启需求，复用 G10/G11 的局部经验 | 模块入口和异步调用；高 | 全局取消掩盖错误；Cancel 不等于任务已结束/资源已释放 | 需要：上游已取消、替换请求、owner 销毁；顺序 2，不先造全局中心 |
| G13 事件分发状态恢复 | [S04-C001](../04-events-async-utilities/candidates.md) | GameEvent/EventDelegateData 延迟增删；[M-E014](architecture.md#m-e014) | 收益：回调异常后 dispatcher 可继续使用；代价：明确异常传播和重入规则 | 确定是否继续执行后续 handler、当次删除可见性 | EventDelegateData 及分发契约；低至中 | 直接吞异常或照抄旧 payload 回收破坏调用方 | 需要：抛错、同事件嵌套、分发中增删；顺序 1，最适合先形成小契约 |
| G14 保留通用 FSM | [S04-C005](../04-events-async-utilities/candidates.md) | FsmModule/Fsm/Procedure，原 S04-E024/E030 | 收益：保留已具备的通用能力；代价：仅调查目标样本的失败/重入 | 需要具体问题样本，master 未全查状态回调 | FSM 核心，若改造影响面中至高 | 以业务 FSM 替换通用 FSM；忽略 OnEnter/OnLeave 抛错 | 需要：有需求后定向重入/异常测试；顺序 2，保留当前 |
| G15 池的重置、代际与异常归还 | [S04-C006](../04-events-async-utilities/candidates.md) | MemoryPool/ObjectPool 已有 Clear、spawn count、locked 和开发检查；[M-E008](architecture.md#m-e008)、[M-E015](architecture.md#m-e015) | 收益：区分 target 与 wrapper 的释放责任；代价：异常路径语义和调用方审计 | 先修 F04；使用 G05/G11 具体复现决定是否需要代际 | 池核心与 owner；中至高 | 为陈旧引用统一加新池系统；异常时重复归还 | 需要：Clear/Release 抛错、二次归还、target/wrapper 计数；顺序 1，先局部 |
| G16 可选窗口缓存 | [S03-C004](../03-ui-framework/candidates.md) | 当前 Hide/Close/Destroy；旧缓存关闭不 Dispose；[M-E012](architecture.md#m-e012)、[M-E013](architecture.md#m-e013) | 收益：可能减少重复创建，未测量；代价：保留资产、重入刷新和清理 | 必须有窗口需求与内存/打开耗时测量 | UI 缓存策略；中 | 内存增加、旧事件/任务复活 | 需要 A/B 测量与关闭重开验证；顺序 3，默认不引入 |
| G17 最小导航策略 | [S03-C005](../03-ui-framework/candidates.md) | 当前类型栈、层级和全屏遮挡，原 S03-E020/E021 | 收益：返回/遮挡规则可解释；代价：补充交互约定 | 具体返回/遮挡需求 | UIModule 的导航策略；中 | 复制 PanelOption 及旧业务交互 | 需要交互用例验证；顺序 2，有需求才规划 |
| G18 显式排队通知 | [S04-C007](../04-events-async-utilities/candidates.md) | 当前同步 GameEvent；旧 NotifyDispatcher 队列为另一通道，原 S04-E006 | 收益：阶段边界可描述；代价：时序和 payload owner 额外协议 | 必须存在需要跨帧/阶段排序的调用 | 事件/模块更新与调用方；高 | 平行双通道导致顺序难解、误以为线程安全 | 需要队列顺序/重入/退出未消费测试；顺序 3，不默认引入 |
| G19 条件性的网络分层与 pending registry | [S05-C001](../05-network-config-persistence/candidates.md)、[S05-C002](../05-network-config-persistence/candidates.md) | Utility.Http 是请求 helper；无已证实的游戏长连接/RPC 链；[M-E016](architecture.md#m-e016) | 收益：transport/codec/dispatch/request 分责；代价：新子系统和服务器协议联调 | 先修 F05；用户确定长连接、请求响应和协议；不能沿用旧消息 | 新运行时网络边界及调用方；高 | 复制旧 JSON pending 遗留、心跳/重连业务；将 binary sendId 当 correlation | 需要协议样例、超时/断线/Dispose/迟到响应试验；顺序 2，缺能力不是 P0 授权 |
| G20 生成配置到资产读取的契约 | [S05-C003](../05-network-config-persistence/candidates.md) | Configs 上层 Luban 脚本/模板、运行库、ResourceModule；运行输出缺失；[M-E017](architecture.md#m-e017) | 收益：schema→代码→bytes→collector→reader 可追踪；代价：版本/路径/owner 接通 | 选择当前配置需求、工具版本和产物 owner；与 G22 联动 | 生成工具、配置资产/reader；中 | 把模板当实际消费者；迁移旧 CSV/DAO 和业务数据 | 需要最小无业务 schema 样本完整链，另行授权；顺序 2；来源已查明不阻塞研究 |
| G21 分层持久化 | [S05-C004](../05-network-config-persistence/candidates.md) | PlayerPrefs、文件/JSON adapter；原 S05-E012–E015 | 收益：按数据重要性定义恢复；代价：格式版本、平台写入与恢复测试 | 先区分设置、可重建缓存、存档；不存在默认旧 key 迁移 | 必要时新增持久化封装；中至高 | 把配置诊断当存档；未经验证宣称原子性 | 需要实际平台的中断写入/版本恢复试验；顺序 2，有存档要求再规划 |
| G22 生成物唯一 owner | [S06-C003](../06-editor-build-generation/candidates.md) | Luban 脚本、UI Gen/Imp 生成器、Roslyn 生成源；[M-E017](architecture.md#m-e017)、[M-E019](architecture.md#m-e019) | 收益：避免手写被覆盖/多入口重复生成；代价：记录输入输出和生成责任 | 确认哪些输出必须随构建产生，区分当前混合 UI 文件 | 生成器/设置/产物边界；中 | 将未激活输出记为运行链；重复生成覆盖手写 | 需要重复生成差异、手写保留、编译集成的小样本；顺序 1 |
| G23 外部工具可判定结果 | [S06-C004](../06-editor-build-generation/candidates.md) | ShellHelper 可启动进程/读取流；[M-E018](architecture.md#m-e018) | 收益：退出码和日志决定阶段成败；代价：进程退出、I/O 与取消协议 | 确定当前实际生成命令、工作目录、日志保存方式 | Editor ShellHelper/工具调用；低至中 | UI 阻塞、流死锁、进程启动被当成功 | 需要返回非零、stderr、大输出、取消；顺序 1 |
| G24 构建阶段结果与必要扩展 | [S06-C001](../06-editor-build-generation/candidates.md)、[S06-C006](../06-editor-build-generation/candidates.md) | ReleaseTools/BuildWithConfig/BuildConfig；[M-E018](architecture.md#m-e018) | 收益：构建成功可信，阶段顺序显式；代价：现有方法结果适配 | 先修 F06；确认当前必需阶段，联动 G03/G22/G23 | 当前编辑器构建入口；中 | 照搬旧 Export/Wwise/业务处理器；新阶段与旧入口双写 | 需要 AB 失败、下游不放行、目标贯穿；顺序 1；typed 扩展待多个真实需求 |

## 先形成哪些明确契约

G13 事件异常后恢复最适合先界定一个小范围契约：已有实现、问题控制流和验证样本都较明确，但仍需决定异常传播和重入，而不是照抄 OLD 的 catch 策略。

G05/G06/G10 应围绕同一窗口加载样本协调：窗口 ready、调用者取消、共享加载结果和资源 owner 必须一致。先解决 PRELOAD 的持有意图和现有 await 语义，避免各模块分别新增相互冲突的状态表。G11/G15 是其局部清理与池责任补充，不能据此批准全局任务中心。

G03/G22/G23/G24 共同形成编辑器到运行时的产物契约：一个目标输入、明确的阶段结果、唯一生成 owner、真实运行消费。第一步可先做只读清单和错误结果设计，无需迁移旧编辑器处理器。

G01/G02 涉及启动和退出的跨对象责任，在尚未确认热重启需求时应缩小行为范围。网络、存档、场景业务、导航等能力是否需要，应由当前产品需求决定；旧工程存在实现不构成当前缺陷。

## 明确排除

- 不迁移旧业务模块、消息类型、配置数据、玩家存档/key、Wwise/SoData 导出器或旧菜单兼容层。
- 不替换当前 UGUI、FSM、MemoryPool/ObjectPool；若后续确有局部问题，围绕现有能力另定契约。
- 不把 OLD 的引用计数、opening 标记、payload Release、JSON pending 清理作为可靠模板。
- 不把生成模板、目录、analyzer 标签、编译调用符号或构建日志单独作为运行成功证据。
- 缓存、请求合并和池设计都没有性能实测；表中的收益是可验证的设计目的，不是已经实现的提升。

源码裁决和完整 M-E 位置见 [architecture.md](architecture.md)。原研究的事实错误与范围修正完成后，才适合用本表冻结下一轮改造契约。

