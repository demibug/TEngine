# 资源与场景生命周期调查结论

## 1. 阅读结论

### 1.1 OLD 有两个资源控制面

旧工程的 AOT GameLauncher 在 Start 中直接调用 InitializeYooAsset；该方法初始化 YooAssetMgr、启动 Launcher 标签下载，并继续加载热更新相关的 RawFile/Asset 资源。[S02-E017] 这条链是启动控制面，不能等同于所有业务资源都通过 YooAsset。

通用业务加载由 LoadMgr.Initialize 分支选择：

    FrameworkConfig.UseYooAsset
        ├─ true  → InitializeYooAsset → YooAssetMgr/YooAssetLoadService
        ├─ false 且 EnableAssetBundle → AssetBundleLoad
        └─ 两者均 false → 文件/Resources 兼容链

该分支在 LoadAssetInner 和 LoadSceneAsync 中都存在。[S02-E004][S02-E005][S02-E020] OLD 的 Assets/Resources/framework_cfg.asset 当前写入 UseYooAsset=0、EnableAssetBundle=0；但 FrameworkConfig.Init 在非编辑器下把 EnableAssetBundle 强制置为 true。[S02-E002][S02-E003] 因此可以确认：

- 编辑器按当前资源值倾向于走非 AssetBundle 的兼容路径；
- 非编辑器默认倾向于走 AssetBundle 路径；
- YooAsset 通用业务路径是可配置分支，不是由本次配置直接证明的默认业务路径；
- YooAsset 仍然是启动器的实际控制面。

这里的“倾向”仍不替代真实构建参数；构建脚本或外部替换配置可能改变最终值。[S02-E002][S02-E003][S02-E038]

### 1.2 当前工程是显式 YooAsset 资源模块

CURRENT 的 TEngine.Runtime 程序集包含 ResourceModule、SceneModule；资源模块初始化 YooAssets、创建/设置默认包，并用 PackageMap 管理命名资源包。[S02-E023][S02-E034] 当前 packages-lock.json 把 UniTask 和 YooAsset 作为 embedded 包接入。[S02-E022] 资源 API 的 packageName 会参与缓存 key，非默认包使用 packageName/location 组合键。[S02-E024]

CURRENT 的 GameLogic 只把 IResourceModule、ISceneModule 暴露为 GameModule.Resource/GameModule.Scene 包装器；本次在 Assets/GameScripts/Procedure 与 HotFix 目录中未发现实际 SceneModule 加载调用，只有接口/包装器定义，故当前场景 API 的业务可达性标为未证实。[S02-E034][S02-E038]

## 2. 所有权与类型边界

| 层 | OLD | CURRENT | 性质 |
| --- | --- | --- | --- |
| 底层资源包 | Unity AssetBundle/文件链；可选 YooAsset DefaultPackage/RawPackage [S02-E004][S02-E006][S02-E016] | YooAsset ResourcePackage 与 PackageMap [S02-E023] | 第三方运行时 + 自有适配 |
| 业务资源入口 | LoadMgr；GameObject 进一步经 ResourceMgr [S02-E004][S02-E013] | IResourceModule/ResourceModule [S02-E023][S02-E034] | 自有适配层 |
| 普通资源 owner | YooAssetLoadService 的 m_assetRefs；或 AssetBundleLoad 的 bundle 缓存 [S02-E008][S02-E010][S02-E016] | AssetObject 对象池中的 HandleBase [S02-E025][S02-E026] | 自有引用层 |
| GameObject 实例 owner | ResourceRef.Key + ResourceMgr.PrefabInfo.Count；直接 YooAsset 实例化路径没有同等追踪 [S02-E012][S02-E014] | 实例上的 AssetsReference.sourceGameObject [S02-E024][S02-E027] | 自有实例边界 |
| 句柄 API | AcquireAsset/AcquireScene 返回给调用者；RawFileHandle 由调用者 Release [S02-E009][S02-E017] | LoadAsset*Handle 返回给调用者，由调用者 Dispose [S02-E025][S02-E026] | 调用者责任 |
| 场景 owner | SceneMgr/GScene 负责业务状态；YooAssetLoadService 另有 scene handle/ref 表 [S02-E009][S02-E018][S02-E020] | SceneModule 的主场景句柄和 additive 字典 [S02-E029][S02-E030] | 状态层 + 句柄层 |
| 预加载 | YooAsset PreloadAssetAsync 成功后立即 Release；ResourceMgr 预热保留 PrefabInfo [S02-E010][S02-E013] | ProcedurePreload 调用资源回调 API，成功后只记完成标记 [S02-E033] | 需区分预热与长期持有 |

