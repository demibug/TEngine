'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('reused arrow has a new ID and no prior attacker, target, progress, hit set or event state', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const firstTarget = h.spawnMobInRange(bow, { offsetX: 220 });
  const first = h.createArrow({ attacker: bow, target: firstTarget, speedScale: 0.5 });
  const oldId = first.projectileId;
  h.tick(80, 80);
  first.renderNode.on('stale-event', first, () => {});
  first.hitEnemyIds.add(9999);
  first.requestRemove(true);
  h.projectileManager.update(80);
  assert.equal(first.projectileId, -1);
  assert.equal(h.objectPool.sizeByKey(first.poolKey), 1);

  const secondTarget = h.spawnMobInRange(bow, { offsetX: 300 });
  const second = h.createArrow({ attacker: bow, target: secondTarget, speedScale: 1.75 });
  assert.equal(second, first);
  assert.notEqual(second.projectileId, oldId);
  assert.equal(second.attacker, bow);
  assert.deepEqual(second.hitStrategy.targetIds, [secondTarget.id]);
  assert.equal(second.movement.targetId, secondTarget.id);
  assert.equal(second.movement.progress, 0);
  assert.equal(second.hitEnemyIds.size, 0);
  assert.equal(second.renderNode.listenerCount('stale-event'), 0);
  assert.equal(second.renderNode.visible, true);
  assert.equal(second.recovered, false);
});
