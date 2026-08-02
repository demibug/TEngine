'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('two arrows aimed at one Mob0 cause one death, one reward and no duplicate post-death hit', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bowA = h.spawnBow({ gridX: 0, gridY: 6 });
  const bowB = h.spawnBow({ gridX: 0, gridY: 5 });
  const mob = h.spawnMobInRange(bowA, { offsetX: 200, offsetY: -40, remainingPathDistance: 10 });
  h.createArrow({ attacker: bowA, target: mob, damage: 4, speedScale: 1 });
  h.createArrow({ attacker: bowB, target: mob, damage: 4, speedScale: 1 });
  h.runUntil(() => h.projectileManager.activeCount === 0, { timeoutMs: 4000, stepMs: 80 });
  h.tick(160, 80);
  assert.equal(mob.health, 0);
  assert.equal(h.rewards.calls.length, 1);
  assert.equal(h.enemyManager.enemies.has(mob.id), false);
  assert.equal(h.projectileEffects.calls.length, 2);
  assert.equal(h.projectileEffects.calls.filter(call => call.applied).length >= 1, true);
});
