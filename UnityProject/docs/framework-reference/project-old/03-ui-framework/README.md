# 03 UI 框架与表现层组织

本目录是对参考工程 `ProjectOld` 的 UI 运行时框架调查，以及对当前 TEngine 工程已有 UI 能力的针对性对照。报告只记录源码、程序集/场景入口和必要配置能够支持的事实；没有进行 Unity 运行、编译、打包、导表或资源生成。所有候选设计都是未批准的研究建议。

## 目标与边界

本轮回答以下问题：

- UI 基类、窗口与组件的职责边界，以及管理器、层级、导航和扩展点如何协作。
- 创建、异步加载、显示、隐藏、关闭、销毁、缓存/复用的状态与所有权。
- 重复打开、异步打开中关闭、失败路径、事件/计时器/资源清理路径。
- 新增窗口需要遵守的运行时约定，以及表现状态与业务状态的边界。
- 至少一条真实业务窗口从打开到最终清理的可追溯调用链。

明确不在本轮范围内：

- 业务窗口的功能、界面资源、配置数据、协议和业务实现；业务代码只作框架调用证据。
- 资源模块内部装载、引用计数和包生命周期的完整分析（交由 02）；本报告只保留 UI 的资源接口边界。
- 通用事件实现的完整分析（交由 04）；本报告只记录窗口如何持有和清理事件订阅。
- 编辑器绑定生成器和 UIProject 素材全量扫描（交由 06）；本报告只引用生成代码的运行时契约和必要入口。
- 任何生产代码改造或具体游戏功能迁移。

## 调查基线

| 项目 | 绝对根路径 | 版本/修订信息 |
|---|---|---|
| OLD | `D:\Work\SAUnity\ProjectOld\` | Unity `2022.3.62f2 (7670c08855a9)`；工程根无 `.git/.svn`，但 `Assets/Scripts/framework` 与 `Assets/Scripts/game` 是 SVN 工作副本，调查时 `svn info` 显示 revision `10480`；`UIProject` 是独立 SVN 工作副本，revision `45216`。 |
| CURRENT | `E:\MyWork\MyFramework\TEngine\UnityProject\` | Unity `2022.3.62f2 (7670c08855a9)`；Git `HEAD=16afccb5df2a2a2efcb5003ecf9fbc0781c0170a`，最近提交主题为 `change version`。 |

调查日期为 `2026-09-06`，时区 `Asia/Shanghai`。OLD 没有工程根级 Git/SVN 修订号，因此对本领域关键 OLD 源码同时记录了调查时 SHA-256：

| 文件 | SHA-256 |
|---|---|
| `Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel.cs` | `016FC7455B02EFC52CF7287D2FB03BA20B2609AD6AC45917A1AFDA683298C2D1` |
| `Assets/Scripts/framework/Library/ZeroFramework/Panel/BaseView.cs` | `1C8855FCD8F8DDAF3CCE67088CF1F2F73BBA428D8B8E264E690B73E10FE2F033` |
| `Assets/Scripts/framework/Library/ZeroFramework/Panel/IPanel.cs` | `681C5741FC149A3C3CCBEFA3925494BB1635FDE28365C91E856B10D34A9CAAD3` |
| `Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs` | `11A1AF5685145228B6642B8EF13A7D06DD385F71D31ECD99212596ECAC935A1D` |
| `Assets/Scripts/framework/Library/ZeroFramework/Panel/UIAwaiter.cs` | `DBC87566E729C47C33E7981DC723A1B8EEF6814C918743FD628A56EAD309E36A` |
| `Assets/Scripts/game/Helper/UIHelper.cs` | `455C704608FE3D6AFF12ABD3EE7B8D0FA543E904B1FEEA6A7BADF2ABB6001121` |
| `Assets/Scripts/game/Utils/UIBindProvider.cs` | `ACAA691BA52DB8786EFD6EFA42E06D93DC3D5CA84822AB8A2C54C6029418483E` |
| `Assets/Scripts/game/Module/MainScene/View/MainPanel.cs` | `DB6AD1D161FDA9A2C91DAA1CEF4681F89A69D1876C51278FE388C83299017929` |
| `Assets/Scripts/game/Module/MainScene/MainSceneModule.cs` | `38CD6C3C91F3E4DD390400E7A5D7061836C0B9FA79079C96A7BD2A388844DF26` |

调查开始时 CURRENT 已存在且保留的变更包括 `ProjectSettings/boot.config` 删除、`UserSettings/Layouts/` 未跟踪内容，以及其他 slave 的 `docs/framework-reference/project-old/handoffs/` 文件。本轮没有恢复、暂存、提交、清理或改写这些内容。

## 推荐阅读顺序

1. [findings.md](./findings.md)：先读职责边界、生命周期图、调用链和对照结论。
2. [evidence.md](./evidence.md)：按 `S03-E###` 复查源码与配置行号。
3. [candidates.md](./candidates.md)：查看带当前工程对照的候选设计，不把建议当成实施决定。
4. [open-questions.md](./open-questions.md)：查看静态调查不能证明的运行时问题和跨领域待协调项。
5. [verification.md](./verification.md)：查看本轮实际执行的范围、链接检查和局限。

