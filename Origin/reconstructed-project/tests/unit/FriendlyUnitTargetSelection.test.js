'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('knife queries the real EnemyManager spatial index and chooses by recovered distance loop', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const firstInserted = h.spawnMobInRange(unit, { offsetX: 30 });
  const secondInserted = h.spawnMobInRange(unit, { offsetX: 0 });
  const candidates = h.enemyManager.queryTargets(40, 520, unit.attackRange, true);
  assert.deepEqual(candidates.map(item => item.id), [firstInserted.id, secondInserted.id]);
  const record = unit.performKnifeAttack();
  assert.equal(record.targetId, secondInserted.id, 'second candidate center is closer under original asymmetric comparison');
});

test('target query rejects the opposite lane and dead targets', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ side: true, gridX: 0, gridY: 6 });
  const sameLane = h.spawnMobInRange(unit, { side: true, offsetX: 80 });
  h.spawnMobInRange(unit, { side: false, offsetX: 0 });
  sameLane.hit(sameLane.health, { id: 999 });
  const candidates = h.enemyManager.queryTargets(40, 520, unit.attackRange, true);
  assert.deepEqual(candidates, []);
});
