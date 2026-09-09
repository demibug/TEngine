# 第 5A 批：启动成功与失败终态 — 实施与验证记录

状态：实施完成，待 master 正确性审查。5B 退出协调未实施。

工程：`E:/MyWork/MyFramework/TEngine/UnityProject/`

依据：[启动终态执行契约](startup-terminal-plan.md)。

## 一、实施摘要

### Bootstrap 与 YooAsset 包状态

- `IResourceModule.WaitUntilInitializedAsync(CancellationToken)` 等待的是 ResourceModule bootstrap，不等同 YooAsset 包或 manifest ready；调用者取消只取消当前等待者。
- `ResourceModule` 保存 bootstrap 的成功/失败终态和原始异常。`ResourceModuleDriver` 的参数读取、对象池配置及 `Initialize` 异常都在 bootstrap 完成前进入共享失败源；缺少 Driver 时流程等待自己的非缩放超时。
- `InitPackage` 为同包同参数的 Processing 请求共享一个 `UniTaskCompletionSource`，成功重复调用返回有效的已完成 `InitializationOperation`；参数冲突显式失败。YooAsset 初始化失败删除 active context，保留包对象以遵守本地 `ResetInitializeAfterFailed` 重试协议；已成功初始化后只重试 manifest，不再次调用 `InitializeAsync`。
- `BeginPackageInitialization` 返回自己捕获的 completion task，避免 YooAsset 操作同步完成时 `ActiveCompletion` 已清空而向调用方返回 null 的竞态。

### 流程代际与终态

- `StartupAttempt` 为每次进入 `ProcedureInitPackage`、`ProcedureLoadAssembly` 和 `ProcedureStartGame` 建立独立代际；离开流程会取消并失效当前代际。所有异步完成、失败、重试和状态跳转都先检查当前代际。
- `ProcedureInitPackage` 先等待 bootstrap，再等待包初始化；成功后才进入 `ProcedureInitResources`。失败、取消、非缩放超时均结束当前等待，旧回调不会再次显示失败或跳转。
- `ProcedureLoadAssembly` 移除计数式 OnUpdate 汇合，按 metadata → DLL → 主程序集入口的顺序提交；每个 `TextAsset` 都在 `finally` 中配对 `UnloadAsset`。DLL/metadata 失败立即阻止启动，已注入 metadata/已加载程序集不做回滚，也不提供进程内热更重载式重试。
- 入口只接受主程序集内精确的 `public static void Entrance(object[])`。在 `Invoke` 前设置一次性闸门；入口正常返回且 attempt 仍有效后，才提交 `ProcedureStartGame`。
- `ProcedureStartGame` 的一帧 `Yield` 后再次验证代际，旧流程不能隐藏新流程的 Launcher。
- 入口同步返回后自行启动的异步 UI/业务不计入本批成功定义。

## 二、实际修改文件

本批修改/新增的相关文件：

