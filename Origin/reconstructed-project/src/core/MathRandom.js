'use strict';

/**
 * 重建来源：bundle.strings-decoded.js:2698-2740（np.range / np.As）。
 * 仅恢复 BOOT-TO-BATTLE 使用到的随机接口。
 */
class MathRandom {
  constructor(random = Math.random) {
    if (typeof random !== 'function') throw new TypeError('random must be a function');
    this.random = random;
  }

  range(min, max, integer = false) {
    if (max < min) {
      console.error(`[MathE].range(): 错误的输入! [${min},${max})`);
      return null;
    }
    const value = min + (max - min) * this.random();
    return integer ? Math.floor(value) : value;
  }

  /** 原始 np.As：不额外修正负权重或空数组。 */
  weightedIndex(weights) {
    let total = 0;
    for (let index = 0; index < weights.length; index += 1) total += weights[index];
    const target = this.random() * total;
    total = 0;
    for (let index = 0; index < weights.length; index += 1) {
      total += weights[index];
      if (target <= total) return index;
    }
    return undefined;
  }
}

module.exports = { MathRandom };
