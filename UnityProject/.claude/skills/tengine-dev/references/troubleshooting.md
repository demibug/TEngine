# TEngine 常见问题排障

> **适用场景**：编译报错/热更失败/资源加载失败/UI 不显示/事件无响应/性能问题/Luban 配置问题排查 | **关联文档**：[resource-api.md](resource-api.md)、[event-system.md](event-system.md)、[hotfix-workflow.md](hotfix-workflow.md)

> 解决新问题后记录到 `.claude/memory/problem_YYYY-MM-DD.md`

## 核心 API

### 场景索引

| 场景 | 常见问题 |
|------|---------|
| 编译/热更 | AOT 泛型、Editor 正常真机报错、DLL 加载失败、iOS 链接失败、生命周期签名错误 |
| 资源加载 | location 无效、内存增长、缓存未更新、SetSprite callback 类型错误 |
| UI | 界面空白、Widget 复用异常、事件销毁后触发、生命周期签名错误 |
| FairyGUI | 包未导入、组件缺失、窗口加载超时 |
| 事件 | 接口事件无响应、监听收不到、UnRegisterAll 不存在 |
| 内存/性能 | GC 频繁、启动慢、DrawCall 高 |
| Luban | 生成报错、Tables 为 null、ConfigSystem 找不到 |
| UniTask | 异常被吞、await 后对象为 null |

---

## 使用模式

### 编译/热更问题

#### AOT 泛型异常：`ExecutionEngineException`

热更代码使用了主包没有 AOT 实例的泛型。菜单 `HybridCLR/Generate/AOTGenericReference`（或 `HybridCLR/Generate/All` 一键全生成）重新生成，若仍缺失则在生成的 `AOTGenericReferences.cs` 类内手动添加占位引用（注意：重新生成会覆盖手动改动）：
```csharp
// AOTGenericReferences 类内添加占位方法，确保 AOT 主包保留该泛型实例
static void UseCustomGenericType() { _ = new List<MyCustomType>(); }
```

#### Editor 正常，真机报错

Editor 走 Mono 编译，真机走 IL2CPP + HybridCLR。检查是否有 `dynamic`/`Emit` 等不支持特性，反射代码是否受限。开启 `Development Build` + `Script Debugging` 获取完整堆栈。

#### 热更 DLL 加载失败

1. `TEngine/UpdateSetting` 资产（`Assets/TEngine/Settings/UpdateSetting.asset`，CreateAssetMenu 名即 `TEngine/UpdateSetting`）的 `HotUpdateAssemblies` 列表是否完整（该列表与 HybridCLRGlobalSettings 自动同步，编译与运行时加载都以它为准）
2. 热更 DLL 是否已复制为 `AssetRaw/DLL/` 下的 `.bytes`（`UpdateSetting.AssemblyTextAssetPath`，两阶段模式为 `AssetRaw/Bootstrap/DLL/`），并被 YooAsset 收集器组 `DLL`/`BootstrapDLL` 收集
3. `ProcedureLoadAssembly.cs` 加载的 DLL 名与 `UpdateSetting` 配置一致（主逻辑 DLL 名取 `LogicMainDllName`）
4. 失败会经 `FailAttempt` 弹 Launcher 提示框（`LauncherMgr.ShowMessageBox`）并 `Log.Error`，日志阶段关键字：`程序集/metadata 阶段超时`、`程序集/metadata/入口阶段`、`Configured hot-update assembly 'xxx' is not loaded`；两阶段模式另有 `更新器装载阶段超时`、`两阶段更新`，资源包初始化阶段为 `资源包初始化阶段超时`

#### iOS 链接失败

Burst/IL2CPP 裁剪了代码。可先执行菜单 `HybridCLR/Generate/LinkXml` 生成补充 link.xml，再按需维护 `Assets/link.xml`（Assets 根目录默认无此文件，需新建；`Assets/TEngine/Extensions/FairyGUI/Runtime/` 下另有 FairyGUI 扩展自带的 link.xml，勿混淆）保留类型：
```xml
<linker><assembly fullname="UnityEngine"><type fullname="UnityEngine.Rigidbody" preserve="all"/></assembly></linker>
```

---

### 资源加载问题

#### location 无效

