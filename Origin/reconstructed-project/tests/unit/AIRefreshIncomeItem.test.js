'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');

const { AIController } = require('../../src/ai/AIController');
const { EventBus, GameEvents } = require('../../src/core/EventBus');
const { BattleState } = require('../../src/battle/BattleState');
const { BattleInputCommand, BattleInputCommandType } = require('../../src/input/BattleInputCommand');

/**
 * 任务 7.3：刷牌/收入/道具用例
 * 覆盖 spec「AI 必须主动刷牌与周期收入」+「AI 必须有道具使用调用契约」Requirement 的全部 Scenario：
 *   - step1 主动 refresh（type:2 AI 侧刷新）：金币够时调 refresh，触发
 *     inputController.execute(REFRESH, {side:false})，失败 warn `AI 刷新失败`。
 *   - PG 波次收入：eventBus emit WAVE_STARTED（等价 ROUND_SPAWN_PREPARED/Jt）
 *     → PG 回调按 ei 波次表 + ii[Si][i] 加钱（au.opponentGold 增加），日志含 `ai加钱`。
 *   - YO 冷却：now - xG >= 5000ms（hu[101]）时从 itemSlots 过滤未使用道具随机选一个调 Yb；
 *     冷却内不触发；空栏不触发。
 *   - Yb type 分派：按 item.type 分派到 itemEffectDispatcher.use(type, item)，
 *     成功日志 `✅AI成功使用道具`、失败 `❌AI使用道具失败`。
 *   - DEFERRED 桩：默认 itemEffectDispatcher.use 返回 {success:false}，日志 `❌AI使用道具失败`。
 *
 * 事件映射说明：spec 中「WAVE_STARTED」等价 bundle `sS.Jt`，src 实际常量为
 * GameEvents.ROUND_SPAWN_PREPARED='Jt'，AIController.startGame 订阅此事件触发 PG。
 * 故测试经 eventBus.event(GameEvents.ROUND_SPAWN_PREPARED) 驱动 PG。
 *
 * 测试策略：用 mock 构造 AIController（mock inputController/eventBus/itemSlots/
 * itemEffectDispatcher/randomSource/时间），驱动 refresh/PG/YO/Yb，断言调用、日志与金币变化。
 */

// ---- 测试夹具：构造一个最小可运行的 AIController mock 体系 ----

/**
 * 构建测试依赖集合。所有外部接口以 mock 承载，便于断言调用与状态。
 * @param {object} [opts] 覆盖项
 * @returns {object} { ai, deps } ai 为 AIController 实例，deps 为各 mock 句柄
 */
