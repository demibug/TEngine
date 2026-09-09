# 第 3 批：资源生命周期可靠性 — 实施与验证记录

状态：实施完成，待 master 正确性审查。
工程：E:/MyWork/MyFramework/TEngine/UnityProject/
依据：[resource-lifecycle-plan.md](resource-lifecycle-plan.md)。

## 一、实施摘要

### 1. ResourceModule 加载核心（ResourceModule.cs）

- **同 key 等待与取消隔离**：重写 `TryWaitingLoading` → `WaitLoadingKeyAsync(key, CancellationToken)`。
  - 等待感知各自 token；取消等待者只结束自己，不动加载标记、不动 handle、不影响其他等待者。
  - 去掉跨请求共享的 `TimeoutController`。编辑器保留 60 秒诊断，改为**请求独立** `CancellationTokenSource(TimeSpan.FromSeconds(60))`，超时仅结束该请求（记 Error 日志并返回失败），不影响 key owner，不绕过标记继续加载。生产无 token 请求不承诺超时。
- **加载标记 owner 协议**：`TryEnterLoading` 用 `HashSet.Add` 原子占位（主线程无 await 间隙）；只有 owner 在 `finally` 中 `ExitLoading`。等待者醒来后循环重查（缓存 → 竞争标记 → 等待）。
- **handle 所有权**：handle 在登记成功前由请求持有；失败/取消/登记异常路径经 `SafeDisposeHandle` 收尾一次（`finally` 双保险）；`RegisterLoadedAsset` 成功后仅池持有 handle。禁止把 Failed、null、类型不符登记为成功对象（登记前统一核验 `Status`/非空/`IsInstanceOfType`）。
- **交付点语义**：缓存 Spawn 后与 owner 加载完成后的交付前取消，立即 `ReturnSpawn` 归还这一份 spawn 后返回 null；交付后取消不回收调用者资产。
- **缓存命中类型校验**：`TrySpawnCacheAsset` 命中后验证可赋值性，不符归还本次 spawn、记 Error、返回失败（不再错误 cast 后占引用）。
- **无接收者与类型入口**：`Action<T>` 成功回调为空时归还本次 spawn；回调集合没有成功接收者时同样只保留预热缓存、不保留 spawn；缓存命中、冷加载和句柄入口统一把 null 类型规范化为 `UnityEngine.Object`，同步登记前完成类型校验。
- **异常与实例补偿**：句柄等待只归一化调用者取消或句柄明确 `Failed`，其他异常在 owner finally 后继续传播；绑定前失败走 `Destroy + ReturnSpawn`，绑定后失败/取消由 `AssetsReference` owner 归还并销毁一次，不再手动摘除后重复归还。
- **同步入口冲突**：`LoadAsset` / `LoadGameObject` 缓存未命中且同 key 异步加载中时，抛出明确的 `GameFrameworkException`（"conflicts with an in-flight async load"），不阻塞等待、不注册第二份。
- **回调入口单终态**：两个 `LoadAssetCallbacks` 重载与 `UniTaskVoid LoadAsset<T>` 收敛到共享核心 `LoadAssetWithCallbacksAsync`，每次请求只产生一个成功或失败终态；成功回调进入前完成资源登记与持有权转移；业务成功回调抛错不触发失败通知、不撤销已交付资源，异常经 UniTaskVoid 通道保持可见；无失败接收者时保持既有"抛 GameFrameworkException"语义。
- **进度观察者**：`ObserveProgress` 逐帧检查句柄有效性；进度回调抛错被捕获、记录 Error 并停止该观察者，不卡住加载、不产生未结束的进度任务。
- **缓存 Key 规范化**：非默认包键改为 `BuildPackageCacheKey`（长度前缀 `N:pkg/location`），消除 `packageName/location` 简单拼接碰撞；空包与 `DefaultPackageName` 显式/隐式共享缓存；`GetAssetInfo` 的信息缓存同步使用该键。
- **修复意外绑定缺陷**：原任务路径 `handle.ToUniTask(cancellationToken: cancellationToken)` 因命名参数匹配落到 `EnumeratorAsyncExtensions.ToUniTask(IEnumerator)`（`HandleBase : IEnumerator`），失败时不抛异常且每帧产生 "yield not supported" 隐患；统一改为与真实语义一致的等待并显式核验 `Status`/`AssetObject`。
  - 备注：本地 TEngine.Runtime 实际仅引用 `UniTask.dll`（未引用 `UniTask.YooAsset.dll`，见 `Library/Bee/artifacts/1900b0aEDbg.dag/TEngine.Runtime.rsp`），所有 handle 等待均走协程轮询版 `ToUniTask`，失败不抛异常。核心对"异常呈现"与"null/Failed 呈现"两种适配器行为都做了收尾，切换引用后行为不变。

