# 固定 Tick 与更新顺序

## 固定时间规则

```text
FIXED_STEP_MS = 80
MAX_ACCUMULATED_MS = 500
```

Unity 不应直接将所有规则塞入 `MonoBehaviour.FixedUpdate()`。建议使用一个独立累加器：

```text
frame delta
→ clamp accumulator to 500ms
→ while accumulator >= 80ms
→ SimulateTick(80)
```

## 当前逻辑更新参与者

- EnemyManager
- ProjectileManager
- BattleManager
- BuffManager
- SkillManager
- Boss/Unit 的攻击逻辑由各 Manager 间接驱动

Laya/开发环境中的表现驱动不属于核心规则，Unity 应在 Tick 后同步 Transform 和动画状态。

## 关键时序要求

1. 暂停时规则 Tick 必须停止。
2. 恢复时不得补算整个暂停时长。
3. Manager 在 Tick 中删除对象时必须安全遍历。
4. 投射物命中只执行一次。
5. 旧生命周期的 timer/动画回调不能作用到对象池复用后的实例。
6. `BATTLE_FINISHED` 可在任意 Tick 中发出，但结算必须防重复。

## C# 建议

- `CombatTickDriver`：累加器和 pause 状态。
- `ICombatTickable.Tick(int deltaMs)`：规则接口。
- 不使用浮点秒作为规则主时间；保持毫秒整数，减少迁移误差。
