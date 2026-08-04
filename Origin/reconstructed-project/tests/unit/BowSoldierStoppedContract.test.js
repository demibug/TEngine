'use strict';
// 6.2 弓兵 STOPPED 契约用例：覆盖 spec「弓兵 STOPPED 发射点必须为正式动画事件契约且 dev 桩为回退」三个 Scenario。
// - 正式动画事件驱动发射：直接 `animation.event(STOPPED)` 模拟正式 Laya/Spine 动画运行时，确认
//   `_onAttackAnimationStopped` 移除监听后调 `launchArrow`，创建 `ProjectileAttackEffect` 登记管理器。
// - dev 桩回退模拟 STOPPED：`DevelopmentAnimationDriver` 按时长模拟（`update()` 累加 `elapsedMs>=durationMs`
//   后 `animation.event(stoppedEvent)`），经同一 `_onAttackAnimationStopped` 入口触发 `launchArrow`。
// - 规则层只依赖 STOPPED 到达信号：无论 STOPPED 来自正式动画还是 dev 桩，`launchArrow` 执行目标再验证 +
//   投射物创建 + 管理器登记，规则层不关心信号源。
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');
const { ProjectileAttackEffect } = require('../../src/combat/ProjectileAttackEffect');

// 释放段时长（0→650ms 段）按初始播放速率 1.25 折算后的 dev 桩模拟时长：(650-0)/1.25 = 520ms。
// 该值即 `DevelopmentAnimationDriver.playSegment` 计算的 `durationMs`，dev 桩 `update()` 累加达此值后
// 触发 `animation.event(stoppedEvent)`。
const DEV_STOPPED_DURATION_MS = 520;

test('正式动画事件驱动发射：STOPPED 触发 _onAttackAnimationStopped 移除监听并调 launchArrow 登记 ProjectileAttackEffect', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 160, remainingPathDistance: 10 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();

  // 正式 STOPPED 触发前：监听器已注册、无箭矢、无 ProjectileAttackEffect 登记。
  assert.equal(bow.animation.listenerCount(h.Laya.Event.STOPPED), 1);
  assert.equal(h.projectileFactory.creationLog.length, 0);
  assert.equal(h.projectileManager.activeCount, 0);
  assert.equal(h.attackEffectManager.activeCount, 0);

  // 模拟正式 Laya/Spine 动画运行时播到释放段末触发 STOPPED（不经 dev 桩时长推进）。
  bow.animation.event(h.Laya.Event.STOPPED);

  // _onAttackAnimationStopped 移除监听后调 launchArrow：监听器归零（防重复）。
  assert.equal(bow.animation.listenerCount(h.Laya.Event.STOPPED), 0);
  // launchArrow 创建投射物并登记管理器。
  assert.equal(h.projectileFactory.creationLog.length, 1);
  assert.equal(h.projectileManager.activeCount, 1);
  assert.equal(h.attackEffectManager.activeCount, 1);
  // 投射物命中策略锁定原始目标（launchArrow 内目标再验证保持原 targetId）。
  assert.equal(h.projectileManager.activeProjectiles[0].hitStrategy.targetIds[0], mob.id);
  // launchArrow 播放 650→1000 收尾段 + bow_attack 音效。
  assert.equal(h.unitAudio.calls.at(-1), 'bow_attack');
  const lastPlay = bow.animation.playCalls.at(-1);
  assert.equal(lastPlay.name, 'attack');
  assert.equal(lastPlay.startMs, bow.attackReleaseEventMs);
  assert.equal(lastPlay.endMs, bow.attackAnimationEndMs);
  // 登记的效果为 ProjectileAttackEffect 且属该弓兵。
  const effect = [...h.attackEffectManager.effects][0];
  assert.ok(effect instanceof ProjectileAttackEffect, '登记的效果应为 ProjectileAttackEffect');
  assert.equal(effect.owner, bow);
  assert.equal(effect.active, true);
});

