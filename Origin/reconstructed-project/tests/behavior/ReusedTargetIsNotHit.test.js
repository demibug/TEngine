'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('pooled Mob0 reused with a new ID is not hit by an arrow targeting its prior lifecycle', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const original = h.spawnMobInRange(bow, { offsetX: 400 });
  const oldId = original.id;
  h.createArrow({ attacker: bow, target: original, speedScale: 0.5 });
  h.tick(80, 80);
  const oldPosition = { x: original.visual.x, y: original.visual.y };
  original.gameOver();
  const reused = h.enemyHarness ? null : h.spawnMobInRange(bow, { offsetX: 400 });
  // createEnemyRuntimeHarness class pool should reuse the same logic object with a new ID.
  assert.equal(reused, original);
  assert.notEqual(reused.id, oldId);
  h.placeEnemy(reused, oldPosition.x, oldPosition.y, { remainingPathDistance: 0 });
  h.runUntil(() => h.projectileManager.activeCount === 0, { timeoutMs: 4000, stepMs: 80 });
  assert.equal(reused.health, 6);
  assert.equal(h.projectileEffects.calls.length, 0);
});
