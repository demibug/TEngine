'use strict';

// 任务 7.2 难度用例（OpenSpec Change: ai-advanced-strategy）。
// 覆盖：
//   1) fG 随 Si 变化（2000/1500/1000/500ms，难度 0 最慢、难度 3 最快）。
//   2) ni/ri 随 Si（ni 4 档均 0.001、ri=[0.1,0.2,0.5,0.8]）。
//   3) ii[Si][i] 随 Si（0/1 全 0、2 每波 +10、3 每波 +20）。
//   4) Tu(±1) 升降级钳制 0-3（升、降、边界 0 不降、3 不升）。
// 直接测 AIDifficultyConfig.resolve(Si) 返回值；测 AIController.Tu(delta) 钳制与回写；
// 可读 ai-difficulty.json 断言数值与 bundle 来源标注。

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');

const { AIDifficultyConfig } = require('../../src/ai/AIDifficultyConfig');
const { AIController } = require('../../src/ai/AIController');

// 难度配置 JSON 路径（与 AIDifficultyConfig 一致，用于断言 bundle 来源标注）
const CONFIG_PATH = path.join(__dirname, '../../unity-export/config/ai-difficulty.json');

// ---------- 最小 mock harness（构造 AIController 所需依赖） ----------
// 目的：仅验证难度字段与 Tu 钳制，不驱动完整状态机，故依赖均为 no-op 桩。
function makeAiController(initialSi) {
  const battle = {
    aiDifficulty: initialSi,           // au.Si 难度档（startGame 读取并钳制）
    opponentGold: 0,                   // au.Ji AI 金币
    opponentRecruitCost: 10,           // au.gi 刷牌阈值
    currentRound: 0,                   // au.li 波次
    opponentPlacementComplete: false,  // au.Xi 布阵已开始
    standardBattleDelayEnabled: true,  // ki 守卫为 true 使 startGame 不提前返回
  };
  const gameData = {
    battle,
    map: { width: 6, height: 10, mapIndex: 0, blockAt: () => '1_1', me: [] },
  };
  const gameLoop = { register() {}, unregister() {}, elapsed: 0 };
  const deckManager = { hand() { return []; }, refresh() {} };
  const inputController = { execute() { return { success: true }; } };
  const eventBus = { on() {}, off() {} };
  return new AIController({
    gameLoop, gameData, deckManager, inputController, eventBus,
    logger: { log() {}, warn() {}, debug() {} },
  });
}

// =================================================================
// 1) fG 随 Si 变化（决策间隔随难度变化）
//    spec Scenario「决策间隔随难度变化」：fG 取 [2000,1500,1000,500][Si]，
//    难度 0 最慢 2000ms、难度 3 最快 500ms（难度越高决策越快）。
// =================================================================

test('AIDifficultyConfig.resolve: fG 随 Si 变化 2000/1500/1000/500ms', () => {
  const cfg = new AIDifficultyConfig();
  // 直接测 resolve(Si) 返回的 fG 字段（标量，已按 Si 索引）
  assert.equal(cfg.resolve(0).fG, 2000, 'Si=0 fG=2000ms（最慢）');
  assert.equal(cfg.resolve(1).fG, 1500, 'Si=1 fG=1500ms');
  assert.equal(cfg.resolve(2).fG, 1000, 'Si=2 fG=1000ms');
  assert.equal(cfg.resolve(3).fG, 500, 'Si=3 fG=500ms（最快）');
  // 难度越高决策越快：fG 递减
  assert.ok(cfg.resolve(0).fG > cfg.resolve(1).fG, '难度 0→1 决策变快');
  assert.ok(cfg.resolve(1).fG > cfg.resolve(2).fG, '难度 1→2 决策变快');
  assert.ok(cfg.resolve(2).fG > cfg.resolve(3).fG, '难度 2→3 决策变快');
});

test('AIController.startGame: fG 经 AIDifficultyConfig.resolve 后按 Si 设置', () => {
  // 经 startGame 后 AIController.fG 字段应等于 resolve(Si).fG
  for (const Si of [0, 1, 2, 3]) {
    const ai = makeAiController(Si);
    ai.startGame();
    assert.equal(ai.fG, [2000, 1500, 1000, 500][Si], `Si=${Si} startGame 后 fG 应为 ${[2000, 1500, 1000, 500][Si]}ms`);
    assert.equal(ai.Si, Si, `Si=${Si} startGame 后 this.Si 应为 ${Si}`);
  }
});

