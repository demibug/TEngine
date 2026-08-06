# 最简战斗闭环 — Unity 移植模块清单

> 生成时间：2026-08-04
> 来源工程：`E:\MyWork\MyTD\TEngine\Origin\reconstructed-project\src`
> 前置条件：OpenSpec change `minimal-battle-loop-gap-fix` 已完成（3 个 BLOCKING 缺口已补全）
> 移植目标：Unity Project（`E:\MyWork\MyTD\TEngine\UnityProject`）
> 移植粒度：~~61 个核心逻辑模块~~ → 正式口径为 **59 个真实 JS 源文件**（译为 C#）+ **7 个 Unity 新建项**单独统计 + 表现层 sync；“61”为历史标题口径，非正式计数（见下冻结声明与总纲 12.0.1）
>
> **冻结状态（2026-08-05，OpenSpec task 1.1 / 1.2）**：本文为历史模块清单，原“61 模块”为**历史标题口径**（非正式迁移计数）。经重新枚举还原工程 `src` 核对，实际可定位并纳入迁移审计的 JS 源文件为 59 个，Phase 计数 7+3+11+5+9+7+12+5=59 复核一致，59 路径全部存在。正式迁移清单与逐项 Keep/Merge/Delete/Replace/Defer 冻结处置及证据链接以 `战斗移植设计总纲.md` 第 12.0 节为权威；本文 Phase 0～6 表格保留为讨论基线，与总纲第 12.0 节冲突时以总纲为准。**7 个 Unity 新建项单独统计**（task 1.2 冻结，按 design.md 确定为：BattleModule、IBattleViewPort/AudioPort/VfxPort 的 Unity 实现、BattleViewRegistry、BattleViewSynchronizer、BattleInputAdapter；原 CombatClockPort 取消归 BattleSimulation、原 RandomSourcePort 归入 59 manifest #6、原 Bootstrapper 改为 BattleModule，见总纲 12.0.1）。**禁止用空壳类补数**：59 JS 与 7 Unity 新建项均须为真实有职责类型，不得为凑“61”或任何口径创建无行为空壳类。

---

## 移植范围说明

本清单覆盖"最简战斗闭环"所需的全部 **59 个 JS 纯逻辑模块**（原标题称“61 个”为历史标题口径，实际可定位 JS 源文件为 59 个，Phase 计数 7+3+11+5+9+7+12+5=59 复核一致；7 个 Unity 新建项单独统计，见 Phase 7）。这些模块在 `minimal-battle-loop-gap-fix` change 完成后即为经过验证的可移植基线。

**不在本清单内**（后续版本补）：
- Skill 全系列（SkillEffectPort / SkillManager / 6 个 effect 类 / skill presentation）
- Boss 全系列（BossFactory / BossManager / BossBase / BossDefinitions）
- AI 控制器（AIController / AIDeploymentController / AIPlanningController / AITemplateResolver）
- Generals 全系列（GeneralPart / GeneralFactory / GeneralUnit / GeneralDefinitions）
- 特殊武器 effect（WeaponSpecialEffects / meteor / snake bullet）
- 多弹种投射物（22 个非 SimpleDynamicArrow 弹种 + 多余移动策略）
- 农民 / 铲子
- 表现层 VFX / 音频实体 / Spine 动画
- 平台 / 网络 / 商店 / 排行 / 场景链（Load/Main/Match）

---

## 移植时的 6 处裁剪

以下裁剪在 C# 译写时执行，**不修改 JS 源码**：

| # | JS 模块 | 裁剪动作 | 理由 |
|---|---|---|---|
| 1 | `projectiles/ProjectileFactory` | 删 22 个多余弹种 require + 注册，只保留 SimpleDynamicArrow | 最简版只用一种弹种 |
| 2 | `units/UnitRegistry` | 删 `require('../generals/GeneralFactory')`，改注入 dummy 或 null-guard | 去武将依赖 |
| 3 | `data/BattleDataCore` | `resolveBossStats` 懒 require skills/bosses 不调 | 死代码路径，Boss 已跳过 |
| 4 | `projectiles/ProjectileBase` | 内联 BuffType 2 常量(BURN_STATIC=14, KNOCKDOWN=12)或移植 34 行 BuffTypes 枚举 | 去 buffs 依赖 |
| 5 | `battle/WaveManager` | 使用 skipBoss=true 模式 | 跳 Boss |
| 6 | `deck/DeckManager` | 使用 minimalMode=true 模式，不译 drawCardNoRepeat/copyGeneralChars | 只要普通抽取 |

