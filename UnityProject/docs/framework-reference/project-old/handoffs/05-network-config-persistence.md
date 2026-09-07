# Slave 05：网络、配置与持久化抽象

## Role
你是独立 slave executor。用户已授权你完成下述源码调查并将文档写入当前工程。使用 $slave-executor 技能；遵守适用的 AGENTS.md、AGENT.md、CLAUDE.md，按需使用 tengine-dev 相关规范。全程单 agent，不调用 master-planner、不启动子 agent、不切换模型。本任务与其他 slave 并行，但无需等待或读取其他任务的结果。

## Objective
以参考工程真实代码为依据，研究“网络、配置与持久化抽象”，沉淀可以独立复查的框架研究文档，为之后选择性复刻优秀设计提供依据。本轮只调查和写文档，不实现改造，不迁移任何具体游戏功能。

参考工程（只读）：`D:\Work\SAUnity\ProjectOld`
当前工程：`E:\MyWork\MyFramework\TEngine\UnityProject`
唯一允许写入的目录：`E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/05-network-config-persistence`
本提示词本身不需要修改。

## Existing architecture
当前工程存在 Assets/TEngine/Runtime、Assets/TEngine/Editor、Assets/GameScripts/GameEntry.cs、Procedure、HotFix，规范入口为 AGENT.md → CLAUDE.md。
参考工程存在 Assets/Scripts/framework、Launcher、HotUpdate、UIBase、Assets/SubSystem/ResLoadCtrl、ILScriptProject/Core、LocalPackages 等目录。
以上只是已观察到的目录，不代表已确认的运行时职责。tengine-dev 文档可作导航，但实际代码与项目配置优先。不要根据目录名或历史文档推断当前可达架构。

## Implementation contract
### 本任务范围
负责网络连接/传输抽象、协议编解码与分发、请求响应关联、超时/取消/重连策略，以及配置加载/访问与通用持久化接口。
重点回答：连接和请求由谁拥有；断线时未完成请求怎样结束；协议与业务处理如何分离；配置是生成还是反射/手写访问；数据加载失败与版本不兼容怎样处理；持久化有哪些实际一致性保障。
仅调查实际存在的框架机制，不预设旧工程使用某个网络库或配置方案。不迁移协议定义、业务消息、游戏配置数据、存档字段或业务缓存。
至少追踪一个网络请求或消息的基础设施路径，以及一个配置或持久化读写路径；缺失时说明搜索范围。生成工具内部交给 06；资源字节读取内部交给 02。
不记录账号、密钥、令牌、真实服务地址或用户数据；引用配置字段名与机制即可。

### 统一证据格式
- 所有文档使用中文。每项关键架构结论及候选建议引用证据编号 `S05-E001` 等。
- 每条证据记录：所属工程（OLD/CURRENT）、相对路径、类/方法/配置字段、核验时行号、支持的具体结论、定义/调用/配置证据的性质。README 记录两个工程绝对根路径。
- 清楚区分“代码确认”“推断”“待验证”。证明定义存在不等于证明正在使用；沿调用、程序集、条件编译与场景/启动配置检查可达性，无法证明时注明。
- 区分自研代码、第三方原始能力、适配层、生成代码和业务调用方。
- 每个重点机制说明职责、接口、依赖方向、关键状态、生命周期/所有权、失败与清理路径、扩展点、局限。没有实现的保障不得写成已存在。
- 候选设计必须回答解决什么问题、当前工程是否已有等价能力、收益与代价、适用条件及适配冲突。允许“保留当前实现”“借鉴局部机制”“进一步验证”“不建议引入”。没有测量，不声称性能更好。
- 冻结的是本次调查范围和证据契约，不是未来生产 API。候选建议均未批准实施。

## Relevant files / symbols
参考工程下的调查入口（存在性与可达性仍须核验）：
- `D:\Work\SAUnity\ProjectOld\Assets\Scripts\framework`
- `D:\Work\SAUnity\ProjectOld\Assets\Scripts\im`
- `D:\Work\SAUnity\ProjectOld\ILScriptProject\Core`
- `D:\Work\SAUnity\ProjectOld\Assets\Config`
- `D:\Work\SAUnity\ProjectOld\LocalPackages`
- `D:\Work\SAUnity\ProjectOld\Packages\manifest.json`

当前工程的按需对照入口：
- `E:\MyWork\MyFramework\TEngine\UnityProject\Assets\TEngine\Runtime`
- `E:\MyWork\MyFramework\TEngine\UnityProject\Assets\GameScripts\HotFix`
- `E:\MyWork\MyFramework\TEngine\UnityProject\Packages\manifest.json`
不预设具体类名。根据真实引用继续调查，避免全库逐文件阅读。

