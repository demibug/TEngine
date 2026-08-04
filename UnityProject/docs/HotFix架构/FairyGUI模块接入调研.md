# FairyGUI 模块接入调研与实施建议

> 调研日期：2026-08-04  
> 状态：设计调研完成，尚未开始实现  
> 用途：作为 TEngine 当前工程接入 FairyGUI（下称 FGUI）的事实基线、设计评审记录与实施清单。  
> 规则：文中“已验证”来自当前源码；“参考做法”来自 SAUnity；“建议”尚需通过原型或真机包验证。

## 1. 结论摘要

建议新增独立的 `GameFUI` 热更程序集和 `FUIModule`，在职责上对齐现有 `UIModule`，但不要继承或复用它的 UGUI 实现。UGUI 与 FGUI 在迁移期并存，通过各自的门面访问。

推荐的核心模型是：

```text
FairyGUI runtime（AOT/基础层）
        ↑
GameFUI（热更基础设施）
  ├─ IFUIModule / FUIModule
  ├─ FUIWindow / FUIWidget
  ├─ FUIPackageManager / IFUIResourceLoader
  └─ FUIBindingRegistry
        ↑
FGUI 生成层（生成的包/组件基类）
        ↑
GamePlay / GameBattle 等业务程序集（业务面板类）
```

面板继承链建议采用：

```text
FUIWindow : GComponent
  └─ UI_BattleStartPanel : FUIWindow      // FairyGUI 自动生成，不手改
       └─ BattleStartPanel                // 业务逻辑
```

在 `UIPackage.CreateObject` 之前，把组件 URL 映射到最末端的业务类型 `BattleStartPanel`。这是 SAUnity 参考工程最值得借鉴的部分，也是当前工程必须先做技术验证的关键点。

资源生命周期不建议照搬“关闭一个窗口立即 `UIPackage.RemovePackage`”，也不建议永久不卸载。第一阶段先做到模块关闭时统一释放；确认正确性后，再加入“包依赖 + 并发加载合并 + 引用计数 + 延迟卸载”。

## 2. 调研范围与证据来源

### 2.1 当前工程

- `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/`
- `Assets/GameScripts/HotFix/GameFUI/`
- `Assets/ThirdParty/FairyGUI/Scripts/`
- `Assets/GameScripts/HotFix/Module/`
- `Assets/GameScripts/HotFix/GameLogic/GameApp.cs`
- HybridCLR 与热更程序集配置
- `docs/HotFix架构/架构设计.md`
- `tengine-dev` 技能的架构、模块、UI、资源、热更与命名参考

### 2.2 参考工程 SAUnity

- `D:/Work/SAUnity/Project/.codex/skills/slg-dev/SKILL.md`
- `slg-dev/references/architecture.md`
- `slg-dev/references/ui-lifecycle.md`
- `slg-dev/references/ui-patterns.md`
- `slg-dev/references/resource-api.md`
- `slg-dev/references/resource-patterns.md`
- `slg-dev/references/hotfix-workflow.md`
- `slg-dev/references/modules.md`
- `slg-dev/references/singleton-lifecycle.md`
- `slg-dev/references/ui-animation-event.md`
- `PanelMgr`、`LoadMgr`、`BasePanel`、`BaseView`、`UIBindProvider`、启动器及生成代码源码

参考工程文档与源码不一致时，以源码为准。

## 3. 当前工程事实

### 3.1 现有 UIModule 的真实职责

当前 `UIModule` 是 `Singleton<UIModule>, IUpdate`，主要负责：

- 窗口创建、异步加载、显示、隐藏、关闭与查询；
- `UILayer` 排序和深度分配；
- 全屏窗口对下层窗口的可见性遮挡；
- 每帧驱动窗口更新；
- 通过 `IUIResourceLoader` 隔离资源加载；
- 维护 `UIWindow` 栈及窗口生命周期。

`UIWindow` 直接拥有 `GameObject`、`Canvas`、`GraphicRaycaster`、`RectTransform` 等 UGUI 对象。`UIBase` 和 `UIWidget` 也绑定 UGUI/Unity GameObject 语义。因此，GameFUI 不能直接继承或复用这些实现，否则会形成错误的程序集依赖和 UI 技术耦合。

可以复用的是行为契约：

- 按类型打开、关闭和查询；
- UI 分层；
- 全屏遮挡；
- 首次创建与重复打开的生命周期区分；
- 事件注销、异步取消和最终销毁；
- 模块级 Update 与 Shutdown。

不应照搬的实现：

- 用轮询等待窗口加载完成；
- 用 `Canvas.sortingOrder` 和超大深度间隔表示层级；
- `WindowAttribute.Location/FromResources` 这类 Prefab/UGUI 元数据；
- 将关闭窗口等价为立即销毁所有资源；
- 用多个布尔值隐式表达复杂的异步状态。

### 3.2 目标架构文档已经给出方向

`docs/HotFix架构/架构设计.md` 明确提出：

- 新建 `GameFUI`；
- 定义 `IFUIModule`；
- `FUIModule : Module, IFUIModule, IUpdateModule`；
- 原文提出建立 `FUIPanel`、`FUIWidget` 与 `FUI` 静态门面；本调研最终将顶层类型落地命名为 `FUIWindow`；
- UGUI 与 FGUI 并存；
- 由统一的热更模块注册入口先注册 FGUI，再注册业务模块；
- FairyGUI runtime 位于基础/AOT 侧，GameFUI 和业务面板位于热更侧。

