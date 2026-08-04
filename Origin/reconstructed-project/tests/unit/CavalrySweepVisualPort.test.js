'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { AttackEffectManager } = require('../../src/combat/AttackEffectManager');
const {
  CavalrySweepEffect,
  CAVALRY_SWEEP_DELAY_MS,
} = require('../../src/combat/CavalrySweepEffect');
const { LayaEnemyPresentation } = require('../../src/presentation/LayaEnemyPresentation');

/**
 * 任务 6.3：骑兵 sweep port 用例
 * 覆盖 spec Scenario：
 *   - 骑兵 sweep 视觉 port 契约：launch 经 port 调 createCavalrySweepVisual，cleanup 经 port 调 removeCavalrySweepVisual
 *   - 视觉 port 桩 no-op 不影响规则层：DEFERRED 桩（create 返回 null、remove 空体）不抛异常、不创建渲染对象
 *   - 伤害结算（150ms 延迟/双横扫/半攻击力/半径）不受视觉缺失影响
 *
 * 对齐 bundle:24818-24820（vA.gx(n) 创建两个 sweep 视觉对象）与 bundle:24821-24823
 * （Laya.timer.once(b[112]→150ms, m.LS(); o.LS()) 命中）。纯逻辑层 CavalrySweepEffect
 * 只持 port 调度与生命周期标志，伤害结算由规则层 hit() 驱动不依赖视觉对象。
 */

// 构造可记录调用的 fake presentation（模拟正式 port 契约）。
// create 返回一个句柄对象（非 null），remove 记录被移除的句柄，供断言 port 调度时序。
function createFakePresentation() {
  let seq = 0;
  const calls = [];
  return {
    calls,
    createCavalrySweepVisual(owner, config) {
      seq += 1;
      const handle = { kind: 'sweepVisual', seq, ownerId: owner && owner.id, config };
      calls.push({ method: 'createCavalrySweepVisual', owner, config, handle });
      return handle;
    },
    removeCavalrySweepVisual(visual) {
      calls.push({ method: 'removeCavalrySweepVisual', visual });
    },
  };
}

// 构造可记录 hit 的 fake enemyManager + 目标列表（queryEnemyObjects 直接返回全量目标，
// 与 UnifiedAttackSystem.test.js 骑兵用例一致，简化命中边界断言）。
function createHarness({ targets }) {
  const hits = [];
  const enemyManager = {
    queryEnemyObjects() { return targets; },
  };
  return { hits, enemyManager };
}

test('骑兵 sweep launch 经表现 port 调 createCavalrySweepVisual', () => {
  // port 调用验证：注入 fake presentation（记录 calls），launch 时确认 createCavalrySweepVisual 被调用，
  // 携带 owner 与 config（半径/倍率/延迟），句柄被逻辑层持有为 sweepVisual 生命周期标志。
  const presentation = createFakePresentation();
  const { enemyManager } = createHarness({ targets: [] });
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const effect = new CavalrySweepEffect().launch({
    owner,
    enemyManager,
    damage: 20,
    multiplier: 0.5,
    radius: 80,
    delayMs: CAVALRY_SWEEP_DELAY_MS,
    presentation,
  });

  const createCalls = presentation.calls.filter((c) => c.method === 'createCavalrySweepVisual');
  assert.equal(createCalls.length, 1, 'launch 应经 port 调度一次 createCavalrySweepVisual');
  assert.equal(createCalls[0].owner, owner, 'createCavalrySweepVisual 应传入 owner');
  assert.deepEqual(
    createCalls[0].config,
    { radius: 80, multiplier: 0.5, delayMs: CAVALRY_SWEEP_DELAY_MS },
    'createCavalrySweepVisual 的 config 应携带半径/倍率/延迟',
  );
  // 纯逻辑层只持生命周期标志（句柄），不直接操作渲染层。
  assert.equal(effect.sweepVisual, createCalls[0].handle, '逻辑层应持 port 返回的视觉句柄');
  assert.equal(effect.sweepVisualActive, true, 'launch 后 sweepVisualActive 应为 true');
});

