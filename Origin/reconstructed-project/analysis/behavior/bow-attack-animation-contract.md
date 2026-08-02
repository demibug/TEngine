# BowSoldier 攻击动画与 STOPPED 契约

## 正式行为

```text
BattleManager 发现目标并满足 800ms 冷却
→ UnitIdle 切换 UnitAttack
→ 下一固定步调用 BowSoldier.attack()
→ 保存目标 ID 与发射初始角
→ 在动画对象注册 Laya.Event.STOPPED
→ 播放 attack 片段 0–650
→ STOPPED 回调先移除监听
→ 再次验证目标
→ 创建 HitEnemyStrategy + TargetEnemyBezierMovement
→ ProjectileManager.create(SimpleDynamicArrow)
→ fire()
→ 播放 attack 片段 650–1000
```

- 监听使用 `on`，但回调第一步执行 `offAll(STOPPED)`，因此同一攻击只发射一次。
- STOPPED 前不得创建箭矢。
- 单位退出攻击状态或回收时移除 STOPPED 监听并取消旧开发动画记录。
- 初始动画播放倍率为 1.25。开发模式把 0–650 的片段换算为 520ms 逻辑时间，在 80ms 固定步下于下一可执行步触发 STOPPED。
- 开发驱动器只存在于 `src/combat/dev/`；正式 BowSoldier 只依赖 Laya 的 STOPPED 事件，不依赖开发驱动器类型。
- GameLoop 暂停时开发动画驱动器不更新；恢复后不会补算无限暂停时间。
- 通过单位 `lifecycleGeneration` 拒绝旧生命周期的 STOPPED 回调。
