---
name: tengine-dev
description: TEngine Unity 游戏框架开发指导。触发词：TEngine, UIWindow, UIWidget, FguiModule, FguiWindow, UIBindComponent, ScriptAutoGenerator, FairyGUI, GameEvent, EventCenter, AddUIEvent, ScriptGenerator, LoadAssetAsync, SetSprite, PreloadRequestRunner, HybridCLR, YooAsset, UniTask, Luban, ConfigSystem, GameModule, Procedure, 热更, 资源加载, UI开发, 事件系统, 配置表
---

# TEngine 开发指导

TEngine 是基于 HybridCLR + YooAsset + UniTask + Luban 的 Unity 游戏框架。
本 skill 提供 AI 专用的精炼参考文档，确保生成的代码与框架 API 完全一致。

## 核心红线

1. **异步优先**：IO 操作用 `UniTask`，禁止同步加载/Coroutine
2. **模块访问**：通过 `GameModule.XXX` 访问，而非 `ModuleSystem.GetModule<T>()`；注意 `GameModule` 静态类定义在 GameLogic 热更程序集（`Assets/GameScripts/HotFix/GameLogic/GameModule.cs`），GameProto 程序集不依赖 GameLogic 无法访问，配置代码需用 `ModuleSystem.GetModule<IResourceModule>()`
3. **资源必须释放**：`LoadAssetAsync` 对应 `UnloadAsset`，GameObject 用 `LoadGameObjectAsync`（实例 `Destroy` 即自动归还）
4. **热更边界**：`Assets/GameScripts/Procedure`、`Assets/Launcher/`、`Assets/TEngine/` 不热更；`Assets/GameScripts/HotFix/`（GameUpdater/GameProto/GameLogic）全部热更
5. **事件解耦**：模块间用 `GameEvent`，UI 内部用 `AddUIEvent`（事件 ID 来自 `[EventInterface]` 接口生成的 `{接口名}_Event` 常量类，当前工程真实接口仅 `ILoginUI`）
6. **FSM 状态切换**：状态内部用基类 protected `ChangeState<TState>()`，`GameModule.Procedure` 无外部 ChangeState API
7. **异步取消语义**：`LoadAssetAsync` 传 CancellationToken 取消时**返回 null 而非抛异常**，调用方判空跳过赋值即可；`await downloader` 合法（UniTask 的 YooAsset 扩展 `AsyncOperationBase.GetAwaiter`，Failed 时抛异常）

## 文档路由

根据任务类型，读取对应的 reference 文档：

| 任务类型 | 必读文档 | 进阶文档 | 优先级 |
|---------|---------|---------|--------|
| UI 开发 | [ui-lifecycle.md](references/ui-lifecycle.md) | [ui-patterns.md](references/ui-patterns.md) | P0 |
| FGUI 开发 | [fgui.md](references/fgui.md) | [ui-lifecycle.md](references/ui-lifecycle.md)（UGUI 对照） | P0 |
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
