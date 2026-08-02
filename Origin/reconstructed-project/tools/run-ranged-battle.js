#!/usr/bin/env node
'use strict';

const { createRangedCombatHarness } = require('../tests/mocks/createRangedCombatHarness');
const { BowSoldier } = require('../src/units/BowSoldier');
const { SimpleDynamicArrow } = require('../src/projectiles/SimpleDynamicArrow');

function runRangedBattleSimulation() {
  const h = createRangedCombatHarness();
  try {
    const bow = h.spawnBow({ gridX: 0, gridY: 6, level: 1 });
    const first = h.spawnMobInRange(bow, { offsetX: 160, offsetY: 0, remainingPathDistance: 10 });
    const second = h.spawnMobInRange(bow, { offsetX: 220, offsetY: -20, remainingPathDistance: 20 });
    const firstId = first.id;
    const secondId = second.id;
    const timing = {
      firstTargetAt: null,
      firstAttackAnimationAt: null,
      firstStoppedAt: null,
      firstArrowCreatedAt: null,
      firstArrowHitAt: null,
      firstMobDeathAt: null,
      firstMobRemovedAt: null,
      retargetSecondAt: null,
      secondMobDeathAt: null,
      secondMobRemovedAt: null,
    };
    const hitRecords = [];
    const arrowCreations = [];
    const trajectory = [];
    let observedAnimation = 0;
    let observedCreation = 0;
    let observedHits = 0;
    let firstProjectileId = null;

    for (let elapsed = 80; elapsed <= 10000; elapsed += 80) {
      h.tick(80, 80);
      if (timing.firstTargetAt == null && bow.currentState === 'UnitAttack') timing.firstTargetAt = elapsed;
      for (const event of h.animationDriver.eventLog.slice(observedAnimation)) {
        if (event.type === 'play' && event.startMs === 0 && timing.firstAttackAnimationAt == null) timing.firstAttackAnimationAt = elapsed;
        if (event.type === 'stopped' && timing.firstStoppedAt == null) timing.firstStoppedAt = elapsed;
      }
      observedAnimation = h.animationDriver.eventLog.length;
      for (const creation of h.projectileFactory.creationLog.slice(observedCreation)) {
        const record = { elapsedMs: elapsed, projectileId: creation.projectileId, typeKey: creation.typeKey, poolKey: creation.poolKey };
        arrowCreations.push(record);
        if (timing.firstArrowCreatedAt == null) {
          timing.firstArrowCreatedAt = elapsed;
          firstProjectileId = creation.projectileId;
        }
      }
      observedCreation = h.projectileFactory.creationLog.length;
      for (const hit of h.projectileEffects.calls.slice(observedHits)) {
        hitRecords.push({ elapsedMs: elapsed, projectileId: hit.projectileId, enemyId: hit.enemyId, damage: hit.damage, applied: hit.applied });
        if (timing.firstArrowHitAt == null) timing.firstArrowHitAt = elapsed;
      }
      observedHits = h.projectileEffects.calls.length;
      const liveFirstArrow = firstProjectileId == null ? null : h.projectileManager.getById(firstProjectileId);
      if (liveFirstArrow && trajectory.length < 8) trajectory.push({ elapsedMs: elapsed, x: liveFirstArrow.x, y: liveFirstArrow.y, rotation: liveFirstArrow.rotation, progress: liveFirstArrow.movement.progress });
      if (timing.firstMobDeathAt == null && first.health <= 0) timing.firstMobDeathAt = elapsed;
      if (timing.firstMobRemovedAt == null && !h.enemyManager.enemies.has(firstId)) timing.firstMobRemovedAt = elapsed;
      if (timing.firstMobRemovedAt != null && timing.retargetSecondAt == null && bow.targetId === secondId) timing.retargetSecondAt = elapsed;
      if (timing.secondMobDeathAt == null && second.health <= 0) timing.secondMobDeathAt = elapsed;
      if (timing.secondMobRemovedAt == null && !h.enemyManager.enemies.has(secondId)) {
        timing.secondMobRemovedAt = elapsed;
        break;
      }
    }
    h.tick(960, 80);

    const beforeCleanup = {
      activeProjectiles: h.projectileManager.activeCount,
      projectileManagerCount: h.projectileManager.activeProjectiles.length,
      enemyManagerCount: h.enemyManager.enemies.size,
      unitRegistryCount: h.unitRegistry.count,
      bowState: bow.currentState,
    };
    const bowId = bow.id;
    h.unitRegistry.removeSoldier(bowId);
    h.projectileManager.gameOver();
    h.animationDriver.gameOver();
    const projectilePoolKey = 'bullet_pool_SimpleDynamicArrow_弓箭小兵箭矢';
    const afterCleanup = {
      activeProjectiles: h.projectileManager.activeCount,
      enemyManagerCount: h.enemyManager.enemies.size,
      unitRegistryCount: h.unitRegistry.count,
      bowLogicPool: h.objectPool.sizeByClass(BowSoldier),
      soldierVisualPool: h.objectPool.sizeByKey('soldier'),
      arrowCompositePool: h.objectPool.sizeByKey(projectilePoolKey),
      mobLogicPool: h.objectPool.sizeByClass(first.constructor),
      mobVisualPool: h.objectPool.sizeByKey('mob'),
    };
    const midpoint = trajectory.length
      ? trajectory.reduce((best, sample) => Math.abs(sample.progress - 0.5) < Math.abs(best.progress - 0.5) ? sample : best, trajectory[0])
      : null;
    const output = {
      mode: 'DEVELOPMENT_RANGED_BATTLE',
      bow: {
        originalSymbol: 'ok', formalKey: '弓', factoryIndex: 1, configIndex: 1,
        config: { damage: 2, rangeCells: 3.5, rangePx: 280, intervalMs: 800, animationKey: 'bow', attackReleaseMs: 650, initialPlaybackRate: 1.25 },
        initialPosition: { grid: { x: 0, y: 6 }, pixel: { x: 0, y: 480 } },
      },
      arrow: {
        originalSymbol: 'rd', formalKey: SimpleDynamicArrow.projectileTypeKey,
        poolKey: projectilePoolKey, speedScale: 1.75, curveHeight: 120,
        progressFormula: 'deltaMs * movementRate * speedScale / 500 with sqrt(max(0.1,currentDistance/originalDistance))',
      },
      enemies: {
        first: { id: firstId, initialHealth: 6, finalHealth: first.health, initialPosition: { x: 160, y: 480 } },
        second: { id: secondId, initialHealth: 6, finalHealth: second.health, initialPosition: { x: 220, y: 460 } },
      },
      timing,
      arrowsCreated: arrowCreations,
      trajectoryMidpoint: midpoint,
      hitRecords,
      retargetResult: { targetId: bow.targetId, secondEnemyId: secondId, switchedToSecond: timing.retargetSecondAt != null, finalState: bow.currentState },
      beforeCleanup,
      afterCleanup,
      rewards: h.rewards.calls.length,
      gameLoopOrder: h.gameLoop.registrationKeys(),
      realNetworkRequests: 0,
      nativePlatformCalls: Number(Boolean(globalThis.wx)) + Number(Boolean(globalThis.tt)),
    };
    const valid = timing.firstTargetAt === 800
      && timing.firstAttackAnimationAt === 880
      && timing.firstStoppedAt === 1440
      && timing.firstArrowCreatedAt === 1440
      && timing.firstArrowHitAt === 1840
      && first.health === 0 && second.health === 0
      && hitRecords.length === 6
      && h.rewards.calls.length === 2
      && beforeCleanup.activeProjectiles === 0
      && beforeCleanup.enemyManagerCount === 0
      && beforeCleanup.unitRegistryCount === 1
      && afterCleanup.unitRegistryCount === 0
      && afterCleanup.bowLogicPool === 1
      && afterCleanup.soldierVisualPool === 1
      && afterCleanup.arrowCompositePool >= 1
      && output.realNetworkRequests === 0 && output.nativePlatformCalls === 0;
    if (!valid) {
      const error = new Error('Ranged battle simulation did not reach the confirmed deterministic completion boundary');
      error.output = output;
      throw error;
    }
    return output;
  } finally { h.cleanup(); }
}

function main() { process.stdout.write(`${JSON.stringify(runRangedBattleSimulation(), null, 2)}\n`); }
if (require.main === module) {
  try { main(); } catch (error) {
    console.error(error && error.stack ? error.stack : error);
    if (error && error.output) console.error(JSON.stringify(error.output, null, 2));
    process.exitCode = 1;
  }
}
module.exports = { runRangedBattleSimulation, main };
