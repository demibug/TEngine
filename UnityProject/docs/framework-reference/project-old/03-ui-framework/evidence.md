# 证据索引

本索引只保留支持结论所需的短摘录，不复制整份源码。路径均相对于对应工程根目录；行号按 2026-09-06 调查时读取的工作区文件核验。性质中的“定义”说明接口/实现存在，“调用”说明在调查范围内找到调用，“配置”说明来自项目配置或场景文本；任何定义都不单独证明运行时已经走到该路径。

## 基线与启动可达性

### S03-E001 — Unity 版本一致

- 所属工程：OLD、CURRENT。
- 相对路径：ProjectSettings/ProjectVersion.txt。
- 类/方法/配置字段：m_EditorVersion、m_EditorVersionWithRevision。
- 核验行号：OLD 1-2；CURRENT 1-2。
- 短摘录：m_EditorVersionWithRevision: 2022.3.62f2 (7670c08855a9)。
- 支持结论：两个工程在本次调查基线使用同一 Unity 编辑器版本。
- 性质：配置证据。

### S03-E002 — OLD 启动把 UI 绑定置于 PanelMgr/模块之前

- 所属工程：OLD。
- 相对路径：Assets/Scripts/game/Module/Launch/GameLaunch.cs；Assets/Scripts/game/Module/Launch/LaunchLoader.cs；Assets/Scripts/framework/Library/ZeroFramework/Manager/MgrCenter.cs。
- 类/方法/配置字段：InitLaunchLoader、MgrRegisterCommander.Run、MgrCore.Add。
- 核验行号：GameLaunch.cs:325-360；LaunchLoader.cs:212-239；MgrCenter.cs:89-101。
- 短摘录：NormalLaunchCommander(UIHelper.Init)；MgrRegisterCommander(PanelMgr.Inst)；g_mgrCore.Add(manager) 后 manager.Initialize()。
- 支持结论：UIHelper 绑定、PanelMgr 注册和 manager.Initialize 有明确启动调用链；不是只凭目录名判断可达。
- 性质：调用证据 + 定义证据。

### S03-E003 — OLD 运行时绑定消费生成常量

- 所属工程：OLD。
- 相对路径：Assets/Scripts/game/Helper/UIHelper.cs；Assets/Scripts/game/Utils/UIBindProvider.cs；Assets/Scripts/UIBase/A_Main/BaseMainPanel.cs。
- 类/方法/配置字段：UIHelper.Init/Create、UIBindProvider.Get/GetBindVo、BaseMainPanel.URL/PkgName/ResName。
- 核验行号：UIHelper.cs:22-40,67-76；UIBindProvider.cs:18-30,57-106,109-130；BaseMainPanel.cs:1,7,92-94。
- 短摘录：UIBindProvider.Get() 读取 dls.ui.base 类型；GetRawConstantValue() 读取 URL/PkgName/ResName；PanelMgr.BindPanelType(binds)。
- 支持结论：生成类、反射绑定消费方和 FairyGUI URL 扩展注册之间存在运行时契约；编辑器生成过程本身不在本报告范围。
- 性质：定义证据 + 调用证据 + 生成代码证据。

### S03-E004 — OLD PanelMgr 初始化层容器和缓存根

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs。
- 类/方法/配置字段：PanelMgr.Initialize、m_cacheTran、m_container、PanelLayer。
- 核验行号：584-633；2407-2416。
- 短摘录：创建 $Cache_Panel 并 DontDestroyOnLoad；按 PanelLayer.Count 创建 GComponent 容器并加入 GRoot。
- 支持结论：PanelMgr 拥有 UI 层容器和 detached cache 根；初始化依赖此前完成 g_hasBind。
- 性质：定义证据 + 运行时初始化证据。

### S03-E005 — OLD 模块注册窗口/视图并携带事件组

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Module/BaseModule.cs；Assets/Scripts/game/Module/MainScene/MainSceneModule.cs；Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs。
- 类/方法/配置字段：BaseModule.Init/BindPanel/RegisterView、MainSceneModule.OnInit、PanelMgr.RegisterPanel/RegisterView。
- 核验行号：BaseModule.cs:127-143,566-579；MainSceneModule.cs:58,78-115；PanelMgr.cs:855-904。
- 短摘录：m_listenerGroupName = "Module_" + Id；BindPanel<MainPanel>()；RegisterPanel(..., m_dispatcher, m_listenerGroupName, null)。
- 支持结论：OLD 由业务模块把 panel/view 注册到 PanelMgr，并把模块 dispatcher/监听组传入管理器；PanelMgr 负责解析生成基类的包名和资源名。
- 性质：调用证据 + 定义证据。

