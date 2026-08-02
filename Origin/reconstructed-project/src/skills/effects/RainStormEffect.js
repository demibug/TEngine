'use strict';
const { BuffType }=require('../../buffs/BuffTypes');
const { BuffTimeMode }=require('../../buffs/BuffTimeMode');
const { EffectHandle }=require('./EffectHandle');
class RainStormEffect {
  constructor({buffManager,unitRegistry,presentation,audioRegistry}={}){Object.assign(this,{buffManager,unitRegistry,presentation,audioRegistry});}
  execute({boss}={}){
    if(!boss||!this.buffManager||!this.unitRegistry)return{status:'MISSING_RAIN_DEPENDENCY'};
    const affected=new Map();const listeners=[];
    for(const unit of this.unitRegistry.soldiers.values()){
      if(unit.side!==boss.isPlayerLane)continue;
      const buffId=this.buffManager.applyBuff(unit.id,BuffType.ATTACK_SPEED,-.2,true,BuffTimeMode.PERMANENT,{source:'RainStorm'});
      affected.set(unit.id,buffId);
      const target=unit.displayObject;
      if(target&&typeof target.on==='function'){
        const onLevel=()=>{const id=affected.get(unit.id);if(id!=null)this.buffManager.removeBuff(unit.id,BuffType.ATTACK_SPEED,id);affected.delete(unit.id);};
        target.on('onLevelChanged',this,onLevel);listeners.push({target,onLevel});
      }
    }
    const overlay=this.presentation&&this.presentation.createOverlay('rain-overlay',{color:'#557da5',skin:'resources/img/gameObject/enemy/rain.png',alpha:.28,zIndex:600,layer:'weatherLayer',ownerId:boss.id});
    if(this.audioRegistry){this.audioRegistry.play('zhenFu_skill_rain',{ownerId:boss.id});this.audioRegistry.play('zhenFu_skill_rain_cycle',{loop:true,ownerId:boss.id});}
    return new EffectHandle({ownerId:boss.id,persistent:true,metadata:{affected},dispose:()=>{
      for(const [targetId,buffId] of affected)this.buffManager.removeBuff(targetId,BuffType.ATTACK_SPEED,buffId);
      affected.clear();for(const item of listeners)item.target.off('onLevelChanged',this,item.onLevel);
      if(overlay)overlay.remove();if(this.audioRegistry)this.audioRegistry.stop('zhenFu_skill_rain_cycle',boss.id);
    }});
  }
}
module.exports={RainStormEffect};