YooAsset、UniTask、Unity Scene/AssetBundle API 属于第三方；YooAssetMgr、YooAssetLoadService、LoadMgr、ResourceMgr、SceneMgr、ResourceModule、SceneModule 是自有适配/框架；ProcedurePreload、ProcedureLoadAssembly 和 WorldMap 调用是业务或启动侧调用者。[S02-E011][S02-E013][S02-E023][S02-E034] 本次没有把生成代码当作核心生命周期证据。

## 3. OLD 资源生命周期

### 3.1 实际通用 GameObject 链

旧工程最完整、最像业务主路径的实例链是：

    业务调用
      ↓
    ResourceMgr.LoadGameObjectAsync/LoadGameObject
      ↓
    LoadMgr.LoadAssetAsync(path, GameObject)
      ↓
    LoadMgr 根据 FrameworkConfig 选择 AssetBundle / YooAsset / 文件
      ↓
    ResourceMgr.GetPrefabInfo + PopGameObject
      ↓
    EnsureResourceRef(instance, path)
      ↓
    显式 ReleaseGameObject 或 ResourceRef.OnDestroy
      ↓
    PrefabInfo.Count--，对象回池或 Destroy
      ↓
    空闲回收 InternalUnloadPrefabInfo
      ↓
    LoadMgr.UnloadAsset(path) → 对应底层 Unload

ResourceMgr 对同一路径维护 m_loads/m_waits，未加载时只发起一次底层加载，并把多个等待项统一派发；从池中命中时可同步返回。[S02-E013] 实例化前后都会确保 ResourceRef 存在并写入 Key。[S02-E014] 显式释放会减 PrefabInfo.Count；普通释放回到缓存根节点和对象池，超容量才 Destroy；从 OnDestroy 进入时以 disDestroy=true 避免再次 Destroy。[S02-E014]

ResourceRef.OnDestroy 调用 ResourceMgr.ReleaseGameObject(gameObject, true)。这意味着“Unity 对象被销毁”和“底层 Prefab 资源立即释放”不是同一时刻：先减实例计数，只有 Count 归零且经过空闲检测，才由 InternalUnloadPrefabInfo 调用 LoadMgr.UnloadAsset。[S02-E014][S02-E015] 这条设计允许池化，但也要求所有实例都带有有效 ResourceRef.Key。

异步实例化还有一层竞态保护：PopGameObjectAsync 在 await Object.InstantiateAsync 前暂时增加 Count，避免空闲回收在挂起窗口清掉 Prefab；若 await 期间 Prefab 已失效，会销毁实例化结果、清理失效记录并回调 null。[S02-E014] 这是 OLD 中对“资源先卸载、回调后到达”的少数明确防护。

### 3.2 OLD YooAsset 普通资源链

当 UseYooAsset 分支实际启用时，LoadMgr 将业务路径去扩展名，并为 GameRes 路径补上 Assets/GameRes/ 前缀，再转调 YooAssetMgr.LoadAssetAsync/LoadAssetSync。[S02-E005] YooAssetMgr 的 InitializeAsync 创建 DefaultPackage 和 RawPackage，给两个包分别初始化参数；普通 LoadService 只持有 _defaultPackage。[S02-E006][S02-E008]

