# GameEvent 跨程序集 Spike 记录（task 1.6）

> 本文件是 OpenSpec change `port-minimal-battle-to-gamebattle` task 1.6 的 spike 产物。
> 为避免与同批次 task 1.2/1.14 并发写 `战斗移植设计总纲.md` 冲突，结论独立记录于此。
> Spike 性质：最小验证，不建生产事件体系。正式事件实现见 task 2.3 / 7.2。
> 静态验证日期：2026-08-05（Unity CLI 不可用，结论基于源码 + SourceGenerator.dll 行为分析 + 静态结构检查）。

## 1. Spike 范围与产物

在 GameCommon、GameBattle、GameLogic 三个真实 asmdef 各定义一个最小 `[EventInterface]` 事件接口，验证 SourceGenerator 生成、`GameEventHelper.Init()`、发送、监听和释放链路。

Spike 文件（验收后可删除，非生产代码）：

| 程序集 | 文件 | 接口 | 事件组 | 角色 |
|---|---|---|---|---|
| GameCommon | `Assets/GameScripts/HotFix/GameCommon/EventSpike.cs` | `IBattlePublicEventSpike`（OnBattleFinishedSpike(int)） | GroupLogic | 跨程序集公共契约（对应 design 的 `IBattlePublicEvent` / GameCommon 不可变 DTO 边界） |
| GameBattle | `Assets/GameScripts/HotFix/GameBattle/EventSpike.cs` | `IBattleUiEventSpike`（OnTargetHpChangedSpike(int)） + `BattleEventSpikeRunner` | GroupLogic | GameBattle 内部 UI 事实（对应 `IBattleUiEvent`） + 发送/监听/释放 runner |
| GameLogic | `Assets/GameScripts/HotFix/GameLogic/EventSpike.cs` | `IGameLogicBattleEventSpike`（OnBattleModuleRegisteredSpike()） + `GameLogicEventSpikeRunner` | GroupLogic | 第三程序集生成验证 + 跨程序集接收方 |

asmdef 最小修改（仅加最小 reference，不建完整 6 程序集拓扑——那是 task 1.7）：

- `GameCommon.asmdef`：新增 `GUID:24c092aee38482f4e80715eaa8148782`（TEngine.Runtime），补 rootNamespace=GameCommon。
- `GameBattle.asmdef`：新增 TEngine.Runtime + `GUID:c9a297ec4539fd644a9fd43c8974f15f`（GameCommon），补 rootNamespace=GameBattle。**name 保持 "GameBattle"**，未删 task 1.12 的 `AssemblyInfo.cs`。
- `GameLogic.asmdef`：新增 GameCommon reference（TEngine.Runtime 已有）。

引用图（无环）：GameCommon → TEngine.Runtime；GameBattle → GameCommon + TEngine.Runtime；GameLogic → GameProto + GameCommon + TEngine.Runtime。

## 2. TEngine GameEvent 实现证据（reuse_evidence）

参考的 TEngine 源文件：

- `Assets/TEngine/Runtime/Core/GameEvent/EventInterfaceAttribute.cs`：`[EventInterface(EEventGroup)]`，`EEventGroup` 仅 `GroupUI`/`GroupLogic` 两个值。
- `Assets/TEngine/Runtime/Core/GameEvent/GameEvent.cs`：全局静态门面，持有 `static readonly EventMgr _eventMgr`；`Get<T>()` 走 `_eventMgr.GetInterface<T>()`；`Send/AddEventListener/RemoveEventListener` 委托 Dispatcher；`Shutdown()` 调 `_eventMgr.Init()` 清空。
- `Assets/TEngine/Runtime/Core/GameEvent/EventMgr.cs`：`GetInterface<T>()` 按 `typeof(T)` 查 `EventEntryData.InterfaceWrap`；`RegWrapInterface<T>(callerWrap)` 注册接口实现实例。
- `Assets/TEngine/Runtime/Core/GameEvent/GameEventMgr.cs`：局部批量管理器，`AddEvent` 系列仅在 `GameEvent.AddEventListener` 返回 true 时记录，`Clear()` 逆序 `RemoveEventListener`。
- `Assets/TEngine/Runtime/Core/GameEvent/RuntimeId.cs`：字符串/int 事件 ID 转换。
- `Assets/TEngine/Runtime/Core/GameEvent/SourceGenerator.dll` / `GameEventAnalyzer.dll`：`.meta` 带 `RoslynAnalyzer` label，作为 Roslyn analyzer/generator 全局生效。
- 现有用法：`Assets/GameScripts/HotFix/GameLogic/IEvent/ILoginUI.cs`（`[EventInterface(EEventGroup.GroupUI)]`）；`Assets/GameScripts/HotFix/GameLogic/GameApp.cs:27` `GameEventHelper.Init()`（唯一引用，`#pragma warning disable CS0436` 抑制生成代码继承告警）。

