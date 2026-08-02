'use strict';
const { BossSkill }=require('../SkillBase');
const { getSkillDefinition }=require('../SkillDefinitions');
class NamedBossSkill extends BossSkill {constructor(key){super(getSkillDefinition(key));this.runtimeContract=key;}onActivate(context){return super.onActivate({...context,sourceSkill:this.runtimeContract});}}
class SoulCaptureSkill extends NamedBossSkill{constructor(){super('SoulCapture');}}
class SoulSummonSkill extends NamedBossSkill{constructor(){super('SoulSummon');}}
class InspireSkill extends NamedBossSkill{constructor(){super('Inspire');}}
class DemolitionSkill extends NamedBossSkill{constructor(){super('Demolition');}}
class RainStormSkill extends NamedBossSkill{constructor(){super('RainStorm');}}
class EnthrallSkill extends NamedBossSkill{constructor(){super('Enthrall');}}
class CavalryOrderSkill extends NamedBossSkill{constructor(){super('CavalryOrder');}}
class FangTianHalberdSkill extends NamedBossSkill{constructor(){super('FangTianHalberd');}}
class DevourSkill extends NamedBossSkill{constructor(){super('Devour');}}
class MadnessSkill extends NamedBossSkill{constructor(){super('Madness');}}
class DevourEyesSkill extends NamedBossSkill{constructor(){super('DevourEyes');}}
class WarlordSealSkill extends NamedBossSkill{constructor(){super('WarlordSeal');}}
module.exports={SoulCaptureSkill,SoulSummonSkill,InspireSkill,DemolitionSkill,RainStormSkill,EnthrallSkill,CavalryOrderSkill,FangTianHalberdSkill,DevourSkill,MadnessSkill,DevourEyesSkill,WarlordSealSkill};
