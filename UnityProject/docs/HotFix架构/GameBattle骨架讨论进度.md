# GameBattle 骨架讨论进度

> 状态：讨论中，尚未形成最终架构决策
>
> 日期：2026-08-04（初始）；2026-08-04 更新（第二轮，整合 61 模块移植可行性分析）
>
> 参考：[架构设计.md](架构设计.md)、`Origin/reconstructed-project/src`、移植模块清单 `E:\MyWork\MyTD\Doc\unity-minimal-battle-port-module-list.md`

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
5. 完整定义”再来一局”和”退出主界面”的清理矩阵；
6. 明确异步加载失败、重复点击、战斗中强退和热更域关闭时的状态转换；
7. 在上述问题确定后，再落定 `GameBattle` 的目录、接口和初始化顺序。

---

## 7. 第二轮讨论（2026-08-04：61 模块移植可行性 + 架构细化）

### 7.1 讨论背景

基于 `Origin/reconstructed-project/src` 的 61 个最简战斗闭环核心模块（清单见 `E:\MyWork\MyTD\Doc\unity-minimal-battle-port-module-list.md`），结合 Unity 工程实际架构审计结果，细化移植落地方案。本轮覆盖五个具体问题。

### 7.2 HotFix 模块注册入口

**问题**：`HotFixModules` 目前不存在，需要在 `GameLogic` 里找个地方注册 HotFix 模块。

**现状**：`GameApp.cs` 的 `StartGameLogic()` 当前只调 `GameModule.UI.ShowUIAsync<BattleMainUI>()`。`GameModule.cs` 是纯静态门面，只读 TEngine 框架模块。

**建议方案**：在 `GameLogic` 里新建 `HotFixModules.cs` 静态类，由 `GameApp.StartGameLogic()` 调用。

```csharp
// GameLogic.dll
public static class HotFixModules
{
    public static void Register()
    {
        // 按依赖顺序注册，顺序 = 初始化 + 更新顺序
        ModuleSystem.RegisterModule<IBattleModule>(new BattleModule());
        // 未来: ModuleSystem.RegisterModule<IBagModule>(new BagModule());
    }
}

// GameApp.cs
private static void StartGameLogic()
{
    HotFixModules.Register();
}
```

**理由**：`GameApp` 是热更入口，注册模块天然属于 `StartGameLogic()` 阶段。但 `GameApp` 不应直接堆注册代码——`HotFixModules` 独立承担注册编排，`GameApp` 只调一行。这与 `架构设计.md` 第 9 节的设计一致，只是尚未实现。

**状态**：已定。

### 7.3 BattleModule 与 BattleManager 的帧驱动职责

**问题**：BattleModule 做 `IUpdateModule.Update`，如何承载 80ms 子步拆分？

**现状**：TEngine `IUpdateModule.Update(float elapseSeconds, float realElapseSeconds)` 由 `RootModule.Update()` → `ModuleSystem.Update()` 每帧驱动，传入 `GameTime.deltaTime`（秒）。JS 端 `GameLoop` 用毫秒（`LOGIC_STEP_MS=80`），500ms 截断，`while` 拆步。

**建议方案**：`BattleModule`（`IUpdateModule`）持有一个 `BattleManager`。`BattleModule.Update` 只做一行 `_battleManager.Tick(elapseSeconds * 1000f)`（秒→毫秒），子步拆分在 `BattleManager.Tick` 内部。

```csharp
// GameBattle.dll
public class BattleModule : Module, IBattleModule, IUpdateModule
{
    private BattleManager _battleManager;

    public void Update(float elapseSeconds, float realElapseSeconds)
    {
        _battleManager?.Tick(elapseSeconds * 1000f);
    }
}

public class BattleManager
{
    private float _accumulator;
    private EnemyManager _enemyManager;
    private ProjectileManager _projectileManager;
    private AttackEffectManager _attackEffectManager;

    public void Tick(float deltaMs)
    {
        _accumulator += Math.Min(deltaMs, 500f); // 500ms 截断
        while (_accumulator >= 80f)
        {
            const float step = 80f;
            _enemyManager.Update(step);
            this.Update(step);            // 波次 + 攻击调度
            _projectileManager.Update(step);
            _attackEffectManager.Update(step);
            _accumulator -= step;
        }
    }
}
```

