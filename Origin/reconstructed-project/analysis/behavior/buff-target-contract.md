# Buff 目标契约

目标查找顺序与 `uf` 一致：

1. `EnemyManager.JS`
2. `UnitRegistry.PA`（士兵）
3. `UnitRegistry.BM`（武将）
4. `UnitRegistry.AA`（次级单位）

目标必须提供：

- `am()`：表现/事件节点
- `jw(type)`：乘法 Buff 的基础属性
- `zw(type, delta, removing)`：数值属性增减
- `setState(channel, enabled, data)`：状态通道
- `onBuffDataChanged` / `onBuffTypeChanged`：数据通知

Enemy 数值类型按原代码只支持 moveSpeed(3)、maxHp(4)、scale(6)；Soldier 支持 attPower(0)、attSpeed(1)、attRange(2)。
