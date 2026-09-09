# 退出协调验证记录

日期：2026-09-09

本记录对应 `shutdown-coordination-plan.md`，记录退出协调实现、静态编译结果，以及真实 Unity 验证的实际限制。

## 结论

统一关闭入口已落地到 `ModuleSystem.Shutdown()`。`RootModule.OnDestroy`、`RootModule.OnApplicationQuit`、显式调用，以及资源/热更清理都进入同一状态机；关闭状态为 `Running -> ShuttingDown -> Stopped`，重复和重入调用直接返回。

本轮复核修复了两个关闭竞态：`UpdateDriver` 的 Update/FixedUpdate/LateUpdate 现在逐项调用监听并在每项前检查会话状态；`ModuleSystem` 的字典命中、具体类型命中和列表回退查询都排除已进入 `_shutdownModules` 的模块，同时保留清理期间访问尚未关闭依赖的能力。资源 PlayMode 用例新增了 `BeforeShutdown` 阶段的池/计数观察点、实例 spawn 归还计数和 handle Dispose 计数，并改为由实际 Root GameObject 延迟销毁触发关闭。

本轮仅修正测试执行方式：三个 UpdateDriver 短路用例迁入 PlayMode，等待两个 listener 通过 UniTask 实际挂载并等待宿主延迟 `Destroy`；它们标记为显式、每个必须独立运行，因为首监听会结束整个框架会话。双会话用例也标记为显式：第一次 Play 创建真实 Root、模块缓存和关闭期间的受控晚订阅，第二次 Play 必须看到静态计数严格为 2，并验证事件、模块/Root 缓存和晚回调均未跨会话污染。

冻结顺序为：

1. 停止更新、启动流程、新模块/新 UI/新资源请求，并取消资源生命周期等待者。
2. 关闭 Procedure/FSM。
3. 分发 `RootModule.BeforeShutdown`，由 `GameApp`/`SingletonSystem` 关闭热更单例、UGUI、FGUI 和其订阅/任务。
4. 关闭其他资源使用模块（音频、计时器、本地化、场景等）。
5. 对象池仍有效时，由 `ResourceModule` 主动释放其 `AssetsReference` owner 持有的源 prefab/资产 spawn；覆盖活动实例、未激活实例和延迟 `Destroy`。
6. 关闭对象池；对象池 shutdown 分支释放 `AssetObject` 持有的 YooAsset handle，并清空池索引。
7. 完成资源模块收尾，按所有权仅在框架负责时调用 `YooAssets.Destroy()`，清理 package/bootstrap/loading 状态。
8. 清空 GameEvent、模块表、单例缓存和 MemoryPool，进入 `Stopped`。

所有逐项清理均先摘除登记，再独立捕获异常；后续项继续执行。关闭期间的入口不创建模块、不创建 UI、不注册资源引用；异步请求用生命周期 token、代际和 owner 检查，晚回调只能归还已有资源，不能向旧会话或新会话写入状态。

## 所有权契约

- `ModuleSystem` 是唯一关闭协调器；`RootModule` 只负责会话所有权和生命周期转发。过期 Root 的延迟 `OnDestroy` 不得关闭新会话。
- `ResourceModule` 是资源池和 YooAsset handle 的 owner。`AssetsReference` 在绑定时保存 owner，并在资源池关闭前由 owner 扫描释放；`OnDestroy` 只做一次性补偿，晚到时 no-op。
- `ObjectPoolModule` 在资源引用归还后关闭；其 `Object<T>` wrapper 与 `ObjectBase` 的 `Release(bool)` 都有 shutdown 释放路径，不能因 `isShutdown` 跳过 handle。
- `SingletonSystem` 在 `BeforeShutdown` 中先摘除静态表、监听和对象登记，再逐项释放 singleton/`GameObject`；未经过 `GameApp` 的 singleton 也会自动挂接 `RootModule.BeforeShutdown`。
- `UpdateDriver`、预加载、Procedure/FSM、场景、音频、FGUI package/external loader 和资源扩展在停止后拒绝新工作，旧任务完成时检查 token/代际/全局状态。
- 新会话通过 `SubsystemRegistration` 显式清理框架静态表、单例静态引用、事件、资源引用索引和 MemoryPool；不支持业务热重启。

## 本批涉及的实际代码文件

