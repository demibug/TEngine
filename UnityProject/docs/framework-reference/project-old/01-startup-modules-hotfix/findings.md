# 调查结论

## 1. 启动入口与可达主链

### 1.1 OLD：Updater 先启动 AOT，再把控制权交给热更 Launcher

**[已确认]** OLD 的 Build Settings 将 Updater 放在首场景，Updater 场景中的 Updater GameObject 挂载 GameLauncher 脚本。GameLauncher.Start 依次执行 YooAsset 初始化、非编辑器下的 AOT 元数据加载、Launcher.dll 加载，然后反射调用 HotUpdateLauncher.Start；异常时尝试直接加载 Launch 场景。[S01-E003] [S01-E005] [S01-E006] [S01-E007] [S01-E008]

OLD 这段边界的实际职责可以分成三层：

1. AOT/Unity 层：GameLauncher、YooAssetMgr、YooAssetLauncher。
2. Launcher 热更程序集：通过 Assembly.Load("Launcher") 找到 HotUpdateLauncher。
3. 其他热更程序集：HotUpdateLauncher 读取热更清单第二行，逐一读取 DLL 字节并 Assembly.Load，随后反射调用 FrameworkEntry.Initialize、GameEntry.Initialize，并加载 Launch 场景。[S01-E009] [S01-E010] [S01-E011]

**[已确认]** OLD 运行时实际读取的热更清单由代码路径和 hotUpdate.txt 共同证明：第一行用于 AOT 元数据程序集，第二行用于热更程序集；HotUpdateLauncher 会排除已经由 AOT 入口加载的 Launcher。[S01-E007] [S01-E009] [S01-E011]

### 1.2 OLD：真正的 manager/module 初始化在 Launch 场景

**[已确认]** HotUpdateLauncher.EnterGame 加载 Assets/Scenes/Launch。Launch 场景的同一 GameObject 上存在 GameLaunchProxy，代理继承 GameLaunch，因此 GameLaunch.Awake 会在进入 Launch 场景后执行。[S01-E010] [S01-E013]

GameLaunch.Awake 先做环境、日志、分辨率和预加载准备，并将 UpdateHandler 放入 Timers。延迟若干帧后 InitLaunchLoader 创建 LaunchLoader，按顺序注册资源/输入/场景/网络/资源/补丁/地图/面板/模块/音频/相机等 manager，最后加入 ModuleInitCommander 并启动 loader。[S01-E014]

**[已确认]** LaunchLoader 的 manager commander 通过 MgrCenter.Register 触发 manager.Initialize；ModuleInitCommander 通过 ModuleProvider.Get 扫描 dls.game、调用 ModuleMgr.RegisterAsync，并分步执行模块初始化。[S01-E015] [S01-E016]

因此，OLD 的一条实际可达链是：

Updater 场景 → GameLauncher.Start → HotUpdateLauncher.Start → Launch 场景 → GameLaunch.Awake → LaunchLoader → MgrCenter manager → ModuleMgr → dls.game IModule。

### 1.3 CURRENT：main 场景的 GameEntry 是启动触发器

**[已确认]** CURRENT 的 Build Settings 只有 Assets/Scenes/main.unity；该场景包含源 prefab GUID 为 e38c712e7904fdf42bd0458b769718dd 的 GameEntry 实例。GameEntry prefab 的根对象名为 GameEntry，挂载 GameEntry、RootModule，并包含 ResourceDriver 与 Settings 子对象。[S01-E004] [S01-E019]

GameEntry.Awake 的直接动作是：

1. 获取 IUpdateDriver、IResourceModule、IDebuggerModule、IFsmModule。
2. 调用 Settings.ProcedureSetting.StartProcedure().Forget()。
3. 对 GameEntry 对象执行 DontDestroyOnLoad。[S01-E019]

**[已确认]** ProcedureSetting.StartProcedure 首次取得 IProcedureModule，按配置中的 availableProcedureTypeNames 反射创建全部流程对象，找到 ProcedureLaunch 作为入口，初始化流程模块，Yield 一次后启动入口流程。[S01-E023]

CURRENT 的可达主链是：

main 场景 → GameEntry.Awake → ModuleSystem 懒加载框架模块 → ProcedureLaunch → ProcedureSplash → ProcedureInitPackage → ProcedureInitResources → ProcedurePreload → ProcedureLoadAssembly → ProcedureStartGame/GameApp.Entrance。

