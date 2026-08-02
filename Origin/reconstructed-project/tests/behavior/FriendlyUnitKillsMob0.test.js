'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');
const { GameEvents } = require('../../src/core/EventBus');

test('one formal level-1 knife soldier kills first-wave Mob0 in two delayed hits', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6 });
  const enemy = h.spawnMobInRange(unit);
  const elapsed = h.runUntil(() => h.enemyManager.count === 0, { timeoutMs: 4000, stepMs: 80 });
  assert.equal(enemy.health, 0);
  assert.equal(h.attackTimeline.started.length, 2);
  assert.equal(h.attackTimeline.settled.filter(record => record.settled).length, 2);
  assert.equal(h.rewards.calls.length, 1);
  assert.equal(h.objectPool.sizeByClass(enemy.constructor), 1);
  assert.equal(h.objectPool.sizeByKey('mob'), 1);
  const killEvent = h.events.find(event => event.type === GameEvents.ENEMY_KILLED_BY);
  assert.ok(killEvent);
  assert.equal(killEvent.args[0], unit.id);
  assert.ok(elapsed >= 2200 && elapsed <= 2600);
});
