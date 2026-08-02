'use strict';

const { createEnemyRuntimeHarness } = require('./createEnemyRuntimeHarness');
const { PlacementReservationRegistry } = require('../../src/core/PlacementReservationRegistry');
const { UnitFactory } = require('../../src/units/UnitFactory');
const { UnitRegistry } = require('../../src/units/UnitRegistry');
const { KnifeAttackTimeline } = require('../../src/combat/KnifeAttackTimeline');
const { BattleManager } = require('../../src/battle/BattleManager');
const {
  DevelopmentUnitPresentation,
  DevelopmentUnitAudio,
  DevelopmentKnifeEffects,
} = require('../../src/battle/dev/DevelopmentUnitServices');
const { DevelopmentUnitSpawner } = require('../../src/battle/dev/DevelopmentUnitSpawner');
const { GameEvents } = require('../../src/core/EventBus');

function createFriendlyUnitCombatHarness(options = {}) {
  const enemyHarness = createEnemyRuntimeHarness(options);
  const {
    Laya,
    eventBus,
    gameLoop,
    gameData,
    parent,
    objectPool,
    enemyManager,
  } = enemyHarness;

  const placementReservations = new PlacementReservationRegistry();
  const presentation = new DevelopmentUnitPresentation({ laya: Laya });
  const audio = new DevelopmentUnitAudio();
  const effects = new DevelopmentKnifeEffects();

  objectPool.registerKey(
    'soldier',
    () => presentation.createSoldierVisual(),
    visual => presentation.resetSoldierVisual(visual),
  );

  const attackTimeline = new KnifeAttackTimeline({
    laya: Laya,
    enemyManager,
    effects,
    logger: options.logger || { log() {}, warn() {}, error() {} },
  });

  const unitFactory = new UnitFactory().configure({
    objectPool,
    dependencyResolver: () => ({
      laya: Laya,
      gameData,
      gameLoop,
      eventBus,
      objectPool,
      presentation,
      audio,
      enemyManager,
      attackTimeline,
      logger: options.logger || { log() {}, warn() {}, error() {} },
      dragThreshold: 10,
    }),
  });

  const unitRegistry = new UnitRegistry().configure({
    unitFactory,
    gameData,
    eventBus,
    placementReservations,
    parentResolver: () => parent,
    logger: options.logger || { log() {}, warn() {}, error() {} },
  });
  unitRegistry.init();

  const deterministicRandom = {
    weightedIndex() { return 0; },
    range(min) { return min; },
  };
  const specialSpawnPolicy = { shouldMarkSpecialSpawn() { return false; } };
  const battleManager = new BattleManager().configure({
    gameData,
    enemyManager,
    eventBus,
    gameLoop,
    unitManager: unitRegistry,
    placementReservations,
    random: deterministicRandom,
    specialSpawnPolicy,
    laya: Laya,
    now: () => Laya.timer.currTimer,
    logger: options.logger || { log() {}, warn() {}, error() {} },
  });
  battleManager.init();
  battleManager.startGame();
  // DEVELOPMENT TEST INPUT：避免自动刷波干扰友军单元测试；攻击轮询仍由真实 BattleManager 执行。
  gameData.battle.delayTime = Number.MAX_SAFE_INTEGER;

  const developmentSpawner = new DevelopmentUnitSpawner({
    unitRegistry,
    placementReservations,
    gameData,
  });

  const events = [];
  for (const type of [GameEvents.ENEMY_KILLED_BY, GameEvents.ENEMY_REMOVED]) {
    eventBus.on(type, events, (...args) => events.push({ type, args }));
  }

  function spawnKnife({ side = true, gridX = 0, gridY = 6, level = 1 } = {}) {
    return developmentSpawner.spawnKnife({ side, gridX, gridY, level });
  }

  function reindexEnemy(enemy) {
    eventBus.event(GameEvents.ENEMY_GRID_LEFT, enemy.id, enemy);
    return enemy;
  }

  function placeEnemy(enemy, x, y, { lockMovement = true } = {}) {
    enemy.visual.pos(x, y);
    enemy.remainingPathDistance = 0;
    enemy.movementLocked = Boolean(lockMovement);
    reindexEnemy(enemy);
    return enemy;
  }

  function spawnMobInRange(unit, {
    side = unit.side,
    offsetX = 80,
    offsetY = 0,
    lockMovement = true,
  } = {}) {
    const enemy = enemyHarness.spawn(side);
    return placeEnemy(enemy, unit.displayObject.x + offsetX, unit.displayObject.y + offsetY, { lockMovement });
  }

  function tick(totalMs, stepMs = totalMs || 0) {
    return enemyHarness.tick(totalMs, stepMs);
  }

  function runUntil(predicate, { timeoutMs = 10000, stepMs = 80 } = {}) {
    let elapsed = 0;
    while (!predicate()) {
      if (elapsed >= timeoutMs) throw new Error(`Condition was not reached within ${timeoutMs}ms`);
      tick(stepMs, stepMs);
      elapsed += stepMs;
    }
    return elapsed;
  }

  function cleanup() {
    try { unitRegistry.gameOver(); } catch (_) { /* best-effort test cleanup */ }
    battleManager.resetForTests();
    unitFactory.resetForTests();
    attackTimeline.resetForTests();
    placementReservations.clear();
    enemyHarness.cleanup();
  }

  return {
    ...enemyHarness,
    placementReservations,
    unitPresentation: presentation,
    unitAudio: audio,
    knifeEffects: effects,
    attackTimeline,
    unitFactory,
    unitRegistry,
    battleManager,
    developmentSpawner,
    friendlyEvents: events,
    spawnKnife,
    placeEnemy,
    spawnMobInRange,
    reindexEnemy,
    tick,
    runUntil,
    cleanup,
  };
}

module.exports = { createFriendlyUnitCombatHarness };
