'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('formal knife and bow share BattleManager and jointly kill Mob0 without regression', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const knife = h.spawnKnife({ gridX: 0, gridY: 6 });
  const bow = h.spawnBow({ gridX: 0, gridY: 5 });
  const mob = h.spawnMobInRange(knife, { offsetX: 80, offsetY: -40, remainingPathDistance: 10 });
  h.runUntil(() => !h.enemyManager.enemies.has(mob.id), { timeoutMs: 6000, stepMs: 80 });
  assert.equal(mob.health, 0);
  assert.ok(h.knifeEffects.calls.some(call => call[0] === 'showKnifeHit'));
  assert.ok(h.projectileEffects.calls.length >= 1);
  assert.equal(h.unitRegistry.getUnit(knife.id), knife);
  assert.equal(h.unitRegistry.getUnit(bow.id), bow);
  assert.equal(h.rewards.calls.length, 1);
});

test('UnitRegistry preserves bow-then-knife insertion order and both cleanly return to their pools', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 5 });
  const knife = h.spawnKnife({ gridX: 0, gridY: 6 });
  assert.deepEqual([...h.unitRegistry.soldiers.values()], [bow, knife]);
  h.unitRegistry.gameOver();
  assert.equal(h.unitRegistry.count, 0);
  assert.equal(h.objectPool.sizeByClass(bow.constructor), 1);
  assert.equal(h.objectPool.sizeByClass(knife.constructor), 1);
});
