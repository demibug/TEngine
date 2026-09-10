# 命名规范与禁止模式

> **适用场景**：C# 类型/成员命名约定、UI 节点前缀规范、禁止的代码模式（Resources.Load/Coroutine/LoadAssetAsync\<Sprite\> 等）| **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（节点前缀）、[resource-api.md](resource-api.md)（禁止模式）

## 核心 API

### C# 类型命名

| 类型 | 规范 | 示例 |
|------|------|------|
| 模块接口 | `IXxxModule` | `IResourceModule` |
| 模块实现 | `XxxModule` | `ResourceModule` |
| 事件接口 | `IXxxEvent` / `IXxxUI` + `[EventInterface(EEventGroup.GroupUI/GroupLogic)]` | `ILoginUI`（工程现有唯一实例，GroupUI） |
| UIWindow 子类 | `XxxUI` / `XxxWindow`（UGUI 侧实例均为 `XxxUI`） | `LoginUI`、`BattleMainUI`、`LogUI` |
| FguiWindow 子类 | `XxxFguiWindow`（Imp 手写）；FairyGUI 官方生成器 Gen 代码为 `UI_xxx : GComponent` | `BundleUsageFguiWindow`、`ModalWaitingFguiWindow` |
| UIWidget 子类 | 手写 `XxxWidget` / `XxxItem`（约定建议，业务代码暂无实例）；剪贴板菜单按 `m_item` 根节点去前缀命名，Inspector 生成路径默认类名带 `Widget` 后缀（见下文 Gen/Imp 约定） | 根节点 `m_itemSlot` → 剪贴板菜单生成 `Slot : UIWidget` |
| 流程状态 | `ProcedureXxx` | `ProcedureLaunch` |
| 状态机状态 | 继承 `FsmState<T>`（泛型） | `ProcedureBase : FsmState<IProcedureModule>` |
| 系统类 | `XxxSystem` | `SingletonSystem`（静态工具类） |
| 配置类（Luban） | `TbXxx` / `Xxx`（行 Bean，Luban 默认约定） | 本工程暂未生成配置代码（luban.conf topModule=GameConfig，配置工程位于仓库根 `Configs/GameConfig/`，UnityProject 之外） |
| 内存池对象 | 实现 `IMemory` | `ObjectBase : IMemory` |

#### 程序集命名（asmdef）

| 类别 | 规范 | 实例 |
|------|------|------|
| 热更业务程序集 | 大驼峰无点分 | `GameLogic`、`GameUpdater`、`GameProto` |
| 框架程序集 | `TEngine.` 点分前缀 | `TEngine.Runtime`、`TEngine.Editor`、`TEngine.FairyGUI` |
| 测试程序集 | `TEngine.*.Tests` / `GameLogic.*.Tests` | `TEngine.GameEvent.Tests`、`GameLogic.FairyGUI.PlayModeTests` |

#### 字段与方法

```csharp
// 私有字段：_小驼峰（生成器生成的 UI 组件引用字段除外，见下方 UI 前缀与 Gen/Imp 约定）
private int _currentHp;
private const int MAX_LEVEL = 100;      // 常量全大写下划线（如 UIModule.LAYER_DEEP / WINDOW_HIDE_LAYER）
public int CurrentHp => _currentHp;     // 公开属性大驼峰
public Action BindGeneratedTypes { get; } // 委托成员大驼峰；业务事件优先 GameEvent/[EventInterface]，少用 C# event

// 异步方法：Async 后缀（如 LoadEntryAsync、CreateWindowAsync、LoadTextureAsync）
public async UniTask LoadDataAsync() { }
// 事件回调：On 前缀（生成器规则 OnClickXxxBtn / OnToggleXxxChange / OnSliderXxxChange / OnTMPDropdownXxxChange）
private void OnHpChanged(int hp) { }
```

---

### UI 节点命名规范

