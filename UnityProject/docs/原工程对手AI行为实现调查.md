# 原工程对手 AI 行为实现调查

> 调查日期：2026-08-28  
> 调查范围：原工程重建代码、原始 JavaScript bundle、当前 Unity 核心战斗工程  
> 结论性质：静态源码与解码 bundle 的行为还原；未修改业务代码

## 1. 结论先行

原工程的“对面电脑”不是一套独立的敌人刷怪 AI，而是一个挂在战斗循环上的本地对手控制器。它主要负责：

1. 管理对手金币与刷新节奏。
2. 从对手牌池重抽手牌。
3. 将手牌单位放到对手半场，或与已有单位合并。
4. 扫描已部署单位，规划武将/组合和下一轮落子位置。
5. 在金币不足时低概率快速布阵或使用道具。
6. 在玩家进入危险状态时，按难度概率响应道具。
7. 在战斗结束后按胜负调整 AI 难度档位。

真正沿路线前进、受到波次控制的敌人，仍由 `BattleManager`/`WaveManager`/`EnemyManager` 这条普通战斗链负责；`AIController` 操作的是对手侧的牌、棋盘单位和对手侧资源。

最重要的实现特点是：

- AI 不是每帧决策，而是由 `GameLoop` 驱动、按难度使用 2 秒/1.5 秒/1 秒/0.5 秒的轮询间隔。
- AI 的核心行为是一个 5 步状态机：刷新手牌 → 部署手牌 → 扫描棋盘 → 规划布阵 → 执行移动。
- 低难度主要随机放置；高难度开始考虑同族、单位价值、路线邻近度、攻击范围覆盖和模板评分。
- bundle 中的高级部署和规划逻辑是完整存在的；当前 `src/ai` 只是把接口和状态机接出来，`AIDeploymentController`、`AIPlanningController`、模板生成等位置仍保留了 `DEFERRED` 空实现。
- 当前 Unity 工程的核心战斗路径尚未接入这套 AI 配置和运行链；现有 Unity 对照文档明确记录了“没有 AI 配置快照/消费链、对手倍率固定为 1、电脑侧刷怪开关默认关闭”。

## 2. 取证口径与实际路径

用户描述的源目录写作 `origin\_project\js`，磁盘上的实际目录名是 `origin_project\js`，本次按实际存在的目录调查：

```text
D:\\UnityProject\\MyTEngine\\TEngine\\Origin\\reconstructed-project\\src
D:\\UnityProject\\MyTEngine\\TEngine\\Origin\\reconstructed-project\\origin_project\\js\\bundle.js
D:\\UnityProject\\MyTEngine\\TEngine\\Origin\\reconstructed-project\\origin_project\\js\\index.js
```

证据分为三层：

| 证据 | 用途 |
|---|---|
| `src` | 可读的重建接口、依赖关系、状态机和当前已恢复的实现。 |
| `origin_project/js/bundle.js` | 原始运行时 bundle；文件本身经过混淆/压缩。 |
| `work/bundle.strings-decoded.js` | 对 bundle 字符串和符号做过解码的取证副本，用于逐行确认原始方法体；它是分析产物，不是新的运行时源代码。 |

本报告中涉及高级 AI 行为时，以解码 bundle 为准；当 `src` 的注释或桩实现与 bundle 不一致时，明确标记为重建缺口或取证冲突。

## 3. 总体调用链

