'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { CavalryOrderEffect } = require('../../src/skills/effects/CavalryOrderEffect');

// ---------- mock 服务构造（风格对齐 GeneralActiveSkills.test.js） ----------
// enemyManager.spawnByKey 返回 mock 骑兵（含 id），audioRegistry.play 记录播放音效 key。
function makeMockServices({ spawnSeqStart = 500 } = {}) {
  let spawnSeq = spawnSeqStart;
  const spawned = [];
  const enemyManager = {
    spawned,
    // 忠实 bundle:32792 vi.jL(5, nm) → EnemyManager.spawnByKey(typeKey, isPlayerSide, isSpecial)。
    // 每次 spawnByKey 返回一个 mock 骑兵（含递增 id），供 enemyIds 映射。
    spawnByKey(typeKey, isPlayerLane, isSpecial) {
      const id = ++spawnSeq;
      const enemy = { id, typeKey, isPlayerLane, isSpecial, x: 100, y: 100 };
      spawned.push({ typeKey, isPlayerLane, isSpecial, enemy });
      return enemy;
    },
  };
  const played = [];
  // play 接收 (key, opts) 两参，与 CavalryOrderEffect 调用 play(key, { ownerId }) 一致；
  // 仅记录 key，opts 验证另设断言（见下）。
  const audioRegistry = {
    played, playedOpts: [],
    play(key, opts) { played.push(key); this.playedOpts.push({ key, opts }); },
    stop() {}, clearOwner() {},
  };
  return { enemyManager, audioRegistry };
}

// 构造 boss mock（仅需 id 与 isPlayerLane 字段，忠实 bundle this["nm"]=boss.isPlayerLane）。
function makeBoss({ id = 31, isPlayerLane = false } = {}) {
  return { id, isPlayerLane };
}

// ---------- CavalryOrder：播放 summon_cavalry_skill + 召唤 5 骑兵 ----------
// 对应 spec Scenario「CavalryOrder 召唤 5 骑兵」：
//   WHEN CavalryOrder 触发 THEN 播放音效 summon_cavalry_skill，
//   召唤 5 个骑兵单位（spawnByKey('Cavalry', isPlayerLane, false) ×5）。
test('CavalryOrder 播放 summon_cavalry_skill 音效并召唤 5 个骑兵', () => {
  const boss = makeBoss({ id: 31, isPlayerLane: false });
  const services = makeMockServices();
  const effect = new CavalryOrderEffect(services);
  const result = effect.execute({ boss });

  // 返回状态对齐 inline lambda：APPLIED + enemyIds（5 个）。
  assert.equal(result.status, 'APPLIED');
  assert.equal(result.enemyIds.length, 5, '召唤 5 个骑兵');
  // enemyIds 应为 5 个不重复 id（spawnByKey 每次返回新 mock 敌人）。
  assert.equal(new Set(result.enemyIds).size, 5, '5 个骑兵 id 不重复');

  // spawnByKey 恰好被调用 5 次。
  assert.equal(services.enemyManager.spawned.length, 5, 'spawnByKey 调用 5 次');

  // 每次调用参数忠实 bundle:32792：typeKey='Cavalry', isPlayerLane=boss.isPlayerLane, isSpecial=false。
  for (let i = 0; i < 5; i++) {
    const call = services.enemyManager.spawned[i];
    assert.equal(call.typeKey, 'Cavalry', `第 ${i + 1} 次召唤 typeKey='Cavalry'`);
    assert.equal(call.isPlayerLane, false, `第 ${i + 1} 次召唤 isPlayerLane=boss.isPlayerLane(false)`);
    assert.equal(call.isSpecial, false, `第 ${i + 1} 次召唤 isSpecial=false`);
  }

  // bundle:32789 播放 summon_cavalry_skill 音效，spec 验收 MUST 播放此音效。
  assert.ok(
    services.audioRegistry.played.includes('summon_cavalry_skill'),
    '已播放 summon_cavalry_skill',
  );
});

// ---------- CavalryOrder：isPlayerLane=true 时 spawnByKey 第二参为 true ----------
test('CavalryOrder boss.isPlayerLane=true 时 spawnByKey 第二参传 true', () => {
  const boss = makeBoss({ id: 32, isPlayerLane: true });
  const services = makeMockServices();
  const effect = new CavalryOrderEffect(services);
  effect.execute({ boss });
  // 忠实 bundle:32792 vi.jL(5, this["nm"])，nm=boss.isPlayerLane=true。
  for (const call of services.enemyManager.spawned) {
    assert.equal(call.isPlayerLane, true, 'isPlayerLane 透传 boss.isPlayerLane=true');
  }
});

