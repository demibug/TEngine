# 敌人生命周期

## 创建

```text
EnemyManager.spawn(typeIndex, side, special)
→ EnemyFactory.create(type key)
→ class pool take Mob0Enemy
→ configure dependencies
→ Mob0Enemy.init(side)
→ key pool take visual "mob"
→ EnemyBase.init: ID、节点、出生坐标、EnemyManager 注册
→ NormalEnemyBase: 属性、动画、A* 路径
→ spawn presentation completion
→ state 0 → 1
```

首波 Mob0 的基础速度为 50 px/s。第 1 波、玩家历史局数为 0 时，基础血量 10 乘早期系数 0.6，得到 6。

## 运行

`EnemyManager` 每个固定逻辑子步先复制 `enemies` 到快照，再跳过状态 0 和 4。Mob0 在状态 1 使用 `speed * deltaMilliseconds / 1000` 沿 `MapData` 路径移动。

## 接触目标

路径索引到 `length-1` 时尝试接触攻击；当前计时必须距上次攻击至少 500ms。表现入口立即触发，50ms 后对对应侧 aDou 固定造成 1 点伤害。到达路径末尾后敌人离场回收。

## 被击杀

`hit` 直接扣减传入伤害并钳制到 0；生命归零切换状态 4。`pe` 使用 100ms 死亡淡出完成边界，随后执行注销和双层池回收。掉落、完整武器碎片和表现特效仍以端口保留。