```text
BattleFlowCoordinator.startBattle()
  ├─ BattleManager.startGame()
  │    └─ 对手初始金币 += initialGold（当前重建值 20）
  ├─ 打开 BattleScene
  ├─ BattleInputController.startGame()
  └─ AIController.startGame()
       ├─ 读取 battle.aiDifficulty，钳制到 0..3
       ├─ 读取难度配置 fG/ni/ri/ii/ei/hi/oi
       ├─ 对手初始金币 += hi（10）
       ├─ 创建 bG=AIDeploymentController
       ├─ 创建 MG=AIPlanningController
       ├─ 创建 GX=单位布阵模板缓存
       └─ 注册 GameLoop 回调

GameLoop（固定逻辑步长 80ms）
  └─ AIController.update(deltaMs)
       └─ 累计时间达到 fG 后调用 TG()
            └─ 5 步 AI 状态机

BattleManager.roundSpawnPrepared
  └─ AIController.PG()
       └─ 到指定波次给对手加钱

BattleScene 玩家危险提示
  └─ 按 ri[难度] 概率调用 AIController.XG()
       └─ 使用 AI 道具栏槽位 1 的危险响应道具
```

对应重建代码的主要位置：

- `Origin/reconstructed-project/src/battle/BattleFlowCoordinator.js:69-110`：战斗启动顺序，AI 在场景创建后启动。
- `Origin/reconstructed-project/src/battle/BattleFlowCoordinator.js:119-150`：战斗清理和胜负后的难度调整。
- `Origin/reconstructed-project/src/ai/AIController.js:218-288`：AI 初始化、难度读取、子控制器创建和注册循环。
- `Origin/reconstructed-project/src/ai/AIController.js:290-364`：更新和 5 步状态机。

## 4. AI 的 5 步状态机

`TG()` 每经过一次难度间隔执行一次。状态保存在 `AIController` 内，不依赖 Unity `MonoBehaviour`；原工程的 bundle 里对应 `vS.TG`，解码位置为 `work/bundle.strings-decoded.js:49802-49836`。

| 步骤 | 条件/动作 | 目的 |
|---|---|---|
| 1. 资源判断 | 对手金币达到刷新阈值时执行 `refresh()`，重置 `XX`，进入步骤 2；否则以 `ni` 概率执行快速布阵 `UG()`，否则尝试道具 `YO()`。 | 决定本轮是重新拿牌、快速结束，还是等待/用道具。 |
| 2. 手牌部署 | 标记 `opponentPlacementComplete=true`，调用 `bG.YX()`；`XX` 走完对手手牌槽位后清理临时列表，进入步骤 3。 | 将当前手牌单位放入对手半场，处理合并和冲突。 |
| 3. 棋盘扫描 | 通过 `KX` 从左到右/逐格扫描对手棋盘，调用 `bG.ZX()`；扫描结束进入步骤 4。 | 合并可合并单位，并收集需要重新规划的单位。 |
| 4. 布局规划 | 过滤死亡单位，依次调用 `MG.tG()`、`MG.iG()`、`MG.hG()`，清空 `nG`，调用 `MG.aG()` 生成目标布局，进入步骤 5。 | 形成组合/武将计划并把目标位置写入规划网格。 |
| 5. 执行布局 | 通过 `MG.lG()` 逐格执行 `nG` 中的移动；全部处理完回到步骤 1。 | 将规划结果变成实际棋盘移动。 |

注意：步骤 2 的“刷新手牌”不是独立的 AI 牌组系统，而是调用公共输入/牌组链：

```text
AIController.refresh()
  └─ BattleInputCommand(REFRESH, { side: false })
       └─ BattleInputController.execute()
            └─ DeckManager.refresh(false)
                 ├─ BattleEconomy.payRefresh(false)
                 ├─ 清理对手手牌槽
                 └─ DeckManager.aiRearrange()/qY 等价逻辑
```

重建代码中 `BattleInputController.execute()` 将 `REFRESH`、`MOVE_UNIT`、`PURCHASE_AND_PLACE`、`MERGE_UNITS` 统一收口；对应 `Origin/reconstructed-project/src/input/BattleInputCommand.js:2-4` 和 `src/input/BattleInputController.js:7-12`。

## 5. 经济、刷新与牌池

### 5.1 初始金币和刷新

当前重建代码的完整启动顺序中：

