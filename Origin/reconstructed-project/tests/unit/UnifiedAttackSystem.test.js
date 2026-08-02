'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { UnitState } = require('../../src/units/UnitBase');
const { AttackScheduler } = require('../../src/combat/AttackScheduler');
const { AttackEffectManager } = require('../../src/combat/AttackEffectManager');
const { PikeAttackEffect } = require('../../src/combat/PikeAttackEffect');
const { CavalrySweepEffect } = require('../../src/combat/CavalrySweepEffect');
const { KnifeAttackTimeline } = require('../../src/combat/KnifeAttackTimeline');
const { ObjectPool } = require('../../src/core/ObjectPool');

test('AttackScheduler keeps target selection and attack dispatch in separate passes', () => {
  const calls = [];
  const enemyManager = { queryTargets() { return [{ id: 7, x: 10, y: 20 }]; } };
  const unit = {
    isActive: true,
    disabled: false,
    inPool: false,
    displayObject: { x: 0, y: 0, width: 20, height: 20 },
    currentState: UnitState.IDLE,
    lastAttackTime: 0,
    attackIntervalSeconds: 1,
    attackRange: 100,
    side: true,
    targets: [],
    changeState(state) { this.currentState = state; },
    attack() { calls.push(this.targets[0].id); return 'attacked'; },
  };
  const scheduler = new AttackScheduler({ enemyManager });

  const ready = scheduler.update(unit, { now: () => 1000 });
  const attacked = scheduler.update(unit, { now: () => 2000 });

  assert.equal(ready.attacked, false);
  assert.equal(unit.currentState, UnitState.ATTACK);
  assert.equal(attacked.attacked, true);
  assert.deepEqual(calls, [7]);
});

test('AttackEffectManager updates pike and cavalry effects and removes them after completion', () => {
  const hits = [];
  const targets = [
    { id: 1, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
    { id: 2, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
  ];
  const enemyManager = {
    queryEnemyObjects() { return targets; },
  };
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const manager = new AttackEffectManager();
  const pike = new PikeAttackEffect().launch({ owner, enemyManager, damage: 10, radius: 50 });
  const cavalry = new CavalrySweepEffect().launch({ owner, enemyManager, damage: 20, radius: 80, delayMs: 80 });
  manager.add(pike);
  manager.add(cavalry);

  manager.update(50);
  assert.equal(hits.length, 2);
  assert.equal(manager.activeCount, 2);
  manager.update(80);
  assert.equal(hits.length, 4);
  manager.update(100);
  assert.equal(manager.activeCount, 0);
  assert.equal(pike.active, false);
  assert.equal(cavalry.active, false);
});

test('AttackEffectManager recycles completed effects through ObjectPool', () => {
  const objectPool = new ObjectPool();
  const manager = new AttackEffectManager({ objectPool });
  const owner = { id: 10, side: true, displayObject: { x: 0, y: 0 } };
  const enemyManager = { queryEnemyObjects() { return []; } };
  const effect = manager.create(PikeAttackEffect).launch({ owner, enemyManager, durationMs: 10 });
  manager.add(effect);
  manager.update(10);

  assert.equal(manager.activeCount, 0);
  assert.equal(objectPool.sizeByClass(PikeAttackEffect), 1);
  assert.equal(manager.create(PikeAttackEffect), effect);
});

test('KnifeAttackTimeline can use the unified manager without changing the 500ms hit timing', () => {
  const manager = new AttackEffectManager();
  const hits = [];
  const enemy = { id: 3, isTargetableBy() { return true; }, hit(damage, attacker) { hits.push({ damage, attacker }); } };
  const timeline = new KnifeAttackTimeline({
    laya: { timer: { currTimer: 0 } },
    enemyManager: { getById() { return enemy; } },
    effects: { startKnifeAttack() {}, showKnifeHit() {} },
    attackEffectManager: manager,
  });
  const attacker = { id: 8, side: true, animationPlaybackRate: 1, lifecycleGeneration: 1, inPool: false, destroyed: false, isActive: true };
  const record = timeline.start({ attacker, target: { id: enemy.id }, damage: 4 });

  manager.update(499);
  assert.equal(record.settled, false);
  assert.equal(hits.length, 0);
  manager.update(1);
  assert.equal(record.settled, true);
  assert.equal(hits.length, 1);
  assert.equal(hits[0].damage, 4);
});

test('KnifeAttackEffect delegates production timing to the animation timer', () => {
  const manager = new AttackEffectManager();
  let timerDelay = 0;
  let timerCallback = null;
  const enemy = { id: 4, isTargetableBy() { return true; }, hit() {} };
  const timeline = new KnifeAttackTimeline({
    laya: {
      timer: {
        currTimer: 0,
        once(delay, _caller, callback) { timerDelay = delay; timerCallback = callback; },
        clearAll() {},
      },
    },
    enemyManager: { getById() { return enemy; } },
    effects: { startKnifeAttack() {}, showKnifeHit() {} },
    attackEffectManager: manager,
  });
  const attacker = { id: 12, side: true, animationPlaybackRate: 1, lifecycleGeneration: 1, inPool: false, destroyed: false, isActive: true };
  const record = timeline.start({ attacker, target: { id: enemy.id }, damage: 2 });

  manager.update(1000);
  assert.equal(timerDelay, 500);
  assert.equal(record.settled, false);
  timerCallback();
  manager.update(0);
  assert.equal(record.settled, true);
});
