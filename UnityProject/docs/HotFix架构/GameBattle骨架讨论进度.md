# GameBattle 骨架讨论进度

> 状态：讨论中，尚未形成最终架构决策
>
> 日期：2026-08-04
>
> 参考：[架构设计.md](架构设计.md)、`Origin/reconstructed-project/src`

## 1. 讨论目标

基于 `D:/UnityProject/MyTEngine/TEngine/Origin/reconstructed-project/src` 的还原工程，确定 Unity 热更程序集 `GameBattle.dll` 的内部骨架、模块职责、单局生命周期、场景与对象池所有权，以及战斗 UI 与战斗状态之间的通信方式。

## 2. 已确认的约束

### 2.1 GameBattle 的范围

- 所有战斗逻辑归属 `GameBattle`；
- 所有战斗专属 UI 行为归属 `GameBattle`；
- `GameBattle` 可以依赖 `GameFUI`，不依赖 `GamePlay`；
- `GameBattle` 与 `GamePlay` 通过 `GameCommon` 中的不可变 DTO 和事实事件通信。

### 2.2 UI 与状态

- UI 不允许直接修改 `BattleState`；
- `BattleModule` 是战斗功能对外的唯一入口；
- 战斗 UI 的打开和关闭由 `BattleModule` 发起；
- UI 应通过公开查询、命令、只读快照和事件与战斗逻辑通信；
- 战斗规则不得反向依赖具体 UI 类型来直接刷新控件。

### 2.3 生命周期

- `BattleModule` 在热更模块注册阶段创建，热更域存续期间长期存在；
- TEngine `Module` 的最终释放入口是 `Shutdown()`，不是 `Dispose()`；
- 热更域释放时，`BattleModule.Shutdown()` 负责释放全部战斗资源和模块缓存；
- 单局结束不能关闭 `BattleModule`，只能执行单局清理或重开流程；
- “再来一局”和“退出到主界面”是两种不同的生命周期路径；
- 返回主界面时，地块对象不保留，应被销毁。

### 2.4 对象回收方向

当前设想如下，具体所有权仍需继续确认：

- 士兵对象进入对象池；
- 弹道对象进入对象池；
- 地块对象在退出主界面时销毁；
- 英雄和 Boss 对象允许直接清理；
- 缓存数据可以按退出路径清理；
- 再来一局时允许复用部分场景资源和对象池，并根据新配置重新组合战斗内容。

## 3. 还原工程的实际处理方式

### 3.1 长期对象

`bootstrap/DevelopmentBootstrap.js` 在启动阶段一次性创建或配置以下对象：

- 全局 `ObjectPool`；
- `BattleManager`；
- `BattleFlowCoordinator`；
- `EnemyManager`、`UnitRegistry`、`ProjectileManager`；
- `SkillManager`、`BuffManager`、`BossManager`、`WaveManager`；
- 其他战斗依赖和表现适配对象。

这些 Manager 和对象池不会为每局战斗重新创建。

### 3.2 开始战斗

`battle/BattleFlowCoordinator.js` 的 `startBattle()` 复用已有 Manager，依次调用各系统的 `startGame()`，然后打开 `BattleScene`，最后启动输入、AI 和焦点控制。

还原工程没有为每局创建独立的 `BattleSession`，而是在原有对象上重新初始化字段。

### 3.3 战斗结束

`BattleFlowCoordinator.cleanupBattle()` 依次调用各子系统的 `gameOver()`：

- `BattleState` 重置金币、生命、回合和布阵等字段；
- `EnemyManager` 回收敌人并清空空间索引；
- `UnitRegistry` 回收士兵并清空单位集合；
- `ProjectileManager` 回收活动弹道；
- Skill、Buff、Wave、Boss、输入、AI 和表现系统分别清理；
- `MapTileManager` 清除技能产生的地块阻塞标记；
- 全局 `ObjectPool` 不在单局结束时清空。

随后关闭 `BattleScene` 并打开 `GameOverScene`。

### 3.4 场景缓存

