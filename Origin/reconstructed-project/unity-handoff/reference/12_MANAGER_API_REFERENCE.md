# 管理器 API 参考

## BattleFlowCoordinator

- `init()`：初始化服务并注册 `BATTLE_FINISHED`。
- `startBattle()`：按正式顺序启动服务并打开 BattleScene。
- `cleanupBattle(isWin)`：全系统清理。
- `gameOver(isWin)`：构造 BattleResult、关 Battle、开 GameOver。

## BattleManager

- `startGame()`：初始化金币、时间、波次策略并注册 Tick。
- `update(deltaMs)`：推进波次状态与友军攻击。
- `gameOver()`：注销 Tick 并复位状态。

## WaveManager

- `planRound(round)`
- `beginRound(round)`
- `spawnNormalPair(...)`
- `gameOver()`

## Deck / Economy / Input

- `DeckManager.startGame/refresh/consume/lock`
- `BattleEconomy.payRecruit/payRefresh/award`
- `BattleInputController.execute/purchaseAndPlace/moveUnit`

## UnitRegistry

- `createUnit/createFromDescriptor`
- `register/place/reposition`
- `getUnit/allUnits/unitsBySide/unitsInRadius`
- `removeUnit/gameOver`

## EnemyManager

- `spawn/spawnByKey`
- `queryTargets/queryAroundEnemy`
- `applyDamage/forceRemove`
- `closestToEnd/randomTarget/lowestHealthTarget`
- `gameOver`

## BossManager

- `spawn`
- `count`
- `gameOver`

## Weapon/Projectile

- `WeaponManager.create/attach/equipDefault/remove/gameOver`
- `ProjectileManager.create/getById/remove/update/gameOver`

## Buff/Skill

- `BuffManager.applyBuff/modify/Jw/SE/update/gameOver`
- `SkillManager.attach/activate/activateBossSkill/update/removeOwner/gameOver`
