'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createBootToBattleHarness } = require('../mocks/createBootToBattleHarness');

test('development startup follows index → LoadScene → MainScene without native platform or network', async () => {
  const { Laya, context, windowRef } = await createBootToBattleHarness({ config: { directBattle: false } });
  assert.ok(Laya.__mock.getScene('MainScene'));
  assert.equal(Laya.__mock.getScene('LoadScene'), null);
  assert.equal(context.fixedUpdate.initialized, true);
  assert.equal(windowRef.splashHidden, 1);
  assert.deepEqual(context.platform.calls.map(x => x[0]), ['initialize','getChannelAppId','preload','shouldEnterMatchDirectly']);
  assert.deepEqual(context.network.calls, [
    ['init', 'development-local'],
    ['login', 'SKIPPED_DEVELOPMENT_MODE'],
    ['synchronizeCloudSaveAfterLogin', 'SKIPPED_DEVELOPMENT_MODE'],
    ['finalizeLoadedPlayerData', 'LOCAL_DATA_ONLY'],
    ['loadRemoteShareConfig', 'SKIPPED_DEVELOPMENT_MODE'],
  ]);
  assert.equal(context.platform.assertNoNativePlatformCalls(), true);
  assert.equal(context.network.assertNoRealNetworkCalls(), true);
  assert.equal(globalThis.wx, undefined);
  assert.equal(globalThis.tt, undefined);
});

test('platform preload failure is logged and LoadScene still reaches MainScene', async () => {
  const { Laya, warnings } = await createBootToBattleHarness({ platformOptions: { failPreload: true } });
  assert.ok(Laya.__mock.getScene('MainScene'));
  assert.ok(warnings.some(args => String(args[0]).includes('preload platform tasks failed')));
});

test('login failure follows the original startup degradation boundary', async () => {
  const { Laya, warnings, context } = await createBootToBattleHarness({
    config: { skipPlatformLogin: false, useLocalPlayerData: false },
    platformOptions: { failLogin: true },
  });
  assert.ok(Laya.__mock.getScene('MainScene'));
  assert.ok(context.platform.calls.some(x => x[0] === 'login'));
  assert.ok(warnings.some(args => String(args[0]).includes('startup platform tasks failed')));
});

test('special launch condition routes LoadScene to MatchScene and prepares match data contract', async () => {
  const { Laya, context } = await createBootToBattleHarness({ config: { forceMatchLaunch: true } });
  assert.ok(Laya.__mock.getScene('MatchScene'));
  assert.equal(Laya.__mock.getScene('MainScene'), null);
  assert.deepEqual(context.matchPreparation.calls.slice(-2).map(x => x.method), ['prepareRank','prepareProps']);
});
