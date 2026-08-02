'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('BattleManager drives BowSoldier through STOPPED, arrow creation, flight and first hit', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 160 });
  let createdAt = null;
  let damagedAt = null;
  for (let elapsed = 80; elapsed <= 3000; elapsed += 80) {
    h.tick(80, 80);
    if (createdAt == null && h.projectileFactory.creationLog.length > 0) createdAt = elapsed;
    if (damagedAt == null && mob.health < 6) { damagedAt = elapsed; break; }
  }
  assert.equal(createdAt, 1440);
  assert.equal(damagedAt, 1840);
  assert.equal(mob.health, 4);
  assert.equal(h.projectileEffects.calls.length, 1);
  assert.equal(h.projectileEffects.calls[0].damage, 2);
});
