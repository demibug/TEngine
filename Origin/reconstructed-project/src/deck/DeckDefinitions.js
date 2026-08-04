'use strict';
const fs = require('fs');
const path = require('path');

// 牌池配置 JSON 路径（相对本模块，任意 cwd 均可加载）
// 数值来源：bundle:11969（nu 类 constructor，eh/ah/nh 三组同数组，各 108 元素）
const DECK_POOL_CONFIG_PATH = path.join(__dirname, '../../unity-export/config/deck-pool.json');

// 4 元素回退牌池（JSON 加载失败时使用，忠实 v0.8.1 原始 BASE_POOL）
const BASE_POOL = Object.freeze(['刀', '弓', '枪', '骑']);

/**
 * 从 deck-pool.json 加载 108 元素牌池并按 count 展开为扁平数组。
 *
 * 对应 bundle:11969（nu 类 constructor，this.eh/this.ah/this.nh 三组同数组，
 * 各 108 元素）：刀×21/弓×19/枪×18/骑×17/铲×11 + 武将字 22 项
 * （刘1+赵2+云1+关1+羽1+平1+兴1+马2+超1+张2+飞1+苞1+翼1+黄2+忠1+盖1+祖1+备1）。
 * 三组数组逐元素完全相同，故取一组展开即可。
 *
 * 展开后的扁平数组供 DeckManager.poolForSide/drawText 按权重均匀抽取，
 * 等价于 bundle 中按数组下标随机取（pool.length 即权重总和 108）。
 *
 * 多次调用返回同一缓存数组。JSON 加载或解析失败时回退到 BASE_POOL（4 元素）。
 *
 * @returns {string[]} 108 元素牌池扁平数组（或回退 4 元素）
 */
function loadDeckPool() {
  if (loadDeckPool._cached) return loadDeckPool._cached;
  try {
    const raw = JSON.parse(fs.readFileSync(DECK_POOL_CONFIG_PATH, 'utf8'));
    const elements = [
      ...(raw.categories && raw.categories.baseUnits ? raw.categories.baseUnits.elements : []),
      ...(raw.categories && raw.categories.generalChars ? raw.categories.generalChars.elements : []),
    ];
    const expanded = [];
    for (const el of elements) {
      const count = Math.max(0, el.count | 0);
      for (let i = 0; i < count; i += 1) expanded.push(el.text);
    }
    // 展开后长度必须为 108（bundle:11969），否则视为异常回退 4 元素
    loadDeckPool._cached = expanded.length === 108 ? Object.freeze(expanded) : BASE_POOL;
  } catch (_err) {
    // JSON 文件缺失或解析失败：回退到 4 元素 BASE_POOL，不抛异常（启动期可用）
    loadDeckPool._cached = BASE_POOL;
  }
  return loadDeckPool._cached;
}

// 108 元素牌池（从 deck-pool.json 加载展开，bundle:11969）
const DECK_POOL = loadDeckPool();

const DeckDefinitions = Object.freeze({
  handSize: 5, // s4.fe
  basePool: DECK_POOL, // 108 元素牌池（bundle:11969），JSON 加载失败回退 BASE_POOL 4 元素
  defaultLevel: 1,
  baseUnitCost: 1,
  maxLevel: 3,
});
module.exports = { BASE_POOL, DECK_POOL, DeckDefinitions };
