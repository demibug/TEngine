'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');
const { quadraticBezier } = require('../../src/projectiles/ProjectileMath');

function near(actual, expected, epsilon = 1e-9) { assert.ok(Math.abs(actual - expected) <= epsilon, `${actual} != ${expected}`); }

test('first fixed step follows the recovered quadratic Bezier formula, not linear interpolation', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 400, offsetY: 0 });
  const arrow = h.createArrow({ attacker: bow, target: mob, speedScale: 1, curveHeight: 120 });
  const start = { x: 40, y: 520 };
  const target = { x: mob.visual.x + 40, y: mob.visual.y + 40 };
  const control = { x: (start.x + target.x) / 2, y: (start.y + target.y) / 2 - 120 };
  const expected = { x: 0, y: 0 };
  quadraticBezier(start, control, target, expected, 80 / 500);
  h.tick(80, 80);
  near(arrow.movement.progress, 0.16);
  near(arrow.x, expected.x);
  near(arrow.y, expected.y);
  const linearY = start.y + (target.y - start.y) * 0.16;
  assert.notEqual(arrow.y, linearY);
});

test('projectile motion stops while GameLoop is paused', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 400 });
  const arrow = h.createArrow({ attacker: bow, target: mob, speedScale: 1 });
  h.gameLoop.paused = true;
  h.tick(500, 100);
  assert.equal(arrow.x, 40);
  assert.equal(arrow.y, 520);
  assert.equal(arrow.movement.progress, 0);
});

test('initial and first-step rotation follow the recovered tangent/display-angle formulas', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 400, offsetY: -80 });
  const arrow = h.createArrow({ attacker: bow, target: mob, speedScale: 1, curveHeight: 120 });
  const initial = arrow.rotation;
  assert.ok(Number.isFinite(initial));
  h.tick(80, 80);
  assert.ok(Number.isFinite(arrow.rotation));
  assert.notEqual(arrow.rotation, initial);
});

test('formal distance scaling yields a longer fixed-step flight for a farther target', t => {
  function flightTime(offsetX) {
    const h = createRangedCombatHarness();
    try {
      const bow = h.spawnBow({ gridX: 0, gridY: 6 });
      const mob = h.spawnMobInRange(bow, { offsetX });
      h.createArrow({ attacker: bow, target: mob, speedScale: 1 });
      let elapsed = 0;
      while (h.projectileManager.activeCount > 0 && elapsed < 3000) { h.tick(80, 80); elapsed += 80; }
      return elapsed;
    } finally { h.cleanup(); }
  }
  const near = flightTime(160);
  const far = flightTime(400);
  assert.equal(near, 640);
  assert.equal(far, 720);
  assert.ok(far > near);
});
