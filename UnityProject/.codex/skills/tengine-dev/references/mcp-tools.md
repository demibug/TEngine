# MCP 工具指南

> **环境状态**：工程内已内嵌安装 MCP for Unity 包（`Packages/MCPForUnity`，`com.coplaydev.unity-mcp` v9.7.3，随工程自动解析）。UnityProject 根目录无 `.mcp.json`，opencode 亦无 MCP 配置，当前未配置 MCP 客户端入口，Python 服务器未接入，工具当前不可达；本文档为配置 MCP 后的参考，工具可用性以实际配置为准。

> **适用场景**：MCP 场景管理（场景加载/对象创建）、GameObject 操作、UI Prefab 创建、脚本生成、编辑器自动化、测试工具 | **关联文档**：[mcp-visual.md](mcp-visual.md)（材质/Shader/VFX/动画）、[naming-rules.md](naming-rules.md)（命名约定）
>
> **实际服务器**：MCP for Unity v9.7.3（`com.coplaydev.unity-mcp`，CoplayDev，Python 端经 uvx 启动）。
> 工具以 Unity 端 handler 为准：`Packages/MCPForUnity/Editor/Tools/`，工具名由 `[McpForUnityTool("xxx")]` 特性注册。

## 工具分组（重要）

仅 `core` 组默认启用；其他组默认隐藏，需在 Unity「MCP Tools」窗口开启，或调用服务器端元工具 `manage_tools` 启用：

| 组 | 工具 |
|----|------|
| `core`（默认启用） | manage_scene / manage_gameobject / manage_components / find_gameobjects / manage_prefabs / manage_script / manage_asset / manage_build / manage_editor / execute_menu_item / read_console / refresh_unity / batch_execute / manage_camera / manage_physics / manage_graphics / manage_material / manage_packages |
| `ui` | manage_ui（**UI Toolkit（UXML/USS）工具，与 UGUI 无关**） |
| `scripting_ext` | execute_code / manage_scriptable_object |
| `testing` | run_tests / get_test_job |
| `vfx` / `animation` / `probuilder` / `profiling` / `docs` | 见 mcp-visual.md 等 |

证据：`McpForUnityToolAttribute.cs:33-40`（组定义与默认值 core）。

## 核心原则：batch_execute 优先

批量操作比单次调用快 10~100 倍，多对象任务必须用 `batch_execute`：

```json
{
  "tool": "batch_execute",
  "commands": [
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "Root" } },
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "Child", "parent": "Root" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "Child", "componentType": "Button" } }
  ],
  "failFast": true
}
```

- 默认每批最多 25 条命令（可在 MCP Tools 窗口配置，硬上限 100）
- `failFast` **默认 false**（继续执行并汇总错误）；示例中显式 `true` 遇错即停
- 命令在主线程顺序执行；`parallel` 参数仅记录警告，不会并行

证据：`BatchExecute.cs:20-23`（25/100 上限）、`BatchExecute.cs:54`（failFast 默认 false）、`BatchExecute.cs:58-61`（parallel 警告）。

## 目标定位方式（target）

| 方式 | 示例值 | 说明 |
|------|--------|------|
| 名称 | `"Canvas"` | 场景中第一个匹配 |
| 层级路径 | `"UIRoot/Canvas/Panel"` | 含 `/` 时按路径查找 |
| InstanceID | `12345` | 最可靠，不受重名影响 |
| Tag / Layer | `searchMethod: "by_tag"` | 查找指定 Tag / Layer 对象 |

`searchMethod`：`by_name`（默认）、`by_path`、`by_id`、`by_tag`、`by_layer`、`by_component`。

未传 `searchMethod` 时多数工具按 `by_id_or_name_or_path` 混合解析。证据：`GameObjectLookup.cs:40-49`（枚举全表）、`GameObjectMoveRelative.cs:26`。

---

## 场景与 GameObject

### manage_scene

