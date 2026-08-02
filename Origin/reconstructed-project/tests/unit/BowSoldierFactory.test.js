'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');
const { BowSoldier } = require('../../src/units/BowSoldier');
const { UnresolvedFriendlyUnitTypeError } = require('../../src/units/UnitFactory');

test('formal key 弓 and index 1 create BowSoldier through UnitFactory and UnitRegistry', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnBow({ gridX: 0, gridY: 6 });
  assert.ok(unit instanceof BowSoldier);
  assert.equal(h.unitFactory.byIndex.get(1).text, '弓');
  assert.equal(h.unitFactory.byText.get('弓').index, 1);
  assert.equal(h.unitRegistry.getUnit(unit.id), unit);
  assert.equal(h.unitFactory.creationLog.at(-1).unit, unit);
  assert.equal(h.objectPool.takeLog.some(entry => entry.kind === 'class' && entry.value === unit), true);
});

test('unreconstructed later ranged unit keys still fail explicitly', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  assert.throws(() => h.unitFactory.createByText('枪'), UnresolvedFriendlyUnitTypeError);
  assert.throws(() => h.unitFactory.createByIndex(2), UnresolvedFriendlyUnitTypeError);
});

test('pooled BowSoldier clears prior STOPPED listeners, target and attack state before reuse', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const first = h.spawnBow({ gridX: 0, gridY: 6 });
  h.spawnMobInRange(first, { offsetX: 160 });
  first.targets = h.enemyManager.queryTargets(40, 520, first.attackRange, true);
  first.attack();
  const oldId = first.id;
  assert.equal(first.animation.listenerCount(h.Laya.Event.STOPPED), 1);
  assert.equal(h.unitRegistry.removeSoldier(oldId), true);
  const second = h.spawnBow({ gridX: 0, gridY: 6 });
  assert.equal(second, first);
  assert.notEqual(second.id, oldId);
  assert.equal(second.targetId, -1);
  assert.equal(second.targets.length, 0);
  assert.equal(second.currentState, 'UnitIdle');
  assert.equal(second.animation.listenerCount(h.Laya.Event.STOPPED), 0);
});
