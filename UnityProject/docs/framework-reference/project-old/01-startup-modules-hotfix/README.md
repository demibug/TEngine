# Slave 01：启动、模块组织与热更边界

## 交付范围

本目录记录对两个 Unity 工程的只读源码调查：

- OLD：D:/Work/SAUnity/ProjectOld
- CURRENT：E:/MyWork/MyFramework/TEngine/UnityProject

调查主题限定为启动入口、模块组织、帧驱动、初始化/销毁、程序集边界、热更加载与桥接。没有修改任何生产源码、场景、资源、ProjectSettings、Packages 或其他文档目录。

## 基线

| 项目 | 基线 |
|---|---|
| Unity 版本 | 两个项目均为 2022.3.62f2，revision 为 7670c08855a9 |
| OLD 启动场景 | Assets/Scenes/Updater.unity、Assets/Scenes/Launch.unity、Assets/Scenes/World.unity |
| CURRENT 启动场景 | Assets/Scenes/main.unity |
| CURRENT Git 基线 | 分支 framework，HEAD 16afccb5df2a2a2efcb5003ecf9fbc0781c0170a |
| 现有工作区变化 | ProjectSettings/boot.config 删除、UserSettings/Layouts/ 未跟踪，以及原有 docs/ 未跟踪内容均保留 |

场景配置证据见 [S01-E003]、[S01-E004]；Unity 版本证据见 [S01-E001]、[S01-E002]。

## 结论快照

1. **OLD 是“两阶段启动 + 场景管理器”的形态。** Updater 场景中的 GameLauncher 先完成 YooAsset、AOT 元数据和 Launcher.dll 装载，再反射进入 HotUpdateLauncher；HotUpdateLauncher 装载其他 DLL 并切换到 Launch 场景。真正的 manager/module 注册在 Launch 场景的 GameLaunch 中通过 LaunchLoader 完成。[S01-E005] [S01-E006] [S01-E009] [S01-E013] [S01-E014]
2. **OLD 的热更桥接和实际业务初始化不是同一层。** FrameworkEntry.Initialize 与 GameEntry.Initialize 会被反射调用，但它们当前有效代码主要是设置 initialized 标志/日志，原本的 manager、业务、UI 初始化调用仍是注释；当前可达的实质初始化是 GameLaunch 注册 MgrCenter、ModuleMgr，再由 ModuleProvider 扫描 dls.game。[S01-E010] [S01-E017]
3. **CURRENT 是“常驻根节点 + TEngine 懒加载模块 + GameLogic Singleton + Procedure 状态机”的形态。** main 场景实例化 GameEntry prefab；GameEntry.Awake 预取四个 TEngine 模块并启动 ProcedureSetting。ModuleSystem 按接口推导实现类型，首次获取时 Activator.CreateInstance 并立即 OnInit；UI 则由 UIModule.Instance/SingletonSystem 管理；RootModule.Update 驱动 TEngine 模块更新队列。[S01-E004] [S01-E019] [S01-E020] [S01-E021] [S01-E030]
4. **CURRENT 的热更边界由 UpdateSetting 和 ProcedureLoadAssembly 共同决定。** 配置声明 GameProto.dll/GameLogic.dll 为热更程序集、若干 AOT 元数据程序集及 GameLogic.dll 为主逻辑；流程通过资源模块加载 TextAsset 字节并 Assembly.Load，最后反射调用 GameApp.Entrance。[S01-E022] [S01-E024] [S01-E025]
5. **两者的可比主链如下。**

   - OLD：Updater/GameLauncher.Start → InitializeYooAsset → AOT metadata + Launcher.dll → HotUpdateLauncher.Start → LoadOtherHotUpdateDLLs → Framework/Game bridge → LoadScene(Launch) → GameLaunch.Awake → LaunchLoader → MgrCenter/ModuleMgr → dls.game modules
   - CURRENT：main/GameEntry.Awake → ModuleSystem.GetModule → ProcedureSetting.StartProcedure → Launch/Splash/Package/Resource/Preload → ProcedureLoadAssembly → Assembly.Load(GameProto/GameLogic) → GameApp.Entrance → GameModule.UI

   上述两条均来自实际代码调用关系；其中 Unity 生命周期函数之间的精确先后仍按“推断”处理，见 [S01-C001]、[S01-C004]。
6. **退出路径都存在清理入口，但必须区分确定的代码顺序和未确定的 Unity 对象销毁顺序。** OLD 的显式重启会反向释放 MgrCenter、Singleton、资源和场景，再加载 Launch；CURRENT 在 Player 构建中由 RootModule.OnDestroy 触发 ModuleSystem.Shutdown，而 UpdateDriver.Shutdown 会先清空 DestroyEvent 再销毁 [UpdateDriver]，因此若 RootModule 先触发，GameApp.Release 不会由这条销毁路径调用；若 [UpdateDriver] 独立对象先被销毁，listener 才可能执行。CURRENT 编辑器分支明确跳过 RootModule 的 ModuleSystem.Shutdown。[S01-E018] [S01-E021] [S01-E025] [S01-C004]

## 文档索引

- [findings.md](findings.md)：按主题整理的已确认结论、推断和边界。
- [evidence.md](evidence.md)：证据台账；每条记录包含项目、相对路径、符号/字段、行号、结论和证据性质。
- [candidates.md](candidates.md)：可作为后续迁移/重构输入的候选点，不等同于已确认缺陷。
- [open-questions.md](open-questions.md)：需要 Unity 运行、构建或进一步外部信息才能闭合的问题。
- [verification.md](verification.md)：本次调查执行过的检查、未执行的检查和文档交付校验。

## 证据口径

- [已确认]：可由当前源码、场景或配置中的直接定义/调用/字段值证明。
- [推断]：由多个直接证据串联出的运行时行为，但没有在 Unity 中运行验证。
- [未验证]：源码中存在相关入口或分歧，但当前调查不能证明最终构建/运行时是否采用。
- 证据编号统一为 S01-E###；候选编号为 S01-C###。
- 旧工程运行时读取的热更清单路径是代码中的 Assets/GameRes/HotUpdate/hotUpdate；仓库中对应可见文件为 hotUpdate.txt。HybridCLRSettings.asset 是另一份配置输入，二者内容并不相同，不能未经构建验证就合并。[S01-E011] [S01-E012]
- 生产代码中的注释、未调用方法和备选分支只作为“存在的设计意图/候选路径”记录；实际可达链优先采用场景、调用点和运行时读取字段。

## 调查限制

本次没有启动 Unity Editor、没有编译、没有构建 Player、没有执行 HybridCLR/YooAsset 导表或打包流程。因此 DLL 是否被正确复制、AOT 元数据是否与目标 Player 一致、场景中所有代理脚本的最终序列化绑定，均保留为未验证项。
