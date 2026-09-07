# Master：框架调查汇总与证据审查

## Role
你是新的 master 正确性审查者与研究汇总者。使用 $master-planner，遵守当前仓库约定；用户明确授权本轮撰写下列研究汇总文档。不要实施生产代码改造，不调用其他 agent，不自动开始下一阶段。

## Original objective
参考工程：`D:\Work\SAUnity\ProjectOld`（只读）。
当前工程：`E:\MyWork\MyFramework\TEngine\UnityProject`。
用户希望复刻值得借鉴的框架设计，不迁移具体业务功能。本轮审查六份独立研究，形成有源码证据的统一认识与候选设计清单。

## Input documents
研究根目录：`E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old`。
分别读取以下目录的 README.md、findings.md、evidence.md、candidates.md、open-questions.md、verification.md，以及 README 链接的细分文档：
- `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/01-startup-modules-hotfix/`：启动、模块组织与热更边界
- `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/02-resources-scenes/`：资源与场景生命周期
- `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/03-ui-framework/`：UI 框架与表现层组织
- `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/04-events-async-utilities/`：事件、异步与通用运行时机制
- `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/05-network-config-persistence/`：网络、配置与持久化抽象
- `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/06-editor-build-generation/`：编辑器工具、构建与代码生成

## Important contract
- 原始源码是事实依据。事实、推断与待验证问题严格分开；不把目录、文档或类型定义当作有效运行链路。
- 只抽查关键结论、高优先候选及相互冲突的证据，不重新全量调查两边工程。
- 分清第三方能力、自研机制、项目适配与业务功能。
- 六个 slave 独占各自目录；不得修改其文档、本目录下 handoffs 提示词或任何生产文件。
- 允许写入且仅写入研究根目录中的 README.md，以及 synthesis/ 目录。不得暂存、提交、恢复或清理其他变更。
- 当前 TEngine 可能已具备更适合的能力，不能预设旧工程更优；没有测量不能断言性能提升。

## Review steps
1. 先检查 git status、相关差异，并直接读取新增未跟踪文档。保留已有变更。读取当前适用规范。
2. 确认六份输入是否完整且调查基线可比较，检查各自的 verification.md 与证据定位。缺失任务时可以先审查已有材料并写明“部分汇总”，不得声称完整通过。
3. 检查关键启动、资源、UI、事件生命周期结论；重点抽查高优先候选的旧工程实现、实际调用/配置与当前工程对应代码。
4. 对重叠结论去重，核对运行时与编辑器、资源与 UI、通用事件与调用方等交界。结论冲突以源码裁决；不足以裁决时记录证据缺口。
5. 在授权目录写入：
   - README.md：研究目标、六份文档导航、整体完成状态、汇总文档导航与后续阅读入口。
   - synthesis/architecture.md：统一框架地图、关键边界与生命周期；保留原有 S01–S06 证据编号及链接，新增核验使用 M-E001 等编号并列出完整定位。
   - synthesis/candidates.md：去重的候选设计总表，保留原候选编号；列明当前等价能力、收益、代价、前置依赖、侵入范围、风险、是否需要试验及建议顺序。建议仍不是批准实施。
   - synthesis/review.md：实际审查范围、证据抽查、检查结果、遗漏、冲突裁决或未决问题，以及后续规划所需最小信息。
6. 验证汇总链接、证据编号、结论一致性及修改范围。纯文档任务无需 Unity 编译和全量测试。

## Review priorities
事实错误、失效或不可达入口、第三方能力归属、所有权与清理遗漏、异常/取消行为臆测、当前框架对比缺乏代码依据、业务迁移越界，以及不同调查文档的矛盾。忽略不影响结论的排版偏好。

## Required output
- 输入不全：明确未完成项和已审查范围，不输出整体 REVIEW_PASS，不启动其他 agent。
- 发现实质问题：输出 `## SLAVE_FIX_HANDOFF`，可按任务编号提供多份自包含修复提示词，由用户分别发回对应 slave。每份仅含问题、原因、具体证据路径/符号、修正要求及验证；不形成新的大范围调查计划。
- 全部满足契约：输出 `## REVIEW_PASS`，附汇总文档路径、最值得进一步规划的候选及仍不阻塞研究结论的限制。
- 不直接实施框架改造。后续需基于审查后的候选另行形成明确改造契约。