function buildHarness(opts = {}) {
  const eventBus = new EventBus();
  const battle = new BattleState(eventBus);
  // 难度/金币/刷牌阈值默认值（可在 opts 覆盖）
  battle.aiDifficulty = opts.Si != null ? opts.Si : 0;
  battle.opponentGold = opts.opponentGold != null ? opts.opponentGold : 0;
  battle.opponentRecruitCost = opts.opponentRecruitCost != null ? opts.opponentRecruitCost : 10;
  battle.currentRound = opts.currentRound != null ? opts.currentRound : 0;
  battle.standardBattleDelayEnabled = true;
  battle.isGameOver = false;
  const gameData = { battle };

  // gameLoop mock：elapsed 控制时间戳（_now 用），register/unregister no-op
  const gameLoop = {
    elapsed: opts.elapsed != null ? opts.elapsed : 0,
    registered: [], unregistered: [],
    register(name, caller, fn) { this.registered.push({ name, caller, fn }); },
    unregister(name) { this.unregistered.push(name); },
  };

  const deckManager = { hand() { return []; }, refresh() {} };

  // inputController mock：execute 返回可配置结果，并记录调用
  const inputCalls = [];
  const inputController = {
    execute(cmd) {
      inputCalls.push(cmd);
      return opts.inputResult != null ? opts.inputResult : { success: true };
    },
  };

  // logger mock：收集所有日志便于断言
  const logs = [];
  const logger = {
    log() { logs.push([...arguments].join(' ')); },
    warn() { logs.push('warn ' + [...arguments].join(' ')); },
    debug() { /* 静默 */ },
    error() { logs.push('error ' + [...arguments].join(' ')); },
  };

  // 道具分派器 mock（可注入）
  const dispatcherUseCalls = [];
  const itemEffectDispatcher = opts.itemEffectDispatcher || {
    use(type, item) {
      dispatcherUseCalls.push({ type, item });
      return opts.dispatcherResult != null ? opts.dispatcherResult : { success: true };
    },
  };

  // 道具栏（vb.KP 等价）
  const itemSlots = opts.itemSlots != null ? opts.itemSlots : [];

  // randomSource mock：可控制随机序列（randomInt 选道具用）
  const randomSeq = opts.randomSeq != null ? opts.randomSeq.slice() : [];
  const randomSource = {
    random() {
      // 返回 randomSeq 中首个 random 值，默认 0.5
      return randomSeq.length ? randomSeq.shift() : 0.5;
    },
    randomInt(min, max) {
      // 返回 randomSeq 中首个 randomInt 值（作为 idx），默认 0
      return randomSeq.length ? (randomSeq.shift() | 0) : 0;
    },
    shuffle(arr) { /* no-op，测试不依赖洗牌顺序 */ return arr; },
  };

  const ai = new AIController({
    gameLoop, gameData, deckManager, inputController,
    randomSource, logger, eventBus,
    itemEffectDispatcher, itemSlots,
  });

  return {
    ai, battle, eventBus, gameLoop, inputController, inputCalls,
    logger, logs, itemEffectDispatcher, dispatcherUseCalls, itemSlots, randomSource,
  };
}

// ===== step1 主动 refresh（type:2 AI 侧刷新）=====

test('step1 金币够则调 refresh，inputController 收到 REFRESH({side:false}) 指令', () => {
  // opponentGold=20 >= opponentRecruitCost=10 → step1 走 refresh 分支
  const h = buildHarness({ Si: 0, opponentGold: 20, opponentRecruitCost: 10 });
  h.ai.startGame();

  // 直接驱动状态机 TG：step1 Ji>=gi → refresh + XX=0 + step=2
  h.ai.TG(0);

  // 断言 refresh 被调用：inputController.execute 收到 REFRESH 指令，side=false
  const refreshCalls = h.inputCalls.filter(c => c.type === BattleInputCommandType.REFRESH);
  assert.equal(refreshCalls.length, 1, '调一次 refresh（type:2 AI 侧刷新）');
  assert.equal(refreshCalls[0].payload.side, false, 'REFRESH payload.side=false（nm=false 对手侧）');

  // 状态机推进到 step2，XX 归零
  assert.equal(h.ai.step, 2, 'refresh 后进入 step2');
  assert.equal(h.ai.XX, 0, 'XX 归零');
});

test('refresh 失败时 warn `AI 刷新失败`', () => {
  // inputController 返回 success:false 触发 warn
  const h = buildHarness({
    Si: 0, opponentGold: 20, opponentRecruitCost: 10,
    inputResult: { success: false, reason: '金币不足' },
  });
  h.ai.startGame();

  // 直接调 refresh 验证 warn 日志（避免 step1 分支条件耦合）
  const result = h.ai.refresh();
  assert.equal(result.success, false, 'refresh 返回失败');
  const warnLog = h.logs.find(l => l.startsWith('warn') && l.includes('AI 刷新失败'));
  assert.ok(warnLog, '失败时 warn `AI 刷新失败`');
});

