'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { SkillEffectPort } = require('../../src/skills/SkillEffectPort');
const { SkillManager } = require('../../src/skills/SkillManager');
const { SkillFactory } = require('../../src/skills/SkillFactory');
const { BattleShoutEffect } = require('../../src/skills/effects/BattleShoutEffect');
const { HolySwordEffect } = require('../../src/skills/effects/HolySwordEffect');
const { ArrowRainEffect } = require('../../src/skills/effects/ArrowRainEffect');
const { FireArrowBarrageEffect } = require('../../src/skills/effects/FireArrowBarrageEffect');
const { LeapSlashEffect } = require('../../src/skills/effects/LeapSlashEffect');
const { SevenInSevenOutEffect } = require('../../src/skills/effects/SevenInSevenOutEffect');
const { BuffType } = require('../../src/buffs/BuffTypes');

// ---------- mock 服务与 fake 武将 ----------
function makeEnemy(id, x, y) {
  return {
    id, x, y, visual: { x, y }, remainingPathDistance: 0,
    damageTaken: 0,
    isTargetableBy() { return true; },
    hit(damage, attacker) { this.damageTaken += Number(damage); this.lastAttacker = attacker; },
    takeDamage(d, a) { this.hit(d, a); },
  };
}

function makeMockServices({ enemies = [] } = {}) {
  const enemyMap = new Map(enemies.map(e => [e.id, e]));
  const buffs = [];
  let buffSeq = 100;
  const removed = [];
  const enemyManager = {
    enemies: enemyMap,
    queryEnemyObjects(cx, cy, r, side) { return enemies.slice(); },
    queryTargets(cx, cy, r, side) { return enemies.map(e => ({ id: e.id, x: e.x, y: e.y, Bm: 0 })); },
    getById(id) { return enemyMap.get(id) || null; },
  };
  const buffManager = {
    applied: buffs, removed,
    applyBuff(targetId, type, num, mult, time, custom) {
      const id = ++buffSeq; buffs.push({ targetId, type, num, mult, time, custom, id }); return id;
    },
    removeBuff(targetId, type, buffId) { removed.push({ targetId, type, buffId }); return true; },
  };
  let projSeq = 0;
  const created = [];
  const projectileManager = {
    created,
    create(config, startPoint) { const p = { projectileId: ++projSeq, active: true, fire() {}, requestedRemoval: false, manager: this, renderNode: { parent: null } }; created.push({ config, startPoint, p }); return p; },
    remove(p) { if (p) p.active = false; },
  };
  const added = [];
  const attackEffectManager = { added, add(lc) { added.push(lc); }, cancelOwner() {} };
  const played = [];
  const audioRegistry = { played, play(key, opts) { played.push(key); }, stop() {}, clearOwner() {} };
  return { enemyManager, buffManager, projectileManager, attackEffectManager, audioRegistry, enemies };
}

function makeOwner(overrides = {}) {
  return Object.assign({
    id: 21, side: false, inPool: false, currentState: 0,
    combatCenter: { x: 100, y: 100 }, attackRange: 80, attackDamage: 50, level: 1,
    stats: { level: 1, damageMultiplier: 1, attackSpeedMultiplier: 1 },
  }, overrides);
}

function makeEffectPort(services) {
  return new SkillEffectPort(services);
}

// ---------- 1. DEFERRED 回归：6 key 均不再 DEFERRED ----------
test('6 个武将技能 key 经 SkillEffectPort.execute 均不返回 DEFERRED', () => {
  const services = makeMockServices({ enemies: [makeEnemy(1, 100, 100)] });
  services.projectileManager; // 箭雨/火箭烈需要
  const port = makeEffectPort(services);
  const owner = makeOwner();
  for (const key of ['LeapSlash', 'SevenInSevenOut', 'BattleShout', 'HolySword', 'ArrowRain', 'FireArrowBarrage']) {
    const result = port.execute(key, { owner });
    assert.notEqual(result && result.status, 'DEFERRED_EFFECT_WITH_EXACT_CONTRACT', `${key} 不应 DEFERRED`);
    assert.equal(port.deferredCalls.find(d => d.key === key), undefined, `${key} 不应入 deferredCalls`);
  }
});

// ---------- 2. 战吼：范围内 STUN 2000ms，范围外不受影响，空表现桩下仍结算 ----------
test('BattleShout 对范围内敌人施加 2000ms STUN，范围外不受影响', () => {
  const inEnemy = makeEnemy(1, 100, 100);
  const services = makeMockServices({ enemies: [inEnemy] });
  // 通过 effect 类直接控制范围语义：mock queryEnemyObjects 返回全部，再单独验证范围外场景
  const effect = new BattleShoutEffect(services);
  const owner = makeOwner();
  const handle = effect.execute({ owner, radius: 80 });
  assert.equal(handle.ownerId, owner.id);
  assert.equal(services.buffManager.applied.length, 1);
  assert.equal(services.buffManager.applied[0].type, BuffType.STUN);
  assert.equal(services.buffManager.applied[0].time, 2000);
  assert.equal(handle.metadata.count, 1);
  handle.dispose();
});

