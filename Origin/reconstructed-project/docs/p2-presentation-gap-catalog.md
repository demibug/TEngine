# P2 表现层缺口清单

> 来源提案：`openspec/changes/gap-sweep-and-presentation/`
> 任务覆盖：6.1（P2-01..04 缺口清单）、6.2（现有 port 体系盘点）、6.3（关键缺口项盘点）
> 性质：**本清单仅为文档产出，不实现任何 VFX/音频/场景/Prefab/微信实体**（spec.md Requirement「P2 表现层必须产出缺口清单与接入契约」Scenario「不实现表现层实体」）。它是后续独立 P2 proposal 的输入。
> 编写依据：实际读取 `src/` 源码、`unity-handoff/` 文档、`analysis/critical-path/deferred-modules.md` 与 `ADouTaskProgress.md:121-128`（P2-01..04 表）交叉盘点。每项证据以 `file:line` 标注。

## 总览

P2 表现层（非纯逻辑）按 [ADouTaskProgress.md](../ADouTaskProgress.md):125-128 拆为四项：

| 编号 | 范围 | 验收要点 | 当前状态 |
|---|---|---|---|
| P2-01 | Unity/Laya 场景、Prefab、Spine、Tween、Trail2D | 逻辑对象具备对应表现层承载 | port 体系已立 + DEFERRED 桩；真实 `.ls` 场景运行时未接入 |
| P2-02 | 图片、骨骼、粒子、音频资源 | 所需资源接入并可按生命周期加载、释放 | 资源目录索引齐全（53 Prefab + ImageCatalog）；music/sound 子包缺失 |
| P2-03 | BattleScene/GameOverScene 完整 UI | 战斗与结算场景 UI 流程完整 | 控制器与节点清单重建；真实 `.ls` 绑定未接入，`tm` 目标控制组件未恢复 |
| P2-04 | 微信登录、广告、分享、排行、云存档、商店、设置、活动 | 外部服务功能具备明确接口和接入验证记录 | PlatformAdapter 契约已立 + DevelopmentPlatform 桩；正式 wx/tt 实现未做 |

规则层独立约束（`CODEX_HANDOFF_v0.8.1.md:339`）：核心逻辑迁移时不要让规则层直接依赖 Laya、Spine、微信或场景节点；表现动画非规则唯一触发来源（`CODEX_HANDOFF_v0.8.1.md:440`）。

---

## P2-01：Laya/Unity Scene、Prefab、Spine、Tween、Trail2D

### 现状

表现层承载骨架已通过 port/适配器建立，规则层不直接依赖 Laya/Spine：

- **引擎中立端口**：`src/ports/CombatPorts.js:3-10` 定义 8 个端口基类（`CombatClockPort`/`RandomSourcePort`/`CombatViewPort`/`AudioPort`/`VfxPort`/`InputPort`/`ScenePort`/`ResourcePort`），未实现方法抛 `not implemented`，供 Unity 侧 `UNITY_PORT_INTERFACES.md` 等价 C# 接口实现。
- **Laya 表现适配器**：`src/presentation/LayaEnemyPresentation.js` 提供 mob/boss visual、spawn/death Tween、Spine 动画创建（`createAnimation` 经 `createLayaSpineAnimation`，`src/presentation/LayaSpineAnimation.js:77`）、Zombie/Cavalry/Puppet 表现方法。
- **Prefab 工厂**：`src/presentation/LayaPrefabFactory.js:4-45` 经 `PREFAB_CATALOG`（53 条 `.lh` prefab）按 key 同步/异步创建节点，要求 prefab 已预加载且含 `create()`。
- **Spine 包装**：`src/presentation/LayaSpineAnimation.js:10-75` 从 `bundle.strings-decoded.js:14170-14280`（符号 `tk`）还原 `LayaSpineAnimation`（extends `Laya.Sprite` + `Spine2DRenderNode`），支持 play/stop/playbackRate/setIsFastMode/showSkinByName/onStop/resetForPool。
- **拖尾适配器**：`src/rendering/Trail2DAdapter.js:2-15` 经 prefabFactory 创建 `bulletTrail/*` 节点并解析 `Trail2DRender` 组件，支持 sync/pause/resume/fade/clear。
- **技能表现**：`src/skills/presentation/SkillPresentationPort.js` + `LayaSkillPresentation.js` 提供 Spine/overlay/tileMarker/entityVfx 调度，`createOverlay`/`createTileMarker` 在基类抛 `requires a concrete implementation`，由 `LayaSkillPresentation` 实现。
- **5 个 DEFERRED VFX 桩**（`src/presentation/LayaEnemyPresentation.js:219-267`）：`createCavalrySweepVisual`/`removeCavalrySweepVisual`/`createPikeTipVisual`/`animatePikeTipThrust`/`hidePikeTipVisual`，桩 no-op 返回 null/不操作，不抛异常，伤害结算由规则层 `hit()` 驱动不依赖视觉对象。

