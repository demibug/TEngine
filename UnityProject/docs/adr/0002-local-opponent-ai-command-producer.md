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

### 2. AI 只生产输入命令

AI 提交 `Recruit(false)`、`DropUnit(sourceSlotId,targetSlotId)`、专用道具命令和
`ReclaimUnit(sourceBattleSlotId,expectedUnitId)`；不得直接调用
`UnitSlotBoard.CommitDrop`、`ReplaceReserve`、`BattleState.Apply*` 或运行时单位写接口。
`OpponentAiActionType.Replace` 只能映射到 `ReclaimUnit`，不得在执行失败时降级为
普通 `DropUnit`。这保留 ADR 0001 的原子事务与事实发布语义。

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

### 7. Raw parity 配置不做跨语义推导

`Basic` 与 `OriginalParity` 是明确的行为边界：Basic 只启用已声明的基础兵/输入路径；
OriginalParity 才启用原工程已取证的牌池、道具和 AI 行为。`EnableValueEvaluation` 只
控制候选评分，不能作为 GeneralPart duplicate/copy 状态的代理。raw `ni`/`ri` 概率、
复制概率、FastDeploy 最大候选数和 one-shot 次数在领域默认值中集中定义，JSON 与 Luban
适配层必须使用同一套契约；生成数据行缺少字段时不得借用无关布尔值推导。

### 8. 外部危险事件与状态机策略分离

`DangerResponse` 只响应显式 `OnPlayerDanger` 入口，并受每局概率与 one-shot guard
约束；普通决策 tick 的道具冷却不得伪造危险事件。FastDeploy 同样只在原工程金币不足
且概率命中时触发，一次最多两个候选部署动作。所有命令在执行前重新校验
source/target/expectedUnitId，失败后清队列并重规划。

## 本期限制

- M1 使用当前四兵对手牌池，不改变对手武将招募规则。
- 满盘时不把 Swap 声称为原工程回收/返还；只有显式 `ReclaimUnit` 才返还单位等级金币。
- 模板候选至少保留路线、价值与攻击覆盖三个可解释评分项。
- FastDeploy、道具、铲牌和危险响应必须保留 raw guard；局外账户快照未注入时，
  铲子额外注入使用显式零值回退并记录诊断。

## 后果

- 玩家 UI 和本地 AI 复用同一输入接缝，槽位、经济、单位和事实事务仍只有一个 owner。
- AI 行为次数不会改变玩家征兵与战斗技能的随机序列。
- `BattleRuntime.EnterSettling` 必须先停止 AI 并取消 `WaveStarted` 订阅，再关闭输入和清理规则。
- 随机流拆分会改变旧黄金随机轨迹，相关基线变化必须显式验收。