### 2. AssetsReference 实例引用（Reference/AssetsReference.cs）

- 绑定时解析并保存实例级 `_ownerModule`（参数为空则 `ModuleSystem.GetModule<IResourceModule>()`），清理时不重新获取或创建模块；删除被任意实例覆盖的 static module。
- 只有登记在 `_originalRefs` 中的原始实例在 `OnDestroy` 归还；克隆（含**未激活克隆**，其 `Awake` 不会提前运行）销毁时仅摘除拷贝记录，绝不归还原实例持有。
- `ReleaseInternal` 幂等（`_released` 标记）；先摘除记录（`sourceGameObject`/列表置空）再逐条归还；某一条归还异常被记录后继续其余条目；`AssetsRefInfo` 每条合法登记都归还，不按对象去重。
- `AssetsReference.Instantiate` 复用实例上已存在的组件，避免 prefab 携带组件时 `DisallowMultipleComponent` 报错。
- `Ref<T>` 与 `Ref(GameObject)` 统一登记原始实例；绑定入口先清理 inactive clone 的复制字段。增加实例追踪补偿：`ResourceModule.Update` 扫描从未激活即销毁的组件，释放前先摘除登记，避免遗漏或二次归还。

### 3. 池与 AssetObject（ResourceModule.Pool.cs / ResourceModule.AssetObject.cs）

- `UnloadAsset`：null 参数告警返回；保持"一份成功加载对应一次归还"契约，未实现"同对象只释放一次"。
- `AssetObject.Release(false)`：先摘除再 `Dispose` 一次，异常记录不中断池回收循环。池 shutdown 场景（`isShutdown=true`）维持现状，全局责任留给第 5 批。
- 新增 internal 诊断入口 `GetAssetPoolObjectInfos` / `ReleaseUnusedPoolAssetsForDiagnostics`（供测试计数核对，不影响全局算法）。

### 4. PRELOAD（PreloadRequestRunner.cs 新增 + ProcedurePreload.cs）

- 抽出可测的 `PreloadRequestRunner`（TEngine.Runtime）：
  - 先建立去重清单再发起加载，PRELOAD 与 WEBGL_PRELOAD 重叠地址只加载一次；
  - 每次成功回调先配对归还预加载自己的 spawn（代际外晚回调同样归还），资产留在现有池按容量/过期策略回收；
  - 代际（`Begin`/`Invalidate` 推进）隔离晚回调：离开或再次进入后，旧终态仍归还资源但不更新新流程状态、不触发跳转；
  - 成功、失败分别记状态（`_failedLocations`），进度按终态项统计，不依赖字典遍历 break；
  - 保留"预热尽力而为，全部终态后继续"，失败记 Warning 日志。
- `ProcedurePreload` 改为使用 runner；`OnLeave` 失效当前代际；`OnUpdate` 无预热请求（EditorSimulate 跳过）时保持原"完成后继续"流转。

### 5. IResourceModule

