# 热更代码开发与热更包管理

> **适用场景**：HybridCLR 热更边界划分、GameApp 热更入口、DLL 加载流程、AOT 泛型补全 | **关联文档**：[architecture.md](architecture.md)（程序集划分）、[modules.md](modules.md)（GameModule 访问）

## 核心 API

### 程序集划分

```
GameScripts/
├── GameEntry.cs             # 主包 MonoBehaviour 入口（非热更，Awake 中触发 ProcedureSetting.StartProcedure()）
├── Procedure/               # 主包启动流程（命名空间 Procedure，无 asmdef，编译进 Assembly-CSharp，不可热更）
│
└── HotFix/                  # 热更域（三个程序集）
    ├── GameUpdater/         # 两阶段更新 bootstrap 程序集（仅 Entry.cs + asmdef，默认不启用）
    ├── GameProto/           # Luban 生成（勿手改）
    │   ├── LubanLib/        # ByteBuf、BeanBase、ITypeId、StringUtil 等序列化库（当前仓库已存在）
    │   ├── GameConfig/      # Tables + 配置类（由 gen_code_bin_to_project.bat 生成；当前仓库尚未生成）
    │   └── ConfigSystem.cs / ExternalTypeUtil.cs（由脚本从 Configs/GameConfig/CustomTemplate 拷贝生成；当前尚未生成）
    │
    └── GameLogic/           # 业务逻辑（主开发区域）
        ├── GameApp.cs                  # 热更主入口（partial class，无命名空间）
        ├── GameModule.cs               # 模块统一访问入口
        ├── IEvent/                     # 事件接口定义（如 ILoginUI.cs）
        ├── Module/                     # 模块实现（UIModule、FguiModule）
        ├── SingletonSystem/            # 单例系统（SingletonSystem.Release()）
        ├── UI/                         # UI 窗口代码（BattleMainUI、LoginUI、FGUI）
        └── ...
```

**依赖规则**（不可逆向）：`GameLogic → GameProto`、`GameLogic/GameProto/GameUpdater → TEngine.Runtime`；GameUpdater 另引用 UniTask（仅此两项，见 GameUpdater.asmdef），GameLogic 另引用 UniTask、YooAsset、FairyGUI、TEngine.FairyGUI、Unity.TextMeshPro（见 GameLogic.asmdef；HybridCLR.Runtime 不在该程序集引用中，主包侧才使用）。
主包代码在 `GameScripts/Procedure/` 与 `GameScripts/GameEntry.cs`（两者均无 asmdef，编译进 Assembly-CSharp，反射契约所在）、`Assets/Launcher/`（独立 Launcher 程序集，见 Launcher.asmdef）、`TEngine/`，均不可热更。

**热更配置（两处名单需保持一致）**：
- `ProjectSettings/HybridCLRSettings.asset`：`hotUpdateAssemblies` = [GameUpdater, GameProto, GameLogic]；`patchAOTAssemblies`（link.xml 补充）含 mscorlib/System/System.Core/TEngine.Runtime/YooAsset/UniTask/UnityEngine.CoreModule
- `Assets/TEngine/Settings/UpdateSetting.asset`：`HotUpdateAssemblies` = [GameUpdater.dll, GameProto.dll, GameLogic.dll]、`AOTMetaAssemblies`（运行时补 metadata）同上、`LogicMainDllName = GameLogic.dll`、DLL 资产目录 `AssemblyTextAssetPath = AssetRaw/DLL`（当前 `ProcedureLoadAssembly` 走 Addressable 加载，直接以程序集名如 "GameLogic.dll" 作为资源地址）
- `UpdateSetting.Enable` 为 false（只读计算属性，未启用 `ENABLE_HYBRIDCLR` 宏时即为 false，不能在资产中手动设置）或 EditorSimulateMode 时，`ProcedureLoadAssembly` 不加载 DLL 资产，直接收集 AppDomain 中已加载的对应程序集
- 新增热更程序集时：建 asmdef → 加入两个名单 → 加入 `UpdateSetting.HotUpdateAssemblies`

---

### GameApp 入口

