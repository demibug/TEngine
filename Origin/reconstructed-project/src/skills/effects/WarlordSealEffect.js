'use strict';
const { BuffType }=require('../../buffs/BuffTypes');
const { BuffTimeMode }=require('../../buffs/BuffTimeMode');
class WarlordSealEffect {
  constructor({unitRegistry,buffManager,presentation}={}){Object.assign(this,{unitRegistry,buffManager,presentation});}
  execute({boss}={}){
    if(!boss||!this.unitRegistry||!this.buffManager)return {status:'MISSING_WARLORD_SEAL_DEPENDENCY'};
    const target=this.unitRegistry.highestLevel(boss.isPlayerLane,1)[0]||null;
    if(!target)return {status:'NO_TARGET',ownerId:boss.id};
    const duration=target.level<5?BuffTimeMode.PERMANENT:10000;
    const buffId=this.buffManager.applyBuff(target.id,BuffType.LOCK,1,false,duration,{source:'WarlordSeal'});
    if(this.presentation)this.presentation.createEntityVfx(target,'warlord-seal');
    return {status:'APPLIED',ownerId:boss.id,targetId:target.id,buffId,duration};
  }
}
module.exports={WarlordSealEffect};