---

## 数据源（JSON → Unity TextAsset）

以下 5 个 JSON 从 `unity-export/config/` 拷入 Unity 作为 TextAsset，C# 反序列化消费：

| 文件 | 内容 | 消费者 |
|---|---|---|
| `maps.json` | 4 张地图：grid(8×10 `"kind_lane"` 矩阵) + opponentPath/playerPath(预算 waypoint) + start/end 点 | MapData |
| `waves.json` | 20 波：waveUnitCounts[20] + bossWaveNumbers[6] + bossSpawnChances[6] + spawnStrategyWeights + spawnStrategies | WaveManager |
| `enemies.json` | 敌人基础数值：healthByWave(20 波) + typeIndex + speed + healthModifier | BattleDataCore/EnemyBase |
| `units.json` | 4 种兵：rangeCells/attackDamage/interval + 等级倍率 + experienceThresholds | UnitConfig |
| `deck-pool.json` | 108 元素牌池（最简版用 BASE_POOL=['刀','弓','枪','骑'] fallback 可选不加载） | DeckDefinitions |

---

## Phase 0 — 核心地基（7 模块）

| # | JS 模块路径 | 作用 | 移植意图 |
|---|---|---|---|
| 1 | `core/SingletonBase.js` | 单例基类，提供 `instance()` 静态获取 | C# 逻辑层单例模式基础，所有 Manager 类继承它 |
| 2 | `core/EventBus.js` | 事件总线 + GameEvents 常量枚举（WAVE_STARTED/ENEMY_KILLED/BATTLE_FINISHED 等） | 模块间解耦通信的核心通道，C# 用 `Dictionary<string, List<Action>>` 实现 |
| 3 | `core/GameLoop.js` | 全局逻辑更新循环：80ms 子步推进，500ms 截断，register/unregister 回调 | 战斗逻辑的"心跳"——所有 update 都由它驱动。C# 用 `Time.deltaTime` 累加 + `while` 拆 80ms 步 |
| 4 | `core/ObjectPool.js` | 泛型对象池：`takeByClass<T>()` / `recoverByClass<T>(obj)`，按 class 分桶 | 敌人/士兵/投射物/攻击效果的复用池，避免频繁 GC。C# 用 `Dictionary<Type, Stack<object>>` |
| 5 | `core/GameObjectEventProxy.js` | 事件转发基类，持有 eventTarget 引用，提供 `event(name,...args)` 转发到 EventBus | EnemyEventProxy 和 UnitDragBase 的父类，统一事件转发入口 |
| 6 | `core/MathRandom.js` | 随机数工具：`range(min,max)` / `weightedIndex(weights)` | 刷怪策略权重选择 + 特殊生成索引。C# 可直接用 UnityEngine.Random 封装 |
| 7 | `core/PlacementReservationRegistry.js` | 放置预留注册表：`add(side,x,y)` / `delete` / `has` / `clear` | 记录格子占用状态，UnitRegistry 放置时检查、回收时释放 |

---

## Phase 1 — 数据层（3 模块）

| # | JS 模块路径 | 作用 | 移植意图 |
|---|---|---|---|
| 8 | `data/CriticalGameState.js` (GameDataCore) | 聚合根：聚合 map/enemy/battle/player/friendlyUnits 数据，提供 `allocateRuntimeId()` / `resolveEnemyStats()` | 全局游戏数据入口，所有模块通过 gameData 访问配置。C# 作为中心数据容器 |
| 9 | `data/PlayerDataCore.js` | 玩家数据：mapIndex / round / roundDay / startGame() | 记录当前地图和波次进度。最简版 stamina/props/rank 是桩不阻塞 |
| 10 | `data/BattleDataCore.js` | 战斗数据：EnemyDataCore（waveUnitCounts/bossWaveNumbers/enemy health 表）+ MapDataCore（=MapData 别名） | 波次配置和敌人数值的数据源。裁剪 resolveBossStats 死代码路径 |

---

## Phase 2 — 地图 + 战斗管理（11 模块）

