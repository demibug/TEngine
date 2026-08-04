'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

/**
 * 任务组 7.6：enemies.json 数值校验用例
 * 覆盖 spec Scenario：
 *   - 共享血量数组填入 7 类敌人（healthByWave 20 个值，来源 bundle:12038）
 *   - 各敌人速度对齐 bundle（Mob0/1/2/3/Zombie=50, Cavalry=80, Puppet=10）
 *   - Zombie 血量减半修饰（bundle:31386）
 *   - Puppet 血量倍率修饰（levelMultipliers [1,1.2,1.4,1.6,1.8]，bundle:12149）
 *   - typeIndex 回退规则标注（typeIndex>=4 回退 map.oe，bundle:11619）
 *
 * 纯 JSON.parse + 字段断言，不依赖运行时。
 */
const ENEMIES_JSON_PATH = path.join(__dirname, '..', '..', 'unity-export', 'config', 'enemies.json');
const EXPECTED_HEALTH_BY_WAVE = [10,16,26,41,61,92,138,200,291,421,611,886,1285,1863,2701,3917,5680,8235,11941,17315];

function loadEnemies() {
  const raw = fs.readFileSync(ENEMIES_JSON_PATH, 'utf8');
  return JSON.parse(raw);
}

test('enemies.json 可解析且含 7 类敌人', t => {
  const data = loadEnemies();
  assert.equal(data.count, 7, 'count=7');
  const keys = Object.keys(data.types);
  assert.deepEqual(keys, ['Mob0', 'Mob1', 'Mob2', 'Mob3', 'Zombie', 'Cavalry', 'Puppet']);
});

test('共享血量数组 healthByWave 填入并含 20 个值（来源 bundle:12038）', t => {
  const data = loadEnemies();
  assert.deepEqual(data.healthByWave, EXPECTED_HEALTH_BY_WAVE, '共享血量数组 20 个值');
  assert.equal(data.healthByWave.length, 20, '20 波次');
  assert.equal(data.healthByWaveSource, 'bundle:12038 (uh[typeIndex].ph, 4 条记录相同, 4 类共享)');
});

test('各敌人速度对齐 bundle：Mob0/1/2/3/Zombie=50, Cavalry=80, Puppet=10', t => {
  const data = loadEnemies();
  const { types } = data;
  assert.equal(types.Mob0.speed, 50, 'Mob0=50（hu[45]）');
  assert.equal(types.Mob1.speed, 50, 'Mob1=50');
  assert.equal(types.Mob2.speed, 50, 'Mob2=50');
  assert.equal(types.Mob3.speed, 50, 'Mob3=50');
  assert.equal(types.Zombie.speed, 50, 'Zombie=50（无速度覆盖）');
  assert.equal(types.Cavalry.speed, 80, 'Cavalry=80（hu[65]）');
  assert.equal(types.Puppet.speed, 10, 'Puppet=10（bundle:31793 字面量）');
});

test('各敌人 typeIndex 0-6 连续', t => {
  const data = loadEnemies();
  const { types } = data;
  assert.equal(types.Mob0.typeIndex, 0);
  assert.equal(types.Mob1.typeIndex, 1);
  assert.equal(types.Mob2.typeIndex, 2);
  assert.equal(types.Mob3.typeIndex, 3);
  assert.equal(types.Zombie.typeIndex, 4);
  assert.equal(types.Cavalry.typeIndex, 5);
  assert.equal(types.Puppet.typeIndex, 6);
});

test('Zombie 血量减半修饰（bundle:31386 type==4 分支 ph/2）', t => {
  const data = loadEnemies();
  const zombie = data.types.Zombie;
  assert.equal(zombie.healthModifier, 'healthByWave[wave]/2', 'Zombie 血量÷2');
  assert.equal(zombie.modifierSource, 'bundle:31386 (type==4 分支 ph/2)', '来源 bundle:31386');
});

test('Puppet 血量倍率修饰 levelMultipliers [1,1.2,1.4,1.6,1.8]（bundle:12149）', t => {
  const data = loadEnemies();
  const puppet = data.types.Puppet;
  assert.equal(puppet.healthModifier, 'healthByWave[wave]*Sh[level-1]', 'Puppet 血量×Sh[level-1]');
  assert.deepEqual(puppet.levelMultipliers, [1, 1.2, 1.4, 1.6, 1.8], 'Sh=[1,1.2,1.4,1.6,1.8]');
  assert.equal(puppet.levelMultipliersSource, 'bundle:12149 (Sh=[1,1.2,1.4,1.6,1.8])', '来源 bundle:12149');
});

test('Mob0/Mob1/Mob2/Mob3/Cavalry 无血量修饰（healthModifier=null）', t => {
  const data = loadEnemies();
  const { types } = data;
  assert.equal(types.Mob0.healthModifier, null);
  assert.equal(types.Mob1.healthModifier, null);
  assert.equal(types.Mob2.healthModifier, null);
  assert.equal(types.Mob3.healthModifier, null);
  assert.equal(types.Cavalry.healthModifier, null);
});

test('typeIndex 回退规则标注：typeIndex>=4 回退 map.oe（bundle:11619）', t => {
  const data = loadEnemies();
  assert.ok(data.typeIndexFallback.includes('typeIndex>=4'), '标注 typeIndex>=4 回退');
  assert.ok(data.typeIndexFallback.includes('bundle:11619'), '来源 bundle:11619');
  assert.ok(data.typeIndexFallback.includes('map.oe'), '回退到 map.oe');
  // Zombie/Cavalry/Puppet typeIndex>=4，基础血量数组与 Mob0 相同（4 条 uh 记录相同），差异由各自修饰施加。
  assert.equal(data.types.Zombie.typeIndex, 4);
  assert.equal(data.types.Cavalry.typeIndex, 5);
  assert.equal(data.types.Puppet.typeIndex, 6);
});

test('每数值标注 bundle 行号来源', t => {
  const data = loadEnemies();
  const { types } = data;
  // 共享血量数组来源标注。
  assert.ok(data.healthByWaveSource.includes('bundle:12038'));
  assert.ok(data.healthByWaveTableSource.includes('bundle:12037-12049'));
  // 各敌人速度来源标注。
  for (const key of ['Mob0', 'Mob1', 'Mob2', 'Mob3', 'Zombie']) {
    assert.ok(types[key].speedSource.includes('bundle:'), `${key} speedSource 标注 bundle`);
  }
  assert.ok(types.Cavalry.speedSource.includes('bundle:32398'), 'Cavalry speed 来源 bundle:32398');
  assert.ok(types.Puppet.speedSource.includes('bundle:31793'), 'Puppet speed 来源 bundle:31793');
  // Zombie/Puppet 修饰来源标注。
  assert.ok(types.Zombie.modifierSource.includes('bundle:31386'));
  assert.ok(types.Puppet.modifierSource.includes('bundle:12149'));
});

test('deviationNote 标注 BattleDataCore 既有偏差（Non-Goal，不修正）', t => {
  // spec Non-Goal：BattleDataCore.normalEnemyHealthByWave 第2-4位偏差不修正，enemies.json 标注差异。
  const data = loadEnemies();
  assert.ok(data.deviationNote, '标注 deviationNote');
  assert.ok(data.deviationNote.includes('11,57,44'), '标注 BattleDataCore 偏差值');
  assert.ok(data.deviationNote.includes('16,26,41'), '标注 bundle 正确值');
});
