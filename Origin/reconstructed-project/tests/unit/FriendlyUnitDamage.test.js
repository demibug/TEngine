'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('knife hit subtracts the exact recovered damage through EnemyBase.hit', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const enemy = h.spawnMobInRange(unit);
  unit.performKnifeAttack();
  h.tick(500, 500);
  assert.equal(enemy.maxHealth, 6);
  assert.equal(enemy.health, 3);
  assert.deepEqual(enemy.damageContributors, [unit.id]);
  assert.deepEqual(h.knifeEffects.calls.map(call => call[0]), ['startKnifeAttack', 'showKnifeHit']);
});

test('base friendly soldier damage is explicitly unsupported because rc/td define no health contract', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  assert.throws(() => unit.receiveDamage(1), error => error.name === 'UnsupportedFriendlyUnitDamageError');
  assert.equal('health' in unit, false);
  assert.equal('maxHealth' in unit, false);
});
