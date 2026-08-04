'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { PuppetEnemy, PUPPET_HEALTH_MULTIPLIERS } = require('../../src/entities/types/PuppetEnemy');
const { EnemyRuntimeState } = require('../../src/entities/EnemyBase');
const { GameEvents } = require('../../src/core/EventBus');

/**
 * 任务组 7.3：Puppet 用例
 * 覆盖 spec Scenario：
 *   - Puppet 爱心粒子持续生成（300ms 周期/0.1~0.5 缩放/放大 1/3000/淡出 1/1000/上限约 8）
 *   - Puppet 速度为 10
 *   - Puppet 路径事件订阅更新 currentPathIndex
 *   - Puppet 待机缩放 0.9
 *   - Puppet gameOver 回收爱心粒子
 *
 * rB 在 update 中 super.update 之后调用；Puppet 走 playSpawn 出生（spawnDurationMs=0 立即 MOVING）。
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

function spawnPuppet(h, deps) {
  h.enemyFactory.registerPooledClass('Puppet', PuppetEnemy, enemy => enemy.configure(deps));
  return h.enemyManager.spawnByKey('Puppet', true, false);
}

test('Puppet 速度为 10（bundle:31793 字面量）', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnPuppet(h, buildDependencies(h));
  assert.equal(enemy.baseMoveSpeed, 10, 'baseMoveSpeed=10（构造 baseSpeedOverride:10）');
  assert.equal(enemy.typeIndex, 6, 'typeIndex=6');
});

test('Puppet 爱心粒子每 300ms 生成一个', t => {
  const origRandom = Math.random;
  Math.random = () => 0.3; // 0.1~0.5 区间内
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnPuppet(h, buildDependencies(h));
    h.tick(0); // playSpawn 立即完成 → MOVING（update 开始被 gameLoop 驱动）
    assert.equal(enemy.currentState, EnemyRuntimeState.MOVING);
    assert.equal(enemy.eB.length, 0, '初始无爱心');
    // update 推进 300ms → 生成第一个爱心。
    h.tick(300, 80);
    assert.equal(enemy.eB.length, 1, '300ms 生成 1 个爱心');
    // 再 300ms 生成第二个。
    h.tick(300, 80);
    assert.equal(enemy.eB.length, 2, '600ms 生成 2 个爱心');
  } finally {
    Math.random = origRandom;
  }
});

test('Puppet 爱心目标缩放 0.1~0.5', t => {
  const origRandom = Math.random;
  const targets = [];
  Math.random = () => 0.3;
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnPuppet(h, buildDependencies(h));
    h.tick(0);
    // 生成多个爱心，校验 targetScale 落在 [0.1, 0.5]。
    h.tick(900, 80);
    for (const p of enemy.eB) {
      targets.push(p.targetScale);
      assert.ok(p.targetScale >= 0.1 && p.targetScale <= 0.5, `targetScale ${p.targetScale} 在 [0.1,0.5]`);
    }
    assert.ok(targets.length > 0, '至少生成 1 个爱心');
  } finally {
    Math.random = origRandom;
  }
});

test('Puppet 爱心目标缩放随 random 变化覆盖 0.1 与 0.5 边界', t => {
  const origRandom = Math.random;
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    // random=0 → range(0.1,0.5)=0.1（下界）
    Math.random = () => 0;
    const enemy = spawnPuppet(h, buildDependencies(h));
    h.tick(0); h.tick(300, 80);
    assert.equal(enemy.eB.length, 1);
    assert.equal(enemy.eB[0].targetScale, 0.1, 'random=0 → targetScale=0.1 下界');
  } finally {
    Math.random = origRandom;
  }
  const origRandom2 = Math.random;
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    // random≈1 → range(0.1,0.5)≈0.5（上界，Math.random 严格 <1）
    Math.random = () => 0.999;
    const enemy = spawnPuppet(h, buildDependencies(h));
    h.tick(0); h.tick(300, 80);
    assert.ok(enemy.eB[0].targetScale > 0.49 && enemy.eB[0].targetScale < 0.5, 'random≈1 → targetScale≈0.5 上界');
  } finally {
    Math.random = origRandom2;
  }
});

test('Puppet 爱心缓慢放大增速 1/3000，达目标后淡出 1/1000', t => {
  const origRandom = Math.random;
  Math.random = () => 0; // targetScale=0.1（最快达目标）
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnPuppet(h, buildDependencies(h));
    h.tick(0); h.tick(300, 80); // 生成 1 爱心，targetScale=0.1
    assert.equal(enemy.eB.length, 1, '生成 1 爱心');
    const firstHeart = enemy.eB[0].img;
    // 放大阶段：scaleX += deltaMs/3000。生成帧后 scale 已略增（生成帧的 rB 更新段已推进）。
    h.tick(900, 80);
    assert.ok(firstHeart.scaleX >= 0.1, `放大至目标 0.1，实际 ${firstHeart.scaleX}`);
    // 达目标后开始淡出：alpha -= deltaMs/1000。
    assert.ok(firstHeart.alpha < 1, '达目标后开始淡出 alpha<1');
    // 推进足够时长使首颗爱心 alpha<=0 回收（期间会生成新爱心，但首颗应被回收）。
    h.tick(1200, 80);
    const firstStillPresent = enemy.eB.some(p => p.img === firstHeart);
    assert.equal(firstStillPresent, false, '首颗爱心达目标后淡出至 alpha<=0 经 port 回收');
    // 验证回收经 port recoverPuppetHeart。
    const recoverCalls = h.presentation.calls.filter(c => c[0] === 'recoverPuppetHeart').length;
    assert.ok(recoverCalls >= 1, '爱心经 port recoverPuppetHeart 回收');
  } finally {
    Math.random = origRandom;
  }
});

test('Puppet 爱心上限约 8 个并存（每 300ms 生成，总寿命约 2.5s）', t => {
  const origRandom = Math.random;
  Math.random = () => 0.999; // targetScale≈0.5（最长寿命：放大 1500ms + 淡出 1000ms = 2500ms）
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnPuppet(h, buildDependencies(h));
    h.tick(0);
    // targetScale≈0.5 时寿命最长（放大 1500ms + 淡出 1000ms ≈ 2500ms）。
    // 推进 2100ms（7 个生成周期，首个尚未回收），峰值约 7。
    let maxObserved = 0;
    // 分帧观察峰值，确保不超过上限 8。
    for (let ms = 0; ms < 2100; ms += 300) {
      h.tick(300, 80);
      if (enemy.eB.length > maxObserved) maxObserved = enemy.eB.length;
    }
    assert.ok(maxObserved <= 8, `并存上限约 8，观察峰值 ${maxObserved}`);
    assert.ok(maxObserved >= 6, `2.1s 内峰值约 7-8，实际 ${maxObserved}`);
  } finally {
    Math.random = origRandom;
  }
});

test('Puppet 路径事件订阅更新 currentPathIndex（yt 事件）', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnPuppet(h, buildDependencies(h));
  const initial = enemy.currentPathIndex;
  // 发 PUPPET_PATH_SYNC(yt) 事件携带 pathIndex。
  h.eventBus.event(GameEvents.PUPPET_PATH_SYNC, 7);
  assert.equal(enemy.currentPathIndex, 7, 'nB 回调更新 currentPathIndex=7');
  h.eventBus.event(GameEvents.PUPPET_PATH_SYNC, 12);
  assert.equal(enemy.currentPathIndex, 12, '再次更新 currentPathIndex=12');
});

test('Puppet 待机缩放 0.9：stopMovingAnimation killAll + scale(0.9,0.9)', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnPuppet(h, buildDependencies(h));
  h.tick(0); // MOVING
  // mw（stopMovingAnimation）：killAll Tween + scale(0.9,0.9) 待机缩放（非 1）。
  enemy.stopMovingAnimation();
  assert.equal(enemy.animation.scaleX, 0.9, '待机 scaleX=0.9');
  assert.equal(enemy.animation.scaleY, 0.9, '待机 scaleY=0.9（非 1）');
});

test('Puppet gameOver 回收全部爱心粒子 + 取消 yt 订阅', t => {
  const origRandom = Math.random;
  Math.random = () => 0.3;
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnPuppet(h, buildDependencies(h));
    h.tick(0); h.tick(900, 80); // 生成 3 个爱心
    assert.ok(enemy.eB.length >= 2, 'gameOver 前有活跃爱心');
    const active = enemy.eB.length;
    // gameOver：取消 yt 订阅 + 回收全部爱心 + 清空数组 + super.gameOver。
    enemy.gameOver();
    assert.equal(enemy.eB.length, 0, '全部爱心回收清空');
    assert.equal(enemy.aB, 0, 'aB 累计时间复位');
    const recoverCalls = h.presentation.calls.filter(c => c[0] === 'recoverPuppetHeart').length;
    assert.ok(recoverCalls >= active, '活跃爱心经 port recoverPuppetHeart 回收');
    // 取消 yt 订阅：gameOver 后发 yt 事件不应再更新 currentPathIndex。
    const after = enemy.currentPathIndex;
    h.eventBus.event(GameEvents.PUPPET_PATH_SYNC, 99);
    assert.equal(enemy.currentPathIndex, after, 'gameOff 后 yt 订阅已取消，不再更新');
    assert.equal(enemy.inPool, true, 'gameOver 后入池');
  } finally {
    Math.random = origRandom;
  }
});

test('Puppet 血量倍率 Sh[level-1]（bundle:12149）', t => {
  // PUPPET_HEALTH_MULTIPLIERS=[1,1.2,1.4,1.6,1.8] 对齐 bundle:12149。
  assert.deepEqual([...PUPPET_HEALTH_MULTIPLIERS], [1, 1.2, 1.4, 1.6, 1.8]);
});