这个总体方向成立，但其中“复用 `WindowAttribute`”和“关闭窗口即卸载包”需要修改，原因见后文。

### 3.3 GameFUI 当前并不是一个完整模块

当前 `Assets/GameScripts/HotFix/GameFUI/` 主要是 FairyGUI 自动生成代码：

- 生成组件直接继承 `GComponent`；
- 例如 `UI_BattleStartPanel.CreateInstance()` 直接调用 `UIPackage.CreateObject`；
- Binder 当前把 URL 绑定到生成类型；
- 尚无 `GameFUI.asmdef`；
- 尚无 `FUIModule`、`FUIWindow`、资源加载器或生命周期框架；
- GameFUI 尚未进入 HybridCLR 热更程序集列表和 DLL 更新配置。

这会产生一个核心冲突：生成类已经继承 `GComponent`，业务窗口若还需要继承 `FUIWindow`，C# 单继承无法同时满足。必须调整生成链，让生成的根窗口类继承 `FUIWindow`，而不是在生成后手工修改代码。

### 3.4 当前包资源存在命名风险

`Assets/AssetRaw/FUI` 下观察到以下命名并存：

- `UICommon_fui.bytes` 与 `Common_fui.bytes`
- `UIBattle_fui.bytes` 与 `BattleUI_fui.bytes`

生成代码使用的包名是 `UIBattle`。在实现资源定位规则之前，必须确认这些文件分别是什么版本、是否仍被引用、资源地址最终使用包名还是文件名。否则很容易出现“编辑器可开、资源包环境找不到”的问题。

### 3.5 模块系统有注册约束

当前 `ModuleSystem.RegisterModule<T>` 会把模块加入生命周期和更新列表，并调用 `OnInit`。如果同一接口重复注册，字典映射会覆盖，但旧模块仍可能留在模块列表和更新列表中。

因此：

- FGUI 注册入口必须集中且只执行一次；
- 最好由 `HotFixModules.Register()` 之类的统一入口负责；
- 可以在上层加幂等保护，但不能把“重复注册不会有副作用”当成前提；
- 业务模块依赖 FGUI 时，FGUI 必须先注册；Shutdown 顺序则应反向。

### 3.6 FairyGUI runtime 的资源回调语义

当前 FairyGUI runtime 支持 `UIPackage.AddPackage(byte[], prefix, LoadResourceAsync)`。异步资源回调本身不是可等待任务，加载方完成后必须调用 `item.owner.SetItemAsset(...)`。

另外：

- `UIPackage.RemovePackage` 会释放整个包持有的资源；
- `GRoot.inst` 会在需要时初始化 `Stage`，模块不应自行复制一套 Stage 生命周期；
- 如果资源实际由 YooAsset handle 持有，GameFUI 必须明确保存并释放这些 handle，不能把资源所有权含糊地交给 FairyGUI 或 `Resources.UnloadAsset`。

这意味着“包描述文件已经解析完成”不一定等于“面板所需贴图等资源已经就绪”，Show API 必须定义清楚何时算加载成功。

### 3.7 热更配置尚未覆盖 GameFUI

当前 HybridCLR 热更程序集和更新 DLL 配置主要包含 `GameProto`、`GameLogic`，没有 `GameFUI`。新增程序集后至少需要核对：

- `GameFUI.asmdef` 的名称和引用方向；
- HybridCLR hot update assemblies；
- DLL 更新/拷贝列表；
- 启动阶段的 DLL 加载顺序；
- 业务程序集对 GameFUI 的引用；
- 真机包中的 AOT 泛型元数据需求。

注意：FairyGUI 当前是源码程序集，而不是简单的一份预编译 `FairyGUI.dll`。不能仅凭架构文档就机械地把 `FairyGUI.dll` 加入补充元数据列表；应以实际生成程序集名、裁剪结果和真机报错为依据验证。

## 4. SAUnity 参考工程可借鉴的部分

### 4.1 生成基类与业务类分层

参考工程的核心链路是：

```text
BasePanel : GComponent
  └─ BaseVipPanel（生成层，保存 URL/PkgName/ResName）
       └─ VipPanel（业务层）
```

启动时扫描生成类型和业务类型，把组件 URL 注册到真正的业务类型：

```text
业务类型 -> 找到生成基类 -> 读取 URL/包名/组件名
        -> UIObjectFactory.SetPackageItemExtension(url, 业务类型)
```

这样 `UIPackage.CreateObject` 得到的对象本身就是业务面板，而不是创建生成对象后再外挂脚本。这一点适合迁移到 TEngine。

### 4.2 包和依赖的递归加载

参考工程在打开面板前：

- 加载目标包；
- 读取 `UIPackage.dependencies`；
- 递归加载依赖；
- 用集合避免重复遍历；
- 加载完成后再创建对象。

当前工程应保留这一行为，但进一步增加并发任务合并、依赖引用计数和失败回滚。

### 4.3 分层容器

参考工程为每个 `PanelLayer` 在 `GRoot` 下创建专用 `GComponent` 容器，再把面板添加到相应容器，并在容器内维护优先级。

这比复制 UGUI 的 `Canvas.sortingOrder` 更符合 FairyGUI：

- 层之间由容器顺序保证；
- 同层面板由 child index 或有限的 sortingOrder 管理；
- 安全区可以落在单独容器；
- 不必把每个面板映射成独立 Canvas。

### 4.4 Close 与 Dispose 分离

参考工程区分：

