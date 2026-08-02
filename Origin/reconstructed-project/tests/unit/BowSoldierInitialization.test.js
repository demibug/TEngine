'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('level-1 BowSoldier restores formal configuration and animation contract', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  assert.equal(bow.unitText, '弓');
  assert.equal(bow.typeIndex, 1);
  assert.equal(bow.animationKey, 'bow');
  assert.equal(bow.attackDamage, 2);
  assert.equal(bow.attackRange, 280);
  assert.equal(bow.attackIntervalSeconds, 0.8);
  assert.equal(bow.initialAnimationPlaybackRate, 1.25);
  assert.equal(bow.projectileSpeedScale, 1.75);
  assert.equal(bow.attackReleaseEventMs, 650);
  assert.equal(bow.attackAnimationEndMs, 1000);
  assert.equal(bow.animation.initialRate, 1.25);
  assert.equal(bow.displayObject.x, 0);
  assert.equal(bow.displayObject.y, 480);
  assert.equal(bow.isActive, true);
});

test('BowSoldier level multipliers are read from the shared formal configuration', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6, level: 2 });
  assert.equal(bow.attackDamage, 3);
  assert.equal(bow.attackRange, 280);
  assert.ok(Math.abs(bow.attackIntervalSeconds - 0.8 / 1.5) < 1e-12);
});
