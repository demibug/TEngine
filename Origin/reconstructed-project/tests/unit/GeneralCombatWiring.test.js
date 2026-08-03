'use strict';

// 武将合成与参战接线测试(general-combat-wiring)。
// 覆盖:合成自动注入数值(5.1)、参战造伤害与射程判定(5.2)、经验阈值(5.3)、命令分流(5.4)。

const test = require('node:test');
const assert = require('node:assert/strict');
const { GeneralPart } = require('../../src/generals/GeneralPart');
const { GeneralFactory } = require('../../src/generals/GeneralFactory');
const { GeneralUnit } = require('../../src/generals/GeneralUnit');
const { UnitRegistry } = require('../../src/units/UnitRegistry');
const { UnitMergeService } = require('../../src/units/UnitMergeService');
const { BattleInputCommandType, BattleInputCommand } = require('../../src/input/BattleInputCommand');
const { BattleInputController } = require('../../src/input/BattleInputController');
const { AttackEffectManager } = require('../../src/combat/AttackEffectManager');
const { ObjectPool } = require('../../src/core/ObjectPool');

/** 构造一个极简 UnitRegistry,仅设置武将合成所需字段(复用 GeneralLifecycle.test.js 模式)。 */
function createRegistry({ nextId = 100, enemyManager = null, attackEffectManager = null, gameData = null } = {}) {
  const registry = new UnitRegistry();
  registry.generalFactory = new GeneralFactory({ nextId });
  if (enemyManager) registry.enemyManager = enemyManager;
  if (attackEffectManager) registry.attackEffectManager = attackEffectManager;
  if (gameData) registry.gameData = gameData;
  return registry;
}

function addParts(registry, words, { side = true, startId = 1 } = {}) {
  const parts = words.map((word, index) => {
    const part = new GeneralPart({ id: startId + index, word, side });
    registry.secondaryUnits.set(part.id, part);
    return part;
  });
  return parts;
}

test('5.1 合成"赵"+"云"产生赵云,自动注入基础攻击力并配置战斗', () => {
  const registry = createRegistry({
    enemyManager: { queryTargets() { return []; } },
    attackEffectManager: new AttackEffectManager({ objectPool: new ObjectPool() }),
  });
  addParts(registry, ['赵', '云']);

  const general = registry.mergeGeneralParts([1, 2]);

  assert.equal(general.name, '赵云');
  assert.equal(general.baseAttackPower, 14);
  assert.equal(general.combatConfigured, true);
  assert.ok(general.weapon, '合成时应自动装备武器');
  assert.equal(typeof general.weapon.attack, 'function');
  assert.equal(general.attackRange, 2);
  assert.equal(general.attackIntervalSeconds, 0.8);
  assert.equal(general.targetPolicy, 'closest_end');
});

test('5.2 合成赵云后,射程内敌人被攻击造伤害,射程外敌人不命中', () => {
  const manager = new AttackEffectManager({ objectPool: new ObjectPool() });
  const hits = [];
  const enemy = {
    id: 1,
    x: 1,
    y: 0,
    targetable: true,
    isTargetableBy() { return true; },
    hit(damage) { hits.push(damage); return true; },
  };
  // 范围感知的 enemyManager:按距离过滤,模拟 queryTargets 的射程判定
  const enemyManager = {
    queryTargets(x, y, range, side) {
      return [enemy].filter(e => (e.x - x) ** 2 + (e.y - y) ** 2 <= range * range);
    },
  };
  const registry = createRegistry({ enemyManager, attackEffectManager: manager });
  addParts(registry, ['赵', '云']);
  const general = registry.mergeGeneralParts([1, 2]);

  // 赵云 range=2,敌人位于 (1,0),距中心 (0,0) 为 1,在射程内
  general.updateCombat(1000); // IDLE → ATTACK 状态转移
  const attack = general.updateCombat(2000); // 攻击
  assert.equal(attack.attacked, true);
  assert.equal(manager.activeCount, 1);
  manager.update(0); // 触发 WeaponAttackEffect.apply → enemy.hit
  assert.equal(hits.length, 1);
  assert.equal(hits[0], 14); // baseAttackPower 14 × level1 damageMultiplier 1
  assert.equal(manager.activeCount, 0);
});

