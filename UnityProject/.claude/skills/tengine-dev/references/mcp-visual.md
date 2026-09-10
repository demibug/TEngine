# MCP 材质与视觉操作

> **环境状态**：Unity 侧插件已就绪（`Packages/MCPForUnity` 内嵌包随工程自动解析，见 packages-lock.json），但工程未配置 MCP 客户端入口（无 .mcp.json / 无 opencode mcp 配置），Python 服务器未接入，工具当前不可达。本文档为配置 MCP 后的参考；工具可用性以实际配置为准。

> **适用场景**：通过 MCP 工具操作材质/Shader/纹理/粒子特效/动画控制器 | **关联文档**：[mcp-tools.md](mcp-tools.md)（通用 MCP）、[naming-rules.md](naming-rules.md)（资源命名）、[resource-api.md](resource-api.md)（资源加载）
>
> **实际服务器**：MCP For Unity（CoplayDev，`com.coplaydev.unity-mcp` v9.7.3）。Unity 侧为内嵌包：`Packages/MCPForUnity` 源码随工程自动解析（packages-lock.json 记录 `source: "embedded"`，与 UniTask/YooAsset 同理，无需 manifest.json 引用）；连通 AI 客户端还需配置 MCP 服务器入口（Python 服务器经 uvx 安装 `unity-mcp-server`，如写入 `.mcp.json`）。本篇工具中仅 `manage_material` 属 `core` 组默认可见；`manage_shader`/`manage_texture`/`manage_vfx` 属 `vfx` 组、`manage_animation` 属 `animation` 组，**调用前需先** `manage_tools(action="activate", group="vfx"/"animation")` 启用本会话可见性。`manage_vfx`/`manage_animation` 的 action 专属参数统一放进 `properties`（dict 或 JSON 字符串，键与 Unity 端 C# 一致，snake_case 会自动转 camelCase）。

---

## 一、核心 API

### manage_material：材质管理

| action | 说明 | 关键参数 |
|--------|------|---------|
| `create` | 创建材质 | `material_path`（必需，.mat 资产路径）, `shader`（默认 `Standard`）, `color`, `property`, `properties`（`{属性名: 值}` 初始化字典） |
| `set_material_color` | 设置颜色 | `material_path`, `color`, `property`（可省略，自动回退 `_BaseColor`→`_Color`） |
| `set_material_shader_property` | 设置 Shader 属性 | `material_path`, `property`, `value`（类型自动推断：数组/浮点/字符串纹理路径均可，**无 propertyType 参数**） |
| `assign_material_to_renderer` | 赋给渲染器 | `target`, `material_path`, `slot`（默认 0）, `search_method` |
| `set_renderer_color` | 快捷设置渲染器颜色 | `target`, `color`, `slot`, `mode`（`property_block`/`shared`/`instance`/`create_unique`） |
| `get_material_info` | 获取材质属性列表与当前值 | `material_path` |

`color` 格式：`[r,g,b,a]` 或 `[r,g,b]`、`{"r":..,"g":..,"b":..,"a":..}`、JSON 字符串或 hex 字符串（`#RGB`/`#RRGGBB`/`#RRGGBBAA`）；数值支持 0-1 或 0-255，服务器自动归一化到 0-1。

常用 Shader 名称（`shader` 参数）：

| 管线 | Shader 名称 |
|------|------------|
| URP 不透明 | `Universal Render Pipeline/Lit` |
| URP 无光照 | `Universal Render Pipeline/Unlit` |
| 精灵/2D | `Sprites/Default` |
| UI | `UI/Default` |

别名机制：`lit`/`default`/`default_lit`/`standard` → 当前管线默认 Lit；`unlit` → 当前管线 Unlit；`urp_lit`/`hdrp_lit`/`built_in_lit` → 指定管线。名称找不到时回退当前管线默认 Lit 并告警。

颜色属性别名自动解析：`_Color`↔`_BaseColor`、`_MainTex`↔`_BaseMap`、`smoothness`→`_Smoothness` 等（按材质实际存在的属性回退）。

---

### manage_shader：Shader 文件

| action | 说明 | 关键参数 |
|--------|------|---------|
| `create` | 创建 Shader（无 `contents` 时生成默认模板） | `name`, `path`, `contents` |
| `read` | 读取 Shader 源码 | `name`, `path` |
| `update` | 覆盖 Shader 源码 | `name`, `path`, `contents` |
| `delete` | 删除 Shader | `name`, `path` |

`name` 仅允许字母/数字/下划线且不能以数字开头（作为文件名与 Shader 名）；`path` 为 `Assets/` 下目录（默认 `Shaders`）。