- `BattleManager.startGame()` 给玩家和对手各加 `initialGold=20`，见 `src/battle/BattleManager.js:57-64`。
- 随后 `AIController.startGame()` 再给对手加 `hi=10`，见 `src/ai/AIController.js:218-288`。
- 因此按当前完整入口，对手初始可用金币通常是 `20+10=30`；单独测试 `AIController` 时不能把这两个加钱动作混为一个。
- 对手刷新/招募相关阈值在重建状态中为 10；刷新费用按经济模块递增，当前 `DeckManager.refresh(false)` 通过公共 `BattleEconomy.payRefresh(false)` 扣除。

### 5.2 对手牌池

bundle 的 `DeckManager` 使用两份可变牌池：

- `kO`：玩家牌池。
- `SO`：AI 牌池。

牌池来自 108 元素展开数组；抽取时随机选下标。非基础字（武将相关字）抽出后会从对应牌池移除，以限制重复。重复标志 `Fi/Oi` 会进一步移除同字牌。

AI 侧抽牌的 bundle 行为位于 `work/bundle.strings-decoded.js:49563-49595`，重建侧对应 `src/deck/DeckManager.js:267-297`：

1. 每次刷新抽取 `handSize=5` 张。
2. 难度 0/1：按原顺序把抽到的牌放入前置桶。
3. 难度 2/3：`铲` 放前置桶，其他牌放后置桶。
4. `铲` 在原 bundle 中占用空手牌槽；普通字通过单位工厂生成对手侧手牌单位，并登记到 AI 控制器的 `$Y` 映射。
5. 低波次还会根据玩家牌池已有的 `铲` 数量，按每 5 张注入 1 张到双方牌池。
6. 原 bundle 在难度配置 `oi` 中还会给 AI 额外注入铲牌：当前取证值为难度 3 注入 5 张；重建 `DeckManager.startGame()` 目前尚未完全接入这个 `oi` 注入点。

这说明 AI 的“决定用什么单位”并不是通过一个单独的随机敌人生成器完成，而是由 AI 专用牌池、公共牌组刷新和公共单位工厂共同决定。

## 6. 手牌部署：`bG.YX()` 的真实策略

`AIDeploymentController` 对应 bundle 中的 `ne`/`bG`，完整方法位于：

```text
work/bundle.strings-decoded.js:46998-47197
```

当前 `src/ai/AIDeploymentController.js:78-135` 仍是接口桩，所以不能只读 `src` 判断原工程没有部署策略。

### 6.1 按单位类型分派

bundle 通过混淆后的单位类判断类型：

| bundle 类型 | 重建语义 | 主要行为 |
|---|---|---|
| `td` | 农民/基础非武器单位 | 先尝试和同类型同等级单位合并；不能合并时寻找 `1_1` 可放置格。 |
| `qo` | 士兵 | 高难度先检查同族单位，再依据单位价值决定是否放置；找不到合法格时进入最小价值冲突目标分支。 |
| `om` | 武将 | 先尝试合并；否则优先找 `2_1` 可扩展格，再退回 `1_1` 普通可放置格。 |
| 空手牌槽 | 通常对应 `铲` 或空槽 | 调用 `sk.AX(1,GX)` 找一个候选位置，并发出 AI 侧放置事件。 |

每次 `YX()` 从 AI 手牌容器 `hX` 逐槽处理，`XX` 记录已处理位置。它不是一次性把全部单位瞬间塞到棋盘，而是让 5 步状态机在多个决策 tick 中逐步完成。

### 6.2 合并逻辑 `HX()`

`HX()` 会在对手棋盘/相关容器里寻找可合并目标：

- `td`：同类型、同等级时尝试合并。
- `qo`：难度低于 2 时不启用这一类主动同族合并；难度 2/3 才检查同字、同类单位，目标未到最高等级时移动到目标位置完成合并。
- 最高等级目标不能继续升级时，低等级输入单位可能走金币返还/回收分支。
- `om`：同类武将、同等级时尝试合并。