Prefab 节点名前缀决定脚本生成器（`Assets/Editor/UIScriptGenerator/`，同一 partial 类 `ScriptGenerator`，菜单 GameObject/ScriptGenerator）生成的绑定字段。生成器分两套菜单，由 `ScriptGeneratorSetting.useBindComponent` 互斥启停（当前工程为 1，即 BindComponent 模式启用）：

- `UIProperty` / `UIPropertyAndListener`（± UniTask）：剪贴板模式，生成 `FindChild`/`FindChildComponent` 绑定代码复制到剪贴板手工粘贴，要求 `useBindComponent = false`
- `UIPropertyBindComponent` / `UIPropertyAndListenerBindComponent`（± UniTask；UniTask 变体菜单实际名为 `UIPropertyBindComponent - UniTask`、`UIPropertyAndListenerBindComponentUniTask - UniTask`）：剪贴板模式，生成 `m_bindComponent.GetComponent<T>(index)` 绑定代码复制到剪贴板，要求 `useBindComponent = true`；**写 Gen/Imp 文件不走右键菜单，唯一入口是 UIBindComponent Inspector 的「生成脚本」按钮（见下文 Gen/Imp 约定）**

两类菜单对节点名的解析规则一致：按 `ScriptGeneratorSetting.asset`（位于 `Assets/Editor/UIScriptGenerator/`）规则表对节点名做 **StartsWith 前缀匹配，首条命中**（`List.Find(t => name.StartsWith(t.uiElementRegex))`，非正则）：

| 前缀 | 生成类型 | 示例节点名 |
|------|---------|----------|
| `m_go` | `GameObject` | `m_goTopInfo` |
| `m_item` | `GameObject` 字段；根节点则生成 UIWidget 子类（其子节点不再递归） | `m_itemRoleInfo` |
| `m_tf` | `Transform` | `m_tfContainer` |
| `m_rect` | `RectTransform` | `m_rectContainer` |
| `m_text` | `Text` | `m_textError` |
| `m_richText` | `RichTextItem` | `m_richTextDesc` |
| `m_btn` | `Button` | `m_btnClose` |
| `m_img` | `Image` | `m_imgIcon` |
| `m_rimg` | `RawImage` | `m_rimgAvatar` |
| `m_scrollBar` | `Scrollbar` | `m_scrollBarVert` |
| `m_scroll` | `ScrollRect` | `m_scrollList` |
| `m_input` | `InputField` | `m_inputName` |
| `m_grid` | `GridLayoutGroup` | `m_gridItems` |
| `m_hlay` | `HorizontalLayoutGroup` | `m_hlayTabs` |
| `m_vlay` | `VerticalLayoutGroup` | `m_vlayList` |
| `m_slider` | `Slider` | `m_sliderVolume` |
| `m_toggle` | `Toggle` | `m_toggleSound` |
| `m_group` | `ToggleGroup` | `m_groupTab` |
| `m_curve` | `AnimationCurve` | `m_curveAnim` |
| `m_canvasGroup` | `CanvasGroup` | `m_canvasGroupFade` |
| `m_tmp` | `TextMeshProUGUI` | `m_tmpName` |
| `m_tmpInput` | `TMP_InputField` | `m_tmpInputSearch` |
| `m_tmpDropdown` | `TMP_Dropdown` | `m_tmpDropdownLang` |

**注意**：
- 匹配是 `List.Find(t => name.StartsWith(t.uiElementRegex))`（非正则），regex 不含尾部下划线；`m_goTopInfo`、`m_go_TopInfo` 均可命中 `m_go` 规则，建议统一连写风格（现有 prefab/代码均为连写，如 `m_goTopInfo`、`m_textError`）
- 生成字段名分两种模式（`ScriptGeneratorSetting.CodeStyle` 当前为 `MPrefix`）：
  - `UIProperty`（剪贴板）模式：字段名 = 节点名按 `CodeStyle` 调整前缀。`UnderscorePrefix` → `m_btnClose` 生成 `_btnClose`（如 BattleMainUI 的 `_rectContainer`，历史 UnderscorePrefix 时期生成）；`MPrefix` → 生成 `m_btnClose`（如 LogUI 的 `m_textError`）
  - `BindComponent` 模式（当前启用）：字段名 = CodeStyle 前缀 + 去掉 `m_`/`_` 前缀后的剩余节点名（不剥离 `btn`/`text` 类型段；`GetVariableName` 实现），如 CodeStyle=MPrefix 时 `m_btnClose` → `m_btnClose`（保持不变）、`_btnClose` → `m_btnClose`；节点名不带 `m_`/`_` 前缀时原样保留（不加前缀）
