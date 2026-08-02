'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('UnitRegistry preserves Map insertion order, ID lookup and battle occupancy', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const first = h.spawnKnife({ gridX: 0, gridY: 6 });
  const second = h.spawnKnife({ gridX: 1, gridY: 6 });
  assert.deepEqual([...h.unitRegistry.soldiers.keys()], [first.id, second.id]);
  assert.equal(h.unitRegistry.getUnit(first.id), first);
  assert.equal(h.unitRegistry.hasBattleOccupant(true, 0, 6), true);
  assert.equal(h.unitRegistry.hasBattleOccupant(false, 0, 6), false);
  assert.equal(h.unitRegistry.playerSoldierCount, 2);
  assert.throws(() => h.spawnKnife({ gridX: 0, gridY: 6 }), /occupied/);
});

test('UnitRegistry cleanup copies IDs before mutation and recovers each unit exactly once', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const units = [
    h.spawnKnife({ gridX: 0, gridY: 6 }),
    h.spawnKnife({ gridX: 1, gridY: 6 }),
    h.spawnKnife({ side: false, gridX: 7, gridY: 3 }),
  ];
  h.unitRegistry.gameOver();
  assert.equal(h.unitRegistry.count, 0);
  assert.equal(units.every(unit => unit.inPool), true);
  const classRecoveries = h.objectPool.recoverLog.filter(entry => entry.kind === 'class' && units.includes(entry.value));
  assert.equal(classRecoveries.length, 3);
});
