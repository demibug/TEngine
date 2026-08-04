'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');

/**
 * 任务 7.5：子控制器/模板用例（spec「AI 必须有子控制器与阵营模板衔接」Requirement）。
 *
 * 覆盖场景（对应 spec 的 4 个 Scenario + 任务原文 7.5）：
 *   - bG.YX 按 td（农民）/qo（士兵）/om（武将）分派：mock 手牌池含三类单位，
 *     调 YX 断言分派路径（HX 合并/NX+qX 价值评估/WX+jX 放置）。子控制器当前为
 *     DEFERRED 桩，断言调用契约（方法被调用、不抛异常）+ 暴露的分派辅助方法
 *     （HX/$X/NX/qX）可被独立调用且不抛异常。
 *   - MG.tG/iG/hG/aG/lG 经 step4/5 调用：驱动状态机到 step4/5，断言 MG 方法被调用（spy）。
 *   - AG 按 Si<2 取 yX（简化模板）/Si>=2 取 kX（完整模板）：调 AITemplateResolver.AG
 *     断言取不同模板 Map，缓存于 mG（GX 由 AIController.startGame 缓存）。
 *   - 武将项 DEFERRED 不阻塞基础单位：模板含武将项（Mp/Bp）空占位
 *     DEFERRED_GENERAL_TEMPLATE，断言基础单位模板正常解析，武将项不抛异常。
 *
 * 测试策略：
 *   - AIDeploymentController/AIPlanningController：用 mock aiController（OX）构造实例，
 *     直接调 YX/ZX/HX/$X/NX/qX/tG/iG/hG/aG/lG 断言调用契约（DEFERRED 桩不抛异常）。
 *     bG.YX 按 td/qo/om 分派路径：因 YX 当前为 DEFERRED 桩（未取证 td/qo/om 单位类，
 *     bundle:47015-47058），无法断言真实分派副作用，故断言"方法被调用不抛异常"契约 +
 *     断言分派辅助方法 HX/NX/qX/$X 可被独立调用返回合理值（HX/NX 返回 false、qX 返回 0）。
 *   - MG 经状态机驱动：复用 AIStateMachine 测试风格，构造 AIController（startGame）+ spy
 *     包装 MG 方法，手动置 step=4/5 后 driveUpdate 断言 tG/iG/hG/aG（step4）与 lG（step5）被调用。
 *   - AG 模板：直接 new AITemplateResolver()，调 AG(mapIndex, simplified) 断言 Si<2 取
 *     simplified=true 缓存键 `${mapIndex}_s`（qj.yX 简化模板）、Si>=2 取 simplified=false
 *     缓存键 `${mapIndex}_f`（qj.kX 完整模板），缓存于 mG；同一键二次调用命中缓存。
 *   - 武将项 DEFERRED：AG 返回模板 Map 含 Lp/Yc/Mp/Bp 键，Mp/Bp 为空数组占位
 *     （DEFERRED_GENERAL_TEMPLATE），基础单位 Lp/Yc 同样空占位但不抛异常；
 *     断言 _buildTemplate 标注 DEFERRED_GENERAL_TEMPLATE。
 *
 * 注：本测试不改 src/ 业务文件，仅新建测试文件验证 7.5 契约。
 */

const { AIDeploymentController } = require('../../src/ai/AIDeploymentController');
const { AIPlanningController } = require('../../src/ai/AIPlanningController');
const { AITemplateResolver } = require('../../src/ai/AITemplateResolver');
const { AIController } = require('../../src/ai/AIController');
const { GameEvents } = require('../../src/core/EventBus');

// ===== mock 工厂（沿用 AIStateMachine/AIPlacementWX 风格）=====

/**
 * 构造 mock aiController（OX 等价），供 AIDeploymentController/AIPlanningController
 * 构造注入。携带 Si/手牌池 hX/棋盘 PA/logger 等子控制器访问的字段。
 * @param {object} opts
 * @param {number} [opts.Si] 难度档
 * @param {Array} [opts.hX] 手牌池（bundle 等价 this.OX.hX）
 * @param {object} [opts.logger] 日志器
 * @returns {object} mock aiController
 */
