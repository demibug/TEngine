# FairyGUI (FGUI) 开发指导

> **适用场景**：FguiWindow 生命周期、FguiModule API、FGUI/UGUI 双体系关系、FguiLifetimeScope 事件绑定、Gen/Imp 代码生成、外部资源 GLoader、包加载与释放 | **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（UGUI UIWindow 对照）、[event-system.md](event-system.md)（AddUIEvent）、[resource-api.md](resource-api.md)（资源 API 对照）

---

## 一、体系总览（FGUI 与 UGUI 并存，非切换）

本项目 **UGUI 与 FGUI 是双 UI 体系**（并存，无宏开关，无 FAIRYGUI 宏）：

| 维度 | UGUI 体系 | FGUI 体系 |
|------|----------|----------|
| 门面 | `GameModule.UI`（UIModule） | `GameModule.FGUI`（FguiModule） |
| 窗口基类 | `UIWindow`（Prefab + ScriptGenerator 节点绑定） | `FguiWindow`（组合 GComponent，**不继承 UIWindow**） |
| 层级 | `UILayer`（Canvas sortingOrder 分层） | `FguiLayer`（GRoot 下 5 个 GComponent 容器层） |
| 渲染 | UIRoot 相机 depth=2、Layer=`UI` | Stage 相机 depth=3、专用 Layer=`FairyGUI`（**整体叠在业务 UGUI 之上**） |
| 模态 | UIStack 处理 | UGUI 侧 shield 遮罩 + FGUI 模态计数 |
| 程序集 | GameLogic（热更） | `TEngine.FairyGUI`（AOT 桥接）+ GameLogic（热更） |

**关键约定**：
- FGUI **显式异步初始化**：不调用 `GameModule.FGUI.InitializeAsync` 就完全不生效；初始化失败仅记日志，UGUI 不受影响（`GameApp.InitializeFairyGuiAsync`）。`GameModule.FGUI` 在模块系统未运行时返回 `null`。
- 未初始化时调用 `ShowAsync` 抛 `InvalidOperationException`；模块关闭中调用任何 API 抛 `ObjectDisposedException`。
- **依赖方向**：GameLogic（热更）→ TEngine.FairyGUI（AOT）→ FairyGUI SDK + TEngine.Runtime；`Launcher`/`GameUpdater` 不引用 FairyGUI；`TEngine.Runtime` 本身不引用 FairyGUI。

### 程序集与目录

| 程序集 | 路径 | 说明 |
|--------|------|------|
| `FairyGUI` | `Assets/ThirdParty/FairyGUI/Scripts` | 官方 SDK v5.2.0（锁定 commit，见 VERSION.md，vendor 源码未修改） |
| `TEngine.FairyGUI` | `Assets/TEngine/Extensions/FairyGUI/Runtime` | AOT 桥接：`FguiSettings` / `FguiPackageCatalog` / `FguiRuntimeHost` |
| `TEngine.FairyGUI.Editor` | `Assets/TEngine/Extensions/FairyGUI/Editor` | Catalog 重建/校验菜单、YooAsset 地址规则 `FguiAddressByRelativePath` |
| `GameLogic`（热更） | `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/` | FguiModule / FguiWindow / FguiWidget / FguiLifetimeScope / FguiWindowDescriptor / FguiPackageService / FguiResourceProvider / FguiExternalLoader / FguiExceptions |
| 窗口代码 | `Assets/GameScripts/HotFix/GameLogic/UI/FGUI/` | **`Gen/` = 生成代码，`Imp/` = 手写控制器** |

- AOT 裁剪保护：`Assets/TEngine/Extensions/FairyGUI/Runtime/link.xml` 中 preserve `FairyGUI` 与 `TEngine.FairyGUI`（HybridCLR 热更程序集的引用无法保护 AOT 程序集，须在 AOT 侧声明）。

### 退出清理链路（设计决策）

