# BOOT-TO-BATTLE 真实调用图

## 正常路径

```text
original/index.js
  → Laya 配置与资源包预加载
  → Laya.init
  → Laya.Scene.open("scene/LoadScene.ls")
  → LoadSceneController.onAwake
  → GameLoop.init / 平台最小契约 / GameDataCore.init
  → BattleFlowCoordinator.init
  → SceneManager.openScene("MainScene")
  → MainSceneController.startGame
  → 扣除 5 点体力并打开 MatchScene
  → MatchSceneController.onOpened
  → 50ms 匹配完成标记
  → GameLoop 累计转场时间
  → MatchSceneController.enterBattle
  → BattleFlowCoordinator.startBattle
  → GameDataCore.startGame
  → BattleManager.startGame（注册 BattleMgr）
  → EnemyManager.startGame / UnitRegistry.startGame
  → SceneManager.openScene("BattleScene")
  → BattleSceneController.onAwake
  → AnimationEntityPool.create("aDou") × 2
  → BattleSceneController.onOpened（注册 BattleScene）
  → GameLoop 第一逻辑帧
  → BattleManager 进入第 1 波
  → 1,500ms 后 EnemyManager.spawn(Mob0, true/false)
```

## 分支

- 特殊启动条件：`LoadScene → MatchScene`，跳过 MainScene，但仍经过真实 Match/BattleFlow/BattleScene。
- 开发 `directBattle=true`：只绕过 Load/Main/Match，通过临时 `window.$_main_` 调用同一 `GameDataCore → BattleFlow → BattleManager → BattleScene`。
- 正式准备期为 10,000ms；开发测试可显式覆盖为 0ms。该覆盖位于开发适配器，不修改 BattleState 正式默认值。

## 关键证据节点

| 节点 | 原符号 | 原始范围 | 恢复状态 |
|---|---|---|---|
| GameBootstrap | `index.js IIFE` | `original/index.js:1-68` | CONFIRMED |
| Laya initialization | `Laya.init` | `original/index.js:51-67` | CONFIRMED |
| Open LoadScene | `startupScene` | `original/index.js:55-63` | CONFIRMED |
| LoadSceneController | `s3` | `bundle.strings-decoded.js:50996-51270` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| GameLoop | `pV / nx` | `bundle.strings-decoded.js:3769-3874; alias:11920` | COMPLETE_FOR_CRITICAL_PATH |
| GameDataCore | `tw / uq` | `bundle.strings-decoded.js:11561-11908; alias:13293` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| PlayerDataCore + StaminaServiceCore | `tY / p0 contracts` | `bundle.strings-decoded.js:8525-9429; 11436-11445` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| MapDataCore | `s4` | `bundle.strings-decoded.js:12483-12847` | COMPLETE_FOR_CRITICAL_PATH |
| EnemyDataCore | `sD` | `bundle.strings-decoded.js:11994-12175` | COMPLETE_FOR_CRITICAL_PATH |
| BattleState | `uo` | `bundle.strings-decoded.js:3163-3297` | COMPLETE_FOR_CRITICAL_PATH |
| BattleFlowCoordinator.init | `sE.init` | `bundle.strings-decoded.js:55027-55229` | COMPLETE_FOR_CRITICAL_PATH |
| SceneManager | `q3 / sF` | `bundle.strings-decoded.js:5715-5869; alias:13165` | COMPLETE_FOR_CRITICAL_PATH |
| MainSceneController.startGame | `nA.startGame` | `bundle.strings-decoded.js:64782-65947; startGame:65424-65443` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| MatchSceneController | `nd / oK` | `bundle.strings-decoded.js:60834-61284` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| BattleFlowCoordinator.startBattle | `sE.startGame` | `bundle.strings-decoded.js:55027-55229` | COMPLETE_FOR_CRITICAL_PATH |
| UnitRegistry | `vc` | `bundle.strings-decoded.js:29460-30477` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| EnemyFactory | `s0 / ss` | `bundle.strings-decoded.js:19220-19260` | COMPLETE_FOR_REGISTERED_TYPES |
| EnemyManager | `vi` | `bundle.strings-decoded.js:32939-33696` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| BattleManager | `vU` | `bundle.strings-decoded.js:50323-50534` | COMPLETE_FOR_BATTLE_ENTRY |
| AnimationEntityPool | `nz` | `bundle.strings-decoded.js:18534-18608` | COMPLETE_FOR_ADOU_CREATION |
| BattleSceneController | `r5` | `bundle.strings-decoded.js:57007-59129; Gq:58444-58490` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| two pooled aDou targets | `nz.$d("aDou") / uz` | `bundle.strings-decoded.js:18560-18570; 58444-58490` | PARTIAL_VISUAL_IMPLEMENTATION |
| BattleManager.update | `vU.update` | `bundle.strings-decoded.js:50323-50534` | COMPLETE_FOR_FIRST_WAVE |
| BattleSceneController.update | `r5.update / B$` | `bundle.strings-decoded.js:57007-59129` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| Mob0Enemy | `Mob0 concrete class` | `bundle.strings-decoded.js:31062-31114` | PARTIAL_CRITICAL_PATH_IMPLEMENTATION |
| first paired Mob0 spawn | `vU spawn pair` | `bundle.strings-decoded.js:50323-50534` | CONFIRMED |

## 进入战斗完成边界

1. `BattleScene` 已由真实 SceneManager 路径创建，并执行 `onAwake`、`onOpened`。
2. `BattleManager.startGame` 已注册 `BattleMgr`，状态进入等待准备。
3. `BattleScene.Gq` 的确定子集创建两个池化 `aDou`，分别挂到 `end1`、`end2`。
4. `BattleScene` 注册自己的逻辑更新，首帧可以执行。
5. 正式 10 秒准备期或开发显式 0 秒覆盖后，第 1 波初始化。
6. 1,500ms 生成间隔到达后，首对 `Mob0` 被创建并注册到 EnemyManager。
7. 退出时 `BattleMgr`、`BattleScene` 更新项和敌人对象被清理；全局 `enemyMgr` 更新注册保留，符合原初始化生命周期。
