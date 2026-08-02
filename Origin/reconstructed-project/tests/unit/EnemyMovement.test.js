'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('80ms fixed step moves Mob0 by exactly 4px and the two sides move in opposite directions', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const player = h.spawn(true);
  const opponent = h.spawn(false);
  const playerY = player.y;
  const opponentY = opponent.y;
  h.tick(80, 80);
  assert.equal(player.y, playerY - 4);
  assert.equal(opponent.y, opponentY + 4);
  assert.equal(player.lastDeltaMs, 80);
  assert.equal(opponent.lastDeltaMs, 80);
});

test('equal elapsed time produces equal positions; 500ms clamp prevents a long-frame teleport', t => {
  const a = createEnemyRuntimeHarness(); t.after(a.cleanup);
  const enemyA = a.spawn(true);
  a.tick(400, 400);
  const yA = enemyA.y;

  const b = createEnemyRuntimeHarness(); t.after(b.cleanup);
  const enemyB = b.spawn(true);
  b.tick(400, 80);
  assert.equal(enemyB.y, yA);
  assert.equal(680 - yA, 20);

  const c = createEnemyRuntimeHarness(); t.after(c.cleanup);
  const enemyC = c.spawn(true);
  c.tick(1000, 1000);
  assert.equal(680 - enemyC.y, 25, 'GameLoop must clamp a 1000ms frame to 500ms');
});

test('pause stops enemy movement and resume does not replay unbounded accumulated time', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  const before = enemy.y;
  h.gameLoop.pause(false);
  h.tick(1000, 1000);
  assert.equal(enemy.y, before);
  h.gameLoop.resume();
  h.tick(80, 80);
  assert.equal(before - enemy.y, 25, 'resume applies at most the confirmed 500ms clamp');
});