| # | JS 模块路径 | 作用 | 移植意图 |
|---|---|---|---|
| 11 | `battle/MapData.js` | 地图数据 + A* 寻路：`changeMap(index)` / `pathForSide(side)` / `isBuildableForSide(side,x,y)` / `blockAt(x,y)` | 地图空间结构：路径（敌人走）+ 格子（玩家放兵）。最简版直接读 opponentPath，A* 可选 |
| 12 | `battle/BattleState.js` | 战斗状态：HP / gold / round / `playerHealth≤0 → BATTLE_FINISHED(false)` | 阿斗血量和胜负判定的核心状态容器 |
| 13 | `battle/EnemyFactory.js` | 敌人工厂：`registerPooledClass(Mob0)` / `create(type)` / `recover(enemy)` 经 ObjectPool | 敌人对象的创建和回收入口，只注册 Mob0 |
| 14 | `battle/EnemyManager.js` | 敌人注册表 + 空间网格：`queryTargets()` / `queryEnemyObjects()` / `spawn()` / `applyDamage()` / `gameOver()` | 敌人全局管理：查询（给士兵找目标）、生成、受伤、清理。AttackScheduler/AttackResolver 依赖它 |
| 15 | `battle/BattleEconomy.js` | 战斗经济：`payRecruit()` / `payRefresh()` / `award()` / `balance` | 金币系统：放兵扣金币、击杀奖金币。DeckManager 和 BattleInputController 依赖它 |
| 16 | `battle/WaveManager.js` | 波次计划器：`planRound(round)` / `beginRound()` / `startGame()` / 20 波配置 | 按波次生成敌人。最简版 skipBoss=true 跳过所有 Boss 波 |
| 17 | `battle/DeadEntityRegistry.js` | 死亡记录：`recordEnemy()` / `consume()` / `clear()` | 记录死亡敌人供经验/奖励分发。EnemyBase._beginDeath 调用 |
| 18 | `battle/BattleResult.js` | 战斗结果：`fromRuntime()` 计算胜负/星级/统计 | 胜负判定结果对象，供 UI 或日志消费 |
| 19 | `battle/CombatServices.js` | 服务容器：`require(name)` / `snapshot()` 命名服务注册 | 编排根的依赖容器，按名称存取服务实例 |
| 20 | `battle/CombatLifecycle.js` | 编排器：`start()` / `gameOver()` 按 START_ORDER/CLEANUP_ORDER 调度 | 轻量编排根（不用 BattleFlowCoordinator，后者硬依赖 skill/boss/AI/network） |
| 21 | `battle/BattleManager.js` | 波次状态机：`update()` → AttackScheduler + AttackEffectManager / `startGame()` / `gameOver()` | 战斗主循环驱动器，每子步调度攻击和效果更新 |

---

## Phase 3 — 实体（5 模块）→ 第一个可跑验证里程碑

| # | JS 模块路径 | 作用 | 移植意图 |
|---|---|---|---|
| 22 | `entities/EnemyEventProxy.js` | 敌人事件代理（extends GameObjectEventProxy） | 敌人事件转发的薄层 |
| 23 | `entities/EnemyBase.js` | 敌人基类：path 移动（沿 waypoint 逐点直线）、`hit()`、`attackBattleTarget()`、`_beginDeath()` → recover、grid membership | 敌人核心逻辑：怎么走、怎么打阿斗、怎么死。soul/blowup DEFERRED 桩不阻塞 |
| 24 | `entities/NormalEnemyBase.js` | Mob0 基类：stats init、death 动画占位；soul/blowup 默认桩 | Mob0 的属性初始化和死亡流程。sB/Xw/Gw 默认不触发 |
| 25 | `entities/Mob0Enemy.js` | Mob0 具体类：typeIndex 0、visualPoolKey 'mob' | 最基础敌人类型，最简版唯一敌人 |
| 26 | `entities/BattleTarget.js` | 阿斗：`receiveEnemyContact()` → BattleState HP-- / `alive` getter / `bindBattleTarget()` | 被攻击目标，敌人到路径末点打它，血量归零判负。视觉对象改用 Unity GameObject |

**验证里程碑**：敌人沿 opponentPath 从 (7,1) 走到 (0,0)，到达后打阿斗，阿斗 HP 递减。

---

## Phase 4 — 战斗效果 + 投射物（16 模块）

### 4a. 战斗效果（9 模块）

