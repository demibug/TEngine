'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('battle cleanup unregisters friendly updates, cancels attacks and empties UnitRegistry', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const first = h.spawnKnife({ gridX: 0, gridY: 6 });
  const second = h.spawnKnife({ gridX: 1, gridY: 6 });
  const enemy = h.spawnMobInRange(first);
  h.tick(880, 80);
  assert.equal(h.attackTimeline.started.length > 0, true);
  h.unitRegistry.gameOver();
  h.battleManager.gameOver();
  h.enemyManager.gameOver();
  h.tick(600, 80);
  assert.equal(h.unitRegistry.count, 0);
  assert.equal(h.enemyManager.count, 0);
  assert.equal(h.gameLoop.isRegistered('BattleMgr'), false);
  assert.equal(h.gameLoop.isRegistered(`soldier_${first.id}`), false);
  assert.equal(h.gameLoop.isRegistered(`soldier_${second.id}`), false);
  assert.equal(enemy.health, 6, 'pending friendly attack was removed before damage settlement');
});
