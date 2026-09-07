# 证据登记

证据路径相对于项目根目录。OLD 没有可用本地 VCS 元数据，因此行号配合 verification.md 中的 SHA-256 使用。证据性质：定义、调用、配置；状态：已确认、推断、未验证。

## 环境、配置和程序集

### S02-E001

- 项目：OLD / CURRENT
- 路径：ProjectSettings/ProjectVersion.txt
- 类型：配置
- 证据性质：配置
- 行号：1-2
- 结论：两个工程均为 Unity 2022.3.62f2，revision 7670c08855a9。
- 状态：已确认。

### S02-E002

- 项目：OLD
- 路径：Assets/Resources/framework_cfg.asset
- 类型/字段：FrameworkConfig.UseYooAsset、FrameworkConfig.EnableAssetBundle
- 证据性质：配置
- 行号：51-52
- 结论：当前序列化配置为 UseYooAsset=0、EnableAssetBundle=0；这只代表该资产内容，不排除构建前覆盖。
- 状态：已确认；构建覆盖未验证。

### S02-E003

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/FrameworkConfig.cs
- 类型/方法：FrameworkConfig.Inst、Init
- 证据性质：定义、调用
- 行号：33-68、87-90、102-110
- 结论：运行时默认从 Resources/framework_cfg 加载；非编辑器分支把 EnableAssetBundle 设为 true；UseYooAsset 没有在该 Init 中被强制打开。
- 状态：已确认。

### S02-E004

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/Load/LoadMgr.cs
- 类型/方法：Initialize、InitializeYooAsset、LoadAssetInner、LoadAssetWithYooAsset
- 证据性质：调用
- 行号：195-250、837-905
- 结论：通用资源初始化和加载按 UseYooAsset/EnableAssetBundle/文件三分支选择；YooAsset 是可选业务后端。
- 状态：已确认。

### S02-E005

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/Load/LoadMgr.cs
- 类型/方法：LoadAssetWithYooAssetAsync、ConvertToYooAssetPath、UnloadYooAsset、UnloadAsset
- 证据性质：调用
- 行号：911-1059、1288-1303
- 结论：YooAsset 资源路径会移除常见扩展名并补 Assets/GameRes；释放通过 YooAssetLoadService.ReleaseAssetRef。
- 状态：已确认。

### S02-E006

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/YooAssetMgr.cs
- 类型/方法：InitializeAsync、InitPackage
- 证据性质：定义、调用
- 行号：308-379、405-415
- 结论：YooAssetMgr 创建 DefaultPackage 和 RawPackage，分别初始化包，并把服务绑定到包。
- 状态：已确认。

### S02-E007

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/YooAssetMgr.cs
- 类型/方法：BeforeDispose、UnloadAllAssetsAsync、OnDispose
- 证据性质：定义、调用
- 行号：223-300
- 结论：服务先 Dispose，再异步发起两包 UnloadAllAssetsAsync，最后 YooAssets.Destroy；卸载任务没有在 BeforeDispose 中等待完成。
- 状态：已确认；是否与 YooAsset 全局销毁安全协同未验证。

### S02-E008

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetLoadService.cs
- 类型/方法：LoadAssetSync、LoadAssetAsync
- 证据性质：定义、调用
- 行号：70-169
- 结论：普通同步/异步资源固定从 _defaultPackage 加载，成功后 RecordAssetRef；异步 API 无 CancellationToken，异常返回 null。
- 状态：已确认。

### S02-E009

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetLoadService.cs
- 类型/方法：AcquireAssetAsync、AcquireAssetSync、LoadSceneAsync、AcquireScene
- 证据性质：定义、调用
- 行号：171-356
- 结论：Acquire 句柄分别写入 m_assetHandles/m_sceneHandles；场景成功后写入 m_sceneHandles 并 RecordSceneRef。
- 状态：已确认。

### S02-E010

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetLoadService.cs
- 类型/方法：ReleaseAssetRef、ReleaseSceneRef、CleanupUnusedAssets、RecordAssetRef、PreloadAssetAsync
- 证据性质：定义、调用
- 行号：358-535
- 结论：普通 asset 的两种表有不同释放优先级；scene handle 首次释放不等待 scene ref 归零；CleanupUnusedAssets 只清表；Preload 成功后立即 Release。
- 状态：已确认；混用 API 的实际调用组合未验证。

