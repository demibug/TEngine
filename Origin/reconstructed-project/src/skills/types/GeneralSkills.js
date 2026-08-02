'use strict';
const { ActiveSkill, PassiveSkill } = require('../SkillBase');
const { getSkillDefinition } = require('../SkillDefinitions');

class LeapSlashSkill extends ActiveSkill { constructor(){ super(getSkillDefinition('LeapSlash')); } }
class SevenInSevenOutSkill extends ActiveSkill { constructor(){ super(getSkillDefinition('SevenInSevenOut')); } }
class BattleShoutSkill extends ActiveSkill { constructor(){ super(getSkillDefinition('BattleShout')); } }
class HolySwordSkill extends ActiveSkill { constructor(){ super(getSkillDefinition('HolySword')); } }
class ArrowRainSkill extends ActiveSkill { constructor(){ super(getSkillDefinition('ArrowRain')); } }
class FireArrowBarrageSkill extends ActiveSkill { constructor(){ super(getSkillDefinition('FireArrowBarrage')); } }
class StunPassiveSkill extends PassiveSkill { constructor(){ super(getSkillDefinition('StunPassive')); } }
module.exports = { LeapSlashSkill, SevenInSevenOutSkill, BattleShoutSkill, HolySwordSkill, ArrowRainSkill, FireArrowBarrageSkill, StunPassiveSkill };
