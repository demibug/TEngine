# 敌人状态机

原代码没有独立枚举文件；状态值直接存于 `curState`。因此源码在 `EnemyBase.js` 内导出只读 `EnemyRuntimeState`，没有制造独立 `EnemyState.js`。

## SPAWNING = 0

- 进入：constructor, pool reuse
- 入口动作：targetable=false
- 更新：EnemyManager skips state 0
- 退出：targetable=true

## MOVING = 1

- 进入：spawn presentation completion, skill/stun completion
- 入口动作：start movement animation
- 更新：move(deltaMs); path index transition; grid membership update
- 退出：stop movement animation

## SKILL = 2

- 进入：special enemy skill trigger
- 入口动作：gw hook
- 更新：base update has no movement branch
- 退出：ww hook

## STUNNED = 3

- 进入：buff/state effect
- 入口动作：stun indicator/handler hook
- 更新：base update has no movement branch
- 退出：hide stun indicator

## DEAD = 4

- 进入：health <= 0
- 入口动作：targetable=false; one-shot death/reward event; 100ms normal-enemy fade
- 更新：EnemyManager skips state 4
- 退出：manager/index removal and pool recovery
