# TEngine 架构与项目结构

> **适用场景**：理解项目分层架构、程序集划分、目录结构、启动流程 | **关联文档**：[modules.md](modules.md)、[hotfix-workflow.md](hotfix-workflow.md)、[resource-api.md](resource-api.md)

## 核心架构

### 分层架构

```
┌─────────────────────────────────────────────┐
│         游戏业务层 (HotFix)                  │
│  GameLogic / GameProto / GameUpdater        │
│  ↑ 热更代码，业务逻辑，高频变更              │
└─────────────────────────────────────────────┘
                     ↓ 依赖
┌─────────────────────────────────────────────┐
│      框架应用层 (Launcher + Assembly-CSharp) │
│  GameEntry 启动入口 / Procedure 主包流程     │
│  LauncherMgr 启动器 UI（不参与热更）         │
└─────────────────────────────────────────────┘
                     ↓ 依赖
┌─────────────────────────────────────────────┐
│         框架核心层 (TEngine.Runtime)         │
│  ModuleSystem / GameEvent / ResourceModule  │
│  Settings / UpdateSetting / ProcedureSetting │
└─────────────────────────────────────────────┘
                     ↓ 依赖
┌─────────────────────────────────────────────┐
│      基础设施层 (Unity + 第三方)             │
│  YooAsset / HybridCLR / UniTask / FairyGUI  │
└─────────────────────────────────────────────┘
```

**依赖规则**：单向向下依赖，上层不能被下层引用，热更代码不能反向依赖主工程 internal 类型。

---

### 程序集划分

| 程序集 | 路径 | 热更 | 职责 |
|--------|------|------|------|
| `TEngine.Runtime` | Assets/TEngine/Runtime/ | 否 | 框架核心 |
| `TEngine.Editor` | Assets/TEngine/Editor/ | 否 | 编辑器工具 |
| `Launcher` | Assets/Launcher/ | 否 | 启动器 UI（LauncherMgr、LoadUpdateUI、LoadTipsUI 等） |
| Assembly-CSharp | Assets/GameScripts/ | 否 | GameEntry 启动入口、Procedure/ 主包流程（无自定义 asmdef） |
| `GameProto` | Assets/GameScripts/HotFix/GameProto/ | 是 | Luban 配置代码（LubanLib 基础库 + 导表生成的 GameConfig） |
| `GameUpdater` | Assets/GameScripts/HotFix/GameUpdater/ | 是 | 可热更的第二阶段更新器入口（GameUpdater.Entry.RunAsync），仅依赖 TEngine.Runtime 与 UniTask 稳定合同 |
| `GameLogic` | Assets/GameScripts/HotFix/GameLogic/ | 是 | 业务逻辑，热更主入口（GameApp.Entrance） |

**约束**：
- `GameLogic` 可依赖 `GameProto`、`TEngine.Runtime` 与 FairyGUI（`FairyGUI`/`TEngine.FairyGUI`，见 GameLogic.asmdef）
- `GameUpdater` 仅依赖 `TEngine.Runtime` 与 `UniTask`（见 GameUpdater.asmdef，RunAsync 返回 `UniTask<UpdateStageResult>`），不得引用 GameLogic/GameProto
- 热更代码不能引用主工程 internal 类型
- `GameEntry.cs` 和 `Procedure/` 属于 Assembly-CSharp，不参与热更
- 流程列表与入口流程由 `ProcedureSetting` 资产配置（Assets/TEngine/Settings/ProcedureSetting.asset），入口为 `Procedure.ProcedureLaunch`

---

### 目录结构

