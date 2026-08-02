'use strict';

const {
  getGeneralDefinition,
  getGeneralDefinitionByIndex,
  GENERAL_ATTACK_SPEED_MULTIPLIERS,
  GENERAL_DAMAGE_MULTIPLIERS,
} = require('./GeneralDefinitions');

/** Engine-independent holder for the recovered GeneralBase combat state. */
class GeneralUnit {
  constructor({ id = -1, name, side = true, level = 1, parts = [] } = {}) {
    this.id = id;
    this.definition = getGeneralDefinition(name);
    this.name = name;
    this.side = Boolean(side);
    this.level = Math.max(1, Math.min(5, Number(level) || 1));
    this.parts = parts.slice();
    this.partIds = this.parts.map(part => part.id);
    this.isPlayer = this.side;
    this.experience = 0;
    this.weaponId = null;
    this.weapon = null;
    this.skill = null;
    this.active = true;
    this.stats = this.getLevelStats();
  }

  init(parts, isPlayer = true, typeIndex = this.definition.index) {
    const definition = getGeneralDefinitionByIndex(typeIndex);
    this.definition = definition;
    this.name = definition.name;
    this.parts = parts.slice();
    this.partIds = this.parts.map(part => part.id);
    this.side = Boolean(isPlayer);
    this.isPlayer = Boolean(isPlayer);
    this.active = true;
    this.stats = this.getLevelStats();
    return this;
  }

  hE(experience) {
    this.experience = Math.max(0, Number(experience) || 0);
    return this;
  }

  setLevel(level) {
    this.level = Math.max(1, Math.min(5, Number(level) || 1));
    this.stats = this.getLevelStats();
    return this;
  }

  getLevelStats() {
    const index = this.level - 1;
    return Object.freeze({
      level: this.level,
      attackSpeedMultiplier: GENERAL_ATTACK_SPEED_MULTIPLIERS[index],
      damageMultiplier: GENERAL_DAMAGE_MULTIPLIERS[index],
    });
  }

  attachWeapon(weapon) {
    this.weapon = weapon;
    if (weapon && typeof weapon.attach === 'function') weapon.attach(this);
    return weapon;
  }

  attachSkill(skill) {
    this.skill = skill;
    if (skill && typeof skill.bindOwner === 'function') skill.bindOwner(this);
    return skill;
  }

  gameOver() {
    if (this.weapon && typeof this.weapon.gameOver === 'function') this.weapon.gameOver();
    if (this.skill && typeof this.skill.gameOver === 'function') this.skill.gameOver();
    this.active = false;
    this.weapon = null;
    this.skill = null;
    this.parts = [];
    this.partIds = [];
  }
}

module.exports = { GeneralUnit };
