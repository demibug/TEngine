'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createBootToBattleHarness, advanceTimer } = require('../mocks/createBootToBattleHarness');

test('MatchScene timing path pauses fixed update, opens real BattleScene, then resumes', async () => {
  const { Laya, context, bootstrap } = await createBootToBattleHarness({
    config: { developmentBattleStartDelayMs: 0 },
  });
  const main = Laya.__mock.getScene('MainScene');
  await main.startGame();
  const match = Laya.__mock.getScene('MatchScene');
  assert.ok(match);
  assert.equal(context.fixedUpdate.hasRegistration('MatchScene'), true);

  await advanceTimer(Laya, 50, 50);
  assert.equal(match.matchComplete, true);
  assert.equal(match.elapsedAfterCompleteMs, -950);

  await advanceTimer(Laya, 2500, 500);
  if (match.enteringBattlePromise) await match.enteringBattlePromise;
  const battle = Laya.__mock.getScene('BattleScene');
  assert.ok(battle);
  assert.equal(bootstrap.lastBattleScene, battle);
  assert.equal(context.battleManager.started, true);
  assert.equal(context.fixedUpdate.paused, false);
  assert.equal(context.fixedUpdate.hasRegistration('BattleMgr'), true);
  assert.equal(context.fixedUpdate.hasRegistration('BattleScene'), true);
  assert.equal(context.fixedUpdate.hasRegistration('MatchScene'), false);
  assert.deepEqual(context.sceneTransition.calls.map(x => x[0]), ['mainToMatch','matchToBattle']);
});