**关键差异**：JS 端各 manager `gameLoop.register` 自注册，GameLoop 循环回调。C# 端改为 `BattleManager.Tick` 主动调各 manager 的 `Update(step)`——少一层注册间接，更直接。

**单位转换注意**：TEngine 传秒，JS 用毫秒。`Tick` 入口乘 1000 转毫秒。

**状态**：已定。

### 7.4 EventBus 适配现有 Event 系统的评估

**问题**：61 模块内部的 EventBus（`event(type, ...args)` / `on(type, caller, listener)`）是否适配 TEngine GameEvent？工作量多大？

#### 7.4.1 GameEvent 实现审计

查了 TEngine `GameEvent` 实现：

- `EventDispatcher`：`Dictionary<int, EventDelegateData>` 查表，`Send` → `TryGetValue` + `d.Callback(arg1, arg2)` 直接调委托。无反射、无装箱（泛型版本）。
- 支持两种 key：`int eventType` 和 `string eventType`（经 `RuntimeId.ToRuntimeId(string)` 映射）。
- 支持 0-6 个泛型参数：`AddEventListener<T1,T2,...>(int, Action<T1,T2,...>)`。
- `caller` 绑定：JS 用 `callback.call(caller, ...args)`，TEngine 用闭包/lambda 捕获 `this`。

**GameEvent 开销不大**——Dictionary 查表 + 委托调用，和 JS EventBus 同级别。此前”避免 GameEvent 开销”的担心不成立。

#### 7.4.2 JS 事件清单

JS `GameEvents` 约 20-30 个常量（`BATTLE_FINISHED`/`ENEMY_REGISTERED`/`ENEMY_REMOVED`/`ENEMY_KILLED_BY`/`WAVE_PLANNED`/`ENEMY_SOUL_DELIVERED` 等），61 模块内约 50-80 处 `eventBus.event(...)` 调用点 + 40-60 处 `on(...)` 注册点。

#### 7.4.3 方案 (a)：全部迁到 TEngine GameEvent

| 工作项 | 量 | 难度 |
|---|---|---|
| 为每个事件定义 int 常量或 string→RuntimeId | 20-30 个 | 低，机械 |
| 61 模块 `eventBus.event(X, ...)` → `GameEvent.Send(X, ...)` | 约 50-80 处 | 中，逐个改签名 |
| 61 模块 `eventBus.on(X, this, handler)` → `GameEvent.AddEventListener<T>(X, handler)` | 约 40-60 处 | 中，caller 绑定改闭包 |
| 事件参数类型适配（JS 变长 `...args` → C# 强类型 T1-T6） | 每事件签名定一次 | 中，需逐个确认参数类型 |
| `off(type, caller, listener)` → `GameEvent.RemoveEventListener` | 约 40-60 处 | 中 |

**难点**：不在工作量，在参数类型确认——JS `eventBus.event(ENEMY_KILLED_BY, enemyId, attackerId, isPlayer)` 三个参数是 JS 动态类型，迁到 GameEvent 要定 `AddEventListener<int, int, bool>`。每个事件都需确认参数个数和类型。

**牵扯范围**：61 个核心模块全要改（EventBus 是内部通信方式）。out-of-scope 模块（skill/boss/AI 等）不受影响。

#### 7.4.4 方案 (b)：保留轻量 EventBus + 跨层桥接

| 工作项 | 量 | 难度 |
|---|---|---|
| 移植 JS EventBus 到 C#（`Dictionary<string, List<Delegate>>`） | 1 个类 | 低 |
| 61 模块内部调用点不动（签名兼容） | 0 | 无 |
| 跨层桥接：逻辑 EventBus 的 5-8 个关键事件 → `GameEvent.Send` 给 UI | 5-8 处 | 低 |

**代价**：两套事件系统并存。

#### 7.4.5 建议

