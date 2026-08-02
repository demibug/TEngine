'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('battle cleanup removes every projectile and clears pending bow animation records', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 200 });
  h.createArrow({ attacker: bow, target: mob, speedScale: 0.25 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  bow.attack();
  assert.equal(h.projectileManager.activeCount, 1);
  assert.equal(h.animationDriver.activeCount, 1);
  h.unitRegistry.gameOver();
  h.projectileManager.gameOver();
  h.animationDriver.gameOver();
  assert.equal(h.unitRegistry.count, 0);
  assert.equal(h.projectileManager.activeCount, 0);
  assert.equal(h.animationDriver.activeCount, 0);
  assert.equal(h.objectPool.sizeByKey('bullet_pool_SimpleDynamicArrow_弓箭小兵箭矢'), 1);
});
