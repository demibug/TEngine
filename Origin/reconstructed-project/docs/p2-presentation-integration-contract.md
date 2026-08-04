# P2 表现层接入契约

> 本文档为 OpenSpec Change `gap-sweep-and-presentation` 任务 6.4-6.5 产出。它定义规则层与表现层之间的 port 边界与接入约定，作为后续独立 P2 proposal 的输入。本文档**不实现**任何 VFX/音频/场景/Prefab/Spine/微信实体，仅记录契约与边界。相关缺口清单见 `docs/p2-presentation-gap-catalog.md`。

## 1. 背景与目的

反向还原工程 v0.8.1 的核心战斗纯逻辑层（提案 ①-⑦）已落地，规则层经 port 注入表现层的 seam 体系已完备。但 P2 表现层（Laya/Spine/音频/场景/微信）尚未起步，缺统一接入契约供后续 proposal 消费。本文档基于现有 port 体系与架构文档，固化以下约定：

1. 规则层独立约束——规则层不直接依赖 Laya/Spine/微信/场景节点。
2. 规则层与表现层的 port 边界——现有 8 引擎中立端口 + 具体适配器。
3. DEFERRED 桩 no-op 约定——不抛异常、不阻塞状态机。
4. 4 层架构——Domain/Rule → Config → Adapter/Port → Presentation。
5. 失踪参考文档记录——`unity-handoff/reference/00_IMPLEMENTATION_STATUS.md`/`16_KNOWN_GAPS.md` 磁盘缺失。

## 2. 规则层独立约束

### 2.1 核心约束（CODEX_HANDOFF_v0.8.1.md:339）

> 「核心逻辑迁移时不要让规则层直接依赖 Laya、Spine、微信或场景节点。」——`CODEX_HANDOFF_v0.8.1.md:339`

规则层（Domain/Rule Layer）MUST 保持引擎中立，SHALL NOT 直接 import 或引用以下任何表现/平台设施：

- **Laya**（`Laya.Sprite`/`Laya.Image`/`Laya.Tween`/`Laya.Pool`/`Laya.Handler`/`Laya.timer` 等）
- **Spine**（`createLayaSpineAnimation`/Spine 运行时骨骼）
- **微信**（`wx.login`/`wx.share`/`wx.createBannerAd`/云存档/排行榜 SDK）
- **场景节点**（MonoBehaviour/Transform/GameObject/Prefab 实例/`.ls` 场景文件节点树）

规则层对表现层/平台的全部依赖 MUST 经 port（抽象接口）注入，规则层只面向 port 编程。

### 2.2 信任与实现规则（CODEX_HANDOFF_v0.8.1.md:437-443）

`CODEX_HANDOFF_v0.8.1.md:437-443` 的「信任与实现规则」一节确立 P2 接入必须遵守的约束，其中与表现层接入直接相关者：

- **`:440` 不让表现动画成为规则唯一触发来源**——伤害结算、状态变更、单位生成/回收等规则变化 MUST 由规则层（`src/`、配置或 decoded bundle）驱动，SHALL NOT 依赖表现动画事件（如 Spine 动画完成回调、Tween 结束回调）作为唯一触发器。表现动画可作为视觉伴随，但规则层 MUST 有独立的时间/事件驱动路径（80ms Tick、`BATTLE_FINISHED` 自动结算等）。
- **`:437` 保留对象池复用隔离**——表现层对象池（如 `Laya.Pool` bubble/loveHeart 池）与规则层池隔离，规则层不直接操作表现层池。
- **`:438` 保留 Manager 的启动和清理顺序**——表现层清理（`gameOver`/`clearOwner`）在规则层清理后执行，不反向。
- **`:439` 保留 `BATTLE_FINISHED` 作为唯一自动结算入口**——结算由规则层驱动，表现层不得绕过。
- **`:441` 不把所有 JS 类机械映射为 MonoBehaviour**——规则层实体保持普通类，不强制 MonoBehaviour 化。
- **`:442` 不要自行补全 `INFERRED`/`PARTIAL`/`DEFERRED_*` 内容**——DEFERRED 桩保持 no-op，不擅自补实现。
- **`:443` 规则变化必须能追溯到 `src/`、配置或 decoded bundle**——表现层不得引入规则层无法追溯的行为变化。

