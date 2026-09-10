# AGENTS.md

请使用中文编写提案、说明和回答。

本文件为 Codex 等代码代理提供处理本代码库的协作与开发指导。

TEngine 基于 HybridCLR、YooAsset、UniTask 和 Luban 构建。

---

## 强制工作流

### 第零步：判断任务等级

执行操作前先判断任务等级：

| 等级 | 判断标准 | 知识查询策略 |
|------|----------|--------------|
| **L1 简单** | typo、注释、日志、单行变量改名；不涉及框架 API、UI 节点前缀、事件定义或资源路径 | 跳过查询，直接修改 |
| **L2 调用** | 调用已知 API、单一模块的局部修改 | 使用 `tengine-dev` 技能查询相关主题 |
| **L3 功能** | 新功能、跨文件修改、新增 UI/资源/事件逻辑 | 使用 `tengine-dev` 技能查询全部相关主题 |
| **L4 架构** | 模块设计、系统重构、多模块协作、架构决策 | 使用 `tengine-dev` 技能并行查询多个相关主题 |

不确定时上调一级，不要低估任务复杂度。

### 第一步：按等级获取规范

L1 任务可直接进入实现阶段。L2-L4 任务必须先使用 `tengine-dev` 技能。

知识源优先使用：

- `.claude/skills/tengine-dev/references/`：项目中的精炼参考文档；
- `.codex/skills/tengine-dev/SKILL.md` 及其引用资源：当前 Codex 的技能说明；
- 当文档和实际代码不一致时，以实际代码为准。

同一会话中已经查询过的主题可以直接复用，不必重复读取；只有涉及新主题时才补充查询。

按场景查询以下主题：

| 场景 | 必查主题 |
|------|----------|
| UI 开发 | `ui-lifecycle.md`：UIWindow 生命周期、UIWidget 规范 |
| 资源加载 | `resource-api.md`：`LoadAssetAsync` API、释放时机 |
| 热更代码 | `hotfix-workflow.md`：程序集划分、GameApp 入口、热更边界 |
| 事件系统 | `event-system.md`：GameEvent、`AddUIEvent` |
| 模块使用 | `modules.md`：`GameModule.XXX` API、模块生命周期 |
| Luban 配置 | `luban-config.md`：配置表生成流程和访问方式 |
| 代码规范 | `naming-rules.md`：命名、节点前缀、设计模式 |

### 第二步：验证并实现

基于查询到的规范编写实现。若参考文档与代码实际 API 冲突：

1. 使用 `rg` 搜索并阅读实际方法签名；
2. 优先信任代码中的实际实现；
3. 在最终说明中标注冲突点；
4. 必要时将问题记录到 `.claude/memory/`。

## 编码红线

1. **异步优先**：IO 操作使用 `UniTask`，禁止同步加载和 Coroutine。
2. **模块访问**：通过 `GameModule.XXX` 访问模块，不使用 `ModuleSystem.GetModule<T>()`。
3. **资源必须释放**：`LoadAssetAsync` 必须对应 `UnloadAsset`；GameObject 使用 `LoadGameObjectAsync`。
4. **热更边界**：`GameScripts/Procedure`、`Assets/Launcher/`、`Assets/TEngine/` 不热更；`GameScripts/HotFix/` 下的 `GameUpdater`、`GameProto`、`GameLogic` 全部热更。
5. **事件解耦**：模块间使用 `GameEvent`，UI 内部使用 `AddUIEvent`。

## 注释与文件格式

- 新增或修改的注释必须使用中文。
- 所有注释必须通俗易懂，直接说明代码的用途、原因或注意事项。
- 文件统一保持 UTF-8 无 BOM 编码。
- 修改已有文件时保持原有 EOF 换行状态；新建文件默认以换行结尾。

## PowerShell 使用规范

- 优先使用 PowerShell 7：`C:\Program Files\PowerShell\7\pwsh.exe`，不要主动使用 Windows PowerShell 5.1。
- 执行 PowerShell 命令时必须设置 UTF-8 输出：

```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; chcp 65001 > $null
```

## 参考文档

AI 参考文档位于 `.claude/skills/tengine-dev/references/`，包括：

- `architecture.md`：项目结构和启动流程
- `modules.md`：Timer、Scene、Audio、Fsm 等模块 API
- `ui-lifecycle.md`：UI 生命周期、层级和属性
- `fgui.md`：FairyGUI 双体系、事件和代码生成
- `event-system.md`：事件系统和核心接口
- `resource-api.md`：资源加载和卸载
- `hotfix-workflow.md`：HybridCLR、程序集划分和热更包
- `luban-config.md`：配置表流程
- `naming-rules.md`：命名约定和节点前缀
- `ui-patterns.md`：UI Widget 模板和节点绑定
- `event-antipatterns.md`：事件内存泄漏、接口无响应和事件风暴
- `resource-patterns.md`：资源生命周期和泄漏根因
- `mcp-tools.md`、`mcp-visual.md`：场景、GameObject、UI、脚本、Editor、材质、Shader、VFX 和动画工具
- `troubleshooting.md`：问题排查

## 问题记录与自我修正

满足以下任一条件时记录问题：

1. 参考文档描述与通过代码验证的实际 API 不符；
2. 生成代码编译或运行时报错，根因是知识库描述错误；
3. 用户明确指出文档描述错误。

记录文件格式为 `problem_YYYY-MM-DD.md`，例如 `problem_2026-04-21.md`，放在 `.claude/memory/`，至少包含：

- **问题现象**：错误表现或报错信息
- **文档位置**：对应 reference 文档及章节
- **正确 API**：经代码验证后的正确用法
- **建议修正**：文档应改成的表述
