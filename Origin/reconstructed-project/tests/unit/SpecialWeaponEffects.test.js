'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const WeaponFactory = require('../../src/weapons/WeaponFactory');

// 武器特殊效果用例（P1-01 / 提案 ④a special-weapons-projectiles）
// 取证来源：work/bundle.strings-decoded.js weaponDesc

function makeTarget(id, opts = {}) {
  const t = {
    id,
    hp: opts.hp != null ? opts.hp : 100,
    maxHp: opts.maxHp || 100,
    currentState: opts.currentState || 0,
    hits: [],
    hit(damage, attacker) { this.hp -= damage; this.hits.push({ damage, attacker }); if (this.hp <= 0) this.currentState = 4; return true; },
    takeDamage(d, a) { return this.hit(d, a); },
    isDead() { return this.currentState === 4 || this.hp <= 0; },
    side: false,
  };
  return t;
}

function makeBuffManager() {
  const applied = [];
  return {
    applied,
    applyBuff(targetId, type, num, multiplicative, time, custom) {
      applied.push({ targetId, type, num, multiplicative, time, custom });
      return applied.length;
    },
    has(targetId, type) { return applied.some(a => a.targetId === targetId && a.type === type); },
  };
}

function makeEconomy() {
  let gold = 0;
  return {
    gold,
    award(side, amount, reason) { gold += amount; this.gold = gold; return amount; },
  };
}

function makeOwner(opts = {}) {
  return {
    id: 9,
    side: true,
    attackDamage: opts.attackDamage || 10,
    attackRange: opts.attackRange || 100,
    combatCenter: { x: 0, y: 0 },
    level: opts.level || 1,
    generalName: opts.generalName || null,
    buffManager: opts.buffManager || makeBuffManager(),
    battleEconomy: opts.battleEconomy || makeEconomy(),
    enemyManager: opts.enemyManager || null,
    unitRegistry: opts.unitRegistry || null,
  };
}

// ---- 概率触发攻速类（任务4.1）----

test('tigerRoar: 10% chance hit grants 30% attack speed to allies for 10s', () => {
  const buffMgr = makeBuffManager();
  const ally = { id: 5, side: true };
  const owner = makeOwner({ buffManager: buffMgr, unitRegistry: { queryAllies: () => [ownerRef, ally] } });
  const ownerRef = owner;
  // 修正：queryAllies 返回含 owner
  owner.unitRegistry = { queryAllies: () => [owner, ally] };
  const weapon = WeaponFactory.create(2, 'hY');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05; // < 0.1 命中
  const enemy = makeTarget(1);
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.attackType, 'tiger-roar');
  const speedBuffs = buffMgr.applied.filter(a => a.type === 1); // ATTACK_SPEED
  assert.ok(speedBuffs.length > 0, 'should apply ATTACK_SPEED buff');
  assert.equal(speedBuffs[0].num, 0.3);
  assert.equal(speedBuffs[0].time, 10000);
  assert.equal(speedBuffs[0].multiplicative, true);
});

test('tigerRoar: chance miss falls back to generic attack', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr });
  const weapon = WeaponFactory.create(2, 'hY');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.5; // >= 0.1 未命中
  const enemy = makeTarget(1);
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, false);
  assert.equal(enemy.hits.length, 1);
  assert.equal(enemy.hits[0].damage, 10);
});

test('wolfHowl: 10% chance grants 20% attack speed for 10s', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr });
  owner.unitRegistry = { queryAllies: () => [owner] };
  const weapon = WeaponFactory.create(2, 'hE');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05;
  const enemy = makeTarget(1);
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.attackType, 'wolf-howl');
  const speedBuffs = buffMgr.applied.filter(a => a.type === 1);
  assert.equal(speedBuffs[0].num, 0.2);
});

test('stunChance: 10% chance stuns target', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr });
  const weapon = WeaponFactory.create(2, 'hU');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05;
  const enemy = makeTarget(1);
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.stunned, true);
  const stunBuffs = buffMgr.applied.filter(a => a.type === 8); // STUN
  assert.ok(stunBuffs.length > 0);
});

// ---- 首击触发类（任务4.2）----

