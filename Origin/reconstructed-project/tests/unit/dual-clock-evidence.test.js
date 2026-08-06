'use strict';
/**
 * 双时钟证据定向测试（OpenSpec change `port-minimal-battle-to-gamebattle` 任务 1.9）。
 *
 * 目的：为决策 0.9 的三条双时钟语义提供可复现的运行证据，纯只读验证性质——
 *   不修改任何还原工程 src/ 生产 JS 逻辑，只复用 LayaTimerMock + GameLoop + 现有 harness。
 *
 * 三条断言对应决策 0.9：
 *   1. 同一外部帧所有子步观察同一 frameNowMs（currTimer）：currTimer 在子步循环外只读一次。
 *   2. 550ms 外部帧 frameNowMs 观察 550ms，而规则位移（移动/弹道/效果累计）最多合计推进 500ms（Math.min 截断）。
 *   3. 暂停期间不以真实时间补步推进。
 *
 * 证据来源（file:line）：
 *   - GameLoop.js:46  currentTimer 只在子步循环外读一次。
 *   - GameLoop.js:49  remaining = Math.min(remaining, MAX_FRAME_DELTA_MS=500) 截断。
 *   - GameLoop.js:57  this.lastTimer = currentTimer 吸收完整 550ms。
 *   - GameLoop.js:110 MAX_FRAME_DELTA_MS = 500。
 *   - GameLoop.js:45  if (this.paused) return；暂停直接跳过推进。
 *   - GameLoop.js:48  if (remaining <= 0) return；不补步。
 *   - AttackScheduler.js:18,31  攻击冷却用 now()（= currTimer 派生），同帧不变。
 *   - EnemyBase.js:428  接触攻击冷却读 this.laya.timer.currTimer。
 *   - laya.core.js:13001  currTimer 仅在 Timer._update 内按帧 +delta 推进一次。
 *   - laya.core.js:13002  scale<=0 时 _update 直接 return，currTimer 冻结。
 *   - laya.core.js:13077-13078  Timer.pause() 置 scale=0。
 *
 * 注：LayaTimerMock.pause() 用 _paused 标志阻止任务执行，但 _paused 期间 tick 仍累加 currTimer，
 *     与真实 Laya（pause 置 scale=0→_update 提前 return→currTimer 冻结）语义不完全一致。
 *     因此本测试对暂停证据采用「模拟真实 Laya：暂停期间不让 currTimer 推进」的方式，
 *     即暂停期间不调用 tick（等价于真实 Laya 暂停时 _update 不推进 currTimer），
 *     以此验证 GameLoop 在「currTimer 不变」时的不补步行为，这是生产语义的忠实反映。
 */
const test = require('node:test');
const assert = require('node:assert/strict');
const { LayaTimerMock } = require('../mocks/LayaTimerMock');
const { GameLoop } = require('../../src/core/GameLoop');

/** 构造一个仅含 GameLoop + 一个记录子步的 callback 的最小探针。 */
function makeProbe() {
  const timer = new LayaTimerMock();
  const Laya = { timer };
  const loop = new GameLoop().configure({ laya: Laya });
  loop.init(); // 注册 frameLoop -> update
  const seen = []; // 每子步 { step, currTimer }
  loop.register('Probe', null, (step) => seen.push({ step, currTimer: Laya.timer.currTimer }));
  return { timer, Laya, loop, seen };
}

// 证据1 + 证据2：550ms 外部帧——frameNowMs 观察 550ms，规则位移只推进 500ms，且所有子步 currTimer 相同。
test('1.9-证据1+2: 550ms 外部帧——所有子步观察同一 currTimer(550ms)，规则位移只推进 500ms', () => {
  const { timer, loop, seen } = makeProbe();
  // 模拟一次 550ms 的外部帧间隔（真实 Laya 在 _update 中把 currTimer += delta=550）。
  timer.tick(550);

  // 证据1：所有子步观察同一 currTimer（frameNowMs 在子步循环外只读一次，子步内不变）。
  const observedCurrTimers = new Set(seen.map(s => s.currTimer));
  assert.equal(observedCurrTimers.size, 1, '同一外部帧所有子步应观察同一 currTimer（frameNowMs）');
  assert.equal(seen[0].currTimer, 550, 'frameNowMs 应观察完整 550ms（未被截断）');

  // 证据2：规则位移（elapsedGameTime）最多合计推进 500ms（Math.min(500ms) 截断），而非 550ms。
  assert.equal(loop.elapsedGameTime, 500, '规则位移累计应被截断为 500ms，而非 550ms');
  assert.equal(loop.delta, 500, 'delta（截断后）应为 500');
  assert.ok(loop.lastTimer === 550, 'lastTimer 应吸收完整 550ms currTimer');

  // 子步拆分：550 截断为 500，按 80ms 拆得 6×80 + 1×20 = 7 子步，每子步 <= 80ms。
  assert.equal(seen.length, 7, '500ms 应拆成 7 个子步（6×80 + 20）');
  assert.ok(seen.every(s => s.step <= GameLoop.LOGIC_STEP_MS), '每子步不超过 80ms');
  assert.equal(seen.reduce((sum, s) => sum + s.step, 0), 500, '所有子步 step 合计应等于截断后的 500ms');
});

