# 证据台账

行号以本次调查读取到的工作区文件为准。路径均相对于对应项目根目录；OLD 与 CURRENT 不混用。

## 基线与场景

### S01-E001

- 项目：OLD
- 相对路径：ProjectSettings/ProjectVersion.txt
- 符号/字段：m_EditorVersion、m_EditorVersionWithRevision
- 行号：1-2
- 结论：OLD 使用 Unity 2022.3.62f2，revision 为 7670c08855a9。
- 证据性质：配置

### S01-E002

- 项目：CURRENT
- 相对路径：ProjectSettings/ProjectVersion.txt
- 符号/字段：m_EditorVersion、m_EditorVersionWithRevision
- 行号：1-2
- 结论：CURRENT 使用 Unity 2022.3.62f2，revision 为 7670c08855a9，与 OLD 基线一致。
- 证据性质：配置

### S01-E003

- 项目：OLD
- 相对路径：ProjectSettings/EditorBuildSettings.asset
- 符号/字段：m_Scenes
- 行号：7-16
- 结论：首个启用场景是 Assets/Scenes/Updater.unity，随后是 Launch.unity 和 World.unity。
- 证据性质：配置

### S01-E004

- 项目：CURRENT
- 相对路径：ProjectSettings/EditorBuildSettings.asset
- 符号/字段：m_Scenes
- 行号：7-10
- 结论：当前 Build Settings 只列出启用的 Assets/Scenes/main.unity。
- 证据性质：配置

### S01-E005

- 项目：OLD
- 相对路径：Assets/Scenes/Updater.unity
- 符号/字段：Updater GameObject、MonoBehaviour.m_Script、SplashScreenUI
- 行号：303-347
- 结论：Updater 场景包含 Updater GameObject，并绑定 GameLauncher 脚本 GUID 83569bc97ef58f646a57417d5932e44b。
- 证据性质：定义/配置

### S01-E013

- 项目：OLD
- 相对路径：Assets/Scenes/Launch.unity
- 符号/字段：Launch GameObject、GameLaunchProxy 组件
- 行号：130-182
- 结论：Launch 场景包含 GameLaunchProxy 脚本 GUID 391978715469dec4799a3c3d2ec1328f；该代理继承 GameLaunch，因此 Launch 场景会承载 GameLaunch 逻辑。
- 证据性质：定义/配置

## OLD 启动与热更

### S01-E006

- 项目：OLD
- 相对路径：Assets/Scripts/Launcher/GameLauncher.cs
- 符号/方法：GameLauncher.Start
- 行号：40-103
- 结论：AOT 入口执行 InitializeYooAsset；非编辑器执行 AOT metadata 和 Launcher.dll 加载；之后调用 CallHotUpdateLauncher；异常分支执行 FallbackEnterGame。
- 证据性质：调用

### S01-E007

- 项目：OLD
- 相对路径：Assets/Scripts/Launcher/GameLauncher.cs
- 符号/方法：InitializeYooAsset、LoadAOTMetadataAssemblies
- 行号：108-137、148-211
- 结论：YooAssetMgr.Inst.Init 与 YooAssetLauncher.StartLaunchAsync 建立资源启动；LoadAOTMetadataAssemblies 读取 Assets/GameRes/HotUpdate/hotUpdate 的第一行，加载 AOTMetadata 文件并调用 RuntimeApi.LoadMetadataForAOTAssembly。
- 证据性质：调用/配置读取

### S01-E008

- 项目：OLD
- 相对路径：Assets/Scripts/Launcher/GameLauncher.cs
- 符号/方法：CallHotUpdateLauncher、FallbackEnterGame
- 行号：422-484
- 结论：通过 Assembly.Load("Launcher")、反射类型 IGG.Launcher.HotUpdate.HotUpdateLauncher 和静态 Start 方法进入热更层；失败时直接尝试加载 Assets/Scenes/Launch。
- 证据性质：调用

### S01-E009

- 项目：OLD
- 相对路径：Assets/Scripts/HotUpdate/Launcher/HotUpdateLauncher.cs
- 符号/方法：Start、LoadOtherHotUpdateDLLs
- 行号：44-102、471-537
- 结论：HotUpdateLauncher 依次显示启动 UI、下载游戏资源、读取热更清单第二行、排除 Launcher、读取 DLL 字节并 Assembly.Load；dls.game 在装载前经过 DllCryptoUtil.Decrypt。
- 证据性质：调用/配置读取

### S01-E010

