# Buff、Skill 与状态效果

## Buff 类型

共 20 个正式 type，见 `unity-export/config/buffs.json`。

### 数值型

- 攻击力
- 攻击速度
- 攻击范围
- 移动速度
- 最大生命
- 当前生命
- 缩放

### 状态型

- 晕眩
- 跌倒/击倒
- 穿刺
- 电击
- 击退
- 混乱
- 火焰灼烧
- 限制
- 封锁
- 压制
- 魅惑

### 自定义

type 7，由自定义 payload 驱动。

## 生命周期

```text
apply
→ 叠加/刷新/冲突处理
→ 每 Tick 更新
→ 到期或按 BuffId 移除
→ 属性重算/状态退出
→ Handler 回池
```

支持永久、毫秒持续和回合持续模式。

## SkillManager

职责：

- 为 owner 创建/绑定 Skill。
- 普通主动技能激活。
- Boss 技能时间线。
- 冷却与 update。
- owner 移除与 gameOver。

## Boss 技能

共 12 个核心 Boss 技能，已恢复状态变化和生命周期，包括：

- 摄魂/混乱
- 招魂/复活
- 鼓舞
- 拆迁
- 巫山云雨
- 魅惑
- 铁骑号令
- 方天画戟
- 饕餮
- 击倒/压制/封锁类效果
- 噬目遮罩

Unity 表现应监听技能时间线的 wind-up/effect/recovery 阶段，不应让动画决定规则是否执行。
