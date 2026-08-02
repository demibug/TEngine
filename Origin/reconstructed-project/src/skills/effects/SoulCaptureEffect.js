'use strict';
const { BuffType }=require('../../buffs/BuffTypes');
const { EffectHandle }=require('./EffectHandle');
/** ZhangLiang: expanding sweep from 500-1400ms, radius +6 each 100ms. */
class SoulCaptureEffect {
  constructor({unitRegistry,buffManager,presentation}={}){Object.assign(this,{unitRegistry,buffManager,presentation});}
  execute({boss,durationMs=900}={}){
    if(!boss||!this.unitRegistry||!this.buffManager)return{status:'MISSING_SOUL_CAPTURE_DEPENDENCY'};
    let elapsed=0,accumulator=0,radius=0;const affected=new Set();let handle=null;
    handle=new EffectHandle({ownerId:boss.id,disposeOnTimelineEnd:true,metadata:{affected},update:(dt,self)=>{
      elapsed+=dt;accumulator+=dt;
      while(accumulator>=100){accumulator-=100;radius+=6;
        for(const unit of this.unitRegistry.unitsInRadius(boss.centerX,boss.centerY,radius,boss.isPlayerLane)){
          if(affected.has(unit.id))continue;affected.add(unit.id);
          this.buffManager.applyBuff(unit.id,BuffType.CHAOS,0,false,2000,{source:'SoulCapture'});
          if(this.presentation)this.presentation.createEntityVfx(unit,'soul-capture');
        }
      }
      if(elapsed>=durationMs)self.dispose('sweep-complete');
    }});
    return handle;
  }
}
module.exports={SoulCaptureEffect};