## OLD 基类与生命周期

### S03-E006 — OLD BasePanel/IPanel 的打开与重复打开契约

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/IPanel.cs；Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel.cs。
- 类/方法/配置字段：IPanel；BasePanel.Initialize/Open/Close/OnRemoveBefore；PanelOption 相关属性。
- 核验行号：IPanel.cs:17-80,83-93；BasePanel.cs:39-76,146-203,205-232。
- 短摘录：if (m_isOpen) { ... OnUpdateParam(param); return true; }；首次成功打开才设置 m_isOpen=true。
- 支持结论：面板具有名称/层/优先级/选项/打开状态契约；同一实例重复打开进入参数刷新；Close 交给 PanelMgr，不直接 Dispose。
- 性质：定义证据 + 调用语义证据。

### S03-E007 — OLD BaseView 的 stage 生命周期

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/BaseView.cs。
- 类/方法/配置字段：BaseView.Initialize、AddToStageHandler、RemoveToStageHandler、OnInit/OnOpen/OnClose。
- 核验行号：8-21,23-64,66-92。
- 短摘录：首次进入 stage 执行 OnInit；移除时执行 OnClose，随后 Timers.inst.RemoveObject(this) 与 RemoveEventListenerByObject(this)。
- 支持结论：BaseView 是嵌入式 GComponent 表现组件，没有 IPanel 的层级/打开状态/缓存接口。
- 性质：定义证据。

### S03-E008 — OLD 异步打开主路径和 opening 标记

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs。
- 类/方法/配置字段：m_openingPanel、OpenPanelAsync(string, object)、PanelInfo.Object。
- 核验行号：1149-1153；1220-1357；2418-2437。
- 短摘录：m_openingPanel.Add(panelName)；异步加载/创建/Initialize/Open 后才 m_openingPanel.Remove(panelName)；Panel.Open 返回 false 的失败分支调用 RemoveUI(info.Object, info)，而创建 null、非 IPanel、非 GObject 分支直接早返。
- 支持结论：opening 集合是状态记录/查询点；打开阶段存在多处在 Remove 之前的早返，且没有看到 finally；成功后才进入层容器与打开事件。
- 性质：定义证据 + 调用证据。

### S03-E009 — OLD UIAwaiter 等待与重复触发语义

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/UIAwaiter.cs。
- 类/方法/配置字段：EnsureHook、EnsureOpenAsync<TPanel>、WaitOpenAsync/WaitCloseAsync、OpenFireAndForget。
- 核验行号：169-214；219-283；288-390；487-517。
- 短摘录：EnsureOpenAsync 仅检查 GetPanelIfLoaded(...).IsOpen，否则直接 OpenPanelAsync；WaitOpenAsync 才把 TCS 放入 s_waitOpen。
- 支持结论：等待打开与触发打开是两种不同能力；等待者列表不等于打开请求合并；多参数 Ensure 重载的异常处理不一致。
- 性质：定义证据 + 调用证据。

### S03-E010 — OLD BasePanel 移除阶段的清理链

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel.cs。
- 类/方法/配置字段：AddToStageHandler、RemoveToStageHandler、OnClose、CancelPanelTasks 调用。
- 核验行号：275-314；347-371；396-409。
- 短摘录：移除 stage 时 OnClose()，随后 Timers.inst.RemoveObject(this)、ModuleMgr.Inst.RemoveEventListenerByObject(this)、Stage.inst.RemoveTouchEvent(this) 和 owner group 移除。
- 支持结论：OLD 基类把逻辑关闭后的表现清理挂在 FairyGUI onRemovedFromStage；OnClose 默认先取消 panel task。
- 性质：定义证据。

## OLD 管理器、层级、缓存和第三方边界

### S03-E011 — OLD Close/Cache/Dispose 分流

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs。
- 类/方法/配置字段：ClosePanel、RemoveUI、ClearCachePanel、UnregPanelByName、PanelInfo.Object。
- 核验行号：911-938；1709-1758；1874-1908；2271-2346；2418-2437。
- 短摘录：缓存分支 ui.parent.RemoveChild(ui)；非缓存分支 ui.parent.RemoveChild(ui, true)；清缓存调用 info.Panel.Dispose()。
- 支持结论：OLD Close 是从打开列表/层容器移除；CanCache 面板保留实例，清缓存/注销才强制 Dispose；PanelInfo 是管理器对实例的持有记录。
- 性质：定义证据 + 调用证据。