一条可追踪的普通资源链如下：

    LoadMgr.LoadAssetAsync
      ↓
    YooAssetMgr.LoadAssetAsync
      ↓
    YooAssetLoadService.LoadAssetAsync
      ↓
    DefaultPackage.LoadAssetAsync → await handle.ToUniTask()
      ↓
    RecordAssetRef(location, handle)
      ↓
    业务使用 asset
      ↓
    LoadMgr.UnloadAsset(path)
      ↓
    YooAssetLoadService.ReleaseAssetRef(location)
      ↓
    RefCount 归零时 handle.Release()

普通 LoadAssetSync/Async 成功后都会 RecordAssetRef；同一 location 的再次加载会再次向 _defaultPackage 请求句柄，但 RecordAssetRef 只增加 RefCount 并继续保留最初保存的 AssetHandle。[S02-E008][S02-E010][S02-E040] 该链要求调用者按加载次数配对释放，或者在更高层用 ResourceMgr 的 PrefabInfo/ResourceRef 做实例计数；新增句柄是否形成独立 provider 引用及其最终归属，仍需第三方包语义或运行时验证。

### 3.3 OLD 句柄、缓存和预加载

AcquireAssetAsync/Sync 是另一种 owner 语义：它把句柄写入 m_assetHandles[location]，由 ReleaseAssetRef 直接 Release；同一 location 再次 Acquire 会覆盖字典值而不是形成独立 token。[S02-E009] ReleaseAssetRef 先检查 m_assetHandles，再检查 m_assetRefs，并且源码自己留下“要么这里，要么下面”的注释；如果两种 API 混用，同一 location 的释放责任不清晰。[S02-E010]

PreloadAssetAsync 成功后立即 handle.Release，并明确注释“不持有引用”；它适合预热包/缓存，不是向调用者转移一个长期 owner。[S02-E010] YooAssetCacheService 主要管理 GameObject 对象池、空闲对象和弱引用映射，不保存 AssetHandle 的完整所有权；Prefab/句柄清理仍由 UnifiedMgr/LoadService/LoadMgr 分别负责。[S02-E011][S02-E042]

YooAssetUnifiedMgr 的 LoadPrefabAsync 以 path 缓存 PrefabInfo、递增 Count，再用 Object.InstantiateAsync/Instantiate 创建实例；但 PrefabInfo.Handle 被写成 null，并注释为“由 LoadService 管理”。[S02-E011] 因而它的 Count 与 LoadService 的 asset ref 并没有在同一结构中闭合。BeforeDispose 只释放非空 PrefabInfo.Handle，不能证明它会释放这条 LoadService 产生的引用。[S02-E011]

此外，YooAssetExtensions 的 GetPooledObjectAsync 直接调用 LoadAssetAsync<GameObject> 后再 Object.Instantiate，没有在实例上建立 ResourceRef/AssetsReference，也没有在该路径显示 UnloadAsset。[S02-E012] 这些是定义级风险；本次在已检索的 game/HotUpdate 目录中没有找到 YooAssetMgr.Inst.UnifiedMgr 的外部调用，因此该风险的生产可达性仍未验证。[S02-E011][S02-E012][S02-E038]

### 3.4 RawPackage 与启动器

YooAssetMgr 把 RawFileHandle 从 RawPackage 返回给调用者。GameLauncher 使用 RawFileHandle 读取热更新配置、AOT metadata 和 Launcher.dll；完成读取后显式调用 handle.Release。Bundle 版本的 TextAsset 路径则用 ReleaseAssetRef。[S02-E017] 这条链证明 RawPackage 是启动/热更新资源边界，不代表普通业务资源默认都在 RawPackage。

### 3.5 失败、取消和重复请求

