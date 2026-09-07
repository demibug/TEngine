# 第 1 批实施验证记录：TEngine 事件分发可靠性

日期：2026-09-07。执行者：slave 实施轮（本记录）。契约：[event-dispatch-reliability-plan.md](event-dispatch-reliability-plan.md)。

## 实施概要

工作树在本次会话前已包含本批的生产实现与测试初稿（未提交）；本次会话逐条审计契约符合性、修复测试缺陷并完成批处理验证。

### 生产行为（`Assets/TEngine/Runtime/Core/GameEvent/`，基线已有修改，本次未再改动）

- `EventDelegateData.cs`：以“分发深度 + 首次有效变更时懒拷贝的下一版本监听列表（`_nextList`）”替换原布尔 `_isExecute`/`_dirty` 与先加后删的 `CheckModify`。
  - 全部 0–6 参数 `Callback` 重载：`_dispatchDepth++` + `try/finally { EndDispatch(); }`；回调异常原样向上传播并结束本次后续回调，finally 在异常退出时同样恢复深度并提交待变更。
  - 仅最外层退出（深度归零）时 `_listExist = _nextList` 提交，嵌套期间不提前提交；无变更的分发不产生列表拷贝。
  - `AddHandler` 成员关系按 `(_nextList ?? _listExist).Contains` 判定：待新增重复返回 false；移除后再注册排到末尾；增删在同一列表上按调用顺序生效。
  - 非分发路径保留 `Log.Fatal` 日志惯例（重复注册 / 缺失移除）；分发中重复移除为无操作；类型不匹配的委托仍被跳过。
- `EventDispatcher.cs`、`GameEvent.cs`：仅补充契约 remarks 注释，无行为改动，公开 API 未变。

### 本次会话修改（均为未跟踪的测试文件）

| 文件 | 修改 |
| --- | --- |
| `Assets/Tests/GameEvent/EditMode/GameEventGlobalTests.cs` | 测试命名空间 `TEngine.GameEvent.Tests` → `TEngine.GameEventTests`（修复编译错误，见下） |
| `Assets/Tests/GameEvent/EditMode/EventDispatcherDispatchTests.cs` | 同上命名空间修复；`NestedThrowsCaughtByOuter_...` 用例将 `nestedPending`/`outerPending` 声明提前至 `b` 之前，并在 `b` 抛异常前注册 `nestedPending`（原用例从未注册 "E" 监听却断言其出现，且新引用早于声明导致 CS0841） |
| `Assets/Tests/GameEvent/EditMode/TEngine.GameEvent.Tests.asmdef` | `rootNamespace` 同步改为 `TEngine.GameEventTests`（程序集名未变） |

### 编译错误根因记录

- CS0234：测试命名空间 `TEngine.GameEvent.Tests` 隐式创建命名空间 `TEngine.GameEvent`，其内部标识符 `GameEvent` 被解析为该命名空间而非 `TEngine.GameEvent` 类，`GameEvent.Send/Shutdown/...` 全部报“不在命名空间中”。改名 `TEngine.GameEventTests` 后不再产生遮蔽层。
- 已记入 `.claude/memory/problem_2026-09-07.md` 同级约定：此为通用命名陷阱，后续新增测试程序集时应避免 `<类型名>.Tests` 形式的命名空间前缀撞同名类型。

## 验证结果

### 命令与入口

```
"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode \
  -projectPath "E:\MyWork\MyFramework\TEngine\UnityProject" \
  -runTests -testPlatform EditMode \
  -testResults <temp>\gameevent-results.xml \
  -logFile <temp>\gameevent-test2.log
```

- 运行前提：用户手动关闭了交互式 Unity 编辑器（原 PID 62668）以释放项目锁；批处理实例自身持有 License 正常（日志含 Successfully updated license）。
- 首次批处理运行（`gameevent-test.log`）因上述测试编译错误中止（"Scripts have compiler errors"），修复后第二次运行完成并生成结果 XML。

### 结果

- 总计 36 个 EditMode 用例，`total=36 passed=36 failed=0 inconclusive=0 skipped=0`，总执行 0.13s。
- 本批范围 29 例全部通过：
  - `TEngine.GameEventTests.EventDispatcherDispatchTests`：20 例（异常传播与恢复、异常后增删提交、嵌套增删不可见、嵌套异常被外层捕获后最外层按序提交、交叉事件、分发中删除 B 仍执行、重复注册 false、新增后删除不存在、顺序增删移至末尾、缺失移除不抛、0–6 参数重载参数传递与异常恢复一致、类型不匹配跳过）。
  - `TEngine.GameEventTests.GameEventGlobalTests`：6 例（GameEventMgr 分发中 AddEvent 后 Clear/重复 Clear 无残留、非分发 Clear、回调内 Shutdown 重注册仅新监听生效、旧待变更不复活、全局异常后恢复、int/string 入口参数传递）。
  - `GameLogic.FairyGUI.Tests.FguiLifetimeScopeEventTests`：3 例（普通 Dispose 后不再接收、分发中 Dispose 当前回调执行完且下一轮不执行、重复 Dispose 不抛）。
