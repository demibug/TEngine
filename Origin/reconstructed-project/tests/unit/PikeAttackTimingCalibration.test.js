'use strict';

// 枪兵动画事件校准用例（OpenSpec change attack-timing-finalization 任务 6.1）。
// 覆盖 spec「枪兵命中时机必须支持动画事件校准且规则层保持管理器驱动」三个 Scenario：
//   1. 动画事件校准命中时机：注入 animationEventTimingProvider + 调 calibrateHitTiming 校准 hitAtMs，命中仍经 hit() 规则路径。
//   2. 无动画事件源回退常量基线：无 provider 时 hitAtMs = PIKE_HIT_DELAY_MS / playbackRate，命中由 update() 推进 elapsed>=hitAtMs 触发 hit()。
//   3. 规则层命中不依赖动画帧：无 provider/无动画运行时时命中仍由管理器驱动触发，规则层不失效。
const test = require('node:test');
const assert = require('node:assert/strict');
const { AttackEffectManager } = require('../../src/combat/AttackEffectManager');
const {
  PikeAttackEffect,
  PIKE_HIT_DELAY_MS,
  PIKE_ATTACK_ROTATE_MS,
  PIKE_ATTACK_THRUST_MS,
  PIKE_EFFECT_DURATION_MS,
} = require('../../src/combat/PikeAttackEffect');
const { MeleeAttackEffect } = require('../../src/combat/MeleeAttackEffect');

