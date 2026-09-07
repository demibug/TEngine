# master 正确性审查记录

审查日期：2026-09-07。角色：新的 master 正确性审查者与汇总者，使用 master-planner；未调用其他 agent。**结论：六份输入完整，汇总交付完成，但存在实质修正项，整体审查未通过。** 下述修复仅针对研究文档，不批准生产改造。

## 规范、输入与基线

先检查 git status、tracked/staged diff，并直接读取未跟踪研究文档；读取适用 AGENT.md/CLAUDE.md、master-planner 和 tengine-dev 规范。按用户更窄的授权范围，仅写研究根 README 与 synthesis；不写记忆、不动六个研究目录/handoffs。

| 输入 | 必需文件 | 原证据 | 原候选 | verification 与链接 |
| --- | --- | --- | --- | --- |
| [S01](../01-startup-modules-hotfix/README.md) | 6/6 | 32 | 9 | 已读，定位可查；失败路径需修正 |
| [S02](../02-resources-scenes/README.md) | 6/6 | 42 | 5 | 已读，含异常补充；预加载/临时引用结论需修正 |
| [S03](../03-ui-framework/README.md) | 6/6 | 24 | 6 | 已读；await/ready 语义需修正 |
| [S04](../04-events-async-utilities/README.md) | 6/6 | 30 | 7 | 已读；owner 和 target/wrapper 表述需修正 |
| [S05](../05-network-config-persistence/README.md) | 6/6 | 16 | 4 | 已读；基线抄录和 pending 容器遗漏需修正 |
| [S06](../06-editor-build-generation/README.md) | 6/6 | 43 | 6 | 已读；候选中旧业务兼容范围需修正 |
| 合计 | 36/36 | 187 | 37 | 输入齐全，不是“部分汇总” |

每份均含 README/findings/evidence/candidates/open-questions/verification。README 所链接的研究细分内容均已读取，没有额外缺失子文档。链接/编号闭合只证明引用存在，不能取代源码真实性核验。

CURRENT Git 根 `E:/MyWork/MyFramework/TEngine`，分支 `framework`，HEAD `16afccb5df2a2a2efcb5003ecf9fbc0781c0170a`。OLD 根无 Git；原研究记录的嵌套 SVN revision（framework/game 10480，UIProject/Tools assembly 等 45216）仅作用于对应嵌套位置，本轮未重新核验 SVN 工作副本状态。Unity 同为 2022.3.62f2（7670c08855a9）。基线校验采用原文件路径与 SHA256，不能把项目目录的版本号视为所有文件洁净。

初始变更保留：

- `ProjectSettings/boot.config`：已有删除，tracked diff 为删除空文件。
- `UserSettings/Layouts/default-2022.dwlt`：已有未跟踪文件。
- `docs/`：已有未跟踪研究与 handoffs。已记录 36 份研究文档和 8 份 handoffs 的 SHA256 作为保护基线。
- 初始 staged diff 为空；没有执行 stage、commit、restore、reset、clean。

## 实际抽查范围

