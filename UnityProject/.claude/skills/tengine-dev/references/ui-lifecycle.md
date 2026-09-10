# UI 生命周期与核心 API

> **适用场景**：UIWindow/UIWidget 生命周期、层级（UILayer）、ShowUI/CloseUI/HideUI API、ScriptGenerator 节点绑定 | **关联文档**：[event-system.md](event-system.md)（AddUIEvent）、[ui-patterns.md](ui-patterns.md)（Widget 模板）、[naming-rules.md](naming-rules.md)（节点前缀）

---

## 一、核心 API

### UILayer 层级

| 值 | 层级 | 用途 |
|----|------|------|
| 0 | Bottom | 底层（世界空间 UI、背景）|
| 1 | UI | 普通 UI 层（主要界面）|
| 2 | Top | 顶层（弹窗、全屏遮罩）|
| 3 | Tips | 提示层（Toast、飘字）|
| 4 | System | 系统层（加载中、异常提示）|

**Depth 排序规则**（`OnSortWindowDepth`）：窗口 Canvas 的 `sortingOrder = WindowLayer * LAYER_DEEP(2000)`，同层窗口按入栈顺序递增 `WINDOW_DEEP(100)`；后入栈的窗口 Depth 更大（显示在上层）。

### WindowAttribute 窗口标记

每个 UIWindow 子类必须标记 `[Window]` 特性：

```csharp
[Window(UILayer.UI, "BattleMainUI")]
public class BattleMainUI : UIWindow { }

[Window(UILayer.Top, "LoginUI", fullScreen: true, hideTimeToClose: 10)]
public class LoginUI : UIWindow { }
```

- **location** 即 Prefab 资源地址（`AssetRaw/UI/` 下的 Prefab 文件名，不含扩展名）；省略时默认使用窗口类名（如 `LoginUI` → `LoginUI.prefab`）。
- **hideTimeToClose** 为 `int` 类型（秒），默认 `10`；`<= 0` 时 HideUI 直接关闭窗口。
- 构造函数第一个参数名为 `windowLayer`（命名传参时写 `windowLayer:`，不要写 `layer:`）；资源地址参数名为 `location`。
- 共 4 个重载：`(int|UILayer windowLayer, string location, ...)`、`(UILayer windowLayer, bool fromResources, ...)`、`(UILayer windowLayer, bool fromResources, string location, ...)`；`fromResources: true` 时 Resources 内部资源无需 AB 加载。

**常用属性**（UIWindow）：

| 属性 | 类型 | 说明 |
|------|------|------|
| `WindowName` | string | 窗口名（窗口类 FullName） |
| `WindowLayer` | int | 所属 UILayer |
| `AssetName` | string | Prefab 资源定位地址 |
| `FullScreen` | bool | 全屏窗口（遮挡下层窗口显示） |
| `FromResources` | bool | 是否 Resources 内部资源 |
| `HideTimeToClose` | int | 隐藏后自动关闭秒数 |
| `Canvas` | Canvas | 窗口根 Canvas（框架用于排序与显隐切换） |
| `GraphicRaycaster` | GraphicRaycaster | 窗口射线交互组件（随窗口显隐启停） |
| `Depth` | int | Canvas sortingOrder（由框架 OnSortWindowDepth 排序驱动，勿手动设置） |
| `Visible` | bool | 窗口可见性（驱动 OnSetVisible；实现为切换 Canvas gameObject 的 layer：Show=5(UI) / Hide=2(Ignore Raycast)） |
| `IsHide` | bool | 是否处于隐藏待关闭状态 |

**窗口实例实用方法**：

- `SetUIFit(RectTransform fitRect, bool liuHaiFit = true, float topSpacing = 0, bool bottomFit = true, float bottomSpacing = 0)`：刘海屏/安全区适配；`SetUINotFit(rect)` 让指定 RectTransform 不受适配影响；`SetUINotFit(rect, refRect)` 指定不受某个参照 RectTransform 影响。
- 全局安全区：`UIModule.ApplyScreenSafeRect(Rect safeRect)`（按屏幕像素设置 UIRoot 安全区）、`UIModule.SimulateIPhoneXNotchScreen()`（模拟 iPhoneX 异形屏），均为静态方法。

### UIWindow 生命周期

