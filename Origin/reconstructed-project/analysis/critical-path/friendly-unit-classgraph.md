# FRIENDLY-UNIT-COMBAT-01 类关系

## 已确认继承链

```text
qE / GameObjectEventProxy
└─ rb / UnitDragBase
   └─ ri = rb（闭包别名，bundle:22656）
      └─ rc / UnitBase
         └─ td / SoldierBase
            ├─ tb.zx[0] / KnifeSoldier（本轮恢复）
            └─ ok / BowSoldierCandidate（本轮仅评估，暂缓）
```

证据：`rb` 明确声明 `class ... extends qE`；`ri = rb` 位于 `rc` 定义前；`rc` 声明 `extends ri`；`td` 声明 `extends rc`；刀兵和 `ok` 均声明 `extends td`。

## 创建与更新关系

```text
vc / UnitRegistry.WA
→ 取得 uq.Oc.op 中的兵种索引
→ sc.produce(tb.zx[index])
→ rc.Pw（写入容器与格位）
→ rc.init（取得 soldier 表现对象）
→ vc.zA（登记到 PA Map）
→ vc.qA/$A（网格与场景放置）
→ BattleManager.wH 遍历 PA.values()
→ EnemyManager.qx 空间查询
→ KnifeSoldier.Nx 二次查询并选择目标
→ tO/r6/vA 攻击效果链
→ EnemyBase.hit(damage, attacker)
```

`UnitRegistry` 负责创建编排、注册、放置和回收；工厂只负责按注册构造函数从类池获取对象。BattleManager 主动轮询单位攻击，UnitRegistry 不执行攻击更新。

## 逻辑对象与表现对象

逻辑对象通过 `sc → Laya.Pool.createByClass` 获取，表现节点通过字符串池键 `soldier` 获取。回收时先注销更新、定时器和事件，再分别回收表现节点与逻辑对象。二者不是同一实例。

完整机器可读图见 `friendly-unit-classgraph.json`。
