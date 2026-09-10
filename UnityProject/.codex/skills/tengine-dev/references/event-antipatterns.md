# 事件系统反模式与避坑指南

> **适用场景**：事件内存泄漏排查、接口事件无响应调试、事件风暴问题定位 | **关联文档**：[event-system.md](event-system.md)（核心 API）、[ui-lifecycle.md](ui-lifecycle.md)（AddUIEvent 自动清理）

> 本文档是 [event-system.md](event-system.md) 的进阶补充，聚焦于难以排查的陷阱和反模式。

> 示例中的 `IPlayerEvent`/`IItemEvent` 等为按生成约定书写的示意事件接口；真实工程以 `[EventInterface]` 接口生成的 `{接口名}_Event` 常量类为准（当前工程真实示例：`ILoginUI` → `ILoginUI_Event.ShowLoginUI`）。

---

## 一、内存泄漏反模式

### 反模式 1：UIWindow 外部直接 AddEventListener

```csharp
// ❌ 错误：退出窗口后监听不清理，回调引用已销毁的对象
public class BagUI : UIWindow
{
    protected override void OnCreate()
    {
        // 在 OnCreate 而非 RegisterEvent 中注册，且用了全局方法
        GameEvent.AddEventListener<int>(IItemEvent_Event.OnItemChanged, OnItemChanged);
    }
}

// ✅ 正确：RegisterEvent 内使用 AddUIEvent，随窗口销毁自动清理
public class BagUI : UIWindow
{
    protected override void RegisterEvent()
    {
        AddUIEvent<int>(IItemEvent_Event.OnItemChanged, OnItemChanged);
    }
}
```

**为何危险**：`AddUIEvent` 的监听由内部 `GameEventMgr` 记账，`UIWindow.InternalDestroy()` 在清理子 Widget 与 `OnDestroy` 之前自动调用 `RemoveAllUIEvent()` 批量清理（完整顺序见反模式 9）；窗口 Close、CloseAll、关服最终都会走 `InternalDestroy`，因此 UI 事件随窗口销毁自动清理。而 `GameEvent.AddEventListener` 直接写入全局事件表，与 UI 生命周期无关联、不会自动清理，窗口销毁后仍持有回调引用，触发时访问已销毁 GameObject。

---

### 反模式 2：非 UI 类不清理监听

```csharp
// ❌ 错误：系统类注册事件但从不移除
public class PlayerSystem
{
    public void Init()
    {
        GameEvent.AddEventListener(IPlayerEvent_Event.OnLevelUp, OnLevelUp);
        GameEvent.AddEventListener<int>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);
        // 没有对应的 Remove
    }
    // OnDisable/OnDestroy 中也没有清理
}

// ✅ 正确：用 GameEventMgr 统一管理，Dispose 时一次清理
public class PlayerSystem
{
    private readonly GameEventMgr _eventMgr = new();

    public void Init()
    {
        _eventMgr.AddEvent(IPlayerEvent_Event.OnLevelUp, OnLevelUp);
        _eventMgr.AddEvent<int>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);
    }

    public void Dispose() => _eventMgr.Clear();
}
```

---

### 反模式 3：Lambda 捕获导致泄漏

```csharp
// ❌ 错误：Lambda 无法用 RemoveEventListener 移除（引用不同）
public void Init()
{
    GameEvent.AddEventListener<int>(eventId, hp => { _textHp.text = hp.ToString(); });
    // 后续 RemoveEventListener 传入新 Lambda，无法匹配
}

// ✅ 正确：命名方法，可精确移除；或改用 GameEventMgr/AddUIEvent
private void OnHpChanged(int hp) => _textHp.text = hp.ToString();

public void Init()   => _eventMgr.AddEvent<int>(eventId, OnHpChanged);
public void Dispose() => _eventMgr.Clear();
```

---

## 二、接口事件无响应

### 反模式 4：遗忘 GameEventHelper.Init()

