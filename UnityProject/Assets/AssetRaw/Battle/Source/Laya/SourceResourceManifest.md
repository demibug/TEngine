# Battle Laya 源资源清单

本清单由 `Layout/Scene/BattleScene.ls`、`Layout/Prefab/mapBg0.lh`、`heart.lh`、`mapItem.lh`、`mob.lh` 的 `_$child` 与 `_$prefab` 递归关系，以及所有 `skin` 字段按序去重后形成。运行时必需单位和箭不在这些布局中直接引用，因此按 Change 设计单独列为补充帧。

## 本地隔离副本

- `resources/img/map/AutoAtlas.*` → `Map/AutoAtlas.*`
- `resources/img/battleUI/AutoAtlas.*` → `Map/BattleUI/AutoAtlas.*`
- `resources/img/gameObject/AutoAtlas.*` → `GameObject/AutoAtlas.*`
- `resources/img/weapon/AutoAtlas.*` → `Weapon/AutoAtlas.*`

BattleUI 图集属于地图端点、路径提示和格子视觉依赖，保留 `BattleUI` 子目录以避免与 Map 图集的 `AutoAtlas.*` 同名冲突。

## 布局引用

- `BattleScene.ls` → `../prefab/heart.lh`，去重后 1 个 Prefab 引用，无循环。
- 五个布局文件没有直接的 Spine skeleton、Spine atlas 或 Spine texture 引用。

## Map 源资源

源文件：

- `resources/img/map/AutoAtlas.atlas`
- `resources/img/map/AutoAtlas.png`
- `resources/img/battleUI/AutoAtlas.atlas`
- `resources/img/battleUI/AutoAtlas.png`

世界层所需图集帧：

- `resources/img/map/bg_0.png`
- `resources/img/map/divide_0.png`
- `resources/img/map/mapBg_0.png`
- `resources/img/map/mapBg_1.png`
- `resources/img/map/mapBg/mapBg2/title.png`
- `resources/img/map/mapBg/mapBg0/mountains.png`
- `resources/img/map/mapBg/mapBg0/bird0.png`
- `resources/img/map/mapBg/mapBg0/bird1.png`
- `resources/img/map/mapBg/mapBg0/deer0.png`
- `resources/img/map/mapBg/mapBg0/deer1.png`
- `resources/img/map/mapBg/mapBg0/deer2.png`
- `resources/img/battleUI/dir.png`
- `resources/img/battleUI/bound3.png`
- `resources/img/battleUI/tip1.png`
- `resources/img/battleUI/tip2.png`
- `resources/img/battleUI/heart1.png`
- `resources/img/battleUI/heart3.png`
- `resources/img/battleUI/heart4.png`

## GameObject 源资源

源文件：

- `resources/img/gameObject/AutoAtlas.atlas`
- `resources/img/gameObject/AutoAtlas.png`

布局直接引用帧：

- `resources/img/gameObject/enemy/shadow1.png`
- `resources/img/gameObject/enemy/hpBg.png`
- `resources/img/gameObject/enemy/hp1.png`
- `resources/img/gameObject/enemy/hp2.png`
- `resources/img/gameObject/enemy/stun1.png`

Change 设计补充帧：

- `resources/img/gameObject/enemy/mob_0.png`
- `resources/img/gameObject/soldier/soldier_0.png`（刀兵）
- `resources/img/gameObject/soldier/soldier_1.png`（弓兵）
- `resources/img/gameObject/soldier/soldier_2.png`（枪兵）
- `resources/img/gameObject/soldier/soldier_3.png`（骑兵）

## Weapon 源资源

源文件：

- `resources/img/weapon/AutoAtlas.atlas`
- `resources/img/weapon/AutoAtlas.png`

Change 设计补充帧：

- `resources/img/weapon/arrow_0.png`

## 排除的旧 Laya UI 引用

以下 21 个去重后的 `skin` 属于旧刷新、退出、危险提示、金币、铲子、卡组、道具或征兵界面，不进入世界 Prefab：

- `resources/img/battleUI/ad/adImg.png`
- `resources/img/battleUI/ad/light.png`
- `resources/img/battleUI/ad/shovel.png`
- `resources/img/battleUI/ad/shovelAd0.png`
- `resources/img/battleUI/ad/shovelAd1.png`
- `resources/img/battleUI/btn2.png`
- `resources/img/battleUI/btn3.png`
- `resources/img/battleUI/danger.png`
- `resources/img/battleUI/deckBtn0.png`
- `resources/img/battleUI/deckBtn1.png`
- `resources/img/battleUI/deckBtn2.png`
- `resources/img/battleUI/gold.png`
- `resources/img/battleUI/goldBg.png`
- `resources/img/battleUI/pauseBtn.png`
- `resources/img/battleUI/propsBoxBg.png`
- `resources/img/battleUI/refreshLight0.png`
- `resources/img/battleUI/ying.png`
- `resources/img/props/activePropsBgLight.png`
- `resources/img/props/activePropsBgNew.png`
- `resources/img/props/activePropsBgTip.png`
- `resources/img/props/passivePropsBgNew.png`
