#!/usr/bin/env node
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { createBootToBattleHarness, advanceTimer } = require('../tests/mocks/createBootToBattleHarness');

async function main() {
  const { Laya, context } = await createBootToBattleHarness({
    config: { developmentBattleStartDelayMs: 0 },
    random: () => 0,
  });
  const scenes = [];
  const recordScene = () => {
    for (const name of ['LoadScene','MainScene','MatchScene','BattleScene','GameOverScene']) {
      if (Laya.__mock.getScene(name) && scenes[scenes.length - 1] !== name) scenes.push(name);
    }
  };
  recordScene();
  const main = Laya.__mock.getScene('MainScene');
  await main.startGame();
  recordScene();
  await advanceTimer(Laya, 3000, 80);
  recordScene();
  const battleScene = Laya.__mock.getScene('BattleScene');
  if (!battleScene) throw new Error('BattleScene did not open');
  context.deckManager.setHand(true, ['刀','刀','弓','枪','骑']);
  context.gameData.battle.forceBossNextRound = true;
  context.gameData.battle.maxRounds = 20;
  context.gameData.battle.opponentMaxHealth = 1;
  context.gameData.battle.opponentHealth = 1;
  const firstKnife = battleScene.purchaseAndPlace(0, 3, 1);
  const secondKnife = battleScene.purchaseAndPlace(1, 4, 1);
  if (!firstKnife.success || !secondKnife.success) throw new Error('Formal merge setup failed');
  const firstKnifeId = firstKnife.unit.id;
  const secondKnifeId = secondKnife.unit.id;
  const merge = battleScene.mergeUnits(firstKnifeId, secondKnifeId);
  if (!merge.success || secondKnife.unit.level !== 2) throw new Error('Formal unit merge failed');
  const purchase = battleScene.purchaseAndPlace(2, 4, 2);
  if (!purchase.success) throw new Error(`Formal purchase failed: ${purchase.reason}`);
  if (!purchase.unit.weapon) throw new Error('Formal weapon was not equipped');
  const purchaseSnapshot = { card: purchase.card.text, cost: purchase.cost, unitId: purchase.unit.id, weapon: purchase.unit.weapon.constructor.name };
  const mergeSnapshot = { sourceId: firstKnifeId, targetId: secondKnifeId, resultLevel: secondKnife.unit.level };
  const playerGoldAfterPurchase = context.gameData.battle.gold;
  // Ensure AI completed via the same deck/input path.
  if (context.unitManager.unitsBySide(false).length < 1) throw new Error('AI did not deploy an official unit');
  // Allow the normal WaveManager/Boss/Enemy path to reach and destroy the opponent target.
  let elapsed = 0;
  while (!Laya.__mock.getScene('GameOverScene') && elapsed < 70000) {
    await advanceTimer(Laya, 80, 80);
    elapsed += 80;
  }
  recordScene();
  const gameOver = Laya.__mock.getScene('GameOverScene');
  if (!gameOver) throw new Error('Automatic BATTLE_FINISHED -> GameOver did not occur');
  const result = context.battleFlow.lastBattleResult;
  if (!result) throw new Error('BattleResult was not generated');
  gameOver.returnToMain();
  if (context.sceneManager && typeof context.sceneManager.whenLastOpenCompletes === 'function') await context.sceneManager.whenLastOpenCompletes();
  await advanceTimer(Laya, 80, 80);
  const reopenedMain = context.sceneManager.getScene('MainScene');
  if (reopenedMain && reopenedMain.closed === false && scenes[scenes.length - 1] !== 'MainScene') scenes.push('MainScene');
  const report = {
    status: 'PASS',
    sceneOrder: scenes,
    purchase: purchaseSnapshot,
    merge: mergeSnapshot,
    playerGoldAfterPurchase,
    ai: context.aiController.snapshot(),
    wavePlans: context.waveManager.planHistory.slice(),
    bossCreations: context.bossManager.creationLog.slice(),
    battleResult: result.toJSON(),
    automaticGameOver: true,
    returnedToMain: Boolean(reopenedMain && reopenedMain.closed === false),
    managersAfterCleanup: {
      units: context.unitManager.count,
      enemies: context.enemyManager.count,
      bosses: context.bossManager.count,
      projectiles: context.projectileManager.activeCount,
      weapons: context.weaponManager.count,
      buffs: context.buffManager.activeCount || context.buffManager.count || 0,
      skills: context.skillManager.activeCount || context.skillManager.count || 0,
    },
    developmentNetworkCalls: context.network.calls.length,
    realNetworkCalls: context.network.assertNoRealNetworkCalls() ? 0 : 1,
    nativePlatformCalls: context.platform.assertNoNativePlatformCalls() ? 0 : 1,
    simulatedMs: elapsed,
  };
  const outDir = path.resolve(__dirname, '../analysis/smoke');
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(path.join(outDir, 'single-game-flow.json'), JSON.stringify(report, null, 2));
  fs.writeFileSync(path.join(outDir, 'single-game-flow.md'), `# Single Game Flow Smoke\n\n- Status: ${report.status}\n- Scenes: ${report.sceneOrder.join(' → ')}\n- Formal purchase: ${report.purchase.card}, weapon ${report.purchase.weapon}\n- Automatic GameOver: ${report.automaticGameOver}\n- Returned to Main: ${report.returnedToMain}\n- Simulated time: ${report.simulatedMs}ms\n`);
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
}
main().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
