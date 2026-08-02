#!/usr/bin/env node
'use strict';

const { createEnemyRuntimeHarness } = require('../tests/mocks/createEnemyRuntimeHarness');

const harness = createEnemyRuntimeHarness({ random: () => 0 });
try {
  const playerEnemy = harness.spawn(true);
  const opponentEnemy = harness.spawn(false);
  const pathTransitions = [];
  const lastIndex = new Map([[playerEnemy.id, playerEnemy.currentPathIndex], [opponentEnemy.id, opponentEnemy.currentPathIndex]]);
  let elapsedMs = 0;
  while (harness.enemyManager.count > 0 && elapsedMs < 40000) {
    harness.tick(80, 80);
    elapsedMs += 80;
    for (const enemy of [playerEnemy, opponentEnemy]) {
      if (enemy.inPool) continue;
      const previous = lastIndex.get(enemy.id);
      if (enemy.currentPathIndex !== previous) {
        pathTransitions.push({
          elapsedMs,
          enemyId: enemy.id,
          playerLane: enemy.isPlayerLane,
          pathIndex: enemy.currentPathIndex,
          x: Number(enemy.x.toFixed(3)),
          y: Number(enemy.y.toFixed(3)),
        });
        lastIndex.set(enemy.id, enemy.currentPathIndex);
      }
    }
  }
  const output = {
    mode: 'DEVELOPMENT_ENEMY_RUNTIME_SIMULATION',
    elapsedMs,
    completed: harness.enemyManager.count === 0,
    paths: {
      player: harness.gameData.map.playerRoute,
      opponent: harness.gameData.map.opponentRoute,
    },
    pathTransitions,
    targets: {
      playerHealth: harness.playerTarget.health,
      opponentHealth: harness.opponentTarget.health,
      playerDamageLog: harness.playerTarget.damageLog,
      opponentDamageLog: harness.opponentTarget.damageLog,
    },
    pools: {
      Mob0Class: harness.objectPool.sizeByClass(playerEnemy.constructor),
      mobVisual: harness.objectPool.sizeByKey('mob'),
    },
    serviceCalls: {
      audio: harness.audio.calls.length,
      effects: harness.effects.calls.length,
      rewards: harness.rewards.calls.length,
    },
    networkRequests: 0,
    nativePlatformCalls: 0,
  };
  process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);
  if (!output.completed || output.targets.playerHealth !== 2 || output.targets.opponentHealth !== 2) process.exitCode = 1;
} finally {
  harness.cleanup();
}
