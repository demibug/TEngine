'use strict';
const { BuffType }=require('../../buffs/BuffTypes');
class FangTianHalberdEffect {
  constructor({buffManager,unitRegistry,presentation}={}){Object.assign(this,{buffManager,unitRegistry,presentation});}
  execute({boss,range,durationMs=5000}={}){
    if(!boss||!this.buffManager||!this.unitRegistry)return {status:'MISSING_FANGTIAN_DEPENDENCY'};
    const affected=[];
    for(const unit of this.unitRegistry.unitsInRadius(boss.centerX,boss.centerY,range,boss.isPlayerLane)){
      const levelReduction=Math.max(1,Math.floor(unit.level/2));
      const buffId=this.buffManager.applyBuff(unit.id,BuffType.SUPPRESSION,0,false,durationMs,{levelReduction,mergeDisabled:true,source:'FangTianHalberd'});
      affected.push({unitId:unit.id,buffId,levelReduction});
      if(this.presentation)this.presentation.createEntityVfx(unit,'level-down',{skin:'prefab/lvlDownEff.lh'});
    }
    // BuffManager owns expiry/removal. Returning a plain result avoids an immortal effect handle.
    return {status:'APPLIED',ownerId:boss.id,affected};
  }
}
module.exports={FangTianHalberdEffect};
