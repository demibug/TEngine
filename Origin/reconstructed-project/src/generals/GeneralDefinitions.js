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
  getGeneralDefinition,
  getGeneralDefinitionByIndex,
  getGeneralPartWords,
  findGeneralByParts,
  getCompatiblePartWords,
};