### S02-E011

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/YooAssetUnifiedMgr.cs
- 类型/方法：BeforeDispose、LoadPrefabAsync、InstantiatePrefabAsync/InstantiatePrefab
- 证据性质：定义、调用
- 行号：164-194、205-255、257-311
- 结论：PrefabInfo 有 Count/Prefab/Handle，但 LoadPrefabAsync 将 Handle 置 null 并声明由 LoadService 管理；实例化使用 Unity Object.InstantiateAsync/Instantiate。
- 状态：已确认；外部业务可达性未验证。

### S02-E012

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Extensions/YooAssetExtensions.cs
- 类型/方法：GetPooledObjectAsync、Transform.GetPooledObjectAsync
- 证据性质：调用
- 行号：67-79、125-136
- 结论：扩展路径先 LoadAssetAsync<GameObject>，再直接 Object.Instantiate，没有显示的资源释放或实例引用组件绑定。
- 状态：已确认；外部业务可达性未验证。

### S02-E013

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceMgr.cs
- 类型/方法：LoadGameObjectAsync、LoadGameObject、m_loads/m_waits
- 证据性质：定义、调用
- 行号：21-29、85-142、356-407、517-546
- 结论：通用 GameObject 入口是 ResourceMgr；同路径加载会进入 m_loads/m_waits 合并等待，Prefab 命中池时可同步返回。
- 状态：已确认。

### S02-E014

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceMgr.cs；Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceRef.cs；Assets/Scripts/framework/Library/ZeroFramework/Resource/GameObjectRes.cs
- 类型/方法：ReleaseGameObject、GetPrefabInfo、EnsureResourceRef、PopGameObjectAsync、ResourceRef.OnDestroy、GameObjectRes.OnDispose
- 证据性质：定义、调用
- 行号：ResourceMgr.cs 619-685、727-929、969-1047；ResourceRef.cs 42-48；GameObjectRes.cs 309-348
- 结论：实例带 ResourceRef.Key；显式释放或 OnDestroy 减 Count；GameObjectRes Dispose 可 CancelLoad；异步实例化在 await 前临时占用 Count 并处理失效 Prefab。
- 状态：已确认。

### S02-E015

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceMgr.cs
- 类型/方法：InternalUnloadPrefabInfo、UnloadUnusedAssets、OnDispose
- 证据性质：定义、调用
- 行号：322-346、1073-1183
- 结论：实例 Count 归零先进入 idle candidate；空闲时间到达后调用 LoadMgr.UnloadAsset；清理和 OnDispose 会销毁池对象并释放空闲 Prefab。
- 状态：已确认。

### S02-E016

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/Load/AssetBundleLoad.cs
- 类型/方法：LoadAsset、UnloadAsset、LoadScene、UnloadScene
- 证据性质：定义、调用
- 行号：456-549
- 结论：AssetBundle 分支按 mapping 找 bundle；UnloadAsset 通过 mapping 找 bundle 并释放依赖；场景加载/卸载也以 bundle 映射为边界。
- 状态：已确认。

### S02-E017

- 项目：OLD
- 路径：Assets/Scripts/Launcher/GameLauncher.cs；Assets/Scripts/framework/Library/ZeroFramework/YooAsset/YooAssetMgr.cs
- 类型/方法：GameLauncher.Start、InitializeYooAsset、LoadAOTMetadataAssemblies、LoadLauncherDLL、LoadSceneAsync
- 证据性质：调用
- 行号：GameLauncher.cs 52-61、105-139、223-285、301-410；YooAssetMgr.cs 638-648、674-696
- 结论：AOT 启动器无条件初始化 YooAsset；RawFile/AssetHandle 读取启动资源后显式 Release/ReleaseAssetRef；这是启动控制面。
- 状态：已确认。

## OLD 场景与调用

