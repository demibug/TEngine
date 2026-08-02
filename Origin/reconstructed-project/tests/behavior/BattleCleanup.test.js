'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createBootToBattleHarness, advanceTimer } = require('../mocks/createBootToBattleHarness');

test('battle cleanup removes battle loops and objects while preserving original enemyMgr registration', async () => {
  const { Laya, context, bootstrap } = await createBootToBattleHarness({
    config: { directBattle: true, developmentBattleStartDelayMs: 0 },
  });
  const battle = bootstrap.lastBattleScene;
  await advanceTimer(Laya, 16, 16);
  await advanceTimer(Laya, 1500, 500);
  assert.equal(context.enemyManager.count, 2);

  context.battleFlow.cleanupBattle(false);
  assert.equal(context.fixedUpdate.hasRegistration('BattleMgr'), false);
  assert.equal(context.fixedUpdate.hasRegistration('BattleScene'), false);
  assert.equal(context.fixedUpdate.hasRegistration('enemyMgr'), true);
  assert.equal(context.enemyManager.count, 0);
  assert.equal(context.unitRegistry.soldiers.size, 0);
  assert.equal(context.unitRegistry.generals.size, 0);
  assert.equal(battle.gameEnded, true);
  assert.equal(battle.end1.visible, false);
  assert.equal(battle.end2.visible, false);
  assert.equal(Laya.timer.taskCountFor(battle), 0);

  battle.destroy(true);
  assert.equal(battle.destroyed, true);
  assert.equal(battle.parent, null);
});
