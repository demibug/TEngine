# 候选点与后续价值

以下条目是基于源码证据整理出的迁移、重构或验证候选，不等同于已经确认的缺陷。每条都给出最小验证动作，避免把推断直接变成改码结论。

## S01-C001：CURRENT 的流程启动与 ResourceDriver.Start 的先后

- 状态：待运行验证
- 证据：[S01-E019] [S01-E023] [S01-E026]
- 观察：GameEntry.Awake 在同一帧启动 StartProcedure；ProcedureSetting 先构造流程并缓存 IResourceModule，随后只 Yield 一次；ResourceModuleDriver 则在 Start 中写入配置并调用 ResourceModule.Initialize。
- 风险/价值：如果 UniTask continuation 或组件生命周期顺序不符合当前假设，ProcedureInitPackage/InitResources 可能在 YooAssets 或包配置完成前访问资源模块。
- 最小验证：在 Development Player 中给 GameEntry.Awake、ResourceModuleDriver.Start、ProcedureInitPackage.OnEnter、ResourceModuleDriver.Initialize 加时间戳日志，核对首次进入流程的顺序。
- 建议：在迁移文档中把“模块对象已创建”和“资源系统已初始化”分成两个状态，不要只以 GetModule 成功作为资源可用条件。

## S01-C002：CURRENT 程序集加载失败后的完成标志

- 状态：代码风险候选，未判定为缺陷
- 证据：[S01-E024]
- 观察：ProcedureLoadAssembly.LoadAssetSuccess 在 TextAsset 为空时直接 return；LoadMetadataAssetSuccess 同样在为空时 return。两处完成标志主要在 finally 或计数归零分支更新。
- 风险/价值：如果资源模块以 null 回调表示失败，流程可能保持等待状态，或无法到达统一失败 UI；如果上层保证不会以 null 回调，则该分支只是防御代码。
- 最小验证：构造缺失 GameProto.dll.bytes、缺失 AOT metadata 和错误资源地址三种包，确认流程是否进入失败提示、是否仍持续 OnUpdate 等待。
- 建议：把加载失败显式建模为失败终态，并记录失败程序集名称；在未验证前不要按“已有重试契约”理解。

## S01-C003：CURRENT 在校验 GameApp 之前切换到 ProcedureStartGame

- 状态：代码风险候选，未判定为缺陷
- 证据：[S01-E024]
- 观察：AllAssemblyLoadComplete 的第一步是 ChangeState<ProcedureStartGame>，随后才检查主逻辑程序集、GameApp 类型和 Entrance 方法。
- 风险/价值：DLL 缺失或入口签名错误时，状态机已经离开 LoadAssembly；后续是否仍有可见错误处理、是否会停留在 StartGame 状态，取决于运行时行为。
- 最小验证：分别移除 GameLogic.dll.bytes、改名主逻辑类型、移除 Entrance 方法，观察日志、流程状态和用户界面。
- 建议：迁移时先完成入口完整性校验，再推进“开始游戏”状态；若保留现状，应把该顺序列为显式契约。

## S01-C004：CURRENT 两个常驻对象的销毁顺序与重复清理

- 状态：待退出验证
- 证据：[S01-E021] [S01-E025]
- 观察：RootModule.OnDestroy 在 Player 中调用 ModuleSystem.Shutdown；GameApp.Entrance 将 Release 注册到独立 UpdateDriver 的 MainBehaviour.DestroyEvent；UpdateDriver.Shutdown 会先调用 MainBehaviour.Release 清空 DestroyEvent，再销毁自身对象。
- 风险/价值：如果 RootModule 先触发 ModuleSystem.Shutdown，GameApp.Release 不会由该 UpdateDriver 销毁路径调用；如果 [UpdateDriver] 独立对象先被 Unity 销毁，listener 才可能执行。编辑器停止运行还跳过 RootModule.Shutdown。
- 最小验证：Player 退出、切场景销毁、Editor Stop 三种场景分别记录 GameApp.Release、RootModule.OnDestroy、UpdateDriver.MainBehaviour.OnDestroy、ModuleSystem.Shutdown 的顺序和次数。
- 建议：如果需要稳定契约，增加单一退出协调点或明确幂等性；在此之前不要假设 GameModule.Shutdown 会自动执行。

## S01-C005：OLD hotUpdate.txt 与 HybridCLRSettings.asset 的集合差异