| action | 说明 | 关键参数 |
|--------|------|---------|
| `get_active` | 当前场景信息 | — |
| `get_hierarchy` | 场景层级（分页） | `parent`, `pageSize`, `cursor`, `maxDepth`, `maxNodes`, `maxChildrenPerNode`, `includeTransform` |
| `save` | 保存当前场景（可另存） | `name`, `path` |
| `load` | 加载场景 | `name`/`path` 或 `buildIndex`；`additive: true` 附加加载 |
| `create` | 创建新场景 | `name`, `path`（默认 Assets/Scenes/）, `template` |
| `screenshot` | 截图到 Assets/Screenshots/ | `fileName`, `superSize`, `capture_source`(game_view/scene_view), `camera`, `include_image`, `batch`(surround/orbit), `view_target` |
| `scene_view_frame` | Scene View 聚焦到目标 | `scene_view_target` |
| `get_build_settings` | Build Settings 场景列表 | — |
| `close_scene` / `set_active_scene` / `get_loaded_scenes` / `move_to_scene` | 多场景编辑 | `sceneName`/`scenePath`, `target`, `removeScene` |
| `validate` | 场景校验 | `autoRepair` |

注意：Build Settings 增删场景已迁移到 `manage_build`（action=`scenes`）。

`get_hierarchy` 返回含 `next_cursor`，`truncated=true` 时需翻页。

证据：`ManageScene.cs:232-303`（action 路由全表）、`ManageScene.cs:291-294`（build settings 迁移提示）。

### manage_gameobject

| action | 说明 | 关键参数 |
|--------|------|---------|
| `create` | 创建 GO（支持直接实例化 prefab） | `name`, `parent`, `position`, `rotation`, `scale`, `layer`, `tag`, `primitiveType`, `componentsToAdd`(数组), `prefabPath`, `saveAsPrefab` |
| `modify` | 修改属性 | `target`, `position`, `rotation`, `scale`, `setActive`, `name`/`newName`, `parent`, `layer`, `tag`, `isStatic`, `componentsToAdd`, `componentsToRemove`, `componentProperties` |
| `delete` | 删除 GO | `target` |
| `duplicate` | 复制 GO | `target`, `name`, `position` |
| `move_relative` | 相对参考对象移动 | `target`, `reference_object`(必需), `offset`(Vector3) 或 `direction`(right/left/up/down/forward/back)+`distance`(默认1), `world_space`(默认 true) |
| `look_at` | 朝向目标 | `target`, `lookAtTarget`, `lookAtUp` |

- `primitiveType`：Unity PrimitiveType 枚举（Cube/Sphere/Plane/Cylinder/Capsule/Quad），大小写不敏感
- `create` **没有 `componentType` 单数参数**，创建时挂组件用 `componentsToAdd: ["Canvas","CanvasScaler"]`
- 对 .prefab 资产路径操作会报错并提示改用 `manage_prefabs`/`manage_asset`

证据：`ManageGameObject.cs:93-104`（6 个 action）、`GameObjectCreate.cs:27,123-146`、`GameObjectModify.cs`、`GameObjectMoveRelative.cs:20-35`、`GameObjectLookAt.cs`、`ManageGameObject.cs:74-85`（prefab 拦截）。

### find_gameobjects

轻量搜索工具，**只返回 instanceID 分页列表**（不返回组件/路径等详情；详情用资源 `unity://scene/gameobject/{id}`）。

| 参数 | 说明 |
|------|------|
| `searchTerm`（或 `target`） | 搜索词（必需） |
| `searchMethod` | `by_name`(默认)/`by_path`/`by_tag`/`by_layer`/`by_component`/`by_id` |
| `includeInactive` | 含未激活对象（别名 `searchInactive`） |
| `pageSize` / `cursor` | 分页（默认 50，上限 500） |

返回：`instanceIDs[]`、`totalCount`、`nextCursor`、`hasMore`。拿到 ID 后用 `manage_gameobject`/资源读取详情。

证据：`FindGameObjects.cs:10-14`（仅 ID 注释）、`FindGameObjects.cs:34-50`（参数）、`FindGameObjects.cs:64-72`（返回结构）。

### manage_components

| action | 说明 | 关键参数 |
|--------|------|---------|
| `add` | 添加组件 | `target`, `componentType`, `properties`（或 `componentProperties`） |
| `remove` | 删除组件 | `target`, `componentType`, `componentIndex`（同类型多实例时指定） |
| `set_property` | 设置组件属性 | `target`, `componentType`, `properties` |

常用组件类型：`Rigidbody`/`Rigidbody2D`、`BoxCollider`/`SphereCollider`、`MeshRenderer`/`SpriteRenderer`、`Light`、`Camera`、`AudioSource`、`Animator`、`NavMeshAgent`、`ParticleSystem`、`Canvas`/`CanvasScaler`/`GraphicRaycaster`、`VerticalLayoutGroup`/`HorizontalLayoutGroup`

