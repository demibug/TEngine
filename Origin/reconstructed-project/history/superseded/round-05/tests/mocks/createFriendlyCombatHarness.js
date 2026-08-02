'use strict';

const { createEnemyRuntimeHarness } = require('./createEnemyRuntimeHarness');
const { UnitRegistry } = require('../../src/units/UnitRegistry');
const { UnitFactory } = require('../../src/units/UnitFactory');
const { KnifeAttackFactory } = require('../../src/combat/KnifeHitAreaAttack');
const { BattleManager } = require('../../src/battle/BattleManager');
const { PlacementReservationRegistry } = require('../../src/core/PlacementReservationRegistry');
const { MathRandom } = require('../../src/core/MathRandom');
const {
  DevelopmentUnitVisualService,
  DevelopmentUnitBoardAdapter,
  DevelopmentKnifePresentation,
} = require('../../src/battle/dev/DevelopmentUnitServices');
const { DevelopmentUnitSpawner } = require('../../src/battle/dev/DevelopmentUnitSpawner');

function createFriendlyCombatHarness(options = {}) {
  const base = createEnemyRuntimeHarness(options);
  const { Laya, gameData, gameLoop, eventBus, enemyManager, objectPool, parent } = base;

  const placementReservations = new PlacementReservationRegistry();
  const visualService = new DevelopmentUnitVisualService({ laya: Laya, objectPool });
  objectPool.registerKey('soldier', () => visualService.createSoldierVisual(), visual => visualService.resetSoldierVisual(visual));
  const knifePresentation = new DevelopmentKnifePresentation();
  const attackFactory = new KnifeAttackFactory({ enemyManager, objectPool, presentation: knifePresentation });
  const unitRegistry = new UnitRegistry();
  const boardAdapter = new DevelopmentUnitBoardAdapter({ gameData, parentResolver: () => parent, placementReservations });
  const unitFactory = new UnitFactory({ objectPool });
  unitFactory.configure({
    objectPool,
    unitDependencies: {
      laya: Laya,
      eventBus,
      gameData,
      objectPool,
      visualService,
      registry: unitRegistry,
      enemyManager,
      attackFactory,
    },
  });
  unitRegistry.configure({ factory: unitFactory, boardAdapter, placementReservations, eventBus });
  unitRegistry.init();

  const now = () => Laya.timer.currTimer;
  const battleManager = new BattleManager().configure({
    gameData,
    enemyManager,
    eventBus,
    gameLoop,
    unitManager: unitRegistry,
    placementReservations,
    random: new MathRandom(options.random || (() => 0)),
    specialSpawnPolicy: { shouldMarkSpecialSpawn: () => false },
    laya: Laya,
    now,
    logger: options.logger || { log() {}, warn() {}, error() {} },
  });
  battleManager.init();
  battleManager.startGame();
  const spawner = new DevelopmentUnitSpawner({ unitRegistry });

  function spawnKnife(config = {}) { return spawner.spawnKnife(config); }

  function moveEnemyNearUnit(enemy, unit, offsetX = 40, offsetY = 0, lockMovement = true) {
    const oldKey = enemyManager.spatialKeyFor(enemy.id);
    enemy.visual.pos(unit.displayObject.x + offsetX, unit.displayObject.y + offsetY);
    enemy.remainingPathDistance = 1;
    enemy.movementLocked = Boolean(lockMovement);
    eventBus.event(require('../../src/core/EventBus').GameEvents.ENEMY_GRID_LEFT, enemy.id, enemy);
    return { oldKey, newKey: enemyManager.spatialKeyFor(enemy.id) };
  }

  function advanceCombat(totalMs, stepMs = 80) {
    let remaining = totalMs;
    while (remaining > 0) {
      const step = Math.min(stepMs, remaining);
      Laya.timer.tick(step);
      remaining -= step;
    }
  }

  const originalCleanup = base.cleanup;
  base.cleanup = () => {
    battleManager.resetForTests();
    unitRegistry.resetForTests();
    boardAdapter.clear();
    objectPool.clear();
    originalCleanup();
  };

  return {
    ...base,
    placementReservations,
    visualService,
    knifePresentation,
    attackFactory,
    boardAdapter,
    unitFactory,
    unitRegistry,
    battleManager,
    spawner,
    spawnKnife,
    moveEnemyNearUnit,
    advanceCombat,
  };
}

module.exports = { createFriendlyCombatHarness };
