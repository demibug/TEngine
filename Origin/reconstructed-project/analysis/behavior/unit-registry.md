# UnitRegistry 行为规格

## 追溯

- 模块：`FRIENDLY-UNIT-COMBAT-01`
- 原始符号：`vc`
- 原始范围：`bundle.strings-decoded.js:29460–30476`
- 重建文件：`src/units/UnitRegistry.js`
- 状态：`COMPLETE_FOR_BASE_SOLDIER_COMBAT`

## 职责边界

`UnitRegistry` 负责单位创建编排、集合注册、战场放置、按 ID 查询、显式移除和战斗清理。具体逻辑类型由 `UnitFactory` 选择并从按类对象池取得；`UnitRegistry` 不直接构造 `KnifeSoldier`，也不负责攻击伤害结算。

## 集合结构与顺序

| 原字段 | 重建字段 | 结构 | 本轮用途 |
|---|---|---|---|
| `PA` | `soldiers` | `Map<id, SoldierBase>` | 正式基础士兵；保持插入顺序供 BattleManager 轮询 |
| `AA` | `secondaryUnits` | `Map` | 武将文字部件等，暂缓完整恢复 |
| `BM` | `generals` | `Map` | 正式武将，暂缓完整恢复 |
| `EA` | `farmers` | `Map` | 农民，暂缓完整恢复 |
| `BA` | `generalComponents` | `Map` | 武将组合数据，暂缓完整恢复 |
| `DA` | `deferredBuffs` | `Array` | 完整 BuffManager 恢复前的显式未解决契约 |

`Map` 的插入顺序直接决定 `BattleManager` 遍历士兵的顺序。本轮没有替换为数组或重新排序。

## 正式创建顺序

```text
createUnit / createFromDescriptor
→ 判断战斗是否结束
→ classifyText
→ 检查战斗格占用
→ UnitFactory.createByText
→ unit.setPlacement
→ unit.initialize
→ register（写入 Map）
→ 可选初始 Buff 契约
→ place（取得父节点、计算 grid×80 像素坐标、activatePlacement）
→ 可选 levelUp
→ 延迟 Buff 契约检查
```

开发生成器 `DevelopmentUnitSpawner` 只负责提供明确标记的测试坐标和占位保护，仍调用上述正式入口。

## 注册与移除

- 士兵注册使用 `soldiers.set(id, unit)`；相同 ID 会按 JavaScript `Map` 语义覆盖，但运行时 ID 分配器保证本链路不重复。
- `removeSoldier(id)` 先解除对应放置预留，再同步调用 `unit.gameOver()`，最后从 `Map` 删除。
- 重复移除返回 `false`，不会重复回收。
- 战斗清理先快照全部 ID，再逐项移除，避免遍历期间修改 `Map` 导致遗漏。

## BattleManager 交互

`BattleManager.startGame()` 保存 `UnitRegistry.soldiers` 与 `generals` 的实时引用。每个固定更新：

1. 按 `Map` 插入顺序遍历士兵。
2. 排除非激活、禁用或已入池对象。
3. 使用 `EnemyManager.queryTargets()` 查询候选。
4. 到达冷却时切换 `UnitAttack`。
5. 下一次满足冷却的更新再次查询并调用具体单位 `attack()`。

`UnitRegistry` 自身不注册独立攻击循环；单位表现级 `update` 注册与 BattleManager 的战斗攻击轮询是两条不同契约。

## 清理和对象池

`unit.gameOver()` 负责：

- 注销单位的 GameLoop 键。
- 清理 Laya timer 和 EventBus caller。
- 取消待执行攻击时间线。
- 回收 `soldier` 表现对象。
- 复位单位字段。
- 将逻辑实例回收到具体类池。

随后 `UnitRegistry` 删除集合项。该顺序保持了原始同步回收语义。

## 明确暂缓

- 武将、农民和文字部件的完整创建与放置。
- Buff 初始应用和跨单位传播。
- 正式卡牌、拖拽 Tween、跨容器交换与合成 UI。
- 基础友军受伤/死亡：所选 `rb → rc → td → knife` 源码范围没有 HP 或受击契约，不能凭空补全。
