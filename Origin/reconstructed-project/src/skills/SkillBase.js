'use strict';
class SkillBase {
  constructor(definition){if(!definition)throw new TypeError('SkillBase requires a definition');this.definition=definition;this.key=definition.key;this.name=definition.name;this.owner=null;this.manager=null;this.effectPort=null;this.cooldownMs=Math.max(0,Number(definition.cooldownSeconds||0)*1000);this.elapsedMs=this.cooldownMs;this.active=false;this.activationCount=0;this.lastContext=null;this.lifecycleId=0;}
  configure({owner=null,manager=null,effectPort=null}={}){this.owner=owner;this.manager=manager;this.effectPort=effectPort;return this;}
  startGame(){this.lifecycleId++;this.elapsedMs=this.cooldownMs;this.active=false;this.activationCount=0;this.lastContext=null;}
  update(deltaMs){if(!this.active)this.elapsedMs+=deltaMs;}
  canActivate(){return !this.active&&this.elapsedMs>=this.cooldownMs&&this.owner&&!this.owner.inPool&&this.owner.currentState!==4;}
  begin(context={}){if(!this.canActivate())return false;this.active=true;this.elapsedMs=0;this.activationCount++;this.lastContext=context;return true;}
  execute(context=this.lastContext||{}){return this.onActivate(context);}
  activate(context={}){if(!this.begin(context))return{activated:false,reason:'cooldown-or-owner-state'};const result=this.execute(context);this.finish();return{activated:true,result};}
  onActivate(context){if(!this.effectPort)return{status:'DEFERRED_MISSING_EFFECT_PORT',key:this.key,context};return this.effectPort.execute(this.key,{owner:this.owner,skill:this,...context});}
  finish(){this.active=false;}
  gameOver(){this.active=false;this.owner=null;this.manager=null;this.effectPort=null;this.lastContext=null;this.lifecycleId++;}
}
class ActiveSkill extends SkillBase{}
class PassiveSkill extends SkillBase{canActivate(){return Boolean(!this.active&&this.owner&&!this.owner.inPool&&this.owner.currentState!==4);}}
class BossSkill extends ActiveSkill{}
module.exports={SkillBase,ActiveSkill,PassiveSkill,BossSkill};
