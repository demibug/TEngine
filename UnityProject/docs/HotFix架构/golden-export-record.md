# 黄金输入/输出导出记录（task 1.3）

> 本文件为 OpenSpec change `port-minimal-battle-to-gamebattle` **task 1.3** 的独立导出结论记录，刻意独立于 `战斗移植设计总纲.md`（后者属同批次 task 1.13，为避免并发写冲突，本任务结论先落此处，待 1.13 收口时整合）。

## 任务概述

从现有 JS 测试和实际代码导出带版本与 hash 的最简配置、随机序列、外部帧时间序列、输入命令及关键轨迹黄金输入/输出，作为 C# GameBattle 对照基线。**禁止修改还原工程来迁就 C# 结果**。

## 来源版本

| 项 | 值 |
|---|---|
| 还原工程仓库 | `E:/MyWork/MyTD/TEngine` |
| git commit | `9b58448e6e0faf09dae19d4349efc31528c92a78` |
| 还原工程根 | `Origin/reconstructed-project` |
| 冻结 JSON 配置目录 | `Origin/reconstructed-project/unity-export/config` |

## 落地位置

`Assets/GameScripts/HotFix/GameBattle.Tests/EditMode/Golden/`：

| 文件 | 用途 |
|---|---|
| `golden-battle-bundle.json` | canonical 黄金数据清单（5 类数据 + 来源 hash），SHA-256 `9c7063dbfd0a803553d3c38296d9d62ffa4ae925c57fb1c3d6cd583630994f11` |
| `GoldenBattleFixtures.cs` | 强类型 C# fixture（不依赖 JSON 解析器），固化与 bundle 逐字段一致的常量 |
| `README.md` | fixture 说明 + manifest + 5 类数据导出结论 |

三个文件均带合法 `.meta`（GUID 见各 .meta，已校验全项目无冲突）。

## 5 类黄金数据导出结论

### 1. 最简配置 ✅
- map0（8×10 列优先 `grid[x][y]`）+ Mob0 + wave1（10 怪 skipBoss）+ 四兵（刀/弓/枪/骑）+ 最简牌组 `['刀','弓','枪','骑']` + SimpleDynamicArrow。
- 数值来源：冻结 JSON（`unity-export/config/`）与代码硬编码常量（`BattleDataCore`/`BattleState`/`DeckDefinitions`）只读导出。
- **字段互补（基线即如此）**：`enemies.json` 无 HP/速度（在 `BattleDataCore`）；`battle-economy.json` 无血量（在 `BattleState`）；`MinimalBattleBootstrap` 消费代码硬编码数据，JSON 为冻结 Oracle。未修改还原工程补字段。

### 2. 随机序列 ✅
- 函数式常量随机源 `() => 0.5`（非 PRNG 种子），前 10 项恒 0.5。
- `weightedIndex([5,2,3])`→index 0；`drawText(minimalMode)`→'枪'（测试用 setHand 覆盖）。
- C# `SeededRandomSource` 需等价复现 0.5 序列。

### 3. 外部帧时间序列 ✅
- `GameLoop` MAX_FRAME_DELTA_MS=500 / LOGIC_STEP_MS=80。
- 标准序列：16ms→1子步；80ms→1子步；550ms→截断500ms拆7子步(6×80+20)，frameNowMs=550；0ms 暂停→不补步。
- 含 task 1.9 双时钟证据帧（`dual-clock-evidence.test.js`）。

### 4. 输入命令 ✅
- 4 个 PurchaseAndPlace（弓[4,2]/刀[3,2]/枪[5,2]/骑[3,1]，玩家侧 slot 0-3）+ Refresh（cost 10→12）。
- **CommandId（0.8）**：JS 源无此字段（基线即如此）；C# 新增单局 CommandId=step，同 ID 重复返回首次结果，不同 ID 独立处理。
- 原子事务：只读校验→扣费→创建→放置→消耗→补牌，失败逆序补偿。

### 5. 关键轨迹黄金输出 ✅
- 10 条事实 T1-T10（出兵/移动/接触扣血/放兵/攻击击杀/死亡入池/判负/判胜/清理/重开），来自 `MinimalBattleLoop.test.js` 断言级输出。
- 更新阶段顺序：Enemy→Projectile→DevAnimDriver→BattleManager(spawn/attack/effect)→动态回调。
- 结果冻结：首个 BATTLE_FINISHED 事实胜出（幂等）。
- **范围说明**：本轨迹为行为契约级（非逐子步数值快照）；逐子步轨迹在 Phase 3/4/5 后由 `BattleTraceRecorder` 生成（task 8.1），届时补充为独立 fixture。