---

### manage_texture：程序化纹理生成与导入设置

| action | 说明 | 关键参数 |
|--------|------|---------|
| `create` / `create_sprite` | 生成纹理（纯色/图案/像素数据/加载本地图片；`create` 在未指定任何填充内容时默认白色） | `path`, `width`, `height`（默认 64x64）, `fill_color`, `pattern`, `palette`, `pattern_size`, `pixels`, `image_path`, `import_settings`；`image_path` 仅限本 action，且不能与 `fill_color`/`pattern`/`pixels` 同用 |
| `modify` | 改像素区或导入设置 | `path`, `set_pixels`（`{x,y,width,height,color/pixels}`）, `import_settings` |
| `delete` | 删除纹理 | `path` |
| `apply_pattern` | 图案：`checkerboard`/`stripes`/`stripes_h`/`stripes_v`/`stripes_diag`/`dots`/`grid`/`brick` | 同 create 的 `pattern`/`palette`/`pattern_size` |
| `apply_gradient` | 渐变纹理 | `gradient_type`（linear/radial）, `gradient_angle`, `palette` |
| `apply_noise` | Perlin 噪声纹理 | `noise_scale`, `octaves`, `palette` |
| `set_import_settings` | 修改导入设置 | `path`, `import_settings`（dict）或 `as_sprite`（dict/bool），二者互斥不可同传 |

`import_settings` 键（snake_case，服务器映射为 Unity 端 camelCase 键，如 `srgb`→`sRGBTexture`、`generate_mipmaps`→`mipmapEnabled`、`compression`→`textureCompression`、`sprite_mode`→`spriteImportMode`）：`texture_type`、`texture_shape`（2d/cube）、`srgb`、`alpha_is_transparency`、`readable`、`generate_mipmaps`、`alpha_source`（none/from_input/from_gray_scale）、`wrap_mode`/`wrap_mode_u`/`wrap_mode_v`（repeat/clamp/mirror/mirror_once）、`filter_mode`（point/bilinear/trilinear）、`mipmap_filter`（box/kaiser）、`compression_crunched`（bool）、`aniso_level`（0-16）、`max_texture_size`（仅 32~16384 的 2 的幂）、`compression`（none/low_quality/normal_quality/high_quality）、`compression_quality`（0-100）、`sprite_mode`（single/multiple/polygon）、`sprite_pixels_per_unit`、`sprite_pivot`、`sprite_mesh_type`（full_rect/tight）、`sprite_extrude`（0-32）。

`texture_type`：`default`、`normal_map`、`editor_gui`、`sprite`、`cursor`、`cookie`、`lightmap`、`directional_lightmap`、`shadow_mask`、`single_channel`。颜色值支持 0-255 整数或 0-1 浮点。

---

### manage_vfx：粒子与特效

action 前缀四类：`particle_*`（ParticleSystem）、`vfx_*`（VFX Graph）、`line_*`（LineRenderer）、`trail_*`（TrailRenderer）。公共顶层参数：`target`、`search_method`、`component_index`；其余参数放 `properties`。

#### ParticleSystem（particle_*）

| action | 说明 | properties 关键键 |
|--------|------|------------------|
| `particle_create` | 在 target 上创建/准备粒子系统（GameObject 不存在时自动创建），自动分配管线默认材质 | `position`, `rotation`, `scale`, `playOnAwake`, `looping` |
| `particle_get_info` | 获取粒子系统信息与当前状态 | — |
| `particle_set_main` | 主模块 | `duration`, `looping`, `startLifetime`, `startSpeed`, `startSize`, `startColor`, `gravityModifier`, `maxParticles`, `simulationSpace`, `playOnAwake` 等 |
| `particle_set_emission` | 发射模块 | `enabled`, `rateOverTime`, `rateOverDistance` |
| `particle_add_burst` | 爆发发射 | `time`, `count`（或 `minCount`/`maxCount`）, `cycles`, `interval`, `probability` |
| `particle_set_shape` | 形状模块 | `shapeType`（Sphere/Cone/Box/Mesh 等）, `radius`, `angle`, `arc`, `position`, `rotation`, `scale` |
| `particle_set_color_over_lifetime` / `particle_set_size_over_lifetime` / `particle_set_velocity_over_lifetime` / `particle_set_noise` / `particle_set_renderer` | 生命周期/速度/噪声/渲染器模块 | 对应模块键 |
| `particle_enable_module` | 开关模块 | `module`, `enabled` |
| `particle_play` / `particle_stop` / `particle_pause` / `particle_restart` / `particle_clear` | 播放控制 | `withChildren` |
| `particle_clear_bursts` | 清空爆发 | — |