```csharp
// ❌ 错误：GameEvent.Get<T>() 返回 null，调用其成员抛 NullReferenceException
public static void Entrance(object[] objects)
{
    _hotfixAssembly = (List<Assembly>)objects[0];
    StartGameLogic();  // 此时所有接口事件都无法响应
}

// ✅ 正确：必须最先调用，在任何 GameEvent.Get<T>() 之前
public static void Entrance(object[] objects)
{
    if (!ModuleSystem.IsRunning)
    {
        Log.Warning("GameApp entrance ignored because the module system is shutting down.");
        return;   // 关服竞态下直接放弃进入，避免 Init 静默失效
    }

    GameEventHelper.Init();                       // 第一行
    _hotfixAssembly = (List<Assembly>)objects[0];
    RootModule.BeforeShutdown += Release;
    Utility.Unity.AddDestroyListener(Release);
    StartGameLogic();
}
```

**实际机制**：`GameEventHelper` 由 Source Generator 生成（每个 `[EventInterface]` 接口生成一个 `{接口名}_Gen` 包装类），`Init()` 内逐个 `new {接口名}_Gen(...)` 并通过 `RegWrapInterface<T>` 注册。**实际后果**：遗忘 Init 时 `GameEvent.Get<T>()` 返回 `default(T)` 即 null，调用其成员直接抛 `NullReferenceException`（有堆栈可查，并非完全无报错）。

**排查方法**：`GameEvent.Get<T>()` 返回 null 时，先确认 `GameEventHelper.Init()` 是否已调用，其次确认调用时 `ModuleSystem.IsRunning == true`——`RegWrapInterface` 在框架非运行态会静默跳过注册。int/string 事件不受生成器影响，但 `AddEventListener` 同样要求 `ModuleSystem.IsRunning`，否则静默注册失败（返回 false，无日志）。

**同族坑**：接口分发只是同步遍历调用已注册的 Action，任一回调抛异常会原样向上传播并立即结束本次分发，排在异常回调之后的监听器本次收不到事件（框架不捕获、不补日志）。

---

### 反模式 5：接口缺少 [EventInterface] 特性

```csharp
// ❌ 错误：没有 [EventInterface]，Source Generator 不生成代码
public interface IBattleEvent
{
    void OnHpChanged(int hp);
}

// ✅ 正确：必须标注 [EventInterface] 并指定事件组
[EventInterface(EEventGroup.GroupLogic)]
public interface IBattleEvent
{
    void OnHpChanged(int hp);
}
```

**注意**：`EEventGroup` 由框架预定义（TEngine Runtime），当前仅 `GroupUI`、`GroupLogic` 两档，不能自造枚举值。生成器按接口名产出 `{接口名}_Event` 常量类（每个方法一个 `public static readonly int` 字段，初始化为 `RuntimeId.ToRuntimeId("...")`）和 `{接口名}_Gen` 包装类。

---

## 三、类型不匹配导致事件静默丢失

### 反模式 6：Send 与 AddEventListener 泛型参数不一致

```csharp
// ❌ 错误：发送 int，监听 float → 监听器被静默跳过（无异常、无日志）
GameEvent.Send<int>(IPlayerEvent_Event.OnHpChanged, 100);
AddUIEvent<float>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);

// ❌ 错误：发送 (int, string)，监听 (string, int) → 同样静默丢失
GameEvent.Send<int, string>(eventId, 1, "sword");
AddUIEvent<string, int>(eventId, OnItemAcquired);

// ✅ 正确：接口事件模式可在编译期发现此类问题
GameEvent.Get<IPlayerEvent>().OnHpChanged(100);  // 编译期类型检查
```

**实际行为**：分发时按委托具体类型匹配（`d is Action<TArg1>`），类型不符的监听器被直接跳过——不抛异常、不打日志。发送方看似成功，监听方永远收不到，比抛异常更难排查。

---

### 反模式 7：混用 int 和 string 事件 ID

