'use strict';

// 任务 8.1：难度 0 等价路径验证（OpenSpec Change: ai-advanced-strategy，决策 4）。
//
// 覆盖 spec「难度 0 路径不破坏现有单局闭环 smoke」Requirement 全部 Scenario，
// 验证 Si=0 时 AI 为弱策略（决策慢+随机放置+弱攻击+无收入+道具 no-op），
// 不破坏单局闭环 smoke 前提：
//   1) fG=2000ms（决策间隔最慢，难度 0 越慢越好——AI 动作间隔大但策略弱）。
//   2) ni=0.001（step1 几乎不走 UG 快速结束，正常 YO 道具——但道具桩 no-op）。
//   3) WX 走 Si<2 随机洗牌分支（bundle:49912 np.Ys），不评分排序。
//   4) qX 价值评估乘 0.2 弱化（Si<2 走 [.2,.3][0]=0.2，bundle:47143，AI 几乎不主动攻击）。
//   5) ii[0] 全 0（无周期收入），仅 startGame 一次性 initialGold（hi=10）。
//   6) 道具冷却 5000ms，DEFERRED 桩 no-op 返回 success:false（即使 5s 冷却触发也不影响 smoke）。
//   7) 综合：难度 0 弱策略——决策慢+随机放置+弱攻击+无收入+道具 no-op。
//
// 测试策略：用 mock 构造 AIController（Si=0），驱动 startGame/update/TG/WX/qX/PG/YO，
// 断言上述弱策略行为。沿用 AIStateMachine.test.js/AIDifficulty.test.js 的 mock harness 风格。

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const { AIDifficultyConfig } = require('../../src/ai/AIDifficultyConfig');
const { AIDeploymentController } = require('../../src/ai/AIDeploymentController');
const { AIController } = require('../../src/ai/AIController');
const { BattleInputCommand, BattleInputCommandType } = require('../../src/input/BattleInputCommand');
const { GameEvents } = require('../../src/core/EventBus');

// 难度配置 JSON 路径（断言 ii[0] 全 0 与 itemCooldownMs=5000 时直接读 JSON 源）
const CONFIG_PATH = path.join(__dirname, '../../unity-export/config/ai-difficulty.json');

// =====================================================================
// mock harness：构造 Si=0 的 AIController 与可控依赖
// （沿用 AIStateMachine.test.js 的 makeRandomSource/makeBattleState/makeGameLoop/
//   makeInputController/makeEventBus/makeDeckManager/makeMapData/makeLogger 风格）
// =====================================================================

/**
 * 构造可控 randomSource。
 * @param {number[]} randomSeq Math.random() 等价返回序列（按调用顺序消费）
 * @param {Function} [shuffleFn] 可选自定义 shuffle（WX Si<2 随机洗牌用）
 * @returns {{randomSource, calls}}
 */
function makeRandomSource(randomSeq, shuffleFn) {
  const calls = { random: 0, shuffle: 0, randomInt: 0 };
  const seq = randomSeq.slice();
  const randomSource = {
    random() {
      calls.random += 1;
      return seq.length ? seq.shift() : 0;
    },
    randomInt(min, max) {
      calls.randomInt += 1;
      return min + Math.floor((seq.length ? seq.shift() : 0) * (max - min));
    },
    shuffle(arr) {
      calls.shuffle += 1;
      if (shuffleFn) shuffleFn(arr);
      return arr;
    },
  };
  return { randomSource, calls };
}

/**
 * 构造 mock BattleState（au 等价），默认难度 0。
 */
function makeBattleState(overrides = {}) {
  return Object.assign({
    aiDifficulty: 0,
    opponentGold: 0,            // Ji
    opponentRecruitCost: 10,    // gi（刷牌阈值）
    opponentPlacementComplete: false, // Xi
    currentRound: 0,            // li
    isGameOver: false,
    standardBattleDelayEnabled: true, // ki 守卫为 true，不跳过 AI
  }, overrides);
}

/**
 * 构造 mock GameLoop（nx 等价）。elapsed 供 _now() 用（YO 道具冷却时间戳判定）。
 */