- `ModuleSystem.Shutdown` 分 8 阶段执行（StopWork → StopProcedures → **BeforeShutdown** → ShutdownModules → ReturnResourceInstances → ShutdownObjectPools → FinalizeResources → ClearState）；其中 BeforeShutdown 阶段由 `RootModule` 派发静态事件 `RootModule.BeforeShutdown`——**一次性**（先摘除委托表再逐个调用，重入/晚订阅无效）、**逐订阅者异常隔离**（单个监听抛错记录 `ShutdownError` 后继续，不影响其余监听）。
- FGUI Host 订阅 `RootModule.BeforeShutdown` 在模块 Shutdown 前同步清理（同步取消/释放，幂等兜底）；**不要**把 FGUI 退出绑定在 `GameApp` 的 destroy listener 上（可能被 UpdateDriver.Release 提前清空）。
- **清理边界**：FGUI Shutdown 只释放自己拥有的对象、包与注册；不调用 `GameEvent.Shutdown()`，不还原 `UIObjectFactory` 的其他用户注册（仅 Reset 自己注册的 loader 扩展）。

### FguiLayer 层级

| 值 | 层 | 用途 |
|----|-----|------|
| 0 | Bottom | 底层 |
| 1 | UI | 普通窗口（descriptor 默认值） |
| 2 | Top | 弹窗 |
| 3 | Tips | 提示 |
| 4 | System | 系统 |

- 与 UGUI `UILayer` **同名意图、完全独立**的两套枚举；两套层互不交叉。
- 层实现：`FguiRuntimeHost` 在 GRoot 下创建 5 个空 `GComponent` 容器（命名 `TEngine.Bottom` 等）；窗口 `AddChild` 进层，**AddChild 顺序决定同层内排序**。
- 同层置顶：模块对已存在窗口 Show 时自动 `BringToFront`（`View.parent.SetChildIndex(View, numChildren-1)`）。
- **禁止合并渲染 Layer**：FGUI Stage 相机 cullingMask 仅含 `FairyGUI` Layer，UGUI 相机含 `UI` Layer；混用会导致两相机渲染同一网格。

---

## 二、核心 API

### FguiModule（经 `GameModule.FGUI` 访问）

```csharp
// 初始化（必须在任何窗口操作前 await 完成；幂等，重复调用直接返回）
// 完整签名：InitializeAsync(string settingsAddress, CancellationToken ct = default,
//     string yooAssetPackageName = "DefaultPackage")
await GameModule.FGUI.InitializeAsync("FGUI/FguiSettings.asset", ct);
// 内部：new FguiResourceProvider(GameModule.Resource) → 加载 FguiSettings → 创建 FguiPackageService
//      + FguiRuntimeHost + 配置 FguiExternalLoader；失败全量回滚

// 注册窗口（重复注册抛 InvalidOperationException；descriptor.BindGeneratedTypes 会被自动调用）
// 示例类与包名来自 Imp/FguiSampleRegistration.cs 的真实注册
GameModule.FGUI.Register<BundleUsageFguiWindow>(FguiWindowDescriptor.Create(
    () => new BundleUsageFguiWindow(), "BundleUsage", "BundleUsage", "Main", FguiLayer.UI));

// 打开：返回窗口实例
BundleUsageFguiWindow win = await GameModule.FGUI.ShowAsync<BundleUsageFguiWindow>(userData, ct);

// 关闭 / 隐藏
GameModule.FGUI.Hide<BundleUsageFguiWindow>();    // 仅置不可见，保留窗口与包 lease（不会自动超时关闭）
GameModule.FGUI.Close<BundleUsageFguiWindow>();   // 销毁窗口并释放包/资源 lease
GameModule.FGUI.CloseAll();

// 查询
bool open       = GameModule.FGUI.IsOpen<BundleUsageFguiWindow>();
bool registered = GameModule.FGUI.IsRegistered<BundleUsageFguiWindow>();
bool inited     = GameModule.FGUI.IsInitialized;  // Shutdown 后为 false
int leases      = GameModule.FGUI.ActiveResourceLeaseCount;  // 当前活跃资源 lease 数（调试用）

// 其他
using (GameModule.FGUI.SuspendPresentation()) { /* UGUI 独占系统界面时暂停 FGUI 表现（幂等 lease） */ }
FguiPackageLease pin = await GameModule.FGUI.PinPackageAsync("Common", ct); // 包+依赖闭包常驻（lease.Package 可取 UIPackage）
```