**建议方案 (a) 全部迁到 TEngine GameEvent。**

理由：GameEvent 实现很轻（Dictionary + 委托，与 JS EventBus 同级），开销不是问题。既然开销不是问题，统一一套事件系统更干净，避免长期维护两套。工作量 100-140 处改动是机械活，可分 Phase 做。

**前置条件**：开工前先把 20-30 个事件的参数签名（个数 + 类型）列死，否则迁移时反复改类型会痛。

**状态**：已定（方案 a），前置条件待执行（事件签名表）。

### 7.5 骨架未决问题的建议

#### 7.5.1 BattleModule vs BattleManager 职责边界（原 §4.1）

**建议**：
- `BattleModule`（长期存在）：持有 `BattleManager`，负责 `OnInit` 创建 / `Shutdown` 销毁、`Update` 驱动 `Tick`、对外提供 `StartBattle` / `RestartBattle` / `ExitBattle` 命令。
- `BattleManager`（长期存在，单局复用）：波次推进、攻击调度、胜负判定。每局 `startGame()` 重置字段，`gameOver()` 清理。

每项职责只有一个拥有者：生命周期状态转换归 `BattleModule`，单局规则归 `BattleManager`，帧更新入口归 `BattleModule.Update → BattleManager.Tick`。

**状态**：已定。

#### 7.5.2 是否引入 BattleSession（原 §4.2 + §4.2 Reset 问题）

**建议：不引入 BattleSession。** 复用可变状态 + `gameOver()` 重置，与还原工程一致。

理由：还原工程没有 BattleSession，用的是”原有对象上重新初始化字段”。C# 端照搬，减少迁移偏差。引入 BattleSession 是额外抽象，最简版不需要。Reset 遗漏风险靠 `gameOver()` 链路完整性保证（JS 端已验证闭环）。

**状态**：已定（不引入）。

#### 7.5.3 对象池根节点归属（原 §4.3）

**建议：池中 GameObject 挂在 `DontDestroyOnLoad` 的 `BattleRoot` 节点下，不挂场景。**

理由：池节点不挂场景就不受场景卸载影响。`BattleRoot` 是 `BattleModule.OnInit` 创建的常驻节点。返回主界面时 `BattleModule` 主动调 `ObjectPool.ClearAll()` 清池。

清理矩阵（初步）：

| 场景 | 处理 |
|---|---|
| 波次结束 | 回收本波 Actor 和 Projectile，保留场景及池 |
| 再来一局 | `BattleManager.gameOver()` 重置单局状态，复用池和 `BattleRoot` |
| 返回主界面 | 关闭战斗 UI、取消监听、`ObjectPool.ClearAll()`、卸载战斗场景、`BattleRoot` 保留（空池） |
| 热更域释放 | `BattleModule.Shutdown()` 销毁 `BattleRoot` 及全部内容 |

**状态**：已定（BattleRoot 方案），清理矩阵中”返回主界面是否销毁 BattleRoot”待确认（当前建议保留空池 BattleRoot，下次进战斗复用）。

#### 7.5.4 MVE 命名（原 §4.4）

**建议：不正式命名 MVE。** 保持 `架构设计.md` 现有描述（Module / View / Event 职责已定义）。最简版用 Module + `UIWindow`/`FUIPanel` + `GameEvent` 三层即可。跑通后如果觉得需要正式命名再定。

**状态**：已定（暂不命名）。

#### 7.5.5 仍保持未定的问题

以下问题暂不阻塞最简闭环移植，留待跑通后迭代：

1. 异步加载失败、重复点击、战斗中强退和热更域关闭时的状态转换（原 §6.6）；
2. `BattleRuntime` 是否引入（当前建议不引入，`BattleModule` + `BattleManager` 两层够用）；
3. MVE 正式定义（原 §4.4，暂不命名）；
4. 返回主界面时 `BattleRoot` 是否销毁（当前建议保留空池）；
5. Scene 缓存策略是否复用还原工程的”closeScene 不移除”方案（Unity 场景机制不同，需单独定）。

### 7.6 移植架构定调