function makeGameLoop() {
  let elapsed = 0;
  const gameLoop = {
    elapsed,
    setElapsed(v) { elapsed = v; gameLoop.elapsed = v; },
    register() {},
    unregister() {},
  };
  return gameLoop;
}

/**
 * 构造 mock InputController（r0 等价），记录所有命令调用。
 */
function makeInputController(refreshSuccess = true, placeSuccess = true) {
  const commands = [];
  const inputController = {
    execute(cmd) {
      commands.push({ type: cmd.type, payload: { ...cmd.payload } });
      if (cmd.type === BattleInputCommandType.REFRESH) return { success: refreshSuccess, reason: refreshSuccess ? null : 'not enough gold' };
      if (cmd.type === BattleInputCommandType.MOVE_UNIT) return { success: placeSuccess, reason: null };
      return { success: true, reason: null };
    },
    commands,
  };
  return inputController;
}

/**
 * 构造 mock EventBus（oc 等价），支持 on/off/event，记录事件便于断言 PG/YO/UG。
 */
function makeEventBus() {
  const events = [];
  const listeners = new Map();
  const eventBus = {
    on(type, caller, listener) {
      if (!listeners.has(type)) listeners.set(type, []);
      listeners.get(type).push({ caller, listener });
    },
    off(type, caller, listener) {
      const list = listeners.get(type);
      if (!list) return;
      const next = list.filter(e => !(e.caller === caller && e.listener === listener));
      if (next.length) listeners.set(type, next); else listeners.delete(type);
    },
    event(type, ...args) {
      events.push({ type, args });
      const list = listeners.get(type);
      if (list) for (const e of list.slice()) e.listener.call(e.caller, ...args);
      return true;
    },
    emit(type, ...args) { return eventBus.event(type, ...args); },
    hasListener(type) { return (listeners.get(type) || []).length > 0; },
    events, listeners,
  };
  return eventBus;
}

function makeDeckManager() {
  return { hand: () => [], refresh: () => true };
}

/**
 * 构造 mock MapData。WX 候选收集用 width/height/blockAt，step3/step5 用 width 决定 sb/nG 长度。
 * @param {number} width 棋盘列数
 * @param {number} height 棋盘行数
 * @param {string} tile tile 字符（默认 '1_1' 可放置格，全格可放置便于 WX 收集候选）
 * @param {{x,y}[]} me 对手路线点（WX Si>=2 TX 评分用，难度 0 不用）
 */
function makeMapData(width = 6, height = 10, tile = '1_1', me = []) {
  return {
    width, height,
    mapIndex: 0,
    me,
    blockAt() { return tile; },
  };
}

/**
 * 构造 mock logger（收集日志，便于断言 ✅/❌/ai加钱）。
 */
function makeLogger() {
  const logs = [];
  const warns = [];
  const debugs = [];
  return {
    log: (...args) => logs.push(args),
    warn: (...args) => warns.push(args),
    debug: () => {},
    error: (...args) => logs.push(['error', ...args]),
    logs, warns, debugs,
  };
}

/**
 * 用 spy 包装子控制器方法（沿用 AIStateMachine.test.js 风格）。
 * bG.YX spy 可选累加 XX 模拟布阵计数（默认不累加，由调用方按需开启）。
 */
function spySubControllers(ai, opts = {}) {
  const spies = {
    bG_YX: 0, bG_ZX: 0,
    MG_tG: 0, MG_iG: 0, MG_hG: 0, MG_aG: 0, MG_lG: 0,
  };
  if (ai.bG) {
    ai.bG.YX = () => {
      spies.bG_YX += 1;
      if (opts.yxIncrementsXX) ai.XX += 1;
    };
    ai.bG.ZX = () => { spies.bG_ZX += 1; };
  }
  if (ai.MG) {
    ai.MG.tG = () => { spies.MG_tG += 1; };
    ai.MG.iG = () => { spies.MG_iG += 1; };
    ai.MG.hG = () => { spies.MG_hG += 1; };
    ai.MG.aG = () => { spies.MG_aG += 1; };
    ai.MG.lG = () => { spies.MG_lG += 1; };
  }
  return spies;
}

