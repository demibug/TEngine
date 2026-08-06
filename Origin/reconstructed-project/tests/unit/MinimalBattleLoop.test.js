'use strict';
/**
 * 最简战斗闭环烟测（OpenSpec change `minimal-battle-loop-gap-fix` 任务 5.1-5.5）。
 *
 * 用 MockLaya（tests/mocks/LayaSceneMock.js 的 createLayaSceneMock，提供 Laya.timer.tick(ms)
 * 推进 frameLoop/once 任务）驱动 MinimalBattleBootstrap 编排的 GameLoop，加速跑完整闭环：
 *   出兵 → 敌人沿 opponentPath/playerPath 移动 → 接触阿斗扣血 → 玩家放刀/弓/枪/骑兵 →
 *   士兵攻击敌人 → 敌人死亡入池 → 胜负判定 → 战斗结束入池 + 可开始下一场。
 *
 * 加速：用 Laya.timer.tick(80)（GameLoop.LOGIC_STEP_MS）大步推进，不实时等待。
 * 隔离：每个用例前后调 MinimalBattleBootstrap.resetSingletonsForTests() 重置所有单例，
 *       避免跨用例污染（参照 bootstrap 静态方法 + DevelopmentBootstrap.resetSingletonsForTests）。
 */
const test = require('node:test');
const assert = require('node:assert/strict');
const { createLayaSceneMock } = require('../mocks/LayaSceneMock');
const { MinimalBattleBootstrap } = require('../../src/bootstrap/MinimalBattleBootstrap');
const { GameEvents } = require('../../src/core/EventBus');

// 固定随机源：MathRandom.weightedIndex 选 spawnStrategy 下标 0（权重 [5,2,3]→0.5*10=5 命中 index 0）；
// DeckManager.drawText(minimalMode) 用 Math.floor(0.5*4)=2 → '枪'，但本测试用 setHand 强制手牌覆盖。
const RANDOM = () => 0.5;
// now() 取 Laya.timer.currTimer + 5001，避开 BattleState.standardBattleDelayEnabled 的 0 点判定。
const NOW = laya => () => laya.timer.currTimer + 5001;

/** 创建并启动一个最简战斗上下文。返回 { Laya, ctx }。 */
function bootBattle() {
  const Laya = createLayaSceneMock();
  const bootstrap = new MinimalBattleBootstrap({ Laya, random: RANDOM, now: NOW(Laya) });
  const ctx = bootstrap.createContext();
  bootstrap.start();
  return { Laya, ctx };
}

/** 推进 Laya.timer N 个 80ms 逻辑子步。 */
function tickN(Laya, n) { for (let i = 0; i < n; i += 1) Laya.timer.tick(80); }

/** 在指定侧手牌槽放兵；用 setHand 预置手牌后逐槽 purchaseAndPlace。返回放置结果数组。 */
function placeUnits(ctx, side, specs) {
  // setHand 接受字符串数组，强制覆盖 minimalMode 抽到的手牌，确保 4 种兵可达。
  ctx.deckManager.setHand(side, specs.map(s => s[0]).concat(Array.from({ length: 5 - specs.length }, () => '枪')));
  const results = [];
  for (let slot = 0; slot < specs.length; slot += 1) {
    const [, gridX, gridY] = specs[slot];
    results.push(ctx.inputController.purchaseAndPlace({ side, slot, gridX, gridY }));
  }
  return results;
}

// 玩家侧可建造格（map0 '1_1'）：[3,1][3,2][4,1][4,2][5,1][5,2]，靠近玩家路径 y=4-6 段。
// 对手侧可建造格（map0 '1_0'）：[2,7][2,8][3,7][3,8][4,7][4,8]，靠近对手路径 y=3-5 段。

