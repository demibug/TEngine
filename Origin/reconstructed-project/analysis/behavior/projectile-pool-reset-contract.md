# 投射物对象池复位契约

## 必须清除

- `projectileId`
- 攻击者和显式伤害快照
- 目标 ID、目标策略和目标移动策略
- 起点、目标点、控制点、上一位置和归一化进度
- 速度倍率、曲率、命中半径和命中启用状态
- 已命中敌人 ID 集合
- active/requestedRemoval/immediateRemoval/recovered 状态
- 表现坐标、尺寸、锚点、旋转、可见性和事件监听
- ProjectileManager 注册
- 旧生命周期代号所对应的开发动画回调

## 复用保证

- ProjectileFactory 每次分配新递增 ID。
- movement 与 hit strategy 分别进入自己的池。
- 复合逻辑/表现对象进入同一字符串键池。
- 旧箭矢持有的目标 ID 不会命中对象池复用后取得新敌人 ID 的对象。
- 重复回收返回 false，不会重复入池。