- Close：结束本轮打开状态、取消本轮任务、解绑事件；
- Cache：从显示树移除但保留对象；
- Dispose：最终释放 GObject 与成员资源。

FGUI 存在面板缓存时，这种区分比当前 UIModule 的单一销毁模型更重要。

## 5. 参考工程不可直接照搬的部分

### 5.1 它使用了定制 FairyGUI runtime

参考工程的 `GComponent.ConstructFromXML` 中接入了反射式 `FGUIAutoBinder.Bind(this)`，当前 TEngine 的 FairyGUI runtime 没有这一改造。不能直接复制相关绑定代码或假设字段会自动注入。

TEngine 首版应优先沿用当前生成代码中的显式字段绑定；如果以后要改自动绑定，需要作为独立的 runtime 定制方案评估。

### 5.2 包卸载策略不完整

参考工程的 `PanelMgr` 打开时加载包和依赖，但普通关闭面板并不会自动卸载包。这个策略运行上较稳，但会让包常驻。

目标工程不宜直接照搬，也不宜走另一个极端——每关一个窗口就卸载整个包。多个面板可能共享同一包或同一依赖，立即卸载会破坏仍存活的对象。

### 5.3 API 文档存在漂移

`slg-dev` 的个别 reference 把面板打开 API 写成静态调用，而源码实际使用 `PanelMgr.Inst.OpenPanelAsync<T>`。参考工程只能借鉴设计，具体签名必须以源码为准。

## 6. grill-me 设计审查

### 6.1 发现

1. “仿照 UIModule 做一个”方向正确，但只能仿照职责和外部使用体验，不能复制 UGUI 数据结构。
2. 当前生成代码继承 `GComponent`，与拟议的 `FUIWindow` 发生继承位冲突；这是实现前必须打通的第一问题。
3. `WindowAttribute` 表达的是 Prefab 路径和 Resources 语义，无法准确表达 FGUI 的包、组件和依赖。
4. 架构文档提出的“关闭窗口后立即 RemovePackage”没有处理共享包、共享依赖、缓存面板和并发创建。
5. 当前 GameFUI 尚未成为热更程序集；即使编辑器代码写完，也不代表资源包/真机加载链成立。
6. 当前包文件存在疑似新旧命名并存，资源定位规则尚未收敛。

### 6.2 漏洞

1. **异步关闭竞态**：打开期间关闭，加载回调仍可能创建并显示过期面板。
2. **并发打开竞态**：同一面板或同一包被同时请求，可能重复加载、重复创建或覆盖回调。
3. **依赖提前卸载**：A、B 包共享依赖 C，关闭 A 不能卸载 C。
4. **绑定时序错误**：在创建对象之后才注册业务类型，实例将是生成基类而非业务类。
5. **缓存对象悬空**：包被卸载，但缓存面板仍引用包内纹理、字体或组件。
6. **资源所有权泄漏**：只向 FairyGUI 设置 asset，却未保存和释放 YooAsset handle。
7. **模块重复注册**：旧模块仍可能留在 Update 列表，产生双更新和重复回调。
8. **热更加载顺序错误**：业务 DLL 先于 GameFUI 加载，继承类型解析可能失败。

### 6.3 薄弱环节

1. 当前 `ShowUIAsyncAwait` 的轮询模式不适合作为 FGUI 新 API 的基础，错误、取消和超时语义都不够清晰。
2. 仅用 `IsLoaded/IsDestroyed` 一类布尔值不足以描述 Loading、Open、Hidden、Closing、Cached、Disposed。
3. FairyGUI 的资源加载回调是异步回填模式，“包已添加”和“视觉资源已就绪”可能是两个状态。
4. 编辑器正常运行不足以证明 HybridCLR 真机加载、代码裁剪和 AOT 泛型元数据正确。

### 6.4 未经验证的假设

1. FairyGUI 编辑器导出流程能够为“根窗口”配置自定义基类 `FUIWindow`，且不会在重新导出时丢失。
2. 生成层可以稳定提供 `URL/PkgName/ResName`，或可由 URL 可靠反查包和组件。
3. 项目希望 UGUI 与 FGUI 长期共存，而不是短期完全替换。
4. 面板是否默认缓存、哪些面板必须销毁，尚无统一策略。
5. 包资源地址是否严格等于 `{PackageName}_fui.bytes` 尚未确认。
6. `UIBattle/BattleUI`、`UICommon/Common` 哪组是有效命名尚未确认。
7. 真机是否需要额外的 AOT 泛型补充元数据，需用实际构建验证。

### 6.5 修改后的方案

把原先“一个类仿照 UIModule + 关闭即卸包”的方案改成四个清晰边界：

```text
FUIModule
  负责：面板注册、打开/关闭/查询、层级、全屏遮挡、模块 Update

FUIPackageManager
  负责：包描述、依赖、并发合并、YooAsset handle、引用计数、延迟卸载

FUIWindow / FUIWidget
  负责：对象生命周期、本轮打开任务取消、事件清理、最终 Dispose

FUIBindingRegistry
  负责：URL -> 业务类型；所有绑定必须早于首次 CreateObject
```

## 7. 推荐的目标设计

### 7.1 程序集和依赖

建议新增：

```text
GameFUI.asmdef
  references:
    - TEngine 热更模块基础程序集
    - FairyGUI runtime
    - YooAsset/资源抽象所需程序集
    - UniTask（若项目约定使用）
```

依赖方向：

```text
FairyGUI/TEngine Runtime
        ↑
GameFUI
        ↑
GameCommon / GamePlay / GameBattle / GameLogic（按真实业务需要）
```

