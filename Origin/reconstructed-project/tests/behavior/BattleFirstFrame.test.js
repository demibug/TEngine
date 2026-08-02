'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createBootToBattleHarness, advanceTimer } = require('../mocks/createBootToBattleHarness');
const { BattleManagerState } = require('../../src/battle/BattleManager');

test('BattleScene first creates two pooled aDou targets; first fixed frame configures wave and Mob0 pair follows after 1500ms', async () => {
  const { Laya, context, bootstrap } = await createBootToBattleHarness({
    config: { directBattle: true, developmentBattleStartDelayMs: 0 },
  });
  const battle = bootstrap.lastBattleScene;
  assert.equal(context.animationEntityPool.createLog.length, 2);
  assert.deepEqual(context.animationEntityPool.createLog.map(x => x.animationId), ['aDou','aDou']);
  assert.equal(context.gameState.battle.wave, 0);
  assert.equal(context.enemyManager.count, 0);
  assert.equal(context.unitRegistry.soldiers.size, 0);
  assert.equal(context.unitRegistry.generals.size, 0);

  await advanceTimer(Laya, 16, 16);
  assert.equal(context.battleManager.updateCount, 1);
  assert.equal(battle.fixedUpdateCount, 1);
  assert.equal(battle.firstFrameExecuted, true);
  assert.equal(context.gameState.battle.wave, 1);
  assert.equal(context.battleManager.state, BattleManagerState.SPAWNING);
  assert.equal(context.enemyManager.prepareWaveCount, 1);
  assert.equal(context.enemyManager.count, 0);

  await advanceTimer(Laya, 1500, 500);
  assert.equal(context.enemyManager.count, 2);
  assert.deepEqual(context.enemyManager.spawnLog.map(x => x.playerSide), [true, false]);
  assert.deepEqual(context.enemyManager.spawnLog.map(x => x.typeName), ['Mob0','Mob0']);
  assert.equal(battle.enemyCreatedCount, 2);

  await advanceTimer(Laya, 16, 16);
  for (const enemy of context.enemyManager.enemies.values()) {
    assert.ok(enemy.fixedUpdateCount >= 1);
    assert.equal(enemy.movementStatus, 'RESTORED_ENEMY_RUNTIME_RO_PE');
  }
});

test('unrestored enemy types fail explicitly instead of fabricating objects', async () => {
  const { context } = await createBootToBattleHarness({ config: { directBattle: true } });
  assert.throws(() => context.enemyManager.spawn(99, true), /Unknown enemy type index/);
  assert.throws(() => context.enemyFactory.create('Mob1'), /未为类型 Mob1 注册创建器/);
});