### S03-E012 — OLD 层、优先级与返回导航

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelOptions.cs；Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs。
- 类/方法/配置字段：PanelOption、PanelLayer、AddUI、GetTopPanel、CloseTopPanel。
- 核验行号：PanelOptions.cs:10-94；PanelMgr.cs:1055-1087,1651-1701,2213-2265,2407-2416。
- 短摘录：Top/Panel/Bottom；CanCache/DisBack/IgnoreBack/FullScreen；同层按 Priority 插入；IBackHandler.DoBack() 决定是否关闭。
- 支持结论：OLD 把层级、同层顺序、返回过滤和表现 flags 放进 PanelMgr/PanelOption，而不是只取最后打开对象。
- 性质：定义证据 + 调用证据。

### S03-E013 — FairyGUI 对象 Dispose 与项目 Disposable 扩展

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/FairyGUI/Scripts/UI/GObject.cs；GComponent.cs；GComponent_Extend.cs；Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanelDisposableExtensions.cs。
- 类/方法/配置字段：GObject.Dispose、GComponent.Dispose、GComponent.AddDisposable/DisposeDisposables、AutoDispose。
- 核验行号：GObject.cs:1819-1850；GComponent.cs:74-103；GComponent_Extend.cs:16-62；BasePanelDisposableExtensions.cs:23-31。
- 短摘录：GComponent.Dispose 先 DisposeDisposables()；GObject.Dispose 调 RemoveFromParent 和 RemoveEventListeners。
- 支持结论：FGUI 对象树销毁和 Disposable 回收提供底层能力；AutoDispose 是项目添加的适配扩展，不能归类为原始 BasePanel 能力。
- 性质：第三方/项目适配层定义证据。

### S03-E014 — OLD Panel 级异步任务取消

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/BasePanel_CancellationToken.cs。
- 类/方法/配置字段：PanelCancellationToken、PanelToken、CancelPanelTasks。
- 核验行号：10-54。
- 短摘录：关闭时 Cancel()、Dispose() 并置空 _panelCts；token 可链接全局 UniTaskManager。
- 支持结论：OLD 明确提供 panel 生命周期级 token，但需要异步任务主动使用该 token 才能产生取消效果。
- 性质：定义证据。

### S03-E015 — OLD 真实 MainPanel 打开/清理和模块解绑

- 所属工程：OLD。
- 相对路径：Assets/Scripts/game/Module/MainScene/MainSceneModule.cs；Assets/Scripts/game/Module/MainScene/View/MainPanel.cs；Assets/Scripts/framework/Library/ZeroFramework/Module/BaseModule.cs。
- 类/方法/配置字段：MainSceneModule.OnInit/OnEnterBindScene、MainPanel.OnInit/OnOpen/OnClose/RegEvent/UnRegEvent、BaseModule.RemoveAllListener/Dispose。
- 核验行号：MainSceneModule.cs:78-130,364-390；MainPanel.cs:54-74,92-124,225-260,265-275,365-380；BaseModule.cs:381-421。
- 短摘录：await UIAwaiter.EnsureOpenAsync<MainPanel>()；OnClose 调 base.OnClose() 后 UnRegEvent/UnRegClickEvent 并移除 timer；模块释放调用 PanelMgr.Inst.UnregPanelByGroup(m_listenerGroupName)。
- 支持结论：存在一条真实窗口的注册→场景进入打开→OnClose 清理→模块组注销链；业务实现只被用于证明框架 hook 和清理责任。
- 性质：调用证据 + 业务调用方证据。

### S03-E016 — OLD 方向重排只遍历打开列表

- 所属工程：OLD。
- 相对路径：Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr_AutoRotate.cs。
- 类/方法/配置字段：InitAutoRotate、OnOrientationChange、UpdatePanelsLayout。
- 核验行号：10-38；91-187。
- 短摘录：方向变化后 await UniTask.NextFrame()，遍历 m_panelList 更新布局和子组件方向；扩展收到 OnOrientationChanged。
- 支持结论：方向处理属于 PanelMgr/扩展层，静态可见对象是打开列表；异步打开和 detached cache 的完整一致性没有得到证明。
- 性质：定义证据。

