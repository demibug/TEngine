'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('formal BowSoldier kills two Mob0 targets, retargets and leaves no projectile or spatial residue', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const first = h.spawnMobInRange(bow, { offsetX: 160, remainingPathDistance: 10 });
  const second = h.spawnMobInRange(bow, { offsetX: 220, offsetY: -20, remainingPathDistance: 20 });
  let firstRemovedAt = null;
  let secondRemovedAt = null;
  for (let elapsed = 80; elapsed <= 10000; elapsed += 80) {
    h.tick(80, 80);
    if (firstRemovedAt == null && !h.enemyManager.enemies.has(first.id)) firstRemovedAt = elapsed;
    if (secondRemovedAt == null && !h.enemyManager.enemies.has(second.id)) { secondRemovedAt = elapsed; break; }
  }
  h.tick(960, 80);
  assert.equal(first.health, 0);
  assert.equal(second.health, 0);
  assert.ok(firstRemovedAt > 0);
  assert.ok(secondRemovedAt > firstRemovedAt);
  assert.equal(h.rewards.calls.length, 2);
  assert.equal(h.projectileManager.activeCount, 0);
  assert.equal(h.enemyManager.enemies.size, 0);
  assert.equal(h.enemyManager.cellToEnemyIds.size, 0);
  assert.equal(h.enemyManager.enemyIdToCell.size, 0);
  assert.equal(h.unitRegistry.getUnit(bow.id), bow);
  assert.equal(bow.currentState, 'UnitIdle');
  assert.equal(globalThis.wx, undefined);
  assert.equal(globalThis.tt, undefined);
});