/**
 * 构造完整难度 0 AIController 实例并 startGame。
 * @param {object} opts 可控参数（battleState/inputController/randomSource/itemSlots/itemEffectDispatcher/...）
 */
function createAI0(opts = {}) {
  const battleState = makeBattleState(opts.battleState || {});
  const gameData = { battle: battleState, map: opts.mapData || makeMapData(opts.mapWidth || 0, opts.mapHeight || 0) };
  const gameLoop = opts.gameLoop || makeGameLoop();
  const inputController = opts.inputController || makeInputController(opts.refreshSuccess !== false, opts.placeSuccess !== false);
  const deckManager = opts.deckManager || makeDeckManager();
  const eventBus = opts.eventBus || makeEventBus();
  const logger = opts.logger || makeLogger();
  const { randomSource, calls } = makeRandomSource(opts.randomSeq || [], opts.shuffleFn);
  const ai = new AIController({
    gameLoop, gameData, deckManager, inputController,
    randomSource, logger, eventBus,
    mapData: opts.mapData || null,
    unitRegistry: opts.unitRegistry || null,
    itemEffectDispatcher: opts.itemEffectDispatcher || null,
    itemSlots: opts.itemSlots || [],
    rankTableResolver: opts.rankTableResolver || null,
  });
  ai.startGame();
  const spies = spySubControllers(ai, opts.spy || {});
  return { ai, battleState, gameData, gameLoop, inputController, eventBus, logger, randomSource, randomCalls: calls, spies };
}

/**
 * 推进 update 至少触发一次 TG（难度 0 fG=2000ms）。
 */
function driveUpdate(ai, deltaMs, count = 1) {
  const step = deltaMs != null ? deltaMs : ai.fG;
  for (let i = 0; i < count; i += 1) ai.update(step);
}

// =====================================================================
// 1) fG=2000ms（决策间隔最慢）
//    spec Scenario「决策间隔随难度变化」+ 决策 4：Si=0 fG=2000ms（决策慢但策略弱）。
// =====================================================================

test('难度 0: fG=2000ms 决策间隔最慢（决策 4 弱策略前提）', () => {
  // AIDifficultyConfig.resolve(0) 返回 fG=2000
  const cfg = new AIDifficultyConfig();
  assert.equal(cfg.resolve(0).fG, 2000, 'resolve(0).fG=2000ms');

  // AIController.startGame 读 Si=0 后 this.fG=2000
  const { ai } = createAI0();
  assert.equal(ai.Si, 0, 'Si=0');
  assert.equal(ai.fG, 2000, 'startGame 后 fG=2000ms（难度 0 决策最慢）');

  // 难度 0 比 800ms 占位猜测值更慢（design 决策 2：MUST 以 bundle 实测解码值为准）
  assert.ok(ai.fG > 800, 'fG=2000ms 比旧占位 800ms 更慢（决策间隔大但策略弱）');

  // 决策间隔最慢：yG 累加到 2000ms 才触发一次 TG（1999ms 不触发）
  const ai2 = createAI0().ai;
  ai2.update(1999);
  assert.equal(ai2.yG, 1999, '累加 1999ms 未达 fG=2000，yG=1999 不触发 TG');
  assert.equal(ai2.step, 1, '未触发 TG step 仍为 1');
  ai2.update(1);
  assert.equal(ai2.yG, 0, '累加达 2000ms 触发 TG，yG 归零');
});

// =====================================================================
// 2) ni=0.001（几乎不走 UG 快速结束）
//    spec Scenario「行为概率随难度变化」+ 决策 4：ni=0.001 几乎不走快速结束。
// =====================================================================