### 缺口

- **真实 `.ls` 场景文件运行时未接入**：`origin_project/scene/*.ls` 物理存在（BattleScene.ls 18KB 等），但运行时经 `src/bootstrap/DevelopmentBootstrap.js:574-608` 的 `installDevelopmentSceneFactories()` 用 `createNode()` 手搓最小节点（注释 `DEVELOPMENT_SCENE_STUB：真实尺寸与序列化组件必须由缺失的 BattleScene.ls 恢复`，`:580`）。`requireNode` 在节点缺失时抛 `${name} is required; scene .ls binding is missing`（`src/scenes/SceneControllerBase.js:25-29`）。
- **5 个 VFX 桩待实体化**：CavalrySweep×2（`bundle:24818-24820` 原版创建两个 sweep 视觉对象 m/o）+ PikeTip×3（`bundle:24585` pikeEff1.png + `24736/24740` Tween 显隐）目前 no-op，无实际渲染对象。
- **`tm` 目标控制组件未恢复**：`src/scenes/BattleSceneController.js:114` 注释 `TODO_UNVERIFIED：原 tm 目标控制组件尚未恢复；不创建无依据替代组件`（阿斗 life UI/目标控制，符号 `tm`）。
- **Spine 资源依赖未验证**：`LayaSpineAnimation` 构造要求 `Laya.Spine2DRenderNode`（`src/presentation/LayaSpineAnimation.js:11`），dev 无 Laya 时抛 `TypeError`；实际 `.skel`/`.atlas` 资源是否齐备未在运行时验证。
- **Trail2D 资源未验证**：`Trail2DAdapter` 要求 prefabFactory 创建 `bulletTrail/*` prefab（`src/rendering/Trail2DAdapter.js:7`），目录中 trail prefab 是否预加载未在运行时验证。

### 证据

- `src/ports/CombatPorts.js:3-10`（8 端口基类）
- `src/presentation/LayaEnemyPresentation.js:219-267`（5 VFX DEFERRED 桩）
- `src/presentation/LayaPrefabFactory.js:4-45`（prefab 工厂）；`src/resources/PrefabCatalog.js`（53 条 prefab）
- `src/presentation/LayaSpineAnimation.js:11,77`（Spine 包装，require `Spine2DRenderNode`）
- `src/rendering/Trail2DAdapter.js:7`（trail prefab 依赖）
- `src/scenes/SceneControllerBase.js:25-29`（requireNode throw）
- `src/scenes/BattleSceneController.js:28-34,114`（requireNode 绑定 + tm TODO_UNVERIFIED）
- `src/bootstrap/DevelopmentBootstrap.js:574-608`（dev 场景桩工厂 + DEVELOPMENT_SCENE_STUB 注释）
- `analysis/critical-path/deferred-modules.md:14`（`Scene .ls | 缺失 | 开发场景工厂提供最小节点 | 真实节点层级、序列化属性、资源绑定 | 主要真实画面阻塞项`）
- `unity-handoff/reference/01_ARCHITECTURE.md:88-96`（Presentation 层职责：Prefab/Spine/投射物/Trail/HUD/GameOver）

---

## P2-02：图片、骨骼、粒子、音频资源（含 music/sound 子包缺失）

### 现状

资源目录已从 `origin_project` 生成索引，供适配器消费：