test('骑兵 sweep cleanup 经表现 port 调 removeCavalrySweepVisual 且幂等', () => {
  // port 调用验证：cleanup 时确认 removeCavalrySweepVisual 被调用一次，传入 launch 时获得的句柄；
  // 重复 cleanup（update 完成与 manager 回收）幂等，不重复调用 remove。
  const presentation = createFakePresentation();
  const { enemyManager } = createHarness({ targets: [] });
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const effect = new CavalrySweepEffect().launch({
    owner,
    enemyManager,
    damage: 20,
    multiplier: 0.5,
    radius: 80,
    delayMs: CAVALRY_SWEEP_DELAY_MS,
    presentation,
  });
  const handle = effect.sweepVisual;

  effect.cleanup('duration-complete');
  const removeCalls = presentation.calls.filter((c) => c.method === 'removeCavalrySweepVisual');
  assert.equal(removeCalls.length, 1, 'cleanup 应经 port 调度一次 removeCavalrySweepVisual');
  assert.equal(removeCalls[0].visual, handle, 'removeCavalrySweepVisual 应传入 launch 时获得的句柄');
  assert.equal(effect.sweepVisualActive, false, 'cleanup 后 sweepVisualActive 应复位为 false');
  assert.equal(effect.sweepVisual, null, 'cleanup 后 sweepVisual 句柄应清空');

  // 幂等：重复 cleanup 不再调度 remove（manager 完成回收与 update 完成可能都触发 cleanup）。
  effect.cleanup('duration-complete');
  const removeCallsAfterRepeat = presentation.calls.filter((c) => c.method === 'removeCavalrySweepVisual');
  assert.equal(removeCallsAfterRepeat.length, 1, '重复 cleanup 不应重复调度 removeCavalrySweepVisual');
});

test('无 presentation 注入时 launch/cleanup 不抛异常且不调度 port', () => {
  // DEFERRED 桩 no-op 边界：无 presentation（或 presentation 缺少 port 方法）时，
  // _createSweepVisual/_removeSweepVisual 经 typeof 保护跳过，不抛异常、不调度 port。
  const { enemyManager } = createHarness({ targets: [] });
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const effect = new CavalrySweepEffect().launch({
    owner,
    enemyManager,
    damage: 20,
    multiplier: 0.5,
    radius: 80,
    delayMs: CAVALRY_SWEEP_DELAY_MS,
    // 不注入 presentation
  });

  assert.equal(effect.sweepVisual, null, '无 presentation 时 sweepVisual 保持 null');
  assert.equal(effect.sweepVisualActive, false, '无 presentation 时 sweepVisualActive 保持 false');
  assert.doesNotThrow(() => effect.cleanup('duration-complete'), '无 presentation 时 cleanup 不抛异常');
});

test('DEFERRED 桩 LayaEnemyPresentation.createCavalrySweepVisual 返回 null 不创建渲染对象', () => {
  // DEFERRED 桩 no-op：真实 LayaEnemyPresentation 的 createCavalrySweepVisual 为 no-op 返回 null，
  // removeCavalrySweepVisual 为空体；两者不抛异常、不创建渲染对象，只记录调用。
  const fakeLaya = {};
  const fakePrefabFactory = { createSync() { return {}; } };
  const presentation = new LayaEnemyPresentation({ Laya: fakeLaya, prefabFactory: fakePrefabFactory });

  const owner = { id: 7 };
  const config = { radius: 50, multiplier: 0.5, delayMs: CAVALRY_SWEEP_DELAY_MS };
  const visual = presentation.createCavalrySweepVisual(owner, config);

  assert.equal(visual, null, 'DEFERRED 桩 createCavalrySweepVisual 应返回 null（不创建渲染对象）');
  assert.doesNotThrow(
    () => presentation.removeCavalrySweepVisual(visual),
    'DEFERRED 桩 removeCavalrySweepVisual 不抛异常',
  );
  // 桩记录调用供测试断言（可选），但不操作渲染对象。
  assert.ok(
    presentation.calls.some((c) => c[0] === 'createCavalrySweepVisual'),
    'DEFERRED 桩应记录 createCavalrySweepVisual 调用',
  );
  assert.ok(
    presentation.calls.some((c) => c[0] === 'removeCavalrySweepVisual'),
    'DEFERRED 桩应记录 removeCavalrySweepVisual 调用',
  );
});

