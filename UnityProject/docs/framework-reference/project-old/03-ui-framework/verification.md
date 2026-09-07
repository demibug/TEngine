# 核验记录

调查日期：2026-09-06，时区 Asia/Shanghai。

本页只记录本任务实际执行过的静态检查和范围检查。没有运行 Unity、旧工程脚本、编译、打包、导表或生成代码，因此不把运行验证写成通过。

## 已完成检查

| 检查 | 实际方式 | 结果 |
| --- | --- | --- |
| 适用说明 | 读取当前工程根目录 AGENT.md、CLAUDE.md，以及本任务使用的 slave-executor 和 tengine-dev 技能说明；按要求使用单 agent | 通过。未调用 master-planner、未启动子 agent、未切换模型。 |
| 当前工作区基线 | 调查开始时执行 git status --short | 已记录。保留 ProjectSettings/boot.config 删除、UserSettings/Layouts/default-2022.dwlt 未跟踪以及其他 handoffs 未跟踪项，未修改、暂存、恢复或清理。 |
| 参考工程可读性 | 检查 OLD 根目录、UIBase、framework、game、UIProject 入口 | 通过。OLD 可读；未执行旧工程脚本。 |
| 版本基线 | 检查两工程 ProjectVersion.txt；对 OLD 相关目录执行只读 svn info；对 CURRENT 执行只读 git log/git rev-parse | 通过。两边 Unity 版本为 2022.3.62f2；OLD framework/game 为 SVN revision 10480，UIProject 为 45216；CURRENT HEAD 为 16afccb5df2a2a2efcb5003ecf9fbc0781c0170a。 |
| 关键源码定位 | 用 rg/Select-String 限定搜索类、方法、配置字段，并按源码行号读取上下文 | 通过。关键定位已登记在 evidence.md 的 S03-E001 至 S03-E024。 |
| 运行时可达性 | 沿 GameLaunch、MgrCenter、ModuleMgr、MainSceneModule、PanelMgr 和 CURRENT GameApp、GameModule、UIModule、LauncherMgr 的静态调用与程序集边界检查 | 部分通过。可确认定义和静态调用关系；无法证明运行时场景、资源加载结果和异常调度时序，缺口写入 open-questions.md。 |
| 生命周期链 | 对 OLD MainPanel 从 UIHelper.Init、PanelMgr.BindPanelType、RegisterPanel、UIAwaiter.EnsureOpenAsync、OpenPanelAsync、BasePanel.Open 到 Close/RemoveUI/OnClose/Dispose 逐段核对 | 通过静态链。完整链条和未验证时序见 findings.md 与 S03-E003 至 S03-E015。 |
| CURRENT 对照 | 对照 UIModule、UIWindow、UIWidget、WindowAttribute、GameEventMgr、TimerModule、IUIResourceLoader、LauncherMgr | 通过静态对照。未把 Launcher UI 与 GameLogic UIModule 混为同一运行时链，见 S03-E017 至 S03-E024。 |
| 文档范围 | 检查本任务只新增目标目录下六个 Markdown 文件，并执行限定目录 git status 与 git diff --name-only | 通过。目标目录仅有六个新增 Markdown；限定目录 diff 没有已跟踪修改。 |
| Markdown 相对链接 | 用正则解析本目录所有 Markdown 的相对链接，并对每个目标执行 Test-Path | 通过。本目录共发现 7 个相对链接，均可解析。 |
| 证据编号引用 | 检查正文中出现的 S03-E 编号是否均在 evidence.md 定义；检查候选的 S03-C 编号是否唯一 | 通过。定义 24 个证据、引用 24 个证据；候选 S03-C001 至 S03-C006 定义和引用一致。 |

## 未运行的检查

- 未启动 Unity Editor，未进入运行场景，未执行旧工程脚本。
- 未编译、打包、导表、生成代码或升级依赖。
- 未运行异步加载、重复打开、关闭中加载、方向切换、缓存恢复或资源句柄释放测试。
- 未扫描 Library、Temp、Logs 等缓存产物，也未扫描 UIProject 素材全量。

## 静态核验结论

当前可复查的结论仅限于源码定义、静态调用、程序集引用、场景字段和版本信息。OLD onRemovedFromStage 时序、PanelMgr opening 标记异常路径、CURRENT 关闭中加载的资源取消、CURRENT 错误回调协议以及 UIRoot prefab 组件仍是待验证项，不能标记为运行时通过。

最终命令复核已确认：目标目录没有源码或项目设置改动；新增文件正好为 README.md、findings.md、evidence.md、candidates.md、open-questions.md、verification.md；所有相对链接和证据编号可解析。全工作区状态另有用户既有删除、布局文件和其他 slave handoff，均未触碰。
