'use strict';
const { BuffType }=require('../../buffs/BuffTypes');
class MadnessEffect {
  constructor({unitRegistry,buffManager,presentation}={}){Object.assign(this,{unitRegistry,buffManager,presentation});}
  execute({boss,range,durationMs=2000}={}){
    if(!boss||!this.unitRegistry||!this.buffManager)return {status:'MISSING_MADNESS_DEPENDENCY'};
    const affected=[];
    for(const unit of this.unitRegistry.unitsInRadius(boss.centerX,boss.centerY,range,boss.isPlayerLane)){
      const dx=(unit.displayObject?unit.displayObject.x:0)-boss.centerX;
      const dy=(unit.displayObject?unit.displayObject.y:0)-boss.centerY;
      const length=Math.hypot(dx,dy)||1;
      const vector={x:dx/length*160,y:dy/length*160};
      const buffId=this.buffManager.applyBuff(unit.id,BuffType.KNOCKDOWN,0,false,durationMs,vector);
      affected.push({unitId:unit.id,buffId,vector});
      if(this.presentation)this.presentation.createEntityVfx(unit,'madness-impact');
    }
    return {status:'APPLIED',ownerId:boss.id,affected};
  }
}
module.exports={MadnessEffect};