```
TEngine/
├── Configs/GameConfig/           # Luban 配置表工程（luban.conf、Defines/、Datas/）
│   ├── Datas/                    # Excel 数据源（__tables__/__beans__/__enums__.xlsx）
│   └── gen_code_bin_to_project.bat（另有 _lazyload、gen_code_bin_to_server 变体）
│
├── Tools/                          # 工具目录（build-luban.bat 构建 Luban、GameEventSourceGenerator 生成器源码）
│
├── UnityProject/Packages/         # 本地 UPM 包（YooAsset、UniTask、MCPForUnity 内嵌源码；HybridCLR 经 manifest.json 以 git 包引入）
│
└── UnityProject/Assets/
    ├── AssetRaw/                 # 热更资源目录（YooAsset 打包来源）
    │   ├── Actor/ Audios/ Bootstrap/ Configs/ DLL/ Effects/
    │   ├── FGUI/ Fonts/ Materials/ Scenes/ Shaders/ UI/ UIRaw/
    │   └── ...
    ├── AssetArt/                 # 美术源资源（不参与打包收集）
    ├── Editor/                   # 主工程编辑器工具（YooAsset 收集器配置 AssetBundleCollector/）
    ├── Scenes/main.unity         # 启动场景（实例化 TEngine/Settings/Prefab/ 下的 GameEntry.prefab 与 UIRoot.prefab）
    ├── ThirdParty/FairyGUI/      # FairyGUI 第三方源码（FairyGUI 程序集）
    ├── Tests/                    # EditMode/PlayMode 测试程序集
    ├── Launcher/                 # 启动器模块
    │   ├── Resources/UIWindow/   # LoadUpdateUI、LoadTipsUI Prefab
    │   └── Scripts/              # LauncherMgr、LoadUpdateUI、LoadTipsUI、LoadText、UIBase 等
    ├── TEngine/                  # 框架核心（UPM 包形态，含 package.json）
    │   ├── Editor/               # 编辑器工具（Utility/ReleaseTools/LubanTools/Localization/HybridCLR/Inspector/AtlasMakerEditor/DefineSymbols 共 8 个子目录；HybridCLR 编辑器命令 BuildDLLCommand 在本目录，运行时包经 UPM 引入）
    │   ├── Runtime/              # 运行时核心（Core/ Extension/ Module/）
    │   ├── Extension/            # 框架扩展（HtmlToUGUI/ InputModule/）
    │   ├── Extensions/FairyGUI/  # FairyGUI 适配（TEngine.FairyGUI 程序集）
    │   ├── Libraries/            # 预编译 DLL（System.Buffers、System.Runtime.CompilerServices.Unsafe）
    │   └── Settings/             # ProcedureSetting/UpdateSetting/AudioSetting 资产
    │       ├── Resources/        # YooAssetSettings.asset
    │       └── Prefab/           # UIRoot.prefab、GameEntry.prefab
    └── GameScripts/              # 游戏逻辑脚本
        ├── GameEntry.cs          # 游戏启动入口（MonoBehaviour，经 Settings/Prefab/GameEntry.prefab 实例化进场景）
        ├── Procedure/            # 主包流程状态机（含 TwoStageUpdateCoordinator 协调器）
        └── HotFix/               # 热更代码
            ├── GameProto/        # LubanLib 基础库；导表生成的 GameConfig 输出至此
            ├── GameUpdater/      # 可热更第二阶段更新器（Entry.RunAsync）
            └── GameLogic/        # 业务逻辑主开发区域（GameApp 热更入口）
```

---

### 启动流程

```
GameEntry.Awake()                                 // Assets/GameScripts/GameEntry.cs
  ├── ModuleSystem.GetModule<IUpdateDriver>()    // 帧驱动
  ├── ModuleSystem.GetModule<IResourceModule>()  // 资源系统
  ├── ModuleSystem.GetModule<IDebuggerModule>()  // 调试器
  ├── ModuleSystem.GetModule<IFsmModule>()       // 状态机
  ├── Settings.ProcedureSetting.StartProcedure().Forget()  // 启动流程（资产配置入口流程）
  └── DontDestroyOnLoad(this)

主包流程（不可热更，入口为 ProcedureLaunch，由 ProcedureSetting.asset 配置）：
ProcedureLaunch → ProcedureSplash → ProcedureInitPackage → ProcedureInitResources
→ 分支：
  · EnableTwoStageUpdate → ProcedureTwoStageUpdate（下载 BOOTSTRAP → 加载 AOT metadata
    与更新器 DLL → 反射调用 GameUpdater.Entry.RunAsync → TwoStageUpdateCoordinator
    校验资源就绪）→ ProcedurePreload
  · HostPlayMode/WebPlayMode → ProcedureCreateDownloader → ProcedureDownloadFile
    → ProcedureDownloadOver →（需要时 ProcedureClearCache）→ ProcedurePreload
  · 离线/编辑器模拟/边玩边下载（WebPlayMode/UpdatableWhilePlaying）→ 直接 ProcedurePreload
→ ProcedurePreload → ProcedureLoadAssembly → ProcedureStartGame

两阶段流程由主包协调器 TwoStageUpdateCoordinator（Assets/GameScripts/Procedure/）管理
BOOTSTRAP 闭包、程序集记录与资源就绪检查；更新器装载与入口阶段默认 60s 超时
（ProcedureTwoStageUpdate.LoadTimeoutSeconds / ProcedureLoadAssembly.StartupTimeoutSeconds）。

热更入口：ProcedureLoadAssembly 加载完热更 DLL 后反射查找 GameApp 类型，
校验 public static void Entrance(object[]) 签名（StartupEntryContract）并同步调用：
  GameApp.Entrance(object[] objects)              // Assets/GameScripts/HotFix/GameLogic/GameApp.cs
  ├── ModuleSystem.IsRunning 检查     // 0. 模块系统关闭中则忽略
  ├── GameEventHelper.Init()          // 1. 必须最先调用
  ├── 保存热更程序集列表              // 2. objects[0] 为 List<Assembly>
  ├── RootModule.BeforeShutdown += Release       // 3. 注册关停回调
  ├── Utility.Unity.AddDestroyListener(Release)  // 4. 注册销毁回调
  └── StartGameLogic()                // 5. 启动游戏逻辑
入口同步返回成功后才切换到 ProcedureStartGame（隐藏启动器 UI）。
```