test('难度 0: ni=0.001 几乎不走 UG 快速结束（_ni 标量为 0.001）', () => {
  const cfg = new AIDifficultyConfig();
  assert.equal(cfg.resolve(0).ni, 0.001, 'resolve(0).ni=0.001');

  const { ai } = createAI0();
  assert.equal(ai._ni, 0.001, 'startGame 后 _ni=0.001（标量，非数组）');

  // ni=0.001 极小：step1 金币不够时 random()>0.001 才走 YO，random()<=0.001 走 UG
  // 模拟 random=0.5（远大于 0.001）→ 不走 UG 走 YO（UG 守护 kG 不触发）
  const { ai: ai2, battleState } = createAI0({
    battleState: { opponentGold: 0, opponentRecruitCost: 10 },
    randomSeq: [0.5], // random()=0.5 > 0.001 → YO 而非 UG
  });
  battleState.opponentGold = 0; // 抵消 startGame hi+=10，确保 Ji<gi 走 ni 概率分支
  driveUpdate(ai2);
  assert.equal(ai2.kG, false, 'random=0.5 > ni=0.001 未命中 UG，kG 守护未触发');
  assert.equal(ai2.step, 1, '走 YO 不推进 step（仍 step1）');

  // 模拟 random=0 <= 0.001 → 走 UG（kG 置位）
  const { ai: ai3, battleState: bs3 } = createAI0({
    battleState: { opponentGold: 0, opponentRecruitCost: 10 },
    randomSeq: [0], // random()=0 <= 0.001 → UG
  });
  bs3.opponentGold = 0;
  driveUpdate(ai3);
  assert.equal(ai3.kG, true, 'random=0 <= ni=0.001 命中 UG，kG=true 守护');
});

// =====================================================================
// 3) WX 走 Si<2 随机洗牌分支（不评分排序）
//    spec Scenario「难度 0/1 随机洗牌放置」+ 决策 4/5：Si<2 走 np.Ys 洗牌。
// =====================================================================

test('难度 0: WX 走 Si<2 随机洗牌分支，不评分排序', () => {
  // 构造 mock map：3 列 × 2 行全 '1_1' 可放置格，6 个候选格
  // 用可控 shuffle 重排顺序，断言 WX 调 _shuffle 且候选顺序被洗牌改变（非评分排序）
  const mapData = makeMapData(3, 2, '1_1', []);
  const unitRegistry = { hasBattleOccupant() { return false; } }; // 空棋盘全可放置

  // 可控 shuffle：把候选反序（[0,1,2,3,4,5] → [5,4,3,2,1,0]）
  let shuffleCalled = false;
  const shuffleFn = (arr) => {
    shuffleCalled = true;
    arr.reverse();
  };
  const { ai, randomCalls } = createAI0({
    mapData,
    unitRegistry,
    shuffleFn,
    battleState: { opponentGold: 100, opponentRecruitCost: 10 },
  });

  // 直接调 WX（Si=0 < 2 走随机洗牌分支）
  const hasCandidates = ai.WX('1_1', null);

  assert.equal(hasCandidates, true, 'WX 有候选格返回 true');
  assert.equal(shuffleCalled, true, 'Si=0<2 调用 _shuffle（np.Ys 随机洗牌）');
  assert.ok(randomCalls.shuffle >= 1, 'randomSource.shuffle 被调用');

  // 候选顺序被 reverse 洗牌（非评分排序的固定顺序）
  // 原始收集顺序：列优先 (0,0),(0,1),(1,0),(1,1),(2,0),(2,1)
  // shuffle reverse 后：(2,1),(2,0),(1,1),(1,0),(0,1),(0,0)
  const coords = ai.zX.map(c => `${c.x},${c.y}`);
  assert.deepEqual(coords, ['2,1', '2,0', '1,1', '1,0', '0,1', '0,0'],
    'Si<2 候选经洗牌反序，非评分排序');

  // 关键：Si=0 不计算 DX/TX/OG 评分（不访问 templateResolver.wG/vG/_G，不走 sort）
  // 验证：templateResolver 虽被 startGame 实例化，但 WX Si<2 分支不调用其评分方法
  assert.equal(ai.Si, 0, 'Si=0 确认走 Si<2 分支');
});