## 3. SourceGenerator 行为分析（核心结论）

通过对 `SourceGenerator.dll` 提取可读字符串得到的关键符号：

- `EventInterfaceGenerator`（ISourceGenerator 实现）、`GenerateGameEventHelper`、`GenerateEventCenter`、`GenerateEventCenterGroup`、`GenerateEventCenterInterfaceInfo`、`GenerateEventClass`、`GenerateImplementationClass`。
- `IsAssemblyNeedAnalyze`、`IsSemanticModelNeedAnalyze`、`analyzeAssemblyNames`、`AnalyzerHelper`：生成器有程序集级过滤门控。
- `get_Compilation`、`get_SyntaxTrees`、`get_FilePath`、`get_ContainingNamespace`：生成器基于当前 Compilation 的语法树扫描 `[EventInterface]` 接口。
- `GameEventHelper` 符号仅出现 1 次（生成的类名）。
- DLL 内**无任何硬编码程序集名**（无 GameLogic/GameCommon/GameBattle/HotFix 字样），`analyzeAssemblyNames` 非硬编码列表，按 Compilation 上下文判定。

### 3.1 生成归属结论：GameEventHelper 按程序集各自生成（per-assembly）

Roslyn SourceGenerator 以**单个 Compilation（= 单个程序集）**为执行单元，`get_SyntaxTrees` 只能看到当前编译程序集的源码语法树；被引用程序集只能看到元数据/符号，看不到 `[EventInterface]` 特性定位的源语法节点。因此：

> **每个含 `[EventInterface]` 接口的 asmdef 各自生成一个 `GameEventHelper` 类，其 `Init()` 只注册本程序集语法树中扫描到的接口 wrap（调用 `GameEvent.EventMgr.RegWrapInterface<T>(new IXxx_Gen())`）。**

三程序集同时含 `[EventInterface]` 时，会生成 3 个同名 `GameEventHelper` 类（各属不同程序集），每个 `Init()` 只注册各自程序集的接口。

### 3.2 唯一初始化入口

- 现状：`GameApp.cs:27` `GameEventHelper.Init()` 是**唯一**显式调用点，位于 GameLogic 程序集的组合根 `GameApp.Entrance`，且是 Entrance 第一行（符合 event-antipatterns.md 反模式 4）。
- 该调用解析到 **GameLogic 程序集生成的 `GameEventHelper`**（GameApp.cs 在 GameLogic 中，`#pragma CS0436` 证实它消费本程序集生成代码）。
- 含义：**只有 GameLogic 的 `[EventInterface]` 接口会被 `RegWrapInterface` 注册到全局 EventMgr**。GameCommon / GameBattle 的接口若不在 GameLogic 中再次 `Init()`，其 `GameEvent.Get<T>()` 返回 `default(T)`（无响应、无报错，符合反模式 4 描述的极难排查场景）。

### 3.3 事件组

