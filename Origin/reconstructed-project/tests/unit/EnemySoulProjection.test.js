'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { ZombieEnemy } = require('../../src/entities/types/ZombieEnemy');
const { Mob1Enemy } = require('../../src/entities/types/Mob1Enemy');
const { GameEvents } = require('../../src/core/EventBus');

/**
 * 任务组 7.5：NormalEnemyBase 灵魂投射用例（sB）
 * 覆盖 spec Scenario：
 *   - 非 typeIndex1 敌人死后灵魂投射（typeIndex!=1 + 塔Ci + num<3 + 距离<range → sB 飞行 300ms + ENEMY_SOUL_DELIVERED）
 *   - typeIndex1 敌人不投射灵魂（Mob1 死亡不触发 sB）
 *   - 塔条件不满足不投射（Ci=false / num>=3 / 距离>=range 三分支）
 *
 * 灵魂投射在死亡流程中触发：beginDeath → playDeath(deathDurationMs) → onComplete → _tryDeliverSoul → sB。
 * 塔状态/飞行管理器经 configure 注入桩（DEFERRED：真实实现属提案 ②③）。
 */
function buildDependencies(h, extra = {}) {
  return {
    laya: h.Laya, eventBus: h.eventBus, gameData: h.gameData,
    enemyFactory: h.enemyFactory, objectPool: h.objectPool,
    parentResolver: () => h.parent, presentation: h.presentation,
    audio: h.audio, effects: h.effects, rewardService: h.rewards,
    gameLoop: h.gameLoop, targetResolver: playerLane => playerLane ? h.playerTarget : h.opponentTarget,
    logger: { log() {}, warn() {}, error() {} },
    ...extra,
  };
}

const EMERGE_MS = 1400;

/**
 * 生成 Zombie（typeIndex=4，非 1）并推进到 MOVING，注入桩塔/飞行管理器。
 * towerState 控制塔条件；flyManager 记录调用。
 */
function spawnZombieWithSoul(h, towerState, flyManager) {
  const deps = buildDependencies(h, {
    soulTowerResolver: () => towerState,
    soulFlightManager: flyManager,
  });
  h.enemyFactory.registerPooledClass('Zombie', ZombieEnemy, enemy => enemy.configure(deps));
  const enemy = h.enemyManager.spawnByKey('Zombie', true, false);
  h.tick(EMERGE_MS, 80); // 浮现完成 → MOVING
  return enemy;
}

test('非 typeIndex1 敌人死后灵魂投射：塔条件满足触发 sB', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5, deathDurationMs: 100 }); t.after(h.cleanup);
  const flyCalls = [];
  const flyManager = {
    fly(fromX, fromY, toX, toY, durationMs, color, resource, onComplete) {
      flyCalls.push({ fromX, fromY, toX, toY, durationMs, color, resource });
      // 模拟飞行 300ms 后到达，立即调 onComplete（桩）。
      if (typeof onComplete === 'function') onComplete();
    },
  };
  // 桩塔：Ci=true、num=0（<3）、range=99999（距离必满足）、pos={0,0}。
  const tower = { Ci: true, num: 0, range: 99999, pos: { x: 0, y: 0 } };
  const enemy = spawnZombieWithSoul(h, tower, flyManager);
  assert.equal(enemy.typeIndex, 4, 'Zombie typeIndex=4（!=1）');
  let soulEvents = 0;
  let soulArgs = null;
  h.eventBus.on(GameEvents.ENEMY_SOUL_DELIVERED, null, (...args) => { soulEvents++; soulArgs = args; });
  // 致死 → beginDeath → playDeath 100ms → onComplete → _tryDeliverSoul → sB。
  enemy.hit(enemy.health, { id: 1 });
  h.tick(100, 80); // 等 playDeath 完成
  assert.equal(flyCalls.length, 1, '塔条件满足触发 sB 飞行');
  assert.equal(flyCalls[0].durationMs, 300, '飞行 300ms（hu[167]）');
  assert.equal(flyCalls[0].color, '#05fe77', '灵魂头绿色 #05fe77');
  assert.equal(flyCalls[0].resource, 'resources/img/gameObject/enemy/soulHead.png', 'soulHead.png 贴图');
  assert.equal(soulEvents, 1, '到达发 ENEMY_SOUL_DELIVERED 事件');
  // 事件携带 isPlayerLane/敌人坐标/路径索引。
  assert.equal(soulArgs[0], enemy.isPlayerLane, '事件携带 isPlayerLane');
  assert.equal(soulArgs.length, 4, '事件 4 参数：isPlayerLane, x, y, pathIndex');
});

