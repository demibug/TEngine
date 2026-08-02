# EnemyManager 空间索引

## 数据结构

```text
JS / enemies              Map<enemyId, EnemyBase>
mB / cellToEnemyIds       Map<"x_y", Set<enemyId>>
wB / enemyIdToCell        Map<enemyId, "x_y">
gridSize                  80
```

## 生命周期

- 注册敌人：写 `JS`，按表现节点中心坐标建立格子记录。
- 移动：`ro.Pw` 在中心格变化时发事件；`vi.xB` 仅当键变化时重建索引。
- 注销：先从格子 Set 删除，再从 `JS` 删除。
- 战斗结束：清空敌人、查询缓冲、所有 Set/Map。

## 查询

`CB` 根据圆心和半径得到格子包围盒，合并候选 ID。`qx` 再执行阵营、状态和圆-矩形相交检测。圆-矩形函数沿用原逻辑，将半径减 1 后计算矩形最近点的平方距离。

当前恢复没有以全表遍历替代 Mob0 使用的空间查询分支。Boss、Buff 传播和若干特殊随机/前排选择函数被列为延后范围。