三程序集 spike 接口统一用 `EEventGroup.GroupLogic`（战斗属逻辑层）。`EEventGroup` 当前仅 `GroupUI`/`GroupLogic`（EventInterfaceAttribute.cs），**不新增枚举值**以免修改 TEngine（超本 change 范围）。事件组用于生成器按组归类生成 EventCenter，不影响跨程序集可见性。

## 4. 发送 / 监听 / 释放最小链路验证

| 链路 | 模式 | 证据状态 |
|---|---|---|
| 发送（接口事件） | `GameEvent.Get<T>().OnXxx(args)` | 已证实：ILoginUI 同款用法（GameApp.cs:38 注释），`Get<T>` 走全局静态 EventMgr，接口类型经 asmdef reference 跨程序集可见 |
| 监听（int 常量） | `GameEventMgr.AddEvent<T>(IXxx_Event.OnYyy, handler)` | 本程序集内确定可用（常量在本程序集生成，可见性确定）；**跨程序集监听待实测**（见 §5 风险） |
| 释放 | `GameEventMgr.Clear()`（逆序 RemoveEventListener） | API 确证（GameEventMgr.cs:33-49），runner `Release()` 演示 |
| 全局清空 | `GameEvent.Shutdown()`（仅游戏退出） | API 确证（GameEvent.cs:596），非单局清理用途 |

spike runner（`BattleEventSpikeRunner` / `GameLogicEventSpikeRunner`）各自演示：`Register()` → `SendXxx()` → `Release()` 的本程序集闭环。

## 5. 跨程序集风险与替代 bridge

### 风险 A（高）：多程序集 Init 缺失导致接口无响应

若 GameCommon/GameBattle 各自定义 `[EventInterface]` 但只在 GameLogic 调一次 `GameEventHelper.Init()`，则这两程序集的接口 `Get<T>()` 返回 default，事件静默丢失。

**替代 bridge（按优先级）：**

1. **单点定义 + 单点 Init（推荐，符合 design 决策 4）**：跨程序集公共事件接口**只**定义在 GameCommon（公共契约层），由 GameLogic 的 `GameEventHelper.Init()` 统一注册。GameBattle 内部 UI 事件（`IBattleUiEvent`）若也走 `[EventInterface]`，需在 GameBattle 也调一次 `GameBattle.EventHelper.Init()`——但生成类名冲突，需实测生成器是否按命名空间区分，或采用下条。
2. **手写 bridge（design 的 `BattleEventBridge` 走法）**：GameBattle 不用 `[EventInterface]` 发送跨程序集事实，而是由 `BattleEventBridge` 直接调 `GameEvent.Send<int>(IXxx_Event.OnYyy, dto)`（int 事件，不依赖被引用程序集的生成 Init）。但跨程序集 `IXxx_Event` 常量可见性仍受风险 B 约束。
3. **接口实现本程序集注册**：接收方在本程序集实现接口并用本程序集生成的 `RegisterListener`/`Init` 注册，发送方 `Get<T>()` 走全局 EventMgr——前提发送方程序集也 Init 过。

### 风险 B（中）：生成常量类 `_Event` 跨程序集可见性未证实

`IXxx_Event`（int 常量类）与 `IXxx_Gen`（接口实现类）由生成器在接口所在程序集生成。其访问修饰符（public/internal）**无法从 DLL 二进制确证**。现有生产代码（ILoginUI）仅在 GameLogic 内通过 `GameEvent.Get<T>()` 用接口，**从未跨程序集引用 `*_Event` 常量**，故无先例证据。

- 若生成类为 `internal`：GameBattle/GameLogic 无法引用 GameCommon 的 `IBattlePublicEventSpike_Event.OnBattleFinishedSpike`，跨程序集 int 监听编译失败。
- 若为 `public`：跨程序集 int 监听可用。

**替代 bridge：**