- YooAssetLoadService.LoadAssetSync/Async 没有 CancellationToken 参数；异常、资源对象为空和句柄失败分支最终返回 null，但源码没有对局部句柄统一执行 Release/Dispose。[S02-E008][S02-E040]
- YooAssetMgr.LoadSceneAsync 在 LoadService 返回 null 的失败路径上仍直接访问 result.Status，再调用回调；这是静态的 null 风险，未运行验证。[S02-E017]
- ResourceMgr 的 GameObject 等待队列能合并同路径加载；GameObjectRes.Dispose 会调用 ResourceMgr.CancelLoad，取消的是等待/实例请求，不是已经开始的底层 YooAsset 操作。[S02-E013][S02-E014]
- LoadService 自身没有按 key 的 in-flight 合并；普通多次 LoadAssetAsync 会各自向 DefaultPackage 发起加载，再通过 m_assetRefs 记账，而重复 location 的新增句柄没有被记录为独立 owner。[S02-E008][S02-E010][S02-E040]
- ResourceMgr 的 PopGameObjectAsync 对异步实例化竞态有保护，但直接 YooAssetMgr/UnifiedMgr 的 plain Instantiate 路径没有同等组件级追踪。[S02-E011][S02-E012][S02-E014]

## 4. OLD 场景生命周期

### 4.1 真实场景切换链

已找到的真实业务入口是 WorldMapModule_API.EnterWorld：它检查 SceneMgr.GetSceneByPath(WorldPath)，未加载时调用 SceneMgr.Load(WorldPath, false, param)。[S02-E019]

完整链路为：

    WorldMapModule_API.EnterWorld
      ↓
    SceneMgr.Load(path, additive=false, param)
      ↓
    CalcScenePath
      ↓
    GScene.Enter；记录旧场景路径；对旧 GScene 调用 UnloadBefore
      ↓
    LoadMgr.LoadSceneAsync(path, scene.Loaded, additive)
      ↓
    根据 FrameworkConfig 选择 YooAsset / AssetBundle / 文件
      ↓
    Unity sceneLoaded
      ↓
    SceneMgr.SceneLoadedHandler → ModuleMgr.EnterMap
      ↓
    GScene.Loaded → OnLoaded / ISceneLaunch / OnLoad

Single 模式下，SceneMgr 自己做的是业务状态的 UnloadBefore 和旧路径登记，并没有在这段方法中显式调用 LoadMgr.UnloadScene；旧场景的实际 Unity 卸载依靠 Single 场景加载/Unity 事件。SceneUnloaded 到达后，SceneMgr 调用 ModuleMgr.LeaveMap、GScene.Unloaded 并从 m_scenes 移除。[S02-E018][S02-E019]

WorldScene.OnUnloadBefore 会清理地图效果、世界上下文、MapDao、ConfigMgr，并触发 GC.Collect；这属于业务场景清理，不是资源包句柄释放。[S02-E019] 两种责任必须分开看。

### 4.2 场景资源释放责任

LoadService.LoadSceneAsync 成功后把 SceneHandle 放到 m_sceneHandles，并调用 RecordSceneRef；AcquireScene 也写入同一张 handle 表。[S02-E009] 设计上应该由 ReleaseSceneRef(location) 解除 owner，但本次搜索到的主要业务链只有 SceneMgr.Load 和 LoadMgr.UnloadScene，没有找到它们调用 YooAssetMgr.ReleaseSceneRef 的证据。[S02-E010][S02-E020][S02-E038]

LoadMgr.UnloadScene 的 YooAsset 分支只在 additive 时调用 Unity SceneManager.UnloadSceneAsync；之后的底层 AssetBundle unload 仅受 EnableAssetBundle 控制。它没有在 UseYooAsset 分支调用 YooAssetMgr.ReleaseSceneRef。[S02-E020] 因此，旧工程 YooAsset 场景引用表和 SceneMgr 的 Unity 场景状态之间存在未闭合的静态边界。不能仅凭源码断言一定泄漏，因为 Single 场景切换和 YooAsset 内部可能还有释放语义；但必须把它列为迁移审查重点。

