# UnityProject Codex 主配置

本文件是 Codex 处理 `UnityProject` 内任务时的主配置。

## 通用项目规范

项目业务技能统一从 `.agents/skills/` 读取，`agent-worker` 和 `task-slice` 使用用户级公共 Skill，不在仓库重复维护。

### 项目定位与设计原则

本项目是单机版小游戏，默认采用满足当前明确需求的最小、直接设计，优先复用现有实现和简单调用链，避免为未确认的未来需求提前增加抽象层、通用框架、复杂状态机或扩展机制。战斗部分尤其不得过度设计；除非当前需求或已有架构明确要求，不引入联机同步、服务器权威、预测回滚、复杂 ECS、通用技能编排框架等面向大型或联网战斗的方案。方案存在多种可行实现时，优先选择代码量少、依赖少、数据流清晰且容易验证和维护的方案。

除非必要，业务对象先存入具名局部变量，再作为方法实参。

除非必要，方法结果先存入具名局部变量，再返回。

### Windows Python 执行

Codex 在 Windows 下执行任何 Python 命令前，必须先通过工作区依赖运行时查询获取捆绑 Python 绝对路径，并使用该路径执行。禁止使用裸 `python` / `python3` / `py`，因为它们可能解析到沙箱无法启动的 WindowsApps 别名。只读 Python 检查必须优先在普通沙箱内执行，不得仅因 Python 启动路径问题申请 `require_escalated`。

## Codex 项目技能

当任务匹配以下条件时，必须在执行任务前读取对应技能目录中的完整 `SKILL.md`，并按照其中的路由继续读取所需的 `references/`、脚本或模板：

| 任务类型 | 必须使用的技能 |
|---|---|
| TEngine 框架 API、UI、事件、资源、模块、热更、代码规范、项目结构或排障 | `.agents/skills/tengine-dev/SKILL.md` |
| Luban 配置表、Schema、代码生成或数据导入 | `.agents/skills/luban-dev/SKILL.md` |
| HTML 转 Unity UGUI | `.agents/skills/html-to-ugui/SKILL.md` |
| OpenSpec 探索、提案、更新、应用、同步或归档 | 对应的 `.agents/skills/openspec-*/SKILL.md` |
| 代码库架构分析或改进 | `.agents/skills/improve-codebase-architecture/SKILL.md` |
| 需要先审问需求或通过文档审查方案 | `grill-me` 或 `grill-with-docs` |

## 技能使用规则

1. L1 仅限不涉及框架 API、UI 节点、事件、资源、生命周期、模块或静态状态的 typo、注释、日志和单行变量改名；典型 L2+ 标志包括 `UIWindow`/`UIWidget`、`GameEvent`/`AddUIEvent`、`LoadAssetAsync`/`UnloadAsset`、`GameModule`、HybridCLR/YooAsset 和资源路径。不确定时上调到 L2。
2. L2（调用）、L3（功能）和 L4（架构）任务必须先使用 `tengine-dev`；reference 选择、章节级读取、源码核验和会话缓存仅以该 skill 为准，触发 skill 不等于读取全部 reference。
3. L2 修改代码前简要说明领域与入口、调用链、方案、风险和验证；L3/L4 完整说明领域与入口、调用链、数据流、生命周期、方案、文件与位置、风险和验证。
4. 一个任务匹配多个技能时，先读取领域技能，再读取通用流程技能。只把包含标准 `SKILL.md` 的目录视为可用技能，不把 `.claude/skills/` 当作 Codex 技能入口。
5. `SKILL.md` 与实际代码 API 冲突时，以搜索确认的当前源码为准并说明冲突。输出代码前必须遵守已读取技能，不能只读取技能名称。

## 委派策略

- L1 小型确定任务由主 Codex 直接完成；单个 L2+ 有界任务使用用户级 `agent-worker`。
- 已确认包含多个独立交付目标、依赖波次或写冲突，或用户明确要求拆分 / 并行时，使用用户级 `task-slice`。
- 本项目不使用 Codex 原生子智能体（subagent / `spawn_agent`）承担代码劳动。
- C 盘写入按用户级 `agent-worker` 的外层审批流程执行；其他盘在已配置 writable roots 内的非工作区写入可正常委派。
- backend、WriteSet、Decision Boundary、结果协议、fallback 和验收流程以对应公共 Skill 为准，`AGENTS.md` 不重复维护。
