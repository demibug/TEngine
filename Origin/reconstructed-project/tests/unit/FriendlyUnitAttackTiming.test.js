'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('level-1 knife first enters attack at 800ms, starts next update and settles after recovered 500ms delay', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const enemy = h.spawnMobInRange(unit);
  h.tick(720, 80);
  assert.equal(h.attackTimeline.started.length, 0);
  h.tick(80, 80);
  assert.equal(unit.currentState, 'UnitAttack');
  assert.equal(h.attackTimeline.started.length, 0, 'state transition and attack invocation are separate BattleManager passes');
  h.tick(80, 80);
  assert.equal(h.attackTimeline.started.length, 1);
  assert.equal(h.attackTimeline.started[0].delayMs, 500);
  assert.equal(enemy.health, 6);
  h.tick(499, 499);
  assert.equal(enemy.health, 6);
  h.tick(1, 1);
  assert.equal(enemy.health, 3);
});

test('attack cooldown prevents multiple starts in one fixed update window', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  h.spawnMobInRange(unit);
  h.tick(880, 80);
  assert.equal(h.attackTimeline.started.length, 1);
  h.tick(799, 79);
  assert.equal(h.attackTimeline.started.length, 1);
  h.tick(1, 1);
  assert.equal(h.attackTimeline.started.length, 2);
});
