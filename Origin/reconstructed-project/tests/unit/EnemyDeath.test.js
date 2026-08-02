'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { EnemyRuntimeState } = require('../../src/entities/EnemyBase');

test('death stops update, waits for the confirmed 100ms fade, then unregisters and recovers', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  enemy.hit(enemy.health, { id: 1 });
  assert.equal(enemy.currentState, EnemyRuntimeState.DEAD);
  assert.equal(h.enemyManager.count, 1);
  const before = { x: enemy.x, y: enemy.y, updates: enemy.updateCount };
  h.tick(99, 33);
  assert.deepEqual({ x: enemy.x, y: enemy.y, updates: enemy.updateCount }, before);
  assert.equal(h.enemyManager.count, 1);
  h.tick(1, 1);
  assert.equal(h.enemyManager.count, 0);
  assert.equal(h.objectPool.sizeByClass(enemy.constructor), 1);
  assert.equal(h.objectPool.sizeByKey('mob'), 1);
  assert.equal(h.rewards.calls.length, 1);
});

test('battle cleanup removes every enemy and all spatial entries', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  h.spawn(true); h.spawn(false); h.spawn(true);
  assert.equal(h.enemyManager.count, 3);
  h.enemyManager.gameOver();
  assert.equal(h.enemyManager.count, 0);
  assert.equal(h.enemyManager.spatialCellCount, 0);
  assert.equal(h.enemyManager.enemyIdToCell.size, 0);
});
