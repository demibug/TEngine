'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('Mob0 killed before arrival never moves or damages a battle target again', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  h.tick(800, 80);
  const positionAtDeath = { x: enemy.x, y: enemy.y };
  enemy.hit(enemy.health, { id: 17 });
  h.tick(99, 33);
  assert.equal(h.enemyManager.count, 1);
  assert.equal(h.playerTarget.health, 3);
  assert.deepEqual({ x: enemy.x, y: enemy.y }, positionAtDeath);
  h.tick(1, 1);
  assert.equal(h.enemyManager.count, 0);
  assert.equal(h.rewards.calls.length, 1);
});
