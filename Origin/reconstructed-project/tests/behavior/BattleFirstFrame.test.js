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
  // 波次配置经 WaveManager 路径（BattleManager._beginWave 恒走 waveManager.beginRound，
  // prepareWave 为死路径，见 src/battle/BattleManager.js:111-123）。首帧 round=1 应在 planHistory 中留 1 条计划。
  assert.equal(context.waveManager.planHistory.length, 1);
  assert.equal(context.waveManager.roundPlans.has(1), true);
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
  // Mob1 已由 P1-02 注册（src/bootstrap/DevelopmentBootstrap.js:202），改用真实未注册类型 Mob99 验证抛异常契约。
  assert.throws(() => context.enemyFactory.create('Mob99'), /未为类型 Mob99 注册创建器/);
});