`set_property` 属性值支持复合类型 JSON 对象（Color/Vector2/3/4/Quaternion/Rect/Bounds），例如 `"color": {"r":0,"g":0,"b":0,"a":0.8}`。反射失败时回退 SerializedProperty 路径（支持 UnityEvent 等特殊序列化）。

证据：`ManageComponents.cs:53-58`（action 路由）、`ManageComponents.cs:105`（properties 别名）、`ManageComponents.cs:150-167`（componentIndex）、`ComponentOps.cs:149-195`、`UnityTypeConverters.cs:12-214`。

### TEngine 场景约定

| 约定 | 说明 |
|------|------|
| **UIRoot** | 场景必须存在，`UIModule.OnInit()` 自动查找 |
| **场景路径** | `Assets/Scenes/` 或 `Assets/AssetRaw/Scenes/` |
| **层级** | 参考约定：GameRoot → Logic / UI / Effect（当前 main.unity 未搭建，仅含 Main Camera） |
| **禁止场景中直接放 UI** | UI 通过 `UIModule.ShowUIAsync` 动态加载 |

---

## UI Prefab 拼接

> v9.7.3 的 `manage_ui` 是 UI Toolkit（UXML/USS）工具（actions：create/read/update/delete/list/attach_ui_document/detach_ui_document/create_panel_settings/update_panel_settings/get_visual_tree/render_ui/link_stylesheet/modify_visual_element/ping 等），**没有 create_button/create_text 等 UGUI action**。
> UGUI Prefab 拼接一律用 `manage_gameobject`（`componentsToAdd`）+ `manage_components`（`set_property`）组合。

### 前缀与 MCP 创建工具对照

完整前缀→C#类型绑定见 [naming-rules.md](naming-rules.md#ui-节点命名规范)。

| 前缀 | MCP 创建方式 |
|------|------------|
| `m_btn_` | `manage_gameobject` create + `componentsToAdd:["Button"]`（文本用子节点 Image+Text/TMP） |
| `m_img_` | `manage_gameobject` create + `componentsToAdd:["Image"]` |
| `m_text_` | `manage_gameobject` create + `componentsToAdd:["Text"]`（再 set_property 设 `text`/`fontSize`） |
| `m_tmp_` | `manage_gameobject` create + `componentsToAdd:["TMPro.TextMeshProUGUI"]` |
| `m_slider_` / `m_toggle_` / `m_input_` | `manage_gameobject` create + `componentsToAdd:[对应类型]`（Slider/Toggle/TMP_InputField） |
| `m_go_/m_tf_/m_rect_` | `manage_gameobject` action=`create`（纯空节点） |
| `m_rimg_/m_scroll_/m_scrollBar_` | `manage_gameobject` + `componentsToAdd:[RawImage/ScrollRect/ScrollBar]` |
| `m_grid_/m_hlay_/m_vlay_/m_canvasGroup_` | `manage_gameobject` + `componentsToAdd:[GridLayoutGroup/HorizontalLayoutGroup/VerticalLayoutGroup/CanvasGroup]` |
| `m_item_` | `manage_gameobject` 创建后挂对应 Widget 脚本（脚本用 `manage_script` 生成） |

### Prefab 结构要求

UIWindow 加载时强制检查根节点必须有 Canvas：

```
XxxUI.prefab（根节点）
├── [Canvas] ← 必须
├── [CanvasScaler] ← 强烈建议
├── [GraphicRaycaster] ← 交互必须
└── 子节点（m_btn_/m_tmp_/m_tf_/...）
```

存放：`Assets/AssetRaw/UI/<PrefabName>.prefab`（工程实际无 `UI/Prefabs/` 子目录，Prefab 直接置于 `AssetRaw/UI/` 下）

### Canvas 适配

| 参数 | 推荐值 |
|------|-------|
| UI Scale Mode | Scale With Screen Size |
| Reference Resolution | 1920 × 1080 |
| Screen Match Mode | Match Width Or Height |
| Match | 0.5 |

锚点规则：全屏→四角拉伸 | 弹窗→中心锚点+固定尺寸 | HUD→锚定对应边

### 标准工作流

```
1. batch_execute 创建根节点（componentsToAdd 挂 Canvas+CanvasScaler+GraphicRaycaster）+ 所有 UI 子节点
2. manage_prefabs action=create_from_gameobject → 保存为 Prefab
3. manage_gameobject action=delete → 清理场景临时 GO
```

