'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('ProjectileManager registers bulletMgr before BattleMgr and resolves IDs', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const keys = h.gameLoop.registrationKeys();
  assert.ok(keys.indexOf('enemyMgr') < keys.indexOf('bulletMgr'));
  assert.ok(keys.indexOf('bulletMgr') < keys.indexOf('BattleMgr'));
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mob = h.spawnMobInRange(bow, { offsetX: 300 });
  const arrow = h.createArrow({ attacker: bow, target: mob });
  assert.equal(h.projectileManager.getById(arrow.projectileId), arrow);
});

test('reverse traversal permits synchronous removal of every active arrow', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const mobs = [120, 200, 280].map((offset, index) => h.spawnMobInRange(bow, { offsetX: offset, offsetY: index * 20 }));
  const arrows = mobs.map(mob => h.createArrow({ attacker: bow, target: mob }));
  for (const arrow of arrows) arrow.requestRemove(true);
  h.projectileManager.update(80);
  assert.equal(h.projectileManager.activeCount, 0);
  assert.deepEqual(h.projectileManager.removalLog.map(entry => entry.projectileId), [2, 1, 0]);
});
