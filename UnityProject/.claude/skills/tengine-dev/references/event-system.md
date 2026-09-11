# 事件系统

> **适用场景**：GameEvent/AddUIEvent/GameEventMgr 使用、接口事件定义与发送、事件监听清理 | **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（AddUIEvent 自动清理）、[event-antipatterns.md](event-antipatterns.md)（避坑）、[naming-rules.md](naming-rules.md)（事件命名）
>
> **示意说明**：下文示例中的 `IGameEvent`/`ITrade`/`IBattleEvent`/`IPlayerEvent` 为按生成约定书写的示意接口；当前工程真实存在的 `[EventInterface]` 接口仅有 `ILoginUI`（生成 `ILoginUI_Event` 的事件 ID `ShowLoginUI`/`CloseLoginUI`、`ILoginUI_Gen` 包装类，以及程序集级 Registrar；`EventCenter` 分组 API 已废弃，见下文）。

## 架构概览

TEngine 事件系统由四个核心组件构成（其中两个为 Source Generator 生成）：

| 组件 | 类型 | 职责 |
|------|------|------|
| **GameEvent** | 全局静态门面 | 持有 `static readonly EventMgr _eventMgr`（另有公开属性 `GameEvent.EventMgr`），监听/移除/分发委托给 `_eventMgr.Dispatcher`，接口注册（`RegWrapInterface`）与获取（`GetInterface`）在 EventMgr 上 |
| **GameEventMgr** | 局部作用域管理器 | 实现 `IMemory`，用于 UI 面板等需要随生命周期自动解绑的场景，只有 `AddEvent` + `Clear()` |
| **GameEventHelper** | TEngine.Runtime 手写类（非生成物） | 事件接口注册唯一入口。`Init()` 启动阶段全量注册（失败清理并抛聚合异常）；`RegisterAssembly(Assembly)` 运行期注册动态加载的程序集（失败不影响全局监听）。两类入口都要求模块系统 Running 且必须在主线程调用 |
| **EventAssemblyRegistrarAttribute / IEventAssemblyRegistrar** | TEngine.Runtime（生成器协作） | 程序集级注册标记 + 注册器接口。每个含 `[EventInterface]` 接口的程序集由 Source Generator 生成一个 Registrar 实现类，Registrar 内编译期直连 `new {接口}_Gen(dispatcher)` 完成注册；`GameEventHelper` 靠扫描程序集级特性找到 Registrar |
| **EventCenter** | 已废弃 | 旧生成式分组监听封装。现仅保留 `GameLogic` 中的 `[Obsolete]` 兼容壳，不再生成分组 API，请改用 `{接口}_Event` + `GameEvent`，参数检查由分析器保障 |

底层由 `EventMgr`（接口注册表，持有分发器）与 `EventDispatcher`（int 事件分发表）支撑，源码位于 `Assets/TEngine/Runtime/Core/GameEvent/`，一般无需直接使用。

TEngine 提供两种事件模式：**int/string 事件**（委托回调）和**接口事件**（类型安全；底层同为 int 事件，事件 ID 由 `RuntimeId` 分配）。

### 模式对比

| 特性 | int/string 事件 | 接口事件 |
|------|----------------|---------|
| 定义方式 | int 事件 ID / string（int ID 推荐来自生成类 `{接口名}_Event` 的 `public static readonly int` 字段） | 带 `[EventInterface]` 的接口 |
| 发送 | `GameEvent.Send(int/string)` | `GameEvent.Get<ITrade>().OnTrade(...)` |
| 监听 | `GameEvent.AddEventListener(int/string, callback)` | `AddUIEvent({接口名}_Event.{方法名}, ...)`（接口实现类由生成器自动注册） |
| 类型安全 | 无编译检查 | 编译期检查 |
| 适用场景 | 简单通知、UI 内部 | 模块间通信、多参数 |

---

## 核心 API

### GameEvent 静态方法

#### Send（发送事件）

```csharp
// int 版本：支持 0~6 个泛型参数
GameEvent.Send(int eventType);
GameEvent.Send<T1>(int eventType, T1 arg1);
GameEvent.Send<T1,T2>(int eventType, T1 arg1, T2 arg2);
// ... 最多 Send<T1,T2,T3,T4,T5,T6>

// string 版本：支持 0~5 个泛型参数（内部通过 RuntimeId.ToRuntimeId 转为 int）
GameEvent.Send(string eventType);
GameEvent.Send<T1>(string eventType, T1 arg1);
// ... 最多 Send<T1,T2,T3,T4,T5>

// 另有 Delegate 参数重载（少见）：Send(int eventType, Delegate handler)、Send(string eventType, Delegate handler)
```

