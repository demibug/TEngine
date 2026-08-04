'use strict';
const fs = require('fs');
const path = require('path');

// 难度配置 JSON 路径（相对本模块，任意 cwd 均可加载）
const CONFIG_PATH = path.join(__dirname, '../../unity-export/config/ai-difficulty.json');

/**
 * AI 难度配置加载器（对应 bundle 的 My 配置类，bundle:3150-3161）。
 * 从 unity-export/config/ai-difficulty.json 读取 4 级难度参数，
 * resolve(Si) 按难度档返回状态机/刷牌/收入/道具所需的配置对象。
 *
 * 字段映射（命名沿用 bundle 符号）：
 *   fG            决策间隔 ms（bundle:49740 fG=[hu[118],hu[122],hu[123],hu[176]]，实测 2000/1500/1000/500）
 *   ni            step1 快速结束概率（bundle:3160 c[1899]）
 *   ri            XG 触发概率（bundle:3160 c[1900]）
 *   hi            初始加钱（bundle:3160 c[1898]，=10）
 *   ii            周期收入，按 Si 取 6 波次数组（bundle:3155-3159 c[1494]）
 *   ei            波次表，6 个波次索引（bundle:3160 c[1495]）
 *   oi            （bundle:3160 c[1901]）
 *   itemCooldownMs 道具冷却 ms（bundle:50074 hu[101]=5000）
 */
class AIDifficultyConfig {
  /**
   * @param {object} [raw] 可选的预解析配置对象；未提供时从 CONFIG_PATH 加载。
   */
  constructor(raw) {
    this.raw = raw || AIDifficultyConfig.load();
  }

  /**
   * 加载并缓存难度配置 JSON。多次调用返回同一对象。
   * @returns {object} 配置对象
   */
  static load() {
    if (!AIDifficultyConfig._cached) {
      AIDifficultyConfig._cached = JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8'));
    }
    return AIDifficultyConfig._cached;
  }

  /**
   * 按难度档解析配置（对应 bundle au.Si 钳制 0-3）。
   * @param {number} Si 难度档 0-3，越界钳制到 [0,3]
   * @returns {{fG:number,ni:number,ri:number,ii:number[],ei:number[],hi:number,oi:number,itemCooldownMs:number}}
   */
  resolve(Si) {
    const idx = Math.min(3, Math.max(0, Si | 0));
    const c = this.raw;
    return {
      fG: c.decisionIntervalMs[idx], // 决策间隔 ms
      ni: c.ni[idx],                 // step1 快速结束概率
      ri: c.ri[idx],                 // XG 触发概率
      ii: c.ii[idx],                 // 周期收入（6 波次值，按 Si 索引）
      ei: c.ei,                      // 波次表（6 波次索引，全档共用）
      hi: c.hi,                      // 初始加钱
      oi: c.oi[idx],                 // 按 Si 索引
      itemCooldownMs: c.itemCooldownMs, // 道具冷却 ms
    };
  }
}

module.exports = { AIDifficultyConfig };