### FguiWindowDescriptor

```csharp
FguiWindowDescriptor.Create<TWindow>(
    Func<TWindow> factory,      // 窗口工厂
    string packageKey,          // FguiPackageCatalog 中的逻辑 key（如 "BundleUsage"）
    string packageName,         // FGUI 包名（UIPackage.CreateObject 第 1 参）
    string componentName,       // 组件名（UIPackage.CreateObject 第 2 参）
    FguiLayer layer = FguiLayer.UI,
    bool modal = false,         // 模态：UGUI shield 全阻挡 + 取消 UGUI 焦点（计数式）
    Action bindGeneratedTypes = null)  // 注册时调用（如绑定生成类型）
```

### FguiWindow 生命周期

```
GameModule.FGUI.ShowAsync<T>(userData, ct)
    │
    ▼
EnsureInitialized()            ← 未初始化抛 InvalidOperationException
    │
    ├── 已存在窗口（_windows 命中）：
    │     InternalSetVisible(true) → OnSetVisible(true)
    │     EnqueueRefreshAsync(userData) → 串行排队（按调用顺序）→ OnRefreshAsync()
    │     BringToFront()（同层置顶）
    │     └ 返回已存在实例；OnRefreshAsync 抛异常会关闭窗口并抛出
    │
    ▼ 首次创建（同类型并发 Show 合并：共享同一次创建，首个 userData 生效，
    │          取消一个等待者不影响其他等待者）
AcquirePackageAsync(descriptor.PackageKey)   ← 包引用计数 + 递归依赖闭包
    │                                          超时 LoadTimeoutSeconds 抛 FguiTimeoutException
    ▼
UIPackage.CreateObject(PackageName, ComponentName)  ← 结果必须为 GComponent，否则 FguiLoadException("window-view")
    │
    ▼
descriptor.Factory() → window.InternalAttach(view, packageLease)
    │
    ▼
host.GetLayer(descriptor.Layer).AddChild(view)   ← 挂到 FguiLayer 容器层（新窗口天然置顶）
    │
    ▼
window.InternalCreateAsync(userData, token)
    ├── OnCreateAsync(userData, ct)   ← 仅首次
    └── OnRefreshAsync(userData, ct)  ← 首次创建也执行
    │
    ▼
_windows 登记 → InternalSetVisible(true) → 模态登记（modal: true 时 host.SetModalActive(true)）
    │
    ▼
Hide<T>()：解除模态登记 → View.visible=false + touchable=false → OnSetVisible(false)（保留实例与包 lease）
Close<T>()：
    BeginDestroy（置销毁标记，后续创建/刷新被拒）
        │
        ▼
    Lifetime.Dispose()      ← GameEvent / FGUI 事件 / Timer / 清理动作全解绑（逆序）
        │
        ▼
    子 Widget 逆序销毁（各自 Lifetime.Dispose → OnDestroy）
        │
        ▼
    OnDestroy()             ← 窗口自身清理（View 此时尚未 Dispose，子 Widget 已销毁）
        │
        ▼
    View.Dispose() → 包 lease.Dispose()   ← GComponent 与包引用一并释放
```