### 2.3 表现动画非规则唯一触发来源的含义

此约束（`:440`）是 P2 接入的核心红线。含义与实操：

- **规则层先行**：伤害/状态/生成/回收等规则变化由规则层在 80ms Tick 内或事件驱动下完成，结算结果落 `BattleState`/`EventBus`。表现层订阅事件或被 port 调用以播放伴随动画，但表现层动画的播放/完成与否不影响规则层结算。
- **DEFERRED 桩可运行**：当表现层 port 为 DEFERRED no-op 桩时，规则层照常结算（如 `CavalrySweepEffect` 的双横扫伤害结算不依赖 `createCavalrySweepVisual` 的视觉对象）。这是「表现动画非唯一触发来源」的直接体现——视觉桩缺失不阻塞规则。
- **P2 实现不得反转**：后续 P2 proposal 实现真实 VFX/音频时，SHALL NOT 将伤害触发改为「动画事件回调里结算」。结算仍由规则层驱动，表现层只播放。

## 3. 规则层与表现层的 Port 边界

### 3.1 8 引擎中立端口（src/ports/CombatPorts.js）

`src/ports/CombatPorts.js:3-10` 定义 8 个引擎中立抽象端口，是规则层对引擎/平台依赖的唯一边界。规则层面向这些抽象编程，具体实现（Laya 适配器/Unity C# 适配器/Development 桩）经注入提供。

| 端口 | 源定义 | 抽象方法 | Unity C# 等价接口 |
|------|--------|----------|-------------------|
| `CombatClockPort` | `CombatPorts.js:3` | `now()` | `ICombatClock.NowMilliseconds` |
| `RandomSourcePort` | `CombatPorts.js:4` | `next()` | `IRandomSource.Next01` |
| `CombatViewPort` | `CombatPorts.js:5` | `spawn()`/`remove()` | `ICombatView.Spawn/Remove` |
| `AudioPort` | `CombatPorts.js:6` | `play()`/`stop()` | `IAudioPort.Play/Stop` |
| `VfxPort` | `CombatPorts.js:7` | `create()`/`remove()` | `IVfxPort.Create/Remove` |
| `InputPort` | `CombatPorts.js:8` | `nextCommand()` | `IInputPort`（产出 battle commands） |
| `ScenePort` | `CombatPorts.js:9` | `open()`/`close()` | `IScenePort.Open/Close` |
| `ResourcePort` | `CombatPorts.js:10` | `load()` | `IResourcePort.Load` |

Unity 侧 C# 等价接口定义见 `unity-handoff/UNITY_PORT_INTERFACES.md:4-13`。Unity 输入（`IInputPort`）MUST 产出 `PurchaseAndPlace`/drag/move/merge/refresh 等 battle commands，Domain 代码 SHALL NOT 直接检视 `PointerEventData`（`UNITY_PORT_INTERFACES.md:14`）。

### 3.2 具体表现适配器（现有）

规则层与表现层之间除 8 引擎中立端口外，另有若干具体适配器承载细分表现契约。这些适配器面向 port/服务接口编程，规则层经 `SkillEffectPort.services()`（`SkillEffectPort.js:12`）等注入通道持有它们，不直接 new Laya 对象。