```
ShowUIAsync<T>(userDatas) / ShowUI<T>() / ShowUIAsyncAwait<T>()
    │
    ▼
CreateInstance()          ← new T()，解析 [Window] 特性 → Init()（WindowName/WindowLayer/AssetName/FromResources/FullScreen/HideTimeToClose）
    │
    ▼
Push() 入栈 → 加载 Prefab  ← LoadGameObjectAsync(AssetName)（同步 ShowUI 走 LoadGameObject）
    │                        创建超时（UIModule.CreationTimeoutSeconds，默认 60s）抛 UIWindowTimeoutException，失败抛 UIWindowLoadException
    ▼
OnWindowPrepare()（加载完成后由框架回调，重复 Show 时也走此入口）：
    ├── InternalCreate()   ← 仅首次（_isCreate 标记）：
    │       Inject()               ← 依赖注入扩展点（UIBase.Injector 静态委托）
    │       ScriptGenerator()      ← 绑定 UI 节点引用
    │       BindMemberProperty()   ← 框架预留扩展点，通常跳过
    │       RegisterEvent()        ← 注册 UI 事件（随窗口销毁自动清理）
    │       OnCreate()             ← 窗口创建初始化
    ├── InternalRefresh()  ← 每次 Show 都执行 → OnRefresh()（刷新显示数据）
    ├── OnSortWindowDepth() ← 重排同层窗口 Depth → 触发 OnSortDepth()
    └── OnSetWindowVisible() ← 计算可见性（全屏窗口遮挡下层）→ 触发 OnSetVisible(true)
    │
    ▼
每帧：UIModule.OnUpdate → window.InternalUpdate()
    仅当 IsPrepare 且 Visible 时执行 → OnUpdate()（窗口自身每帧均调用；子 Widget 由父级更新列表惰性筛选）
    │
    ▼
HideUI()：window.Visible = false → 触发 OnSetVisible(false)
    ├── HideTimeToClose <= 0：直接进入关闭流程
    └── HideTimeToClose > 0：挂 GameModule.Timer 定时器，超时后 CloseWindow
CloseUI() 关闭流程：
    Pop 出栈 → BeginClose()（状态置 Closing、BlockUIEvents、取消生命周期 Token）
        │
        ▼
    重排同层 Depth（OnSortWindowDepth）→ 刷新可见性（OnSetWindowVisible）
        │
        ▼
    RemoveAllUIEvent()     ← 自动清理所有 UI 事件监听
        │
        ▼
    子Widget.InternalDestroy()（倒序，各自 RemoveAllUIEvent → 其 OnDestroy → Destroy 其 gameObject）
        │
        ▼
    OnDestroy()            ← 窗口自身销毁前清理
        │
        ▼
    Object.Destroy(panel)  ← 销毁 GameObject（实例化资源随 Destroy 自动卸载，无需 UnloadAsset）
```