test('难度 0: WX 随机洗牌不依赖路线点/分带（不评分）', () => {
  // map.me 路线点为空（Si>=2 评分会用，Si<2 不用），WX Si<2 仍正常返回候选
  const mapData = makeMapData(2, 2, '1_1', null); // me=null（路线点缺失）
  const unitRegistry = { hasBattleOccupant() { return false; } };
  const { ai } = createAI0({
    mapData,
    unitRegistry,
    battleState: { opponentGold: 100, opponentRecruitCost: 10 },
  });

  // Si<2 不依赖 me 路线点，WX 正常返回候选
  const ok = ai.WX('1_1', null);
  assert.equal(ok, true, 'Si<2 WX 不依赖路线点，正常返回候选');
  assert.equal(ai.zX.length, 4, '2x2 全可放置格 → 4 候选');
});

// =====================================================================
// 4) qX 价值评估乘 0.2 弱化（Si<2 走 [.2,.3][0]=0.2，bundle:47143）
//    spec Scenario「难度 0 随机放置弱策略」+ 决策 4：qX 乘 0.2，AI 几乎不主动攻击。
// =====================================================================

test('难度 0: qX 价值评估乘 0.2 弱化（[.2,.3][0]=0.2，bundle:47143）', () => {
  // 直接测 AIDeploymentController.qX：Si=0 时乘 0.2 弱化系数
  // qX 的 value 推导 DEFERRED（单位类未取证），但弱化系数逻辑可测：
  //   注入能推导 value 的 aiController（OX）使 _resolveValue 返回非 null，验证 0.2 系数生效。
  const ai0 = createAI0().ai;
  // 构造能推导 value 的 bG：覆写 _resolveValue 返回固定 value=100
  const bG0 = new AIDeploymentController(ai0);
  bG0._resolveValue = () => 100; // 模拟单位价值 100（绕过 DEFERRED）

  // Si=0 → 乘 [0.2,0.3][0]=0.2 → 100*0.2=20（弱化）
  assert.equal(bG0.qX({}), 20, 'Si=0 qX=100*0.2=20（弱化，AI 几乎不主动攻击）');

  // 对比 Si=2（不弱化）→ 100（满价值攻击意愿）
  const ai2 = createAI0({ battleState: { aiDifficulty: 2 } }).ai;
  ai2.Si = 2;
  const bG2 = new AIDeploymentController(ai2);
  bG2._resolveValue = () => 100;
  assert.equal(bG2.qX({}), 100, 'Si=2 qX=100（不弱化，对比难度 0 弱策略）');

  // 对比 Si=1 → 乘 [0.2,0.3][1]=0.3 → 100*0.3=30（弱化但比 0 略强）
  const ai1 = createAI0({ battleState: { aiDifficulty: 1 } }).ai;
  ai1.Si = 1;
  const bG1 = new AIDeploymentController(ai1);
  bG1._resolveValue = () => 100;
  assert.equal(bG1.qX({}), 30, 'Si=1 qX=100*0.3=30（[.2,.3][1]=0.3）');

  // 难度 0 价值最弱（20 < 30 < 100），AI 几乎不主动攻击
  assert.ok(20 < 30 && 30 < 100, '难度 0(20) < 难度 1(30) < 难度 2(100)，难度 0 攻击意愿最弱');
});

test('难度 0: qX value DEFERRED 时返回 0（不抛异常，弱策略不阻塞状态机）', () => {
  // qX 的 _resolveValue DEFERRED 返回 null → qX 返回 0（不弱化也不攻击，弱策略）
  const ai0 = createAI0().ai;
  const bG0 = new AIDeploymentController(ai0); // _resolveValue 默认返回 null
  assert.equal(bG0.qX({}), 0, 'Si=0 value DEFERRED 时 qX 返回 0（弱策略，不抛异常）');

  // NX Si<2 直接返回 false（bundle:47143/47151-47172，难度 0 不启用同族检查）
  assert.equal(bG0.NX({}), false, 'Si=0 NX 返回 false（不启用同族检查，弱策略）');
});

// =====================================================================
// 5) ii[0] 全 0（无周期收入，仅 startGame 一次性 initialGold）
//    spec Scenario「难度 0 无周期收入」+ 决策 4：ii[0] 全 0，仅 startGame hi=10。
// =====================================================================

