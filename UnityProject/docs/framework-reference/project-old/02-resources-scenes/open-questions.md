# 未决问题与验证建议

本文件刻意把源码无法单独证明的内容列为问题，不把推测写成既成事实。

## A. OLD 分支与可达性

1. OLD 的 framework_cfg.asset 当前 UseYooAsset=0，但构建脚本是否在特定渠道、平台或热更包中替换该资产，尚未追到。需要 06 的构建/打包调查确认；本次只读了运行时代码、Resources 配置和相关 asmdef。[S02-E002][S02-E003][S02-E038]
2. GameLauncher.Start 无条件初始化 YooAsset，但通用 LoadMgr 是否在目标包中也使用 YooAsset 取决于 FrameworkConfig。需要按 Editor、Standalone、Android/WebGL 目标各取一份实际配置和启动日志。[S02-E004][S02-E017]
3. YooAssetUnifiedMgr、YooAssetExtensions 的直接实例化风险已在定义上确认，但本次在 game/HotUpdate 的指定目录没有发现外部 UnifiedMgr 调用。需要扩大到全部业务程序集或运行时调用计数，确认该路径是否已弃用。[S02-E011][S02-E012][S02-E038]
4. OLD ReleaseSceneRef 在搜索范围内没有发现与 SceneMgr/LoadMgr 连接的调用。需要在完整旧工程程序集和场景切换日志中确认 scene handle 是否由其他入口释放。[S02-E010][S02-E020][S02-E038]
5. OLD YooAssetMgr.LoadSceneAsync 对 null result 访问 Status 的失败行为需要用一个无效 location 或失败清单复现；静态代码只能确认潜在 null 风险。[S02-E017]

## B. OLD 资源 owner

6. ResourceMgr 的主要 GameObject 链有 ResourceRef 和空闲延迟释放，但部分业务直接使用 LoadMgr.LoadAssetAsync<GameObject> 得到 Prefab 后自行 Instantiate；需要逐类确认这些调用者是否显式 UnloadAsset 或将 Prefab 长期缓存。[S02-E013][S02-E014]
7. YooAssetLoadService 的普通 LoadAssetRef 与 AcquireAssetHandle 可对同一 location 混用；需要确认业务约定是否禁止混用，否则 m_assetHandles 优先释放会绕过 m_assetRefs 的计数。[S02-E009][S02-E010]
8. UnifiedMgr 的 PrefabInfo.Handle 为 null，但 LoadService 管理的引用何时释放依赖其未展示的 idle/unload 调用链；需要运行时打开句柄诊断并观察 PrefabInfo、LoadService ref snapshot 同步性。[S02-E011]
9. ResourceMgr 的 idle unload 时间、池大小和强制清理调用点会影响“GameObject Destroy 后多久释放底层包”；本次只确认默认字段和调用，不做性能或内存判断。[S02-E013][S02-E015]
9a. OLD YooAssetLoadService 的失败/空资源路径没有显式释放局部句柄；重复 location 加载也只在 m_assetRefs 保存首个 AssetHandle。需要用旧工程实际 YooAsset 包或运行时 provider 引用计数确认是否会产生额外未归属句柄。[S02-E040]

## C. CURRENT 资源 owner 与关闭

10. ResourceModule.Shutdown 为空，ObjectPoolModule 以 isShutdown=true 释放 AssetObject，而 AssetObject.Release 在 shutdown 时跳过 HandleBase.Dispose。需要查完整 ModuleSystem Shutdown 顺序和 YooAssets.Destroy 所在 owner，确认是否存在全局兜底；目前只列为静态风险。[S02-E026][S02-E037]
11. ResourceModule.UnloadUnusedAssets/ForceUnloadAllAssets 不 await YooAsset 异步卸载；需要确认调用者是否依赖返回后的立即内存下降，或是否存在统一的 operation completion 追踪。[S02-E035]
12. ResourceModule.LoadAsset<T> 回调形式没有 cancellationToken；需要明确 UI/业务是否统一改用 Task 形式，还是为回调形式补充 owner/cancel 协议。[S02-E025][S02-E036]
13. ResourceExtComponent 的 SetSprite 取消和迟到回调保护较完整，但 SubSprite 使用的 subasset handle 与 packageName/异常路径仍需单独验证；本次只纳入资源扩展采样，未展开全部子资源代码。[S02-E028]
14. ProcedurePreload 只设置完成标记，不显式 UnloadAsset；需要确认 PRELOAD 的预期保留窗口和 Asset Pool 的 AutoRelease/ExpireTime，不能仅凭调用点决定是否为泄漏。[S02-E033]
14a. CURRENT ResourceModule 的同步加载和回调失败分支没有统一检查/释放句柄；需要用无效 location、失败清单和同步/回调 API 分别复现 AssetObject、loading marker 与 provider 引用状态。[S02-E041]

## D. CURRENT 场景 owner

15. SceneModule 当前已定义主/子场景句柄，但本次在 GameScripts/Procedure 和 HotFix 中没有发现真实加载调用；需要查启动器、配置驱动或外部程序集是否以反射/接口间接调用。[S02-E034][S02-E038]
16. 主场景 LoadSceneMode.Single 替换旧句柄是否由 YooAsset/Unity 自动完成，源码没有显式释放；需要运行时记录旧 SceneHandle.IsValid、包引用以及 UnloadUnusedAssets completion。[S02-E029]
17. callback LoadScene 在操作刚启动时触发 ForceUnloadUnusedAssets，而 Task 版本在完成后触发；需要确认这是有意的早期清理还是时序缺陷。[S02-E029][S02-E035]
18. callback Unload 的两次 UnloadAsync 是明确源码事实，但实际 YooAsset 是否将第二次调用合并/拒绝，需用 Additive 场景复现；后续修复不能假设二次调用无副作用。[S02-E030]
19. SceneModule Shutdown 不等待子场景卸载，也没有显式处理当前主场景；需要在退出、重启和热重载路径验证是否有更高层全局清理。[S02-E030]
19a. CURRENT embedded UniTask 的 HandleBase.ToUniTask 签名没有 cancellationToken 参数，而 ResourceModule.cs 855 使用了该命名参数；需要先完成目标 Unity 程序集编译，再判断这是源码不匹配还是外部适配器覆盖。[S02-E039]

## E. 调查边界

20. OLD 没有本地 VCS 元数据；若需要精确到某次历史提交，必须提供旧工程 SVN/Git 导出版本或外部仓库信息。[S02-E001][S02-E038]
21. 本次没有运行旧脚本、Unity、资源导入、构建、打包或 Play Mode，也没有读取 Unity 缓存目录；所有“可达/释放/失败”结论均按 evidence.md 的状态标签理解。[S02-E038]
