'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('a pending contact callback cannot leak into a reused Mob0 lifecycle', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const first = h.spawn(true);
  first.movementLocked = true;
  h.tick(500, 500);
  first.movementLocked = false;
  assert.equal(first.attackBattleTarget(), true);
  assert.equal(h.Laya.timer.taskCountFor(first), 1);
  first.gameOver();
  assert.equal(h.Laya.timer.taskCountFor(first), 0);
  const reused = h.spawn(false);
  assert.equal(reused, first);
  h.tick(100, 100);
  assert.equal(h.playerTarget.health, 3);
  assert.equal(h.opponentTarget.health, 3);
  assert.equal(reused.lastAttackTime, 0);
  assert.equal(reused._contactDamageScheduled, false);
});