```csharp
// ❌ 错误：注册用 int 字面量，发送用 string → 两个不同的运行时 ID
GameEvent.AddEventListener<int>(1001, OnHpChanged);   // 事件 ID = 1001
GameEvent.Send<int>("OnHpChanged", 50);               // RuntimeId 运行时递增分配，≠ 1001

// 上面两行不是同一个事件，监听收不到

// ✅ 正确：统一使用接口生成的常量
AddUIEvent<int>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);
GameEvent.Send<int>(IPlayerEvent_Event.OnHpChanged, 50);
```

**实际行为**：string 重载会经 `RuntimeId.ToRuntimeId` 映射为从 1 开始递增分配的运行时 ID，与手写 int 字面量几乎必然不同；两者最终走**同一个** int 事件表，并不存在两套路由。接口生成的常量同样是 `RuntimeId.ToRuntimeId("{接口名}_Event.{方法名}")`，注册与发送使用同一常量即可对齐。

---

## 四、生命周期时序陷阱

### 反模式 8：在 OnCreate 之前访问事件

```csharp
// UIWindow 实际生命周期顺序：Inject → ScriptGenerator → BindMemberProperty → RegisterEvent → OnCreate →（窗口 Prepare/显示时）OnRefresh
// RegisterEvent 在 OnCreate 之前执行，此时数据可能未就绪

// ❌ 错误：RegisterEvent 中直接访问需在 OnCreate 初始化的数据
protected override void RegisterEvent()
{
    AddUIEvent<int>(IPlayerEvent_Event.OnHpChanged, RefreshHp);
    RefreshHp(_currentHp);   // _currentHp 在 OnCreate 中初始化，此时为默认值
}

// ✅ 正确：RegisterEvent 只注册，OnRefresh 做刷新
protected override void RegisterEvent()
{
    AddUIEvent<int>(IPlayerEvent_Event.OnHpChanged, RefreshHp);
}

protected override void OnRefresh()
{
    RefreshHp(PlayerData.Hp);   // 每次 ShowUI 时刷新
}
```

---

### 反模式 9：在 Widget.OnDestroy 中访问父 Window 的事件

```csharp
// UIWindow.InternalDestroy 实际顺序：BlockUIEvents → RemoveAllUIEvent → 子 Widget 逆序 InternalDestroy → OnDestroy
// 子 Widget 内部同样先 RemoveAllUIEvent 再执行自己的 OnDestroy

// ❌ 错误：Widget 销毁时向父 Window 发送事件，但父 Window 已不再监听
public class ItemWidget : UIWidget
{
    protected override void OnDestroy()
    {
        GameEvent.Send(IItemEvent_Event.OnItemDestroyed);  // 发出去，但父 Window 已不再监听
    }
}

// ✅ 正确：Widget 间通信通过公开方法，或 Widget.OnDestroy 不依赖父 Window 事件
```

**附加坑**：进入关闭流程即 `BlockUIEvents()`，此后再调用 `AddUIEvent` 会直接抛 `ObjectDisposedException`（关闭中的 UI 禁止重新挂接事件），也不要期望在 `OnDestroy` 里补注册生效。

---

## 五、事件风暴反模式

### 反模式 10：事件回调中触发同类事件

```csharp
// ❌ 危险：OnGoldChanged 回调中又发送 OnGoldChanged → 嵌套递归分发
private void OnGoldChanged(int gold)
{
    _textGold.text = gold.ToString();
    if (gold > 1000)
    {
        GameEvent.Get<IPlayerEvent>().OnGoldChanged(gold - 100);  // 递归！
    }
}

// ✅ 正确：事件回调只更新 UI，业务逻辑由系统层控制
private void OnGoldChanged(int gold)
{
    _textGold.text = gold.ToString();
    // 不在回调中修改状态或触发新事件
}
```

**框架契约**：同事件回调中再 `Send` 属于嵌套分发，示例为有限次，但递归深度随 gold 线性增长，条件恒真即无限递归栈溢出。分发期间对同一事件的增删只在本次（含嵌套）分发结束后按调用顺序生效，非分发状态下立即生效。