test('AIDifficultyConfig.resolve: Si 越界钳制到 [0,3]', () => {
  const cfg = new AIDifficultyConfig();
  // 负数钳制到 0（fG=2000）
  assert.equal(cfg.resolve(-5).fG, 2000, 'Si=-5 钳制到 0，fG=2000ms');
  assert.equal(cfg.resolve(-1).fG, 2000, 'Si=-1 钳制到 0，fG=2000ms');
  // 超过 3 钳制到 3（fG=500）
  assert.equal(cfg.resolve(4).fG, 500, 'Si=4 钳制到 3，fG=500ms');
  assert.equal(cfg.resolve(99).fG, 500, 'Si=99 钳制到 3，fG=500ms');
});

// =================================================================
// 2) ni/ri 随 Si（行为概率随难度变化）
//    spec Scenario「行为概率随难度变化」：ni[Si] 4 档均为 0.001（快速结束概率）；
//    ri[Si] 难度越高越大（.1/.2/.5/.8）。
// =================================================================

test('AIDifficultyConfig.resolve: ni 4 档均为 0.001', () => {
  const cfg = new AIDifficultyConfig();
  for (const Si of [0, 1, 2, 3]) {
    assert.equal(cfg.resolve(Si).ni, 0.001, `Si=${Si} ni=0.001（step1 快速结束概率，4 档相同）`);
  }
});

test('AIDifficultyConfig.resolve: ri 随 Si 递增 [0.1,0.2,0.5,0.8]', () => {
  const cfg = new AIDifficultyConfig();
  const expectedRi = [0.1, 0.2, 0.5, 0.8];
  for (const Si of [0, 1, 2, 3]) {
    assert.equal(cfg.resolve(Si).ri, expectedRi[Si], `Si=${Si} ri=${expectedRi[Si]}（XG 触发概率）`);
  }
  // 难度越高 ri 越大
  assert.ok(cfg.resolve(0).ri < cfg.resolve(1).ri, 'ri 0→1 递增');
  assert.ok(cfg.resolve(1).ri < cfg.resolve(2).ri, 'ri 1→2 递增');
  assert.ok(cfg.resolve(2).ri < cfg.resolve(3).ri, 'ri 2→3 递增');
});

test('AIController.startGame: _ni/_ri 经 resolve 后按 Si 设置为标量', () => {
  // 验证 startGame 将 resolve 返回的标量 ni/ri 存入 _ni/_ri（等价 My.ni[Si]/My.ri[Si]）
  const ai2 = makeAiController(2);
  ai2.startGame();
  assert.equal(ai2._ni, 0.001, 'Si=2 _ni=0.001（标量，非数组，避免双重索引 bug）');
  assert.equal(ai2._ri, 0.5, 'Si=2 _ri=0.5（标量）');

  const ai3 = makeAiController(3);
  ai3.startGame();
  assert.equal(ai3._ni, 0.001, 'Si=3 _ni=0.001');
  assert.equal(ai3._ri, 0.8, 'Si=3 _ri=0.8');
});

// =================================================================
// 3) ii[Si][i] 随 Si（周期收入随难度变化）
//    spec Scenario「周期收入随难度变化」：ii[0]/ii[1] 全 0（无收入），
//    ii[2] 每波 +10，ii[3] 每波 +20（hu[1]→20 已解码确认）。
// =================================================================

test('AIDifficultyConfig.resolve: ii[Si] 随 Si 变化（0/1 全 0、2 每 +10、3 每 +20）', () => {
  const cfg = new AIDifficultyConfig();
  // ii[0]/ii[1] 全 0（6 波次均无收入）
  assert.deepEqual(cfg.resolve(0).ii, [0, 0, 0, 0, 0, 0], 'Si=0 ii 全 0（无周期收入）');
  assert.deepEqual(cfg.resolve(1).ii, [0, 0, 0, 0, 0, 0], 'Si=1 ii 全 0（无周期收入）');
  // ii[2] 每波 +10
  assert.deepEqual(cfg.resolve(2).ii, [10, 10, 10, 10, 10, 10], 'Si=2 ii 每波 +10');
  // ii[3] 每波 +20（hu[1]=20 已解码确认）
  assert.deepEqual(cfg.resolve(3).ii, [20, 20, 20, 20, 20, 20], 'Si=3 ii 每波 +20（hu[1]=20）');
});

test('AIDifficultyConfig: ii 二维表 [4][6] 结构完整（PG 周期收入按 ii[Si][i] 取值）', () => {
  // 验证完整 ii[Si][i] 二维表（bundle:3155-3159 语义，PG 用 ii2d[Si][i]）
  const cfg = new AIDifficultyConfig();
  const ii2d = cfg.raw.ii;
  assert.equal(ii2d.length, 4, 'ii 二维表 4 行（对应 4 级难度）');
  for (const row of ii2d) {
    assert.equal(row.length, 6, 'ii 每行 6 个波次值');
  }
  assert.deepEqual(ii2d[0], [0, 0, 0, 0, 0, 0], 'ii[0] 全 0');
  assert.deepEqual(ii2d[1], [0, 0, 0, 0, 0, 0], 'ii[1] 全 0');
  assert.deepEqual(ii2d[2], [10, 10, 10, 10, 10, 10], 'ii[2] 每波 +10');
  assert.deepEqual(ii2d[3], [20, 20, 20, 20, 20, 20], 'ii[3] 每波 +20');
});