## Implementation steps
1. 检查适用说明和当前 git status，记录已有变更。参考工程路径不可读时先报告，不改用其他工程。
2. 记录调查日期、两个工程根路径、Unity 版本和可取得的 Git/SVN 修订信息。若参考工程没有版本信息，记录调查时间与本任务关键证据文件 SHA-256，不执行更新操作。
3. 先建立本领域机制清单，再沿代表性入口追踪实现、调用者和配置，覆盖上述重点问题。分别标记网络、配置、持久化的调查状态。发现与当前 Luban 集成有关的内容时按仓库要求读取 luban-dev 技能，只研究架构，不修改配置或导表。
4. 仅围绕发现的候选设计，读取当前 TEngine 对应源码做适用性比较。不重复全量介绍当前工程。
5. 在唯一允许目录内写入下列文件：
   - README.md：目标、边界、调查基线、阅读顺序、覆盖清单（已调查/未找到/待确认）。
   - findings.md：深入机制分析与真实调用链；需要时使用 Mermaid 图。篇幅过大可拆分 mechanisms/*.md，并更新本目录 README。
   - evidence.md：带编号的证据索引、必要短摘录和复查定位信息，不粘贴整份源码。
   - candidates.md：候选编号 S05-C001 等；记录设计、证据、当前等价能力、收益、代价、约束、跨领域依赖和建议优先级。不要为凑数量提出候选。
   - open-questions.md：缺失证据、查过的位置、推断、下一步验证方法、是否阻塞后续决策。
   - verification.md：实际完成的定位/链接/范围检查、结果，以及运行验证局限。
6. 核验文档和修改范围，完成交接。不要开始生产代码改造。

## Constraints
- 参考工程只读；当前工程只能写本任务目录。不得修改共享索引、其他 slave 文档、提示词、源码、资源、项目设置、包依赖或已有规范。
- 不复制业务逻辑、素材、游戏配置、协议数据、第三方包或整份源码；业务调用仅用于验证框架使用。
- 不执行旧工程脚本，不编译、打包、导表、生成代码、升级依赖。不扫描 Library、Temp、Logs 等缓存产物。
- 不提交、不暂存、不恢复或清理其他变更。此前观察到 ProjectSettings/boot.config 删除和 UserSettings/Layouts/ 未跟踪；执行时重新核对并保留。
- 其他 slave 可能同时新增文件；只审核自己目录的内容和差异，不把别人的变更当作异常处理。
- 可以读取跨领域边界代码验证本领域结论，但不要完整分析或写入其他领域；在 open-questions.md 记录需汇总协调的问题，无需等待其他任务。
- 不修改知识库；遇到旧文档与源码冲突，在本任务文档中记录。
- 静态分析不能证明的运行时行为明确注明，不伪称测试通过。

## Acceptance criteria
- 文档已实际落盘，单独阅读不依赖聊天历史或其他 slave 的输出。
- 本领域重点机制覆盖情况明确，关键结论有实现及调用/配置证据。
- 生命周期、所有权和错误路径得到分析，缺失证据公开列出。
- 候选有当前工程代码对照，区分收益、代价、适配条件，不把旧工程默认视为更优。
- 没有业务迁移或生产代码变更。

## Verification
逐项核验关键结论和优先候选的证据文件、符号、行号；检查本目录 Markdown 相对链接、证据编号和图文一致性。
使用 git status、限定本任务目录的 git diff，并直接检查新增未跟踪文档（git diff 不会显示其正文），确认自己只写授权目录。
本任务不需要 Unity 编译或全量测试。记录实际检查，未运行的检查不标记成功。

## Handling unexpected repository reality
小范围路径或命名差异自行适配并记录。重要源码缺失、参考工程不可访问，或核心机制只能看到无法取得实现的二进制导致目标无法满足时，保存已确认结果并输出 `## PLAN_CONFLICT`：简述证据、影响和需重新决定的问题。不委派，不虚构。

## Completion requirements
留下全部文档，完成针对性核验，简要列出文档绝对路径、核心发现、未决问题和检查结果。不粘贴大段文档或巨大 diff。
最后必须输出以下可交给新 master 会话的完整提示词。

## Required master review handoff
以 `## MASTER_REVIEW_HANDOFF` 开始，包含：
- Role：最终正确性审查者；直接读取当前共享工作区、本任务文档和关键原始源码，先检查状态、限定范围的差异与新增文件，不要求用户粘贴全量 diff，不重新实现。
- Original objective：研究 网络、配置与持久化抽象 的实际框架机制，为后续选择性复刻提供依据，不迁移业务。
- Important implementation contract：OLD=D:\Work\SAUnity\ProjectOld；CURRENT=E:\MyWork\MyFramework\TEngine\UnityProject；仅写 E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/05-network-config-persistence；事实与推断分开，关键结论有证据，建议需对照当前框架。
- Implementation summary：实际调查范围和核心发现。
- Changed files：本任务全部变更文件绝对路径，排除其他 slave 的变更。
- Tests/checks already run：实际检查与结果。
- Known deviations or concerns：偏差、证据缺口、运行验证局限和跨领域待协调项；没有则写无。
- Review priorities：错误调用链、可达性、第三方归属、所有权/清理遗漏、无依据优劣判断、业务越界，以及候选适配依据。按结论抽查证据，不重复全量调查。
- Required review output：合格输出 `## REVIEW_PASS`；需修正输出 `## SLAVE_FIX_HANDOFF`，作为发回本 slave 会话的完整最小提示词，包含具体问题、原因、证据位置、所需修正和验证。不启动 agent，不进入框架改造。