```csharp
// GameApp.cs（partial class，无命名空间——反射按全名 "GameApp" 查找）
public partial class GameApp
{
    private static List<Assembly> _hotfixAssembly;

    /// <summary>
    /// 热更域App主入口。由 ProcedureLoadAssembly 通过反射调用。
    /// </summary>
    public static void Entrance(object[] objects)
    {
        if (!ModuleSystem.IsRunning)                             // 0. 模块系统关闭中则忽略
        {
            return;
        }
        GameEventHelper.Init();                                  // 1. 事件系统初始化（见下方注）
        _hotfixAssembly = (List<Assembly>)objects[0];            // 2. 保存热更程序集列表
        RootModule.BeforeShutdown += Release;                    // 3. 注册关停回调
        Utility.Unity.AddDestroyListener(Release);               // 4. 注册销毁回调
        StartGameLogic();
    }

    private static void StartGameLogic()
    {
        GameModule.UI.ShowUIAsync<BattleMainUI>();               // 当前直接打开 UI
        InitializeFairyGuiAsync().Forget();                      // FairyGUI 显式初始化（失败不影响 UGUI）
    }

    private static void Release()
    {
        if (_releaseStarted) return;                             // 防重入（Entrance/Release 均有状态保护）
        _releaseStarted = true;
        RootModule.BeforeShutdown -= Release;
        SingletonSystem.Release();                               // 释放单例（try/catch 保护）
        GameModule.Shutdown();                                   // 清理模块缓存（try/catch 保护）
    }
}
```

**关键约束**：
- 反射契约精确匹配：`GameApp` 必须**无命名空间**、声明**唯一一个** `public static void Entrance(object[])`（校验逻辑见 `Procedure/ProcedureBase.cs` 的 `StartupEntryContract`）
- `Entrance` 参数类型是 `object[]`，不是 `Assembly[]`——`objects[0]` 才是 `List<Assembly>`
- 可通过 `partial class GameApp` 拓展入口逻辑（如注册系统）
- 同进程仅允许成功进入一次：`ProcedureLoadAssembly` 在反射 Invoke 前置位 `_entranceInvoked` 闸门，`Entrance` 抛错后本进程不能再次注入程序集，只能重启
- `GameEventHelper.Init()` 必须最先调用：该类为 TEngine.Runtime 手写类（`Assets/TEngine/Runtime/Core/GameEvent/GameEventHelper.cs`），扫描已加载程序集的程序集级 `EventAssemblyRegistrarAttribute` 并实例化对应 Registrar 完成事件接口注册（Registrar 由 `Tools/GameEventSourceGenerator` 为每个含 `[EventInterface]` 接口的程序集生成，生成器以 `Assets/TEngine/Runtime/Core/GameEvent/SourceGenerator.dll` RoslynAnalyzer 形式交付），详见 event-system.md
- `GameApp` 还声明 `ResetForNewSession()`（`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`，重置静态状态以支持域重载）

---

### 流程状态机

主包启动流程在 `GameScripts/Procedure/`，继承 `Procedure.ProcedureBase`（派生自 `TEngine.ProcedureBase`，额外要求实现 `bool UseNativeDialog { get; }`）。热更域流程直接继承 `TEngine.ProcedureBase`（即 `FsmState<IProcedureModule>`）。下为示例（当前 GameLogic 暂无热更流程代码）：

```csharp
public class ProcedureLogin : ProcedureBase
{
    protected override void OnEnter(IFsm<IProcedureModule> owner)
        => GameModule.UI.ShowUIAsync<LoginUI>();
    protected override void OnLeave(IFsm<IProcedureModule> owner, bool isShutdown)
        => GameModule.UI.CloseUI<LoginUI>();
}

// 流程切换
ChangeState<ProcedureMain>(procedureOwner);   // 状态内部（FsmState<T> 方法）
```

⚠️ `IProcedureModule` 没有 `ChangeState` 方法；外部切换通过 `GameModule.Fsm` 获取流程状态机后切换，或 `GameModule.Procedure.StartProcedure<T>()`（用于重启/开始流程）。

---

### HybridCLR 注意事项

```csharp
// ❌ 热更代码不能引用主包 internal 类型
// ❌ 避免 System.Reflection 大量使用（AOT 限制多）
// ❌ 不支持：dynamic、Expression<T> 编译、部分 Emit、Marshal/P-Invoke
```

AOT 补元数据由 `ProcedureLoadAssembly` 加载：遍历 `UpdateSetting.AOTMetaAssemblies`，`RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.SuperSet)`（`#if UNITY_EDITOR` 直接跳过；EditorSimulateMode 更是走"收集已加载程序集"分支，完全不加载 DLL 资产）。加载失败或校验不过时，`ProcedureLoadAssembly` 通过 `LauncherMgr` 弹窗提示"热更启动失败"并退出（程序集加载不可回滚）。整个"程序集/metadata/入口"阶段另有 60 秒超时（`ProcedureLoadAssembly.StartupTimeoutSeconds`），超时同样按启动失败弹窗处理。