test('goldenSpearArray: first-hit 20% triggers 3 arrays 3x damage + STUN 500ms', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr });
  const weapon = WeaponFactory.create(1, 'hy');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05;
  const enemy = makeTarget(1, { hp: 1000, maxHp: 1000 });
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.arrays, 3);
  assert.equal(result.multiplier, 3);
  const stunBuffs = buffMgr.applied.filter(a => a.type === 8);
  assert.ok(stunBuffs.length > 0);
  assert.equal(stunBuffs[0].time, 500);
});

test('goldenSpearArray: 马超 exclusive triggers 5 arrays', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr, generalName: '马超' });
  const weapon = WeaponFactory.create(1, 'hy');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05;
  const enemy = makeTarget(1, { hp: 1000, maxHp: 1000 });
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.arrays, 5);
});

test('ironSpearArray: first-hit 20% triggers 1 array (NOT 3) 3x + STUN 500ms', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr });
  const weapon = WeaponFactory.create(1, 'hq');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05;
  const enemy = makeTarget(1, { hp: 1000, maxHp: 1000 });
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.arrays, 1, '铁枪 bundle:43066 为 1 个枪阵，非 3 个');
});

test('hookFall: first-hit 20% applies KNOCKDOWN 2000ms', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr });
  const weapon = WeaponFactory.create(1, 'hv'); // 钩镰枪 bundle:40028 register(1,hv)
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05;
  const enemy = makeTarget(1);
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  const knockdownBuffs = buffMgr.applied.filter(a => a.type === 17); // KNOCKDOWN
  assert.ok(knockdownBuffs.length > 0);
  assert.equal(knockdownBuffs[0].time, 2000);
});

test('ancientGold: first-hit awards 1 gold via economy', () => {
  const economy = makeEconomy();
  const owner = makeOwner({ battleEconomy: economy });
  const weapon = WeaponFactory.create(2, 'hV');
  weapon.attach(owner);
  const enemy = makeTarget(1);
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.gold, 1);
  assert.equal(economy.gold, 1);
});

// ---- 计数触发类（任务4.3）----

test('tripleBlade: every 10th attack releases 2x group blade qi', () => {
  const owner = makeOwner({ attackDamage: 10 });
  const enemies = [makeTarget(1, { hp: 1000 }), makeTarget(2, { hp: 1000 })];
  owner.enemyManager = { queryEnemyObjects: () => enemies };
  const weapon = WeaponFactory.create(2, 'hF');
  weapon.attach(owner);
  // 前 9 次普通攻击
  for (let i = 0; i < 9; i += 1) weapon.attack({ target: enemies[0], targets: enemies, damage: 10 });
  assert.equal(enemies[0].hp, 1000 - 90);
  // 第 10 次触发刀气
  const result = weapon.attack({ target: enemies[0], targets: enemies, damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.multiplier, 2);
  assert.ok(result.hits.length >= 1);
  // 每个命中敌人受 20 伤害
  assert.ok(result.hits.every(h => h.damage === 20));
});

test('ironKnifeSpeed: same target stacks +5% attack speed, target switch resets', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr });
  const weapon = WeaponFactory.create(2, 'hD');
  weapon.attach(owner, buffMgr);
  const enemyA = makeTarget(1, { hp: 1000 });
  const enemyB = makeTarget(2, { hp: 1000 });
  weapon.attack({ target: enemyA, damage: 10 });
  assert.equal(weapon._ironKnifeStacks, 1);
  weapon.attack({ target: enemyA, damage: 10 });
  assert.equal(weapon._ironKnifeStacks, 2);
  // 切目标重置
  weapon.attack({ target: enemyB, damage: 10 });
  assert.equal(weapon._ironKnifeStacks, 1);
});