// 构造一个能记录命中结算的 fake 敌人管理器/敌人，复用 UnifiedAttackSystem.test.js 的 stub 模式。
function createHitRecordingHarness({ damage = 10, radius = 50 } = {}) {
  const hits = [];
  const targets = [
    { id: 1, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
    { id: 2, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
  ];
  const enemyManager = { queryEnemyObjects() { return targets; } };
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  return { hits, targets, enemyManager, owner, damage, radius };
}

// Scenario「无动画事件源回退常量基线」
test('PikeAttackEffect falls back to PIKE_HIT_DELAY_MS / playbackRate constant baseline when no animationEventTimingProvider is injected', () => {
  // playbackRate=1：hitAtMs 应为 360（PIKE_HIT_DELAY_MS）。
  {
    const harness = createHitRecordingHarness();
    const effect = new PikeAttackEffect().launch({
      owner: harness.owner,
      enemyManager: harness.enemyManager,
      damage: harness.damage,
      radius: harness.radius,
      playbackRate: 1,
    });
    assert.equal(effect.hitAtMs, PIKE_HIT_DELAY_MS, 'playbackRate=1 时 hitAtMs 回退常量基线 360');
    assert.equal(effect.animationEventTimingProvider, null, '无 provider 注入时字段为 null');
  }
  // playbackRate=2：hitAtMs 应为 180（360/2），与 v0.8.1 现状一致。
  {
    const harness = createHitRecordingHarness();
    const effect = new PikeAttackEffect().launch({
      owner: harness.owner,
      enemyManager: harness.enemyManager,
      damage: harness.damage,
      radius: harness.radius,
      playbackRate: 2,
    });
    assert.equal(effect.hitAtMs, PIKE_HIT_DELAY_MS / 2, 'playbackRate=2 时 hitAtMs 回退常量基线 180');
    assert.equal(effect.hitAtMs, 180);
  }
  // durationMs 同样按 rate 缩放：PIKE_EFFECT_DURATION_MS(480) / 2 = 240。
  {
    const harness = createHitRecordingHarness();
    const effect = new PikeAttackEffect().launch({
      owner: harness.owner,
      enemyManager: harness.enemyManager,
      damage: harness.damage,
      radius: harness.radius,
      playbackRate: 2,
    });
    assert.equal(effect.durationMs, PIKE_EFFECT_DURATION_MS / 2, 'durationMs 按 playbackRate 缩放');
  }
});

// Scenario「无动画事件源回退常量基线」——命中由管理器 update() 推进 elapsed>=hitAtMs 触发 hit()
test('PikeAttackEffect hit is driven by manager update() advancing elapsed to hitAtMs constant baseline (no provider)', () => {
  const harness = createHitRecordingHarness();
  const manager = new AttackEffectManager();
  const effect = new PikeAttackEffect().launch({
    owner: harness.owner,
    enemyManager: harness.enemyManager,
    damage: harness.damage,
    radius: harness.radius,
    playbackRate: 2, // hitAtMs=180
  });
  manager.add(effect);

  // 推进到 hitAtMs-1：不应命中。
  manager.update(179);
  assert.equal(harness.hits.length, 0, 'elapsed < hitAtMs 时不命中');
  assert.equal(effect.hitTriggered, false);
  assert.equal(manager.activeCount, 1);

  // 再推进 1ms 达到 hitAtMs：命中经 hit() 规则路径触发（两个目标各一次）。
  manager.update(1);
  assert.equal(harness.hits.length, 2, 'elapsed >= hitAtMs 时由 update() 推进触发 hit()');
  assert.equal(effect.hitTriggered, true);
  assert.equal(harness.hits[0].damage, harness.damage, '命中伤害经 AttackResolver 规则结算');
  assert.equal(harness.hits[0].attacker, harness.owner, '命中携带攻击者引用（规则层路径）');
});

// Scenario「动画事件校准命中时机」
test('PikeAttackEffect.calibrateHitTiming calibrates hitAtMs to animation event timing while hit still resolves via hit() rule path', () => {
  const harness = createHitRecordingHarness();
  const manager = new AttackEffectManager();
  // 注入 animationEventTimingProvider（DEFERRED，正式 Spine/Tween 第三段 onStart 接入后由动画回调路由调 calibrateHitTiming）。
  // provider 本身只是契约占位：launch() 仍以常量基线起步，校准由外部显式调 calibrateHitTiming 模拟动画事件到达。
  const animationEventTimingProvider = { onPikeHitEvent: null };
  const effect = new PikeAttackEffect().launch({
    owner: harness.owner,
    enemyManager: harness.enemyManager,
    damage: harness.damage,
    radius: harness.radius,
    playbackRate: 1, // 常量基线 hitAtMs=360
    animationEventTimingProvider,
  });
  manager.add(effect);

  // 确认 launch 后以常量基线起步（满足「规则层始终有非动画回退」）。
  assert.equal(effect.hitAtMs, PIKE_HIT_DELAY_MS, '校准前以常量基线 360 起步');
  assert.equal(effect.animationEventTimingProvider, animationEventTimingProvider, 'provider 已注入');

  // 推进 100ms（尚未到常量基线 360，也未校准）。
  manager.update(100);
  assert.equal(effect.elapsed, 100);
  assert.equal(harness.hits.length, 0, '校准前未达常量基线，不命中');

  // 模拟正式动画第三段 onStart 到达：调 calibrateHitTiming 校准 hitAtMs 为当前 elapsed（100）。
  // 设计决策：calibrateHitTiming 将 hitAtMs 重置为当前 elapsed，使下次 update() 满足 elapsed>=hitAtMs 触发 hit()。
  const calibrated = effect.calibrateHitTiming(100);
  assert.equal(calibrated, true, 'calibrateHitTiming 在激活且未命中时返回 true（已校准）');
  assert.equal(effect.hitAtMs, 100, '校准后 hitAtMs 为动画事件到达时机（当前 elapsed）');
  assert.equal(effect.hitTriggered, false, 'calibrateHitTiming 不直接调 hit()（不倒退为动画回调直接结算）');

  // 下次 update() 即满足 elapsed>=hitAtMs 触发 hit() 规则路径——命中仍经 MeleeAttackEffect.hit()。
  manager.update(0);
  assert.equal(effect.hitTriggered, true, '校准后命中仍由 update()→hit() 规则路径触发');
  assert.equal(harness.hits.length, 2, '命中经 hit() 规则路径结算（AttackResolver + hitSet 去重）');
  assert.equal(harness.hits[0].damage, harness.damage);
  assert.equal(harness.hits[0].attacker, harness.owner);
});

// Scenario「动画事件校准命中时机」——calibrateHitTiming 边界：已命中/未激活时不校准
test('PikeAttackEffect.calibrateHitTiming returns false and keeps state when already hit or inactive', () => {
  const harness = createHitRecordingHarness();
  // 已命中后调 calibrateHitTiming 应返回 false 且不改变 hitAtMs。
  const effect = new PikeAttackEffect().launch({
    owner: harness.owner,
    enemyManager: harness.enemyManager,
    damage: harness.damage,
    radius: harness.radius,
    playbackRate: 1,
  });
  // 推进到命中。
  effect.update(PIKE_HIT_DELAY_MS);
  assert.equal(effect.hitTriggered, true);
  const hitAtMsBefore = effect.hitAtMs;
  assert.equal(effect.calibrateHitTiming(50), false, '已命中时 calibrateHitTiming 返回 false');
  assert.equal(effect.hitAtMs, hitAtMsBefore, '已命中时不改变 hitAtMs');

  // 未激活时（cleanup 后）调 calibrateHitTiming 应返回 false。
  effect.cleanup('test-complete');
  assert.equal(effect.active, false);
  assert.equal(effect.calibrateHitTiming(50), false, '未激活时 calibrateHitTiming 返回 false');
});

// Scenario「动画事件校准命中时机」——基类 MeleeAttackEffect.calibrateHitTiming 默认 no-op
test('MeleeAttackEffect base calibrateHitTiming is no-op returning false (does not alter constant baseline)', () => {
  const effect = new MeleeAttackEffect();
  effect.launch({
    owner: { id: 1, side: true, displayObject: { x: 0, y: 0 } },
    enemyManager: { queryEnemyObjects() { return []; } },
    damage: 5,
    radius: 40,
    durationMs: 100,
    hitAtMs: 25,
  });
  const hitAtMsBefore = effect.hitAtMs;
  assert.equal(effect.calibrateHitTiming(10), false, '基类 calibrateHitTiming 默认 no-op 返回 false');
  assert.equal(effect.hitAtMs, hitAtMsBefore, '基类 no-op 不改变 hitAtMs（保持常量基线）');
});

// Scenario「规则层命中不依赖动画帧」
test('PikeAttackEffect rule-layer hit still fires via manager drive when animation runtime is missing and no provider injected', () => {
  // 无 animationEventTimingProvider、无 pikeTipPresentation（动画运行时缺失/未注入）。
  // 规则层（AttackResolver.queryEnemyObjects + hitSet 去重 + resolver.hit）不失效，命中由管理器驱动常量基线触发。
  const harness = createHitRecordingHarness();
  const manager = new AttackEffectManager();
  const effect = new PikeAttackEffect().launch({
    owner: harness.owner,
    enemyManager: harness.enemyManager,
    damage: harness.damage,
    radius: harness.radius,
    playbackRate: 1,
    // 无 animationEventTimingProvider、无 pikeTipPresentation：模拟动画运行时缺失。
  });
  manager.add(effect);

  assert.equal(effect.animationEventTimingProvider, null, '无 provider 注入（动画运行时缺失）');
  assert.equal(effect.pikeTipPresentation, null, '无枪尖 Qx 表现 port（视觉缺失）');

  // 推进至常量基线前：不命中。
  manager.update(PIKE_HIT_DELAY_MS - 1);
  assert.equal(harness.hits.length, 0, '动画运行时缺失时规则层仍按常量基线计时，未达不命中');

  // 达常量基线：命中由管理器 update() 驱动触发，规则层不依赖动画帧。
  manager.update(1);
  assert.equal(harness.hits.length, 2, '无动画运行时时命中仍由管理器驱动常量基线触发，规则层不失效');
  assert.equal(effect.hitTriggered, true);

  // 验证 hitSet 去重：再次推进不会对同一目标重复结算（命中已触发，hitTriggered 守卫）。
  manager.update(10);
  assert.equal(harness.hits.length, 2, 'hitSet 去重 + hitTriggered 守卫，规则层不重复结算');
});

// Scenario「规则层命中不依赖动画帧」——校准 provider 注入但未调用时，规则层仍走常量基线
test('PikeAttackEffect with injected provider but no calibration event still hits via constant baseline (rule-layer independent of animation frames)', () => {
  // provider 已注入但动画事件尚未到达（未调 calibrateHitTiming）：规则层不因 provider 存在而失效，
  // 仍以常量基线 hitAtMs 由管理器驱动命中——满足「不让表现动画成规则唯一触发来源」。
  const harness = createHitRecordingHarness();
  const manager = new AttackEffectManager();
  const provider = { onPikeHitEvent: null };
  const effect = new PikeAttackEffect().launch({
    owner: harness.owner,
    enemyManager: harness.enemyManager,
    damage: harness.damage,
    radius: harness.radius,
    playbackRate: 2, // 常量基线 hitAtMs=180
    animationEventTimingProvider: provider,
  });
  manager.add(effect);

  assert.equal(effect.hitAtMs, 180, 'provider 注入但未校准时仍以常量基线起步');
  // 不调 calibrateHitTiming（动画事件未到达），仅由管理器推进。
  manager.update(179);
  assert.equal(harness.hits.length, 0);
  manager.update(1);
  assert.equal(harness.hits.length, 2, 'provider 存在但动画事件未到达时，命中仍由常量基线 + 管理器驱动触发');
  assert.equal(effect.hitTriggered, true);
});

// 常量来源标注回归（任务 2.4/1.1 间接验证）：常量值与原始 Tween 链段时长对应。
test('PikeAttackEffect constants align with original Tween chain segment durations (rotate 90 + thrust 270 = hit 360, duration 480)', () => {
  assert.equal(PIKE_ATTACK_ROTATE_MS, 90, '段1 旋转 90ms（bundle:24733-24741）');
  assert.equal(PIKE_ATTACK_THRUST_MS, 270, '段2 突刺 270ms');
  assert.equal(PIKE_HIT_DELAY_MS, PIKE_ATTACK_ROTATE_MS + PIKE_ATTACK_THRUST_MS, '命中延迟 = 旋转+突刺 = 360ms（段3 onStart 等价）');
  assert.equal(PIKE_HIT_DELAY_MS, 360);
  assert.equal(PIKE_EFFECT_DURATION_MS, PIKE_HIT_DELAY_MS + 120, '总时长 = 命中延迟 + 回收 120ms = 480ms');
  assert.equal(PIKE_EFFECT_DURATION_MS, 480);
});
