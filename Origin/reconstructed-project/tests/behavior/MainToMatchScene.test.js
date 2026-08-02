'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createBootToBattleHarness } = require('../mocks/createBootToBattleHarness');

test('MainScene consumes the confirmed five stamina and opens MatchScene', async () => {
  const { Laya, context } = await createBootToBattleHarness();
  const main = Laya.__mock.getScene('MainScene');
  assert.equal(context.gameState.player.stamina, 30);
  assert.equal(await main.startGame(), undefined);
  assert.equal(context.gameState.player.stamina, 25);
  assert.ok(Laya.__mock.getScene('MatchScene'));
  assert.equal(Laya.__mock.getScene('MainScene'), null);
});

test('MainScene preserves the 5000ms debounce and does not consume stamina twice', async () => {
  const { Laya, context } = await createBootToBattleHarness();
  const main = Laya.__mock.getScene('MainScene');
  await main.startGame();
  assert.equal(await main.startGame(), undefined);
  assert.equal(context.gameState.player.stamina, 25);
});

test('MainScene fails explicitly when stamina is below the confirmed cost', async () => {
  const { Laya, context } = await createBootToBattleHarness();
  const main = Laya.__mock.getScene('MainScene');
  context.gameState.player.stamina = 4;
  await assert.rejects(() => main.startGame(), /体力不足/);
  assert.deepEqual(context.tipService.messages, ['体力不足，无法开始游戏！']);
  assert.equal(Laya.__mock.getScene('MatchScene'), null);
});
