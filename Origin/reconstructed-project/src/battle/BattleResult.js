'use strict';
class BattleResult {
  constructor(values={}){
    this.isWin=Boolean(values.isWin);this.star=Number(values.star||0);this.gold=Number(values.gold||0);this.battleDuration=Number(values.battleDuration||0);this.round=Number(values.round||0);this.playerTargetHealth=Number(values.playerTargetHealth||0);this.opponentTargetHealth=Number(values.opponentTargetHealth||0);this.weaponFragments=Array.isArray(values.weaponFragments)?values.weaponFragments.slice():[];this.killCount=Number(values.killCount||0);this.bossKillCount=Number(values.bossKillCount||0);this.endlessRound=Number(values.endlessRound||0);this.gameMode=values.gameMode||'normal';this.resultState=values.resultState||(this.isWin?'WIN':'LOSE');this.bj=Boolean(values.bj);this.raw=values.raw||null;Object.freeze(this.weaponFragments);
  }
  static calculateStar(isWin,battle){if(!isWin)return 0;const max=Math.max(1,Number(battle.playerMaxHealth)||3),hp=Math.max(0,Number(battle.playerHealth)||0);return hp>=max?3:hp>=Math.ceil(max/2)?2:1;}
  static fromRuntime({isWin,gameData,battleScene,now=Date.now,economy=null}={}){const battle=gameData&&gameData.battle||{};const end=now();const duration=battle.startTime?Math.max(0,end-battle.startTime):0;return new BattleResult({isWin,star:battle.resultStar||this.calculateStar(isWin,battle),gold:economy&&economy.snapshot?economy.snapshot().playerGold:(battle.gold||0),battleDuration:duration,round:battle.currentRound||0,playerTargetHealth:battle.playerHealth||0,opponentTargetHealth:battle.opponentHealth||0,weaponFragments:battle.weaponFragments||[],killCount:battle.killCount||0,bossKillCount:battle.bossKillCount||0,endlessRound:battle.endlessMode?battle.currentRound:0,gameMode:battle.endlessMode?'endless':'normal',bj:Boolean(battle.bj),raw:{sceneName:battleScene&&battleScene.name}});}
  toJSON(){return {...this,weaponFragments:this.weaponFragments.slice()};}
}
module.exports={BattleResult};