1. 在 GameCommon **手写一个 public static 常量包装类**（如 `BattlePublicEventIds`，用 `RuntimeId.ToRuntimeId("...")` 显式定义 int ID），供跨程序集监听引用，绕过生成类可见性问题。
2. 接收方改用**接口事件实现**：在接收程序集实现接口并经本程序集 `Init` 注册，发送方 `Get<T>()` 触发——但仍受风险 A 的多程序集 Init 约束。
3. 跨程序集只用 `GameEvent.Get<T>()` 发送 + 接收方本程序集内 `AddEventListener<int>(本程序集常量, ...)`，避免引用对端生成常量。

### 风险 C（低）：生成器 `analyzeAssemblyNames` 过滤可能排除某些程序集

生成器有 `IsAssemblyNeedAnalyze` 门控，具体过滤规则（是否排除特定名前缀）未确证。若某 asmdef 名不满足规则，该程序集不生成 Helper。**需在 Unity 编辑器实测三程序集均生成 `IXxx_Event`/`IXxx_Gen`/`GameEventHelper`**（检查 `Library/ScriptAssemblies` 生成产物或编译日志）。

## 6. 待 Unity 编辑器实测的验收项（静态分析无法闭环）

以下需在 Unity 编辑器中实测确认，是 task 1.6 真正闭环的剩余步骤（本任务环境无 Unity CLI）：

1. 三程序集编译后，`Library/ScriptAssemblies/{GameCommon,GameBattle,GameLogic}.dll` 各自生成 `GameEventHelper` + 对应 `IXxx_Event`/`IXxx_Gen`。
2. 生成的 `IXxx_Event`/`IXxx_Gen` 访问修饰符（public/internal）——决定风险 B 走哪条 bridge。
3. GameBattle 程序集是否需要独立 `GameEventHelper.Init()` 调用才能让其 `IBattleUiEventSpike` 生效——决定风险 A 走单点还是多点 Init。
4. 跨程序集链路：GameBattle `SendPublicEvent` → GameLogic 监听回调是否触发。
5. `analyzeAssemblyNames` 是否过滤掉三程序集中任一——检查编译日志无 "assembly not analyzed" 类告警。

## 7. 对后续 task 的输入

- **task 7.2（IBattleUiEvent / BattleEventBridge）**：按本 spike 决定接口放置与 Init 策略。建议跨程序集公共事件接口放 GameCommon、由 GameLogic 单点 Init；GameBattle 内部 UI 事件若需独立 Init，需先实测生成类命名是否冲突。
- **task 1.7（热更拓扑）**：本 task 已建立 GameCommon→GameBattle→GameLogic 最小引用子图（无环），1.7 在此基础上扩展为完整 `GameProto → GameCommon → GameFUI → GamePlay → GameBattle → GameLogic` 拓扑。
- **task 9.3 暂停条件**："EventInterface 跨程序集 spike 失败" 已部分触发风险 A/B，但存在手写 bridge 替代方案，**不构成强制暂停**——除非 Unity 实测确认生成类既非 public 又无法多程序集 Init 且无 bridge 可行。
- **design Open Question**（"GameEvent Source Generator 在三程序集真实生成和初始化结果"）：本 spike 给出静态分析结论 + 待实测项，正式结论待 §6 实测后回写总纲（task 1.14）。

## 8. 文件清单

修改：
- `Assets/GameScripts/HotFix/GameCommon/GameCommon.asmdef`
- `Assets/GameScripts/HotFix/GameBattle/GameBattle.asmdef`
- `Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef`

新建（+ .meta）：
- `Assets/GameScripts/HotFix/GameCommon/EventSpike.cs` + `.meta`
- `Assets/GameScripts/HotFix/GameBattle/EventSpike.cs` + `.meta`
- `Assets/GameScripts/HotFix/GameLogic/EventSpike.cs` + `.meta`
- `docs/HotFix架构/game-event-spike-record.md`（本文件）

未碰：GameBattle.Tests.asmdef、SmokeTest.cs、AssemblyInfo.cs、Origin/、openspec/ artifacts、CSV、总纲.md/module-list.md（属 1.2）。