这部分不是单纯的“找空格”，而是先尝试把新牌并入已有单位，再决定是否占用新的棋盘格。

### 6.3 士兵价值 `qX()` 和同族检查 `NX()`

士兵价值大致由以下因素组成：

```text
单位基础价值/攻击字段 Op
  + VX(P_) 提供的同字/牌池相关数量加成
  = qX(unit) 的基础评估值
```

低难度会进一步弱化价值：

- 难度 0：乘 `0.2`。
- 难度 1：乘 `0.3`。
- 难度 2/3：不乘低难度弱化系数。

难度 2/3 的 `NX()` 会根据单位定义表 `Oc` 判断同族，并扫描对手棋盘是否已经有同族单位；难度 0/1 直接跳过同族检查。因此难度提升不仅是“思考更快”，还改变了部署分支。

### 6.4 没有可放置格时的最小价值目标 `$X()`

`$X()` 会扫描对手自己的棋盘，在 `1_1` 格中找价值最低的已部署单位，再按当前单位价值差和单位类别系数计算概率，尝试把当前单位放到该冲突位置。

更准确地说，它是“最小价值单位的冲突/替换落子分支”；目标位置的最终碰撞、合并或回收结果由公共放置接口处理。不要把这段逻辑误读成对路线敌人的直接攻击。

## 7. 棋盘扫描与组合规划

### 7.1 `bG.ZX()`：扫描并收集待规划单位

步骤 3 通过 `KX` 行扫描对手棋盘：

1. 遍历 `PA.sb` 中的已部署单位。
2. 先调用 `HX()` 尝试消除可合并的单位。
3. 仍然存活的士兵类 `qo` 放入 `AIController.rp`。
4. 扫描完成后交给 `AIPlanningController`。

bundle 证据为 `work/bundle.strings-decoded.js:47174-47197`。

### 7.2 `MG.tG()`：同族/相关字配对

`tG()` 首先按 `Op` 从高到低排序 `rp`，再通过单位定义表 `Oc.Xp` 找相关字：

- 为每个高价值单位寻找同族或关联单位。
- 找到后形成一对，写入 `sG`。
- 多余的相关单位可能进入 `QX()` 回收/返还分支。

这一步的目标是把现有士兵整理成可合成的组合，而不是先按几何位置移动。

### 7.3 `MG.iG()`：武将组合

当 `Oc.Ep` 武将系统开关启用时：

- 一侧筛选属于某个武将家族的字。
- 另一侧筛选非该家族的可配对字。
- 两侧按 `Op` 排序后逐项配对。
- 配对结果加入 `sG`，并从 `rp` 移除。

因此武将生成依赖牌池中抽到的字和单位定义表，不是 AI 随机凭空创建一个武将。

### 7.4 `MG.hG()`：按组合价值排序

`hG()` 把 `sG` 中的配对映射成武将组合字符串，再通过 `Oc.Yp` 查询组合价值：

- 有定义的组合使用定义表价值；没有定义的组合使用默认值 10。
- 按价值从高到低排序。
- 把可用的既有武将/扩展单位加入后续计划列表。

### 7.5 `MG.aG()`：把计划映射到目标格

`aG()` 使用 `GX` 模板 Map 和半场棋盘生成 `nG` 目标网格：

- 只在合法的 `1_1` 格上评估普通单位。
- 多格组合要求水平方向连续、且连续格都为空。
- 单位模板矩阵乘以路线权重，取评分最高的候选位置。
- 难度 2/3 在选择位置后会更新邻近区域权重，使后续单位考虑已放置单位的影响范围/间距。

最后 `MG.lG()` 从 `nG` 逐格扫描；如果目标单位仍存活且当前位置不同，就通过 `AIController.pG()` 发出移动请求。重建侧 `pG()` 最终适配为 `BattleInputCommandType.MOVE_UNIT`，见 `src/ai/AIController.js:535-560` 和 `src/input/BattleInputController.js:10-12`。