- 项目：OLD
- 相对路径：Assets/Scripts/HotUpdate/Launcher/HotUpdateLauncher.cs
- 符号/方法：InitializeFramework、InitializeGame、EnterGame
- 行号：616-715
- 结论：通过反射调用 FrameworkEntry.Initialize 和 GameEntry.Initialize，之后关闭启动 UI 并加载 Assets/Scenes/Launch。
- 证据性质：调用

### S01-E011

- 项目：OLD
- 相对路径：Assets/GameRes/HotUpdate/hotUpdate.txt
- 符号/字段：第一行 AOT 元数据程序集、第二行热更程序集
- 行号：1-2
- 结论：第一行包含 mscorlib.dll、System.dll、System.Core.dll、UnityEngine.CoreModule.dll、dls.framework.dll 等；第二行包含 dls.message、dls.config、dls.ui.base、dls.game、Binding、Framework、Game。
- 证据性质：配置

### S01-E012

- 项目：OLD
- 相对路径：ProjectSettings/HybridCLRSettings.asset；Assets/Plugins/link.xml
- 符号/字段：hotUpdateAssemblies、preserve assembly
- 行号：HybridCLRSettings.asset:15-43；link.xml:15-19
- 结论：HybridCLRSettings 的热更程序集列表比 hotUpdate.txt 第二行更长；link.xml 至少保留 dls.framework、dls.game、dls.config。它们是构建/裁剪相关输入，不等于已经证实的运行时清单。
- 证据性质：配置

### S01-E017

- 项目：OLD
- 相对路径：Assets/Scripts/HotUpdate/Framework/FrameworkEntry.cs；Assets/Scripts/HotUpdate/Game/GameEntry.cs
- 符号/方法：FrameworkEntry.Initialize、GameEntry.Initialize
- 行号：FrameworkEntry.cs:16-53；GameEntry.cs:23-128
- 结论：两个反射入口保留 initialized 标志和日志，但 manager、业务、UI 的实质初始化调用是注释代码；不能单独作为当前真实初始化完成的证明。
- 证据性质：定义/调用

### S01-E027

- 项目：OLD
- 相对路径：Assets/Scripts/HotUpdate/HotUpdateEntry.cs
- 符号/方法：HotUpdateEntry.Initialize、RegisterModules
- 行号：9-40
- 结论：该入口只调用示例性的 RegisterModules；注册内容是注释，且调查范围内未发现调用点。
- 证据性质：定义

### S01-E029

- 项目：OLD
- 相对路径：Assets/Scripts/framework/Framework.asmdef；Assets/Scripts/game/Game.asmdef；Assets/Scripts/HotUpdate/Launcher/Launcher.asmdef；Assets/Scripts/HotUpdate/Framework/Framework.asmdef；Assets/Scripts/HotUpdate/Game/Game.asmdef
- 符号/字段：name、references
- 行号：各 asmdef 的 name/references 字段；本次读取的关键 name 分别为 dls.framework、dls.game、Launcher、Framework、Game
- 结论：OLD 以多个 asmdef 形成 AOT 框架、Launcher 入口和 Framework/Game 热更层；dls.game 是业务程序集，Framework/Game 是由 Launcher 反射进入的热更程序集。
- 证据性质：配置

## OLD 模块、帧驱动与销毁

### S01-E014

- 项目：OLD
- 相对路径：Assets/Scripts/game/Module/Launch/GameLaunch.cs
- 符号/方法：Awake、UpdateHandler、InitLaunchLoader、CompleteHandler、OnDestroy
- 行号：55-155、315-387、439-500
- 结论：进入 Launch 场景后 GameLaunch 先做环境准备并延迟启动 LaunchLoader；LaunchLoader 注册 manager、ModuleMgr 和动态加载；完成后释放 loader、注册低内存/动态下载；销毁时清理 loader、计时器、低内存和网络。
- 证据性质：调用/定义

### S01-E015

- 项目：OLD
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Manager/IManager.cs；Assets/Scripts/framework/Library/ZeroFramework/Manager/MgrCenter.cs
- 符号/方法：IManager、MgrCenter.Register、MgrCore.Add、Update、LateUpdate、OnDispose
- 行号：IManager.cs:14-27；MgrCenter.cs:14-187
- 结论：IManager 是 Initialize/Update/LateUpdate/BeforeDispose/Dispose 契约；MgrCenter.Register 立即触发 manager.Initialize，MonoBehaviour Update/LateUpdate 转发，清理采用反向 BeforeDispose/Dispose。
- 证据性质：定义/调用

### S01-E016

