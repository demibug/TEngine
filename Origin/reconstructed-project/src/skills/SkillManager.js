'use strict';
const { SingletonBase }=require('../core/SingletonBase');
const { SkillAnimationTimeline }=require('./presentation/SkillAnimationTimeline');
class SkillManager extends SingletonBase {
  constructor(){super();this.skills=new Map();this.ownerToSkills=new Map();this.timelines=new Set();this.initialized=false;}
  configure({gameLoop,factory,effectPort,presentation=null,logger=console}={}){if(!gameLoop||!factory||!effectPort)throw new TypeError('SkillManager requires gameLoop, factory and effectPort');Object.assign(this,{gameLoop,factory,effectPort,presentation,logger});return this;}
  init(){this.factory.validate();this.initialized=true;}
  startGame(){if(!this.initialized)this.init();this.gameLoop.register('SkillMgr',this,this.update);}
  attach(owner,skillKey){const skill=this.factory.create(skillKey).configure({owner,manager:this,effectPort:this.effectPort});skill.startGame();const runtimeKey=`${owner.id}:${skillKey}`;this.skills.set(runtimeKey,skill);let set=this.ownerToSkills.get(owner.id);if(!set){set=new Set();this.ownerToSkills.set(owner.id,set);}set.add(runtimeKey);return skill;}
  get(ownerId,skillKey){return this.skills.get(`${ownerId}:${skillKey}`)||null;}
  activate(ownerId,skillKey,context={}){const skill=this.get(ownerId,skillKey);if(!skill)throw new Error(`Skill ${skillKey} is not attached to owner ${ownerId}`);return skill.activate(context);}
  activateBossSkill(boss,skill,context,timelineDefinition={}){if(!skill.begin(context))return{activated:false,reason:'cooldown-or-owner-state'};const timeline=new SkillAnimationTimeline({owner:boss,skill,context,presentation:this.presentation,definition:timelineDefinition,onEffect:()=>skill.execute(context),onComplete:()=>{skill.finish();if(boss&&!boss.inPool&&boss.currentState!==4)boss.completeSkillState();},onCancel:()=>skill.finish()});this.timelines.add(timeline);timeline.start();return{activated:true,timeline};}
  update(deltaMs){for(const skill of this.skills.values())skill.update(deltaMs);for(const timeline of [...this.timelines]){timeline.update(deltaMs);if(timeline.completed||timeline.cancelled)this.timelines.delete(timeline);}if(this.effectPort)this.effectPort.update(deltaMs);}
  removeOwner(ownerId){for(const timeline of [...this.timelines])if(timeline.owner&&timeline.owner.id===ownerId){timeline.cancel('owner-removed');this.timelines.delete(timeline);}if(this.effectPort)this.effectPort.clearOwner(ownerId);const keys=this.ownerToSkills.get(ownerId);if(!keys)return;for(const key of keys){const skill=this.skills.get(key);if(skill){skill.gameOver();this.factory.recover(skill);}this.skills.delete(key);}this.ownerToSkills.delete(ownerId);}
  gameOver(){this.gameLoop.unregister('SkillMgr');for(const timeline of [...this.timelines])timeline.cancel('game-over');this.timelines.clear();for(const ownerId of [...this.ownerToSkills.keys()])this.removeOwner(ownerId);this.skills.clear();if(this.effectPort)this.effectPort.gameOver();}
  get count(){return this.skills.size;}
}
module.exports={SkillManager};
