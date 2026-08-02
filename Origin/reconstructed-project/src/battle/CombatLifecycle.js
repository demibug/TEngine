'use strict';
const START_ORDER=Object.freeze(['economy','deckManager','battleManager','enemyManager','unitManager','weaponManager','projectileManager','buffManager','skillManager','bossManager','waveManager','inputController','aiController']);
const CLEANUP_ORDER=Object.freeze(['aiController','inputController','deckManager','waveManager','battleManager','bossManager','enemyManager','unitManager','weaponManager','projectileManager','skillManager','buffManager']);
class CombatLifecycle{
 constructor(services){this.services=services;}
 call(order,method,...args){const calls=[];for(const key of order){const service=this.services[key];if(service&&typeof service[method]==='function'){service[method](...args);calls.push(key);}}return calls;}
 start(){return this.call(START_ORDER,'startGame');}
 pause(){return this.call(START_ORDER,'pause');}
 resume(){return this.call(START_ORDER,'resume');}
 gameOver(isWin){return this.call(CLEANUP_ORDER,'gameOver',isWin);}
}
module.exports={CombatLifecycle,START_ORDER,CLEANUP_ORDER};