- 项目：OLD
- 相对路径：Assets/Scripts/game/Utils/ModuleProvider.cs；Assets/Scripts/framework/Library/ZeroFramework/Module/IModule.cs；Assets/Scripts/framework/Library/ZeroFramework/Module/ModuleMgr.cs；Assets/Scripts/game/Module/Launch/LaunchLoader.cs
- 符号/方法：ModuleProvider.Get、IModule、ModuleMgr.RegisterAsync/ProcessNewModule/Update、ModuleInitCommander.Run
- 行号：ModuleProvider.cs:14-54；IModule.cs:10-38；ModuleMgr.cs:18-155、270-312、481-511；LaunchLoader.cs:403-439
- 结论：ModuleProvider 限定扫描 dls.game，按优先级和 ID 排序；ModuleMgr 先 Init 后 InitAfter，再在 Update/LateUpdate 驱动；ModuleInitCommander 是从 LaunchLoader 进入这条链的调用点。
- 证据性质：定义/调用

### S01-E018

- 项目：OLD
- 相对路径：Assets/Scripts/game/Module/Launch/GameLaunch_Reload.cs
- 符号/方法：DoRestart、Quit、OnApplicationQuit
- 行号：40-157
- 结论：显式重启会清空 UpdateMgr、UI、MgrCenter、Singleton、场景对象和资源，再加载 Launch；Player Quit 调用 Application.Quit，退出时保存聊天和调试日志。
- 证据性质：调用

## CURRENT 启动与模块

### S01-E019

- 项目：CURRENT
- 相对路径：Assets/Scenes/main.unity；Assets/TEngine/Settings/Prefab/GameEntry.prefab；Assets/GameScripts/GameEntry.cs
- 符号/字段/方法：PrefabInstance.m_SourcePrefab、GameEntry prefab、GameEntry.Awake
- 行号：main.unity:266-322；GameEntry.prefab:161-351；GameEntry.cs:4-14
- 结论：main 场景实例化 GameEntry prefab；prefab 根对象包含 GameEntry、RootModule、ResourceDriver 子对象和 Settings；Awake 预取四个接口模块、启动 ProcedureSetting 并 DontDestroyOnLoad。
- 证据性质：定义/配置/调用

### S01-E020

- 项目：CURRENT
- 相对路径：Assets/TEngine/Runtime/Core/ModuleSystem.cs；Assets/TEngine/Runtime/Core/Module.cs
- 符号/方法：GetModule<T>、CreateModule、RegisterUpdate、Update、Shutdown；Module.Priority/OnInit/Shutdown
- 行号：ModuleSystem.cs:9-207；Module.cs:8-38
- 结论：ModuleSystem 按接口命名约定懒加载实现，创建后注册并立即 OnInit；IUpdateModule 进入按 Priority 排序的更新队列；Shutdown 反向关闭并清理全局容器。
- 证据性质：定义/调用

### S01-E021

- 项目：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/RootModule.cs
- 符号/方法：Awake、Update、OnApplicationQuit、OnDestroy、Shutdown
- 行号：113-167、209-212、287-301
- 结论：RootModule 初始化日志/JSON/帧时间和低内存回调，Update 驱动 ModuleSystem；Player 构建的 OnDestroy 调用 ModuleSystem.Shutdown，编辑器分支跳过；OnApplicationQuit 取消低内存事件并停止协程。
- 证据性质：调用/定义

### S01-E022