test('step1 金币不够不调 refresh', () => {
  // startGame 会加 hi=10（bundle:49740），故 opponentGold=5 + 10 = 15。
  // 设 opponentRecruitCost=100 使 15 < 100 → 不走 refresh 分支。
  // 难度 0 的 ni=0.001，randomSource.random 默认 0.5 > 0.001 → 走 YO 分支
  // （空栏不触发 YO，便于隔离只断言 refresh 未被调）。
  const h = buildHarness({
    Si: 0, opponentGold: 5, opponentRecruitCost: 100,
    itemSlots: [], // 空栏，YO 不触发，便于隔离
  });
  h.ai.startGame();
  assert.ok(h.battle.opponentGold < h.battle.opponentRecruitCost, 'startGame 后金币仍 < 刷牌阈值');

  h.ai.TG(0);

  const refreshCalls = h.inputCalls.filter(c => c.type === BattleInputCommandType.REFRESH);
  assert.equal(refreshCalls.length, 0, '金币不够不调 refresh');
});

// ===== PG 波次收入（ai加钱 日志）=====

test('PG 波次收入：eventBus emit ROUND_SPAWN_PREPARED → PG 按 ei 波次表 + ii[Si][i] 加钱，日志含 ai加钱', () => {
  // 难度 2：ii[2]=[10,10,...]，ei=[3,5,8,11,14,17]
  // currentRound=3 匹配 ei[0]=3 → 加 ii[2][0]=10
  const h = buildHarness({ Si: 2, opponentGold: 0, currentRound: 3 });
  h.ai.startGame();

  // startGame 已加 hi=10：0 + 10 = 10
  const goldBeforeEvent = h.battle.opponentGold;
  assert.equal(goldBeforeEvent, 10, 'startGame 加 hi=10 后 opponentGold=10');

  // 经事件触发 PG（WAVE_STARTED 等价 ROUND_SPAWN_PREPARED）
  h.eventBus.event(GameEvents.ROUND_SPAWN_PREPARED);

  // PG 加 ii[2][0]=10 → 10 + 10 = 20
  assert.equal(h.battle.opponentGold, 20, 'PG 按 ii[Si][i] 加钱（Si=2,round=3,+10）');
  const incomeLog = h.logs.find(l => l.includes('ai加钱'));
  assert.ok(incomeLog, '日志含 `ai加钱`');
  assert.ok(incomeLog.includes('10'), 'ai加钱 日志含金额 10');
});

test('PG 难度 0 无周期收入（ii[0] 全 0）', () => {
  // 难度 0：ii[0]=[0,0,...]，即使波次匹配也不加钱
  const h = buildHarness({ Si: 0, opponentGold: 0, currentRound: 3 });
  h.ai.startGame();

  const goldBefore = h.battle.opponentGold;
  h.eventBus.event(GameEvents.ROUND_SPAWN_PREPARED);

  assert.equal(h.battle.opponentGold, goldBefore, 'ii[0] 全 0，PG 不加钱');
  // 难度 0 波次匹配时日志仍含 ai加钱 但金额为 0（bundle:50048 仍 log）
  const incomeLog = h.logs.find(l => l.includes('ai加钱'));
  assert.ok(incomeLog, '难度 0 波次匹配仍打 ai加钱 日志（金额 0）');
});

test('PG 难度 3 每波 +20（hu[1]=20）', () => {
  // 难度 3：ii[3]=[20,20,...]，currentRound=8 匹配 ei[2]=8 → +20
  const h = buildHarness({ Si: 3, opponentGold: 0, currentRound: 8 });
  h.ai.startGame();

  h.eventBus.event(GameEvents.ROUND_SPAWN_PREPARED);

  // startGame 加 hi=10，PG 加 ii[3][2]=20 → 10 + 20 = 30
  assert.equal(h.battle.opponentGold, 30, '难度 3 PG 加 ii[3][2]=20（hu[1]=20）');
  const incomeLog = h.logs.find(l => l.includes('ai加钱'));
  assert.ok(incomeLog.includes('20'), 'ai加钱 日志含 20');
});

test('PG 波次不匹配 ei 表不加钱', () => {
  // currentRound=4 不在 ei=[3,5,8,11,14,17] → 不加钱
  const h = buildHarness({ Si: 2, opponentGold: 0, currentRound: 4 });
  h.ai.startGame();

  const goldBefore = h.battle.opponentGold;
  h.eventBus.event(GameEvents.ROUND_SPAWN_PREPARED);

  assert.equal(h.battle.opponentGold, goldBefore, '波次不匹配 ei 表 PG 不加钱');
});

