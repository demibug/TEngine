# UI 开发模式与模板

> **适用场景**：UIWidget 8 种创建方式（CreateWidget/CreateWidgetByPath/CreateWidgetByPrefab 等）、AdjustIconNum 列表数量管理、动态列表复用模式、ScriptGenerator 代码生成与节点绑定 | **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（生命周期）、[naming-rules.md](naming-rules.md)（节点前缀）

## 一、核心 API

### UIWidget 创建方式（8 个方法）

> 全部定义在 `UIBase`（UIWidget 与 UIWindow 的共同基类，均位于 `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/`、热更程序集 GameLogic），因此**窗口和 Widget 实例上均可调用**。泛型 `T` 约束：`where T : UIWidget, new()`。

| # | 方法签名 | 说明 | 同步/异步 |
|---|---------|------|----------|
| 1 | `CreateWidget<T>(string goPath, bool visible = true)` | 从子节点路径创建（Prefab 中已有节点）；节点不存在返回 `null` | 同步 |
| 2 | `CreateWidget<T>(Transform parentTrans, string goPath, bool visible = true)` | 从指定父节点 + 子路径创建 | 同步 |
| 3 | `CreateWidget<T>(GameObject goRoot, bool visible = true)` | 从 GameObject 创建 | 同步 |
| 4 | `CreateWidgetByPath<T>(Transform parentTrans, string assetLocation, bool visible = true)` | 按资源定位地址创建，内部调用 `UIModule.Resource.LoadGameObject`（同步实例化，Destroy 时自动 UnloadAsset，无需手动释放） | 同步 |
| 5 | `CreateWidgetByPathAsync<T>(Transform parentTrans, string assetLocation, bool visible = true)` | 按资源定位地址创建，内部调用 `LoadGameObjectAsync`，返回 `UniTask<T>` | 异步 |
| 6 | `CreateWidgetByPrefab<T>(GameObject goPrefab, Transform parentTrans = null, bool visible = true)` | 克隆预制体创建 | 同步 |
| 7 | `CreateWidgetByType<T>(Transform parentTrans, bool visible = true)` | 用 `typeof(T).Name` 作资源地址，内部调用 `CreateWidgetByPath` | 同步 |
| 8 | `CreateWidgetByTypeAsync<T>(Transform parentTrans, bool visible = true)` | 用 `typeof(T).Name` 作资源地址，内部调用 `CreateWidgetByPathAsync` | 异步 |

> UIWidget 还有一组实例级低层方法：`Create(parentUI, widgetRoot, visible)`、`CreateByPath(resPath, parentUI, parentTrans, visible)`、`CreateByPrefab(parentUI, goPrefab, parentTrans, visible)`。其中 `Create`、`CreateByPrefab` 分别由 `CreateWidget<T>(GameObject)`、`CreateWidgetByPrefab` 内部调用；`CreateByPath` 是独立公开方法（内部同样经 `UIModule.Resource.LoadGameObject` 加载，当前未被高层方法封装）。所属 UI 正在关闭或框架已停止时调用会抛 `ObjectDisposedException`。

### 列表管理方法（2 个方法）

```csharp
// 同步调整列表数量
void AdjustIconNum<T>(List<T> listIcon, int number, Transform parentTrans,
    GameObject prefab = null, string assetPath = "")
    where T : UIWidget, new()

// 异步调整列表数量（支持分帧 + 逐项回调）
void AsyncAdjustIconNum<T>(List<T> listIcon, int tarNum, Transform parentTrans,
    GameObject prefab = null, string assetPath = "",
    int maxNumPerFrame = 5, Action<T, int> updateAction = null)
    where T : UIWidget, new()
```

**AdjustIconNum 逻辑**：数量不足时补建——若 `prefab != null` 用 `CreateWidgetByPrefab`，否则用 `CreateWidgetByType`；数量超出时移除多余项并 `Destroy`。`assetPath` 参数**未被内部使用**，传了也没有效果。

**AsyncAdjustIconNum 逻辑**：内部用 `.Forget()` 启动异步任务（返回 `void`，调用方无法 await）。按索引遍历到 `tarNum`：已有项直接复用，缺少项才创建——`prefab != null` 用 `CreateWidgetByPrefab`（同步克隆），否则用 `CreateWidgetByPathAsync(parentTrans, assetPath)`（**此分支中 assetPath 作为资源地址生效**）。每处理一项（含复用项）调用一次 `updateAction(widget, index)`；每帧最多处理 `maxNumPerFrame` 项后 `UniTask.Yield()` 分帧。结束时超出 `tarNum` 的多余项被移除并 `Destroy`。

---

## 二、使用模式

### 完整 UIWindow 示例

