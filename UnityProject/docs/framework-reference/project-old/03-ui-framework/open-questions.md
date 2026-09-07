# 待确认问题与后续验证

本页只记录静态源码、项目配置和可取得的版本信息仍不能闭合的问题。问题不是已证实的缺陷；除非另有说明，本文对运行时行为只作静态推断。每项均给出已查位置和最小后续验证方法，便于主审或后续领域任务复查。

| 编号 | 待确认事项 | 已查位置与当前判断 | 最小后续验证方法 | 是否阻塞后续决策 |
| --- | --- | --- | --- | --- |
| S03-OQ001 | OLD 中 FairyGUI 的 onRemovedFromStage 触发顺序、是否始终晚于 PanelMgr.RemoveUI 以及多次移除时序 | 已查 OLD BasePanel、PanelMgr、FairyGUI GComponent/GObject。代码确认 BasePanel 在 onRemovedFromStage 中执行 OnClose、计时器和事件清理，但精确事件派发时序未由静态代码闭合，见 S03-E010、S03-E013。 | 在隔离场景打开一个最小 BasePanel，记录 RemoveChild、onRemovedFromStage、OnClose、Dispose 的时间顺序；重复执行关闭、切场景和父节点销毁。 | 对 OLD 清理时序结论有条件阻塞；不阻塞文档中的接口形状结论。 |
| S03-OQ002 | OLD 并发打开同一窗口时，m_openingPanel 是否会在真实失败或异常路径中残留 | 已查 PanelMgr.OpenPanelAsync、UIAwaiter 和所有同名入口。代码确认存在静态打开标记，但未发现统一 finally；若提前返回或异常发生在移除标记前，存在残留风险，见 S03-E008、S03-E009。 | 用可控延迟和可控失败的包加载替身，交错两次同类型 OpenPanelAsync，分别覆盖空对象、非 IPanel、加载异常和正常完成。 | 阻塞 S03-C003 的最终取舍；不阻塞“当前实现不存在同形 opening 标记”的比较。 |
| S03-OQ003 | CURRENT 在 UIWindow 资源加载期间关闭时，底层资源请求是否被取消，还是仅由晚到的完成回调销毁对象 | 已查 UIWindow.InternalLoad、InternalDestroy、Handle_Completed、IUIResourceLoader 和 IResourceModule。代码确认 UIWindow.InternalLoad 未把取消令牌传入资源层；关闭设置 IsDestroyed，晚到回调再销毁 GameObject，见 S03-E019、S03-E023。 | 在测试资源加载器中注入延迟，打开后立即 CloseUI，检查 CancellationToken、资源句柄释放、完成回调和 GameObject 数量。 | 阻塞 S03-C002 的实现级建议，也影响 S03-C003 的失败策略。 |
| S03-OQ004 | CURRENT 资源加载返回 null 或窗口缺少 Canvas 时，异步任务、日志和 IsLoadDone 的最终状态 | 已查 UIWindow.Handle_Completed 与 UIModule.ShowUIImp/ShowUIAwaitImp。代码确认 null 分支直接返回，未见 IsLoadDone=true 或回调失败协议；Canvas 缺失会抛异常，ShowUIAsync 使用 Forget，见 S03-E019、S03-E020。 | 构造 null 资源、缺 Canvas prefab、加载异常三类测试，观察任务异常、回调、栈日志和后续同名重开。 | 阻塞 S03-C003 的错误契约建议；不影响已确认的正常路径。 |
| S03-OQ005 | CURRENT 场景 UIRoot prefab 的 Canvas、事件相机、层级和持久化组件是否与 UIModule 假设完全一致 | 已查 main.unity 对 UIRoot 的 prefab 实例修改和 UIModule.OnInit；未继续解析引用 prefab 的全部组件，见 S03-E021、S03-E024。 | 打开 UIRoot 所引用 prefab 与场景，逐项核对 Canvas、GraphicRaycaster、Camera、层级和 DontDestroyOnLoad 预期。 | 只阻塞运行时层级/输入结论；不阻塞代码层生命周期结论。 |
| S03-OQ006 | CURRENT GetTopWindow、隐藏窗口、加载中窗口和全屏窗口之间的返回语义是否符合所有输入系统调用者预期 | 已查 UIModule.GetTopWindow、Push、HideUI、全屏可见性更新，并做了范围内调用搜索；缺少输入系统和产品级导航调用场景，见 S03-E020、S03-E021。 | 读取实际输入/返回键调用者，建立“顶层”“可交互顶层”“正在加载顶层”三种需求，再用多层窗口矩阵测试。 | 条件阻塞 S03-C005；当前层排序结论不阻塞。 |
| S03-OQ007 | CURRENT UIBase 的 GameEventMgr 在 OnDestroy 后通过 MemoryPool.Release 是否会再次安全初始化，若允许实例复用会不会持有旧状态 | 已查 UIBase 事件懒加载、RemoveAllUIEvent、GameEventMgr.Clear 和 UIWindow.Destroy；代码确认销毁时清理事件，但未确认 MemoryPool 实现的字段重置与复用约束，见 S03-E018、S03-E019。 | 连续创建、注册、销毁、池化复用同一 UIBase，检查 EventMgr、子组件列表、回调和可见性字段。 | 仅阻塞 S03-C004 的池化/缓存扩展；不阻塞默认销毁建议。 |
| S03-OQ008 | OLD BasePanel.OnShow 是否由统一管理器可靠调用，还是仅为扩展钩子保留 | 已查 BasePanel.OnShow 定义、PanelMgr 显示路径和范围内调用结果；暂未形成统一调用证据，见 S03-E006、S03-E010。 | 在 OLD 全仓库中限定搜索 OnShow 调用点，并在最小窗口上记录打开、显示、重新显示的钩子顺序。 | 不阻塞当前文档；影响是否把 OnShow 作为可复刻公共契约。 |
| S03-OQ009 | OLD PanelMgr.OnDispose 未直接遍历全部窗口时，实际 MgrCenter、ModuleMgr、场景切换顺序是否总能先清理窗口 | 已查 GameLaunch、MgrCenter、ModuleMgr、BaseModule 和 PanelMgr。代码确认普通模块释放会注销面板组，而 PanelMgr 自身 OnDispose 没有明显的 CloseAll 调用，见 S03-E002、S03-E005、S03-E015。 | 在隔离启动/退出或场景切换流程中记录模块释放顺序；覆盖模块注册失败和重复释放。 | 阻塞独立重启/热重载场景的结论；不阻塞普通关闭链。 |
| S03-OQ010 | OLD 缓存窗口与方向旋转、异步打开之间是否存在状态同步遗漏 | 已查 PanelMgr 缓存分支、PanelMgr_AutoRotate 和 BasePanel orientation 接口；静态代码不足以证明缓存实例恢复后的布局状态，见 S03-E011、S03-E016。 | 交错横竖屏、缓存关闭、重新打开和异步加载，记录尺寸、层级、visible 和扩展回调。 | 阻塞缓存/旋转组合的推荐；不阻塞“CanCache 是显式策略”结论。 |
| S03-OQ011 | CURRENT IResourceModule 的“销毁自动卸载”注释是否与实际 AssetHandle/实例所有权完全一致 | 已查 IUIResourceLoader 和 IResourceModule 接口及注释，资源句柄内部实现属于资源领域，见 S03-E023。 | 由资源领域任务核对 UIResourceLoader 的实例化、句柄绑定、取消和 Object.Destroy 的实际实现及日志。 | 阻塞 S03-C002、S03-C004 的资源所有权细节；不阻塞 UIWindow 自身状态事实。 |
| S03-OQ012 | OLD UIProject 是否包含影响运行时 UI 绑定或包生成的入口，而不是仅包含工程设置和 SVN 元数据 | 已查 UIProject 目录顶层、Settings 与 SVN 信息，并核对生成 BaseMainPanel 的运行时常量；未做素材全量扫描，见 S03-E003、S03-E024。 | 由编辑器/生成领域任务按 UIProject 工具入口、生成脚本和绑定配置做限定核验，不扫描素材。 | 不阻塞本领域运行时文档；阻塞生成约定的最终归属判断。 |

## 推断与决策边界

当前文档可以直接支持的事实包括：两个工程都使用同一 Unity 版本；OLD 有基于 FairyGUI 的 PanelMgr、BasePanel、BaseView 与模块注册链；CURRENT 有 UIModule、UIWindow、UIWidget 和类型栈；两套实现的关闭、缓存、事件清理和异步加载契约不同，见 S03-E001、S03-E004、S03-E017、S03-E018。

不能从静态代码直接升级为“已验证运行时保障”的内容包括：OLD 的 stage 事件精确时序、OLD 并发 opening 标记最终一致性、CURRENT 加载取消是否下沉至资源句柄、CURRENT null/异常错误回调，以及 UIRoot prefab 的组件细节。候选文档已将这些内容标记为验证或条件建议，没有把缺失保障写成现有能力。

跨领域问题只保留接口边界：资源所有权和句柄行为交给资源领域，生成绑定入口交给编辑器/生成领域，通用事件实现交给事件领域；本页不复制这些领域的完整研究。
