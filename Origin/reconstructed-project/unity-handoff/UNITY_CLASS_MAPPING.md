# Unity Class Mapping

| JavaScript | Unity recommendation | Kind |
|---|---|---|
| BattleState | `BattleState` | plain C# class |
| BattleManager | `BattleController` | plain class driven by CombatTick |
| WaveManager | `WaveService` | plain class |
| BattleEconomy | `BattleEconomy` | plain class |
| DeckManager | `DeckService` | plain class |
| BattleInputController | `BattleCommandHandler` | plain class |
| AIController | `OpponentAI` | plain class |
| Unit/Enemy/Boss definitions | ScriptableObject or JSON | data |
| UnitRegistry/EnemyManager | runtime repositories | plain classes |
| ProjectileManager | projectile simulation service | plain class |
| Weapon/Buff/Skill managers | domain services | plain classes |
| BattleScene presentation | `BattleView` | MonoBehaviour |
| Unit/Enemy/Boss visuals | prefab presenters | MonoBehaviour |
| Spine timelines | Spine-Unity adapters | MonoBehaviour/adapter |
| Input adapter | `UnityBattleInputAdapter` | MonoBehaviour |
| Scene adapter | Unity scene service | adapter |

Do not convert every domain object to MonoBehaviour. Keep fixed combat simulation independent of Unity `FixedUpdate`; call it from a dedicated accumulator using the recovered 80ms step and 500ms cap.
