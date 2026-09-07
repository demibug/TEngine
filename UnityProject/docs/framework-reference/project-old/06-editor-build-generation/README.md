# S06：编辑器工具、构建与代码生成

## 目标

本目录记录旧参考工程中“编辑器工具、资源构建/收集、程序集与热更构建、UI/协议/配置代码生成”的真实源码机制，并以当前 TEngine 工程作受控对比，供后续选择性复刻时评审。结论只来自静态源码、配置和调用关系；没有执行 Unity、构建、导表或外部脚本。

## 边界与基线

- 旧参考工程（只读）：`D:\Work\SAUnity\ProjectOld`
- 当前工程：`E:\MyWork\MyFramework\TEngine\UnityProject`
- 当前工程相关的外部工具源码：`E:\MyWork\MyFramework\TEngine\Configs`、`E:\MyWork\MyFramework\TEngine\Tools\GameEventSourceGenerator`。它们只作为当前工程的工具契约对比来源读取。
- 本调查唯一写入目录：`E:\MyWork\MyFramework\TEngine\UnityProject\docs\framework-reference\project-old\06-editor-build-generation`
- 旧工程根目录没有 Git 元数据；可见的 `Tools\assembly` SVN 工作副本报告 revision `45216`。当前 Git 基线为 `16afccb5df2a2a2efcb5003ecf9fbc0781c0170a`。
- 两个 Unity 工程的 `ProjectVersion.txt` 都是 `2022.3.62f2`。基线和文件哈希详见 [verification.md](verification.md)。[S06-E001]

本目录不覆盖启动流程、通用资源运行时、UI 生命周期、网络协议运行时和配置运行时本身，只在构建/生成链路需要时引用其消费端。没有修改源码、资源、ProjectSettings、Packages、外部工具脚本或本目录之外的文档。

## 推荐阅读顺序

1. [findings.md](findings.md)：先看旧链路、当前对比和两条端到端流。
2. [evidence.md](evidence.md)：按 `S06-E***` 回查每个结论的文件、符号、行号和归属。
3. [candidates.md](candidates.md)：查看未批准的复刻候选及其约束。
4. [open-questions.md](open-questions.md)：查看未证明的激活关系、契约缺口和下一步核验。
5. [verification.md](verification.md)：查看本次静态检查、范围搜索、状态保护和未执行项目。

## 已覆盖

- 旧工程菜单入口：AssetBundle 面板、一键生成并打包、配置代码/配置资产菜单、通用代码生成菜单和 HybridCLR 构建菜单。[S06-E002][S06-E003][S06-E010][S06-E014][S06-E018]
- 旧工程构建：`AssetBundleImporter` 的收集/预处理、`AssetBundleBuilder` 的构建/收尾、`YooAssetBuildHelper` 的 DefaultPackage + RawPackage、复制/清理和失败结果。[S06-E004][S06-E009]
- 旧工程扩展：`[Export]` 反射式前后置处理、外部 Python→Unity 参数/结果协议、自定义 Pack/Filter/Ignore 规则。[S06-E005][S06-E006][S06-E016][S06-E017]
- 旧工程代码生成：CSV→C# 三文件、CSV→`.asset`、DAO 运行时消费、`ICodeGenerator` 分段写入及 HybridCLR 热更程序集导出。[S06-E010][S06-E011][S06-E012][S06-E013][S06-E014][S06-E018]
- 当前工程对比：ReleaseTools/YooAsset、BuildConfig/EditorPrefs、HybridCLR DLL 复制、UpdateSetting 同步、Luban、UI 脚本生成器和 Roslyn 事件生成器。[S06-E020][S06-E021][S06-E022][S06-E023][S06-E024][S06-E026][S06-E029][S06-E032][S06-E043]
- 两条实际静态调用链：旧配置构建到 DAO 消费；当前构建到 DefaultPackage/热更程序集运行时加载。[S06-E003][S06-E010][S06-E011][S06-E012][S06-E013][S06-E020][S06-E021][S06-E023][S06-E025][S06-E036][S06-E043]

## 已发现但不作为“已激活”结论的内容

- 旧工程的一键入口调用 `GenCodeTool.GenCode()`，但已找到的 `ICodeGenerator` 实现位置含注释掉的示例，未在限定搜索范围内证明有可执行 provider。因此“入口存在”不等于“本次一定产出代码”。[S06-E014][S06-E015]
- 当前工程有 `SourceGenerator.dll`、`GameEventAnalyzer.dll` 和对应源码；`ILoginUI` 使用标记、`GameApp` 调用 `GameEventHelper.Init()`，但生成的 `.g.cs` 不落在工作树中，且插件 `.meta` 的 Editor 启用字段为 `0`，不能仅凭文件存在断言 Unity 当前导入一定成功。[S06-E031][S06-E032][S06-E033]
- 当前 Luban 脚本明确声明输出目录和模板，但调查时目标目录不存在；这是“可生成契约”，不是“当前工作区已有生成物”。[S06-E026][S06-E027][S06-E028]

## 未找到 / 待核验

- 未执行任何 Unity 菜单、批处理、Shell、Luban、HybridCLR、YooAsset 构建、Player 导出或代码生成；因此没有运行日志、构建报告、最终包目录或编译通过证明。详见 [verification.md](verification.md)。
- 当前 `BuildCLI` 的注释示例仍写 `TEngine.ReleaseTools.BuildPackage`，实际源码没有该方法；现有 Android 脚本调用 `AutomationBuildAndroid`。当前 CLI 的 `packageVersion` 读取后也未传入旧兼容构建函数，DLL CLI 入口的实际构建调用被注释。另有 `BuildCLI/path_define.bat` 硬编码 `G:/github/TEngine/UnityProject` 和 Unity `2021.3.20f1c1`，与当前工程路径和 `2022.3.62f2` 基线不一致；该脚本应视为待核验的本机模板，而不是已验证可执行入口。[S06-E001][S06-E036]
- 当前收集器只确认 `DefaultPackage`、`OtherPackage`、`Dlc1Package`、`Dlc2Package` 配置片段，未确认运行时是否会启用多个包；旧工程的 `RawPackage` 没有当前对应物。[S06-E008][S06-E025]
- 当前 UI 自动生成路径、当前已提交 UI 脚本中的生成区域、Roslyn 生成器 DLL 与 asmdef/Unity 编译产物之间的最终激活关系，需要在允许执行 Unity 导入/编译时再验证。[S06-E029][S06-E030][S06-E031][S06-E033]

“已确认”“推断”“待核验”在正文中分开写；候选方案均以 `S06-C***` 编号，未表示已批准或已实施。
