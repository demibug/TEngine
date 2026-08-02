'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');

test('Mob0 contact attack preserves the 500ms cooldown and 50ms delayed damage', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  assert.equal(enemy.attackBattleTarget(), false, 'first attack is gated until currTimer reaches 500ms');
  enemy.movementLocked = true;
  h.tick(500, 500);
  enemy.movementLocked = false;
  assert.equal(enemy.attackBattleTarget(), true);
  assert.equal(h.playerTarget.health, 3);
  assert.equal(enemy.attackBattleTarget(), false, 'same-timestamp duplicate attack is rejected');
  h.tick(49, 49);
  assert.equal(h.playerTarget.health, 3);
  h.tick(1, 1);
  assert.equal(h.playerTarget.health, 2);
  assert.equal(h.playerTarget.damageLog[0].sourceEnemyId, enemy.id);
});

test('path transition to the final point invokes the real contact attack entry', t => {
  const h = createEnemyRuntimeHarness(); t.after(h.cleanup);
  const enemy = h.spawn(true);
  h.prepareContact(enemy);
  h.tick(80, 80);
  assert.equal(enemy.currentPathIndex, enemy.path.length - 1);
  assert.equal(h.effects.calls.some(call => call[0] === 'contactAttack' && call[1] === enemy.id), true);
  h.tick(50, 50);
  assert.equal(h.playerTarget.health, 2);
});