- `Assets/GameScripts/Procedure/ProcedureInitPackage.cs`
- `Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs`
- `Assets/GameScripts/Procedure/ProcedureStartGame.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/PreloadRequestRunner.cs`（预加载改用可等待接口，异步异常收敛为失败终态）
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModuleDriver.cs`
- `Assets/TEngine/Runtime/AssemblyInfo.cs`（仅保留资源/启动测试所需的 Runtime 内部可见性）
- `Assets/GameScripts/Procedure/ProcedureBase.cs`（承载应用层启动 attempt 与入口契约辅助类型）
- `Assets/Tests/StartupLifecycle/`（EditMode/PlayMode 测试、asmdef、meta）
- `Assets/Tests/ResourceLifecycle/EditMode/PreloadRequestRunnerTests.cs`（覆盖预加载同步/异步异常的终态收敛）
- 本文件

未修改 5B 退出顺序、GameApp 入口、热更程序集名单、业务入口、第三方 YooAsset/HybridCLR 源码及资源引用核心。工作树中其他前批改动均保留。

## 三、测试与验证

### 已执行的静态验证

以下命令均在共享工作树执行：

| 命令 | 结果 |
| --- | --- |
| `dotnet build TEngine.Runtime.csproj --no-restore` | 通过，0 警告、0 错误 |
| `dotnet build TEngine.ResourceLifecycle.Tests.csproj --no-restore` | 通过，0 警告、0 错误 |
| `dotnet build TEngine.ResourceLifecycle.PlayModeTests.csproj --no-restore` | 通过，0 警告、0 错误 |
| `dotnet build GameLogic.csproj --no-restore` | 通过，0 警告、0 错误 |
| `dotnet build GameLogic.FairyGUI.Tests.csproj --no-restore` | 通过，0 警告、0 错误 |
| `dotnet build GameLogic.FairyGUI.PlayModeTests.csproj --no-restore` | 通过，0 警告、0 错误 |
| `dotnet build GameLogic.UILifecycle.PlayModeTests.csproj --no-restore` | 通过，0 警告、0 错误 |
| `git diff --check` | 通过；仅有 Git 报告的 LF/CRLF 转换提示 |
| `dotnet build Assembly-CSharp.csproj --no-restore` | 通过，0 错误；仅有既有程序集引用冲突警告。22 条 `CS0507` 已由移除 `Assembly-CSharp` friend 可见性消除，未批量修改流程覆写签名。 |

当前新增 `TEngine.StartupLifecycle.Tests` / `TEngine.StartupLifecycle.PlayModeTests` asmdef 尚未进入 Unity 生成的 `.csproj`，因此不能声称这两个测试程序集已完成 `dotnet build` 编译验证；其中的启动辅助类型测试改为反射访问已编译的应用层 `Assembly-CSharp` 类型，不再依赖 Runtime friend 可见性。

### 已添加的测试覆盖

- EditMode：attempt 终态只能提交一次；离开后的取消 token/晚回调不可再成功；入口缺失、返回值错误、重载歧义均拒绝；bootstrap 原始异常共享；单个等待者取消不取消共享 bootstrap；非法 Driver 对象池配置在 bootstrap 完成前失败。共 9 个 NUnit case（含 3 个入口 TestCase 实例）。
- PlayMode：`timeScale=0` 时非缩放超时仍触发；真实 YooAsset 同包并发初始化合并、成功重复调用返回同一有效操作、不同参数显式冲突，并覆盖配置失败后的重试。
- ResourceLifecycle EditMode：预加载去重、配对归还、失败终态、代际隔离和重入；同步或异步加载异常均会记为失败，所有地址终态后继续启动流程。
- 资源加载与 HybridCLR Player 的代码路径已人工核对：metadata 返回码仅 `LoadImageErrorCode.OK` 成功；DLL/metadata 的 null、加载异常、错误返回码均在入口前失败；每个取得的 `TextAsset` 均有 finally 归还。

### Unity 测试命令

实际执行过：

```text
C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe
  -batchmode -nographics
  -projectPath E:\MyWork\MyFramework\TEngine\UnityProject
  -runTests -testPlatform editmode
  -testFilter TEngine.StartupLifecycleTests
  -testResults E:\MyWork\MyFramework\TEngine\UnityProject\Temp\startup-lifecycle-editmode-results.xml
  -quit
  -logFile E:\MyWork\MyFramework\TEngine\UnityProject\Temp\startup-lifecycle-editmode.log
```

结果不是测试失败，而是 Unity 在编译/测试前无法连接本机 `LicenseClient-Administrator`：日志显示 IPC refused，等待 60 秒后 return code 199；没有生成 `startup-lifecycle-editmode-results.xml`。因此本批不能声称上述 EditMode/PlayMode 用例已在 Unity 中通过。

另一次仅项目刷新/编译的 Unity batch 也得到相同 Licensing IPC 失败。未继续重试或修改本机授权服务。

同样的命令以 `-testPlatform playmode` 和 `startup-lifecycle-playmode-results.xml` 再执行一次，结果仍为相同 Licensing IPC refused/return code 199，未生成 PlayMode XML。

## 四、未运行项与边界

- 未运行真实 Unity Editor EditMode/PlayMode 结果、完整流程生命周期、真实 Player、IL2CPP、真包下载、HybridCLR metadata 注入及 DLL 损坏/入口抛错的运行时注入场景；这些只能由静态控制流和测试源码证明，不能冒充 Player 验证。
- 同步 `Assembly.Load`/`GameApp.Entrance` 内部不可被超时强行抢占；超时保护异步等待，入口同步段的限制已保留并记录。
- `Entrance` 抛错后程序集/metadata 不可卸载，失败 UI 只允许退出/重启，不提供进程内重新加载式重试。
- Unity 授权恢复前无法运行 StartupLifecycle EditMode/PlayMode；静态 `Assembly-CSharp`、Runtime、资源模块、GameLogic 及现有相关测试项目已分别通过，新增 StartupLifecycle asmdef 的 csproj 编译仍待 Unity 重新生成项目文件后验证。
- 5B 退出协调、全局退出顺序和资源池 shutdown 收尾没有实施。

## 五、手动审查重点

请 master 直接检查共享工作树，不重实现、不委派：

1. bootstrap completion、包初始化 context 和 YooAsset 失败重试是否可能假成功、返回 null 或永久占住 Processing。
2. timeout/leave 后旧回调是否仍能 ChangeState、更新新 attempt 或泄漏 TextAsset/metadata 资源。
3. metadata/DLL 全部成功、精确入口校验及入口一次性闸门是否严格先于 `ProcedureStartGame`。
4. `Entrance` 失败后的不可回滚行为是否错误地提供了进程内重试。
5. `ProcedureStartGame` 的晚 Yield 是否可能隐藏新流程 UI。

本批结束时不自动实施 5B。