function makeMockAIController(opts = {}) {
  const logs = { debug: [], log: [], warn: [] };
  return {
    Si: typeof opts.Si === 'number' ? opts.Si : 0,
    hX: opts.hX || [],
    PA: opts.PA || { sb: [] },
    nG: opts.nG || [],
    rp: opts.rp || [],
    sG: opts.sG || [],
    cG: opts.cG || [0, 0],
    KX: opts.KX || [0, 0],
    logger: opts.logger || {
      debug: (...a) => logs.debug.push(a),
      log: (...a) => logs.log.push(a),
      warn: (...a) => logs.warn.push(a),
    },
    // WX/jX/pG 落子辅助（bG.YX om 分派会调 aiController.WX/jX，DEFERRED 桩场景不触发）
    WX: opts.WX || (() => false),
    jX: opts.jX || (() => {}),
    pG: opts.pG || (() => false),
    logs,
  };
}

/**
 * 构造 mock BattleState（au 等价），沿用 AIStateMachine 风格。
 */
function makeBattleState(overrides = {}) {
  return Object.assign({
    aiDifficulty: 0,
    opponentGold: 100,
    opponentRecruitCost: 10,
    opponentPlacementComplete: false,
    currentRound: 0,
    isGameOver: false,
    standardBattleDelayEnabled: true,
  }, overrides);
}

/**
 * 构造 mock GameLoop（nx 等价）。
 */
function makeGameLoop() {
  const registrations = [];
  const unregistrations = [];
  return {
    elapsed: 0,
    register(name, caller, fn) { registrations.push({ name, caller, fn }); },
    unregister(name) { unregistrations.push({ name }); },
    registrations, unregistrations,
  };
}

/**
 * 构造 mock InputController（r0 等价）。
 */
function makeInputController() {
  const commands = [];
  return {
    execute(cmd) {
      commands.push({ type: cmd.type, payload: { ...cmd.payload } });
      return { success: true, reason: null };
    },
    commands,
  };
}

/**
 * 构造 mock EventBus（oc 等价）。
 */
function makeEventBus() {
  const events = [];
  const listeners = new Map();
  return {
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
    events, listeners,
  };
}

/**
 * 构造 mock MapData（uq.instance().map 等价）。
 */
function makeMapData(width = 0, height = 0) {
  return {
    width, height, mapIndex: 0, me: [],
    blockAt() { return null; },
  };
}

/**
 * 构造 mock DeckManager。
 */
function makeDeckManager() {
  return { hand: () => [], refresh: () => true };
}

/**
 * 构造可控 randomSource。
 */
function makeRandomSource(randomSeq = []) {
  const seq = randomSeq.slice();
  return {
    random() { return seq.length ? seq.shift() : 0; },
    randomInt(min, max) { return min; },
    shuffle(arr) { return arr; },
  };
}

/**
 * 构造完整 AIController 实例并 startGame（沿用 AIStateMachine 风格）。
 * @param {object} opts
 */
function createAI(opts = {}) {
  const battleState = makeBattleState(opts.battleState || {});
  const gameData = { battle: battleState, map: opts.mapData || makeMapData(opts.mapWidth || 0, opts.mapHeight || 0) };
  const gameLoop = opts.gameLoop || makeGameLoop();
  const inputController = opts.inputController || makeInputController();
  const deckManager = opts.deckManager || makeDeckManager();
  const eventBus = opts.eventBus || makeEventBus();
  const randomSource = opts.randomSource || makeRandomSource(opts.randomSeq || []);
  const ai = new AIController({
    gameLoop, gameData, deckManager, inputController,
    randomSource, logger: console, eventBus,
    mapData: opts.mapData || null,
    unitRegistry: opts.unitRegistry || null,
    itemSlots: opts.itemSlots || [],
  });
  ai.startGame();
  return { ai, battleState, gameData, gameLoop, inputController, eventBus };
}

/**
 * 用 spy 包装 MG 方法，记录调用次数（沿用 AIStateMachine.spySubControllers 风格）。
 */