test('BattleShout 在无 presentation/audioRegistry 时仍结算 STUN', () => {
  const enemy = makeEnemy(1, 100, 100);
  const services = makeMockServices({ enemies: [enemy] });
  delete services.audioRegistry;
  const effect = new BattleShoutEffect({ enemyManager: services.enemyManager, buffManager: services.buffManager });
  const handle = effect.execute({ owner: makeOwner(), radius: 80 });
  assert.equal(services.buffManager.applied.length, 1);
  assert.equal(services.buffManager.applied[0].type, BuffType.STUN);
  handle.dispose();
});

test('BattleShout 范围外敌人不被施加 STUN', () => {
  const farEnemy = makeEnemy(2, 1000, 1000);
  const services = makeMockServices({ enemies: [farEnemy] });
  // 覆盖 queryEnemyObjects 模拟半径过滤：半径 10 时无命中
  services.enemyManager.queryEnemyObjects = (cx, cy, r, side) => (r < 50 ? [] : [farEnemy]);
  const effect = new BattleShoutEffect(services);
  const handle = effect.execute({ owner: makeOwner(), radius: 10 });
  assert.equal(services.buffManager.applied.length, 0);
  handle.dispose();
});

// ---------- 3. 圣剑：范围内受伤 + KNOCKDOWN ----------
test('HolySword 对范围内敌人造成伤害并施加 KNOCKDOWN', () => {
  const enemy = makeEnemy(1, 100, 100);
  const services = makeMockServices({ enemies: [enemy] });
  const effect = new HolySwordEffect(services);
  const owner = makeOwner({ attackDamage: 50 });
  const handle = effect.execute({ owner, radius: 80 });
  assert.equal(enemy.damageTaken, 50, '圣剑范围伤害已结算');
  assert.equal(services.buffManager.applied.length, 1);
  assert.equal(services.buffManager.applied[0].type, BuffType.KNOCKDOWN);
  assert.equal(handle.metadata.count, 1);
  // bundle:45696 holyBlade_skill 音效
  assert.ok(services.audioRegistry.played.includes('holyBlade_skill'));
  handle.dispose();
});

// ---------- 4. 箭雨：发射多支箭经 ProjectileAttackEffect/ProjectileManager 登记与回收 ----------
test('ArrowRain 经 ProjectileAttackEffect/ProjectileManager 发射并登记多支箭', () => {
  const enemy = makeEnemy(1, 100, 100);
  const services = makeMockServices({ enemies: [enemy] });
  const effect = new ArrowRainEffect(services);
  const owner = makeOwner();
  const handle = effect.execute({ owner, count: 6 });
  assert.equal(services.projectileManager.created.length, 6, '创建 6 支箭投射物');
  assert.equal(services.attackEffectManager.added.length, 6, '6 支均登记到统一攻击效果管理器');
  assert.equal(handle.metadata.count, 6);
  assert.ok(services.audioRegistry.played.includes('general_arrow_rain'));
  // dispose 回收每支
  handle.dispose();
  assert.equal(services.projectileManager.created.filter(c => c.p.active).length, 0, 'dispose 后投射物均被清理');
});

// ---------- 5. 火箭烈：多重数公式 + 加箭 + DEFERRED_PROJECTILE_VARIANT ----------
test('FireArrowBarrage 多重数 n=floor(max(1,(level-1)/2))：level1 下限钳制为 1，level5 为 2', () => {
  assert.equal(FireArrowBarrageEffect.multiCount(1), 1, 'level1 下限钳制为 1');
  assert.equal(FireArrowBarrageEffect.multiCount(3), 1);
  assert.equal(FireArrowBarrageEffect.multiCount(5), 2);
  assert.equal(FireArrowBarrageEffect.multiCount(7), 3);
});

test('FireArrowBarrage 每目标加箭 k=range(1,3,true)*n，注入确定性 range', () => {
  const enemy = makeEnemy(1, 100, 100);
  const services = makeMockServices({ enemies: [enemy] });
  const rangeFn = () => 2; // 确定性 R=2
  const effect = new FireArrowBarrageEffect(Object.assign({}, services, { rangeFn }));
  // level5 → n=2，k=R*n=2*2=4 支/目标，1 目标 → 4 支
  const handle = effect.execute({ owner: makeOwner({ level: 5 }), rangeFn, radius: 80 });
  assert.equal(handle.metadata.multiCount, 2);
  assert.equal(handle.metadata.perTarget[0].k, 4, 'k = R(2) * n(2)');
  assert.equal(services.projectileManager.created.length, 4, '发射 4 支');
  assert.equal(handle.metadata.DEFERRED_PROJECTILE_VARIANT, true, '标记火焰弹种延后');
  handle.dispose();
});