ReleaseSceneRef 本身也不是严格的共享引用实现：只要 m_sceneHandles 有值，第一次调用就先 Release 并移除 handle，然后才把 m_sceneRefs.RefCount 减一；后续调用只减 bookkeeping。[S02-E010] 这与普通 asset ref 的“计数归零才释放”不同。

## 5. CURRENT 资源生命周期

### 5.1 包初始化与定位

ResourceModule.Initialize 调用 YooAssets.Initialize，创建/获取 DefaultPackage、设置默认包并绑定 Asset Pool。InitPackage 通过 packageName、PlayMode、EditorSimulate/Offline/Host/Web 参数初始化对应 ResourcePackage，可选请求版本和更新 Manifest。[S02-E023] AutoUnloadBundleWhenUnused 被写入各模式参数，属于包层 bundle 生命周期策略，不等同于 AssetObject 引用释放。

ResourceModule.GetCacheKey 对默认包保留 location，对非默认包使用 packageName/location；这让同一路径在不同资源包中不会共享同一 AssetObject。[S02-E024] 当前 packages-lock.json 记录 YooAsset 和 UniTask 为 embedded 包，代码直接引用它们。[S02-E022]

### 5.2 普通资源和 GameObject

CURRENT 的普通资源路径是：

    IResourceModule.LoadAsset/LoadAssetAsync
      ↓
    CheckLocationValid + GetCacheKey
      ↓
    _assetPool.Spawn(key)，未命中才 GetHandleSync/Async
      ↓
    AssetObject.Create(target, handle)
      ↓
    _assetPool.Register(assetObject, true)
      ↓
    调用者使用 asset
      ↓
    IResourceModule.UnloadAsset(asset)
      ↓
    Asset Pool Unspawn；spawn count 归零/自动回收时 AssetObject.Release
      ↓
    HandleBase.Dispose

LoadAsset、回调 LoadAsset<T> 和 Task LoadAssetAsync<T> 都把成功句柄包进 AssetObject 并注册到多生成对象池。[S02-E024][S02-E025][S02-E026] 资源对象不是简单的静态字典缓存：同一 AssetObject 可以被多次 Spawn，Unspawn 只减少使用计数。

GameObject 路径额外做：

    ResourceModule.LoadGameObject*
      ↓
    AssetsReference.Instantiate(prefab, parent, ResourceModule)
      ↓
    返回带 AssetsReference 的实例
      ↓
    实例 OnDestroy
      ↓
    UnloadAsset(sourceGameObject)
      ↓
    AssetObject 的池计数减少

同步和异步的缓存命中、首次加载路径都使用 AssetsReference.Instantiate；接口注释也明确写明 GameObject Destroy 时自动 UnloadAsset。[S02-E024][S02-E025][S02-E027] 这解决了 OLD 直接 Object.Instantiate 路径里“实例销毁不知道 Prefab owner”的结构性问题，但前提是所有实例都从该 API 创建。

### 5.3 取消、重复与目标销毁

ResourceModule 用 _assetLoadingList 按 cache key 等待同一资源的正在加载请求；编辑器等待超过 60 秒会报超时，生产路径没有同样的编辑器超时。[S02-E036] Task 资源路径的取消分支会移除 loading marker、Dispose handle 并返回 null，但 YooAsset ToUniTask 在失败状态会抛异常，当前代码没有统一 finally 清理；而 LoadAssetAsync<T> 传入的 cancellationToken 命名参数与当前 embedded UniTask 适配器签名不一致，需先完成编译确认。[S02-E025][S02-E039]

ResourceExtComponent 为 SetSprite/SetAssetByResources 维护“目标对象 → LoadingState”：新请求会取消旧请求，await 后检查是不是当前请求，目标销毁时取消全部状态；如果资源已经加载但没有成功转交到 AssetItemObject，finally 会 UnloadAsset。[S02-E028] 这条边界覆盖了替换图片、销毁 UI 目标和晚到回调。

