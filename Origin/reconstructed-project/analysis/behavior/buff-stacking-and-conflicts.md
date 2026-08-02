# Buff 叠加与冲突

## Number Buff

每次应用生成独立子层。`Nw=true` 时，实际增量为 `target.jw(type) × num`。修改子层时先撤销旧增量，再应用新增量。

## State Buff

默认合并：数值相加、非永久持续时间相加。跌倒与击倒使用新持续时间并重置计时器。火焰灼烧不合并。

## 冲突

从 `rF.sh/ih` 恢复：

- `knockback(12)` 在 `limit(15)` 已存在时被拒绝。
- 应用 `limit(15)` 时先移除 `knockback(12)`。
