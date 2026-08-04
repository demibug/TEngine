'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const { AttackEffectManager } = require('../../src/combat/AttackEffectManager');
const { KnifeAttackTimeline } = require('../../src/combat/KnifeAttackTimeline');
const { KnifeAttackEffect } = require('../../src/combat/KnifeAttackEffect');

/**
 * 任务 6.5：刀兵时序文档用例
 *
 * 覆盖 attack-timing-finalization spec「刀兵时序必须文档化为原始 Laya timer 方案」的三个 Scenario：
 *   1. usesTimer 路径由 Laya.timer.once 精确触发，update() 只 return active 不推进 elapsed
 *   2. 无 Laya 运行时回退管理器推进路径（update() 累加 elapsed>=delayMs 触发 resolve）
 *   3. 刀兵时序约定文档化（KnifeAttackEffect 源码注释包含时序约定关键词）
 *
 * 时序约定（原始 Laya timer 方案，hu[176]→500，bundle:18885）：
 * 刀兵时序非管理器驱动；usesTimer 时 update() 只跟踪存活，命中由 Laya.timer.once 精确触发，
 * 避免固定步进漂移；无 Laya 运行时回退管理器推进路径。
 */

const SRC_DIR = path.join(__dirname, '..', '..', 'src');

function readSource(rel) {
  return fs.readFileSync(path.join(SRC_DIR, rel), 'utf8');
}

/**
 * 构造最小可用刀兵攻击者对象（对齐 UnifiedAttackSystem.test.js 刀兵 timeline 用例）。
 */
function makeAttacker(id = 8) {
  return {
    id,
    side: true,
    animationPlaybackRate: 1,
    lifecycleGeneration: 1,
    inPool: false,
    destroyed: false,
    isActive: true,
  };
}

/**
 * 构造可被命中的敌人对象。
 */
function makeEnemy(id = 3) {
  return {
    id,
    isTargetableBy() { return true; },
    hit(damage, attacker) { this._lastHit = { damage, attacker }; },
  };
}

// ----------------------------------------------------------------------------
// Scenario 1: 刀兵 usesTimer 路径由 Laya.timer 精确触发
// ----------------------------------------------------------------------------

test('刀兵 usesTimer 路径：Laya.timer.once 精确触发命中，update() 只 return active 不推进 elapsed', () => {
  const manager = new AttackEffectManager();
  const hits = [];
  const enemy = { id: 4, isTargetableBy() { return true; }, hit(damage, attacker) { hits.push({ damage, attacker }); } };

  // 捕获 Laya.timer.once 注册的延迟与回调，模拟原始 Laya timer 一次性精确触发。
  let timerDelay = null;
  let timerCaller = null;
  let timerCallback = null;
  const timeline = new KnifeAttackTimeline({
    laya: {
      timer: {
        currTimer: 0,
        once(delay, caller, callback) { timerDelay = delay; timerCaller = caller; timerCallback = callback; },
        clearAll() {},
      },
    },
    enemyManager: { getById() { return enemy; } },
    effects: { startKnifeAttack() {}, showKnifeHit() {} },
    attackEffectManager: manager,
  });
  const attacker = makeAttacker(12);
  const record = timeline.start({ attacker, target: { id: enemy.id }, damage: 5 });

  // launch() 检测到 timeline.laya.timer.once 存在即置 usesTimer=true
  assert.equal(record.effect.usesTimer, true, 'usesTimer 路径：检测到 Laya.timer.once 置 usesTimer=true');
  // Laya.timer.once 以精确延迟 500ms（hu[176]→500）注册
  assert.equal(timerDelay, 500, 'Laya.timer.once 以 500ms 精确延迟注册（hu[176]→500）');
  assert.equal(timerCaller, record.effect, 'Laya.timer.once caller 为效果自身');

  // 管理器固定步进推进远超 500ms，usesTimer 路径下 update() 只 return active 不推进计时——
  // 命中只由 Laya.timer.once 一次性回调精确触发，管理器步进不应提前命中。
  manager.update(1000);
  assert.equal(record.settled, false, 'usesTimer 路径：管理器推进 1000ms 不命中（update 不推进 elapsed）');
  assert.equal(hits.length, 0, 'usesTimer 路径：管理器推进 1000ms 不产生命中');
  assert.equal(record.effect.elapsed, 0, 'usesTimer 路径：update() 不累加 elapsed（仍为 0）');
  assert.equal(record.effect.active, true, 'usesTimer 路径：update() 只 return active 做存活跟踪，效果仍存活');

  // Laya.timer.once 一次性回调精确触发命中（模拟原始 timer 到点回调）
  timerCallback();
  assert.equal(record.settled, true, 'Laya.timer.once 回调精确触发命中');
  assert.equal(hits.length, 1, 'Laya.timer.once 回调产生一次命中');
  assert.equal(hits[0].damage, 5, '命中伤害正确');
  assert.equal(record.effect.active, false, 'Laya.timer.once 回调后效果置非存活');

  // 回调后管理器 update() 观察到 active=false，回收效果（存活跟踪职责）
  manager.update(0);
  assert.equal(manager.activeCount, 0, 'usesTimer 路径：回调命中后管理器回收效果');
});