## CURRENT UI 栈

### S03-E017 — CURRENT UIModule 初始化、栈和释放

- 所属工程：CURRENT。
- 相对路径：Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs。
- 类/方法/配置字段：UIModule、OnInit、OnRelease、_uiStack、CloseAll、OnUpdate。
- 核验行号：15-65；96-114；425-434；712-730。
- 短摘录：private readonly List<UIWindow> _uiStack；GameObject.Find("UIRoot")；释放时 CloseAll(isShutDown:true)；IUpdate.OnUpdate 遍历栈。
- 支持结论：CURRENT UI 管理器是 GameLogic 热更程序集内的 Singleton/IUpdate，持有当前窗口栈并在模块释放时销毁栈内窗口。
- 性质：定义证据。

### S03-E018 — CURRENT UIBase/Widget 的 hook、父子和事件池

- 所属工程：CURRENT。
- 相对路径：Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs；Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs；Assets/TEngine/Runtime/Core/GameEvent/GameEventMgr.cs。
- 类/方法/配置字段：UIBase hooks、AddUIEvent/RemoveAllUIEvent、UIWidget.CreateImp/OnDestroyWidget、GameEventMgr.Clear。
- 核验行号：UIBase.cs:18-24,106-197,284-330；UIWidget.cs:71-140,201-233,276-311；GameEventMgr.cs:9-49。
- 短摘录：InternalDestroy 递归子组件、归还 _eventMgr 并调用 OnDestroy；Widget 创建后加入 Parent.ListChild。
- 支持结论：CURRENT 由 UIBase 提供表现 hooks 和 UI 自有事件回收，UIWidget 是父 UI 持有的递归组件，不是独立窗口。
- 性质：定义证据 + TEngine 基础能力调用证据。

### S03-E019 — CURRENT UIWindow 加载、关闭加载中窗口和最终销毁

- 所属工程：CURRENT。
- 相对路径：Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs。
- 类/方法/配置字段：InternalLoad、InternalCreate、InternalDestroy、Handle_Completed、CancelHideToCloseTimer。
- 核验行号：300-349；356-425；427-502；504-522。
- 短摘录：异步加载调用 LoadGameObjectAsync(location, parent: UIModule.UIRoot)；销毁时 Object.Destroy(_panel)；完成回调若 IsDestroyed 则销毁迟到 panel。
- 支持结论：窗口加载不传取消 token；Close 先销毁逻辑实例/对象并由迟到完成回调兜底销毁 GameObject；null 结果不会设置 IsLoadDone。
- 性质：定义证据。

### S03-E020 — CURRENT 类型去重、刷新、层级和 Hide/Close

- 所属工程：CURRENT。
- 相对路径：Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs；Assets/GameScripts/HotFix/GameLogic/Module/UIModule/WindowAttribute.cs。
- 类/方法/配置字段：ShowUIImp/TryGetWindow/ShowUIAwaitImp、CloseUI/HideUI、OnWindowPrepare、Push、WindowAttribute。
- 核验行号：UIModule.cs:250-363,370-420,472-558,565-664,666-710；WindowAttribute.cs:8-77。
- 短摘录：已有类型被 Pop 后 Push 并 TryInvoke；HideTimeToClose<=0 直接 CloseUI；WindowAttribute 提供 layer/location/fullscreen/fromResources/延迟关闭字段。
- 支持结论：同一 Type.FullName 的窗口在栈中去重并置顶；重复打开加载中实例只更新数据/回调；Close 销毁而 Hide 可暂时隐藏；扩展窗口依赖继承、new/Activator 和属性元数据。
- 性质：定义证据 + 调用语义证据。

### S03-E021 — CURRENT Canvas 深度、全屏遮挡和更新筛选

- 所属工程：CURRENT。
- 相对路径：Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs；Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs。
- 类/方法/配置字段：UIWindow.Depth/Visible/Interactable、UIModule.OnSortWindowDepth/OnSetWindowVisible/OnUpdate。
- 核验行号：UIWindow.cs:87-218；UIModule.cs:480-516,712-730。
- 短摘录：Visible 同时切换 Canvas layer 与子 raycaster；全屏窗口从栈顶向下使下方窗口不可见；更新调用 window.InternalUpdate()。
- 支持结论：CURRENT 将表现可见性、交互性、Canvas 深度和全屏遮挡保留在窗口实例/管理器，不以销毁实现遮挡；更新由 SingletonSystem 的 IUpdate 链接到 UIModule。
- 性质：定义证据。