| 适配器 | 源文件 | 职责 | DEFERRED 桩要点 |
|--------|--------|------|-----------------|
| `LayaEnemyPresentation` | `src/presentation/LayaEnemyPresentation.js` | 敌人/Boss 视觉（Prefab/Spine/Tween/池）、CavalrySweep×2/PikeTip×3 VFX 桩 | `createCavalrySweepVisual`/`createPikeTipVisual` 返回 null，`animatePikeTipThrust`/`hidePikeTipVisual`/`removeCavalrySweepVisual` no-op（`:219-267`） |
| `SkillPresentationPort` | `src/skills/presentation/SkillPresentationPort.js` | 技能 Spine/Overlay/TileMarker/EntityVfx 表现 | `createEntityVfx()` 返回 null（`:18`）；`createOverlay`/`createTileMarker` 需具体实现否则抛异常（非 DEFERRED 桩） |
| `LayaSkillPresentation` | `src/skills/presentation/LayaSkillPresentation.js` | `SkillPresentationPort` 的 Laya 具体实现 | 继承父类；`createSpine` 无资源时经 `requireResource` 标 `TODO_RESOURCE_MISSING` |
| `SkillAudioRegistry` | `src/skills/presentation/SkillAudioRegistry.js` | 技能音效播放/停止/owner 清理 | 无 `audio` 注入时 `play` 返回 null、`stop` no-op（`:7-17`） |
| `Trail2DAdapter` | `src/rendering/Trail2DAdapter.js` | 投射物 Trail2D 拖尾 | 需 `prefabFactory` 否则 `create()` 抛异常（非 DEFERRED 桩） |
| `LayaPrefabFactory` | `src/presentation/LayaPrefabFactory.js` | Prefab 实例化（origin_project 资源） | — |
| `LayaSpineAnimation` | `src/presentation/LayaSpineAnimation.js` | Spine2D 骨骼动画封装 | — |
| `DevelopmentAudio` | `src/platform/dev/DevelopmentAudio.js` | dev 桩 AudioPort（music-only） | 仅 `playMusic`/`stopMusic`，无 `playSound`（bundle 音频未解码） |

### 3.3 注入边界约定

- **规则层持 port 引用，不持 Laya/Spine 引用**：`SkillEffectPort.configure`（`SkillEffectPort.js:6-11`）注入 `presentation`/`audioRegistry` 等，规则层只调 `this.presentation.createCavalrySweepVisual(...)` 等 port 方法，不访问 `Laya.Sprite`。
- **表现层适配器内部用 Laya/Spine，但不泄漏给规则层**：`LayaEnemyPresentation` 内部用 `this.Laya`/`this.prefabFactory`，但对外只暴露 port 方法签名，返回值为 null/节点句柄/`{node, remove}` 等中立结构。
- **services() 注入通道**：`SkillEffectPort.services()`（`SkillEffectPort.js:12`）聚合 `buffManager`/`enemyManager`/`unitRegistry`/`presentation`/`audioRegistry`/`projectileManager`/`attackEffectManager` 等，效果类经构造注入 `services()` 后访问这些服务（与 10 个已有 Boss 技能效果类一致）。
- **Unity 侧组合入口**：`unity-handoff/reference/01_ARCHITECTURE.md:100-108` 建议 Unity 由 `CombatCompositionRoot` 在 Battle Scene 加载时创建纯逻辑服务并注入 View/Audio/Input/Scene Adapter。P2 proposal 沿用此入口。

## 4. DEFERRED 桩 no-op 约定

### 4.1 约定定义

DEFERRED 桩是 P2 表现层实体未实现时的占位实现。约定如下（取证自 `SkillEffectPort.js:14`/`LayaEnemyPresentation.js:219-267`/`SkillPresentationPort.js:18`）：

1. **不抛异常**：DEFERRED 桩方法 SHALL NOT throw。规则层调用 port 方法时，即使表现实体未实现，也得到中立返回值，不中断调用栈。
2. **不阻塞状态机**：DEFERRED 桩 SHALL NOT 阻塞战斗状态机、伤害结算、单位生成/回收。规则层结算路径独立于表现桩。
3. **返回中立值**：
   - 创建类方法返回 `null`（如 `createCavalrySweepVisual`→`null`，`LayaEnemyPresentation.js:222`；`createPikeTipVisual`→`null`，`:246`；`createEntityVfx`→`null`，`SkillPresentationPort.js:18`）。
   - 操作类方法 no-op（如 `animatePikeTipThrust`/`hidePikeTipVisual`/`removeCavalrySweepVisual` 仅 push calls 不操作渲染对象，`LayaEnemyPresentation.js:230-267`）。
   - 抽象方法未实现时返回 `null`/`false`/空（如 `InputPort.nextCommand`→`null`，`CombatPorts.js:8`）。