test('PG 经 eventBus.on 订阅自动触发（startGame 已注册回调）', () => {
  // 验证 startGame 时已订阅 ROUND_SPAWN_PREPARED → PG
  const h = buildHarness({ Si: 2, opponentGold: 0, currentRound: 5 });
  h.ai.startGame();

  // 发事件后金币应变化（证明订阅生效，非手动调 PG）
  h.eventBus.event(GameEvents.ROUND_SPAWN_PREPARED);
  // ei[1]=5 匹配 → ii[2][1]=10；startGame hi=10 + 10 = 20
  assert.equal(h.battle.opponentGold, 20, 'PG 经 eventBus 订阅自动触发');
});

// ===== YO 冷却检查（hu[101]=5000ms）=====

test('YO 冷却内不触发（now - xG < 5000ms）', () => {
  const h = buildHarness({
    Si: 0, opponentGold: 0, elapsed: 1000,
    itemSlots: [{ type: 3, txt: '道具A', _used: false }],
    // dispatcherResult 成功，便于区分"未调用"与"调用失败"
    dispatcherResult: { success: true },
  });
  h.ai.startGame();
  // startGame 后 xG=0，now=elapsed=1000 → 1000-0=1000 < 5000 冷却内
  assert.equal(h.ai.xG, 0, 'startGame 后 xG=0');
  assert.equal(h.ai._now(), 1000, 'now=1000ms');

  h.ai.YO();

  assert.equal(h.dispatcherUseCalls.length, 0, '冷却内 YO 不调 Yb/use');
  assert.equal(h.ai.xG, 0, '冷却内 xG 不更新');
});

test('YO 冷却满足时触发（now - xG >= 5000ms）：从 itemSlots 过滤未使用道具随机选一个调 Yb，更新 xG=now', () => {
  const h = buildHarness({
    Si: 0, opponentGold: 0, elapsed: 5001,
    itemSlots: [
      { type: 3, txt: '道具A', _used: false },
      { type: 6, txt: '道具B', _used: false },
    ],
    dispatcherResult: { success: true },
    // randomInt 返回 0 → 选 unused[0]（道具A）
    randomSeq: [0],
  });
  h.ai.startGame();
  // now=5001, xG=0 → 5001-0=5001 >= 5000 冷却满足
  assert.equal(h.ai._now(), 5001, 'now=5001ms');

  h.ai.YO();

  assert.equal(h.dispatcherUseCalls.length, 1, '冷却满足调一次 Yb→use');
  assert.equal(h.dispatcherUseCalls[0].type, 3, '按 item.type=3 分派');
  assert.equal(h.dispatcherUseCalls[0].item.txt, '道具A', '随机选未使用道具（randomInt=0 → 道具A）');
  assert.equal(h.ai.xG, 5001, '触发后更新 xG=now');
});

test('YO 过滤已使用道具（_used=true 不选）', () => {
  const h = buildHarness({
    Si: 0, opponentGold: 0, elapsed: 5001,
    itemSlots: [
      { type: 3, txt: '已用', _used: true },  // 已使用，过滤
      { type: 6, txt: '可用', _used: false }, // 未使用，应被选
    ],
    dispatcherResult: { success: true },
    randomSeq: [0], // unused=[道具B]，randomInt=0 → 道具B
  });
  h.ai.startGame();

  h.ai.YO();

  assert.equal(h.dispatcherUseCalls.length, 1, '过滤已使用后仍选一个未使用道具');
  assert.equal(h.dispatcherUseCalls[0].item.txt, '可用', '过滤掉已用道具，选未使用');
});

test('YO 空道具栏不触发', () => {
  const h = buildHarness({
    Si: 0, opponentGold: 0, elapsed: 5001,
    itemSlots: [],
  });
  h.ai.startGame();

  h.ai.YO();

  assert.equal(h.dispatcherUseCalls.length, 0, '空道具栏 YO 不触发');
  // 空栏不更新 xG（提前 return）
  assert.equal(h.ai.xG, 0, '空栏 xG 不更新');
});