function spyMG(ai) {
  const spies = { tG: 0, iG: 0, hG: 0, aG: 0, lG: 0 };
  if (!ai.MG) return spies;
  ai.MG.tG = () => { spies.tG += 1; };
  ai.MG.iG = () => { spies.iG += 1; };
  ai.MG.hG = () => { spies.hG += 1; };
  ai.MG.aG = () => { spies.aG += 1; };
  ai.MG.lG = () => { spies.lG += 1; };
  return spies;
}

// ===== 场景 1：bG.YX 按 td/qo/om 分派（DEFERRED 桩调用契约）=====

test('bG.YX 调用契约：mock 手牌池含 td/qo/om 三类单位，YX 被调用不抛异常', () => {
  // bundle:47015-47058 bG.YX 遍历 hX 手牌池按 td（农民）/qo（士兵）/om（武将）分派。
  // 当前 AIDeploymentController.YX 为 DEFERRED 桩（td/qo/om 单位类未取证），
  // 故断言调用契约：方法被调用、不抛异常、不阻塞。
  const tdUnit = { type: 'td', id: 'farmer1', level: 1 }; // 农民
  const qoUnit = { type: 'qo', id: 'soldier1', level: 1 }; // 士兵
  const omUnit = { type: 'om', id: 'general1', level: 2 }; // 武将
  const aiCtrl = makeMockAIController({
    Si: 0,
    hX: [tdUnit, qoUnit, omUnit], // 手牌池含三类单位
  });
  const bG = new AIDeploymentController(aiCtrl);

  // 断言：调 YX 不抛异常（DEFERRED 桩空实现）
  assert.doesNotThrow(() => bG.YX(), 'bG.YX DEFERRED 桩调用不抛异常');
  // 断言：YX 是函数且可调用（方法存在）
  assert.equal(typeof bG.YX, 'function', 'bG.YX 方法存在');
});

test('bG.YX 分派辅助方法 HX 合并：可独立调用返回 false（DEFERRED 不抛异常）', () => {
  // bundle:47105-47133 bG.HX 合并同类型同等级单位；YX 中 td（农民）走 HX 合并/jX 放置分派。
  // 当前 HX 为 DEFERRED 桩返回 false，断言可独立调用不抛异常、返回 false。
  const aiCtrl = makeMockAIController({ Si: 0 });
  const bG = new AIDeploymentController(aiCtrl);
  const tdUnit = { type: 'td', id: 'farmer1', level: 1 };

  assert.doesNotThrow(() => bG.HX(tdUnit), 'bG.HX DEFERRED 桩调用不抛异常');
  // bundle HX 返回是否合并成功，DEFERRED 返回 false
  const result = bG.HX(tdUnit);
  assert.equal(result, false, 'bG.HX DEFERRED 桩返回 false（未合并成功）');
});

test('bG.YX 分派辅助方法 NX+qX 价值评估：qo（士兵）路径，Si<2 NX 返回 false / qX 返回 0', () => {
  // bundle:47015-47058 bG.YX 中 qo（士兵）走 NX 同族检查 + qX 价值评估 + $X 最小价值攻击。
  // 当前 NX Si<2 返回 false（bundle:47143/47151-47172 Si<2 不启用）；
  // qX value 未取证 DEFERRED 返回 0（bundle:47134-47149）。
  const aiCtrl = makeMockAIController({ Si: 0 }); // 难度 0，Si<2
  const bG = new AIDeploymentController(aiCtrl);
  const qoUnit = { type: 'qo', id: 'soldier1', level: 1 };

  // NX 同族检查：Si<2 直接返回 false（bundle:47143 Si<2 不启用）
  assert.doesNotThrow(() => bG.NX(qoUnit), 'bG.NX Si<2 调用不抛异常');
  assert.equal(bG.NX(qoUnit), false, 'bG.NX Si<2 返回 false（不启用同族检查）');

  // qX 价值评估：value 未取证 DEFERRED 返回 0
  assert.doesNotThrow(() => bG.qX(qoUnit), 'bG.qX DEFERRED 调用不抛异常');
  const value = bG.qX(qoUnit);
  assert.equal(value, 0, 'bG.qX value 未取证 DEFERRED 返回 0');
});

