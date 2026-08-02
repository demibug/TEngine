# “成功进入战斗”完成边界

本轮不把“打开 BattleScene”当作完成。以下条件已经由源码和测试共同验证：

| 条件 | 结果 | 证据 |
|---|---|---|
| BattleScene 实例创建 | CONFIRMED | `SceneManager → Laya.Scene.open("scene/BattleScene.ls")` |
| BattleScene 生命周期执行 | CONFIRMED | `onAwake`、`onOpened` 测试顺序 |
| BattleManager 已绑定战斗数据 | CONFIRMED | `BattleFlow.init → BattleManager.init` |
| 战斗状态已进入等待/生成 | CONFIRMED | `BattleManager.startGame` 置 `WAITING_TO_START`；首帧可置 `SPAWNING` |
| 双方首个战斗目标创建 | CONFIRMED | `Gq` 创建两个 `nz.$d("aDou")`，分别加入 `end1/end2` |
| 波次/敌人配置初始化 | CONFIRMED | 首帧 `currentRound=1`、读取原始波数表和 map enemyTypeIndex |
| 更新循环注册 | CONFIRMED | `enemyMgr`、`BattleMgr`、`BattleScene` 三个 GameLoop 键 |
| 第一帧无未定义依赖错误 | CONFIRMED_IN_MOCK | Laya mock 下完整执行 |
| 暂停/结束/退出监听 | PARTIAL_CONFIRMED | BattleScene 暂停入口和 game-over 事件已恢复；PauseDialog 暂缓 |
| 战斗启动状态可观测 | CONFIRMED | BattleManager.started、BattleScene.battleStarted、匹配准备标记 |
| 首对普通敌人可创建 | CONFIRMED | 准备期结束后 1,500ms 创建双方 Mob0 |

## 与“首批战斗对象”的精确定义

原代码没有在 BattleScene 初始化时自动创建玩家士兵或武将。最先创建的是双方各一个 `aDou` 骨骼动画对象。UnitRegistry 此时为空。普通敌人由 BattleManager 的波次循环在准备期结束后成对创建。

## 正式与开发时间差异

- 正式默认：准备期 `10,000ms`，除非双方放置完成条件提前满足。
- 开发测试：`DevelopmentBattleTimingOverride` 可显式设为 `0ms`。
- 该覆盖不修改 `BattleState` 正式默认值，也不进入正式场景代码。