回调形式的 ResourceModule.LoadAsset<T> 没有 CancellationToken 参数；它依赖 loading key 和 callback，不具备 Task 形式同等的取消/错误释放路径，而且失败回调分支只回调 null、没有显式 Dispose 句柄。同步 LoadAsset/LoadGameObject 也没有在注册 AssetObject 或实例化前检查失败状态。[S02-E025][S02-E036][S02-E041] 直接句柄 API 则把生命周期交给调用者。

### 5.4 当前真实资源调用样本

- ProcedureLoadAssembly 通过 IResourceModule.LoadAsset<TextAsset> 加载 DLL/metadata，Assembly.Load 或 HybridCLR 处理完成后显式 UnloadAsset(textAsset)。这是清晰的“临时资源 owner”。[S02-E032]
- ProcedurePreload 根据 PRELOAD 标签调用回调式 LoadAssetAsync，成功回调只设置完成标记，没有调用 UnloadAsset；这更像把资源留在 Asset Pool 中等待自动回收/后续使用，但实际预加载保持时长取决于对象池配置，不能从调用者单独断言为泄漏。[S02-E033]
- AudioAgent 使用 AssetHandle；非池化音频在切换待播放请求时 Dispose，AudioData 回收时也会 Dispose；池化音频由 AudioModule.RemoveClipFromPool/CleanSoundPool 统一 Dispose。[S02-E031]
- UI 只作采样：UIWindow/UIBase 通过 IResourceModule.LoadGameObject* 创建 UI/Widget，因 GameObject API 自带 AssetsReference，UI 销毁后由资源引用组件释放；Resources.Load 是另一个业务分支，不代表 TEngine ResourceModule 的生命周期。[S02-E034][S02-E038]

### 5.5 当前模块关闭与回收

ResourceModule.Shutdown 为空；资源池通过 ObjectPoolModule 管理。ResourceModule Priority=4，ObjectPoolModule Priority=6，框架注释说明高优先级模块关闭较晚；ObjectPool 在 Shutdown 时以 isShutdown=true 释放内部对象，而 AssetObject.Release 在 isShutdown=true 时跳过 HandleBase.Dispose。[S02-E023][S02-E026][S02-E037] 这是静态可疑闭环：必须由整体 ModuleSystem 关闭顺序和 YooAsset 全局销毁策略确认是否有意设计，当前没有运行时证明。

UnloadUnusedAssets 会先 ReleaseAllUnused，再对每个包调用 UnloadUnusedAssetsAsync，但不 await；ForceUnloadAllAssets 同样只发起异步操作。[S02-E035] SceneModule 触发的 ForceUnloadUnusedAssets 也应与场景句柄状态一起评估，不能把它当作同步的“资源已释放”屏障。

## 6. CURRENT 场景生命周期

### 6.1 定义链

SceneModule.OnInit 清空主场景句柄并以 BuildIndex=0 的场景名作为当前主场景。[S02-E029] LoadSceneAsync 先将 location 加入 _handlingScene：

    GameModule.Scene / ISceneModule
      ↓
    SceneModule.LoadSceneAsync
      ├─ Additive → YooAssets.LoadSceneAsync → _subScenes[location] → await → 返回 SceneObject
      └─ Single   → YooAssets.LoadSceneAsync → _currentMainScene → await → ForceUnloadUnusedAssets

Additive 场景禁止同 location 重复加载，并由 _subScenes 保存句柄；UnloadAsync 会调用 SceneHandle.UnloadAsync、等待完成、移除字典和 handling 标记。[S02-E029][S02-E030] 主场景赋值新 SceneHandle 后依赖 YooAsset/Unity 的 Single 语义替换旧场景，源码没有显式调用旧主句柄 Unload；这点只能标记为“设计依赖，运行时未验证”，不能直接判定泄漏。[S02-E029]

### 6.2 发现的静态风险

