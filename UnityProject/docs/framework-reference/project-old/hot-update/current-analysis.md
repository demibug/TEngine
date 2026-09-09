# TEngine 当前工程热更新策略独立调查（current-analysis）

> 调查对象：`E:\MyWork\MyFramework\TEngine\UnityProject`（含工作区未提交修改，以工作区版本为准）
> 调查方式：只读本地源码与配置；**未运行 Unity、未出包、未联网、未做真机验证**。
> 本报告为独立证据链，不引用参考工程结论，不评价两工程优劣，不冻结移植方案。
> 撰写日期：2026-09-09。关键文件 SHA256 快照见文末。

---

## 1. 摘要

两个命题分别判定：

| 命题 | 结论 | 核心证据 |
|------|------|---------|
| A. 代码支持联机热更新 | **是（完整代码路径存在）** | `ProcedureInitResources`（远端版本+manifest）→ `ProcedureCreateDownloader`/`ProcedureDownloadFile`（下载）→ `ProcedureLoadAssembly`（AOT metadata + `Assembly.Load` + 反射 `GameApp.Entrance`）；`ResourceModule.InitPackage` 支持 HostPlayMode/WebPlayMode |
| B. 按当前工作区配置出 Player 实际启用热更新 | **否（当前状态会以离线模式出包）** | ① `ProjectSettings.asset` 的 `scriptingDefineSymbols` 所有平台均**无 `ENABLE_HYBRIDCLR`**（ProjectSettings.asset:795-809）→ `UpdateSetting.Enable` 恒为 false（UpdateSetting.cs:59-69）；且含运行时代码的 asmdef 其 `versionDefines` 均无 HybridCLR 条目（TEngine.Runtime.asmdef:21-27 仅 InputSystem；TEngine.Editor.asmdef/TEngine.FairyGUI 为空）→ 宏不会经由包存在性自动引入；② `HybridCLRData/` 目录不存在 → HybridCLR Installer 未执行、无法编译热更 DLL；③ `Assets/AssetRaw/DLL` 为空目录，无 `.bytes` 产物；④ `Assets/StreamingAssets` 不存在；⑤ `GameEntry.prefab` 中 `ResourceModuleDriver.playMode: 0`（EditorSimulateMode），Player 运行时回退 `OfflinePlayMode`（ResourceModuleDriver.cs:77-80）；⑥ 全工程无 `csc.rsp`/`mcs.rsp` 补充宏文件 |

三点必须强调：

1. **"热更代码可热更"与"更新器可热更"是两回事。** 业务热更程序集（GameProto/GameLogic）有完整的加载管线；但整个**启动/更新流程本身（Procedure FSM + Launcher UI）位于非热更主包**（`Assets/Launcher/Launcher.asmdef` + `Assets/GameScripts/Procedure/`），更新器自身不可热更。
2. **工作区存在大量未提交修改**（startup/shutdown/resource lifecycle 修复），本报告全部以工作区版本分析；其中 `StartupAttempt` 终态代际模型、`ModuleSystem` 八阶段关闭、`ResourceModule` bootstrap 事务化是未来移植必须保留的约束（§8）。
3. 命题 B 的判定存在**构建期可覆盖项**：`ENABLE_HYBRIDCLR` 宏可由菜单 `HybridCLR/Define Symbols/Enable HybridCLR` 一键添加（BuildDLLCommand.cs:75-95），且编辑器实际 PlayMode 存于 `EditorPrefs["EditorPlayMode"]`（不落盘于版本库）。这两项在开发者本机的真实状态**无法从工作区文件证实**，属未验证事项（§11）。

---

## 2. 真实启动/热更流程

### 2.1 入口与模块装配

- 构建场景：`Assets/Scenes/main.unity`（EditorBuildSettings.asset 唯一启用场景），场景内实例化 `Assets/TEngine/Settings/Prefab/GameEntry.prefab`（guid `e38c712e7904fdf42bd0458b769718dd`）与 UIRoot。
- `GameEntry.Awake`（Assets/GameScripts/GameEntry.cs:6-14）：创建 `IUpdateDriver / IResourceModule / IDebuggerModule / IFsmModule`，随后 `Settings.ProcedureSetting.StartProcedure().Forget()`。
- `ProcedureSetting.StartProcedure`（Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureSetting.cs:55-112）：按 `ProcedureSetting.asset` 的 `availableProcedureTypeNames` 实例化 11 个流程，入口 `entranceProcedureTypeName: Procedure.ProcedureLaunch`（Assets/TEngine/Settings/ProcedureSetting.asset:15-27）。
- `ResourceModuleDriver.Start`（Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs:236-305）：从 GameEntry.prefab 子物体 `ResourceDriver` 读取序列化参数（包名 `DefaultPackage`、`playMode: 0`、`updatableWhilePlaying: 0`、`downloadingMaxNum: 10`、`failedTryAgain: 3`、`encryptionType: 0`；GameEntry.prefab:209-215），并从 `Settings.UpdateSetting.asset` 取下载地址（`http://127.0.0.1:8081` / 备用 `:8082`；UpdateSetting.asset:32-33），调用 `_resourceModule.Initialize()` 完成 YooAsset bootstrap；**任何参数/初始化异常必须走 `FailInitialization`**（ResourceModuleDriver.cs:294-304）。

### 2.2 流程图（实际代码路径）

