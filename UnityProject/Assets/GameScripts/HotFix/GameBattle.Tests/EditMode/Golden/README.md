# 黄金输入/输出 Fixture（task 1.3）

本目录承载 OpenSpec change `port-minimal-battle-to-gamebattle` **task 1.3** 导出的黄金输入/输出，作为 C# GameBattle 与 JS 还原工程行为等价对照的基线。所有数据从还原工程 `Origin/reconstructed-project` **只读导出**，未修改还原工程任何文件来迁就 C# 结果（基线即如此）。

## 来源版本

| 项 | 值 |
|---|---|
| 还原工程仓库 | `E:/MyWork/MyTD/TEngine` |
| git commit | `9b58448e6e0faf09dae19d4349efc31528c92a78` |
| 还原工程根 | `Origin/reconstructed-project` |
| 冻结 JSON 配置目录 | `Origin/reconstructed-project/unity-export/config` |
| 导出时间 | 2026-08-05 |
| 导出者 | execution-subagent |

## 决策依据

- **0.6** 冻结 JSON 只作为黄金测试 Oracle，Luban 为最终生产配置源。
- **0.8** 购买放置和刷新命令携带单局 CommandId；同 ID 重复提交返回首次结果，不再次扣费/消耗/创建。
- **0.9** 战斗只使用 `elapseSeconds` 作为逻辑时间源；550ms 外部帧 `frameNowMs` 观察 550ms，规则位移最多推进 500ms；暂停不以 `realElapseSeconds` 补偿。

## Fixture 清单与 hash

| 文件 | 用途 | SHA-256 |
|---|---|---|
| `golden-battle-bundle.json` | canonical 黄金数据清单（5 类数据 + 来源 hash），可读且可由 task 8.2 对照工具独立解析 | `45bdbecaebee3f900fe705b18308f3ff94244c213335f649c72f4b34b05c1492` |
| `GoldenBattleFixtures.cs` | 强类型 C# fixture（不依赖 JSON 解析器），固化与 bundle 逐字段一致的常量/只读集合，供 EditMode 测试直接消费 | 见下方 note |

> **hash note**：`GoldenBattleFixtures.cs` 内 `BundleSha256` 常量填入后文件内容再次变化，故 C# 文件自身 hash 在此不固化（自引用无意义）；其内容正确性以 `golden-battle-bundle.json` 为 canonical 凭证，由 task 8.2 对照工具校验 C# 常量与 JSON 字段一致。`golden-battle-bundle.json` 的 hash 为最终写入值，可稳定复算。

## 5 类黄金数据导出结果

### 1. 最简配置
- **地图**：map0（8×10，列优先 `grid[x][y]`），来自 `maps.json`（SHA-256 `c3a705…b6db`）。
- **敌人**：Mob0（speed=50，HP 由 `BattleDataCore` 硬编码，`enemies.json` 只含 type 清单无数值），波 1 HP=6.0（10×1×0.6）。
- **波次**：`waves.json` 与 `BattleDataCore` 逐元素一致；wave1 出 10 怪，skipBoss=true，delayTime=10000ms。
- **四兵**：刀/弓/枪/骑（`units.json`，range/damage/interval/等级倍率）。
- **最简牌组**：`DeckDefinitions.BASE_POOL=['刀','弓','枪','骑']`（minimalMode=true，不消费 108 元素完整牌池）。
- **SimpleDynamicArrow**：唯一注册弹种（`projectiles.json` 列 7 类，只注册 1 类）。
- **经济**：initialGold=20，refreshCost 起始 10 每次 +2，阿斗血量 3（`battle-economy.json` + `BattleState` 硬编码）。

### 2. 随机序列
- 函数式常量随机源 `() => 0.5`（非 PRNG 种子），来自 `MinimalBattleBootstrap.js:334` 与 `MinimalBattleLoop.test.js:22`。
- 前 10 项恒 0.5；`weightedIndex([5,2,3])` 首次 → index 0；`drawText(minimalMode)` 首次 → '枪'（测试用 setHand 覆盖）。
- C# `SeededRandomSource` 需以等价方式复现 0.5 序列。

### 3. 外部帧时间序列
- 来源：`GameLoop.js`（MAX_FRAME_DELTA_MS=500，LOGIC_STEP_MS=80）+ `dual-clock-evidence.test.js`（550/16/暂停）+ `MinimalBattleLoop.test.js`（tick(80)）。
- 标准序列：16ms→1子步；80ms→1子步；550ms→截断500ms拆7子步(6×80+20)，frameNowMs=550；0ms 暂停→不补步。

### 4. 输入命令
- 命令类型来自 `BattleInputCommand.js`（本期只用 `PurchaseAndPlace` + `Refresh`）。
- 黄金序列：4 个 PurchaseAndPlace（弓[4,2]/刀[3,2]/枪[5,2]/骑[3,1]，玩家侧 slot 0-3），来自 `MinimalBattleLoop.test.js:89`。
- **CommandId 语义（0.8）**：JS 源无 CommandId 字段（基线即如此）；C# 新增单局 CommandId=step，同 ID 重复返回首次结果，不同 ID 独立处理。
- 原子事务步骤：只读校验→扣费→创建→放置→消耗卡牌→补牌，任一步失败逆序补偿。

### 5. 关键轨迹黄金输出
- 来源：`MinimalBattleLoop.test.js` 断言级黄金输出（10 条事实 T1-T10，覆盖出兵/移动/接触扣血/放兵/攻击击杀/死亡入池/判负/判胜/清理/重开）。
- 更新阶段顺序：EnemyManager→ProjectileManager→DevelopmentAnimationDriver→BattleManager(spawn/attack/effect)→动态回调。
- 结果冻结：首个 BATTLE_FINISHED 事实胜出（幂等），TryFreeze 不重入销毁 Manager。
- **注意**：本轨迹为行为契约级（非逐子步数值快照）；逐子步位置/HP/事件轨迹在 Phase 3/4/5 实现后由 `BattleTraceRecorder` 生成（task 8.1）。

## 字段互补说明（基线即如此，非缺失需补）

- `enemies.json` 只含 type 清单（key/symbol/resource），**不含** HP/速度——数值在 `BattleDataCore.js` 硬编码。
- `battle-economy.json` 含 initialGold/refreshCost，**不含** playerMaxHealth——血量在 `BattleState.js` 硬编码。
- `maps.json` grid 与 `MapData.js` MAP_BLOCKS 常量等价；`MinimalBattleBootstrap` 实际消费代码硬编码数据，JSON 为冻结 Oracle。
- `deck-pool.json` 记录 108 元素完整牌池；最简模式用 4 元素 `BASE_POOL` fallback，不消费完整牌池。

以上互补关系是还原工程既有形态，**禁止修改还原工程来迁就 C#**；C# 适配层（`BattleConfigNormalizer`）负责将 JSON 与硬编码数据统一规范化为 `BattleConfigSnapshot`。

## 消费方式

- EditMode 测试直接引用 `GoldenBattleFixtures` 强类型常量（无需 JSON 解析器）。
- task 8.2 对照工具读 `golden-battle-bundle.json` 做 JS/C# 自动对照（工具只读还原工程基线）。
- 逐子步轨迹 fixture 在 Phase 3/4/5 后补充到本目录（task 8.1）。