- **图片目录**：`src/resources/ImageCatalog.js`（`IMAGE_PATHS` 冻结数组）含 aDou/boss0-2/dancer/dongZhuo/huaXiong/lvBu/maChao/mihuan/stamina/thief/zhangFei/zhaoYun 等 Spine skeleton.png 路径。
- **Prefab 目录**：`src/resources/PrefabCatalog.js` 含 53 条 `.lh` prefab（含 `dialog:AuthorizeDialog` 等弹窗、`bulletTrail/*`、`loveHeart` 等），每条标注 path/sourcePath/type/variables。
- **场景目录**：`src/resources/SceneCatalog.js` 含 11 个场景（AvatarSettingScene/BattleScene/GMScene/GameOverScene/LoadMaskScene/LoadScene/MainScene/MapEditor/MatchScene/RankScene/SettingScene），每场景列出 variables 节点清单（BattleScene 含 map/gameObjectBox/effectBox/round/goldNumTxt/end1/end2/refreshBtn/deckBtn/shovelAd 等，`SceneCatalog.js:134-634`）。
- **音频桩**：`src/platform/dev/DevelopmentAudio.js:3-8` 仅 `init(musicVolume,soundVolume)`/`playMusic(name)`/`stopMusic()` 三个方法，music-only，无 `playSound`/`stopSound`。
- **技能音频注册表**：`src/skills/presentation/SkillAudioRegistry.js:3-20` 经 `BOSS_RESOURCE_MANIFEST`（`src/skills/presentation/SkillResourceManifest.js`）派发 boss 技能音频 key，实际播放委托注入的 `audio` 对象的 `playSound`/`play`。
- **资源可用性探测**：`SkillPresentationPort.requireResource`（`src/skills/presentation/SkillPresentationPort.js:25`）经 `resourceCatalog.has(path)` 标 `AVAILABLE_IN_ORIGIN_PROJECT` 或 `TODO_RESOURCE_MISSING`，未命中入 `missingResources[]`。

### 缺口

- **music/sound 子包缺失**：`unity-handoff/MISSING_ASSETS_AND_UI.md:8` 明确 `Actual music/sound files (the supplied origin project did not contain the declared music/sound subpackages)`——原始工程未包含声明的 music/sound 子包，`DevelopmentAudio` 为 music-only 桩，bundle 音频未解码。
- **生产 `AudioPort` 实现缺失**：`src/ports/CombatPorts.js:6` `AudioPort.play/stop` 抛 `not implemented`；`DevelopmentAudio` 无 `playSound`（`GameOverSceneController.js:25` 调 `audio.playSound('game_win'/'game_lose')` 在 dev 下会因 `audio` 为 `DevelopmentAudio` 而失败或被外层吞掉），生产侧 `IAudioPort.Play/Stop`（`unity-handoff/UNITY_PORT_INTERFACES.md:8`）未实现。
- **粒子资源未验证**：Zombie 气泡（`bubble`，`LayaEnemyPresentation.js:51`）、Puppet 爱心（`loveHeart`，`LayaEnemyPresentation.js:177`）经 `Laya.Pool.getItemByCreateFun` 取池对象，无 prefab 时用 `Sprite` 占位；真实 `.lh` 粒子 prefab 未在运行时验证。
- **骨骼资源依赖未验证**：`ImageCatalog` 列出 skeleton.png，但 `LayaSpineAnimation` 需 `Spine2DRenderNode` + templet（`.skel`/`.atlas`），dev 无 Laya 抛 `TypeError`（`LayaSpineAnimation.js:11`）；`requireResource` 标注的 `TODO_RESOURCE_MISSING` 项未统计清零。

### 证据

- `src/resources/PrefabCatalog.js`（53 条 prefab）；`src/resources/ImageCatalog.js`（IMAGE_PATHS）；`src/resources/SceneCatalog.js:134-634`（BattleScene 节点清单）
- `src/platform/dev/DevelopmentAudio.js:3-8`（music-only 桩，无 playSound）
- `src/ports/CombatPorts.js:6`（AudioPort 抛 not implemented）
- `src/skills/presentation/SkillAudioRegistry.js:3-20`（音频 key 派发，委托 audio）
- `src/skills/presentation/SkillPresentationPort.js:25`（requireResource 资源探测）
- `unity-handoff/MISSING_ASSETS_AND_UI.md:8`（music/sound 子包缺失）
- `unity-handoff/MISSING_ASSETS_AND_UI.md:10`（53 prefabs/Boss-A-Dou Spine/skill images/Trail2D/battle UI metadata 已索引）
- `src/scenes/GameOverSceneController.js:25`（`audio.playSound('game_win'/'game_lose')`）