`core/SceneManager.js` 将已创建场景保存在 `scenes` 字典中。`closeScene()` 不会从字典移除场景；下一次打开同名场景时会复用原场景对象。

`GameOverSceneController` 中：

- 再来一局：`GameOverScene -> MatchScene -> BattleScene`；
- 返回主界面：`GameOverScene -> MainScene`；
- 后续再次进入：`MainScene -> MatchScene -> BattleScene`。

两条路径最终都会复用长期 Manager、全局对象池和缓存场景。还原工程没有为“返回主界面”实现独立的对象池彻底释放策略。

## 4. 已发现的架构风险

### 4.1 BattleModule 与 BattleManager 可能存在双重所有权

当前描述中，`BattleModule` 是公开入口，`BattleManager` 又负责创建 Scene、Actor 和 UI。如果两者都长期存在、都能启动和重置战斗，则以下职责可能重叠：

- 战斗生命周期状态转换；
- 当前战斗状态所有权；
- 场景和 UI 创建；
- 对象池创建与释放；
- 帧更新入口；
- 再来一局和退出主界面的清理策略。

后续必须确保每项职责只有一个拥有者。

### 4.2 Reset 容易遗漏状态

还原工程依赖所有系统的 `gameOver()` 正确清理字段、监听、计时器、异步任务和池对象。任意遗漏都可能污染下一局。

需要决定 Unity 实现是继续完全复用同一套可变状态，还是保留运行时和对象池、但每局创建新的单局状态容器。

### 4.3 Scene 与对象池生命周期尚未完全确定

如果对象池中的 `GameObject` 挂在 `BattleScene` 下，卸载场景会使池中引用失效。必须明确：

- 再来一局时 `BattleScene` 是否保持加载；
- 返回主界面时是否真正卸载 `BattleScene`；
- 士兵、弹道和特效池的根节点归属；
- 返回主界面时哪些池清空，哪些非场景资源缓存允许保留。

### 4.4 MVE 尚未形成正式定义

讨论中提出 `BattleModule` 内采用 MVE 架构，但当前还没有明确三个字母对应的角色、接口和依赖方向。

现有 HotFix 架构文档可以支持以下职责，但尚未把它正式命名为 MVE：

- Module：维护状态并提供查询、命令和快照；
- View：战斗专属 `FUIPanel` 和 Presenter；
- Event：Module 发送已经发生的事实事件，View 使用 `AddUIEvent` 监听。

需要继续确认这是否就是本项目所说的 MVE。

## 5. 当前候选骨架

以下只是待验证候选，不是已确认决策：

```text
BattleModule                 # 热更域长期存在，对外唯一入口
  └── BattleRuntime          # 进入战斗区域时创建
        ├── BattleScene
        ├── 战斗 UI
        ├── Actor 表现
        ├── 对象池
        └── BattleSession    # 每局状态和规则
```

候选生命周期：

```text
Idle
  -> Entering
  -> Running
  -> Settling
       ├── Restarting -> Running
       └── Exiting -> Idle
```

候选清理规则：

| 场景 | 候选处理 |
|---|---|
| 波次结束 | 回收本波 Actor 和 Projectile，保留场景及池 |
| 再来一局 | 清理单局状态并按新配置重组，复用可复用资源 |
| 返回主界面 | 关闭战斗 UI、取消任务和监听、销毁地块、清理场景对象池、卸载战斗场景 |
| 热更域释放 | `BattleModule.Shutdown()` 最终释放全部内容 |

## 6. 明天继续确认的问题

建议按以下顺序继续：

1. 正式定义 MVE 的 M、V、E 及其依赖方向；
2. 明确 `BattleModule` 与 `BattleManager` 各自存在的必要性；
3. 确认是否引入 `BattleRuntime` 和每局独立的 `BattleSession`；
4. 给 Scene、UI、Actor、逻辑状态和对象池分别指定唯一拥有者；
5. 完整定义“再来一局”和“退出主界面”的清理矩阵；
6. 明确异步加载失败、重复点击、战斗中强退和热更域关闭时的状态转换；
7. 在上述问题确定后，再落定 `GameBattle` 的目录、接口和初始化顺序。

