# FairyGUI 与 UGUI 并存接入计划

日期：2026-09-07。用户目标：通用框架可靠性与长期维护；明确选择“两套并存，新增界面可用 FairyGUI”。本轮由 master-planner 做规划，只写本计划及研究导航，未安装 SDK 或改生产代码。旧研究中“不迁移 FairyGUI”的判断适用于当时未提出 FGUI 需求的范围；本次用户的新需求建立独立接入契约，仍保留 UGUI。

## 结论与代码清单

采用独立 `GameModule.FGUI`，保留 `GameModule.UI` 及既有调用。引入官方 FairyGUI SDK，新写 TEngine 适配层。SDK、项目资源适配、窗口控制器、生成视图分层；不让现有 UIWindow 承担两种渲染对象。

| 代码块 | 处理方式 | 主要责任 | 首轮必要性 |
| --- | --- | --- | --- |
| 官方 FairyGUI Unity SDK | 引入固定版本，保留许可证；含运行时、Editor、所需 shaders/内置资源与 asmdef | GObject/GComponent/GRoot、UIObjectFactory、UIPackage、渲染/文本/动效/输入 | 必需 |
| FguiSettings / FguiPackageCatalog | 新写 AOT 数据定义和编辑器生成/校验 | 设计分辨率、包名/id、描述文件、依赖、发布前缀、资产类型/完整地址 | 必需 |
| FguiResourceProvider / FguiAssetLease | 新写，调用当前 IResourceModule 的 raw handle API | 异步取得 TextAsset/Texture/AudioClip；唯一持有/释放 handle | 必需 |
| FguiPackageService / FguiPackageLease | 新写，参考旧包依赖思想 | 包与依赖的预加载、并发合并、引用、失败回滚、RemovePackage 顺序 | 必需 |
| FguiModule / FguiWindow / FguiWidget / FguiWindowDescriptor | 新写，保留当前类型化访问习惯 | 窗口注册、打开/隐藏/关闭、ready、层级、子组件、取消、清理 | 必需 |
| FguiLifetimeScope | 小型局部组合类 | GameEventMgr、FGUI listener、timer、CTS 与额外释放动作的登记 | 必需；不新造全局任务中心 |
| FguiExternalLoader | 新写 GLoader 扩展，不改 SDK | 外部图标异步加载、URL 替换、迟到结果/销毁时释放 | 必需，首轮只支持本地 YooAsset 资产 |
| FguiRuntimeHost / FguiInputBridge | 新写 AOT 组件＋热更配置入口 | Stage/相机/缩放/安全区、UGUI 输入阻挡、退出清理 | 必需 |
| FguiEditorTools / FguiCatalogBuilder / 生成 Binder | 新写或使用官方生成方式 | 发布目录校验、完整地址、显式绑定、生成文件保护 | 必需 |
| GameModule / GameApp / GameLogic.asmdef / collector | 小范围接入 | facade、注册入口、程序集依赖、FGUI 资源组 | 必需修改点 |
| RootModule 生命周期通知 | 小范围中立扩展 | 在模块资源 shutdown 前通知 FGUI 释放；Editor 也有明确路径 | 必需修改点，见退出契约 |
| 最小编辑器源工程、发布资产、示例、测试 | 新建无业务样本 | 两窗口共享 Common 包、外部图标、UGUI 并存、Player 验证 | 必需交付 |
| 旧 PanelMgr/BasePanel/FGUIResourceMgr/UIHelper | 只读参考 | 提供边界设计样本 | 不复制 |
| 旧 UI/业务程序集/分支脚本/图集扫描卸载 | 排除 | 旧产品相关行为 | 不进入首轮 |