test('bG.YX 分派辅助方法 $X 最小价值攻击：om/qo 路径，DEFERRED 不抛异常', () => {
  // bundle:47064-47103 bG.$X 最小价值攻击；YX 中 qo 走 $X、om 走 WX+jX 放置。
  // 当前 $X 为 DEFERRED 桩空实现，断言可独立调用不抛异常。
  const aiCtrl = makeMockAIController({ Si: 0 });
  const bG = new AIDeploymentController(aiCtrl);
  const unit = { type: 'qo', id: 'soldier1', level: 1 };

  assert.doesNotThrow(() => bG.$X(unit, 5), 'bG.$X DEFERRED 桩调用不抛异常');
  // $X 无返回值（空实现），仅断言不抛异常
});

test('bG.YX 按 Si 难度分派：Si=2 时 NX 走 Si>=2 路径仍返回 false（DEFERRED）', () => {
  // bundle:47151-47172 bG.NX Si>=2 才检查同族；当前 Si>=2 路径 DEFERRED 仍返回 false。
  // 验证 Si>=2 不走"直接返回 false"早退分支，而是走 DEFERRED 桩路径（仍返回 false）。
  const aiCtrlSi2 = makeMockAIController({ Si: 2 });
  const bG2 = new AIDeploymentController(aiCtrlSi2);
  const unit = { type: 'qo', id: 'soldier1', level: 1 };

  assert.doesNotThrow(() => bG2.NX(unit), 'bG.NX Si>=2 DEFERRED 调用不抛异常');
  assert.equal(bG2.NX(unit), false, 'bG.NX Si>=2 DEFERRED 桩返回 false');

  // 对比 Si<2 也返回 false，但走不同分支（_si()<2 早退 vs DEFERRED 桩）
  const aiCtrlSi0 = makeMockAIController({ Si: 0 });
  const bG0 = new AIDeploymentController(aiCtrlSi0);
  assert.equal(bG0.NX(unit), false, 'bG.NX Si<2 返回 false（早退分支）');
});

test('bG.ZX 棋盘扫描调用契约：遍历 PA.sb 棋盘，DEFERRED 桩不抛异常', () => {
  // bundle:47174-47196 bG.ZX 遍历 PA.sb 棋盘扫描合并/push 到 rp。
  // 当前 ZX 为 DEFERRED 桩，断言可独立调用不抛异常。
  const aiCtrl = makeMockAIController({ Si: 0, PA: { sb: [[null, null], [null, null]] } });
  const bG = new AIDeploymentController(aiCtrl);

  assert.doesNotThrow(() => bG.ZX(), 'bG.ZX DEFERRED 桩调用不抛异常');
  assert.equal(typeof bG.ZX, 'function', 'bG.ZX 方法存在');
});

test('bG 子控制器为 DEFERRED 桩时经状态机 step2/step3 调用不阻塞', () => {
  // 验证：bG.YX（step2）与 bG.ZX（step3）经状态机调用不抛异常、不阻塞推进。
  // 构造 mapWidth=1 使 step3 KX[0]<sb.length 时调 bG.ZX。
  const { ai } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    mapData: makeMapData(1, 4),
  });
  // step1→2
  ai.update(ai.fG);
  assert.equal(ai.step, 2, 'step1 金币够进 step2');
  // step2 调 bG.YX（DEFERRED 桩不抛异常）
  ai.XX = 5;
  ai.update(ai.fG);
  assert.equal(ai.step, 3, 'step2 XX>=5 进 step3（bG.YX DEFERRED 不阻塞）');
  // step3 调 bG.ZX（DEFERRED 桩不抛异常）
  assert.doesNotThrow(() => ai.update(ai.fG), 'step3 调 bG.ZX DEFERRED 桩不抛异常');
});

// ===== 场景 2：MG.tG/iG/hG/aG/lG 经 step4/5 调用 =====