// ---------- CavalryOrder：音效先于召唤播放（bundle:32789 先于 32792） ----------
test('CavalryOrder 音效先于 5 次召唤播放（bundle:32789 先于 32792）', () => {
  const boss = makeBoss({ id: 33 });
  const services = makeMockServices();
  const effect = new CavalryOrderEffect(services);
  const order = [];
  // 包装 audioRegistry.play 与 enemyManager.spawnByKey 记录调用顺序。
  const origPlay = services.audioRegistry.play.bind(services.audioRegistry);
  services.audioRegistry.play = (key, opts) => { order.push('play'); origPlay(key, opts); };
  const origSpawn = services.enemyManager.spawnByKey.bind(services.enemyManager);
  services.enemyManager.spawnByKey = (tk, lane, sp) => { order.push('spawn'); return origSpawn(tk, lane, sp); };
  effect.execute({ boss });
  // 忠实 bundle：playSound(32789) 在 jL(32792) 之前。
  assert.equal(order[0], 'play', '音效先于召唤播放');
  assert.equal(order.filter(x => x === 'spawn').length, 5);
});

// ---------- CavalryOrder：audioRegistry 缺省时仍召唤 5 骑兵且不抛异常 ----------
test('CavalryOrder 无 audioRegistry 时仍召唤 5 骑兵且不抛异常', () => {
  const boss = makeBoss({ id: 34 });
  const services = makeMockServices();
  delete services.audioRegistry;
  // 仅注入 enemyManager，audioRegistry 缺省应跳过音效，不阻塞召唤。
  const effect = new CavalryOrderEffect({ enemyManager: services.enemyManager });
  assert.doesNotThrow(() => effect.execute({ boss }));
  assert.equal(services.enemyManager.spawned.length, 5, '无 audioRegistry 仍召唤 5 骑兵');
});

// ---------- CavalryOrder：boss 缺失返回 MISSING_CAVALRY_DEPENDENCY ----------
test('CavalryOrder 缺失 boss 时返回 MISSING_CAVALRY_DEPENDENCY 且不召唤', () => {
  const services = makeMockServices();
  const effect = new CavalryOrderEffect(services);
  // inline lambda：!boss 直接返回状态，不召唤。
  const result = effect.execute({ boss: null });
  assert.equal(result.status, 'MISSING_CAVALRY_DEPENDENCY');
  assert.equal(services.enemyManager.spawned.length, 0, 'boss 缺失不召唤骑兵');
  assert.equal(services.audioRegistry.played.length, 0, 'boss 缺失不播音效');
});

// ---------- CavalryOrder：enemyManager 缺失（含 ctx 与构造均无）返回 MISSING_CAVALRY_DEPENDENCY ----------
test('CavalryOrder 缺失 enemyManager 时返回 MISSING_CAVALRY_DEPENDENCY', () => {
  const boss = makeBoss({ id: 35 });
  // 构造时不注入 enemyManager，execute 也不传 enemyManager。
  const effect = new CavalryOrderEffect({ audioRegistry: makeMockServices().audioRegistry });
  const result = effect.execute({ boss });
  assert.equal(result.status, 'MISSING_CAVALRY_DEPENDENCY');
});

// ---------- CavalryOrder：ctx.enemyManager 优先于构造注入的 enemyManager ----------
test('CavalryOrder ctx.enemyManager 优先于构造注入的 enemyManager', () => {
  const boss = makeBoss({ id: 36 });
  const ctorServices = makeMockServices({ spawnSeqStart: 100 });
  const ctxServices = makeMockServices({ spawnSeqStart: 200 });
  const effect = new CavalryOrderEffect(ctorServices);
  // inline lambda：manager = ctx.enemyManager || this.enemyManager，ctx 优先。
  const result = effect.execute({ boss, enemyManager: ctxServices.enemyManager });
  assert.equal(result.status, 'APPLIED');
  // 应使用 ctxServices 的 spawnByKey，id 从 201 起。
  assert.deepEqual(result.enemyIds, [201, 202, 203, 204, 205]);
  assert.equal(ctxServices.enemyManager.spawned.length, 5, 'ctx.enemyManager 被使用');
  assert.equal(ctorServices.enemyManager.spawned.length, 0, '构造注入的 enemyManager 未被使用');
});
