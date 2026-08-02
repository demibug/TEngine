'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('the first Mob0 pair moves in opposite directions and contacts its own lane target', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const playerEnemy = h.spawn(true);
  const opponentEnemy = h.spawn(false);
  const playerStartY = playerEnemy.y;
  const opponentStartY = opponentEnemy.y;
  h.tick(80, 80);
  assert.ok(playerEnemy.y < playerStartY);
  assert.ok(opponentEnemy.y > opponentStartY);

  playerEnemy.movementLocked = true;
  opponentEnemy.movementLocked = true;
  h.tick(500, 500);
  playerEnemy.movementLocked = false;
  opponentEnemy.movementLocked = false;
  h.placeAtPathIndex(playerEnemy, playerEnemy.path.length - 2);
  h.placeAtPathIndex(opponentEnemy, opponentEnemy.path.length - 2);
  h.tick(80, 80);
  h.tick(50, 50);
  assert.equal(h.playerTarget.health, 2);
  assert.equal(h.opponentTarget.health, 2);
});
