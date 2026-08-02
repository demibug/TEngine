'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { runFriendlyUnitMicroBattle } = require('../../tools/run-friendly-unit-simulation');

test('development micro-battle exposes the complete formal KnifeSoldier → Mob0 evidence boundary', () => {
  const result = runFriendlyUnitMicroBattle();
  assert.equal(result.completed, true);
  assert.equal(result.unit.originalSymbol, 'tb.zx[0]');
  assert.equal(result.unit.registrationKey, '刀');
  assert.equal(result.unit.factoryIndex, 0);
  assert.equal(result.formalUnitConfig.attackDamage, 3);
  assert.equal(result.formalUnitConfig.rangeCells, 1.5);
  assert.equal(result.formalUnitConfig.attackIntervalSeconds, 0.8);
  assert.equal(result.timeline.firstTargetDetectedAt, 80);
  assert.equal(result.timeline.firstAttackStateAt, 800);
  assert.equal(result.timeline.firstAttackStartedAt, 880);
  assert.equal(result.timeline.firstDamageSettledAt, 1440);
  assert.equal(result.timeline.retargetedAt, 2480);
  assert.equal(result.timeline.postCombatIdleAt, 4080);
  assert.deepEqual(result.attacks.damageSequence, [3, 3, 3, 3]);
  assert.deepEqual(result.retargetResult.targetIdsByAttack, [
    result.retargetResult.firstEnemyId,
    result.retargetResult.firstEnemyId,
    result.retargetResult.secondEnemyId,
    result.retargetResult.secondEnemyId,
  ]);
  assert.equal(result.managersBeforeFriendlyCleanup.enemyManagerCount, 0);
  assert.equal(result.managersBeforeFriendlyCleanup.unitRegistryCount, 1);
  assert.equal(result.managersBeforeFriendlyCleanup.spatialCellCount, 0);
  assert.equal(result.managersBeforeFriendlyCleanup.spatialEnemyRecordCount, 0);
  assert.equal(result.friendlyCleanup.unitRegistryCount, 0);
  assert.equal(result.friendlyCleanup.logicPool, 1);
  assert.equal(result.friendlyCleanup.visualPool, 1);
  assert.equal(result.mobPools.logic, 2);
  assert.equal(result.mobPools.visual, 2);
  assert.equal(result.networkRequests, 0);
  assert.equal(result.nativePlatformCalls, 0);
});
