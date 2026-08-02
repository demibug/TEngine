# Codex App 接手说明 — 赵云救阿斗核心战斗重建工程 v0.8.1

> 将本文件放在解压后的工程根目录，或把全文作为 Codex App 的首条项目上下文。

## 1. 你的角色与目标

你正在接手一个从 LayaAir 3.x 微信小游戏混淆产物中重建出的核心战斗工程。

当前目标不是继续维护原微信/Laya 客户端，而是：

- 以当前 JavaScript 核心战斗逻辑为行为参考；
- 使用现有 Unity handoff 文档与 JSON 配置，在本地继续完成 Unity/C# 接入；
- 保持已确认的战斗规则、时序、数值、事件和清理顺序；
- 对仍为 `PARTIAL`、`INFERRED` 或 `DEFERRED_*` 的内容保持明确标记，不得自行补成“原版规则”。

不要重新逆向整个 bundle，也不要从旧 ZIP 或旧 Round 开始。

---

## 2. 当前基线

```text
项目版本：0.8.1
当前阶段：ROUND-08A-UNITY-CORE-COMBAT-DOCUMENTATION
累计范围：Round 01–08
源码格式：CommonJS JavaScript
Node.js：>= 20
```

最新交付包：

```text
reconstructed-project-core-combat-unity-docs-v0.8.1.zip
SHA-256:
b61f6e8717a447b33a3829822a4846f2bc8897b453c65b6ee122ca5aa50f2bf0
```

工程根目录应包含：

```text
src/
unity-handoff/
unity-export/config/
analysis/
origin_project/
original/
work/
tools/
package.json
handoff.txt
README.md
```

---

## 3. 不可修改的取证基线

以下文件用于追溯原始行为，除非用户明确授权，否则不要修改：

```text
original/bundle.js
work/bundle.strings-decoded.js
original/index.js
src/network/HttpClient.js
```

已确认 SHA-256：

```text
original/bundle.js
19157bd71fd0bd9bdd79da1ea5e5e6ca8d8d786a406b7ac9dd92692676a0a595

work/bundle.strings-decoded.js
f2d6517d19329955e4c761299dfb2ee7323bc7021c0cb8454c4f379d1cd2141b

original/index.js
4024b47ce0f6832a4aa969a6d7329385f6df4c0bbbfb6f26047fd22920574d1b

src/network/HttpClient.js
bfb3ffeff499ba0077dfbfcf94c3a94215ff09166a84bbb522f698943ed89189
```

需要确认某条规则时，按以下优先级取证：

1. `src/` 中当前可执行逻辑；
2. `unity-export/config/`；
3. `analysis/behavior/`、`analysis/catalogs/` 和 `unity-handoff/reference/`；
4. `work/bundle.strings-decoded.js`；
5. `original/bundle.js`。

---

## 4. 当前已闭合的单场核心流程

当前确定性 Smoke 已验证：

```text
MainScene
→ MatchScene
→ BattleScene
→ DeckManager 初始化 5 格手牌
→ 玩家购买并放置正式单位
→ 玩家单位合成升级
→ 单位装备正式武器
→ AI 通过相同 Deck/Input/Factory 路径部署
→ 普通波
→ Boss 波
→ Unit / Enemy / Boss / Weapon / Projectile / Buff / Skill 运行
→ 一方阿斗生命归零
→ 自动 BATTLE_FINISHED
→ 自动生成 BattleResult
→ 自动打开 GameOverScene
→ 返回 MainScene
```

该流程：

- 不依赖 `DevelopmentUnitSpawner`；
- 不需要手工调用 `gameOver()`；
- 不访问真实网络；
- 不调用微信或字节平台 API。

Smoke 记录：

```text
analysis/smoke/single-game-flow.json
analysis/smoke/single-game-flow.md
analysis/round-08-validation.json
```

已验证的样本结果：

```text
场景顺序：MainScene → MatchScene → BattleScene → GameOverScene → MainScene
玩家购买：弓，费用 1，装备 LongBow
合成：刀兵合成到 2 级
AI：使用正式卡牌、经济、放置和 Factory 链部署刀兵
普通波：10 个单位
Boss 波：双侧张梁
结果：胜利，1 星，23 金币，Round 2，6 次击杀
战斗时长：29320ms
```

---

## 5. 必须保持的运行时不变量

### 固定战斗 Tick

```text
固定步长：80ms
单帧最大累计：500ms
所有时间规则默认使用毫秒
```

不要把毫秒数值直接当成 Unity 秒值。

