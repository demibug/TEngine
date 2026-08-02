#!/usr/bin/env node
'use strict';

const { createFriendlyCombatHarness } = require('../tests/mocks/createFriendlyCombatHarness');

const harness = createFriendlyCombatHarness({ deathDurationMs: 0 });
try {
  const unit = harness.spawnKnife({ side: true, gridX: 1, gridY: 6, level: 1 });
  const enemy = harness.spawn(true);
  harness.moveEnemyNearUnit(enemy, unit, 40, 0, true);
  const healthLog = [{ time: 0, health: enemy.health }];
  for (let elapsed = 80; elapsed <= 1680; elapsed += 80) {
    harness.advanceCombat(80, 80);
    if (healthLog[healthLog.length - 1].health !== enemy.health) healthLog.push({ time: elapsed, health: enemy.health });
  }
  harness.Laya.timer.tick(0);
  const summary = {
    mode: 'DEVELOPMENT_MICRO_BATTLE',
    elapsedMs: harness.Laya.timer.currTimer,
    formalUnit: { key: '刀', id: unit.id, damage: unit.attackDamage, range: unit.attackRange, intervalMs: 1000 * unit.attackIntervalScale },
    enemy: { id: enemy.id, finalHealth: enemy.health, removed: !harness.enemyManager.enemies.has(enemy.id) },
    healthLog,
    attackCount: harness.knifePresentation.hits.length,
    rewardCalls: harness.rewards.calls.length,
    pools: {
      knifeClass: harness.objectPool.sizeByClass(unit.constructor),
      mob0Class: harness.objectPool.sizeByClass(enemy.constructor),
      mobVisual: harness.objectPool.sizeByKey('mob'),
    },
    registry: { friendly: harness.unitRegistry.soldierCount, enemies: harness.enemyManager.count },
    networkRequests: 0,
    nativePlatformCalls: 0,
  };
  process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);
} finally {
  harness.cleanup();
}
