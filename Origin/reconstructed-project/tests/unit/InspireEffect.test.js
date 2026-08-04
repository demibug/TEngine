'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { InspireEffect } = require('../../src/skills/effects/InspireEffect');
const { BuffType } = require('../../src/buffs/BuffTypes');

// ---------- mock 服务构造（风格对齐 GeneralActiveSkills.test.js） ----------
// buffManager.applyBuff 记录每次调用入参，audioRegistry.play 记录播放音效 key。
function makeMockServices() {
  const applied = [];
  let buffSeq = 100;
  const buffManager = {
    applied,
    applyBuff(targetId, type, num, mult, time) {
      const id = ++buffSeq;
      applied.push({ targetId, type, num, mult, time, id });
      return id;
    },
  };
  const played = [];
  const audioRegistry = { played, play(key) { played.push(key); }, stop() {}, clearOwner() {} };
  return { buffManager, audioRegistry };
}

// 构造一个友军敌 mock（仅需 id 字段供 applyBuff 使用）。
function makeAlliedEnemy(id) {
  return { id, x: 100, y: 100 };
}

// ---------- Inspire：对范围内友军敌连施 3 buff + 号角音效 ----------
// 对应 spec Scenario「Inspire 施加 3 个 buff」：
//   WHEN Inspire 触发 THEN 对范围内友军敌施加 SCALE .2/MAX_HP .5/MOVE_SPEED .3
//   （durationMs=5000）+ 音效 zhangJiao_skill_horn。
test('Inspire 对每个范围内友军敌施加 SCALE .2 / MAX_HP .5 / MOVE_SPEED .3（durationMs=5000）', () => {
  const enemy1 = makeAlliedEnemy(1);
  const enemy2 = makeAlliedEnemy(2);
  const services = makeMockServices();
  const effect = new InspireEffect(services);
  const result = effect.execute({ alliedEnemies: [enemy1, enemy2], durationMs: 5000 });

  // 返回状态对齐 inline lambda：APPLIED + ids（每目标 3 个 buffId）。
  assert.equal(result.status, 'APPLIED');
  assert.equal(result.ids.length, 6, '2 目标 × 3 buff = 6 个 buffId');

  // 每目标施加 3 buff，共 6 次调用。
  assert.equal(services.buffManager.applied.length, 6);

  // 验证 enemy1 的 3 个 buff（忠实 bundle:31176 顺序：SCALE/MAX_HP/MOVE_SPEED）。
  const e1Buffs = services.buffManager.applied.filter(b => b.targetId === 1);
  assert.equal(e1Buffs.length, 3, 'enemy1 施加 3 个 buff');

  assert.equal(e1Buffs[0].type, BuffType.SCALE, '第一个 buff 为 SCALE(6)');
  assert.equal(e1Buffs[0].num, 0.2, 'SCALE 值 .2');
  assert.equal(e1Buffs[0].mult, true, 'multiplicative=true');
  assert.equal(e1Buffs[0].time, 5000, 'durationMs=5000');

  assert.equal(e1Buffs[1].type, BuffType.MAX_HP, '第二个 buff 为 MAX_HP(4)');
  assert.equal(e1Buffs[1].num, 0.5, 'MAX_HP 值 .5');
  assert.equal(e1Buffs[1].mult, true, 'multiplicative=true');
  assert.equal(e1Buffs[1].time, 5000, 'durationMs=5000');

  assert.equal(e1Buffs[2].type, BuffType.MOVE_SPEED, '第三个 buff 为 MOVE_SPEED(3)');
  assert.equal(e1Buffs[2].num, 0.3, 'MOVE_SPEED 值 .3');
  assert.equal(e1Buffs[2].mult, true, 'multiplicative=true');
  assert.equal(e1Buffs[2].time, 5000, 'durationMs=5000');

  // 验证 enemy2 同样施加 3 buff（数值/类型一致）。
  const e2Buffs = services.buffManager.applied.filter(b => b.targetId === 2);
  assert.equal(e2Buffs.length, 3, 'enemy2 施加 3 个 buff');
  assert.equal(e2Buffs[0].type, BuffType.SCALE);
  assert.equal(e2Buffs[1].type, BuffType.MAX_HP);
  assert.equal(e2Buffs[2].type, BuffType.MOVE_SPEED);
});

// ---------- Inspire：默认 durationMs=5000 ----------
test('Inspire 未传 durationMs 时默认 5000', () => {
  const enemy = makeAlliedEnemy(1);
  const services = makeMockServices();
  const effect = new InspireEffect(services);
  effect.execute({ alliedEnemies: [enemy] });
  // inline lambda 默认 durationMs=5000，忠实还原 bundle 第 5 参 b[101]。
  assert.equal(services.buffManager.applied.length, 3);
  for (const b of services.buffManager.applied) {
    assert.equal(b.time, 5000, '默认 durationMs=5000');
  }
});

// ---------- Inspire：播放 zhangJiao_skill_horn 音效 ----------
test('Inspire 触发后播放 zhangJiao_skill_horn 音效', () => {
  const enemy = makeAlliedEnemy(1);
  const services = makeMockServices();
  const effect = new InspireEffect(services);
  effect.execute({ alliedEnemies: [enemy], durationMs: 5000 });
  // bundle:31177 playSound("zhangJiao_skill_horn")，spec 验收 MUST 播放此音效。
  assert.ok(
    services.audioRegistry.played.includes('zhangJiao_skill_horn'),
    '已播放 zhangJiao_skill_horn',
  );
});

// ---------- Inspire：无 audioRegistry 时仍结算 buff 且不抛异常 ----------
test('Inspire 无 audioRegistry 时仍结算 3 buff 且不抛异常', () => {
  const enemy = makeAlliedEnemy(1);
  const services = makeMockServices();
  delete services.audioRegistry;
  // 仅注入 buffManager，audioRegistry 缺省应跳过音效，不阻塞 buff 施加。
  const effect = new InspireEffect({ buffManager: services.buffManager });
  assert.doesNotThrow(() => effect.execute({ alliedEnemies: [enemy], durationMs: 5000 }));
  assert.equal(services.buffManager.applied.length, 3, '无 audioRegistry 仍施加 3 buff');
});

// ---------- Inspire：无 buffManager 时跳过目标，返回空 ids ----------
test('Inspire 无 buffManager 时跳过目标，ids 为空但状态 APPLIED', () => {
  const enemy = makeAlliedEnemy(1);
  const effect = new InspireEffect({ audioRegistry: makeMockServices().audioRegistry });
  // inline lambda：buffManager 缺失 continue，仍返回 APPLIED + 空 ids。
  const result = effect.execute({ alliedEnemies: [enemy], durationMs: 5000 });
  assert.equal(result.status, 'APPLIED');
  assert.equal(result.ids.length, 0, '无 buffManager 时 ids 为空');
});

// ---------- Inspire：空目标列表不施加 buff ----------
test('Inspire 友军敌列表为空时不施加任何 buff', () => {
  const services = makeMockServices();
  const effect = new InspireEffect(services);
  const result = effect.execute({ alliedEnemies: [], durationMs: 5000 });
  assert.equal(result.status, 'APPLIED');
  assert.equal(result.ids.length, 0);
  assert.equal(services.buffManager.applied.length, 0, '无目标则无 buff 施加');
});
