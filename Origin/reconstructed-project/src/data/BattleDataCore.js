'use strict';

/**
 * 重建模块：关键路径地图与敌人波次数据
 * 原始范围：
 * - bundle.strings-decoded.js:11994-12175（sD）
 * - bundle.strings-decoded.js:12194-12847（tl/ru/oS/s4）
 * 重建状态：COMPLETE_FOR_ENEMY_RUNTIME
 */

const { MAP_BLOCKS, MapData } = require('../battle/MapData');

class EnemyDataCore {
  constructor() {
    this.waveUnitCounts = Object.freeze([10, 11, 12, 13, 15, 16, 18, 19, 21, 24, 26, 29, 31, 35, 38, 42, 46, 51, 56, 61]);
    this.earlyRoundHealthMultipliers = Object.freeze([0.6,0.6,0.6,0.6,0.7,0.7,0.7,0.8,0.8,0.8]);
    this.normalEnemyHealthByWave = Object.freeze([10,11,57,44,39,92,138,200,291,421,611,886,1285,1863,2701,3917,5680,8235,11941,17315]);
    this.normalEnemyTypes = Object.freeze([
      Object.freeze({ healthByWave: this.normalEnemyHealthByWave, speed: 50 }),
      Object.freeze({ healthByWave: this.normalEnemyHealthByWave, speed: 50 }),
      Object.freeze({ healthByWave: this.normalEnemyHealthByWave, speed: 50 }),
      Object.freeze({ healthByWave: this.normalEnemyHealthByWave, speed: 50 }),
    ]);
    this.bossWaveNumbers = Object.freeze([3, 6, 9, 12, 15, 20]);
    this.bossSpawnChances = Object.freeze([0.1, 0.2, 0.3, 0.5, 0.9, 1]);
    this.spawnStrategyWeights = Object.freeze([5, 2, 3]);
    this.spawnStrategies = Object.freeze([
      Object.freeze([1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1]),
      Object.freeze([1.1,1.2,1.3,1.2,1.3,1.7,2,1,1.5,1,1,1,1,1,1,1,1,1,1,1]),
      Object.freeze([1,1,1.5,1,1.8,2,1,1,2,1,1,1.3,1,1,1.4,1,1,1.5,1,1]),
    ]);
    this.bossRotationIndex = 0;
  }


  resolveBossStats(typeIndex, options = {}) {
    const { getSkillDefinition } = require('../skills/SkillDefinitions');
    const { BOSS_DEFINITIONS } = require('../bosses/BossDefinitions');
    const definition = BOSS_DEFINITIONS[typeIndex];
    if (!definition) throw new Error(`EnemyDataCore: missing boss config for type ${typeIndex}`);
    const skill = getSkillDefinition(definition.skillKey);
    const normal = this.resolveNormalStats(options.mapEnemyTypeIndex == null ? 0 : options.mapEnemyTypeIndex, options);
    return {
      ph: normal.ph * Number(skill.healthMultiplier || 10),
      speed: Number(skill.speed || 10),
      Lh: Number(skill.rangeTiles || 0) * Number(options.gridWidth || 80),
      mh: Number(skill.cooldownSeconds || 10) * 1000,
    };
  }

  startGame() {}
  gameOver() { this.bossRotationIndex = 0; }

  /**
   * 原始方法：tw.Dy
   * 原始范围：bundle.strings-decoded.js:11599-11628
   * 说明：返回对象在原代码中为共享临时对象；这里返回新对象以避免模块间意外覆盖，
   * 数值计算和索引规则保持一致。
   */
  resolveNormalStats(typeIndex, {
    mapEnemyTypeIndex = 0,
    currentRound = 1,
    endlessMode = false,
    maxRounds = 20,
    spawnStrategy = [],
    playerRound = 0,
    rankHealthBonus = 0,
  } = {}) {
    if (typeIndex >= this.normalEnemyTypes.length) typeIndex = mapEnemyTypeIndex;
    const cfg = this.normalEnemyTypes[typeIndex];
    if (!cfg) throw new Error(`EnemyDataCore: missing normal enemy config for type ${typeIndex}`);
    const wave = Math.max(1, currentRound);
    let health;
    if (endlessMode && wave > maxRounds) {
      health = cfg.healthByWave[0] * Math.pow(1.5, wave - 1);
    } else {
      const healthIndex = Math.min(wave, cfg.healthByWave.length) - 1;
      const strategyIndex = Math.max(0, Math.min(wave - 1, Math.max(0, spawnStrategy.length - 1)));
      const strategyMultiplier = spawnStrategy[strategyIndex] == null ? 1 : spawnStrategy[strategyIndex];
      if (playerRound < 10 && wave <= 10) {
        const earlyMultiplier = this.earlyRoundHealthMultipliers[playerRound] == null ? 1 : this.earlyRoundHealthMultipliers[playerRound];
        health = cfg.healthByWave[healthIndex] * strategyMultiplier * earlyMultiplier;
      } else {
        health = cfg.healthByWave[healthIndex] * strategyMultiplier;
      }
      if (wave > 10) health += health * rankHealthBonus;
    }
    return { ph: health, speed: cfg.speed };
  }
}

const MapDataCore = MapData;
module.exports = { MAP_BLOCKS, MapDataCore, MapData, EnemyDataCore };