以下是本批实现/收口所涉及的代码文件；工作树中的前批变更保持原状，未暂存、未提交、未恢复、未清理：

- 核心/事件/驱动：
  - `Assets/TEngine/Runtime/Core/ModuleSystem.cs`
  - `Assets/TEngine/Runtime/Core/GameEvent/EventDispatcher.cs`
  - `Assets/TEngine/Runtime/Core/GameEvent/EventMgr.cs`
  - `Assets/TEngine/Runtime/Core/Utility/Utility.Unity.cs`
  - `Assets/TEngine/Runtime/Module/RootModule.cs`
  - `Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs`
- Procedure/FSM/场景：
  - `Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureModule.cs`
  - `Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureBase.cs`
  - `Assets/TEngine/Runtime/Module/ProcedureModule/ProcedureSetting.cs`
  - `Assets/TEngine/Runtime/Module/FsmModule/FsmModule.cs`
  - `Assets/TEngine/Runtime/Module/FsmModule/Fsm.cs`
  - `Assets/GameScripts/Procedure/ProcedureBase.cs`
  - `Assets/GameScripts/Procedure/ProcedureClearCache.cs`
  - `Assets/GameScripts/Procedure/ProcedureCreateDownloader.cs`
  - `Assets/GameScripts/Procedure/ProcedureDownloadFile.cs`
  - `Assets/GameScripts/Procedure/ProcedureInitPackage.cs`
  - `Assets/GameScripts/Procedure/ProcedureInitResources.cs`
  - `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`
  - `Assets/GameScripts/Procedure/ProcedurePreload.cs`
  - `Assets/GameScripts/Procedure/ProcedureStartGame.cs`
  - `Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs`
- 资源/对象池：
  - `Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.AssetObject.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/PreloadRequestRunner.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs`
  - `Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsSetHelper.cs`
  - `Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.cs`
  - `Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.ObjectPool.cs`
  - `Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.Object.cs`
- 其他运行时模块：
  - `Assets/TEngine/Runtime/Module/AudioModule/AudioModule.cs`
  - `Assets/TEngine/Runtime/Module/AudioModule/AudioAgent.cs`
  - `Assets/TEngine/Runtime/Module/AudioModule/AudioData.cs`
  - `Assets/TEngine/Runtime/Module/AudioModule/AudioCategory.cs`
  - `Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs`
  - `Assets/TEngine/Runtime/Module/LocalizationModule/LocalizationModule.cs`
  - `Assets/TEngine/Runtime/Module/LocalizationModule/LocalizationManager.cs`