#### AddEventListener（监听事件）

返回 `bool`（是否监听成功）。返回 `false` 的情形：`ModuleSystem` 未处于 Running 状态（如退出流程中）；同一 handler 重复注册（输出 `Log.Fatal("Repeated Add Handler")`）。UI 内推荐用 `AddUIEvent`（见下文，自动清理，无需关心返回值）。

```csharp
// int 版本：支持 0~6 个泛型参数
bool GameEvent.AddEventListener(int eventType, Action handler);
bool GameEvent.AddEventListener<T1>(int eventType, Action<T1> handler);
// ... 最多 AddEventListener<T1,T2,T3,T4,T5,T6>

// string 版本：支持 0~5 个泛型参数
bool GameEvent.AddEventListener(string eventType, Action handler);
bool GameEvent.AddEventListener<T1>(string eventType, Action<T1> handler);
// ... 最多 AddEventListener<T1,T2,T3,T4,T5>
```

#### RemoveEventListener（移除监听）

```csharp
// int 版本：支持 0~5 个泛型参数 + Delegate 重载
GameEvent.RemoveEventListener(int eventType, Action handler);
GameEvent.RemoveEventListener<T1>(int eventType, Action<T1> handler);
// ... 最多 RemoveEventListener<T1,T2,T3,T4,T5>
GameEvent.RemoveEventListener(int eventType, Delegate handler);  // Delegate 重载

// string 版本：支持 0~5 个泛型参数 + Delegate 重载
GameEvent.RemoveEventListener(string eventType, Action handler);
// ... 最多 RemoveEventListener<T1,T2,T3,T4,T5>
GameEvent.RemoveEventListener(string eventType, Delegate handler);  // Delegate 重载
```

#### Get（接口事件获取）

```csharp
// 返回接口实例，内部调用 _eventMgr.GetInterface<T>()；未注册时返回 null（不抛异常）
T GameEvent.Get<T>();
```

#### Shutdown（清除所有事件注册）

```csharp
// 仅在游戏退出时调用，内部调用 _eventMgr.Init() 重置所有事件
GameEvent.Shutdown();
```

> **注意**：源码中没有 `UnRegisterAll<T>()` 或 `UnRegisterAll()` 方法。需要清除所有事件时使用 `Shutdown()`（全局）或 `GameEventMgr.Clear()`（局部）。

### GameEventMgr 局部管理器

实现 `IMemory`，仅支持 `int` eventType，最多 5 个泛型参数。没有 `RemoveEvent` 方法，通过 `Clear()` 一次性移除所有已注册事件。

```csharp
private readonly GameEventMgr _eventMgr = new();

// 注册（仅在 AddEventListener 返回 true 时才记录到内部列表）
_eventMgr.AddEvent(int eventType, Action handler);
_eventMgr.AddEvent<T1>(int eventType, Action<T1> handler);
// ... 最多 AddEvent<T1,T2,T3,T4,T5>

// 一次性移除所有
_eventMgr.Clear();
```

---

## 使用模式

### int 事件

```csharp
// 1. 定义事件接口（必须 [EventInterface]，需指定事件组）
[EventInterface(EEventGroup.GroupUI)]
public interface IGameEvent
{
    void OnGoldChanged();

    void OnHpChanged(int hp);
}

// 2. Source Generator 自动生成（生成于 GameLogic 命名空间）
public partial class IGameEvent_Event
{
    public static readonly int OnGoldChanged = RuntimeId.ToRuntimeId("IGameEvent_Event.OnGoldChanged");
    public static readonly int OnHpChanged = RuntimeId.ToRuntimeId("IGameEvent_Event.OnHpChanged");
}

// _Gen 实现类：构造函数接收 EventDispatcher，构造时经 RegWrapInterface<IGameEvent>(this) 注册
public partial class IGameEvent_Gen : IGameEvent
{
    public void OnGoldChanged() { /* 内部 _dispatcher.Send(IGameEvent_Event.OnGoldChanged) */ }

    public void OnHpChanged(int hp) { /* 内部 _dispatcher.Send(IGameEvent_Event.OnHpChanged, hp) */ }
}

// 发送
GameEvent.Get<IGameEvent>().OnGoldChanged();
GameEvent.Get<IGameEvent>().OnHpChanged(hp);

// 监听（UI 内用 AddUIEvent 自动清理）
AddUIEvent(IGameEvent_Event.OnGoldChanged, OnGoldChanged);
AddUIEvent<int>(IGameEvent_Event.OnHpChanged, OnHpChanged);

// 非 UI 类监听（必须手动移除）
void OnEnable() { GameEvent.AddEventListener<int>(IGameEvent_Event.OnHpChanged, OnHpChanged); }
void OnDisable() { GameEvent.RemoveEventListener<int>(IGameEvent_Event.OnHpChanged, OnHpChanged); }
```

