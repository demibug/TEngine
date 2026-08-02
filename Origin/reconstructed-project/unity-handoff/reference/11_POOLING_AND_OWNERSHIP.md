# 对象池与运行时所有权

## 两类对象池

1. 按 class 的逻辑对象池。
2. 按字符串 key 的表现对象池。

Unity 建议保持同样区分：

- Domain object pool：纯 C# 对象，可选。
- View pool：Prefab/MonoBehaviour 必须使用独立池。

## 复用前必须清理

- ID
- owner/attacker/target
- target ID
- side
- position/path/progress
- health/max health
- state/death flag
- attack cooldown
- active timers
- event listeners
- Buff/Skill
- weapon/projectile references
- visual transform
- trail/vfx
- lifecycle token

## 所有权

| 对象 | 主要所有者 |
|---|---|
| Friendly Unit | UnitRegistry |
| Enemy | EnemyManager |
| Boss | EnemyManager + BossManager |
| Weapon | WeaponManager |
| Projectile | ProjectileManager |
| Buff Handler | BuffManager/Factory |
| Skill | SkillManager |
| Card | DeckManager |

Manager 是唯一注册/注销入口。UI 和 View 不得直接销毁 Domain 对象。
