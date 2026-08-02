'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('old delayed attack cannot leak into the reused knife soldier lifecycle', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const first = h.spawnKnife({ gridX: 0, gridY: 6 });
  const oldEnemy = h.spawnMobInRange(first);
  first.performKnifeAttack();
  const oldId = first.id;
  h.unitRegistry.removeSoldier(oldId);
  const reused = h.spawnKnife({ side: false, gridX: 7, gridY: 3 });
  assert.equal(reused, first);
  h.tick(500, 100);
  assert.equal(oldEnemy.health, 6);
  assert.equal(reused.targets.length, 0);
  assert.equal(reused.lastAttackTime, 0);
  assert.equal(reused.side, false);
  assert.equal(reused.currentState, 'UnitIdle');
});
