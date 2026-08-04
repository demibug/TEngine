'use strict';

// 任务 3.10：Deck 牌池用例
// 覆盖 spec.md「牌池含铲与武将字」「武将字 no-repeat 抽取」「AI 侧重排铲前置」
// 「铲注入按 roundDay」「武将字 50% 复制」「刷新两阶段清除」六个 Scenario。
// 测试框架：node:test + node:assert/strict（对齐 InspireEffect.test.js / GeneralActiveSkills.test.js）。

const test = require('node:test');
const assert = require('node:assert/strict');
const { DeckManager } = require('../../src/deck/DeckManager');
const { DeckDefinitions } = require('../../src/deck/DeckDefinitions');
const { BattleState } = require('../../src/battle/BattleState');
const { EventBus } = require('../../src/core/EventBus');
const { PlayerDataCore } = require('../../src/data/PlayerDataCore');

// ---------- mock 构造（风格对齐 InspireEffect.test.js / AIRefreshIncomeItem.test.js） ----------
// gameData 含 battle(BattleState,Fi/Oi/Si)/player(PlayerDataCore,roundDay)。
// economy 为最小桩：payRefresh 默认成功，供 refresh 流程不阻塞（NY/qY 用例不依赖经济数值）。

/**
 * 构造 DeckManager 测试夹具。
 * @param {object} opts
 * @param {number} [opts.roundDay=1] 玩家 roundDay（winDay+loseDay+1 经 PlayerDataCore getter）。
 * @param {number} [opts.Si=0] AI 难度档（aiDifficulty）。
 * @param {boolean} [opts.playerDup=false] 玩家侧武将字重复标志 Fi。
 * @param {boolean} [opts.opponentDup=false] AI 侧武将字重复标志 Oi。
 * @param {function} [opts.rand] randomSource（默认 Math.random）。
 * @param {string[]} [opts.pool] 自定义 basePool（覆盖 DeckDefinitions.basePool，用于隔离分支用例）。
 * @returns {{dm:DeckManager,battle:BattleState,player:PlayerDataCore}}
 */
function makeDeck(opts = {}) {
  const eventBus = new EventBus();
  const battle = new BattleState(eventBus);
  battle.aiDifficulty = opts.Si != null ? opts.Si : 0;
  battle.playerDuplicateFlag = opts.playerDup === true;
  battle.opponentDuplicateFlag = opts.opponentDup === true;
  // 经真实 PlayerDataCore 构造 roundDay（getter = winDay+loseDay+1），忠实 bundle:8525-9429 语义。
  const winDay = Math.max(0, (opts.roundDay != null ? opts.roundDay : 1) - 1);
  const player = new PlayerDataCore({ winDay, loseDay: 0 });
  const gameData = { battle, player };
  // 最小 economy 桩：refresh 流程需要 payRefresh 成功，但不验证经济数值。
  const economy = {
    payRefresh(_side) { return { success: true, amount: 10, nextCost: 12 }; },
  };
  const ctorOpts = { gameData, economy, randomSource: opts.rand || Math.random };
  if (opts.pool) {
    ctorOpts.definitions = Object.freeze({
      handSize: DeckDefinitions.handSize,
      basePool: Object.freeze(opts.pool.slice()),
      defaultLevel: DeckDefinitions.defaultLevel,
      baseUnitCost: DeckDefinitions.baseUnitCost,
      maxLevel: DeckDefinitions.maxLevel,
    });
  }
  const dm = new DeckManager(ctorOpts);
  return { dm, battle, player };
}

/**
 * 统计扁平牌池中各字出现次数。
 * @param {string[]} pool
 * @returns {Record<string, number>}
 */
function countBy(pool) {
  const cnt = {};
  for (const c of pool) cnt[c] = (cnt[c] || 0) + 1;
  return cnt;
}

// 基础字判定表（对齐 DeckManager.drawCardNoRepeat/copyGeneralChars 内部表，bundle:46519/46553）。
const BASE_CHARS = ['刀', '弓', '枪', '骑', '铲', '农'];
const isBaseChar = (c) => BASE_CHARS.includes(c);