```mermaid
flowchart TD
    A[GameEntry.Awake<br/>创建模块 + StartProcedure] --> B[ProcedureLaunch<br/>LauncherMgr.Initialize + 语言/声音]
    B --> C[ProcedureSplash]
    C --> D[ProcedureInitPackage<br/>等 ResourceModule bootstrap<br/>60s 超时, YooAsset InitializeAsync]
    D --> E[ProcedureInitResources<br/>Host/Web: RequestPackageVersion<br/>→ UpdatePackageManifest]
    E -->|Host 或 Web| F{Web 或 UpdatableWhilePlaying?}
    E -->|Offline / EditorSimulate| H[ProcedurePreload]
    F -->|是| H
    F -->|否| G[ProcedureCreateDownloader<br/>CreateResourceDownloader<br/>并发10/失败重试3]
    G -->|TotalDownloadCount==0| I[ProcedureDownloadOver]
    G -->|有更新| G2[确认弹窗: 开始/退出] --> J[ProcedureDownloadFile<br/>BeginDownload + await]
    J -->|错误回调| G
    J -->|成功| I
    I[ProcedureDownloadOver<br/>PlayerPrefs 保存 GAME_VERSION] --> H
    H[ProcedurePreload<br/>GetAssetInfos PRELOAD/WEBGL_PRELOAD<br/>PreloadRequestRunner 加载后立即归还]
    H --> K[ProcedureLoadAssembly]
    K -->|!Enable 或 EditorSimulate| K1[CollectAlreadyLoadedAssemblies<br/>AppDomain 查找已加载程序集]
    K -->|Player 且 Enable| K2[LoadMetadataForAOTAssemblies<br/>仅非编辑器 + ENABLE_HYBRIDCLR<br/>SuperSet 模式]
    K2 --> K3[foreach HotUpdateAssemblies<br/>LoadAssetAsync&lt;TextAsset&gt; → Assembly.Load]
    K1 --> L[EnsureMainAssemblyIsReady<br/>反射 GameApp.Entrance(object[])<br/>同步调用, 一次性闸门]
    K3 --> L
    L --> M[ProcedureStartGame<br/>Yield 一帧后 HideAllUI]
    D -->|失败| D1[MessageBox 重试/退出<br/>StartupAttempt 代际失效]
    E -->|失败| E1{Host 且 UpdateStyle=Optional?}
    E1 -->|是且有本地版本| E2[回退本地 GAME_VERSION<br/>可选提示/直接跳 Preload]
    E1 -->|否| E3[MessageBox 重试/退出]
```

`ProcedureClearCache`（下载完成后清理未用缓存文件 → 回到 Preload）存在完整实现（ProcedureClearCache.cs），但其唯一触发条件 `ProcedureDownloadOver._needClearCache` **硬编码为 false**（ProcedureDownloadOver.cs:12,24-33），当前不可达。

### 2.3 PlayMode 判定的实际规则（编辑器 vs Player）

| 环境 | PlayMode 来源 | 证据 |
|------|--------------|------|
| 编辑器 | `EditorPrefs.GetInt("EditorPlayMode")`（工具栏下拉写入，EditorPlayMode.cs:74；ResourceModuleDriver.cs:73-75；ResourceModule.cs:540-542 重复读取同一键） | 序列化字段 `playMode: 0` 在编辑器内被忽略 |
| Player | 序列化 `playMode`；若为 `EditorSimulateMode(0)` **回退 `OfflinePlayMode(1)`**（ResourceModuleDriver.cs:77-80, 251-257） | GameEntry.prefab:210 序列化为 0 ⇒ 出包即离线，除非构建时改 prefab |

EPlayMode 枚举顺序（Packages/YooAsset/Runtime/InitializeParameters.cs:8-34）：`EditorSimulateMode=0, OfflinePlayMode=1, HostPlayMode=2, WebPlayMode=3, CustomPlayMode=4`。

**重要细节**：`ProcedureLoadAssembly` 中"是否复用已加载程序集"只由 `!UpdateSetting.Enable || PlayMode==EditorSimulateMode` 决定（ProcedureLoadAssembly.cs:127-132）。因此：
- 当前配置（无 `ENABLE_HYBRIDCLR`）出 Player：`Enable=false` → 直接用 `Assembly-CSharp` 等已加载程序集，业务代码**全部 AOT 编译进包**，无任何热更。
- 若开启宏但保持 OfflinePlayMode：走 metadata + `Assembly.Load` 路径，从 StreamingAssets 内置 bundle 加载 `GameProto.dll.bytes/GameLogic.dll.bytes`——"可热更结构，但无更新动作"。
- 只有"宏开启 + HostPlayMode + 服务器存在新版本"才构成完整联机热更。

---

## 3. 程序集划分（asmdef）

| 程序集 | 路径 | 热更？ | 说明 |
|--------|------|--------|------|
| Launcher | Assets/Launcher/Launcher.asmdef | 否 | Procedure FSM 全部 11 个流程 + LauncherMgr/LoadUpdateUI；引用 HybridCLR.Runtime + 1 个未解析 GUID（§11） |
| TEngine.Runtime | Assets/TEngine/Runtime/TEngine.Runtime.asmdef | 否 | 框架核心（含 ResourceModule/ModuleSystem），被列入 AOT metadata 名单 |
| Assembly-CSharp | Assets/GameScripts/GameEntry.cs（无 asmdef） | 否 | GameEntry 挂点；与 `Assets/Editor/` 一起进默认程序集 |
| GameProto | Assets/GameScripts/HotFix/GameProto/GameProto.asmdef | **是** | HotUpdateAssemblies[0] |
| GameLogic | Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef | **是** | HotUpdateAssemblies[1] + LogicMainDllName；allowUnsafeCode=true；引用 TEngine.Runtime/UniTask/YooAsset/GameProto/FairyGUI 等 |
| HybridCLR.Runtime / UniTask / YooAsset 2.3.17 / FairyGUI | Packages 或 ThirdParty | 否 | YooAsset、UniTask 为嵌入式本地包（packages-lock.json: `file:YooAsset`、`file:UniTask`） |

HybridCLR 配置三处对照（含义不同，勿混淆）：

| 配置 | 位置 | 当前值 | 作用 |
|------|------|--------|------|
| `HybridCLRSettings.enable` | ProjectSettings/HybridCLRSettings.asset:15 | **1** | HybridCLR 插件自身开关（决定 BuildProcessors 是否注入 il2cpp 源码） |
| `hotUpdateAssemblies` | 同上:20-22 | GameProto, GameLogic | 编译热更 DLL 的名单 |
| `patchAOTAssemblies` | 同上:27-34 | mscorlib/System/System.Core/TEngine.Runtime/YooAsset/UniTask/UnityEngine.CoreModule | 补充 metadata 名单 |
| `ENABLE_HYBRIDCLR` 宏 | ProjectSettings.asset:795-809 | **缺失** | 决定 `UpdateSetting.Enable` 与运行时热更分支；由 TEngine 菜单手工添加（BuildDLLCommand.cs:75-95），非自动。已复核：含运行时代码的 asmdef `versionDefines` 均无 HybridCLR 条目（TEngine.Runtime.asmdef 仅 InputSystem；GameLogic.asmdef 仅 URP；TEngine.Editor.asmdef/TEngine.FairyGUI 为空），无 csc.rsp/mcs.rsp——宏不存在其他自动注入途径 |