**关键规则**：
- 只有 **4 个钩子**：`OnCreateAsync`（仅首次）、`OnRefreshAsync`（首次 + 每次 Show）、`OnSetVisible(bool)`、`OnDestroy()`——**没有 OnUpdate**，每帧逻辑用 `Lifetime.AddTimer` 或 `GameModule.Timer`。
- 钩子均带 `CancellationToken`（窗口生命周期令牌 + 调用方令牌 + 超时令牌的组合）；窗口关闭后 `Token` 取消，异步逻辑应响应取消。
- `Hide` 不会自动关闭（**与 UGUI `HideTimeToClose` 行为不同**），需显式 `Close`。
- `OnRefreshAsync` 串行排队执行（`_refreshTail` 链保证按 Show 调用顺序刷新）。
- 创建/刷新失败全量回滚：view Dispose + 包 lease 释放，不泄漏。
- 异常类型：`FguiLoadException`（带 `Stage`（如 "window-create"/"window-view"/"window-refresh"/"package-load"）、`PackageKey`、`Location`）、`FguiTimeoutException : TimeoutException`。
- 超时：`FguiSettings.LoadTimeoutSeconds`（默认 30s，下限 1s），作用于窗口创建与包加载。
- `internal` 方法（InternalAttach/InternalCreateAsync/InternalRefreshAsync/InternalSetVisible/InternalDestroy/BeginDestroy）为模块内部调用，子类**不要调用、不要 override**。

### FguiWidget 子组件

```csharp
var widget = CreateWidget<MyItemWidget>(TypedView.m_slotCom);  // 参数为子 GComponent
```

- `FguiWidget` 钩子是同步 `OnCreate()` / `OnDestroy()`；自带 `View`（传入的 GComponent）与 `Lifetime`。
- 窗口销毁时子 Widget **逆序**自动销毁，无需手动管理。
- Widget 的 `OnCreate` 中即可用 `Lifetime.AddListener/AddUIEvent/AddTimer` 登记事件（自动清理）。

### FguiLifetimeScope（事件统一入口，自动清理）

窗口/Widget 实例的 `Lifetime` 属性即 `FguiLifetimeScope`，窗口销毁时全部登记**自动解绑**：

```csharp
protected override UniTask OnCreateAsync(object userData, CancellationToken ct)
{
    // 1. FairyGUI 事件（onClick/onTouchBegin 等 EventListener）
    Lifetime.AddListener(TypedView.m_btnClose.onClick, (EventCallback0)OnCloseClick);
    // 2. TEngine GameEvent（与 UGUI AddUIEvent 同名 API，支持 0~4 个泛型参数；
    //    事件 ID 来自 [EventInterface] 生成类 {接口名}_Event，工程内置如 ILoginUI_Event）
    Lifetime.AddUIEvent(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
    // 3. Timer（自动 RemoveTimer；回调签名 TimerHandler = void(object[] args)）
    Lifetime.AddTimer(OnTick, 1f, loop: true);
    // 4. 任意 IDisposable / 清理动作
    Lifetime.Add(myDisposable);
    Lifetime.AddCleanup(() => { /* 自定义清理 */ });
    return UniTask.CompletedTask;
}
```

- **FGUI 的 onClick 与 TEngine GameEvent 是两条独立通道**，不互相转换；统一经 Lifetime 登记获得自动清理。
- `AddUIEvent` 事件 ID 来自 `[EventInterface]` 生成的 `IXxx_Event` 类（与 UGUI 相同，详见 event-system.md）。
- `Lifetime.Token` 可用于异步操作取消（窗口销毁后自动取消）。

---

## 三、使用模式

### 初始化与注册入口（参照 `Imp/FguiSampleRegistration.cs`）

```csharp
public static class FguiSampleRegistration
{
    public const string SettingsAddress = "FGUI/FguiSettings.asset";

    public static async UniTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await GameModule.FGUI.InitializeAsync(SettingsAddress, cancellationToken);  // 1. 初始化模块
        FguiGeneratedBinder.BindAll();                                              // 2. 绑定所有生成类型（必须！）
        if (!GameModule.FGUI.IsRegistered<BundleUsageFguiWindow>())                 // 3. 注册窗口（防御重复）
        {
            GameModule.FGUI.Register<BundleUsageFguiWindow>(FguiWindowDescriptor.Create(
                () => new BundleUsageFguiWindow(), "BundleUsage", "BundleUsage", "Main", FguiLayer.UI));
        }
        if (!GameModule.FGUI.IsRegistered<ModalWaitingFguiWindow>())
        {
            GameModule.FGUI.Register<ModalWaitingFguiWindow>(FguiWindowDescriptor.Create(
                () => new ModalWaitingFguiWindow(), "ModalWaiting", "ModalWaiting", "Main", FguiLayer.Top,
                modal: true));
        }
    }
}
```