不是重新全量调查。完整读取输入后，按高优先候选和交界定向读取源码、检索调用/配置；[architecture.md 的 M-E001–M-E019](architecture.md#master-核验证据账本) 列出绝对路径、符号和行号。

| 领域 | master 实际核验 | 能确认的界限 |
| --- | --- | --- |
| 启动 | BuildSettings/GUID 关联、GameLauncher、反射桥接、GameLaunch 注册、当前 Awake/Start/Procedure、DLL 与 metadata 控制流 | 有条件的入口/调用链，不声称发布包成功启动 |
| 资源 | ResourceRef/ResourceMgr 显式销毁与实际调用、LoadService 释放、当前回调/Task/实例加载、AssetsReference、池 Spawn/Unspawn、PRELOAD | 正常路径和静态异常缺口，不声称实际内存泄漏数值 |
| 场景 | 旧 SceneMgr/LoadMgr/ReleaseSceneRef、当前加载与卸载实现 | 旧业务样本已见；当前仅接口实现，缺业务使用闭环 |
| UI | 旧异步打开/stage/缓存/取消与 MainPanel 样本；当前重复打开、await、IsLoadDone、销毁、事件字段 | 不能把窗口对象存在或加载完成位当作 ready |
| 事件/通用机制 | OLD/当前分发核心，ResourceExt 提交/回收，池 target/wrapper；跨系统 shutdown | 未逐项重查所有 timer/FSM/通知队列调用方 |
| 网络/配置 | JsonNetwork pending/reply/dispose，连接失败边界，BaseDao 分支，HTTP helper，Luban 上层脚本/模板与输出缺失 | 不执行网络；不把旧 binary 心跳当 RPC，不把模板当运行 consumer |
| 编辑器/生成 | 构建覆盖 UseYooAsset、ReleaseTools/窗口结果、DLL target/复制、ShellHelper、地址规则、事件生成源/metadata | 不执行生成/构建；来源与集成生效分别判断 |

主审直接重新读取记录中 60 个不同源码文件并核对其 SHA256，全部匹配，无失配；这是哈希检查覆盖，不等于每份文件全部语义重新审查。M-E 新增抽查采用当前完整路径/行号；没有借哈希匹配替原结论背书。

## 实质发现与冲突裁决

| 问题 | 影响 | 裁决/出处 | 原研究动作 |
| --- | --- | --- | --- |
| F01 DLL 与 metadata 的 null/异常混述 | 错误定位启动等待及失败终态 | DLL 正常循环尾可置完成；metadata 回调另判；重抛可跳过 Unload。[M-E006](architecture.md#m-e006) | S01 局部修正 |
| F02 PRELOAD 自动回收与临时资源“完整链” | owner 设计建立在错误回收假设上 | in-use 不进入过期筛选；TextAsset 释放非异常安全；旧实例存在条件性双减。[M-E007](architecture.md#m-e007)、[M-E009](architecture.md#m-e009) | S02 局部修正 |
| F03 await 的 60 秒/ready 简化 | 高优先窗口 API 契约失真 | 已有窗口立即返回；新建才累计 deltaTime；IsLoadDone 先于有效性/prepare。[M-E013](architecture.md#m-e013) | S03 局部修正 |
| F04 transfer 顺序/owner 证据不足、池归还对象错误 | 无法据此判断异常后的持有/清理 | SetAsset 前 transfer，回调前登记 owner；Object<T>.Release 归还 target。[M-E008](architecture.md#m-e008)、[M-E015](architecture.md#m-e015) | S04 局部修正 |
| F05 基线与 JSON pending map 遗漏 | 证据复现错误、销毁引用责任不全 | Git 值纠正；OnDispose 不清 m_reqMap；工具源存在不证明运行接通。[M-E001](architecture.md#m-e001)、[M-E016](architecture.md#m-e016)、[M-E017](architecture.md#m-e017) | S05 局部修正 |
| F06 旧业务/Export 兼容迁移越界 | 候选变成未授权迁移工作 | 仅借鉴阶段结果/扩展约束，基座是当前 ReleaseTools。[M-E018](architecture.md#m-e018) | S06 收窄范围 |

其他交界已在汇总中统一，不要求重新全量调查：

- S01/S02 的 UseYooAsset 分支疑问由 S06-E037 部分补足：有特定构建覆盖，不代表所有发布任务；保留最终任务/产物缺口。
- S05 的当前运行时配置缺失与 S06 的仓库上层生成器存在并不矛盾；source、output、collector、consumer 分开。缺能力不会阻塞纯研究。
- S04 的 Assets 中未找到生成实现与 S06 的 Tools 中找到源生成器并不矛盾；不根据 Editor importer disabled 单独断言 analyzer 失效。
- S01/S03/S04 的全局与局部释放不能拼成自动闭环。UI 属于 SingletonSystem，ModuleSystem 的退出受编译宏与对象顺序约束。
- S02 资源异常结论保留 S02-E039 的限制；资源设置回调错误也须检查已转移 owner，不能只看到 finally 跳过 release 就判定无人持有。
- S02/S03 都不能把旧 opening/load 队列或当前类型去重称为完整 single-flight；是否共享结果与消费者取消是另一契约。
- 原候选优先级统一为研究顺序；S05 的 P0 不自动高于当前事件/资源/窗口纠错，也不等于批准新网络系统。

## 遗漏、限制与后续最小信息

master 没有逐项重验全部 187 个证据，没有全量重走启动器所有宏/回退、全部第三方加载实现、所有业务订阅/计时器、FSM/池使用者、平台存档写入与编辑器导出器。上述范围依赖原研究，候选表明确标注条件；不因此声称零缺陷。

没有 Unity 编译/Play Mode、Player 构建、联网调用、导表/代码生成、故障注入或性能测试。具体待验证包括：实际发布任务与最终 UseYooAsset；UniTask 适配器有效宏/重载绑定；退出顺序和第三方 shutdown；生成物编译接入；窗口 hook 异常与取消后的资源终态。纯文档审查无需在此补跑这些任务。

下一轮规划最少需要：

1. 选择一个候选组及明确用户可见目标；是否需要热重启、重复启动或仅 Player 退出。
2. 定义资源/窗口的成功、失败、取消、超时、owner 销毁和 PRELOAD 常驻策略。
3. 若选构建：真实命令/菜单、目标平台、必需包和生成物、版本与日志来源。
4. 若选网络/配置/持久化：当前协议与表/存档需求；旧业务和数据不默认继承。

以上是将来冻结契约的输入，不是要求用户本轮补充，也不是自动开始下一阶段。

<a id="fixes"></a>

## SLAVE_FIX_HANDOFF

以下六份提示词可分别复制给对应现有 slave。每份只修其原研究目录，本 master 未代改原文。

### F01 / S01：区分 DLL Task、metadata 回调与异常收尾

问题与原因：S01-C002 将 DLL 和 metadata 的 null 路径放进同一种“null 回调可能持续等待”的描述，未说明 DLL 循环尾的完成更新，影响高优先失败终态候选。

证据：`E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`，LoadAssembly:78–108（await 后直接调用 LoadAssetSuccess，循环尾置完成）；LoadAssetSuccess:185–218（先减计数、null return、异常重抛、UnloadAsset 位于 finally 后）；LoadMetadataForAOTAssembly/LoadMetadataAssetSuccess:224–292（回调与完成位）；AllAssemblyLoadComplete:124–150（切换流程早于入口校验）。

修正要求：仅修改 `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/01-startup-modules-hotfix/` 中受影响文档，保留其他变更；更新 S01-E024、S01-C002 和引用它们的概述。分别说明 DLL null 正常到达循环尾、DLL await/Assembly.Load 抛错、metadata 末次 null/抛错；完成位不代表成功。记录异常可跳过 TextAsset 释放，避免把流程等待判断扩大为已复现故障。不修改生产文件，不调用 agent，不进入实施。

验证：逐条复核上述控制流和资源模块实际重载；检查目录内结论、编号与链接一致。纯文档修正无需构造包、运行 Unity 或扩展调查。

### F02 / S02：修正 PRELOAD 回收、临时资源与旧实例计数

问题与原因：S02-E033/findings 将 PRELOAD 保持归于池自动回收配置，但成功加载持有未归还的 Spawn；S02-E032 将 DLL/metadata 描述为完整临时资源释放链，遗漏异常路径；S02-C001 的旧 owner 样本还需披露条件性双减计数。

证据：CURRENT 根 `E:/MyWork/MyFramework/TEngine/UnityProject/`：Assets/GameScripts/Procedure/ProcedurePreload.cs:126–168 的 PreLoad/OnPreLoadAssetSuccess；Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:1037–1129 的回调 overload（Spawn:1067、Register true:1118）；Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.ObjectPool.cs:541–598（先排除 IsInUse，再按过期筛选）；Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs:78–108、185–292（DLL Task 与 metadata 回调、Unload 在 finally 后）。OLD 根 `D:/Work/SAUnity/ProjectOld/`：Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceMgr.cs:619–685 的 ReleaseGameObject/DestroyGameObject；同目录 ResourceRef.cs:36–48 的 Key/OnDestroy；Assets/Scripts/game/Module/Skin/Comps/SkinControlNode.cs:159–176 的真实 DestroyGameObject 调用。

修正要求：仅修改 `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/02-resources-scenes/`，保留其他变更。改正 E032/E033 与对应 findings、open-questions、候选引用：自动回收不能消除 in-use 预加载引用，常驻意图待确认；临时资源仅成功路径有配对释放。对 E014/E015/C001 补充 manager/prefab 记录存活且非重启时显式减计数后 OnDestroy 再减的静态条件风险，不能宣称运行泄漏或照抄旧实现。不改生产代码，不调用 agent，不执行新阶段。

验证：核对预加载实际 overload→Spawn/Register→成功回调→池释放筛选；核对 DLL 与 metadata 异常是否跨过 Unload；核对 Key 未失效及双减成立条件；保持 S02-E039 的异常限定与新结论一致，检查链接/编号。无需 Unity 测试。

### F03 / S03：明确 await 返回与窗口 ready 的区别

问题与原因：S03-C003 和 findings 对 ShowUIAsyncAwait 的“最多 60 秒轮询”描述不适用于已有窗口，也不是墙钟上限；IsLoadDone 不是 IsPrepare。这会直接影响异步窗口设计。

证据：`E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`，ShowUIAsyncAwait:282–285、TryGetWindow/ShowUIAwaitImp:323–364：已有窗口343–345立即返回，只有新建路径累计 Time.deltaTime。相同目录 UIWindow.cs:427–502：InternalDestroy 用户 hook 后才执行部分收尾；Handle_Completed 在 destroyed/Canvas/IsPrepare 检查之前置 IsLoadDone。OLD `D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel_CancellationToken.cs`:44–54：已取消 CTS 不进入 Dispose/null 分支。

修正要求：仅修改 `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/03-ui-framework/` 中受影响文档，保留其他变更。同步 S03-E019/E020、C002/C003 与概述，列明已有且加载中、新建等待、timeScale=0/超时、加载完成但未 prepare 四种情形；返回对象不能当成功终态。清理表对正常 hook 与异常/上游已取消分别限定，避免无条件承诺关闭已释放全部资源。不改生产代码，不调用 agent，不开始实现。

验证：沿公开 API 到分支及 Handle_Completed 的实际语句顺序核对，交叉检查所有 60 秒、ready、取消/清理表述和链接。无需 Unity 运行或新增大范围 UI 调查。

### F04 / S04：纠正资源提交顺序与池 target/wrapper 归还责任

问题与原因：findings §5.3 图将 SetAsset 放在 transfer=true 前，与代码相反；异常缺口论述未纳入 linked-list owner 和 setter 的执行顺序。§8.3 将 Object<T>.Release 的 MemoryPool.Release 对象写成 wrapper，实际为目标 _object。

证据：CURRENT 根 `E:/MyWork/MyFramework/TEngine/UnityProject/`：Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs:90–152（transfer 与 finally）；同目录 ResourceExtComponent.cs:109–157（SetAsset 先 AddLast，再调 setter；后续 IsCanRelease→Unspawn/Release）；同目录 Implement/SetSpriteObject.cs:51–86（先赋 sprite 再用户回调）；Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.Object.cs:181–186（归还 _object）；ObjectPoolModule.ObjectPool.cs:511–523（调用层归还 wrapper）。

修正要求：仅修改 `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/04-events-async-utilities/`，保留其他变更。纠正图与 §8.3 的对象名称；补足回调抛错时已存在 owner、后续条件释放的控制流，区分错误终态/销毁收尾待验证与“无 owner 泄漏”，同步 S04-E023/E028/E026 及候选中受影响表述。保留源码确证的风险，不以本轮未运行得出必然安全。不改生产文件，不调用 agent。

验证：核对 transfer、AddLast、setter、用户回调、finally 和正常回收的顺序；分别跟踪 target 与 wrapper 归还，确保图、正文和表格一致。无需运行测试或重查其他工具模块。

### F05 / S05：修正调查基线、pending 销毁与候选范围

问题与原因：README/evidence/verification 的 CURRENT Git 根和 HEAD 抄录有误；S05-E006 的“Dispose 丢弃 pending callback”遗漏 m_reqMap 未清空，不能据此作为可借鉴的清理链；配置工具来源已获跨域补证，旧 key 迁移也不是本轮目标。

证据：在 `E:/MyWork/MyFramework/TEngine/UnityProject/` 执行 git rev-parse --show-toplevel / HEAD 得 `E:/MyWork/MyFramework/TEngine` 与 `16afccb5df2a2a2efcb5003ecf9fbc0781c0170a`。OLD `D:/Work/SAUnity/ProjectOld/Assets/Scripts/game/Managers/Network/JsonNetwork.cs`:102–180、182–241，特别 OnDispose:234–241 重复清 listenerMap、未清 m_reqMap。CURRENT 工具来源是 `E:/MyWork/MyFramework/TEngine/Configs/GameConfig/gen_code_bin_to_project_lazyload.bat`:1–22 和 CustomTemplate/ConfigSystem.cs:9–56；对应 GameProto/GameConfig 与 AssetRaw/Configs/bytes 输出本轮不存在，参见 S06-E026/E027。

修正要求：仅修改 `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/05-network-config-persistence/`，保留其他变更。改正 S05-E001 及基线重复值；对 E006/C002/清理表明确 pending 未主动完成且 map 未清，引用保留风险以 JsonNetwork 仍存活为条件。将工具源存在与运行链未接通分开；PLAN_CONFLICT 如保留，应限定为未来接入所需信息，不能阻塞本次研究或自动批准 P0 实施。C004 不把迁移旧 key 当必要成本。不迁移业务、不改生产代码、不调用 agent。

验证：运行只读 git 命令，核对 OnDispose 的所有容器及 context 引用，核对工具与输出存在性、相关原编号和目录内一致性；无需网络、生成器或 Unity 测试。

### F06 / S06：收回旧 Export/业务处理器兼容迁移建议

问题与原因：candidates.md 的 S06-C001 建议把旧 [Export] 处理器挂入兼容阶段、定义旧菜单兼容层；S06-C006 要保留旧适配器并迁移旧业务处理器。此范围超过“只借鉴框架设计，不迁移业务”，也没有当前需求证明。

证据：`E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/06-editor-build-generation/candidates.md`:22–34、155–183；当前实际改良基座为 `E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs`:119–212 的 BuildWithConfig/BuildInternalWithConfig。旧业务耦合已由同目录 S06-E006/E007 记录，不是当前必需兼容项。

修正要求：仅修改上述 S06 研究目录，保留其他变更。将 C001/C006 限定为当前 ReleaseTools 的阶段结果与在确有当前扩展需求时的显式注册；删除迁移旧 handler/菜单兼容为必要步骤或成本的表述。旧 Export 只保留为风险比较，Wwise/SoData/具体业务处理器明确排除。同步受影响的 findings/README/open-questions 引用；不改生产代码、不调用 agent、不开始后续规划或实现。

验证：定向检索该目录的“迁移”“兼容”“旧处理器”表述，确认均未转为当前交付义务；保留第三方/自研/业务归属和证据链接。无需构建或执行导出器。

## 汇总验证

本轮只新增/写入：

- [研究根 README](../README.md)
- [统一架构与证据](architecture.md)
- [去重候选](candidates.md)
- [本审查记录](review.md)

最终检查通过：36/36 输入文档齐全；187 个原证据定义闭合；记录中的 60 个不同源码/配置文件 SHA256 全部匹配；19 个 M-E 定义及引用一致；37 个原候选在总表各有且仅有一个主归属；输出相对/绝对文件链接、锚点和源码起止行号范围有效。36 份原研究与 8 份 handoffs 的 SHA256 和审查前一致；最终 status 仅增加上述 4 份汇总文档，原有 boot.config 删除和 layout 未跟踪项保留，staged diff 仍为空。没有 Unity 编译或全量测试，也没有暂存、提交、恢复、清理或启动下一阶段。
