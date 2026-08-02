'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('battle cleanup removes enemy updates, timers, manager entries and spatial cells', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemies = [h.spawn(true), h.spawn(false), h.spawn(true)];
  h.tick(160, 80);
  h.enemyManager.gameOver();
  assert.equal(h.enemyManager.count, 0);
  assert.equal(h.enemyManager.spatialCellCount, 0);
  for (const enemy of enemies) {
    assert.equal(enemy.inPool, true);
    assert.equal(h.Laya.timer.taskCountFor(enemy), 0);
  }
  const updateCounts = enemies.map(enemy => enemy.updateCount);
  h.tick(400, 80);
  assert.deepEqual(enemies.map(enemy => enemy.updateCount), updateCounts);
});
