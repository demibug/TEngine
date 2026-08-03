'use strict';

/**
 * Recovered from tG at bundle.strings-decoded.js:11069-11134.
 *
 * This file intentionally contains data only.  Rendering, Spine and resource
 * names belong to the original client layer and are not guessed here.
 */
const GENERAL_PART_WORDS = Object.freeze([
  '赵', '云', '张', '飞', '马', '超', '关', '羽', '平', '兴',
  '黄', '忠', '苞', '翼', '盖', '祖', '甄', '宓', '刘', '备',
]);

const GENERAL_MERGE_RECIPES = Object.freeze([
  ['赵', '云'], ['张', '飞'], ['马', '超'], ['关', '羽'],
  ['黄', '忠'], ['关', '平'], ['关', '兴'], ['张', '苞'],
  ['张', '翼'], ['黄', '盖'], ['刘', '备'], ['黄', '祖'],
].map(recipe => Object.freeze(recipe.slice())));

const GENERAL_FAMILY_NAMES = Object.freeze(['赵', '张', '马', '关', '黄', '刘']);
const GENERAL_FAMILY_SUFFIXES = Object.freeze({
  赵: Object.freeze(['云']),
  张: Object.freeze(['飞', '苞', '翼']),
  马: Object.freeze(['超']),
  关: Object.freeze(['羽', '平', '兴']),
  黄: Object.freeze(['忠', '盖', '祖']),
  刘: Object.freeze(['备']),
});

// tG.Op.  The source stores these as Map values; the meaning of each value is
// kept as source data until the consuming progression system is restored.
const GENERAL_PROGRESSION_REQUIREMENTS = Object.freeze({
  刀: Object.freeze([1, 2, 3, 4, 5]),
  弓: Object.freeze([1, 2, 3, 4, 5]),
  枪: Object.freeze([1, 2, 3, 4, 5]),
  骑: Object.freeze([1, 2, 3, 4, 5]),
  赵: Object.freeze([7]), 云: Object.freeze([7]),
  关: Object.freeze([8]), 羽: Object.freeze([7]),
  平: Object.freeze([4]), 兴: Object.freeze([4]),
  张: Object.freeze([8]), 飞: Object.freeze([7]),
  苞: Object.freeze([4]), 翼: Object.freeze([4]),
  黄: Object.freeze([6]), 忠: Object.freeze([6]),
  盖: Object.freeze([5]), 祖: Object.freeze([4]),
  马: Object.freeze([6]), 超: Object.freeze([6]),
  刘: Object.freeze([7]), 备: Object.freeze([6]),
  农: Object.freeze([1, 2, 3, 4, 5]),
});

// tG.Cp/Tp and their cumulative forms Rp/Fp.
const GENERAL_LEVEL_ATTACK_SPEED_INCREMENTS = Object.freeze([0, 0.3, 0.2, 0.15, 0.1]);
const GENERAL_LEVEL_DAMAGE_INCREMENTS = Object.freeze([0, 0.5, 0.4, 0.3, 0.2]);
const GENERAL_ATTACK_SPEED_MULTIPLIERS = Object.freeze([1, 1.3, 1.56, 1.794, 1.9734]);
const GENERAL_DAMAGE_MULTIPLIERS = Object.freeze([1, 1.5, 2.1, 2.73, 3.276]);

/** Recovered from tG.Yc/Xc. weaponType is the source Xc.type value. */
const GENERAL_DEFINITIONS = Object.freeze([
  ['赵云', 1], ['张飞', 1], ['马超', 1], ['关羽', 2], ['黄忠', 0], ['关平', 2],
  ['关兴', 2], ['张苞', 1], ['张翼', 3], ['黄盖', 3], ['刘备', 3], ['黄祖', 0],
].map(([name, weaponType], index) => Object.freeze({
  index,
  name,
  family: name[0],
  partWords: Object.freeze([name[0], name.slice(1)]),
  weaponType,
  status: 'PARTIAL_CORE_CONFIG',
})));

const byName = new Map(GENERAL_DEFINITIONS.map(value => [value.name, value]));

// tG.Yp(bundle.strings-decoded.js:11302-11314)。武将基础攻击力,值引用 hu 解码表:
// hu[14]=14、hu[12]=15、hu[9]=12、hu[3]=13(已 node 独立解码复核)。
// 武将创建处 bundle:47314 用 Yp.get(name) ?? 10 取值。
const GENERAL_BASE_ATTACK_POWER = new Map([
  ['赵云', 14], ['关羽', 15], ['张飞', 15], ['马超', 13],
  ['黄忠', 13], ['刘备', 13], ['关平', 12], ['关兴', 12],
  ['张苞', 12], ['张翼', 12], ['黄盖', 12], ['黄祖', 12],
]);