- 热更/UI/FGUI：
  - `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
  - `Assets/GameScripts/HotFix/GameLogic/GameModule.cs`
  - `Assets/GameScripts/HotFix/GameLogic/SingletonSystem/SingletonSystem.cs`
  - `Assets/GameScripts/HotFix/GameLogic/SingletonSystem/Singleton.cs`
  - `Assets/GameScripts/HotFix/GameLogic/SingletonSystem/SingletonBehaviour.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiExternalLoader.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiLifetimeScope.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiModule.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiPackageService.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiResourceProvider.cs`
  - `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiWindow.cs`
  - `Assets/TEngine/Extensions/FairyGUI/Runtime/FguiRuntimeHost.cs`
  - `Assets/TEngine/Runtime/AssemblyInfo.cs`
- 测试：
- `Assets/Tests/ShutdownLifecycle/EditMode/ShutdownLifecycleTests.cs`（保留纯 C# 关闭状态/异常/模块查询测试；不再创建 UpdateDriver）
- `Assets/Tests/ShutdownLifecycle/PlayMode/ShutdownLifecyclePlayModeTests.cs`
  - `Assets/Tests/ShutdownLifecycle/EditMode/TEngine.ShutdownLifecycle.Tests.asmdef`
  - `Assets/Tests/ShutdownLifecycle/PlayMode/TEngine.ShutdownLifecycle.PlayModeTests.asmdef`

未修改第三方 YooAsset、UniTask、HybridCLR 源码，未改正常对象池算法或业务内容。

## 已执行验证

### 串行 .NET 编译

以下命令均以 `--no-restore --nologo` 串行执行并成功：

- `dotnet build TEngine.Runtime.csproj --no-restore --nologo`：0 警告，0 错误。
- `dotnet build GameLogic.csproj --no-restore --nologo`：0 警告，0 错误（早先一次增量检查曾显示 `UIBase.cs(24,37)` 的原有 CS8632 警告，最终串行全量结果已无警告）。
- `dotnet build TEngine.GameEvent.Tests.csproj --no-restore --nologo`：0 警告，0 错误。
- `dotnet build TEngine.ResourceLifecycle.Tests.csproj --no-restore --nologo`：0 警告，0 错误。
- `dotnet build TEngine.ResourceLifecycle.PlayModeTests.csproj --no-restore --nologo`：0 警告，0 错误。
- `dotnet build GameLogic.UILifecycle.PlayModeTests.csproj --no-restore --nologo`：0 警告，0 错误。
- `dotnet build GameLogic.FairyGUI.Tests.csproj --no-restore --nologo`：0 警告，0 错误。
- `dotnet build GameLogic.FairyGUI.PlayModeTests.csproj --no-restore --nologo`：0 警告，0 错误。

新建的 ShutdownLifecycle asmdef 尚未生成独立 `.csproj`；其最终编译应由 Unity Test Runner 执行。

本轮修复后，使用 Unity 2022.3 Roslyn、以两个 asmdef 声明的程序集名分别编译测试源码：

- `ShutdownLifecycle/EditMode/ShutdownLifecycleTests.cs`：成功。
- `ShutdownLifecycle/PlayMode/ShutdownLifecyclePlayModeTests.cs`：成功。

该编译只验证 C#/InternalsVisibleTo 兼容性，不等同于 Unity Test Runner 的生命周期执行。

### 静态检查

`git diff --check` 已执行，无实际空白错误；Git 只报告工作树已有的 LF/CRLF 转换提示。未暂存或提交任何变更。

### Unity 实际测试尝试

使用项目声明的 Unity 版本 `2022.3.62f2` 尝试运行本轮修复后的 EditMode、PlayMode 过滤测试。此前 master 尝试的日志为 `Temp/ShutdownLifecycle.Master.EditMode.log` 和 `Temp/ShutdownLifecycle.Master.PlayMode.log`，上轮复核日志为 `Temp/ShutdownLifecycle.Master2.EditMode.log` 和 `Temp/ShutdownLifecycle.Master2.PlayMode.log`，本轮日志为 `Temp/ShutdownLifecycle.TestFix.EditMode.log`、`Temp/ShutdownLifecycle.TestFix.PlayMode.log`、`Temp/ShutdownLifecycle.TestFix.DomainReloadOff.log` 以及三个独立 UpdateDriver 用例日志：`Temp/ShutdownLifecycle.TestFix.Update.log`、`Temp/ShutdownLifecycle.TestFix.FixedUpdate.log`、`Temp/ShutdownLifecycle.TestFix.LateUpdate.log`。

```text
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform editmode -testFilter "TEngine.ShutdownLifecycleTests.ModuleSystemShutdownTests" -testResults "Temp\ShutdownLifecycle.EditMode.xml" -logFile "Temp\ShutdownLifecycle.EditMode.log"
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform editmode -testFilter "TEngine.ShutdownLifecycleTests" -testResults "Temp\ShutdownLifecycle.Fix.EditMode.xml" -logFile "Temp\ShutdownLifecycle.Fix.EditMode.log"
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform playmode -testFilter "TEngine.ShutdownLifecycleTests.ShutdownLifecyclePlayModeTests" -testResults "Temp\ShutdownLifecycle.Fix.PlayMode.xml" -logFile "Temp\ShutdownLifecycle.Fix.PlayMode.log"
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform editmode -testFilter "TEngine.ShutdownLifecycleTests" -testResults "Temp\ShutdownLifecycle.TestFix.EditMode.xml" -logFile "Temp\ShutdownLifecycle.TestFix.EditMode.log"
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform playmode -testFilter "TEngine.ShutdownLifecycleTests.ShutdownLifecyclePlayModeTests.Shutdown_ReturnsActiveAndPendingDestroyedInstancesBeforeDelayedDestroy" -testResults "Temp\ShutdownLifecycle.TestFix.PlayMode.xml" -logFile "Temp\ShutdownLifecycle.TestFix.PlayMode.log"
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform playmode -testFilter "TEngine.ShutdownLifecycleTests.ShutdownLifecyclePlayModeTests.UpdateDriver_UpdateListenerShutdownStopsRemainingListeners" -testResults "Temp\ShutdownLifecycle.TestFix.Update.xml" -logFile "Temp\ShutdownLifecycle.TestFix.Update.log"
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform playmode -testFilter "TEngine.ShutdownLifecycleTests.ShutdownLifecyclePlayModeTests.UpdateDriver_FixedUpdateListenerShutdownStopsRemainingListeners" -testResults "Temp\ShutdownLifecycle.TestFix.FixedUpdate.xml" -logFile "Temp\ShutdownLifecycle.TestFix.FixedUpdate.log"
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform playmode -testFilter "TEngine.ShutdownLifecycleTests.ShutdownLifecyclePlayModeTests.UpdateDriver_LateUpdateListenerShutdownStopsRemainingListeners" -testResults "Temp\ShutdownLifecycle.TestFix.LateUpdate.xml" -logFile "Temp\ShutdownLifecycle.TestFix.LateUpdate.log"
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -runTests -testPlatform playmode -testFilter "TEngine.ShutdownLifecycleTests.ShutdownLifecyclePlayModeTests.IndependentPlaySession_StartsWithFreshShutdownState" -testResults "Temp\ShutdownLifecycle.TestFix.DomainReloadOff.xml" -logFile "Temp\ShutdownLifecycle.TestFix.DomainReloadOff.log"
```

实际结果：本轮六次 batchmode 尝试均未进入测试发现/执行，未生成 XML 结果；Unity LicensingClient IPC 在约 60 秒超时，进程返回码 199。核心信息为 `IPC channel to LicensingClient doesn't exist; aborting`。项目同时存在 `Temp/UnityLockfile` 和已有 Unity 会话，因此没有删除锁、没有强杀/重启现有编辑器，也没有伪造 Unity 测试通过结果。三个 UpdateDriver 用例和双会话用例需要在可用许可证的 Unity Editor Test Runner 中按下述独立流程运行，不能由一次 batchmode 进程替代；本轮 Domain Reload Off 命令也只留下了许可证阻断日志，未构成双会话证据。