test('5.2b 射程外敌人不命中', () => {
  const manager = new AttackEffectManager({ objectPool: new ObjectPool() });
  const farEnemy = { id: 2, x: 10, y: 0, targetable: true, isTargetableBy() { return true; }, hit() { } };
  const enemyManager = {
    queryTargets(x, y, range, side) {
      return [farEnemy].filter(e => (e.x - x) ** 2 + (e.y - y) ** 2 <= range * range);
    },
  };
  const registry = createRegistry({ enemyManager, attackEffectManager: manager });
  addParts(registry, ['赵', '云']);
  const general = registry.mergeGeneralParts([1, 2]);

  const result = general.updateCombat(1000);
  assert.equal(result.attacked, false);
  assert.equal(result.reason, 'no-target');
});

test('5.3 武将经验阈值为 [0,10,35,75,130],各级跨阈值升级且满级不超', () => {
  const general = new GeneralUnit({ id: 1, name: '赵云' });
  assert.deepEqual([...general.experienceThresholds], [0, 10, 35, 75, 130]);
  assert.ok(general.experienceThresholds.every(t => Number.isFinite(t)), '阈值不得有 null 占位');

  general.setExperience(10);
  assert.equal(general.level, 2);
  general.setExperience(35);
  assert.equal(general.level, 3);
  general.setExperience(75);
  assert.equal(general.level, 4);
  general.setExperience(130);
  assert.equal(general.level, 5);
  general.setExperience(999);
  assert.equal(general.level, 5, '满级后不再升级');
});

function createDispatchRegistry() {
  const registry = new UnitRegistry();
  registry.generalFactory = new GeneralFactory({ nextId: 100 });
  registry.gameData = {
    friendlyUnits: ['刀', '弓', '枪', '骑'],
    map: { gridWidth: 80, gridHeight: 80 },
    battle: { isGameOver: false },
  };
  return registry;
}

function mockSoldier(id, text, level = 1) {
  return { id, unitText: text, level, side: true, mergeDisabled: false, gameOver() { } };
}

test('5.4a 两同字同级士兵合并触发升级,不产生武将', () => {
  const registry = createDispatchRegistry();
  registry.soldiers.set(10, mockSoldier(10, '刀'));
  registry.soldiers.set(11, mockSoldier(11, '刀'));
  const levelService = { upgrade(unit, delta) { unit.level += delta; return { success: true, level: unit.level }; } };
  const mergeService = new UnitMergeService({ unitRegistry: registry, levelService });
  const controller = new BattleInputController({ deckManager: {}, economy: {}, unitRegistry: registry, mergeService });

  const result = controller.execute(new BattleInputCommand(BattleInputCommandType.MERGE_UNITS, { sourceId: 10, targetId: 11 }));

  assert.equal(result.success, true);
  assert.equal(registry.soldiers.get(11).level, 2); // 目标士兵升级
  assert.equal(registry.soldiers.has(10), false); // 源士兵移除
  assert.equal(registry.generals.size, 0, '士兵合并不产生武将');
});

test('5.4b 两武将部件合并产生武将,不触发士兵升级', () => {
  const registry = createDispatchRegistry();
  const soldier = mockSoldier(20, '刀');
  registry.soldiers.set(20, soldier);
  addParts(registry, ['关', '羽']);
  const levelService = { upgrade() { return { success: true }; } };
  const mergeService = new UnitMergeService({ unitRegistry: registry, levelService });
  const controller = new BattleInputController({ deckManager: {}, economy: {}, unitRegistry: registry, mergeService });

  const result = controller.execute(new BattleInputCommand(BattleInputCommandType.MERGE_UNITS, { sourceId: 1, targetId: 2 }));

  assert.equal(result.success, true);
  assert.equal(result.merged, true);
  assert.equal(registry.generals.size, 1);
  const general = [...registry.generals.values()][0];
  assert.equal(general.name, '关羽');
  assert.equal(soldier.level, 1, '武将部件合并不触发士兵升级');
  assert.ok(registry.soldiers.has(20), '士兵未被武将合成触及');
});

test('5.4c 不构成配方的两字合并不产生武将', () => {
  const registry = createDispatchRegistry();
  addParts(registry, ['张', '云']); // 张+云 非任何武将配方
  const mergeService = new UnitMergeService({ unitRegistry: registry, levelService: { upgrade() { return { success: true }; } } });
  const controller = new BattleInputController({ deckManager: {}, economy: {}, unitRegistry: registry, mergeService });

  const result = controller.execute(new BattleInputCommand(BattleInputCommandType.MERGE_UNITS, { sourceId: 1, targetId: 2 }));

  assert.equal(result.success, false, '非配方组合应失败');
  assert.equal(registry.generals.size, 0, '不产生武将');
});