### S02-E018

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/Scene/SceneMgr.cs
- 类型/方法：SceneLoadedHandler、UnloadSceneHandler、Load
- 证据性质：定义、调用
- 行号：106-151、222-271
- 结论：SceneMgr 在加载前对旧 GScene 调用 UnloadBefore，调用 LoadMgr.LoadSceneAsync；Unity sceneLoaded/sceneUnloaded 再驱动 GScene.Loaded/Unloaded 和 ModuleMgr 进入/离开地图。
- 状态：已确认。

### S02-E019

- 项目：OLD
- 路径：Assets/Scripts/game/Module/WorldMap/WorldMapModule_API.cs；Assets/Scripts/game/Module/WorldMap/WorldScene.cs
- 类型/方法：EnterWorld、WorldScene.OnLoaded、OnUnloadBefore
- 证据性质：调用
- 行号：WorldMapModule_API.cs 194-210；WorldScene.cs 50-104
- 结论：WorldMap 是已找到的真实场景调用者；WorldScene 的业务清理包含地图上下文、配置和 GC，但不等于资源句柄释放。
- 状态：已确认。

### S02-E020

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/Load/LoadMgr.cs
- 类型/方法：LoadSceneAsync、UnloadScene
- 证据性质：定义、调用
- 行号：1305-1349
- 结论：场景加载按 UseYooAsset/EnableAssetBundle/文件分支；YooAsset 分支调用 YooAssetMgr.LoadSceneAsync，但 UnloadScene 没有调用 ReleaseSceneRef。
- 状态：已确认；底层 YooAsset Single 行为未验证。

## CURRENT 资源与场景

### S02-E021

- 项目：OLD
- 路径：Assets/SubSystem/ResLoadCtrl/Scripts/ResLoadCtrl.cs
- 类型/方法：Awake、Update、Dispose 相关
- 证据性质：定义、调用
- 行号：31-64、67-201
- 结论：ResLoadCtrl 是业务侧序列化预加载器，按组串行推进并在 OnDestroy Dispose GameObjectRes；不是 OLD 核心资源服务。
- 状态：已确认。

### S02-E022

- 项目：CURRENT
- 路径：Packages/packages-lock.json
- 类型/配置：com.cysharp.unitask、com.tuyoogame.yooasset
- 证据性质：配置
- 行号：26-40
- 结论：UniTask 和 YooAsset 以 embedded 包接入当前工程；manifest.json 没有同名 registry 依赖行。
- 状态：已确认。

### S02-E023

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs
- 类型/方法：Initialize、InitPackage
- 证据性质：定义、调用
- 行号：17-53、97-138、140-260
- 结论：ResourceModule 初始化 YooAssets、默认包、Object Pool，并按 packageName/PlayMode 初始化 named package；AutoUnloadBundleWhenUnused 传入各模式参数。
- 状态：已确认。

### S02-E024

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs；Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs
- 类型/方法：GetCacheKey、LoadAsset、LoadGameObject、AssetsReference.Instantiate
- 证据性质：定义、调用
- 行号：ResourceModule.cs 681-759；AssetsReference.cs 160-174
- 结论：缓存 key 含 packageName；普通资源用 AssetObject；GameObject 创建通过 AssetsReference.Instantiate 记录 Prefab owner。
- 状态：已确认。

### S02-E025

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs
- 类型/方法：LoadAsset<T> callback、LoadAssetAsync<T>、LoadGameObjectAsync
- 证据性质：定义、调用
- 行号：769-921
- 结论：回调和 Task 资源加载都注册 AssetObject；Task/GameObject Task 取消时 Dispose handle；GameObject 返回带 AssetsReference 的实例。
- 状态：已确认。

### S02-E026

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.AssetObject.cs；ResourceModule.Pool.cs；ObjectPoolModule.ObjectPool.cs
- 类型/方法：AssetObject.Create/Release、UnloadAsset、Register/Spawn/Unspawn
- 证据性质：定义、调用
- 行号：AssetObject.cs 16-62；Pool.cs 43-66；ObjectPoolModule.ObjectPool.cs 143-163、255-277
- 结论：AssetObject 保存 HandleBase；普通 UnloadAsset 通过对象池 Unspawn，最终 Release 时 Dispose handle；同一对象支持多次 Spawn。
- 状态：已确认。

