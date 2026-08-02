'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { GameEvents } = require('../../src/core/EventBus');

test('target queries reject the opposite lane and dead enemies', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const playerLane = h.spawn(true);
  const opponentLane = h.spawn(false);
  playerLane.visual.pos(100, 100);
  opponentLane.visual.pos(100, 100);
  h.eventBus.event(GameEvents.ENEMY_GRID_LEFT, playerLane.id, playerLane);
  h.eventBus.event(GameEvents.ENEMY_GRID_LEFT, opponentLane.id, opponentLane);
  assert.deepEqual(h.enemyManager.queryTargets(100, 100, 100, true).map(x => x.id), [playerLane.id]);
  assert.deepEqual(h.enemyManager.queryTargets(100, 100, 100, false).map(x => x.id), [opponentLane.id]);
  playerLane.hit(playerLane.health);
  assert.deepEqual(h.enemyManager.queryTargets(100, 100, 100, true), []);
});

test('moving outside the query radius removes an enemy from subsequent selection', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  enemy.visual.pos(80, 80);
  h.eventBus.event(GameEvents.ENEMY_GRID_LEFT, enemy.id, enemy);
  assert.equal(h.enemyManager.queryTargets(100, 100, 100, true).length, 1);
  enemy.visual.pos(560, 720);
  h.eventBus.event(GameEvents.ENEMY_GRID_LEFT, enemy.id, enemy);
  assert.equal(h.enemyManager.queryTargets(100, 100, 100, true).length, 0);
});