| # | JS 模块路径 | 作用 | 移植意图 |
|---|---|---|---|
| 27 | `combat/AttackResolver.js` | 目标查询包装：`queryTargets()` / `queryEnemyObjects()` / `hit()` 封装 EnemyManager | AttackScheduler 和 MeleeAttackEffect 通过它查目标和扣血，隔离 EnemyManager 直接依赖 |
| 28 | `combat/AttackScheduler.js` | 每子步攻击分派：cooldown 检查 + 目标查询 + `unit.attack()` 调用 | 驱动所有单位"谁该攻击了"的调度器，BattleManager.update 每子步调它 |
| 29 | `combat/AttackEffectManager.js` | 效果池生命周期：`create()` / `add()` / `update()` / `cancelOwner()` / `gameOver()` | 统一管理所有攻击效果（近战延迟/投射物）的创建、更新、回收 |
| 30 | `combat/MeleeAttackEffect.js` | 近战延迟命中基类：到期 `hit()` 结算范围内目标 | PikeAttackEffect 和 CavalrySweepEffect 的父类 |
| 31 | `combat/KnifeAttackEffect.js` | 刀兵效果：usesTimer 模式（Laya.timer 500ms 精确触发）或管理器驱动 fallback | 刀兵的攻击效果对象，注册到 AttackEffectManager |
| 32 | `combat/KnifeAttackTimeline.js` | 刀兵 500ms 时序：`start()` / `resolve()` / `cancel()`，Laya.timer 驱动 | 刀兵命中的精确时机控制。C# 改用协程或逻辑步累计计时 |
| 33 | `combat/PikeAttackEffect.js` | 枪兵 360ms 命中：extends MeleeAttackEffect，`/播放倍率` 延迟 | 枪兵的攻击效果，命中时机按原版动画事件契约 |
| 34 | `combat/CavalrySweepEffect.js` | 骑兵 150ms 双扫：extends MeleeAttackEffect，两次横扫各半攻击力 | 骑兵的攻击效果，双次范围伤害 |
| 35 | `combat/ProjectileAttackEffect.js` | 弓兵投射效果：创建投射物、跟踪活跃、清理 | 弓兵攻击时创建 SimpleDynamicArrow 并登记到 ProjectileManager |

### 4b. 投射物（7 模块）

| # | JS 模块路径 | 作用 | 移植意图 |
|---|---|---|---|
| 36 | `projectiles/ProjectileMath.js` | 数学工具：`distance()` / `displayAngle()` / `quadraticBezier()` / `quadraticTangentDegrees()` | 投射物移动和旋转的角度/距离计算 |
| 37 | `projectiles/ProjectileBase.js` | 投射物基类：`resetData()` / `fire()` / `update()` / `hit()` / `recover()` + impact 效果 | 所有投射物的生命周期基础。裁剪 BuffType 依赖（内联 2 常量） |
| 38 | `projectiles/HitEnemyStrategy.js` | 单体命中策略：命中后 `requestRemove` | SimpleDynamicArrow 使用的命中策略——碰到敌人就扣血并移除 |
| 39 | `projectiles/TargetEnemyBezierMovement.js` | 贝塞尔追踪移动：`attach()` / `onFire()` / `update()` 追踪目标敌人 | SimpleDynamicArrow 的移动方式——沿贝塞尔曲线飞向目标 |
| 40 | `projectiles/SimpleDynamicArrow.js` | 直飞箭：extends ProjectileBase，`initializeVisual()` / `applyHit()` / `onRecover()` | 唯一 COMPLETE 弹种，弓兵的箭矢。最简版唯一投射物 |
| 41 | `projectiles/ProjectileFactory.js` | 投射物工厂：`produce()` / `recover()` 经 ObjectPool | 投射物创建回收入口。**裁剪——只注册 SimpleDynamicArrow** |
| 42 | `projectiles/ProjectileManager.js` | 投射物管理：活跃列表、`update()` 循环、命中策略、离场移除、`gameOver()` | 全局投射物更新和清理。ProjectileAttackEffect 创建的投射物由它驱动 |

**验证里程碑**：AttackEffectManager 效果生命周期正确，SimpleDynamicArrow 追踪命中敌人扣血。

---

## Phase 5 — 单位（12 模块）

