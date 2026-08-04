'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const { AttackEffectManager } = require('../../src/combat/AttackEffectManager');
const {
  PikeAttackEffect,
  PIKE_ATTACK_THRUST_MS,
  PIKE_HIT_DELAY_MS,
  PIKE_EFFECT_DURATION_MS,
} = require('../../src/combat/PikeAttackEffect');
const { LayaEnemyPresentation } = require('../../src/presentation/LayaEnemyPresentation');

// ===========================================================================
// 任务 6.4：枪尖 Qx 表现 port 用例
// 覆盖 spec「枪尖 Qx 表现 port 契约」+「视觉 port 桩 no-op 不影响规则层」两个 Scenario：
//   1. port 调用验证：注入 fake presentation（记录 calls），launch 时确认 createPikeTipVisual
//      + animatePikeTipThrust 被调用，cleanup 时确认 hidePikeTipVisual 被调用。
//   2. DEFERRED 桩 no-op：真实 LayaEnemyPresentation 桩 createPikeTipVisual 返回 null、
//      其余空体，不抛异常、不创建渲染对象（animatePikeTipThrust/hidePikeTipVisual 不触发，
//      因 PikeAttackEffect 对 null 视觉做保护）。
//   3. 命中结算不受 Qx 视觉缺失影响：无 presentation（或桩 no-op）时，360ms 常量基线命中
//      仍由管理器 update() → MeleeAttackEffect.hit() 触发，规则层（AttackResolver 命中
//      + hitSet 去重）不失效。
// ===========================================================================

// 构造记录命中的 fake enemyManager（对齐 UnifiedAttackSystem.test.js 模式）。
function createFakeEnemyManager(targets) {
  return {
    queryEnemyObjects() { return targets; },
  };
}

// 构造记录 port 调用的 fake presentation（突刺段 createPikeTipVisual 返回 truthy 视觉句柄，
// 使 animatePikeTipThrust/hidePikeTipVisual 均会被调度，验证 port 调用契约）。
function createFakePresentation(visual = { id: 'fake-pike-tip' }) {
  const calls = [];
  return {
    calls,
    createPikeTipVisual(owner) {
      calls.push({ method: 'createPikeTipVisual', owner });
      return visual;
    },
    animatePikeTipThrust(v, durationMs) {
      calls.push({ method: 'animatePikeTipThrust', visual: v, durationMs });
    },
    hidePikeTipVisual(v) {
      calls.push({ method: 'hidePikeTipVisual', visual: v });
    },
  };
}