### 本次执行者追加探针（2026-09-09）

为确认阻塞状态没有因先前其他工程的 Unity 会话而改变，先检查了进程归属：现存的 5 个 Unity 进程均为 `D:\Work\SAUnity\Project` 的编辑器/AssetImportWorker，不占用本工程；本工程仍只有旧的 `Temp/UnityLockfile`（未删除）。随后只执行一次不带 `-runTests` 的许可证/项目启动探针：

```text
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" -logFile "Temp\ShutdownLifecycle.LicenseProbe.log"
```

日志路径：`Temp/ShutdownLifecycle.LicenseProbe.log`。实际结果仍为 `Connection to channel LicenseClient-Administrator refused`，等待约 60 秒后 `IPC channel to LicensingClient doesn't exist; aborting`，Unity 记录返回码 199；`Temp/ShutdownLifecycle.LicenseProbe.xml` 未生成。Test Runner 未进入 discovery，因此本次实际执行用例数为 0，不能给出通过/失败/跳过计数，也不能计作通过。探针后没有再启动任何重复的 batchmode 测试，没有删除锁、修改 Editor 设置或强杀进程。

因此本批不能声称已完成真实 EditMode/PlayMode、真实延迟 `Destroy`、真实 YooAsset EditorSimulate handle 释放或下一次独立 Play 会话验证；这些是交给 master 在可用 Unity 会话中直接复核的未运行项。

特别是 `ProjectSettings/EditorSettings.asset` 当前仍为 `m_EnterPlayModeOptionsEnabled: 0`（`m_EnterPlayModeOptions: 3`）。本批没有越出契约范围修改编辑器全局设置；双会话测试会直接读取 `UnityEditor.EditorSettings`，若未启用 Enter Play Mode Options/Disable Domain Reload 会失败，而不是把普通单次运行误报为通过。master 必须在同一 Unity Editor 中启用该选项，运行 `IndependentPlaySession_StartsWithFreshShutdownState`，退出 Play，再运行同一个测试第二次；第一次必须记录 session #1，第二次必须严格观察到 session #2。第一次会话由真实 Root 销毁触发关闭，关闭期间尝试注册受控晚回调；第二次在 `SubsystemRegistration` 后先验证 `TryGetExistingModule`/`RootModule.Instance` 为空，再创建全新的 Root/UpdateDriver，最后触发第二会话关闭并断言第一会话订阅和晚回调仍未执行。该流程不调用反射重置，也不支持同进程业务 Restart。

