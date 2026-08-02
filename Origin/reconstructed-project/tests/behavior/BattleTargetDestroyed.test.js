'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { GameEvents } = require('../../src/core/EventBus');

test('three real Mob0 contacts destroy aDou and emit the BattleState defeat entry once', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const results = [];
  h.eventBus.on(GameEvents.BATTLE_FINISHED, results, win => results.push(win));

  for (let i = 0; i < 3; i += 1) {
    const enemy = h.spawn(true);
    h.prepareContact(enemy);
    h.tick(80, 80);
    h.tick(50, 50);
    enemy.gameOver();
  }
  assert.equal(h.playerTarget.health, 0);
  assert.equal(h.playerTarget.alive, false);
  assert.equal(h.playerTarget.battleTargetState, 'DESTROYED');
  assert.deepEqual(results, [false]);
});
