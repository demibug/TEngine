'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createRangedCombatHarness } = require('../mocks/createRangedCombatHarness');

test('pooled SimpleDynamicArrow identity is reused with a clean target, event and transform state', t => {
  const h = createRangedCombatHarness(); t.after(h.cleanup);
  const bow = h.spawnBow({ gridX: 0, gridY: 6 });
  const firstMob = h.spawnMobInRange(bow, { offsetX: 240 });
  const first = h.createArrow({ attacker: bow, target: firstMob });
  first.rotation = 123;
  first.hitEnemyIds.add(999);
  first.renderNode.on('custom', first, () => {});
  const oldId = first.projectileId;
  first.requestRemove(true);
  h.projectileManager.update(80);
  assert.equal(h.objectPool.sizeByKey(first.poolKey), 1);
  assert.equal(first.projectileId, -1);

  const secondMob = h.spawnMobInRange(bow, { offsetX: 300 });
  const second = h.createArrow({ attacker: bow, target: secondMob });
  assert.equal(second, first);
  assert.notEqual(second.projectileId, oldId);
  assert.equal(second.rotation !== 123, true);
  assert.equal(second.hitEnemyIds.size, 0);
  assert.equal(second.renderNode.listenerCount('custom'), 0);
  assert.equal(second.hitStrategy.targetIds[0], secondMob.id);
  assert.equal(second.renderNode.visible, true);
});