test('5.1+5.2: 敌人按波次生成并沿路径移动到阿斗，阿斗血量随接触扣减', () => {
  MinimalBattleBootstrap.resetSingletonsForTests();
  const { Laya, ctx } = bootBattle();
  try {
    // 5.1：推进到 delayTime(10000ms) 后第一波开始 + 敌人陆续生成。
    tickN(Laya, 160); // 12800ms：第一波已开始生成。
    assert.ok(ctx.enemyManager.count > 0, '第一波应有敌人生成');

    // 5.2：敌人沿 opponentPath/playerPath 移动——currentPathIndex 从出生 0 推进 >0。
    // 继续推进让敌人沿路径前进（每段 80px ÷ 50px·s⁻¹ ≈ 1600ms/段）。
    tickN(Laya, 60); // 累计 17600ms：敌人应已推进数个路径点。
    const enemies = [...ctx.enemyManager.enemies.values()];
    const moved = enemies.filter(e => e.currentPathIndex > 0);
    assert.ok(moved.length > 0, '应有敌人沿路径移动（currentPathIndex 推进）');
    assert.ok(moved.some(e => e.path && e.path.length > 0), '敌人应已解析路径');

    // 5.2：敌人走到路径末段接触阿斗，阿斗血量随接触攻击扣减。
    // 路径约 15 段，首敌约 38480ms 接触阿斗；推进到血量下降或战斗结束。
    const healthBefore = ctx.battleState.playerHealth + ctx.battleState.opponentHealth;
    let healthDropped = false;
    for (let i = 0; i < 400; i += 1) {
      Laya.timer.tick(80);
      if (ctx.battleState.playerHealth + ctx.battleState.opponentHealth < healthBefore) { healthDropped = true; break; }
      if (ctx.battleState.isGameOver) break;
    }
    assert.ok(healthDropped, '阿斗血量应随敌人接触攻击扣减');
  } finally {
    MinimalBattleBootstrap.resetSingletonsForTests();
  }
});

test('5.3: 玩家放置刀/弓/枪/骑兵，士兵攻击敌人，敌人死亡入池', () => {
  MinimalBattleBootstrap.resetSingletonsForTests();
  const { Laya, ctx } = bootBattle();
  try {
    // 放置刀/弓/枪/骑 4 种兵到玩家可建造格（弓/枪射程可覆盖玩家路径 y=4 段敌人）。
    const results = placeUnits(ctx, true, [['弓', 4, 2], ['刀', 3, 2], ['枪', 5, 2], ['骑', 3, 1]]);
    assert.deepEqual(results.map(r => r.success), [true, true, true, true], '4 种兵应全部放置成功');
    assert.deepEqual(results.map(r => r.card.text), ['弓', '刀', '枪', '骑'], '应放置刀/弓/枪/骑 4 种兵');
    assert.equal(ctx.unitManager.count, 4, '应有 4 个活跃士兵');

    const killEvents = [];
    ctx.eventBus.on(GameEvents.ENEMY_KILLED_BY, null, () => killEvents.push(true));

    const resetForPoolBefore = ctx.enemyPresentation.calls.filter(c => c[0] === 'resetForPool').length;
    const mobRecoverBefore = ctx.objectPool.recoverLog.filter(r => r.key === 'mob').length;

    // 推进让波次开始、敌人进入弓/枪攻击范围、士兵攻击并击杀。
    tickN(Laya, 400); // 32000ms：敌人进入射程，弓(DevelopmentAnimationDriver STOPPED→箭矢命中) + 枪(MeleeAttackEffect 管理器推进命中) 击杀。

    // 5.3：士兵攻击敌人——弓/枪进入 ATTACK 状态。
    const soldiers = [...ctx.unitManager.soldiers.values()];
    const attacking = soldiers.filter(u => u.currentState === 'UnitAttack');
    assert.ok(attacking.length > 0, '应有士兵进入攻击状态');

    // 5.3：敌人死亡——killCount>0 + ENEMY_KILLED_BY 事件。
    assert.ok(ctx.battleState.killCount > 0, '应有敌人被击杀（killCount>0）');
    assert.ok(killEvents.length > 0, '应触发 ENEMY_KILLED_BY 事件');

    // 5.3：死亡入池——presentation.resetForPool + objectPool.recoverByKey('mob') 回收表现节点。
    const resetForPoolAfter = ctx.enemyPresentation.calls.filter(c => c[0] === 'resetForPool').length;
    const mobRecoverAfter = ctx.objectPool.recoverLog.filter(r => r.key === 'mob').length;
    assert.ok(resetForPoolAfter > resetForPoolBefore, '死亡敌人应触发 resetForPool 入池');
    assert.ok(mobRecoverAfter > mobRecoverBefore, "死亡敌人表现节点应经 objectPool.recoverByKey('mob') 回收");
  } finally {
    MinimalBattleBootstrap.resetSingletonsForTests();
  }
});

