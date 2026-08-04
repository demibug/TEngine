'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { AIController } = require('../../src/ai/AIController');
const { BattleInputCommand, BattleInputCommandType } = require('../../src/input/BattleInputCommand');
const { GameEvents } = require('../../src/core/EventBus');

/**
 * 任务 7.1：5 步状态机用例（bundle:49819-49831 TG）。
 *
 * 覆盖场景：
 *   - step1 金币够（Ji>=gi）→ refresh + step2。
 *   - step1 金币不够 + ni 概率命中 → UG；否则 → YO。
 *   - step2 Xi=true + bG.YX + XX>=5 → step3。
 *   - step3 bG.ZX 遍历完（KX[0]>=sb.length）→ step4。
 *   - step4 rp.filter + MG.tG/iG/hG/aG 规划 → step5。
 *   - step5 MG.lG 遍历完（cG[0]>=nG.length）→ step1（循环）。
 *   - 持续循环至 gameOver 不停（不部署 N 个即停）。
 *   - gameOver 重置 step/yG/XX/KX/cG/sG/kG/SG。
 *
 * 测试策略：用 stub 依赖构造 AIController 实例，控制 randomSource 与 BattleState
 * 字段驱动 update(deltaMs) 累加到 fG 触发 TG，断言 step 推进与子控制器方法被调用。
 * 子控制器为真实实例（AIDeploymentController/AIPlanningController），用 spy 包装其方法
 * 断言调用契约；refresh/YO/UG 经 spy inputController/logger/eventBus 间接断言。
 *
 * 注：难度 0 fG=2000ms（bundle:49740 hu[118]→2000），ni=0.001。
 */

// ===== mock 工厂 =====

/**
 * 构造可控 randomSource。
 * @param {number[]} randomSeq Math.random() 等价返回序列（按调用顺序消费）
 * @param {Function} shuffleFn 可选自定义 shuffle
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
 * 构造 mock BattleState（au 等价）。
 * 默认难度 0，金币 0（< gi 触发 ni 概率分支）。
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
 * 构造 mock GameLoop（nx 等价）。
 * register/unregister 记录调用，elapsed 供 _now() 用。
 */
function makeGameLoop() {
  const registrations = [];
  const unregistrations = [];
  let elapsed = 0;
  const gameLoop = {
    elapsed,
    setElapsed(v) { elapsed = v; gameLoop.elapsed = v; },
    register(name, caller, fn) { registrations.push({ name, caller, fn }); },
    unregister(name) { unregistrations.push({ name }); },
    registrations, unregistrations,
  };
  return gameLoop;
}

/**
 * 构造 mock InputController（r0 等价）。
 * execute 返回可控结果，记录所有命令调用。
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
 * 构造 mock EventBus（oc 等价）。
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

/**
 * 构造 mock DeckManager（PA/hX 等价）。
 */
function makeDeckManager() {
  return { hand: () => [], refresh: () => true };
}

/**
 * 构造 mock MapData（uq.instance().map 等价）。
 * width/height 决定 step3 的 sb.length 与 step5 的 nG.length。
 * width=0 时 step3 直接进 step4；nG.length=0 时 step5 直接回 step1。
 */
function makeMapData(width = 0, height = 0) {
  return {
    width, height,
    mapIndex: 0,
    me: [],
    blockAt(x, y) { return null; },
  };
}

/**
 * 构造 mock logger（收集日志，便于断言 ✅/❌/warn）。
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
 * 用 spy 包装子控制器方法，记录调用次数。
 * 替换 AIController 实例的 bG/MG 方法为计数 spy。
 * bG.YX spy 可选累加 XX 模拟布阵计数（默认不累加，由调用方按需开启）。
 */
