# S06 静态核验报告

## 1. 范围与可访问性

| 检查项 | 结果 |
|---|---|
| OLD 根目录 | 可访问：D:\Work\SAUnity\ProjectOld |
| CURRENT 根目录 | 可访问：E:\MyWork\MyFramework\TEngine\UnityProject |
| 唯一写入目录 | 本目录：E:\MyWork\MyFramework\TEngine\UnityProject\docs\framework-reference\project-old\06-editor-build-generation |
| OLD 根 Git | 未发现；不能用根仓库 diff 表示参考工程版本 |
| OLD 可见 SVN | Tools\assembly 报 URL 为 SVN library，revision 45216，last changed revision 45215 |
| CURRENT Git | 根为 E:\MyWork\MyFramework\TEngine，HEAD 为 16afccb5df2a2a2efcb5003ecf9fbc0781c0170a |
| Unity 版本 | OLD/CURRENT 的 ProjectSettings/ProjectVersion.txt 均为 2022.3.62f2 |

以上基线用于确定源码可比较，不能证明构建环境依赖已安装或运行时状态一致。[S06-E001]

## 2. 工作树保护检查

调查开始时 CURRENT Git 状态为：

- 已有删除：ProjectSettings/boot.config
- 已有未跟踪：UserSettings/Layouts/
- 已有未跟踪：docs/

这些状态属于用户/并行任务已有变化，本次没有恢复、删除或修改。文档只创建在本目录；完成后再次检查必须确认除了既有 docs/ 汇总外，没有源码/资源/ProjectSettings/Packages 变化。

## 3. 关键文件哈希

以下 SHA-256 是本次读取时计算，用于后续复查“调查期间没有改动源码”：

| 项目 | 文件 | SHA-256 |
|---|---|---|
| OLD | D:\Work\SAUnity\ProjectOld\Assets\Scripts\framework\Editor\AssetBundle\AssetBundlePanel.cs | 4E5B4EB97D176599BE57B3AA276FACA587A8CD6491D96145643CC686C3BDF52B |
| OLD | D:\Work\SAUnity\ProjectOld\Assets\Scripts\framework\Editor\AssetBundle\YooAssetBuildHelper.cs | 4E523E955F768BE0F74BE9DD6FE5FA37143905C598DBAA62333163EF743B210F |
| OLD | D:\Work\SAUnity\ProjectOld\Assets\Scripts\framework\Editor\Config\ConfigTool.cs | 4181BC5A437F78A9BCD2EF70B953E588A8187EEE0F7D211E81A23B431B420D38 |
| OLD | D:\Work\SAUnity\ProjectOld\Assets\Editor\Export\ExportProcessor.cs | 2AD5F6B9ECEC8FA0DC89A14AEB0BB843E9E1E272D88AE6A2A0FFE06B4085E1C3 |
| CURRENT | E:\MyWork\MyFramework\TEngine\UnityProject\Assets\TEngine\Editor\ReleaseTools\ReleaseTools.cs | F89A72EE81204A25CFD8D1ABB8B88C93CFB12AA90F790A8172E1BC69C0B46B9E |
| CURRENT | E:\MyWork\MyFramework\TEngine\UnityProject\Assets\TEngine\Editor\HybridCLR\BuildDLLCommand.cs | EEBC57B13B4DEFE5C8C9136A26C5CD65EB1A7D8DF81D5432BA6A05642DCD0F09 |
| CURRENT 外部工具 | E:\MyWork\MyFramework\TEngine\Configs\GameConfig\luban.conf | E9DE28BB4758EF684EED3A051F4F5EB0CD999CDE4140F0A124AEA7B1E54BFC4C |

哈希只对上述文件负责，不替代完整源码审查。[S06-E001]

## 4. 范围搜索结果

