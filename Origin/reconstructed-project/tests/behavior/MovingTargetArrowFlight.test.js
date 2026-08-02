'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('SimpleDynamicArrow refreshes the target center every fixed step', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 400 });
  const arrow = h.createArrow({ attacker: bow, target: mob, speedScale: 0.5 });
  h.tick(80, 80);
  const before = { x: arrow.movement.targetPosition.x, y: arrow.movement.targetPosition.y, arrowX: arrow.x, arrowY: arrow.y };
  h.placeEnemy(mob, mob.visual.x - 80, mob.visual.y - 160, { remainingPathDistance: 20 });
  h.tick(80, 80);
  assert.notEqual(arrow.movement.targetPosition.x, before.x);
  assert.notEqual(arrow.movement.targetPosition.y, before.y);
  assert.equal(arrow.movement.targetPosition.x, mob.visual.x + 40);
  assert.equal(arrow.movement.targetPosition.y, mob.visual.y + 40);
  assert.notDeepEqual({ x: arrow.x, y: arrow.y }, { x: before.arrowX, y: before.arrowY });
});

test('arrow continues tracking and can hit after target leaves the BowSoldier attack radius', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 200 });
  h.createArrow({ attacker: bow, target: mob, speedScale: 0.5 });
  h.tick(80, 80);
  h.placeEnemy(mob, bow.displayObject.x + bow.attackRange + 240, bow.displayObject.y - 160, { remainingPathDistance: 10 });
  assert.equal(h.enemyManager.queryTargets(40, 520, bow.attackRange, true).some(item => item.id === mob.id), false);
  h.runUntil(() => h.projectileManager.activeCount === 0, { timeoutMs: 5000, stepMs: 80 });
  assert.equal(mob.health, 4);
  assert.equal(h.projectileEffects.calls.length, 1);
});
