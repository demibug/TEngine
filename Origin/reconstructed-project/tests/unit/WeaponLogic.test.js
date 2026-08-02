'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const WeaponFactory = require('../../src/weapons/WeaponFactory');
const { WeaponAttackEffect } = require('../../src/weapons/WeaponAttackEffect');
const { IronBow } = require('../../src/weapons/bows/IronBow');
const { OverlordBow } = require('../../src/weapons/bows/OverlordBow');
const { ZhugeCrossbow } = require('../../src/weapons/bows/ZhugeCrossbow');

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
      const projectile = { config, fired: false, fire() { this.fired = true; } };
      this.created.push(projectile);
      return projectile;
    },
  };
}

test('generic registered weapons resolve to concrete direct impacts', () => {
  const enemy = target(1);
  const owner = { id: 9, side: true, attackDamage: 7 };
  const weapon = WeaponFactory.create(2, 'hY');
  weapon.attach(owner);
  const result = weapon.attack({ target: enemy, targets: [enemy], damage: 7 });
  assert.equal(result.attacked, true);
  assert.equal(result.attackType, 'melee');
  assert.deepEqual(enemy.hits.map(hit => hit.damage), [7]);
});

test('seven-star knife can independently apply five two-times meteor impacts', () => {
  const enemies = [target(1), target(2), target(3)];
  const owner = { id: 9, side: true, attackDamage: 4, attackRange: 100, combatCenter: { x: 0, y: 0 }, enemyManager: { queryEnemyObjects: () => enemies } };
  const weapon = WeaponFactory.create(2, 'hZ');
  weapon.attach(owner);
  weapon.randomSource = () => 0;
  const result = weapon.attack({ target: enemies[0], targets: enemies, damage: 4 });
  assert.equal(result.triggered, true);
  assert.equal(result.hits.length, 5);
  assert.equal(enemies[0].hits[0].damage, 8);
});

test('bow attacks accept GeneralUnit attack contexts and preserve special projectile data', () => {
  const enemy = target(1);
  enemy.visual = { x: 100, y: 100 };
  const manager = projectileManager([enemy]);
  const owner = { id: 9, side: true, attackDamage: 5, attackRange: 200, combatCenter: { x: 0, y: 0 }, projectileManager: manager };
  const weapon = new IronBow();
  weapon.init(5, 0);
  weapon.attach(owner);
  weapon.randomSource = () => 0;
  const projectile = weapon.attack({ target: enemy, targets: [enemy], damage: 5 });
  assert.equal(projectile.config.type, 'FireArrow');
  assert.equal(projectile.config.impact.burn.durationMs, 5000);
  assert.equal(projectile.fired, true);
});

test('overlord bow forwards ricochet and Zhuge crossbow emits ten fire arrows after ten normal shots', () => {
  const enemy = target(1);
  enemy.visual = { x: 100, y: 100 };
  const manager = projectileManager([enemy]);
  const owner = { id: 9, side: true, attackDamage: 5, attackRange: 200, combatCenter: { x: 0, y: 0 }, projectileManager: manager };
  const overlord = new OverlordBow();
  overlord.init(7, 0);
  overlord.attach(owner);
  const overlordProjectile = overlord.attack({ target: enemy, damage: 5 });
  assert.equal(overlordProjectile.config.impact.ricochet.chance, .5);

  const crossbow = new ZhugeCrossbow();
  crossbow.init(9, 0);
  crossbow.attach(owner);
  for (let index = 0; index < 10; index += 1) crossbow.attack({ target: enemy, damage: 5 });
  const volley = crossbow.attack({ target: enemy, damage: 5 });
  assert.equal(Array.isArray(volley), true);
  assert.equal(volley.length, 10);
  assert.equal(volley.every(item => item.config.type === 'FireArrow'), true);
});

test('weapon attack effects are reusable without a presentation dependency', () => {
  const enemy = target(1);
  const effect = new WeaponAttackEffect({ type: 'direct', target: enemy, damage: 3 });
  assert.deepEqual(effect.apply().hits.map(hit => hit.damage), [3]);
  assert.deepEqual(effect.apply().hits.map(hit => hit.damage), [3]);
});