// ============================================================
// 用例 1：108 元素分布（spec Scenario「牌池含铲与武将字」）
// ============================================================
test('DeckDefinitions.basePool 覆盖 108 元素分布（刀21/弓19/枪18/骑17/铲11 + 武将字22）', () => {
  const pool = DeckDefinitions.basePool;
  // bundle:11969 三组同数组各 108 元素，展开后扁平数组长度必须为 108。
  assert.equal(pool.length, 108, '牌池展开后必须为 108 元素（bundle:11969）');

  const cnt = countBy(pool);

  // 基础单位字权重分布（忠实 bundle:11969 实测计数）。
  assert.equal(cnt['刀'], 21, '刀 ×21');
  assert.equal(cnt['弓'], 19, '弓 ×19');
  assert.equal(cnt['枪'], 18, '枪 ×18');
  assert.equal(cnt['骑'], 17, '骑 ×17');
  assert.equal(cnt['铲'], 11, '铲 ×11');

  // 武将字清单（18 个去重字符，重复项 赵/马/张/黄 各 2，合计 22 项）。
  const generalChars = ['刘', '赵', '云', '关', '羽', '平', '兴', '马', '超', '张', '飞', '苞', '翼', '黄', '忠', '盖', '祖', '备'];
  let generalTotal = 0;
  for (const ch of generalChars) {
    assert.ok(cnt[ch] >= 1, `武将字 ${ch} 必须可达（牌池含武将字）`);
    generalTotal += cnt[ch];
  }
  assert.equal(generalTotal, 22, '武将字合计 22 项（bundle:11969）');
  // 重复项校验：赵/马/张/黄 各 2。
  assert.equal(cnt['赵'], 2, '赵 ×2');
  assert.equal(cnt['马'], 2, '马 ×2');
  assert.equal(cnt['张'], 2, '张 ×2');
  assert.equal(cnt['黄'], 2, '黄 ×2');

  // spec 验收：抽牌可产出 铲 与武将字——basePool 含铲与武将字已证（drawText 见用例 2）。
  assert.ok(pool.includes('铲'), '牌池含 铲');
  assert.ok(generalChars.some((ch) => pool.includes(ch)), '牌池含武将字');
});

// ============================================================
// 用例 2：铲/武将字可达（drawText/poolForSide 可产出铲与武将字）
// spec Scenario「牌池含铲与武将字」——多次抽取消重覆盖。
// ============================================================
test('drawText / poolForSide 可产出 铲 与武将字（多次抽取消重覆盖）', () => {
  const { dm } = makeDeck();
  // poolForSide 返回 108 元素副本，含铲与武将字。
  const playerPool = dm.poolForSide(true);
  assert.equal(playerPool.length, 108, 'poolForSide 返回 108 元素副本');
  assert.ok(playerPool.includes('铲'), '玩家侧牌池含铲');
  assert.ok(playerPool.some((c) => !isBaseChar(c)), '玩家侧牌池含武将字（非基础字）');

  // drawText 多次抽取：固定随机序列覆盖铲下标与武将字下标，证明两路径均可达。
  // 108 元素扁平数组，找到铲与某武将字的下标，构造随机值命中。
  const base = DeckDefinitions.basePool;
  const shovelIdx = base.indexOf('铲');
  const generalIdx = base.findIndex((c) => !isBaseChar(c)); // 第一个武将字
  assert.ok(shovelIdx >= 0 && generalIdx >= 0, 'basePool 存在铲与武将字下标');

  // 构造随机值：r = idx/length 命中指定下标（drawText 用 floor(r*len)）。
  const seq = [
    shovelIdx / 108,
    generalIdx / 108,
  ];
  let i = 0;
  const { dm: dmSeq } = makeDeck({ rand: () => seq[i++] });
  assert.equal(dmSeq.drawText(true), '铲', 'drawText 可产出铲（命中铲下标）');
  const drawnGeneral = dmSeq.drawText(true);
  assert.ok(!isBaseChar(drawnGeneral), `drawText 可产出武将字（命中武将字下标，实际=${drawnGeneral}）`);
});