---

## 使用模式

### 资源目录组织

```
Assets/AssetRaw/              # 所有热更资源的根目录
├── Actor/                    # 角色 Prefab
├── Audios/                   # 音频
├── Bootstrap/DLL/            # 两阶段更新 BOOTSTRAP 标签资源（更新器 DLL + AOT metadata，收集器组 BootstrapDLL）
├── Configs/bytes/            # Luban 生成数据（gen_code_bin_to_project.bat 输出，需导表生成）
├── DLL/                      # 热更 DLL（HybridCLR 补充元数据 + 业务程序集）
├── Effects/                  # 粒子特效
├── FGUI/                     # FairyGUI 资源
├── Scenes/                   # 场景
├── Shaders/                  # Shader
├── UI/ UIRaw/                # UI 资源
└── ...
```

- **PRELOAD** 标签：`ProcedurePreload` 通过 `GetAssetInfos("PRELOAD")` 收集启动预加载资源（另有 `WEBGL_PRELOAD`）；标签在 YooAsset 收集器中配置（Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset，主包当前仅 `BOOTSTRAP`、`fgui` 组预置了标签，示例 DLC 包的 ModelGroup/SceneGroup 另有 models/scenes 标签）
- 资源 location 等于文件名（不含路径和扩展名，DefaultPackage 开启 EnableAddressable + AddressByFileName）；FGUI 例外，按相对路径寻址（FguiAddressByRelativePath）
- **禁止** `Resources.Load()`，所有资源通过 `AssetRaw/` + YooAsset 管理（唯一例外：启动器 UI 由 `Assets/Launcher/Resources/UIWindow/` 的 Prefab 经 LauncherMgr 的 `Resources.Load` 加载，不参与热更）

---

## 常见错误

| 错误 | 原因 | 修复 |
|------|------|------|
| 误认为 GameScripts.Main 程序集存在 | GameEntry.cs 和 Procedure/ 无自定义 asmdef | 它们属于 Assembly-CSharp |
| 热更入口签名写错 | 反射契约要求精确签名 | 必须是 `public static void Entrance(object[])`，objects[0] 强转为 List<Assembly>（见 StartupEntryContract） |
| 流程链顺序记错 | InitPackage 先于 InitResources | ProcedureSplash → ProcedureInitPackage → ProcedureInitResources |
| 误认为 LubanLib 是生成的表代码 | LubanLib 是 Luban 运行时基础库（BeanBase/ByteBuf 等） | 导表生成的代码输出到 GameProto/GameConfig/，数据输出到 AssetRaw/Configs/bytes/ |
| 误认为启动流程只有单阶段更新 | 遗漏两阶段更新分支 | UpdateSetting.EnableTwoStageUpdate 开启后走 ProcedureTwoStageUpdate → GameUpdater.Entry.RunAsync |

---

## 交叉引用

- 模块 API 见 [modules.md](modules.md)
- 热更开发见 [hotfix-workflow.md](hotfix-workflow.md)
- 事件系统见 [event-system.md](event-system.md)
- 资源加载见 [resource-api.md](resource-api.md)