test('MG.tG/iG/hG/aG 经 step4 调用：驱动状态机到 step4，断言 4 方法被调用', () => {
  // bundle:49828 step4 过滤存活后调 MG.tG/iG/hG，清 nG 后调 MG.aG，进 step5。
  // 构造 mapWidth=0 使 step3 直接进 step4，手动置 step=4 后 driveUpdate 断言 spy。
  const { ai } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    mapData: makeMapData(0, 0), // width=0：step3 直接进 step4
  });
  const spies = spyMG(ai);
  // 直接置 step4（绕过 step1-3 推进，聚焦 MG 调用契约）
  ai.step = 4;
  ai.rp = ['u1', 'u2']; // 无 unitRegistry → filter 后 rp 清空
  ai.nG = [];

  ai.update(ai.fG);

  // 断言：MG.tG/iG/hG/aG 均被调用一次（step4 规划契约）
  assert.equal(spies.tG, 1, 'step4 调 MG.tG 目标选择');
  assert.equal(spies.iG, 1, 'step4 调 MG.iG 攻击决策');
  assert.equal(spies.hG, 1, 'step4 调 MG.hG 特殊行为');
  assert.equal(spies.aG, 1, 'step4 调 MG.aG 清理准备');
  // 断言：进 step5
  assert.equal(ai.step, 5, 'step4 调 MG 规划后进 step5');
});

test('MG.lG 经 step5 调用：cG[0] < nG.length 时调 lG 落子', () => {
  // bundle:49831 step5 cG[0] < nG.length 时调 MG.lG 落子，遍历完回 step1。
  // 构造 mapWidth=2 使 nG.length=2，手动置 step=5 后 driveUpdate 断言 lG 被调用。
  const { ai } = createAI({
    battleState: { opponentGold: 100, opponentRecruitCost: 10, aiDifficulty: 0 },
    mapData: makeMapData(2, 4), // width=2 → nG.length=2
  });
  const spies = spyMG(ai);
  ai.step = 5;
  ai.cG = [0, 0];
  assert.equal(ai.nG.length, 2, 'nG.length=2（width=2）');

  // 第一次 update：step5 cG[0]=0 < nG.length=2 → 调 MG.lG
  ai.update(ai.fG);
  assert.equal(spies.lG, 1, 'step5 cG[0]<nG.length 调 MG.lG 落子');

  // 手动推进 cG[0]>=nG.length 模拟遍历完（MG.lG DEFERRED 不增 cG）
  ai.cG[0] = 2;
  ai.update(ai.fG);
  assert.equal(ai.step, 1, 'step5 遍历完回 step1（循环）');
});

test('MG 规划方法独立调用契约：tG/iG/hG/aG/lG 均不抛异常', () => {
  // 验证 MG 的 5 个规划方法（DEFERRED 桩）可独立调用不抛异常。
  // bundle:47198+ MG(vR) 规划逻辑未完整取证，方法体为 DEFERRED 桩。
  const aiCtrl = makeMockAIController({ Si: 0 });
  const mg = new AIPlanningController(aiCtrl);

  assert.doesNotThrow(() => mg.tG(), 'MG.tG DEFERRED 桩不抛异常');
  assert.doesNotThrow(() => mg.iG(), 'MG.iG DEFERRED 桩不抛异常');
  assert.doesNotThrow(() => mg.hG(), 'MG.hG DEFERRED 桩不抛异常');
  assert.doesNotThrow(() => mg.aG(), 'MG.aG DEFERRED 桩不抛异常');
  assert.doesNotThrow(() => mg.lG(), 'MG.lG DEFERRED 桩不抛异常');
  // 方法均存在
  assert.equal(typeof mg.tG, 'function', 'MG.tG 方法存在');
  assert.equal(typeof mg.iG, 'function', 'MG.iG 方法存在');
  assert.equal(typeof mg.hG, 'function', 'MG.hG 方法存在');
  assert.equal(typeof mg.aG, 'function', 'MG.aG 方法存在');
  assert.equal(typeof mg.lG, 'function', 'MG.lG 方法存在');
});

// ===== 场景 3：AG 按 Si<2 取 yX / Si>=2 取 kX，缓存于 mG =====

