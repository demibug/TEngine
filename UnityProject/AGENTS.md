# UnityProject Codex 主配置

本文件是 Codex 处理 `UnityProject` 内任务时的主配置。

## 通用项目规范

开始任务前，先读取并遵守本目录 `CLAUDE.md` 中的通用项目规范。

`CLAUDE.md` 保持原样以兼容 Claude Code。其中涉及 Claude Code 专用工具、Skill 调用方式或 `.claude/skills/` 的内容不适用于 Codex；Codex 技能统一从 `.codex/skills/` 读取。

ZCode 会话同样以 `.codex/skills/` 为本仓库技能的权威来源：仓库未使用 `.zcode/skills/`，ZCode 不会通过原生自动发现加载这些技能，应按上表手动读取对应 `SKILL.md`。

## Codex 项目技能

当任务匹配以下条件时，必须在执行任务前读取对应技能目录中的完整 `SKILL.md`，并按照其中的路由继续读取所需的 `references/`、脚本或模板：

| 任务类型 | 必须使用的技能 |
|---|---|
| TEngine 框架 API、UI、事件、资源、模块、热更、代码规范、项目结构或排障 | `.codex/skills/tengine-dev/SKILL.md` |
| Luban 配置表、Schema、代码生成或数据导入 | `.codex/skills/luban-dev/SKILL.md` |
| HTML 转 Unity UGUI | `.codex/skills/html-to-ugui/SKILL.md` |
| OpenSpec 探索、提案、更新、应用、同步或归档 | 对应的 `.codex/skills/openspec-*/SKILL.md` |
| 代码库架构分析或改进 | `.codex/skills/improve-codebase-architecture/SKILL.md` |
| 需要先审问需求或通过文档审查方案 | `grill-me` 或 `grill-with-docs` |

## 技能使用规则

1. L2（调用）、L3（功能）和 L4（架构）任务必须先使用 `tengine-dev`；L1 简单文字或注释修改可跳过。
2. 一个任务匹配多个技能时，先读取领域技能，再读取通用流程技能。
3. 只能把包含标准 `SKILL.md` 的技能目录视为可用技能；不要把 `.claude/skills/` 当作 Codex 技能入口。
4. `SKILL.md` 与实际代码 API 冲突时，先搜索代码验证实际签名，并在回答中说明冲突。
5. 输出代码前，必须遵守已读取技能中的规范；不能只读取技能名称而不读取内容。

## Codex 多代理联动规则

仅 Codex 在处理 L4 任务，或任务存在明显的并行调查、上下文隔离、专业分工、复杂日志分析、跨模块取证或不重叠文件批量处理价值时，触发全局 `dev-swarm` Skill，由 Main Agent 按决策类型、问题边界、系统职责和影响范围选择 `luna_explorer`、`luna_tester`、`luna_triage`、`luna_worker` 或 `terra_engineer`。不得仅因任务涉及 Bug、测试、修改、代码、修复、实现或 Unity 就启动多代理；小型单步任务由 Main Agent 直接完成。

`dev-swarm` 只负责 Codex 的代理编排和职责路由，不能替代本项目的 `tengine-dev` 规范闸。所有 L2+ 代码任务，无论由 Main Agent、`luna_worker` 还是 `terra_engineer` 执行，修改代码前都必须先触发 `tengine-dev`，读取其路由的相关 references，并以当前源码为准完成搜索证据和修改前清单。只读调查、日志分类和单纯测试执行不因 `dev-swarm` 自动获得产品代码修改权限。

所有 Codex subagent 默认禁止继续创建自己的 subagent。并行写入仅允许在文件 ownership 完全不重叠时进行；无法可靠隔离时必须串行，或交给单一 Agent 统一处理。最终方案、Review、Validation 与 Acceptance 始终由 Main Agent 负责。本规则为 Codex 特化规则，不要求同步到 `CLAUDE.md` 或 `.claude/` 下的任何文件。
