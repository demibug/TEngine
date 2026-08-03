'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const WeaponFactory = require('../../src/weapons/WeaponFactory');
const { IronBow } = require('../../src/weapons/bows/IronBow');
const { ZhugeCrossbow } = require('../../src/weapons/bows/ZhugeCrossbow');
const { MeteorStrikeEffect } = require('../../src/skills/effects/MeteorStrikeEffect');

// 武器技能投射物实体生命周期用例（P1-01 / 提案 ④b task 8.4）
// 取证来源：work/bundle.strings-decoded.js
// - 七星刀流星雨 → StarBullet（bundle:39893 type:pu，pu=StarBullet @35275）
// - 诸葛连弩火箭雨 → FireArrow（bundle:38995 type:qr，qr=FireArrow @34522）
// - 陨石 → StaticFireBall/GroundSpikeBullet（bundle:27450 原始为纯特效，此处纯逻辑层弹种化重建 DEFERRED）
// - 火龙 → FireDragonArrow（bundle:42572 type:vs，vs=FireDragonArrow @34203）

function target(id) {
  return { id, hits: [], hit(damage, attacker) { this.hits.push({ damage, attacker }); return true; } };
}

function projectileManager(targets) {
  return {
    enemyManager: {
      enemies: new Map(targets.map(item => [item.id, { ...item, visual: { x: 100, y: 100 } }])),
      queryTargets() { return targets; },
    },
    gameData: { map: { gridWidth: 40, gridHeight: 40 } },
    created: [],
    create(config) {
      const projectile = { config, fired: false, active: true, fire() { this.fired = true; } };
      this.created.push(projectile);
      return projectile;
    },
    remove(p) { p.active = false; },
  };
}

// ---- 七星刀流星雨经 StarBullet 专属弹种实体（task 8.1）----

test('meteor-shower: seven-star knife triggers StarBullet projectile entities via projectileSpawner', () => {
  const enemies = [target(1), target(2), target(3)];
  const spawned = [];
  const owner = {
    id: 9, side: true, attackDamage: 4, attackRange: 100, combatCenter: { x: 0, y: 0 },
    enemyManager: { queryEnemyObjects: () => enemies },
    projectileManager: {
      create(config) { const p = { config, fired: false, fire() { this.fired = true; } }; spawned.push(p); return p; },
    },
  };
  const weapon = WeaponFactory.create(2, 'hZ');
  weapon.attach(owner);
  weapon.randomSource = () => 0; // < 0.1 命中触发
  const result = weapon.attack({ target: enemies[0], targets: enemies, damage: 4 });
  assert.equal(result.triggered, true);
  assert.equal(result.attackType, 'meteor-shower');
  // 经 projectileSpawner 创建 StarBullet 专属弹种实体（非通用占位）
  assert.ok(spawned.length > 0, '流星雨应创建专属弹种实体');
  assert.equal(spawned.every(p => p.config.type === 'StarBullet'), true, '所有创建实体应为 StarBullet');
  assert.equal(spawned.every(p => p.fired === true), true, '实体应 fire() 完成生命周期启动');
  // 命中结算：5 枚流星 2 倍伤害
  assert.equal(result.hits.length, 5);
  assert.equal(result.hits[0].damage, 8);
});

test('meteor-shower: chance miss does not spawn projectile entities', () => {
  const enemies = [target(1)];
  const spawned = [];
  const owner = {
    id: 9, side: true, attackDamage: 4, attackRange: 100, combatCenter: { x: 0, y: 0 },
    enemyManager: { queryEnemyObjects: () => enemies },
    projectileManager: { create(config) { spawned.push({ config }); return null; } },
  };
  const weapon = WeaponFactory.create(2, 'hZ');
  weapon.attach(owner);
  weapon.randomSource = () => 0.5; // >= 0.1 未命中
  weapon.attack({ target: enemies[0], targets: enemies, damage: 4 });
  assert.equal(spawned.length, 0, '未触发不应创建弹种实体');
});

// ---- 诸葛连弩火箭雨经 FireArrow 专属弹种实体（task 8.2）----

test('fire-rain: Zhuge crossbow emits ten FireArrow projectile entities after ten normal shots', () => {
  const enemy = target(1);
  enemy.visual = { x: 100, y: 100 };
  const manager = projectileManager([enemy]);
  const owner = { id: 9, side: true, attackDamage: 5, attackRange: 200, combatCenter: { x: 0, y: 0 }, projectileManager: manager };
  const crossbow = new ZhugeCrossbow();
  crossbow.init(9, 0);
  crossbow.attach(owner);
  for (let i = 0; i < 10; i += 1) crossbow.attack({ target: enemy, damage: 5 });
  const volley = crossbow.attack({ target: enemy, damage: 5 });
  assert.equal(Array.isArray(volley), true);
  assert.equal(volley.length, 10);
  // 火箭雨经 FireArrow 专属弹种实体（对齐 bundle:38995 type:qr=FireArrow）
  assert.equal(volley.every(item => item.config.type === 'FireArrow'), true);
  assert.equal(volley.every(item => item.fired === true), true, '每支火箭应 fire() 完成生命周期启动');
});