### string 事件类型

除 `int` 外，`GameEvent` 也支持 `string` 作为事件 ID（API 与 int 版本对称，但是不推荐使用）：

```csharp
// 发送
GameEvent.Send("OnGoldChanged");
GameEvent.Send<int>("OnHpChanged", 50);

// 监听（UI 内）：AddUIEvent 只有 int eventType 重载，string 需先转 RuntimeId
AddUIEvent(RuntimeId.ToRuntimeId("OnGoldChanged"), OnGoldChanged);
AddUIEvent<int>(RuntimeId.ToRuntimeId("OnHpChanged"), OnHpChanged);

// 非 UI 类监听（GameEvent 有 string 重载，无需手动转换）
GameEvent.AddEventListener<int>("OnHpChanged", OnHpChanged);
GameEvent.RemoveEventListener<int>("OnHpChanged", OnHpChanged);
```

适用场景：事件名需要动态拼接、或跨模块字符串约定时使用。性能略低于 int（内部通过 `RuntimeId.ToRuntimeId` 转换），优先用 int。

### 接口事件

```csharp
// 1. 定义接口（必须 [EventInterface]，需指定事件组）
[EventInterface(EEventGroup.GroupUI)]
public interface ITrade
{
    void OnTradeComplete(int itemId, int count);
}

// 2. 源代码生成器自动实现并注册（生成于 GameLogic 命名空间）
public partial class ITrade_Gen : ITrade
{
    public void OnTradeComplete(int itemId, int count) { /* 内部 _dispatcher.Send(ITrade_Event.OnTradeComplete, itemId, count) */ }
}

// 3. 发送
GameEvent.Get<ITrade>().OnTradeComplete(itemId, count);
```

**前提**：`GameEventHelper.Init()` 已在 `GameApp.Entrance` 中最先调用（须在模块系统 Running 且主线程执行，否则抛 `InvalidOperationException`）。

> **注意**：`GameEventHelper` 是 `TEngine.Runtime` 中的手写固定类（`Assets/TEngine/Runtime/Core/GameEvent/GameEventHelper.cs`），不是生成物。它扫描每个已加载程序集的程序集级 `EventAssemblyRegistrarAttribute`，实例化对应 Registrar（Source Generator 为每个含 `[EventInterface]` 接口的程序集生成一个），由 Registrar 编译期直连 `new {接口}_Gen(dispatcher)` 完成注册（`_Gen` 构造器调用 `GameEvent.EventMgr.RegWrapInterface<T>(this)`）。
>
> **注册时机约束**：`RegWrapInterface` 仅在 `ModuleSystem.IsRunning` 时才真正注册（`EventMgr.cs`），`GameEventHelper` 入口会先校验运行态与主线程。事件接口可定义在**任何引用 TEngine.Runtime 的程序集**（热更的 GameLogic/GameProto、非热更模块、测试程序集等），注册由 `Init()` 自动覆盖调用时已加载的程序集；**之后动态加载的程序集需在加载完成后调用 `GameEventHelper.RegisterAssembly(assembly)`**（运行期禁止再次调用 `Init()`，失败仅抛异常且不影响已注册监听）。
>
> **一次性语义**：`Init()` 同一游戏会话只允许成功一次；`GameEvent.Shutdown()` 或新会话（`SubsystemRegistration`）后可重新 `Init()`。

### EventCenter（已废弃，兼容壳）

`EventCenter` 曾由 Source Generator 按事件组生成 `GameLogic.EventCenter.AddEvent.Xxx.Method(Action)` / `RemoveEvent.Xxx.Method(Action)` 分组封装（组名为接口名去掉 `I` 前缀）。**现已停止生成**，仅保留 `GameLogic/EventCenter.cs` 中的 `[Obsolete]` 兼容壳（防止旧代码编译期类型丢失），不再提供任何分组 API：

```csharp
// 旧写法（现已废弃，编译报错提示迁移）
EventCenter.AddEvent.LoginUI.ShowLoginUI(OnShowLoginUI);

// 新写法：{接口}_Event 静态 ID + GameEvent（参数类型检查由 GameEventAnalyzer EVENT001/EVENT002 保障）
GameEvent.AddEventListener(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
GameEvent.RemoveEventListener(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
```

