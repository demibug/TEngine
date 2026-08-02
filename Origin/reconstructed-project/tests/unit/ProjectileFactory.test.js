'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');
const { SimpleDynamicArrow } = require('../../src/projectiles/SimpleDynamicArrow');

test('SimpleDynamicArrow uses the formal type, appearance and composite pool key', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 240 });
  const arrow = h.createArrow({ attacker: bow, target: mob });
  assert.ok(arrow instanceof SimpleDynamicArrow);
  assert.notEqual(arrow, arrow.renderNode);
  assert.equal(arrow.projectileId, 0);
  assert.equal(arrow.poolKey, 'bullet_pool_SimpleDynamicArrow_弓箭小兵箭矢');
  assert.equal(arrow.renderNode.width, 22);
  assert.equal(arrow.renderNode.height, 72);
  assert.equal(arrow.renderNode.anchorX, 0.5);
  assert.equal(arrow.renderNode.anchorY, 0.9);
  assert.equal(arrow.imageNode.skin, 'resources/img/weapon/arrow_0.png');
  assert.equal(arrow.renderNode.parent, h.parent);
  assert.equal(arrow.active, true);
});

test('ProjectileFactory rejects an unreconstructed projectile key instead of falling back', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  assert.throws(
    () => h.projectileFactory.produce({ type: 'UnknownArrow', appearance: { label: 'unknown' } }),
    error => error && error.name === 'UnresolvedProjectileTypeError',
  );
});