4. **记录调用**：DEFERRED 桩通常 `this.calls.push([...])` 或 `this.deferredCalls.push(...)` 记录调用，供测试断言与后续 P2 实现校验调用契约（`LayaEnemyPresentation.js:8` `calls=[]`；`SkillEffectPort.js:5` `deferredCalls=[]`）。
5. **不擅自补实现**：`:442` 约定——DEFERRED 桩保持 no-op，SHALL NOT 自行补全为原版视觉/音频实现。补实现属后续 P2 proposal 范畴。

### 4.2 DEFERRED 桩的规则层兜底机制

`SkillEffectPort.execute`（`SkillEffectPort.js:14`）演示了 DEFERRED 兜底：当 handler 未注册时，push `deferredCalls` 记录 `{key, context, status:'DEFERRED_EFFECT_WITH_EXACT_CONTRACT'}`，经 `eventBus` 发 `skill:effect:deferred` 事件，返回 deferred 对象——不抛异常、不阻塞。这确保规则层在表现/效果桩缺失时仍可推进状态机。

### 4.3 P2 实现时的替换约定

后续 P2 proposal 实现 DEFERRED 桩对应的真实实体时：

- **保持签名**：MUST 保持 port 方法签名不变，规则层调用方无需改动。
- **保持中立返回结构**：若返回节点句柄，保持 `{node, remove}`/`{key, node, remove}` 等结构（如 `LayaSkillPresentation.createOverlay` 返回 `{key,node,remove,setAlpha}`，`LayaSkillPresentation.js:27`）。
- **不得引入规则依赖**：真实实现内部可用 Laya/Spine/资源，但 SHALL NOT 让规则层新增对表现层的依赖（仍经 port）。
- **不得反转触发方向**：真实 VFX/音频 SHALL NOT 成为规则结算的唯一触发来源（`:440`）。

## 5. 4 层架构

### 5.1 分层定义（unity-handoff/reference/01_ARCHITECTURE.md:36-98 / 14_UNITY_BLUEPRINT.md:5-61）

P2 接入遵循 4 层架构，规则层与表现层严格分层：

```
1. Domain / Rule Layer      ── 普通类，引擎中立，不引用 MonoBehaviour/Transform/Time.deltaTime/Spine/Unity UI
2. Configuration Layer      ── unity-export/config/*.json（稳定后可转 ScriptableObject）
3. Adapter / Port Layer     ── 8 端口的引擎具体实现（Laya 适配器 / Unity C# 适配器）
4. Presentation Layer       ── MonoBehaviour/节点：Prefab/Spine/投射物/Trail/地块高亮/HUD/GameOver
```

### 5.2 各层职责与约束

**Layer 1 — Domain / Rule**（`01_ARCHITECTURE.md:37-57`/`14_UNITY_BLUEPRINT.md:14-33`）

- 内容：`BattleState`/`BattleManager`/`WaveManager`/`BattleEconomy`/`DeckManager`/`BattleInputController`/`AIController`/`UnitRegistry`/`EnemyManager`/`BossManager`/`WeaponManager`/`ProjectileManager`/`BuffManager`/`SkillManager`/`BattleResult`。
- 约束：**不得引用 `MonoBehaviour`、`Transform`、`Time.deltaTime`、Spine 或 Unity UI**（`01_ARCHITECTURE.md:57`）。
- 落地：普通 C# 类（`Game.Combat.Domain` 程序集，`14_UNITY_BLUEPRINT.md:7`）。

**Layer 2 — Configuration**（`01_ARCHITECTURE.md:59-73`/`14_UNITY_BLUEPRINT.md:14-33`）

- 内容：Units/Generals/Enemies/Bosses/Weapons/Projectiles/Buffs/Skills/Waves/Maps/Battle economy 配置。
- 落地：优先 `unity-export/config/*.json`，稳定后可转 ScriptableObject（`01_ARCHITECTURE.md:61`）。**SHALL NOT 用 ScriptableObject 同时保存运行时状态**（`14_UNITY_BLUEPRINT.md:69`）。