### 启动顺序

```text
economy
deckManager
battleManager
enemyManager
unitManager
weaponManager
projectileManager
buffManager
skillManager
bossManager
waveManager
inputController
aiController
```

### 清理顺序

```text
aiController
inputController
deckManager
waveManager
battleManager
bossManager
enemyManager
unitManager
weaponManager
projectileManager
skillManager
buffManager
```

不要随意调整清理顺序。对象池、旧回调、旧目标 ID 和固定更新注册依赖该顺序。

### 正式输入命令

```text
PurchaseAndPlace
BeginDrag
MoveDrag
CommitPlacement
CancelDrag
MoveUnit
MergeUnits
Refresh
```

UI 或 Unity 输入层应转成命令，不应直接修改金币、Registry 或单位状态。

---

## 6. 当前核心系统状态

### 完成度较高，可作为迁移基线

- 单场启动、自动胜负、GameOver 和返回主界面；
- 80ms 固定步与 500ms 累计限制；
- 牌组、购买、刷新、放置命令；
- 战斗经济；
- 刀、弓、枪、骑 4 个基础兵种；
- 7 个普通敌人注册；
- 12 个 Boss 和 Boss 技能时间线；
- 普通波、Boss 波、双侧生成和无尽扩展；
- Projectile Factory / Manager；
- Buff Manager；
- Skill Manager；
- BattleResult；
- 对象池与生命周期清理；
- 原始场景、Prefab、Boss Spine、技能 VFX 和 Trail2D 资源目录映射。

### 配置导出数量

`unity-export/config/` 当前包含：

```text
units.json               4 个基础兵种
 generals.json           12 名武将配置（部分完成）
enemies.json             7 个普通敌人
bosses.json              12 个 Boss
weapons.json             44 个武器注册项
projectiles.json         7 个投射物类型
buffs.json               20 个 Buff 类型
skills.json              19 个技能
waves.json               波次数量、Boss 波、概率和生成策略
maps.json                8×10 格，80px 格尺寸，双方路径
battle-economy.json      战斗经济
battle-result-schema.json 结算字段
```

### 战斗经济基线

```text
初始金币：20
刷新初始费用：10
每次刷新费用递增：2
基础单位费用：1
手牌槽位：5
```

### BattleResult 字段

```text
isWin
star
gold
battleDuration
round
playerTargetHealth
opponentTargetHealth
weaponFragments
killCount
bossKillCount
endlessRound
gameMode
resultState
```

---

## 7. 关键源码目录

### 核心组合和生命周期

```text
src/battle/CoreCombatRuntime.js
src/battle/CombatServices.js
src/battle/CombatLifecycle.js
src/battle/BattleFlowCoordinator.js
src/battle/BattleManager.js
src/battle/BattleState.js
```

### 玩家操作与单局经济

```text
src/deck/
src/input/
src/ai/
src/battle/BattleEconomy.js
src/units/UnitMergeService.js
src/units/UnitLevelService.js
```

### 实体与战斗对象

```text
src/units/
src/entities/
src/generals/
src/bosses/
src/weapons/
src/projectiles/
src/buffs/
src/skills/
```

### 地图、波次和目标

```text
src/battle/MapData.js
src/battle/MapTileManager.js
src/battle/WaveManager.js
src/battle/EnemyManager.js
src/battle/DeadEntityRegistry.js
src/entities/BattleTarget.js
```

### 引擎端口和资源参考

```text
src/ports/CombatPorts.js
src/resources/
src/presentation/
src/rendering/
src/scenes/
origin_project/
```

核心逻辑迁移时不要让规则层直接依赖 Laya、Spine、微信或场景节点。

---

## 8. Unity 接手资料入口

按以下顺序阅读：

```text
unity-handoff/README.md
unity-handoff/reference/00_IMPLEMENTATION_STATUS.md
unity-handoff/reference/01_ARCHITECTURE.md
unity-handoff/reference/02_SINGLE_GAME_FLOW.md
unity-handoff/reference/03_COMBAT_TICK.md
unity-handoff/reference/04_LIFECYCLE_AND_CLEANUP.md
unity-handoff/reference/05_COMMANDS_EVENTS_RESULTS.md
unity-handoff/reference/06_MAP_AND_PLACEMENT.md
unity-handoff/reference/07_DECK_ECONOMY_AI.md
unity-handoff/reference/08_ENTITIES.md
unity-handoff/reference/09_WEAPONS_PROJECTILES_DAMAGE.md
unity-handoff/reference/10_BUFFS_SKILLS.md
unity-handoff/reference/11_POOLING_AND_OWNERSHIP.md
unity-handoff/reference/12_MANAGER_API_REFERENCE.md
unity-handoff/reference/13_CONFIG_GUIDE.md
unity-handoff/reference/14_UNITY_BLUEPRINT.md
unity-handoff/reference/15_MIGRATION_CHECKLIST.md
unity-handoff/reference/16_KNOWN_GAPS.md
unity-handoff/reference/17_SOURCE_TRACEABILITY.md
```