// ============================================================
// 用例 3：bO 武将字 no-repeat（Fi/Oi 置位时武将字抽出后池缩减，splice）
// spec Scenario「武将字 no-repeat 抽取」
// ============================================================
test('bO drawCardNoRepeat：武将字抽出后从池移除（splice），Fi 置位时再移除一份同字', () => {
  // 子用例 3a：无重复标志——武将字抽出后移除抽中项（池缩减 1）。
  {
    const { dm } = makeDeck({
      playerDup: false,
      pool: ['刀', '弓', '枪', '骑', '铲', '农', '刘', '祖'], // 2 武将字
      rand: () => 0.99, // floor(0.99*8)=7 -> '祖'
    });
    dm._initPools();
    const before = dm.kO.length;
    const drawn = dm.drawCardNoRepeat(true);
    assert.equal(drawn, '祖', '抽到武将字 祖');
    assert.equal(dm.kO.length, before - 1, '武将字抽后池缩减 1（splice 移除抽中项）');
    assert.ok(!dm.kO.includes('祖'), '池中不再含 祖');
  }
  // 子用例 3b：基础字不移除——抽到基础字时池不缩减（spec：基础单位字不移除）。
  {
    const { dm } = makeDeck({
      playerDup: false,
      pool: ['刀', '弓', '枪', '骑', '铲', '农', '刘', '祖'],
      rand: () => 0.0, // idx0 -> '刀'
    });
    dm._initPools();
    const before = dm.kO.length;
    const drawn = dm.drawCardNoRepeat(true);
    assert.equal(drawn, '刀', '抽到基础字 刀');
    assert.equal(dm.kO.length, before, '基础字抽后池不缩减（不移除）');
  }
  // 子用例 3c：Fi 置位——池中有两份同武将字，抽中后两份均移除（no-repeat 严格）。
  {
    const { dm } = makeDeck({
      playerDup: true, // Fi 置位
      pool: ['刀', '祖', '祖', '弓'], // 两份 祖
      rand: () => 0.5, // floor(0.5*4)=2 -> 第二个 祖
    });
    dm._initPools();
    const before = dm.kO.length;
    const drawn = dm.drawCardNoRepeat(true);
    assert.equal(drawn, '祖', '抽到武将字 祖');
    assert.equal(dm.kO.length, before - 2, 'Fi 置位时两份 祖 均移除（no-repeat，bundle:46519-46522）');
    assert.ok(!dm.kO.includes('祖'), '池中不再含 祖');
  }
  // 子用例 3d：AI 侧读 Oi（!side）——对称语义。
  {
    const { dm } = makeDeck({
      opponentDup: true, // Oi 置位
      pool: ['刀', '备', '备', '弓'],
      rand: () => 0.5, // idx2 -> 第二个 备
    });
    dm._initPools();
    const before = dm.SO.length;
    const drawn = dm.drawCardNoRepeat(false); // AI 侧
    assert.equal(drawn, '备', 'AI 侧抽到武将字 备');
    assert.equal(dm.SO.length, before - 2, 'Oi 置位时两份 备 均移除（AI 侧对称语义）');
  }
  // 子用例 3e：空池兜底 '刀'（bundle:46516）。
  {
    const { dm } = makeDeck({ pool: ['刀'] });
    dm._initPools();
    dm.kO.length = 0; // 清空玩家池
    assert.equal(dm.drawCardNoRepeat(true), '刀', '空池兜底返回 刀（bundle:46516）');
  }
});

