# SimpleDynamicArrow 真实轨迹

## 起点、控制点和目标点

```text
P0 = 弓兵表现中心
P2(t) = 每固定步按 enemyId 读取敌人表现中心
P1 = midpoint(P0, P2_at_fire) + (0, -120)
```

控制点在发射时确定；目标终点在飞行期间刷新。目标失效后保留最后一次有效终点。

## 进度

```js
baseDelta = deltaMs * movementRate * projectileSpeedScale / 500;
ratio = currentDistance / originalDistance;
progressDelta = baseDelta * Math.sqrt(Math.max(0.1, ratio));
progress += progressDelta;
```

基础弓兵参数：

```text
deltaMs = 80ms fixed step
movementRate = 1
projectileSpeedScale = 1.75
curveHeight = 120px
```

## 二次贝塞尔位置

```js
x = (1-t)^2 * P0.x + 2*(1-t)*t * P1.x + t^2 * P2.x;
y = (1-t)^2 * P0.y + 2*(1-t)*t * P1.y + t^2 * P2.y;
```

这不是直线插值，也不是重力物理抛物线。

## 旋转

- 发射时：二次贝塞尔在 `t=0` 的切线角度 `+ 90°`。
- 飞行中：使用上一位置到当前位置的显示角度；基础弓兵没有启用平滑旋转分支。

## 到达和命中窗口

- 箭矢表现高度为 72px；命中半径为 `72 / 1.5 = 48px`。
- `progress >= 0.8` 后才允许命中。
- 当目标距离小于 48px 或 `progress >= 1` 时请求移除；ProjectileManager 在移除前按策略执行一次最终目标命中判断。
