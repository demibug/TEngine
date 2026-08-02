#!/usr/bin/env node
'use strict';
const { createBootToBattleHarness, advanceTimer } = require('../tests/mocks/createBootToBattleHarness');
(async()=>{
  const {Laya,context}=await createBootToBattleHarness({config:{directBattle:true,developmentBattleStartDelayMs:0}});
  await advanceTimer(Laya,80,80);
  const enemyTypes=['Mob0','Mob1','Mob2','Mob3','Zombie','Cavalry','Puppet'];
  const spawned=[];
  for(const key of enemyTypes){ const enemy=context.enemyManager.spawnByKey(key,true,false); spawned.push({key,id:enemy.id,health:enemy.health,speed:enemy.baseMoveSpeed}); }
  context.gameData.battle.forceBossNextRound=true;
  const plan=context.waveManager.beginRound(3);
  await advanceTimer(Laya,160,80);
  const result={
    version:require('../package.json').version,
    enemyFactoryKeys:[...context.enemyFactory.creators.keys()],
    bossFactoryKeys:context.bossFactory.keys(),
    skillFactoryKeys:context.skillFactory.keys(),
    spawned,
    wavePlan:plan,
    activeEnemies:context.enemyManager.count,
    activeBosses:context.bossManager.count,
    activeSkills:context.skillManager.count,
    fixedUpdateNames:context.gameLoop.names?context.gameLoop.names():undefined,
    realNetworkRequests:context.network.assertNoRealNetworkCalls()?0:1,
    nativePlatformCalls:context.platform.assertNoNativePlatformCalls()?0:1,
  };
  context.battleFlow.cleanupBattle(false);
  result.afterCleanup={enemies:context.enemyManager.count,bosses:context.bossManager.count,skills:context.skillManager.count,buffs:context.buffManager.activeHandlerCount};
  process.stdout.write(JSON.stringify(result,null,2)+'\n');
})().catch(e=>{console.error(e.stack||e);process.exit(1)});
