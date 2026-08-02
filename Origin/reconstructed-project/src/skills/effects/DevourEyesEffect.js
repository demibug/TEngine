'use strict';
const { EffectHandle }=require('./EffectHandle');
class DevourEyesEffect {
  constructor({presentation,audioRegistry}={}){Object.assign(this,{presentation,audioRegistry});}
  execute({boss,durationMs=5000}={}){
    if(!boss||!this.presentation)return {status:'MISSING_DARKNESS_PRESENTATION'};
    const overlay=this.presentation.createOverlay('devour-eyes',{color:'#000000',skin:'resources/img/gameObject/enemy/blackCloud0.png',alpha:.78,zIndex:900,layer:'overlayLayer',ownerId:boss.id,blocksInput:false});
    if(this.audioRegistry)this.audioRegistry.play('xiahouDun_skill_cloud',{ownerId:boss.id});
    let elapsed=0;let handle=null;
    handle=new EffectHandle({ownerId:boss.id,metadata:{durationMs},update:(dt,self)=>{elapsed+=dt;if(elapsed>=durationMs)self.dispose('duration-complete');},dispose:()=>{if(overlay)overlay.remove();}});
    return handle;
  }
}
module.exports={DevourEyesEffect};
