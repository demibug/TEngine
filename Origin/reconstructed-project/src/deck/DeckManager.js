'use strict';
const { DeckDefinitions, BASE_SOLDIER_TEXTS } = require('./DeckDefinitions');
const { UnitCard } = require('./UnitCard');

/** Engine-independent reconstruction of vN + r0 refresh path. */
class DeckManager {
  constructor({ gameData, economy, randomSource = Math.random, logger = console, definitions = DeckDefinitions, minimalMode = false } = {}) {
    if (!gameData || !economy) throw new TypeError('DeckManager requires gameData and BattleEconomy');
    Object.assign(this, { gameData, economy, randomSource, logger, definitions });
    // 最简战斗模式开关（D1，默认 false 保持完整模式行为不变）：
    //   true  → drawText 只从 BASE_SOLDIER_TEXTS（刀/弓/枪/骑）抽取，injectShovel 跳过
    //   false → 走原 poolForSide（108 元素）逻辑，injectShovel 正常执行
    this.minimalMode = Boolean(minimalMode);
    this.hands = { player: [], opponent: [] };
    this.nextCardId = 1;
    this.started = false;
    // 运行时可变牌池（对应 bundle vN 的 kO/SO）：
    //   kO = 玩家侧牌池（bundle this["kO"] = uq.instance().eh.ah，108 元素 slice）
    //   SO = AI 侧牌池（bundle this["SO"] = uq.instance().eh.nh，108 元素 slice）
    // bO 抽牌（武将字 no-repeat splice）/ xO 铲注入（push）/ dP 武将字复制（push）
    // 均就地变异这两个数组，故必须与 definitions.basePool 解耦为独立可变副本。
    this.kO = null; // 玩家侧牌池（可变副本）
    this.SO = null; // AI 侧牌池（可变副本）
  }

  init() {}

  /**
   * 取 BattleState（gameData.battle，对应 bundle uq.instance().au）。
   * BattleState 含 aiDifficulty(Si)/playerDuplicateFlag(Fi)/opponentDuplicateFlag(Oi)（3.9 已加）。
   * @returns {object|null}
   * @private
   */
  _battle() { return this.gameData && this.gameData.battle ? this.gameData.battle : null; }

  /**
   * 取玩家数据（gameData.player，对应 bundle uq.instance().player）。
   * PlayerDataCore.roundDay getter = winDay+loseDay+1（bundle:8525-9429）。
   * xO 铲注入依赖 player.roundDay<=3（bundle:46536 uq.instance().player.roundDay）。
   * @returns {object|null}
   * @private
   */
  _player() { return this.gameData && this.gameData.player ? this.gameData.player : null; }

  /**
   * 初始化两侧可变牌池：从 definitions.basePool（108 元素 DECK_POOL，bundle:11969）
   * slice 出独立副本。对应 bundle vN.startGame/init（bundle:46473/46488）：
   *   this["kO"] = g["eh"]["ah"]["slice"](); this["SO"] = g["eh"]["nh"]["slice"]();
   * eh.ah/eh.nh 三组同数组（逐元素一致），故两侧初始均取 108 元素副本。
   * @private
   */
  _initPools() {
    const base = Array.from(this.definitions.basePool);
    this.kO = base.slice();
    this.SO = base.slice();
  }

  startGame() {
    this.started = true;
    this.nextCardId = 1;
    this._initPools();
    // bundle vN.startGame 末尾调 this["xO"]()（bundle:46497）——roundDay<=3 铲注入。
    this.injectShovel();
    this.hands.player = this.drawHand(true);
    this.hands.opponent = this.drawHand(false);
  }

  /**
   * 返回指定侧牌池的副本供外部只读/均匀抽取。
   *
   * 3.3 关键修复：不再优先 friendlyUnits.texts（src/units/UnitConfig.js BASE_SOLDIER_TEXTS
   * 仅 4 元素 ['刀','弓','枪','骑']，会屏蔽 DeckDefinitions 的 108 元素牌池）。
   * 现从运行时可变牌池 kO/SO（108 元素，bundle:11969）取副本；若未初始化则回退
   * definitions.basePool（108 元素 DECK_POOL）。SHALL NOT 仅从 4 元素均匀抽。
   *
   * @param {boolean} _side true=玩家侧(kO)，false=AI侧(SO)
   * @returns {string[]} 牌池副本（108 元素扁平数组，count 已展开，均匀抽即反映权重）
   */
  poolForSide(_side) {
    const pool = _side ? this.kO : this.SO;
    if (pool && pool.length) return Array.from(pool);
    return Array.from(this.definitions.basePool);
  }

