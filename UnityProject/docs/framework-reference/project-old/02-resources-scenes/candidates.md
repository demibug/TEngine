# 后续迁移/重构候选

本文件只提出候选，不在本任务实施。每个候选都必须回到 evidence.md 的证据；“保留”表示当前已经有相似机制，应优先验证和收敛，而不是重复造一套。

## S02-C001：统一 GameObject 实例的资源 owner 边界

- 优先级：P1
- 候选：所有从资源 Prefab 创建的实例都经过一个明确的 reference component；旧工程保留/借鉴 ResourceRef 的“实例计数 + 对象池”思想，但不要直接迁移其 plain Instantiate 路径。
- 依据：OLD ResourceMgr 会给实例挂 ResourceRef，OnDestroy 回到 ResourceMgr；OLD YooAssetUnifiedMgr/扩展却有直接 Object.Instantiate 路径；CURRENT 已由 AssetsReference 在 LoadGameObject* 边界建立 Prefab owner。[S02-E012][S02-E014][S02-E024][S02-E027]
- 预期收益：对象销毁、池化归还和底层句柄释放拥有单一可观测入口，降低“Prefab 仍被引用但实例已销毁”或“实例销毁后资源 owner 丢失”的风险。
- 成本/风险：需要盘点所有直接 Object.Instantiate(loadedPrefab) 调用；若第三方组件或业务自行克隆，需提供显式绑定 API。
- 验证条件：静态扫描所有资源加载后的 Instantiate；Play Mode 测试实例 Destroy、池 Return、场景切换和资源卸载。

## S02-C002：场景句柄与业务场景状态由同一 owner 协调

- 优先级：P1
- 候选：明确区分主场景 owner、Additive 场景 owner、业务 GScene/SceneObject 状态；每一次加载、卸载、取消、Shutdown 都有一次性 handle 释放和状态清理。
- 依据：OLD SceneMgr 依赖 Unity sceneLoaded/sceneUnloaded，YooAssetLoadService 另存 scene handle/ref；LoadMgr.UnloadScene 没有连接 ReleaseSceneRef。CURRENT SceneModule 已有 _currentMainScene、_subScenes、_handlingScene，但 callback 卸载重复调用、Shutdown 不 await。[S02-E010][S02-E018][S02-E020][S02-E029][S02-E030]
- 预期收益：Single 切换和 Additive 卸载的资源包释放可追踪；减少“场景已离开但句柄表仍存”以及异常后 handling 标记残留。
- 成本/风险：依赖 YooAsset SceneHandle 的真实 Single 语义和业务场景事件时序；需要处理已加载、加载中、失败和重复请求。
- 验证条件：至少覆盖主场景替换、两个 Additive 场景、重复加载、取消/失败、应用退出；记录 handle validity、包引用和 Unity 场景事件。

## S02-C003：按缓存 key 合并 in-flight 请求，并将取消绑定到 owner

- 优先级：P1
- 候选：保留 CURRENT ResourceModule 的 _assetLoadingList/等待和 ResourceExtComponent 的“目标对象 → LoadingState”模式；把回调 API 的取消、失败清理和迟到回调规则写入统一契约。
- 依据：OLD ResourceMgr 只在 GameObject 上做 wait 合并，YooAssetLoadService 普通资源没有 token/请求合并；CURRENT Task API 的取消分支会 Dispose handle，但失败异常、同步路径和回调失败路径仍缺少统一清理，ResourceExt 能取消旧请求并在未转交时 UnloadAsset。[S02-E008][S02-E013][S02-E025][S02-E028][S02-E036][S02-E039][S02-E041]
- 预期收益：减少重复下载/重复句柄，防止 UI/组件销毁后迟到回调重新绑定旧资源。
- 成本/风险：请求共享时要定义“一个调用者取消是否影响其他调用者”；需要区分共享加载操作和每个调用者的使用引用。
- 验证条件：同 key 并发 2/10 次、其中一方取消、目标销毁、加载失败、加载完成后立即替换 location。

## S02-C004：命名资源包/Raw 资源边界

- 优先级：P2
- 候选：保留 CURRENT packageName 参与 cache key 的能力；只有在实际存在视频、音频 bank、DLC 或热更原始文件边界时，才建立类似 OLD DefaultPackage/RawPackage 的明确包契约。
- 依据：OLD YooAssetConfig 定义 DefaultPackageName=DefaultPackage、RawPackageName=RawPackage，YooAssetMgr 普通资源固定 default、RawFile 固定 raw；CURRENT ResourceModule 使用 PackageMap 和 packageName，但未发现同名 RawPackage 约定。[S02-E006][S02-E017][S02-E023][S02-E024]
- 预期收益：避免把 RawFileHandle、普通 AssetHandle、场景句柄混在一个释放协议中。
- 成本/风险：额外包会带来初始化顺序、版本清单、缓存清理和平台文件系统差异；没有真实需求时会增加复杂度。
- 验证条件：明确每种资源的 package、初始化前置、下载/缓存策略、owner 和清理 API；不要只依据目录名称推断。

## S02-C005：只借鉴诊断，不复制旧引用计数实现

- 优先级：P2
- 候选：增加资源/场景句柄、cache key、spawn count、加载耗时和 unload 原因的可观测快照；不直接复制 OLD 的按 location 双表和 ReleaseSceneRef 算法。
- 依据：OLD LoadService 有 ref snapshot/statistics/diagnostics，但普通 ref 与 Acquire handle 分表，SceneHandle 释放顺序存在静态不一致；CURRENT AssetObject/SceneModule 缺少同等统一快照。[S02-E010][S02-E026][S02-E029]
- 预期收益：为迁移后的泄漏、迟到回调、对象池滞留和场景切换问题提供证据。
- 成本/风险：诊断字段必须标识 packageName 和 owner，否则只会生成另一份 location 计数。
- 验证条件：调试面板或日志能回答“谁加载、谁持有、何时 spawn/unspawn、何时 handle Dispose、哪个场景触发清理”。
