# ProjectOld 框架参考研究

审查日期：2026-09-07。目标是从只读参考工程 `D:/Work/SAUnity/ProjectOld/` 提取值得借鉴的框架设计，与当前 TEngine 的源码和实际入口比较；不迁移旧业务，不预设旧框架更优。

当前工程为 `E:/MyWork/MyFramework/TEngine/UnityProject/`，Git 仓库根为 `E:/MyWork/MyFramework/TEngine/`。本轮仅撰写本 README 与 synthesis 文档，未修改六份原研究、handoffs 或生产文件。

## 整体状态

**六份输入完整，统一汇总已完成，正确性审查待修正；不输出整体 REVIEW_PASS。** 已读取 36 份输入及 README 的文档导航，抽查关键源码、实际调用和配置。发现热更失败路径、预加载引用、UI await、资源设置所有权、JSON 销毁及候选迁移范围等问题；汇总已给出源码裁决，原研究需由各自作者修正。

已检查 187 个原证据编号的定义、37 个原候选的覆盖、输入相对链接及记录中的 60 个不同源码文件哈希；哈希全部匹配。此结果证明定位和基线可复查，不等于 187 项结论均经 master 重新核验。具体范围、未决问题和修复提示词见审查记录。

## 六份独立研究

| 任务 | 研究入口 | 输入状态 | master 审查状态 |
| --- | --- | --- | --- |
| S01 | [启动、模块组织与热更边界](01-startup-modules-hotfix/README.md) | 6/6 文档齐全 | 需要局部修正，见 review.md |
| S02 | [资源与场景生命周期](02-resources-scenes/README.md) | 6/6 文档齐全 | 需要局部修正，见 review.md |
| S03 | [UI 框架与表现层组织](03-ui-framework/README.md) | 6/6 文档齐全 | 需要局部修正，见 review.md |
| S04 | [事件、异步与通用运行时机制](04-events-async-utilities/README.md) | 6/6 文档齐全 | 需要局部修正，见 review.md |
| S05 | [网络、配置与持久化抽象](05-network-config-persistence/README.md) | 6/6 文档齐全 | 需要局部修正，见 review.md |
| S06 | [编辑器工具、构建与代码生成](06-editor-build-generation/README.md) | 6/6 文档齐全 | 需要局部修正，见 review.md |

每个目录的 README 导航到 findings、evidence、candidates、open-questions、verification。原 S01–S06 编号保留，master 新增证据使用 M-E001 起编号；原研究中的错误不会因被引用而获得确认。

## 汇总与后续阅读

- [第 3 批资源生命周期执行契约](synthesis/resource-lifecycle-plan.md)：用户确认前批完成后形成的下一步手动实施提示词；本轮只规划，未实施资源改造，也未补作前批验收。

- [architecture.md](synthesis/architecture.md)：统一框架地图、生命周期、第三方归属及 master 源码核验定位。
- [candidates.md](synthesis/candidates.md)：37 个原候选去重后的设计清单，当前等价能力、代价、依赖、风险和建议研究顺序。
- [review.md](synthesis/review.md)：基线、实际抽查、冲突裁决、修复提示词与验证结果。

建议先读 architecture 的边界和生命周期，再读 review 的实质问题，最后按 candidates 选择下一轮规划主题。优先考虑现有事件分发的异常恢复、资源与窗口的异步终态和所有权、构建结果及目标一致性；这些只是进一步规划建议。

本轮没有运行游戏、构建 Player、调用网络或执行生成器，也没有性能测量。之后须基于修正后的研究另行确定行为、接口、失败/取消、兼容和验收契约；本轮不批准实施，也不自动进入下一阶段。

## 后续规划：FairyGUI 与 UGUI 并存

2026-09-07 用户明确提出为通用工程接入 FairyGUI，并选择“两套并存，新增界面可用 FairyGUI”。已形成 [FGUI 接入计划与完整手动实施交接](synthesis/fgui-integration-plan.md)，列出代码清单、资源/窗口/输入/生成契约与验收。该新需求更新了此前无 FGUI 需求时的候选取舍；不修改原研究事实，也不迁移旧业务或替换现有 UGUI。本次只完成规划，尚未导入 SDK 或实施生产改造。

## 下一批执行计划：事件分发可靠性

状态续记：用户确认事件批次完成，[实施及 master 复核记录](synthesis/event-dispatch-reliability-verification.md)记录 36/36 EditMode 通过；下一步为[第 2 批：构建结果可信执行契约](synthesis/build-reliability-plan.md)。本轮只规划，尚未实施或验收构建批次。

后续状态更新：用户已确认 FGUI 接入完成，当前工作树也已有相关实现；上段“尚未导入”仅描述先前规划轮，不代表当前实现状态。本轮没有对 FGUI 实施作完整验收。

按“通用框架可靠性与长期维护”目标，下一批为 [事件分发可靠性执行契约](synthesis/event-dispatch-reliability-plan.md)。已冻结异常传播、嵌套分发、增删监听的可见时机及测试边界，供用户手动交给 slave 实施。本轮只撰写计划，未修改事件生产代码；后续依次考虑构建、资源、UI 与启动退出协调，每批独立验收。