1. `AssetBundleCollector`（`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset`）是否收集该资产
2. location 不含路径和扩展名（`HeroPrefab` 而非 `Actor/Hero/HeroPrefab.prefab`），默认寻址规则为 `AddressByFileName`
3. `GameModule.Resource.CheckLocationValid("location")` 返回 false 说明未收集
4. 重新打包资源（Editor 模拟器可能引用旧清单）
5. 加载失败日志关键字：回调版为 `Can not load asset '{0}'. status:{1} error:{2}`；await 版为 `Load asset '{location}' failed. status:... error:...`（location 未收集时为 `Could not found location [{location}]`）。await 版 `LoadAssetAsync` 失败返回 null 并保留原因日志，不抛异常

#### 内存持续增长

用 Profiler Memory 快照与调试器窗口（激活方式由 `DebuggerActiveWindowType` 控制：AlwaysOpen/OnlyOpenWhenDevelopment/OnlyOpenInEditor/AlwaysClose）观察。常见原因：忘记 `UnloadAsset`、静态变量持有引用、UI Widget Asset 未释放、异步加载完成后对象已销毁导致 OnDestroy 未执行释放。引擎侧 `UnloadUnusedAssets` 只回收引用计数为零的资源，手动释放配对才是根治手段。API 详情见 [resource-api.md](resource-api.md)，生命周期模式见 [resource-patterns.md](resource-patterns.md)。

#### 热更后旧资源未更新

1. CDN 资源版本文件是否更新
2. `RequestPackageVersionAsync` 是否获取到新版本号
3. 本地测试清空 `Application.persistentDataPath` 缓存

---

### UI 问题

#### 界面空白/节点找不到

1. `[Window]` 特性 location 与 Prefab 文件名一致（如 `[Window(UILayer.UI, location:"BattleMainUI")]`）
2. Prefab 在 `AssetRaw/UI/` 且被收集器组 `UI` 收集（`AddressByFileName` 按文件名寻址）
3. `ScriptGenerator()`/`BindMemberProperty()` 中绑定的节点路径与 Prefab 节点层级一致
4. `FindChild` 返回 null 时加日志
5. 显示失败日志关键字：`UGUI window show failed`、`Window {type} create instance failed`；加载超时 60s 抛 `UIWindowTimeoutException`，资源加载失败抛 `UIWindowLoadException`

#### FairyGUI 窗口加载失败/超时

1. 包未导入时先确认 `FguiPackageCatalog` 资产（菜单 `TEngine/FairyGUI/Rebuild Package Catalog`，重建/登记包；`Validate Package Catalog` 校验）已登记对应包，`FguiSettings`（菜单 `TEngine/FairyGUI/Settings`）已引用该 Catalog
2. 加载超时由 `FguiSettings.LoadTimeoutSeconds` 控制（默认 30s，最小 1s），超时抛 `FguiTimeoutException`，日志关键字 `FairyGUI window '{type}' exceeded {n} seconds.`；组件缺失或工厂返回类型不符抛 `FguiLoadException`。详见 [fgui.md](fgui.md)。

#### UI 事件销毁后仍触发

在 `RegisterEvent()` 外使用 `GameEvent.AddEventListener` 不会自动清理。必须用 `AddUIEvent`。详见 [event-system.md](event-system.md)。

---

### 事件系统问题

#### 接口事件无响应

