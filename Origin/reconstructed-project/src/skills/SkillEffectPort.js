'use strict';
const { BuffType }=require('../buffs/BuffTypes');
const { SoulCaptureEffect,SoulSummonEffect,DemolitionEffect,RainStormEffect,FangTianHalberdEffect,DevourEffect,DevourEyesEffect,EnthrallEffect,MadnessEffect,WarlordSealEffect,BattleShoutEffect,HolySwordEffect,ArrowRainEffect,FireArrowBarrageEffect,LeapSlashEffect,SevenInSevenOutEffect,MeteorStrikeEffect,InspireEffect,CavalryOrderEffect }=require('./effects');
class SkillEffectPort {
  constructor(options={}){this.handlers=new Map();this.deferredCalls=[];this.activeEffects=new Set();this.configure(options);this._installCoreHandlers();}
  configure(options={}){
    const keys=['buffManager','enemyManager','unitRegistry','eventBus','deadEntityRegistry','mapTileManager','presentation','audioRegistry','projectileManager','attackEffectManager','logger'];
    for(const key of keys)if(Object.prototype.hasOwnProperty.call(options,key))this[key]=options[key];
    if(!this.logger)this.logger=console;
    return this;
  }
  services(){return{deadRegistry:this.deadEntityRegistry,deadEntityRegistry:this.deadEntityRegistry,enemyManager:this.enemyManager,unitRegistry:this.unitRegistry,buffManager:this.buffManager,mapTileManager:this.mapTileManager,presentation:this.presentation,audioRegistry:this.audioRegistry,projectileManager:this.projectileManager,attackEffectManager:this.attackEffectManager,logger:this.logger};}
  register(key,handler){if(typeof handler!=='function')throw new TypeError(`Skill handler for ${key} must be a function`);this.handlers.set(key,handler);return this;}
  execute(key,context={}){const handler=this.handlers.get(key);if(!handler){const deferred={key,context,status:'DEFERRED_EFFECT_WITH_EXACT_CONTRACT'};this.deferredCalls.push(deferred);if(this.eventBus)this.eventBus.event('skill:effect:deferred',key,context);return deferred;}const result=handler(context);if(result&&typeof result.update==='function'&&typeof result.dispose==='function')this.activeEffects.add(result);return result;}
  update(deltaMs){for(const effect of [...this.activeEffects]){if(effect.disposed){this.activeEffects.delete(effect);continue;}effect.update(deltaMs);if(effect.disposed)this.activeEffects.delete(effect);}}
  clearOwner(ownerId){for(const effect of [...this.activeEffects])if(effect.ownerId===ownerId){effect.dispose('owner-clear');this.activeEffects.delete(effect);}if(this.presentation)this.presentation.clearOwner(ownerId);}
  /** 武将每次攻击的技能 hook 通道（如跳斩溅射）：遍历该 owner 名下活跃 effect，调用其 onOwnerAttack。 */
  onOwnerAttack(ownerId,context={}){if(ownerId==null)return;for(const effect of [...this.activeEffects]){if(effect.disposed||effect.ownerId!==ownerId)continue;if(typeof effect.onOwnerAttack==='function')effect.onOwnerAttack(context);if(effect.disposed)this.activeEffects.delete(effect);}}
  gameOver(){for(const effect of [...this.activeEffects])effect.dispose('game-over');this.activeEffects.clear();if(this.presentation)this.presentation.gameOver();}
  _installCoreHandlers(){
    this.register('StunPassive',({target,durationMs=2000})=>target&&this.buffManager?this.buffManager.applyBuff(target.id,BuffType.STUN,1,false,durationMs):{status:'NO_TARGET_OR_BUFF_MANAGER'});
    this.register('SoulCapture',ctx=>new SoulCaptureEffect(this.services()).execute(ctx));
    // 鼓舞（张角，bundle:31120-31186）：从 inline lambda 迁出为独立效果类 InspireEffect（task 2.1/2.4）。
    this.register('Inspire',ctx=>new InspireEffect(this.services()).execute(ctx));
    // 铁骑号令（华雄，bundle:32753-32802）：从 inline lambda 迁出为独立效果类 CavalryOrderEffect（task 2.2/2.4）。
    this.register('CavalryOrder',ctx=>new CavalryOrderEffect(this.services()).execute(ctx));
    this.register('SoulSummon',ctx=>new SoulSummonEffect(this.services()).execute(ctx));
    this.register('Demolition',ctx=>new DemolitionEffect(this.services()).execute(ctx));
    this.register('RainStorm',ctx=>new RainStormEffect(this.services()).execute(ctx));
    this.register('Enthrall',ctx=>new EnthrallEffect(this.services()).execute(ctx));
    this.register('FangTianHalberd',ctx=>new FangTianHalberdEffect(this.services()).execute({...ctx,durationMs:5000}));
    this.register('Devour',ctx=>new DevourEffect(this.services()).execute(ctx));
    this.register('Madness',ctx=>new MadnessEffect(this.services()).execute({...ctx,durationMs:2000}));
    this.register('DevourEyes',ctx=>new DevourEyesEffect(this.services()).execute({...ctx,durationMs:5000}));
    this.register('WarlordSeal',ctx=>new WarlordSealEffect(this.services()).execute(ctx));
    this.register('BattleShout',ctx=>new BattleShoutEffect(this.services()).execute(ctx));
    this.register('HolySword',ctx=>new HolySwordEffect(this.services()).execute(ctx));
    this.register('ArrowRain',ctx=>new ArrowRainEffect(this.services()).execute(ctx));
    this.register('FireArrowBarrage',ctx=>new FireArrowBarrageEffect(this.services()).execute(ctx));
    this.register('LeapSlash',ctx=>new LeapSlashEffect(this.services()).execute(ctx));
    this.register('SevenInSevenOut',ctx=>new SevenInSevenOutEffect(this.services()).execute(ctx));
    // 提案 ④b（task 8.3）：陨石经 StaticFireBall/GroundSpikeBullet 孤子弹种实体承载。
    // DEFERRED: bundle:27450 陨石原始为纯 Laya.Image 视觉特效不走弹种通道，此处为纯逻辑层弹种化重建。
    this.register('MeteorStrike',ctx=>new MeteorStrikeEffect(this.services()).execute(ctx));
  }
  _applyToTargets(targets,type,value,durationMs){if(!this.buffManager)return{status:'DEFERRED_MISSING_BUFF_MANAGER'};return targets.filter(Boolean).map(target=>this.buffManager.applyBuff(target.id,type,value,false,durationMs));}
}
module.exports={SkillEffectPort};
