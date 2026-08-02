'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('Mob0 leaving range before the attack pass makes the fixed knife return to idle without firing', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const enemy = h.spawnMobInRange(unit, { offsetX: 80 });
  h.tick(800, 80);
  assert.equal(unit.currentState, 'UnitAttack');
  h.placeEnemy(enemy, 400, 480);
  h.tick(80, 80);
  assert.equal(unit.currentState, 'UnitIdle');
  assert.equal(h.attackTimeline.started.length, 0);
  assert.equal(enemy.health, 6);
  assert.equal(unit.displayObject.x, 0, 'knife soldier is fixed in its placed grid');
  assert.equal(unit.displayObject.y, 480);
});