- OLD 真实找到 AssetBundlePanel、AssetBundleBuilder、YooAssetBuildHelper、AssetBundleImporter、ConfigTool、CfgCodeCreator、GenCodeTool、CommandHelper、MakeInterface、ExportProcessor、HybridCLRPatchSync 和自定义 Pack/Filter/Ignore 规则。[S06-E003][S06-E004][S06-E006][S06-E010][S06-E012][S06-E014][S06-E018][S06-E019][S06-E039]
- CURRENT 真实找到 ReleaseTools、BuildConfig、BuildPipelineWindow、BuildDLLCommand、UpdateSetting/Editor、ProcedureLoadAssembly、LubanTools、ShellHelper、UI ScriptGenerator 和 Roslyn 事件生成器工件/源码；同时核对了 YooAsset 的默认地址规则。[S06-E020][S06-E022][S06-E023][S06-E024][S06-E028][S06-E029][S06-E033][S06-E034][S06-E043]
- CURRENT 的 AssetBundleCollectorSetting 看到 DefaultPackage、OtherPackage、Dlc1Package、Dlc2Package；当前默认收集路径包含 DLL、Configs、UI/UIRaw 等 AssetRaw 子目录，未看到旧 RawPackage。[S06-E025]
- CURRENT 非缓存工作树中，以下路径均不存在：Assets/GameScripts/HotFix/GameProto/GameConfig、Assets/AssetRaw/Configs/bytes、Assets/GameScripts/HotFix/GameLogic/UI/Gen。因此没有把生成缓存或推测文件当成当前产物。[S06-E026][S06-E030][S06-E034]

搜索排除 UnityProject 的 Library、Temp、Logs、UserSettings、obj、bin 以及大型运行输出目录；没有执行全库二进制反编译。

## 5. 调用链与契约核验

| 链路 | 静态结果 | 运行限制 |
|---|---|---|
| OLD 一键菜单→配置资产→GameConfig 收集→BaseDao/LoadMgr→BuffMgr | 入口、输出路径、收集器和业务消费者均找到；完整 BuildAll 路径的 EncodeAllCfg 会在入口和 Export 前置处理中各调用一次 | 未执行 CSV 编码、YooAsset 构建或运行时加载。[S06-E003][S06-E006][S06-E011][S06-E013][S06-E040][S06-E042] |
| CURRENT BuildWithConfig→DLL/AOT 复制→DefaultPackage pipeline→ProcedureInitPackage/ResourceModule→ProcedureLoadAssembly | 参数、包名、物理复制目录、资源地址映射和运行时消费者均找到；DLL 前置仍取 activeBuildTarget，AssetBundle 取 config.BuildTarget，且 BuildWithConfig 没有结构化返回结果 | 未执行 HybridCLR 编译、资源构建、Player 或运行时初始化。[S06-E020][S06-E021][S06-E022][S06-E023][S06-E041][S06-E043] |
| CURRENT Luban 菜单→外部脚本→C#/bytes 输出→ConfigSystem 模板 | 脚本和模板输入/输出关系找到 | 未执行脚本；目标生成目录不存在，退出码和编译结果未知。[S06-E026][S06-E027][S06-E028] |
| CURRENT EventInterface→Roslyn AddSource→GameEventHelper | 源码、工件、业务标记和调用找到 | 未执行 Unity 导入/编译；.meta 显示的 Editor enabled: 0 需解释。[S06-E033][S06-E034][S06-E035] |

## 6. 明确未执行项目

遵守任务边界，本次没有：

- 启动 Unity、打开菜单、进入 Play Mode 或读取运行时日志；
- 执行 Luban、dotnet、批处理、Shell、Python、HybridCLR 安装/生成/导出；
- 执行 YooAsset/AssetBundle/Player/AAB/补丁/CDN 构建或上传；
- 修改任何源码、资源、配置资产、程序集设置、包清单、外部脚本或其他文档；
- 读取或依赖 Unity Library/Temp 中的生成缓存来填补当前工作树证据。

因此文档中的“输出”“消费”“失败路径”均是源码契约或静态调用结论，不是构建成功、性能或运行稳定性保证。[S06-E001][S06-E034]

## 7. 完成后复查结果

本次写入完成后已复查：

1. 本目录只有六个文件：README.md、findings.md、evidence.md、candidates.md、open-questions.md、verification.md。
2. 当前 Git 的 tracked diff 仍只有既有的 UnityProject/ProjectSettings/boot.config 删除；Assets、ProjectSettings（该既有删除除外）、Packages 和外部工具源码没有本次变更。
3. 当前 status 中本目录六个文件均为新增文档；同级 docs/ 下另有并行任务已有的 handoffs/06-editor-build-generation.md，本次没有读取后修改或覆盖它。
4. 证据交叉引用检查得到 S06-E001 至 S06-E043 全部有定义，没有未定义证据 ID；候选为 S06-C001 至 S06-C006，问题为 S06-Q001 至 S06-Q010。
5. 关键源码哈希与调查开始时记录值一致，详见本文件第 3 节。

这些是文档/工作树完整性检查，不代表 Unity 编译、外部工具退出成功、资源构建或运行时验证。