test('gentlemanVillain: every 10th attack triggers 50% gentleman/villain', () => {
  const owner = makeOwner({ attackDamage: 10 });
  const weapon = WeaponFactory.create(3, 'h8'); // 龙渊剑
  weapon.attach(owner);
  const enemy = makeTarget(1, { hp: 1000 });
  for (let i = 0; i < 9; i += 1) weapon.attack({ target: enemy, damage: 10 });
  weapon.randomSource = () => 0.3; // < 0.5 → gentleman
  const result = weapon.attack({ target: enemy, damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.branch, 'gentleman');
});

test('gentlemanVillain: 刘备 only triggers gentleman', () => {
  const owner = makeOwner({ attackDamage: 10, generalName: '刘备' });
  const weapon = WeaponFactory.create(3, 'h8');
  weapon.attach(owner);
  const enemy = makeTarget(1, { hp: 1000 });
  for (let i = 0; i < 9; i += 1) weapon.attack({ target: enemy, damage: 10 });
  weapon.randomSource = () => 0.8; // > 0.5 但刘备限君子
  const result = weapon.attack({ target: enemy, damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.branch, 'gentleman');
});

test('gentlemanVillain: 曹操 only triggers villain', () => {
  const owner = makeOwner({ attackDamage: 10, generalName: '曹操' });
  const weapon = WeaponFactory.create(3, 'hJ'); // 莫邪
  weapon.attach(owner);
  const enemy = makeTarget(1, { hp: 1000 });
  for (let i = 0; i < 9; i += 1) weapon.attack({ target: enemy, damage: 10 });
  weapon.randomSource = () => 0.2; // < 0.5 但曹操限小人
  const result = weapon.attack({ target: enemy, damage: 10 });
  assert.equal(result.branch, 'villain');
});

// ---- 击杀触发类（任务4.4）----

test('pearBlossom: kill triggers 8 petals hitting 8 enemies', () => {
  const owner = makeOwner({ attackDamage: 100 });
  const enemies = [];
  for (let i = 1; i <= 8; i += 1) enemies.push(makeTarget(i, { hp: 1000 }));
  const victim = makeTarget(99, { hp: 50 }); // 被 100 伤害击杀
  owner.enemyManager = { queryEnemyObjects: () => enemies };
  const weapon = WeaponFactory.create(1, 'hx'); // 梨花枪
  weapon.attach(owner);
  weapon.randomSource = () => 0.5;
  const result = weapon.attack({ target: victim, targets: [victim], damage: 100 });
  assert.equal(result.triggered, true);
  assert.equal(result.petals, 8);
  assert.ok(result.hits.length >= 8 + 1); // 原目标 + 8 花瓣
});

test('dragonBladeQi: kill releases blade qi to all enemies (PARTIAL multiplier)', () => {
  const owner = makeOwner({ attackDamage: 100 });
  const enemies = [makeTarget(1, { hp: 1000 }), makeTarget(2, { hp: 1000 })];
  const victim = makeTarget(99, { hp: 50 });
  owner.enemyManager = { queryEnemyObjects: () => enemies };
  const weapon = WeaponFactory.create(2, 'h0'); // 青龙偃月刀
  weapon.attach(owner);
  const result = weapon.attack({ target: victim, targets: [victim], damage: 100 });
  assert.equal(result.triggered, true);
  assert.equal(result.multiplier, 1.5); // PARTIAL 标注的默认倍率
  assert.ok(result.hits.some(h => h.bladeQi));
});

test('steelTipSpeed: kill grants +50% attack speed for 2s', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr, attackDamage: 100 });
  const victim = makeTarget(99, { hp: 50 });
  const weapon = WeaponFactory.create(1, 'hw'); // 点钢枪
  weapon.attach(owner, buffMgr);
  const result = weapon.attack({ target: victim, targets: [victim], damage: 100 });
  assert.equal(result.triggered, true);
  const speedBuffs = buffMgr.applied.filter(a => a.type === 1);
  assert.ok(speedBuffs.length > 0);
  assert.equal(speedBuffs[0].num, 0.5);
  assert.equal(speedBuffs[0].time, 2000);
});

// ---- 等级/概率类（任务4.5）----

test('skyHalberd: level 1 has 10% chance, 5x damage + KNOCKDOWN', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr, level: 1, attackDamage: 10 });
  const weapon = WeaponFactory.create(2, 'hL'); // 方天画戟
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05; // < 0.1
  const enemy = makeTarget(1, { hp: 1000, maxHp: 1000 });
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.multiplier, 5);
  assert.equal(result.instantKill, false); // hp 充足不瞬杀
  const knockdownBuffs = buffMgr.applied.filter(a => a.type === 17);
  assert.ok(knockdownBuffs.length > 0);
});

test('skyHalberd: level 5 has 30% chance', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr, level: 5, attackDamage: 10 });
  const weapon = WeaponFactory.create(2, 'hL');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.25; // < 0.3 但 > 0.1
  const enemy = makeTarget(1, { hp: 1000, maxHp: 1000 });
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.level, 5);
});