```csharp
[Window(UILayer.UI, "BattleMainUI", fullScreen: true)]
public class BattleMainUI : UIWindow
{
    private Button    _btnBack;
    private Text      _textHp;
    private Text      _textGold;
    private Transform _tfSkillPanel;
    private readonly List<SkillSlotWidget> _skillSlots = new();

    protected override void ScriptGenerator()
    {
        _btnBack      = FindChildComponent<Button>("m_btnBack");
        _textHp       = FindChildComponent<Text>("m_textHp");
        _textGold     = FindChildComponent<Text>("m_textGold");
        _tfSkillPanel = FindChild("m_tfSkillPanel");
        // 按钮绑定（无 RegisterButtonClick 之类的辅助方法，直接 AddListener）
        _btnBack.onClick.RemoveAllListeners();
        _btnBack.onClick.AddListener(OnBackClicked);
    }

    protected override void RegisterEvent()
    {
        AddUIEvent<int>(IBattleEvent_Event.OnHpChanged, RefreshHp);
        AddUIEvent<int>(IBattleEvent_Event.OnGoldChanged, RefreshGold);
    }

    protected override void OnCreate()
    {
        for (int i = 0; i < 4; i++)
        {
            var slot = CreateWidget<SkillSlotWidget>($"m_tfSkillPanel/Slot{i}");
            _skillSlots.Add(slot);
        }
    }

    protected override void OnRefresh()
    {
        RefreshHp(PlayerData.Hp);
        RefreshGold(PlayerData.Gold);
        for (int i = 0; i < _skillSlots.Count; i++)
            _skillSlots[i].SetData(PlayerData.Skills[i]);
    }

    private void RefreshHp(int hp)    => _textHp.text   = $"HP: {hp}";
    private void RefreshGold(int gold) => _textGold.text = $"Gold: {gold}";
    private void OnBackClicked() => GameModule.UI.CloseUI<BattleMainUI>();
}
```

> 真实项目中 `ScriptGenerator()` 的绑定代码由 ScriptGenerator 代码生成工具生成（见下文"代码生成与节点绑定"），实际示例见 `Assets/GameScripts/HotFix/GameLogic/UI/BattleMainUI/BattleMainUI.cs`。示例中的事件 ID（`IBattleEvent_Event`）与数据源（`PlayerData`、`ItemConfig`、`SkillSlotWidget`）为示意占位，实际项目请替换为真实事件常量与配置类型。

### UIWidget 模板

```csharp
public class ItemWidget : UIWidget
{
    private Text  _textName;
    private Image _imgIcon;

    protected override void ScriptGenerator()
    {
        _textName = FindChildComponent<Text>("m_textName");
        _imgIcon  = FindChildComponent<Image>("m_imgIcon");
    }

    public void SetData(ItemConfig cfg)
    {
        _textName.text = cfg.Name;
        _imgIcon.SetSprite(cfg.IconPath);  // 扩展方法，内部经资源缓存池管理，无需手动 UnloadAsset
    }
}
```

### Widget 创建方法速查

```csharp
// 1. Prefab 中已有节点 — 最常用（节点不存在返回 null）
var w1 = CreateWidget<ItemWidget>("path/to/node");

// 2. 指定父级 + 子路径
var w2 = CreateWidget<ItemWidget>(parentTrans, "goPath");

// 3. 直接用 GameObject
var w3 = CreateWidget<ItemWidget>(goRoot);

// 4. 按资源定位地址同步加载（阻塞主线程，慎用）
var w4 = CreateWidgetByPath<ItemWidget>(parent, "ItemWidget");

// 5. 按资源定位地址异步加载（推荐）
var w5 = await CreateWidgetByPathAsync<ItemWidget>(parent, "ItemWidget");

// 6. 克隆预制体 — 列表项常用
var w6 = CreateWidgetByPrefab<ItemWidget>(prefab, parent);

// 7. 用类型名作资源地址（同步）— 资源名必须与类名一致
var w7 = CreateWidgetByType<ItemWidget>(parentTrans);

// 8. 用类型名作资源地址（异步）
var w8 = await CreateWidgetByTypeAsync<ItemWidget>(parentTrans);
```

### 列表 Widget 复用

```csharp
// 同步调整数量（有 prefab 用 CreateWidgetByPrefab，无 prefab 用 CreateWidgetByType）
AdjustIconNum<ItemWidget>(listIcon, number: items.Count, parent, prefab: prefab);

// 注意：AdjustIconNum 的 assetPath 参数未被实现使用，传了无效果
// 同步版创建后需手动刷新数据
for (int i = 0; i < items.Count; i++)
    listIcon[i].SetData(items[i]);

// 异步调整数量（分帧处理；updateAction 对每个槽位回调，含复用项，可直接在里面 SetData）
AsyncAdjustIconNum<ItemWidget>(listIcon, tarNum: items.Count, parent,
    prefab: prefab, maxNumPerFrame: 3,   // 每帧最多处理 3 个（不传默认 5）
    updateAction: (widget, idx) => widget.SetData(items[idx]));
```