- 项目：CURRENT
- 相对路径：Assets/TEngine/Settings/UpdateSetting.asset
- 符号/字段：HotUpdateAssemblies、AOTMetaAssemblies、LogicMainDllName、AssemblyTextAssetPath
- 行号：15-36
- 结论：热更程序集为 GameProto.dll、GameLogic.dll；主逻辑为 GameLogic.dll；AOT 元数据和 AssetRaw/DLL/*.bytes 地址规则由同一配置声明。
- 证据性质：配置

### S01-E023

- 项目：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureSetting.cs；Assets/GameScripts/Procedure/ProcedureBase.cs；Assets/TEngine/Settings/ProcedureSetting.asset
- 符号/方法/字段：StartProcedure、ProcedureBase._resourceModule、availableProcedureTypeNames、entranceProcedureTypeName
- 行号：ProcedureSetting.cs:55-102；ProcedureBase.cs:5-14；ProcedureSetting.asset:15-27
- 结论：流程配置把 ProcedureLaunch 作为入口；StartProcedure 懒取 IProcedureModule、反射创建流程、初始化后 Yield 一次再启动；游戏层 ProcedureBase 在构造时缓存 IResourceModule。
- 证据性质：定义/配置/调用

### S01-E024

- 项目：CURRENT
- 相对路径：Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs；Assets/GameScripts/Procedure/ProcedureStartGame.cs
- 符号/方法：LoadAssembly、AllAssemblyLoadComplete、LoadAssetSuccess、LoadMetadataForAOTAssembly、StartGame
- 行号：ProcedureLoadAssembly.cs:19-292；ProcedureStartGame.cs:8-22
- 结论：流程加载 TextAsset 字节、Assembly.Load、AOT metadata，并反射寻找 GameApp.Entrance；完成函数先 ChangeState<ProcedureStartGame>，再校验主逻辑程序集、类型和入口；StartGame 下一帧隐藏 Launcher UI。
- 证据性质：调用

### S01-E025

- 项目：CURRENT
- 相对路径：Assets/GameScripts/HotFix/GameLogic/GameApp.cs；Assets/GameScripts/HotFix/GameLogic/GameModule.cs；Assets/TEngine/Runtime/Core/Utility/Utility.Unity.cs；Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs
- 符号/方法：GameApp.Entrance、GameApp.Release、GameModule.Get/Shutdown、Utility.Unity.AddDestroyListener、MainBehaviour.OnDestroy/Release
- 行号：GameApp.cs:17-46；GameModule.cs:5-117；Utility.Unity.cs:252-272；UpdateDriver.cs:24-34、288-341、389-452
- 结论：热更入口注册销毁回调并显示 BattleMainUI；回调最终进入 SingletonSystem.Release；GameModule 只是缓存 facade，Shutdown 清空引用但当前没有证明被自动调用；UpdateDriver 的独立对象负责承载 DestroyEvent。UpdateDriver.Shutdown 会先调用 MainBehaviour.Release 清空 DestroyEvent，再销毁实体，因此若 RootModule 先触发 ModuleSystem.Shutdown，GameApp.Release 不会由该路径调用。
- 证据性质：定义/调用

### S01-E026

- 项目：CURRENT
- 相对路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs；Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs
- 符号/方法：ResourceModule.OnInit/Shutdown/Initialize；ResourceModuleDriver.Start
- 行号：ResourceModule.cs:17-53、119-138；ResourceModuleDriver.cs:236-271
- 结论：ResourceModule 的 OnInit/Shutdown 为空；ResourceModuleDriver.Start 先取得 IResourceModule、写入包名/运行模式/下载配置，再调用 Initialize 完成 YooAssets.Initialize、默认包创建和对象池绑定。
- 证据性质：定义/调用

### S01-E028

- 项目：CURRENT
- 相对路径：Assets/TEngine/Runtime/TEngine.Runtime.asmdef；Assets/Launcher/Launcher.asmdef；Assets/GameScripts/HotFix/GameProto/GameProto.asmdef；Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef
- 符号/字段：name、references
- 行号：各 asmdef 的 name/references 字段
- 结论：CURRENT 的主要装配边界是 TEngine.Runtime、Launcher、GameProto、GameLogic；GameLogic 和 GameProto 显式引用 TEngine.Runtime，GameLogic 另引用 GameProto。GameEntry/Procedure 属于未单独拆出 asmdef 的 Unity 脚本侧编排。
- 证据性质：配置

### S01-E030

- 项目：CURRENT
- 相对路径：Assets/GameScripts/HotFix/GameLogic/GameModule.cs；Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs；Assets/GameScripts/HotFix/GameLogic/SingletonSystem/Singleton.cs；Assets/GameScripts/HotFix/GameLogic/SingletonSystem/SingletonSystem.cs
- 符号/方法：GameModule.UI、UIModule、Singleton<T>.Instance、SingletonSystem.Retain/BuildLifeCycle
- 行号：GameModule.cs:60-65；UIModule.cs:12-16；Singleton.cs:13-22；SingletonSystem.cs:60-65、98-105
- 结论：UIModule 不属于 TEngine.ModuleSystem；GameModule.UI 通过 Singleton<UIModule>.Instance 创建对象，调用 OnInit 后由 SingletonSystem.Retain 注册，并因实现 IUpdate 进入 SingletonSystem 的更新列表。
- 证据性质：定义/调用

### S01-E031

- 项目：OLD
- 相对路径：Assets/Resources/framework_cfg.asset
- 符号/字段：LaunchSceneName、UseYooAsset
- 行号：36-52
- 结论：仓库中可见配置把 LaunchSceneName 设为 Launch，并把 UseYooAsset 设为 0；这只能证明静态资产值，不能单独证明最终发布分支。
- 证据性质：配置

### S01-E032

- 项目：OLD
- 相对路径：ILScriptProject/Main.cs
- 符号/方法：ILScript.Main.Init
- 行号：7-17
- 结论：ILScriptProject 中存在独立的 ILScript.Main.Init 入口，当前源码调查未将它连接到 Updater、Launch 或 GameLaunch 主链。
- 证据性质：定义
