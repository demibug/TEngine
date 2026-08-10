---
name: tengine-dev
description: TEngine Unity 框架代码任务的按需 reference 路由、源码核验与会话缓存指导。涉及 TEngine、UIWindow、UIWidget、GameEvent、AddUIEvent、LoadAssetAsync、SetSprite、HybridCLR、YooAsset、Luban、GameModule、热更、资源、UI、事件或配置表时使用。
---

# TEngine 开发指导

TEngine 是基于 HybridCLR + YooAsset + UniTask + Luban 的 Unity 游戏框架。
本 skill 提供 AI 专用的精炼参考文档。reference 是按需索引，不是需要预读的知识库；当前源码始终优先于 reference。

## 核心红线

1. **异步优先**：IO 操作用 `UniTask`，禁止同步加载/Coroutine
2. **模块访问**：通过 `GameModule.XXX` 访问，而非 `ModuleSystem.GetModule<T>()`
3. **资源必须释放**：`LoadAssetAsync` 对应 `UnloadAsset`，GameObject 用 `LoadGameObjectAsync`
4. **热更边界**：`GameScripts/Main` 不热更，`GameScripts/HotFix/` 全部热更
5. **事件解耦**：模块间用 `GameEvent`，UI 内部用 `AddUIEvent`

## 最小读取规则

1. 读取 reference 前，先列出任务等级、计划读取的文档、目标章节或源码符号，以及每项必要性。
2. L2 只读取 1 篇最相关主文档的命中章节及必要上下文；仅当存在明确的进阶缺口时，才读取补充文档并说明缺口。
3. L3 只读取直接影响当前改动的文档和命中章节，不得因同属一个领域而预读全部 reference。
4. L4 先按主题拆分，每个主题分别遵循 L2/L3 的最小读取范围，不得在拆分前批量读取 reference。
5. 用 Grep/`rg` 先缩小源码范围。命中超过 50 条时，先按目录、文件类型或关键词继续收窄，不得直接截断并据此下结论；只保留解决当前问题所需的命中和上下文。
6. 仅在无法定位所需章节时读取整篇 reference，并在交付中说明原因；不得为“可能有用”读取文档。

## 文档路由

根据任务类型选择 reference，不要读取未命中的文档：

| 任务类型 | 主文档 | 补充文档（仅命中进阶问题时） | 优先级 |
|---------|---------|---------|--------|
| UI 开发 | [ui-lifecycle.md](references/ui-lifecycle.md) | [ui-patterns.md](references/ui-patterns.md) | P0 |
| 事件系统 | [event-system.md](references/event-system.md) | [event-antipatterns.md](references/event-antipatterns.md) | P0 |
| 资源加载 | [resource-api.md](references/resource-api.md) | [resource-patterns.md](references/resource-patterns.md) | P0 |
| 模块使用 | [modules.md](references/modules.md) | — | P0 |
| 热更代码 | [hotfix-workflow.md](references/hotfix-workflow.md) | — | P1 |
| 代码规范 | [naming-rules.md](references/naming-rules.md) | — | P1 |
| Luban 配置 | [luban-config.md](references/luban-config.md) | — | P1 |
| 项目结构 | [architecture.md](references/architecture.md) | — | P2 |
| 问题排查 | [troubleshooting.md](references/troubleshooting.md) | — | P2 |
| MCP 场景/GO/UI/脚本/Editor | [mcp-tools.md](references/mcp-tools.md) | — | P1 |
| MCP 材质/Shader/动画/VFX | [mcp-visual.md](references/mcp-visual.md) | — | P2 |

## 源码核验、缓存与交付

- 读取 reference 后，用 Grep/`rg` 核对当前源码的实际声明、调用链、生命周期和释放点；冲突时以源码为准并说明差异。
- 同一会话缓存“结论 + 证据路径/符号”，不缓存大段正文或原始检索输出。相关源码变化后只重查受影响符号和调用链；除非 reference 已修改、缓存无法覆盖当前问题或发现文档冲突，否则不重读 reference。新会话不继承缓存。
- L2 修改前简要说明领域与入口、调用链、方案、风险和验证；L3/L4 完整说明领域与入口、调用链、数据流、生命周期、方案、文件与位置、风险和验证。