test('刀兵 usesTimer 路径：Laya.timer.once 延迟精确为 KNIFE_HIT_DELAY_BASE_MS/playbackRate', () => {
  const manager = new AttackEffectManager();
  let timerDelay = null;
  let timerCallback = null;
  const enemy = makeEnemy(7);
  const timeline = new KnifeAttackTimeline({
    laya: {
      timer: {
        currTimer: 0,
        once(delay, caller, callback) { timerDelay = delay; timerCallback = callback; },
        clearAll() {},
      },
    },
    enemyManager: { getById() { return enemy; } },
    effects: { startKnifeAttack() {}, showKnifeHit() {} },
    attackEffectManager: manager,
  });
  // playbackRate=2 → 延迟 250ms（500/2），验证 Laya timer 精确按 playbackRate 校准
  const attacker = { ...makeAttacker(21), animationPlaybackRate: 2 };
  const record = timeline.start({ attacker, target: { id: enemy.id }, damage: 3 });

  assert.equal(record.effect.usesTimer, true, 'usesTimer=true');
  assert.equal(timerDelay, 250, 'Laya.timer.once 延迟精确为 500/playbackRate（250ms@rate2）');
  assert.equal(record.delayMs, 250, '效果 delayMs 与 Laya timer 延迟一致');

  // 管理器推进不应提前命中——usesTimer 路径命中只由 timer 回调触发
  manager.update(300);
  assert.equal(record.settled, false, '管理器推进 300ms 不命中（usesTimer 不推进）');
  timerCallback();
  assert.equal(record.settled, true, 'timer 回调精确触发命中');
});

// ----------------------------------------------------------------------------
// Scenario 2: 无 Laya 运行时回退管理器推进
// ----------------------------------------------------------------------------

test('无 Laya 运行时回退：timeline.laya.timer.once 缺失时 usesTimer=false，管理器 update() 累加 elapsed>=delayMs 触发 resolve', () => {
  const manager = new AttackEffectManager();
  const hits = [];
  const enemy = { id: 3, isTargetableBy() { return true; }, hit(damage, attacker) { hits.push({ damage, attacker }); } };

  // laya.timer 存在但无 once 方法（模拟无 Laya 运行时环境——KnifeAttackTimeline 构造仅要求 laya.timer 存在）
  const timeline = new KnifeAttackTimeline({
    laya: { timer: { currTimer: 0 } },
    enemyManager: { getById() { return enemy; } },
    effects: { startKnifeAttack() {}, showKnifeHit() {} },
    attackEffectManager: manager,
  });
  const attacker = makeAttacker(8);
  const record = timeline.start({ attacker, target: { id: enemy.id }, damage: 4 });

  // timeline.laya.timer.once 缺失 → usesTimer=false（回退管理器推进路径）
  assert.equal(record.effect.usesTimer, false, '无 Laya 运行时：timeline.laya.timer.once 缺失置 usesTimer=false');
  assert.equal(record.delayMs, 500, '回退路径延迟仍为 500ms（hu[176]→500）');

  // 管理器推进 499ms 不命中（elapsed 累加但未达 delayMs）
  manager.update(499);
  assert.equal(record.settled, false, '回退路径：推进 499ms < 500ms 不命中');
  assert.equal(hits.length, 0, '回退路径：推进 499ms 不产生命中');
  assert.equal(record.effect.elapsed, 499, '回退路径：update() 累加 elapsed=499');
  assert.equal(record.effect.active, true, '回退路径：未达 delayMs 效果仍存活');

  // 管理器再推进 1ms，elapsed>=delayMs 触发 resolve（回退管理器推进路径）
  manager.update(1);
  assert.equal(record.settled, true, '回退路径：elapsed>=delayMs 触发 resolve 命中');
  assert.equal(hits.length, 1, '回退路径：产生一次命中');
  assert.equal(hits[0].damage, 4, '回退路径：命中伤害正确');
  assert.equal(record.effect.active, false, '回退路径：resolve 后效果置非存活');
  assert.equal(manager.activeCount, 0, '回退路径：管理器回收效果');
});