注意 `UpdateSetting.asset` 的 `AOTMetaAssemblies` 与 `HybridCLRSettings.patchAOTAssemblies` 基本一致（均含 UnityEngine.CoreModule），但 `UpdateSetting.cs:75` 的 C# 默认值少一项——以 asset 实际序列化值为准。

---

## 4. DLL 的 Bundle 粒度、标签与寻址

### 4.1 采集器配置（Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset）

- `DefaultPackage`：`EnableAddressable=1`、`SupportExtensionless=1`、`AutoCollectShaders=1`（:19-26）。另有 `OtherPackage/Dlc1Package/Dlc2Package` 三个空壳包（Collectors 为空，:179-230），运行时未使用。
- **DLL 组**（:67-79）：`CollectPath=Assets/AssetRaw/DLL`，`AddressRuleName=AddressByFileName`，`PackRuleName=PackDirectory`，`FilterRuleName=CollectAll`，**无 AssetTags**。
  - 含义：全部 DLL `.bytes`（热更 + AOT metadata 混在同一目录）打进**同一个 bundle**；任一 DLL 变更即整包重下。
  - 寻址：addressable 开启 → 资源地址 = 文件名（`GameLogic.dll`）。
- 其余组均 `PackDirectory`，仅 UI 组 `PackSeparately`（:132-144）；**所有组均未设置 `PRELOAD`/`WEBGL_PRELOAD` 标签**，而 `ProcedurePreload` 恰恰按这两个标签取资源（ProcedurePreload.cs:109,116）。当前标签为空 ⇒ `GetAssetInfos("PRELOAD")` 返回空 ⇒ 预加载为空操作（PreloadRequestRunner.IsEmpty → 直接跳 LoadAssembly）。属"代码支持、配置未用"。
- **`Assets/AssetRaw/DLL` 目录当前为空**（无任何 `.bytes`），进一步证实本工作区从未（或尚未）在本机完成热更 DLL 编译复制。

### 4.2 运行时寻址规则

`ProcedureLoadAssembly.GetAssemblyAssetLocation`（ProcedureLoadAssembly.cs:314-326）：
- `_enableAddressable=true`（硬编码 :28，与采集器一致）→ 地址 = `configuredName`（即 `"GameLogic.dll"`、`"mscorlib.dll"`）。
- 备用（addressable 关）→ `Assets/AssetRaw/DLL/GameLogic.dll.bytes`（`AssemblyTextAssetPath`+`AssemblyTextAssetExtension`，UpdateSetting.asset:28-29）。
- **AOT metadata 与热更 DLL 使用完全相同的寻址规则**（同一函数），加载顺序为先 metadata 后热更 DLL（ProcedureLoadAssembly.cs:142-149）。

`UpdateSetting.ReplaceAssetPathWithAddress=false` 是**构建期**参数（仅 ReleaseTools.cs:425 的 ScriptableBuildPipeline 使用），与运行时 `_enableAddressable` 无连线；运行时代码从未读取该字段。

### 4.3 "只新增启动标签能否独立下载更新器 DLL"——当前不能

- 下载器创建不按标签：`package.CreateResourceDownloader(DownloadingMaxNum, FailedTryAgain)`（ResourceModule.cs:818-833），下载**全部**待更新文件；YooAsset 支持 `downloader.DownloadByTags`，但本工程**未接**。
- DLL 混在 `PackDirectory` 单 bundle 且无标签，即使给 DLL 加 tag，bundle 粒度仍使其无法与其他内容分离。
- 因此"新增启动标签 → 独立下载热更更新器 DLL"需要至少两处改动接缝（标签化 DLL 组 + 改 PackRule/PackSeparately 或独立目录，并接入 `DownloadByTags`）；当前结构不满足。**此处仅指认接缝，不做迁移设计。**

### 4.4 业务入口传参不变项

- `GameApp.Entrance(object[] objects)`：`objects[0] = List<Assembly> _hotfixAssemblyList`（ProcedureLoadAssembly.cs:409-410；GameApp.cs:37-53）。
- 入口契约由 `StartupEntryContract.TryFindEntrance` 精确校验：唯一 `public static void Entrance(object[])`，签名不符即失败（ProcedureBase.cs:126-184）。
- 该传参形态是主包与热更域之间**唯一的数据交接**，移植时必须原样保留。

---

## 5. 发布管线与 DLL/内容更新先后关系

`ReleaseTools.ExecuteBuildWithResult`（Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:166-260）：

```
Preflight（无副作用校验，含 HybridCLR 可用性）
→ Dll（BuildHotFixDll=true: 编译并复制 DLL.bytes；false: 复用校验哈希一致）
→ AssetBundle（YooAsset ScriptableBuildPipeline(默认)/BuiltinBuildPipeline，LZ4，BundleName_HashName，ClearAndCopyAll 到 StreamingAssets）
→ MinimalPackage（可选，RetainTags 保留 + 删除其余内置文件）
→ Player（BuildPipeline.BuildPlayer）
→ Completed
```