### 手动绑定 API

```csharp
protected override void ScriptGenerator()
{
    var trans = FindChild("m_tfContainer");                        // Transform
    var go    = FindChild("m_goEffect").gameObject;                // GameObject
    var btn   = FindChildComponent<Button>("m_btnStart");          // 泛型组件
    var rect  = FindChildComponent<RectTransform>("m_rectPanel");
    btn.onClick.RemoveAllListeners();
    btn.onClick.AddListener(OnClickStartBtn);
}
```

---

## 三、代码生成与节点绑定（ScriptGenerator）

编辑器菜单 `GameObject/ScriptGenerator/`（实现位于 `Assets/Editor/UIScriptGenerator/`，类 `TEngine.Editor.UI.ScriptGenerator`）。选中 Prefab 根节点执行，按**前缀规则**遍历子节点生成绑定代码。

### 两种绑定模式

由 `ScriptGeneratorSetting.asset` 的 `UseBindComponent` 开关决定（菜单按开关显示/隐藏）：

| 模式 | 菜单项 | 生成的绑定代码 |
|------|--------|--------------|
| FindChild 模式 | `UIProperty` / `UIPropertyAndListener` | `FindChild` / `FindChildComponent<T>("路径")` |
| UIBindComponent 模式 | `UIPropertyBindComponent` / `UIPropertyAndListenerBindComponent` | `m_bindComponent.GetComponent<T>(索引)` |

> 当前工程 `ScriptGeneratorSetting.asset` 实际配置（`Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.asset`）：`UseBindComponent = true`（默认启用 UIBindComponent 模式，FindChild 系菜单隐藏）、`CodeStyle = MPrefix`（字段保持 `m_` 前缀）、`NullableEnable = true`（生成字段带 ` = null!;`）。菜单 `GameObject/ScriptGenerator/About` 或 `TEngine/Settings/TEngineUISettings` 可打开设置界面。

带 `Listener` 的菜单额外生成按钮/开关/滑条的事件监听（BindComponent 模式还支持 TMP_Dropdown 回调，FindChild 模式不支持）。每种模式均有 `- UniTask` 变体，回调生成方式不同：

- FindChild 模式：`private async UniTaskVoid OnClickXxxBtn()`（**非 partial**，生成在剪贴板代码内）
- BindComponent 模式：`private partial UniTaskVoid OnClickXxxBtn();` 声明，实现写在"生成实现类"文件中

> 注意：BindComponent 组第二个 UniTask 菜单实际名为 `UIPropertyAndListenerBindComponentUniTask - UniTask`（命名有重复，以编辑器实际菜单为准）。

FindChild 模式（含 Listener）生成的代码形如（真实示例 `GameLogic/UI/BattleMainUI/BattleMainUI.cs`）：

```csharp
[Window(UILayer.UI,location:"BattleMainUI")]
class BattleMainUI : UIWindow
{
    #region 脚本工具生成的代码
    private RectTransform _rectContainer;
    private GameObject _itemTouch;
    protected override void ScriptGenerator()
    {
        _rectContainer = FindChildComponent<RectTransform>("m_rectContainer");
        _itemTouch = FindChild("m_rectContainer/m_itemTouch").gameObject;
    }
    #endregion
}
```

### 规则要点

- **前缀规则**：`ScriptGeneratorSetting.asset` 配置（默认 `m_go`→GameObject、`m_item`→GameObject(Widget)、`m_tf`→Transform、`m_rect`→RectTransform、`m_text`→Text、`m_btn`→Button、`m_img`→Image、`m_rimg`→RawImage、`m_scrollBar`→Scrollbar、`m_scroll`→ScrollRect、`m_input`→InputField、`m_slider`→Slider、`m_toggle`→Toggle、`m_tmp`→TextMeshProUGUI 等，另有 `m_richText`/`m_grid`/`m_hlay`/`m_vlay`/`m_group`/`m_curve`/`m_canvasGroup`/`m_tmpInput`/`m_tmpDropdown`）；字段名按 `CodeStyle` 转换（`UnderscorePrefix`：节点 `m_btnBack` → 字段 `_btnBack`；`MPrefix`：保持 `m_btnBack`）
- **Widget 识别**：根节点名以"字段前缀 + WidgetName"开头（WidgetName 默认 `item`，可配置；前缀跟随 CodeStyle——当前工程 MPrefix 下为 `m_item`，UnderscorePrefix 下为 `_item`）→ 生成 `class Xxx : UIWidget`（类名去掉前缀），且**不再递归其子节点**（跳过递归匹配的是规则表中勾选 UIWidget 的前缀，当前配置为 `m_item`）；其余根节点生成 `[Window(UILayer.UI, location:"Xxx")] class Xxx : UIWindow`
- **回调命名**：`OnClick{Xxx}Btn`、`OnToggle{Xxx}Change`、`OnSlider{Xxx}Change`、`OnTMPDropdown{Xxx}Change`
- **输出**：普通菜单生成到**剪贴板**，自行粘贴（FindChild 模式生成的类体**不带** `partial`；BindComponent 模式无论剪贴板还是写文件均生成 `public partial class`）。生成区带 `#region 脚本工具生成的代码` 标记，可整体删除重新生成