// ============================================================
// 用例 4：xO 铲注入按 roundDay（roundDay<=3 每 5 铲注入 1 到双方牌池；>3 不注入）
// spec Scenario「铲注入按 roundDay」
// ============================================================
test('xO injectShovel：roundDay<=3 时每 5 铲注入 1 额外铲到双方牌池，roundDay>3 不注入', () => {
  // 子用例 4a：roundDay=1，11 铲 -> floor(11/5)=2 注入，双方牌池各 +2。
  {
    const { dm, player } = makeDeck({
      roundDay: 1, // winDay=0,loseDay=0 -> roundDay=1
      pool: Array.from({ length: 11 }, () => '铲'), // 11 铲
    });
    dm._initPools();
    assert.equal(player.roundDay, 1, 'PlayerDataCore.roundDay=1（winDay+loseDay+1）');
    assert.equal(dm.kO.length, 11, '注入前玩家池 11 铲');
    assert.equal(dm.SO.length, 11, '注入前 AI 池 11 铲');
    dm.injectShovel();
    assert.equal(dm.kO.length, 13, '注入后玩家池 +2 铲（floor(11/5)=2，bundle:46540）');
    assert.equal(dm.SO.length, 13, '注入后 AI 池 +2 铲（注入到双方牌池）');
  }
  // 子用例 4b：roundDay=3（边界），仍注入。
  {
    const { dm, player } = makeDeck({
      roundDay: 3,
      pool: Array.from({ length: 10 }, () => '铲'), // 10 铲 -> floor(10/5)=2
    });
    dm._initPools();
    assert.equal(player.roundDay, 3, 'roundDay=3 边界仍注入');
    dm.injectShovel();
    assert.equal(dm.kO.length, 12, 'roundDay=3 注入 2 铲');
    assert.equal(dm.SO.length, 12, 'roundDay=3 AI 池 +2 铲');
  }
  // 子用例 4c：roundDay>3，不注入。
  {
    const { dm, player } = makeDeck({
      roundDay: 5, // >3
      pool: Array.from({ length: 11 }, () => '铲'),
    });
    dm._initPools();
    assert.equal(player.roundDay, 5, 'roundDay=5 >3');
    const before = dm.kO.length;
    dm.injectShovel();
    assert.equal(dm.kO.length, before, 'roundDay>3 不注入（bundle:46536 直接返回）');
    assert.equal(dm.SO.length, before, 'roundDay>3 AI 池不变');
  }
  // 子用例 4d：不足 5 铲不注入（floor(<5/5)=0）。
  {
    const { dm } = makeDeck({
      roundDay: 1,
      pool: ['铲', '铲', '铲', '铲'], // 4 铲
    });
    dm._initPools();
    dm.injectShovel();
    assert.equal(dm.kO.length, 4, '4 铲 floor(4/5)=0 不注入');
    assert.equal(dm.SO.length, 4, 'AI 池不变');
  }
  // 子用例 4e：混合牌池，仅数铲计数（非铲字不计入）。
  {
    const { dm } = makeDeck({
      roundDay: 1,
      pool: ['铲', '刀', '铲', '弓', '铲', '枪', '铲', '骑', '铲', '刘', '铲'], // 6 铲 + 5 非铲
    });
    dm._initPools();
    dm.injectShovel();
    // 6 铲 -> floor(6/5)=1 注入
    assert.equal(dm.kO.length, 12, '6 铲 floor(6/5)=1 注入 1（非铲字不计入，bundle:46538）');
    assert.equal(dm.SO.length, 12, 'AI 池 +1 铲');
    assert.equal(countBy(dm.kO)['铲'], 7, '玩家池铲 6+1=7');
  }
});

// ============================================================
// 用例 5：dP 武将字 50% 复制（50% 概率复制武将字入池 + 置位 Fi/Oi）
// spec Scenario「武将字 50% 复制」
// ============================================================
test('dP copyGeneralChars：武将字 50% 概率复制入池 + 置位该侧 Fi/Oi 重复标志', () => {
  // 子用例 5a：random<0.5 复制——2 武将字均复制入池，Fi 置位。
  {
    const { dm, battle } = makeDeck({
      playerDup: false,
      pool: ['刀', '弓', '刘', '祖'], // 2 基础 + 2 武将字
      rand: () => 0.4, // <0.5 复制
    });
    dm._initPools();
    assert.equal(battle.playerDuplicateFlag, false, 'Fi 初始未置位');
    const before = dm.kO.length;
    dm.copyGeneralChars(true);
    assert.equal(dm.kO.length, before + 2, '2 武将字各复制 1 份入池（50% 概率命中，bundle:46563）');
    assert.equal(battle.playerDuplicateFlag, true, '复制后置位 Fi（bundle:46565）');
    // 复制项为武将字（刘/祖 各 +1）。
    assert.equal(countBy(dm.kO)['刘'], 2, '刘 复制后 2 份');
    assert.equal(countBy(dm.kO)['祖'], 2, '祖 复制后 2 份');
  }
  // 子用例 5b：random>=0.5 不复制——池不增长，但 Fi 仍置位（bundle:46565 末尾无条件置位）。
  {
    const { dm, battle } = makeDeck({
      playerDup: false,
      pool: ['刀', '弓', '刘', '祖'],
      rand: () => 0.6, // >=0.5 不复制
    });
    dm._initPools();
    const before = dm.kO.length;
    dm.copyGeneralChars(true);
    assert.equal(dm.kO.length, before, '武将字未复制（50% 未命中），池不增长');
    assert.equal(battle.playerDuplicateFlag, true, '即使未复制，Fi 仍置位（bundle:46565 无条件置位）');
  }
  // 子用例 5c：基础字不复制——只有武将字参与复制判定。
  {
    const { dm, battle } = makeDeck({
      pool: ['刀', '弓', '枪', '骑', '铲', '农'], // 全基础字
      rand: () => 0.1, // <0.5 但全是基础字
    });
    dm._initPools();
    const before = dm.kO.length;
    dm.copyGeneralChars(true);
    assert.equal(dm.kO.length, before, '全基础字时无武将字可复制，池不增长');
    assert.equal(battle.playerDuplicateFlag, true, 'Fi 仍置位');
  }
  // 子用例 5d：AI 侧（!side）置位 Oi。
  {
    const { dm, battle } = makeDeck({
      opponentDup: false,
      pool: ['刀', '弓', '刘', '祖'],
      rand: () => 0.4,
    });
    dm._initPools();
    dm.copyGeneralChars(false); // AI 侧
    assert.equal(battle.opponentDuplicateFlag, true, 'AI 侧复制后置位 Oi（对称语义）');
    assert.equal(battle.playerDuplicateFlag, false, '玩家侧 Fi 不受影响');
    assert.equal(dm.SO.length, 6, 'AI 池武将字复制 +2');
  }
  // 子用例 5e：50% 概率统计——大量武将字时复制数趋近半数（概率语义验证）。
  {
    // 20 个武将字（刘），random 均匀 [0,1)：约半数复制。
    const pool = ['刀', ...Array.from({ length: 20 }, () => '刘')];
    const { dm } = makeDeck({ pool, rand: Math.random });
    dm._initPools();
    const before = dm.kO.length;
    dm.copyGeneralChars(true);
    const copied = dm.kO.length - before;
    // 20 武将字 50% -> 期望 10，容忍 [3,17]（统计波动，避免 flaky）。
    assert.ok(copied >= 3 && copied <= 17, `20 武将字 50% 复制约 10（实际 ${copied}，容忍 [3,17]）`);
  }
});

