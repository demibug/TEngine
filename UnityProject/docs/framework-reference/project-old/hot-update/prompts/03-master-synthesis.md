# Master：汇总两份调查并制定移植计划

使用 `$master-planner`（`C:\Users\Administrator\.agents\skills\master-planner\SKILL.md`）。你是规划与正确性审查角色，只总结和制定计划，不实现、不自动分派代理、不创建会话、不切模型。所有后续交接均生成提示词，由用户手动开启会话执行。

## Objective
判断参考工程 `D:\Work\SAUnity\ProjectOld` 的“两步热更新”与当前工程 `E:\MyWork\MyFramework\TEngine\UnityProject` 是否不一致、各自优势与缺陷、在当前工程目标下哪种更合适，最后生成参考工程 → 当前工程的可执行移植计划。

## Required inputs
先确认用户手动开启的两位 slave 已完成下列文档；缺任何一份或用户尚未确认调查完成，列出缺失输入并停止，不自动代查，不根据旧草稿先定方案。

- `E:\MyWork\MyFramework\TEngine\UnityProject\docs\framework-reference\project-old\hot-update\reference-analysis.md`
- `E:\MyWork\MyFramework\TEngine\UnityProject\docs\framework-reference\project-old\hot-update\current-analysis.md`

## Review and synthesis
读取两份报告，按其证据索引窄范围复核关键源码。以当前工作区为准，保留已有未提交修改；核对报告快照是否已过期。事实矛盾先核验，不把两份报告简单拼接，不重新进行全库调查。

1. 给出两条真实启动链，定义“两步”的实际边界；分别说明资源下载阶段、程序集执行阶段及更新器哪些部分可热更。
2. 用表比较：实际启用模式、AOT/热更程序集、包和 Bundle 粒度、版本一致性、下载量与启动耗时的证据边界、失败终态、重试/离线/回退、成功记录、缓存、退出、构建与发布成本。
3. 回答“哪个更好”，按更新器可修复性、可靠性、复杂度及当前项目需求区分。不能因流程更多就认定更好，也不能把风险当作已运行复现。若收益不足，明确建议保留当前策略或局部移植。
4. 列出移植、保留、拒绝照搬、延后四类机制。不得预先假定必须使用双资源包、照搬旧 Launcher、增加全局服务或整体搬迁 Procedure。
5. 提出目标边界和分批计划，明确程序集依赖、资源布局、稳定入口 ABI、版本快照、基础 Player 兼容性、不可逆装载边界、失败/重试规则、成功提交和缓存所有权。
6. 确定是否需要新基础安装包、如何兼容旧发布通道、哪些变化只能在下一进程生效。保留当前启动/退出/资源生命周期修复，不承诺程序集可卸载回滚。
7. 每批有文件/符号起点、依赖顺序、验收条件、针对性测试、真 Player 验证门槛与范围限制。未知项和设计假设明确标记；仅对确实影响方案且无法从源码判断的问题向用户澄清。

## Deliverables
仅新增/更新以下规划文档，不改生产代码或工程配置：

- `E:\MyWork\MyFramework\TEngine\UnityProject\docs\framework-reference\project-old\hot-update\comparison.md`：证据支持的差异、优劣与推荐。
- `E:\MyWork\MyFramework\TEngine\UnityProject\docs\framework-reference\project-old\hot-update\migration-plan.md`：从参考机制到当前工程的分批移植计划。
- 计划稳定后，在同目录 prompts 下生成后续实施及独立正确性审查提示词。按 master-planner 包含 Role、Objective、Existing architecture、Implementation contract、Relevant files/symbols、Implementation steps、Constraints、Acceptance criteria、Verification、Handling unexpected repository reality、Completion requirements 和 Required master review handoff。实施提示词必须自包含关键冻结决策；重大矛盾要求输出 PLAN_CONFLICT，不允许 slave 私自扩大范围。

中文交付。最终向用户简要总结结论、理由及文档路径，提供可复制的 SLAVE_IMPLEMENTATION_HANDOFF，然后停止。由用户决定何时手动交给实施 slave；本次不得自动实施或发布。