仅补充 LoadAssetAsync/LoadGameObjectAsync/UnloadAsset 的所有权与取消行为文档，无 API 增删。

## 二、修改文件清单（本批全部）

新增：
- `Assets/TEngine/Runtime/Module/ResourceModule/PreloadRequestRunner.cs`（+meta）
- `Assets/TEngine/Runtime/AssemblyInfo.cs`（+meta，InternalsVisibleTo：TEngine.ResourceLifecycle.Tests / PlayModeTests）
- `Assets/Tests/ResourceLifecycle/`（EditMode：CacheKeyTests、PreloadRequestRunnerTests、asmdef；PlayMode：ResourceLifecyclePlayModeTests、asmdef，全部含 meta）
- `Assets/Tests/ResourceLifecycle/EditMode/PreloadRequestRunnerTests.cs`（另含材质绑定与从未激活对象的补偿扫描用例）

修改：
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`（加载核心重写）
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.AssetObject.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs`
- `Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs`（仅文档）
- `Assets/GameScripts/Procedure/ProcedurePreload.cs`

未触碰（只读核对）：ResourceExtComponent（含 SubSprite）、AssetsSetHelper、UGUI UIWindow、FGUI 桥接（FguiResourceProvider 使用裸 handle API，未接入自动 UnloadAsset）、场景加载器、YooAsset/UniTask 源码、ObjectPool 全局算法。
前批遗留工作树状态保持原样：`ProjectSettings/boot.config` 删除与 `UserSettings/Layouts/` 未跟踪均为批前已存在，未动。

## 三、验证记录

### 命令/入口

- 全量编译：`Unity.exe -batchmode -quit -nographics -projectPath … -logFile …`（退出码 0，无 CS 错误；多次迭代）。
- EditMode 上一轮记录：`Unity.exe -batchmode -nographics … -runTests -testPlatform EditMode -testResults te_editmode_final.xml` → **104/104 通过**。本轮在 `PreloadRequestRunnerTests.cs` 新增材质/未激活引用 2 例、补充同步抛错的后续请求终态断言，未能重新启动 Unity 测试。
- PlayMode 本批过滤：`-runTests -testPlatform PlayMode -testFilter TEngine.ResourceLifecycleTests` → **16/16 通过**。
- PlayMode 全量：`-runTests -testPlatform PlayMode` → **17/18 通过**，唯一失败为 `GameLogic.FairyGUI.PlayModeTests.FguiWindowModuleTests.WindowModule_MergesConcurrentShow_AndClosePreventsLateWindow`（System.OperationCanceledException）。
- 本轮 Unity 重跑尝试：生成 `Temp/resource-lifecycle-editmode-slave.log` 后因本机 `LicenseClient-Administrator` IPC 连接超时退出（return code 199），未生成测试 XML；因此本轮新增/修改的 Unity 用例没有宣称已运行通过。
- 本轮静态编译：`dotnet build TEngine.Runtime.csproj --no-restore`、`dotnet build TEngine.ResourceLifecycle.Tests.csproj --no-restore`、`dotnet build TEngine.ResourceLifecycle.PlayModeTests.csproj --no-restore` → 均 0 错误；`GameLogic.csproj` 仍有存量 `UIWidget.cs` 的 `Object`/`InternalDestroy` 编译错误，与本批资源文件无交集。
- `git diff --check` 通过；测试副作用（UTF 修改的 runInBackground、InitTestScene 临时场景）已清理还原。

### PlayMode 用例覆盖（真实 EditorSimulate + 真实 GameObject 生命周期）

