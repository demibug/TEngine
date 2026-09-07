# 调查与交付验证

## 已执行的只读检查

### 1. 工程与基线

- 确认 OLD 根目录 D:/Work/SAUnity/ProjectOld 存在，且包含 Assets、ProjectSettings、Packages 等工程目录。
- 确认 CURRENT 根目录 E:/MyWork/MyFramework/TEngine/UnityProject 存在。
- 读取两个项目的 ProjectSettings/ProjectVersion.txt，确认 Unity 版本和 revision 一致。[S01-E001] [S01-E002]
- 读取两个项目的 ProjectSettings/EditorBuildSettings.asset，确认 OLD 的 Updater/Launch/World 与 CURRENT 的 main 场景差异。[S01-E003] [S01-E004]

### 2. 启动链与调用点

- 逐段读取 OLD 的 GameLauncher、HotUpdateLauncher、GameLaunch、LaunchLoader、MgrCenter、ModuleProvider、ModuleMgr。
- 逐段读取 CURRENT 的 GameEntry、ProcedureSetting、ProcedureLoadAssembly、GameApp、GameModule、UIModule/SingletonSystem、ModuleSystem、RootModule、ResourceModuleDriver、UpdateDriver。
- 对入口字符串做了源码检索：HotUpdateEntry.Initialize 在 Assets 范围未发现调用点；FrameworkEntry.Initialize 与 GameEntry.Initialize 的已知调用来自 HotUpdateLauncher 的反射路径，不能把同名定义自动视为另一条主链。
- 对模块/程序集配置做了 asmdef 名称和 references 读取，形成 OLD 的 dls.game/Framework/Game/Launcher 边界与 CURRENT 的 TEngine.Runtime/GameProto/GameLogic/Launcher 边界；另核对了 CURRENT 的 SingletonSystem UI 路径与 OLD 的 framework_cfg/ILScript 定义。[S01-E028] [S01-E029] [S01-E030] [S01-E031] [S01-E032]

### 3. 文档结构

本目录应包含且只由以下本次交付文件组成：

- README.md
- findings.md
- evidence.md
- candidates.md
- open-questions.md
- verification.md

证据编号覆盖 S01-E001 至 S01-E032 的实际引用；候选编号使用 S01-C001 至 S01-C009；未决项使用 UQ-001 至 UQ-012。

## 未执行的检查

- 未启动 Unity Editor。
- 未编译或构建任何工程。
- 未执行 HybridCLR、IL2CPP、YooAsset 构建/导表/打包脚本。
- 未运行 Player，因此没有把 Unity 生命周期函数顺序、资源缺失行为、退出销毁顺序写成已验证事实。
- 未修改生产源码来验证候选点。

## 工作区保护检查

写入范围仅为：

E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/01-startup-modules-hotfix

调查期间保留了用户在任务开始前已存在的 ProjectSettings/boot.config 删除、UserSettings/Layouts/ 未跟踪内容和其他 docs/ 内容。最终交付检查已执行以下两类检查：

1. 只查看 tracked diff，确认除既有 boot.config 删除外没有源码/资源/设置修改。
2. 只查看目标目录状态，确认六个文档文件可被识别为本次交付。

检查结果：

- 目标目录恰有 README.md、findings.md、evidence.md、candidates.md、open-questions.md、verification.md 六个文件。
- 证据/候选引用闭合检查通过，没有发现文档引用但未定义的 S01-E### 或 S01-C###。
- 已复核 CURRENT 的 UpdateDriver.Shutdown/DestroyEvent 清理顺序，以及 UIModule.Instance 经 SingletonSystem 管理的独立路径。
- tracked diff 仍只有 UnityProject/ProjectSettings/boot.config 的既有删除。
- 目标目录状态只显示上述六个新增文档；其他 docs/ 下的既有未跟踪交付物未被修改。
- 全局未跟踪状态仍包含 UserSettings/Layouts/default-2022.dwlt 和其他 slave 文档，均保留。

## 结论等级

本目录的主链结论可以直接用于后续架构对照；涉及资源产物、HybridCLR 生成物、Unity 生命周期函数精确顺序和退出回调顺序的内容必须先完成 open-questions.md 中的验证，不能作为已闭合迁移契约。