- DLL 编译复制（BuildDLLCommand.cs:149-195 + BuildBaseCopyPlan:390-409）：先 `CompileDllCommand.CompileDll(target)`，从 `HybridCLRData/HotUpdateDlls/<target>`（热更）与 `HybridCLRData/AssembliesPostIl2CppStrip/<target>`（AOT 裁剪产物）复制到 `Assets/AssetRaw/DLL/*.bytes`；**缺任一文件整体失败**（先校验后复制，不允许 continue）。
- **先后关系结论**：热更 DLL 必须在 AssetBundle 构建前进入 `AssetRaw/DLL`，与内容一起打 bundle；AOT strip 产物则要求**先出过一次该平台的 Player**（注释明确：ReleaseTools.cs:394）。即发布顺序约束为：Player（产 AOT strip）→ DLL 编译复制 → AB → （可再出 Player 打入内置文件）。`isAutoAssetCopeToBuildAddress=0`（UpdateSetting.asset:35），"复制资源到打包后 StreamingAssets"的自动步骤关闭，需依赖 `BuildinFileCopyOption=ClearAndCopyAll` 在 AB 构建时写入 StreamingAssets。
- CLI 路径（旧 `BuildInternal`）：`BuildHotFixDll=false`，只构建 AB，DLL 由前置步骤准备（ReleaseTools.cs:55-99）。`BuildCLI/build_android.bat` 走此 CLI 路径。
- 构建失败语义：任一阶段失败/取消立即停止下游，阶段化错误保留在 `BuildExecutionResult`（BuildExecutionResult.cs），`BuildPreflight` 显式禁止"请求编译热更 DLL 但 HybridCLR 未启用"时空操作成功（BuildPreflight.cs:51）。

---

## 6. 失败语义逐项核查

| 场景 | 代码行为 | 证据 |
|------|---------|------|
| 包初始化失败/超时 | 60s（`UniTask.Timeout`，UnscaledDeltaTime）；失败弹 `LoadUpdateUI`+MessageBox（确认=重试 `StartAttempt`，取消=`Application.Quit`）；对 `PackageManifest_DefaultPackage.version ... 404` 有特化中文提示（指向 StreamingAssets 缺文件） | ProcedureInitPackage.cs:21,68-98,169-201 |
| 远端版本/manifest 失败 | 协程内检查 `operation.Status`；Host 模式先走 `IsNeedUpdate()`：`UpdateStyle=Optional` 且非边玩边下时回退 `PlayerPrefs["GAME_VERSION"]`，无记录则强制更新提示；`UpdateNotice=Notice` 时提示可选更新（确认重试/取消跳 Preload 直接进游戏） | ProcedureInitResources.cs:84-133,143-241 |
| 下载失败 | YooAsset `DownloadErrorCallback` → 弹窗：确认=回 `ProcedureCreateDownloader` 重建下载器重试（可无限循环），取消=退出；`failedTryAgain=3` 由 YooAsset 内部逐文件重试。次要观察：`BeginDownload` 在 `await downloader` 结束后若 `Status != Succeed` 仅静默 `return`（ProcedureDownloadFile.cs:94-95）——正常失败会先触发错误回调弹窗，但"无回调的失败/取消"路径会使流程停留在 DownloadFile 无任何 UI 提示 | ProcedureDownloadFile.cs:100-115；ResourceModule.cs:831 |
| 热更 DLL/metadata/入口失败 | 60s 超时；失败仅弹窗 + `Application.Quit`（确认与取消都退出），提示"程序集加载不可回滚，请重启游戏或退出" | ProcedureLoadAssembly.cs:443-458 |
| 下载取消 | 无用户取消下载的入口；确认弹窗的"取消"即退出进程 | ProcedureCreateDownloader.cs:81-82 |
| 重复进入/旧回调 | `StartupAttempt` 终态（Running/Succeeded/Failed/Cancelled）+ `OnLeave` Invalidate+Dispose；晚到的旧代际回调经 `IsCurrentRunning`（`ModuleSystem.IsRunning && ReferenceEquals(_currentAttempt, attempt) && attempt.IsRunning`）丢弃 | ProcedureBase.cs:23-121；ProcedureInitPackage.cs:156-167 |
| 程序集不可卸载 | `_entranceInvoked`/`_startGameSubmitted` 一次性闸门：Entrance 已调用后禁止本进程再次注入程序集（重试只能重启）；Entrance 抛异常在 Invoke **前**置位闸门 | ProcedureLoadAssembly.cs:385-428 |
| 下载进度 | 滑动窗口平均速度 + 剩余时间估算，`OnLeave` 清空 `DownloadErrorCallback/DownloadUpdateCallback` 防悬挂 | ProcedureDownloadFile.cs:23-37,55-71 |
| 弱网/断点续传 | 框架层无实现（无磁盘空间检查、无限速、无并行度自适应）；依赖 YooAsset 2.3.17 内置行为 | ProcedureCreateDownloader.cs:73 注释自认"开发者需要在下载前检测磁盘空间不足"但未实现 |
| 强更 | `UpdateStyle=Force`（UpdateSetting.asset:30）⇒ `IsNeedUpdate` 恒 true，无跳过路径 | ProcedureInitResources.cs:192-240 |
| 缓存清理 | 仅 `ClearUnusedBundleFiles` 模式且当前不可达（§2.2）；无用户入口清理全部缓存 | ProcedureClearCache.cs:28；ResourceModule.cs:840-856 |
| 重复成功调用包初始化 | 幂等：同参数且已成功返回已完成操作；参数冲突抛异常；初始化成功后失败只重试 manifest 不再 `InitializeAsync` | ResourceModule.cs:492-518,613-632 |

---

## 7. 版本一致性

- `GAME_VERSION`（PlayerPrefs）写入时机**不对称**：
  - `ProcedureInitResources`：拿到远端版本后**仅当 key 已存在**才写入（ProcedureInitResources.cs:111-114）；
  - `ProcedureDownloadOver`：下载完成后**无条件**写入（ProcedureDownloadOver.cs:21）。
  - 推断后果（未运行验证）：首次安装+下载中途退出 ⇒ 无本地记录 ⇒ 下次启动强制更新（安全）；但**已有记录用户**在 `InitResources` 成功后记录即被更新为新版本，若随后下载失败退出，下次启动 Optional 回退将指向"记录已新、缓存未新"的状态，依赖 YooAsset manifest/缓存校验兜底——此路径为**未验证风险**。