| # | JS 模块路径 | 作用 | 移植意图 |
|---|---|---|---|
| 43 | `units/UnitDragBase.js` | 拖拽基类（extends GameObjectEventProxy）：press/threshold/release hook | 拖拽交互的基础，处理按下/拖动/释放。C# 改用 Unity Input 系统 |
| 44 | `units/UnitBase.js` | 单位基类（extends UnitDragBase）：placement / init / gameOver / reset / pool recover / levelUp | 所有单位的生命周期基础。UnitContainerType 枚举在此 |
| 45 | `units/SoldierBase.js` | 士兵基类（extends UnitBase）：attackDamage/range/interval / `initializeUnit()` / `configure()` | 4 种兵的父类，定义攻击力和攻速。receiveDamage throw 是 by design（友军无受击契约） |
| 46 | `units/UnitConfig.js` | 兵种数值：刀/弓/枪/骑 stats（rangeCells/attackDamage/interval）+ 等级倍率 + experienceThresholds | 4 种兵的战斗数值定义，SoldierBase 读取它初始化属性 |
| 47 | `units/KnifeSoldier.js` | 刀兵：`performKnifeAttack()` via KnifeAttackTimeline | 近战刀兵，500ms 延迟命中 |
| 48 | `units/BowSoldier.js` | 弓兵：`selectTarget()` / `attack()` / `launchArrow()` via ProjectileAttackEffect + SimpleDynamicArrow | 远程弓兵，发射追踪箭矢 |
| 49 | `units/SpearSoldier.js` | 枪兵：`attack()` via PikeAttackEffect | 近战枪兵，360ms 延迟命中 |
| 50 | `units/CavalrySoldier.js` | 骑兵：`attack()` via CavalrySweepEffect（双扫） | 近战骑兵，150ms 双次范围伤害 |
| 51 | `units/UnitFactory.js` | 单位工厂：注册 4 种兵 / `createByText()` / pool via ObjectPool | 按牌面文字创建对应士兵。非 4 种兵 throw（by design） |
| 52 | `units/UnitLevelService.js` | 升级服务：`canUpgrade()` / `upgrade()` levelUp | 士兵升级逻辑，BattleInputController 构造要求（即使最简版不升级也要注入） |
| 53 | `units/UnitMergeService.js` | 合并服务：`canMerge()` / `merge()` / `swap()` 同字士兵 levelUp | 士兵合并逻辑，BattleInputController 构造要求。最简版不合并但需注入 |
| 54 | `units/UnitRegistry.js` | 单位注册表：`createUnit()` / `createFromDescriptor()` / `register()` / `place()` / `removeSoldier()` / `gameOver()` | 我方单位全局管理：创建、放置、注册、移除、清理。**裁剪——删 GeneralFactory require** |

**验证里程碑**：放兵、兵打敌人、敌人死、入池（4 种兵各放一个，确认攻击结算+死亡回收+ObjectPool 往返）。

---

## Phase 6 — 牌组 + 输入（5 模块）

| # | JS 模块路径 | 作用 | 移植意图 |
|---|---|---|---|
| 55 | `deck/UnitCard.js` | 牌对象：id/text/level/cost/source/locked | 手牌中一张牌的数据结构 |
| 56 | `deck/DeckDefinitions.js` | 牌组定义：handSize=5 / basePool / BASE_SOLDIER_TEXTS | 牌池和手牌大小配置。最简版用 minimalMode=true，BASE_POOL fallback |
| 57 | `deck/DeckManager.js` | 抽牌/手牌：`startGame()` → `drawHand()` / `consume()` / `refresh()` / `gameOver()` | 牌组核心：发牌、消耗、刷新。最简版 minimalMode=true 只抽 4 种基础兵 |
| 58 | `input/BattleInputCommand.js` | 命令枚举 + payload：PURCHASE_AND_PLACE / MERGE / MOVE 等类型 | 输入命令的数据结构 |
| 59 | `input/BattleInputController.js` | 输入控制：`execute(PURCHASE_AND_PLACE)` → validatePlacement → payRecruit → createUnit → consume | 玩家拖牌放置的入口：验证格子→扣金币→创建单位→消耗手牌 |

**验证里程碑**：拖牌放置、抽牌补手牌（拖一张刀兵到合法格子，确认扣金币+创建单位+消耗手牌+补牌）。

---

## Phase 7 — Unity Port 实现 + 表现层 sync（非 JS 模块，Unity 新建）

以下不是 JS 模块移植，而是 Unity 侧新建的实现。**task 1.2 冻结口径（2026-08-05，按 design.md 校正）**：7 项 = BattleModule + 3 Port 的 Unity 实现 + BattleViewRegistry + BattleViewSynchronizer + BattleInputAdapter。下表保留原 #60～#66 历史编号以便对照，但 #60 CombatClockPort 已取消（逻辑时钟归 BattleSimulation 唯一入口）、#61 RandomSourcePort 归入 59 manifest #6（MathRandom Keep as Port）、#66 Bootstrapper 改为 BattleModule（不建独立 Bootstrapper MonoBehaviour）；权威口径以 `战斗移植设计总纲.md` 12.0.1 节为准。

