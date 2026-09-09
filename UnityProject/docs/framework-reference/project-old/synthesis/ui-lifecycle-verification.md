# 第 4 批：UGUI / FGUI 窗口生命周期验证记录

日期：2026-09-08。工程：`E:/MyWork/MyFramework/TEngine/UnityProject/`。

本记录对应 `ui-lifecycle-plan.md`。资源批次已有工作树变更未暂存、未提交、未恢复，也未修改资源模块核心、事件核心、对象池核心、SDK、业务 UI、生成文件或全局退出顺序。

## 实际实现

- UGUI 在 `UIModule` / `UIWindow` 中增加每实例 Loading、Ready、Closing、Closed、Failed 状态、完成源和生命周期取消源。异步成功只在资源组件、创建、首次刷新、层级和可见性处理完成后提交；加载失败、准备异常、关闭取消和不受时间缩放影响的超时分别传播。
- UGUI Loading 重复打开共享同一实例和完成源，最后一次 `userDatas` 用于首次刷新；Ready 重复打开只刷新。关闭先取消并移出当前实例，再运行可重入的展示清理；资源 loader 不响应取消时，晚到面板由 relay 销毁。
- UGUI 清理将事件 manager 先摘除字段再归还，关闭 owner 禁止重新注册事件/子项；timer 按登记 owner 移除；子 widget、事件、`OnDestroy`、面板清理逐项继续，异常记录但不阻断后续资源释放。
- FGUI 保留首个创建者参数和等待者取消语义；创建等待者先登记，关闭旧创建后立即重开时等待共享包加载的取消收尾完成再开启新代际。创建、可见性和关闭均按实例身份校验，避免旧 finally 删除新窗口。
- FGUI 已存在窗口的 `OnRefreshAsync` 使用同窗串行队列，等待 token 与窗口生命周期联合取消；关闭先使窗口失效并从 `_windows` 移除，刷新结束后不得 BringToFront 或返回失效 View。失败刷新关闭当前实例。

## 实际修改文件

生产代码：

- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiModule.cs`
- `Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/FguiWindow.cs`

测试和元数据：

- `Assets/Tests/UILifecycle/UGUIWindowLifecycleTests.cs`
- `Assets/Tests/UILifecycle/GameLogic.UILifecycle.PlayModeTests.asmdef`
- 上述目录及文件的 `.meta`
- `Assets/Tests/FairyGUI/PlayMode/FguiWindowModuleTests.cs`

文档：

- `docs/framework-reference/project-old/synthesis/ui-lifecycle-verification.md`

本批没有修改 `IUIResourceLoader.cs`、`FguiLifetimeScope.cs` 或 `FguiPackageService.cs`。

## 验证命令与结果

Unity 版本：`2022.3.62f2`。

1. 先按资源批次记录复现 FGUI 并发打开测试：

   `Unity.exe -batchmode -nographics -projectPath E:/MyWork/MyFramework/TEngine/UnityProject -runTests -testPlatform PlayMode -testFilter GameLogic.FairyGUI.PlayModeTests.FguiWindowModuleTests.WindowModule_MergesConcurrentShow_AndClosePreventsLateWindow`

   基线结果：`Temp/FguiWindowModuleTests.baseline2.xml` 失败，`passed=0 failed=1`，测试以 `System.OperationCanceledException` 结束；随后保留该失败事实并在 `FguiModule` 内修复关闭旧创建后立即重开代际竞态。

2. Unity 编译：

   `Unity.exe -batchmode -nographics -quit -projectPath E:/MyWork/MyFramework/TEngine/UnityProject -logFile Temp/UILifecycle.compile-final3.log`

   结果：退出码 0；无 `error CS`、`Compilation failed` 或 `Scripts have compiler errors`。日志中的既有 nullable warning 不影响编译。

3. UGUI 真实 GameObject PlayMode：

   `Unity.exe -batchmode -nographics -projectPath E:/MyWork/MyFramework/TEngine/UnityProject -runTests -testPlatform PlayMode -testFilter GameLogic.UILifecycle.PlayModeTests.UGUIWindowLifecycleTests -testResults Temp/UGUIWindowLifecycleTests.final3.xml`

   结果：`testcasecount=4 passed=4 failed=0`。覆盖：重复 Loading 合并和最新参数、同步打开冲突、Hide→Show timer 归属、加载中关闭/晚到面板/立即重开、Get 等待取消及回调、null/异常 loader、`timeScale=0` 超时、hook 自关闭、子 widget/窗口清理异常、池化 `GameEventMgr` 复用和重复关闭。

4. FGUI 真实 View PlayMode：

   `Unity.exe -batchmode -nographics -projectPath E:/MyWork/MyFramework/TEngine/UnityProject -runTests -testPlatform PlayMode -testFilter GameLogic.FairyGUI.PlayModeTests.FguiWindowModuleTests -testResults Temp/FguiWindowModuleTests.final2.xml`

   结果：`testcasecount=2 passed=2 failed=0`。覆盖：并发创建、取消一个 waiter、创建/可见性 hook 失败重试、关闭后立即重开、清理 hook 异常，以及已有窗口刷新串行和关闭取消竞态；provider 活跃资源租约最终为 0。

5. FGUI EditMode 回归：

   `Unity.exe -batchmode -nographics -projectPath E:/MyWork/MyFramework/TEngine/UnityProject -runTests -testPlatform EditMode -testFilter GameLogic.FairyGUI.Tests -testResults Temp/FguiEditMode.final.xml`

   结果：`testcasecount=10 passed=10 failed=0`，包含 `FguiLifetimeScopeEventTests` 和 `FguiPackageServiceTests`。

6. 静态检查：`git diff --check` 通过；仅报告 Git 对现有工作树文件的换行转换提示。

## 限制和未运行项

- 未运行 Player/Standalone 构建、真机、多平台、真 YooAsset AssetBundle 资源路径或性能/压力测试。FGUI PlayMode 使用项目现有 AssetDatabase provider 和真实 FairyGUI View；这不能替代真 YooAsset Player smoke test。
- 没有把业务任务强行纳入框架清理；资源 loader 或业务 hook 若忽略传入 token，框架只能在其晚到结果处丢弃/销毁并阻止状态提交。
- 工作树仍包含资源批次及其他既有变更；审查者必须直接检查共享工作树和 `git diff`，不要依据本记录推断其他文件属于本批。

## 2026-09-08 审查返修（3 项）

本节记录返修后的状态；上文 4/4、2/2、10/10 是返修前历史结果，不代表新增回归已通过。

- P1 关闭重入：`CloseWindow` 先按实例从栈移除，再调用 `BeginClose`，因此 timer/Token 回调同步重开同类型时不再命中旧窗口。新增取消回调同步 ShowUI、旧等待取消、晚到旧面板销毁且新实例保持有效的测试。
- P1 Widget 资源：路径加载和 Prefab 实例化前预检 owner；对已加载/克隆但未成功创建 Widget 的实例用 finally 销毁，覆盖 loader 忽略取消、同步 loader 重入关闭、关闭 owner 的路径和 Prefab 创建。保留原有同步 API 语义。
- P2 FGUI hook：`ThrowIfInvalid` 检查调用者 Token、窗口状态及生命周期 Token。新增工厂中取消创建测试、队列取消测试，以及通过反射直接调用刷新入口，确定性模拟队列前置检查后 Token 已取消的边界状态；断言用户 hook 未执行及 View/Lifetime/package lease 清理。入口边界测试不是多线程压力测试。

返修验证：

- `dotnet build GameLogic.UILifecycle.PlayModeTests.csproj --no-restore -v:q`：成功，0 错误，1 条既有 CS8632 warning。
- `dotnet build GameLogic.FairyGUI.PlayModeTests.csproj --no-restore -v:q`：成功，0 错误，0 warning。以上仅验证 C# 编译，不代替 Unity Test Runner。
- Unity PlayMode 联合筛选 UGUIWindowLifecycleTests 与 FguiWindowModuleTests：未进入 Test Runner。`Temp/UILifecycle.review-fixes.log` 记录 Licensing IPC 等待 60 秒失败及 return code 199；没有生成 `Temp/UILifecycle.review-fixes.xml`。尚需在许可可用环境运行新增和既有测试。
- `git diff --check`：通过，只有换行转换提示。

本次仅修改 UIModule.cs、UIBase.cs、UIWidget.cs、FguiWindow.cs、两份生命周期测试和本记录；资源批次及其他既有变更保持原状，未暂存或提交。