- 版本号格式：构建默认 `yyyy-MM-dd-HHmm`（BuildConfig.cs:50-54）。
- 清单版本获取：`RequestPackageVersionAsync(appendTimeTicks:false, timeout:60)` → `UpdatePackageManifestAsync(version, timeout:60)`（ResourceModule.cs:778-807）；`ResourceModule` 内另有 `needInitMainFest` 通道（当前 `ProcedureInitPackage.InitPackage` 调用不传，默认 false ⇒ manifest 更新统一由 `ProcedureInitResources` 负责，避免两处竞争）。

---

## 8. 工作区未提交修改：未来移植必须保留的终态约束

以下约束来自工作区当前（未提交）代码，属"已实现且被注释声明"的终态，任何热更更新器迁移**不得回退**：

1. **`StartupAttempt` 单次终态 + 代际隔离**（ProcedureBase.cs:23-121）：`TrySucceed/TryFail/TryCancel` 只能成功一次；`OnLeave` 必须 `Invalidate+Dispose`；所有异步完成点必须 `EnsureCurrentRunning`（含 `ModuleSystem.IsRunning`）。`ProcedureInitPackage`、`ProcedureLoadAssembly`、`ProcedureStartGame` 均已接入。
2. **ResourceModule bootstrap 事务**：`_initializationState`（NotStarted/Initializing/Succeeded/Failed）+ `_initializationCompletionSource`；重复 `Initialize()` 幂等；Driver 侧异常必须 `FailInitialization` 使等待者见到真实原因而非超时（ResourceModule.cs:334-470；ResourceModuleDriver.cs:294-304）。
3. **包初始化 context 协议**：同参数幂等/复用，参数冲突报错；**已成功的 YooAsset InitializeAsync 不可重放**，失败只重试 manifest（ResourceModule.cs:492-518,613-632，注释 :620-627）。
4. **关闭阶段化（ModuleSystem 八阶段）**：`StopWork → StopProcedures → BeforeShutdown → ShutdownModules → ReturnResourceInstances → ShutdownObjectPools → FinalizeResources → ClearState`（ModuleSystem.cs:20-32,183-225）；ResourceModule 关闭四步 `BeginShutdown（取消 LifetimeToken+bootstrap 等待者）→ ReleaseOwnedInstances → MarkAssetPoolClosed → FinalizeShutdown（仅 `_ownsYooAssets` 时 `YooAssets.Destroy`）`（ResourceModule.cs:72-199）。所有新请求入口 `EnsureAcceptingRequests` 抛 GameFrameworkException（:81,201-207）。
5. **热更域释放链**：`GameApp.Entrance` 挂 `RootModule.BeforeShutdown += Release` + `Utility.Unity.AddDestroyListener(Release)`；`Release` 幂等（`_releaseStarted`），顺序为 `SingletonSystem.Release()` → `GameModule.Shutdown()`（GameApp.cs:49-113）。`RootModule.BeforeShutdown` 在关闭开始后拒绝新订阅（RootModule.cs:23-35）。
6. **预加载代际执行器**：`PreloadRequestRunner`（新文件）：去重发起、成功回调**先配对归还 spawn 再判代际**、晚到回调只归还资源不污染新代际状态、失败也计终态防永久等待（PreloadRequestRunner.cs:109-234）。
7. **下载回调解绑**：`ProcedureDownloadFile.OnLeave` 置空 `DownloadErrorCallback/DownloadUpdateCallback`（ProcedureDownloadFile.cs:55-71）。
8. **DLL 身份校验**：配置名与程序集名（含 `.dll` 后缀归一）逐一比对，重复解析即失败（ProcedureLoadAssembly.cs:341-369）；配置名去重告警（:172-199）。
9. 相关测试目录已存在（`Assets/Tests/StartupLifecycle|ShutdownLifecycle|ResourceLifecycle|UILifecycle`，未运行）；另有既有分析草稿 `docs/framework-reference/project-old/hot-update/reference-analysis.md`，**本报告未采信其内容**，仅注记存在。

---

## 9. 优缺点（就当前实现本身）

**优点**
- 标准且完整的 YooAsset 2.3.17 + HybridCLR 接入：主备下载地址（RemoteServices fallback，ResourceModule.Services.cs:23-31）、三种解密服务（FileOffSet/FileStream/Web，加密 `None` 当前生效）。
- 更新策略可配（Force/Optional + Notice），Optional 断网回退本地版本是少见的务实设计。
- （工作区新增）启动终态代际、关闭阶段化、bootstrap 事务化使异步取消/重复进入语义严谨，明显优于裸轮询 FSM。
- DLL 身份校验 + 入口反射契约，把"错 DLL/重 DLL/错签名"从难查的运行时错提前为明确报错。
- 构建管线阶段化 + 先校验后复制，防止旧产物掩盖缺失。

**缺点/风险**
- `ENABLE_HYBRIDCLR` 宏与 HybridCLR 安装、DLL 产物三者均缺失，当前出 Player 即纯离线；宏状态靠手工菜单维护，易与 CI 期望漂移（构建期可覆盖但未证实）。
- DLL `PackDirectory` 单 bundle：任一 DLL 变更全量重下；AOT metadata 与热更 DLL 同 bundle，难以做更新器分离。
- 下载确认弹窗"取消=退出进程"，无跳过/后台下载选项；Optional 分支也仅在 HostPlayMode 生效，**WebPlayMode 无 Optional 回退**（OnInitResourcesError 的 IsNeedUpdate 分支仅包在 HostPlayMode 下，ProcedureInitResources.cs:151-171）。
- 无磁盘空间检查（注释自认）、无限速、无用户取消下载。
- `ProcedureInitResources` 用 Unity 协程而非 UniTask（主包代码不受热更规范约束，但与框架"异步优先"红线不一致）；`ModuleSystem.IsRunning` 守卫重复散布于每个回调。
- `_needClearCache` 硬编码 false ⇒ 清缓存流程不可达。
- `GAME_VERSION` 写入时机不对称（§7）。
- 超时 60s 为硬编码默认（两个可注入属性仅测试用）。

---

## 10. 改成"可热更更新器"的具体接缝（仅指认，不设计/不实现）

