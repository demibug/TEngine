# 生命周期、启动与清理顺序

## 通用 CombatLifecycle 顺序

### 启动顺序

```text
economy
→ deckManager
→ battleManager
→ enemyManager
→ unitManager
→ weaponManager
→ projectileManager
→ buffManager
→ skillManager
→ bossManager
→ waveManager
→ inputController
→ aiController
```

### 清理顺序

```text
aiController
→ inputController
→ deckManager
→ waveManager
→ battleManager
→ bossManager
→ enemyManager
→ unitManager
→ weaponManager
→ projectileManager
→ skillManager
→ buffManager
```

## BattleFlowCoordinator 的实际职责

`BattleFlowCoordinator` 额外负责：

- 网络开始/结束上报的可选端口。
- 打开 BattleScene。
- 注册 `BATTLE_FINISHED`。
- 构造 `BattleResult`。
- 关闭 BattleScene 和打开 GameOverScene。
- 表现、地图地块、动画驱动、死亡快照的清理。

## Unity 清理要求

结束后必须满足：

```text
UnitRegistry       = 0
EnemyManager       = 0
BossManager        = 0
ProjectileManager  = 0
WeaponManager      = 0
BuffManager        = 0
SkillManager       = 0
活动 Tick 注册      = 0
事件监听             = 0
延迟回调             = 0
Trail/VFX           = 0
```

## 防重复结算

保留等价状态：

```text
_gameOverInProgress
BattleState.isGameOver
lastBattleResult
```

同一帧双方目标均死亡时，Unity 端必须定义并保持固定的事件顺序；不要让两个 GameOver Scene 同时打开。