## 机制覆盖清单

| 机制 | 状态 | 主要证据/说明 |
|---|---|---|
| OLD `BasePanel` / `IPanel` 生命周期 | 已调查 | `Initialize → OnInit → Open/OnUpdateParam → OnClose → Dispose`，见 `S03-E006`、`S03-E010`。 |
| OLD `BaseView` 组件边界 | 已调查 | 组件没有 `IPanel` 的打开状态和缓存职责，见 `S03-E007`。 |
| OLD `PanelMgr` 注册、层级、导航 | 已调查 | 层容器、同层优先级、返回过滤和扩展回调，见 `S03-E004`、`S03-E012`、`S03-E016`。 |
| OLD 生成 UIBase 与运行时绑定 | 已调查到运行时消费 | 反射绑定和常量契约已核对；编辑器生成过程不展开，见 `S03-E003`。 |
| OLD 缓存/复用与关闭/销毁分离 | 已调查 | `CanCache` 脱离但不 Dispose；清缓存/注销才销毁，见 `S03-E011`。 |
| OLD 异步打开、重复打开、失败清理 | 已调查并标注缺陷风险 | 有 `m_openingPanel` 状态查询，但打开前无抑制，异常/早返缺少统一 `finally`，见 `S03-E008`、`S03-E009`。 |
| OLD 事件、计时器、异步任务、Disposable 清理 | 已调查 | 基类移除阶段、模块事件组、Panel token 与 FGUI Disposable 分层，见 `S03-E010`、`S03-E014`、`S03-E015`。 |
| OLD 真实窗口打开到清理链 | 已调查 | `MainPanel` 作为调用证据，见 `S03-E015` 及 findings 的链路图。 |
| CURRENT `UIModule` 类型栈、层级和显隐 | 已调查 | `_uiStack`、`WindowAttribute`、全屏遮挡、排序与更新，见 `S03-E017`、`S03-E020`、`S03-E021`。 |
| CURRENT `UIWindow` / `UIWidget` 边界 | 已调查 | 窗口异步加载和销毁、组件父子创建和递归销毁，见 `S03-E018`、`S03-E019`。 |
| CURRENT 关闭中异步加载、重复打开和资源 token | 已调查为静态语义 | 类型去重存在；窗口加载未传取消 token，关闭后由完成回调销毁迟到实例，见 `S03-E019`、`S03-E020`、`S03-E023`。 |
| CURRENT 事件/定时器生命周期 | 已调查到 UI 自有能力 | `GameEventMgr` 可随 UI 归还内存池；隐藏关闭定时器显式移除；通用 timer 归属仍由调用方管理，见 `S03-E018`、`S03-E023`。 |
| CURRENT Launcher UI | 已调查边界 | `Launcher` 是与 `GameLogic` 分开的轻量资源 UI，关闭即 `DestroyImmediate`，见 `S03-E022`。 |
| UIProject 入口 | 已做必要检查 | 仅检查 `settings` 文件名和 SVN 信息，没有扫描素材；详见 verification。 |
| 运行时行为、性能、真实资源引用释放 | 待确认 | 本轮没有运行 Unity；不声称性能或运行测试通过，见 [open-questions.md](./open-questions.md)。 |

## 证据约定

结论使用“代码确认”“推断”“待验证”标记。`S03-E###` 是可复查证据编号；每项证据在 [evidence.md](./evidence.md) 中给出工程、相对路径、符号/字段、核验行号、短摘录、支持结论和证据性质。定义存在不自动等于运行时可达；可达性判断会同时参考调用者、程序集、启动配置和条件编译。
