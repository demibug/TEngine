'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('BowSoldier chooses minimum remaining-path distance Bm, not geometric nearest', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const near = h.spawnMobInRange(bow, { offsetX: 80, remainingPathDistance: 500 });
  const front = h.spawnMobInRange(bow, { offsetX: 200, remainingPathDistance: 40 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  assert.equal(bow.selectTarget(false).id, front.id);
  assert.notEqual(bow.selectTarget(false).id, near.id);
});

test('STOPPED-time validation skips removed or non-targetable candidates', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const first = h.spawnMobInRange(bow, { offsetX: 100, remainingPathDistance: 10 });
  const second = h.spawnMobInRange(bow, { offsetX: 180, remainingPathDistance: 20 });
  bow.targets = h.enemyManager.queryTargets(40, 520, bow.attackRange, true);
  first.targetable = false;
  assert.equal(bow.selectTarget(true).id, second.id);
});