test('typeIndex1 敌人不投射灵魂（Mob1 死亡不触发 sB）', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5, deathDurationMs: 100 }); t.after(h.cleanup);
  const flyCalls = [];
  const flyManager = { fly(...args) { flyCalls.push(args); const cb = args[args.length - 1]; if (typeof cb === 'function') cb(); } };
  const tower = { Ci: true, num: 0, range: 99999, pos: { x: 0, y: 0 } };
  const deps = buildDependencies(h, {
    soulTowerResolver: () => tower,
    soulFlightManager: flyManager,
  });
  h.enemyFactory.registerPooledClass('Mob1', Mob1Enemy, enemy => enemy.configure(deps));
  const enemy = h.enemyManager.spawnByKey('Mob1', true, false);
  assert.equal(enemy.typeIndex, 1, 'Mob1 typeIndex=1');
  h.tick(0); // playSpawn 0ms → MOVING
  let soulEvents = 0;
  h.eventBus.on(GameEvents.ENEMY_SOUL_DELIVERED, null, () => soulEvents++);
  enemy.hit(enemy.health, { id: 1 });
  h.tick(100, 80);
  assert.equal(flyCalls.length, 0, 'typeIndex=1 不触发 sB 灵魂投射');
  assert.equal(soulEvents, 0, '不发 ENEMY_SOUL_DELIVERED 事件');
});

test('塔条件不满足：Ci=false 不投射', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5, deathDurationMs: 100 }); t.after(h.cleanup);
  const flyCalls = [];
  const flyManager = { fly(...args) { flyCalls.push(args); const cb = args[args.length - 1]; if (typeof cb === 'function') cb(); } };
  // Ci=false（灵魂塔未启用）。
  const tower = { Ci: false, num: 0, range: 99999, pos: { x: 0, y: 0 } };
  const enemy = spawnZombieWithSoul(h, tower, flyManager);
  enemy.hit(enemy.health, { id: 1 });
  h.tick(100, 80);
  assert.equal(flyCalls.length, 0, 'Ci=false 塔未启用不投射');
});

test('塔条件不满足：num>=3 不投射', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5, deathDurationMs: 100 }); t.after(h.cleanup);
  const flyCalls = [];
  const flyManager = { fly(...args) { flyCalls.push(args); const cb = args[args.length - 1]; if (typeof cb === 'function') cb(); } };
  // num=3（灵魂数已达上限）。
  const tower = { Ci: true, num: 3, range: 99999, pos: { x: 0, y: 0 } };
  const enemy = spawnZombieWithSoul(h, tower, flyManager);
  enemy.hit(enemy.health, { id: 1 });
  h.tick(100, 80);
  assert.equal(flyCalls.length, 0, 'num>=3 灵魂数达上限不投射');
});

test('塔条件不满足：距离>=range 不投射', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5, deathDurationMs: 100 }); t.after(h.cleanup);
  const flyCalls = [];
  const flyManager = { fly(...args) { flyCalls.push(args); const cb = args[args.length - 1]; if (typeof cb === 'function') cb(); } };
  // range=1（极小，敌人中心到塔距离必 >=1）。
  const tower = { Ci: true, num: 0, range: 1, pos: { x: 0, y: 0 } };
  const enemy = spawnZombieWithSoul(h, tower, flyManager);
  enemy.hit(enemy.health, { id: 1 });
  h.tick(100, 80);
  assert.equal(flyCalls.length, 0, '距离>=range 死于接收范围外不投射');
});

test('默认桩塔（未注入）不触发 sB', t => {
  // DEFERRED 默认桩 soulTowerResolver 返回 {Ci:false}，sB 条件永不满足。
  const h = createEnemyRuntimeHarness({ random: () => 0.5, deathDurationMs: 100 }); t.after(h.cleanup);
  const flyCalls = [];
  const flyManager = { fly(...args) { flyCalls.push(args); const cb = args[args.length - 1]; if (typeof cb === 'function') cb(); } };
  // 不注入 soulTowerResolver（用默认桩）。
  const deps = buildDependencies(h, { soulFlightManager: flyManager });
  h.enemyFactory.registerPooledClass('Zombie', ZombieEnemy, enemy => enemy.configure(deps));
  const enemy = h.enemyManager.spawnByKey('Zombie', true, false);
  h.tick(EMERGE_MS, 80);
  enemy.hit(enemy.health, { id: 1 });
  h.tick(100, 80);
  assert.equal(flyCalls.length, 0, '默认桩塔 Ci=false 不触发 sB');
});

test('sB 飞行到达发 ENEMY_SOUL_DELIVERED 携带 isPlayerLane/坐标/pathIndex', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5, deathDurationMs: 100 }); t.after(h.cleanup);
  let flyOnComplete = null;
  const flyManager = { fly(...args) { flyOnComplete = args[args.length - 1]; } };
  const tower = { Ci: true, num: 0, range: 99999, pos: { x: 0, y: 0 } };
  const enemy = spawnZombieWithSoul(h, tower, flyManager);
  // 设已知 pathIndex 供事件校验。
  enemy.currentPathIndex = 5;
  let soulArgs = null;
  h.eventBus.on(GameEvents.ENEMY_SOUL_DELIVERED, null, (...args) => { soulArgs = args; });
  enemy.hit(enemy.health, { id: 1 });
  h.tick(100, 80);
  assert.equal(typeof flyOnComplete, 'function', 'sB 调飞行管理器传 onComplete');
  // 模拟飞行到达：调 onComplete。
  flyOnComplete();
  assert.ok(soulArgs, 'onComplete 发 ENEMY_SOUL_DELIVERED 事件');
  assert.equal(soulArgs[0], enemy.isPlayerLane, '携带 isPlayerLane');
  assert.equal(soulArgs[3], 5, '携带 pathIndex=currentPathIndex');
});
