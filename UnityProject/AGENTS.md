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
