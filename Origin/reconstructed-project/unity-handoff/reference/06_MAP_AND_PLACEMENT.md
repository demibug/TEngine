# 地图、坐标与放置规则

## 基础网格

```text
Grid width:  8 cells
Grid height: 10 cells
Cell size:   80 × 80 source pixels
```

Unity 推荐将规则坐标保持为整数格坐标，不直接使用世界坐标。

```text
Rule grid (x,y)
→ GridToWorld adapter
→ Unity world position
```

## MapData

- 使用四方向 A*，不允许对角移动。
- `0_0`、`0_1` 是可行走路径格。
- `1_0`、`1_1` 是对应阵营的可部署格。
- 其他格不可行走/不可部署。
- 玩家和对手有独立路径。

正式 Map 0 路径已导出到：

```text
unity-export/config/maps.json
```

## 放置校验顺序

```text
MapData.isBuildableForSide
→ MapTileManager.canPlace
→ UnitRegistry.hasBattleOccupant
→ BattleEconomy.payRecruit
→ UnitRegistry.createUnit
→ DeckManager.consume
```

支付后创建失败时必须回滚金币。

## 地块技能

`MapTileManager` 维护技能禁用地块。拆迁类技能必须同时影响：

- 放置合法性
- 地块标记
- 战斗结束清理

Unity 可将 `MapTileState` 作为纯数据，将格子高亮作为 View Adapter。