### manage_prefabs 速查

| action | 说明 | 关键参数 |
|--------|------|---------|
| `create_from_gameobject` | 场景 GO 存为 Prefab | `target`(或 name), `prefabPath`(必需), `searchInactive`, `allowOverwrite`, `unlinkIfInstance` |
| `get_info` | Prefab 信息 | `prefabPath` |
| `get_hierarchy` | Prefab 层级 | `prefabPath` |
| `modify_contents` | headless 修改（见下） | `prefabPath`, `target`(默认根), `componentProperties`, 位置/名称等 |
| `open_prefab_stage` / `save_prefab_stage` / `close_prefab_stage` | Prefab Stage 交互编辑 | `prefabPath` 或 `path` |

证据：`ManagePrefabs.cs:23-30`（SupportedActions）、`ManagePrefabs.cs:186-225`（create 参数）、`ManagePrefabs.cs:599-633`（modify_contents）。

### 骨架模板

#### 全屏窗口
```json
{ "tool": "batch_execute", "commands": [
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "FSUI", "componentsToAdd": ["Canvas", "CanvasScaler", "GraphicRaycaster"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_tf_Bg", "parent": "FSUI", "componentsToAdd": ["Image"] } },
  { "tool": "manage_components", "params": { "action": "set_property", "target": "m_tf_Bg", "componentType": "Image", "properties": { "color": { "r": 0, "g": 0, "b": 0, "a": 0.8 } } } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_tmp_Title", "parent": "FSUI", "componentsToAdd": ["TMPro.TextMeshProUGUI"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_btn_Close", "parent": "FSUI", "componentsToAdd": ["Button", "Image"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_tf_Content", "parent": "FSUI" } }
], "failFast": true }
```

#### 弹窗
```json
{ "tool": "batch_execute", "commands": [
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "PopUI", "componentsToAdd": ["Canvas", "CanvasScaler", "GraphicRaycaster"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_img_Mask", "parent": "PopUI", "componentsToAdd": ["Image"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_tf_Win", "parent": "PopUI", "componentsToAdd": ["Image"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_tmp_Title", "parent": "m_tf_Win", "componentsToAdd": ["TMPro.TextMeshProUGUI"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_btn_OK", "parent": "m_tf_Win", "componentsToAdd": ["Button", "Image"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_btn_Cancel", "parent": "m_tf_Win", "componentsToAdd": ["Button", "Image"] } }
], "failFast": true }
```

> 尺寸/锚点/文案等细节用 `manage_components` action=`set_property` 按属性补设（RectTransform.sizeDelta/anchoredPosition、Text.text 等）。

#### 列表（带 ScrollView）
```json
{ "tool": "batch_execute", "commands": [
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "ListUI", "componentsToAdd": ["Canvas", "CanvasScaler", "GraphicRaycaster"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_scroll_List", "parent": "ListUI", "componentsToAdd": ["ScrollRect", "Image"] } },
  { "tool": "manage_gameobject", "params": { "action": "create", "name": "m_tf_Content", "parent": "m_scroll_List", "componentsToAdd": ["VerticalLayoutGroup"] } }
], "failFast": true }
```

### Prefab headless 编辑

不打开 Prefab Stage，直接修改 .prefab 文件：
```json
{ "tool": "manage_prefabs", "params": {
  "action": "modify_contents",
  "prefabPath": "Assets/AssetRaw/UI/XxxUI.prefab",
  "target": "m_btn_Back",
  "componentProperties": { "RectTransform": { "localPosition": { "x": -400, "y": 250, "z": 0 } } }
} }
```

查看层级：`manage_prefabs` action=`get_hierarchy`

---

## 脚本与资源管理

### manage_script：C# 脚本管理

| action | 说明 | 关键参数 |
|--------|------|---------|
| `create` | 创建脚本 | `name`, `path`, `contents`, `namespace`；大文件可用 `contentsEncoded`+`encodedContents`(base64) |
| `delete` | 删除脚本 | `name`, `path` |
| `get_sha` | 获取 SHA256 | `name`, `path`（编辑前必须先获取） |
| `validate` | 验证语法 | `name`, `path`, `level`（basic/standard/comprehensive/strict） |
| `apply_text_edits` | 精确文本编辑（见下） | `name`, `path`, `edits`, `precondition_sha256` |

