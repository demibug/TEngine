'use strict';
const { BuffType }=require('../buffs/BuffTypes');
const { SoulCaptureEffect,SoulSummonEffect,DemolitionEffect,RainStormEffect,FangTianHalberdEffect,DevourEffect,DevourEyesEffect,EnthrallEffect,MadnessEffect,WarlordSealEffect }=require('./effects');
class SkillEffectPort {
  constructor(options={}){this.handlers=new Map();this.deferredCalls=[];this.activeEffects=new Set();this.configure(options);this._installCoreHandlers();}
  configure(options={}){
    const keys=['buffManager','enemyManager','unitRegistry','eventBus','deadEntityRegistry','mapTileManager','presentation','audioRegistry','logger'];
    for(const key of keys)if(Object.prototype.hasOwnProperty.call(options,key))this[key]=options[key];
    if(!this.logger)this.logger=console;
    return this;
  }
  services(){return{deadRegistry:this.deadEntityRegistry,deadEntityRegistry:this.deadEntityRegistry,enemyManager:this.enemyManager,unitRegistry:this.unitRegistry,buffManager:this.buffManager,mapTileManager:this.mapTileManager,presentation:this.presentation,audioRegistry:this.audioRegistry,logger:this.logger};}
  register(key,handler){if(typeof handler!=='function')throw new TypeError(`Skill handler for ${key} must be a function`);this.handlers.set(key,handler);return this;}
  execute(key,context={}){const handler=this.handlers.get(key);if(!handler){const deferred={key,context,status:'DEFERRED_EFFECT_WITH_EXACT_CONTRACT'};this.deferredCalls.push(deferred);if(this.eventBus)this.eventBus.event('skill:effect:deferred',key,context);return deferred;}const result=handler(context);if(result&&typeof result.update==='function'&&typeof result.dispose==='function')this.activeEffects.add(result);return result;}
  update(deltaMs){for(const effect of [...this.activeEffects]){if(effect.disposed){this.activeEffects.delete(effect);continue;}effect.update(deltaMs);if(effect.disposed)this.activeEffects.delete(effect);}}
  clearOwner(ownerId){for(const effect of [...this.activeEffects])if(effect.ownerId===ownerId){effect.dispose('owner-clear');this.activeEffects.delete(effect);}if(this.presentation)this.presentation.clearOwner(ownerId);}
  gameOver(){for(const effect of [...this.activeEffects])effect.dispose('game-over');this.activeEffects.clear();if(this.presentation)this.presentation.gameOver();}
  _installCoreHandlers(){
    this.register('StunPassive',({target,durationMs=2000})=>target&&this.buffManager?this.buffManager.applyBuff(target.id,BuffType.STUN,1,false,durationMs):{status:'NO_TARGET_OR_BUFF_MANAGER'});
    this.register('SoulCapture',ctx=>new SoulCaptureEffect(this.services()).execute(ctx));
    this.register('Inspire',({alliedEnemies=[],durationMs=5000})=>{const ids=[];for(const target of alliedEnemies){if(!this.buffManager)continue;ids.push(this.buffManager.applyBuff(target.id,BuffType.SCALE,.2,true,durationMs));ids.push(this.buffManager.applyBuff(target.id,BuffType.MAX_HP,.5,true,durationMs));ids.push(this.buffManager.applyBuff(target.id,BuffType.MOVE_SPEED,.3,true,durationMs));}return{status:'APPLIED',ids};});
    this.register('CavalryOrder',({boss,enemyManager})=>{const manager=enemyManager||this.enemyManager;if(!manager||!boss)return{status:'MISSING_CAVALRY_DEPENDENCY'};const enemies=[];for(let i=0;i<5;i++)enemies.push(manager.spawnByKey('Cavalry',boss.isPlayerLane,false));return{status:'APPLIED',enemyIds:enemies.map(e=>e.id)};});
    this.register('SoulSummon',ctx=>new SoulSummonEffect(this.services()).execute(ctx));
    this.register('Demolition',ctx=>new DemolitionEffect(this.services()).execute(ctx));
    this.register('RainStorm',ctx=>new RainStormEffect(this.services()).execute(ctx));
    this.register('Enthrall',ctx=>new EnthrallEffect(this.services()).execute(ctx));
    this.register('FangTianHalberd',ctx=>new FangTianHalberdEffect(this.services()).execute({...ctx,durationMs:5000}));
    this.register('Devour',ctx=>new DevourEffect(this.services()).execute(ctx));
    this.register('Madness',ctx=>new MadnessEffect(this.services()).execute({...ctx,durationMs:2000}));
    this.register('DevourEyes',ctx=>new DevourEyesEffect(this.services()).execute({...ctx,durationMs:5000}));
    this.register('WarlordSeal',ctx=>new WarlordSealEffect(this.services()).execute(ctx));
  }
  _applyToTargets(targets,type,value,durationMs){if(!this.buffManager)return{status:'DEFERRED_MISSING_BUFF_MANAGER'};return targets.filter(Boolean).map(target=>this.buffManager.applyBuff(target.id,type,value,false,durationMs));}
}
module.exports={SkillEffectPort};