test('无 Laya 运行时回退：update() 累加 elapsed 推进，不依赖 Laya.timer.once', () => {
  const manager = new AttackEffectManager();
  const enemy = makeEnemy(11);
  const timeline = new KnifeAttackTimeline({
    laya: { timer: { currTimer: 0 } },
    enemyManager: { getById() { return enemy; } },
    effects: { startKnifeAttack() {}, showKnifeHit() {} },
    attackEffectManager: manager,
  });
  const attacker = { ...makeAttacker(31), animationPlaybackRate: 2 };
  const record = timeline.start({ attacker, target: { id: enemy.id }, damage: 6 });

  assert.equal(record.effect.usesTimer, false, '无 once：usesTimer=false');
  assert.equal(record.delayMs, 250, 'playbackRate=2 回退延迟 250ms');

  // 分两步累加 elapsed，验证管理器推进路径逐次累加
  manager.update(200);
  assert.equal(record.effect.elapsed, 200, '回退路径第一步累加 elapsed=200');
  assert.equal(record.settled, false, '200ms < 250ms 不命中');
  manager.update(50);
  assert.equal(record.settled, true, '累加达 250ms 触发 resolve 命中');
  assert.equal(enemy._lastHit.damage, 6, '回退路径命中伤害正确');
});

// ----------------------------------------------------------------------------
// Scenario 3: 刀兵时序约定文档化
// ----------------------------------------------------------------------------

test('刀兵时序约定文档化：KnifeAttackEffect 源码注释包含原始 Laya timer 时序约定关键词', () => {
  const source = readSource('combat/KnifeAttackEffect.js');

  // 文档化「原始 Laya timer 方案，非管理器驱动」
  assert.ok(source.includes('Laya timer'), '注释标注「Laya timer」时序约定');
  assert.ok(source.includes('非管理器驱动'), '注释标注「非管理器驱动」');
  // 文档化原始来源 hu[176]→500 / bundle:18885
  assert.ok(source.includes('hu[176]'), '注释标注原始来源 hu[176]→500');
  assert.ok(source.includes('bundle:18885'), '注释标注原始来源 bundle:18885');
  // 文档化 usesTimer 时 update() 只 return active 不推进
  assert.ok(source.includes('return active'), '注释标注 usesTimer 时 update() 只 return active');
  assert.ok(source.includes('精确触发'), '注释标注命中由 Laya.timer.once 精确触发');
  // 文档化无 Laya 运行时回退管理器推进
  assert.ok(source.includes('回退'), '注释标注无 Laya 运行时回退路径');
  assert.ok(source.includes('elapsed>=delayMs') || source.includes('elapsed >= delayMs'),
    '注释标注回退路径 update() 累加 elapsed>=delayMs');
});

test('刀兵时序约定文档化：KnifeAttackEffect 实现满足 spec 时序契约', () => {
  const source = readSource('combat/KnifeAttackEffect.js');

  // usesTimer 检测条件文档化：检测 timeline.laya.timer.once 存在
  assert.ok(source.includes('timeline.laya.timer.once'), 'usesTimer 检测条件文档化（timeline.laya.timer.once）');
  // usesTimer 路径调用 Laya.timer.once(delayMs, resolve) 精确触发
  assert.ok(source.includes('timeline.laya.timer.once(this.delayMs'), 'usesTimer 路径调用 Laya.timer.once(delayMs, ...) 精确触发');
  // update() usesTimer 分支只 return active 不推进
  assert.ok(/if\s*\(this\.usesTimer\)\s*return\s*this\.active/.test(source),
    'update() usesTimer 分支只 return this.active 不推进 elapsed');
  // 回退分支累加 elapsed 并在 elapsed>=delayMs 触发 resolve
  assert.ok(source.includes('this.elapsed +=') || source.includes('this.elapsed+='),
    '回退分支累加 elapsed');
  assert.ok(source.includes('this.timeline.resolve(this)'),
    '回退分支 elapsed>=delayMs 触发 timeline.resolve(this)');
});

test('刀兵时序约定文档化：KnifeAttackTimeline 延迟基线 KNIFE_HIT_DELAY_BASE_MS=500 与原始 hu[176] 一致', () => {
  const source = readSource('combat/KnifeAttackTimeline.js');
  // 原始 hu[176]→500 基线常量
  assert.ok(source.includes('KNIFE_HIT_DELAY_BASE_MS = 500'), 'KNIFE_HIT_DELAY_BASE_MS=500 基线常量（hu[176]）');
  assert.ok(source.includes('hu[176]'), '注释标注 500 来源为 hu[176]');
  // 延迟按 playbackRate 校准
  assert.ok(source.includes('KNIFE_HIT_DELAY_BASE_MS / playbackRate'),
    '刀兵延迟按 playbackRate 校准（delayMs = 500/playbackRate）');
});
