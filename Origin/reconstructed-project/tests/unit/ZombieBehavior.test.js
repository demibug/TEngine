'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { createEnemyRuntimeHarness } = require('../mocks/createEnemyRuntimeHarness');
const { ZombieEnemy } = require('../../src/entities/types/ZombieEnemy');
const { EnemyRuntimeState } = require('../../src/entities/EnemyBase');

/**
 * 任务组 7.1：Zombie 用例
 * 覆盖 spec Scenario：
 *   - Zombie 浮现阶段机取代 playSpawn 出生（三阶段推进 + 到位触发 MOVING）
 *   - 气泡粒子仅在浮现过程生成（5%/上限3/上升40px/s/淡出回收）
 *   - Zombie 蹒跚呼吸覆写（startMovingAnimation 启动 tB / stopMovingAnimation 复位）
 *   - Zombie gameOver 清理浮现资源（定时器/遮罩/贴图/气泡）
 *
 * harness 不导出内部 dependencies，测试自行组装（对齐 createEnemyRuntimeHarness 内部结构）。
 * gB 由 gameLoop frameLoop→update 以 80ms 子步长驱动（harness tick 触发 Laya.timer.tick）。
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

// 浮现总时长（80ms 步长量化）：phase1 约 3 步 + phase2 约 3 步 + phase3 约 10 步 ≈ 16 步 = 1280ms，
// 取 1400ms 保证到位触发 MOVING（步长量化使实际推进略慢于连续时间理论值）。
const EMERGE_TOTAL_MS = 1400;

test('Zombie 浮现状态机取代 playSpawn：phase1→phase2→phase3→到位触发 MOVING', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  // 出生后 lB=1（phase1），未走 playSpawn（visual.visible 由 Hw 置 true，非 playSpawn 缩放）。
  assert.equal(enemy.lB, 1);
  assert.equal(enemy.visual.visible, true);
  assert.equal(enemy.currentState, EnemyRuntimeState.SPAWNING);
  assert.equal(enemy.pB.name, 'swampDecal');   // 沼泽贴图已创建
  assert.equal(enemy.yB.name, 'swampMask');    // 遮罩已创建
  assert.equal(enemy.gameLoop.isRegistered(`mob1_${enemy.id}`), true); // gB 定时器已注册
  // playSpawn 不应被调用（Zombie 覆写 init 跳过 NormalEnemyBase 的 playSpawn 段）。
  const playSpawnCalls = h.presentation.calls.filter(c => c[0] === 'playSpawn');
  assert.equal(playSpawnCalls.length, 0, 'Zombie 不应调用 playSpawn');

  // phase1：沼泽 alpha 淡入 200ms。
  h.tick(200, 80);
  assert.equal(enemy.lB, 2, 'phase1 完成 → phase2');
  assert.equal(enemy.pB.alpha, 1, '沼泽淡入完成 alpha=1');
  assert.equal(enemy.animation.y, 160, 'phase1 末 y=160');

  // phase2：上升 160→140，80px/s 需 250ms。
  h.tick(250, 80);
  assert.equal(enemy.lB, 3, 'phase2 完成 → phase3');
  assert.equal(enemy.animation.y, 140, 'phase2 末 y=140');

  // phase3：上升 140→80，80px/s 需 750ms；到位触发 fB→changeState(MOVING)+dB 清理。
  h.tick(750, 80);
  assert.equal(enemy.lB, 0, 'phase3 完成 → dB 清理 lB=0');
  assert.equal(enemy.currentState, EnemyRuntimeState.MOVING, '到位触发 MOVING');
  assert.equal(enemy.animation.y, 80, 'phase3 末 y=80');
  assert.equal(enemy.gameLoop.isRegistered(`mob1_${enemy.id}`), false, 'dB 注销 gB 定时器');
});

test('气泡仅 phase2/3 生成：phase1 与 MOVING 态不生成', t => {
  const origRandom = Math.random;
  Math.random = () => 0.01; // 始终 < 0.05 触发生成
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnZombie(h, buildDependencies(h));
    // phase1（200ms）期间不应生成气泡（bubble 仅在 phase2/3 由 gB 调用）。
    h.tick(180, 80);
    assert.equal(enemy.lB, 1, '仍在 phase1');
    assert.equal(enemy.bubbles.length, 0, 'phase1 不生成气泡');
    // 推进到 phase1 完成（200ms）进入 phase2，但 lB=1→2 的转换发生在 lB===1 分支内，
    // 该帧不调 bubble；需再推进一帧让 gB 走 lB===2 分支才生成气泡。
    h.tick(80, 80);  // 完成 phase1（alpha 达 1）→ lB=2
    h.tick(80, 80);  // phase2 第一帧：调 bubble 生成
    assert.ok(enemy.lB >= 2, '进入 phase2/3');
    const phase2Count = h.presentation.calls.filter(c => c[0] === 'createBubbleParticle').length;
    assert.ok(phase2Count > 0, 'phase2/3 生成气泡');
  } finally {
    Math.random = origRandom;
  }
});

test('气泡 5% 概率：random>=0.05 不生成，random<0.05 生成', t => {
  // random=0.5（>=0.05）→ 不生成
  const origRandom = Math.random;
  Math.random = () => 0.5;
  let h;
  try {
    h = createEnemyRuntimeHarness({ random: () => 0.5 });
    const enemy = spawnZombie(h, buildDependencies(h));
    h.tick(EMERGE_TOTAL_MS, 80); // 跑完整个浮现
    assert.equal(enemy.bubbles.length, 0, 'random>=0.05 不生成气泡');
    const createCalls = h.presentation.calls.filter(c => c[0] === 'createBubbleParticle');
    assert.equal(createCalls.length, 0, '5% 概率下 random=0.5 不触发');
  } finally {
    Math.random = origRandom;
    h.cleanup();
  }
});

test('气泡上限 3 个并存', t => {
  const origRandom = Math.random;
  Math.random = () => 0.01; // 始终触发
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnZombie(h, buildDependencies(h));
    h.tick(200, 80); // phase1 完成
    // phase2/3 持续推进，每帧生成 1 个直到上限 3。
    h.tick(80, 80);
    h.tick(80, 80);
    h.tick(80, 80);
    h.tick(80, 80);
    assert.ok(enemy.bubbles.length <= 3, '气泡并存不超过 3 个');
    assert.equal(enemy.bubbles.length, 3, '持续触发后达上限 3');
  } finally {
    Math.random = origRandom;
  }
});

test('气泡以 40px/s 上升、alpha 0.7/s 衰减、出水面(y<=0)淡出回收', t => {
  const origRandom = Math.random;
  Math.random = () => 0.01;
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnZombie(h, buildDependencies(h));
    h.tick(200, 80); // phase1
    h.tick(80, 80);  // phase2 第一帧：生成 1 气泡，初始 y=40
    assert.equal(enemy.bubbles.length, 1);
    const bubble = enemy.bubbles[0];
    assert.equal(bubble.y, 40 - 40 * 80 / 1000, '上升 40px/s：80ms 升 3.2px');
    assert.ok(bubble.alpha < 1, 'alpha 衰减 0.7/s');
    // 持续上升至 y<=0（初始 40px，40px/s 需约 1000ms）。phase3 完成前持续冒泡推进。
    h.tick(1000, 80);
    // 气泡出水面后 splice + recoverBubbleParticle 回收。
    const recoverCalls = h.presentation.calls.filter(c => c[0] === 'recoverBubbleParticle').length;
    assert.ok(recoverCalls > 0, '出水面气泡经 port 淡出回收');
  } finally {
    Math.random = origRandom;
  }
});

test('Zombie 蹒跚呼吸覆写：startMovingAnimation 启动 tB，stopMovingAnimation 复位 scale', t => {
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_TOTAL_MS, 80); // 浮现完成 → MOVING（_enterState(MOVING) 调 startMovingAnimation→tB）
  const breathCalls = h.presentation.calls.filter(c => c[0] === 'startZombieBreathing');
  assert.ok(breathCalls.length > 0, 'MOVING 态启动 tB 蹒跚呼吸');
  // 停止移动（_exitState(MOVING)）调 stopMovingAnimation→stopZombieBreathing killAll+scale(1,1)。
  enemy.stopMovingAnimation();
  const stopCalls = h.presentation.calls.filter(c => c[0] === 'stopZombieBreathing');
  assert.ok(stopCalls.length > 0, '停止移动调 stopZombieBreathing');
  assert.equal(enemy.animation.scaleX, 1);
  assert.equal(enemy.animation.scaleY, 1, 'killAll Tween + scale(1,1) 复位');
});

test('Zombie gameOver 清理浮现资源：定时器/遮罩/贴图/气泡', t => {
  const origRandom = Math.random;
  Math.random = () => 0.01;
  try {
    const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
    const enemy = spawnZombie(h, buildDependencies(h));
    h.tick(200, 80); // phase1
    h.tick(240, 80); // phase2 + 部分 phase3，留有活跃气泡
    const activeBubbles = enemy.bubbles.length;
    assert.ok(activeBubbles > 0, 'gameOver 前有活跃气泡');
    assert.equal(enemy.gameLoop.isRegistered(`mob1_${enemy.id}`), true, 'gB 定时器活跃');
    // gameOver：先 dB 清理浮现资源，再 super.gameOver。
    // dB 在 super.gameOver 前执行：注销定时器、清遮罩、移除沼泽贴图、回收气泡、lB=0。
    enemy.gameOver();
    assert.equal(enemy.gameLoop.isRegistered(`mob1_${enemy.id}`), false, 'dB 注销 gB 定时器');
    assert.equal(enemy.bubbles.length, 0, '全部气泡回收');
    assert.equal(enemy.lB, 0, 'lB 复位');
    const recoverCalls = h.presentation.calls.filter(c => c[0] === 'recoverBubbleParticle').length;
    assert.ok(recoverCalls >= activeBubbles, '活跃气泡经 port 淡出回收');
    assert.equal(enemy.inPool, true, 'gameOver 后入池');
  } finally {
    Math.random = origRandom;
  }
});

test('Zombie gameOver 先 dB 再 super.gameOver（dB 在 super 前）', t => {
  // 验证覆写顺序：dB 清理在前，避免 super.gameOver 后定时器/Tween 已被父类清理导致 dB 误操作。
  const h = createEnemyRuntimeHarness({ random: () => 0.5 }); t.after(h.cleanup);
  const enemy = spawnZombie(h, buildDependencies(h));
  h.tick(EMERGE_TOTAL_MS, 80);
  // gameOver 返回 true（非池状态首次回收），dB 已先执行使 lB=0。
  const result = enemy.gameOver();
  assert.equal(result, true);
  assert.equal(enemy.lB, 0, 'dB 先于 super 执行，lB=0');
});
