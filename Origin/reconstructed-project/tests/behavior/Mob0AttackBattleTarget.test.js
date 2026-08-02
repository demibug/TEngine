'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('player-lane Mob0 damages only the player aDou target', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  h.prepareContact(enemy);
  h.tick(80, 80);
  h.tick(50, 50);
  assert.equal(h.playerTarget.health, 2);
  assert.equal(h.opponentTarget.health, 3);
  assert.equal(h.gameData.battle.contactOccurred, true);
});

test('opponent-lane Mob0 damages only the opponent aDou target', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(false);
  h.prepareContact(enemy);
  h.tick(80, 80);
  h.tick(50, 50);
  assert.equal(h.playerTarget.health, 3);
  assert.equal(h.opponentTarget.health, 2);
});