### S03-E022 — CURRENT Launcher UI 是独立轻量层

- 所属工程：CURRENT。
- 相对路径：Assets/Launcher/Scripts/LauncherMgr.cs；Assets/Launcher/Scripts/UIBase.cs；Assets/Launcher/Launcher.asmdef；Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef。
- 类/方法/配置字段：LauncherMgr.ShowUI/CloseUI/HideAllUI、两个程序集的 name/rootNamespace。
- 核验行号：LauncherMgr.cs:11-66,68-114；UIBase.cs:8-56；Launcher.asmdef:2-16；GameLogic.asmdef:2-30。
- 短摘录：Launcher 以 Resources.Load("UIWindow/" + uiName) 创建；Close 后 DestroyImmediate 和 m_uiMapDict.Remove(uiName)。
- 支持结论：Launcher UI 与 GameLogic.UIModule 分属不同程序集和管理器；Launcher 的字典是短期活动对象表，Close 即销毁，不可当成 GameLogic 窗口缓存。
- 性质：定义证据 + 程序集配置证据。

### S03-E023 — CURRENT 资源接口和计时器所有权边界

- 所属工程：CURRENT。
- 相对路径：Assets/GameScripts/HotFix/GameLogic/Module/UIModule/IUIResourceLoader.cs；Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs；Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs；Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs。
- 类/方法/配置字段：IUIResourceLoader.LoadGameObject/LoadGameObjectAsync、IResourceModule.LoadGameObjectAsync、ITimerModule.AddTimer/RemoveTimer。
- 核验行号：IUIResourceLoader.cs:11-32,38-66；IResourceModule.cs:211-259；ITimerModule.cs:3-64；TimerModule.cs:40-57,232-264。
- 短摘录：资源异步接口接收 CancellationToken；timer 接口通过 timerId 添加/移除，没有 owner 参数；UIWindow 的调用行见 UIWindow.cs:320-323。
- 支持结论：CURRENT 基础资源能力支持取消、资源模块文档定义实例销毁后的释放边界；UIWindow 没有把 token 传入，通用 timer 也不会自动按窗口清理。
- 性质：接口定义证据 + 调用对照证据。

### S03-E024 — CURRENT GameApp/UIRoot 的启动可达性

- 所属工程：CURRENT。
- 相对路径：Assets/GameScripts/HotFix/GameLogic/GameApp.cs；Assets/GameScripts/HotFix/GameLogic/GameModule.cs；Assets/GameScripts/HotFix/GameLogic/SingletonSystem/Singleton.cs；Assets/GameScripts/HotFix/GameLogic/SingletonSystem/SingletonSystem.cs；Assets/GameScripts/Procedure/ProcedureLoadAssembly.cs；Assets/Scenes/main.unity。
- 类/方法/配置字段：GameApp.Entrance/StartGameLogic、GameModule.UI、Singleton<T>.Instance、SingletonSystem.BuildLifeCycle、AllAssemblyLoadComplete、场景 m_Name。
- 核验行号：GameApp.cs:25-45；GameModule.cs:60-65；Singleton.cs:13-24,50-62；SingletonSystem.cs:98-105,212-248,337-343；ProcedureLoadAssembly.cs:124-150；main.unity:260-265,317-322。
- 短摘录：反射获取 GameApp.Entrance 后调用；StartGameLogic 调 GameModule.UI.ShowUIAsync<BattleMainUI>()；场景修改记录含 m_Name: UIRoot。
- 支持结论：CURRENT 的代表性 UI 打开调用具有程序集入口→GameApp→UIModule 的代码链，场景文本至少确认了 UIRoot prefab 实例命名；Canvas 具体层级未在本轮展开验证。
- 性质：调用证据 + 生命周期定义证据 + 场景配置证据。

## 证据使用提醒

- OLD FairyGUI 文件位于 Assets/Scripts/framework/FairyGUI，本报告将其写成“第三方原始能力与项目改造混合边界”，没有把 GComponent_Extend 或 BasePanelDisposableExtensions 误归类为纯第三方原始 API。[S03-E013]
- S03-E015 的 MainPanel 只用于证明业务窗口确实使用基类 hook、订阅和计时器清理；报告不从这些行推导任何业务功能。
- S03-E019、S03-E023 的异步结论是控制流静态分析；异常、取消和 FairyGUI/资源模块真实时序尚未运行验证。
