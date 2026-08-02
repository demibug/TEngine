'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createBootToBattleHarness } = require('../mocks/createBootToBattleHarness');

test('directBattle still executes real GameFlow, BattleManager and BattleScene lifecycle', async () => {
  const { Laya, context, bootstrap, windowRef } = await createBootToBattleHarness({
    config: { directBattle: true, developmentBattleStartDelayMs: 0 },
  });
  const battle = Laya.__mock.getScene('BattleScene');
  assert.ok(battle);
  assert.equal(Laya.__mock.getScene('LoadScene'), null);
  assert.equal(Laya.__mock.getScene('MainScene'), null);
  assert.equal(Laya.__mock.getScene('MatchScene'), null);
  assert.deepEqual(battle.lifecycle.slice(0, 2), ['onAwake','onOpened']);
  assert.equal(context.battleManager.started, true);
  assert.equal(context.battleManager.startCount, 1);
  assert.equal(bootstrap.lastBattleScene, battle);
  assert.equal(context.unitRegistry.soldiers.size, 0);
  assert.equal(context.unitRegistry.generals.size, 0);
  assert.equal(context.animationEntityPool.createLog.length, 2);
  assert.deepEqual(context.animationEntityPool.createLog.map(x => x.poolKey), ['sk_aDou','sk_aDou']);
  assert.deepEqual(context.animationEntityPool.createLog.map(x => x.resourcePath), [
    'resources/anim/aDou/skeleton.json',
    'resources/anim/aDou/skeleton.json',
  ]);
  assert.equal(battle.playerTarget.name, 'sk');
  assert.equal(battle.playerTarget.anchorX, 0.5);
  assert.equal(battle.playerTarget.anchorY, 1);
  assert.deepEqual([battle.playerTarget.x, battle.playerTarget.y], [45, 70]);
  assert.equal(battle.playerTarget.parent, battle.end1);
  assert.equal(battle.opponentTarget.parent, battle.end2);
  assert.equal(battle.playerTarget.fastMode, false);
  assert.equal(battle.opponentTarget.fastMode, false);
  assert.deepEqual(context.platform.calls.map(call => call[0]), ['initialize','getChannelAppId','startGame']);
  assert.deepEqual(context.network.calls.map(call => call[0]), ['init','reportGameStart']);
  assert.equal(context.platform.assertNoNativePlatformCalls(), true);
  assert.equal(context.network.assertNoRealNetworkCalls(), true);
  assert.equal(windowRef.splashHidden, 1);
});