颜色/曲线支持常量或结构化对象：如 `startColor` 用 `[r,g,b,a]`（0-1）或 `{mode:"two_colors",colorMin:..,colorMax:..}`；数值模块可用 `{mode:"random_between_constants",min:..,max:..}`。

#### LineRenderer（line_*）

| action | 说明 | properties 关键键 |
|--------|------|------------------|
| `line_create_line` | 创建线段 | `start`, `end`, `width`/`startWidth`/`endWidth`, `color`/`startColor`/`endColor` |
| `line_create_circle` | 创建圆 | `center`, `radius`, `segments`, `normal` |
| `line_create_arc` | 创建圆弧 | `center`, `radius`, `startAngle`, `endAngle`, `segments`, `normal` |
| `line_create_bezier` | 创建贝塞尔曲线 | `start`, `end`, `controlPoint1`, `controlPoint2`, `segments` |
| `line_set_positions` / `line_add_position` / `line_set_position` | 设置位置 | `positions`（`[[x,y,z],...]`）/ `position` / `index`+`position` |
| `line_set_width` / `line_set_color` / `line_set_material` / `line_set_properties` / `line_clear` / `line_get_info` | 渲染属性与控制 | 对应键 |

#### TrailRenderer（trail_*）

`trail_get_info`、`trail_set_time`、`trail_set_width`、`trail_set_color`、`trail_set_material`、`trail_set_properties`（`minVertexDistance`、`autodestruct`、`emitting` 等）、`trail_clear`、`trail_emit`。

#### VFX Graph（vfx_*，需安装 com.unity.visualeffectgraph 包）

资产管理：`vfx_create_asset`、`vfx_assign_asset`、`vfx_list_templates`、`vfx_list_assets`；运行时控制：`vfx_get_info`、`vfx_set_float/int/bool`、`vfx_set_vector2/3/4`、`vfx_set_color`、`vfx_set_gradient`、`vfx_set_texture`、`vfx_set_mesh`、`vfx_set_curve`、`vfx_send_event`、`vfx_play/stop/pause/reinit`、`vfx_set_playback_speed`、`vfx_set_seed`。

---

### manage_animation：动画控制器与 Animator

action 前缀三类：`controller_*`（控制器资产）、`clip_*`（AnimationClip 资产）、`animator_*`（场景 Animator 运行控制）。公共顶层参数：`action`, `target`, `search_method`, `clip_path`, `controller_path`；其余参数放 `properties`。

| action | 说明 | properties 关键键 |
|--------|------|------------------|
| `controller_create` | 创建 AnimatorController | `controllerPath`（顶层 `controller_path`） |
| `controller_add_parameter` | 添加参数 | `parameterName`, `parameterType`（float/int/bool/trigger，亦接受 integer/boolean）, `defaultValue` |
| `controller_add_state` | 添加状态 | `stateName`, `clipPath`, `layerIndex`, `speed`, `isDefault` |
| `controller_add_transition` | 添加过渡 | `fromState`（可为 `AnyState`）, `toState`, `layerIndex`, `hasExitTime`（默认 true）, `duration`, `exitTime`, `conditions` |
| `controller_create_blend_tree_1d` | 创建 1D 混合树状态 | `stateName`, `blendParameter`, `layerIndex` |
| `controller_create_blend_tree_2d` | 创建 2D 混合树状态 | `stateName`, `blendParameterX`, `blendParameterY`, `blendType`（simpledirectional2d/freeformdirectional2d/freeformcartesian2d） |
| `controller_add_blend_tree_child` | 添加混合树子运动 | `stateName`, `clipPath`, `threshold`（1D）或 `position:[x,y]`（2D） |
| `controller_assign` | 把控制器赋给 target 的 Animator | 顶层 `target` |
| `controller_add_layer` / `controller_remove_layer` / `controller_set_layer_weight` / `controller_get_info` | 层管理/查询 | 对应键 |
| `clip_create` | 创建动画片段 | `clipPath`（顶层）, `name`, `length`, `frameRate`, `loop` |
| `clip_add_curve` / `clip_set_curve` / `clip_set_vector_curve` / `clip_add_event` / `clip_remove_event` / `clip_create_preset` / `clip_assign` / `clip_get_info` | 曲线/事件/预设 | 对应键 |
| `animator_play` / `animator_crossfade` / `animator_set_parameter` / `animator_set_speed` / `animator_set_enabled` / `animator_get_info` / `animator_get_parameter` | 运行时控制 | `stateName`, `parameterName`, `parameterType`, `value`, `duration`, `layer` 等 |