流程状态迁移由各 Procedure 的 OnUpdate/异步方法驱动，具体资源下载、缓存清理、预加载和程序集加载均在同一套流程配置中。[S01-E023] [S01-E024]

### 1.4 CURRENT：从主逻辑 DLL 到可使用游戏逻辑

**[已确认]** UpdateSetting 将 GameProto.dll、GameLogic.dll 声明为 HotUpdateAssemblies，将 GameLogic.dll 声明为 LogicMainDllName，并声明 AOTMetaAssemblies 与 AssetRaw/DLL/*.bytes 的地址规则。[S01-E022]

ProcedureLoadAssembly 在非编辑器的启用路径中：

1. 逐一从资源模块加载热更程序集 TextAsset。
2. 对每个 TextAsset 调用 Assembly.Load(textAsset.bytes)。
3. 按 LogicMainDllName 保存主逻辑程序集，同时收集热更程序集列表。
4. AOT 元数据加载完成后查找 GameApp 类型和 Entrance 方法。
5. 以嵌套 object 数组传入热更程序集列表并 Invoke Entrance。[S01-E024]

GameApp.Entrance 初始化 GameEventHelper，保存程序集列表，注册 UpdateDriver 的 Destroy listener，随后调用 StartGameLogic；当前 StartGameLogic 的实际动作是通过 GameModule.UI 显示 BattleMainUI。GameModule 的模块属性再通过 ModuleSystem.GetModule<T> 或 UIModule.Instance 取得框架服务。[S01-E025]

## 2. 模块组织与初始化语义

### 2.1 OLD：Manager 中心和业务 Module 中心是两级结构

**[已确认]** OLD 的 IManager 契约包括 Initialize、Update、LateUpdate、BeforeDispose；MgrCenter 持有 MgrCore，Register 时把 manager 加入列表并立即调用 Initialize，Unity Update/LateUpdate 再转发给所有 manager。销毁时先反向 BeforeDispose，再反向 Dispose。[S01-E015]

**[已确认]** OLD 的 IModule 契约包括 Id、Priority、Init、InitAfter、Update、LateUpdate。ModuleProvider 只扫描已经加载的 dls.game 程序集，过滤抽象类和 Disable 类型，实例化后按 Priority 降序、Id 降序排序。ModuleMgr.RegisterAsync 收集模块；ProcessNewModule 先全部 Init，再全部 InitAfter；Update/LateUpdate 负责持续驱动。[S01-E016]

这意味着 OLD 的主要模块边界是：

- MgrCenter：系统级 manager 的注册、生命周期和帧转发。
- ModuleMgr：业务热更模块的注册、排序、两阶段初始化和帧转发。
- dls.game：ModuleProvider 明确规定的业务模块程序集。

### 2.2 CURRENT：ModuleSystem 以接口命名约定做懒加载

**[已确认]** CURRENT 的 ModuleSystem 保存 interface-to-module 映射、按优先级排列的全部模块和更新模块。GetModule<T> 要求 T 是接口，然后按接口命名约定拼出 Namespace + 去掉首字母 I 的实现类型 + 接口程序集名，使用 Type.GetType 查找；不存在时 Activator.CreateInstance，加入队列并立即调用 OnInit。[S01-E020]

实现 IUpdateModule 的模块会被放入更新队列；ModuleSystem.Update 在脏标志存在时重建队列，再按已排序顺序调用 Update。优先级数值越大越先更新；Shutdown 则从模块链表尾部反向调用 Shutdown，并清理映射、更新队列、内存池和缓存句柄。[S01-E020]

CURRENT 不是由一个显式的“注册所有模块”方法完成初始化。GameEntry.Awake 的四次 GetModule 会提前创建 IUpdateDriver、IResourceModule、IDebuggerModule、IFsmModule；后续 Procedure、Audio、Scene、Timer、Localization 等 TEngine Module 在第一次访问时再创建。UI 例外：GameModule.UI 通过 UIModule.Instance 创建 GameLogic 的 Singleton<UIModule>，再由 SingletonSystem 管理其生命周期和 IUpdate 回调。[S01-E019] [S01-E020] [S01-E025] [S01-E030]

### 2.3 资源模块有“对象存在”和“YooAsset 已初始化”两个阶段

**[已确认]** ResourceModule 的 OnInit 和 Shutdown 为空；真正的 YooAssets.Initialize、默认包创建和对象池模块绑定发生在 ResourceModuleDriver.Start 调用 IResourceModule.Initialize 之后。ResourceDriver 是 GameEntry prefab 的子对象。[S01-E019] [S01-E026]

**[推断]** GameEntry.Awake 启动的 StartProcedure 会先同步构造流程对象并保存 IResourceModule 引用，但在 ProcedureSetting.StartProcedure 的一次 UniTask.Yield 之后才真正进入入口流程；这给 Unity 的其他 Start 生命周期函数留下了初始化 ResourceModuleDriver 的机会。该先后依赖 Unity/UniTask 的实际调度，尚未在运行时验证。[S01-C001] [S01-E023] [S01-E026]

## 3. 帧驱动、常驻对象与销毁

### 3.1 OLD 帧驱动

**[已确认]** MgrCenter 是 Launch 场景中的 MonoBehaviour，Update 和 LateUpdate 调用 MgrCore；MgrCore 将调用转发给 IManager。ModuleMgr 作为一个 IManager 被注册后，其 Update 处理待注册模块和模块 Update，其 LateUpdate 处理通知和模块 LateUpdate。[S01-E015] [S01-E016]

GameLaunch 使用 Timers 作为启动延迟和部分调度入口，并在 CompleteHandler 中释放 LaunchLoader、注册低内存回调和开始动态下载；GameLaunch.OnDestroy 会释放 loader、移除计时器、取消低内存回调并关闭网络。[S01-E014]

### 3.2 CURRENT 帧驱动

**[已确认]** RootModule.Awake 初始化文本、日志、JSON、帧率和低内存回调；RootModule.Update 调用 GameTime.StartFrame 后转发 ModuleSystem.Update；FixedUpdate/LateUpdate 也刷新 GameTime。[S01-E021]

UpdateDriver 自己创建名为 [UpdateDriver] 的 DontDestroyOnLoad GameObject，并由 MainBehaviour 暴露 Update、FixedUpdate、LateUpdate、Destroy 等事件。GameApp.Entrance 使用的 Utility.Unity.AddDestroyListener 最终把 Release 注册到这个 DestroyEvent。[S01-E023] [S01-E025]

### 3.3 退出与重启语义

**[已确认]** OLD 的显式 Restart 会设置 disposing，调用 AppEventHelper.RestartGame，清空 UpdateMgr，关闭 UI，调用 MgrCenter.DisposeAll 与 SingletonMgr.Dispose，销毁场景对象，卸载 YooAsset/AssetBundle 资源，GC 后重新加载 Launch 场景；Quit 在 Player 分支调用 Application.Quit，OnApplicationQuit 保存聊天和日志状态。[S01-E018]

**[已确认]** CURRENT 的 Player 分支 RootModule.OnDestroy 调用 ModuleSystem.Shutdown；RootModule.OnApplicationQuit 取消低内存事件并停止协程。ModuleSystem.Shutdown 是框架模块的统一清理入口，且按模块注册链逆序调用 Shutdown。[S01-E021] [S01-E020]

**[已确认]** CURRENT 的 GameApp.Release 仅调用 SingletonSystem.Release 并写日志；它没有调用 GameModule.Shutdown 或 ModuleSystem.Shutdown。GameModule.Shutdown 只清空缓存引用，当前未找到它的调用点。[S01-E025]

**[已确认 + 未验证边界]** CURRENT 的 RootModule 和 [UpdateDriver] 是两个独立的 DontDestroyOnLoad 对象。RootModule.OnDestroy 若先调用 ModuleSystem.Shutdown，UpdateDriver.Shutdown 会先执行 MainBehaviour.Release 清空 DestroyEvent，再销毁 [UpdateDriver]，因此 GameApp.Release 不会由该路径触发；只有 [UpdateDriver] 独立对象先被 Unity 销毁时，Destroy listener 才可能执行。两个对象在真实退出/销毁时哪个 OnDestroy 先发生，以及回调是否重复，仍未被运行时验证。[S01-E021] [S01-E025] [S01-C004]

**[已确认]** CURRENT 在 UNITY_EDITOR 编译分支跳过 RootModule.OnDestroy 中的 ModuleSystem.Shutdown。这使编辑器停止运行与 Player 退出的清理语义不同。[S01-E021]

## 4. 程序集与热更边界

### 4.1 OLD

**[已确认]** OLD 的 asmdef 和运行时清单显示 AOT/基础程序集包括 dls.framework、dls.framework.common、Unity/第三方依赖；运行时热更清单第二行包括 dls.message、dls.config、dls.ui.base、dls.game、Binding、Framework、Game。Launcher 是进入热更层的独立程序集；HotUpdateLauncher 再按清单加载其余 DLL。[S01-E011] [S01-E012] [S01-E029]

**[已确认]** dls.game 是业务 ModuleProvider 的固定扫描目标；HotUpdateLauncher 对 dls.game 有额外解密后再 Assembly.Load 的特殊分支。[S01-E009] [S01-E016]

**[未验证]** HybridCLRSettings.asset 列出的 hotUpdateAssemblies 比 hotUpdate.txt 第二行更长，包含 dls.game.core、dls.game.notifys、dls.game.leafmodules、HotUpdate、IGC.Game 等名称。两份配置的生成/消费关系必须由构建脚本或实际产物确认，当前不能据此断言最终 Player 的 DLL 集合。[S01-E011] [S01-E012] [S01-C005]

### 4.2 CURRENT

**[已确认]** CURRENT 的主要 asmdef 是 TEngine.Runtime、Launcher、GameProto、GameLogic；GameLogic 显式依赖 TEngine.Runtime 和 GameProto，GameProto 也依赖 TEngine.Runtime。Assembly-CSharp 中的 GameEntry 和 Procedure 负责 Unity 场景启动与流程编排，UpdateSetting 则把 GameProto/GameLogic 标为运行时热更 DLL。[S01-E022] [S01-E028]

CURRENT 的边界可表示为：

- Unity 场景/AOT 编排：GameEntry、Settings、Procedure、ResourceModuleDriver、Launcher。
- 框架运行时：TEngine.Runtime 的 ModuleSystem、RootModule、各 Module。
- 热更代码：GameProto.dll、GameLogic.dll。
- 桥接输入：ProcedureLoadAssembly 传给 GameApp.Entrance 的程序集列表。

**[已确认]** CURRENT 的程序集装载入口是资源流程，不是场景中直接挂载一个热更入口组件；GameApp 是 DLL 载入后的反射目标。[S01-E022] [S01-E024] [S01-E025]

## 5. 旧入口中“定义存在但当前未形成主链”的部分

**[已确认]** OLD 的 HotUpdateEntry.Initialize 只包含示例性的 RegisterModules 注释，且在 Assets 范围内没有找到它的调用点；它不能作为已确认启动主链。[S01-E027]

**[已确认]** OLD 的 FrameworkEntry.Initialize 和 GameEntry.Initialize 的反射调用点存在，但实质性的 InitializeManagers、InitializeGameModules、InitializeBusinessSystems、InitializeUISystems 调用被注释；FrameworkEntry 的辅助方法更像验证/迁移中的保留实现。真正可达的 manager/module 初始化仍由 GameLaunch/LaunchLoader 负责。[S01-E010] [S01-E017] [S01-E014] [S01-C006]

**[未验证]** OLD 的 ILScriptProject/Main.cs、HotUpdateEntry 和部分配置字段可能属于旧方案、工具方案或迁移中方案；本次只确认了定义存在，没有确认任何 Player 入口会调用它们。[S01-E027] [S01-E032] [S01-C008]

## 6. 对 CURRENT 迁移阅读的直接含义

1. 不能把 OLD 的 MgrCenter/ModuleMgr 直接等同于 CURRENT 的 ModuleSystem：前者是显式注册、场景组件帧转发、manager 与业务 module 两级；后者是接口命名约定、按需创建、RootModule 统一驱动。[S01-E015] [S01-E016] [S01-E020] [S01-E021]
2. 不能把 OLD 的 FrameworkEntry/GameEntry 代码体直接当作 OLD 当前业务启动事实；应以 GameLaunch 的 LaunchLoader 和 ModuleProvider 链为准。[S01-E014] [S01-E017]
3. CURRENT 的热更“是否可用”至少取决于三件事：UpdateSetting 的 DLL 名称、资源包中对应 TextAsset 是否存在、AOT 元数据与 Player 构建是否匹配；源码只证明了读取和调用逻辑，未证明产物闭环。[S01-E022] [S01-E024] [S01-C003] [S01-C005]
4. CURRENT 的退出契约需要补充统一入口或运行时测试才能确认：GameApp.Release、UpdateDriver.DestroyEvent、RootModule.OnDestroy、ModuleSystem.Shutdown 目前是多条清理路径。[S01-E021] [S01-E023] [S01-E025] [S01-C004]
