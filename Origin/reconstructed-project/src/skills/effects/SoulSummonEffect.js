'use strict';
const { EffectHandle }=require('./EffectHandle');
class SoulSummonEffect {
  constructor({deadRegistry,enemyManager,presentation,logger=console}={}){Object.assign(this,{deadRegistry,enemyManager,presentation,logger});}
  execute({boss,range}){if(!boss||!this.deadRegistry||!this.enemyManager)return {status:'MISSING_SOUL_SUMMON_DEPENDENCY'};let revived=0;const maxRevive=3;const revive=s=>{if(revived>=maxRevive||s.isPlayerLane!==boss.isPlayerLane)return;const dx=s.x-boss.centerX,dy=s.y-boss.centerY;if(dx*dx+dy*dy>range*range)return;const consumed=this.deadRegistry.consume(s.snapshotId);if(!consumed)return;try{const enemy=this.enemyManager.spawnByKey(consumed.typeKey,consumed.isPlayerLane,consumed.isSpecial);enemy.pos(consumed.x,consumed.y);enemy.currentPathIndex=Math.max(0,consumed.pathIndex||0);enemy.lastPathIndex=enemy.currentPathIndex;revived++;if(this.presentation)this.presentation.createEntityVfx(enemy,'soul-revive',{skin:'resources/img/gameObject/enemy/soulHead.png'});}catch(error){this.logger.warn('SoulSummon revive failed',error);}};const unsubscribe=this.deadRegistry.onRecord(revive);return new EffectHandle({ownerId:boss.id,disposeOnTimelineEnd:true,dispose:()=>unsubscribe(),update:()=>{}});}
}
module.exports={SoulSummonEffect};