### UIBindComponent 模式

1. 根节点挂 `UIBindComponent`（无独立 MenuItem，通过 `AddComponent` 添加：由 Inspector 顶部注入的 "Bind UI Component" 按钮（注入实现位于 `Assets/Editor/TEngineSettingsProvider/UnityEditorInspectorDrawHelper.cs`）或 Inspector "重新绑定组件" 按钮触发）。收集时按前缀规则遍历子节点（GameObject/Widget 节点收集其 `RectTransform`，其余收集对应组件；索引按遍历顺序递增）
2. 生成代码按**索引**取组件（`GameLogic/Module/UIModule/UIBindComponent/UIBindComponent.cs`）：

```csharp
m_bindComponent = gameObject.GetComponent<UIBindComponent>();
_btnStart = m_bindComponent.GetComponent<Button>(0);
```

3. Inspector 的"生成脚本"/"生成UniTask脚本"直接写入只读文件 `Xxx_Gen.g.cs`（默认目录 `Assets/GameScripts/HotFix/GameLogic/UI/Gen/`，可在 Inspector 中按生成物配置）；另有"生成标准版绑定代码"/"生成UniTask代码"按钮走剪贴板。勾选"生成实现类"后同时向实现类路径（默认 `Assets/GameScripts/HotFix/GameLogic/UI`）生成事件回调的 partial 实现文件（含 `#region 事件`；实现类文件已存在时跳过生成）

---

## 四、常见错误

1. **混淆同步/异步创建**：`CreateWidgetByPath` 是同步阻塞，`CreateWidgetByPathAsync` 是异步非阻塞。在 UIWindow 的 `OnCreate` 中优先使用异步版本，避免卡顿。
2. **CreateWidgetByType 的资源命名**：该方法用 `typeof(T).Name` 作为 assetLocation，所以资源名必须与 Widget 类名完全一致，否则加载失败。
3. **AsyncAdjustIconNum 无 prefab 时与同步版行为不同**：无 prefab 时异步版调用 `CreateWidgetByPathAsync`（以 `assetPath` 为资源地址，此时必须传有效地址，传空会加载失败），同步版 `AdjustIconNum` 无 prefab 时调用 `CreateWidgetByType`（且其 `assetPath` 无效）。混用时注意部分 Widget 可能尚未创建完成。
4. **AsyncAdjustIconNum 返回 void**：该方法内部用 `.Forget()` 启动异步任务，调用方无法 await 等待完成。如需等待创建完成再操作，应手动循环使用 `CreateWidgetByPathAsync`。
5. **AdjustIconNum 的 assetPath 参数无效**：同步版 `AdjustIconNum` 声明了 `assetPath` 参数，但内部实现并未使用（只看 `prefab` 是否为 null 决定 `CreateWidgetByType` 还是 `CreateWidgetByPrefab`），传 `assetPath` 无实际效果。
6. **不存在 RegisterButtonClick 等绑定辅助方法**：按钮绑定用 `btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(OnClickXxxBtn);`（与代码生成器输出一致）。
7. **窗口/Widget 关闭后创建 Widget 抛异常**：所属 UI 正在关闭时调用创建方法会抛 `ObjectDisposedException`，且创建失败的实例会被自动销毁，不会泄漏。

---

## 五、交叉引用

| 主题 | 文档 |
|------|------|
| UIWindow 生命周期、层级、属性 | [ui-lifecycle.md](ui-lifecycle.md) |
| 事件系统（AddUIEvent / GameEvent） | [event-system.md](event-system.md) |
| 节点前缀命名规范 | [naming-rules.md](naming-rules.md#ui-节点命名规范) |
| 资源加载/卸载 API | [resource-api.md](resource-api.md) |