**Layer 3 — Adapter / Port**（`01_ARCHITECTURE.md:75-86`/`14_UNITY_BLUEPRINT.md:43-52`）

- 内容：8 端口的 Unity 实现——`ICombatClock`/`IRandomSource`/`ICombatView`/`IAudioPort`/`IVfxPort`/`IInputPort`/`IScenePort`/`IResourcePort`。
- 落地：`Game.Combat.UnityAdapters` 程序集（`UnityCombatClock`/`UnityRandomSource`/`UnityScenePort`/`UnityInputAdapter`/`UnityResourcePort`/`UnityAudioPort`/`UnityVfxPort`）。

**Layer 4 — Presentation**（`01_ARCHITECTURE.md:88-96`/`14_UNITY_BLUEPRINT.md:54-61`）

- 内容：单位/敌人/Boss Prefab、Spine-Unity 动画、投射物和 Trail、地块高亮/雨幕/黑暗遮罩、HUD/牌组/GameOver。
- 落地：`Game.Combat.Presentation` 程序集。

### 5.3 禁止项（14_UNITY_BLUEPRINT.md:64-69）

`14_UNITY_BLUEPRINT.md:64-69`「不要这样做」一节，P2 接入 MUST 遵守：

- 不要把每个 Domain 实体都做成 MonoBehaviour。
- 不要用 `Time.deltaTime` 直接驱动规则冷却（规则用 80ms Tick + 毫秒单位，`:435`）。
- 不要让 UI 直接改金币或 Registry（经 command/规则层）。
- **不要让动画事件成为唯一伤害来源**（与 `:440` 一致）。
- 不要让 ScriptableObject 同时保存运行时状态。

## 6. 失踪参考文档记录（任务 6.5）

### 6.1 磁盘缺失确认

`CODEX_HANDOFF_v0.8.1.md:349`/`:365` 的 Unity 接手资料阅读顺序引用了以下两份 `unity-handoff/reference/` 文档，但磁盘实测缺失：

| 文档 | CODEX_HANDOFF 引用 | 磁盘状态 |
|------|-------------------|----------|
| `unity-handoff/reference/00_IMPLEMENTATION_STATUS.md` | `:349`（阅读顺序首项） | **缺失** |
| `unity-handoff/reference/16_KNOWN_GAPS.md` | `:365`（阅读顺序中 15 与 17 之间） | **缺失** |

实测 `unity-handoff/reference/` 现存文件：`01_ARCHITECTURE.md`、`02_SINGLE_GAME_FLOW.md`、`03_COMBAT_TICK.md`、`04_LIFECYCLE_AND_CLEANUP.md`、`05_COMMANDS_EVENTS_RESULTS.md`、`06_MAP_AND_PLACEMENT.md`、`07_DECK_ECONOMY_AI.md`、`08_ENTITIES.md`、`09_WEAPONS_PROJECTILES_DAMAGE.md`、`10_BUFFS_SKILLS.md`、`11_POOLING_AND_OWNERSHIP.md`、`12_MANAGER_API_REFERENCE.md`、`13_CONFIG_GUIDE.md`、`14_UNITY_BLUEPRINT.md`、`15_MIGRATION_CHECKLIST.md`、`17_SOURCE_TRACEABILITY.md`（共 16 份，缺 00 与 16）。

### 6.2 影响

- `00_IMPLEMENTATION_STATUS.md` 缺失：P2 proposal 无法从该文档获取整体实现状态概览，需依赖 `ADouTaskProgress.md` + 本契约 + 缺口清单交叉判断。
- `16_KNOWN_GAPS.md` 缺失：P2 proposal 无法从该文档获取已知缺口汇总，需依赖 `docs/p2-presentation-gap-catalog.md`（任务 6.1 产出）+ `CODEX_HANDOFF_v0.8.1.md:387-427`「当前准确缺口」节。

### 6.3 建议

建议后续独立 P2 proposal 补齐这两份参考文档：