## 8. 难度如何改变 AI 行为

原工程当前取证到的难度配置位于 `Origin/reconstructed-project/unity-export/config/ai-difficulty.json`，参数如下：

| 难度 `Si` | 决策间隔 `fG` | 快速布阵 `ni` | 危险响应 `ri` | 周期收入 | 额外 AI 铲牌 `oi` |
|---:|---:|---:|---:|---:|---:|
| 0 | 2000ms | 0.001 | 0.1 | 0 | 0 |
| 1 | 1500ms | 0.001 | 0.2 | 0 | 0 |
| 2 | 1000ms | 0.001 | 0.5 | 每个指定波次 +10 | 0 |
| 3 | 500ms | 0.001 | 0.8 | 每个指定波次 +20 | 5 |

公共参数：

- `hi=10`：AI 启动时额外加钱。
- `ei=[3,5,8,11,14,17]`：周期收入触发波次。
- `itemCooldownMs=5000`：道具随机尝试的最小间隔。
- `Tu(+1/-1)`：胜利/失败后调整难度，最终钳制在 0..3；跨段位的详细 rank 表解析在当前 `src` 仍是简化适配。

难度差异不仅体现在轮询速度：

| 行为 | 难度 0/1 | 难度 2 | 难度 3 |
|---|---|---|---|
| 普通放置 | 候选格随机洗牌 | 按路线邻接度 `DX`、路线距离 `TX` 排序；前 5 个再随机 | 在 `DX`/`TX` 之前加入模板/攻击覆盖评分 `OG` |
| 士兵同族判断 | 不启用 | 启用 | 启用 |
| 单位价值 | 乘 0.2/0.3，决策偏弱 | 使用完整价值 | 使用完整价值 |
| 周期补钱 | 无 | +10 | +20 |
| 道具危险响应 | 10% | 50% | 80% |

## 9. 普通放置的候选评分 `WX()`

`AIController.WX()` 先收集“空棋盘格 + 指定地形”的候选。默认普通单位使用地形键 `1_1`；武将优先尝试扩展地形 `2_1`。

### 9.1 难度 0/1：随机

候选格收集完成后直接 Fisher-Yates 洗牌，返回随机顺序。此时不计算路线、模板和攻击覆盖。

### 9.2 难度 2：路线邻近启发式

每个候选格计算：

- `DX`：四个正交邻格中属于路线块 `0_1` 的数量；越多越优先。
- `TX`：到对手路线中间 15%～85% 区段的最小曼哈顿距离；越近越优先。

按 `DX` 降序、`TX` 升序排序。如果候选超过 3 个，取前 5 个再次洗牌，保留一定随机性。

### 9.3 难度 3：模板/攻击覆盖启发式

在 `DX`、`TX` 之前增加 `OG`：

- 由 `qj.bX(GX,x,y,context)` 从单位模板中读取候选格评分。
- 模板评分反映路线点是否落在单位攻击范围、攻击模式和路线权重内。
- 排序顺序为 `OG` 降序 → `DX` 降序 → `TX` 升序。

当前 `src/ai/AITemplateResolver.js:148-150` 的 `bX()` 返回 0，因此重建 `src` 的难度 3 会退化为 `DX/TX` 排序；原 bundle 的模板和路径评分实现仍在 `work/bundle.strings-decoded.js:47633-47886`。

## 10. 模板与路线评分的原始实现

原 bundle 的 `AITemplateResolver` 对应 `qj` 对象，主要分为四部分：

1. `kX/yX`：为基础单位、扩展单位、武将和平民生成模板矩阵。
2. `xX`：从地图的可放置/可扩展格生成路线点。
3. `mX`：按路线归一化位置生成权重带，路线头尾 15% 被压低或置零，中段用于攻击覆盖评分。
4. `bX/MX`：从模板 Map 读取单元格评分；找不到指定单位时取模板集合中的最大值。