---

## P2-03：BattleScene/GameOverScene 完整 UI 节点绑定

### 现状

场景控制器与节点清单已重建，但运行时绑定依赖 dev 桩：

- **BattleSceneController**（`src/scenes/BattleSceneController.js:13`，重建状态 `PARTIAL_CRITICAL_PATH_IMPLEMENTATION`）：`onAwake` 经 `requireNode` 绑定 map/gameObjectBox/effectBox/round/goldNumTxt/end1/end2（`:28-34`），订阅 `BATTLE_SCENE_GAME_OVER`/`ROUND_STARTED`/`ENEMY_CREATED`（`:45-47`）；`_createBattleTargets` 建 playerTarget/opponentTarget 并 addChild 到 end1/end2（`:100-110`）。
- **GameOverSceneController**（`src/scenes/GameOverSceneController.js:5`）：`onAwake` 经 `_fallbackNode` 兜底绑定 winBg/loseBg/box/goldBg/allGoldNumTxt/winBox/loseBox/rankSp/weaponBox/goldLight/gold/goldNumTxt/getBtn/getBtnAd/getTxt（`:11`），`_createResultAnimations` 经 `animationEntityPool.create('aDou')` 建胜负 Spine（`:16`）；`onOpened` 切 win/lose 显隐、设金币/段位文本、播 `game_win`/`game_lose` 音效（`:17-26`）。
- **节点清单完备**：`SceneCatalog.js` 已索引 BattleScene 全部 UI 节点（refreshBtn/deckBtn/shovelAd/danger0-3/propsBox 等，`SceneCatalog.js:134-634`）与 GameOverScene 全部节点（rankSp/star0-5/scroll0-2/lightning/cloud 等，`SceneCatalog.js:956-1516`）。
- **表现层分层**：`BattleSceneController._ensurePresentationLayers`（`:50-60`）建 battleWorldLayer/skillVfxLayer/weatherLayer/overlayLayer。

### 缺口

- **真实 `.ls` 绑定未接入**：`requireNode` 在节点缺失抛 `scene .ls binding is missing`（`SceneControllerBase.js:27`）；dev 桩只手搓 map/gameObjectBox/effectBox/end1/end2/round/goldNumTxt/shovelAdBg/adLight（`DevelopmentBootstrap.js:574-599`），refreshBtn/deckBtn/shovelAd/danger/propsBox 等 UI 节点未在 dev 桩创建，真实 `.ls` 反序列化未接入。
- **`tm` 目标控制组件未恢复**：`BattleSceneController.js:114` `TODO_UNVERIFIED`——原 `tm` 组件（阿斗 life UI/目标控制，`deferred-modules.md:8` `aDou 骨骼包装类 | uz、tm | ... | Spine 播放、受击、生命、目标控制组件 | 阻止真实视觉与胜负伤害链`）未恢复。
- **GameOver UI 流不完整**：`GameOverSceneController` 用 `_fallbackNode` 兜底（节点不存在则 new Sprite，`:7`），真实 rankSp/star/scroll/lightning/cloud 动画与领取（getBtn/getBtnAd）→ `platformResultPort.claimReward/claimDoubleReward`（`:27`）依赖未实现的 platform 结果端口。
- **PauseDialog/GameOverScene 真实 UI 不可渲染**：`deferred-modules.md:13` `PauseDialog/GameOverScene | 多个 UI 类 | 暂停入口与 game-over 事件 | UI 节点、动画、结算页 | 清理可测，真实 UI 不可渲染`。

### 证据

- `src/scenes/BattleSceneController.js:13,28-34,50-60,100-114`（PARTIAL 状态 + requireNode 绑定 + tm TODO_UNVERIFIED）
- `src/scenes/GameOverSceneController.js:5-32`（fallback 绑定 + 动画 + platformResultPort 依赖）
- `src/scenes/SceneControllerBase.js:25-29`（requireNode throw）
- `src/bootstrap/DevelopmentBootstrap.js:574-608`（dev 桩只建部分节点）
- `src/resources/SceneCatalog.js:134-634,956-1516`（BattleScene/GameOverScene 节点清单）
- `analysis/critical-path/deferred-modules.md:8,13`（tm 阻塞 + GameOver UI 不可渲染）