- 状态：待构建验证
- 证据：[S01-E011] [S01-E012] [S01-E029]
- 观察：运行时读取的清单第二行与 HybridCLRSettings.hotUpdateAssemblies 的名称集合不同；link.xml 也只保留其中一部分程序集。
- 风险/价值：源码装载顺序、HybridCLR 生成的 AOT/桥接产物、YooAsset 打包清单和最终 DLL 集合可能由不同阶段分别生成；迁移时直接复制任一列表都可能遗漏程序集或加载多余程序集。
- 最小验证：执行旧工程实际构建脚本，导出最终 Player 中的 AOT metadata、热更 DLL、资源地址和生成 link.xml，逐项与两份配置对照。
- 建议：把“构建输入清单”和“运行时装载清单”分成两个有名字的产物，并在 CI 中做集合一致性检查。

## S01-C006：OLD FrameworkEntry/GameEntry 是桥接验证层还是应有初始化层

- 状态：设计意图待确认
- 证据：[S01-E010] [S01-E014] [S01-E017]
- 观察：HotUpdateLauncher 确实反射调用两个 Entry，但核心初始化方法调用被注释；FrameworkEntry 的注释还说明 manager 已由 AOT 的 MgrCenter 注册。真实 manager/module 初始化由 LaunchLoader 完成。
- 风险/价值：若后续人员按类名把 FrameworkEntry/GameEntry 当作主初始化入口，可能重复注册或漏掉 LaunchLoader 的依赖顺序。
- 最小验证：在旧工程运行时捕获 MgrCenter.Register、ModuleMgr.RegisterAsync、FrameworkEntry.Initialize、GameEntry.Initialize 的调用日志和调用栈，确认各自的有效职责。
- 建议：迁移说明应把它们标成“热更层桥接/状态验证入口”，除非后续清理代码后重新建立其初始化责任。

## S01-C007：OLD UseYooAsset 分支与实际 LaunchLoader 路径

- 状态：配置来源待确认
- 证据：[S01-E014] [S01-E018] [S01-E031]
- 观察：GameLaunch.InitLaunchLoader 根据 FrameworkConfig.Inst.UseYooAsset 选择 YooAssetLoadCommander 或 LoadMgr；旧 Resources/framework_cfg.asset 中可见 UseYooAsset: 0，但热更启动和重启代码仍保留 YooAsset 分支。
- 风险/价值：当前源码可能同时支持离线/旧资源管线与 YooAsset 管线；仅凭静态资产值不能确认发布包选中的分支。
- 最小验证：确定最终 Resources/framework_cfg 资产、构建覆盖配置和运行时 FrameworkConfig.Inst 来源，在 Editor、Development Player、Release Player 各打印 UseYooAsset 和实际 commander。
- 建议：将配置来源与构建变体记录进迁移基线，避免把保留分支误写成当前唯一架构。

## S01-C008：OLD HotUpdateEntry、ILScriptProject 和未调用辅助入口的可达性

- 状态：范围待确认
- 证据：[S01-E017] [S01-E027] [S01-E032]
- 观察：HotUpdateEntry.Initialize 有定义但在 Assets 范围未发现调用点；ILScriptProject/Main.cs 也未形成从 Updater/Launch/GameLaunch 的调用链；FrameworkEntry 的私有辅助初始化方法同样不是当前主链证据。
- 风险/价值：这些代码可能是历史迁移残留、工具测试入口或由外部生成/反射调用；若误当作主链会污染框架边界图。
- 最小验证：在完整构建脚本、场景序列化、RuntimeInitializeOnLoadMethod、反射字符串和生成代码中检索入口；运行时启用一次入口日志确认。
- 建议：在架构文档中独立标注“定义存在但未证实可达”，不要并入当前主启动路径。

## S01-C009：CURRENT GameModule.Shutdown 的生命周期责任

- 状态：调用关系待确认
- 证据：[S01-E025] [S01-E021]
- 观察：GameModule.Shutdown 只清空静态 facade 缓存；GameApp.Release 没有调用它，RootModule.OnDestroy 也只进入 ModuleSystem.Shutdown。
- 风险/价值：若 facade 缓存跨重启/重载场景保留，可能出现旧对象引用；若 ModuleSystem 已清空模块，facade 的缓存清理时机也需要明确。
- 最小验证：重复进入/退出主场景或执行 Player 重启，检查 GameModule 各静态字段、UIModule.Instance、ModuleSystem 映射的对象身份和调用次数。
- 建议：确定 facade 是随模块系统统一失效，还是由 GameApp 作为热更域边界显式清理；两种语义不要混用。
