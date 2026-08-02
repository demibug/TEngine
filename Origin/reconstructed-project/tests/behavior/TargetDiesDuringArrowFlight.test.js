'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('target removed during flight leaves stale endpoint but cannot be hit by old ID', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 400 });
  const arrow = h.createArrow({ attacker: bow, target: mob, speedScale: 0.5 });
  h.tick(80, 80);
  const staleEnd = { ...arrow.movement.targetPosition };
  const expectedStaleEnd = { x: mob.visual.x + 40, y: mob.visual.y + 40 };
  mob.gameOver();
  h.runUntil(() => h.projectileManager.activeCount === 0, { timeoutMs: 4000, stepMs: 80 });
  assert.deepEqual(arrow.movement, null);
  assert.equal(h.projectileEffects.calls.length, 0);
  assert.deepEqual(staleEnd, expectedStaleEnd);
});