---

## P2-04：微信登录、广告、分享、排行、云存档、商店、设置、活动

### 现状

平台契约已立，dev 桩可运行，正式平台实现未做：

- **PlatformAdapter 契约**：`src/platform/PlatformAdapter.js:12-19` 定义 `initialize`/`preload`/`login`/`getChannelAppId`/`shouldEnterMatchDirectly`/`startGame`，未实现抛 `PlatformMethodNotImplementedError`。
- **DevelopmentPlatform 桩**：`src/platform/dev/DevelopmentPlatform.js:6-54` 注释 `DEVELOPMENT_ONLY：不调用 wx.*、tt.*、广告、分享或云存储`，返回 DEVELOPMENT_SAMPLE 登录结果，`assertNoNativePlatformCalls()` 返回 true。
- **平台相关场景已索引**：`SceneCatalog.js` 含 SettingScene（`SettingScene.ls`，musicSliderBar 等设置节点，`:2627+`）、RankScene（`RankScene.ls`，list/playerRank/countryBtn/provinceBtn，`:2534+`）、AvatarSettingScene（`:5+`）；MainScene 含 shopBtn/rankBtn/dySidebarBtn/followBtn（`SceneCatalog.js:1939-2009`）。
- **GameOver 领奖接口**：`GameOverSceneController.js:22,27` 经 `deps.platformResultPort.claimReward/claimDoubleReward`（含 `getBtnAd` 看广告翻倍）。

### 缺口

- **微信/字节平台实现缺失**：`deferred-modules.md:5` `微信/字节平台实现 | qN、qB、r2 | initialize/preload/login/channelAppId/direct-match/startGame | 授权、广告、分享、开放数据域、录屏等 | 开发模式可运行；正式平台尚不可用`——授权、广告、分享、开放数据域（排行）、录屏均未做。
- **云存档缺失**：`deferred-modules.md:7` `完整 PlayerData | tY/tw | 体力、场次、地图、日胜负、最小排行/道具契约 | 云存档、武器碎片、经济外循环等 | 不阻止进入战斗`——云存档未实现。
- **商店/设置/活动 UI 流缺失**：`unity-handoff/MISSING_ASSETS_AND_UI.md:7` `Shop, settings, weapon inventory and event screens` 明确为核心战斗不需要、未接入；MainScene 的 shopBtn（`SceneCatalog.js:1939` `visible:false`）、SettingScene/RankScene 控制器未恢复完整流。
- **广告/分享/排行端口未定义**：`PlatformAdapter` 仅有登录/启动 6 方法，无 `createRewardedVideoAd`/`shareAppMessage`/`openDataContext`/云存档方法；`GameOverSceneController` 的 `platformResultPort`（claimReward/claimDoubleReward）无实现类。
- **Laya 最终 bundle/build 管线缺失**：`unity-handoff/MISSING_ASSETS_AND_UI.md:6` `Laya final bundle/build pipeline` 未接入。

### 证据

- `src/platform/PlatformAdapter.js:12-19`（契约 + 未实现抛错）
- `src/platform/dev/DevelopmentPlatform.js:6,53-54`（DEVELOPMENT_ONLY + assertNoNativePlatformCalls）
- `src/scenes/GameOverSceneController.js:22,27`（platformResultPort 依赖）
- `src/resources/SceneCatalog.js:1939-2009`（shopBtn/rankBtn/dySidebarBtn/followBtn）、`:2534+`（RankScene）、`:2627+`（SettingScene）
- `unity-handoff/MISSING_ASSETS_AND_UI.md:5-8`（微信/广告/分享/排行/云存档 + shop/settings/event + Laya build + music/sound 缺失）
- `analysis/critical-path/deferred-modules.md:5,7`（微信/字节平台 + 云存档）

---

## 现有 port 体系盘点（任务 6.2）

