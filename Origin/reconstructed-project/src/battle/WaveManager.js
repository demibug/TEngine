'use strict';
const { SingletonBase } = require('../core/SingletonBase');
const { GameEvents } = require('../core/EventBus');
const { BOSS_TYPE_KEYS } = require('./EnemyFactory');

/**
 * Core wave reconstruction based on BattleManager vU and EnemyManager vi.jB/zB.
 * Visual BossTipDialog and audio are intentionally outside this class.
 */
class WaveManager extends SingletonBase {
  constructor(){super();this.currentPlan=null;this.started=false;this.roundPlans=new Map();this.planHistory=[];}
  configure({gameData,enemyManager,bossManager,eventBus,randomSource=Math.random,logger=console,skipBoss=false}={}){
    // skipBoss=true 时 bossManager 从必填降为可选（最简模式可不注入 bossManager）
    const missingRequired=skipBoss?(!gameData||!enemyManager||!eventBus):(!gameData||!enemyManager||!bossManager||!eventBus);
    if(missingRequired)throw new TypeError('WaveManager requires gameData, enemyManager, bossManager and eventBus');
    Object.assign(this,{gameData,enemyManager,bossManager,eventBus,randomSource,logger,skipBoss});return this;
  }
  init(){}
  startGame(){this.started=true;this.currentPlan=null;this.roundPlans.clear();this.planHistory=[];}
  planRound(round){
    const data=this.gameData.enemy,battle=this.gameData.battle;
    const counts=data.waveUnitCounts;
    const normalCount=battle.endlessMode&&round>counts.length?counts[counts.length-1]+2*(round-counts.length):counts[Math.min(round,counts.length)-1];
    if(!Number.isFinite(normalCount))throw new Error(`Missing wave count for round ${round}`);
    let boss=false,bossIndex=null,bossKey=null;
    // skipBoss=true 时 boss 始终为 false，不读 bossWaveNumbers/bossSpawnChances
    if(!this.skipBoss){
      const bossWaveIndex=data.bossWaveNumbers.indexOf(round);
      if(battle.forceBossNextRound){boss=true;battle.forceBossNextRound=false;}
      else if(bossWaveIndex>=0){ if(battle.bossDecisionByRound[round]===undefined)battle.bossDecisionByRound[round]=this.randomSource()<data.bossSpawnChances[bossWaveIndex]; boss=Boolean(battle.bossDecisionByRound[round]); }
    }
    if(boss){
      if(battle.bossTypeByRound[round]!==undefined) bossIndex=battle.bossTypeByRound[round];
      else { bossIndex=this.gameData.map.mapIndex*3+data.bossRotationIndex; battle.bossTypeByRound[round]=bossIndex; data.bossRotationIndex=(data.bossRotationIndex+1)%3; }
      bossKey=BOSS_TYPE_KEYS[bossIndex]; if(!bossKey)throw new Error(`Unknown boss index ${bossIndex} for round ${round}`);
      if(!battle.bossRounds.includes(round))battle.bossRounds.push(round);
    }
    const plan={round,normalCount,normalTypeIndex:this.gameData.map.enemyTypeIndex,boss,bossIndex,bossKey,bossSpawned:false};
    this.roundPlans.set(round,plan);this.planHistory.push({...plan});this.currentPlan=plan;this.eventBus.event(GameEvents.ROUND_SPAWN_PREPARED,plan);return plan;
  }
  // plan.boss 为 false 时跳过 bossManager.spawn；并对 bossManager 做 null-guard（skipBoss 模式可不注入）
  beginRound(round){const plan=this.planRound(round);if(plan.boss&&!plan.bossSpawned&&this.bossManager){this.bossManager.spawn(plan.bossKey,true);this.bossManager.spawn(plan.bossKey,false);plan.bossSpawned=true;const last=this.planHistory[this.planHistory.length-1];if(last&&last.round===round)last.bossSpawned=true;}return plan;}
  spawnNormalPair(index,playerSpecialIndex=-1,opponentSpecialIndex=-1){if(!this.currentPlan)throw new Error('WaveManager.beginRound() must run before spawning');const t=this.currentPlan.normalTypeIndex;return [this.enemyManager.spawn(t,true,playerSpecialIndex===index),this.enemyManager.spawn(t,false,opponentSpecialIndex===index)];}
  gameOver(){this.started=false;this.currentPlan=null;this.roundPlans.clear();}
}
module.exports={WaveManager};