---

### 反模式 11：高频事件直接更新 UI

```csharp
// ❌ 错误：每帧都发送 OnPositionChanged 并直接更新 UI，产生大量 UI 重绘
void Update()
{
    GameEvent.Get<IHeroEvent>().OnPositionChanged(_hero.position);
}

// ✅ 正确：高频数据用 Timer 轮询或 OnUpdate 节流
int _timerId;

protected override void OnCreate()
{
    // TimerHandler 签名为 void(object[] args)，回调必须带 object[] 参数
    _timerId = GameModule.Timer.AddTimer(RefreshPosition, time: 0.1f, isLoop: true);
}

protected override void OnDestroy()
{
    GameModule.Timer.RemoveTimer(_timerId);
}

private void RefreshPosition(object[] args) => _textPos.text = _hero.position.ToString();
```

**注意**：框架未运行或已关服时 `AddTimer` 返回 0（无效 ID），不要把 0 当有效定时器使用。

---

## 六、重复注册与无效移除的 Fatal 日志

### 反模式 12：重复注册同一监听或移除不存在的监听

```csharp
// ❌ 错误：同一 handler 对同一事件重复注册 → Log.Fatal("Repeated Add Handler")，第二次注册不生效
GameEvent.AddEventListener<int>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);
GameEvent.AddEventListener<int>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);

// ❌ 错误：移除从未注册（或已移除）的 handler → Log.Fatal("Delete handle failed, not exist, EventId: ...")
GameEvent.RemoveEventListener<int>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);

// ✅ 正确：注册/移除成对出现，用返回值确认注册结果
private bool _hpListenerAdded;

public void Init()
{
    _hpListenerAdded = GameEvent.AddEventListener<int>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);
}

public void Dispose()
{
    if (_hpListenerAdded)
    {
        GameEvent.RemoveEventListener<int>(IPlayerEvent_Event.OnHpChanged, OnHpChanged);
        _hpListenerAdded = false;
    }
}
```

**实际行为**：`AddHandler` 检测到同一事件下重复委托时打印 `Log.Fatal("Repeated Add Handler")` 并返回 false——经 `GameEventMgr`/`AddUIEvent` 注册时因返回 false 不记账，失败的那次不会进入待清理列表；直接调用 `GameEvent.AddEventListener` 时若忽略 bool 返回值，会误以为第二次注册成功（实际事件表里仍只有一个监听）。`RmvHandler` 找不到对应委托时打印 `Log.Fatal("Delete handle failed, not exist, EventId: ...")`。两者都只打日志不抛异常，但 Fatal 级日志会污染控制台和日志文件，排查事件问题时可优先搜索这两条固定文案。

---

## 七、不存在的 API（AI 常见幻觉）

```csharp
// ❌ 以下 API 均不存在，编译失败：
GameEvent.UnRegisterAll();                    // 不存在
GameEvent.UnRegisterAll<int>(eventId);        // 不存在
GameEvent.RegisterListener<ITrade>(impl);     // 不存在（由 Source Generator 处理）
GameEvent.ClearAll();                         // 不存在
GameEvent.RemoveAll(eventId);                 // 不存在

// ✅ 正确替代：
GameEvent.RemoveEventListener(eventId, handler);   // 移除单个
GameEventMgr.Clear();                              // 局部批量清除
GameEvent.Shutdown();                              // 全局清除（框架关闭/新会话重置时由框架调用）
```

---

## 交叉引用

| 主题 | 文档 |
|------|------|
| 事件系统核心 API | [event-system.md](event-system.md) |
| UIWindow 生命周期与 AddUIEvent | [ui-lifecycle.md](ui-lifecycle.md) |
| Timer 替代高频事件 | [modules.md](modules.md) |
| 问题排查 | [troubleshooting.md](troubleshooting.md) |
