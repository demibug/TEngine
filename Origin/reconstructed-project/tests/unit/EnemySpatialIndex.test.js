'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { GameEvents } = require('../../src/core/EventBus');

test('EnemyManager registers, moves and removes the exact grid index entry', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  assert.equal(h.enemyManager.hasSpatialRegistration(enemy.id), true);
  assert.equal(h.enemyManager.spatialKeyFor(enemy.id), '0_9');
  enemy.visual.pos(320, 320);
  h.eventBus.event(GameEvents.ENEMY_GRID_LEFT, enemy.id, enemy);
  assert.equal(h.enemyManager.spatialKeyFor(enemy.id), '4_4');
  enemy.hit(enemy.health);
  h.tick(100, 100);
  assert.equal(h.enemyManager.hasSpatialRegistration(enemy.id), false);
  assert.equal(h.enemyManager.spatialCellCount, 0);
});

test('circle query uses spatial candidates and preserves insertion order', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const first = h.spawn(true);
  const second = h.spawn(true);
  first.visual.pos(80, 80);
  second.visual.pos(90, 80);
  h.eventBus.event(GameEvents.ENEMY_GRID_LEFT, first.id, first);
  h.eventBus.event(GameEvents.ENEMY_GRID_LEFT, second.id, second);
  const ids = h.enemyManager.queryTargets(100, 100, 100, true).map(item => item.id);
  assert.deepEqual(ids, [first.id, second.id]);
});