- 其余 7 例为基线已有的 `FguiPackageServiceTests`（非本批范围），一并通过，说明既有 FGUI 功能未受影响。
- 结果 XML 存档：`event-dispatch-reliability-test-results.xml`（与本文件同目录）；原始日志在临时目录 `gameevent-test2.log`（临时文件，未入库）。

### 验收矩阵对照

| 契约场景 | 用例 | 结果 |
| --- | --- | --- |
| A 抛异常后 B 不执行，移除 A 后 B 可用 | `Send_FirstListenerThrows_PropagatesSkipsRestAndRecovers` | 通过 |
| A 添加 C 后抛出，finally 提交 C | `Send_ListenerAddsSameEventThenThrows_CommitsAddViaFinally` | 通过 |
| A 删除 B，本轮仍执行、下一轮不执行 | `RemoveDuringDispatch_CurrentStillInvoked_NextNot` | 通过 |
| A 添加 C + 受控同事件嵌套，嵌套不可见 | `AddDuringDispatch_CurrentAndNestedDoNotSeePending_NextSendDoes` | 通过 |
| 删除 B 再添加 B，本轮 A/B/C、下轮 A/C/B | `RemoveThenAddDuringDispatch_CurrentUnchanged_NextReorderedToTail` | 通过 |
| 待新增重复 true/false；新增后删除不存在 | `AddPendingTwice_...` / `AddThenRemovePendingDuringDispatch_...` | 通过 |
| 内层异常被外层捕获，最外层按序提交 | `NestedThrowsCaughtByOuter_OuterMutates_PendingCommitOnlyAtOuterExit` | 通过（本次修复后） |
| 两事件交叉 Send 互不污染 | `CrossEventSend_IndependentDepthAndCommits` | 通过 |
| 分发中 AddEvent 后 Clear、重复 Clear | `GameEventMgr_ClearDuringDispatch_NoResidueAndRepeatClearSafe` | 通过 |
| 回调内 Shutdown 后重注册，旧待变更不复活 | `ShutdownDuringCallback_ReregisteredEventOnlyNewListenerFires` / `ShutdownDuringCallback_PendingChangeDoesNotRevive` | 通过 |
| int/string 入口与 0–6 参数重载 | `GameEvent_IntAndStringEntries_PassArguments` + `Send_ZeroArg..SixArgs_...` 系列 | 通过 |
| FguiLifetimeScope 注册/Dispose | `FguiLifetimeScopeEventTests` 3 例 | 通过 |

## 其他检查

- `git diff --check`：通过（仅既有 LF/CRLF 警告，属工作树原有状态）。
- 生产修改范围核对：本批相关生产行为改动仅限 `Assets/TEngine/Runtime/Core/GameEvent/` 三个文件（`EventDelegateData.cs` 行为改动 + 两文件注释）；其余工作树变更（FGUI 接入、GameApp/GameModule/RootModule、资源设置等）为基线已有，未触碰。
- 新增文件均有配套 `.meta`（Tests 目录、子目录、asmdef、各 .cs）。

## 未运行 / 范围外项

- PlayMode 测试（`FguiRuntimeHostTests`、`FguiWindowModuleTests`）未运行：本批契约仅要求事件解绑回归，不需要图形资源或完整 UI 场景。
- Player 构建、全量游戏测试、性能 benchmark：契约明确不要求。
- `GameEvent.Send(int, Delegate)` 直通重载未新增行为断言（契约禁止将其解释为直接调用传入委托，保持现状）。

## Master 复核记录（2026-09-07）

- 复核方式：从共享工作树 git status/diff 与新增文件静态审查，并独立重跑 EditMode 测试。
- 一手命令：`"C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe" -batchmode -nographics -projectPath <工程> -runTests -testPlatform EditMode -testResults <temp>\review-results.xml -logFile <temp>\review-test.log`。
- 一手结果：`total=36 passed=36 failed=0 skipped=0 result=Passed`，与本记录归档 XML 一致，复核通过。
- 附带核对：新增文件 meta 全部齐备；本批生产行为改动仍仅限 `Assets/TEngine/Runtime/Core/GameEvent/`；公开 API 未变。
- 复核结论：满足契约验收矩阵，输出 `## REVIEW_PASS`。

## 已知限制

- 分发中移除不存在/已不存在的监听会触发一次懒拷贝（`EnsureNextList`）即使最终无有效变更，属可接受的微小分配，不影响正确性。
- `GameEventMgr` 构造函数中 `readonly _isInit` 的无意义自检为基线遗留代码，未清理（超出本批范围）。
- 全局退出顺序、UGUI owner 池化引用、FGUI 完整生命周期审查等留待后续批次。