启动接入（GameApp.StartGameLogic）：`InitializeFairyGuiAsync().Forget()`，内部 try/catch 失败仅 `Log.Error`，不影响 UGUI；当前入口内部调用 `FguiSampleRegistration.ShowCoexistenceSampleAsync()`（初始化并打开 FGUI/UGUI 共存示例窗口）。

### 典型窗口实现

```csharp
public sealed class BundleUsageFguiWindow : FguiWindow
{
    public UI_BundleUsageMain TypedView => (UI_BundleUsageMain)View;  // 类型化视图（强转生成类）

    protected override UniTask OnCreateAsync(object userData, CancellationToken ct)
    {
        View.Center();   // ModalWaitingFguiWindow 的真实写法
        // 或全屏跟随层尺寸（BundleUsageFguiWindow 的真实写法）：
        // View.SetSize(View.parent.width, View.parent.height);
        // View.AddRelation(View.parent, RelationType.Size);
        // 事件绑定（示意成员，编辑器中命名 m_xxx 后生成）：
        // Lifetime.AddListener(TypedView.m_btnClose.onClick, OnCloseClick);
        return UniTask.CompletedTask;
    }

    protected override UniTask OnRefreshAsync(object userData, CancellationToken ct)
    {
        // 每次 Show 都执行；返回 UniTask 支持异步刷新（未完成前 ShowAsync 不返回）
    }

    protected override void OnSetVisible(bool visible) { }

    protected override void OnDestroy()
    {
        // 子 Widget 已销毁；View 尚未 Dispose（还可读取，但不要再创建内容）
    }
}
```

### 访问子组件（`getMemberByName: false` 模式）

- 生成配置：`classNamePrefix: "UI_"`、`memberNamePrefix: "m_"`、`getMemberByName: false`。
- 编辑器中**给组件命名**（m_xxx 前缀）→ 生成类型化成员（如 `m_btnClose`）。
- 未命名组件用运行时查询 + 转型：

```csharp
GTextField text = TypedView.GetChild("n1").asTextField;
GButton btn    = TypedView.GetChild("m_btnOk").asButton;
GComponent com = TypedView.GetChild("m_slot").asCom;
```

- 需要复用逻辑的子组件 → `CreateWidget<TWidget>(子GComponent)` 创建 `FguiWidget`。

### 外部资源（GLoader 扩展）

```csharp
loader.url = "asset://" + catalogKey;   // 例："asset://sample-icon"
```

- `FguiExternalLoader` 继承 FairyGUI `GLoader`，经 `UIObjectFactory.SetLoaderExtension` 注册（模块初始化时自动 Configure，Shutdown 自动 Reset）。
- 仅支持 **`asset://<catalog-key>`** 协议，且 Catalog 中对应 `FguiExternalAsset.Kind` 必须为 `Texture2D`；`ui://` 保留给 SDK 包内解析。
- URL 快速切换由 generation 计数防迟到结果；加载失败回调 `onExternalLoadFailed` 仅告警。

### 渲染/输入互操作（FguiRuntimeHost，一般无需直接接触）

- Stage 相机 depth=3（`FguiSettings.StageCameraDepth` 可调）、cullingMask 仅 `FairyGUI` Layer；切场景/退出时恢复或禁用。
- 模态：UGUI 侧透明 shield（ScreenSpaceOverlay Canvas，`sortingOrder = short.MaxValue - 1` + `ICanvasRaycastFilter`）——模态计数 > 0 时**阻挡全部** UGUI 点击并取消 UGUI 焦点；否则仅当 `Stage.HitTest` 命中 FGUI 对象才阻挡。
- 安全区：`respectSafeArea: true` 时每帧检测 `Screen.safeArea` 变化，按 scaleFactor 换算后调整所有层 GComponent 的位置尺寸。
- UGUI 需要独占全屏（过场/系统界面）时用 `SuspendPresentation()`（幂等 lease，Dispose 恢复）。

