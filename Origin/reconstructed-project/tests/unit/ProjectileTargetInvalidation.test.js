'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('missing target at initialization creates a hidden immediate-removal arrow without damage', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 200 });
  const id = mob.id;
  mob.gameOver();
  const movementTarget = { id };
  assert.equal(h.enemyManager.enemies.has(id), false);
  // Use Bow launch path to preserve the original invalid-target contract.
  bow.targets = [movementTarget];
  bow.targetId = id;
  const arrow = bow.launchArrow();
  assert.equal(arrow.requestedRemoval, true);
  assert.equal(arrow.immediateRemoval, true);
  assert.equal(arrow.renderNode.visible, false);
  h.tick(80, 80);
  assert.equal(h.projectileManager.activeCount, 0);
  assert.equal(h.projectileEffects.calls.length, 0);
});