1. **更新器代码位置**：更新流程全部在 `Launcher` + `Procedure/*`（非热更主包）。可热更化的最小接缝是 `ProcedureLoadAssembly.SubmitEntrance` 的反射调用点（ProcedureLoadAssembly.cs:385-428）——主包只需保留"引导下载器 + 加载第一段 DLL"能力，其余更新 UI/逻辑下沉进热更域。前提是第一段 DLL 的寻址规则（§4.2）保持稳定。
2. **下载器标签接缝**：`ResourceDownloaderOperation` 未接 `DownloadByTags`（ResourceModule.cs:831）；DLL 组 `PackDirectory`（CollectorSetting.asset:67-79）需重新评估粒度。两者是"独立下载更新器 DLL"的必要条件（§4.3 结论：当前不成立）。
3. **PlayMode/URL 注入接缝**：`ResourceModule.SetRemoteServicesUrl`（ResourceModule.cs:788-792）与 `ResourceModuleDriver` 属性均为运行时可写；更新器若由热更域驱动，可通过此接缝改写远端地址。当前唯一写入方是 Driver.Start。
4. **更新策略读取接缝**：`UpdateStyle/UpdateNotice` 仅在 `ProcedureInitResources.IsNeedUpdate` 消费（ProcedureInitResources.cs:192,213）；策略判断可下沉，但需保留 `GAME_VERSION` 读写语义（§7 的不对称性若保留需显式说明）。
5. **风险清单**：
   - 程序集不可卸载 ⇒ 热更更新器自身的失败重试只能重建流程状态、不能重载程序集；`_entranceInvoked` 闸门必须保留。
   - 主包保留的"引导下载器"仍依赖 AOT metadata 完整（mscorlib 等 7 个 metadata 名单），任何 metadata 缺失在 Player 上直接失败（ProcedureLoadAssembly.cs:273-276 对无宏 Player 抛错）。
   - WebGL 路径（`WebPlayMode` + `LoadResWayWebGL=Remote`）下 Update/Preload 语义与 Host 不同（跳过下载阶段直入 Preload，ProcedureInitResources.cs:66-72），两段式更新器需分别验证。
   - 启动超时、LauncherMgr UI 均为主包资源；热更 UI 与主包 UI（FguiModule/UIModule）的可见性交接点在 `ProcedureStartGame.HideLauncherAfterYield`（ProcedureStartGame.cs:45-62）。

---

## 11. 未验证事项 / 未知项

1. **未做运行验证**：未运行 Unity 编辑器 Play、未出 Player、未连服务器；所有"运行时会……"的表述均为静态代码推断。
2. `EditorPrefs["EditorPlayMode"]` 的当前实际值（决定编辑器内 Play 的真实模式）不落盘于工程文件，无法证实。
3. `ENABLE_HYBRIDCLR` 是否曾在某开发者本机通过菜单启用过（历史状态）无法从版本库证实；当前快照为未启用。
4. `Launcher.asmdef`/`GameLogic.asmdef` 中的 GUID `4140bd2e2764f1f47ab93125ecb61942` 在 `Assets/`、`Packages/`、`Library/`（排除 Artifacts）均未命中——推断为本地嵌入式包（YooAsset/UniTask）由 Unity 包管理器生成的非落盘 GUID，未最终证实（不影响流程结论）。
5. YooAsset 2.3.17 内部的下载重试/断点续传/HTTP 细节未逐行阅读（仅确认接口层参数：并发 10、失败重试 3、超时 60s）。
6. `WEIXINMINIGAME`（微信小游戏）分支依赖 `WeChatWASM`，`Packages/manifest.json` 未含微信 SDK 包，宏未在 ProjectSettings 出现 ⇒ 推断该分支当前不可编译，未验证。
7. `Assets/Tests/*` 各生命周期测试目录存在，但本轮未执行——**测试源码存在不等于测试通过**。
8. MinimalPackage 的 `RetainTags` 实际可用标签集（BuildPipelineWindow 运行时输入，非持久化）未核实。
9. 目录 `docs/framework-reference/project-old/hot-update/` 下已有 `reference-analysis.md` 与 `prompts/`，视为未经审查草稿，未采信。
10. `boot.config` 在工作区被删除（git status `D`），其对 Player 启动的影响未评估（Unity 会重新生成该文件）。

---

## 12. 证据索引（绝对路径 + 行号 + 符号）

**Procedure 流程（Assets/GameScripts/Procedure/）**
- `ProcedureBase.cs:13` `_resourceModule` 字段；`:23-121` `StartupAttempt`；`:126-184` `StartupEntryContract.TryFindEntrance`
- `ProcedureLaunch.cs:23-35` `OnEnter`（LauncherMgr.Initialize）；`:42` → Splash
- `ProcedureSplash.cs:19` → ProcedureInitPackage
- `ProcedureInitPackage.cs:21` 60s 默认超时；`:68-98` `InitializePackage` 超时/取消/失败三分支；`:101-154` `InitializePackageCore`（:109 `WaitUntilInitializedAsync`；:113 `InitPackage`；:143-146 Host/Web 显示 LoadUpdateUI；:148-153 `TrySucceed`→ChangeState）；`:169-201` `FailAttempt/Retry`（:177 404 特化）
- `ProcedureInitResources.cs:34` 协程启动；`:60-79` PlayMode 分流（:66-72 Web/边玩边下跳下载）；`:95-109` `RequestPackageVersionAsync`+`GAME_VERSION` 条件写；`:119-130` `UpdatePackageManifestAsync`；`:143-182` `OnInitResourcesError`；`:184-241` `IsNeedUpdate`（:192 Optional 判定）
- `ProcedureCreateDownloader.cs:46-84` `CreateDownloader`（:55 创建下载器；:62 空下载直跳；:81 确认弹窗）
- `ProcedureDownloadFile.cs:73-98` `BeginDownload`（:94-97 状态检查→DownloadOver）；`:100-115` 错误回调（:112 回 CreateDownloader）；`:55-71` OnLeave 解绑
- `ProcedureDownloadOver.cs:12` `_needClearCache=false`；`:21` 无条件写 `GAME_VERSION`；`:24-34` 跳转
- `ProcedurePreload.cs:41` 新建 `PreloadRequestRunner`；`:73-89` 空清单/终态判断；`:100-128` `LoadAllConfig`（:109 `GetAssetInfos("PRELOAD")`；:115-121 `#if UNITY_WEBGL` WEBGL_PRELOAD）；`:50-62` OnLeave Invalidate
- `ProcedureLoadAssembly.cs:26` 60s 超时；`:28` `_enableAddressable=true`；`:88-119` 超时/取消/失败；`:127-150` `useAlreadyLoadedAssemblies` 分流；`:218-258` `LoadHotUpdateAssembly`（:228 LoadAssetAsync；:239 `Assembly.Load`；:253-256 finally UnloadAsset）；`:260-312` `LoadMetadataForAOTAssemblies`（:265 `#if UNITY_EDITOR return`；:273-275 无宏 Player 抛错；:293-294 SuperSet + `RuntimeApi.LoadMetadataForAOTAssembly`）；`:314-326` `GetAssemblyAssetLocation`；`:341-369` `AddAssemblyWithIdentityCheck`；`:385-428` `SubmitEntrance`（:393 `GetType("GameApp")`；:406 闸门前置；:409-410 传参+Invoke；:420-427 TrySucceed→StartGame）；`:443-458` `FailAttempt`（只允许 Quit）
- `ProcedureStartGame.cs:45-62` `HideLauncherAfterYield`（代际校验）
- `ProcedureClearCache.cs:16-43` 清缓存（当前不可达）