// tG.Mp(bundle.strings-decoded.js:11168-11272)。按武将索引(与 GENERAL_DEFINITIONS.index 对齐)
// 存放基础战斗参数与技能字段。武将战斗更新处(bundle:44689)读 Mp[type].kp/_p。
// range=_p(攻击范围)、interval=kp(攻击间隔)、targetPolicy=xp(目标策略);
// skillRange=Pp、skillActive=Ap、skillMode=Sp 为技能字段,作为数据携带,本阶段不接线(留待技能提案)。
// 注:bundle Mp 共 13 条,第 13 条(wp4.5/_p2/kp.8/Pp5.5/Ap!0/Sp"单体")无对应武将,疑模板/未启用,此处不收录。
const GENERAL_COMBAT_PARAMS = Object.freeze([
  Object.freeze({ range: 2, interval: 0.8, targetPolicy: 'closest_end', skillRange: 3.5, skillActive: true, skillMode: '快攻贯穿' }),  // 0 赵云
  Object.freeze({ range: 10, interval: 1, targetPolicy: 'nearest', skillRange: 3.5, skillActive: true, skillMode: '范围' }),          // 1 张飞
  Object.freeze({ range: 10, interval: 1, targetPolicy: 'nearest', skillRange: 3.5, skillActive: true, skillMode: '单体' }),          // 2 马超
  Object.freeze({ range: 20, interval: 1, targetPolicy: 'nearest', skillRange: 3.5, skillActive: true, skillMode: '单体' }),          // 3 关羽
  Object.freeze({ range: 6, interval: 0.8, targetPolicy: 'nearest', skillRange: 5.5, skillActive: true, skillMode: '贯穿' }),          // 4 黄忠
  Object.freeze({ range: 3, interval: 1, targetPolicy: 'nearest', skillRange: 3.5, skillActive: false, skillMode: '范围' }),          // 5 关平
  Object.freeze({ range: 7, interval: 1, targetPolicy: 'closest_end', skillRange: 3.5, skillActive: false, skillMode: '单体' }),      // 6 关兴
  Object.freeze({ range: 7, interval: 1, targetPolicy: 'closest_end', skillRange: 3.5, skillActive: false, skillMode: '单体' }),      // 7 张苞
  Object.freeze({ range: 7, interval: 1, targetPolicy: 'closest_end', skillRange: 3.5, skillActive: false, skillMode: '单体' }),      // 8 张翼
  Object.freeze({ range: 8, interval: 1, targetPolicy: 'nearest', skillRange: 3.5, skillActive: false, skillMode: '单体' }),          // 9 黄盖
  Object.freeze({ range: 10, interval: 0.8, targetPolicy: 'nearest', skillRange: 3.5, skillActive: true, skillMode: '单体' }),        // 10 刘备
  Object.freeze({ range: 6, interval: 0.8, targetPolicy: 'closest_end', skillRange: 3.5, skillActive: false, skillMode: '单体' }),    // 11 黄祖
].map((value, index) => Object.freeze({ index, ...value })));

/** 取武将基础攻击力(Yp Map);未知武将回退 10,与 bundle:47314 的 ?? 10 一致。 */
function getGeneralBaseAttackPower(name) {
  const value = GENERAL_BASE_ATTACK_POWER.get(name);
  return Number.isFinite(value) ? value : 10;
}

/** 取武将基础战斗参数(Mp[index]);未知索引返回范围 0 的安全默认。 */
function getGeneralCombatParams(index) {
  const params = GENERAL_COMBAT_PARAMS[index];
  if (!params) return Object.freeze({ range: 0, interval: 1, targetPolicy: 'nearest' });
  return params;
}

function getGeneralDefinition(name) {
  const value = byName.get(name);
  if (!value) throw new Error(`Unknown general: ${name}`);
  return value;
}

function getGeneralDefinitionByIndex(index) {
  const value = GENERAL_DEFINITIONS[index];
  if (!value) throw new Error(`Unknown general index: ${index}`);
  return value;
}

function getGeneralPartWords(name) {
  return getGeneralDefinition(name).partWords.slice();
}

function findGeneralByParts(parts) {
  const words = parts.map(part => typeof part === 'string' ? part : part.word);
  const name = words.join('');
  const definition = byName.get(name);
  if (!definition) return null;
  const recipe = GENERAL_MERGE_RECIPES.find(item => item[0] === words[0] && item[1] === words[1]);
  return recipe ? definition : null;
}

function getCompatiblePartWords(word) {
  const matches = [];
  for (const definition of GENERAL_DEFINITIONS) {
    if (definition.partWords.indexOf(word) >= 0) matches.push(definition.name);
  }
  return matches;
}

module.exports = {
  GENERAL_PART_WORDS,
  GENERAL_MERGE_RECIPES,
  GENERAL_FAMILY_NAMES,
  GENERAL_FAMILY_SUFFIXES,
  GENERAL_PROGRESSION_REQUIREMENTS,
  GENERAL_LEVEL_ATTACK_SPEED_INCREMENTS,
  GENERAL_LEVEL_DAMAGE_INCREMENTS,
  GENERAL_ATTACK_SPEED_MULTIPLIERS,
  GENERAL_DAMAGE_MULTIPLIERS,
  GENERAL_DEFINITIONS,
  GENERAL_BASE_ATTACK_POWER,
  GENERAL_COMBAT_PARAMS,
  getGeneralDefinition,
  getGeneralDefinitionByIndex,
  getGeneralPartWords,
  findGeneralByParts,
  getCompatiblePartWords,
  getGeneralBaseAttackPower,
  getGeneralCombatParams,
};