function spySubControllers(ai, opts = {}) {
  const spies = {
    bG_YX: 0, bG_ZX: 0,
    MG_tG: 0, MG_iG: 0, MG_hG: 0, MG_aG: 0, MG_lG: 0,
  };
  if (ai.bG) {
    // YX 每次调用可选累加 XX（模拟 bundle bG.YX 部署后递增布阵计数），供循环测试自动推进 step2→3
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
 * 构造完整 AIController 实例并 startGame。
 * @param {object} opts 可控参数（battleState/inputController/randomSource/refreshSuccess/itemSlots 等）
 */
function createAI(opts = {}) {
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
 * 推进 update 至少触发一次 TG（fG=2000ms 难度 0）。
 * @param {object} ai AIController 实例
 * @param {number} [deltaMs] 单次 update 增量，默认 fG（必触发一次）
 * @param {number} [count] 调用次数，默认 1
 */
function driveUpdate(ai, deltaMs, count = 1) {
  const step = deltaMs != null ? deltaMs : ai.fG;
  for (let i = 0; i < count; i += 1) ai.update(step);
}

// ===== 测试用例 =====

test('step1 金币够（Ji>=gi）→ refresh + step2', () => {
  // 难度 0，金币 100 >= gi=10 → refresh 主动刷牌，XX=0，step=2
  const { ai, battleState, inputController } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    refreshSuccess: true,
  });
  // 初始 step=1
  assert.equal(ai.step, 1, '初始 step=1');
  assert.equal(ai.XX, 0, '初始 XX=0');

  // 推进一个 fG 周期（2000ms）触发 TG
  driveUpdate(ai);

  // 断言：调了 refresh（REFRESH 命令），XX=0，step=2
  const refreshCalls = inputController.commands.filter(c => c.type === BattleInputCommandType.REFRESH);
  assert.equal(refreshCalls.length, 1, 'step1 金币够调 refresh（type:2 REFRESH）');
  assert.equal(refreshCalls[0].payload.side, false, 'refresh side=false（AI 侧）');
  assert.equal(ai.XX, 0, 'step1 刷新后 XX=0');
  assert.equal(ai.step, 2, 'step1 刷新后进 step2');
});

test('step1 金币不够 + ni 概率命中（random<=ni）→ UG 快速结束分支', () => {
  // 难度 0 ni=0.001，random=0 <= 0.001 命中 → UG
  // 注：startGame 会 Ji += hi(10)，故 startGame 后手动置 opponentGold=0 模拟"金币不够刷牌"
  const { ai, battleState, eventBus } = createAI({
    battleState: { opponentGold: 0, opponentRecruitCost: 10, aiDifficulty: 0 },
    randomSeq: [0], // random()=0 <= 0.001 命中 UG
  });
  battleState.opponentGold = 0; // 抵消 startGame 的 hi+=10，确保 Ji<gi 走 ni 分支
  assert.equal(ai.step, 1, '初始 step=1');
  assert.equal(ai.kG, false, '初始 kG=false');

  // UG 置 kG=true 守护，并尝试经 WX 取候选发 'At' 事件（mapData width=0 无候选，WX 返回 false）
  driveUpdate(ai);

  // 断言：UG 触发（kG=true 守护置位）；step 仍为 1（UG 后 void return 不进 step2）
  assert.equal(ai.kG, true, 'UG 触发置 kG=true 守护');
  assert.equal(ai.step, 1, 'UG 后 void return 不推进 step');
  // 无候选格时 'At' 事件不发
  const atEvents = eventBus.events.filter(e => e.type === 'At');
  assert.equal(atEvents.length, 0, 'mapData 无候选格时 UG 不发 At 事件');
});

test('step1 金币不够 + ni 未命中（random>ni）→ YO 道具分支', () => {
  // 难度 0 ni=0.001，random=0.5 > 0.001 未命中 → YO
  // YO 冷却 5000ms，xG=0（startGame 归零），now=gameLoop.elapsed=0 → now-xG=0 < 5000 不触发道具
  const { ai, battleState, logger } = createAI({
    battleState: { opponentGold: 0, opponentRecruitCost: 10, aiDifficulty: 0 },
    randomSeq: [0.5], // random()=0.5 > 0.001 未命中 UG → YO
  });
  battleState.opponentGold = 0; // 抵消 startGame 的 hi+=10，确保 Ji<gi 走 ni 分支
  assert.equal(ai.step, 1, '初始 step=1');
  assert.equal(ai.xG, 0, '初始 xG=0（冷却时间戳归零）');

  driveUpdate(ai);

  // 断言：走 YO 分支（冷却内不触发道具，step 不推进，仍为 1）
  assert.equal(ai.step, 1, 'YO 后不推进 step（仍 step1）');
  // YO 冷却内（now=0 - xG=0 = 0 < 5000）不触发道具调用
  const failLogs = logger.logs.filter(args => args[0] && /❌/.test(String(args[0])));
  assert.equal(failLogs.length, 0, '冷却内 YO 不触发道具分派');
});

test('step2 Xi=true + bG.YX 调用 + XX>=5 → step3', () => {
  // 构造进 step2 的条件：金币够走 step1→refresh→step2，然后手动设 XX>=5 模拟 5 次布阵
  const { ai, battleState, inputController, spies } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
  });
  // 第一次 update：step1 → refresh + step2（Xi 在 step2 分支设，下一次 TG 才执行）
  driveUpdate(ai);
  assert.equal(ai.step, 2, 'step1 后进 step2');
  // 手动设 XX>=5 模拟 bG.YX 已累计 5 次（bG.YX 为 DEFERRED 桩不增 XX，故手动模拟状态机推进条件）
  ai.XX = 5;

  // 第二次 update：step2 分支执行 → 置 Xi=true + 调 bG.YX() + XX>=5 → 清 rp/KX + step3
  driveUpdate(ai);

  // 断言 Xi=true（opponentPlacementComplete，bundle:49825 Xi=true 标记布阵已开始）
  assert.equal(battleState.opponentPlacementComplete, true, 'step2 置 Xi=true（布阵已开始）');
  // 断言：bG.YX 被调用（step2 调部署子控制器）
  assert.equal(spies.bG_YX, 1, 'step2 调 bG.YX 部署子控制器');
  // 断言：XX>=5 进 step3，rp 清空，KX 归零
  assert.equal(ai.step, 3, 'step2 XX>=5 进 step3');
  assert.equal(ai.rp.length, 0, 'step2 进 step3 清 rp');
  assert.equal(ai.KX[0], 0, 'step2 进 step3 KX[0]=0');
  assert.equal(ai.KX[1], 0, 'step2 进 step3 KX[1]=0');
});

