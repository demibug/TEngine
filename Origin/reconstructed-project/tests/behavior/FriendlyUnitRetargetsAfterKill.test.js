'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('knife clears a dead target, returns to idle and attacks the next indexed Mob0', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const first = h.spawnMobInRange(unit, { offsetX: 40 });
  const second = h.spawnMobInRange(unit, { offsetX: 100 });
  h.runUntil(() => h.enemyManager.getById(first.id) === null, { timeoutMs: 4000, stepMs: 80 });
  assert.equal(second.health, 6);
  h.runUntil(() => second.health < 6, { timeoutMs: 2500, stepMs: 80 });
  assert.equal(h.attackTimeline.started.some(record => record.targetId === first.id), true);
  assert.equal(h.attackTimeline.started.some(record => record.targetId === second.id), true);
  assert.equal(unit.inPool, false);
  assert.equal(unit.isActive, true);
});