规则层与表现层的接入边界由「8 引擎中立端口 + 具体适配器」构成，规则层不直接依赖 Laya/Spine/微信/场景节点（`CODEX_HANDOFF_v0.8.1.md:339`）。

### 8 引擎中立端口（`src/ports/CombatPorts.js:3-10`）

| 端口 | 基类方法（未实现抛错） | Unity 等价接口（`UNITY_PORT_INTERFACES.md:5-12`） |
|---|---|---|
| `CombatClockPort` | `now()`（`:3`） | `ICombatClock.NowMilliseconds` |
| `RandomSourcePort` | `next()`（`:4`） | `IRandomSource.Next01` |
| `CombatViewPort` | `spawn()/remove()`（`:5`） | `ICombatView.Spawn/Remove` |
| `AudioPort` | `play()/stop()`（`:6`） | `IAudioPort.Play/Stop` |
| `VfxPort` | `create()/remove()`（`:7`） | `IVfxPort.Create/Remove` |
| `InputPort` | `nextCommand()` 返回 null（`:8`） | `IInputPort` 产 PurchaseAndPlace/drag/move/merge/refresh |
| `ScenePort` | `open()/close()`（`:9`） | `IScenePort.Open/Close` |
| `ResourcePort` | `load()`（`:10`） | `IResourcePort.Load` |

Unity 侧输入不得直接 inspect `PointerEventData`（`UNITY_PORT_INTERFACES.md:14`）。

### 具体适配器

- **`SkillEffectPort`**（`src/skills/SkillEffectPort.js:4-46`）：Boss/武将技能 effect 注册与调度，`execute` 未注册 handler 入 `deferredCalls[]` 标 `DEFERRED_EFFECT_WITH_EXACT_CONTRACT`（`:14`），`services()`（`:12`）注入 buffManager/enemyManager/unitRegistry/presentation/audioRegistry/projectileManager/attackEffectManager；`onOwnerAttack`（`:18`）武将每次攻击 hook 通道（跳斩溅射）。
- **`LayaEnemyPresentation`**（`src/presentation/LayaEnemyPresentation.js:5-269`）：敌人表现，含 5 VFX DEFERRED 桩（`:219-267`）+ Zombie/Cavalry/Puppet 表现方法（swampDecal/bubble/heart/aura/breathing）。
- **`SkillPresentationPort`**（`src/skills/presentation/SkillPresentationPort.js:2-30`）：技能表现基类，`createOverlay`/`createTileMarker` 抛 `requires a concrete implementation`（`:15,17`），`createEntityVfx` 返回 null（`:18`），`requireResource`（`:25`）资源探测，`clearOwner`/`gameOver`（`:26,28`）生命周期。
- **`LayaSkillPresentation`**（`src/skills/presentation/LayaSkillPresentation.js:6-51`）：`SkillPresentationPort` 的 Laya 实现，createSpine/createOverlay/createTileMarker/createEntityVfx 经 prefabFactory/SpineFactory/SKILL_VFX_MANIFEST。
- **`SkillAudioRegistry`**（`src/skills/presentation/SkillAudioRegistry.js:3-20`）：音频 key 派发，`playBossSkill` 经 `BOSS_RESOURCE_MANIFEST`（`src/skills/presentation/SkillResourceManifest.js`），`activeLoops` Map 跟踪 loop 音频，`clearOwner`/`gameOver` 清理。
- **`Trail2DAdapter`**（`src/rendering/Trail2DAdapter.js:2-15`）：拖尾，经 prefabFactory 创建 `bulletTrail/*` 节点，解析 `Trail2DRender` 组件，sync/pause/resume/fade/clear 生命周期。
- **`LayaPrefabFactory`**（`src/presentation/LayaPrefabFactory.js:4-45`）：经 `PREFAB_CATALOG`（53 条）按 key 创建节点，`createSync` 要求 prefab 已预加载且含 `create()`。
- **`LayaSpineAnimation`**（`src/presentation/LayaSpineAnimation.js:10-82`）：`bundle.strings-decoded.js:14170-14280`（符号 `tk`）还原的 LayaAir 3.3 Spine2D 包装，extends `Laya.Sprite` + `Spine2DRenderNode`，play/stop/playbackRate/setIsFastMode/showSkinByName/onStop/resetForPool/recover/destroy。
- **`DevelopmentSkillPresentation`**（`src/skills/presentation/dev/DevelopmentSkillPresentation.js`，`DevelopmentBootstrap.js:340` 注入）：dev 桩技能表现。