模板生成会检查：

- 格子是否是 `1_1` 或 `2_1`。
- 单位是否需要多个横向连续格。
- 候选格到路线点的距离是否落入攻击范围。
- 单位目标策略是最近端、普通单体还是范围攻击。

这里有一个重建注释需要特别注意：bundle 的 `AG()` 条件表达式实际是 `simplified ? kX : yX`（解码 bundle `49847-49859`），而当前 `src/ai/AITemplateResolver.js` 的注释将 `kX/yX` 的“简化/完整”语义写成了相反方向。移植时应以 bundle 的条件和 `kX/yX` 方法体为准，不应只依赖 `src` 注释。

## 11. 快速布阵、道具和危险响应

### 11.1 快速布阵 `UG()`

当步骤 1 没有足够金币时，AI 以 `ni=0.001` 的概率触发一次快速布阵：

1. `kG` 守护确保整局只触发一次。
2. 调 `sk.AX(2,GX)` 取最多两个候选位置。
3. 以 `sS.At` 事件、`side=false` 参数发出两个 AI 侧放置请求。

`sk.AX()` 的候选策略随难度变化：低难度随机；高难度考虑扩展格、路线邻接和模板评分。原 bundle 位置为 `work/bundle.strings-decoded.js:47909-48041` 和 `50134-50146`。

当前 `src/ai/AIController.js:706-729` 复用了 `WX()`，并把 `At` 作为字符串事件发出；事件消费者和原始 `sk.AX()` 的严格适配仍标记为 `DEFERRED_FAST_END`。

### 11.2 金币不足时的随机道具 `YO()`

当步骤 1 没有钱且没有触发快速布阵时：

- 检查距离上次道具尝试是否已超过 5 秒。
- 从 AI 道具栏筛选未使用道具。
- 随机选一个并调用 `Yb()`。
- `Yb()` 按道具类型分派到不同的道具效果适配器。

bundle 中已确认的类型分派包括：

| 道具类型 | 行为形态 |
|---|---|
| 3、4、10 | 进入同一组普通道具效果适配器。 |
| 5 | 对固定坐标调用坐标型效果。 |
| 6 | 进入另一组使用型效果适配器。 |
| 2 | 进入独立使用型效果适配器。 |
| 7 | 在 AI 半场随机坐标调用坐标型效果。 |
| 8、9 | 进入另一组公共使用型效果适配器。 |

当前 `src` 已保留 `itemEffectDispatcher` 注入点，但默认实现是失败桩；道具实际效果不能视为已经在当前重建代码中完整恢复。

### 11.3 玩家危险响应 `XG()`

玩家进入危险提示时，BattleScene 的外部逻辑先跳过教程和重复触发，再以 `ri[Si]` 概率调用 `XG()`：

- 难度 0：10%。
- 难度 1：20%。
- 难度 2：50%。
- 难度 3：80%。

`XG()` 只触发一次，读取 AI 道具栏槽位 1，并用危险响应道具常量调用 `Yb()`。对应 bundle `work/bundle.strings-decoded.js:50148-50160`，重建入口在 `src/ai/AIController.js:748-797`。

## 12. 周期收入和战斗结束

### 12.1 周期收入 `PG()`

`BattleManager` 在每轮准备刷怪时发出 `ROUND_SPAWN_PREPARED`；AI 订阅该事件，在当前轮次命中 `ei` 时增加 `ii[Si][index]`：

- 难度 0/1：指定轮次不额外加钱。
- 难度 2：每个指定轮次加 10。
- 难度 3：每个指定轮次加 20。

重建代码见 `src/ai/AIController.js:432-446`；难度表见 `unity-export/config/ai-difficulty.json`。

### 12.2 战斗结束 `gameOver()` 与 `Tu()`