适合非 UI 类按接口维度管理监听，方法签名编译期检查。

### GameEventMgr 批量管理

非 UI 类的事件监听推荐用 `GameEventMgr` 统一管理，避免忘记移除：

```csharp
private readonly GameEventMgr _eventMgr = new();

public void Init()
{
    // 事件 ID 使用 Source Generator 生成类字段（工程中不存在 GameEventDef 常量类）
    _eventMgr.AddEvent(IGameEvent_Event.OnGoldChanged, OnGoldChanged);
    _eventMgr.AddEvent<int>(IGameEvent_Event.OnHpChanged, OnHpChanged);
}

public void Dispose() => _eventMgr.Clear();  // 一次性移除所有
```

### AddUIEvent（UI 内监听）

定义于 `UIBase`（`UIWindow`/`UIWidget` 的共同基类），仅支持 `int` eventType，最多 4 个泛型参数，**没有 string 重载**：

```csharp
// UIBase.cs（GameLogic 程序集）
public void AddUIEvent(int eventType, Action handler);
protected void AddUIEvent<T>(int eventType, Action<T> handler);
protected void AddUIEvent<T, U>(int eventType, Action<T, U> handler);
protected void AddUIEvent<T, U, V>(int eventType, Action<T, U, V> handler);
protected void AddUIEvent<T, U, V, W>(int eventType, Action<T, U, V, W> handler);
```

- 内部通过 `GameEventMgr`（`MemoryPool.Acquire` 获取）注册，窗口/控件销毁清理流程中自动 `RemoveAllUIEvent()`（等价 `Clear()` 并回池），窗口复用 `Init` 时也会先经 `ResetUIEventLifecycle()` 清理旧监听，无需手动移除
- 推荐在 `UIBase.RegisterEvent()`（virtual）重写中注册，UIWindow/UIWidget 生命周期中自动调用
- 关闭流程开始后（`BlockUIEvents`）再注册会抛 `ObjectDisposedException`，不要在窗口关闭过程中挂接新事件
- FairyGUI 场景的 `FguiLifetimeScope` 提供同名 `AddUIEvent`（0~4 个泛型参数，Dispose 时统一清理）

---

## 常见错误

### 1. 忘记 GameEventHelper.Init()

```csharp
// 错误：GameEvent.Get<T>() 全部返回 null / 无响应，无报错，极难排查
public static void Entrance(object[] objects)
{
    // GameEventHelper.Init();  <- 忘记调用
    ...
}

// 正确：热更入口中最先调用（见 GameApp.Entrance）
public static void Entrance(object[] objects)
{
    TEngine.GameEventHelper.Init();
    ...
}
```

### 2. UI 外部使用 AddEventListener（内存泄漏）

```csharp
// 错误：退出窗口不会自动清理
void SomeMethod()
    => GameEvent.AddEventListener<int>(IBattleEvent_Event.OnHpChanged, OnHpChanged);

// 正确：UIWindow 中用 AddUIEvent（自动清理）
protected override void RegisterEvent()
    => AddUIEvent<int>(IBattleEvent_Event.OnHpChanged, OnHpChanged);

// 正确：非 UI 类用 GameEventMgr
private readonly GameEventMgr _eventMgr = new();
public void Dispose() => _eventMgr.Clear();
```

### 3. 手写事件 ID 常量

```csharp
// 错误：手写 int 常量，容易重复/拼错
public const int OnHpChanged = 1001;

// 正确：Source Generator 自动生成
AddUIEvent(IBattleEvent_Event.OnHpChanged, OnHpChanged);
```

### 4. 事件回调签名不匹配

```csharp
// 错误：注册 Action<string>、发送 int -> 分发时按 Action<int> 类型匹配委托，回调静默不执行（无异常、无日志）
GameEvent.Send<int>(IBattleEvent_Event.OnHpChanged, hp);
AddUIEvent<string>(IBattleEvent_Event.OnHpChanged, OnHp);

// 正确：接口事件模式可编译期检查
GameEvent.Get<IBattleEvent>().OnHpChanged(hp); // 类型安全
```

### 5. 非 UI 类忘记移除监听

```csharp
// 错误：销毁时不移除，回调引用已释放对象
public class PlayerSystem
{
    public void Init() => GameEvent.AddEventListener(IPlayerEvent_Event.OnDead, OnDead);
    // 没有 RemoveEventListener -> 泄漏
}

// 正确：使用 GameEventMgr 批量清理
private readonly GameEventMgr _eventMgr = new();
public void Init()    => _eventMgr.AddEvent(IPlayerEvent_Event.OnDead, OnDead);
public void Dispose() => _eventMgr.Clear();
```

