'use strict';
const assert = require('node:assert/strict');

function prepareKnifeAndMob0(options = {}) {
  const { createFriendlyCombatHarness } = require('./createFriendlyCombatHarness');
  const harness = createFriendlyCombatHarness({ deathDurationMs: options.deathDurationMs ?? 0, ...options });
  const unit = harness.spawnKnife(options.unit || { side: true, gridX: 1, gridY: 6 });
  const enemy = harness.spawn(options.enemySide == null ? true : options.enemySide);
  harness.moveEnemyNearUnit(enemy, unit, options.offsetX ?? 40, options.offsetY ?? 0, options.lockMovement ?? true);
  return { harness, unit, enemy };
}

function advanceToFirstHit(harness) {
  harness.advanceCombat(800, 80);
  harness.advanceCombat(80, 80);
}

function advanceToKill(harness) {
  advanceToFirstHit(harness);
  harness.advanceCombat(800, 80);
  harness.Laya.timer.tick(0);
}

function assertNoNativeOrNetwork() {
  assert.equal(globalThis.wx, undefined);
  assert.equal(globalThis.tt, undefined);
}

module.exports = { prepareKnifeAndMob0, advanceToFirstHit, advanceToKill, assertNoNativeOrNetwork };