test('AIController.startGame: _ii2d 完整二维表与 _iiRow 当前行均正确', () => {
  const ai2 = makeAiController(2);
  ai2.startGame();
  // _iiRow 为 resolve 返回的 ii[Si] 单行（PG 可用 _iiRow[i] 等价 ii[Si][i]）
  assert.deepEqual(ai2._iiRow, [10, 10, 10, 10, 10, 10], 'Si=2 _iiRow=ii[2] 每波 +10');
  // _ii2d 为完整二维表（PG 用 _ii2d[Si][i] 还原 bundle 原语义）
  assert.deepEqual(ai2._ii2d[2], [10, 10, 10, 10, 10, 10], '_ii2d[2]=ii[2]');
  assert.deepEqual(ai2._ii2d[3], [20, 20, 20, 20, 20, 20], '_ii2d[3]=ii[3] 每波 +20');
});

// =================================================================
// 4) Tu(±1) 升降级钳制 0-3
//    spec Scenario「升降级 Tu 在 gameOver 时触发」：胜 Tu(1) 升级、败 Tu(-1) 降级，
//    Si 跨档调整，钳制 0-3。
// =================================================================

test('AIController.Tu(1) 升级：Si 0→1→2→3', () => {
  // 默认 rankTableResolver 桩：resolve(Si,delta)=clamp(0,3,Si+delta)
  const ai0 = makeAiController(0); ai0.startGame();
  assert.equal(ai0.Tu(1), 1, 'Si=0 Tu(1) 升级到 1');

  const ai1 = makeAiController(1); ai1.startGame();
  assert.equal(ai1.Tu(1), 2, 'Si=1 Tu(1) 升级到 2');

  const ai2 = makeAiController(2); ai2.startGame();
  assert.equal(ai2.Tu(1), 3, 'Si=2 Tu(1) 升级到 3');
});

test('AIController.Tu(-1) 降级：Si 3→2→1→0', () => {
  const ai3 = makeAiController(3); ai3.startGame();
  assert.equal(ai3.Tu(-1), 2, 'Si=3 Tu(-1) 降级到 2');

  const ai2 = makeAiController(2); ai2.startGame();
  assert.equal(ai2.Tu(-1), 1, 'Si=2 Tu(-1) 降级到 1');

  const ai1 = makeAiController(1); ai1.startGame();
  assert.equal(ai1.Tu(-1), 0, 'Si=1 Tu(-1) 降级到 0');
});

test('AIController.Tu 边界钳制：Si=0 不降、Si=3 不升', () => {
  // 边界 0：Tu(-1) 不降到 -1，钳制在 0
  const ai0 = makeAiController(0); ai0.startGame();
  assert.equal(ai0.Tu(-1), 0, 'Si=0 Tu(-1) 钳制在 0，不降为 -1');

  // 边界 3：Tu(1) 不升到 4，钳制在 3
  const ai3 = makeAiController(3); ai3.startGame();
  assert.equal(ai3.Tu(1), 3, 'Si=3 Tu(1) 钳制在 3，不升为 4');
});

test('AIController.Tu 回写 au.aiDifficulty（升降级结果跨局持久化）', () => {
  // spec「升降级 Tu 在 gameOver 时触发」+ BattleFlowCoordinator 在 gameData.gameOver 之后调 Tu，
  // Tu 须回写 au.aiDifficulty 使结果跨局持久化（避免被 BattleState.gameOver 重置覆盖）。
  const ai1 = makeAiController(1); ai1.startGame();
  const newSi = ai1.Tu(1);
  assert.equal(newSi, 2, 'Si=1 Tu(1)→2');
  assert.equal(ai1.Si, 2, 'this.Si 回写为 2');
  assert.equal(ai1._au().aiDifficulty, 2, 'au.aiDifficulty 回写为 2（跨局持久化）');

  const ai0 = makeAiController(0); ai0.startGame();
  ai0.Tu(-1);
  assert.equal(ai0._au().aiDifficulty, 0, 'Si=0 Tu(-1) 钳制回写 au.aiDifficulty=0');
});

