'use strict';
const assert=require('node:assert/strict');
const { createLayaSceneMock }=require('../tests/mocks/LayaSceneMock');
const { DevelopmentBootstrap }=require('../src/bootstrap/DevelopmentBootstrap');
const { EnemyRuntimeState }=require('../src/entities/EnemyBase');

async function advance(Laya,totalMs,step=80){let remaining=totalMs;while(remaining>0){const dt=Math.min(step,remaining);Laya.timer.tick(dt);remaining-=dt;await Promise.resolve();}}
function moveUnitNear(unit,boss,offsetX=0,offsetY=0){if(unit&&unit.displayObject&&boss&&boss.visual)unit.displayObject.pos(boss.centerX+offsetX,boss.centerY+offsetY);}

(async()=>{
  DevelopmentBootstrap.resetSingletonsForTests();
  const Laya=createLayaSceneMock();
  const app=new DevelopmentBootstrap({Laya,config:{directBattle:true,developmentBattleStartDelayMs:0},random:()=>0});
  const ctx=await app.start();
  const scene=ctx.sceneManager.getScene('BattleScene');
  const unit=ctx.developmentUnitSpawner.spawnKnife({side:true,gridX:1,gridY:1,level:4});
  const unitId=unit.id;

  const zhangBao=ctx.bossManager.spawn('ZhangBao',true);
  const beforeSummon=ctx.enemyManager.enemies.size;
  zhangBao.skillElapsedMs=zhangBao.skillCooldownMs;
  const summonActivation=zhangBao.activateSkill();
  const summonStateHeld=zhangBao.currentState===EnemyRuntimeState.SKILL;
  const doomed=ctx.enemyManager.spawnByKey('Mob0',true,false);
  doomed.hit(doomed.health+1,{id:999});
  await advance(Laya,160);
  const afterSummon=ctx.enemyManager.enemies.size;

  const sun=ctx.bossManager.spawn('SunShangXiang',true);sun.skillElapsedMs=sun.skillCooldownMs;sun.activateSkill();const sunStateHeld=sun.currentState===EnemyRuntimeState.SKILL;await advance(Laya,560);const blockedDuringBattle=ctx.mapTileManager.count;

  const zhen=ctx.bossManager.spawn('ZhenFu',true);moveUnitNear(unit,zhen);zhen.skillElapsedMs=zhen.skillCooldownMs;zhen.activateSkill();await advance(Laya,960);
  const rainBuffApplied=ctx.buffManager.has(unit.id,1);const rainLayerCount=scene.weatherLayer?scene.weatherLayer.numChildren:0;

  const lvbu=ctx.bossManager.spawn('LvBu',true);moveUnitNear(unit,lvbu);const beforeLevel=unit.level;lvbu.skillElapsedMs=lvbu.skillCooldownMs;lvbu.activateSkill();await advance(Laya,720);
  const fangTian={beforeLevel,afterLevel:unit.level,mergeDisabled:Boolean(unit.mergeDisabled)};

  const dong=ctx.bossManager.spawn('DongZhuo',true);const food=ctx.enemyManager.spawnByKey('Mob0',true,false);food.pos(dong.centerX,dong.centerY);
  const beforeDevour={health:dong.health,maxHealth:dong.maxHealth,scale:dong.baseVisualScale||1,foodExists:Boolean(ctx.enemyManager.getById(food.id))};
  dong.skillElapsedMs=dong.skillCooldownMs;dong.activateSkill();await advance(Laya,560);
  const afterDevour={health:dong.health,maxHealth:dong.maxHealth,scale:dong.baseVisualScale||1,foodExists:Boolean(ctx.enemyManager.getById(food.id))};

  const xiahou=ctx.bossManager.spawn('XiaHouDun',true);xiahou.skillElapsedMs=xiahou.skillCooldownMs;xiahou.activateSkill();await advance(Laya,1080);
  const darknessDuring=scene.overlayLayer?scene.overlayLayer.numChildren:0;await advance(Laya,5040);const darknessAfter=scene.overlayLayer?scene.overlayLayer.numChildren:0;

  const preGameOver={unitId,summonActivated:Boolean(summonActivation&&summonActivation.activated),summonStateHeld,sunStateHeld,beforeSummon,afterSummon,deadSnapshots:ctx.deadEntityRegistry.count,blockedDuringBattle,rainBuffApplied,rainLayerCount,fangTian,beforeDevour,afterDevour,darknessDuring,darknessAfter,deferred:ctx.skillEffectPort.deferredCalls.map(v=>v.key)};
  assert.equal(preGameOver.summonActivated,true);assert.equal(preGameOver.summonStateHeld,true);assert.equal(preGameOver.sunStateHeld,true);
  assert.ok(afterSummon>beforeSummon,'SoulSummon did not revive an enemy');assert.ok(blockedDuringBattle>0,'Demolition did not block a tile');
  assert.equal(rainBuffApplied,true);assert.ok(rainLayerCount>0,'Rain overlay missing');assert.ok(fangTian.afterLevel<fangTian.beforeLevel);assert.equal(fangTian.mergeDisabled,true);
  assert.equal(afterDevour.foodExists,false);assert.ok(afterDevour.maxHealth>beforeDevour.maxHealth);assert.ok(afterDevour.scale>beforeDevour.scale);
  assert.ok(darknessDuring>0);assert.equal(darknessAfter,0);assert.deepEqual(preGameOver.deferred,[]);

  const result=ctx.battleFlow.gameOver(true);await ctx.sceneManager.whenLastOpenCompletes();const gameOver=ctx.sceneManager.getScene('GameOverScene');
  const postGameOver={blockedTiles:ctx.mapTileManager.count,activeEffects:ctx.skillEffectPort.activeEffects.size,buffTargets:ctx.buffManager.activeTargetCount,gameOverOpened:Boolean(gameOver),title:gameOver&&gameOver.title.text};
  assert.equal(postGameOver.blockedTiles,0);assert.equal(postGameOver.activeEffects,0);assert.equal(postGameOver.buffTargets,0);assert.equal(postGameOver.gameOverOpened,true);assert.equal(postGameOver.title,'胜利');
  gameOver.returnToMain();await ctx.sceneManager.whenLastOpenCompletes();const returnedToMain=Boolean(ctx.sceneManager.getScene('MainScene'));assert.equal(returnedToMain,true);
  console.log(JSON.stringify({status:'PASS',preGameOver,result,postGameOver,returnedToMain},null,2));
})().catch(error=>{console.error(error);process.exitCode=1;});