#### AOT 泛型补充

```
菜单 HybridCLR/Generate/AOTGenericReference（或一键 HybridCLR/Generate/All）
→ 生成 HybridCLRGenerate/AOTGenericReferences.cs
如出现 ExecutionEngineException，手动添加对应泛型使用后重新打包
```

常见需补充：`List<自定义类型>`、`Dictionary<K,V>` 新组合、`UniTask<自定义类型>`、`Action<自定义类型>`

---

## 使用模式

### 热更包下载流程

主包流程驱动，不可热更（`GameScripts/Procedure/`）。主包入口为 `GameScripts/GameEntry.cs`（MonoBehaviour，Awake 中 `Settings.ProcedureSetting.StartProcedure()` 启动流程状态机）：

```
GameEntry(Awake) → ProcedureLaunch → ProcedureSplash → ProcedureInitPackage → ProcedureInitResources
→ 分支①常规更新（HostPlayMode/WebPlayMode 非边玩边下载）：ProcedureCreateDownloader →（存在待下载文件时）ProcedureDownloadFile
  → ProcedureDownloadOver →（仅 `_needClearCache` 为 true 时进入 ProcedureClearCache；当前版本该字段无赋值处恒为 false，实际直接跳过）→ ProcedurePreload
  分支②无更新（待下载文件数为 0）：ProcedureCreateDownloader → ProcedureDownloadOver → ProcedurePreload
  分支③EditorSimulate/离线/边玩边下载（WebPlayMode 或 UpdatableWhilePlaying=true）：直接 ProcedurePreload
  分支④两阶段更新（EnableTwoStageUpdate=true）：ProcedureTwoStageUpdate
→ 汇合 ProcedurePreload（预加载 PRELOAD 与 WEBGL_PRELOAD 标签资源）→ ProcedureLoadAssembly
→ （Entrance 同步执行成功后）ProcedureStartGame（仅 LauncherMgr.HideAllUI()）
```

下载失败在 ProcedureDownloadFile 弹窗重试，回退到 ProcedureCreateDownloader。

#### 核心代码（对应实际流程）

```csharp
// 1+2. 请求远端版本并更新 Manifest（ProcedureInitResources 内）
var versionOp = _resourceModule.RequestPackageVersionAsync();   // yield return versionOp
var manifestOp = _resourceModule.UpdatePackageManifestAsync(versionOp.PackageVersion);

// 3. 创建下载器（ProcedureCreateDownloader）
var downloader = _resourceModule.CreateResourceDownloader();
if (downloader.TotalDownloadCount == 0) { /* 无更新 */ }

// 4. 下载（ProcedureDownloadFile）
downloader.DownloadErrorCallback = OnDownloadErrorCallback;     // 回调属性（非 OnXxx 事件）
downloader.DownloadUpdateCallback = OnDownloadProgressCallback;
downloader.BeginDownload();
await downloader;                                               // await 操作对象本身
if (downloader.Status != EOperationStatus.Succeed) return;      // 检查终态

// 5. 清理旧缓存（ProcedureClearCache，Completed 回调）
var clearOp = _resourceModule.ClearCacheFilesAsync();
clearOp.Completed += op => { /* 完成 → ProcedurePreload */ };
```

#### API 速查

| 方法 | 说明 |
|------|------|
| `GetPackageVersion()` | 本地资源包版本号 |
| `RequestPackageVersionAsync()` | 请求远端版本号（RequestPackageVersionOperation） |
| `UpdatePackageManifestAsync(ver)` | 更新资源清单（UpdatePackageManifestOperation） |
| `CreateResourceDownloader()` | 创建差量下载器（ResourceDownloaderOperation） |
| `CreateResourceDownloaderByTags(tags)` | 按标签创建下载器 |
| `downloader.TotalDownloadCount` | 待下载文件数 |
| `downloader.TotalDownloadBytes` | 待下载总字节 |
| `downloader.DownloadUpdateCallback` | 下载进度回调属性 |
| `ClearCacheFilesAsync()` | 清理冗余缓存（ClearCacheFilesOperation） |

---

### 日常开发步骤

```
1. 在 GameScripts/HotFix/ 下修改/添加代码
2. Editor 模式：直接 Play（EditorSimulateMode 直接使用已加载程序集，无需加载 DLL 资产）
3. 模拟热更：菜单 HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath
   （编译热更 DLL + AOT 补充 DLL 并拷贝到 AssetRaw/DLL）→ 打资源包
4. 真机测试：出包 → 部署热更资源到 CDN → 启动触发热更
```