---

## 四、代码生成与资源管线

### FairyGUI 编辑器工程 → Unity 工作流

1. 编辑器工程：`UIProject/FguiIntegrationSample/FguiIntegrationSample.fairy`（包放 `assets/` 下）。
2. 发布设置（Publish.json）关键项：
   - `codePath: ../../Assets/GameScripts/HotFix/GameLogic/UI/FGUI/Gen`（**C# 只生成到 Gen**）
   - `classNamePrefix: "UI_"`、`memberNamePrefix: "m_"`、`getMemberByName: false`、`ignoreNoname: true`、`binaryFormat: true`、`packageName: "GameLogic.UI.FGUI.Gen"`（生成代码命名空间）
   - 发布路径：`../../Assets/AssetRaw/FGUI/Packages/{publish_file_name}`（整目录打包）
3. 发布后必须运行菜单 **TEngine → FairyGUI → Rebuild Package Catalog**（解析各包 `*_fui.bytes` 重建 `FguiPackageCatalog.asset`，自动补依赖并校验；完成后自动 Validate）。Catalog/Settings 资产固定路径：`Assets/AssetRaw/FGUI/FguiPackageCatalog.asset`、`Assets/AssetRaw/FGUI/FguiSettings.asset`。必要时再运行 **Validate Package Catalog**（校验目录完整性、Settings 引用、渲染 Layer 存在且不得是 UGUI 的 "UI" 层、地址可解析）。
4. YooAsset 收集器（`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset`）：GroupName=FairyGUI、CollectPath=`Assets/AssetRaw/FGUI`（覆盖 Packages、Catalog、Settings 与外部资源）、AssetTags=fgui、PackRule=PackDirectory、专属地址规则 `FguiAddressByRelativePath`——地址 = AssetRaw 下相对路径**强制含扩展名**（如 `FGUI/Packages/BundleUsage/BundleUsage_fui.bytes`）。

### 包加载与释放（FguiPackageService，自动管理）

- `ShowAsync` 自动经包引用计数 Acquire/Release，业务无需手动管包；需要常驻用 `PinPackageAsync`（lease 期间包+依赖闭包不卸载）。
- 加载流程：递归 Acquire 依赖包 → 加载描述 TextAsset → 按 Catalog 预加载全部资源 → `UIPackage.AddPackage(desc.bytes, AssetNamePrefix, 自定义回调)`（回调只查内存字典，`destroyMethod = DestroyMethod.None`，原生纹理由 YooAsset lease 管理）→ 校验包 id/name 与 Catalog 一致 + 描述器依赖声明校验 → `Package.LoadAllAssets()` → Ready。
- 释放：引用归零 → `UIPackage.RemovePackage` → Dispose 资源 lease → 递归释放依赖；Shutdown 按拓扑序逆序 ForceUnload。
- **不支持同一会话内替换已加载包版本**；热更产物走现有启动流程，下次初始化生效。
- **禁止**业务代码直接调 `UIPackage.AddPackage` / 直接用 `GameModule.Resource` 加载 FGUI 资源；FGUI 走 raw YooAsset handle（lease 唯一释放者），**绝不与 `ResourceModule.UnloadAsset` 混用**。

### 生成代码结构（Gen）