`GameFUI` 不应反向引用 `GameLogic` 或具体业务程序集。业务面板注册可由业务侧生成/提供注册器，再在启动入口调用。

### 7.2 FGUI 专用元数据

不建议复用 `WindowAttribute`。可选方案：

```csharp
[FUIWindow("UIBattle", "BattleStartPanel",
    Layer = UILayer.Normal,
    FullScreen = true,
    CacheMode = FUICacheMode.None)]
public sealed class BattleStartPanel : UI_BattleStartPanel
{
}
```

或者完全使用生成注册表，避免运行时反射。无论使用 Attribute 还是注册表，都应显式表达：

- PackageName；
- ComponentName 或 URL；
- Layer；
- FullScreen；
- CacheMode；
- 可选优先级和安全区策略。

### 7.3 生命周期

建议状态机：

```text
Unloaded -> Loading -> Opening -> Open
                      └---------> Failed
Open <-> Hidden
Open/Hidden -> Closing -> Cached
Open/Hidden/Cached -> Disposed
```

建议回调语义：

- `OnCreate`：对象构造完成后仅一次；
- `OnOpen`：每轮打开一次；
- `OnRefresh(args)`：重复 Show 时刷新参数；
- `OnHide`：临时隐藏，不释放本轮对象；
- `OnClose`：结束本轮打开，取消任务和解绑本轮事件；
- `OnDispose`：最终释放，仅一次；
- `OnUpdate`：只驱动需要更新且处于有效状态的面板。

Close 与 Dispose 必须分开；缓存面板 Close 后仍持有包租约，直到最终 Dispose 才释放。

### 7.4 打开链路

```text
业务调用 FUI.ShowAsync<BattleStartPanel>(args, token)
  -> FUIModule 查注册信息和现存实例
  -> 合并同类型 in-flight 打开任务
  -> FUIPackageManager.AcquireAsync("UIBattle")
       -> 合并同包加载任务
       -> 加载包描述
       -> AddPackage
       -> 递归 Acquire 依赖
       -> 建立资源加载与 handle 所有权
  -> 确认 URL 已绑定到 BattleStartPanel
  -> UIPackage.CreateObject
  -> 校验对象类型
  -> OnCreate（首次）/ OnOpen / OnRefresh
  -> 加入目标层容器并执行全屏遮挡计算
```

如果调用方在加载期间 Close 或取消：

- 本次打开操作标记失效；
- 完成中的共享包加载不必强制取消；
- 创建前再次检查操作版本/取消令牌；
- 已创建但过期的对象立即 Dispose；
- 正确释放本次取得的包租约；
- 不执行过期回调。

### 7.5 关闭和资源释放链路

```text
FUI.Close<T>()
  -> 面板 OnClose
  -> 从层容器移除
  -> 重新计算下层 visible/touchable
  -> 若 Cache：保留实例和包租约
  -> 若 Dispose：销毁 GObject，释放包租约
       -> 包自身引用为 0 且无创建任务/缓存对象
       -> 依赖引用递减
       -> 延迟窗口到期后 RemovePackage
       -> 释放该包对应的 YooAsset handles
```

`RemovePackage` 的前提必须同时满足：

- 没有存活或缓存的该包对象；
- 没有正在创建对象；
- 没有上层包依赖它；
- 没有待完成的资源回填；
- 延迟卸载窗口结束后仍为零引用。

### 7.6 层级、全屏与安全区

建议在 `GRoot` 下按 `UILayer` 创建固定容器，例如：

```text
GRoot
  ├─ BackgroundLayer
  ├─ NormalLayer
  ├─ PopupLayer
  ├─ GuideLayer
  ├─ TipsLayer
  └─ SystemLayer
```

全屏遮挡保持现有 UIModule 的行为语义，但应分别考虑：

- `visible`：是否渲染；
- `touchable`：是否接收输入；
- 是否仍留在 stage 上。

不要仅为了遮挡就反复从 stage 移除和添加，否则会额外触发 `onAddedToStage/onRemovedFromStage`，污染业务生命周期。

安全区建议由专用容器处理坐标和尺寸，并在分辨率、方向或根节点尺寸改变时重算；不要直接把 `GRoot` 改成安全区大小。

## 8. 分阶段实施计划

### 阶段 0：最小技术验证

只打通一个 `UIBattle/BattleStartPanel`：

1. 确认 FairyGUI 导出器能否让生成根窗口继承 `FUIWindow`；
2. 确认重新导出不会覆盖必要配置；
3. 在 CreateObject 前把 URL 注册到业务类型；
4. 验证创建结果确实是业务类型；
5. 通过 YooAsset 加载包描述和至少一张纹理；
6. 在编辑器与一次 HybridCLR 真机包中验证。

如果第 1 项失败，再评估组合式包装；不要先大规模搭建框架。

### 阶段 1：基础模块

实现：

- `GameFUI.asmdef`；
- `IFUIModule/FUIModule/FUI`；
- `FUIWindow/FUIWidget`；
- FGUI 专用窗口描述；
- 层容器；
- 真实可等待的 `ShowAsync<T>`；
- 业务类型绑定注册；
- `HotFixModules` 集中注册；
- GameFUI 热更与更新配置。

第一阶段可以选择“包加载后保持到 `FUIModule.Shutdown`”，先保证生命周期正确，避免过早引入复杂卸载错误。

### 阶段 2：完整资源生命周期

加入：