test('YO 全部已使用不触发', () => {
  const h = buildHarness({
    Si: 0, opponentGold: 0, elapsed: 5001,
    itemSlots: [{ type: 3, txt: '已用', _used: true }],
  });
  h.ai.startGame();

  h.ai.YO();

  assert.equal(h.dispatcherUseCalls.length, 0, '全部已使用 YO 不触发');
});

// ===== Yb type 分派（成功 ✅ / 失败 ❌ 日志）=====

test('Yb 成功日志 `✅AI成功使用道具` 并标记 item._used', () => {
  const h = buildHarness({
    Si: 0, opponentGold: 0,
    dispatcherResult: { success: true },
  });
  h.ai.startGame();

  const item = { type: 6, txt: '道具X', _used: false };
  h.ai.Yb(item);

  assert.equal(h.dispatcherUseCalls.length, 1, '调一次 use');
  assert.equal(h.dispatcherUseCalls[0].type, 6, '按 item.type=6 分派');
  assert.equal(item._used, true, '成功后标记 item._used=true');
  const okLog = h.logs.find(l => l.includes('✅AI成功使用道具'));
  assert.ok(okLog, '成功日志 `✅AI成功使用道具`');
  assert.ok(okLog.includes('道具X'), '日志含道具 txt');
});

test('Yb 失败日志 `❌AI使用道具失败`', () => {
  const h = buildHarness({
    Si: 0, opponentGold: 0,
    dispatcherResult: { success: false },
  });
  h.ai.startGame();

  const item = { type: 3, txt: '道具Y', _used: false };
  h.ai.Yb(item);

  const failLog = h.logs.find(l => l.includes('❌AI使用道具失败'));
  assert.ok(failLog, '失败日志 `❌AI使用道具失败`');
  assert.ok(failLog.includes('道具Y'), '日志含道具 txt');
  // 失败不标记 _used
  assert.equal(item._used, false, '失败不标记 item._used');
});

test('Yb 按 type 分派到 itemEffectDispatcher.use(type, item)', () => {
  // 验证不同 type 均透传到 use 的第一参
  const h = buildHarness({ Si: 0, opponentGold: 0, dispatcherResult: { success: true } });
  h.ai.startGame();

  for (const type of [3, 4, 10, 5, 6, 2, 7, 8, 9]) {
    h.dispatcherUseCalls.length = 0;
    h.ai.Yb({ type, txt: 't' + type, _used: false });
    assert.equal(h.dispatcherUseCalls[0].type, type, `type=${type} 透传到 use 第一参`);
  }
});

// ===== DEFERRED 桩返回 false =====

test('DEFERRED 桩：默认 itemEffectDispatcher.use 返回 {success:false}，日志 ❌', () => {
  // 不注入 itemEffectDispatcher，使用 AIController 默认桩（DEFERRED_ITEM_SYSTEM）
  const eventBus = new EventBus();
  const battle = new BattleState(eventBus);
  battle.aiDifficulty = 0; battle.opponentGold = 0; battle.opponentRecruitCost = 10;
  const gameData = { battle };
  const gameLoop = { elapsed: 0, register() {}, unregister() {} };
  const deckManager = { hand() { return []; }, refresh() {} };
  const inputController = { execute() { return { success: true }; } };
  const logs = [];
  const logger = { log() { logs.push([...arguments].join(' ')); }, warn() {}, debug() {}, error() {} };

  const ai = new AIController({
    gameLoop, gameData, deckManager, inputController, eventBus, logger,
    // 不传 itemEffectDispatcher → 默认 DEFERRED 桩
    itemSlots: [{ type: 3, txt: '桩道具', _used: false }],
  });
  ai.startGame();

  // 直接调 Yb 验证桩返回
  const item = { type: 3, txt: '桩道具', _used: false };
  ai.Yb(item);

  const failLog = logs.find(l => l.includes('❌AI使用道具失败'));
  assert.ok(failLog, 'DEFERRED 桩 use 返回 false → 日志 `❌AI使用道具失败`');
  // 桩不标记 _used（失败路径）
  assert.equal(item._used, false, 'DEFERRED 桩不标记 _used');
});