**资源模块（Assets/TEngine/Runtime/Module/ResourceModule/）**
- `ResourceModuleDriver.cs:69-91` `PlayMode`（编辑器 EditorPrefs / Player 回退 Offline）；`:236-305` `Start`（:259-269 参数注入；:281 Initialize；:294-304 异常→FailInitialization）；`:313-325` ForceUnloadUnusedAssets；`:328-360` Update 的 UnloadUnusedAssets 周期策略
- `ResourceModule.cs:22` DefaultPackageName="DefaultPackage"；`:27` PlayMode 默认 Offline；`:45` Priority=4；`:47-56` OnInit 重置；`:62-70` Update（AssetsReference 回收补偿）；`:72-79` Shutdown 四步；`:81` IsAcceptingRequests；`:104-138` LifetimeToken/BeginShutdown；`:150-199` FinalizeShutdown（:179-182 `_ownsYooAssets` 才 `YooAssets.Destroy`）；`:201-207` EnsureAcceptingRequests；`:212-214` HostServerURL；`:269-304` 包初始化参数记录；`:334-397` `Initialize()`；`:437-453` `WaitUntilInitializedAsync`；`:458-470` `FailInitialization`；`:472-536` `InitPackage`（:492-518 幂等/冲突/manifest 重试协议）；`:538-557` `CapturePackageInitializationParameters`（:540-542 编辑器读 EditorPrefs）；`:568-633` `InitializePackageAsync`（:607-632 失败协议）；`:635-704` `CreatePackageInitializationOperation`（四种 PlayMode 参数，:685-698 WebGL/微信分支）；`:706-724` `InitializePackageManifestAsync`；`:778-807` 版本/manifest API；`:812-833` Downloader/CreateResourceDownloader（:831 并发/重试参数）；`:840-856` ClearCacheFilesAsync
- `ResourceModule.Services.cs:12-32` `RemoteServices`（主/备 URL 拼接）；`:37-241` 加密/解密服务；`:247-269` `BundleStream`
- `PreloadRequestRunner.cs:19-261`（:27 `_generation`；:109-140 `Begin` 去重；:146-149 `Invalidate`；:182-206 `OnPreloadSuccess` 配对归还+代际丢弃；:208-218 `OnPreloadFailure`）

**框架/会话（Assets/TEngine/Runtime/）**
- `Core/ModuleSystem.cs:10-32` 状态与阶段枚举；`:76` IsRunning；`:92-118` 会话重置；`:142-150` Root 所有权关闭；`:183-225` `Shutdown()` 八阶段；`:444-469` `StopNewWork`（Resource/ObjectPool/UpdateDriver BeginShutdown）；`:471-475` `StopProcedures`；`:500-532` ReturnResourceInstances/ShutdownObjectPools/FinalizeResources；`:264-294` `GetModule<T>`（:280-284 关闭期禁止创建）
- `Module/RootModule.cs:16-35` `BeforeShutdown`（关闭开始后拒绝订阅）；`:39-44` 会话重置
- `Core/UpdateSetting.cs:59-69` `Enable`（仅看宏）；`:72-75` 程序集名单默认值；`:93-95` Force/Notice 默认；`:100-107` 下载地址字段；`:167-178` `GetResDownLoadPath/GetFallbackResDownLoadPath`（projectName/平台子目录拼接，projectName="Demo"）；`:184-218` `GetPlatformName`
- `Module/ProcedureModule/ProcedureSetting.cs:55-112` `StartProcedure`
- `Module/UpdataDriver/UpdateDriver.cs:18-60` OnInit/BeginShutdown/Shutdown
- `Runtime/AssemblyInfo.cs`（新增，未提交）——InternalsVisibleTo 相关，未逐条核对用途

**启动配置（Assets/TEngine/Settings/）**
- `ProcedureSetting.asset:15-27` 11 流程 + 入口 ProcedureLaunch
- `UpdateSetting.asset:15-36`（:16-18 热更名单；:19-26 AOT 名单；:27 主 DLL；:30-31 Force/Notice；:32-33 双地址；:34 WebGL=Remote(0)；:35 自动拷贝关闭）
- `Prefab/GameEntry.prefab:209-215` ResourceDriver 序列化值（playMode:0 等）；`:349-351` Settings 引用三个 asset
- `Assets/TEngine/Settings/Resources/YooAssetSettings.asset` 存在（YooAsset 全局设置，未展开）

