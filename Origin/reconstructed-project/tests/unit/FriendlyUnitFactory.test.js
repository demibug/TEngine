'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');
const { KnifeSoldier } = require('../../src/units/KnifeSoldier');
const { BowSoldier } = require('../../src/units/BowSoldier');
const { UnresolvedFriendlyUnitTypeError } = require('../../src/units/UnitFactory');

test('formal knife soldier is registered by original index 0 and text 刀', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const byIndex = h.unitFactory.createByIndex(0);
  assert.ok(byIndex instanceof KnifeSoldier);
  assert.equal(h.unitFactory.byIndex.get(0).text, '刀');
  assert.equal(h.unitFactory.byText.get('刀').index, 0);
  assert.equal(h.unitFactory.creationLog.at(-1).unit, byIndex);
});

test('formal bow registration occupies original index 1 while later base types remain explicit gaps', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  assert.equal(h.unitFactory.byIndex.get(1).text, '弓');
  assert.equal(h.unitFactory.byIndex.get(1).ClassType, BowSoldier);
  assert.equal(h.unitFactory.byText.get('弓').index, 1);
  assert.throws(() => h.unitFactory.createByText('枪'), UnresolvedFriendlyUnitTypeError);
  assert.throws(() => h.unitFactory.createByIndex(2), UnresolvedFriendlyUnitTypeError);
});

test('development placement still creates through UnitFactory and UnitRegistry', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  assert.equal(h.unitFactory.creationLog.length, 1);
  assert.equal(h.unitFactory.creationLog[0].unit, unit);
  assert.equal(h.unitRegistry.getUnit(unit.id), unit);
  assert.equal(h.objectPool.takeLog.some(entry => entry.kind === 'class' && entry.value === unit), true);
  assert.equal(h.objectPool.takeLog.some(entry => entry.kind === 'key' && entry.key === 'soldier'), true);
});
