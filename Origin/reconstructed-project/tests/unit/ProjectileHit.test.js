'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('SimpleDynamicArrow applies the launch-time damage snapshot exactly once with BowSoldier as attacker', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 240 });
  const arrow = h.createArrow({ attacker: bow, target: mob, damage: bow.attackDamage, speedScale: 0.25 });
  const launchDamage = arrow.damage;
  bow.levelUp(1, false);
  assert.equal(launchDamage, 2);
  assert.equal(bow.attackDamage, 3);
  assert.equal(arrow.hit(mob), true);
  assert.equal(mob.health, 4);
  assert.equal(mob.damageContributors.includes(bow.id), true);
  assert.equal(arrow.hit(mob), false);
  assert.equal(mob.health, 4);
  assert.equal(h.projectileEffects.calls.length, 1);
  assert.equal(h.projectileEffects.calls[0].damage, 2);
});

test('manager-delivered arrow hit is single-target and non-piercing', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const first = h.spawnMobInRange(bow, { offsetX: 160, offsetY: 0 });
  const second = h.spawnMobInRange(bow, { offsetX: 160, offsetY: 20 });
  h.createArrow({ attacker: bow, target: first, damage: 2 });
  h.runUntil(() => h.projectileManager.activeCount === 0, { timeoutMs: 3000, stepMs: 80 });
  assert.equal(first.health, 4);
  assert.equal(second.health, 6);
  assert.equal(h.projectileEffects.calls.length, 1);
});