（`read`/`update`/`edit` 为已废弃兼容 action，优先用资源读取与 `apply_text_edits`。）

```json
{ "tool": "manage_script", "params": {
  "action": "create", "name": "BattleMainUI",
  "path": "Assets/GameScripts/HotFix/GameLogic/UI/BattleMainUI",
  "contents": "using TEngine;\nnamespace GameLogic\n{\n    [Window(UILayer.UI, \"BattleMainUI\")]\n    public class BattleMainUI : UIWindow { }\n}",
  "namespace": "GameLogic"
} }
```

UIWindow/UIWidget 骨架模板见 [ui-patterns.md](ui-patterns.md)。

### apply_text_edits：精确文本编辑（manage_script 的 action，非独立工具）

**推荐流程：读取 → get_sha → 精确编辑**，避免全量覆写风险。

```json
{ "tool": "manage_script", "params": {
  "action": "apply_text_edits",
  "name": "BattleMainUI",
  "path": "Assets/GameScripts/HotFix/GameLogic/UI/BattleMainUI",
  "precondition_sha256": "<上一步的sha256>",
  "edits": [
    { "startLine": 15, "startCol": 1, "endLine": 18, "endCol": 1,
      "newText": "        protected override void OnRefresh()\n        {\n            RefreshHp(PlayerData.Hp);\n        }\n" }
  ],
  "options": { "refresh": "debounced", "validate": "standard" }
} }
```

规则：`precondition_sha256` 必须匹配当前文件 | 行列从 1 开始 | 多编辑区域不能重叠 | `refresh: "debounced"`（默认）延迟合并编译，`"immediate"`/`"sync"` 立即导入+编译 | 事务性应用，结果会做括号平衡与语法检查 | 禁止编辑首个 `using` 之前的头部区域 | 单次编辑负载上限 64KB。

| 错误码 | 处理 |
|--------|------|
| `precondition_required` | 先调用 `get_sha` |
| `stale_file` | 重新获取 SHA |
| `overlap` | 按行号降序排列编辑项 |
| `using_guard` | 不要编辑 using 头部之前的内容 |
| `too_large` | 拆分为更小的编辑 |
| `unbalanced_braces` / `syntax_error` | 缩小编辑范围重发，保持结构平衡 |

证据：`ManageScript.cs:214-314`（action 路由）、`ManageScript.cs:552-556`（SHA 校验）、`ManageScript.cs:565-569`（1-based 行列）、`ManageScript.cs:589-608`（using_guard）、`ManageScript.cs:675-678`（64KB 上限）、`ManageScript.cs:680-689`（overlap）、`ManageScript.cs:716-723`（括号平衡）、`ManageScript.cs:783-799`（refresh 模式）。

### manage_asset：资源文件管理

| action | 说明 | 关键参数 |
|--------|------|---------|
| `search` | 搜索资源 | `query`, `type`（Prefab/Texture2D/AudioClip/...）, `path` |
| `get_info` | 获取资源信息 | `path`（返回类型、GUID、大小、依赖项） |
| `get_components` | 列出 Prefab 上组件 | `path` |
| `create` | 创建文件夹等基础资产 | `path`, `type` |
| `import` | 触发资源导入 | `path` |
| `move` | 移动资源 | `path`, `newPath` |
| `rename` | 重命名 | `path`, `newName` |
| `duplicate` | 复制资源 | `path`, `newPath` |
| `modify` | 修改资产属性 | `path`, `properties` |
| `delete` | 删除资源 | `path` |
| `create_folder` | 创建文件夹 | `path` |

注意：`create` 不用于从零生成 Prefab——先 `manage_gameobject` 搭好再 `manage_prefabs create_from_gameobject`。

刷新：`refresh_unity`（manage_script 操作后自动调度刷新，无需手动）

证据：`ManageAsset.cs`（action 路由表）、`ManageAsset.cs:238`（create Prefab 限制提示）。

### manage_scriptable_object：SO 创建与补丁修改

| action | 说明 | 关键参数 |
|--------|------|---------|
| `create` | 创建 SO 实例 | `typeName`, `folderPath`, `assetName`, `overwrite` |
| `modify` | SerializedProperty 补丁式修改 | `target`(路径或 GUID), `patches[]`, `dryRun`(先校验不落盘) |

没有 `read`/`write` action（旧版 API 已移除）。补丁按 Unity 序列化属性路径应用。

