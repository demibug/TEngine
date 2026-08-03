'use strict';

const { createLayaSceneMock } = require('./LayaSceneMock');
const { EventBus, GameEvents } = require('../../src/core/EventBus');
const { GameLoop } = require('../../src/core/GameLoop');
const { ObjectPool } = require('../../src/core/ObjectPool');
const { GameDataCore } = require('../../src/data/CriticalGameState');
const { EnemyFactory } = require('../../src/battle/EnemyFactory');
const { EnemyManager } = require('../../src/battle/EnemyManager');
const { Mob0Enemy } = require('../../src/entities/Mob0Enemy');
const { BattleTarget } = require('../../src/entities/BattleTarget');
const {
  DevelopmentEnemyPresentation,
  DevelopmentEnemyAudio,
  DevelopmentEnemyEffects,
  DevelopmentEnemyRewardService,
} = require('../../src/battle/dev/DevelopmentCombatServices');

function createEnemyRuntimeHarness(options = {}) {
  delete globalThis.wx;
  delete globalThis.tt;

  const Laya = createLayaSceneMock();
  globalThis.Laya = Laya;
  const eventBus = new EventBus();
  const gameLoop = new GameLoop().configure({ laya: Laya });
  gameLoop.init();

  const gameData = new GameDataCore().configure({
    eventBus,
    developmentSample: true,
    playerOverrides: options.playerOverrides || {},
    random: options.random || (() => 0),
    now: () => Laya.timer.currTimer,
  });
  gameData.init();
  gameData.startGame();
  if (Number.isInteger(options.mapIndex)) gameData.map.changeMap(options.mapIndex);
  gameData.battle.currentRound = options.currentRound == null ? 1 : options.currentRound;
  gameData.battle.spawnStrategy = options.spawnStrategy || gameData.enemy.spawnStrategies[0];

  const parent = new Laya.Sprite();
  parent.name = 'gameObjectBox';
  Laya.stage.addChild(parent);

  const playerTarget = new BattleTarget({ laya: Laya });
  const opponentTarget = new BattleTarget({ laya: Laya });
  playerTarget.name = 'playerADou';
  opponentTarget.name = 'opponentADou';
  playerTarget.bindBattleTarget({ battleState: gameData.battle, playerLaneTarget: true });
  opponentTarget.bindBattleTarget({ battleState: gameData.battle, playerLaneTarget: false });
  parent.addChild(playerTarget);
  parent.addChild(opponentTarget);

  const objectPool = new ObjectPool({ laya: Laya });
  const presentation = new DevelopmentEnemyPresentation({
    laya: Laya,
    spawnDurationMs: options.spawnDurationMs == null ? 0 : options.spawnDurationMs,
    deathDurationMs: options.deathDurationMs == null ? 100 : options.deathDurationMs,
  });
  objectPool.registerKey(
    'mob',
    () => presentation.createMobVisual(),
    visual => presentation.resetMobVisual(visual),
  );

  const enemyFactory = new EnemyFactory().configure({ objectPool });
  const enemyManager = new EnemyManager().configure({
    gameLoop,
    gameData,
    eventBus,
    factory: enemyFactory,
    laya: Laya,
    logger: options.logger || { log() {}, warn() {}, error() {} },
    randomSource: options.random || (() => 0),
  });
  enemyManager.init();

  const audio = new DevelopmentEnemyAudio();
  const effects = new DevelopmentEnemyEffects();
  const rewards = new DevelopmentEnemyRewardService(gameData.battle);
  const dependencies = {
    laya: Laya,
    eventBus,
    gameData,
    enemyFactory,
    objectPool,
    parentResolver: () => parent,
    presentation,
    audio,
    effects,
    rewardService: rewards,
    // 吹飞推进 gameLoop（bundle:31461 nx.La / 31344 nx.wa）：注入同一 GameLoop 单例，
    // NormalEnemyBase.Xw/Gw 经其 register/unregister 以 80ms 子步长推进吹飞。
    gameLoop,
    targetResolver: playerLane => playerLane ? playerTarget : opponentTarget,
    logger: options.logger || { log() {}, warn() {}, error() {} },
  };
  enemyFactory.registerPooledClass('Mob0', Mob0Enemy, enemy => enemy.configure(dependencies));

  const events = [];
  const observed = [
    GameEvents.ENEMY_REGISTERED,
    GameEvents.ENEMY_REMOVED,
    GameEvents.ENEMY_GRID_LEFT,
    GameEvents.ENEMY_GRID_ENTERED,
    GameEvents.ENEMY_KILLED_BY,
    GameEvents.BATTLE_FINISHED,
  ];
  for (const type of observed) eventBus.on(type, events, (...args) => events.push({ type, args }));

  function activate(enemy) {
    if (Laya.timer.taskCountFor(enemy) > 0) Laya.timer.tick(0);
    return enemy;
  }

  function spawn(playerLane = true, isSpecial = false) {
    return activate(enemyManager.spawn(0, playerLane, isSpecial));
  }

  function tick(totalMs, stepMs = totalMs || 0) {
    if (totalMs === 0) {
      Laya.timer.tick(0);
      return;
    }
    let remaining = totalMs;
    while (remaining > 0) {
      const step = Math.min(stepMs, remaining);
      Laya.timer.tick(step);
      remaining -= step;
    }
  }


  function placeAtPathIndex(enemy, pathIndex) {
    if (!enemy.path || !enemy.path[pathIndex]) throw new RangeError(`Invalid path index ${pathIndex}`);
    const point = enemy.path[pathIndex];
    enemy.currentPathIndex = pathIndex;
    enemy.lastPathIndex = pathIndex;
    enemy.visual.pos(point.x * gameData.map.gridWidth, point.y * gameData.map.gridHeight);
    enemy.remainingPathDistance = (enemy.path.length - 1 - pathIndex) * gameData.map.gridWidth;
    eventBus.event(GameEvents.ENEMY_GRID_LEFT, enemy.id, enemy);
    return enemy;
  }

  function prepareContact(enemy) {
    enemy.movementLocked = true;
    tick(500, 500);
    enemy.movementLocked = false;
    return placeAtPathIndex(enemy, enemy.path.length - 2);
  }

  function cleanup() {
    enemyManager.resetForTests();
    gameLoop.resetForTests();
    eventBus.resetForTests();
    objectPool.clear();
    parent.destroy(true);
    delete globalThis.Laya;
  }

  return {
    Laya,
    eventBus,
    gameLoop,
    gameData,
    parent,
    playerTarget,
    opponentTarget,
    objectPool,
    presentation,
    audio,
    effects,
    rewards,
    enemyFactory,
    enemyManager,
    events,
    spawn,
    activate,
    tick,
    placeAtPathIndex,
    prepareContact,
    cleanup,
  };
}

module.exports = { createEnemyRuntimeHarness };
