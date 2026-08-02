'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('three formal bow arrows kill 6-HP Mob0 through EnemyBase death and pool recovery', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 160 });
  h.runUntil(() => !h.enemyManager.enemies.has(mob.id), { timeoutMs: 6000, stepMs: 80 });
  assert.equal(mob.health, 0);
  assert.equal(h.projectileFactory.creationLog.length, 3);
  assert.equal(h.projectileEffects.calls.length, 3);
  assert.equal(h.rewards.calls.length, 1);
  assert.equal(h.enemyManager.hasSpatialRegistration(mob.id), false);
  assert.equal(h.objectPool.sizeByClass(mob.constructor), 1);
});
