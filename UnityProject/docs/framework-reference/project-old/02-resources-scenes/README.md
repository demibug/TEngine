# 02：资源与场景生命周期

本目录是旧工程与当前 TEngine 工程的静态源码调查结果。调查日期为 2026-09-06（Asia/Shanghai）。

## 调查对象

- OLD：D:\Work\SAUnity\ProjectOld
- CURRENT：E:\MyWork\MyFramework\TEngine\UnityProject
- 两个工程的 Unity 版本均为 2022.3.62f2，revision 为 7670c08855a9。
- OLD 当前目录可读，但未发现本地 .git/.svn 工作副本元数据；因此不能给出旧工程提交号。本次以源码相对路径、行号和 SHA-256 记录证据。
- CURRENT 的工作区已有其他用户/其他 slave 的变更；本任务只新增本目录文件，未清理、回退或覆盖它们。

## 结论摘要

1. OLD 不是单一的 YooAsset 全局资源链。AOT 启动器会无条件初始化 YooAsset，但通用业务资源仍由 FrameworkConfig.UseYooAsset 选择 YooAsset、AssetBundle 或文件链；当前序列化配置 UseYooAsset=0，非编辑器初始化又会把 EnableAssetBundle 置为 true。[S02-E002][S02-E003][S02-E004][S02-E017]
2. OLD 通用 GameObject 的主要生命周期边界是 ResourceMgr 的 PrefabInfo、对象池和 ResourceRef，而不是直接调用 YooAssetMgr.LoadGameObjectAsync。ResourceRef.OnDestroy 或显式 ReleaseGameObject 会减掉实例计数，空闲回收再把资源释放回 LoadMgr。[S02-E013][S02-E014][S02-E015]
3. OLD 的 YooAssetLoadService 同时维护 m_assetRefs/m_sceneRefs 和 m_assetHandles/m_sceneHandles 两套按 location 的表；普通加载、Acquire 句柄和场景释放之间没有统一 owner。场景引用释放尤其存在“句柄先释放、引用计数后递减”的静态不一致风险。[S02-E008][S02-E009][S02-E010]
4. CURRENT 把资源句柄放进 AssetObject 多生成对象池；GameObject 实例通过 AssetsReference 反向持有 Prefab 资源，实例 OnDestroy 会调用 IResourceModule.UnloadAsset。这个边界比 OLD 的直接 YooAsset 实例化路径清晰，但同步/回调失败清理、Task 异常清理、场景关闭和模块 Shutdown 仍有待收紧的静态风险。[S02-E024][S02-E026][S02-E027][S02-E029][S02-E030][S02-E039][S02-E041]
5. 本次没有运行 Unity、导入资源、构建、打包、旧工程脚本或 Play Mode；关于 YooAsset 实际引用计数、单场景切换时旧句柄的最终行为，文档只作“源码已确认、运行时未验证”的区分。[S02-E038]

## 范围

已覆盖：

- 资源定位、同步/异步加载、AssetHandle/SceneHandle、缓存和对象池；
- 默认资源包与 RawPackage/命名资源包；
- GameObject 实例化、对象销毁、Prefab/资源释放；
- 场景加载、Single/Additive、场景事件、场景卸载和资源清理；
- 启动配置、程序集边界、条件编译和主要真实调用者；
- 至少一条资源加载到释放链、至少一条场景切换链；
- UI 只做资源调用采样，不展开窗口架构。

不在本目录展开：

- 编辑器打包、收集器、生成工具；交由 06；
- 热更新下载/代码入口本身；只记录它对 RawFile/AssetHandle 生命周期的调用，入口设计交由 01；
- 网络、事件、配置、UI 窗口架构；
- Unity Library、Temp、Logs 等缓存目录。

## 证据约定

- S02-E###：已登记在 evidence.md 的证据 ID。
- 已确认：直接由源码、配置或程序集文件读到。
- 推断：由已确认的调用关系拼出的可达链，但未用运行时验证。
- 未验证：源码无法单独证明，或需要特定构建/Play Mode/真实资源清单。
- 路径均相对于 OLD 或 CURRENT 根目录；行号以本次读取时的文件内容为准。
- 代码性质分为：第三方（Unity/YooAsset/UniTask）、自有适配层（LoadMgr/ResourceMgr/ResourceModule/SceneModule）、业务调用者、配置或生成/工具。核心结论不把第三方行为伪装成自有实现。

## 文档导航

- [findings.md](findings.md)：按机制、生命周期和对照关系整理的调查结论。
- [evidence.md](evidence.md)：逐条证据，含项目、相对路径、类型/方法/字段、行号、证据性质和结论。
- [candidates.md](candidates.md)：只记录可供后续迁移/重构评审的候选，不在本任务实施。
- [open-questions.md](open-questions.md)：静态调查仍不能证明的调用可达性、运行时语义和验证建议。
- [verification.md](verification.md)：文件完整性、Git 范围、链接、证据 ID 与本次未执行事项。
