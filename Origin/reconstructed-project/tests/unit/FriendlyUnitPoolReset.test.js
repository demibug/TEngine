'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('pooled knife identity is reused with combat and placement state reset', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const first = h.spawnKnife({ side: true, gridX: 0, gridY: 6, level: 2 });
  const firstId = first.id;
  first.addAttackPower = 9;
  first.rangeBonusCells = 2;
  first.attackSpeedBonus = 3;
  first.lastAttackTime = 777;
  first.targets.push({ id: 9 });
  h.unitRegistry.removeSoldier(firstId);

  const second = h.spawnKnife({ side: false, gridX: 7, gridY: 3, level: 1 });
  assert.equal(second, first);
  assert.notEqual(second.id, firstId);
  assert.equal(second.side, false);
  assert.equal(second.level, 1);
  assert.equal(second.addAttackPower, 0);
  assert.equal(second.rangeBonusCells, 0);
  assert.equal(second.attackSpeedBonus, 0);
  assert.equal(second.lastAttackTime, 0);
  assert.deepEqual(second.targets, []);
  assert.equal(second.attackDamage, 3);
  assert.equal(second.attackRange, 120);
  assert.equal(second.currentState, 'UnitIdle');
  assert.equal(second.displayObject.x, 560);
  assert.equal(second.displayObject.y, 240);
});
