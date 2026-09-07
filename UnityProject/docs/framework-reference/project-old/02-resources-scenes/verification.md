# 调查与文档验证

## 已执行的静态检查

1. 阅读当前工程 AGENT.md、CLAUDE.md，以及本任务要求的 slave-executor、tengine-dev 和资源参考文档；遵循“实际源码优先、只读旧工程、只写目标目录”的约束。
2. 检查 OLD 与 CURRENT 的 ProjectVersion.txt，确认 Unity 2022.3.62f2 / revision 7670c08855a9。
3. 检查 OLD 根目录：可读取；未发现本地 .git/.svn 元数据。旧工程存在 SVN 操作批处理文件，但不能据此推断当前工作副本 revision。
4. 检查程序集边界：
   - OLD：YooAssetMgr.asmdef、dls.framework、Framework、Game/HotUpdate 等；YooAssetMgr 是单独程序集。
   - CURRENT：TEngine.Runtime、Launcher、GameLogic 等；ResourceModule/SceneModule 位于 TEngine.Runtime，业务入口位于 GameLogic/Procedure。
5. 检查条件编译和启动配置：
   - OLD FrameworkConfig 的 UNITY_EDITOR/非编辑器分支、LoadMgr 的资源后端分支、GameLauncher 的 WebGL/LOAD_HOT_UPDATE_FROM_BUNDLE 分支；
   - CURRENT ResourceModule 的 UNITY_EDITOR、UNITY_WEBGL 条件以及 SceneModule 的 EditorFixedMaterialShader 条件。
6. 使用 rg 对 OLD 资源/场景核心和业务调用目录搜索：
   - YooAssetUnifiedMgr、YooAssetMgr.Inst.UnifiedMgr、LoadGameObjectAsync、ReleaseGameObject；
   - ReleaseSceneRef、LoadSceneAsync、UnloadScene；
   - CURRENT 的 ISceneModule、SceneModule.Load/Unload、GameModule.Scene；
   - CURRENT 的 ResourceModule、AssetsReference、ProcedurePreload/LoadAssembly、AudioAgent。
7. 对关键源码和配置文件执行 SHA-256，结果见下节。

## 关键文件 SHA-256

哈希在 2026-09-06 调查时读取；OLD 无 VCS 版本号，所以保留这些值用于复核。

