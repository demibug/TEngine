'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('multiple friendly units may attack the same Mob0; no reservation limiter is applied', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const first = h.spawnKnife({ gridX: 0, gridY: 6 });
  const second = h.spawnKnife({ gridX: 1, gridY: 6 });
  const enemy = h.spawnMobInRange(first, { offsetX: 80 });
  h.runUntil(() => enemy.health === 0, { timeoutMs: 2500, stepMs: 80 });
  assert.equal(h.attackTimeline.started.length, 2);
  assert.deepEqual(new Set(h.attackTimeline.started.map(record => record.attackerId)), new Set([first.id, second.id]));
  assert.deepEqual(enemy.damageContributors, [first.id, second.id]);
  assert.equal(h.placementReservations.size, 0);
});
