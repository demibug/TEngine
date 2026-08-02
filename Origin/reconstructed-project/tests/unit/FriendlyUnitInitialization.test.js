'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createFriendlyUnitCombatHarness } = require('../mocks/createFriendlyUnitCombatHarness');

test('knife initialization restores formal level-1 combat values and placement', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ side: true, gridX: 0, gridY: 6 });
  assert.equal(unit.unitText, '刀');
  assert.equal(unit.typeIndex, 0);
  assert.equal(unit.side, true);
  assert.equal(unit.level, 1);
  assert.equal(unit.attackDamage, 3);
  assert.equal(unit.attackRange, 120);
  assert.equal(unit.attackIntervalSeconds, 0.8);
  assert.equal(unit.animationKey, 'knife');
  assert.equal(unit.displayObject.x, 0);
  assert.equal(unit.displayObject.y, 480);
  assert.equal(unit.displayObject.parent, h.parent);
  assert.equal(unit.isActive, true);
  assert.equal(h.gameLoop.isRegistered(`soldier_${unit.id}`), true);
});

test('level multipliers use the recovered cumulative tables without changing range', t => {
  const h = createFriendlyUnitCombatHarness(); t.after(h.cleanup);
  const unit = h.spawnKnife({ gridX: 0, gridY: 6, level: 2 });
  assert.equal(unit.level, 2);
  assert.equal(unit.attackDamage, 4.5);
  assert.equal(unit.attackRange, 120);
  assert.ok(Math.abs(unit.attackIntervalSeconds - (0.8 / 1.5)) < 1e-12);
  assert.equal(unit.animationPlaybackRate, 1.5);
  assert.equal(unit.levelLabel.text, '2');
});
