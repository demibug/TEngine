'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('removing a friendly unit during its delayed attack prevents stale damage', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const enemy = h.spawnMobInRange(unit);
  h.tick(880, 80);
  assert.equal(h.attackTimeline.started.length, 1);
  const oldId = unit.id;
  h.unitRegistry.removeSoldier(oldId);
  h.tick(600, 80);
  assert.equal(enemy.health, 6);
  assert.equal(h.unitRegistry.getUnit(oldId), undefined);
  assert.equal(h.gameLoop.isRegistered(`soldier_${oldId}`), false);
});
