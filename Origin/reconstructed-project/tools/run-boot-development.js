#!/usr/bin/env node
'use strict';

const { createBootToBattleHarness, advanceTimer } = require('../tests/mocks/createBootToBattleHarness');

async function main() {
  const directBattle = process.argv.includes('--direct-battle');
  const forceMatchLaunch = process.argv.includes('--force-match');
  const config = {
    directBattle,
    forceMatchLaunch,
    developmentBattleStartDelayMs: 0,
  };

  const { Laya, context, windowRef } = await createBootToBattleHarness({ config });
  if (directBattle) await advanceTimer(Laya, 80, 80);

  const sceneNames = ['LoadScene', 'MainScene', 'MatchScene', 'BattleScene'];
  const activeScenes = sceneNames.filter(name => Boolean(Laya.__mock.getScene(name)));
  const battleScene = Laya.__mock.getScene('BattleScene');
  const result = {
    mode: directBattle ? 'DIRECT_BATTLE' : forceMatchLaunch ? 'MATCH_LAUNCH' : 'NORMAL_BOOT',
    activeScenes,
    battleStarted: Boolean(context.battleManager && context.battleManager.started),
    battleState: context.gameData && context.gameData.battleData
      ? context.gameData.battleData.state
      : null,
    battleSceneLifecycle: battleScene ? [...battleScene.lifecycle] : [],
    fixedUpdateInitialized: Boolean(context.gameLoop && context.gameLoop.initialized),
    splashHidden: windowRef.splashHidden,
    platformCalls: context.platform.calls.map(call => call[0]),
    networkCalls: context.network.calls.map(call => call[0]),
    realNetworkRequests: context.network.assertNoRealNetworkCalls() ? 0 : 1,
    nativePlatformCalls: context.platform.assertNoNativePlatformCalls() ? 0 : 1,
    wxPresent: typeof globalThis.wx !== 'undefined',
    ttPresent: typeof globalThis.tt !== 'undefined',
  };

  if (result.realNetworkRequests !== 0 || result.nativePlatformCalls !== 0) {
    throw new Error('Development boot unexpectedly touched a real network or native platform API');
  }
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
}

main().catch(error => {
  console.error(error && error.stack ? error.stack : String(error));
  process.exitCode = 1;
});