test('AIController.Tu 支持注入 rankTableResolver 跨档计算（DEFERRED_RANK_TABLE 接口）', () => {
  // design 决策 2 + Risks：Tu 经可注入 rankTableResolver 跨 rank 表计算（Va.get(l).level），
  // 默认 DEFERRED 桩 no-op 钳制 0-3，真实 rank 表注入后可跨档（如 Si=1+1→3）。
  const ai = makeAiController(1); ai.startGame();
  // 注入跨档 resolver：resolve(Si, delta) 模拟 rank 表跨档（Si=1 胜 +1 经 rank 表跳到 3）
  ai.rankTableResolver = {
    resolve(Si, delta) { return Si + delta + 1; }, // 跨档：1+1+1=3
  };
  assert.equal(ai.Tu(1), 3, '注入跨档 resolver 后 Si=1 Tu(1)→3（rank 表跨档计算）');

  // 跨档结果仍钳制 0-3
  const aiHigh = makeAiController(3); aiHigh.startGame();
  aiHigh.rankTableResolver = { resolve(Si, delta) { return Si + delta + 5; } }; // 远超 3
  assert.equal(aiHigh.Tu(1), 3, '跨档结果超过 3 钳制在 3');
});

// =================================================================
// 5) ai-difficulty.json 数值与 bundle 来源标注（spec Scenario「难度配置来源 bundle 标注」）
//    每数值（fG/ni/ri/ii/hi/ei/oi/itemCooldownMs）标注 bundle 行号来源，
//    hu[N] 解码值标注 hu[N]→解码值。
// =================================================================

test('ai-difficulty.json: 数值与 bundle 来源标注完整', () => {
  const raw = JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8'));

  // 数值断言（与 resolve 间接测的值一致，此处直接断言 JSON 源）
  assert.deepEqual(raw.decisionIntervalMs, [2000, 1500, 1000, 500], 'fG=[2000,1500,1000,500]');
  assert.deepEqual(raw.ni, [0.001, 0.001, 0.001, 0.001], 'ni 4 档 0.001');
  assert.deepEqual(raw.ri, [0.1, 0.2, 0.5, 0.8], 'ri=[0.1,0.2,0.5,0.8]');
  assert.deepEqual(raw.ii, [
    [0, 0, 0, 0, 0, 0],
    [0, 0, 0, 0, 0, 0],
    [10, 10, 10, 10, 10, 10],
    [20, 20, 20, 20, 20, 20],
  ], 'ii[0/1] 全 0、ii[2] +10、ii[3] +20');
  assert.equal(raw.hi, 10, 'hi=10');
  assert.deepEqual(raw.ei, [3, 5, 8, 11, 14, 17], 'ei 波次表');
  assert.deepEqual(raw.oi, [0, 0, 0, 5], 'oi=[0,0,0,5]');
  assert.equal(raw.itemCooldownMs, 5000, 'itemCooldownMs=5000（hu[101]）');

  // bundle 来源标注：每个数值字段须有对应 source_* 字段标注 bundle 行号
  const sourceFields = [
    'source_decisionIntervalMs', 'source_ni', 'source_ri', 'source_ii',
    'source_hi', 'source_ei', 'source_oi', 'source_itemCooldownMs',
  ];
  for (const field of sourceFields) {
    assert.ok(typeof raw[field] === 'string' && raw[field].length > 0, `${field} 须标注 bundle 来源`);
    assert.ok(raw[field].includes('bundle:'), `${field} 须含 bundle: 行号标注`);
  }

  // hu[N] 解码值标注：fG 的 hu[118]/hu[122]/hu[123]/hu[176] 与 ii[3] 的 hu[1]、
  // itemCooldownMs 的 hu[101] 须标注 hu[N]→解码值
  assert.ok(raw.source_decisionIntervalMs.includes('hu[118]'), 'fG 来源须标注 hu[118]');
  assert.ok(raw.source_decisionIntervalMs.includes('hu[176]'), 'fG 来源须标注 hu[176]');
  assert.ok(raw.source_ii.includes('hu[1]'), 'ii 来源须标注 hu[1]（ii[3]=20 解码）');
  assert.ok(raw.source_itemCooldownMs.includes('hu[101]'), 'itemCooldownMs 来源须标注 hu[101]');

  // hu 解码值确认：fG 解码 2000/1500/1000/500、ii[3]=20、itemCooldownMs=5000
  assert.ok(raw.source_decisionIntervalMs.includes('2000'), 'fG 来源须标注 hu[118]→2000');
  assert.ok(raw.source_decisionIntervalMs.includes('500'), 'fG 来源须标注 hu[176]→500');
  assert.ok(raw.source_ii.includes('20'), 'ii 来源须标注 hu[1]=20');
  assert.ok(raw.source_itemCooldownMs.includes('5000'), 'itemCooldownMs 来源须标注 hu[101]=5000');
});
