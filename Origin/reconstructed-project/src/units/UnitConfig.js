'use strict';

/**
 * 重建来源：bundle.strings-decoded.js:11069-11352
 * 原始配置对象：uq.Oc / tG
 * 重建状态：COMPLETE_FOR_BASE_SOLDIERS
 */
const BASE_SOLDIER_TEXTS = Object.freeze(['刀', '弓', '枪', '骑']);

const BASE_SOLDIER_CONFIGS = Object.freeze([
  Object.freeze({
    index: 0,
    text: '刀',
    animationKey: 'knife',
    rangeCells: 1.5,
    attackDamage: 3,
    attackIntervalSeconds: 0.8,
    damageMode: '单体',
    targetPolicy: 'nearest',
  }),
  Object.freeze({
    index: 1,
    text: '弓',
    animationKey: 'bow',
    rangeCells: 3.5,
    attackDamage: 2,
    attackIntervalSeconds: 0.8,
    damageMode: '单体',
    targetPolicy: 'closest_end',
  }),
  Object.freeze({
    index: 2,
    text: '枪',
    animationKey: 'pike',
    rangeCells: 2.5,
    attackDamage: 2,
    attackIntervalSeconds: 0.8,
    damageMode: '近战枪击',
    targetPolicy: 'nearest',
  }),
  Object.freeze({
    index: 3,
    text: '骑',
    animationKey: 'cavalry',
    rangeCells: 2,
    attackDamage: 2,
    attackIntervalSeconds: 0.8,
    damageMode: '范围',
    targetPolicy: 'nearest',
  }),
]);

const ATTACK_SPEED_LEVEL_INCREMENTS = Object.freeze([0, 0.5, 0.4, 0.3, 0.25]);
const DAMAGE_LEVEL_INCREMENTS = Object.freeze([0, 0.5, 0.4, 0.3, 0.25]);
const MAX_SOLDIER_LEVEL = 3;

// 原 Dp 阈值表（bundle:11278 的 this["Dp"]=[0,8,23]），3 元素，对齐 maxLevel=3。
// 与武将 Ip 表 [0,10,...] 严格区分；4 级及以上在小兵不可达。
const EXPERIENCE_THRESHOLDS = Object.freeze([0, 8, 23]);

function cumulativeMultipliers(increments) {
  const values = [];
  for (let index = 0; index < increments.length; index += 1) {
    values.push(index === 0 ? 1 : values[index - 1] * (1 + increments[index]));
  }
  return Object.freeze(values);
}

const ATTACK_SPEED_LEVEL_MULTIPLIERS = cumulativeMultipliers(ATTACK_SPEED_LEVEL_INCREMENTS);
const DAMAGE_LEVEL_MULTIPLIERS = cumulativeMultipliers(DAMAGE_LEVEL_INCREMENTS);

class FriendlyUnitConfig {
  constructor() {
    this.texts = BASE_SOLDIER_TEXTS;
    this.configs = BASE_SOLDIER_CONFIGS;
    this.attackSpeedLevelMultipliers = ATTACK_SPEED_LEVEL_MULTIPLIERS;
    this.damageLevelMultipliers = DAMAGE_LEVEL_MULTIPLIERS;
    this.maxLevel = MAX_SOLDIER_LEVEL;
    this.experienceThresholds = EXPERIENCE_THRESHOLDS;
  }

  indexOf(text) {
    return this.texts.indexOf(text);
  }

  getByIndex(index) {
    const config = this.configs[index];
    if (!config) throw new RangeError(`Unknown base soldier config index: ${index}`);
    return config;
  }

  getByText(text) {
    const index = this.indexOf(text);
    if (index < 0) throw new Error(`Unknown base soldier text: ${text}`);
    return this.getByIndex(index);
  }

  resolveLevelStats(text, level, gridWidth) {
    const config = this.getByText(text);
    const normalizedLevel = Math.min(this.maxLevel, Math.max(1, Number(level) || 1));
    return {
      config,
      level: normalizedLevel,
      attackRange: config.rangeCells * gridWidth,
      attackDamage: config.attackDamage * this.damageLevelMultipliers[normalizedLevel - 1],
      attackIntervalSeconds: config.attackIntervalSeconds / this.attackSpeedLevelMultipliers[normalizedLevel - 1],
      animationPlaybackRate: this.attackSpeedLevelMultipliers[normalizedLevel - 1],
    };
  }
}

module.exports = {
  BASE_SOLDIER_TEXTS,
  BASE_SOLDIER_CONFIGS,
  ATTACK_SPEED_LEVEL_INCREMENTS,
  DAMAGE_LEVEL_INCREMENTS,
  ATTACK_SPEED_LEVEL_MULTIPLIERS,
  DAMAGE_LEVEL_MULTIPLIERS,
  MAX_SOLDIER_LEVEL,
  EXPERIENCE_THRESHOLDS,
  FriendlyUnitConfig,
};
