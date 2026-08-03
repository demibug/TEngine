'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { GeneralUnit } = require('../../src/generals/GeneralUnit');
const Weapon = require('../../src/weapons/types/Weapon');
const LongBow = require('../../src/weapons/bows/LongBow').LongBow;
const { AttackEffectManager } = require('../../src/combat/AttackEffectManager');
const { ObjectPool } = require('../../src/core/ObjectPool');

function createGeneral({ manager, enemy, enemyManager = null, now = () => 0 } = {}) {
  const general = new GeneralUnit({ id: 101, name: '赵云' });
  general.configureCombat({
    enemyManager: enemyManager || { queryTargets() { return [enemy]; } },
    position: { x: 0, y: 0, width: 0, height: 0 },
    attackPower: 7,
    attackRange: 100,
    attackIntervalSeconds: 1,
    attackEffectManager: manager,
    now,
  });
  const weapon = new Weapon();
  weapon.init('10', 1);
  general.attachWeapon(weapon);
  return general;
}

test('GeneralUnit routes direct weapon impacts through AttackEffectManager', () => {
  const manager = new AttackEffectManager({ objectPool: new ObjectPool() });
  const hits = [];
  const enemy = {
    id: 1,
    x: 20,
    y: 0,
    targetable: true,
    isTargetableBy() { return true; },
    hit(damage, attacker) { hits.push({ damage, attacker }); return true; },
  };
  let now = 1000;
  const general = createGeneral({ manager, enemy, now: () => now });

  assert.equal(general.updateCombat(now).attacked, false);
  now = 2000;
  const attack = general.updateCombat(now);
  assert.equal(attack.attacked, true);
  assert.equal(hits.length, 0);
  assert.equal(manager.activeCount, 1);
  assert.equal(attack.result.effectHandle.type, 'weapon');

  manager.update(0);
  assert.equal(hits.length, 1);
  assert.equal(hits[0].damage, 7);
  assert.equal(hits[0].attacker, general);
  assert.equal(manager.activeCount, 0);
});

test('GeneralUnit recycling cancels a pending unified weapon impact', () => {
  const manager = new AttackEffectManager();
  let hits = 0;
  const enemy = {
    id: 2,
    x: 20,
    y: 0,
    targetable: true,
    isTargetableBy() { return true; },
    hit() { hits += 1; return true; },
  };
  let now = 1000;
  const general = createGeneral({ manager, enemy, now: () => now });
  general.updateCombat(now);
  now = 2000;
  general.updateCombat(now);
  assert.equal(manager.activeCount, 1);

  assert.equal(general.recycle('test'), true);
  assert.equal(manager.activeCount, 0);
  manager.update(0);
  assert.equal(hits, 0);
});

test('GeneralUnit registers bow weapon projectiles in the same effect manager', () => {
  const manager = new AttackEffectManager();
  const projectile = {
    active: true,
    projectileId: 77,
    fireCount: 0,
    fire() { this.fireCount += 1; },
  };
  const enemy = {
    id: 3,
    x: 80,
    y: 0,
    visual: { x: 80, y: 0 },
    targetable: true,
    isTargetableBy() { return true; },
  };
  const projectileEnemyManager = {
    enemies: new Map([[enemy.id, enemy]]),
    queryTargets() { return [enemy]; },
  };
  const projectileManager = {
    enemyManager: projectileEnemyManager,
    gameData: { map: { gridWidth: 80, gridHeight: 80 } },
    create() { projectile.manager = this; return projectile; },
    remove(value) { value.active = false; },
  };
  let now = 1000;
  const general = createGeneral({ manager, enemy, enemyManager: projectileEnemyManager, now: () => now });
  general.buffManager = { applyBuff() { return 1; }, removeBuff() {} };
  general.projectileManager = projectileManager;
  const bow = new LongBow();
  bow.init(1, 0);
  general.attachWeapon(bow);

  general.updateCombat(now);
  now = 2000;
  const attack = general.updateCombat(now);
  assert.equal(attack.attacked, true);
  assert.equal(projectile.fireCount, 1);
  assert.equal(manager.activeCount, 1);

  projectile.active = false;
  manager.update(0);
  assert.equal(manager.activeCount, 0);
});
