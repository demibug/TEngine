'use strict';
class DeadEntityRegistry {
  constructor({eventBus=null,logger=console}={}){this.eventBus=eventBus;this.logger=logger;this.enemySnapshots=[];this.listeners=new Set();this.nextSnapshotId=1;}
  recordEnemy(enemy){if(!enemy||enemy.isBoss)return null;const visual=enemy.visual;const snapshot={snapshotId:this.nextSnapshotId++,kind:'enemy',typeKey:enemy.constructor.typeKey||enemy.typeKey||enemy.constructor.name.replace(/Enemy$/,''),typeIndex:enemy.typeIndex,isPlayerLane:Boolean(enemy.isPlayerLane),isSpecial:Boolean(enemy.isSpecial),x:visual?visual.x:0,y:visual?visual.y:0,pathIndex:enemy.currentPathIndex||0,level:enemy.level||1,sourceEnemyId:enemy.id,recordedAt:Date.now()};this.enemySnapshots.push(snapshot);for(const listener of [...this.listeners])listener(snapshot);return snapshot;}
  onRecord(listener){if(typeof listener!=='function')throw new TypeError('DeadEntityRegistry listener must be a function');this.listeners.add(listener);return()=>this.listeners.delete(listener);}
  consume(snapshotId){const index=this.enemySnapshots.findIndex(v=>v.snapshotId===snapshotId);if(index<0)return null;return this.enemySnapshots.splice(index,1)[0];}
  recent({side=null,limit=Infinity,predicate=null}={}){const out=[];for(let i=this.enemySnapshots.length-1;i>=0&&out.length<limit;i--){const s=this.enemySnapshots[i];if(side!=null&&s.isPlayerLane!==Boolean(side))continue;if(predicate&&!predicate(s))continue;out.push(s);}return out;}
  clear(){this.enemySnapshots.length=0;this.listeners.clear();}
  get count(){return this.enemySnapshots.length;}
}
module.exports={DeadEntityRegistry};
