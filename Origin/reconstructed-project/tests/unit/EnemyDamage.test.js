'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('damage is subtracted exactly, hp UI follows the ratio and attacker IDs are retained once', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  const attacker = { id: 701 };
  assert.equal(enemy.health, 6);
  enemy.hit(2.5, attacker);
  enemy.hit(1, attacker);
  assert.equal(enemy.health, 2.5);
  assert.equal(enemy.healthBarImmediate.width, enemy.healthBarWidth * (2.5 / 6));
  assert.deepEqual(enemy.damageContributors, [701]);
  assert.equal(h.effects.calls.filter(call => call[0] === 'damageNumber').length, 2);
});

test('zero health triggers death once and further damage is rejected', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  let deadEvents = 0;
  enemy.on('onDead', enemy, () => { deadEvents += 1; });
  assert.equal(enemy.hit(999, { id: 9 }), true);
  assert.equal(enemy.health, 0);
  assert.equal(enemy.hit(1, { id: 10 }), false);
  assert.equal(deadEvents, 1);
  assert.equal(h.rewards.calls.length, 1);
});