机器可读摘要：

```text
unity-handoff/CORE_COMBAT_REFERENCE.json
unity-handoff/EVENT_CATALOG.json
unity-handoff/BATTLE_RESULT_SCHEMA.json
unity-handoff/*_CATALOG.json
unity-export/config/*.json
```

C# 参考骨架仅用于理解边界，不是已完成 Unity 工程：

```text
unity-handoff/csharp-reference/
```

---

## 9. 当前准确缺口

### 高优先级

#### 武将系统

- 12 名武将名称和 weaponType 已导出；
- 原始汉字/字形碎片合成武将的完整创建链仍为部分恢复；
- 正式 General 战斗组件仍为 `PARTIAL_CORE_CONFIG`；
- 不要把当前 12 名武将配置误认为完整武将玩法。

#### AI

- 当前 AI 使用正式 Deck、Economy、Placement 和 Factory；
- 当前只恢复了可完成对局的最小策略；
- 原始高级阵容、刷新、合成和决策策略未完全闭合。

### 中优先级

#### 武器高级效果

- 44 个武器注册已导出；
- 部分高级专属效果仍可能含 `DEFERRED_*` 或 `PARTIAL_WITH_EXACT_GAPS`；
- 开始实现某把武器前先读：

```text
analysis/catalogs/weapon-registry.json
unity-handoff/WEAPON_CATALOG.json
```

#### 普通敌人表现

- Mob0 核心逻辑最完整；
- Zombie、Cavalry 等个别敌人的专属 VFX/表现未完全恢复；
- 核心数值、移动、受击、死亡和回收可作为基线。

### 非核心

- 微信登录、广告、分享、排行、云存档；
- 商店、设置、活动等元游戏 UI；
- 原始 music/sound 音频子包缺失。

---

## 10. 信任与实现规则

### 必须遵守

- 保留 80ms Tick 和毫秒单位；
- 保留稳定 ID 查询，不要用裸对象引用替代；
- 保留对象池复用隔离；
- 保留 Manager 的启动和清理顺序；
- 保留 `BATTLE_FINISHED` 作为唯一自动结算入口；
- 不让表现动画成为规则唯一触发来源；
- 不把所有 JS 类机械映射为 MonoBehaviour；
- 不要自行补全 `INFERRED`、`PARTIAL`、`DEFERRED_*` 内容；
- 规则变化必须能追溯到 `src/`、配置或 decoded bundle。

### 状态标签

```text
COMPLETE
COMPLETE_FOR_LOGIC_NO_ASSETS
CORE_COMPLETE
PARTIAL_WITH_EXACT_GAPS
PARTIAL_CORE_CONFIG
DEFERRED_*
INFERRED
UNKNOWN
```

看到非完整状态时，先记录缺口，再决定实现策略。

---

## 11. 本地验证入口

第一次接手时建议运行：

```bash
npm install
npm run check:round08
npm run dev:single-game
npm run export:unity
```

说明：

- `check:round08` 和 `dev:single-game` 是当前核心战斗基线检查；
- `verify:all` 包含部分历史旧断言，已知可能因后续功能恢复而失败，不能作为当前版本唯一完成标准；
- 不要为了让旧测试通过而回退 Mob1、WaveManager 或其他已恢复逻辑。

---

## 12. Codex 接手后的首个输出要求

在开始修改代码前，先完成一次项目接手审计，并仅输出：

1. 识别到的工程版本；
2. 已读取的关键文档和配置；
3. 当前核心单局是否可复现；
4. 准备使用的 JS 参考模块；
5. 目标 Unity 代码与 JS 参考的映射范围；
6. 将保持不变的 Tick、事件、配置和清理顺序；
7. 当前任务涉及的准确缺口；
8. 本次预计修改的文件；
9. 不会修改的取证基线文件。

不要在未读完上述资料前大规模生成 C#，不要重新设计战斗规则，也不要删除原工程中的分析和追溯资料。
