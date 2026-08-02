# BOOT-TO-BATTLE 行为规格

## 正式行为等价层

- `GameBootstrap` 复制 `index.js` 的配置应用、自动资源包、`Laya.init`、`window.$_main_` 优先级、启动场景和 Splash 顺序。
- `LoadSceneController` 保留平台预加载失败继续、登录失败继续使用本地数据、数据初始化和 Main/Match 分支。
- `SceneManager.openScene` 保留原方法无 Promise 返回值；另提供不改变生产调用的观察辅助 `openSceneAndWait`。
- `MainSceneController.startGame` 保留 5 点体力、5000ms 防抖、异步转场后打开 MatchScene；防抖分支无显式返回值。
- `MatchSceneController` 保留 50ms 匹配完成、无道具时 -1000ms 偏移、1500ms 阈值、进入战斗前暂停全局逻辑且不暂停 Laya timer。
- `BattleFlowCoordinator` 保留 GameData/BattleManager/EnemyManager/UnitManager 等调用顺序，并在 BattleScene 打开后启动 AI/输入/焦点契约。
- `BattleManager` 保留 20 初始金币、10秒准备期、1500ms 成对生成、5000ms 波间隔、原始波数表和 80ms GameLoop 子步长。
- `BattleSceneController.Gq` 保留两个 `aDou` 对象池创建、`sk` 名称、0.5/1 锚点、45/70 坐标和 end1/end2 父节点。
- `EnemyManager` 首轮根据 map0 的 enemyTypeIndex=0 创建双方 `Mob0`；未恢复类型明确报错。

## 开发模式

开发模式仅替代平台、网络、远程资源和缺失 `.ls` 节点来源。它不修改 `HttpClient`、SceneManager、BattleManager 或场景控制器的正式逻辑。

- 默认：`directBattle=false`，走 LoadScene → MainScene → MatchScene → BattleScene。
- `forceMatchLaunch=true`：走 LoadScene → MatchScene。
- `directBattle=true`：临时安装 `window.$_main_`，直接调用真实 GameData/BattleFlow/BattleManager/BattleScene 链。
- `developmentBattleStartDelayMs=0`：只在开发 pre-battle 服务中把本局 delayTime 覆盖为 0，便于首帧测试。
- 未实现调用、未注册敌人、缺失必要节点和工厂均明确抛错。

## 后续维护建议（本轮不实施）

1. 取得 `.ls` 后以真实序列化节点替换开发场景工厂。
2. 恢复 `uz`/`tm`，将 aDou 从开发 Sprite 替换为真实 Spine 目标与生命逻辑。
3. 恢复 `ro`/`pe` 敌人基类和路径系统，再启用真实攻击/胜负测试。
4. 在完整 UnitFactory 后恢复玩家招兵和首个可控单位。
