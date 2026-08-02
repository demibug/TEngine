# ENEMY-RUNTIME-01 行为规格

## CONFIRMED

- `st → pe → ro → qE`。
- 逻辑实体与表现节点分离，并分别进入类池和 `mob` 键池。
- 地图只把 `0_0`、`0_1` 设为可走；A* 不使用对角线。
- 移动单位为毫秒：`position += direction * speed * deltaMs / 1000`。
- Mob0 基础速度 50 px/s。
- 第 1 波开发样本生命值为 6。
- 路径接触攻击冷却 500ms，伤害延迟 50ms，伤害固定为 1。
- `hit` 不在基础层执行防御、暴击或减伤。
- 普通敌人死亡淡出为 100ms。
- 普通死亡奖励入口为 1，特殊敌人为 10。
- `EnemyManager` 使用 80px 网格、双向索引和更新快照。

## PARTIAL / DEFERRED

- `ro` 中 Buff 属性叠加、击退表现、足迹、武器碎片掉落和特殊技能钩子只保留核心调用边界。
- `pe` 的灵魂飞行、击飞贝塞尔和部分特殊死亡分支未进入 Mob0 最小闭包。
- `vi` 的 Boss、Buff、随机特殊查询和临近终点 UI 提示表现未完整恢复。
- Spine 包装 `ve`、正式 Tween 表现、音频、特效和真实 `mob` prefab 尚缺资源。

## 无行为差异的结构调整

- 原闭包状态改为显式构造依赖。
- `curState` 数字额外导出只读语义常量。
- aDou 原本通过全局 BattleState 字段直接扣血，现由 `BattleTarget.receiveEnemyContact` 封装同一写入。
