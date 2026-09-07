# 统一框架地图与源码裁决

本文件是 2026-09-07 的静态研究汇总，**六份输入齐全，但原研究有待修正项**。阅读时以本文件中 master 直接核验的限定结论为准；其余结论保留原证据来源，不表示已逐项复审。源文件完整位置见文末 M-E 账本，原 S01–S06 编号不重编。

“事实”指源码、序列化配置或直接调用可确认的内容；“推断”必须附成立条件；“待验证”不补写为运行结果。本轮没有运行 Unity、生成器或构建流程。

## 基线与归属

CURRENT Git 根为 `E:/MyWork/MyFramework/TEngine/`，分支 `framework`，HEAD 为 `16afccb5df2a2a2efcb5003ecf9fbc0781c0170a`。OLD 项目根没有 Git；各研究记录的嵌套 SVN 修订不能充当整个工程的唯一版本号。两工程 Unity 均为 2022.3.62f2（7670c08855a9）。记录中 60 个不同源码文件的 SHA256 与本轮读取一致；S05 的根路径/HEAD 是文档抄录错误。[M-E001](#m-e001)

| 边界 | OLD | CURRENT | 借鉴限制 |
| --- | --- | --- | --- |
| 第三方基础能力 | Unity、YooAsset、HybridCLR、UniTask、FairyGUI；工程内另有适配与修改 | Unity UGUI、embedded YooAsset/UniTask、HybridCLR、Luban 运行库及工具 | 包内代码存在不等于原版能力；第三方加载/渲染/异步能力不能记为项目自研 |
| 项目框架机制 | LaunchLoader、MgrCenter、ModuleMgr、PanelMgr/BasePanel、LoadMgr/ResourceMgr、事件与通知封装 | ModuleSystem/Procedure/FSM、ResourceModule/ObjectPool、UIWindow/UIWidget/GameEvent 等 | 比较契约和 owner；不整体复制旧对象模型 |
| 项目适配 | 包名、地址转换、生成器、DLL 清单、绑定代码、构建入口 | UpdateSetting、collector、ReleaseTools、Luban/UI/事件生成接入 | 输入→生成→收集→运行消费逐段核对 |
| 业务层 | dls.game 模块、WorldMap/MainPanel、登录/心跳、Wwise 与具体导出处理器 | GameLogic 中窗口/入口等实际样本 | 仅作为可达性样本，全部排除出迁移范围 |

归属和原始定位：[S01-E029/S01-E028](../01-startup-modules-hotfix/evidence.md)、[S03-E013/S03-E024](../03-ui-framework/evidence.md)、[S04-E017](../04-events-async-utilities/evidence.md)、[S05-E004/S05-E008](../05-network-config-persistence/evidence.md)、[S06-E006/S06-E023/S06-E026](../06-editor-build-generation/evidence.md)。框架地图描述的是可见机制，不是运行性能排序。

## 启动、装配与热更

OLD 的证据链为 BuildSettings 的 Updater → 场景绑定的 GameLauncher → YooAsset 启动 → 条件性 metadata/Launcher 装载 → HotUpdateLauncher → 反射桥接 → Launch 场景中的 GameLaunchProxy/GameLaunch → LaunchLoader 注册经理与 ModuleMgr。Updater/Launch 的脚本 GUID 和继承关系支持入口可达，后续仍受平台、宏、资源和配置约束。[S01-E003/S01-E005/S01-E013](../01-startup-modules-hotfix/evidence.md)、[M-E003](#m-e003)

必须区分两件事：反射 FrameworkEntry/GameEntry 的调用确实存在，但实质经理初始化在这两个方法里是注释；GameLaunch 的 commander 注册才提供另一条具体装配链。ModuleProvider 扫描 dls.game 的机制来自原 S01-E016，不由存在某个 asmdef 推导而来。HotUpdateEntry/ILScriptProject 仍未连接到主启动链，不作为待迁移入口。[S01-E016/S01-E017/S01-E027/S01-E032](../01-startup-modules-hotfix/evidence.md)

CURRENT 为 main 场景的 GameEntry prefab → Awake 预取模块并 StartProcedure().Forget() → 配置中的 ProcedureLaunch 及后续更新流程 → ProcedureLoadAssembly → GameApp.Entrance；GameApp 有实际 `ShowUIAsync<BattleMainUI>()` 调用。ModuleSystem 的注册/OnInit 与 ResourceModuleDriver.Start 内的资源配置/Initialize 是两个时点；没有运行顺序实证时，既不能宣布“必有竞态”，也不能把一次 Yield 当成完整 ready 协议。[S01-E019/S01-E023/S01-E024](../01-startup-modules-hotfix/evidence.md)、[M-E004](#m-e004)、[M-E005](#m-e005)

当前 DLL 和 AOT metadata 必须分别描述。DLL 是串行 await TextAsset 后直接调用处理函数；返回 null 会先减计数，若循环正常结束，尾部仍可设置 DLL 完成标志。metadata 是回调加载，最后一次 null 回调可能绕过完成标志更新，是否卡住取决于完成顺序。await/Assembly.Load 抛异常又是另一条路径；两种处理函数的 UnloadAsset 都在 finally 之后，重抛会跳过释放。AllAssemblyLoadComplete 在校验 GameApp 前先切换流程，不能将“完成位”视为成功终态。[M-E006](#m-e006)，修正 [S01-C002/S01-C003](../01-startup-modules-hotfix/candidates.md) 与 [S02-E032](../02-resources-scenes/evidence.md) 的解释。

旧 UseYooAsset=0 仅是检入配置。非 Editor Init 强制 EnableAB，特定编辑器入口又强制 UseYooAsset=true，因此“启动器先用 YooAsset”和“后续 LoadMgr 按配置分支”可以同时成立。S06 的构建覆盖证据补足 S01/S02 的问题，但仍缺实际发布任务选择和最终产物快照，不能断言所有发布包使用同一分支。[M-E002](#m-e002)

OLD 的通用经理由 MgrCenter 立即 Initialize 并转发 Update/LateUpdate，关闭有 BeforeDispose/Dispose 阶段；业务模块另由 ModuleMgr 组织 Init、InitAfter 和更新。CURRENT 将通用 ModuleSystem 与热更侧 SingletonSystem 分开，不能把两个“模块”一一机械对应。这部分装配细节保留原研究依据。[S01-E015/S01-E016/S01-E020/S01-E030](../01-startup-modules-hotfix/evidence.md)

## 资源、场景与清理

| 对象/阶段 | 直接事实 | 条件或未决问题 |
| --- | --- | --- |
| OLD 普通资产 | LoadService 持有句柄/引用记录，LoadMgr 有不同后端 | 多套表和释放入口不同；Acquire/普通加载/场景引用不能混为一个计数 |
| OLD prefab 实例 | ResourceRef.OnDestroy 回到 ResourceMgr；显式 DestroyGameObject 有实际调用 | Destroy 之前减计数而不清 Key，销毁回调可再次减计数；池满分支同类。借鉴 owner 思路，不能复制实现 |
| CURRENT 普通资产 | AssetObject 持 handle；UnloadAsset 是 Unspawn，随后池策略才可能 Dispose | shutdown=true 跳过 handle Dispose；不能由普通释放链推导完整退出 |
| CURRENT prefab 实例 | AssetsReference 将 source prefab 绑定到克隆，实例销毁释放对应资产使用引用 | Unity 实例、资产池使用计数、YooAsset handle 是三层所有权 |
| CURRENT PRELOAD | 非 EditorSimulate 路径产生 Spawn 引用；成功回调只记完成 | 原预加载引用没有配对 Unspawn，过期时间不会选中 IsInUse 对象；常驻意图和最终 owner 未定义 |
| OLD 场景 | SceneMgr→LoadMgr 有业务样本，Unity scene 回调驱动地图阶段 | LoadMgr.UnloadScene 没有接入 ReleaseSceneRef；后者首次释放 handle 与 scene ref 归零也不是同一条件 |
| CURRENT 场景 | SceneModule 分主场景与 additive handle，局部 unload API 存在 | 未找到研究范围内业务调用；加载失败状态恢复、主场景旧 handle、重复 UnloadAsync 等需专项契约 |

定位：[M-E007](#m-e007)、[M-E008](#m-e008)、[M-E009](#m-e009)、[M-E011](#m-e011)；原证据 [S02-E008–E015/S02-E018–E020/S02-E024–E030/S02-E034/S02-E037](../02-resources-scenes/evidence.md)。

资源合并请求已存在缓存 key、loading marker 和等待机制，不能直接等同于“共享一个可等待结果且取消独立”。部分缓存命中等待不消费调用方 token；await 抛异常可能跳过 marker/handle 收尾；失败回调也可能被适配器的异常提前绕过。因此 S02-C003 候选应先定义异常、取消和各消费者的引用账，而非新增一张全局请求表。[M-E010](#m-e010)

## UI、事件与局部任务的交界

OLD 的 FairyGUI 渲染/Timers 属于第三方基础，PanelMgr/BasePanel 的层级、导航、缓存和 owner 清理是项目封装。移出 stage、OnClose 和最终 Dispose 不同：缓存窗口会关闭却不 Dispose；m_openingPanel 的记录没有完整 finally 和重复请求共享结果；linked CTS 在已被上游取消时也不保证 Dispose/null。当前 UGUI 类型栈和 UIWidget 足以作为改良基座，没有证据支持迁移 FairyGUI。[M-E012](#m-e012)、[S03-E011/S03-E013/S03-E017](../03-ui-framework/evidence.md)

CURRENT ShowUIAsyncAwait 的实际契约：

1. 找到已有窗口便返回，包括仍加载中的窗口。
2. 仅新建窗口轮询 IsLoadDone；以 Time.deltaTime 累计超过 60 退出，不是墙钟上限，timeScale=0 时累计不会增长。
3. 轮询超时仍返回窗口；IsLoadDone 又早于 destroyed/Canvas/IsPrepare 检查，因此返回不证明准备成功。
4. InternalDestroy 先解绑部分事件/子项，再调用用户 OnDestroy，之后才销毁 panel、设 destroyed 和取消 timer；hook 抛异常可能中断尾部。加载中关闭后的 late panel 有销毁分支，但不代表加载已被取消。

这是高优先窗口状态候选的实际起点，修正 S03 的“一律最多等待 60 秒”简化。[M-E013](#m-e013)

事件方面，CURRENT EventDelegateData 在 handler 抛异常后可能不执行 CheckModify，留下 _isExecute 与待增删列表；OLD 逐 handler catch，但还有拒绝同事件重入、当次可见新增监听、每回调 Release payload 等语义。借鉴异常后的状态恢复，需要单独决定是否继续分发、重入策略及 payload owner，不能原样移植旧 dispatcher。[M-E014](#m-e014)、[S04-E003/S04-E004/S04-E019](../04-events-async-utilities/evidence.md)

UIBase 已有 GameEventMgr 的局部订阅登记，销毁后未清空池化 manager 字段是条件性陈旧引用风险，不是已复现泄漏。资源设置的 transfer 标志则在 SetAsset 之前；SetAsset 登记 linked list 后才调用 setter，Sprite 也在用户回调前赋值。用户回调异常时资源仍有 owner，之后按 IsCanRelease 条件回收；“finally 跳过 release”本身不足以证明失去所有权。应审查的是错误终态、目标继续持有和 owner 销毁后的完整释放。[M-E014](#m-e014)、[M-E015](#m-e015)

CURRENT FSM/Procedure 与 MemoryPool/ObjectPool 已具通用机制。原 S04 记录的 timer 取消、系统计时器线程/Dispose、FSM 回调重入和池异常归还问题保留为候选，master 未逐一复查全部调用方。通用 owner 方案应先在一个窗口/资源样本上验证，再决定是否扩展到模块任务域。[S04-E021/S04-E024/S04-E025/S04-E026/S04-E030](../04-events-async-utilities/evidence.md)

## 网络、配置、持久化与生成物

OLD 的传输 facade、worker、codec、消息分发与 JSON 请求关联不能合并成一个“统一 RPC 框架”。只有已读 JsonNetwork 子层按 SessionId 管 pending；普通 binary sendId 不是请求完成证据。OnDispose 没有清 m_reqMap，也不主动终结 pending，若对象仍被引用可继续保留 context/回调。连接失败的重试不等同于断线后完整重连恢复。[M-E016](#m-e016)、[S05-E002–E008](../05-network-config-persistence/evidence.md)

CURRENT Utility.Http 是请求级 helper，Editor MCP WebSocket 属于编辑器工具；本轮没有建立游戏长连接或 RPC 消费链。缺少这些能力不构成本次研究阻塞，也不产生实现授权。持久化已有 PlayerPrefs/文件/JSON 等局部能力，旧工程同样未证明统一原子提交/迁移；具体账户、聊天、地图、旧 key 都不是本轮迁移输入。[S05-E012–E016](../05-network-config-persistence/evidence.md)

S06 在仓库上层找到了 Configs/GameConfig 的 Luban 脚本和模板，补足 S05 限于 UnityProject 的工具来源；本轮检查 GameProto/GameConfig、ConfigSystem.cs、AssetRaw/Configs/bytes 仍不存在。因此可以确认生成契约来源，不能确认运行时表访问已接通。模板自身 LoadByteBuf 的 TextAsset 引用也需在将来接入时明确 owner。[M-E017](#m-e017)

同理，Tools 中事件生成器源码、RoslynAnalyzer DLL 标签和 GameApp 中 GameEventHelper.Init 调用是三个不同证据点。是否实际生成、参与本轮编译、注册并被业务消费仍未运行验证；不能仅以 DLL 的 Editor importer disabled 判定 analyzer 失效。UI 的生成/手写分区可作为所有权设计参考，当前混合样本不应被未来生成器覆盖。[M-E019](#m-e019)、[S06-E029–E035](../06-editor-build-generation/evidence.md)

构建关注点是当前 ReleaseTools 的结构化结果、target 贯穿、产物预检和生成物 owner。AB 失败返回后窗口仍可日志“构建完成”；DLL/复制的 activeBuildTarget 与配置 PlayerPlatform 不必相同；ShellHelper 启动成功不等于工具退出成功。旧反射 [Export]、Wwise、SoData 等仅用于解释风险，不作为兼容层或业务处理器迁移任务。[M-E018](#m-e018)、[S06-E006/S06-E007](../06-editor-build-generation/evidence.md)

## 统一退出边界与未决信息

ModuleSystem、SingletonSystem、GameModule facade、静态 GameEvent、Unity 对象和第三方资源系统是不同生命期。ModuleSystem 是按 Priority 排序链的逆向关闭，不是简单逆创建时间；RootModule 的 Editor/Player 路径不同。GameApp 的销毁监听会受 UpdateDriver.Release 清事件的顺序影响，所以 UI/Event 的正常局部清理不能证明全局关闭已串联。[M-E005](#m-e005)

后续规划最少需要：目标是否支持热重启/重复入场；窗口 await 的成功定义及取消 owner；预加载常驻策略；游戏是否需要长连接；实际发布入口/平台/包清单；哪些生成物由构建负责。缺少这些决策时可以确认源码事实，但不能冻结新 API 或承诺性能收益。范围和修复要求见 [review.md](review.md)，建议顺序见 [candidates.md](candidates.md)。

## master 核验证据账本

所有下列文件链接均为完整绝对路径，链接打开起始行，正文同时给出检查范围与符号。编号代表一次相关源码抽查，不等于独立测试。除 M-E001 的命令事实外，均是静态读取；原编号只表示关联原始证据。

<a id="m-e001"></a>

### M-E001：可比较基线

事实：只读 Git 命令确认仓库根、分支 framework、完整 HEAD；两份版本文件一致。S05 抄录根路径与 HEAD 有误，非源码哈希漂移。

- [ProjectSettings/ProjectVersion.txt:1–2](<E:/MyWork/MyFramework/TEngine/UnityProject/ProjectSettings/ProjectVersion.txt:1>)（m_EditorVersion / m_EditorVersionWithRevision）
- [ProjectSettings/ProjectVersion.txt:1–2](<D:/Work/SAUnity/ProjectOld/ProjectSettings/ProjectVersion.txt:1>)（同上）

原始证据：[S01-E001](../01-startup-modules-hotfix/evidence.md)、[S01-E002](../01-startup-modules-hotfix/evidence.md)；[S05-E001](../05-network-config-persistence/evidence.md)。

<a id="m-e002"></a>

### M-E002：旧运行分支受编辑器产物影响

事实：非 Editor 配置初始化只强制 EnableAB；BuildAll 和 CommandHelper 的特定入口另行强制 UseYooAsset=true。不能外推所有入口。

- [Assets/Scripts/framework/Library/ZeroFramework/FrameworkConfig.cs:33–110](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/FrameworkConfig.cs:33>)（Init / 配置读取）
- [Assets/Scripts/framework/Editor/AssetBundle/AssetBundlePanel.cs:96–121](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Editor/AssetBundle/AssetBundlePanel.cs:96>)（BuildAll）
- [Assets/Scripts/framework/Editor/Export/CommandHelper.cs:300–345](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Editor/Export/CommandHelper.cs:300>)（UseYooAsset 赋值所在构建流程）

原始证据：[S01-E031](../01-startup-modules-hotfix/evidence.md)；[S02-E002](../02-resources-scenes/evidence.md)、[S02-E003](../02-resources-scenes/evidence.md)；[S06-E003](../06-editor-build-generation/evidence.md)、[S06-E037](../06-editor-build-generation/evidence.md)。

<a id="m-e003"></a>

### M-E003：旧启动桥接与实际模块装配

事实：GameLauncher 先初始化 YooAsset；反射桥接调用不等于经理已经装配，具体 LaunchLoader 注册在 GameLaunch。

- [Assets/Scripts/Launcher/GameLauncher.cs:52–137](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/Launcher/GameLauncher.cs:52>)（Start / InitializeYooAsset）
- [Assets/Scripts/HotUpdate/Launcher/HotUpdateLauncher.cs:616–715](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/HotUpdate/Launcher/HotUpdateLauncher.cs:616>)（InitializeFramework / InitializeGame / EnterGame）
- [Assets/Scripts/game/Module/Launch/GameLaunch.cs:315–387](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/game/Module/Launch/GameLaunch.cs:315>)（InitLaunchLoader / 注册 commander）

原始证据：[S01-E006](../01-startup-modules-hotfix/evidence.md)、[S01-E007](../01-startup-modules-hotfix/evidence.md)、[S01-E010](../01-startup-modules-hotfix/evidence.md)、[S01-E014](../01-startup-modules-hotfix/evidence.md)、[S01-E017](../01-startup-modules-hotfix/evidence.md)。

<a id="m-e004"></a>

### M-E004：当前模块构造不等于资源 ready

事实：GameEntry.Awake 预取模块并启动 UniTaskVoid 流程；ModuleSystem 按优先级插入后调用 OnInit；资源实际 Initialize 在 ResourceModuleDriver.Start。Yield 一次不能单独证明所有资源准备完成。

- [Assets/GameScripts/GameEntry.cs:6–14](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/GameEntry.cs:6>)（Awake）
- [Assets/TEngine/Runtime/Core/ModuleSystem.cs:107–118](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Core/ModuleSystem.cs:107>)（CreateModule）
- [Assets/TEngine/Runtime/Core/ModuleSystem.cs:143–193](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Core/ModuleSystem.cs:143>)（RegisterUpdate / OnInit）
- [Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureSetting.cs:55–102](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureSetting.cs:55>)（StartProcedure）
- [Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs:236–271](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs:236>)（Start）

原始证据：[S01-E019](../01-startup-modules-hotfix/evidence.md)、[S01-E020](../01-startup-modules-hotfix/evidence.md)、[S01-E023](../01-startup-modules-hotfix/evidence.md)、[S01-E026](../01-startup-modules-hotfix/evidence.md)。

<a id="m-e005"></a>

### M-E005：当前退出不是已证明的统一闭环

事实：ModuleSystem 逆优先级链关闭；RootModule.OnDestroy 仅在非 Editor 调用它。UpdateDriver.Shutdown 先 Release 清 DestroyEvent，再 Destroy；GameApp 将 SingletonSystem.Release 注册在该事件。不同对象销毁先后仍需运行核验。

- [Assets/TEngine/Runtime/Core/ModuleSystem.cs:29–60](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Core/ModuleSystem.cs:29>)（Update / Shutdown）
- [Assets/TEngine/Runtime/Module/RootModule.cs:140–167](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/RootModule.cs:140>)（Update / OnDestroy）
- [Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs:24–37](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs:24>)（Shutdown）
- [Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs:335–341](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs:335>)（MainBehaviour.OnDestroy）
- [Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs:443–452](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs:443>)（MainBehaviour.Release）
- [Assets/GameScripts/HotFix/GameLogic/GameApp.cs:25–46](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/GameApp.cs:25>)（Entrance / Release）

原始证据：[S01-E020](../01-startup-modules-hotfix/evidence.md)、[S01-E021](../01-startup-modules-hotfix/evidence.md)、[S01-E025](../01-startup-modules-hotfix/evidence.md)、[S01-E030](../01-startup-modules-hotfix/evidence.md)；[S04-E029](../04-events-async-utilities/evidence.md)。

<a id="m-e006"></a>

### M-E006：热更失败按 Task 与回调分别裁决

事实：DLL await 后直接调用 LoadAssetSuccess；null 会先减计数，正常循环尾仍能置完成。metadata 使用回调，末次 null 可以跳过完成更新。两处理函数异常重抛，UnloadAsset 都在 finally 之后；不能声称异常也释放。进入游戏的流程切换早于入口校验。

- [Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs:42–150](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs:42>)（OnEnter / LoadAssembly / OnUpdate / AllAssemblyLoadComplete）
- [Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs:185–292](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs:185>)（LoadAssetSuccess / LoadMetadataForAOTAssembly / LoadMetadataAssetSuccess）

原始证据：[S01-E024](../01-startup-modules-hotfix/evidence.md)；[S02-E032](../02-resources-scenes/evidence.md)。

<a id="m-e007"></a>

### M-E007：旧实例引用存在条件性重复减计数路径

事实：DestroyGameObject 先减计数后 Destroy，未清 Key；ResourceRef.OnDestroy 再进入 ReleaseGameObject。若 manager 和 prefab 记录仍存活、非重启，则再次减计数；池满销毁分支同类。不是运行复现或内存损失测量。

- [Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceRef.cs:36–48](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceRef.cs:36>)（Key / OnDestroy）
- [Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceMgr.cs:619–685](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceMgr.cs:619>)（ReleaseGameObject / DestroyGameObject）
- [Assets/Scripts/game/Module/Skin/Comps/SkinControlNode.cs:159–176](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/game/Module/Skin/Comps/SkinControlNode.cs:159>)（ReleaseGameInstance 实际调用）

原始证据：[S02-E014](../02-resources-scenes/evidence.md)、[S02-E015](../02-resources-scenes/evidence.md)。

<a id="m-e008"></a>

### M-E008：当前实例与池的不同所有者

事实：AssetsReference 将实例销毁映射为源资产 Unload；Unload 是池 Unspawn，底层 handle 真正释放另有阶段；AssetObject.Release(true) 跳过 Dispose。Object<T>.Release 归还目标 _object，wrapper 由 ObjectPool 的调用层归还。

- [Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs:61–120](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs:61>)（Ref / OnDestroy / CheckRelease）
- [Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs:160–174](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs:160>)（Instantiate）
- [Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs:43–66](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs:43>)（UnloadAsset / CreateMultiSpawnAssetPool）
- [Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.AssetObject.cs:11–61](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.AssetObject.cs:11>)（Create / Release）
- [Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.Object.cs:116–186](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.Object.cs:116>)（Create / Spawn / Unspawn / Release）
- [Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.ObjectPool.cs:511–559](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.ObjectPool.cs:511>)（Shutdown / GetCanReleaseObjects）

原始证据：[S02-E026](../02-resources-scenes/evidence.md)、[S02-E027](../02-resources-scenes/evidence.md)；[S04-E026](../04-events-async-utilities/evidence.md)。

<a id="m-e009"></a>

### M-E009：预加载持有不能由过期时间自动抵消

事实：非 EditorSimulate 预加载调用回调 overload；首次 Register(...,true)，命中也 Spawn；成功回调只置标记，没有配对 Unspawn。释放候选先排除 IsInUse，随后才按过期时间筛选。常驻是否有意未知。

- [Assets/GameScripts/Procedure/ProcedurePreload.cs:126–168](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/Procedure/ProcedurePreload.cs:126>)（LoadAllConfig / PreLoad / OnPreLoadAssetSuccess）
- [Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:1037–1129](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:1037>)（LoadAssetAsync(string,int,LoadAssetCallbacks,object,string)）
- [Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.ObjectPool.cs:541–598](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.ObjectPool.cs:541>)（GetCanReleaseObjects / DefaultReleaseObjectFilterCallback）

原始证据：[S02-E033](../02-resources-scenes/evidence.md)。

<a id="m-e010"></a>

### M-E010：资源 await 的异常与取消不是统一终态

事实：资源模块多个重载没有共同 finally；本地 YooAsset UniTask 适配器的 Failed 分支抛异常，能绕过 await 后的失败回调/标记清理。泛型资源重载使用 cancellationToken 命名参数，但已读适配器签名不含该参数；有效宏和编译绑定未验证，不能宣布整仓编译失败。

- [Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:769–921](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:769>)（LoadAsset / LoadAssetAsync<T> / LoadGameObjectAsync）
- [Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:1093–1129](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:1093>)（await 之后的失败回调和登记）
- [Packages/UniTask/Runtime/External/YooAsset/OperationHandleBaseExtensions.cs:1–28](<E:/MyWork/MyFramework/TEngine/UnityProject/Packages/UniTask/Runtime/External/YooAsset/OperationHandleBaseExtensions.cs:1>)（UNITASK_YOOASSET_SUPPORT / ToUniTask 签名）
- [Packages/UniTask/Runtime/External/YooAsset/OperationHandleBaseExtensions.cs:145–161](<E:/MyWork/MyFramework/TEngine/UnityProject/Packages/UniTask/Runtime/External/YooAsset/OperationHandleBaseExtensions.cs:145>)（BaseContinuation 状态分支）

原始证据：[S02-E025](../02-resources-scenes/evidence.md)、[S02-E039](../02-resources-scenes/evidence.md)、[S02-E041](../02-resources-scenes/evidence.md)。

<a id="m-e011"></a>

### M-E011：场景业务状态与句柄须同时审查

事实：旧 SceneMgr 有实际 WorldMap 调用，但 LoadMgr.UnloadScene 的现有分支不能证明抵达 YooAsset ReleaseSceneRef；当前 SceneModule 维护句柄但本轮未建立业务调用闭环，方法自身缺乏统一 finally，回调卸载路径连续两次调用 UnloadAsync。

- [Assets/Scripts/framework/Library/ZeroFramework/Scene/SceneMgr.cs:222–270](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Scene/SceneMgr.cs:222>)（Load）
- [Assets/Scripts/game/Module/WorldMap/WorldMapModule_API.cs:194–210](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/game/Module/WorldMap/WorldMapModule_API.cs:194>)（EnterWorld 中的 SceneMgr.Load 实际调用）
- [Assets/Scripts/framework/Library/ZeroFramework/Load/LoadMgr.cs:1311–1349](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Load/LoadMgr.cs:1311>)（LoadSceneAsync / UnloadScene）
- [Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetLoadService.cs:361–424](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetLoadService.cs:361>)（ReleaseAssetRef / ReleaseSceneRef）
- [Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs:25–128](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs:25>)（OnInit / Shutdown / LoadSceneAsync）
- [Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs:361–395](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs:361>)（UnloadScene 回调路径）

原始证据：[S02-E010](../02-resources-scenes/evidence.md)、[S02-E018](../02-resources-scenes/evidence.md)、[S02-E019](../02-resources-scenes/evidence.md)、[S02-E020](../02-resources-scenes/evidence.md)、[S02-E029](../02-resources-scenes/evidence.md)、[S02-E030](../02-resources-scenes/evidence.md)、[S02-E034](../02-resources-scenes/evidence.md)。

<a id="m-e012"></a>

### M-E012：旧窗口打开、关闭、缓存是不同阶段

事实：opening 标记并非可靠 single-flight，若中途返回或异常会遗留；移出 stage 的 close 清理与最终 Dispose 分开。linked CTS 已被上游取消时，CancelPanelTasks 条件不再进入 Dispose/null 分支。

- [Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs:1220–1358](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs:1220>)（异步打开路径）
- [Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs:2271–2347](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs:2271>)（RemoveUI / cache / Dispose）
- [Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel.cs:280–314](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel.cs:280>)（RemoveToStage）
- [Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel.cs:368–370](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel.cs:368>)（OnClose）
- [Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel_CancellationToken.cs:10–54](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel_CancellationToken.cs:10>)（PanelCancellationToken / PanelToken / CancelPanelTasks）

原始证据：[S03-E008](../03-ui-framework/evidence.md)、[S03-E010](../03-ui-framework/evidence.md)、[S03-E011](../03-ui-framework/evidence.md)、[S03-E014](../03-ui-framework/evidence.md)。

<a id="m-e013"></a>

### M-E013：当前 UI await 不保证窗口 ready

事实：已存在窗口立即返回，包括仍在加载的窗口；仅新建窗口轮询 IsLoadDone，累计 Time.deltaTime 超过 60 才退出，不是墙钟超时，退出仍返回窗口。Handle_Completed 在检查 destroyed、Canvas 和 IsPrepare 前置 IsLoadDone；无效 Canvas 可使加载标志与准备状态分离。

- [Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs:282–285](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs:282>)（ShowUIAsyncAwait）
- [Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs:310–364](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs:310>)（ShowUIImp / TryGetWindow / ShowUIAwaitImp）
- [Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs:300–350](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs:300>)（TryInvoke / InternalLoad / InternalCreate）
- [Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs:427–502](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs:427>)（InternalDestroy / Handle_Completed）

原始证据：[S03-E019](../03-ui-framework/evidence.md)、[S03-E020](../03-ui-framework/evidence.md)。

<a id="m-e014"></a>

### M-E014：事件语义不同，旧机制也有负担

事实：旧 dispatcher 拒绝同事件嵌套、逐回调 catch，并在每个回调后 Release 可回收 payload；当前 Callback 异常可绕过 CheckModify，使执行标志和延迟变更未恢复。选择异常隔离、重入和增删可见时机必须另定契约。

- [Assets/Scripts/framework/Library/ZeroFramework/Event/EventDispatcher.cs:350–458](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Event/EventDispatcher.cs:350>)（DispatchEvent / IsDispatch / payload Release）
- [Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs:32–153](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs:32>)（Add / Remove / CheckModify / Callback）
- [Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs:284–330](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs:284>)（事件 manager 获取与 RemoveAllUIEvent）

原始证据：[S04-E003](../04-events-async-utilities/evidence.md)、[S04-E004](../04-events-async-utilities/evidence.md)、[S04-E019](../04-events-async-utilities/evidence.md)、[S04-E020](../04-events-async-utilities/evidence.md)。

<a id="m-e015"></a>

### M-E015：资源设置 callback 抛错前已有 owner

事实：先 transfer=true 再 SetAsset；SetAsset 先登记 linked list，再调用 setter；SetSpriteObject 先赋 sprite 再调用用户回调。finally 跳过回收不等于无人持有，随后由 IsCanRelease 决定是否 Unspawn/归还。组件 OnDestroy 只 Cancel pending，不等待完成。

- [Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs:40–152](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs:40>)（SetAssetByResources / transfer / finally）
- [Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs:187–196](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs:187>)（OnDestroy）
- [Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.cs:109–157](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.cs:109>)（ReleaseUnused / SetAsset）
- [Assets/TEngine/Runtime/Module/ResourceModule/Extension/Implement/SetSpriteObject.cs:51–86](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/Extension/Implement/SetSpriteObject.cs:51>)（SetAsset / IsCanRelease）

原始证据：[S04-E023](../04-events-async-utilities/evidence.md)、[S04-E028](../04-events-async-utilities/evidence.md)。

<a id="m-e016"></a>

### M-E016：JSON pending 销毁遗漏

事实：Send 仅为有 reply callback 的请求登记 context；回复按 SessionId 移除 pending。OnDispose 清 list、listenerMap（重复两次）、pool，却没有清 m_reqMap 或完成 pending；若 JsonNetwork 仍被引用，map 可继续保留 context/delegate。不是通用 binary RPC 的证明。

- [Assets/Scripts/game/Managers/Network/JsonNetwork.cs:102–180](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/game/Managers/Network/JsonNetwork.cs:102>)（Send / 超时处理）
- [Assets/Scripts/game/Managers/Network/JsonNetwork.cs:182–241](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/game/Managers/Network/JsonNetwork.cs:182>)（GeneralReplyHandler / DoReqContext / OnDispose）

原始证据：[S05-E006](../05-network-config-persistence/evidence.md)、[S05-E007](../05-network-config-persistence/evidence.md)。

<a id="m-e017"></a>

### M-E017：配置生成源与运行时接入分开

事实：旧 BaseDao 按平台、UseYooAsset 和文件存在性选择 CSV/资产。当前仓库上层存在 Luban 生成脚本和模板；模板同步加载 TextAsset 后取 bytes，未配对 Unload。目标配置代码与 bytes 目录本轮不存在，模板不能当当前实际消费者。

- [Assets/Scripts/framework/Library/ZeroFramework/Config/BaseDao.cs:199–271](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Config/BaseDao.cs:199>)（Load 配置分支）
- [Configs/GameConfig/gen_code_bin_to_project_lazyload.bat:1–22](<E:/MyWork/MyFramework/TEngine/Configs/GameConfig/gen_code_bin_to_project_lazyload.bat:1>)（生成入口与输出参数）
- [Configs/GameConfig/CustomTemplate/ConfigSystem.cs:9–56](<E:/MyWork/MyFramework/TEngine/Configs/GameConfig/CustomTemplate/ConfigSystem.cs:9>)（Tables / LoadByteBuf）

原始证据：[S05-E009](../05-network-config-persistence/evidence.md)、[S05-E010](../05-network-config-persistence/evidence.md)、[S05-E011](../05-network-config-persistence/evidence.md)、[S05-E016](../05-network-config-persistence/evidence.md)；[S06-E026](../06-editor-build-generation/evidence.md)、[S06-E027](../06-editor-build-generation/evidence.md)。

<a id="m-e018"></a>

### M-E018：构建失败结果与目标传播缺口

事实：BuildWithConfig AB 失败可日志后正常 return；窗口随后打印完成。其 DLL 无参入口及复制使用 activeBuildTarget，而 Player 使用 config.PlayerPlatform；显式 target 重载也未贯穿复制。缺失 AOT 可日志后继续。

- [Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:119–212](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:119>)（BuildWithConfig / BuildInternalWithConfig）
- [Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs:490–507](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Editor/ReleaseTools/BuildPipelineWindow.cs:490>)（BuildWithConfig 调用与完成日志）
- [Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs:86–107](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs:86>)（BuildAndCopyDlls / CopyAOTHotUpdateDlls）
- [Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs:136–174](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs:136>)（CopyAOTAssembliesToAssetPath / CopyHotUpdateAssembliesToAssetPath）
- [Assets/TEngine/Editor/Utility/ShellHelper.cs:107–153](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Editor/Utility/ShellHelper.cs:107>)（RunByPath：启动进程而非退出结果）

原始证据：[S06-E020](../06-editor-build-generation/evidence.md)、[S06-E021](../06-editor-build-generation/evidence.md)、[S06-E022](../06-editor-build-generation/evidence.md)、[S06-E023](../06-editor-build-generation/evidence.md)、[S06-E028](../06-editor-build-generation/evidence.md)。

<a id="m-e019"></a>

### M-E019：DLL 地址与事件生成不靠文件名猜测

事实：YooAsset 地址规则取去扩展名文件名，可将 GameLogic.dll.bytes 映射为 GameLogic.dll；须与实际 collector 和运行配置共同解释。事件生成器源码在 Tools，AddSource 路径可见；DLL 带 RoslynAnalyzer 标签，Editor importer enabled=0 本身不能判定 analyzer 未运行。

- [Packages/YooAsset/Editor/AssetBundleCollector/DefaultRules/DefaultAddressRule.cs:15–20](<E:/MyWork/MyFramework/TEngine/UnityProject/Packages/YooAsset/Editor/AssetBundleCollector/DefaultRules/DefaultAddressRule.cs:15>)（GetAssetAddress）
- [Assets/TEngine/Settings/UpdateSetting.asset:16–29](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Settings/UpdateSetting.asset:16>)（程序集名称配置）
- [Tools/GameEventSourceGenerator/SourceGenerator/Generator/EventInterfaceGenerator.cs:30–74](<E:/MyWork/MyFramework/TEngine/Tools/GameEventSourceGenerator/SourceGenerator/Generator/EventInterfaceGenerator.cs:30>)（Execute / AddSource）
- [Assets/TEngine/Runtime/Core/GameEvent/SourceGenerator.dll.meta:1–29](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Core/GameEvent/SourceGenerator.dll.meta:1>)（RoslynAnalyzer / PluginImporter）

原始证据：[S01-E022](../01-startup-modules-hotfix/evidence.md)；[S04-E017](../04-events-async-utilities/evidence.md)；[S06-E033](../06-editor-build-generation/evidence.md)、[S06-E034](../06-editor-build-generation/evidence.md)、[S06-E035](../06-editor-build-generation/evidence.md)、[S06-E043](../06-editor-build-generation/evidence.md)。

M-E001 附加只读命令：在 CURRENT 执行 `git rev-parse --show-toplevel`、`git rev-parse HEAD`、`git branch --show-current`、`git status --short`、`git diff --stat`、`git diff --cached --stat`；输出基线与保留变更见 review。M-E017 附加存在性检查：`Assets/GameScripts/HotFix/GameProto/GameConfig`、其 `ConfigSystem.cs` 与 `Assets/AssetRaw/Configs/bytes` 均为 False；不将此扩大为整个仓库不存在生成工具。
