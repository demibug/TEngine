# 投射物更新顺序与时间单位

## 固定更新注册顺序

开发和测试环境中恢复后的顺序为：

```text
enemyMgr
→ bulletMgr / ProjectileManager
→ developmentAnimationDriver（仅开发环境）
→ BattleMgr
→ BattleScene
```

`ProjectileManager.update(deltaMs)` 内部顺序：

```text
从活动 Array 末尾向前遍历
→ movement.update(deltaMs, speedScale)
→ projectile.update(deltaMs)
→ 若请求移除，执行策略命中/完成
→ 同步注销、回收并从 Array splice
```

## 时间语义

- 所有投射物更新参数使用毫秒。
- GameLoop 固定子步保持 80ms。
- 单次输入 delta 最大累计保持 500ms。
- 暂停时 GameLoop 不调用投射物更新；恢复后不会无限补算暂停时长。
- 反向遍历允许同一固定步同步删除当前箭矢，不会跳过更早插入的箭矢。
- 如果目标在该步的 `enemyMgr` 阶段死亡，随后 `bulletMgr` 按 ID 查询不到目标，不会命中已回收或已复用对象。