test('6.4 port 调用验证：launch 时调度 createPikeTipVisual + animatePikeTipThrust，cleanup 时调度 hidePikeTipVisual', () => {
  const hits = [];
  const targets = [
    { id: 1, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
  ];
  const enemyManager = createFakeEnemyManager(targets);
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const presentation = createFakePresentation();

  const pike = new PikeAttackEffect().launch({
    owner,
    enemyManager,
    damage: 10,
    radius: 50,
    pikeTipPresentation: presentation,
  });

  // 突刺段 port 调度：createPikeTipVisual 与 animatePikeTipThrust 均被调用。
  const createCall = presentation.calls.find(c => c.method === 'createPikeTipVisual');
  assert.ok(createCall, 'launch 时调用 createPikeTipVisual');
  assert.equal(createCall.owner, owner, 'createPikeTipVisual 传入 owner');

  // 捕获 createPikeTipVisual 返回的视觉句柄（cleanup 会将 pike.pikeTipVisual 置 null，故此处捕获）。
  const visualHandle = pike.pikeTipVisual;
  assert.equal(visualHandle.id, 'fake-pike-tip', 'pikeTipVisual 持有 createPikeTipVisual 返回的句柄');

  const animateCall = presentation.calls.find(c => c.method === 'animatePikeTipThrust');
  assert.ok(animateCall, 'launch 时调用 animatePikeTipThrust');
  assert.equal(animateCall.visual, visualHandle, 'animatePikeTipThrust 传入 createPikeTipVisual 返回的视觉句柄');
  // 突刺段时长 = PIKE_ATTACK_THRUST_MS(270) / playbackRate（playbackRate 默认 1，bundle:24733-24741 段2 时长等价）。
  assert.equal(animateCall.durationMs, PIKE_ATTACK_THRUST_MS, 'animatePikeTipThrust 时长为突刺段 270ms 常量基线');

  // 回收段前 hidePikeTipVisual 尚未调用。
  assert.ok(!presentation.calls.some(c => c.method === 'hidePikeTipVisual'),
    'cleanup 前 hidePikeTipVisual 未调用');

  // 经管理器推进至效果完成触发 cleanup（回收段 port 调度 hidePikeTipVisual）。
  const manager = new AttackEffectManager();
  manager.add(pike);
  manager.update(PIKE_EFFECT_DURATION_MS);

  const hideCall = presentation.calls.find(c => c.method === 'hidePikeTipVisual');
  assert.ok(hideCall, 'cleanup 时调用 hidePikeTipVisual');
  assert.equal(hideCall.visual, visualHandle, 'hidePikeTipVisual 传入 launch 时创建的视觉句柄');
  assert.equal(pike.pikeTipVisual, null, 'cleanup 后 pikeTipVisual 置 null');
  assert.equal(pike.active, false, '效果完成已 cleanup');
  assert.equal(manager.activeCount, 0, '管理器已回收效果');
});

test('6.4 DEFERRED 桩 no-op：真实 LayaEnemyPresentation 桩 createPikeTipVisual 返回 null 不抛异常、不创建渲染对象', () => {
  // 真实 LayaEnemyPresentation 桩（DEFERRED）：构造时需 Laya + prefabFactory，桩方法本身不依赖它们。
  const laya = {};
  const prefabFactory = { createSync() { return {}; } };
  const presentation = new LayaEnemyPresentation({ Laya: laya, prefabFactory });

  // 桩 createPikeTipVisual 返回 null（DEFERRED no-op，不创建渲染对象）。
  const visual = presentation.createPikeTipVisual({ id: 'owner' });
  assert.equal(visual, null, 'DEFERRED 桩 createPikeTipVisual 返回 null');
  // 桩记录调用（便于回归断言桩被调度）。
  assert.ok(presentation.calls.some(c => c[0] === 'createPikeTipVisual'), '桩记录 createPikeTipVisual 调用');

  // 桩 animatePikeTipThrust/hidePikeTipVisual 空体不抛异常。
  assert.doesNotThrow(() => presentation.animatePikeTipThrust({ id: 'visual' }, 270));
  assert.doesNotThrow(() => presentation.hidePikeTipVisual({ id: 'visual' }));
  assert.ok(presentation.calls.some(c => c[0] === 'animatePikeTipThrust'), '桩记录 animatePikeTipThrust 调用');
  assert.ok(presentation.calls.some(c => c[0] === 'hidePikeTipVisual'), '桩记录 hidePikeTipVisual 调用');
});

test('6.4 DEFERRED 桩 no-op 经 PikeAttackEffect 调度：launch/cleanup 不抛异常且 null 视觉保护跳过 animate/hide', () => {
  const hits = [];
  const targets = [
    { id: 1, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
  ];
  const enemyManager = createFakeEnemyManager(targets);
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const laya = {};
  const prefabFactory = { createSync() { return {}; } };
  const presentation = new LayaEnemyPresentation({ Laya: laya, prefabFactory });

  // 注入真实 DEFERRED 桩 presentation，launch 不抛异常。
  const pike = new PikeAttackEffect();
  assert.doesNotThrow(() => pike.launch({
    owner,
    enemyManager,
    damage: 10,
    radius: 50,
    pikeTipPresentation: presentation,
  }), 'DEFERRED 桩 presentation 下 launch 不抛异常');

  // 桩返回 null → pikeTipVisual 为 null，animatePikeTipThrust 因 null 保护不调度。
  assert.equal(pike.pikeTipVisual, null, '桩 createPikeTipVisual 返回 null 时 pikeTipVisual 为 null');
  assert.ok(!presentation.calls.some(c => c[0] === 'animatePikeTipThrust'),
    'null 视觉保护：animatePikeTipThrust 不被调度（避免对 null 执行 Tween）');

  // 经管理器推进完成触发 cleanup，hidePikeTipVisual 因 pikeTipVisual null 保护不调度，不抛异常。
  const manager = new AttackEffectManager();
  manager.add(pike);
  assert.doesNotThrow(() => manager.update(PIKE_EFFECT_DURATION_MS), 'DEFERRED 桩下 cleanup 不抛异常');
  assert.ok(!presentation.calls.some(c => c[0] === 'hidePikeTipVisual'),
    'null 视觉保护：hidePikeTipVisual 不被调度');
  assert.equal(pike.active, false, '效果完成已 cleanup');
});

test('6.4 命中结算不受 Qx 视觉缺失影响：无 presentation 时 360ms 常量基线命中仍由管理器 hit() 触发', () => {
  const hits = [];
  const targets = [
    { id: 1, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
    { id: 2, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
  ];
  const enemyManager = createFakeEnemyManager(targets);
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const manager = new AttackEffectManager();

  // 无 pikeTipPresentation 注入（Qx 视觉完全缺失），规则层命中必须仍可用。
  const pike = new PikeAttackEffect().launch({
    owner,
    enemyManager,
    damage: 10,
    radius: 50,
  });
  assert.equal(pike.pikeTipPresentation, null, '无 presentation 注入时 pikeTipPresentation 为 null');
  assert.equal(pike.pikeTipVisual, null, '无 presentation 时 pikeTipVisual 为 null');
  manager.add(pike);

  // 360ms 常量基线命中（PIKE_HIT_DELAY_MS，playbackRate 默认 1）。
  manager.update(PIKE_HIT_DELAY_MS - 1);
  assert.equal(hits.length, 0, '命中前（359ms）无 hit() 触发');
  assert.equal(manager.activeCount, 1, '效果仍在存活');
  manager.update(1);
  assert.equal(hits.length, 2, '命中点（360ms）经 update()→hit() 触发两目标命中');
  // hitSet 去重：同一目标不重复结算。
  manager.update(1);
  assert.equal(hits.length, 2, 'hitSet 去重：命中后重复 update 不重复结算');
});

test('6.4 命中结算不受 Qx 视觉缺失影响：DEFERRED 桩 no-op 时 360ms 命中仍由规则层正确触发', () => {
  const hits = [];
  const targets = [
    { id: 1, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
  ];
  const enemyManager = createFakeEnemyManager(targets);
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const manager = new AttackEffectManager();
  const presentation = new LayaEnemyPresentation({
    Laya: {},
    prefabFactory: { createSync() { return {}; } },
  });

  // 注入 DEFERRED 桩 presentation（Qx 视觉桩 no-op 不渲染），命中结算必须与无视觉时一致。
  const pike = new PikeAttackEffect().launch({
    owner,
    enemyManager,
    damage: 10,
    radius: 50,
    pikeTipPresentation: presentation,
  });
  assert.equal(pike.pikeTipVisual, null, 'DEFERRED 桩返回 null 视觉');
  manager.add(pike);

  manager.update(PIKE_HIT_DELAY_MS - 1);
  assert.equal(hits.length, 0, '桩 no-op 下命中前（359ms）无 hit()');
  manager.update(1);
  assert.equal(hits.length, 1, '桩 no-op 下命中点（360ms）经规则层 hit() 触发');
  assert.equal(hits[0].damage, 10, '命中伤害由规则层 AttackResolver.hit 传递');
  assert.equal(hits[0].attacker, owner, '命中 attacker 由规则层传递');

  // 推进至效果完成 cleanup（回收段桩 hidePikeTipVisual 因 null 保护不调度，规则层不失效）。
  manager.update(PIKE_EFFECT_DURATION_MS - PIKE_HIT_DELAY_MS);
  assert.equal(manager.activeCount, 0, '效果完成经管理器回收');
  assert.equal(pike.active, false, '效果已 cleanup');
});

test('6.4 playbackRate 缩放：360ms 常量基线命中与突刺段 port 时长均按 playbackRate 缩放', () => {
  const hits = [];
  const targets = [
    { id: 1, hit(damage, attacker) { hits.push({ id: this.id, damage, attacker }); } },
  ];
  const enemyManager = createFakeEnemyManager(targets);
  const owner = { id: 9, side: true, displayObject: { x: 0, y: 0 } };
  const presentation = createFakePresentation();
  const rate = 2;

  const pike = new PikeAttackEffect().launch({
    owner,
    enemyManager,
    damage: 10,
    radius: 50,
    playbackRate: rate,
    pikeTipPresentation: presentation,
  });

  // 命中基线 360 / playbackRate(2) = 180ms。
  assert.equal(pike.hitAtMs, PIKE_HIT_DELAY_MS / rate, '命中时机按 playbackRate 缩放为 180ms 基线');
  // 突刺段 port 时长 270 / playbackRate(2) = 135ms。
  const animateCall = presentation.calls.find(c => c.method === 'animatePikeTipThrust');
  assert.equal(animateCall.durationMs, PIKE_ATTACK_THRUST_MS / rate, '突刺段 port 时长按 playbackRate 缩放为 135ms');

  const manager = new AttackEffectManager();
  manager.add(pike);
  manager.update(PIKE_HIT_DELAY_MS / rate - 1);
  assert.equal(hits.length, 0, '缩放后命中前（179ms）无 hit()');
  manager.update(1);
  assert.equal(hits.length, 1, '缩放后命中点（180ms）经规则层 hit() 触发');
});

test('6.4 DEFERRED 标注回归：PikeAttackEffect 与 LayaEnemyPresentation 枪尖 Qx port 标注 DEFERRED 桩 no-op', () => {
  // 扫描源码确认枪尖 Qx port 标注 DEFERRED（对齐 DeferredAnnotations.test.js 模式），
  // 确认未自行补成原版 VFX 渲染实体（P2 范畴）。
  const srcRoot = path.join(__dirname, '..', '..', 'src');
  const pikeEffect = fs.readFileSync(path.join(srcRoot, 'combat', 'PikeAttackEffect.js'), 'utf8');
  const presentation = fs.readFileSync(path.join(srcRoot, 'presentation', 'LayaEnemyPresentation.js'), 'utf8');

  // PikeAttackEffect 标注 DEFERRED 桩 no-op + 实体 VFX 归 P2。
  assert.ok(pikeEffect.includes('DEFERRED'), 'PikeAttackEffect 标注 DEFERRED');
  assert.ok(pikeEffect.includes('pikeTipPresentation'), 'PikeAttackEffect 定义 pikeTipPresentation port 注入');
  assert.ok(pikeEffect.includes('createPikeTipVisual'), 'PikeAttackEffect 调度 createPikeTipVisual');
  assert.ok(pikeEffect.includes('animatePikeTipThrust'), 'PikeAttackEffect 调度 animatePikeTipThrust');
  assert.ok(pikeEffect.includes('hidePikeTipVisual'), 'PikeAttackEffect 调度 hidePikeTipVisual');
  // 命中结算不依赖 Qx 视觉：注释明确标注。
  assert.ok(pikeEffect.includes('不依赖 Qx 视觉'), 'PikeAttackEffect 标注命中不依赖 Qx 视觉');

  // LayaEnemyPresentation 三个 port 方法标注 DEFERRED 桩 no-op。
  assert.ok(presentation.includes('createPikeTipVisual'), 'LayaEnemyPresentation 定义 createPikeTipVisual');
  assert.ok(presentation.includes('animatePikeTipThrust'), 'LayaEnemyPresentation 定义 animatePikeTipThrust');
  assert.ok(presentation.includes('hidePikeTipVisual'), 'LayaEnemyPresentation 定义 hidePikeTipVisual');
  // 桩 createPikeTipVisual 返回 null（DEFERRED no-op，不创建渲染对象）。
  assert.ok(/createPikeTipVisual[\s\S]*?return null/.test(presentation), '桩 createPikeTipVisual 返回 null');
  // 未自行补成原版 VFX 渲染实体：pikeEff1.png 与 Tween.create(this.Qx) 仅作为注释中的 bundle 取证引用，
  // 不出现在实际渲染代码行。逐行检查含这些标记的行必须为注释行（以 * 或 // 开头，含前导空白）。
  const lines = presentation.split('\n');
  const isCommentLine = (line) => /^\s*(\*|\/\/|\/\*\*)/.test(line);
  for (const line of lines) {
    if (/pikeEff1\.png/.test(line)) {
      assert.ok(isCommentLine(line), `pikeEff1.png 仅出现在注释行（非渲染代码）: ${line.trim()}`);
    }
    if (/Tween\.create\s*\(\s*this\.Qx/.test(line)) {
      assert.ok(isCommentLine(line), `Tween.create(this.Qx) 仅出现在注释行（非渲染代码）: ${line.trim()}`);
    }
  }
  // 三个桩方法均不含实际渲染对象创建（new Image / addChild）——注释行不计。
  const codeLines = lines.filter(line => !isCommentLine(line));
  assert.ok(!codeLines.some(line => /new\s+\S*Image\s*\(\s*['"]pikeEff1/.test(line)),
    '桩内不 new Image("pikeEff1...") 创建渲染实体（VFX 归 P2）');
  assert.ok(!codeLines.some(line => /\.addChild\s*\(\s*['"]pikeEff1/.test(line)),
    '桩内不 addChild pikeEff1 渲染节点（VFX 归 P2）');
});