- 包加载任务合并；
- 递归依赖加载与环/重复保护；
- 包租约和依赖引用计数；
- YooAsset handle 所有权表；
- 失败回滚；
- 延迟卸载；
- 缓存面板与包租约联动；
- 资源就绪状态定义和诊断日志。

### 阶段 3：迁移与治理

- 逐个业务域迁移，不一次替换 UIModule；
- 为生成代码、业务代码和注册表确定固定目录；
- 增加生成后校验，禁止根面板退回直接继承 `GComponent`；
- 清理 FUI 包重名和历史文件；
- 补齐自动化测试与真机回归清单；
- 数据证明没有遗留 UGUI 后，再讨论是否移除 UIModule。

## 9. 建议新增文件与修改位置

具体命名可以随工程惯例调整，职责边界建议保持：

```text
Assets/GameScripts/HotFix/GameFUI/
  GameFUI.asmdef
  Runtime/
    IFUIModule.cs
    FUIModule.cs
    FUI.cs
    FUIWindow.cs
    FUIWidget.cs
    FUIWindowAttribute.cs              // 或 FUIWindowDescriptor.cs
    FUILayer.cs                        // 若不复用纯枚举 UILayer
    Binding/FUIBindingRegistry.cs
    Resource/IFUIResourceLoader.cs
    Resource/FUIPackageManager.cs
    Resource/FUIPackageLease.cs
  Generated/
    UIBattle/...
    UICommon/...
```

其他修改点：

- 热更模块统一注册入口：注册 `IFUIModule`；
- HybridCLR settings：加入 `GameFUI`；
- DLL 更新配置：加入 `GameFUI.dll`；
- 业务 asmdef：按需引用 `GameFUI`；
- FairyGUI 导出设置：根面板基类和生成目录；
- FUI 资源构建规则：统一 package/file/address 命名。

## 10. 验证清单

### 10.1 功能与生命周期

- [ ] 打开、关闭、再次打开同一面板；
- [ ] 已打开面板重复 Show 时刷新参数，不重复创建；
- [ ] 同一类型两个并发 Show 只产生一个有效实例；
- [ ] 加载期间 Close/取消后不会闪现过期面板；
- [ ] Cache 面板 Close 后可恢复，Dispose 后不可恢复；
- [ ] OnCreate/OnOpen/OnClose/OnDispose 次数符合约定；
- [ ] 业务绑定后 CreateObject 的实际类型正确。

### 10.2 包与资源

- [ ] 单包无依赖加载；
- [ ] 包含一层和多层依赖；
- [ ] 两个面板共享同一包；
- [ ] 两个包共享同一依赖；
- [ ] 依赖失败时所有引用和 handle 正确回滚；
- [ ] 面板缓存期间包不会卸载；
- [ ] 最终关闭后引用计数和 YooAsset handle 回到基线；
- [ ] 延迟卸载期间重新打开不会重复抖动加载；
- [ ] `UIBattle/BattleUI` 等命名冲突已消除。

### 10.3 显示与交互

- [ ] 各层容器顺序正确；
- [ ] 同层优先级正确；
- [ ] 全屏面板正确隐藏/禁用下层；
- [ ] 关闭全屏面板后下层正确恢复；
- [ ] 遮挡不会错误触发 stage 添加/移除生命周期；
- [ ] 横竖屏、刘海屏和分辨率变化时安全区正确；
- [ ] 模态窗口、Tips、Guide 输入穿透符合预期。

### 10.4 热更与退出

- [ ] 编辑器 PlayMode 退出时没有遗留 Stage/模块/回调；
- [ ] 模块 Shutdown 逆序正确；
- [ ] GameFUI DLL 先于继承它的业务 DLL 加载；
- [ ] HybridCLR 真机包能解析所有继承类型；
- [ ] IL2CPP 裁剪和泛型元数据无缺失；
- [ ] 资源包模式下地址与编辑器模拟模式一致。

## 11. 实现前需要确认的产品/工程决策

1. UGUI 与 FGUI 是长期并存，还是 FGUI 最终完全替代 UGUI？
2. 默认关闭策略是 Dispose 还是 Cache？哪些面板例外？
3. FGUI 导出器是否支持按组件配置自定义基类？团队是否接受修改导出模板？
4. 包卸载目标是模块退出统一释放，还是运行期零引用延迟释放？
5. 包名、描述文件名和 YooAsset 地址的唯一规范是什么？
6. GameFUI 是独立热更 DLL，还是合并进现有 GameLogic？本调研推荐独立 DLL。
7. 首个端到端验证面板是否就选 `UIBattle/BattleStartPanel`？

## 12. 导出插件专项结论（2026-08-04 补充）

已进一步对比：

- 参考工程：`D:/Work/SAUnity/Project/UIProject/plugins/组件扩展/` 与 `plugins/发布代码/`；
- 当前工程：`UnityProject/FGUIProject/`；
- 当前仓库自带 FairyGUI Editor 的默认 `GenCode_CSharp.lua`。

结论：**当前 FGUIProject 应新增项目级“组件扩展注册”插件，但首版不应复制参考工程的自定义“发布代码”插件。**

原因如下：

