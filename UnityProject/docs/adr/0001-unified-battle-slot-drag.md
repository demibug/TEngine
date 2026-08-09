# ADR 0001：统一槽位拖拽 —— `SlotId` 是固定位置身份

日期：2026-08-09
状态：已接受
相关领域：`UnitSlotBoard`、`BattleDragController`、`BattleInputController`、`BattleHudPanel`

## 背景

原方案先实现了"统一槽位换槽/合并"的规则层（`UnitSlotBoard` 的 `TryPlanDrop`/`CommitDrop`），
但玩家侧只能拖动待上场卡片到战场，场上单位没有输入绑定。规则层支持的四向迁移
（待上场→待上场、待上场→战场、战场→战场、战场→待上场）无法从真实 UI 触发。

同时存在两套未接通的拖拽半成品：`BattlePresenter` 里按运行时 ID 解析战场源的第二套
世界拖拽链从未被调用，且只能解析战场目标，不能解析待上场槽。

## 决策

### 1. `SlotId` 是固定位置身份

每个槽位拥有单局内固定不变的 `UnitSlotId.Id`。槽位本身不移动，只有单位在槽位间迁移。
任何拖放最终都提交 `DropUnit(sourceSlotId, targetSlotId)`，由
`BattleInputController` 原子执行。UI 只解析槽位 ID，不操作战斗对象。

### 2. `BattleUnit.UnitId` 是单位身份

`BattleUnit` 是局内权威数据（兵种、阵营、等级、攻击冷却），`UnitId` 递增分配不复用。
`SoldierBase` 只是单位位于战场槽时创建的战斗运行时对象，不作为待上场单位数据源。

### 3. `Recruit` 与 `DropUnit` 分离

输入层只提交两个命令：
- `Recruit(side)`：清除全部待上场单位（不论等级）并填满 1 级四兵，扣馒头。
- `DropUnit(sourceSlotId, targetSlotId)`：换槽或合并，免费。

上场、下场、移动和合并都是 `DropUnit` 的不同结果，由目标槽占用状态决定。

### 4. 上场、下场、移动和合并都是槽位事务

`UnitSlotBoard` 拆为 `TryPlanDrop`（只读校验）与 `CommitDrop`（一次性提交）。
`BattleInputController` 按"同步实时冷却 → Plan → Prepare（真 Acquire）→ Commit Board →
Commit Runtime → Publish"执行，任何失败槽位不变化（`RollbackDrop` 回滚）。

### 5. HUD 只解析槽位，不操作战斗对象

`BattleHudPanel` 通过 Stage 全局捕获识别源槽/目标槽，创建纯 UI 拖动影子。
拖动期间不修改任何业务状态；松手命中目标才提交 `DropUnit`。HUD 只查询
`UnitSlotSnapshot`，不访问 `UnitRegistry` 或 `UnitSlotBoard` 的写接口。

### 6. Stage 捕获作为场上/待上场统一输入适配

场上单位不挂 Collider/EventTrigger。改用 FairyGUI `Stage.inst` 的
`onTouchBegin/onTouchMove/onTouchEnd` 全局捕获：
- 按下时排除 HUD 控件（征兵/退出按钮、待上场卡），解析战场源槽。
- 拖动时只移动 UI 影子。
- 松手时经 `BattleDragController.EndDrag(stageX, stageY, touchId)` 统一解析目标并提交。

### 7. 触摸所有权

`BattleDragController` 保存 `touchId`：只有开始拖拽的 `touchId` 能结束或取消它，
防止多指交叉提交。HUD 关闭、退出战斗、征兵刷新前强制 `Cancel()`。

## 被否决的方案

- 给每个场上单位加 Collider + EventTrigger + PhysicsRaycaster：侵入性强，且与
  FairyGUI 输入体系重复。
- 保留 `BattlePresenter` 的第二套世界拖拽链（`BeginWorldDrag`/`EndWorldDrag`）：
  它按运行时 ID 解析源、只解析战场目标，无法覆盖待上场目标，且无调用者。

## 后果

- 四向空槽搬迁与四区域合并都从真实 UI 触发，统一走 `DropUnit`。
- 非法目标不改变槽位/单位/冷却/经济状态（表现弹回）。
- HUD 关闭后 Stage 监听对称注销，不残留。
- 旧 `DeckManager` 牌组状态已删除，征兵改由 `RecruitManager` 生成批次。