### DEFERRED 桩约定

DEFERRED 桩 no-op：不创建渲染对象、不执行 Tween、不操作节点，返回 null/false/空，记 `calls[]`/`deferredCalls`，不抛异常，不阻塞状态机/伤害结算（`LayaEnemyPresentation.js:220,232,244,255,266`；`SkillEffectPort.js:14`）。表现动画非规则唯一触发来源（`CODEX_HANDOFF_v0.8.1.md:440`；`14_UNITY_BLUEPRINT.md:68`「动画事件成为唯一伤害来源」为「不要这样做」）。

---

## 关键缺口项盘点（任务 6.3）

以下为阻塞真实表现的关键缺口项，每项均经源码 grep/read 确认。

| 缺口项 | 现状（证据） | 缺口 | 标签 |
|---|---|---|---|
| 真实 `.ls` 场景文件运行时未接入 | `SceneControllerBase.requireNode` 节点缺失抛 `scene .ls binding is missing`（`src/scenes/SceneControllerBase.js:25-29`）；dev 桩 `installDevelopmentSceneFactories` 手搓最小节点 + `DEVELOPMENT_SCENE_STUB` 注释（`src/bootstrap/DevelopmentBootstrap.js:574-599`，`:580`）；`deferred-modules.md:14` `Scene .ls | 缺失` | 真实节点层级、序列化属性、资源绑定未恢复 | 主要真实画面阻塞项 |
| `tm` 目标控制组件 | `src/scenes/BattleSceneController.js:114` `TODO_UNVERIFIED：原 tm 目标控制组件尚未恢复`；`deferred-modules.md:8` `aDou 骨骼包装类 | uz、tm | ... | Spine 播放、受击、生命、目标控制组件 | 阻止真实视觉与胜负伤害链` | 阿斗 life UI/目标控制组件未恢复 | TODO_UNVERIFIED |
| 5 VFX 桩 | `LayaEnemyPresentation.js:219-267` `createCavalrySweepVisual`/`removeCavalrySweepVisual`/`createPikeTipVisual`/`animatePikeTipThrust`/`hidePikeTipVisual` 桩 no-op（CavalrySweep×2 对齐 `bundle:24818-24820`，PikeTip×3 对齐 `bundle:24585/24736/24740`） | 无实际渲染对象/Tween | DEFERRED（实体 VFX 归 P2） |
| 生产 `AudioPort` 实现 | `src/ports/CombatPorts.js:6` `AudioPort.play/stop` 抛 `not implemented`；`src/platform/dev/DevelopmentAudio.js:3-8` 仅 music-only（init/playMusic/stopMusic，无 playSound）；`GameOverSceneController.js:25` 调 `audio.playSound('game_win'/'game_lose')` | 生产音频播放未实现，bundle 音频未解码 | DEFERRED |
| music/sound 子包缺失 | `unity-handoff/MISSING_ASSETS_AND_UI.md:8` `Actual music/sound files (the supplied origin project did not contain the declared music/sound subpackages)` | 原始工程未含声明的 music/sound 子包 | 资源缺失 |
| BattleScene/GameOverScene 完整 UI 流 | `BattleSceneController.js:13` `PARTIAL_CRITICAL_PATH_IMPLEMENTATION`；`GameOverSceneController.js:7` `_fallbackNode` 兜底；dev 桩只建部分节点（`DevelopmentBootstrap.js:574-608`）；`deferred-modules.md:13` `真实 UI 不可渲染` | refreshBtn/deckBtn/danger/propsBox/rankSp/star 等真实绑定 + 领奖流未接入 | DEFERRED |
| `IronBow`/`IronArrowBow` 效果依赖 | `src/weapons/bows/IronBow.js:3` `status:'DEFERRED_EFFECT_DEPENDENCY'`（铁胎弓，projectile `vs`）；`src/weapons/bows/IronArrowBow.js:3` `status:'DEFERRED_EFFECT_DEPENDENCY'`（铁弓，projectile `rd`） | 效果依赖未补（火焰灼烧/击退等表现层效果） | DEFERRED_EFFECT_DEPENDENCY |
| `FireArrowBarrage` 弹种变体 | `src/skills/effects/FireArrowBarrageEffect.js:10,80` `DEFERRED_PROJECTILE_VARIANT: true`（火焰箭专属弹种待提案 ④，本 effect 先用通用箭发射） | 火焰箭专属弹种未实现 | DEFERRED_PROJECTILE_VARIANT |
| 微信/字节平台实现 | `src/platform/PlatformAdapter.js:12-19` 仅登录/启动 6 方法；`src/platform/dev/DevelopmentPlatform.js:6` `DEVELOPMENT_ONLY：不调用 wx.*、tt.*、广告、分享或云存储`；`deferred-modules.md:5` 授权/广告/分享/开放数据域/录屏未做 | 授权、广告、分享、排行、录屏均未实现 | DEFERRED |
| 云存档 | `deferred-modules.md:7` `完整 PlayerData | tY/tw | ... | 云存档、武器碎片、经济外循环等` | 云存档未实现 | DEFERRED |
| 商店/设置/活动 UI | `unity-handoff/MISSING_ASSETS_AND_UI.md:7` `Shop, settings, weapon inventory and event screens` 未接入；MainScene shopBtn `visible:false`（`SceneCatalog.js:1939`） | 商店/设置/武器库/活动屏未恢复 | DEFERRED |

