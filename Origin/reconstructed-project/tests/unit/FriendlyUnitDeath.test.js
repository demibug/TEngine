'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');
const { KnifeSoldier } = require('../../src/units/KnifeSoldier');

test('UnitRegistry removal performs the original gameOver/unregister/recover lifecycle once', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const id = unit.id;
  assert.equal(h.unitRegistry.removeSoldier(id), true);
  assert.equal(h.unitRegistry.removeSoldier(id), false);
  assert.equal(h.unitRegistry.getUnit(id), undefined);
  assert.equal(h.gameLoop.isRegistered(`soldier_${id}`), false);
  assert.equal(unit.inPool, true);
  assert.equal(unit.displayObject, null);
  assert.equal(h.objectPool.sizeByClass(KnifeSoldier), 1);
  assert.equal(h.objectPool.sizeByKey('soldier'), 1);
});

test('removal cancels pending attack callbacks so a recovered unit cannot damage later', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const enemy = h.spawnMobInRange(unit);
  unit.performKnifeAttack();
  h.unitRegistry.removeSoldier(unit.id);
  h.tick(500, 500);
  assert.equal(enemy.health, 6);
  assert.equal(h.attackTimeline.settled.length, 0, 'Laya.timer.clearAll removes the callback before settlement');
});