#### 新功能开发

```
1. IEvent/ 定义事件接口（跨模块通信时）
2. UI/ 创建 UIWindow 子类（[Window] 特性）
3. Module/ 实现业务模块
4. partial class GameApp 拓展入口初始化逻辑
5. GameApp（StartGameLogic）或热更域流程中连接 UI 打开与系统初始化
```

---

### 两阶段更新（可选，默认关闭）

`UpdateSetting.EnableTwoStageUpdate = true` 时启用：AOT 主包先下载并加载 bootstrap 程序集 `GameUpdater.dll`（`BootstrapAssemblyName`，构建校验固定此名），再由它驱动其余资源更新。

```
链路：ProcedureInitPackage（EnableTwoStageUpdate 时先经 TwoStageUpdateCoordinator.PrepareAsync 下载并校验 release 描述符）
     → ProcedureInitResources（固定 release 清单校验）→ ProcedureTwoStageUpdate
     → 下载 BOOTSTRAP 标签的 GameUpdater.dll → 加载 AOT metadata
     → 契约校验（UpdateStageEntryContract.TryFindRunAsync）后
       反射调用 GameUpdater.Entry.RunAsync(context, host)（IUpdateHost 由 ProcedureTwoStageUpdate 实现）
     → 资源就绪校验（VerifyResourcesActuallyReady）→ ProcedurePreload → ProcedureLoadAssembly（加载业务程序集）
```

- `GameUpdater/Entry.cs` 是第二阶段入口（`namespace GameUpdater`），只依赖 `TEngine.Runtime` 稳定契约，不引用业务代码
- 关键设置：`BootstrapTextAssetPath = "AssetRaw/Bootstrap/DLL"`、`BootstrapTag = "BOOTSTRAP"`、`TwoStageReleaseEntryUrl`（固定 `current.json` 地址）；资产中另有 `TwoStageContractVersion = 1`、`TwoStageHostServerUrl` / `TwoStageFallbackHostServerUrl`、`TwoStageNoProgressTimeoutSeconds = 60`、`EditorSimulateReleaseDescriptorJson` 等字段
- 业务程序集顺序/列表必须与 release 描述符一致（逐项有序比对），否则 `ProcedureLoadAssembly` 抛异常拒绝进入；`BootstrapAssemblyName` 对应的 DLL 在业务名单加载阶段会被跳过（第一阶段已加载）
- 主包侧会话状态由 `Procedure/TwoStageUpdateCoordinator.cs` 管理（IsPrepared / MarkResourcesReady / 入口 checkpoint）

---

## 常见错误

| 错误 | 原因 | 修复 |
|------|------|------|
| `GameApp_RegisterSystem` 不存在 | 文档描述了不存在的文件 | GameLogic 下无此文件，使用 partial class GameApp 拓展 |
| `Entrance(Assembly[])` 签名错误 | 文档与源码不一致 | 实际为 `Entrance(object[])`，objects[0] 是 `List<Assembly>`；且必须无命名空间、仅一个重载 |
| 热更代码找不到 GameApp.Entrance | 主包反射调用失败 | 确认 `UpdateSetting` 中 `LogicMainDllName` 指向 GameLogic.dll（当前资产值即此） |
| 流程链错误 | 文档遗漏 ProcedureInitPackage/ProcedureDownloadOver/ProcedureClearCache | 正确链路见上文"热更包下载流程" |
| 下载进度回调赋值编译失败 | 误写 `downloader.OnDownloadProgressCallback` | 实际字段为 `DownloadUpdateCallback` / `DownloadErrorCallback` |
| 外部 `GameModule.Procedure.ChangeState` 编译失败 | IProcedureModule 无此方法 | 状态内用 `ChangeState<T>(owner)`，外部经 `GameModule.Fsm` 或 `StartProcedure<T>()` |
| 两阶段构建预检失败 | BootstrapAssemblyName 不在 HotUpdateAssemblies 或不等于 GameUpdater.dll | 构建（BuildPreflight）固定要求 `BootstrapAssemblyName = "GameUpdater.dll"` 且属于业务热更名单 |

---

## 交叉引用

- 架构总览见 [architecture.md](architecture.md)
- 事件系统见 [event-system.md](event-system.md)
- 资源加载见 [resource-api.md](resource-api.md)
- UI 生命周期见 [ui-lifecycle.md](ui-lifecycle.md)