test('step3 KX[0] < sb.length → bG.ZX 调用；遍历完 → step4', () => {
  // 构造 step3 场景：mapWidth>0 使 sb.length>0，KX[0]<sb.length 时调 bG.ZX
  // 用 mapWidth=1（sb.length=1），KX[0]=0<1 → 调 bG.ZX（DEFERRED 不增 KX，需手动推进模拟遍历）
  const { ai, spies } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    mapData: makeMapData(1, 4), // width=1 → sb.length=1
  });
  // 推进到 step2
  driveUpdate(ai);
  ai.XX = 5;
  driveUpdate(ai);
  assert.equal(ai.step, 3, '进 step3');
  assert.equal(ai.KX[0], 0, 'step3 初始 KX[0]=0');

  // 第三次 update：step3 KX[0]=0 < sb.length=1 → 调 bG.ZX
  driveUpdate(ai);
  assert.equal(spies.bG_ZX, 1, 'step3 KX[0]<sb.length 调 bG.ZX 棋盘扫描');
  // bG.ZX 为 DEFERRED 桩不增 KX，手动设 KX[0]>=sb.length 模拟遍历完
  ai.KX[0] = 1; // >= sb.length=1

  // 第四次 update：step3 KX[0]>=sb.length → step4
  driveUpdate(ai);
  assert.equal(ai.step, 4, 'step3 遍历完进 step4');
});

test('step4 rp.filter + MG.tG/iG/hG/aG 规划 → step5', () => {
  // 构造 step4 场景：直接手动设 step=4，断言 MG 规划方法被调用 + step5
  const { ai, battleState, spies } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    mapData: makeMapData(0, 0), // width=0：step3 直接进 step4
  });
  // rp 存一些 id，uG(id) 缺 unitRegistry 返回 null → filter 后 rp 为空（验证 filter 调用）
  ai.rp = ['u1', 'u2', 'u3'];
  ai.step = 4; // 直接置 step4
  ai.nG = []; // nG 已在 startGame 初始化

  driveUpdate(ai);

  // 断言：rp 经 filter 过滤（无 unitRegistry 全部返回 null → rp 清空）
  assert.equal(ai.rp.length, 0, 'step4 rp.filter 过滤存活单位（无 unitRegistry 全 null）');
  // 断言：MG.tG/iG/hG/aG 均被调用
  assert.equal(spies.MG_tG, 1, 'step4 调 MG.tG 目标选择');
  assert.equal(spies.MG_iG, 1, 'step4 调 MG.iG 攻击决策');
  assert.equal(spies.MG_hG, 1, 'step4 调 MG.hG 特殊行为');
  assert.equal(spies.MG_aG, 1, 'step4 调 MG.aG 清理准备');
  // 断言：cG 归零，进 step5
  assert.equal(ai.cG[0], 0, 'step4 cG[0]=0');
  assert.equal(ai.cG[1], 0, 'step4 cG[1]=0');
  assert.equal(ai.step, 5, 'step4 进 step5');
});

test('step5 cG[0] < nG.length → MG.lG 调用；遍历完 → step1', () => {
  // 构造 step5 场景：nG.length>0 使 cG[0]<nG.length 时调 MG.lG
  const { ai, spies } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    mapData: makeMapData(2, 4), // width=2 → nG.length=2
  });
  ai.step = 5;
  ai.cG = [0, 0];
  assert.equal(ai.nG.length, 2, 'nG.length=2（width=2）');

  // 第一次 update：step5 cG[0]=0 < nG.length=2 → 调 MG.lG
  driveUpdate(ai);
  assert.equal(spies.MG_lG, 1, 'step5 cG[0]<nG.length 调 MG.lG 落子');
  // MG.lG DEFERRED 不增 cG，手动设 cG[0]>=nG.length 模拟遍历完
  ai.cG[0] = 2; // >= nG.length=2

  // 第二次 update：step5 cG[0]>=nG.length → 回 step1
  driveUpdate(ai);
  assert.equal(ai.step, 1, 'step5 遍历完回 step1（循环）');
});

