# 命令、事件与 BattleResult

## BattleInputCommand

| 命令 | Payload 核心字段 | 作用 |
|---|---|---|
| `PurchaseAndPlace` | side, slot, gridX, gridY | 购买卡牌并放置单位 |
| `BeginDrag` | side, slot, pointer/grid data | 开始拖拽 |
| `MoveDrag` | position/grid | 更新拖拽 |
| `CommitPlacement` | gridX, gridY | 提交放置 |
| `CancelDrag` | 无 | 取消拖拽 |
| `MoveUnit` | unitId, gridX, gridY | 移动现有单位 |
| `MergeUnits` | sourceId, targetId | 合成 |
| `Refresh` | side | 刷新手牌 |

Unity UI 只产生命令，不直接扣金币、创建单位或写 Registry。

## 关键事件

完整映射见 `unity-export/config/events.json`。

| 事件 | 用途 |
|---|---|
| `BATTLE_FINISHED` | 触发唯一结算入口 |
| `ROUND_STARTED` | 新波开始 |
| `ROUND_SPAWN_PREPARED` | 本波生成参数已准备 |
| `BATTLE_SCENE_GAME_OVER` | 表现层停止 |
| `BATTLE_RESULT_READY` | GameOver 数据就绪 |
| `HEALTH_CHANGED` | 双方阿斗生命变化 |
| `GOLD_CHANGED` | 战斗金币变化 |
| `ENEMY_REGISTERED/REMOVED` | 敌人和 Boss Registry 同步 |
| `ENEMY_KILLED_BY` | 击杀归属、经济与统计 |
| `BOSS_SPAWNED/REMOVED` | Boss 生命周期 |
| `WAVE_PLANNED` | 波次计划可视化/调试 |

## BattleResult

```json
{
  "isWin": true,
  "star": 1,
  "gold": 23,
  "battleDuration": 29320,
  "round": 2,
  "playerTargetHealth": 1,
  "opponentTargetHealth": 0,
  "weaponFragments": [],
  "killCount": 6,
  "bossKillCount": 0,
  "endlessRound": 0,
  "gameMode": "normal",
  "resultState": "WIN"
}
```

星级当前规则：失败 0 星；胜利时按玩家阿斗剩余生命比例得到 1–3 星。