| # | 组件 | 作用 | 实现方式 / 口径校正 |
|---|---|---|---|
| 60 | ~~`CombatClockPort`~~ → **BattleModule**（U1） | TEngine 模块组合入口；持有当前 BattleRuntime，`OnUpdate` 转交帧时间 | 取消独立 CombatClockPort：逻辑时钟归 `BattleSimulation` 唯一入口（design.md）；BattleModule 取代旧 Bootstrapper MonoBehaviour，由 `GameLogic/HotFixModules.cs` 注册一次 |
| 61 | ~~`RandomSourcePort`~~ → 归入 59 manifest #6 | 随机范围/权重选择 | JS `core/MathRandom.js` 冻结为 Keep as Port → C# `IRandomSource/SeededRandomSource`，属 59 JS 迁移项，不重复计入 7 Unity 新建项 |
| 62 | `IBattleViewPort` + Unity 实现（U2） | 单位生成/销毁 + 位置 sync | Instantiate Prefab / Destroy / 每帧读逻辑 x/y 写 transform.position；含 Null/Test 实现 |
| 63 | `IBattleAudioPort` + Unity 实现（U3） | 音频意图端口 | 含 Null/Test 桩与 Unity 真实实现；不阻塞 complete 链 |
| 64 | `IBattleVfxPort` + Unity 实现（U4） | 特效意图端口 | 含 Null/Test 桩与 Unity 真实实现；不阻塞 complete 链 |
| 65 | `BattleViewRegistry` + `BattleViewSynchronizer`（U5/U6） | 运行时 ID→表现对象映射 + Unity 帧插值/同步 | Registry 维护映射；Synchronizer 在 Unity 帧插值/同步，不得推进战斗逻辑 |
| 66 | ~~Bootstrapper MonoBehaviour~~ → **BattleInputAdapter**（U7） | Unity/FairyGUI 点击拖放 → 强类型战斗命令 | 取消独立 Bootstrapper；BattleInputAdapter 把输入转 BattleInputCommand；组合根由 HotFixModules 注册 BattleModule |

**验证里程碑**：完整闭环跑通——开始→出兵→移动→打阿斗→扣血→放兵→攻击→死亡入池→胜负→入池复用。

---

## 移植顺序总览

```
Phase 0: 核心地基 (7)     → 可编译可单测
Phase 1: 数据层 (3)       → JSON 加载 + GameDataCore 就绪
Phase 2: 地图+战斗管理 (11) → WaveManager/BattleManager 就绪
Phase 3: 实体 (5)          → ★敌人移动+阿斗扣血可验证
Phase 4: 效果+投射物 (16)  → ★攻击结算可验证
Phase 5: 单位 (12)         → ★放兵+攻击可验证
Phase 6: 牌组+输入 (5)     → ★拖牌放置可验证
Phase 7: Port+表现层 (7)   → ★完整闭环可验证
```

## 技术要点备忘

### maps.json 格式
- `grid[y][x]`：8 行 × 10 列，第一维是行/y，第二维是列/x
- 每格 `"kind_lane"` 字符串：kind=0 路径/1 可建/2 障碍，lane=1 玩家侧/0 敌方侧
- `opponentPath`：17 点 `[{x:7,y:1}...{x:0,y:0}]`，敌人沿线走
- 坐标：`{x,y}` 中 x=列(0-7)，y=行(0-9)

### GameLoop 机制
- `remaining = currTimer - lastTimer`，`min(remaining, 500)` 截断
- `while remaining > 0: step = min(80, remaining); 回调(step); remaining -= step`
- C# 移植：`Time.deltaTime` 累加 → 500ms 截断 → 80ms 子步 while 拆步

### 命中时序常量（最简版无 Spine，按常量延迟）
- 刀兵：500ms / 播放倍率
- 枪兵：360ms / 播放倍率
- 骑兵：150ms 双扫
- 弓兵：STOPPED 动画事件 → 创建 SimpleDynamicArrow（dev 桩按时长模拟）

### 编排根选择
- 用 `CombatLifecycle` + `CombatServices`，**不用 `BattleFlowCoordinator`**
- 后者 `_requireDependencies()` 硬依赖 skill/boss/AI/network/telemetry 等 13 个服务