战斗清理顺序中先停止 AI 更新、清理状态机和事件订阅，再根据胜负调用 `Tu(+1/-1)`：

- 胜利：难度档位加 1。
- 失败：难度档位减 1。
- 最终钳制到 0..3。

完整 rank 表跨档逻辑在当前 `src` 以注入的 `rankTableResolver` 承载；默认 resolver 只有简单加减和钳制。

## 13. 当前 `src` 与原 bundle 的恢复状态

| 能力 | 原 bundle 已确认 | 当前 `src` 状态 |
|---|---|---|
| AI 启动、难度读取、定时轮询 | `vS.startGame/update/TG` 完整 | `AIController.js:218-364` 已恢复 |
| 5 步状态机骨架 | 完整 | 已恢复，依赖子控制器实际填充 |
| 经济刷新/对手牌重抽 | `r0.xY/qY/NY` 完整 | `DeckManager`、`BattleInputController` 已恢复主要公共链 |
| 普通候选格评分 | `WX` 完整 | `AIController.WX()` 已恢复 `DX/TX`，难度 3 的 `OG` 依赖桩 |
| 手牌类型分派 | `bG.YX` 完整 | `AIDeploymentController.YX()` 仍是空桩 |
| 合并、同族、最小价值冲突 | `bG.HX/NX/qX/$X` 完整 | `AIDeploymentController` 仅保留 `Si<2`/接口和注释，核心方法未实现 |
| 棋盘扫描 | `bG.ZX` 完整 | `AIDeploymentController.ZX()` 仍是空桩 |
| 组合/武将规划 | `vR.tG/iG/hG` 完整 | `AIPlanningController.tG/iG/hG()` 均为 `DEFERRED` |
| 目标矩阵与实际落子 | `vR.aG/lG` 完整 | `AIPlanningController.aG/lG()` 均为 `DEFERRED` |
| 单位模板 | `qj.kX/yX` 完整 | `AITemplateResolver._buildTemplate()` 返回空 Map |
| 路线点 | `qj.xX` 完整 | `_buildRoutePoints()` 返回空数组 |
| 难度 3 路径/模板评分 | `qj.bX/MX` 完整 | `bX()` 固定返回 0 |
| 快速布阵 | `sk.AX` + `sS.At` 完整 | 复用 `WX`，事件消费者仍是 `DEFERRED` |
| 道具实际效果 | 多个效果适配器 | `itemEffectDispatcher` 默认失败桩 |
| 危险响应道具 | `vb._A(false,1).Yb(rB.QY)` | 复用 `itemSlots[1]`，危险常量/真实道具容器仍是适配桩 |
| rank 跨档 | 原 bundle 使用 rank 数据 | 默认仅简单加减，rank resolver 可注入 |

因此当前 `src` 可以表达“AI 控制器应该如何被调用”，但不能代表原工程高级 AI 已在重建代码中端到端运行。

## 14. Unity 工程现状与移植含义

本次对 `UnityProject` 核心战斗路径的搜索结果：

- `UnityProject/doc/战斗配置覆盖与微信工程对照.md:112-113` 记录：对手攻击倍率固定为 1，电脑侧刷怪开关 `ENABLE_COMPUTER_LANE_ENEMY_SPAWN=false`。
- 同文 `:135`、`:148`、`:164-170` 明确记录 Unity 当前没有 AI 难度表、AI 配置快照和 AI 消费链。
- `Origin/reconstructed-project/unity-handoff/UNITY_CLASS_MAPPING.md` 建议将 JavaScript `AIController` 映射为普通 C# `OpponentAI`，并保持独立于 `MonoBehaviour` 的固定 80ms 战斗模拟。

移植时建议保留以下关系，而不是在 Unity 中重新做一套与牌组/单位不同步的敌人系统：