  /**
   * 从指定侧牌池按权重抽取一个字（3.3）。
   *
   * 牌池为 108 元素扁平数组（count 已展开），下标均匀随机即等价按权重抽取
   * （bundle 中 np.range(0,k.length,true) 即下标随机）。空池兜底 '刀'。
   * 此为简单抽取路径（不 no-repeat）；武将字 no-repeat 抽取见 drawCardNoRepeat(bO)。
   *
   * D1 最简模式（this.minimalMode=true）：从 BASE_SOLDIER_TEXTS（刀/弓/枪/骑 4 元素）
   * 抽取，不抽农/铲/武将字，避免 UnitRegistry throw。minimalMode=false 走原 poolForSide。
   *
   * @param {boolean} side true=玩家侧，false=AI侧
   * @returns {string} 抽到的字（可为 铲/武将字），空池返回 '刀'
   */
  drawText(side) {
    // D1 最简模式：只从 4 元素基础兵抽取，绕过 108 元素 poolForSide
    if (this.minimalMode) {
      const pool = BASE_SOLDIER_TEXTS;
      const r = Math.max(0, Math.min(0.999999999, Number(this.randomSource()) || 0));
      return pool[Math.floor(r * pool.length)] || '刀';
    }
    const pool = this.poolForSide(side);
    const r = Math.max(0, Math.min(0.999999999, Number(this.randomSource()) || 0));
    return pool[Math.floor(r * pool.length)] || '刀';
  }

