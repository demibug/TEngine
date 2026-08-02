'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('BowSoldier removal cancels pending STOPPED so old animation cannot spawn an arrow', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  h.spawnMobInRange(bow, { offsetX: 160 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();
  assert.equal(h.animationDriver.activeCount, 1);
  const id = bow.id;
  assert.equal(h.unitRegistry.removeSoldier(id), true);
  h.tick(1000, 80);
  assert.equal(h.animationDriver.activeCount, 0);
  assert.equal(h.projectileFactory.creationLog.length, 0);
  assert.equal(h.objectPool.sizeByClass(bow.constructor), 1);
});