官方发布页本轮显示 v5.2.0 为 Latest，可作为待锁定版本候选。**本轮未取得该版本完整源码，不能把 OLD 的扩展 API 当作 v5.2.0 的已验证签名。** 实施首步须解析真实 tag/commit、记录来源并核对扩展点；不静默跟随 master/latest。[官方发布页](https://github.com/fairygui/FairyGUI-unity/releases)

官方说明包移除会影响仍使用包内容的组件；输入采用自身命中机制。因此包引用管理和 UGUI 输入互操作都是接入工作，不能只添加 SDK。[包加载与卸载说明](https://www.fairygui.com/docs/unity)、[输入说明](https://www.fairygui.com/docs/unity/input)

## 已核验的工程事实

以下是本次新增定向核验，编号 FGUI-E001 起；原研究仍保留 S03/S02 等编号。

| 编号 | 完整源码位置/符号 | 事实与设计影响 |
| --- | --- | --- |
| FGUI-E001 | [IUIResourceLoader.cs:12](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/IUIResourceLoader.cs:12>)，12–41；[UIWindow.cs:464](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs:464>)，464–501 | 旧接口仅加载 GameObject，窗口检查 Canvas/GraphicRaycaster；不适合作为 GComponent 的资源或窗口基类 |
| FGUI-E002 | [GameModule.cs:63](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/GameModule.cs:63>)，63–65、103–117 的 UI facade 与 Shutdown；[UIModule.cs:15](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs:15>)，15–114 | UIModule 是热更 Singleton，管理独立 UIRoot；新增平行 facade 比修改其资源协议侵入更小 |
| FGUI-E003 | [IResourceModule.cs:279](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs:279>)，279–281；[ResourceModule.cs:1179](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs:1179>)，1179–1193；[HandleBase.cs:18](<E:/MyWork/MyFramework/TEngine/UnityProject/Packages/YooAsset/Runtime/ResourceManager/Handle/HandleBase.cs:18>)，18–40、63–123 | LoadAssetAsyncHandle 返回未进入 TEngine AssetObject 池的 handle；应由适配层 Dispose，不配对 UnloadAsset |
| FGUI-E004 | [LoadMgr.cs:1576](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Load/LoadMgr.cs:1576>)，1576–1642；[PanelMgr.cs:328](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/Library/ZeroFramework/Panel/PanelMgr.cs:328>)，328–415 | 旧实现先加载描述再 AddPackage，并显式遍历依赖；该异步回调仅处理 Texture.Reload，不能直接作为通用包资源加载器 |
| FGUI-E005 | [GLoader.cs:1](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/FairyGUI/Scripts/UI/GLoader.cs:1>)，1–7、495–536；[UIPackage.cs:1](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/framework/FairyGUI/Scripts/UI/UIPackage.cs:1>)，1–35 | OLD SDK 文件已经引用 IGG、UniTask 及项目扩展，不是干净的官方 SDK；不能整目录复制 |
| FGUI-E006 | [UIHelper.cs:23](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/game/Helper/UIHelper.cs:23>)，23–88；[UIBindProvider.cs:18](<D:/Work/SAUnity/ProjectOld/Assets/Scripts/game/Utils/UIBindProvider.cs:18>)，18–45；[GenCode_CSharp.lua:1](<D:/Work/SAUnity/ProjectOld/UIProject/plugins/发布代码/GenCode_CSharp.lua:1>)，1–79 | 旧绑定依赖 dls.ui.base/业务程序集反射；发布脚本清理目标 cs，含 portrait 特化并取消 Binder 生成；仅借鉴 URL/package/component 元数据 |
| FGUI-E007 | [AssetBundleCollectorSetting.asset:18](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset:18>)，18–130；[DefaultAddressRule.cs:15](<E:/MyWork/MyFramework/TEngine/UnityProject/Packages/YooAsset/Editor/AssetBundleCollector/DefaultRules/DefaultAddressRule.cs:15>)，15–41 | 当前 DefaultPackage 各组多用去扩展名文件地址，FGUI 不应凭包名拼出短地址；新增专属规则且保留现有组 |
| FGUI-E008 | [UIRoot.prefab:76](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Settings/Prefab/UIRoot.prefab:76>)，76–118、179–207；[GraphicsSettings.asset:42](<E:/MyWork/MyFramework/TEngine/UnityProject/ProjectSettings/GraphicsSettings.asset:42>)；[ProjectSettings.asset:916](<E:/MyWork/MyFramework/TEngine/UnityProject/ProjectSettings/ProjectSettings.asset:916>) | 当前 UGUI 根为 Camera 模式，设计尺寸 750×1334、UI 相机 depth=2；Graphics/Quality 配置未启用 SRP，activeInputHandler=0。首轮以当前 Built-in/旧 Input 基线验证，不能宣称 URP/InputSystem 通用兼容 |
| FGUI-E009 | [RootModule.cs:156](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/RootModule.cs:156>)，156–167；[GameApp.cs:25](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/GameApp.cs:25>)，25–46；[UpdateDriver.cs:24](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Module/UpdataDriver/UpdateDriver.cs:24>)，24–37、443–452 | 当前 GameApp 销毁监听可能先被清空，不能只把 FGUI 放进 SingletonSystem 就宣布退出安全；需要资源 shutdown 之前的确定通知 |
| FGUI-E010 | [GameLogic.asmdef:1](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef:1>)；[TEngine.Runtime.asmdef:1](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/TEngine.Runtime.asmdef:1>)；[GameEventMgr.cs:9](<E:/MyWork/MyFramework/TEngine/UnityProject/Assets/TEngine/Runtime/Core/GameEvent/GameEventMgr.cs:9>)，9–78 | 保持 GameLogic 向底层依赖；局部事件登记已有实现，FGUI 不必引入 IGG 的 dispatcher |

## 架构与建议目录

下列目录是待实施位置，本轮未创建。新增代码随相关 asmdef 编译；不以目录名称推定热更边界。

```text
Assets/ThirdParty/FairyGUI/                         官方 SDK、许可证、固定版本记录
Assets/TEngine/Extensions/FairyGUI/Runtime/          AOT：Settings/Catalog、Host、互操作组件
Assets/TEngine/Extensions/FairyGUI/Editor/           Editor-only：Catalog/导入校验工具
Assets/GameScripts/HotFix/GameLogic/Module/FguiModule/
  FguiModule / FguiWindow / FguiWidget
  FguiPackageService / FguiResourceProvider
  FguiLifetimeScope / FguiExternalLoader
Assets/GameScripts/HotFix/GameLogic/UI/FGUI/Gen/      生成视图与 Binder
Assets/GameScripts/HotFix/GameLogic/UI/FGUI/Imp/      手写窗口/组件控制器
Assets/AssetRaw/FGUI/Packages/<Package>/              发布描述、图集、声音等
Assets/AssetRaw/FGUI/External/                       测试外部图标
Assets/AssetRaw/FGUI/FguiPackageCatalog.asset         生成的运行时目录
UIProject/FguiIntegrationSample/                    新的最小 .fairy 源工程/发布设置
Assets/Tests/FairyGUI/                              独立测试程序集/测试资产
```

依赖方向：

```text
GameModule.UI  → 现有 UIModule → Canvas/Prefab              保留
GameModule.FGUI → FguiModule → FguiWindow + generated GComponent
                         └→ FguiPackageService
                            └→ FguiResourceProvider
                               └→ GameModule.Resource.LoadAssetAsyncHandle
GameLogic → TEngine.FairyGUI → FairyGUI SDK + TEngine.Runtime
Editor 工具 → AOT Catalog/SDK；不得反向引用 GameLogic 业务
```

SDK 与 AOT 桥接放主包；FGUI 模块、控制器、生成视图/Binder 在已有 GameLogic 热更程序集。不新增热更 DLL 名单，不把 SDK 设为热更程序集，不让 TEngine.Runtime 直接引用 GameLogic。如 SDK 官方没有运行时 asmdef，只增加明确的程序集边界文件并记录，不把旧框架引用带入 SDK。

## 核心设计契约

### 1. 资源层：显式句柄持有

使用 `GameModule.Resource.LoadAssetAsyncHandle(location, type, yooPackageName)`；不再调用 YooAssets.Initialize/CreatePackage，也不通过旧 LoadMgr/FGUIResourceMgr。专用 FguiResourceProvider 负责 await、状态校验、取消和一次性 Dispose。

这里采用 raw handle 模式，**不再对同一资产调用 ResourceModule.UnloadAsset**，避免把未入 TEngine 池的句柄误做 Unspawn。失败和取消即释放未转交句柄；成功返回 lease，只有 lease owner 释放。等待可使用当前 handle 的 IsDone/Status/LastError 加 UniTask，不依赖本地存在签名疑问的带 cancellationToken ToUniTask 重载。Completed 若用于实现，必须覆盖同步触发、解绑和 Dispose 的顺序。

取消等待不承诺中断所有底层 IO；确保迟到结果不会写入已关闭窗口，也不会重新登记已退出的包。所有 Unity/FGUI 操作在主线程。

### 2. 包层：首轮预加载，避免“返回窗口但图集仍未完成”

首轮选择：从 Catalog 获取依赖和完整资产清单 → 异步取得该依赖闭包中的描述与资源 → 在内存中准备 lookup → AddPackage 的自定义同步资源回调只查询内存 → 创建组件。同步回调中禁止文件 IO、Resources.Load、LoadAssetSync 或 WaitForAsyncComplete。

这一选择让 ready/失败可以判定，代价是首次开包加载其清单全部资源；不承诺节省内存或提高速度。暂不做旧工程按当前显示树扫描图集并卸载的策略。将来按需异步 atlas 加载另立契约。

- Catalog 明确每包 id/name、YooAsset package、描述地址、assetNamePrefix、依赖及资源 lookup key/type/address；运行时与发布校验使用同一映射。
- package name/id 在一个 FGUI 会话全局唯一。不同 YooAsset 包中出现同名/id 也要报冲突，不能只在自建字典用复合 key 就忽略 SDK 的全局注册。
- 依赖必须存在；首轮明确拒绝循环依赖并报告完整环路。不能像 OLD 一样记录依赖错误后继续创建窗口。
- 首次加载保留临时引用，全部准备成功才转交包 lease；失败按逆依赖顺序回滚本次独占登记，保留其他窗口持有的共享包。
- 同包并发共享准备操作，每个窗口得到独立 lease；一个等待者取消只撤销自己的等待/保留，不取消其他 owner 的包。
- 没有窗口/组件/加载保留时卸载；显式 Pin lease 可让 Common 常驻，必须可解除。Hide 仍持有包；Close 最终释放。
- 释放顺序为：销毁该 owner 的 GObject → 释放外部资源 → 最后一个 package owner 触发 RemovePackage → 释放原生资产 handles → 释放依赖 leases。
- 设置 SDK wrapper 的原生资源销毁策略为由适配层管理（使用锁定 SDK 的公开 API，预期 DestroyMethod.None）。不得让 SDK Destroy/Resources.UnloadAsset 原生纹理，同时自己又 Dispose YooAsset handle。
- runtime 仅本模块能 Add/Remove 它拥有的包；直接创建脱离 lease 的 GObject 不属于受支持 API。受支持的动态组件同样通过工厂获得 owner。
- 不支持同一会话替换已加载包版本；热更下载使用现有启动流程，在下一次初始化使用新产物。

### 3. 窗口层：控制器与生成 GComponent 分离

`FguiWindow` 是框架控制器，不继承现有 UIWindow。生成类继续继承 GComponent/GButton 等 SDK 类型，通过组合持有；`FguiWidget` 管理子组件行为。现有 UGUI UIBase 的 Transform/Canvas 逻辑不带入新基类。

首轮公共 API（可采用同义命名，改变语义须报冲突）：

```csharp
UniTask InitializeAsync(FguiSettings settings, CancellationToken cancellationToken = default);
UniTask<TWindow> ShowAsync<TWindow>(object userData = null,
    CancellationToken cancellationToken = default) where TWindow : FguiWindow;
void Hide<TWindow>() where TWindow : FguiWindow;
void Close<TWindow>() where TWindow : FguiWindow;
bool IsOpen<TWindow>() where TWindow : FguiWindow;
void CloseAll();
void Shutdown(); // 同步取消/收尾且幂等，不等待 Unity 退出中的后续帧
```

- 框架注册显式 Type → factory、package/component、层级/模态描述。binder 先注册，组件后创建；不扫描旧 dls.* 程序集。
- 每类型一个窗口实例/一个正在创建的 generation。已有窗口仍加载时等待同一完成结果，不能立即返回。
- 首次创建中的多个 Show：第一次请求的数据用于初始化，其他请求只加入等待；准备完成后的 Show 再刷新数据并置顶。接口文档明确这一规则。
- 取消某次 Show 只取消该调用者等待；若创建中已无任何等待者，则取消本窗口 generation 的创建。Close/Shutdown 取消该窗口全部等待者。已显示窗口不会因某个调用者 token 后续取消而自动关闭。
- ready 必须包含依赖/静态资源加载成功、创建与绑定完成、OnCreate/首次 OnRefresh 成功、挂接到所属层并可交互。空资源、类型不符、hook 异常均失败抛错，不返回 null/半准备对象。
- 不使用 Time.deltaTime 模拟超时；使用可配置的非缩放时间预算，超时有独立错误类型。调用方取消抛 OperationCanceledException；加载错误携带 stage、package、location 和内层异常。
- Close：先标记关闭并取消 lifetime，再解绑事件/计时器、销毁子控制器和视图、归还 lease；try/finally 确保某个 hook 抛错不跳过后续清理。
- Hide 只影响可见/可交互，保留对象与 lifetime。首轮没有自动缓存已 Close 的窗口，没有旧 PanelOption、开关动画回调或跨系统返回栈。
- SDK 的 Transition/GList 等由视图正常使用；不为首轮新增框架级动效/虚拟列表包装。

### 4. 局部生命周期与外部图标

FguiLifetimeScope 组合现有 GameEventMgr、FGUI listener removal、TimerModule id 与 CTS。提供与当前习惯接近的 AddUIEvent；窗口/组件关闭保证解绑。FGUI 的 onClick 和 GameEvent 是不同通道，不能把 SDK 事件转成旧 EventDispatcher。新 scope 优先普通对象；如果用 MemoryPool，Release 后清字段并测试再次访问，不能复制旧陈旧引用风险。

FguiExternalLoader 通过官方 GLoader 扩展点注册，支持 `asset://<catalog-key>`；`ui://` 保留 SDK 包内解析。未知 scheme 明确失败；HTTP/头像网络缓存不在首轮。每个 URL generation 拥有一个资源 lease；URL 变更/Dispose 时取消旧请求，迟到结果立刻归还；创建 NTexture 后由适配层管理 native texture 句柄。通过 LoadExternal/FreeExternal 或锁定版本等价公开 API 实现，不改写 vendor GLoader。

### 5. 两套 UI 的显示与输入

首轮采用两个独立窗口栈，FGUI 业务 UI 整体位于 UGUI 业务 UI 之上；FGUI 内部保留 Bottom/UI/Top/Tips/System 五层意图，但不与 UGUI 的同名层交叉插入。跨后端逐窗口排序、世界空间 HUD、URP 相机栈不在首轮。启动器在 FGUI 初始化之前继续使用现有 UGUI。

- 当前基线是 Built-in，FGUI 独立 Stage camera/渲染 layer，禁止只复制现有 Canvas.sortingOrder 数值假设跨相机排序有效。
- 设置 750×1334 默认设计尺寸，可配置；旋转/窗口 resize 更新缩放和 safe-area 容器。FGUI 启用只影响自身 camera/layer。
- 加入最小输入互操作桥：FGUI 命中区域阻挡下层 UGUI；模态窗口阻挡整个业务 UGUI；空白可穿透区域让 UGUI 正常响应。
- 可采用 UGUI 透明 raycast shield + ICanvasRaycastFilter，使用当前 screenPoint 对 FGUI 公共 hit-test 查询；不能只检查上一帧全局 touchTarget。该方案是实施起点，必须以鼠标、多点触摸、拖拽、滚轮与焦点试验确认。
- 一个按下→移动→抬起手势只归属一个后端；FGUI 接管模态/焦点时不能让既有 UGUI selection/drag 在后台继续响应。关闭后恢复输入，不能把全局 EventSystem 长期禁用。
- 提供显式 `SuspendPresentation()` 返回幂等 lease：需要 UGUI 独占系统界面时临时隐藏/停用 FGUI 展示与输入，释放 lease 后按窗口原状态恢复。保留 UGUI 旧 API；第一轮不自动猜测所有现有 UGUI 窗口的语义。
- FGUI 未 Initialize 或关闭后，不留下会挡住 UGUI 的透明 shield、相机或输入订阅。

### 6. 初始化、关闭和 AOT 边界

GameModule.FGUI 只提供模块访问；OnInit 不 fire-and-forget 资源 IO。由热更启动的显式 async 接入方法，在现有 Resource package ready 后 await InitializeAsync；新增样例从该已初始化入口打开，不改变 GameApp.Entrance(object[]) 签名。初始化失败要被调用入口观察并记录，UGUI 可继续工作。

RootModule 增加不依赖 FGUI 的 `BeforeShutdown` 通知：Player 的 OnDestroy 在 ModuleSystem.Shutdown 前派发；Editor 的 OnDestroy 也通知订阅者，但保留当前不调用整个 ModuleSystem.Shutdown 的规则。通知逐订阅者隔离异常，最终仍执行原有关闭；本 Root 实例最多派发一次。FGUI Host 订阅这一事件并同步 Shutdown，自身 OnDestroy 和 Singleton OnRelease 做幂等兜底。正常手动关闭也先停用输入和取消请求再释放句柄。

不把新 FGUI 的可靠退出绑在可能被 UpdateDriver.Release 清掉的 GameApp destroy listener 上。此处只补必要生命周期接入，不顺手重构整个 ModuleSystem/SingletonSystem。FGUI 清理只操作自己拥有的对象、包和注册；不调用 GameEvent.Shutdown 或清空全局 UIObjectFactory 的其他用户注册。SDK 工厂默认值/静态 hook 的还原以锁定版本公开 API 为准，并测试停止播放再进入。

### 7. 编辑器、地址和生成文件

建立新的最小 .fairy 源工程，默认发布路径为 `Assets/AssetRaw/FGUI/Packages/<Package>/`。catalog 的确切资源表由编辑器/发布校验生成，不人工维护每张 atlas，也不把目录名本身当依赖证据。用锁定 SDK 的描述元数据接口和实际发布清单核验 dependency id/name、导出前缀、请求名称/扩展名映射；不重写 FGUI 二进制解析器。无法完整列出资源时校验失败，不以运行时同步加载兜底。

新增 FGUI collector 使用专属完整相对地址：例如 `FGUI/Packages/Common/Common_fui.bytes`、`FGUI/Packages/Common/Common_atlas0.png`，保留扩展名和目录；所有 lookup 来源为该表。描述与资源名字不可把 UI 包名、YooAsset 包名、component URL 当同一字符串。

首轮仍放 DefaultPackage，新增 FGUI 分组；避免 FGUI 根组与子组重复收集。FGUI shaders/所需基础资源进入可构建集合，验证裁剪后 Player 不出现粉色材质或空白字；不依赖 Editor 中自动找到 shader/系统字体。普通 atlas 按 SDK 要求导入 Texture2D，不按 UGUI Sprite 导入；分离 alpha、声音、bitmap font 依赖都进清单。首轮样例使用可随包发布的字体，系统字体/动态字体支持不凭编辑器结果宣称完成。

FGUI 官方代码生成/Binder 为起点，输出放 Gen；手写 controller 放 Imp。生成任务只能清理自己的 Gen 和发布清单内文件，禁止沿用旧 GenCode_CSharp.lua 对未知目标目录清理。显式 Binder 注册所有使用的生成类型，并生成/维护一份框架窗口注册入口；不运行时反射扫描整个工程。首次接入通过 Editor/Player 编译验证热更调用和必要裁剪保留，不能只证明 Editor 直接引用成功。

## 实施顺序与验收

| 阶段 | 工作 | 必须先拿到的证据 |
| --- | --- | --- |
| P0 SDK 与最小互操作 | 锁定官方 SDK、asmdef/许可证、当前渲染和输入基线；最小 FGUI 元素与现有 UGUI 并存 | Unity 编译；Player 可见、层级正确；输入阻挡试验；官方扩展 API 符合契约 |
| P1 包资源闭环 | provider/leases、catalog、依赖预加载与事务回滚、collector | 描述/atlas/声音/字体及 Common 依赖真实加载；关闭后适配层 handles 与 leases 回基线 |
| P2 类型窗口与生命周期 | module/window/widget/scope、外部 loader、Root 通知、绑定入口 | 并发 Show、单等待者取消、全部取消、加载中 Close、回调抛错、重复 Shutdown |
| P3 可复用交付 | .fairy 源工程、生成保护、示例、说明、Player/HybridCLR 验证 | 从发布产物经 YooAsset 到 hotfix 窗口的整条链；原 UGUI 行为回归 |

这些阶段属于同一个接入工作包，不能只完成 P0 导入 SDK 就报告完成。若要缩小首轮交付，须在实施前另行修改本契约。

最低验收矩阵：

- 两个不同窗口共享 Common 包，关闭其中一个另一个仍正确；全部关闭且无 pin 后适配层活跃 lease/handle 归零。不要把进程内存立即回落作为唯一标准。
- 缺描述、缺依赖、缺 atlas、错误类型、循环依赖、重复包 id/name：都进入明确错误，能重试，registry 不留永久 Loading。
- 同类型并发 Show 只有一个实例；取消一个等待者不伤其他等待者；全部取消/Close/Shutdown 不生成迟到窗口。
- 一个 OnCreate/OnRefresh/OnDestroy 抛错，清理仍完成，UGUI 保持可用。
- 外部图标 A→B 快速切换及加载中销毁，A 的迟到结果不覆盖 B；资源释放一次。
- 可见、Hide、Close、再开、重复 Shutdown/停止播放再进入，各阶段事件/timer/FGUI listener 不重复登记。
- Windows 当前 Built-in/旧 Input 的鼠标、滚轮、焦点；手机触摸/多触点与 safe area 至少用对应测试环境验证。未执行的目标平台明确列为未验收，不能声称“所有平台支持”。
- 一次真实 AssetBundle + IL2CPP/HybridCLR Player 冒烟，验证字体/shader/绑定/包地址；仅 EditorSimulate 不算资源链交付。
- 二次生成只更新 Gen/发布清单，Imp 内容不变；FGUI 未启用时现有 BattleMainUI 与 Launcher 正常。

## 范围、限制及本轮验证

本轮只读当前源码、OLD 上述适配样本及官方发布/教程；没有重新全量调查两工程，也没有导入 SDK、运行 Unity/FGUI Editor、构建或测量性能。官方 GitHub raw/API 获取在本环境失败（网页工具获取失败，shell TLS 认证失败）；因此 SDK 实现细节必须在 P0 固定源码后核对。本轮没有据此请求安装、降级 TLS 验证或复制 OLD vendor。

不把原研究中的整个通用事件/资源/FSM 重构作为本接入的隐含前置条件。若发现当前工程独立编译问题，只定位并说明；本功能所需的极小修正须有直接证据，重大范围变化报 PLAN_CONFLICT。

本计划是后续 slave 的手动交接材料；本 master 不实施。下面的提示词包含执行所需完整约束，并引用本文件的固定契约。

## SLAVE_IMPLEMENTATION_HANDOFF

### Role

你是本接入任务的主要实现者，使用 slave-executor，遵守当前仓库 AGENT.md/CLAUDE.md 和 tengine-dev。不要调用 agent、委派、切换模型或自动执行后续阶段以外的任务。工作目录为 `E:/MyWork/MyFramework/TEngine/UnityProject/`。先读 git status/diff，保留已有 boot.config 删除、UserSettings 和未跟踪 docs 等全部无关变更。

### Objective

为通用 TEngine 接入 FairyGUI，与现有 UGUI 并存。新增界面可通过 GameModule.FGUI 的类型化异步 API 使用 FairyGUI；GameModule.UI/Launcher/BattleMainUI 保持现有使用方式。完成包资源、绑定生成、窗口生命周期、外部图标、输入互操作和可构建样例，不能仅导入 SDK。

### Existing architecture

现有 UIModule/UIWindow 基于 SingletonSystem、GameObject/Canvas/GraphicRaycaster；IUIResourceLoader 仅支持 Prefab。IResourceModule 已有 LoadAssetAsyncHandle<T>/非泛型 overload，可借用当前 DefaultPackage，不需再初始化 YooAsset。该 raw handle 不入 TEngine AssetObject 池，必须由新适配层 Dispose。SDK/AOT 组件与 GameLogic 热更依赖单向。RootModule 的 Player shutdown 与 GameApp listener 存在顺序缺口，新功能必须有资源关闭前的确定清理通知。

### Implementation contract

先完整读取本文件 `E:/MyWork/MyFramework/TEngine/UnityProject/docs/framework-reference/project-old/synthesis/fgui-integration-plan.md` 的“核心设计契约”“实施顺序与验收”；该文件就是本工作包契约，关键点重述如下：

1. 官方 SDK 固定 tag+commit，保留许可证；官方 v5.2.0 是候选，P0 核对真实源码/API 与 Unity 2022.3.62f2。禁止从 OLD 复制修改版 SDK/IGG 引用。
2. GameModule.FGUI 平行于 GameModule.UI。SDK/AOT Settings/Catalog/Host 在主包；controller/module/Gen/Binder 放已有 GameLogic，不新增热更 DLL。
3. 资源走当前 LoadAssetAsyncHandle；成功交 lease、失败/取消释放，一次 Dispose，不与 UnloadAsset 混用。不改 SDK 原生加载源码。
4. 首轮包和依赖清单异步预加载，自定义同步资源回调只读内存；静态资源全部 ready 后才返回窗口。全局唯一 package id/name、拒绝循环、共享依赖引用、失败事务回滚；GObject Dispose→RemovePackage→handle Dispose。
5. 类型窗口独立于 UIWindow，生成 GComponent 与控制器组合。一个类型单实例/generation；加载中重复 Show 等待同一结果，第一次数据用于创建；已有 ready 窗口再 Show 刷新。单调用者取消不关闭其他 owner；全部等待者取消/Close/Shutdown 终止创建。
6. Hide 保留 lifetime，Close 销毁不缓存；异常/取消不能返回半成品。清理 finally，支持重复调用；非缩放超时和包含定位的错误结果。
7. 局部 scope 复用 GameEventMgr/timer，登记 FGUI listener；GLoader 扩展只支持 ui:// 与本地 asset://，generation 保护与释放，不接网络头像系统。
8. 两个栈，FGUI 整体在业务 UGUI 之上；独立相机/layer，750×1334 默认可配及 safe area。命中阻挡/模态/手势与焦点必须有互操作桥验证；显式 SuspendPresentation lease 允许 UGUI 临时独占。
9. RootModule 增加中立 BeforeShutdown，资源模块关闭前通知，Editor 也通知；逐 listener 异常隔离及实例级一次派发，保留其原有 Editor ModuleSystem 分支。FGUI shutdown 同步取消/释放并幂等，不能只依赖 GameApp destroy listener。
10. 新增 FGUI collector 使用完整相对地址含扩展名，仍在 DefaultPackage；catalog 自动校验真实发布描述/资产/依赖。生成写 Gen，手写 Imp；不复制旧清目录脚本、portrait/业务绑定扫描。
11. 没有账户/存档/网络格式迁移、旧资产迁移、世界空间 HUD、跨后端逐窗口排序、按图集自动卸载或全局任务中心。本轮不承诺性能提升。

### Relevant files / symbols

- 当前接入点：`Assets/GameScripts/HotFix/GameLogic/GameModule.cs`、`GameApp.cs`、`GameLogic.asmdef`。
- 当前资源接口：`Assets/TEngine/Runtime/Module/ResourceModule/IResourceModule.cs`、`ResourceModule.cs:1179–1193`；`Packages/YooAsset/Runtime/ResourceManager/Handle/HandleBase.cs`。
- 生命周期：`Assets/TEngine/Runtime/Module/RootModule.cs:156–167`；`Assets/GameScripts/HotFix/GameLogic/SingletonSystem/`。
- Collector：`Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset`；现有 `Packages/YooAsset/Editor/AssetBundleCollector/DefaultRules/DefaultAddressRule.cs` 只参考。
- 现有 UI 基线：`Assets/TEngine/Settings/Prefab/UIRoot.prefab`、`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/`。
- 新目录以本计划“架构与建议目录”为准。
- OLD 为 `D:/Work/SAUnity/ProjectOld/` 只读；只借鉴本文件 FGUI-E004–E006 中的设计事实，不迁移对应实现。

### Implementation steps

按 P0→P1→P2→P3 顺序完成：先固定 SDK 与程序集并验证渲染/输入扩展点；再打通 catalog/handle/包依赖的最小闭环；再接入窗口、局部 scope、外部 loader 和确定退出通知；最后交付最小 .fairy 源工程、可重生成产物、UGUI 并存样例、测试和使用说明。每阶段围绕契约提供证据，不顺手重写其他通用模块。

### Constraints

OLD 全程只读；六份研究与原 handoffs 不修改。禁止恢复、清理、暂存或提交无关变更。不得复制旧业务、修改 vendor 以耦合 GameLogic、用同步资源 IO 掩盖异步适配问题。删除过期生成文件仅限自己生成清单列出的文件，绝不递归清理未知目录。P0 SDK 获取失败时不以 OLD 修改版替代。

### Acceptance criteria

本文件最低验收矩阵全部满足：共享包不提前卸载、失败/取消可重试、无迟到窗口/错误图标、清理幂等、生成不覆盖手写、UGUI 并存不穿透、真实 YooAsset 包到 HybridCLR 生成类型可工作。未测平台和独立环境阻塞必须明确列出，不能冒充通过。

### Verification

运行与改动直接相关的 EditMode（目录校验/状态/引用账）、PlayMode（窗口/图标/输入/生命周期）测试，完成 Unity 编译和一次真实 IL2CPP/HybridCLR Player 冒烟。用假 provider 精确检验句柄获取/释放次数，用真包验证地址/shader/font/绑定。不要将广泛无关测试作为默认任务；环境缺测试框架时先核对可用工具并记录必要测试依赖。第一次基线编译若失败，区分已有错误与本次引入。

### Handling unexpected repository reality

小型实现差异按工程判断处理并记录，不改变冻结行为。若官方 SDK 缺必要公开扩展、现有输入/渲染无法在范围内互操作、目录/热更边界与证据矛盾，停止相关实现并输出 `## PLAN_CONFLICT`，给出具体源码/复现及唯一待决定事项；不要调用 master 或其他 agent，也不要默默扩为旧框架迁移。

### Completion requirements

交付完整代码/资产/.fairy 源/生成配置/使用文档与验证结果，列明全部相关修改；不贴大 diff。保留原 UGUI 行为，记录 SDK 来源/commit 和检查限制。完成后只输出下面的 master 审查交接，不自行开始新能力。

### Required master review handoff

最后输出 `## MASTER_REVIEW_HANDOFF`，写成新 master 可直接复制的完整提示词，包含：

- Role：最终正确性审查者，只审查不重实现；从共享工作树 git status/diff 与定向源码检查开始。
- Original objective：为通用 TEngine 添加 FGUI/UGUI 并存能力，保留旧 UI，不迁移 OLD 业务。
- Important implementation contract：SDK 来源/固定版本、raw handle 唯一 owner、package lease/依赖、窗口终态/取消、生成绑定与热更边界、互操作、退出通知。
- Implementation summary、Changed files：真实完成内容和每个相关文件/资产。
- Tests/checks already run、Known deviations or concerns：命令/平台/结果及未执行范围，不能省略失败或环境阻塞。
- Review priorities：引用/句柄恰好释放、失败回滚、取消竞态、输入穿透、资源地址、字体/shader 裁剪、生成覆盖与 UGUI 回归。
- Required review output：符合契约输出 REVIEW_PASS；有具体问题则 SLAVE_FIX_HANDOFF，仅含最小问题、证据、修正和验证，不建立新大计划或委派。