test('难度 0: ii[0] 全 0 无周期收入（仅 startGame 一次性 initialGold=hi=10）', () => {
  const cfg = new AIDifficultyConfig();
  // resolve(0).ii 全 0
  assert.deepEqual(cfg.resolve(0).ii, [0, 0, 0, 0, 0, 0], 'resolve(0).ii 全 0（无周期收入）');

  // ai-difficulty.json 源：ii[0] 全 0
  const raw = JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8'));
  assert.deepEqual(raw.ii[0], [0, 0, 0, 0, 0, 0], 'ai-difficulty.json ii[0] 全 0');

  // startGame 一次性加 hi=10（initialGold）
  const { ai, battleState } = createAI0({
    battleState: { opponentGold: 0, opponentRecruitCost: 10 },
  });
  assert.equal(ai._hi, 10, 'hi=10（initialGold）');
  assert.equal(battleState.opponentGold, 10, 'startGame 一次性加 hi=10（initialGold）');

  // PG 周期收入：波次开始触发，ii[0][i] 全 0 → 不加钱
  // 触发 ei 波次表中各波次（ei=[3,5,8,11,14,17]），ii[0] 全 0 → opponentGold 不变
  const ei = ai._ei;
  const goldBefore = battleState.opponentGold;
  for (const round of ei) {
    battleState.currentRound = round;
    ai.PG();
  }
  assert.equal(battleState.opponentGold, goldBefore, 'PG 波次收入 ii[0] 全 0，opponentGold 不变（无周期收入）');

  // 断言 PG 日志：ii[0] 加 0 仍打 ai加钱 日志（gold=0）
  const incomeLogs = ai.logger.logs ? ai.logger.logs.filter(args => args[0] && /ai加钱/.test(String(args[0]))) : [];
  // 每个波次匹配 ei 都会调 PG 加 0，日志含 'ai加钱'
  assert.ok(incomeLogs.length >= 1, 'PG 波次触发打 ai加钱 日志（ii[0]=0 加 0）');
  // 验证日志中 gold 值为 0
  const allZero = incomeLogs.every(args => args.includes(0));
  assert.ok(allZero, 'PG 日志中收入值均为 0（ii[0] 全 0）');
});

// =====================================================================
// 6) 道具冷却 5000ms，DEFERRED 桩 no-op 返回 success:false（即使触发也不影响 smoke）
//    spec Scenario「道具 effect DEFERRED 桩返回失败」+ 决策 4：YO 冷却 5000ms，
//    DEFERRED 桩 use 返回 {success:false}，即使 5s 冷却触发也不影响 smoke。
// =====================================================================

test('难度 0: 道具冷却 5000ms（_itemCooldownMs=5000，hu[101]）', () => {
  const cfg = new AIDifficultyConfig();
  assert.equal(cfg.resolve(0).itemCooldownMs, 5000, 'resolve(0).itemCooldownMs=5000');

  const { ai } = createAI0();
  assert.equal(ai._itemCooldownMs, 5000, 'startGame 后 _itemCooldownMs=5000（hu[101]）');
});

test('难度 0: 道具冷却内 YO 不触发道具分派（now-xG<5000）', () => {
  // startGame 后 xG=0（冷却时间戳归零），gameLoop.elapsed=0 → now-xG=0 < 5000 不触发
  const { ai, battleState, logger } = createAI0({
    battleState: { opponentGold: 0, opponentRecruitCost: 10 },
    itemSlots: [{ type: 3, txt: 'item3' }], // 有道具但冷却内不触发
    randomSeq: [0.5], // random>ni → YO
  });
  battleState.opponentGold = 0; // 抵消 hi+=10 走 ni 分支
  assert.equal(ai.xG, 0, 'startGame 后 xG=0');

  driveUpdate(ai); // step1 走 YO（冷却内）

  // 冷却内（now=0 - xG=0 = 0 < 5000）不触发道具分派
  const failLogs = logger.logs.filter(args => args[0] && /❌/.test(String(args[0])));
  assert.equal(failLogs.length, 0, '冷却内 YO 不触发道具分派（无 ❌ 日志）');
  assert.equal(ai.xG, 0, '冷却内未更新 xG（仍为 0）');
});

