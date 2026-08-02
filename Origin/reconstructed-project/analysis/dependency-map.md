# 模块依赖图

## Round 04：ENEMY-RUNTIME-01

```text
BattleManager
  └─ EnemyManager [vi]
       ├─ EnemyFactory [s0 / ss]
       │    ├─ ObjectPool.classPool
       │    └─ Mob0Enemy [st]
       │         └─ NormalEnemyBase [pe]
       │              └─ EnemyBase [ro]
       │                   └─ EnemyEventProxy [qE]
       ├─ enemies: Map<runtimeId, EnemyBase>       [JS]
       ├─ cellToEnemyIds: Map<cellKey, Set<id>>    [mB]
       └─ enemyIdToCell: Map<id, cellKey>          [wB]

EnemyBase
  ├─ GameDataCore
  │    ├─ EnemyDataCore [sD]
  │    ├─ MapData [s4]
  │    │    ├─ AStarPathfinder [tl]
  │    │    ├─ AStarNode [ru]
  │    │    └─ AStarGrid [oS]
  │    └─ BattleState [uo]
  ├─ EventBus [oc / sS]
  ├─ Laya.timer
  ├─ BattleTarget (aDou compatibility contract)
  ├─ presentation/audio/effects/reward ports
  └─ EnemyFactory.recover → ObjectPool.classPool

Mob0Enemy
  ├─ ObjectPool.takeByKey("mob")
  ├─ resources/img/gameObject/enemy/mob_0.png
  └─ ObjectPool.recoverByKey("mob")
```

### 创建和复用依赖

```text
EnemyManager.spawn(0, lane)
  → ENEMY_TYPE_KEYS[0] = "Mob0"
  → EnemyFactory.create("Mob0")
  → ObjectPool.takeByClass(Mob0Enemy)
  → configure(dependencies)
  → Mob0Enemy.init(lane)
  → ObjectPool.takeByKey("mob")
  → EnemyBase.init
  → EventBus.ENEMY_REGISTERED
  → EnemyManager spatial index
```

回收路径：

```text
EnemyBase.gameOver
  → clear timers/listeners/path/state
  → EventBus.ENEMY_REMOVED
  → EnemyManager unindex/delete
  → EnemyFactory.recover(logic entity)
  → Mob0Enemy.gameOver
  → ObjectPool.recoverByKey("mob", visual)
```

原始 `st.gameOver` 在回收表现节点后仍保留该引用，以允许 `ro.move()` 在同一调用帧完成剩余网格检查。重建代码保留了这一反直觉顺序。

### 路径与固定更新

```text
GameLoop.frameLoop(1)
  → max delta 500ms
  → 80ms fixed substeps
  → EnemyManager.update(snapshot)
  → EnemyBase.update(deltaMs)
  → EnemyBase.move(deltaMs)
  → MapData.pathForSide(lane)
```

位移公式：

```text
position += normalizedDirection × moveSpeed × deltaMilliseconds / 1000
```

双方使用独立反向路线；A* 只允许上下左右四方向。

### 接触伤害与胜负入口

```text
EnemyBase pathIndex == path.length - 1
  → attackBattleTarget
  → 500ms cooldown
  → Laya.timer.once(50ms)
  → targetResolver(lane)
  → BattleTarget.receiveEnemyContact(1, enemy)
  → BattleState.playerHealth/opponentHealth -= 1
  → health == 0 时 EventBus.BATTLE_FINISHED
```

Mob0 不在该路径中搜索普通单位。当前空间索引的直接消费者是友军/武将攻击查询。

### 空间索引

```text
ENEMY_REGISTERED → _indexEnemy(id, enemy)
ENEMY_GRID_LEFT  → _indexEnemy(id, enemy) / cell update
ENEMY_REMOVED    → _unindexEnemy(id) → delete enemy
```

查询使用：

```text
cell candidates
→ side/targetable/dead filter
→ circle-vs-AABB intersection
→ DTO { id, x, y, Bm }
```

没有用全量遍历替换 `vi.qx` 的 Mob0 相关查询分支。

## Round 03：BOOT-TO-BATTLE（保持）