## 来源源文件 SHA-256（只读导出基线）

| 源文件（相对 reconstructed-project） | SHA-256 |
|---|---|
| `unity-export/config/maps.json` | `c3a7056ae9604c8778d74c524c44e9681950fdb19696b65788e7f8ba9241b6db` |
| `unity-export/config/waves.json` | `cda521667ba18edff4ac24dc9a6d78ea7b5f436c6be2f2a3933e79016a0662f3` |
| `unity-export/config/units.json` | `4289ab60fdba28f9a2884e6be04085ed83e543307a0d4518ff7e39a03c327a1d` |
| `unity-export/config/enemies.json` | `051fceb8a1bba384fae7bde56e9538dfa5f39fa8de45edc8b83790242ce932d7` |
| `unity-export/config/deck-pool.json` | `9fe6d16148fb082d903e5b44a32e36ad550643c02eb6d43789bace171ca321e9` |
| `unity-export/config/projectiles.json` | `89f83e82f03ed5532024ab5d609e12f7174b05ea00df305301b759283dfd2e3c` |
| `unity-export/config/battle-economy.json` | `009bbd4e88a65569195223651a1f93d4ca833c6958254103f3b92a78ba2d71cf` |
| `src/core/GameLoop.js` | `a95cf0388dcccc15e16b7ac1120071e5a4e4f90e78876d4b8e97bc79a7c0a92e` |
| `src/core/MathRandom.js` | `6fc2e0c74288dd5e29cac774a046b8910d1ded230a74e3a91121a541efb0343b` |
| `src/data/BattleDataCore.js` | `2f58f32aabe3812d2d14e0a7e29b98b437441e164fb6c57c51e5f219dc8ab9fc` |
| `src/battle/BattleState.js` | `b3e546e6cd97ba842073f4e2c69ed76b8757d5c289ad9406f0349f1d7c4013a6` |
| `src/battle/BattleEconomy.js` | `a30b225054e39e8b8cd639d8cc32be7f43fd86549c97f0a7c4b51ae3aa5dff9d` |
| `src/deck/DeckDefinitions.js` | `d5f13c9ae13f43800d47e6352656a9ffbea974ed5bd61532c2117976ca8e2626` |
| `src/bootstrap/MinimalBattleBootstrap.js` | `d4e1f24c86d7d014824c3097430277abf427cda8e4d0c57a01d0bed0d8036c58` |
| `src/input/BattleInputCommand.js` | `d2d29d903533926ce7ce9478cd91b1777711502dad8d19ca1a08f540061278bd` |
| `src/input/BattleInputController.js` | `94a895e352a91e8f7dd1443daef00be32d63b0d5467ee659b06f027ed24ddcf8` |
| `tests/unit/MinimalBattleLoop.test.js` | `f778994d53188a9b7b42f0a15667a5917d7e9d41194a7bf82c5e74dd9edb0b4b` |
| `tests/unit/dual-clock-evidence.test.js` | `b72d031065485b45f115cac5a8d8b06f18decbcdd9f01ce615ce6f5d8984c41b` |

## 验证

- ✅ 5 类黄金数据（配置/随机/帧时间/输入/轨迹）全部导出。
- ✅ 每个 golden 文件有版本（git commit）与 hash（源文件 SHA-256 + bundle 内容 SHA-256）。
- ✅ 未修改还原工程（`git status Origin/` 无本任务产生改动——仅含 1.9 等前置任务的既有改动）。
- ✅ golden fixture 在 `GameBattle.Tests/EditMode/Golden/` 下，`.meta` 格式合法且 GUID 全项目无冲突。
- ✅ 不依赖修改 `GameBattle.Tests.asmdef`（C# fixture 不引入 JSON 解析器依赖）。

## 已知风险/后续

- **轨迹粒度**：当前为断言级（行为契约），非逐子步数值快照。Phase 3/4/5 实现 `BattleTraceRecorder` 后（task 8.1）补充逐子步 fixture，并在 task 8.2 对照工具中消费。
- **CommandId 为 C# 新增语义**：JS 源无此字段，C# 需在 `BattleInputCommand` 新增；这是决策 0.8 的要求，非还原工程既有字段。
- **JSON 与硬编码数据互补**：C# `BattleConfigNormalizer`（task 3.3）需将 JSON 与硬编码数据统一规范化为 `BattleConfigSnapshot`，不得静默补默认值（task 1.8 已审计字段覆盖）。
