# BOW-PROJECTILE-COMBAT-01 行为规格

## 已确认闭环

```text
UnitFactory.createByText("弓")
→ BowSoldier(level/config index 1)
→ UnitRegistry 注册
→ BattleManager 轮询
→ EnemyManager.queryTargets(280px)
→ 选择 Bm 最小目标
→ 800ms 冷却到期进入 UnitAttack
→ 下一固定步开始 attack 动画
→ STOPPED 前无箭矢
→ STOPPED 后再次验证目标并创建 SimpleDynamicArrow
→ ProjectileManager 反向遍历更新二次贝塞尔箭矢
→ progress>=0.8 后允许命中
→ EnemyBase.hit(2, bowSoldier)
→ 箭矢单目标、单次命中并回池
→ 三箭击杀 6HP Mob0
→ Mob0 继续使用第四轮死亡、空间索引注销和双池回收
→ 弓兵重新选择下一目标
```

## 异步与事件

- 攻击结算依赖 STOPPED，不是立即扣血。
- 目标在 STOPPED 前失效时重新选择；没有可用目标时创建的箭矢立即失效，不造成伤害。
- 飞行中每步刷新目标中心，但按 enemy ID 验证生命周期。
- 战斗暂停同时冻结敌人、投射物和开发动画驱动。

## 暂缓

- 正式 Spine/prefab 和箭矢资源渲染。
- 其他箭矢、枪兵、骑兵、武器、Trail2D、Buff 与技能。
- 通用 ProjectileBase 中未被 SimpleDynamicArrow 使用的分支。
