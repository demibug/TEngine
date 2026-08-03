'use strict';

const { GeneralPart } = require('./GeneralPart');
const { GeneralUnit } = require('./GeneralUnit');
const { findGeneralByParts, getGeneralBaseAttackPower, getGeneralCombatParams } = require('./GeneralDefinitions');
const Weapon = require('../weapons/types/Weapon');

/** 武将默认武器 index,与既有 GeneralUnifiedAttack.test.js 的 weapon.init('10', 1) 一致;每将专属武器 index 留待武器配置提案。 */
const DEFAULT_GENERAL_WEAPON_INDEX = '10';

/** 创建武将默认通用武器,使 attack() 走 weapon→WeaponAttackEffect 路径实际造伤害。 */
function createDefaultGeneralWeapon(definition) {
  const weapon = new Weapon();
  weapon.init(DEFAULT_GENERAL_WEAPON_INDEX, definition.weaponType);
  return weapon;
}

/** Pure recovery of the original UnitRegistry's general-part merge path. */
class GeneralFactory {
  constructor({ nextId = 1 } = {}) {
    this.nextId = nextId;
  }

  createPart({ id = this.nextId++, word, side = true, level = 1 } = {}) {
    return new GeneralPart({ id, word, side, level });
  }

  createGeneral(parts, { id = this.nextId++, side = true, isPlayer = true, weaponId = null, weapon = null, combat = null, experienceThresholds = null, experience = 0, skillManager = null, skillKey = null, skill = null } = {}) {
    if (!Array.isArray(parts) || parts.length !== 2) throw new Error('General merge requires exactly two parts');
    const definition = findGeneralByParts(parts);
    if (!definition) throw new Error(`Unsupported general part merge: ${parts.map(part => part.word).join('')}`);
    const general = new GeneralUnit({ id, name: definition.name, side, level: 1, experienceThresholds, experience });
    general.init(parts, isPlayer, definition.index);
    general.weaponId = weaponId;

    // 武将基础战斗数值(Yp 攻击力 + Mp 范围/间隔/目标策略),合成即注入,不依赖外部手动配置。
    // 来源:Yp Map(bundle:11302-11314)、Mp[generalIndex](bundle:11168-11272,武将战斗更新处 bundle:44689 读取)。
    const combatParams = getGeneralCombatParams(definition.index);
    general.baseAttackPower = getGeneralBaseAttackPower(definition.name);
    general.baseAttackRange = combatParams.range;
    general.baseAttackIntervalSeconds = combatParams.interval;
    general.targetPolicy = combatParams.targetPolicy;

    // 装备通用武器(调用方未显式提供时),使 attack() 走 weapon→WeaponAttackEffect→enemy.hit 造伤害。
    if (weapon) general.attachWeapon(weapon);
    else general.attachWeapon(createDefaultGeneralWeapon(definition));

    // 合成时自动配置战斗:enemyManager 经 mergeGeneralParts 由 UnitRegistry 注入;
    // 调用方 combat 的显式字段覆盖 Yp/Mp 默认值,targetPolicy 缺省时回退 Mp。
    if (combat) {
      const resolvedCombat = { ...combat };
      if (resolvedCombat.targetPolicy == null) resolvedCombat.targetPolicy = combatParams.targetPolicy;
      general.configureCombat(resolvedCombat);
    }
    if (skillManager || skill) general.configureSkill({ skillManager, skillKey, skill });
    for (const part of parts) part.assignTo(general.id);
    return general;
  }
}

module.exports = { GeneralFactory, createDefaultGeneralWeapon, DEFAULT_GENERAL_WEAPON_INDEX };
