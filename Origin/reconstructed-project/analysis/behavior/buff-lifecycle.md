# Buff 生命周期

```text
BuffManager.applyBuff
→ BuffTargetResolver
→ BuffConflictResolver
→ BuffHandlerFactory.create
→ handler.tv/applyData
→ 子 Buff 层创建
→ 数值或状态生效
→ 固定更新/回合事件
→ modify/Jw/SE
→ 属性或状态还原
→ Handler 回池
```

- 永久：`-1`。
- 一回合：`-2`，监听 `ROUND_STARTED/Ft` 后移除对应子层。
- 正时长：按 80ms 固定更新累计毫秒。
- Handler 拥有独立 ID，子 Buff 层也拥有独立 ID。
- Number Buff 添加时调用 `zw(type, delta)`；撤销时调用 `zw(type, -delta, true)`，保留敌方最大生命的原始处理差异。
- State Buff 默认把重复应用合并到同一层；跌倒和击倒刷新持续时间；火焰灼烧保留独立层并每 1000ms 汇总伤害。