```text
GameBootstrap
  → LoadSceneController [s3]
  → SceneManager [q3 / sF]
  → MainSceneController [nA]
  → MatchSceneController [nd]
  → BattleFlowCoordinator [sE]
  → BattleManager [vU]
  → BattleSceneController [r5]
  → aDou × 2
  → EnemyManager / EnemyFactory / Mob0
```

固定更新注册顺序仍为：

```text
enemyMgr → BattleMgr → BattleScene
```

Round 04 没有修改启动顺序、MainScene 体力消耗、MatchScene 转场时序或 BattleFlow 调用顺序。

## Round 02：NET-01（保持）

```text
HttpClient
  ├─ SingletonBase
  ├─ Laya.HttpRequest
  ├─ Laya.Event.COMPLETE / ERROR
  ├─ Laya.timer
  └─ 晚绑定数据/事件/云存档接口
```

敌人运行时和开发模拟不导入 HttpClient，不访问真实 URL。

## CommonJS 静态依赖检查

- `src/` 本地 require 均可解析。
- 已重建 CommonJS require 图无循环。
- 敌人逻辑实体不在模块顶层解析 EnemyManager 单例；管理器通过事件和构造注入连接。
- `Mob0Enemy.resetIdsForTests()` 的延迟 require 只用于测试重置，不参与生产初始化。

## 下一轮建议依赖闭包：FRIENDLY-UNIT-COMBAT-01

```text
Unit drag/event base rb          24863–24930
Unit core rc                     22694–23112
Soldier base td                  23114–23437
Knife/Pike/Cavalry creators      24443–24834
Bow soldier ok                   26093–26406（后续方法延伸至约26506）
UnitRegistry vc                  29460–30476
BattleManager attack polling     50471–50519
EnemyManager.qx                  已于 Round 04 恢复
EnemyBase.hit                    已于 Round 04 恢复
```

该闭包的验收目标应是：创建一个真实基础士兵，按原攻击间隔通过 `vi.qx` 选择同阵营路径上的 Mob0，执行对应武器/动画事件或受控开发时钟结算伤害，并触发已恢复的敌人死亡/回收链。

## Round 05：FRIENDLY-UNIT-COMBAT-01

```text
GameObjectEventProxy [qE]
  → UnitDragBase [rb]
  → UnitBase [rc]
  → SoldierBase [td]
  → KnifeSoldier [tb.zx[0]]

UnitFactory
  → ObjectPool.takeByClass(KnifeSoldier)

DevelopmentUnitSpawner / formal placement caller
  → UnitRegistry.createSoldier
  → UnitFactory.createByIndex(0)
  → UnitBase.setPlacement
  → UnitBase.initialize
  → UnitRegistry Map register
  → UnitBase.activatePlacement

BattleManager._updateUnitAttacks
  → EnemyManager.queryTargets
  → SoldierBase state/cooldown
  → KnifeSoldier.attack
  → KnifeAttackTimeline.start
  → EnemyManager.getById
  → EnemyBase.hit
  → Round 04 enemy death / unindex / dual pooling
```

### 数据依赖

```text
FriendlyUnitConfig
  → base text table [刀, 弓, 枪, 骑]
  → range/damage/interval table
  → cumulative level multipliers

KnifeSoldier
  → grid width 80px from MapData contract
  → level-1 range 120px
  → level-1 damage 3
  → level-1 interval 0.8s
  → 500/playbackRate delayed settlement
```

### 更新和清理顺序

```text
GameLoop
  → BattleManager attack polling
  → UnitRegistry Map insertion order
  → EnemyManager spatial query

UnitRegistry.removeSoldier
  → pending attack cancel
  → unit.gameOver
  → GameLoop/timer/EventBus unregister
  → soldier visual key-pool recover
  → unit class-pool recover
  → Map delete
```

`PlacementReservationRegistry` 只参与格位冲突，不限制多个友军攻击同一 Mob0。刀兵固定站位，不依赖移动控制器。

### 暂缓的直接后继闭包