`conditions` 元素：`{ "parameter": "Speed", "mode": "greater/less/equals/notequal/if/ifnot"（另接受 not_equal/if_not/true/false 别名）, "threshold": 0.1 }`。

---

## 二、使用模式

### 材质创建完整流程

```json
// 步骤 1：创建材质（本项目为内置管线，用 Standard；可同时设置初始颜色/属性）
{ "tool": "manage_material", "params": {
  "action": "create",
  "material_path": "Assets/AssetRaw/Materials/EnemyMat.mat",
  "shader": "Standard",
  "color": [0.8, 0.2, 0.2, 1.0]
} }

// 步骤 2：设置自发光（propertyType 不存在，value 类型自动推断）
{ "tool": "manage_material", "params": {
  "action": "set_material_shader_property",
  "material_path": "Assets/AssetRaw/Materials/EnemyMat.mat",
  "property": "_EmissionColor",
  "value": [0.5, 0.0, 0.0, 1.0]
} }

// 步骤 3：赋给场景对象的渲染器
{ "tool": "manage_material", "params": {
  "action": "assign_material_to_renderer",
  "target": "EnemyModel",
  "material_path": "Assets/AssetRaw/Materials/EnemyMat.mat",
  "slot": 0
} }
```

---

### 粒子特效完整流程（击中特效）

```json
// 步骤 1：在 target 上创建粒子系统（GameObject 不存在会自动创建）
{ "tool": "manage_vfx", "params": {
  "action": "particle_create",
  "target": "HitEffect"
} }

// 步骤 2：设置主模块（短暂爆发效果，参数放 properties）
{ "tool": "manage_vfx", "params": {
  "action": "particle_set_main",
  "target": "HitEffect",
  "properties": {
    "duration": 0.5, "looping": false,
    "startLifetime": 0.3, "startSpeed": 3.0, "startSize": 0.2,
    "startColor": [1.0, 0.6, 0.1, 1.0],
    "maxParticles": 30, "simulationSpace": "World"
  }
} }

// 步骤 3：关闭持续发射 + 步骤 4：添加一次爆发
{ "tool": "manage_vfx", "params": {
  "action": "particle_set_emission",
  "target": "HitEffect",
  "properties": { "rateOverTime": 0 }
} }
{ "tool": "manage_vfx", "params": {
  "action": "particle_add_burst",
  "target": "HitEffect",
  "properties": { "time": 0, "count": 20, "cycles": 1 }
} }

// 步骤 5：设置球形发射形状
{ "tool": "manage_vfx", "params": {
  "action": "particle_set_shape",
  "target": "HitEffect",
  "properties": { "shapeType": "Sphere", "radius": 0.1 }
} }
```

---

### 动画控制器完整流程（角色移动状态机）

```json
// 步骤 1：创建控制器
{ "tool": "manage_animation", "params": {
  "action": "controller_create",
  "controller_path": "Assets/AssetRaw/Animations/Hero.controller"
} }

// 步骤 2：添加 Speed 参数
{ "tool": "manage_animation", "params": {
  "action": "controller_add_parameter",
  "controller_path": "Assets/AssetRaw/Animations/Hero.controller",
  "properties": { "parameterName": "Speed", "parameterType": "float", "defaultValue": 0.0 }
} }

// 步骤 3：添加 Idle 状态（默认）
{ "tool": "manage_animation", "params": {
  "action": "controller_add_state",
  "controller_path": "Assets/AssetRaw/Animations/Hero.controller",
  "clip_path": "Assets/AssetRaw/Animations/HeroIdle.anim",
  "properties": { "stateName": "Idle", "isDefault": true }
} }

// 步骤 4：添加 Run 状态
{ "tool": "manage_animation", "params": {
  "action": "controller_add_state",
  "controller_path": "Assets/AssetRaw/Animations/Hero.controller",
  "clip_path": "Assets/AssetRaw/Animations/HeroRun.anim",
  "properties": { "stateName": "Run" }
} }

// 步骤 5：Idle→Run（Speed > 0.1）
{ "tool": "manage_animation", "params": {
  "action": "controller_add_transition",
  "controller_path": "Assets/AssetRaw/Animations/Hero.controller",
  "properties": {
    "fromState": "Idle", "toState": "Run", "hasExitTime": false,
    "conditions": [{ "parameter": "Speed", "mode": "greater", "threshold": 0.1 }]
  }
} }

// 步骤 6：Run→Idle（Speed < 0.1）
{ "tool": "manage_animation", "params": {
  "action": "controller_add_transition",
  "controller_path": "Assets/AssetRaw/Animations/Hero.controller",
  "properties": {
    "fromState": "Run", "toState": "Idle", "hasExitTime": false,
    "conditions": [{ "parameter": "Speed", "mode": "less", "threshold": 0.1 }]
  }
} }
```