- Shutdown 遍历 additive 句柄并调用 UnloadAsync，但不 await；主场景句柄没有显式 unload/reset。[S02-E030]
- 回调式主场景 LoadScene 在刚发起 handle 后就调用 ForceUnloadUnusedAssets，早于 Completed 回调；Task 版本则在 await 后调用。两种 API 的清理时序不一致。[S02-E029]
- 回调式 Unload 在同一 location 上连续调用两次 subScene.UnloadAsync，第二次才挂 Completed；这是明确的重复卸载调用。[S02-E030]
- Load/Unload 的异常路径没有统一 finally 清理 _handlingScene；如果 YooAsset 任务抛出异常，可能留下“仍在处理”的状态。该结论为静态推断，需运行时验证。[S02-E029][S02-E030]
- 在当前已检索的 GameScripts/Procedure、GameScripts/HotFix 中没有发现实际调用 SceneModule.LoadSceneAsync/UnloadAsync；因此上述 API 行为是框架定义结论，不是已证实的当前业务切换链。[S02-E034][S02-E038]

## 7. OLD 与 CURRENT 对照

| 关注点 | OLD | CURRENT |
| --- | --- | --- |
| 普通资源 owner | LoadService 的 location ref 表，或 AssetBundle cache；调用者必须按约定释放 | AssetObject + 多生成对象池；UnloadAsset 进入统一 pool |
| GameObject 销毁 | ResourceMgr 通过 ResourceRef.OnDestroy 归还实例；直接 YooAsset Instantiate 路径没有统一追踪 | AssetsReference.OnDestroy 自动 UnloadAsset |
| 同路径重复请求 | ResourceMgr 的 GameObject wait 队列会合并；YooAssetLoadService 自身不合并 | ResourceModule _assetLoadingList 等待；编辑器 60 秒超时 |
| 取消 | GameObjectRes 可取消等待；底层 YooAssetLoadService 无 token | Task 资源和 ResourceExt 有 token/旧请求取消；回调 API较弱 |
| 句柄 API | AssetHandle/SceneHandle/RawFileHandle 分散；Acquire 表与普通 ref 表分离 | 直接 Handle API 明确由调用者 Dispose |
| 包边界 | DefaultPackage + RawPackage；普通 LoadService 固定 default | DefaultPackage + PackageMap/packageName；当前没有旧工程同名 RawPackage 约定 |
| 场景 owner | SceneMgr 的业务状态与 YooAsset scene ref 表未闭合 | SceneModule 持有主/子场景句柄；关闭时序仍需强化 |
| 低内存清理 | YooAssetMgr/LoadMgr/ResourceMgr 多处清理，部分 fire-and-forget | ResourceModule/SceneModule 触发包级异步清理，不 await |

这些不是“旧代码全部迁移”的建议：旧工程包含两套历史资源链，迁移时应按 owner、实例生命周期和可达分支逐项选择。[S02-E002][S02-E014][S02-E024][S02-E029]

## 8. 静态审查优先级

1. 先确认 OLD 实际目标平台的 FrameworkConfig 覆盖和 UseYooAsset 是否在构建时改变；否则容易把启动器 YooAsset 与业务 AssetBundle 链混写。[S02-E002][S02-E003][S02-E017]
2. 继续确认所有 Prefab 实例化是否经过 ResourceMgr 或 CURRENT AssetsReference；直接 Instantiate 是最容易失去资源 owner 的边界。[S02-E012][S02-E014][S02-E027]
3. 以场景句柄为中心补做运行时测试：Single 替换、Additive 多次引用、失败/取消、应用退出和模块 Shutdown。[S02-E010][S02-E020][S02-E029][S02-E030]
4. 对对象池和资源包回收分别测量：AssetObject spawn count 归零、AutoRelease、UnloadUnusedAssetsAsync 完成时点，不要用 GC.Collect 或 Unity 场景事件替代句柄释放。[S02-E026][S02-E035][S02-E037]