```csharp
// Gen/UI_BundleUsageMain.cs —— 组件段（有 m_ 命名成员时还含成员字段与构造绑定）
public sealed class UI_BundleUsageMain : GComponent
{
    public const string URL = "ui://d8m5tmokfou90";      // 组件 URL 常量

    public static UI_BundleUsageMain CreateInstance()
    {
        return (UI_BundleUsageMain)UIPackage.CreateObject("BundleUsage", "Main");
    }
}

// Gen/FguiGeneratedBinder.cs —— 汇总注册（BindAll 由初始化流程调用）
public static class FguiGeneratedBinder
{
    public static void BindAll()
    {
        UIObjectFactory.SetPackageItemExtension(UI_BundleUsageMain.URL, typeof(UI_BundleUsageMain));
        UIObjectFactory.SetPackageItemExtension(UI_ModalWaitingMain.URL, typeof(UI_ModalWaitingMain));
    }
}
```

- **Gen 目录只允许生成物；Imp 目录只允许手写控制器**——发布/重新生成绝不触碰 Imp。
- 官方生成器对带 `m_` 命名成员的组件会产出 XXXBinder/扩展方法（`ConstructFromXML` 自动调用）；本项目把注册统一收敛到 `FguiGeneratedBinder.BindAll()` 一个显式入口，**不做运行时反射扫描**（也可经 `descriptor.BindGeneratedTypes` 在 Register 时触发）。
- 未调用 `BindAll()` 时 `UIPackage.CreateObject` 返回原生 `GComponent`，`TypedView` 强转会抛 `InvalidCastException`。

---

## 五、常见错误

| 错误 | 正确做法 |
|------|---------|
| 未 `await InitializeAsync` 就 `ShowAsync` | 先初始化（未初始化抛 `InvalidOperationException`） |
| 初始化后忘记 `FguiGeneratedBinder.BindAll()` | 生成类型未注册 → `TypedView` 强转 `InvalidCastException` |
| 直接调 `UIPackage.AddPackage` 或用 `GameModule.Resource` 加载 FGUI 资源 | 包加载只能经 FguiModule/`PinPackageAsync`（lease 管理）；raw handle 禁止与 `UnloadAsset` 混用 |
| 期望 Hide 后超时自动关闭（UGUI HideTimeToClose 习惯） | FGUI 的 `Hide` 只置不可见，必须显式 `Close` |
| 在 FguiWindow 中找 `OnUpdate` | 不存在；用 `Lifetime.AddTimer` / `GameModule.Timer` |
| 把 FGUI onClick 当 GameEvent 发/收 | 两条独立通道：onClick 用 `Lifetime.AddListener`，跨模块用 `Lifetime.AddUIEvent` |
| 在 `OnDestroy` 中访问子 Widget | 子 Widget 逆序销毁**先于**窗口 `OnDestroy` 执行 |
| 合并 UGUI 与 FGUI 相机 cullingMask | 两套 Layer 必须隔离（`UI` vs `FairyGUI`），否则两相机渲染同一网格 |
| 同一窗口类型 `Register` 两次（不查 `IsRegistered`） | 抛 `InvalidOperationException` |
| `asset://` 指向非 Texture2D 资源 | 仅支持 Texture2D；其他类型放包内或扩展 Catalog（`FguiAssetKind` 仅 Texture2D 生效） |
| 直接修改 FairyGUI SDK vendor 源码 | 锁定 v5.2.0（VERSION.md），定制一律走桥接层（TEngine.FairyGUI / FguiModule） |
| 期望 FguiWindow 钩子同步返回 | `OnCreateAsync`/`OnRefreshAsync` 是异步钩子，返回 `UniTask`；完成前 `ShowAsync` 不会返回 |

---

## 六、交叉引用

| 主题 | 文档 |
|------|------|
| UGUI UIWindow 生命周期（对照） | [ui-lifecycle.md](ui-lifecycle.md) |
| 事件系统（GameEvent / AddUIEvent） | [event-system.md](event-system.md) |
| 资源加载/卸载 API（对照；FGUI 走 lease 不走 UnloadAsset） | [resource-api.md](resource-api.md) |
| 模块访问（GameModule.FGUI） | [modules.md](modules.md) |
| 程序集划分/热更边界 | [hotfix-workflow.md](hotfix-workflow.md) |
| 命名规范与节点前缀 | [naming-rules.md](naming-rules.md) |