证据：`ManageScriptableObject.cs:30-38`（ValidActions 仅 create/modify）、`ManageScriptableObject.cs:77-97`（create 参数）、HandleModify（target/patches/dryRun）。

### TEngine 脚本路径约定

| 类型 | 路径 |
|------|------|
| UIWindow/Widget | `Assets/GameScripts/HotFix/GameLogic/UI/<模块名>/` |
| 生成代码（绑定） | UGUI 绑定代码由 ScriptGenerator 内嵌写入窗口类（`#region 脚本工具生成的代码`）；FGUI 生成代码在 `Assets/GameScripts/HotFix/GameLogic/UI/FGUI/Gen/` |
| 模块代码 | `Assets/GameScripts/HotFix/GameLogic/Module/<ModuleName>/` |
| 事件接口 | `Assets/GameScripts/HotFix/GameLogic/IEvent/` |
| GameProto（Luban） | `Assets/GameScripts/HotFix/GameProto/`（自动生成，勿手改） |

---

## 编辑器控制与调试

### manage_editor

| action | 说明 |
|--------|------|
| `play` | 进入 Play Mode（无 `waitForCompletion` 参数；编译未完成时进入可能失败，建议先等编译） |
| `pause` | 暂停/恢复（仅 Play Mode 中有效） |
| `stop` | 退出 Play Mode |
| `undo` / `redo` | 撤销/重做（Play Mode 中有警告） |
| `set_active_tool` | 设置工具（View/Move/Rotate/Scale/Rect/Transform），参数 `toolName` |
| `add_tag` / `remove_tag` | Tag 管理，参数 `tagName` |
| `add_layer` / `remove_layer` | Layer 管理，参数 `layerName`（add 自动选 8~31 空闲槽） |
| `deploy_package` / `restore_package` | 部署/恢复 MCP 服务器包 |

**没有 `list_tags`/`list_layers`**；Tag/Layer/编辑器状态等只读信息走 MCP resources。

证据：`ManageEditor.cs:53-175`（action 路由）、`ManageEditor.cs:53-66`（play 无等待）、`ManageEditor.cs:300-310`（空闲槽）、`ManageEditor.cs:174`（读状态用 resources 提示）。

### execute_menu_item：执行菜单命令

参数名：`menu_path`（或 `menuPath`）。黑名单拦截 `File/Quit`。

TEngine 常用菜单：

| 操作 | menuItem 路径 |
|------|-------------|
| 刷新资源 | `Assets/Refresh`（内置） |
| 保存项目 | `File/Save Project`（内置） |
| 清理缓存 | `Tools/Clear Build Cache`（YooAsset） |
| 生成 UI 绑定脚本 | `GameObject/ScriptGenerator/UIProperty`（Hierarchy 选中 UI 节点；另有 UIPropertyAndListener / UIPropertyBindComponent 及 - UniTask 变体） |
| Luban 配置生成 | `TEngine/Luban/转表 &X` |
| HybridCLR 生成 | `HybridCLR/Generate/All` |
| 热更 DLL 编译 | `HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath` |

证据：`ExecuteMenuItem.cs:14-19`（黑名单）、`ExecuteMenuItem.cs:24`（menu_path）、`Assets/TEngine/Editor/LubanTools/LubanTools.cs:8`、`Assets/Editor/UIScriptGenerator/ScriptGenerator.cs:12-54`、HybridCLR 包 MenuItem。

### run_tests / get_test_job：自动化测试

```json
{ "tool": "run_tests", "params": { "mode": "EditMode", "assemblyNames": ["TEngine.GameEvent.Tests"] } }
```

- 过滤参数（数组）：`testNames` / `groupNames` / `categoryNames` / `assemblyNames`（**没有 `filter` 参数**）
- 其他：`includeDetails`, `includeFailedTests`, `initTimeout`
- 返回 `job_id`，后台运行。轮询：

```json
{ "tool": "get_test_job", "params": { "job_id": "<job_id>" } }
```

`status`：`running` → `succeeded` / `failed`；`get_test_job` 可带 `includeDetails`/`includeFailedTests`（`job_id` 亦接受 `jobId` 别名）。

清理卡住任务：`run_tests` params=`{ "clear_stuck": true }`。已有任务运行时报 `tests_running`（约 5s 后重试）。