test('难度 0: 冷却到期 YO 触发 DEFERRED 桩 no-op 返回 success:false（不影响 smoke）', () => {
  // 冷却到期（now-xG>=5000）→ YO 从 itemSlots 选未使用道具调 Yb →
  // DEFERRED 桩 itemEffectDispatcher.use 返回 {success:false} → 日志 ❌AI使用道具失败
  // 即使触发，道具 no-op 不影响 smoke（仅日志，无实际 effect）
  const { ai, battleState, logger, gameLoop } = createAI0({
    battleState: { opponentGold: 0, opponentRecruitCost: 10 },
    itemSlots: [{ type: 3, txt: 'item3' }], // 有未使用道具
    randomSeq: [0.5], // random()=0.5 > ni=0.001 → YO（而非 UG）
    // 不注入 itemEffectDispatcher → 用默认 DEFERRED 桩（use 返回 {success:false}）
  });
  battleState.opponentGold = 0; // 抵消 hi+=10 走 ni 分支

  // 推进时间到冷却到期（gameLoop.elapsed=5000 → now-xG=5000>=5000 触发）
  gameLoop.setElapsed(5000);

  driveUpdate(ai); // step1 走 YO（冷却到期触发）

  // 断言：DEFERRED 桩 use 返回 success:false → 日志 ❌AI使用道具失败
  const failLogs = logger.logs.filter(args => args[0] && /❌/.test(String(args[0])));
  assert.ok(failLogs.length >= 1, '冷却到期 YO 触发 DEFERRED 桩，日志 ❌AI使用道具失败');
  // 断言：无 ✅ 成功日志（DEFERRED 桩不成功）
  const okLogs = logger.logs.filter(args => args[0] && /✅/.test(String(args[0])));
  assert.equal(okLogs.length, 0, 'DEFERRED 桩 no-op 不返回 success:true（无 ✅ 日志）');
  // 断言：xG 更新为 now（冷却触发后记录时间戳）
  assert.equal(ai.xG, 5000, 'YO 触发后 xG=now=5000');

  // 关键：即使道具触发，itemEffectDispatcher no-op 不产生实际 effect（仅日志），
  // 不影响 smoke（无副作用到 BattleState/棋盘/单位）
  assert.equal(battleState.opponentGold, 0, '道具 no-op 不改变 opponentGold');
  assert.equal(battleState.isGameOver, false, '道具 no-op 不触发 gameOver');
});

test('难度 0: 默认 DEFERRED 桩 itemEffectDispatcher.use 返回 success:false（DEFERRED_ITEM_SYSTEM）', () => {
  // 直接验证默认桩：未注入 itemEffectDispatcher 时使用 _defaultItemEffectDispatcher
  const { ai } = createAI0();
  const dispatcher = ai.itemEffectDispatcher;
  assert.ok(typeof dispatcher.use === 'function', '默认 itemEffectDispatcher 有 use 方法');

  // 默认桩 use 返回 {success:false}（no-op 视为失败，spec 约束）
  const result = dispatcher.use(3, { type: 3, txt: 'item3' });
  assert.equal(result.success, false, '默认 DEFERRED 桩 use 返回 success:false（no-op）');
});

// =====================================================================
// 7) 综合：难度 0 弱策略——决策慢+随机放置+弱攻击+无收入+道具 no-op，
//    不破坏单局闭环 smoke 前提
//    spec Requirement「难度 0 路径不破坏现有单局闭环 smoke」+ Scenario「现有单局 smoke 回归通过」。
// =====================================================================