```text
BowSoldier ok
  → SimpleDynamicArrow rd
  → projectile base qY
  → movement descriptor on
  → BulletFactory vj/vk
  → attack effect manager vA
```

## Round 06：BOW-PROJECTILE-COMBAT-01

```text
UnitFactory [1 / 弓]
  → BowSoldier [ok → td]
  → UnitRegistry Map
  → BattleManager attack polling
  → EnemyManager.queryTargets(280px)
  → minimum remaining-path distance Bm
  → Laya.Event.STOPPED
  → HitEnemyStrategy [type 100]
  → TargetEnemyBezierMovement [pP/on]
  → ProjectileManager [vA / bulletMgr]
  → ProjectileFactory [vj/vk]
  → SimpleDynamicArrow [rd → qY]
  → EnemyBase.hit(2, BowSoldier)
  → Round 04 Mob0 death/unindex/pooling
```

ProjectileManager 使用活动 `Array`，按插入顺序保存、按索引从尾到头更新和删除。固定更新顺序为 `enemyMgr → bulletMgr → developmentAnimationDriver（开发）→ BattleMgr → BattleScene`。投射物不单独注册固定更新。

## Round 06 下一建议闭包：BATTLE-END-AND-GAMEOVER-CORE

```text
BattleTarget / BattleManager finish event
  → BattleFlowCoordinator.gameOver [sE]
  → GameDataCore.gameOver
  → BattleManager / EnemyManager / UnitRegistry / ProjectileManager cleanup
  → HttpClient.reportGameEnd（保持现有失败仅日志行为）
  → SceneManager.openScene("GameOverScene", { isWin, bj, round })
  → GameOverScene [nE]
  → SceneManager.closeScene("GameOverScene")
  → SceneManager.openScene("MainScene")
```

核心范围应继续使用平台无关端口。GameOverScene 内的广告、分享、排行和开放数据调用不是关闭对局生命周期的必要依赖，应在下一轮保持显式暂缓。


## ROUND-07A-SPEAR
- 恢复正式枪兵注册：index=2/text=枪/animationKey=pike。
- 来源：work/bundle.strings-decoded.js:24556-24749。
- 未恢复完整 sv/vA 枪击对象池依赖，保留到后续 Weapon/Bullet 阶段。

## ROUND-07B-CAVALRY
- ROUND-07B-CAVALRY: restored CavalrySoldier and cavalrySweep direct attack dependency.


## ROUND-07C-PROJECTILE-RUNTIME
Public projectile runtime extended with separate movement and hit strategy modules.

## Round 07D
Projectile registry dependencies updated.


Round 07E Weapon Foundation completed.

ROUND-07H repaired combat foundation dependencies: units -> combat effects -> enemy query -> damage.

## Round 07K

`BattleFlowCoordinator → BuffManager → BuffHandlerFactory → Number/State/Custom Handler → UnitBase/EnemyBase`。弓武器通过 `WeaponBuffPort` 注入 BuffManager。

## Round 07L additions

- BattleFlowCoordinator -> SkillManager, BossManager, WaveManager
- WaveManager -> EnemyManager, BossManager, GameDataCore, EventBus
- BossManager -> BossFactory, EnemyManager, EventBus
- BossBase -> EnemyBase, SkillManager, BuffManager contract
- SkillManager -> SkillFactory, SkillEffectPort, GameLoop
- SkillEffectPort -> BuffManager, EnemyManager, UnitRegistry, EventBus


## Round 07M additions
`BossBase -> SkillManager -> SkillAnimationTimeline -> SkillEffectPort -> BuffManager / UnitRegistry / EnemyManager / MapTileManager / DeadEntityRegistry / SkillPresentationPort`

`BATTLE_FINISHED -> BattleFlowCoordinator -> BattleResult -> GameOverSceneController -> MainScene`

## Round 08 playable flow

`DeckManager → BattleEconomy → BattleInputController → UnitFactory/UnitRegistry → WeaponManager → ProjectileManager`

`AIController → DeckManager/BattleInputController` (same creation path as player)

`BattleTarget/BattleState → GameEvents.BATTLE_FINISHED → BattleFlowCoordinator → BattleResult/GameOverScene`
