# FRIENDLY-UNIT-COMBAT-01 行为规格

## 行为等价层

- `UnitRegistry` 使用 `Map` 保存基础士兵，保留插入和遍历顺序。
- 正式刀兵通过工厂索引 `0` 或文字键 `刀` 创建。
- 一级刀兵：伤害 3、范围 1.5 格即 120px、攻击间隔 0.8 秒。
- 单位固定站位；源码无追击移动。
- BattleManager 先查询目标并切换 `UnitAttack`，在后续更新且冷却满足时再次查询并调用 `attack()`。
- 刀兵攻击结算延迟为 `500 / animationPlaybackRate` 毫秒。
- 命中直接调用已恢复的 `EnemyBase.hit(damage, attacker)`。
- 第一波 Mob0 生命为 6，因此一级刀兵两击击杀。
- 敌人死亡继续使用第四轮的一次性死亡、奖励、空间注销和双层对象池回收。
- 目标死亡后刀兵在下一次轮询清除目标并继续寻找。
- 单位退出或战斗清理会取消旧延迟攻击、注销更新并回收。

## 明确不存在的行为

在 `rb → rc → td → knife` 源码范围中没有友军生命值、受击、死亡、护盾或防御接口。本轮用明确的 `UnsupportedFriendlyUnitDamageError` 防止开发代码伪造这些规则。友军的“死亡和回收”验收边界按源码实际定义解释为主动移除、合成/换位或战斗清理触发的 `gameOver` 回收。

## 开发适配

缺少 prefab 和 Spine 时，`DevelopmentUnitPresentation` 提供可观测的节点和动画调用；`KnifeAttackTimeline` 按原延迟模拟正式动画/效果结算时机。它们不进入平台层，不调用微信、字节或真实网络。

## 后续维护建议（本轮未实施）

完整资源恢复后，可将 `KnifeAttackTimeline` 的开发表现端替换为真实 `vA/tO/r6` 表现实现，同时保留同一时间和伤害契约。不要在该步骤中改为即时伤害。
