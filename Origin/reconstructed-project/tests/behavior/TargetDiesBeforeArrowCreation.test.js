'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('target removed before STOPPED is revalidated and produces no erroneous damage', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 160 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();
  mob.gameOver();
  h.tick(560, 80);
  assert.equal(h.projectileFactory.creationLog.length, 1);
  assert.equal(h.projectileManager.activeCount, 1);
  h.tick(80, 80);
  assert.equal(h.projectileManager.activeCount, 0);
  assert.equal(h.projectileEffects.calls.length, 0);
});