1. 成功交付一份持有；Destroy 实例配对归还（spawn 计数 0→1→0）。
2. 两实例共享 prefab：销毁一个不影响另一个；计数 0→2→1→0。
3. 同 key A/B/C：冻结 YooAssetDriver 保持加载中；B 取消返回 null 且不清除 A 标记；A、C 各一份 spawn。
4. owner 加载中取消：标记清除、无持有遗留；同 key 重新加载可成功（handle 已释放后重试）。
5. 预取消 token：不注册标记、不触碰缓存，返回 null。
6. 缓存命中后交付前取消：立即归还这一份 spawn。
7. 同步入口撞未完成异步：`LoadAsset`/`LoadGameObject` 均明确抛冲突异常，无双登记；异步完成后同步走缓存。
8. 不存在地址：返回 null、标记清除、可重试不卡死。
9. 缓存类型不符：不错误交付、不残留引用（spawn 归还），正确类型仍可领取。
10. 回调成功只通知一次；失败只通知一次且标记清除。
11. 业务成功回调抛错：不触发失败通知、已交付资源可由接收方归还（异常经 LogAssert 验证可见）。
12. 进度回调抛错：被观察、以 Error 记录、观察者停止（progressCalls==1）、加载继续完成。
13. 外部克隆不继承持有权：克隆销毁不归还原实例 spawn，原实例存活后正常归还。
14. AssetsReference 外来条目归还异常：记录 Error 且继续归还源 prefab（一条异常不跳过其余）。
15. PreloadRequestRunner + 真实模块：重复地址去重、成功后配对归还、条目可正常池回收。
16. PreloadRequestRunner（假模块 EditMode）：去重、配对归还、终态进度、代际隔离、离开后晚成功只归还资源、重入状态清零。
17. 新增材质绑定 EditMode：SpriteRenderer/MeshRenderer 的 `Ref<T>` 各自登记并归还一份材质持有。
18. 新增未激活对象 EditMode/PlayMode 覆盖：绑定入口重绑定清理复制记录，资源模块补偿扫描与绑定后取消不依赖 `OnDestroy`。

### 计数方式

断言基于 `ObjectInfo.SpawnCount` 绝对/增量与 `_assetLoadingList` 标记状态，不是"没有抛错"式断言。

## 四、已知限制与偏差

1. **失败后重试的边界**：YooAsset 2.3 的 provider 按 `LoadAssetAsync+assetGUID` 缓存，**失败 provider 会驻留 ProviderDic**，同 location 再次加载会加入死 provider（重复失败）。本批保证：标记清除、handle 释放、调用可干净地重试并在 provider 层恢复后成功；但"YooAsset 层失败后立即成功"依赖 provider 销毁时机（`UnloadUnusedAssetsAsync` 触发），属于 YooAsset 内部设计，不在本批范围（改 YooAsset 被禁止）。测试覆盖的失败场景（地址不存在、类型不符、取消失败后重试）均验证通过。
2. **同 target 多别名登记**：池的 `_objectMap` 以 target 为键，同一资产通过"地址"与"资源路径"两种 location 加载时第二次 `Register` 会抛（既有架构约束）。本批类型预检消除了"错误 cast 后仍占引用"的变体；别名双登记未修改（需改池结构，超范围）。
3. **真实包模式未跑**：PRELOAD 在 EditorSimulate 被跳过，验证通过"抽出的 PreloadRequestRunner + 可控回调（假模块）+ 真实模块单资源预热"完成；未跑真实包/真机。
4. **FairyGUI PlayMode 用例失败（非本批引入的证据）**：`FguiWindowModuleTests.WindowModule_MergesConcurrentShow_AndClosePreventsLateWindow` 报 `OperationCanceledException`。该用例调用图（FguiModule 自身取消/超时机制 + 本地 AssetDatabaseProvider）与本批修改文件零交集；FGUI 资产桥接（FguiResourceProvider）只读未改。建议 master 复核确认存量属性。
5. 未运行：全量游戏流程、真机全平台、性能测试；ResourceModule.Shutdown/池 isShutdown 全局收尾（按契约留给第 5 批）。
6. TEngine.Runtime 未引用 `UniTask.YooAsset.dll`（存量事实）。若未来加入该引用，核心已兼容两种适配器行为（异常呈现/null 呈现），无需再改。
