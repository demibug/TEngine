'use strict';

const { createEnemyRuntimeHarness } = require('./createEnemyRuntimeHarness');
const { PlacementReservationRegistry } = require('../../src/core/PlacementReservationRegistry');
const { UnitFactory } = require('../../src/units/UnitFactory');
const { UnitRegistry } = require('../../src/units/UnitRegistry');
const { KnifeAttackTimeline } = require('../../src/combat/KnifeAttackTimeline');
const { AttackEffectManager } = require('../../src/combat/AttackEffectManager');
const { DevelopmentAnimationDriver } = require('../../src/combat/dev/DevelopmentAnimationDriver');
const { ProjectileFactory } = require('../../src/projectiles/ProjectileFactory');
const { ProjectileManager } = require('../../src/projectiles/ProjectileManager');
const { TargetEnemyBezierMovement } = require('../../src/projectiles/TargetEnemyBezierMovement');
const { HitEnemyStrategy } = require('../../src/projectiles/HitEnemyStrategy');
const { SimpleDynamicArrow } = require('../../src/projectiles/SimpleDynamicArrow');
const { BattleManager } = require('../../src/battle/BattleManager');
const {
  DevelopmentUnitPresentation,
  DevelopmentUnitAudio,
  DevelopmentKnifeEffects,
} = require('../../src/battle/dev/DevelopmentUnitServices');
const { DevelopmentProjectileEffects } = require('../../src/battle/dev/DevelopmentRangedBattleServices');
const { DevelopmentUnitSpawner } = require('../../src/battle/dev/DevelopmentUnitSpawner');
const { GameEvents } = require('../../src/core/EventBus');

function createRangedCombatHarness(options = {}) {
  const enemyHarness = createEnemyRuntimeHarness(options);
  const { Laya, eventBus, gameLoop, gameData, parent, objectPool, enemyManager } = enemyHarness;
  const logger = options.logger || { log() {}, warn() {}, error() {} };

  const projectileEffects = new DevelopmentProjectileEffects();
  const projectileFactory = new ProjectileFactory({
    laya: Laya,
    objectPool,
    enemyManager,
    gameData,
    parentResolver: () => parent,
    effects: projectileEffects,
    logger,
  });
  const projectileManager = new ProjectileManager().configure({
    gameLoop,
    enemyManager,
    gameData,
    projectileFactory,
    laya: Laya,
    logger,
  });
  projectileManager.init();

  const animationDriver = new DevelopmentAnimationDriver({
    gameLoop,
    stoppedEvent: Laya.Event.STOPPED,
    logger,
  });
  animationDriver.init();

  const placementReservations = new PlacementReservationRegistry();
  const unitPresentation = new DevelopmentUnitPresentation({ laya: Laya, animationDriver });
  const unitAudio = new DevelopmentUnitAudio();
  const knifeEffects = new DevelopmentKnifeEffects();
  const attackEffectManager = new AttackEffectManager({ objectPool });
  objectPool.registerKey(
    'soldier',
    () => unitPresentation.createSoldierVisual(),
    visual => unitPresentation.resetSoldierVisual(visual),
  );

  const knifeAttackTimeline = new KnifeAttackTimeline({
    laya: Laya,
    enemyManager,
    effects: knifeEffects,
    logger,
  });
  const unitFactory = new UnitFactory().configure({
    objectPool,
    dependencyResolver: () => ({
      laya: Laya,
      gameData,
      gameLoop,
      eventBus,
      objectPool,
      presentation: unitPresentation,
      audio: unitAudio,
      enemyManager,
      attackTimeline: knifeAttackTimeline,
      attackEffectManager,
      projectileManager,
      logger,
      dragThreshold: 10,
    }),
  });
  const unitRegistry = new UnitRegistry().configure({
    unitFactory,
    gameData,
    eventBus,
    placementReservations,
    parentResolver: () => parent,
    logger,
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
    attackEffectManager,
    laya: Laya,
    now: () => Laya.timer.currTimer,
    logger,
  });
  battleManager.init();
  battleManager.startGame();
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

  function spawnBow({ side = true, gridX = 0, gridY = 6, level = 1 } = {}) {
    return developmentSpawner.spawnBow({ side, gridX, gridY, level });
  }

  function reindexEnemy(enemy) {
    eventBus.event(GameEvents.ENEMY_GRID_LEFT, enemy.id, enemy);
    return enemy;
  }

  function placeEnemy(enemy, x, y, { lockMovement = true, remainingPathDistance = 0 } = {}) {
    enemy.visual.pos(x, y);
    enemy.remainingPathDistance = remainingPathDistance;
    enemy.movementLocked = Boolean(lockMovement);
    reindexEnemy(enemy);
    return enemy;
  }

  function spawnMobInRange(unit, {
    side = unit.side,
    offsetX = 160,
    offsetY = 0,
    lockMovement = true,
    remainingPathDistance = 0,
  } = {}) {
    const enemy = enemyHarness.spawn(side);
    return placeEnemy(
      enemy,
      unit.displayObject.x + offsetX,
      unit.displayObject.y + offsetY,
      { lockMovement, remainingPathDistance },
    );
  }

  function createArrow({ attacker, target, damage = attacker.attackDamage, speedScale = 1.75, curveHeight = 120 } = {}) {
    if (!attacker || !target) throw new TypeError('createArrow requires attacker and target');
    const movement = TargetEnemyBezierMovement.create({
      enemyManager,
      gameData,
      curveHeight,
      distanceScaling: true,
      smoothRotation: false,
      hitRadiusEnabled: true,
    }).setTargetId(target.id);
    const hitStrategy = HitEnemyStrategy.create({ targetId: target.id });
    const startPoint = {
      x: attacker.displayObject.x + attacker.displayObject.width / 2,
      y: attacker.displayObject.y + attacker.displayObject.height / 2,
    };
    const arrow = projectileManager.create({
      type: SimpleDynamicArrow.projectileTypeKey,
      appearance: SimpleDynamicArrow.DEFAULT_APPEARANCE,
      attacker,
      damage,
      speedScale,
      hitStrategy,
      movement,
    }, startPoint);
    arrow.fire();
    return arrow;
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
    projectileManager.resetForTests();
    animationDriver.resetForTests();
    unitFactory.resetForTests();
    knifeAttackTimeline.resetForTests();
    attackEffectManager.resetForTests();
    placementReservations.clear();
    enemyHarness.cleanup();
  }

  return {
    ...enemyHarness,
    projectileEffects,
    projectileFactory,
    projectileManager,
    animationDriver,
    placementReservations,
    unitPresentation,
    unitAudio,
    knifeEffects,
    knifeAttackTimeline,
    attackEffectManager,
    unitFactory,
    unitRegistry,
    battleManager,
    developmentSpawner,
    rangedEvents: events,
    spawnKnife,
    spawnBow,
    placeEnemy,
    spawnMobInRange,
    reindexEnemy,
    createArrow,
    tick,
    runUntil,
    cleanup,
  };
}

module.exports = { createRangedCombatHarness };