```
GameLogic (热更入口)
  ├─ GameApp.StartGameLogic() → HotFixModules.Register()
  └─ HotFixModules.cs                    # 新建，注册编排
       └─ ModuleSystem.RegisterModule<IBattleModule>(new BattleModule())

GameBattle (61 模块 + 战斗 UI)
  ├─ BattleModule.cs                     # : Module, IBattleModule, IUpdateModule
  │    └─ Update() → _battleManager.Tick(deltaMs)
  ├─ BattleManager.cs                    # Tick: 80ms 子步拆分 → 各 manager.Update(step)
  ├─ Core/                               # EventBus→GameEvent 适配 / ObjectPool / MathRandom / ...
  ├─ Data/                               # CriticalGameState 等，读 ConfigSystem.Instance.Tables.Tb*
  ├─ Battle/                             # WaveManager / EnemyManager / MapData / BattleState / ...
  ├─ Entities/                           # EnemyBase / Mob0Enemy / BattleTarget / ...
  ├─ Units/                              # SoldierBase / 4兵 / UnitRegistry / ...
  ├─ Combat/                             # AttackEffectManager / AttackScheduler / ...
  ├─ Projectiles/                        # ProjectileManager / SimpleDynamicArrow / ...
  ├─ Deck/                               # DeckManager / DeckDefinitions / ...
  ├─ Input/                              # BattleInputController / ...
  ├─ Ports/                              # UnityPort 实现 (CombatClock/Random/View/Audio桩/Vfx桩)
  └─ UI/                                 # 战斗专属 UIWindow / FUIPanel

GameProto (已存在)
  └─ ConfigSystem.Instance.Tables.Tb*    # 61 模块读这里（Luban 配置）
```

### 7.7 关键决策汇总

| # | 问题 | 决策 | 状态 |
|---|---|---|---|
| 1 | HotFix 模块注册入口 | 新建 `HotFixModules.cs`，`GameApp.StartGameLogic()` 调用 | 已定 |
| 2 | 80ms 子步驱动 | `BattleModule.Update` → `BattleManager.Tick`（秒→毫秒，500ms 截断，80ms 拆步） | 已定 |
| 3 | EventBus 适配 | 全部迁到 TEngine GameEvent（方案 a）；前置：事件签名表 | 已定 |
| 4 | GameEvent 开销 | 实现很轻（Dictionary + 委托），开销不是问题 | 已定 |
| 5 | BattleModule vs BattleManager | BattleModule=门面+生命周期+帧入口；BattleManager=单局规则+Tick 拆步 | 已定 |
| 6 | BattleSession | 不引入，复用可变状态 + gameOver() 重置 | 已定 |
| 7 | 对象池根节点 | `DontDestroyOnLoad` 的 `BattleRoot`，不挂场景 | 已定 |
| 8 | MVE 命名 | 暂不正式命名 | 已定 |
| 9 | 配置数据源 | Luban `Tb*` 表（需导 xlsx）；maps.json grid 格式需验证 | 已定 |
| 10 | 逻辑/表现分离 | 逻辑纯数据字段 + 表现层每帧 sync Transform | 已定 |
| 11 | port 桩 complete 回调 | Unity 桩必须调 complete（死亡链依赖） | 已定 |

### 7.8 开工前待验证项

1. **事件签名表**：列出 20-30 个 JS GameEvents 的参数个数 + 类型，作为 GameEvent 迁移基线。
2. **Luban TbMap grid 字段**：确认 `TbMap` 的 grid 字段类型和维度是否与 JS `maps.json` 的 `string[][]`（`”kind_lane”`）兼容。如不兼容需调整 Luban schema 或 MapData 消费方式。
3. **asmdef + 热更注册**：`GameBattle.asmdef` 加引用（GameProto/TEngine.Runtime/UniTask/GameCommon/GameFUI）；`UpdateSetting.asset` + `HybridCLRSettings.asset` 加 `GameBattle.dll`。
4. **maps/waves/enemies/units/deck-pool JSON → xlsx**：导出为 Luban 可消费的 xlsx 格式。