test('5.4a: 阿斗血量≤0 判负（玩家阿斗不防御被敌人打至血≤0）', () => {
  MinimalBattleBootstrap.resetSingletonsForTests();
  const { Laya, ctx } = bootBattle();
  try {
    // 对手侧放兵防御对手阿斗（对手路径敌人被击杀→对手阿斗存活），玩家阿斗不防御→玩家阿斗死亡判负。
    // 这样判负信号(playerHealth≤0)先于判胜信号到达，幂等守卫固定判负，结果确定。
    placeUnits(ctx, false, [['枪', 3, 7], ['弓', 4, 7], ['枪', 2, 7], ['弓', 3, 8]]);

    let safety = 0;
    while (!ctx.battleState.isGameOver && safety < 3000) { Laya.timer.tick(80); safety += 1; }

    assert.ok(ctx.battleState.isGameOver, '战斗应结束（isGameOver=true）');
    assert.equal(ctx.battleState.lastBattleResult, false, '玩家阿斗血量≤0 应判负（lastBattleResult=false）');
    assert.equal(ctx.battleState.playerHealth, 0, '玩家阿斗血量应归零');
    assert.ok(ctx.battleState.opponentHealth > 0, '对手阿斗应被防御存活（保证判负非判胜竞争）');
  } finally {
    MinimalBattleBootstrap.resetSingletonsForTests();
  }
});

test('5.4b: 对手阿斗血量≤0 判胜（玩家侧防御，对手阿斗不防御被打至血≤0）', () => {
  MinimalBattleBootstrap.resetSingletonsForTests();
  const { Laya, ctx } = bootBattle();
  try {
    // 玩家侧放兵防御玩家阿斗（玩家路径敌人被击杀→玩家阿斗存活），对手阿斗不防御→对手阿斗死亡判胜。
    // 这样判胜信号(opponentHealth≤0)先于判负信号到达，幂等守卫固定判胜，结果确定。
    placeUnits(ctx, true, [['弓', 4, 2], ['枪', 5, 2], ['弓', 3, 2], ['枪', 4, 2]]);

    let safety = 0;
    while (!ctx.battleState.isGameOver && safety < 3000) { Laya.timer.tick(80); safety += 1; }

    assert.ok(ctx.battleState.isGameOver, '战斗应结束（isGameOver=true）');
    assert.equal(ctx.battleState.lastBattleResult, true, '对手阿斗血量≤0 应判胜（lastBattleResult=true）');
    assert.equal(ctx.battleState.opponentHealth, 0, '对手阿斗血量应归零');
    assert.ok(ctx.battleState.playerHealth > 0, '玩家阿斗应被防御存活（保证判胜非判负竞争）');
  } finally {
    MinimalBattleBootstrap.resetSingletonsForTests();
  }
});

test('5.5: 战斗结束后全部入池，可开始下一场', () => {
  MinimalBattleBootstrap.resetSingletonsForTests();
  const { Laya, ctx } = bootBattle();
  try {
    // 跑一局到战斗结束（不防御，双侧阿斗同归，幂等守卫取首信号结束）。
    let safety = 0;
    while (!ctx.battleState.isGameOver && safety < 3000) { Laya.timer.tick(80); safety += 1; }
    assert.ok(ctx.battleState.isGameOver, '战斗应结束');

    // 战斗结束后触发各管理器 gameOver 清理（入池复用路径）：敌人/士兵/箭矢/攻击效果全部入池。
    ctx.enemyManager.gameOver();
    ctx.unitManager.gameOver();
    ctx.projectileManager.gameOver();
    if (ctx.attackEffectManager && typeof ctx.attackEffectManager.gameOver === 'function') ctx.attackEffectManager.gameOver();

    assert.equal(ctx.enemyManager.count, 0, '战斗结束后全部敌人应入池（enemyManager.count=0）');
    assert.equal(ctx.unitManager.count, 0, '战斗结束后全部士兵应入池（unitManager.count=0）');
    assert.equal(ctx.projectileManager.activeCount, 0, '战斗结束后全部箭矢应入池（projectileManager.activeCount=0）');
    assert.equal(ctx.attackEffectManager.activeCount, 0, '战斗结束后攻击效果应全部入池');
  } finally {
    MinimalBattleBootstrap.resetSingletonsForTests();
  }

  // 可开始下一场：重置单例后用新 MockLaya 重新编排 + 启动，新战斗可正常出兵。
  const Laya2 = createLayaSceneMock();
  const bootstrap2 = new MinimalBattleBootstrap({ Laya: Laya2, random: RANDOM, now: NOW(Laya2) });
  const ctx2 = bootstrap2.createContext();
  bootstrap2.start();
  try {
    assert.equal(ctx2.battleState.isGameOver, false, '下一场应可启动（isGameOver=false）');
    assert.equal(ctx2.battleState.playerHealth, ctx2.battleState.playerMaxHealth, '下一场阿斗血量应重置');
    assert.equal(ctx2.enemyManager.count, 0, '下一场开始前应无残留敌人');
    tickN(Laya2, 160); // 推进到第一波出兵。
    assert.ok(ctx2.enemyManager.count > 0, '下一场应能正常出兵（敌人生成）');
  } finally {
    MinimalBattleBootstrap.resetSingletonsForTests();
  }
});