test('DEFERRED 桩 no-op 时骑兵 sweep 伤害结算由规则层 hit() 驱动不受视觉缺失影响', () => {
  // 伤害结算不受视觉缺失影响：注入真实 DEFERRED 桩 presentation（create 返回 null、remove 空体），
  // 150ms 延迟后命中仍由 update()→hit() 触发，双横扫/半攻击力/半径正确。
  const fakeLaya = {};
  const fakePrefabFactory = { createSync() { return {}; } };
  const presentation = new LayaEnemyPresentation({ Laya: fakeLaya, prefabFactory: fakePrefabFactory });
  const targets = [
    { id: 1, hit(damage, attacker) { this._lastDamage = damage; this._lastAttacker = attacker; } },
    { id: 2, hit(damage, attacker) { this._lastDamage = damage; this._lastAttacker = attacker; } },
  ];
  const { enemyManager } = createHarness({ targets });
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const manager = new AttackEffectManager();
  const effect = new CavalrySweepEffect().launch({
    owner,
    enemyManager,
    damage: 20,
    multiplier: 0.5,
    radius: 80,
    delayMs: CAVALRY_SWEEP_DELAY_MS,
    presentation,
  });
  manager.add(effect);

  // 150ms 前：视觉桩已调度（create 返回 null），命中未触发。
  assert.equal(effect.sweepVisual, null, 'DEFERRED 桩 create 返回 null，逻辑层持 null 句柄');
  manager.update(CAVALRY_SWEEP_DELAY_MS - 1);
  assert.equal(targets[0]._lastDamage, undefined, '150ms 前不应命中目标 1');
  assert.equal(targets[1]._lastDamage, undefined, '150ms 前不应命中目标 2');

  // 到达 150ms：命中由规则层 hit() 触发，半攻击力（20*0.5=10）作用于两目标。
  manager.update(1);
  assert.equal(targets[0]._lastDamage, 10, '150ms 后目标 1 应受半攻击力命中（20*0.5）');
  assert.equal(targets[1]._lastDamage, 10, '150ms 后目标 2 应受半攻击力命中（20*0.5）');
  assert.equal(targets[0]._lastAttacker, owner, '命中 attacker 应为 owner');
});

test('无 presentation 时双横扫各半攻击力/半径与 150ms 命中时机不受视觉缺失影响', () => {
  // 伤害结算不受视觉缺失影响（无 presentation 场景）：模拟 CavalrySoldier.attack 双横扫——
  // 两个 CavalrySweepEffect（半径各半/全、倍率 0.5），150ms 延迟后双双命中，半径边界正确。
  // 与 UnifiedAttackSystem.test.js 骑兵双横扫断言对齐，额外验证无视觉 port 时不影响规则层。
  const hits = [];
  const innerTarget = { id: 1, hit(damage, attacker) { hits.push({ id: 1, damage, attacker }); } };
  const outerTarget = { id: 2, hit(damage, attacker) { hits.push({ id: 2, damage, attacker }); } };
  // queryEnemyObjects 返回全量目标，半径边界由 radius 参数体现（内横扫 radius=50、外横扫 radius=100）。
  const enemyManager = { queryEnemyObjects() { return [innerTarget, outerTarget]; } };
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const manager = new AttackEffectManager();

  // 内横扫：半径 50（attackRange/2）、倍率 0.5，无 presentation。
  const inner = new CavalrySweepEffect().launch({
    owner,
    enemyManager,
    damage: 20,
    multiplier: 0.5,
    radius: 50,
    delayMs: CAVALRY_SWEEP_DELAY_MS,
  });
  // 外横扫：半径 100（attackRange）、倍率 0.5，无 presentation。
  const outer = new CavalrySweepEffect().launch({
    owner,
    enemyManager,
    damage: 20,
    multiplier: 0.5,
    radius: 100,
    delayMs: CAVALRY_SWEEP_DELAY_MS,
  });
  manager.add(inner);
  manager.add(outer);

  assert.equal(inner.sweepVisual, null, '无 presentation 内横扫 sweepVisual 为 null');
  assert.equal(outer.sweepVisual, null, '无 presentation 外横扫 sweepVisual 为 null');
  assert.equal(inner.radius, 50, '内横扫半径应为 50（半半径）');
  assert.equal(outer.radius, 100, '外横扫半径应为 100（全半径）');
  assert.equal(inner.multiplier, 0.5, '内横扫倍率应为 0.5（半攻击力）');
  assert.equal(outer.multiplier, 0.5, '外横扫倍率应为 0.5（半攻击力）');

  // 150ms 前：双横扫均未命中。
  manager.update(CAVALRY_SWEEP_DELAY_MS - 1);
  assert.equal(hits.length, 0, '150ms 前双横扫均不应命中');

  // 到达 150ms：双横扫各命中两个目标，半攻击力（20*0.5=10）。
  manager.update(1);
  assert.equal(hits.length, 4, '150ms 后双横扫应各命中两目标共 4 次命中');
  const damages = hits.map((h) => h.damage).sort((a, b) => a - b);
  assert.deepEqual(damages, [10, 10, 10, 10], '双横扫每次命中均应半攻击力（20*0.5=10）');
  assert.ok(hits.every((h) => h.attacker === owner), '每次命中 attacker 应为 owner');

  // cleanup 经 manager 回收触发，无 presentation 不抛异常（幂等双重 cleanup 保护）。
  // 推进至 durationMs（150+120=270）使效果完成回收。
  manager.update(120);
  assert.equal(manager.activeCount, 0, '双横扫完成后应从管理器回收');
  assert.equal(inner.active, false, '内横扫 active 应为 false');
  assert.equal(outer.active, false, '外横扫 active 应为 false');
});