- `m_scrollBar` 必须排在 `m_scroll` 之前，否则 `m_scrollBar_X` 会被 `m_scroll` 先命中（setting 中 scrollBar 确在 scroll 之前）
- `m_tmpInput` / `m_tmpDropdown` 必须排在 `m_tmp` 之前，否则会被 `m_tmp` 先命中（setting 中已按此顺序；这三条规则在 `#if TextMeshPro` 内）
- `m_richText` 与 `m_text` 互不为前缀，无匹配冲突（setting 中 `m_text` 在 `m_richText` 之前）
- `m_richText` / `m_curve` 规则引用的 `RichTextItem`、`AnimCurveObject` 类型当前工程未实现，启用会生成无法解析的类型，慎用
- 不需要绑定的节点无需加前缀，生成器会忽略；TMP 规则依赖 `TextMeshPro` 宏定义
- 表中示例节点名仅为格式示意，并非全部来自现有代码（真实实例如 BattleMainUI 的 `m_goTopInfo`/`m_rectContainer`/`m_itemRoleInfo`、LogUI 的 `m_textError`/`m_btnClose`）

### 脚本生成管线与 Gen/Imp 约定（BindComponent 模式）

当前工程 `useBindComponent = 1`，标准流程由 `UIBindComponent` 组件驱动（组件本体位于 `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBindComponent/`，GameLogic 热更程序集）：

1. **Prefab 侧**：根节点挂 `UIBindComponent` 组件并按遍历顺序登记命中的节点组件（GameObject/m_item 等只登记 RectTransform 占位）。登记入口为 UIBindComponent Inspector 的「重新绑定组件」按钮（或 Prefab 编辑模式 Inspector 注入的 Bind 按钮），二者均调用 `ScriptGenerator.GenerateUIComponentScript()`：先 `Clear()` 再按规则表重新登记；节点缺少对应组件时报错并跳过（不占索引）
2. **生成配置**：组件上序列化 `className`、`uiType`（UIWindow/UIWidget）、`genCodePath`、`impCodePath`、`isGenImpClass` 字段；`className` 默认值由 Inspector「默认」按钮计算——UIWindow 即物体名；UIWidget 为 `{父 Window 名去 "UI"}{物体名去 m_item 前缀}Widget`（如父 Window `LoginUI` + 节点 `m_itemSlot` → `LoginSlotWidget`）
3. **Gen 文件**：Inspector「生成脚本」/「生成UniTask脚本」按钮写 `<className>_Gen.g.cs`（UTF-8、只读、`<auto-generated>` 头）到 `genCodePath`（当前配置 `Assets/GameScripts/HotFix/GameLogic/UI/Gen`，目录不存在时自动创建），内容为 `public partial class` + 字段声明（`NullableEnable=1` 时带 `= null!;`）+ `protected override void ScriptGenerator()` 绑定代码（运行时经 `m_bindComponent.GetComponent<T>(index)` 按索引取组件），事件回调只声明 partial 签名：按钮 `private partial void OnXxxBtn();`（UniTask 版为 `private partial UniTaskVoid OnXxxBtn();`）、Toggle/Slider/TMP_Dropdown 为 `private partial void OnToggleXxxChange(bool isOn)` / `OnSliderXxxChange(float value)` / `OnTMPDropdownXxxChange(int selectedIndex)`
4. **Imp 文件**：勾选 `isGenImpClass` 时同步生成 partial 实现类 `{className}.cs` 到 `impCodePath`（当前配置 `Assets/GameScripts/HotFix/GameLogic/UI`），存放事件回调实现，已存在则跳过不覆盖；uiType 为 UIWindow 时 Imp 文件同样带 `[Window(UILayer.UI, location:"xxx")]` 特性
5. **剪贴板菜单的类名约定**（与 Inspector 路径不同）：根节点名即类名；根节点以 `m_item` 开头则按 UIWidget 生成（类名去 `m_item` 前缀）