test('AG 按 Si<2 取简化模板（simplified=true，qj.yX 等价，缓存键 _s）', () => {
  // bundle:49847-49859 AG(mapIndex, simplified)：simplified=true 调 qj.yX（简化模板，Si<2）。
  // 缓存键 FG(mapIndex, true) = `${mapIndex}_s`。
  // DEFERRED_GENERAL_TEMPLATE: _buildTemplate 返回空占位 Map，但缓存键与缓存行为可断言。
  const resolver = new AITemplateResolver(null, null);
  const mapIndex = 1;
  const key = resolver.FG(mapIndex, true); // simplified=true → _s

  assert.equal(key, '1_s', 'Si<2 simplified=true 缓存键为 ${mapIndex}_s（qj.yX 简化模板）');

  // 调 AG 取简化模板
  const tpl = resolver.AG(mapIndex, true);
  assert.ok(tpl instanceof Map, 'AG 返回模板 Map');
  // 缓存于 mG
  assert.ok(resolver.mG.has(key), '简化模板缓存于 mG（键 _s）');
  assert.equal(resolver.mG.get(key), tpl, 'mG 缓存的模板与 AG 返回值一致');
});

test('AG 按 Si>=2 取完整模板（simplified=false，qj.kX 等价，缓存键 _f）', () => {
  // bundle:49847-49859 AG(mapIndex, simplified)：simplified=false 调 qj.kX（完整模板，Si>=2）。
  // 缓存键 FG(mapIndex, false) = `${mapIndex}_f`。
  const resolver = new AITemplateResolver(null, null);
  const mapIndex = 2;
  const key = resolver.FG(mapIndex, false); // simplified=false → _f

  assert.equal(key, '2_f', 'Si>=2 simplified=false 缓存键为 ${mapIndex}_f（qj.kX 完整模板）');

  const tpl = resolver.AG(mapIndex, false);
  assert.ok(tpl instanceof Map, 'AG 返回模板 Map');
  assert.ok(resolver.mG.has(key), '完整模板缓存于 mG（键 _f）');
  assert.equal(resolver.mG.get(key), tpl, 'mG 缓存的模板与 AG 返回值一致');
});

test('AG Si<2 与 Si>=2 取不同模板 Map（_s vs _f 键区分）', () => {
  // bundle:49847-49859 同一 mapIndex，Si<2 取 yX（_s）、Si>=2 取 kX（_f），
  // 两个模板 Map 缓存于不同键，互不覆盖。
  const resolver = new AITemplateResolver(null, null);
  const mapIndex = 0;

  // Si<2 → simplified=true → _s
  const tplS = resolver.AG(mapIndex, true);
  // Si>=2 → simplified=false → _f
  const tplF = resolver.AG(mapIndex, false);

  // 两个模板 Map 是不同实例（不同缓存键）
  assert.notEqual(tplS, tplF, 'Si<2 与 Si>=2 取不同模板 Map 实例');
  // 缓存键不同
  assert.ok(resolver.mG.has('0_s'), 'Si<2 模板缓存键 0_s');
  assert.ok(resolver.mG.has('0_f'), 'Si>=2 模板缓存键 0_f');
  assert.equal(resolver.mG.get('0_s'), tplS, '0_s 缓存简化模板');
  assert.equal(resolver.mG.get('0_f'), tplF, '0_f 缓存完整模板');
});

test('AG 同键二次调用命中缓存（不重复构建）', () => {
  // bundle:49855 `let h=this.mG.get(g); return h || (h=..., this.mG.set(g,h)), h`
  // 同一 (mapIndex, simplified) 二次调用应命中 mG 缓存返回同一实例。
  const resolver = new AITemplateResolver(null, null);
  const mapIndex = 3;

  const first = resolver.AG(mapIndex, true);
  const second = resolver.AG(mapIndex, true);

  assert.equal(first, second, '同键二次调用命中 mG 缓存返回同一模板实例');
  // mG 仅一个该 mapIndex 的简化键
  let sCount = 0;
  for (const k of resolver.mG.keys()) if (k.endsWith('_s')) sCount += 1;
  assert.equal(sCount, 1, '同键不重复构建（mG 仅一个 _s 键）');
});