// ============================================================
// 用例 6：qY AI 重排（Si<2 + 铲前置逐槽生成 handSize 张）
// spec Scenario「AI 侧重排铲前置」
// ============================================================
test('qY aiRearrange：Si<2 时铲前置排序逐槽生成 handSize 张手牌', () => {
  // 子用例 6a：Si<2，抽到铲与基础字，铲排在前置桶首位 -> 手牌首位为铲。
  {
    const { dm } = makeDeck({
      Si: 0, // Si<2
      pool: ['铲', '刀', '弓', '枪', '骑', '刘', '赵', '备'],
    });
    // 随机序列：首轮抽铲(idx0)，后续抽基础字（铲是基础字不 splice）。
    let i = 0;
    const seq = [0.0, 0.5, 0.6, 0.7, 0.8];
    dm.randomSource = () => seq[i++]; // 覆盖 randomSource
    dm._initPools();
    dm.hands.opponent = [];
    dm.aiRearrange();
    const hand = dm.hand(false);
    assert.equal(hand.length, DeckDefinitions.handSize, `逐槽生成 handSize=${DeckDefinitions.handSize} 张`);
    assert.equal(hand[0].text, '铲', 'Si<2 铲前置排序，手牌首位为铲（bundle:49579 前置桶）');
  }
  // 子用例 6b：Si>=2，铲仍入前置桶，非铲入后置桶再追加 -> 铲仍居首（高难度后置追加到前置）。
  {
    const { dm } = makeDeck({
      Si: 2, // Si>=2
      pool: ['铲', '刀', '弓', '枪', '骑', '刘', '赵', '备'],
    });
    let i = 0;
    const seq = [0.0, 0.5, 0.6, 0.7, 0.8];
    dm.randomSource = () => seq[i++];
    dm._initPools();
    dm.hands.opponent = [];
    dm.aiRearrange();
    const hand = dm.hand(false);
    assert.equal(hand.length, DeckDefinitions.handSize, 'Si>=2 仍生成 handSize 张');
    assert.equal(hand[0].text, '铲', 'Si>=2 铲仍前置（bundle:49579 铲恒入前置桶）');
  }
  // 子用例 6c：Si<2 全部前置——无铲时所有抽到的卡均入前置桶（顺序保持）。
  {
    const { dm } = makeDeck({
      Si: 0,
      pool: ['刀', '弓', '枪', '骑', '刘'], // 无铲
    });
    let i = 0;
    const seq = [0.0, 0.25, 0.5, 0.75, 0.99];
    dm.randomSource = () => seq[i++];
    dm._initPools();
    dm.hands.opponent = [];
    dm.aiRearrange();
    const hand = dm.hand(false);
    assert.equal(hand.length, DeckDefinitions.handSize, 'Si<2 无铲仍生成 handSize 张');
    // Si<2 所有卡入 front，handSize 张全填。
    assert.ok(hand.every((c) => c), '所有槽均填满');
  }
  // 子用例 6d：铲占空槽——抽到铲时该槽以铲卡占位（bundle:49594 k.setItem(null,d) 等价）。
  {
    const { dm } = makeDeck({
      Si: 0,
      pool: ['铲', '铲', '铲', '铲', '铲'], // 全铲
    });
    dm.randomSource = () => 0.0;
    dm._initPools();
    dm.hands.opponent = [];
    dm.aiRearrange();
    const hand = dm.hand(false);
    assert.equal(hand.length, DeckDefinitions.handSize, '全铲仍生成 handSize 张');
    assert.ok(hand.every((c) => c.text === '铲'), '全部以铲卡占槽（bundle:49594）');
  }
});

