'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const WeaponFactory = require('../../src/weapons/WeaponFactory');

// 属性武器加成用例（P1-01 / 提案 ④ special-weapons-projectiles）
// 取证：铁剑 bundle:39704 攻击力+3；大戟 bundle:41430 攻击距离+1；
//       长剑 bundle:40025 / 长刀 bundle:43892 / 长枪 bundle:43895 攻击距离+0.5

test('iron sword (3:hN) grants +3 attack power', () => {
  const weapon = WeaponFactory.create(3, 'hN');
  const mods = weapon.getCombatModifiers();
  assert.equal(mods.attackPower, 3);
  assert.equal(mods.range, 0);
  assert.equal(mods.attackSpeed, 0);
});

test('great halberd (1:hs) grants +1 attack range', () => {
  const weapon = WeaponFactory.create(1, 'hs');
  const mods = weapon.getCombatModifiers();
  assert.equal(mods.range, 1);
  assert.equal(mods.attackPower, 0);
});

test('long sword (3:h2) grants +0.5 attack range', () => {
  const weapon = WeaponFactory.create(3, 'h2');
  assert.equal(weapon.txt, '长剑');
  const mods = weapon.getCombatModifiers();
  assert.equal(mods.range, 0.5);
});

test('long blade (2:hC) grants +0.5 attack range', () => {
  const weapon = WeaponFactory.create(2, 'hC');
  const mods = weapon.getCombatModifiers();
  assert.equal(mods.range, 0.5);
});

test('long spear (1:hp) grants +0.5 attack range', () => {
  const weapon = WeaponFactory.create(1, 'hp');
  const mods = weapon.getCombatModifiers();
  assert.equal(mods.range, 0.5);
});

test('attribute weapon bonuses apply to owner via attach', () => {
  // 模拟单位应用武器修正：attach 后 owner 可读取 getCombatModifiers 合入自身属性
  const owner = { id: 1, attackPower: 10, attackRange: 40, attackSpeed: 0 };
  const weapon = WeaponFactory.create(3, 'hN');
  weapon.attach(owner);

  const mods = weapon.getCombatModifiers();
  const effectiveAttackPower = owner.attackPower + mods.attackPower;
  assert.equal(effectiveAttackPower, 13);

  weapon.detach();
  // detach 后武器不再提供加成（owner 属性由挂载方负责回滚，武器侧 mods 仍可查但不再作用于 owner）
  assert.equal(weapon.active, false);
});

test('base weapons without attribute bonuses return zero modifiers', () => {
  // 木刀/短刀/短剑 等基础武器无属性加成
  const wood = WeaponFactory.create(2, '-1');
  assert.deepEqual(wood.getCombatModifiers(), { attackPower: 0, range: 0, attackSpeed: 0 });
});