| 项目 | 相对路径 | SHA-256 |
| --- | --- | --- |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/YooAsset/YooAssetMgr.cs | 835609B9264BD4B623195C8FEEFB2732FAFD4DB326355EE0FD6ADFB94B3638F6 |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetLoadService.cs | 3403AE53A6D971DC0E9346A3326FA53D64920B443C2EDA14723E13462259EBE5 |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/YooAsset/Services/YooAssetCacheService.cs | 833F54023711FBCA0AF16DF49548A10ECF27736A78E5CC71655F5A39FB0202DB |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Load/LoadMgr.cs | C15B5B025D43964FA34A9BBB1ED89265E1F2B425D4AED12A50B37EFC8F6573E6 |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceMgr.cs | 91F777B17F3EAF5316CD68D61921D83C6F784B5F80AA7C14A8AF3841D70291A8 |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Resource/ResourceRef.cs | 82030BF617BCE7E188FAA8896F23408E18C3F1FE43999FEC04D4AB1B30DC34EA |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/Scene/SceneMgr.cs | DF310A37856D658FA29BDAA50CF996069D5FF26B5E918BDE1E87246D458D59D5 |
| OLD | Assets/Scripts/framework/Library/ZeroFramework/FrameworkConfig.cs | F481D45931DD1B8CE98A5B95D573F66DBB61E9428AF0D6B798A516969055AFEB |
| OLD | Assets/Resources/framework_cfg.asset | ED1445577C624D7302BB5CA77B1F81D1C330F05F8D1CBF2D39C5803AB4E5CE30 |
| OLD | Assets/Resources/YooAssetConfig.asset | 758E79F9D564FDD6FA2CFA64D9AD4A27527DDD7C0B19A217480D118560CDF7EA |
| OLD | Assets/Scripts/Launcher/GameLauncher.cs | CCA4E5E518FBF63BCE84893792417486C098439587CA4E42647190C4B099323B |
| CURRENT | Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs | 9DB5E2503E82C78207D7FF6391F2742F55B80738942F82CFB22AEBA9CE877EC9 |
| CURRENT | Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.AssetObject.cs | 1BF102571DC7BBC7E2A8B3FB685D57928CDC4F1583C9F9ADE0F85924A82AF3E9 |
| CURRENT | Assets/TEngine/Runtime/Module/ResourceModule/Reference/AssetsReference.cs | C7B7D63330C37A9773CBB7CC9E713F90B67AA127FE464DA65E538F8935347EDE |
| CURRENT | Assets/TEngine/Runtime/Module/ResourceModule/Extension/ResourceExtComponent.Resource.cs | 7B0452B3175776DBD122A162856B07444D78ED4B887737EDB4931F28C9BA2725 |
| CURRENT | Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs | 9839A55FAB61B39C52B7BC3B506E0865D27510F6D5E7E4D0C3AD9133166B002A |
| CURRENT | Assets/TEngine/Runtime/Module/AudioModule/AudioAgent.cs | 1D8EC82D66A8D75B6B4D8F9F9166C3D40559CC4DD2689E1D7009C96ADB4CA2B5 |
| CURRENT | Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs | B4933CDB8F1C70446F0EB8E777935C79BE5C64536EF8A5048C0ECD5EE4C46994 |
| CURRENT | Packages/UniTask/Runtime/External/YooAsset/OperationHandleBaseExtensions.cs | CB7A7E6FDA9F281262C158F40762255965B3AB02A89415A03114D298E1BB492F |
| CURRENT | Packages/packages-lock.json | A6A397C5C8F8D5204523C5C74486FE3CA4EA655C2972DDB5A57A044C9A991644 |

复核结果：表内 20 个路径均存在，SHA-256 全部匹配。

## 本目录文件检查

- 已生成：README.md、findings.md、evidence.md、candidates.md、open-questions.md、verification.md。
- 已使用相对 Markdown 链接，目标均在本目录内。
- evidence.md 现有 42 个 S02-E### 定义；本目录文档引用 42 个，缺失 0、未使用 0；所有证据记录均包含项目、路径、证据性质、行号、结论和状态。
- 已使用相对 Markdown 链接，目标均在本目录内；链接检查无断链。
- 所有架构结论和候选建议均带 S02-E###；evidence.md 负责登记这些 ID。
- findings.md 中的资源链、场景链与 evidence.md 的路径/行号保持一致；没有引入未登记的 S02-E ID。
- Mermaid 不是本次必需依赖；文档使用纯文本箭头图，避免渲染器差异。

## Git 范围检查

调查开始时，工作区已有以下不属于本任务的变更，均未触碰：

- ProjectSettings/boot.config（删除状态）
- UserSettings/Layouts/default-2022.dwlt（未跟踪）
- docs/framework-reference/project-old/01-startup-modules-hotfix/README.md（未跟踪）
- docs/framework-reference/project-old/handoffs/ 下已有的多份 handoff 文件

本任务允许写入且只写入：

    E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/02-resources-scenes

最终交付前应再次执行：

    git status --short --untracked-files=all
    git diff -- docs/framework-reference/project-old/02-resources-scenes
    Get-ChildItem -LiteralPath docs/framework-reference/project-old/02-resources-scenes -File
    rg -n "S02-E[0-9]{3}" docs/framework-reference/project-old/02-resources-scenes

注意：新文件不会出现在普通 git diff 中，必须用 Get-ChildItem、git status 和必要时 git diff --no-index/文件读取检查其内容。

## 未执行事项

- 未运行 Unity Editor/Batchmode、编译、导入、构建、打包或 Play Mode。
- 未运行旧工程 SVN/构建/导表/生成脚本。
- 未修改源代码、项目设置、Packages、现有索引或其他 handoff。
- 未扫描 Unity Library、Temp、Logs 等缓存目录。