1. 参考工程通过插件向 FairyGUI 编辑器注册两种自定义组件扩展：面板基类 `BasePanel` 和组件基类 `BaseView`。被设计人员标记后的组件 XML 会保存 `customExtention`，发布时 `classInfo.superClassName` 就不再是普通 `GComponent`。
2. 当前工程的 `BattleStartPanel.xml` 没有 `customExtention`，所以生成结果必然是 `UI_BattleStartPanel : GComponent`。
3. 当前仓库自带的默认 C# 生成器本身已经使用：

   ```lua
   writer:writeln('public partial class %s : %s',
       classInfo.className, classInfo.superClassName)
   ```

   因此，只要编辑器正确注册扩展并在组件上选中它，默认生成器就能生成自定义基类。
4. 默认生成器还会生成 `CreateInstance`、`ConstructFromXML` 字段绑定和包 Binder；这些正是当前标准 FairyGUI runtime 所需要的。SAUnity 的自定义生成器删除了这些代码，是因为它使用了定制的反射自动绑定 runtime，不能直接移植。

建议在 `UnityProject/FGUIProject/plugins/组件扩展/` 新增项目插件，提供两种扩展：

```text
界面窗口 -> TEngine GameFUI 中的 FUIWindow
界面组件 -> TEngine GameFUI 中的 FUIWidget
```

扩展类名建议使用完整命名空间，避免生成代码所在的 `UIBattle/UICommon` 命名空间无法解析基类。

需要注意当前仓库附带 FairyGUI Editor 的 API 签名是：

```text
RegisterComExtension(name, className, superClassName)
```

而 SAUnity 插件使用旧版两参数调用。因此只能借鉴机制，不能原样复制代码。第三个参数的具体值应在当前 Editor 中以一个测试扩展验证；预期用于声明自定义扩展所基于的 FairyGUI 原生类型（面板/组件均为 `GComponent`）。

推荐导出规则：

- 只有由 `FUIModule` 管理的顶层界面选择“界面窗口”，生成类继承 `FUIWindow`；
- 可独立复用且需要生命周期的复合组件选择“界面组件”，生成类继承 `FUIWidget`；
- Button、ProgressBar、List item 等保持 FairyGUI 原生扩展，不要一律改成 `FUIWidget`；
- 普通无逻辑容器继续继承 `GComponent`；
- 插件只负责注册可选扩展，不依据类名包含 `Panel` 自动判断，避免命名误判；
- 增加发布后校验：约定为顶层面板的组件如果仍生成 `: GComponent`，则发布失败或给出明确错误。

完成插件后的最小验收：

1. 在 FairyGUI Editor 中打开 `FGUIProject.fairy`，`BattleStartPanel` 能选择“界面面板”；
2. XML 持久化自定义扩展标记；
3. 连续发布两次，生成结果稳定为 `UI_BattleStartPanel : FUIWindow`；
4. 默认 `ConstructFromXML` 和成员字段绑定仍然存在；
5. Unity 编译通过；
6. 注册最终业务类型后，`UIPackage.CreateObject` 返回 `BattleStartPanel`。

## 13. 最终建议

先用一个真实窗口验证“生成继承链 + 业务类型绑定 + YooAsset 包加载 + HybridCLR 真机加载”四件事，再展开完整模块。这个验证通过后，按“FUIModule 管窗口、FUIPackageManager 管资源、FUIWindow 管生命周期、BindingRegistry 管类型”的边界实现。

该方案保留现有 UIModule 已经验证过的外部行为，同时避开 UGUI 技术细节、资源共享误卸载和异步竞态。SAUnity 最值得复用的是生成层/业务层继承绑定、依赖递归加载、层容器及 Close/Dispose 分离；其定制 FairyGUI 自动绑定和包常驻策略不应直接复制。

## 14. Window/Widget 生成继承规格与计划调整（2026-08-04）

### 14.1 先澄清参考工程的三层结构

SAUnity 并不是由 FairyGUI 生成框架类 `BasePanel` 和 `BaseView`：

- `IGG.Framework.Panel.BasePanel`：手写框架基类，表示由 `PanelMgr` 管理的顶层面板；
- `IGG.Framework.Panel.BaseView`：手写框架基类，表示嵌入面板或页面的可复用组件；
- `BaseVipPanel`、`BaseRiftBurstMainView`：FairyGUI 自动生成的绑定类，因为发布设置的 `classNamePrefix` 是 `Base`。

完整继承链为：

```text
手写 BasePanel : GComponent
  -> 生成 BaseVipPanel : BasePanel
     -> 业务 VipPanel : BaseVipPanel

手写 BaseView : GComponent
  -> 生成 BaseXXXView : BaseView
     -> 业务 XXXView : BaseXXXView
```

因此，本工程真正要仿照的是“三层继承和绑定结构”，不是照搬参考工程的 `Base` 生成前缀。`FUIWindow` 和 `FUIWidget` 是手写框架基类，FairyGUI 导出插件负责让具体 UI 生成类继承它们。

### 14.2 当前推荐的 TEngine 映射

为与现有 `UIWindow/UIWidget` 语义保持一致，推荐采用：

| SAUnity | TEngine 推荐 | 职责 |
|---|---|---|
| 手写 `BasePanel` | 手写 `FUIWindow` | 顶层窗口，由 `FUIModule` 打开、关闭、分层和缓存 |
| 手写 `BaseView` | 手写 `FUIWidget` | 页面内嵌或可复用组件，不直接进入窗口栈 |
| 生成 `BaseVipPanel` | 生成 `UI_BattleStartPanel` | 保留当前 `UI_` 前缀，只保存节点绑定和资源元数据 |
| 业务 `VipPanel` | 业务 `BattleStartPanel` | 业务生命周期和交互逻辑 |
| `PanelMgr` | `FUIModule` | UI 管理入口 |

推荐继承链：