**关键规则**：
- `Inject` / `ScriptGenerator` / `BindMemberProperty` / `RegisterEvent` / `OnCreate` 只执行一次（`_isCreate` 标记）
- `OnRefresh` 每次 Show 都执行；`OnSortDepth` / `OnSetVisible` 在每次 Prepare 与显隐切换时触发
- 窗口 `OnUpdate` 在 IsPrepare 且 Visible 时每帧都会被调用；基类实现仅设 `_hasOverrideUpdate = false`。`_hasOverrideUpdate` 的实际作用是惰性筛选子 Widget：只有 override 了 `OnUpdate` 的 Widget 会进入父级的持续更新列表（每帧调用），未 override 的 Widget 仅在框架重建列表那一帧被调用一次；因此 Widget 的 `OnUpdate` override 中不要调用 `base.OnUpdate()`；Widget 创建/销毁会触发 `SetUpdateDirty` 重建列表
- 窗口隐藏（Visible=false）时不执行 `OnUpdate`；尽量避免使用，改用 `GameModule.Timer`
- 生命周期销毁回调为 `OnDestroy()`，不存在 `OnClose` 回调；窗口实例内有 `protected virtual void Close()` / `Hide()` 便捷方法（内部转调 `UIModule.CloseUI/HideUI`）
- 窗口关闭开始后（`BeginClose`）事件注册与子 Widget 创建均被禁止（`IsLifecycleInvalid`）
- 完整前缀表见 [naming-rules.md](naming-rules.md#ui-节点命名规范)

### 扩展虚方法

| 方法 | 签名 | 用途 |
|------|------|------|
| `OnSortDepth` | `protected virtual void OnSortDepth()` | 窗口层级排序回调（Depth 变化或从隐藏恢复显示时触发，仅 Visible 状态） |
| `OnSetVisible` | `protected virtual void OnSetVisible(bool visible)` | 窗口显隐回调（Visible 属性变更时触发：Show/Hide/被全屏窗口遮挡） |

### UIModule 核心 API

```csharp
// 打开窗口
GameModule.UI.ShowUIAsync<BattleMainUI>();                       // 异步打开，fire and forget
var win = await GameModule.UI.ShowUIAsyncAwait<BattleMainUI>();  // 异步打开并等待实例
GameModule.UI.ShowUI<BattleMainUI>();                            // 同步打开（资源同步加载）
GameModule.UI.ShowUIAsync<ItemDetailUI>(itemId, extraData);      // 携带用户数据（UserData 取第一个，UserDatas 取全部）

// 关闭 / 隐藏
GameModule.UI.CloseUI<BattleMainUI>();          // 关闭并销毁
GameModule.UI.HideUI<BattleMainUI>();           // 隐藏（HideTimeToClose > 0 时超时自动关闭，否则直接关闭）
GameModule.UI.CloseAll();                       // 关闭所有
GameModule.UI.CloseAllWithOut<BattleMainUI>();  // 保留指定窗口

// 查询 / 获取
bool exists     = GameModule.UI.HasWindow<BattleMainUI>();
bool loading    = GameModule.UI.IsAnyLoading();
string topName  = GameModule.UI.GetTopWindow();                     // 全栈顶部窗口名（重载 GetTopWindow(int layer) 可指定层级）
var win2        = await GameModule.UI.GetUIAsyncAwait<BattleMainUI>(); // 异步获取已存在窗口（不主动打开，不存在返回 null）
GameModule.UI.GetUIAsync<BattleMainUI>(win => { });              // 回调式获取已存在窗口
CloseUI/HideUI/ShowUI/ShowUIAsync 亦支持 Type 重载：GameModule.UI.CloseUI(typeof(BattleMainUI))
```

### UIWidget 子组件

```csharp
var widget = CreateWidget<ItemWidget>("path/to/node");                     // 按父 UI 下的节点路径查找
var widget = CreateWidget<ItemWidget>(parentTrans, "path/to/node");        // 指定起始 Transform 查找
var widget = CreateWidget<ItemWidget>(goRoot);                             // 直接挂到已有 GameObject
var widget = CreateWidgetByPath<ItemWidget>(parent, "Location");           // 按资源地址加载（同步）
var widget = await CreateWidgetByPathAsync<ItemWidget>(parent, "Loc");     // 按资源地址加载（异步）
var widget = CreateWidgetByPrefab<ItemWidget>(prefab, parent);             // Prefab 克隆
var widget = CreateWidgetByType<ItemWidget>(parent);                       // 以类型名作为资源地址
var widget = await CreateWidgetByTypeAsync<ItemWidget>(parent);            // 以类型名作为资源地址（异步）
AdjustIconNum<ItemWidget>(listIcon, items.Count, parent, prefab);          // 调整列表数量（同步）
AsyncAdjustIconNum<ItemWidget>(listIcon, items.Count, parent, assetPath: "Loc"); // 调整列表数量（异步、分帧，可传 maxNumPerFrame/updateAction）
widget.Destroy();                                                          // 主动销毁单个 Widget
```

UIWidget 创建链（`CreateImp` 内一次性连续执行）：`Inject → ScriptGenerator → BindMemberProperty → RegisterEvent → OnCreate → OnRefresh`。

与 UIWindow 的差异：
- Widget 创建时立即依次执行到 `OnRefresh`（一次完成），而非等下一次 Show 才刷新
- 销毁链与窗口一致：`RemoveAllUIEvent → 子Widget 倒序销毁 → OnDestroy → Destroy(gameObject)`
- Widget 的 `OnUpdate` 同样靠 `_hasOverrideUpdate` 惰性筛选：只有 override（且不调 base）的 Widget 会被父级持续每帧调用；Widget 自身 `Visible` 即 `gameObject.activeSelf`

---

## 二、使用模式

### UI 内部事件（AddUIEvent）

在 `RegisterEvent()` 中使用，事件监听随窗口销毁**自动清理**：

```csharp
protected override void RegisterEvent()
{
    // 事件 ID 来自 [EventInterface] 接口生成的 Xxx_Event 类静态字段：
    // 接口 ILoginUI → 生成 ILoginUI_Event，成员为 public static readonly int <方法名>
    // 无泛型参数（ILoginUI 的 ShowLoginUI/CloseLoginUI 均为无参方法，
    // 泛型数量须与接口方法参数数量一致，带参事件示例见 event-system.md）
    AddUIEvent(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
    AddUIEvent(ILoginUI_Event.CloseLoginUI, OnCloseLoginUI);
}
```

**AddUIEvent 支持 0~4 个泛型参数**：`AddUIEvent(int eventType, Action handler)`（public）、`AddUIEvent<T>`、`AddUIEvent<T,U>`、`AddUIEvent<T,U,V>`、`AddUIEvent<T,U,V,W>`（protected）。首个参数为 `int eventType`，泛型数量须与事件接口方法的参数数量一致（由 SourceGenerator 生成，详见 event-system.md）。

### 典型窗口实现

```csharp
[Window(UILayer.UI, "BagUI")]
public class BagUI : UIWindow
{
    private List<ItemWidget> _items = new();

    protected override void ScriptGenerator()
    {
        // 绑定 UI 节点引用（UI 绑定工具生成，前缀 m_，用 FindChildComponent/FindChild）
        _rectContainer = FindChildComponent<RectTransform>("m_rectContainer");
        _goItemRoot = FindChild("m_rectContainer/m_goItemRoot").gameObject;
    }

    protected override void RegisterEvent()
    {
        // 事件类须来自工程内真实定义的 [EventInterface] 接口（如 ILoginUI → ILoginUI_Event）
        AddUIEvent(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
    }

    protected override void OnCreate()
    {
        // 一次性初始化（创建 Widget 等）
    }

    protected override void OnRefresh()
    {
        // 每次 Show 时刷新数据
    }

    // 仅在需要每帧更新时 override；Widget 不 override（或调用了 base.OnUpdate）不会进入每帧更新列表
    // protected override void OnUpdate() { }

    protected override void OnDestroy()
    {
        // 销毁前清理（子 Widget 会先于此方法销毁）
    }

    private void OnShowLoginUI()
    {
    }
}
```

---

## 三、常见错误

| 错误 | 正确做法 |
|------|---------|
| 在 `RegisterEvent` 外使用 `GameEvent.AddEventListener` | 使用 `AddUIEvent`（自动随窗口销毁清理），详见 [event-system.md](event-system.md#常见错误) |
| override `OnUpdate` 但忘记实际需要每帧更新 | 优先使用 `GameModule.Timer`，避免不必要的每帧计算 |
| override `OnUpdate` 时调用 `base.OnUpdate()` | 基类实现把 `_hasOverrideUpdate` 置 false，该 Widget 会退出父级每帧更新列表（窗口自身 `OnUpdate` 仍每帧执行，但请保持不调 base 的写法） |
| 使用不存在的 `OnClose` 方法 | 销毁回调为 `OnDestroy()`；主动关闭用 `Close()`（UIModule.CloseUI） |
| 在 `OnDestroy` 中访问子 Widget | 子 Widget 的 `OnDestroy` 先于窗口 `OnDestroy` 执行，此时子 Widget 已销毁 |
| 在窗口关闭开始后创建 Widget / 注册事件 | `BeginClose` 后 `IsLifecycleInvalid` 为 true，会抛 `ObjectDisposedException` |
| `[Window]` 命名参数写 `layer:` 或 `hideTimeToClose: 10f` | 参数名为 `windowLayer`，`hideTimeToClose` 为 `int` |

---

## 四、交叉引用

| 主题 | 文档 |
|------|------|
| 事件系统详细用法与避坑 | [event-system.md](event-system.md) |
| UI 进阶模式（Widget 模板/节点绑定） | [ui-patterns.md](ui-patterns.md) |
| 资源加载与生命周期 | [resource-api.md](resource-api.md) |
| 命名规范与节点前缀 | [naming-rules.md](naming-rules.md) |
| 模块 API（Timer 等） | [modules.md](modules.md) |
