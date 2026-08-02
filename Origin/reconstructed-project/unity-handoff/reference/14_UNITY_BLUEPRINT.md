# Unity/C# 落地蓝图

## 推荐程序集

```text
Game.Combat.Domain
Game.Combat.Config
Game.Combat.Application
Game.Combat.UnityAdapters
Game.Combat.Presentation
```

## Domain

普通 C# 类：

```text
BattleState
BattleManager
WaveManager
BattleEconomy
DeckService
BattleCommandHandler
OpponentAI
UnitRepository
EnemyRepository
BossRepository
WeaponService
ProjectileService
BuffService
SkillService
BattleResult
```

## Application

- `BattleFlowService`
- `CombatCompositionRoot`
- `CombatTickDriver`
- Command handlers
- Scene transition orchestration

## UnityAdapters

- `UnityCombatClock`
- `UnityRandomSource`
- `UnityScenePort`
- `UnityInputAdapter`
- `UnityResourcePort`
- `UnityAudioPort`
- `UnityVfxPort`

## Presentation

- Unit/Enemy/Boss View
- Projectile View
- Trail View
- Grid View
- Deck HUD
- Battle HUD
- GameOver View

## 不要这样做

- 每个 Domain 实体都做成 MonoBehaviour。
- 用 `Time.deltaTime` 直接驱动规则冷却。
- UI 直接改金币或 Registry。
- 动画事件成为唯一伤害来源。
- ScriptableObject 同时保存运行时状态。