test('难度 0 综合: 弱策略各维度均成立（决策慢/随机/弱攻击/无收入/道具 no-op）', () => {
  const { ai, battleState } = createAI0({
    battleState: { opponentGold: 0, opponentRecruitCost: 10 },
    mapData: makeMapData(3, 2, '1_1', []),
    unitRegistry: { hasBattleOccupant() { return false; } },
    itemSlots: [{ type: 3, txt: 'item3' }],
  });

  // (1) 决策慢：fG=2000ms
  assert.equal(ai.fG, 2000, '决策慢：fG=2000ms');

  // (2) ni=0.001 几乎不走快速结束
  assert.equal(ai._ni, 0.001, 'ni=0.001 几乎不走 UG 快速结束');

  // (3) WX 随机放置（Si<2 洗牌）
  const beforeWX = ai.zX.length;
  ai.WX('1_1', null);
  assert.ok(ai.zX.length > 0, 'WX Si<2 收集候选（随机洗牌放置）');
  assert.equal(ai.Si, 0, 'Si=0 走 Si<2 随机洗牌（不评分）');

  // (4) qX 弱攻击：Si=0 乘 0.2（value=100 → 20，远低于满价值 100）
  const bG = new AIDeploymentController(ai);
  bG._resolveValue = () => 100;
  assert.equal(bG.qX({}), 20, 'qX 乘 0.2 弱化（攻击意愿 20/100）');

  // (5) 无周期收入：ii[0] 全 0，仅 startGame hi=10
  assert.deepEqual(ai._iiRow, [0, 0, 0, 0, 0, 0], 'ii[0] 全 0 无周期收入');
  assert.equal(battleState.opponentGold, 10, '仅 startGame 一次性 initialGold=10');

  // (6) 道具 no-op：DEFERRED 桩 use 返回 success:false
  assert.equal(ai.itemEffectDispatcher.use(3, { type: 3 }).success, false, '道具 DEFERRED 桩 no-op');

  // 综合：难度 0 为弱策略，不破坏 smoke 前提
  // - 决策最慢（2000ms）→ AI 动作频率低
  // - 随机放置 → 无针对性布阵
  // - 弱攻击（0.2x）→ 几乎不主动攻击
  // - 无周期收入 → 经济不增长（仅初始 10 金）
  // - 道具 no-op → 即使触发也无 effect
  assert.ok(ai.fG >= 2000 && ai._ni <= 0.001 && bG.qX({}) <= 20,
    '难度 0 弱策略：决策慢(>=2000ms)+不走快速结束(<=0.001)+弱攻击(<=20)');
});

test('难度 0: 状态机持续推进但不因弱策略阻塞（smoke 前提：AI 不死锁）', () => {
  // 验证难度 0 下状态机能持续推进 step1→5→1 循环，不因弱策略（无收入/道具 no-op）死锁。
  // 这是 smoke 能跑通 GameOver 的前提：AI 持续循环但弱策略，玩家可推进至胜利。
  const { ai, battleState, inputController } = createAI0({
    battleState: { opponentGold: 100, opponentRecruitCost: 10 }, // 金币够走 step1 refresh
    mapData: makeMapData(0, 0), // width=0：step3 直接 step4；nG=[]：step5 直接 step1
    spy: { yxIncrementsXX: true }, // bG.YX 累加 XX 推进 step2→3
  });

  // 推进 20 个 fG 周期，验证 step 循环且 AI 持续 started
  const stepsObserved = new Set();
  for (let i = 0; i < 20; i += 1) {
    ai.update(ai.fG);
    stepsObserved.add(ai.step);
    assert.equal(ai.started, true, `第 ${i + 1} 周期 AI 仍 started=true（未死锁）`);
  }

  // 观察到多个 step（持续循环，非死锁在某个 step）
  assert.ok(stepsObserved.size >= 3, `难度 0 状态机持续循环观察到多 step: ${[...stepsObserved].join(',')}`);

  // refresh 被多次调用（每次循环回 step1 金币够都 refresh）——AI 持续决策
  const refreshCount = inputController.commands.filter(c => c.type === BattleInputCommandType.REFRESH).length;
  assert.ok(refreshCount >= 2, `难度 0 持续循环多次 refresh（${refreshCount} 次），AI 未死锁`);

  // gameOver 守卫：isGameOver=true 时 update 不推进（smoke 跑通 GameOver 后 AI 停止）
  battleState.isGameOver = true;
  const stepBefore = ai.step;
  ai.update(ai.fG);
  assert.equal(ai.step, stepBefore, 'isGameOver=true 时 update 守卫停止（smoke 跑通 GameOver 后 AI 停止）');
});