> 注意：`UI/Gen` 目录当前不存在（尚未有 UGUI 窗口走该管线生成，首次生成时自动创建）；`UI/FGUI/Gen` 是 FairyGUI 官方生成器产物，与此管线无关。

---

## 使用模式

### 异步编程规范

```csharp
// ✅ UniTask 替代 Task，UniTaskVoid 替代 void async
public async UniTask<int> GetDataAsync() { }
public async UniTaskVoid StartBattleAsync() { }  // 调用方加 .Forget()（如 GameApp 初始化）

// ✅ CancellationToken 防止销毁后回调（FguiLifetimeScope / UIWindow 同款）
private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
public void Dispose() { _cancellation.Cancel(); _cancellation.Dispose(); }

// ✅ 组合令牌：生命周期令牌 + 超时令牌（FguiPackageService 同款）
using var timeoutCts = new CancellationTokenSource();
var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(loadToken, timeoutCts.Token);
```

---

## 常见错误

| 错误 | 原因 | 修复 |
|------|------|------|
| 节点 `m_tInput_Search` 不生成 TMP_InputField | 旧文档前缀 `m_tInput_` 已过时 | 正确前缀为 `m_tmpInput_` |
| `m_tmp_Search` 生成 TextMeshProUGUI 而非输入框 | 节点名以 `m_tmp` 开头命中 `m_tmp` 规则（StartsWith 首条命中） | TMP 输入框需命名为 `m_tmpInput_Search` |
| 前缀匹配顺序错误 | 按规则表顺序 StartsWith 匹配，存在前缀包含关系时长前缀需先声明 | scrollBar 在 scroll 前、tmpInput/tmpDropdown 在 tmp 前 |
| 缺少 TMP_Dropdown 绑定 | 旧文档未记录此前缀 | 使用 `m_tmpDropdown_` 前缀 |
| 节点 `m_canvas_X` / `m_dropdown_X` 不生成绑定 | setting 规则表中无对应前缀（枚举含 Canvas/Dropdown 但未配置规则） | 需先在 ScriptGeneratorSetting 添加规则，否则节点被忽略 |
| 运行时报 "根物体: xxx 缺少组件 UIBindComponent, 请检查！！！" | BindComponent 模式生成代码依赖根节点 `UIBindComponent` 按索引取组件 | 在 prefab 根节点通过 UIBindComponent Inspector「重新绑定组件」重建组件列表；勿手工增删组件列表顺序 |

### 禁止的异步模式

```csharp
// ❌ Task → 用 UniTask
// ❌ Coroutine → 用 async/await
// ❌ Update 中 await → 用 Timer
// ❌ 忽略 UniTask 返回值 → 加 .Forget() 或 await
```

### 禁止的代码模式

```csharp
// ❌ Resources.Load → GameModule.Resource（框架内部 FromResources 兜底除外，业务代码禁止）
// ❌ Instantiate(prefab) → LoadGameObjectAsync（同上，仅框架兜底路径使用）
// ❌ FindObjectOfType → GameModule 或事件
// ❌ Update 中 new 对象 → MemoryPool
// ❌ 跨模块强引用 → GameEvent
// ❌ 外部访问 UI 私有组件 → 事件或公共方法
// ❌ 静态持有 Asset 引用 → 内存泄漏
// ❌ 忽略 async 返回值 → await 或 .Forget()
```

---

## 交叉引用

- 架构总览见 [architecture.md](architecture.md)
- UI 生命周期见 [ui-lifecycle.md](ui-lifecycle.md)
- UI 进阶模式见 [ui-patterns.md](ui-patterns.md)