1. `GameEvent` 为静态门面，无需手动初始化；新会话开始时 `ModuleSystem.ResetForNewSession`（`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 阶段）会调用 `GameEvent.Shutdown()` 清理上一会话事件
2. 接口标记 `[EventInterface(EEventGroup.GroupUI)]`（或 `GroupLogic`）
3. 接口包装由 Source Generator 自动生成：编译时生成 `{接口名}_Gen` 包装类，其构造函数内调用 `GameEvent.EventMgr.RegWrapInterface<T>()` 自动注册；`GameEventHelper.Init()`（GameApp.Entrance 中调用）统一实例化全部包装类。无需手动注册
4. `Send<T>` 与监听接口必须来自同一接口类型（源生成器按接口生成包装）

#### 监听收不到

`Send<T>` 和 `AddEventListener<T>` 泛型类型必须完全一致（`Send<int>` 对 `AddEventListener<int>`）。

---

### 内存/性能问题

#### GC 频繁

Profiler 查看 GC Alloc 热点。常见：字符串拼接改 `StringBuilder`、闭包捕获避免 Lambda、Linq 改 `for` 循环、频繁 `new` 改 `MemoryPool.Acquire<T>()`。

#### 启动慢

减少 `PRELOAD` 标签资源、用进度回调展示、大型 Prefab 异步实例化。

#### DrawCall 高

Frame Debugger 查看合批。UI 同 Atlas 同 Canvas、3D 用 GPU Instancing/Static Batching、确认材质 Shader 一致。

---

### Luban 问题

#### 生成报错

Excel 第3行 `##type` 类型拼写正确（`int`/`string`/`float`，区分大小写；第1行 `##var` 字段名，第2行 `##var` Bean 子字段展开，第4行 `##group` 分组）；list 元素用英文分号 `;` 分隔（`list#sep=;`，元素内部字段用逗号，如 `10001,2;10002,3`）；Bean 先在 `__beans__.xlsx` 定义；`__tables__.xlsx` 的 `value_type` 列填记录类名（如 `Item`）。生成入口：菜单 `TEngine/Luban/转表 &X`（快捷键 Alt+X），Windows 实际执行仓库根 `Configs/GameConfig/gen_code_bin_to_project_lazyload.bat`，macOS/Linux 执行同名 `.sh`。

#### Tables 为 null

1. `Tables` 属性为懒加载，首次访问自动调用 `Load()`；确认加载时机在 `ProcedurePreload` 之后（资源模块已初始化）
2. `.bytes` 输出在 `AssetRaw/Configs/bytes/`（转表脚本 `DATA_OUTPATH`）且被收集器组 `Configs`（CollectPath `Assets/AssetRaw/Configs`）收集
3. 配置表为懒加载，Tables 工作不依赖 PRELOAD 标签；`ProcedurePreload` 的 `GetAssetInfos("PRELOAD")`/`GetAssetInfos("WEBGL_PRELOAD")` 是可选的启动期预加载机制，当前收集器 Configs 组未配置 `PRELOAD` 标签时该查询返回空（跳过预热，不影响懒加载）

#### ConfigSystem.cs 找不到

`ConfigSystem.cs` 不在 Assets 默认目录中，转表脚本会从 `Configs/GameConfig/CustomTemplate/ConfigSystem.cs` 复制到 `Assets/GameScripts/HotFix/GameProto/`；未生成时可手动执行转表脚本。详见 [luban-config.md](luban-config.md)。

---

### UniTask 问题

#### 异常被吞

`UniTaskVoid` 异常经 `UniTaskScheduler.PublishUnobservedTaskException` 上报，默认仅写 LogType.Exception 日志而不传播。方法内 try-catch，或订阅全局回调（是事件，用 `+=` 订阅）：
```csharp
UniTaskScheduler.UnobservedTaskException += e => Log.Error($"未处理 UniTask 异常: {e}");
```

#### await 后对象为 null

await 期间对象被销毁。返回后检查：
```csharp
if (this == null || _imgIcon == null) return;
```

---

### 测试（EditMode/PlayMode）问题

#### EditMode 测试偶现/连带失败，断言计数全为 0

1. EditMode 测试运行在编辑器主域，`ModuleSystem` 是跨测试共享的静态单例；被测代码读取 `ModuleSystem.IsRunning`/`State` 时（如 `PreloadRequestRunner.Begin`、`AssetsReference.Ref`），会话状态残留为非 Running 会静默提前返回。
2. 修复模式：`[SetUp]`/`[TearDown]` 反射调用 `ModuleSystem.ResetForNewSession`，参考 `ResourceLifecycleTestBase`（`Assets/Tests/ResourceLifecycle/EditMode/PreloadRequestRunnerTests.cs:174`）。

#### EditMode 下普通 MonoBehaviour 的 OnDestroy 不执行

1. Unity 只为 `ExecuteInEditMode/ExecuteAlways` 脚本在编辑器域执行事件函数；`DestroyImmediate` 后 `ReleaseInternal` 无人触发。
2. 断言"销毁→归还"语义时显式调用补偿入口（如 `AssetsReference.ReleaseDestroyedReferences()`），或把用例放进 PlayMode；不要为了测试给运行时组件加 `[ExecuteAlways]`。

