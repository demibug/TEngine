'use strict';
class DevourEffect {
  constructor({enemyManager,presentation}={}){Object.assign(this,{enemyManager,presentation});}
  execute({boss,range}={}){
    if(!boss||!this.enemyManager)return {status:'MISSING_DEVOUR_DEPENDENCY'};
    const targets=[];this.enemyManager.queryEnemyObjects(boss.centerX,boss.centerY,range,boss.isPlayerLane,targets);
    let count=0;
    const normalBase=boss.gameData.resolveEnemyStats?boss.gameData.resolveEnemyStats(boss.gameData.map.enemyTypeIndex,boss.isPlayerLane).ph:10;
    const consumed=[];
    for(const enemy of targets){
      if(enemy===boss||enemy.isBoss||enemy.currentState===4)continue;
      consumed.push(enemy.id);count++;
      if(this.presentation)this.presentation.createEntityVfx(enemy,'devour-target',{skin:'resources/img/battleUI/eat1.png'});
      this.enemyManager.forceRemove(enemy.id);
    }
    if(count){
      const gain=2*normalBase*count;
      boss.maxHealthBase+=gain;
      boss.health=Math.min(boss.maxHealth,boss.health+gain);
      boss.baseVisualScale=(boss.baseVisualScale||1)+.01*count;
      if(this.presentation)this.presentation.updateEntityScale(boss,boss.baseVisualScale);
      if(boss.healthText)boss.healthText.text=boss.health.toFixed(0);
    }
    return {status:'APPLIED',ownerId:boss.id,consumed,gain:2*normalBase*count,scale:boss.baseVisualScale||1};
  }
}
module.exports={DevourEffect};