---

### 纹理导入设置（UI 图标）

```json
{ "tool": "manage_texture", "params": {
  "action": "set_import_settings",
  "path": "Assets/AssetRaw/UI/Icons/item_sword.png",
  "import_settings": {
    "texture_type": "sprite",
    "max_texture_size": 512,
    "compression": "none",
    "generate_mipmaps": false
  }
} }
```

---

### batch_execute 批量操作（推荐）

多个视觉操作应合并为一个 `batch_execute` 调用（默认上限 25 条，硬上限 100；`parallel` 参数实际仍按顺序执行）：

```json
{ "tool": "batch_execute", "commands": [
  { "tool": "manage_material", "params": {
    "action": "create",
    "material_path": "Assets/AssetRaw/Materials/HeroMat.mat",
    "shader": "Standard",
    "color": [0.2, 0.5, 0.9, 1.0]
  } },
  { "tool": "manage_material", "params": {
    "action": "assign_material_to_renderer",
    "target": "HeroModel",
    "material_path": "Assets/AssetRaw/Materials/HeroMat.mat",
    "slot": 0
  } }
], "fail_fast": true }
```

---

## 三、常见错误

| 错误写法 | 正确写法 | 原因 |
|---------|---------|------|
| `materialName`+`savePath`+`shaderName` 创建材质 | `material_path` + `shader` | 当前版本 create 的参数是 `material_path`/`shader`，无 materialName/savePath |
| `colorProperty` + 独立 `r/g/b/a` 字段 | `property` + `color: [r,g,b,a]` | 颜色是单一 `color` 参数（数组/对象/JSON 字符串），属性名参数叫 `property` |
| `propertyName`+`propertyType` | `property` + `value` | set_material_shader_property 无 propertyType，值类型自动推断 |
| `materialIndex` | `slot` | 渲染器槽位参数名是 `slot` |
| `colorProperty: "_Color"` 用于 URP 材质 | `colorProperty` 省略或 `property: "_BaseColor"` | `_Color` 是标准管线属性；URP Lit 使用 `_BaseColor`（省略 property 时会自动回退 _BaseColor→_Color） |
| 直接调用 `manage_vfx`/`manage_animation` 返回工具不存在 | 先 `manage_tools(action="activate", group="vfx"/"animation")` | vfx/animation 组工具默认隐藏，仅 core 组默认可见 |
| `manage_vfx` 参数平铺（如 `"duration": 0.5`） | 放进 `properties`：`"properties": { "duration": 0.5 }` | manage_vfx 顶层只接受 action/target/search_method/component_index |
| `line_create` + `positions` | `line_create_line` + `properties: { "start": [..], "end": [..] }` | 线段创建 action 是 `line_create_line/circle/arc/bezier`，无 `line_create` |
| `create_controller` / `add_state` / `create_clip` | `controller_create` / `controller_add_state` / `clip_create` | manage_animation 的 action 必须带 `controller_`/`clip_`/`animator_` 前缀 |
| `create_blend_tree` 一次建树 | `controller_create_blend_tree_1d`（或 `_2d`）+ `controller_add_blend_tree_child` | 混合树分两个 action，子节点逐个添加（1D 用 `threshold`，2D 用 `position`） |
| `clip_create` 传 `isLooping` | `loop` | 循环参数名是 `loop` |
| `set_import_settings` 平铺 `maxSize`/`format`/`textureType` | 嵌套 `import_settings: { "max_texture_size": 512, "texture_type": "sprite" }` | 导入设置是嵌套 dict，snake_case 键，且无 `format`（用 `compression`） |
| `parameterType: "Float"` | `"float"` | parameterType 值小写（float/int/bool/trigger） |
| 状态响应过渡 `hasExitTime: true` | `hasExitTime: false` | 默认 true 需等动画播完才过渡，移动/战斗状态需显式传 false 即时响应 |

---

## 四、交叉引用

| 主题 | 文档 |
|------|------|
| 通用 MCP 操作（batch_execute/场景/脚本） | [mcp-tools.md](mcp-tools.md) |
| 资源文件命名与路径约定 | [naming-rules.md](naming-rules.md) |
| 资源加载/卸载 API（运行时） | [resource-api.md](resource-api.md) |
| UI Prefab 拼接与组件操作 | [mcp-tools.md](mcp-tools.md#ui-prefab-拼接) |