#### PlayMode 报 Unhandled log message / 轮询兜底不生效

1. `LogAssert.Expect` 正则耦合实现日志文案易脱节，改用实际日志的稳定子串（如 `UnloadAsset failed during cleanup`），不要 `^...$` 锚定（DefaultLogHelper 会加 `<color=red>` 装饰前缀）。
2. 手动引导（无 GameEntry/RootModule）的 PlayMode 套件中 `ModuleSystem.Update` 无人驱动，轮询兜底（未激活销毁归还等补偿扫描）永不执行——断言前显式 pump 一帧：`ModuleSystem.Update(0.016f, 0.016f)`。

---

## 常见错误

| 错误 | 原因 | 修复 |
|------|------|------|
| UIWindow `OnCreate(object userData)` 编译失败 | 生命周期方法无参数 | `OnCreate()` 无参，数据通过 `UserData` 属性获取 |
| UIWindow `OnRefresh(object userData)` 编译失败 | 同上 | `OnRefresh()` 无参 |
| UIWindow `OnDestroy()` 误写为 `OnDestroy(bool isShutdown)` | 与 ProcedureBase.OnDestroy 签名混淆 | UIWindow.OnDestroy() 无参数 |
| `SetSprite` callback 写成 `Action<Sprite>` | callback 参数类型不是 Sprite | 实际为 `Action<Image>`，回调参数是设置完 Sprite 后的 Image 组件 |
| `GameEvent.UnRegisterAll()` 编译失败 | GameEvent 中不存在此方法 | UI 内改用 `AddUIEvent` 自动清理，手动移除用 `GameEvent.RemoveEventListener(eventType, handler)` |
| `SetSprite` callback 写成 `Action<SpriteRenderer>`（Image 场景） | Image 重载与 SpriteRenderer 重载的 callback 类型不同 | Image 版 callback 为 `Action<Image>`，SpriteRenderer 版为 `Action<SpriteRenderer>`，按组件类型使用 |

### UIWindow 生命周期签名速查

```csharp
// ✅ 正确签名（热更层 GameLogic 的 UIBase 中定义）
protected virtual void OnCreate()      // 无参数
protected virtual void OnRefresh()     // 无参数
protected virtual void OnUpdate()      // 无参数
protected virtual void OnDestroy()     // 无参数（非 ProcedureBase 的 OnDestroy(ProcedureOwner)）

// ❌ 常见错误签名
protected override void OnCreate(object userData)   // 不存在此签名，数据通过 UserData/UserDatas 属性获取
protected override void OnDestroy(bool isShutdown)  // 这是 ProcedureBase 的签名，不是 UIWindow
```

### SetSprite callback 签名速查

`SetSprite` 的 callback 类型是 `Action<Image>`（Image 扩展）或 `Action<SpriteRenderer>`（SpriteRenderer 扩展），**不是** `Action<Sprite>`。完整签名见 [resource-api.md](resource-api.md#setsprite-扩展方法4-个签名)。

### GameEvent 清理方法速查

```csharp
// ✅ UI 内部事件自动清理（AddUIEvent 在 OnDestroy 时自动 RemoveAllUIEvent）
AddUIEvent(eventType, handler);

// ✅ 手动移除单个事件监听
GameEvent.RemoveEventListener(eventType, handler);

// ✅ 清理 UI 内所有事件（UIBase 派生类内调用）
RemoveAllUIEvent();           // protected UIBase 方法，事件管理器经 MemoryPool 回收时触发 GameEventMgr.Clear()
// 或
EventMgr.Clear();             // UIBase.EventMgr 属性（GameEventMgr 实例）的 Clear 方法

// ❌ 不存在的方法
GameEvent.UnRegisterAll();    // 编译错误：GameEvent 无此方法
```

---

## 交叉引用

- UI 生命周期见 [ui-lifecycle.md](ui-lifecycle.md)
- 事件系统见 [event-system.md](event-system.md)
- 资源加载见 [resource-api.md](resource-api.md)
- 热更开发见 [hotfix-workflow.md](hotfix-workflow.md)
- Luban 配置见 [luban-config.md](luban-config.md)
- 架构总览见 [architecture.md](architecture.md)
