'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('micro battle creates a formal knife, kills two Mob0 sequentially and cleans both sides', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const first = h.spawnMobInRange(unit, { offsetX: 40 });
  const second = h.spawnMobInRange(unit, { offsetX: 100 });
  const elapsed = h.runUntil(() => h.enemyManager.count === 0, { timeoutMs: 7000, stepMs: 80 });
  assert.equal(first.health, 0);
  assert.equal(second.health, 0);
  assert.equal(h.rewards.calls.length, 2);
  assert.equal(h.attackTimeline.settled.filter(record => record.settled).length, 4);
  assert.equal(h.objectPool.sizeByClass(first.constructor), 2);
  assert.equal(h.objectPool.sizeByKey('mob'), 2);
  assert.equal(h.unitRegistry.count, 1);
  assert.equal(unit.isActive, true);
  h.unitRegistry.gameOver();
  assert.equal(h.unitRegistry.count, 0);
  assert.equal(h.objectPool.sizeByKey('soldier'), 1);
  assert.ok(elapsed >= 4000 && elapsed <= 6000);
  assert.equal(globalThis.wx, undefined);
  assert.equal(globalThis.tt, undefined);
});
