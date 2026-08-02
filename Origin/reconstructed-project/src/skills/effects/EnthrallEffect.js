'use strict';
const { BuffType }=require('../../buffs/BuffTypes');
const { BuffTimeMode }=require('../../buffs/BuffTimeMode');
const { EffectHandle }=require('./EffectHandle');
class EnthrallEffect {
  constructor({unitRegistry,enemyManager,buffManager,presentation}={}){Object.assign(this,{unitRegistry,enemyManager,buffManager,presentation});}
  execute({boss,durationMs=1000}={}){
    if(!boss||!this.unitRegistry||!this.enemyManager)return{status:'MISSING_ENTHRALL_DEPENDENCY'};
    const candidates=this.unitRegistry.lowestLevel(boss.isPlayerLane,this.unitRegistry.unitsBySide(boss.isPlayerLane).length)
      .map(unit=>({unitId:unit.id,type:unit.unitText,level:unit.level,gridX:unit.gridPosition.x,gridY:unit.gridPosition.y}));
    if(!candidates.length)return{status:'NO_TARGET',ownerId:boss.id};
    for(const item of candidates)if(this.buffManager)this.buffManager.applyBuff(item.unitId,BuffType.CHARM,0,false,BuffTimeMode.PERMANENT,{source:'Enthrall'});
    let elapsed=0,index=0;const interval=durationMs/candidates.length,converted=[];let handle=null;
    const convert=item=>{const unit=this.unitRegistry.getUnit(item.unitId);if(!unit)return;
      if(this.presentation)this.presentation.createEntityVfx(unit,'enthrall-beam');
      if(!this.unitRegistry.removeUnit(item.unitId))return;
      const puppet=this.enemyManager.spawnByKey('Puppet',boss.isPlayerLane,false,enemy=>{
        if(typeof enemy.configurePuppet==='function')enemy.configurePuppet({level:item.level,soldierSkinIndex:Math.max(0,['刀','弓','枪','骑'].indexOf(item.type)),startPosition:{x:item.gridX*80,y:item.gridY*80},pathIndex:boss.currentPathIndex||0});
      });
      if(puppet&&puppet.visual)puppet.pos(item.gridX*80,item.gridY*80);
      converted.push({unitId:item.unitId,enemyId:puppet.id,type:item.type,level:item.level});
    };
    handle=new EffectHandle({ownerId:boss.id,disposeOnTimelineEnd:true,metadata:{converted},update:(dt,self)=>{elapsed+=dt;while(index<candidates.length&&elapsed>=(index+1)*interval){convert(candidates[index]);index++;}if(index>=candidates.length)self.dispose('conversion-complete');}});
    return handle;
  }
}
module.exports={EnthrallEffect};
