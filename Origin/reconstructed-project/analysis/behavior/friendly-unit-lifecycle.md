# 友军基础单位生命周期

## 构造期（仅首次 new）

`UnitBase` 创建等级、经验、前后容器与格位、放置 Tween 临时数据、Buff 容器、攻击基础字段和状态字段；`SoldierBase` 增加攻击修正、攻击范围修正、攻速修正、播放倍率、上次攻击时间和目标数组；`KnifeSoldier` 固定表现键为 `knife`。

## 每次从池中启用

1. `UnitFactory` 按索引或文字键从类池获取正式类。
2. 注入 Laya、GameData、GameLoop、EventBus、ObjectPool、表现、音频、EnemyManager 和攻击时间线。
3. `UnitRegistry` 先调用 `setPlacement`，再调用 `initialize`。
4. `initialize` 从 `soldier` 表现池取得节点，取得名为 `lvl` 的等级节点，分配运行时 ID。
5. `SoldierBase.initializeUnit` 读取正式配置，设置伤害、范围、攻击间隔和动画键。
6. `UnitRegistry` 按 Map 插入顺序登记到 `soldiers/PA`。
7. 通过正式放置入口设置父节点、像素坐标和激活状态。
8. BattleManager 之后可在固定更新中轮询该单位。

## 战斗状态

- `UnitIdle`：固定站位，等待 BattleManager 查询目标。
- `UnitAttack`：满足冷却后由 BattleManager 进入；刀兵再次查询并创建延迟攻击。
- 源码没有友军移动追击状态，也没有生命值、受击或死亡状态。

## 清理与回收

`UnitRegistry.removeSoldier → UnitBase/SoldierBase.gameOver`：触发 `onDestroy`、注销 GameLoop 键、清除 Laya timer、移除 EventBus 调用者、移除并复位表现、回收 `soldier` 表现、复位逻辑字段、回收逻辑类。旧生命周期的延迟攻击由 `clearAll(unit)` 和生命周期代号共同隔离。