test('状态机持续循环至 gameOver 不停（不部署 N 个即停）', () => {
  // 验证：AI 不会因部署若干单位置 opponentPlacementComplete=true 停止 update。
  // 构造完整循环：mapWidth=0（step3 直接进 step4）、nG=[]（step5 直接回 step1），
  // bG.YX spy 每次累加 XX 模拟布阵计数，使 step2 在第 5 次 YX 后自动进 step3。
  // 多次推进 update 应持续循环 step1→2→3→4→5→1，update 持续被调用不停止。
  const { ai, battleState, inputController } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    mapData: makeMapData(0, 0), // width=0：step3 直接 step4；nG=[]：step5 直接 step1
    spy: { yxIncrementsXX: true }, // bG.YX 累加 XX，模拟布阵计数推进 step2→3
  });

  // 推进 30 个 fG 周期，验证 step 在 1..5 间循环且不停止
  const stepsObserved = new Set();
  for (let i = 0; i < 30; i += 1) {
    ai.update(ai.fG);
    stepsObserved.add(ai.step);
    // 每次 update 后 started 仍 true（未停）
    assert.equal(ai.started, true, `第 ${i + 1} 次后 AI 仍 started=true 未停止`);
  }

  // 断言：观察到了多个 step（证明持续循环，非停在某个 step）
  assert.ok(stepsObserved.size >= 3, `持续循环观察到多个 step: ${[...stepsObserved].join(',')}`);
  // 断言：opponentPlacementComplete 在 step2 被置 true 后不被 update 重置为 false（不以此为停止信号）
  assert.equal(battleState.opponentPlacementComplete, true, 'opponentPlacementComplete=true 仅标记布阵已开始，非停止');
  // 断言：refresh 被多次调用（每次循环回 step1 金币够都 refresh）
  const refreshCount = inputController.commands.filter(c => c.type === BattleInputCommandType.REFRESH).length;
  assert.ok(refreshCount >= 3, `持续循环多次调 refresh（${refreshCount} 次），非部署一次即停`);

  // 触发 gameOver 后 update 不再推进（isGameOver 守卫）
  battleState.isGameOver = true;
  const stepBefore = ai.step;
  ai.update(ai.fG);
  assert.equal(ai.step, stepBefore, 'isGameOver=true 时 update 守卫 return 不推进');
});

test('gameOver 重置 step/yG/XX/KX/cG/sG/kG/SG 并注销 update', () => {
  // 构造运行中的状态机，手动污染状态后调 gameOver 验证重置
  const { ai, battleState, gameLoop } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    mapData: makeMapData(2, 4),
  });
  // 推进状态机至非初始状态
  ai.XX = 5;
  ai.update(ai.fG); // step1→2
  ai.update(ai.fG); // step2→3
  // 手动污染待重置字段
  ai.step = 4;
  ai.yG = 999;
  ai.XX = 7;
  ai.KX = [3, 5];
  ai.cG = [2, 4];
  ai.sG = ['a', 'b'];
  ai.kG = true;
  ai.SG = true;
  assert.equal(ai.started, true, 'gameOver 前 started=true');

  // 调 gameOver
  ai.gameOver();

  // 断言：状态机字段全部重置（bundle:49785）
  assert.equal(ai.step, 1, 'gameOver 重置 step=1');
  assert.equal(ai.yG, 0, 'gameOver 重置 yG=0');
  assert.equal(ai.XX, 0, 'gameOver 重置 XX=0');
  assert.equal(ai.KX[0], 0, 'gameOver 重置 KX[0]=0');
  assert.equal(ai.KX[1], 0, 'gameOver 重置 KX[1]=0');
  assert.equal(ai.cG[0], 0, 'gameOver 重置 cG[0]=0');
  assert.equal(ai.cG[1], 0, 'gameOver 重置 cG[1]=0');
  assert.equal(ai.sG.length, 0, 'gameOver 清空 sG');
  assert.equal(ai.kG, false, 'gameOver 重置 kG=false');
  assert.equal(ai.SG, false, 'gameOver 重置 SG=false');
  assert.equal(ai.started, false, 'gameOver 置 started=false');
  // 断言：注销 update 注册（gameLoop.unregister）
  const unreg = gameLoop.unregistrations.filter(u => u.name === 'AIController');
  assert.equal(unreg.length, 1, 'gameOver 注销 AIController 的 update 注册');
});