// ============================================================
// 用例 7：NY 两阶段清除（清除后 hand 槽为空）
// spec Scenario「刷新两阶段清除」
// ============================================================
test('NY clearSlotsTwoPhase：清除后手牌槽为空（逐槽 despawn + removeAll 等价）', () => {
  // 子用例 7a：玩家侧 5 槽清除后为空。
  {
    const { dm } = makeDeck({ pool: ['刀', '弓', '枪', '骑', '铲'] });
    dm._initPools();
    dm.hands.player = dm.drawHand(true); // 5 张
    assert.equal(dm.hand(true).length, DeckDefinitions.handSize, '清除前满槽');
    const cleared = dm.clearSlotsTwoPhase(true);
    assert.equal(cleared, DeckDefinitions.handSize, `返回清除槽位数=${DeckDefinitions.handSize}`);
    assert.equal(dm.hand(true).length, 0, '清除后玩家手牌槽为空（bundle:49613 逐槽+49614 removeAll）');
  }
  // 子用例 7b：AI 侧对称清除。
  {
    const { dm } = makeDeck({ pool: ['刀', '弓', '枪', '骑', '铲'] });
    dm._initPools();
    dm.hands.opponent = dm.drawHand(false); // 5 张
    assert.equal(dm.hand(false).length, DeckDefinitions.handSize, 'AI 侧清除前满槽');
    dm.clearSlotsTwoPhase(false);
    assert.equal(dm.hand(false).length, 0, 'AI 侧清除后为空（对称语义）');
  }
  // 子用例 7c：锁定卡保留（JS 重建保留 lock 语义，NY 不清除 locked 卡）。
  {
    const { dm } = makeDeck({ pool: ['刀', '弓', '枪', '骑', '铲'] });
    dm._initPools();
    dm.hands.player = dm.drawHand(true);
    dm.hands.player[2].locked = true; // 锁定槽 2
    dm.clearSlotsTwoPhase(true);
    const hand = dm.hand(true);
    assert.equal(hand.length, 1, '仅锁定卡保留，其余清除');
    assert.equal(hand[0].locked, true, '保留的为锁定卡');
  }
  // 子用例 7d：refresh 流程接入 NY——先清槽再重抽，刷新后仍满槽。
  {
    const { dm } = makeDeck({ pool: ['刀', '弓', '枪', '骑', '铲', '刘', '赵', '备'] });
    dm.startGame(); // 初始化牌池+手牌+injectShovel
    const handBefore = dm.hand(true).slice();
    assert.equal(handBefore.length, DeckDefinitions.handSize, '刷新前满槽');
    // refresh 内部先 clearSlotsTwoPhase 再重抽填槽（NY 两阶段）。
    const result = dm.refresh(true);
    assert.equal(result.success, true, 'refresh 成功');
    assert.equal(dm.hand(true).length, DeckDefinitions.handSize, 'refresh 后仍满槽（NY 清除+重抽填槽）');
    // 至少有一张卡文本变化（证明清除后重抽，非原槽保留）。
    const handAfter = dm.hand(true).map((c) => c.text);
    assert.ok(
      handBefore.some((c, idx) => c.text !== handAfter[idx]),
      '刷新后手牌至少一槽变化（NY 两阶段清除后重抽）',
    );
  }
  // 子用例 7e：AI 侧 refresh 走 qY 重排填槽（NY 清除 + aiRearrange）。
  {
    const { dm } = makeDeck({
      Si: 0,
      pool: ['铲', '刀', '弓', '枪', '骑', '刘', '赵', '备'],
    });
    dm.startGame();
    const lenBefore = dm.hand(false).length;
    assert.equal(lenBefore, DeckDefinitions.handSize, 'AI 侧刷新前满槽');
    const result = dm.refresh(false); // AI 侧 -> NY + qY
    assert.equal(result.success, true, 'AI 侧 refresh 成功');
    assert.equal(dm.hand(false).length, DeckDefinitions.handSize, 'AI 侧 refresh 后满槽（NY 清除+qY 重排填槽）');
  }
});