test('dev 桩回退模拟 STOPPED：DevelopmentAnimationDriver 按时长累加达 durationMs 后经同一 _onAttackAnimationStopped 入口触发 launchArrow', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 160, remainingPathDistance: 10 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();

  // attack() 后 dev 桩登记一段非循环动画推进任务。
  assert.equal(h.animationDriver.activeCount, 1);
  assert.equal(bow.animation.listenerCount(h.Laya.Event.STOPPED), 1);

  // dev 桩未达 durationMs：不触发 STOPPED、无箭矢。
  h.tick(DEV_STOPPED_DURATION_MS - 80, 80);
  assert.equal(h.animationDriver.activeCount, 1);
  assert.equal(h.projectileFactory.creationLog.length, 0);
  assert.equal(h.attackEffectManager.activeCount, 0);
  // 确认 dev 桩 eventLog 未记录 stopped。
  assert.ok(!h.animationDriver.eventLog.some(e => e.type === 'stopped'));

  // dev 桩累加达 durationMs：触发 animation.event(stoppedEvent)，经同一 _onAttackAnimationStopped 入口。
  // 注意：launchArrow 内会播放 650→1000 收尾段并经 dev 桩登记新推进任务，故 STOPPED 后 activeCount 重回 1
  // （与 BowSoldierAttackEvent.test.js 既有断言一致）。
  h.tick(80, 80);
  const stoppedEntry = h.animationDriver.eventLog.find(e => e.type === 'stopped');
  assert.ok(stoppedEntry, 'dev 桩应记录 stopped 事件');
  assert.equal(stoppedEntry.name, 'attack');
  assert.ok(stoppedEntry.elapsedMs >= DEV_STOPPED_DURATION_MS, 'dev 桩应在累加达 durationMs 后触发 stopped');
  // 原释放段推进任务已完成移除；launchArrow 登记 650→1000 收尾段新任务（activeCount=1）。
  assert.equal(h.animationDriver.activeCount, 1);
  // 经同一 _onAttackAnimationStopped 入口触发 launchArrow：监听器移除 + 投射物创建 + 管理器登记。
  assert.equal(bow.animation.listenerCount(h.Laya.Event.STOPPED), 0);
  assert.equal(h.projectileFactory.creationLog.length, 1);
  assert.equal(h.projectileManager.activeCount, 1);
  assert.equal(h.attackEffectManager.activeCount, 1);
  assert.equal(h.projectileManager.activeProjectiles[0].hitStrategy.targetIds[0], mob.id);
  assert.equal(h.unitAudio.calls.at(-1), 'bow_attack');
  const effect = [...h.attackEffectManager.effects][0];
  assert.ok(effect instanceof ProjectileAttackEffect);
  assert.equal(effect.owner, bow);
});