```text
FUIWindow : GComponent
  -> UI_BattleStartPanel : FUIWindow
     -> BattleStartPanel : UI_BattleStartPanel

FUIWidget : GComponent
  -> UI_BattleResultView : FUIWidget
     -> BattleResultView : UI_BattleResultView
```

这里用 `FUIWindow` 而不是 `FUIPanel`，原因是：

- 和当前工程的 `UIWindow/UIWidget` 术语一一对应；
- `Window` 明确表示由模块管理、进入窗口栈的顶层对象；
- `Panel` 在 FairyGUI/业务命名中也常被当作普通组件名，容易混淆“资源名”和“运行时职责”；
- `FUIWidget` 已能准确表达嵌入组件，不需要再引入 `FUIView` 同义词。

本节的当前决策为 `FUIWindow -> UI_BattleStartPanel -> BattleStartPanel`，替代前文尚未定稿的 `FUIPanel` 命名；`UI_` 前缀保持不变。

### 14.3 Window 与 Widget 的职责边界

#### FUIWindow

- 必须由 `FUIModule` 创建和管理；
- 进入 `UILayer` 对应的 GComponent 容器；
- 参与全屏遮挡、同层排序、缓存和包租约；
- 生命周期由模块显式驱动，不把 `onRemovedFromStage` 当作唯一 Close 信号；
- 建议回调：`OnCreate`、`OnOpen`、`OnRefresh`、`OnHide`、`OnClose`、`OnDispose`；
- 每轮 Open 拥有取消域，Close 时取消本轮任务；
- 可以查到自身包、资源名、URL、Layer、FullScreen 和 CacheMode。

#### FUIWidget

- 作为 Window 或其他 Widget 的子组件存在；
- 不进入 `FUIModule` 的顶层窗口栈；
- 不单独决定 UILayer、全屏遮挡和包卸载；
- 默认继承所属 Window 的资源租约和生命周期域；
- 可以按 `onAddedToStage/onRemovedFromStage` 驱动 `OnOpen/OnClose`，但必须避免重复订阅；
- `OnCreate` 仅一次，`OnDispose` 仅最终释放时一次；
- 应能获得 `OwnerWindow`；若创建时尚未知道 Owner，需要在 Window 完成 XML 构造后统一注入；
- Button、ProgressBar、Loader、普通列表项等不自动视为 Widget。

参考工程在 `RegisterView` 时使用 `UIObjectFactory.SetPackageItemExtension(url, creator)`，由 creator 创建业务 View 并注入上下文。本工程可以借鉴“creator 负责初始化”的思路，但 Widget 的 OwnerWindow 和包租约应由 TEngine 自己的上下文模型提供，不能直接复制参考工程的 EventDispatcher/PanelMgr 参数。

### 14.4 生成代码目标

FairyGUI 发布设置继续使用：

```json
"classNamePrefix": "UI_"
```

窗口期望生成：

```csharp
public partial class UI_BattleStartPanel : FUIWindow
{
    public GButton m_btn;

    public const string URL = "ui://56fffadntdew0";
    public const string PkgName = "UIBattle";
    public const string ResName = "BattleStartPanel";

    public static UI_BattleStartPanel CreateInstance() { /* 默认实现 */ }
    public override void ConstructFromXML(XML xml) { /* 默认成员绑定 */ }
}
```

生成层只允许包含：

- FairyGUI 成员字段；
- `URL/PkgName/ResName` 常量；
- `CreateInstance`；
- `ConstructFromXML` 成员绑定；
- 必要的自动生成标记。

生成层不得包含业务生命周期、网络请求、事件响应或包引用计数逻辑。

### 14.5 导出插件需要做什么

导出能力拆成两个小职责，避免复制 SAUnity 的定制 runtime 假设。

#### A. 组件扩展注册

在 `UnityProject/FGUIProject/plugins/组件扩展/`：

- 注册“界面窗口”扩展，生成基类指向完整命名空间的 `FUIWindow`；
- 注册“界面组件”扩展，生成基类指向完整命名空间的 `FUIWidget`；
- 适配当前 Editor 的三参数 `RegisterComExtension`；
- 让选择结果持久化为组件 XML 的自定义扩展；
- 不按资源类名自动猜测 Window/Widget。

当前仓库附带的 FairyGUI Editor 暴露三参数 API：

```text
RegisterComExtension(name, className, superClassName)
```

插件目录建议为：

```text
UnityProject/FGUIProject/plugins/组件扩展/
  package.json
  main.lua
```

`main.lua` 的目标调用形式如下，命名空间以最终 `GameFUI.asmdef` 中的实际代码为准：

```lua
App.project:RegisterComExtension(
    'Window',
    'GameFUI.FUIWindow',
    'FairyGUI.GComponent')

App.project:RegisterComExtension(
    'Widget',
    'GameFUI.FUIWidget',
    'FairyGUI.GComponent')
```

三个参数分别表示：

- `name`：FairyGUI Editor“扩展”下拉框中的显示名称；
- `className`：发布 C# 代码时使用的自定义父类全名；
- `superClassName`：该自定义扩展基于的 FairyGUI 原生类型；Window/Widget 均基于 `GComponent`。

不建议直接复制参考工程的 `ClearComExtensions()`：它可能清除同一项目中其他插件注册的自定义扩展。除非确认本插件是项目唯一的扩展所有者，否则只注册自己负责的 Window/Widget。

设计人员使用步骤：