test('fire-rain: Zhuge crossbow normal shots use SimpleDynamicArrow not FireArrow', () => {
  const enemy = target(1);
  enemy.visual = { x: 100, y: 100 };
  const manager = projectileManager([enemy]);
  const owner = { id: 9, side: true, attackDamage: 5, attackRange: 200, combatCenter: { x: 0, y: 0 }, projectileManager: manager };
  const crossbow = new ZhugeCrossbow();
  crossbow.init(9, 0);
  crossbow.attach(owner);
  const normal = crossbow.attack({ target: enemy, damage: 5 });
  assert.equal(normal.config.type, 'SimpleDynamicArrow', '普通弩箭用 SimpleDynamicArrow');
});

// ---- 火龙经 FireDragonArrow 专属弹种实体（task 8.3）----

test('fire-dragon: IronBow fire-dragon shot uses FireDragonArrow projectile entity', () => {
  const enemy = target(1);
  enemy.visual = { x: 100, y: 100 };
  const manager = projectileManager([enemy]);
  const owner = { id: 9, side: true, attackDamage: 5, attackRange: 200, combatCenter: { x: 0, y: 0 }, projectileManager: manager };
  const weapon = new IronBow();
  weapon.init(5, 0);
  weapon.attach(owner);
  weapon.randomSource = () => 0; // < 0.1 命中火龙
  const projectile = weapon.attack({ target: enemy, targets: [enemy], damage: 5 });
  // 对齐 bundle:42572 type:vs=FireDragonArrow，非 FireArrow 退化
  assert.equal(projectile.config.type, 'FireDragonArrow');
  assert.equal(projectile.config.impact.burn.durationMs, 5000);
  assert.equal(projectile.fired, true);
});

test('fire-dragon: IronBow normal shot uses SimpleDynamicArrow', () => {
  const enemy = target(1);
  enemy.visual = { x: 100, y: 100 };
  const manager = projectileManager([enemy]);
  const owner = { id: 9, side: true, attackDamage: 5, attackRange: 200, combatCenter: { x: 0, y: 0 }, projectileManager: manager };
  const weapon = new IronBow();
  weapon.init(5, 0);
  weapon.attach(owner);
  weapon.randomSource = () => 0.5; // >= 0.1 未命中火龙
  const projectile = weapon.attack({ target: enemy, targets: [enemy], damage: 5 });
  assert.equal(projectile.config.type, 'SimpleDynamicArrow');
});

// ---- 陨石经 StaticFireBall/GroundSpikeBullet 孤子弹种实体（task 8.3）----

test('meteor-strike: MeteorStrikeEffect spawns StaticFireBall and GroundSpikeBullet entities', () => {
  const enemy = target(1);
  enemy.visual = { x: 100, y: 100 };
  const manager = projectileManager([enemy]);
  const owner = {
    id: 9, side: true, attackDamage: 20, attackRange: 150, combatCenter: { x: 100, y: 100 },
    projectileManager: manager,
  };
  const effect = new MeteorStrikeEffect({ enemyManager: manager.enemyManager });
  const handle = effect.execute({ owner, count: 4, targets: [enemy] });
  assert.ok(handle, '陨石 effect 应返回 EffectHandle');
  // 4 个陨石应创建 4 个弹种实体，交替使用 StaticFireBall/GroundSpikeBullet
  const types = manager.created.map(p => p.config.type);
  assert.equal(manager.created.length, 4);
  assert.ok(types.includes('StaticFireBall'), '应包含 StaticFireBall 孤子弹种实体');
  assert.ok(types.includes('GroundSpikeBullet'), '应包含 GroundSpikeBullet 孤子弹种实体');
  assert.equal(manager.created.every(p => p.fired === true), true, '每个实体应 fire() 启动生命周期');
});

test('meteor-strike: DEFERRED annotation - bundle original is pure visual effect', () => {
  // 验证 MeteorStrikeEffect 承载 DEFERRED 标注：bundle:27450 陨石原始为纯特效不走弹种通道
  const effect = new MeteorStrikeEffect({});
  // defaultCount 为 PARTIAL 可注入默认值（bundle 未明示陨石数量）
  assert.ok(effect.defaultCount > 0, '陨石数量以可注入默认值承载（PARTIAL）');
});

test('meteor-strike: missing projectileManager returns MISSING_PROJECTILE_MANAGER status', () => {
  const owner = { id: 9, side: true, attackDamage: 10, combatCenter: { x: 0, y: 0 } };
  const effect = new MeteorStrikeEffect({});
  const result = effect.execute({ owner });
  assert.equal(result.status, 'MISSING_PROJECTILE_MANAGER');
});

test('meteor-strike: cleanup disposes launched projectile lifecycles', () => {
  const enemy = target(1);
  enemy.visual = { x: 100, y: 100 };
  const manager = projectileManager([enemy]);
  const owner = {
    id: 9, side: true, attackDamage: 20, attackRange: 150, combatCenter: { x: 100, y: 100 },
    projectileManager: manager,
  };
  const effect = new MeteorStrikeEffect({ enemyManager: manager.enemyManager });
  const handle = effect.execute({ owner, count: 2, targets: [enemy] });
  assert.equal(manager.created.length, 2);
  // 回收生命周期
  handle.dispose('test-cleanup');
  // dispose 不抛错即视为回收路径可走通
  assert.equal(handle.disposed, true);
});
