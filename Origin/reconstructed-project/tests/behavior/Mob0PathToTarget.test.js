'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('Mob0 traverses every confirmed path index, damages aDou once, then exits and recovers', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  const visited = [enemy.currentPathIndex];
  let elapsed = 0;
  let previous = enemy.currentPathIndex;
  while (h.enemyManager.count > 0 && elapsed < 40000) {
    h.tick(80, 80);
    elapsed += 80;
    if (h.enemyManager.count === 0) break;
    if (enemy.currentPathIndex !== previous) {
      assert.equal(enemy.currentPathIndex, previous + 1, 'Mob0 must not skip route points');
      previous = enemy.currentPathIndex;
      visited.push(previous);
    }
  }
  assert.equal(h.enemyManager.count, 0);
  assert.deepEqual(visited, Array.from({ length: 17 }, (_, i) => i));
  assert.equal(h.playerTarget.health, 2);
  assert.equal(h.playerTarget.damageLog.length, 1);
  assert.equal(h.objectPool.sizeByClass(enemy.constructor), 1);
  assert.equal(h.objectPool.sizeByKey('mob'), 1);
});