test('FireArrowBarrage 弹种缺失时不阻塞触发与计数', () => {
  const enemy = makeEnemy(1, 100, 100);
  const services = makeMockServices({ enemies: [enemy] });
  const effect = new FireArrowBarrageEffect(Object.assign({}, services, { rangeFn: () => 1 }));
  const handle = effect.execute({ owner: makeOwner({ level: 1 }), radius: 80 });
  assert.equal(handle.metadata.multiCount, 1);
  assert.ok(handle.metadata.count >= 1, '仍发射至少一支通用箭');
  assert.equal(handle.metadata.DEFERRED_PROJECTILE_VARIANT, true);
  handle.dispose();
});

// ---------- 6. 跳斩：窗口内 50% 溅射，5 次后停止 ----------
test('LeapSlash 激活后 5 次攻击每次对周围 50% 溅射，5 次后停止', () => {
  const main = makeEnemy(1, 100, 100); // 主目标（攻击对象）
  const splash = makeEnemy(2, 110, 110); // 周围溅射目标
  const services = makeMockServices({ enemies: [main, splash] });
  // queryEnemyObjects 返回周围溅射目标（不含主目标，由 effect 排除主目标 id）
  const effect = new LeapSlashEffect(services);
  const owner = makeOwner({ attackDamage: 100 });
  const handle = effect.execute({ owner, splashRadius: 80 });
  assert.equal(handle.metadata.remaining, 5, 'bundle:45942 gT=5');
  assert.ok(services.audioRegistry.played.includes('guanYu_skill_roar'), 'bundle:45983 音效');
  // 施加了 ATTACK_SPEED buff 标记
  assert.ok(services.buffManager.applied.some(b => b.type === BuffType.ATTACK_SPEED));

  // 模拟 5 次攻击通知
  for (let i = 0; i < 5; i++) {
    const before = splash.damageTaken;
    handle.onOwnerAttack({ owner, target: main, damage: 100 });
    assert.equal(splash.damageTaken - before, 50, `第 ${i + 1} 次溅射 50%`);
  }
  assert.equal(handle.disposed, true, '5 次后自动 dispose');
  assert.equal(handle.metadata.remaining, 0);
  // 第 6 次不再溅射
  const afterExhaust = splash.damageTaken;
  handle.onOwnerAttack({ owner, target: main, damage: 100 });
  assert.equal(splash.damageTaken, afterExhaust, '窗口结束后不再溅射');
  // dispose 撤销 buff
  assert.ok(services.buffManager.removed.some(r => r.type === BuffType.ATTACK_SPEED));
});

// ---------- 7. 七进七出：update 驱动 7 次突进，无表现桩下仍结算 ----------
test('SevenInSevenOut 无表现桩下经 update 完成 7 次突进计数与伤害', () => {
  const enemy = makeEnemy(1, 100, 100);
  const services = makeMockServices({ enemies: [enemy] });
  delete services.audioRegistry;
  const effect = new SevenInSevenOutEffect({ enemyManager: services.enemyManager }); // 无 presentation/audioRegistry
  const owner = makeOwner({ attackDamage: 30 });
  const handle = effect.execute({ owner, dashIntervalMs: 100, dashDamage: 30, radius: 80 });
  assert.equal(handle.metadata.dashCount, 7, 'bundle:45655 七次');
  // 每次 100ms，7 次 = 700ms；分步 update
  for (let i = 0; i < 7; i++) handle.update(100);
  assert.equal(handle.disposed, true, '7 次后 dispose');
  assert.equal(handle.metadata.dashesDone, 7);
  assert.equal(enemy.damageTaken, 30 * 7, '每次突进 30 伤害，7 次共 210');
});

// ---------- 8. recycle 回归：clearOwner 清理活跃 effect，链路幂等 ----------
test('SkillManager.removeOwner 经 effectPort.clearOwner 清理活跃技能 effect', () => {
  const services = makeMockServices({ enemies: [makeEnemy(1, 100, 100)] });
  const port = makeEffectPort(services);
  const skillManager = new SkillManager().configure({
    gameLoop: { register() {}, unregister() {} },
    factory: new SkillFactory(),
    effectPort: port,
  });
  skillManager.init();
  const owner = makeOwner();
  skillManager.attach(owner, 'BattleShout');
  const result = skillManager.activate(owner.id, 'BattleShout', { owner });
  assert.equal(result.activated, true);
  // 活跃 effect 已登记
  assert.ok([...port.activeEffects].some(e => e.ownerId === owner.id), 'BattleShout effect 进入 activeEffects');
  // recycle 链路：removeOwner → clearOwner
  skillManager.removeOwner(owner.id);
  const remaining = [...port.activeEffects].filter(e => e.ownerId === owner.id);
  assert.equal(remaining.length, 0, 'clearOwner 已 dispose 该 owner 名下 effect');
  // 幂等：再次 removeOwner 不抛错
  assert.doesNotThrow(() => skillManager.removeOwner(owner.id));
});

test('SkillEffectPort.clearOwner 对无活跃 effect 的 owner 幂等不抛错', () => {
  const services = makeMockServices();
  const port = makeEffectPort(services);
  assert.doesNotThrow(() => port.clearOwner(999));
});
