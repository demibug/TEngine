# ADR 0002：本地对手 AI 是单局命令生产者

日期：2026-08-28
状态：已接受
相关领域：`OpponentAI`、`BattleInputController`、`BattleSimulation`、`BattleRuntime`

## 背景

当前战斗运行时已经由固定槽位、原子输入事务和七阶段确定性模拟承载规则，但对手侧
没有主动征兵、部署和整理阵型的控制器。原工程 AI 是由固定逻辑步驱动的普通对象，
通过公共输入链操作对手棋盘和经济，不是沿路线移动的敌人管理器。

新增第二个命令生产者还会暴露两个既有隐含假设：CommandId 原先只由 UI 私有计数器
分配；双方征兵和战斗技能原先共享同一个随机进度。

## 决策

### 1. `OpponentAI` 是单局纯 C# 对象

AI 由 `BattleRuntimeFactory` 每局构造，不继承 `MonoBehaviour`，不使用 Unity `Update`、
全局单例或第二时间源。它随 Runtime 启动、停止和销毁。

### 2. AI 只生产既有战斗命令

AI 只能提交 `Recruit(false)` 和 `DropUnit(sourceSlotId,targetSlotId)`，不得直接调用
`UnitSlotBoard.CommitDrop`、`ReplaceReserve`、`BattleState.Apply*` 或运行时单位写接口。
这保留 ADR 0001 的原子事务与事实发布语义。

### 3. UI 与 AI 共享单局 CommandId 分配器

`BattleCommandIdAllocator` 由 Factory 每局创建并注入两个真实生产者：
`BattleInputAdapter` 与 `OpponentAI`。实体 `RuntimeIdAllocator` 不承担命令身份。

### 4. AI 位于 `WaveSpawn` 后、`UnitAttack` 前

不增加第八个 `BattleUpdatePhase`。`UnitAttack` handler 先执行 `OpponentAI.Update(stepMs)`，
再取得活动单位快照并调用 `AttackScheduler.Update`。这映射原工程 BattleManager 先于 AI、
AI 新放置单位回调又位于 AI 之后的注册顺序。

### 5. 随机进度按领域拆流

从 `BattleLoadoutDto.RandomSeed` 以固定整数混合算法派生玩家征兵、对手征兵、战斗技能、
AI 策略四条流。禁止使用进程或运行时版本相关的 `string.GetHashCode()`。

### 6. 多步计划不持有全局 BoardRevision

`SlotDropPlan.BoardRevision` 继续保护单条 Drop 事务。AI 跨 tick 的计划只保存 UnitId 与
目标身份，每次执行前重新解析当前槽位；失败时重新规划。AI 自身第一条命令、另一阵营
变化或冷却写回都不会使整份计划被错误判废。

## 本期限制

- M1 使用当前四兵对手牌池，不改变对手武将招募规则。
- 满盘时不把 Swap 声称为原工程回收/返还；没有合法落点时等待。
- 难度 3 暂时使用路线评分，模板攻击覆盖评分在 M2 接入。
- 快速布阵、道具、铲牌、危险响应和局外难度调整留到对应运行时存在后实施。

## 后果

- 玩家 UI 和本地 AI 复用同一输入接缝，槽位、经济、单位和事实事务仍只有一个 owner。
- AI 行为次数不会改变玩家征兵与战斗技能的随机序列。
- `BattleRuntime.EnterSettling` 必须先停止 AI 并取消 `WaveStarted` 订阅，再关闭输入和清理规则。
- 随机流拆分会改变旧黄金随机轨迹，相关基线变化必须显式验收。