test('规则层只依赖 STOPPED 到达信号：正式动画与 dev 桩两路 STOPPED 经同一入口产生一致规则层结果', t => {
  // 路径 A：正式动画事件驱动 STOPPED。
  const hA = createRangedCombatHarness();
  try {
    const bowA = hA.spawnBow({ gridX: 0, gridY: 6 });
    const mobA = hA.spawnMobInRange(bowA, { offsetX: 160, remainingPathDistance: 10 });
    bowA.targets = hA.enemyManager.queryTargets(40, 520, bowA.attackRange, true);
    bowA.attack();
    bowA.animation.event(hA.Laya.Event.STOPPED);

    // 路径 B：dev 桩按时长模拟 STOPPED。
    const hB = createRangedCombatHarness();
    try {
      const bowB = hB.spawnBow({ gridX: 0, gridY: 6 });
      const mobB = hB.spawnMobInRange(bowB, { offsetX: 160, remainingPathDistance: 10 });
      bowB.targets = hB.enemyManager.queryTargets(40, 520, bowB.attackRange, true);
      bowB.attack();
      hB.tick(DEV_STOPPED_DURATION_MS, 80);

      // 规则层结果一致：两路均经同一 _onAttackAnimationStopped→launchArrow，投射物创建 + 管理器登记 + 目标锁定。
      assert.equal(hA.projectileFactory.creationLog.length, hB.projectileFactory.creationLog.length, '两路 STOPPED 应创建同等数量投射物');
      assert.equal(hA.projectileManager.activeCount, hB.projectileManager.activeCount);
      assert.equal(hA.attackEffectManager.activeCount, hB.attackEffectManager.activeCount);
      // 两路监听器均移除（防重复）。
      assert.equal(bowA.animation.listenerCount(hA.Laya.Event.STOPPED), 0);
      assert.equal(bowB.animation.listenerCount(hB.Laya.Event.STOPPED), 0);
      // 两路目标再验证结果一致（锁定原始目标）。
      assert.equal(hA.projectileManager.activeProjectiles[0].hitStrategy.targetIds[0], mobA.id);
      assert.equal(hB.projectileManager.activeProjectiles[0].hitStrategy.targetIds[0], mobB.id);
      // 两路登记效果均为 ProjectileAttackEffect 且 active。
      const effectA = [...hA.attackEffectManager.effects][0];
      const effectB = [...hB.attackEffectManager.effects][0];
      assert.ok(effectA instanceof ProjectileAttackEffect);
      assert.ok(effectB instanceof ProjectileAttackEffect);
      assert.equal(effectA.active, true);
      assert.equal(effectB.active, true);
    } finally { hB.cleanup(); }
  } finally { hA.cleanup(); }
});

test('规则层目标再验证：STOPPED 到达后原目标失效时 launchArrow 重选可攻击目标', t => {
  // 规则层只依赖 STOPPED 到达信号：launchArrow 内目标再验证（原 targetId 失效则 selectTarget(true) 重选）。
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const primary = h.spawnMobInRange(bow, { offsetX: 160, remainingPathDistance: 10 });
  // 备选目标位于攻击范围内，供目标再验证重选。
  const secondary = h.spawnMobInRange(bow, { offsetX: 200, offsetY: 0, remainingPathDistance: 10 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();
  assert.equal(bow.targetId, primary.id);

  // STOPPED 到达前令原目标不可攻击（targetable=false），验证 launchArrow 内目标再验证重选。
  primary.targetable = false;
  bow.animation.event(h.Laya.Event.STOPPED);

  // 规则层 launchArrow 仍执行：投射物创建 + 管理器登记（目标再验证不阻塞发射）。
  assert.equal(h.projectileFactory.creationLog.length, 1);
  assert.equal(h.projectileManager.activeCount, 1);
  assert.equal(h.attackEffectManager.activeCount, 1);
  // 重选后的命中目标应为备选可攻击目标（非失效原目标）。
  const hitTargetId = h.projectileManager.activeProjectiles[0].hitStrategy.targetIds[0];
  assert.notEqual(hitTargetId, primary.id);
  assert.equal(hitTargetId, secondary.id);
  assert.equal(bow.animation.listenerCount(h.Laya.Event.STOPPED), 0);
});

test('规则层不关心信号源：dev 桩 STOPPED 后重复正式 STOPPED 不再创建第二箭（同一入口移除监听防重复）', t => {
  // 规则层只依赖 STOPPED 到达信号：dev 桩触发 STOPPED 后监听器即移除，后续任何来源的 STOPPED 均不重复发射。
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  h.spawnMobInRange(bow, { offsetX: 160, remainingPathDistance: 10 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();

  // dev 桩按时长模拟 STOPPED（信号源 A）。
  h.tick(DEV_STOPPED_DURATION_MS, 80);
  assert.equal(h.projectileFactory.creationLog.length, 1);
  assert.equal(bow.animation.listenerCount(h.Laya.Event.STOPPED), 0);

  // 同一入口移除监听后，模拟正式动画运行时再次发出 STOPPED（信号源 B）不重复创建投射物。
  bow.animation.event(h.Laya.Event.STOPPED);
  assert.equal(h.projectileFactory.creationLog.length, 1);
  assert.equal(h.attackEffectManager.activeCount, 1);
});