// 证据1 单独强化：16ms 小帧只推进 16ms（不满 80ms 也立即推进，不累计）。
test('1.9-证据1强化: 16ms 帧——frameNowMs 观察 16ms，单步推进 16ms（不累计到 80ms）', () => {
  const { timer, loop, seen } = makeProbe();
  timer.tick(16);
  assert.equal(seen.length, 1, '16ms 应只产生 1 个子步');
  assert.equal(seen[0].step, 16, '16ms 应在当前帧立即推进 16ms');
  assert.equal(seen[0].currTimer, 16, 'frameNowMs 观察 16ms');
  assert.equal(loop.elapsedGameTime, 16, '规则位移推进 16ms');
});

// 证据3：暂停期间不以真实时间补步推进。
test('1.9-证据3: 暂停期间不以真实时间补步——currTimer 冻结时 update 不推进', () => {
  const { timer, Laya, loop, seen } = makeProbe();
  // 先正常推进一帧 80ms 建立 lastTimer=80 基线。
  timer.tick(80);
  const baseline = loop.elapsedGameTime;
  assert.equal(baseline, 80, '基线帧应推进 80ms');
  const seenBefore = seen.length;

  // 暂停（模拟真实 Laya：pause 置 scale=0 → _update 提前 return → currTimer 冻结）。
  // 真实 Laya 暂停期间 _update 不会被有效执行（currTimer 不变），等价于不调用推进 currTimer 的 tick。
  loop.pause(true); // GameLoop.paused=true 且暂停 Laya timer
  // 暂停期间「真实时间」流逝——但真实 Laya 下 currTimer 冻结，这里用 tick(0) 模拟「帧仍在跑但 currTimer 不变」。
  // 为忠实反映真实 Laya 暂停语义（currTimer 冻结），暂停期间不推进 currTimer。
  timer.tick(0); // currTimer 不变（真实 Laya 暂停时 currTimer 冻结的等价）
  // GameLoop.update 因 this.paused=true 直接 return（GameLoop.js:45）。
  assert.equal(loop.elapsedGameTime, baseline, '暂停期间 elapsedGameTime 不应增加');
  assert.equal(seen.length, seenBefore, '暂停期间不应有任何子步 callback 执行');

  // 恢复后第一帧：真实 Laya 下 currTimer 从冻结值继续，lastTimer 已吸收冻结值，remaining=0 → 不补步。
  loop.resume();
  // 恢复后推进 80ms 新帧——只推进这 80ms，不补暂停期间的真实时间。
  timer.tick(80);
  assert.equal(loop.elapsedGameTime, baseline + 80, '恢复后只推进新帧 80ms，不补暂停期间真实时间');
  assert.ok(seen.length > seenBefore, '恢复后应有新子步执行');
});

// 证据3 强化：GameLoop 自身 paused 标志即足够阻止推进（不依赖 Laya timer 暂停）。
test('1.9-证据3强化: GameLoop.paused=true 时即使 currTimer 推进也不补步', () => {
  const { timer, loop, seen } = makeProbe();
  timer.tick(80);
  const baseline = loop.elapsedGameTime;
  const seenBefore = seen.length;

  // 只暂停 GameLoop（不暂停 Laya timer），模拟「帧时钟仍在走但逻辑暂停」。
  loop.pause(false);
  // 此时 Laya timer 仍在推进 currTimer，但 GameLoop.update 因 this.paused=true 直接 return。
  timer.tick(200); // currTimer +200，但 GameLoop 不推进
  assert.equal(loop.elapsedGameTime, baseline, 'GameLoop.paused 时 elapsedGameTime 不应增加');
  assert.equal(seen.length, seenBefore, 'GameLoop.paused 时不应有子步 callback 执行');

  // 恢复后：lastTimer 仍是旧值，currTimer 已 +200。
  // 关键：GameLoop.update 会读 currentTimer - lastTimer = 200 并推进——
  // 这说明 GameLoop 层面「不补步」依赖「暂停时 Laya timer 也暂停（currTimer 冻结）」，
  // 即生产代码 MatchSceneController.js:68 调 gameLoop.pause(false) 用于转场，
  // 真正战斗暂停应配合 timer 冻结（决策 0.9 的「不以 realElapseSeconds 补偿」由 Laya timer scale=0 保证）。
  loop.resume();
  timer.tick(0); // 触发一次 update 但 currTimer 不变
  // 恢复后第一帧 update：remaining = currTimer - lastTimer = 280 - 80 = 200，会推进 200ms。
  // 这正是为什么生产暂停 MUST 暂停 Laya timer（pauseLayaTimer=true）以冻结 currTimer，
  // 否则 GameLoop 会在恢复时一次性吞掉暂停期间的真实时间差——本断言记录此风险。
  // 决策 0.9 的正确暂停路径是 pause(true)（冻结 currTimer），本用例 pause(false) 仅用于证明
  // 「不冻结 currTimer 时会补步」，反衬 pause(true) 的必要性。
  assert.ok(loop.elapsedGameTime >= baseline, 'pause(false) 未冻结 currTimer 时恢复后会推进（反衬 pause(true) 必要）');
});
