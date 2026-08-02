'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { EnemyRuntimeState } = require('../../src/entities/EnemyBase');

test('class and visual pools reuse Mob0 with a clean lifecycle state', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const first = h.spawn(true);
  const firstVisual = first.visual;
  const firstId = first.id;
  first.currentPathIndex = 7;
  first.lastAttackTime = 900;
  first.damageContributors.push(1001);
  first.on('custom', first, () => {});
  first.movementLocked = true;
  h.Laya.timer.once(999, first, () => { throw new Error('stale timer executed'); });
  first.hit(first.health, { id: 1001 });
  h.tick(100, 100);
  assert.equal(firstVisual.listenerCount(), 0);
  assert.equal(h.Laya.timer.taskCountFor(first), 0);

  const second = h.spawn(false);
  assert.equal(second, first);
  assert.equal(second.visual, firstVisual);
  assert.notEqual(second.id, firstId);
  assert.equal(second.health, second.maxHealth);
  assert.equal(second.currentState, EnemyRuntimeState.MOVING);
  assert.equal(second.deathStarted, false);
  assert.equal(second.currentPathIndex, 0);
  assert.equal(second.lastAttackTime, 0);
  assert.deepEqual(second.damageContributors, []);
  assert.equal(second.movementLocked, false);
  assert.equal(second.isPlayerLane, false);
  assert.equal(h.enemyManager.count, 1);
  assert.equal(h.enemyManager.hasSpatialRegistration(second.id), true);
  assert.equal(firstVisual.listenerCount(), 0);
});