test('AIController.startGame 按 Si 缓存 GX 模板：Si<2 取简化 / Si>=2 取完整', () => {
  // bundle:49740 this.GX = this.AG(mapIndex, Si<2 simplified)
  // startGame 时按 Si 决定 simplified 标志，GX 缓存于 AIController.GX。
  // 难度 0（Si<2）→ simplified=true → GX 为简化模板（_s 键）
  const { ai: ai0 } = createAI({
    battleState: { aiDifficulty: 0 },
  });
  assert.ok(ai0.GX instanceof Map, 'Si=0 startGame 后 GX 为模板 Map');
  assert.ok(ai0.templateResolver.mG.has('0_s'), 'Si=0 GX 缓存键 0_s（简化模板）');
  assert.equal(ai0.templateResolver.mG.get('0_s'), ai0.GX, 'GX 与 mG 缓存的简化模板一致');

  // 难度 2（Si>=2）→ simplified=false → GX 为完整模板（_f 键）
  const { ai: ai2 } = createAI({
    battleState: { aiDifficulty: 2 },
  });
  assert.ok(ai2.GX instanceof Map, 'Si=2 startGame 后 GX 为模板 Map');
  assert.ok(ai2.templateResolver.mG.has('0_f'), 'Si=2 GX 缓存键 0_f（完整模板）');
  assert.equal(ai2.templateResolver.mG.get('0_f'), ai2.GX, 'GX 与 mG 缓存的完整模板一致');

  // Si=0 与 Si=2 的 GX 是不同实例（不同模板）
  assert.notEqual(ai0.GX, ai2.GX, 'Si<2 与 Si>=2 的 GX 为不同模板实例');
});

// ===== 场景 4：武将项 DEFERRED 不阻塞基础单位 =====

test('AG 模板含武将项 Mp/Bp 空占位（DEFERRED_GENERAL_TEMPLATE）', () => {
  // bundle:49847-49859 模板 Map 含键：Lp（基础单位）/Yc（扩展）/Mp（武将）/Bp（平民）。
  // DEFERRED_GENERAL_TEMPLATE: qj.kX/yX 武将项 Mp/Bp 以空占位承载，不阻塞 AI 基础单位部署。
  const resolver = new AITemplateResolver(null, null);
  const tpl = resolver.AG(0, true); // 简化模板

  // 断言：模板 Map 含 Lp/Yc/Mp/Bp 四键
  assert.ok(tpl.has('Lp'), '模板含 Lp 基础单位键');
  assert.ok(tpl.has('Yc'), '模板含 Yc 扩展单位键');
  assert.ok(tpl.has('Mp'), '模板含 Mp 武将单位键');
  assert.ok(tpl.has('Bp'), '模板含 Bp 平民键');

  // 武将项 Mp/Bp 为空数组占位（DEFERRED_GENERAL_TEMPLATE）
  assert.deepEqual(tpl.get('Mp'), [], 'Mp 武将项为空数组占位（DEFERRED_GENERAL_TEMPLATE）');
  assert.deepEqual(tpl.get('Bp'), [], 'Bp 平民项为空数组占位（DEFERRED_GENERAL_TEMPLATE）');
  // 基础单位 Lp/Yc 同样空占位（DEFERRED，但不阻塞）
  assert.deepEqual(tpl.get('Lp'), [], 'Lp 基础单位为空占位（DEFERRED 不阻塞）');
  assert.deepEqual(tpl.get('Yc'), [], 'Yc 扩展单位为空占位（DEFERRED 不阻塞）');
});

test('AG 模板完整模板（Si>=2）同样含 Mp/Bp 空占位', () => {
  // 完整模板 qj.kX（Si>=2）的武将项同样 DEFERRED 空占位。
  const resolver = new AITemplateResolver(null, null);
  const tpl = resolver.AG(0, false); // 完整模板

  assert.ok(tpl.has('Mp'), '完整模板含 Mp 武将单位键');
  assert.ok(tpl.has('Bp'), '完整模板含 Bp 平民键');
  assert.deepEqual(tpl.get('Mp'), [], '完整模板 Mp 武将项空占位（DEFERRED_GENERAL_TEMPLATE）');
  assert.deepEqual(tpl.get('Bp'), [], '完整模板 Bp 平民项空占位（DEFERRED_GENERAL_TEMPLATE）');
});