test('skyHalberd: instant-kills enemy below 20% HP', () => {
  const buffMgr = makeBuffManager();
  const owner = makeOwner({ buffManager: buffMgr, level: 1, attackDamage: 10 });
  const weapon = WeaponFactory.create(2, 'hL');
  weapon.attach(owner, buffMgr);
  weapon.randomSource = () => 0.05;
  const enemy = makeTarget(1, { hp: 10, maxHp: 1000 }); // 1% < 20%
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.instantKill, true);
});

test('dragonSpearFly: 10% chance flies spear to all enemies 5x damage', () => {
  const owner = makeOwner({ attackDamage: 10 });
  const enemies = [makeTarget(1, { hp: 1000 }), makeTarget(2, { hp: 1000 }), makeTarget(3, { hp: 1000 })];
  owner.enemyManager = { queryEnemyObjects: () => enemies };
  const weapon = WeaponFactory.create(1, 'hA'); // 龙胆亮银枪
  weapon.attach(owner);
  weapon.randomSource = () => 0.05;
  const result = weapon.attack({ target: enemies[0], targets: enemies, damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.multiplier, 5);
  assert.ok(result.hits.length >= 3);
  assert.ok(result.hits.every(h => h.damage === 50));
});

test('dragonSpearFly: 赵云 exclusive 5% chance', () => {
  const owner = makeOwner({ attackDamage: 10, generalName: '赵云' });
  const enemies = [makeTarget(1, { hp: 1000 })];
  owner.enemyManager = { queryEnemyObjects: () => enemies };
  const weapon = WeaponFactory.create(1, 'hA');
  weapon.attach(owner);
  weapon.randomSource = () => 0.04; // < 0.05
  const result = weapon.attack({ target: enemies[0], targets: enemies, damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.exclusive, true);
});

test('snakeSpear: snake count = base 1 + (level-1)*1', () => {
  const owner = makeOwner({ level: 3, attackDamage: 10 });
  const weapon = WeaponFactory.create(1, 'hz'); // 丈八蛇矛
  weapon.attach(owner);
  const enemy = makeTarget(1, { hp: 1000 });
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 10 });
  assert.equal(result.triggered, true);
  assert.equal(result.snakeCount, 3); // 1 + (3-1)*1
});

// ---- DEFERRED/PARTIAL 标注回归（任务4.6）----

test('PARTIAL annotations: dragonBladeQi and gentlemanVillain use injectable constants', () => {
  const { DRAGON_BLADE_QI_MULTIPLIER, GENTLEMAN_VILLAIN_DAMAGE_MULTIPLIER } = require('../../src/weapons/WeaponSpecialEffects');
  assert.ok(DRAGON_BLADE_QI_MULTIPLIER > 0, 'PARTIAL 倍率以可注入常量承载');
  assert.ok(GENTLEMAN_VILLAIN_DAMAGE_MULTIPLIER > 0, 'PARTIAL 伤害以可注入常量承载');
});

// ---- 基础武器与七星刀不受影响（任务4.7）----

test('base weapons (wood knife) still do generic direct attack', () => {
  const owner = makeOwner({ attackDamage: 7 });
  const weapon = WeaponFactory.create(2, '-1'); // 木刀
  weapon.attach(owner);
  const enemy = makeTarget(1);
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 7 });
  assert.equal(result.attacked, true);
  assert.equal(result.triggered, false);
  assert.equal(result.attackType, 'melee');
  assert.equal(enemy.hits[0].damage, 7);
});

test('seven-star knife meteor shower still works', () => {
  const enemies = [makeTarget(1), makeTarget(2), makeTarget(3)];
  const owner = makeOwner({ attackDamage: 4, attackRange: 100 });
  owner.enemyManager = { queryEnemyObjects: () => enemies };
  const weapon = WeaponFactory.create(2, 'hZ');
  weapon.attach(owner);
  weapon.randomSource = () => 0;
  const result = weapon.attack({ target: enemies[0], targets: enemies, damage: 4 });
  assert.equal(result.triggered, true);
  assert.equal(result.attackType, 'meteor-shower');
  assert.equal(result.hits.length, 5);
});