  createCard(text, level = 1, source = 'deck') { return new UnitCard({ id: this.nextCardId++, text, level, cost: Math.max(this.definitions.baseUnitCost, level), source }); }
  drawHand(side = true) { return Array.from({ length: this.definitions.handSize }, () => this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent')); }
  hand(side = true) { return this.hands[side ? 'player' : 'opponent']; }
  getCard(side, slot) { return this.hand(side)[slot] || null; }
  setHand(side = true, cards = []) {
    const key = side ? 'player' : 'opponent';
    this.hands[key] = cards.slice(0, this.definitions.handSize).map(card => {
      if (card instanceof UnitCard) return card;
      if (typeof card === 'string') return this.createCard(card, 1, side ? 'player' : 'opponent');
      return this.createCard(card.text, card.level || 1, card.source || (side ? 'player' : 'opponent'));
    });
    while (this.hands[key].length < this.definitions.handSize) this.hands[key].push(this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent'));
    return this.hands[key];
  }

  /**
   * bO 抽牌（武将字 no-repeat，bundle:46503-46528）。
   *
   * bundle vN.bO(a) 逻辑：
   *   1) au.Li.length>=2 → 走 Li 特殊武将合成列表抽牌（bundle:46514）。
   *      Li 语义未完整取证（武将合成相关列表），标 DEFERRED_LI_DRAW_LIST，
   *      走下方正常牌池抽取兜底（不阻塞铲/武将字抽取）。
   *   2) k = a ? this.kO : this.SO（side 选 pool，bundle:46515）。
   *   3) 空池 → 返回 '刀'（bundle:46516）。
   *   4) l = np.range(0,k.length,true) 随机下标，m = k[l]（bundle:46517-46518）。
   *   5) 若 k[l] 非基础字（≠刀/枪/弓/骑/铲/农，即武将字）→ splice(l,1) 移除该抽中项
   *      （bundle:46519）；且当 (a&&Fi) || (!a&&Oi) 重复标志置位时，再 indexOf(m)
   *      splice 移除池中另一份同字（确保武将字不重复抽取，bundle:46519-46522）。
   *   6) 返回 m。
   *
   * 基础字判定表（bundle:46519）：刀/枪/弓/骑/铲/农——非此六者即武将字，抽后移除。
   * Fi/Oi 从 BattleState.playerDuplicateFlag/opponentDuplicateFlag 读（3.9 已加）。
   *
   * DEFERRED_LI_DRAW_LIST: au.Li（武将合成特殊抽牌列表，bundle:46514）完整语义
   * 待 ② 武将系统还原后取证；当前走正常牌池兜底。
   *
   * @param {boolean} side true=玩家侧(kO,读Fi)，false=AI侧(SO,读Oi)
   * @returns {string} 抽到的字；武将字抽后从池移除（no-repeat）；空池返回 '刀'
   */
  drawCardNoRepeat(side) {
    const battle = this._battle();
    // DEFERRED_LI_DRAW_LIST: bundle:46514 au.Li.length>=2 走 Li 特殊列表。
    // Li 为武将合成相关列表，完整语义未取证（依赖 ② 武将系统），此处不消费 Li，
    // 统一走下方正常牌池抽取兜底，保证铲/武将字抽取可达。
    const pool = side ? this.kO : this.SO;
    if (!pool || pool.length === 0) return '刀'; // bundle:46516 空池兜底
    const r = Math.max(0, Math.min(0.999999999, Number(this.randomSource()) || 0));
    const l = Math.floor(r * pool.length); // bundle:46517 np.range 下标
    const m = pool[l]; // bundle:46518
    // 基础字判定表（bundle:46519）：非基础字（武将字）抽后 splice 移除
    const isBase = m === '刀' || m === '枪' || m === '弓' || m === '骑' || m === '铲' || m === '农';
    if (!isBase) {
      pool.splice(l, 1); // bundle:46519 移除抽中项
      // 重复标志置位时再移除池中另一份同字（武将字 no-repeat，bundle:46519-46522）
      // bundle: (a && au.Fi) || (!a && au.Oi)
      const dupFlag = side ? (battle && battle.playerDuplicateFlag) : (battle && battle.opponentDuplicateFlag);
      if (dupFlag) {
        const idx = pool.indexOf(m); // bundle:46520
        if (idx >= 0) pool.splice(idx, 1); // bundle:46521
      }
    }
    return m;
  }

  /**
   * xO 铲注入（bundle:46529-46545）。
   *
   * bundle vN.xO() 逻辑：
   *   1) uq.instance().player.roundDay > 3 → 直接返回（仅 roundDay<=3 注入，bundle:46536）。
   *   2) 数 kO 中 '铲' 数量 f（bundle:46538）。
   *   3) f = Math.floor(f/5)（每 5 铲注入 1，bundle:46539）。
   *   4) 循环 f 次：kO.push('铲') + SO.push('铲')（注入到双方牌池，bundle:46540）。
   *
   * roundDay 从 gameData.player.roundDay 读（PlayerDataCore.roundDay = winDay+loseDay+1）。
   * roundDay 访问途径：经构造注入的 gameData.player（CriticalGameState.player getter）。
   * 若 player 不可达（gameData 无 player 引用）则视为 roundDay>3 不注入（安全兜底）。
   *
   * 由 startGame 末尾调用（bundle:46497），亦可在 round 切换时按需调用。
   */
  injectShovel() {
    // D1 最简模式：跳过铲子注入（牌池不注入额外铲，保持只含基础兵）
    if (this.minimalMode) return;
    const player = this._player();
    if (!player) return; // player 不可达：安全兜底不注入
    const roundDay = Number(player.roundDay) || 0;
    if (roundDay > 3) return; // bundle:46536 仅 roundDay<=3 注入
    if (!this.kO) return;
    let f = 0;
    for (let i = 0; i < this.kO.length; i += 1) { // bundle:46538 数 kO 中铲
      if (this.kO[i] === '铲') f += 1;
    }
    f = Math.floor(f / 5); // bundle:46539 每 5 铲注入 1
    for (let b = 0; b < f; b += 1) { // bundle:46540 注入双方牌池
      this.kO.push('铲');
      if (this.SO) this.SO.push('铲');
    }
  }

  /**
   * dP 武将字复制（bundle:46546-46574）。
   *
   * bundle vN.dP(a) 逻辑：
   *   1) 基础字表 g = ['刀','弓','枪','骑','铲','农']（bundle:46553）。
   *   2) h = a ? this.kO : this.SO（side 选 pool，bundle:46554）。
   *   3) 遍历 h（i = h.length 快照，bundle:46555）：
   *      对每个字，若非基础字（武将字）且 Math.random()<.5 → h.push(h[a]) 复制一份入池
   *      （bundle:46563）。
   *   4) 末尾 a ? au.Fi=true : au.Oi=true（置位该侧重复标志，bundle:46565）。
   *
   * DEFERRED_GENERAL_MERGE: 武将字复制使牌池中武将字可达且可重复（配合 bO no-repeat
   * 抽取）；但武将字合成端到端验证（合成产物/合成 UI/合成后单位属性）依赖 ② 武将系统
   * 完整还原，本提案仅保证牌池武将字可达 + dP 复制置位 Fi/Oi，合成端到端验证待 ②。
   *
   * @param {boolean} side true=玩家侧(kO,置Fi)，false=AI侧(SO,置Oi)
   */
  copyGeneralChars(side) {
    const battle = this._battle();
    const pool = side ? this.kO : this.SO;
    if (!pool) return;
    const baseTable = ['刀', '弓', '枪', '骑', '铲', '农']; // bundle:46553 基础字判定表
    const i = pool.length; // bundle:46555 快照长度（仅遍历初始部分，复制项不参与本轮）
    for (let a = 0; a < i; a += 1) {
      let isBase = false;
      for (let b = 0; b < baseTable.length; b += 1) { // bundle:46559-46562 基础字判定
        if (pool[a] === baseTable[b]) { isBase = true; break; }
      }
      // bundle:46563 非基础字（武将字）50% 概率复制入池
      if (!isBase && this.randomSource() < 0.5) pool.push(pool[a]);
    }
    // bundle:46565 置位该侧重复标志（Fi/Oi），供 bO 武将字 no-repeat 消费
    if (battle) {
      if (side) battle.playerDuplicateFlag = true; // au.Fi
      else battle.opponentDuplicateFlag = true; // au.Oi
    }
    // DEFERRED_GENERAL_MERGE: 武将字合成端到端验证待 ② 武将系统完整还原。
  }

  /**
   * qY AI 重排（bundle:49563-49595）。
   *
   * bundle r0.qY() 逻辑（AI 侧刷新后重排手牌）：
   *   1) k = this.WO.ub(3,false)（AI 手牌槽容器，bundle:49575）——JS 等价 hand(false)。
   *   2) n = this.sw.map.fe（抽牌数=handSize=5，bundle:49575）。
   *   3) 循环 n 次：a = vN.instance().bO(false)（AI 侧 no-repeat 抽牌，bundle:49578）。
   *   4) Si<2 || '铲'===a → 前置列表 l.push（低难度+铲前置，bundle:49579）；
   *      否则后置列表 m.push（bundle:49579）。
   *   5) Si>=2 → 把后置 m 全部追加到 l（高难度不前置，bundle:49581-49582）。
   *   6) 逐槽 d 生成：'铲'!==f → gP(3,f,false,d) 生成单位卡入槽（bundle:49588）；
   *      '铲'===f → k.setItem(null,d)（铲占空槽，bundle:49594）。
   *
   * Si 从 BattleState.aiDifficulty 读（3.9 已加，⑥ aiDifficulty 字段）。gP 单位生成
   * 在 JS 重建中无对应独立工厂，此处以 createCard 等价填槽（规则层牌池可达即可，
   * 实际单位生成由 UnitRegistry 承载，非 DeckManager 职责）。
   *
   * 铲拆分前置排序：Si<2（低难度）时铲与非铲分桶，铲入前置 l、非铲入后置 m，
   * 最终逐槽按 l 顺序填入——等价 bundle 铲前置排序后逐槽生成。
   */
  aiRearrange() {
    const battle = this._battle();
    const Si = battle ? Number(battle.aiDifficulty) || 0 : 0; // bundle sw.au.Si
    const n = this.definitions.handSize; // bundle sw.map.fe（=5）
    const front = []; // l：前置桶（铲 + Si<2 卡）
    const back = []; // m：后置桶（Si>=2 非铲卡）
    for (let a = 0; a < n; a += 1) { // bundle:49576 抽 n 卡
      const card = this.drawCardNoRepeat(false); // bundle:49578 vN.bO(false)
      if (Si < 2 || card === '铲') front.push(card); // bundle:49579 前置
      else back.push(card); // bundle:49579 后置
    }
    if (Si >= 2) { // bundle:49581-49582 高难度后置追加到前置
      for (let a = 0; a < back.length; a += 1) front.push(back[a]);
    }
    // bundle:49583-49595 逐槽生成
    const hand = this.hand(false);
    for (let d = 0; d < front.length && d < this.definitions.handSize; d += 1) {
      const f = front[d];
      if (f !== '铲') {
        // bundle:49588 gP(3,f,false,d) 生成单位卡——JS 以 createCard 等价填槽
        hand[d] = this.createCard(f, 1, 'opponent');
      } else {
        // bundle:49594 铲占空槽（k.setItem(null,d)）——JS 以铲卡占槽
        hand[d] = this.createCard('铲', 1, 'opponent');
      }
    }
    // 不足 handSize 的槽补抽（防御，保持手牌满槽契约）
    while (hand.length < this.definitions.handSize) {
      hand.push(this.createCard(this.drawText(false), 1, 'opponent'));
    }
  }

  /**
   * NY 两阶段清除（bundle:49597-49614）。
   *
   * bundle r0.NY(a) 逻辑：刷新时先清空指定侧手牌槽（逐槽 despawn：WP/HP/Nb/cA，
   * bundle:49613），再 removeAll（bundle:49614），然后由 xY 重抽填槽。
   *
   * JS 重建中手牌为 UnitCard 数组（无引擎节点），两阶段语义等价为：
   *   阶段1：清空 handSize 个槽（置 null/移除，对应逐槽 despawn）。
   *   阶段2：由调用方 refresh 重抽填槽。
   *
   * 本方法仅执行阶段1清除，返回被清的槽位数供 refresh 重抽。
   *
   * @param {boolean} side true=玩家侧，false=AI侧
   * @returns {number} 被清除的槽位数（=handSize）
   */
  clearSlotsTwoPhase(side) {
    const key = side ? 'player' : 'opponent';
    const hand = this.hands[key];
    const slots = this.definitions.handSize;
    // 阶段1：逐槽清除（bundle:49613 逐槽 despawn 等价）
    for (let a = 0; a < hand.length && a < slots; a += 1) {
      // 锁定卡保留（尊重既有 lock 契约，bundle NY 无 lock 概念但 JS 重建保留 lock 语义）
      if (hand[a] && hand[a].locked) continue;
      hand[a] = null;
    }
    // bundle:49614 removeAll——清除 null 槽（保留 locked 卡）
    for (let a = hand.length - 1; a >= 0; a -= 1) {
      if (hand[a] === null) hand.splice(a, 1);
    }
    return slots;
  }

  /**
   * 刷新手牌（bundle xY，bundle:49525-49561）。
   *
   * bundle xY(a) 逻辑：
   *   1) 金币不足 → {success:false,reason:'馒头不足'}（cost 由 BattleEconomy.payRefresh 忠实）。
   *   2) 扣费 + fi/gi += 2（BattleEconomy.payRefresh 忠实）。
   *   3) this.NY(a) 两阶段清除（bundle:49552）。
   *   4) !a（AI 侧）→ this.qY() AI 重排 + this.PY()（bundle:49552）。
   *   5) 返回 {success:true}。
   *
   * cost 逻辑（10 基础 +2/次、馒头不足）已由 BattleEconomy.payRefresh 忠实还原，
   * 此处保留。3.8 在 refresh 中接入 NY 两阶段清除：先 clearSlotsTwoPhase 清槽，
   * 再重抽填槽（玩家侧）；AI 侧由 aiRearrange 接管填槽（qY）。
   *
   * 保持既有调用契约：BattleInputController.execute(REFRESH) → deckManager.refresh(side)，
   * 返回 {success,cost,nextCost,hand} 或 {success:false,reason}。
   *
   * @param {boolean} side true=玩家侧，false=AI侧
   * @returns {object} 刷新结果
   */
  refresh(side = true) {
    if (!this.started) throw new Error('DeckManager.startGame() must run before refresh');
    const result = this.economy.payRefresh(side);
    if (!result.success) return result;
    // NY 两阶段清除（bundle:49552 this.NY(l)）：先清 5 槽
    this.clearSlotsTwoPhase(side);
    if (!side) {
      // bundle:49552 !l → this.qY() AI 重排填槽
      this.aiRearrange();
    } else {
      // 玩家侧重抽填槽（阶段2，bundle xY 隐式重抽）
      const hand = this.hand(side);
      while (hand.length < this.definitions.handSize) {
        hand.push(this.createCard(this.drawText(side), 1, 'player'));
      }
    }
    const hand = this.hand(side);
    // 确保锁定卡保留后仍满槽（防御）
    for (let i = 0; i < hand.length; i += 1) {
      if (!hand[i]) hand[i] = this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent');
    }
    return { success: true, cost: result.amount, nextCost: result.nextCost, hand: hand.map(c => c.toJSON()) };
  }

  consume(side, slot) { const hand = this.hand(side); const card = hand[slot]; if (!card) return null; hand[slot] = this.createCard(this.drawText(side), 1, side ? 'player' : 'opponent'); return card; }
  lock(side, slot, locked = true) { const card = this.getCard(side, slot); if (!card) return false; card.locked = Boolean(locked); return true; }
  gameOver() { this.started = false; this.hands.player.length = 0; this.hands.opponent.length = 0; this.kO = null; this.SO = null; }
  snapshot() { return { player: this.hands.player.map(c => c.toJSON()), opponent: this.hands.opponent.map(c => c.toJSON()) }; }
}
module.exports = { DeckManager };