test('DEFERRED 桩 YO 冷却满足时触发但 use 返回 false（不阻塞状态机）', () => {
  // 默认桩 + 冷却满足 + 非空栏 → YO 触发调 Yb，桩返回 false 打 ❌，不抛异常
  const eventBus = new EventBus();
  const battle = new BattleState(eventBus);
  battle.aiDifficulty = 0; battle.opponentGold = 0; battle.opponentRecruitCost = 10;
  const gameData = { battle };
  const gameLoop = { elapsed: 6000, register() {}, unregister() {} };
  const deckManager = { hand() { return []; }, refresh() {} };
  const inputController = { execute() { return { success: true }; } };
  const logs = [];
  const logger = { log() { logs.push([...arguments].join(' ')); }, warn() {}, debug() {}, error() {} };

  const ai = new AIController({
    gameLoop, gameData, deckManager, inputController, eventBus, logger,
    itemSlots: [{ type: 3, txt: '桩道具', _used: false }],
    // 不传 itemEffectDispatcher → 默认 DEFERRED 桩
  });
  ai.startGame();

  // 不应抛异常
  assert.doesNotThrow(() => ai.YO(), 'DEFERRED 桩 YO 触发不抛异常');
  const failLog = logs.find(l => l.includes('❌AI使用道具失败'));
  assert.ok(failLog, 'DEFERRED 桩触发后打 `❌AI使用道具失败`');
  // xG 仍更新（YO 已尝试分派）
  assert.equal(ai.xG, 6000, 'DEFERRED 桩触发后 xG 仍更新');
});

// ===== 端到端：状态机 step1 → refresh/YO 衔接 =====

test('端到端：step1 金币够→refresh→step2；金币不够+冷却满足→YO→Yb', () => {
  // 场景 A：金币够 → refresh + step2
  const hA = buildHarness({
    Si: 0, opponentGold: 20, opponentRecruitCost: 10,
    inputResult: { success: true },
  });
  hA.ai.startGame();
  hA.ai.TG(0);
  assert.equal(hA.inputCalls.filter(c => c.type === BattleInputCommandType.REFRESH).length, 1, '场景A：金币够 refresh 被调');
  assert.equal(hA.ai.step, 2, '场景A：进入 step2');

  // 场景 B：金币不够（5<10），难度0 ni=0.001，random=0.5>0.001 → YO 分支
  // 冷却满足（elapsed=5001）+ 非空栏 → YO 调 Yb
  // randomSeq 需提供两个值：第一个供 step1 的 _random()（ni 比较，须 > ni=0.001 才跳过 UG 走 YO），
  // 第二个供 YO 的 _randomInt(0,unused.length) 选道具索引（0 → 道具Z）。
  const hB = buildHarness({
    Si: 0, opponentGold: 5, opponentRecruitCost: 100, elapsed: 5001,
    itemSlots: [{ type: 6, txt: '道具Z', _used: false }],
    dispatcherResult: { success: true },
    randomSeq: [0.5, 0], // 0.5>0.001 跳过 UG；0 选 unused[0]
  });
  hB.ai.startGame();
  // startGame 加 hi=10 → opponentGold=15，仍 < 100 → 走 YO 分支（无需手动压金币）
  assert.ok(hB.battle.opponentGold < hB.battle.opponentRecruitCost, '场景B：金币 < 刷牌阈值');
  hB.ai.step = 1;
  hB.ai.TG(0);

  assert.equal(hB.dispatcherUseCalls.length, 1, '场景B：金币不够+冷却满足 → YO 调 Yb');
  assert.equal(hB.dispatcherUseCalls[0].type, 6, '场景B：按 type=6 分派');
  const okLog = hB.logs.find(l => l.includes('✅AI成功使用道具'));
  assert.ok(okLog, '场景B：成功日志 ✅');
});
