# ENEMY-RUNTIME-01 类关系

## 已确认继承链

```text
st / Mob0Enemy
  → pe / NormalEnemyBase
    → ro / EnemyBase
      → qE / EnemyEventProxy
```

`pe` 不是移动组件。路径推进、路径索引、接触阿斗、受击和基础死亡状态均位于 `ro`；`pe` 负责普通敌人的数值/动画初始化、受击摆动、死亡淡出和表现回收。`st` 只加入 Mob0 资源、`mob` 表现池和呼吸动画钩子。

## 对象所有权

- `s0 / EnemyFactory` 从按类对象池取得逻辑实体。
- `st.init` 从 `rw` 的字符串键 `mob` 取得表现节点。
- 逻辑实体持有表现节点；二者不是同一个实例。
- `vi / EnemyManager` 持有 `id → EnemyBase` 映射及空间索引。
- `s4 / MapData` 持有四套地图及双方 A* 路径。

## 回收顺序

```text
pe/st death completion
→ ro.gameOver: onDestroy、EnemyManager 注销、清理计时器/路径/状态
→ s0.recover: 逻辑实体进入类池
→ pe 清理并回收动画对象
→ st 将表现节点回收到 key=mob 的池
```

原代码在 `st.gameOver` 后仍保留 `enemy` 引用，因为 `ro.move()` 在 `Mw/gameOver()` 返回后还会执行一次 `Pw()` 网格检查。重建代码保留了这一反直觉顺序。