### S02-E027

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs
- 类型/方法：CheckRelease、OnDestroy、Instantiate
- 证据性质：定义、调用
- 行号：61-120、160-174
- 结论：实例销毁时释放 sourceGameObject 和附加资产；Instantiate 将 source Prefab 绑定到克隆实例。
- 状态：已确认。

### S02-E028

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs
- 类型/方法：SetAssetByResources、ReplaceLoadingState、IsCurrentRequest、OnDestroy
- 证据性质：定义、调用
- 行号：40-153、156-196
- 结论：新请求取消旧请求；await 后检查目标和请求代数；未成功转交的已加载资源在 finally UnloadAsset；组件销毁取消未完成请求。
- 状态：已确认。

### S02-E029

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs
- 类型/方法：OnInit、Shutdown、LoadSceneAsync、LoadScene
- 证据性质：定义、调用
- 行号：25-29、31-47、58-128、140-200
- 结论：主场景和 additive 场景分别由 _currentMainScene/_subScenes 持有；Task 主场景 await 后清理，回调主场景启动后即清理；主场景未显式释放旧句柄。
- 状态：前半已确认；旧句柄最终释放和清理时序运行时未验证。

### S02-E030

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs
- 类型/方法：UnloadAsync、Unload
- 证据性质：定义、调用
- 行号：312-353、361-395
- 结论：Task 卸载等待并移除子场景；回调卸载连续调用两次 UnloadAsync 后才挂 Completed。
- 状态：已确认静态代码事实。

### S02-E031

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/AudioModule/AudioAgent.cs；AudioData.cs；AudioModule.cs
- 类型/方法：AudioAgent.Load、OnAssetLoadComplete、AudioData.RecycleToPool、RemoveClipFromPool/CleanSoundPool
- 证据性质：定义、调用
- 行号：AudioAgent.cs 241-375；AudioData.cs 24-64；AudioModule.cs 495-553
- 结论：非池化句柄在 pending/AudioData 回收时 Dispose；池化句柄由音频池移除/清空时 Dispose。
- 状态：已确认。

### S02-E032

- 项目：CURRENT
- 路径：Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs
- 类型/方法：LoadAssetSuccess、LoadMetadataAssetSuccess
- 证据性质：调用
- 行号：181-218、256-292
- 结论：程序集/metadata TextAsset 使用完成后显式 ResourceModule.UnloadAsset，构成完整临时资源链。
- 状态：已确认。

### S02-E033

- 项目：CURRENT
- 路径：Assets/GameScripts/Procedure/ProcedurePreload.cs
- 类型/方法：LoadAllConfig、PreLoad、OnPreLoadAssetSuccess
- 证据性质：调用
- 行号：126-168
- 结论：PRELOAD 标签资源通过回调加载并只更新完成标记；调用者没有显式 UnloadAsset，保持策略由 Asset Pool 配置决定。
- 状态：已确认调用事实；意图和最终保留时长未验证。

### S02-E034

- 项目：CURRENT
- 路径：Assets/GameScripts/HotFix/GameLogic/GameModule.cs；Assets/TEngine/Runtime/Module/SceneModule/ISceneModule.cs
- 类型/方法：GameModule.Resource/GameModule.Scene、ISceneModule API
- 证据性质：定义、调用
- 行号：GameModule.cs 49-72；ISceneModule.cs 7-85
- 结论：当前工程公开资源/场景模块包装器和接口；本次检索的 Procedure/HotFix 目录未找到实际 SceneModule.Load/Unload 调用。
- 状态：接口定义已确认；“没有更多调用者”仅限本次检索范围。

### S02-E035

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs
- 类型/方法：UnloadUnusedAssets、ForceUnloadAllAssets、ForceUnloadUnusedAssets
- 证据性质：定义、调用
- 行号：409-447
- 结论：清理函数调用各包异步卸载但不 await；ForceUnloadUnusedAssets 交给外部 action。
- 状态：已确认。

### S02-E036

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs
- 类型/方法：TryWaitingLoading
- 证据性质：定义、调用
- 行号：1197-1221
- 结论：同 key 正在加载时等待；编辑器使用 60 秒 TimeoutController，非编辑器不带该超时。
- 状态：已确认。

