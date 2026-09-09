# Slave B：当前 TEngine 热更新策略调查

## Role
使用 `$slave-executor`（`C:\Users\Administrator\.agents\skills\slave-executor\SKILL.md`）的单代理工作方式，但本任务只调查并编写文档，不实现代码。本提示词的调查范围优先于技能的默认实施流程。不得自动分派代理、创建会话、切模型或调用 master。遵守适用 AGENTS.md/AGENT.md/CLAUDE.md，使用 tengine-dev 技能但以当前工作区源码为准。

## Objective
核查 `E:\MyWork\MyFramework\TEngine\UnityProject` 的真实热更新策略，为与参考工程两步热更新比较提供独立证据。

## Investigation contract
1. 从 GameEntry、ProcedureSetting 实际启用配置追踪资源初始化、版本与 manifest 获取、下载、预加载、AOT metadata、程序集加载、GameApp 启动。
2. 核查 asmdef、HybridCLR/UpdateSetting、YooAsset 采集与构建发布脚本，明确更新代码是否可热更、包结构、DLL 与内容更新先后关系。
3. 调查版本一致性、失败重试、离线/弱网、强更、缓存清理、退出/取消、重复进入、程序集不可卸载、编辑器与 Player 差异。
4. 当前工作区已有用户未提交修改，包含 startup/shutdown/resource lifecycle 修复。以工作区版本分析，识别并明确未来移植必须保留的终态、generation、句柄释放等约束；可以定向阅读现有相关文档和测试。
5. 每项重要结论提供绝对路径、行号、符号。区分实际启用路径、可选能力、推断与未验证事项。列出改成可热更更新器的具体接缝及风险，但不要直接设计或实现整个迁移。
6. 将“代码支持联机热更新”与“按当前配置出 Player 实际启用热更新”分别判断；联合核查平台宏、HybridCLR 开关、PlayMode、场景/Prefab 覆盖、远端地址。指出可能由构建覆盖但尚未证实的配置。
7. 核查 DLL 的 Bundle 打包粒度、标签、metadata 与业务 DLL 地址规则；说明只新增启动标签是否足以独立下载更新器。识别程序集访问边界及业务入口传参的不变项。

## Constraints
只读当前工程源码及配置。不要查看参考工程或参考工程调查结论，以维持调查独立性。不修改其他已有文件或其他人的文档。若自己的目标报告已存在，仅视作未经审查的草稿，独立核验后允许重写。不运行 Unity/构建，不联网；调查本地固定版本源码。避免无关全库遍历。

## Output
仅创建 `E:\MyWork\MyFramework\TEngine\UnityProject\docs\framework-reference\project-old\hot-update\current-analysis.md`。中文撰写，包含摘要、真实流程图、程序集/资源包关系、证据索引、发布与失败语义、优缺点、未来迁移必须保留的约束、未验证事项。记录关键文件 SHA256 或快照信息，明确未做真机运行验证。完成后回报最重要结论及文档绝对路径，不输出实施变更。

## Acceptance criteria / Completion
报告应明确既有修复与仍存在的风险，不重复引用过期研究作为当前事实。每个重大问题都有源码证据和触发条件；测试源码存在不等于测试通过，历史测试结果不等于本轮重新验证。不评价两工程孰优，不提前冻结移植方案。缺失证据写入未知项；如路径不可读，说明精确阻碍，不编造结论。完成后停止，将报告路径交给用户手动转交 master，不自动进入下一阶段。
