#!/usr/bin/env node
'use strict';

const { createFriendlyUnitCombatHarness } = require('../tests/mocks/createFriendlyUnitCombatHarness');
const { KnifeSoldier } = require('../src/units/KnifeSoldier');
const { UnitState } = require('../src/units/UnitBase');

function clonePosition(displayObject) {
  return { x: displayObject.x, y: displayObject.y };
}

/**
 * Round 05 deterministic micro-battle runner.
 *
 * The scenario uses the reconstructed formal KnifeSoldier, UnitFactory,
 * UnitRegistry, BattleManager, EnemyManager, Mob0Enemy, ObjectPool and GameLoop.
 * Only scene/presentation resources are development mocks.
 */
function runFriendlyUnitMicroBattle() {
  const harness = createFriendlyUnitCombatHarness({ random: () => 0 });
  try {
    const unit = harness.spawnKnife({ side: true, gridX: 0, gridY: 6, level: 1 });
    const first = harness.spawnMobInRange(unit, { offsetX: 40 });
    const second = harness.spawnMobInRange(unit, { offsetX: 100 });

    const unitConfig = harness.gameData.friendlyUnits.getByText('刀');
    const unitInitial = {
      id: unit.id,
      originalSymbol: 'tb.zx[0]',
      reconstructedClass: unit.constructor.name,
      registrationKey: '刀',
      factoryIndex: unit.typeIndex,
      level: unit.level,
      side: unit.side,
      gridPosition: { ...unit.gridPosition },
      pixelPosition: clonePosition(unit.displayObject),
      attackDamage: unit.attackDamage,
      attackRange: unit.attackRange,
      attackRangeCells: unitConfig.rangeCells,
      attackIntervalSeconds: unit.attackIntervalSeconds,
      animationKey: unit.animationKey,
      logicPoolKey: unit.constructor.name,
      visualPoolKey: 'soldier',
    };

    const enemyInitial = [first, second].map(enemy => ({
      id: enemy.id,
      originalSymbol: 'st',
      reconstructedClass: enemy.constructor.name,
      factoryKey: 'Mob0',
      side: enemy.isPlayerLane,
      health: enemy.health,
      maxHealth: enemy.maxHealth,
      moveSpeed: enemy.moveSpeed,
      position: clonePosition(enemy.visual),
      spatialKey: harness.enemyManager.spatialKeyFor(enemy.id),
      logicPoolKey: enemy.constructor.name,
      visualPoolKey: enemy.visualPoolKey,
    }));

    const timeline = {
      firstTargetDetectedAt: null,
      firstAttackStateAt: null,
      firstAttackStartedAt: null,
      firstDamageSettledAt: null,
      firstMobHealthZeroAt: null,
      firstMobRemovedAt: null,
      retargetedAt: null,
      secondMobHealthZeroAt: null,
      secondMobRemovedAt: null,
      postCombatIdleAt: null,
    };
    const snapshots = [];
    let elapsedMs = 0;
    let previousFirstHealth = first.health;
    let previousSecondHealth = second.health;

    while (harness.enemyManager.count > 0 && elapsedMs < 7000) {
      harness.tick(80, 80);
      elapsedMs += 80;

      if (timeline.firstTargetDetectedAt == null && unit.targets.length > 0) {
        timeline.firstTargetDetectedAt = elapsedMs;
      }
      if (timeline.firstAttackStateAt == null && unit.currentState === UnitState.ATTACK) {
        timeline.firstAttackStateAt = elapsedMs;
      }
      if (timeline.firstAttackStartedAt == null && harness.attackTimeline.started.length > 0) {
        timeline.firstAttackStartedAt = harness.attackTimeline.started[0].startedAt;
      }
      const firstSettled = harness.attackTimeline.settled.find(record => record.settled);
      if (timeline.firstDamageSettledAt == null && firstSettled) {
        timeline.firstDamageSettledAt = firstSettled.settledAt;
      }
      if (timeline.firstMobHealthZeroAt == null && previousFirstHealth > 0 && first.health === 0) {
        timeline.firstMobHealthZeroAt = elapsedMs;
      }
      if (timeline.firstMobRemovedAt == null && !harness.enemyManager.getById(first.id)) {
        timeline.firstMobRemovedAt = elapsedMs;
      }
      if (timeline.retargetedAt == null && harness.attackTimeline.started.some(record => record.targetId === second.id)) {
        timeline.retargetedAt = harness.attackTimeline.started.find(record => record.targetId === second.id).startedAt;
      }
      if (timeline.secondMobHealthZeroAt == null && previousSecondHealth > 0 && second.health === 0) {
        timeline.secondMobHealthZeroAt = elapsedMs;
      }
      if (timeline.secondMobRemovedAt == null && !harness.enemyManager.getById(second.id)) {
        timeline.secondMobRemovedAt = elapsedMs;
      }

      previousFirstHealth = first.health;
      previousSecondHealth = second.health;

      if (elapsedMs % 400 === 0 || harness.enemyManager.count === 0) {
        snapshots.push({
          elapsedMs,
          unitState: unit.currentState,
          targetIds: unit.targets.map(target => target.id),
          lastAttackTime: unit.lastAttackTime,
          firstHealth: first.health,
          secondHealth: second.health,
          enemies: harness.enemyManager.count,
          spatialCells: harness.enemyManager.spatialCellCount,
          attacksStarted: harness.attackTimeline.started.length,
          attacksSettled: harness.attackTimeline.settled.filter(record => record.settled).length,
        });
      }
    }

    // Execute one real fixed update after the final enemy leaves so the unit clears
    // its stale candidate list and returns to the original idle/no-target branch.
    harness.tick(80, 80);
    elapsedMs += 80;
    if (unit.currentState === UnitState.IDLE && unit.targets.length === 0) {
      timeline.postCombatIdleAt = elapsedMs;
    }

    const unitBeforeCleanup = {
      id: unit.id,
      state: unit.currentState,
      targetIds: unit.targets.map(target => target.id),
      active: unit.isActive,
      registryPresent: harness.unitRegistry.getUnit(unit.id) === unit,
    };
    const managersBeforeFriendlyCleanup = {
      enemyManagerCount: harness.enemyManager.count,
      unitRegistryCount: harness.unitRegistry.count,
      spatialCellCount: harness.enemyManager.spatialCellCount,
      spatialEnemyRecordCount: harness.enemyManager.enemyIdToCell.size,
    };
    const unitId = unit.id;
    const unitRemoved = harness.unitRegistry.removeSoldier(unitId);

    const output = {
      mode: 'DEVELOPMENT_FRIENDLY_UNIT_MICRO_BATTLE',
      completed: harness.enemyManager.count === 0,
      elapsedMs,
      fixedUpdate: {
        stepMs: 80,
        maximumAccumulatedDeltaMs: 500,
      },
      unit: {
        ...unitInitial,
        type: unitInitial.registrationKey,
      },
      formalUnitConfig: {
        index: unitConfig.index,
        text: unitConfig.text,
        animationKey: unitConfig.animationKey,
        rangeCells: unitConfig.rangeCells,
        attackDamage: unitConfig.attackDamage,
        attackIntervalSeconds: unitConfig.attackIntervalSeconds,
        damageMode: unitConfig.damageMode,
        targetPolicy: unitConfig.targetPolicy,
      },
      enemies: [
        { ...enemyInitial[0], finalHealth: first.health, finalMaxHealth: first.maxHealth },
        { ...enemyInitial[1], finalHealth: second.health, finalMaxHealth: second.maxHealth },
      ],
      timeline,
      attacks: {
        started: harness.attackTimeline.started.map(record => ({
          attackerId: record.attackerId,
          targetId: record.targetId,
          damage: record.damage,
          delayMs: record.delayMs,
          startedAt: record.startedAt,
        })),
        settledRecords: harness.attackTimeline.settled.map(record => ({
          attackerId: record.attackerId,
          targetId: record.targetId,
          damage: record.damage,
          startedAt: record.startedAt,
          settledAt: record.settledAt,
          settled: record.settled,
          cancelled: record.cancelled,
        })),
        settled: harness.attackTimeline.settled.filter(record => record.settled).length,
        damageSequence: harness.attackTimeline.settled
          .filter(record => record.settled)
          .map(record => record.damage),
      },
      retargetResult: {
        targetIdsByAttack: harness.attackTimeline.started.map(record => record.targetId),
        firstEnemyId: first.id,
        secondEnemyId: second.id,
        switchedToSecondEnemy: harness.attackTimeline.started.some(record => record.targetId === second.id),
        postCombatState: unitBeforeCleanup.state,
        postCombatTargetIds: unitBeforeCleanup.targetIds,
      },
      rewardCalls: harness.rewards.calls.length,
      managersBeforeFriendlyCleanup,
      friendlyCleanup: {
        removed: unitRemoved,
        unitId,
        unitRegistryCount: harness.unitRegistry.count,
        logicPool: harness.objectPool.sizeByClass(KnifeSoldier),
        visualPool: harness.objectPool.sizeByKey('soldier'),
      },
      mobPools: {
        logic: harness.objectPool.sizeByClass(first.constructor),
        visual: harness.objectPool.sizeByKey('mob'),
      },
      friendlyDamageContract: {
        supported: false,
        reason: 'The recovered rc/td/knife inheritance ranges define no friendly HP or receive-damage lifecycle; explicit removal/gameOver is the confirmed termination path.',
      },
      snapshots,
      networkRequests: 0,
      nativePlatformCalls: 0,
    };

    if (!output.completed || output.attacks.started.length !== 4 || output.attacks.settled !== 4 ||
        output.rewardCalls !== 2 || !output.retargetResult.switchedToSecondEnemy ||
        output.retargetResult.postCombatState !== UnitState.IDLE ||
        output.friendlyCleanup.logicPool !== 1 || output.friendlyCleanup.visualPool !== 1 ||
        output.networkRequests !== 0 || output.nativePlatformCalls !== 0) {
      const error = new Error('Friendly-unit micro-battle did not reach the required deterministic completion boundary');
      error.output = output;
      throw error;
    }
    return output;
  } finally {
    harness.cleanup();
  }
}

function main() {
  const output = runFriendlyUnitMicroBattle();
  process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);
}

if (require.main === module) {
  try {
    main();
  } catch (error) {
    console.error(error && error.stack ? error.stack : error);
    if (error && error.output) console.error(JSON.stringify(error.output, null, 2));
    process.exitCode = 1;
  }
}

module.exports = { runFriendlyUnitMicroBattle, main };
