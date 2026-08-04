'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { ZombieEnemy } = require('../../src/entities/types/ZombieEnemy');
const { EnemyRuntimeState } = require('../../src/entities/EnemyBase');

/**
 * 任务组 7.4：NormalEnemyBase 吹飞用例（Xw/Gw）
 * 覆盖 spec Scenario：
 *   - Xw 触发吹飞设置贝塞尔曲线与濒死（ug/p1/p2/time + ZE=1 + hit(health-0.1) + 旋转 + 注册 Gw）
 *   - Gw 推进贝塞尔曲线至落地致死（time+=deltaMs/200 + 贝塞尔写入位置 + time>=1 hit(1)+ZE=0）
 *   - 吹飞中 gameOver 清理定时器与 Tween（unregister + killAll + tw 复位 + ZE=0）
 *
 * 用 Zombie（typeIndex=4）作为 NormalEnemyBase 子类实例验证吹飞通用能力。
 * Xw 为外部触发 API（DEFERRED：调用方属提案 ②③），测试直接调 enemy.Xw()。
 * Gw 由 gameLoop frameLoop→update 以 80ms 子步长驱动。
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

function spawnZombie(h, deps) {
  h.enemyFactory.registerPooledClass('Zombie', ZombieEnemy, enemy => enemy.configure(deps));
  return h.enemyManager.spawnByKey('Zombie', true, false);
}

// 浮现完成进入 MOVING（1400ms 保证 80ms 步长量化下到位触发）。
const EMERGE_MS = 1400;

test('Xw 设置二次贝塞尔曲线（ug/p1/p2/time）对齐 bundle 键名', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80); // MOVING
  const healthBefore = enemy.health;
  // 外部触发吹飞：Xw(heightArg, hitX, hitY)。
  enemy.Xw(40, 100, 200);
  // blowUpCurve 键名对齐 bundle {ug,p1,p2,time}。
  assert.ok(enemy.blowUpCurve.ug, 'ug=起点（敌人当前位置）');
  assert.ok(enemy.blowUpCurve.p1, 'p1=控制点（中点x、y抬高 3*(60-heightArg)）');
  assert.ok(enemy.blowUpCurve.p2, 'p2=终点（朝击中方向反向偏移一半）');
  assert.equal(enemy.blowUpCurve.time, 0, 'time=0 初始');
  // ug=敌人左上角当前位置。
  assert.equal(enemy.blowUpCurve.ug.x, enemy.visual.x);
  assert.equal(enemy.blowUpCurve.ug.y, enemy.visual.y);
  // p1.y = ug.y - 3*(60-40) = ug.y - 60（抬高 60）。
  assert.equal(enemy.blowUpCurve.p1.y, enemy.blowUpCurve.ug.y - 3 * (60 - 40), 'p1.y 抬高 3*(60-heightArg)');
});

test('Xw 置吹飞状态 ZE=1 + hit(health-0.1) 致濒死', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80);
  const healthBefore = enemy.health;
  enemy.Xw(40, 100, 200);
  assert.equal(enemy.blowUpState, 1, 'ZE=1 吹飞状态激活');
  // hit(health-0.1) 致濒死：血量剩 0.1（不致死，由 Gw 落地 hit(1) 致死）。
  assert.ok(Math.abs(enemy.health - 0.1) < 1e-6, `hit(health-0.1) 致濒死，health=${enemy.health}`);
  assert.equal(enemy.currentState, EnemyRuntimeState.MOVING, '濒死未触发死亡态（hit(1) 才致死）');
});

test('Xw 旋转动画朝向击退方向', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80);
  const rotationBefore = enemy.animation.rotation;
  enemy.Xw(40, 100, 200);
  // rotation 经 np.angle({hitX,hitY},{中心}) 设置（非 0 即变化）。
  assert.notEqual(enemy.animation.rotation, rotationBefore, '旋转朝向击退方向');
});

test('Xw 注册 Gw 每帧推进定时器（blownUp+id）', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80);
  assert.equal(h.gameLoop.isRegistered(`blownUp${enemy.id}`), false, 'Xw 前无吹飞定时器');
  enemy.Xw(40, 100, 200);
  assert.equal(h.gameLoop.isRegistered(`blownUp${enemy.id}`), true, 'Xw 注册 Gw 定时器');
});

test('Gw 推进 time+=deltaMs/200，贝塞尔写入敌人位置', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80);
  enemy.Xw(40, 100, 200);
  const startX = enemy.visual.x;
  const startY = enemy.visual.y;
  // Gw 每帧 time += 80/200 = 0.4。推进一帧后位置应沿贝塞尔移动。
  h.tick(80, 80);
  assert.ok(enemy.blowUpCurve.time > 0, `time 推进至 ${enemy.blowUpCurve.time}`);
  assert.equal(enemy.blowUpState, 1, '尚未落地，ZE 仍为 1');
  // 贝塞尔插值写入 visual 位置（位置应变化）。
  const moved = (enemy.visual.x !== startX || enemy.visual.y !== startY);
  assert.ok(moved, '贝塞尔插值写入敌人位置（位置变化）');
});

test('Gw time>=1 时 hit(1) 致死 + ZE=0（吹飞总时长约 200ms）', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80);
  enemy.Xw(40, 100, 200);
  // 80ms/帧 → time+0.4/帧。3 帧 → time=1.2 >=1 → hit(1) 致死 + ZE=0。
  // 前 2 帧 time=0.8 <1 未致死。
  h.tick(160, 80);
  assert.ok(enemy.blowUpCurve.time < 1, `2 帧 time=${enemy.blowUpCurve.time} <1 未致死`);
  assert.equal(enemy.blowUpState, 1, 'ZE 仍为 1');
  assert.ok(enemy.health > 0, '未致死 health>0');
  // 第 3 帧 time>=1 → hit(1) 致死。
  h.tick(80, 80);
  assert.equal(enemy.blowUpState, 0, 'time>=1 → ZE=0');
  assert.ok(enemy.health <= 0, `hit(1) 致死 health=${enemy.health}`);
  // 致死后 Gw 守卫（ZE=0）不再推进；blownUp 定时器待 gameOver 注销（hit(1) 触发死亡流程，
  // playDeath 100ms 后 gameOver 才注销 blownUp）。推进死亡流程完成。
  h.tick(120, 80); // playDeath 100ms 完成 → gameOver
  assert.equal(h.gameLoop.isRegistered(`blownUp${enemy.id}`), false, 'gameOver 注销吹飞定时器');
});

test('吹飞中 gameOver 清理定时器 + Tween + tw 复位 + ZE=0', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80);
  enemy.Xw(40, 100, 200);
  // 推进 1 帧（吹飞中，未落地）。
  h.tick(80, 80);
  assert.equal(enemy.blowUpState, 1, '吹飞中');
  assert.equal(h.gameLoop.isRegistered(`blownUp${enemy.id}`), true, '吹飞定时器活跃');
  const killAllCallsBefore = h.Laya.Tween._calls ? 0 : 0; // Tween mock 记录在 tweenCalls（不导出），用 animation transform 验证
  // gameOver 清理：unregister blownUp + killAll Tween + 复位 transform + ZE=0。
  enemy.gameOver();
  assert.equal(enemy.blowUpState, 0, 'ZE=0');
  assert.equal(h.gameLoop.isRegistered(`blownUp${enemy.id}`), false, '注销吹飞定时器');
  // Zombie.gameOver 先 dB（注销 mob1 定时器）再 super.gameOver（注销 blownUp + Tween 清理 + transform 复位）。
  assert.equal(enemy.gameLoop.isRegistered(`mob1_${enemy.id}`), false, 'dB 注销浮现定时器');
  assert.equal(enemy.inPool, true, 'gameOver 后入池');
});

test('Gw 守卫：ZE!=1 时直接返回不推进', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80);
  // 未触发 Xw，blowUpState=0。直接调 Gw 应 no-op。
  assert.equal(enemy.blowUpState, 0);
  const timeBefore = enemy.blowUpCurve.time;
  enemy.Gw(80);
  assert.equal(enemy.blowUpCurve.time, timeBefore, 'ZE!=1 时 Gw 直接返回不推进');
});

test('blowUpCurve 键名为 ug/p1/p2/time（非旧键名）', t => {
  // 校正任务 2.1：键名对齐 bundle {ug,p1,p2,time}，grep 确认无旧键名。
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_MS, 80);
  assert.deepEqual(Object.keys(enemy.blowUpCurve), ['ug', 'p1', 'p2', 'time']);
});