```text
OpponentAI
  ├─ AI 难度配置
  ├─ 对手金币/刷新
  ├─ 对手牌池和手牌容器
  ├─ 部署/合并策略
  ├─ 规划模板和路线评分
  └─ 通过统一 BattleCommandHandler 执行放置/移动/合并
```

建议的移植顺序：

1. 先移植定时器、5 步状态机、金币、刷新和可注入随机源。
2. 接通对手牌池、5 槽手牌和公共单位工厂。
3. 还原 `YX/HX/NX/qX/$X/ZX`，让对手能够稳定布阵、合并和处理满盘。
4. 还原 `tG/iG/hG/aG/lG` 以及 `kX/yX/xX/bX` 模板/路线评分。
5. 最后接入道具、危险响应、rank 调整和完整 108 张牌池。

测试时应使用固定随机种子，至少覆盖：金币不足、刷新、空槽/铲牌、同族合并、满盘冲突、武将配对、难度 0/2/3 的位置排序、危险响应和战斗结束难度调整。

## 15. 证据索引

### 原工程重建代码

- `Origin/reconstructed-project/src/ai/AIController.js`
  - `:218-364`：启动、定时器和 5 步状态机。
  - `:414-488`：刷新、波次加钱、道具分派。
  - `:610-688`：普通候选格评分。
  - `:706-797`：快速布阵、危险响应。
- `Origin/reconstructed-project/src/ai/AIDeploymentController.js:1-177`：bundle 方法映射及当前 `DEFERRED` 部署桩。
- `Origin/reconstructed-project/src/ai/AIPlanningController.js:1-93`：规划方法映射及当前 `DEFERRED` 桩。
- `Origin/reconstructed-project/src/ai/AITemplateResolver.js:1-151`：模板/路线接口、当前空占位和难度 3 退化点。
- `Origin/reconstructed-project/src/ai/AIDifficultyConfig.js`：难度配置加载与 `Si` 钳制。
- `Origin/reconstructed-project/src/deck/DeckManager.js:17-30、149-204、267-373`：AI 牌池、铲牌注入、AI 重排和刷新。
- `Origin/reconstructed-project/src/input/BattleInputController.js:7-12`：公共刷新、放置、移动、合并命令。
- `Origin/reconstructed-project/src/battle/BattleFlowCoordinator.js:69-150`：启动/清理顺序和胜负后的 `Tu()`。

### 原始/解码 bundle

- `Origin/reconstructed-project/origin_project/js/bundle.js`：原始运行时 bundle。
- `Origin/reconstructed-project/work/bundle.strings-decoded.js`
  - `:46998-47197`：`bG/ne` 手牌部署、合并、价值和棋盘扫描。
  - `:47237-47421`：`MG/vR` 组合规划、模板落子计划和执行。
  - `:47633-48041`：模板、路线点和快速布阵候选评分。
  - `:49681-50160`：`AIController/vS` 初始化、5 步状态机、经济、道具和危险响应。

### Unity 对照

- `UnityProject/doc/战斗配置覆盖与微信工程对照.md:112-170`：当前 Unity 对手 AI/难度配置缺口。
- `Origin/reconstructed-project/unity-handoff/UNITY_CLASS_MAPPING.md`：`AIController → OpponentAI` 的 Unity 映射建议。

## 16. 最终判断

原工程对手 AI 的本质是“共享战斗规则之上的、按难度调节的启发式布阵器”：

```text
对手牌池/金币
    ↓
刷新 5 槽手牌
    ↓
按单位类型尝试合并、占格、冲突替换
    ↓
扫描棋盘并按同族/武将规则组合
    ↓
使用路线与攻击模板评分规划位置
    ↓
通过公共单位/输入链执行移动
```

如果目标是让 Unity 版本表现接近原工程，最不能遗漏的是 `bG` 和 `vR` 两组 bundle 方法。只移植 `AIController` 的 5 步外壳、随机放置和刷新，会得到“会抽牌但不会形成原工程策略”的简化 AI。