### 6. 误用不存在的 UnRegisterAll

```csharp
// 错误：源码中不存在 UnRegisterAll<T>() 或 UnRegisterAll() 方法
GameEvent.UnRegisterAll<int>(eventType);
GameEvent.UnRegisterAll();

// 正确：按需使用以下方式清除
GameEvent.RemoveEventListener(eventType, handler);  // 移除单个监听
GameEventMgr.Clear();                               // 局部批量清除
GameEvent.Shutdown();                               // 全局清除（仅游戏退出时）
```

### 7. 误用不存在的 RegisterListener

```csharp
// 错误：GameEvent 中不存在 RegisterListener<T>() 方法
GameEvent.RegisterListener<ITrade>(implementation);

// 正确：GameEventHelper.Init() 扫描程序集级注册标记，Registrar 自动完成注册，无需手动调用 RegisterListener
TEngine.GameEventHelper.Init();
```

---

## 事件定义规范

| 规则 | 说明 |
|------|------|
| ID 来源 | 事件 ID = `RuntimeId.ToRuntimeId(长度前缀事件键)`，事件键 = `程序集完整身份 | 接口元数据全名 | 方法名`（长度前缀编码，程序集身份格式为 `名称|版本|PublicKeyToken`，形如 `16:GameLogic|1.0.0|18:GameLogic.ILoginUI11:ShowLoginUI`），杜绝跨命名空间/跨程序集同名接口碰撞；勿手写小整数事件 ID |
| 命名 | `On` + 过去式动词 + 名词：`OnGoldChanged`、`OnBattleEnded` |
| 接口命名 | `I` + 动词 + 名词：`ITrade`、`ILoginUI` |
| 泛型参数 | int 事件最多 6 个，string 事件最多 5 个，GameEventMgr 最多 5 个，AddUIEvent 最多 4 个 |
| 禁止 | 手写事件 ID 硬编码数字，应用生成类字段或 `RuntimeId.ToRuntimeId` |

## 事件接口支持契约（跨程序集规则）

`[EventInterface]` 接口可定义在**任何引用 TEngine.Runtime 的程序集**（热更/非热更/测试），生成器按接口实际命名空间生成 `{接口}_Event`/`{接口}_Gen`/程序集 Registrar，其余程序集无需任何初始化代码。但接口**形状受契约约束**（违规编译期报 `EVENT003`，生成器零产出）：

| 允许 | 禁止（EVENT003） |
|------|-----------------|
| 顶层、非泛型、`public interface`（支持 partial 多声明，按符号去重） | 嵌套接口、泛型接口、非 public 接口 |
| 无继承 | 继承其他接口（含默认接口实现） |
| `void` 实例方法，0~5 个普通参数（对齐 GameEventMgr；底层 Send 支持到 6） | 非 void 返回、参数超 5 个、同名重载、泛型方法、`ref/out/in`、`params`、默认参数、指针/函数指针/`ref struct`（`Span<T>` 族）参数、默认接口实现、非 public 方法 |
| 方法名为关键字时自动 `@` 转义 | 接口内的静态成员、属性、事件、常量、索引器、嵌套类型等其他成员 |

`EventAssemblyRegistrarAttribute`/`IEventAssemblyRegistrar` 由 Source Generator 独占生成，**禁止手写 `[assembly: EventAssemblyRegistrar]`**（EVENT004 编译报错）；Registrar 为纯生成物（编译期直连 `new {接口}_Gen`），无业务代码。

### 分析器覆盖范围（EVENT001/EVENT002）

`GameEventAnalyzer` 对 `AddUIEvent`/`AddEventListener`/`RemoveEventListener` 三类调用做编译期参数检查（泛型参数数量、类型与接口方法一致；Remove 写错会导致静默移除失败，与 Add 同构检查），并提供 CodeFix 一键修复（同步改写回调方法签名，参数名为关键字时自动 `@` 转义）。两点边界：

- handler 静态类型为 `System.Delegate` 的非泛型重载没有泛型参数可校验，分析器跳过不报错；
- 分析器按方法名匹配（`AddUIEvent` 等），语义绑定 `_Event` 类 + 接口符号，跨程序集事件接口同样生效。

---

## 交叉引用

| 相关文档 | 内容 |
|---------|------|
| ui-lifecycle.md | AddUIEvent 在 UIWindow 生命周期中的自动清理机制 |
| modules.md | GameModule 事件相关模块 |
| event-antipatterns.md | 事件系统反模式与避坑指南 |
| naming-rules.md | 事件常量与接口的命名约定 |