证据：`RunTests.cs:22-30`（clear_stuck）、`RunTests.cs:32-49`（mode 默认 EditMode）、`RunTests.cs:79-95`（过滤器数组）、`GetTestJob.cs:16-24`。

### read_console：控制台日志

```json
{ "tool": "read_console", "params": { "count": 30, "types": ["error"] } }
```

| 参数 | 说明 |
|------|------|
| `action` | `get`(默认) / `clear` |
| `types` | 数组，默认 `["error","warning"]`；`["all"]` 展开为 error/warning/log；可含 `exception`/`assert` |
| `count` | 非分页模式返回条数 |
| `pageSize` / `cursor` | 分页（默认 50，上限 500；分页时忽略 count） |
| `filterText` | 大小写不敏感的子串过滤（**不是 `filter`，也没有 `logLevel`**） |
| `format` | `plain`(默认) / `detailed` / `json` |
| `includeStacktrace` | 附带堆栈 |

证据：`ReadConsole.cs:153-187`（参数解析）、`ReadConsole.cs:174-177`（all 展开）、`ReadConsole.cs:245`（分页上限）。

### refresh_unity

| 参数 | 说明 |
|------|------|
| `mode` | `if_dirty`(默认) / `force` |
| `scope` | `all`(默认) / `scripts` |
| `compile` | `none`(默认) / `request`（请求脚本编译） |
| `wait_for_ready` | 等待编译/刷新完成 |

`manage_script` 操作后自动调度导入与编译，通常无需手动。测试运行期间调用会报 `tests_running`。

证据：`RefreshUnity.cs:23-26`（参数）、`ManageScript.cs:393`（创建后自动导入）。

### 调试工作流

#### 场景运行调试

```
1. manage_scene action=save 保存场景
2. manage_editor action=play 进入运行
3. read_console types=["error"] count=20 检查错误
4. manage_editor action=stop 退出
5. manage_script action=apply_text_edits 修复 → 等编译 → 重新运行
```

#### 编译错误排查

```
1. 修改脚本后等待编译
2. read_console types=["error"] count=30
3. 根据错误行号 apply_text_edits 修复
4. read_console 确认无新错误
```

#### TEngine 热更重新生成

```json
{ "tool": "batch_execute", "commands": [
  { "tool": "execute_menu_item", "params": { "menu_path": "HybridCLR/Generate/All" } },
  { "tool": "refresh_unity", "params": { "compile": "request" } }
], "failFast": true }
```

---

## 其他常用工具（core 组）

| 工具 | 用途 |
|------|------|
| `manage_build` | 构建管理（Build Settings 场景增删、构建任务；长任务用轮询） |
| `manage_packages` | UPM 包管理（长任务，RequiresPolling） |
| `manage_camera` | 相机创建/配置 |
| `manage_physics` | 刚体/碰撞矩阵/物理模拟查询 |
| `manage_graphics` | 渲染管线/Skybox/灯光烘焙 |
| `manage_profiler` | Profiler 计数器/内存快照（`profiling` 组） |
| `execute_code` | 执行任意 C# 代码片段（`scripting_ext` 组，慎用） |
| `unity_reflect` | 工程文档/反射查询（`docs` 组） |

材质/纹理/Shader/VFX/动画等视觉工具（manage_material/manage_texture/manage_shader/manage_vfx/manage_animation）见 [mcp-visual.md](mcp-visual.md)。

---

## 常见错误

| 错误 | 原因 | 解决 |
|------|------|------|
| `Target ... not found` | 名称不存在 | 先 `find_gameobjects` 确认（注意其只返回 instanceID） |
| `Current scene has unsaved changes` | 未保存即切换场景 | 先 `manage_scene` save |
| `Target 'xxx' is a prefab asset. Use manage_asset... or manage_prefabs...` | 对 .prefab 用错工具 | 改用 `manage_prefabs`（modify_contents）或 `manage_asset` |
| `precondition_required` | 缺少 SHA | 先 `get_sha` |
| `stale_file` | 文件已被修改 | 重新 `get_sha` |
| `using_guard` | 编辑到了 using 头部之前 | 改从首个 using 之后的位置编辑 |
| `batch too large` | 单批超限（>25 或配置值） | 拆分多个 batch_execute |
| `tests_running` | 测试运行中 | 等待约 5s 后重试 |
| 工具不存在/不可见 | 工具组未启用 | MCP Tools 窗口启用或 `manage_tools` 激活 |
