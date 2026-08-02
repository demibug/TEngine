'use strict';
const { SingletonBase }=require('../core/SingletonBase');
const { GameEvents }=require('../core/EventBus');
class BossManager extends SingletonBase{
 constructor(){super();this.bosses=new Map();this.initialized=false;this.creationLog=[];}
 configure({factory,eventBus,enemyManager,logger=console}={}){if(!factory||!eventBus||!enemyManager)throw new TypeError('BossManager requires factory,eventBus,enemyManager');Object.assign(this,{factory,eventBus,enemyManager,logger});return this;}
 init(){this.factory.validate();this.eventBus.on(GameEvents.ENEMY_REGISTERED,this,this._onRegistered);this.eventBus.on(GameEvents.ENEMY_REMOVED,this,this._onRemoved);this.initialized=true;}
 startGame(){this.creationLog=[];}
 spawn(key,playerLane){if(!this.initialized)this.init();const boss=this.factory.create(key);boss.init(Boolean(playerLane));this.creationLog.push({key,playerLane:Boolean(playerLane),id:boss.id});return boss;}
 _onRegistered(id,enemy){if(enemy&&enemy.isBoss)this.bosses.set(id,enemy);}
 _onRemoved(id){this.bosses.delete(id);}
 gameOver(){for(const boss of [...this.bosses.values()])boss.gameOver();this.bosses.clear();}
 get count(){return this.bosses.size;}
}
module.exports={BossManager};