## 覆盖矩阵

| 场景 | 代码/测试覆盖 | 实际执行状态 |
| --- | --- | --- |
| 重复退出、退出重入 | `ModuleSystemShutdownTests`：重复 `Shutdown`、模块内重入 | 测试源码已加入；Unity 未执行 |
| 清理异常后继续 | Root listener、模块异常汇总和后续模块继续 | 测试源码已加入；Unity 未执行 |
| 退出中注册模块/事件 | `RegisterModule`、`GetModule`、`BeforeShutdown`、GameEvent 和各入口 gate | 静态编译通过；Unity 未执行 |
| 更新回调内触发退出 | EditMode 保留 `Update_ShutdownReentryFromCallback_DoesNotVisitClearedList`；三个 UpdateDriver 首监听短路用例在 PlayMode 真实 PlayerLoop 中等待挂载、触发退出并等待延迟宿主销毁，同时捕获错误日志 | 新增源码已编译；Unity 未执行 |
| 关闭后查询模块 | `ShutdownQueriesExcludeClosedModulesButKeepOpenDependencies`：字典接口别名、列表回退和未关闭依赖 | 新增源码已编译；Unity 未执行 |
| 部分初始化失败 | 模块 `OnInit` 失败后一次性局部 shutdown；FSM/FGUI/资源依赖也有回滚 | 静态编译通过；Unity 未执行 |
| 加载中退出/晚回调 | 资源 lifetime token、预加载代际、FGUI package/external loader、音频/场景/UI generation | 既有针对性测试工程编译通过；真实运行未执行 |
| 活动/未激活实例和延迟 Destroy | 真实 Root GameObject 延迟销毁触发关闭；`BeforeShutdown` 观察池条目 `SpawnCount=2`，关闭后断言两个实例各归还一次 | PlayMode 未执行，原因见 Unity 限制 |
| YooAsset handle | `ShutdownHandleDisposeCount=1`、池关闭后索引为空、延迟 `OnDestroy` 后计数不变 | 静态编译通过；真实 handle 运行验证未执行 |
| Root/UpdateDriver 销毁顺序 | PlayMode trace 断言 `RootModule.BeforeShutdown` 先于实际 `UpdateDriver.OnDestroy`，Root/driver 均实际落地销毁 | PlayMode 未执行，原因见 Unity 限制 |
| 下一次独立 Play / Domain Reload Off | 显式 `IndependentPlaySession_StartsWithFreshShutdownState`：EditorSettings 断言、session #1/#2 严格计数、Root/模块缓存、订阅和受控晚回调隔离 | 代码已落地；真实两次会话未执行，且当前项目选项未启用 |

## Master 后续复核

请在可用且没有其他 Unity 会话占用项目的编辑器中，直接检查共享工作树，并优先运行：

1. `TEngine.ShutdownLifecycle.Tests` EditMode 全部用例；确认不再包含 UpdateDriver Unity `Destroy`。
2. 分别在独立 Play 会话运行三个显式 UpdateDriver 测试：Update、FixedUpdate、LateUpdate；检查两个 listener 均已挂载、首 listener 退出后第二个计数为 0、宿主延迟销毁完成且无 Error/Exception/Assert 日志。
3. 运行 PlayMode 中的 EditorSimulate handle/延迟 Destroy 用例，检查 `BeforeShutdown` 阶段、池 `SpawnCount=2 -> 0`、`ShutdownAssetUnspawnCount=2`、`ShutdownHandleDisposeCount=1`，以及延迟 `OnDestroy` 后计数不变。
4. 在同一 Unity Editor 的 Enter Play Mode Options 中启用 Disable Domain Reload，独立进入/退出 Play 两次，运行同一个显式双会话测试两次；确认 XML/日志分别包含 session #1/#2 和第二会话隔离断言。
5. 真实 Root 销毁、UpdateDriver 延迟销毁、YooAsset handle 释放及 Domain Reload Off 双会话均通过后，输出 `## REVIEW_PASS`；任一断言失败则输出具体 `## SLAVE_FIX_HANDOFF`。

若 Unity 运行结果确认顺序、handle 计数、订阅/缓存均无遗留，则输出 `## REVIEW_PASS`；否则按失败断言和源码位置输出具体 `## SLAVE_FIX_HANDOFF`。