- `00_IMPLEMENTATION_STATUS.md`：汇总当前各子系统实现状态标签（`COMPLETE`/`COMPLETE_FOR_LOGIC_NO_ASSETS`/`CORE_COMPLETE`/`PARTIAL_WITH_EXACT_GAPS`/`PARTIAL_CORE_CONFIG`/`DEFERRED_*`/`INFERRED`，见 `CODEX_HANDOFF_v0.8.1.md:447-454`）。
- `16_KNOWN_GAPS.md`：汇总已知缺口（含 P2-01..04 表现层缺口 + `DEFERRED_FALL_IMPACT`/`DEFERRED_LI_DRAW_LIST`/`DEFERRED_GENERAL_MERGE` 等逻辑层 DEFERRED 项）。

本提案 SHALL NOT 补齐这两份文档（超出 `gap-sweep-and-presentation` 范围，属 P2 proposal 范畴），仅在此记录缺失并建议。

## 7. P2 接入契约总结

后续独立 P2 proposal 实现表现层实体时，MUST 遵守本契约：

1. **规则层中立**：不引入规则层对 Laya/Spine/微信/场景节点的直接依赖（`:339`）。
2. **经 port 注入**：表现层经 8 引擎中立端口 + 具体适配器注入规则层，不反向。
3. **DEFERRED 桩替换保签名**：替换 DEFERRED no-op 桩时保持方法签名与中立返回结构，不抛异常、不阻塞状态机（除非 port 契约明确要求具体实现，如 `SkillPresentationPort.createOverlay`/`Trail2DAdapter.create`）。
4. **表现非唯一触发**：真实 VFX/音频 SHALL NOT 成为规则结算的唯一触发来源（`:440`），结算仍由规则层 80ms Tick/`BATTLE_FINISHED` 驱动。
5. **4 层分层**：遵守 Domain/Rule → Config → Adapter/Port → Presentation 分层，Domain 不引用 MonoBehaviour/Transform/Time.deltaTime/Spine/Unity UI。
6. **不擅自补 DEFERRED**：`:442`——不自行补全 `INFERRED`/`PARTIAL`/`DEFERRED_*` 内容，补实现须有 bundle/src/配置追溯（`:443`）。
7. **补齐失踪文档**：建议 P2 proposal 补齐 `00_IMPLEMENTATION_STATUS.md`/`16_KNOWN_GAPS.md`。

## 8. 证据索引

| 证据 | 源 |
|------|----|
| 规则层独立约束 | `CODEX_HANDOFF_v0.8.1.md:339` |
| 信任与实现规则（含表现动画非唯一触发） | `CODEX_HANDOFF_v0.8.1.md:437-443`（`:440` 表现动画非规则唯一触发来源） |
| 8 引擎中立端口定义 | `src/ports/CombatPorts.js:3-10` |
| Unity C# 等价接口 | `unity-handoff/UNITY_PORT_INTERFACES.md:4-14` |
| 4 层架构（分层定义） | `unity-handoff/reference/01_ARCHITECTURE.md:36-98` |
| Unity 落地蓝图（程序集/禁止项） | `unity-handoff/reference/14_UNITY_BLUEPRINT.md:5-69` |
| 组合入口（CombatCompositionRoot） | `unity-handoff/reference/01_ARCHITECTURE.md:100-108` |
| DEFERRED 桩 deferredCalls 机制 | `src/skills/SkillEffectPort.js:5,14` |
| CavalrySweep/PikeTip VFX 桩 no-op | `src/presentation/LayaEnemyPresentation.js:219-267` |
| createEntityVfx 返回 null | `src/skills/presentation/SkillPresentationPort.js:18` |
| SkillAudioRegistry 无 audio 兜底 | `src/skills/presentation/SkillAudioRegistry.js:7-17` |
| DevelopmentAudio music-only 桩 | `src/platform/dev/DevelopmentAudio.js:4-8` |
| services() 注入通道 | `src/skills/SkillEffectPort.js:6-12` |
| 失踪文档引用 | `CODEX_HANDOFF_v0.8.1.md:349,365`（磁盘缺失 00/16） |
| 现存 reference 文件 | `unity-handoff/reference/01-15,17`（16 份，缺 00/16） |