### S02-E037

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs；Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.cs；Assets/TEngine/Runtime/Module/ObjectPoolModule/ObjectPoolModule.Object.cs
- 类型/方法：Priority、Shutdown、Object.Release
- 证据性质：定义、调用
- 行号：ResourceModule.cs 41-53；ObjectPoolModule.cs 19-70；ObjectPoolModule.Object.cs 178-186
- 结论：ResourceModule Priority=4，ObjectPoolModule Priority=6；对象池关闭使用 isShutdown=true，随后 AssetObject.Release 会跳过 handle Dispose。
- 状态：静态事实已确认；整体关闭顺序和是否另有全局销毁未验证。

## 验证边界

### S02-E038

- 项目：OLD / CURRENT
- 路径：本目录 verification.md 所列命令与扫描范围
- 类型：调用审计/验证
- 证据性质：调用、验证
- 行号：见 verification.md
- 结论：未运行 Unity 编译、导入、构建、脚本、Play Mode；OLD YooAssetUnifiedMgr 外部调用、OLD ReleaseSceneRef 调用、CURRENT SceneModule 业务调用只在指定目录做了静态搜索。
- 状态：已确认本次操作边界；运行时结论未验证。

## 补充证据

### S02-E039

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs；Packages/UniTask/Runtime/External/YooAsset/OperationHandleBaseExtensions.cs
- 类型/方法：LoadAssetAsync<T>、LoadGameObjectAsync、HandleBaseExtensions.ToUniTask、BaseContinuation
- 证据性质：定义、调用
- 行号：ResourceModule.cs 823-921；OperationHandleBaseExtensions.cs 19-31、144-161
- 结论：embedded UniTask 的 YooAsset ToUniTask(HandleBase) 签名没有 cancellationToken 参数；YooAsset 失败状态由适配器转成异常。CURRENT Task 资源路径只有 cancelOrFailed 分支做 marker/handle 清理，没有覆盖异常的 finally，因此“失败时也会 Dispose 并返回 null”不能作为已确认事实；ResourceModule.cs 855 的命名参数还需要编译确认。
- 状态：静态源码事实已确认；Unity 编译和实际异常路径未验证。

### S02-E040

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetLoadService.cs
- 类型/方法：LoadAssetSync、LoadAssetAsync、RecordAssetRef
- 证据性质：定义、调用
- 行号：73-168、489-509
- 结论：同步/异步普通加载在资源对象为空或异常时直接返回 null，没有显式释放局部句柄；每次普通加载都会向 _defaultPackage 请求句柄，但同一 location 已存在记录时 RecordAssetRef 只递增 RefCount、继续保留首个 AssetHandle，新增句柄的归属需要第三方包语义或运行时确认。
- 状态：未释放代码路径和记账行为已确认；新增句柄是否实际形成独立 provider 引用未验证。

### S02-E041

- 项目：CURRENT
- 路径：Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs
- 类型/方法：LoadAsset、LoadGameObject、LoadAsset<T> callback
- 证据性质：定义、调用
- 行号：692-821
- 结论：同步资源/Prefab 路径在注册 AssetObject 或实例化前没有检查 handle.Status；回调加载失败时只回调 null 并移除 loading marker，没有显式 Dispose 失败句柄。两者都是静态清理缺口，不能由成功路径证据推导为完整失败处理。
- 状态：静态代码事实已确认；实际失败句柄的 provider 回收效果未验证。

### S02-E042

- 项目：OLD
- 路径：Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetCacheService.cs
- 类型/方法：对象池、弱引用映射、CleanupAllPools、Dispose
- 证据性质：定义、调用
- 行号：42-69、118-218、290-378、479-497
- 结论：YooAssetCacheService 管理 GameObject 对象池、对象 ID 到 PoolInfo/WeakReference 的映射、池清理和自身容器清空；该文件没有 AssetHandle 持有或释放逻辑，因此 Prefab/句柄 owner 仍需由 UnifiedMgr、LoadService 或 LoadMgr 侧分别确认。
- 状态：已确认。