**编辑器/构建**
- `Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs:20` 宏名；`:62-95` Enable/Disable 菜单；`:149-195` `BuildAndCopyDllsWithResult`；`:390-409` `BuildBaseCopyPlan`（AOT strip 目录 + 热更输出目录 → AssetRaw/DLL）
- `Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs:105-147` 菜单入口（:109 F8 一键 AB 强制 BuildHotFixDll）；`:166-260` 阶段编排；`:361-394` AB 阶段；`:410-455` 构建参数（:425 ReplaceAssetPathWithAddress；:430 BuildinFileRoot=StreamingAssets）；`:55-99` CLI 路径
- `BuildConfig.cs:12-36` 默认值（ScriptableBuildPipeline/LZ4/ClearAndCopyAll/BundleName_HashName/BuildHotFixDll=true）
- `BuildPreflight.cs:51` HybridCLR 未启用时 DLL 构建请求必须失败
- `Assets/Editor/ToolbarExtender/UnityToolbarExtenderRight/EditorPlayMode.cs:42-79` 编辑器 PlayMode 工具栏（写 EditorPrefs）
- `Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset:19-26`（DefaultPackage addressable）、`:67-79`（DLL 组）、`:132-144`（UI PackSeparately）、`:179-230`（空壳包）

**工程/环境**
- `ProjectSettings/ProjectSettings.asset:795-809` 各平台 define 无 ENABLE_HYBRIDCLR；工作区 diff 仅 `runInBackground: 0→1`
- `ProjectSettings/HybridCLRSettings.asset:15-40`（enable=1、名单、输出目录、link.xml/AOTGenericReferences 输出）
- `ProjectSettings/EditorBuildSettings.asset`（唯一场景 main.unity）
- `Packages/manifest.json:3` HybridCLR git 包；`Packages/packages-lock.json`（YooAsset/UniTask 嵌入式；YooAsset 2.3.17 见 `Packages/YooAsset/package.json:3`）
- `Packages/YooAsset/Runtime/InitializeParameters.cs:8-34` EPlayMode 枚举
- `Assets/Scenes/main.unity:273-313` GameEntry prefab 实例（guid 匹配）
- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs:37-53` Entrance；`:78-113` Release
- `Assets/GameScripts/GameEntry.cs:6-14` Awake 装配
- 工作区状态：`git status --porcelain`（60 个已修改 + 1 个删除的已跟踪文件，另 23 个未跟踪条目：新增测试目录/文档/AssemblyInfo/PreloadRequestRunner 等，均为用户既有未提交修改，本轮零写入）

---

## 13. 关键文件快照（SHA256 前 16 位 / 字节数 / 工作区相对路径）

```
8007c9e6a7f9968b   5637  Assets/GameScripts/Procedure/ProcedureBase.cs
6153ec7109acec4f   7964  Assets/GameScripts/Procedure/ProcedureInitPackage.cs
4052d6cf171c5009   8772  Assets/GameScripts/Procedure/ProcedureInitResources.cs
61cb40b9cff37866   2847  Assets/GameScripts/Procedure/ProcedureCreateDownloader.cs
88dda07b2d782a32   5464  Assets/GameScripts/Procedure/ProcedureDownloadFile.cs
99e3c0df18c2ce87   1059  Assets/GameScripts/Procedure/ProcedureDownloadOver.cs
ba21e42e7ed808d7   4316  Assets/GameScripts/Procedure/ProcedurePreload.cs
d9ff049c474cbbcd  17660  Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs
1c9d6d29e70cd4ee   1940  Assets/GameScripts/Procedure/ProcedureStartGame.cs
da0914655b5d9c1a   1226  Assets/GameScripts/Procedure/ProcedureClearCache.cs
289ab83de59f6d08   6863  Assets/TEngine/Runtime/Core/UpdateSetting.cs
d80628445ad22eba   1024  Assets/TEngine/Settings/UpdateSetting.asset
3c409d955c1f51e8    896  Assets/TEngine/Settings/ProcedureSetting.asset
261d035cf2fc02b3  12068  Assets/TEngine/Settings/Prefab/GameEntry.prefab
f1e299beffda829a  12243  Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs
5d54edae4ef73e92  90391  Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs
137b5dd89d12733e   9109  Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Services.cs
e58f15d5498f1e2d   8621  Assets/TEngine/Runtime/Module/ResourceModule/PreloadRequestRunner.cs
e416502f3bd0acaa   3373  Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureSetting.cs
60cac0a5b70d6baa  22996  Assets/TEngine/Runtime/Core/ModuleSystem.cs
fa7911e13e0caf2a    411  Assets/GameScripts/GameEntry.cs
6cba082e2bc1d00d   3179  Assets/GameScripts/HotFix/GameLogic/GameApp.cs
aa33aecb13c1daae   6870  Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset
9e37f528fac4c6bb   1319  ProjectSettings/HybridCLRSettings.asset
3d15ee8ebaeeecb0  25301  ProjectSettings/ProjectSettings.asset
8bb041b6b72606c3  17101  Assets/TEngine/Editor/HybridCLR/BuildDLLCommand.cs
beecae1b46084dd8  33399  Assets/TEngine/Editor/ReleaseTools/ReleaseTools.cs
467174ed6d7fc147  15866  Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs
6416f5c140e7258f  12252  Assets/TEngine/Runtime/Module/RootModule.cs
dbbf7dd9a87dfde6   1793  Packages/manifest.json
```

（快照哈希用于后续比对工作区漂移；本次调查零文件写入，报告本身除外。）

---

## 14. 结论重申

- **代码支持联机热更新：是**——但更新器（Procedure/Launcher）本身不可热更，且 DLL 打包粒度与无标签现状不支持"只下更新器"。
- **按当前工作区配置出 Player 实际启用热更新：否**——缺 `ENABLE_HYBRIDCLR` 宏、缺 HybridCLR 安装产物、缺 DLL.bytes、缺 StreamingAssets 内置数据，且 GameEntry.prefab 的 playMode 序列化值在 Player 侧回退为 OfflinePlayMode。
- 上述"否"中的每一项都存在明确的**手工/构建期开启路径**（菜单加宏、Installer 安装、F8 一键构建写 StreamingAssets、工具栏/prefab 切 PlayMode），它们是否在某台构建机上已被启用，属本工作区无法证实的未知项。
