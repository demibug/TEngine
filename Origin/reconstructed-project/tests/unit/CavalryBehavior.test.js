'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { CavalryEnemy } = require('../../src/entities/types/CavalryEnemy');
const { EnemyRuntimeState } = require('../../src/entities/EnemyBase');

/**
 * 任务组 7.2：Cavalry 用例
 * 覆盖 spec Scenario：
 *   - Cavalry init 创建黄圈光环（yellowCircle.png 80×30/zIndex=-1/pos(0,40)）
 *   - Cavalry 骑兵呼吸（0.78→0.82→0.8，130ms/段自循环，幅度大于 Zombie/Puppet）
 *   - Cavalry 速度 80（hu[65]）
 *   - Cavalry gameOver 移除光环复用（不销毁）
 *
 * Cavalry 不跳过 NormalEnemyBase.init（仍走 playSpawn 出生），init 后追加光环创建。
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

function spawnCavalry(h, deps) {
  h.enemyFactory.registerPooledClass('Cavalry', CavalryEnemy, enemy => enemy.configure(deps));
  return h.enemyManager.spawnByKey('Cavalry', true, false);
}

test('Cavalry init 创建黄圈光环：80×30/zIndex=-1/pos(0,40)', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnCavalry(h, buildDependencies(h));
  // 经 presentation port createCavalryAura 创建光环并 addChild。
  const createCalls = h.presentation.calls.filter(c => c[0] === 'createCavalryAura');
  assert.equal(createCalls.length, 1, 'init 调一次 createCavalryAura');
  assert.equal(createCalls[0][2], enemy.auraResource, '传 auraResource=yellowCircle.png 路径');
  // 光环节点 addChild 到 visual，名 cavalryAura。
  const aura = enemy.visual.getChildByName('cavalryAura');
  assert.ok(aura, '光环已 addChild 到 visual');
  assert.equal(aura.width, 80, '光环宽 80');
  assert.equal(aura.height, 30, '光环高 30');
  assert.equal(aura.zIndex, -1, 'zIndex=-1 地面层');
  assert.equal(aura.x, 0, 'pos x=0');
  assert.equal(aura.y, 40, 'pos y=40（脚下）');
});

test('Cavalry 速度为 80（hu[65]）', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnCavalry(h, buildDependencies(h));
  assert.equal(enemy.baseMoveSpeed, 80, 'baseMoveSpeed=80（构造 baseSpeedOverride:80）');
  assert.equal(enemy.typeIndex, 5, 'typeIndex=5');
});

test('Cavalry 骑兵呼吸：MOVING 态启动 tB（0.78→0.82→0.8）', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnCavalry(h, buildDependencies(h));
  // playSpawn spawnDurationMs=0 立即完成 → changeState(MOVING) → startMovingAnimation → tB。
  h.tick(0);
  assert.equal(enemy.currentState, EnemyRuntimeState.MOVING, '出生后进入 MOVING');
  const breathCalls = h.presentation.calls.filter(c => c[0] === 'startCavalryBreathing');
  assert.ok(breathCalls.length > 0, 'MOVING 态启动骑兵呼吸 tB');
});

test('Cavalry 停止移动：stopCavalryBreathing killAll + scale(1,1) 复位', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnCavalry(h, buildDependencies(h));
  h.tick(0); // MOVING
  enemy.stopMovingAnimation();
  const stopCalls = h.presentation.calls.filter(c => c[0] === 'stopCavalryBreathing');
  assert.ok(stopCalls.length > 0, '停止移动调 stopCavalryBreathing');
  // stopCavalryBreathing killAll Tween + scale(1,1)。
  assert.equal(enemy.animation.scaleX, 1);
  assert.equal(enemy.animation.scaleY, 1, 'scale(1,1) 复位');
});

test('Cavalry gameOver 移除光环复用（不销毁）', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnCavalry(h, buildDependencies(h));
  assert.ok(enemy.visual.getChildByName('cavalryAura'), 'gameOver 前光环存在');
  // gameOver：super.gameOver 后 removeCavalryAura 移除光环（removeSelf 不销毁）。
  enemy.gameOver();
  const removeCalls = h.presentation.calls.filter(c => c[0] === 'removeCavalryAura');
  assert.equal(removeCalls.length, 1, 'gameOver 调一次 removeCavalryAura');
  // gameOver 后 visual 已回收入池，光环 removeSelf 不再是 visual 子节点。
  assert.equal(enemy.inPool, true, 'gameOver 后入池');
});

test('Cavalry 光环经 presentation port 承载，纯逻辑层只持 auraResource 字段', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnCavalry(h, buildDependencies(h));
  // 纯逻辑层只持有 auraResource 字段与启停调用，不直接操作渲染层。
  assert.equal(enemy.auraResource, 'resources/img/gameObject/enemy/yellowCircle.png');
  // createCavalryAura/removeCavalryAura 均经 port（calls 已记录）。
  assert.ok(h.presentation.calls.some(c => c[0] === 'createCavalryAura'));
  enemy.gameOver();
  assert.ok(h.presentation.calls.some(c => c[0] === 'removeCavalryAura'));
});
