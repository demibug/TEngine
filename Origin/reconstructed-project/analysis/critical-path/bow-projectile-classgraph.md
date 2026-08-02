# Bow / Projectile 类与管理关系

## 已确认类图

```text
qE → rb/ri → rc → td → ok
                       SoldierBase → BowSoldier

qY → rd
ProjectileBase → SimpleDynamicArrow

pP → on
ProjectileMovementBase → TargetEnemyBezierMovement

BowSoldier --STOPPED--> HitEnemyStrategy(type 100)
BowSoldier --STOPPED--> TargetEnemyBezierMovement(curveHeight=120)
BowSoldier --STOPPED--> ProjectileManager.create(SimpleDynamicArrow)
ProjectileManager → ProjectileFactory → composite object pool
ProjectileManager → movement.update → projectile.update/hit → recover
SimpleDynamicArrow → EnemyBase.hit(damage, attacker)
```

## 关键结论

- `ok` 直接继承 `td`，即第五轮恢复的 `SoldierBase`。
- `rd` 直接继承 `qY`。`qY` 是逻辑投射物基类；Laya 表现节点是独立对象。
- `vA` 既持有活动集合又执行更新和注销，职责更接近 `ProjectileManager`；类型选择与复合池创建由 `vj/vk` 承担。
- 弓兵攻击对象与飞行箭矢不是同一对象。弓兵在动画 `STOPPED` 时创建箭矢。
- 投射物不独立注册固定更新；`ProjectileManager` 以 `bulletMgr` 名称注册到现有固定更新管理器。
- 活动集合为 `Array`，更新和清理采用反向遍历，以允许当前帧同步删除。
- 相邻类 `r6` 虽继承 `qY`，但基础弓兵链没有调用它，本轮保留为明确暂缓项。