1. 重启 FairyGUI Editor 或重新打开 `FGUIProject.fairy`，使项目插件加载；
2. 打开一个组件并选中组件根节点，而不是某个子节点实例；
3. 在属性面板“扩展”下拉框中选择 `Window`、`Widget` 或原有 FairyGUI 类型；
4. 顶层窗口选择 `Window`，需要独立生命周期的嵌入组件选择 `Widget`；
5. Button、ProgressBar 等继续选择 FairyGUI 原生扩展；一个组件不能同时是 Button 和 Widget；
6. 保存后检查组件 XML 已持久化自定义扩展；
7. 发布后检查生成父类分别为 `FUIWindow`、`FUIWidget`。

选择发生在组件定义上，该组件的所有引用实例都会使用相同扩展类型。插件只是让编辑器出现选项并把选择写入工程；运行时仍必须真实存在可编译的 `FUIWindow/FUIWidget` 类型。

#### B. 最小代码发布扩展

当前默认生成器只有 `URL`，没有参考工程用于自动注册和包定位的 `PkgName/ResName`。若确定采用自动绑定注册，需增加项目级最小发布扩展：

- 以当前仓库默认 `GenCode_CSharp.lua` 为基线；
- 完整保留 `CreateInstance`、`ConstructFromXML`、字段绑定和 Binder；
- 只增加 `PkgName`、`ResName`、自动生成文件头等明确需求；
- 不复制 SAUnity 删除成员绑定的生成器；
- 不修改 `Tools/FairyGUI-Editor` 下的全局默认脚本，所有定制留在 `FGUIProject/plugins`，便于项目随仓库版本化。

如果后续决定不通过生成类元数据自动发现包和组件，而是另行生成强类型注册表，则 `PkgName/ResName` 可以进入注册表，不必强制放进每个类；在确定绑定方案前，不应同时维护两套元数据来源。

### 14.6 绑定顺序

推荐启动顺序：

```text
1. 注册 FairyGUI 生成类型（普通组件可直接使用生成类型）
2. 注册业务 Widget 类型，覆盖对应 URL
3. 注册业务 Window 类型，最后覆盖对应 URL
4. 加载/创建第一个包对象
```

最终业务绑定必须早于首次 `UIPackage.CreateObject`。当前 FairyGUI runtime 的 `SetPackageItemExtension` 对同一 URL 允许后注册覆盖前注册，因此生成 Binder 可以保留，但业务注册器必须最后执行。

建议不要完全照搬 SAUnity 的全 AppDomain 反射扫描。可先比较两种方案：

- 反射扫描：实现快，但依赖程序集加载顺序，且对裁剪/热更类型发现更敏感；
- 生成注册表：代码更多但确定性强，更容易检查缺失绑定和包名错误。

首版推荐生成或手写一个显式 `FUIBindingRegistry` 完成端到端验证，再决定是否引入自动扫描。

### 14.7 调整后的实施顺序

#### P0：锁定命名与生成契约

- [ ] 确认手写基类使用 `FUIWindow/FUIWidget`；
- [x] 确认生成类继续使用 `UI_` 前缀；
- [ ] 确认业务类不带 `UI_` 前缀；
- [ ] 确认包元数据唯一来源是生成类常量还是生成注册表；
- [x] 不进行 `UI_ -> Base` 重命名，避免无收益的引用迁移。

#### P1：导出插件原型

- [ ] 实现 Window/Widget 两种组件扩展；
- [ ] 将 `BattleStartPanel` 标记为 Window；
- [ ] 发布为 `UI_BattleStartPanel : FUIWindow`；
- [ ] 保留默认字段绑定、CreateInstance 和 Binder；
- [ ] 若选用类常量方案，生成 `PkgName/ResName`；
- [ ] 连续发布两次并检查输出稳定。

#### P2：最小运行时基类

- [ ] 新增 `IFUIWindow/FUIWindow/FUIWidget`；
- [ ] 只实现构造、显式生命周期入口、OwnerWindow 和 Dispose 骨架；
- [ ] 不在这一阶段实现完整包卸载、缓存和动画；
- [ ] 保证生成代码可编译。

#### P3：业务继承与绑定闭环

- [ ] 新增 `BattleStartPanel : UI_BattleStartPanel`；
- [ ] 在首次 CreateObject 前把 URL 绑定到业务类型；
- [ ] 验证实际创建类型为 `BattleStartPanel`；
- [ ] 新增一个 Widget 示例，验证嵌套创建和 OwnerWindow 注入；
- [ ] 验证业务 Window/Widget 与普通 GComponent 可以共存。

#### P4：接入 FUIModule 和资源系统

- [ ] 将 Window 纳入层级、全屏遮挡、打开/关闭和缓存；
- [ ] 将 Widget 生命周期绑定到所属 Window；
- [ ] 接入包租约、依赖和 YooAsset handle；
- [ ] 完成 HybridCLR 真机加载顺序验证。

### 14.8 本轮决策状态

当前已确认：

```text
框架基类：FUIWindow / FUIWidget
生成前缀：UI_
业务类型：无 UI_ 前缀
生成示例：UI_BattleStartPanel
业务示例：BattleStartPanel
```

不推荐：

- 同时存在 `BasePanel`、`FUIPanel`、`FUIWindow` 三套同义框架基类；
- 把所有 FairyGUI 组件统一改为 `FUIWidget`；
- 手工修改生成文件；
- 直接复制 SAUnity 的自动绑定 runtime 或删减版生成器；
- 在生成类和独立注册表中重复维护不同的包名/组件名。
