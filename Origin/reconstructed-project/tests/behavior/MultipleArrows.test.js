'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('multiple active arrows update and unregister without skipping array entries', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mobs = [180, 260, 340].map((offset, index) => h.spawnMobInRange(bow, { offsetX: offset, offsetY: index * 30 }));
  const arrows = mobs.map(mob => h.createArrow({ attacker: bow, target: mob, damage: 2 }));
  assert.equal(h.projectileManager.activeCount, 3);
  h.runUntil(() => h.projectileManager.activeCount === 0, { timeoutMs: 4000, stepMs: 80 });
  assert.deepEqual(mobs.map(mob => mob.health), [4, 4, 4]);
  assert.deepEqual(arrows.map(arrow => arrow.projectileId), [-1, -1, -1]);
  assert.equal(h.projectileEffects.calls.length, 3);
});
