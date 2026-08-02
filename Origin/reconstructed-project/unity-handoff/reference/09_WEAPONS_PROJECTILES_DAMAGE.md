# 武器、投射物与伤害

## 武器链

```text
Owner/General
→ WeaponManager.attach/equipDefault
→ Weapon.attack(target)
→ ProjectileFactory 或近战攻击对象
→ ProjectileManager Tick
→ HitStrategy
→ EnemyBase.hit(damage, attacker)
```

WeaponManager 负责创建、绑定、活动集合、更新、移除和 gameOver。

## 投射物正式类型

```text
SimpleDynamicArrow
EagleArrow
FireArrow
HuoFengHuang
LightningChain
ShenBiPunch
PikeSnakeBullet
```

## 运动策略

- 直线
- 二次贝塞尔
- 固定终点
- 目标追踪/动态终点（具体类型决定）

`SimpleDynamicArrow` 使用二次贝塞尔并动态读取目标位置。

## 命中策略

- 单目标
- 贯穿
- 范围
- 链式/特殊策略

命中必须通过 EnemyManager/目标 ID 再验证，避免对象池复用后旧引用误伤新对象。

## 伤害规则

- 伤害通常在攻击/发射时快照。
- 目标生命最低钳制到 0。
- 死亡只执行一次。
- 奖励、空间索引注销、表现回收和逻辑回收由敌人死亡链统一处理。
- 投射物不得复制 Enemy 的死亡逻辑。

## Unity 实现建议

规则投射物使用纯 C# 数据：位置、进度、目标 ID、伤害、状态。MonoBehaviour 只订阅快照并更新 Transform/Trail。
