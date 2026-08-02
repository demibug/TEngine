'use strict';
const { SKILL_VFX_MANIFEST } = require('./SkillResourceManifest');
class SkillVfxRegistry {
  constructor(){this.entries=new Map(Object.entries(SKILL_VFX_MANIFEST));}
  get(key){return this.entries.get(key)||null;}
  keys(){return [...this.entries.keys()];}
}
module.exports={SkillVfxRegistry};
