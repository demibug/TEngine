'use strict';

/**
 * 重建模块：FRIENDLY-UNIT-COMBAT-01 / 友军配置
 * 原始范围：bundle.strings-decoded.js:11060-11361
 * 原始主要符号：Oc.mp / Oc.pp / Oc.yp / Oc.fp / Oc.gp
 * 重建状态：COMPLETE_FOR_BASE_SOLDIER_STATS
 */
const BASE_SOLDIER_KEYS = Object.freeze(['刀', '弓', '枪', '骑']);

const BASE_SOLDIER_STATS = Object.freeze([
  Object.freeze({ key: '刀', typeIndex: 0, rangeCells: 1.5, attackDamage: 3, attackIntervalScale: 0.8, attackShape: '单体', targetPolicy: 'nearest' }),
  Object.freeze({ key: '弓', typeIndex: 1, rangeCells: 3.5, attackDamage: 2, attackIntervalScale: 0.8, attackShape: '单体', targetPolicy: 'closest_end' }),
  Object.freeze({ key: '枪', typeIndex: 2, rangeCells: 2.5, attackDamage: 2, attackIntervalScale: 0.8, attackShape: '贯穿', targetPolicy: 'nearest' }),
  Object.freeze({ key: '骑', typeIndex: 3, rangeCells: 2, attackDamage: 2, attackIntervalScale: 0.8, attackShape: '范围', targetPolicy: 'nearest' }),
]);

const LEVEL_RATE_STEPS = Object.freeze([0, 0.5, 0.4, 0.3, 0.25]);

function buildCumulativeMultipliers(steps = LEVEL_RATE_STEPS) {
  const result = [];
  for (let index = 0; index < steps.length; index += 1) {
    result.push(index === 0 ? 1 : result[index - 1] * (1 + steps[index]));
  }
  return Object.freeze(result);
}

const ATTACK_INTERVAL_DIVISORS = buildCumulativeMultipliers();
const ATTACK_DAMAGE_MULTIPLIERS = buildCumulativeMultipliers();

class FriendlyUnitData {
  constructor() {
    this.keys = BASE_SOLDIER_KEYS;
    this.baseStats = BASE_SOLDIER_STATS;
    this.attackIntervalDivisors = ATTACK_INTERVAL_DIVISORS;
    this.attackDamageMultipliers = ATTACK_DAMAGE_MULTIPLIERS;
  }

  resolveByKey(key) {
    const typeIndex = this.keys.indexOf(key);
    if (typeIndex < 0) throw new Error(`FriendlyUnitData: unknown base soldier key ${key}`);
    return this.resolveByTypeIndex(typeIndex);
  }

  resolveByTypeIndex(typeIndex) {
    const config = this.baseStats[typeIndex];
    if (!config) throw new Error(`FriendlyUnitData: missing base soldier config for type ${typeIndex}`);
    return config;
  }

  resolveLevelStats(key, level = 1) {
    const config = this.resolveByKey(key);
    const index = Math.max(0, Math.min(4, Number(level) - 1));
    return {
      ...config,
      level: index + 1,
      attackDamage: config.attackDamage * this.attackDamageMultipliers[index],
      attackIntervalScale: config.attackIntervalScale / this.attackIntervalDivisors[index],
    };
  }
}

module.exports = {
  BASE_SOLDIER_KEYS,
  BASE_SOLDIER_STATS,
  LEVEL_RATE_STEPS,
  ATTACK_INTERVAL_DIVISORS,
  ATTACK_DAMAGE_MULTIPLIERS,
  FriendlyUnitData,
};