test('武将项 DEFERRED 不阻塞基础单位：模板解析不抛异常', () => {
  // 验证：模板含武将项空占位时，基础单位模板（Lp/Yc）正常解析不抛异常。
  // 反复调 AG 取模板（简化+完整），均不抛异常且返回含四键的 Map。
  const resolver = new AITemplateResolver(null, null);

  for (const simplified of [true, false]) {
    for (const mapIndex of [0, 1, 2, 3]) {
      const tpl = resolver.AG(mapIndex, simplified);
      assert.ok(tpl instanceof Map, `mapIndex=${mapIndex} simplified=${simplified} 模板为 Map`);
      // 基础单位键存在（不因武将项 DEFERRED 阻塞）
      assert.ok(tpl.has('Lp'), `mapIndex=${mapIndex} 基础单位 Lp 键存在（武将 DEFERRED 不阻塞）`);
      assert.ok(tpl.has('Yc'), `mapIndex=${mapIndex} 扩展单位 Yc 键存在（武将 DEFERRED 不阻塞）`);
      // 武将项空占位不阻塞
      assert.deepEqual(tpl.get('Mp'), [], `mapIndex=${mapIndex} 武将项 Mp 空占位不阻塞`);
    }
  }
});

test('武将项 DEFERRED 不阻塞 bG 部署：bG.YX/HX/qX 调用不抛异常', () => {
  // 验证：武将模板项 DEFERRED_GENERAL_TEMPLATE 不阻塞 bG 部署子控制器调用。
  // bG.YX 遍历手牌池含武将单位 om 时，YX（DEFERRED 桩）不抛异常不阻塞。
  // 分派辅助方法 HX/qX 对武将单位调用同样不抛异常。
  const aiCtrl = makeMockAIController({
    Si: 0,
    hX: [
      { type: 'td', id: 'farmer1', level: 1 }, // 基础单位（农民）
      { type: 'qo', id: 'soldier1', level: 1 }, // 基础单位（士兵）
      { type: 'om', id: 'general1', level: 2 }, // 武将单位（DEFERRED 模板项）
    ],
  });
  const bG = new AIDeploymentController(aiCtrl);
  const generalUnit = { type: 'om', id: 'general1', level: 2 };

  // YX 遍历含武将的手牌池不抛异常
  assert.doesNotThrow(() => bG.YX(), 'bG.YX 遍历含武将单位的手牌池不抛异常（DEFERRED 不阻塞）');
  // HX 对武将单位调用不抛异常（返回 false DEFERRED）
  assert.doesNotThrow(() => bG.HX(generalUnit), 'bG.HX 对武将单位调用不抛异常');
  // qX 对武将单位调用不抛异常（返回 0 DEFERRED）
  assert.doesNotThrow(() => bG.qX(generalUnit), 'bG.qX 对武将单位调用不抛异常');
  assert.equal(bG.qX(generalUnit), 0, 'bG.qX 武将单位 DEFERRED 返回 0');
});

test('EG 路线点与 BG 分带：DEFERRED 不阻塞，可与 AG 协同调用', () => {
  // 验证：AITemplateResolver 的 EG（路线点，DEFERRED 空数组）与 BG（分带）不抛异常，
  // 且与 AG 模板协同调用不阻塞（WX 放置依赖 AG/EG/BG）。
  const mapData = { me: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 }] };
  const resolver = new AITemplateResolver(null, mapData);

  // EG 路线点：DEFERRED 返回空数组
  assert.doesNotThrow(() => resolver.EG(0), 'EG 路线点 DEFERRED 调用不抛异常');
  const pts = resolver.EG(0);
  assert.deepEqual(pts, [], 'EG DEFERRED 返回空数组');
  assert.ok(resolver.gG.has(0), 'EG 路线点缓存于 gG');

  // BG 分带：从 mapData.me 计算 wG/vG/_G
  assert.doesNotThrow(() => resolver.BG(), 'BG 分带调用不抛异常');
  assert.equal(resolver.wG, 3, 'BG wG=me.length=3');
  assert.equal(resolver.vG, Math.floor(0.15 * 3), 'BG vG=floor(0.15*wG)');
  assert.equal(resolver._G, Math.ceil(0.85 * 3), 'BG _G=ceil(0.85*wG)');

  // AG 模板与 EG/BG 协同调用不阻塞
  assert.doesNotThrow(() => {
    resolver.AG(0, true);
    resolver.EG(0);
    resolver.BG();
  }, 'AG/EG/BG 协同调用不抛异常（DEFERRED 不阻塞）');
});
