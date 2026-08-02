'use strict';
const General = require('./types/GeneralSkills');
const Boss = require('./types/BossSkills');
const { SKILL_DEFINITIONS } = require('./SkillDefinitions');

const CLASS_BY_KEY = Object.freeze({
  LeapSlash: General.LeapSlashSkill,
  SevenInSevenOut: General.SevenInSevenOutSkill,
  BattleShout: General.BattleShoutSkill,
  HolySword: General.HolySwordSkill,
  ArrowRain: General.ArrowRainSkill,
  FireArrowBarrage: General.FireArrowBarrageSkill,
  StunPassive: General.StunPassiveSkill,
  SoulCapture: Boss.SoulCaptureSkill,
  SoulSummon: Boss.SoulSummonSkill,
  Inspire: Boss.InspireSkill,
  Demolition: Boss.DemolitionSkill,
  RainStorm: Boss.RainStormSkill,
  Enthrall: Boss.EnthrallSkill,
  CavalryOrder: Boss.CavalryOrderSkill,
  FangTianHalberd: Boss.FangTianHalberdSkill,
  Devour: Boss.DevourSkill,
  Madness: Boss.MadnessSkill,
  DevourEyes: Boss.DevourEyesSkill,
  WarlordSeal: Boss.WarlordSealSkill,
});

class SkillFactory {
  constructor({ objectPool = null } = {}) { this.objectPool = objectPool; this.registry = new Map(Object.entries(CLASS_BY_KEY)); }
  register(key, ClassType) { if (this.registry.has(key)) throw new Error(`Duplicate skill key: ${key}`); this.registry.set(key, ClassType); }
  create(key) {
    const ClassType = this.registry.get(key);
    if (!ClassType) throw new Error(`Unknown skill key: ${key}`);
    return this.objectPool ? this.objectPool.takeByClass(ClassType, () => new ClassType()) : new ClassType();
  }
  recover(skill) { if (this.objectPool) return this.objectPool.recoverByClass(skill); return false; }
  keys() { return [...this.registry.keys()]; }
  validate() {
    for (const definition of SKILL_DEFINITIONS) if (!this.registry.has(definition.key)) throw new Error(`Missing skill class: ${definition.key}`);
    return true;
  }
}
module.exports = { SkillFactory, CLASS_BY_KEY };