### unity-handoff 参考文档缺失（6.5 任务范围，此处记录）

`unity-handoff/reference/` 目录实存 01-15、17（共 16 份），缺 `00_IMPLEMENTATION_STATUS.md` 与 `16_KNOWN_GAPS.md`（探查报告称应有序号 00/16）。此缺失属任务 6.5 范畴，本清单仅记录，建议后续 P2 proposal 补齐。

### 4 层架构证据

`unity-handoff/reference/01_ARCHITECTURE.md:37-96` 定义 4 层：
1. **Domain/Rule Layer**（`:37-57`）：普通类，不得引用 `MonoBehaviour`/`Transform`/`Time.deltaTime`/Spine/Unity UI。
2. **Configuration Layer**（`:59-73`）：`unity-export/config/*.json` 优先。
3. **Adapter/Port Layer**（`:75-86`）：Unity 实现 8 端口接口。
4. **Presentation Layer**（`:88-96`）：MonoBehaviour 负责 Prefab/Spine/投射物/Trail/地块高亮/HUD/GameOver。

`14_UNITY_BLUEPRINT.md:6-11` 推荐程序集 `Game.Combat.Domain/Config/Application/UnityAdapters/Presentation`；`:63-69`「不要这样做」含「动画事件成为唯一伤害来源」「UI 直接改金币或 Registry」「ScriptableObject 同时保存运行时状态」。

---

## 本清单边界（spec 验收对照）

- **覆盖 P2-01..04**（spec Scenario「缺口清单覆盖 P2-01..04」）：P2-01 场景/Prefab/Spine/Tween/Trail2D、P2-02 资源+音频子包缺失、P2-03 UI 节点绑定、P2-04 微信/商店/设置/活动，每项含现状+缺口+证据。
- **不实现表现层实体**（spec Scenario「不实现表现层实体」）：本清单仅为 Markdown 文档，未新增任何 VFX/音频/场景/Prefab/微信实体代码，未修改任何 `.js`/CSV/其他 docs/unity-handoff 文件。
- **port 体系盘点准确**：8 端口 + 8 具体适配器（SkillEffectPort/LayaEnemyPresentation/SkillPresentationPort/LayaSkillPresentation/SkillAudioRegistry/Trail2DAdapter/LayaPrefabFactory/LayaSpineAnimation）均经源码确认。
- **关键缺口项真实**：requireNode throw/DEVELOPMENT_SCENE_STUB/TODO_UNVERIFIED/5 VFX 桩/DevelopmentAudio music-only/IronBow/IronArrowBow DEFERRED_EFFECT_DEPENDENCY/FireArrowBarrage DEFERRED_PROJECTILE_VARIANT 均经 grep/read 确认 file:line。
