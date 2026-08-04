'use strict';

const { BattleInputCommand, BattleInputCommandType } = require('../input/BattleInputCommand');
const { GameEvents } = require('../core/EventBus');
const { AIDifficultyConfig } = require('./AIDifficultyConfig');
const { AIDeploymentController } = require('./AIDeploymentController');
const { AIPlanningController } = require('./AIPlanningController');
const { AITemplateResolver } = require('./AITemplateResolver');

/**
 * AI 主控制器（对应 bundle 的 vS/b 类，bundle:49660-50160）。
 *
 * 角色：对手（nm=false）AI 决策中枢。持 5 步状态机 TG（bundle:49819-49831），
 * 按 fG 决策间隔（难度 0-3 = 2000/1500/1000/500ms）调度 bG 部署子控制器与
 * MG 规划子控制器，并承担刷牌/周期收入/道具使用/分层放置/快速结束等职责。
 *
 * 字段映射（命名沿用 bundle 符号，等价结构见各字段注释）：
 *   Si  难度档 0-3（au.Si，bundle:3177）
 *   fG  决策间隔 ms（bundle:49740）
 *   yG  累计时间 ms（bundle:49796）
 *   step 状态机当前步（bundle:49819）
 *   XX  step2 布阵计数器（bundle:49825，>=5 进 step3）
 *   KX  step3 棋盘扫描游标 [0,0]（bundle:49826）
 *   cG  step5 落子游标 [0,0]（bundle:49831）
 *   sG  规划辅助列表（bundle:49828）
 *   rp  存活单位 id 列表（bundle:49826/49828）
 *   nG  棋盘缓存二维数组（bundle:49710/49829/49831）
 *   kG  UG 已触发守护（bundle:50139）
 *   SG  XG 已触发守护（bundle:50153）
 *   xG  道具冷却时间戳（bundle:50074）
 *   zX  WX 候选格缓存（bundle:49904）
 *   GX  当前模板 Map（AG 返回值，bundle:49740）
 *   bG  AIDeploymentController 实例（bundle:49708）
 *   MG  AIPlanningController 实例（bundle:49708）
 *
 * 等价结构翻译依据：
 *   uq.instance().au        → gameData.battle（BattleState：Ji=opponentGold/gi=opponentRecruitCost/
 *                            li=currentRound/Xi=opponentPlacementComplete/Si=aiDifficulty/ki=standardBattleDelayEnabled）
 *   uq.instance().map       → mapData（MapData：pe=棋盘 tile 矩阵=this.map、me=opponentRoute、width/height）
 *   uq.instance().map.pe    → mapData.map（tile 字符矩阵，blockAt(x,y)===mapData.map[x][y]）
 *   this.PA                 → deckManager（na.instance().ub(1,false) 棋盘容器等价）；PA.sb 棋盘矩阵
 *                            经 unitRegistry+mapData 翻译（见 WX/PA_sb）
 *   this.hX                 → deckManager.hand(false)（na.instance().ub(3,false) 手牌池等价）
 *   r0.instance()._Y        → inputController.execute（type:1=PURCHASE_AND_PLACE/type:2=REFRESH）
 *   nx.instance().La/wa     → gameLoop.register/unregister
 *   oc.instance.on/event    → eventBus.on/event
 *   vb.instance().KP        → itemSlots（道具栏，可注入；默认空，YO 不触发）
 *   np.Ys/np.range          → randomSource.shuffle/randomInt（Fisher-Yates 等价）
 *
 * 设计原则（决策 1/6）：组合非继承；状态机字段集中持有；DEFERRED 项不抛异常不阻塞状态机。
 */
class AIController {
  /**
   * @param {object} deps
   * @param {object} deps.gameLoop GameLoop（nx 等价），register/unregister 推进 update
   * @param {object} deps.gameData GameDataCore，battle=BattleState、map=MapData
   * @param {object} deps.deckManager DeckManager（PA/hX 等价），hand(side)/refresh
   * @param {object} deps.inputController BattleInputController（r0 等价），execute(BattleInputCommand)
   * @param {object} [deps.randomSource] 随机源（np 等价），需 random()/shuffle?/randomInt?
   * @param {object} [deps.logger] 日志器，默认 console
   * @param {object} [deps.eventBus] EventBus（oc 等价），订阅 ROUND_SPAWN_PREPARED
   * @param {object} [deps.economy] BattleEconomy，award/refreshCost（PA 金币操作等价）
   * @param {object} [deps.mapData] MapData（uq.instance().map 等价），blockAt/width/height/me
   * @param {object} [deps.unitRegistry] UnitRegistry（vc 等价），uM 查单位/hasBattleOccupant
   * @param {object} [deps.itemEffectDispatcher] 道具效果分派器（nO/pM/o0/tE 等价聚合），
   *   默认 DEFERRED 桩 use() 返回 {success:false}；标 DEFERRED_ITEM_SYSTEM
   * @param {Array} [deps.itemSlots] 道具栏（vb.KP 等价），默认空数组（YO 不触发）
   * @param {object} [deps.rankTableResolver] rank 表解析器，默认 DEFERRED 桩 no-op 钳制 0-3
   */
  constructor({
    gameLoop, gameData, deckManager, inputController,
    randomSource = Math, logger = console,
    eventBus = null, economy = null, mapData = null, unitRegistry = null,
    itemEffectDispatcher = null, itemSlots = null, rankTableResolver = null,
  } = {}) {
    if (!gameLoop || !gameData || !deckManager || !inputController) {
      throw new TypeError('AIController requires gameLoop, gameData, deckManager and inputController');
    }
    Object.assign(this, {
      gameLoop, gameData, deckManager, inputController,
      randomSource, logger,
      eventBus, economy, mapData, unitRegistry,
    });
    // DEFERRED_ITEM_SYSTEM: 道具效果分派器默认桩（nO/pM/o0/tE 聚合等价未取证）
    this.itemEffectDispatcher = itemEffectDispatcher || AIController._defaultItemEffectDispatcher(this);
    // vb.KP 等价道具栏；默认空数组，YO 因 length===0 不触发
    this.itemSlots = itemSlots || [];
    // DEFERRED_RANK_TABLE: rank 表跨档解析器默认桩（no-op 钳制 0-3）
    this.rankTableResolver = rankTableResolver || AIController._defaultRankTableResolver();

    this.started = false;
    this.actions = [];
    // 状态机字段（startGame 重置，构造仅声明默认避免 undefined 访问）
    this.Si = 0;          // au.Si 难度档
    this.fG = 2000;       // 决策间隔 ms（难度 0 默认，startGame 按 Si 覆盖）
    this.yG = 0;          // 累计时间
    this.step = 1;        // 状态机步
    this.XX = 0;          // step2 布阵计数
    this.KX = [0, 0];     // step3 棋盘扫描游标
    this.cG = [0, 0];     // step5 落子游标
    this.sG = [];         // 规划辅助列表
    this.rp = [];         // 存活单位 id 列表
    this.nG = [];         // 棋盘缓存二维数组
    this.kG = false;      // UG 守护
    this.SG = false;      // XG 守护
    this.xG = 0;          // 道具冷却时间戳
    this.zX = [];         // WX 候选格缓存
    this.GX = null;       // 当前模板 Map（AG 返回值）
    this._pgBound = false; // PG 是否已订阅（防重复）
    // 子控制器（startGame 实例化；构造先置 null 避免 update 提前访问）
    this.bG = null;       // AIDeploymentController
    this.MG = null;       // AIPlanningController
    this.templateResolver = null; // AITemplateResolver（AG 等价）
    // 难度配置解析器（My 等价）
    this._difficultyConfig = new AIDifficultyConfig();
  }

  init() {}

  /**
   * BattleState 访问快捷（uq.instance().au 等价）。
   * @returns {object} BattleState
   * @private
   */
  _au() { return this.gameData.battle; }

  /**
   * MapData 访问快捷（uq.instance().map 等价）；缺失返回 null。
   * 优先用注入 mapData，其次 gameData.map。
   * @returns {object|null}
   * @private
   */
  _map() { return this.mapData || (this.gameData && this.gameData.map) || null; }

  /**
   * 当前时间戳（nx.instance().fa 等价，bundle:50073）。
   * 缺失时回退 Date.now()。
   * @returns {number}
   * @private
   */
  _now() {
    if (this.gameLoop && typeof this.gameLoop.elapsed === 'number') return this.gameLoop.elapsed;
    return Date.now();
  }

  /**
   * 随机浮点 [0,1)（Math.random 等价，bundle:49822 Math.random）。
   * @returns {number}
   * @private
   */
  _random() {
    const r = this.randomSource;
    if (r && typeof r.random === 'function') return r.random();
    if (typeof r === 'function') return r();
    return Math.random();
  }

  /**
   * Fisher-Yates 洗牌（np.Ys 等价，bundle:49912/49951）。
   * 原地打乱 arr 并返回。
   * @param {Array} arr
   * @returns {Array}
   * @private
   */
  _shuffle(arr) {
    const rs = this.randomSource;
    if (rs && typeof rs.shuffle === 'function') { rs.shuffle(arr); return arr; }
    for (let i = arr.length - 1; i > 0; i -= 1) {
      const j = Math.floor(this._random() * (i + 1));
      [arr[i], arr[j]] = [arr[j], arr[i]];
    }
    return arr;
  }

  /**
   * 随机整数 [min,max)（np.range(min,max,true) 等价，bundle:50078）。
   * @param {number} min 含
   * @param {number} max 不含
   * @returns {number}
   * @private
   */
  _randomInt(min, max) {
    const rs = this.randomSource;
    if (rs && typeof rs.randomInt === 'function') return rs.randomInt(min, max);
    return min + Math.floor(this._random() * (max - min));
  }

  /**
   * 棋盘矩阵 PA.sb 翻译（bundle:49903 this.PA.sb）。
   * bundle 中 PA.sb 是棋盘单位二维矩阵 [x][y]，null 表示空格。
   * 翻译依据：MapData 无 PA.sb 等价字段，经 unitRegistry.hasBattleOccupant(side,x,y)
   * 判定占用（side=false 对手侧）。返回 true 表示 (x,y) 空闲可放置。
   * @param {number} x
   * @param {number} y
   * @returns {boolean} true=空格（null==PA.sb[x][y] 等价）
   * @private
   */
  _isBoardEmpty(x, y) {
    const reg = this.unitRegistry;
    if (reg && typeof reg.hasBattleOccupant === 'function') {
      try { return !reg.hasBattleOccupant(false, x, y); } catch (_) { /* 防御 */ }
    }
    // DEFERRED: unitRegistry 缺失时视为空（不阻塞 WX 候选收集）
    return true;
  }

  // ===== 难度接入（T12/T11）=====

  /**
   * startGame（bundle:49724-49745）。
   * 读 Si 钳制 0-3，经 AIDifficultyConfig.resolve 取 fG/ni/ri/ii/ei/hi/itemCooldownMs；
   * 初始化状态机字段；实例化 bG/MG/templateResolver；Ji += hi（初始加钱 10）；
   * 注册 update；订阅 PG；调 AG/EG/BG 算模板/路线点/分带。
   *
   * 等价 bundle:49737 `if(!uq.au.ki) return` —— ki=standardBattleDelayEnabled，
   * 为兼容冒烟（ki 默认 true）不复制此守卫；若 ki=false 则跳过 AI 启动。
   */
  startGame() {
    const au = this._au();
    if (au && au.standardBattleDelayEnabled === false) return; // bundle:49737 ki 守卫等价

    // T12：读 Si 钳制 0-3，解析难度配置
    const Si = Math.min(3, Math.max(0, (au && typeof au.aiDifficulty === 'number' ? au.aiDifficulty : 0) | 0));
    this.Si = Si;
    const cfg = this._difficultyConfig.resolve(Si);
    this.fG = cfg.fG;
    this._ni = cfg.ni;             // step1 快速结束概率（resolve 返回标量，T9 契约；TG step1 用 this._ni 直接比较，等价 My.ni[Si]）
    this._ri = cfg.ri;             // XG 触发概率（resolve 返回标量，T9 契约；onPlayerDanger 按 this._ri 概率触发 XG，等价 My.ri[Si]，bundle:57469）
    this._ii = cfg.ii;             // 周期收入（resolve 返回 ii[Si] 行，PG 用 _iiRow[i]）
    this._iiRow = cfg.ii;          // 当前 Si 行（resolve 已按 Si 索引），PG 用 _iiRow[i] 等价 ii[Si][i]
    this._ii2d = this._difficultyConfig.raw.ii; // 完整 ii[Si][i] 二维表（bundle:3155-3159 语义）
    this._ei = cfg.ei;             // 波次表
    this._hi = cfg.hi;             // 初始加钱
    this._itemCooldownMs = cfg.itemCooldownMs; // 道具冷却 ms

    // T16：初始化状态机字段
    this.step = 1;
    this.yG = 0;
    this.XX = 0;
    this.KX = [0, 0];
    this.cG = [0, 0];
    this.sG = [];
    this.rp = [];
    this.kG = false;
    this.SG = false;
    this.xG = 0; // 道具冷却时间戳归零（首次 YO 立即可触发）
    this.zX = [];

    // nG 棋盘缓存（bundle:49710：map.pe.length × map.pe[0].length/2 的 null 二维数组）
    const map = this._map();
    if (map && typeof map.width === 'number' && typeof map.height === 'number') {
      const w = map.width;
      const h = Math.floor(map.height / 2);
      this.nG = Array.from({ length: w }, () => new Array(h).fill(null));
    } else {
      // 简化兜底：空数组（bundle 翻译依据缺失时）
      this.nG = [];
    }

    // 实例化子控制器（组合，bundle:49708 this.bG=new ne(this)/this.MG=new vR(this)）
    this.bG = new AIDeploymentController(this);
    this.MG = new AIPlanningController(this);
    this.templateResolver = new AITemplateResolver(this, map);

    // T12：初始加钱 Ji += hi（bundle:49740 `uq.au.Ji += uq.My.hi`）
    if (au && typeof au.opponentGold === 'number') au.opponentGold += this._hi;

    // bundle:49740 this.GX=this.AG(l,m)：算当前模板 Map（mapIndex, Si<2 simplified）
    const mapIndex = (map && typeof map.mapIndex === 'number') ? map.mapIndex : 0;
    const simplified = Si < 2;
    try { this.GX = this.templateResolver.AG(mapIndex, simplified); } catch (_) { this.GX = null; }
    // bundle:49740 this.dG=this.EG(l)：路线点缓存
    try { this.templateResolver.EG(mapIndex); } catch (_) { /* 防御 */ }
    // bundle:49740 this.BG()：算分带 wG/vG/_G
    try { this.templateResolver.BG(); } catch (_) { /* 防御 */ }

    this.started = true;
    this.actions.length = 0;

    // 注册 update（bundle:49740 nx.La("AICtr",this,this.update)）
    this.gameLoop.register('AIController', this, this.update);

    // T19：订阅 ROUND_SPAWN_PREPARED → PG（bundle:49712 oc.on(sS.Jt,this,this.PG)）
    if (this.eventBus && !this._pgBound) {
      this.eventBus.on(GameEvents.ROUND_SPAWN_PREPARED, this, this.PG);
      this._pgBound = true;
    }
  }

  /**
   * update（bundle:49791-49800）。
   * yG+=deltaMs；yG>=fG 时 yG=0 调 TG(deltaMs)。
   * !started || isGameOver 时 return。
   * @param {number} deltaMs
   */
  update(deltaMs) {
    if (!this.started || (this._au() && this._au().isGameOver)) return;
    this.yG += deltaMs;
    if (this.yG >= this.fG) {
      this.yG = 0;
      this.TG(deltaMs);
    }
  }

  /**
   * TG 5 步状态机（bundle:49819-49831）。
   * @param {number} deltaMs
   */
  TG(deltaMs) {
    const au = this._au();
    if (this.step === 1) {
      // bundle:49820 Ji>=gi → refresh+XX=0+step=2
      if (au.opponentGold >= au.opponentRecruitCost) {
        this.refresh();
        this.XX = 0;
        this.step = 2;
      } else {
        // bundle:49822 random<=ni[Si] → UG；否则 YO
        // 注：AIDifficultyConfig.resolve(Si) 返回标量 ni（已按 Si 索引，T9 契约），
        // 故此处用标量 this._ni 直接比较，等价 bundle `Math.random() <= My.ni[Si]`。
        if (this._random() <= this._ni) {
          this.UG();
          return; // bundle:49822 void return
        }
        this.YO();
      }
    } else if (this.step === 2) {
      // bundle:49825 Xi=true；bG.YX()；XX>=5 → 清 rp/KX + step=3
      if (!au.opponentPlacementComplete) au.opponentPlacementComplete = true;
      if (this.bG) this.bG.YX();
      if (this.XX >= 5) {
        this.rp.length = 0;
        this.KX[0] = 0;
        this.KX[1] = 0;
        this.step = 3;
      }
    } else if (this.step === 3) {
      // bundle:49826 KX[0] < PA.sb.length → bG.ZX()；否则 step=4
      const sbLen = this._boardWidth();
      if (this.KX[0] < sbLen) {
        if (this.bG) this.bG.ZX();
      } else {
        this.step = 4;
      }
    } else if (this.step === 4) {
      // bundle:49828 rp=rp.filter(id→uG(id)!=null)；sG.length=0；MG.tG/iG/hG；清 nG；MG.aG；cG=[0,0]；step=5
      this.rp = this.rp.filter(id => this.uG(id) != null);
      this.sG.length = 0;
      if (this.MG) { this.MG.tG(); this.MG.iG(); this.MG.hG(); }
      // bundle:49829 for(const a of this.nG) a.fill(null)
      for (const col of this.nG) { if (Array.isArray(col)) col.fill(null); }
      if (this.MG) this.MG.aG();
      this.cG[0] = 0;
      this.cG[1] = 0;
      this.step = 5;
    } else if (this.step === 5) {
      // bundle:49831 cG[0] < nG.length → MG.lG()；否则 step=1
      if (this.cG[0] < this.nG.length) {
        if (this.MG) this.MG.lG();
      } else {
        this.step = 1;
      }
    }
  }

  /**
   * PA.sb.length 翻译（bundle:49826/49903 this.PA.sb.length）。
   * PA.sb 是棋盘矩阵 [x][y]，length=列数（width）。翻译依据：MapData.width。
   * 缺失返回 0（step3 直接进 step4）。
   * @returns {number}
   * @private
   */
  _boardWidth() {
    const map = this._map();
    if (map && typeof map.width === 'number') return map.width;
    return 0;
  }

  /**
   * gameOver（bundle:49777-49790）。
   * 重置 step/yG/XX/KX/cG/sG/kG/SG；注销 update。
   * 注意 bundle:49785 不重置 nG/rp/zX（仅 length=0 sG）；本实现按 bundle 严格还原。
   */
  gameOver() {
    const au = this._au();
    if (au && au.standardBattleDelayEnabled === false) return; // bundle:49785 ki 守卫等价
    this.gameLoop.unregister('AIController');
    this.zX.length = 0;
    this.yG = 0;
    this.XX = 0;
    this.KX[0] = 0;
    this.KX[1] = 0;
    this.cG[0] = 0;
    this.cG[1] = 0;
    this.step = 1;
    this.sG.length = 0;
    this.kG = false;
    this.SG = false;
    this.started = false;
    // 注销 PG 订阅防泄漏（bundle 未显式 off，但实例复用场景需清理）
    if (this.eventBus && this._pgBound) {
      try { this.eventBus.off(GameEvents.ROUND_SPAWN_PREPARED, this, this.PG); } catch (_) { /* 防御 */ }
      this._pgBound = false;
    }
  }

  // ===== 刷牌/周期收入/道具（T18/T19/T20/T21/T22）=====

  /**
   * refresh（bundle:49746-49757）。
   * inputController.execute(BattleInputCommand(REFRESH,{side:false}))（等价 r0._Y({type:2,nm:false})）。
   * 失败 warn `AI 刷新失败`。
   */
  refresh() {
    const result = this.inputController.execute(new BattleInputCommand(BattleInputCommandType.REFRESH, { side: false }));
    if (!result.success) {
      try { this.logger.warn('AI 刷新失败:', result.reason); } catch (_) { /* 防御 */ }
    }
    this.actions.push({ type: 'refresh', result });
    return result;
  }

  /**
   * PG（bundle:50036-50054）—— 周期收入。
   * 按 au.li（currentRound）匹配 ei 波次表，Ji += ii[Si][i]，日志 `ai加钱`。
   * 由 eventBus.on(ROUND_SPAWN_PREPARED) 触发。
   *
   * 注：bundle 语义为 `My.ii[Si][i]`（ii 为 [4][6] 二维表，按 Si 取行）。
   * AIDifficultyConfig.resolve(Si) 返回的 ii 已按 Si 索引成单行（ii[Si]），
   * 故此处用 _ii2d[Si][i]（完整二维表）还原 bundle 原语义，等价 _iiRow[i]。
   */
  PG() {
    const au = this._au();
    const ei = this._ei;
    const ii2d = this._ii2d;
    const Si = this.Si;
    if (!au || !ei || !ii2d) return;
    for (let i = 0; i < ei.length; i += 1) {
      if (au.currentRound === ei[i]) {
        const gold = ii2d[Si] ? ii2d[Si][i] : 0; // bundle:50048 ii[Si][i]
        au.opponentGold += gold;
        try { this.logger.log('ai加钱', gold); } catch (_) { /* 防御 */ }
        break;
      }
    }
  }

  /**
   * YO（bundle:50066-50083）—— 道具使用尝试。
   * now - xG >= itemCooldownMs（5000ms）时从 itemSlots 过滤未使用道具（Gb()=false 等价）
   * 随机选一个调 Yb，更新 xG=now；冷却内/空栏不触发。
   */
  YO() {
    const now = this._now();
    if (now - this.xG < this._itemCooldownMs) return;
    const slots = this.itemSlots;
    if (!slots || slots.length === 0) return;
    // bundle:50077 g.filter(a=>!a.Gb()) — Gb() 等价"已使用"判定，DEFERRED 用 _used 标志
    const unused = slots.filter(a => a && !a._used && typeof a.Gb === 'function' ? !a.Gb() : (a && !a._used));
    if (unused.length === 0) return;
    const idx = this._randomInt(0, unused.length);
    this.Yb(unused[idx]);
    this.xG = now;
  }

  /**
   * Yb（bundle:50084-50132）—— 道具分派。
   * 按 item.type 分派到 itemEffectDispatcher.use(type, item)；
   * 成功日志 `✅AI成功使用道具`、失败 `❌AI使用道具失败`。
   * @param {object} item 道具对象（含 type 字段）
   */
  Yb(item) {
    if (!item || item == null) return;
    const dispatcher = this.itemEffectDispatcher;
    let result = { success: false };
    try {
      result = dispatcher.use(item.type, item);
    } catch (e) {
      result = { success: false, error: e };
    }
    const txt = (item && item.txt) || (item && item.type) || 'unknown';
    if (result && result.success) {
      try { this.logger.log('✅AI成功使用道具 -', txt); } catch (_) { /* 防御 */ }
      if (item) item._used = true;
    } else {
      try { this.logger.log('❌AI使用道具失败 -', txt); } catch (_) { /* 防御 */ }
    }
  }

  // ===== 分层放置（T27/T23/T24/T25/T26）=====

  /**
   * uG(id)（bundle:49959-49968）—— 单位存活查询。
   * bundle: vc.instance().uM(id)，d && d.l_!==0 返回 d 否则 null。
   * 翻译依据：unitRegistry.getUnit(id)，存活判定 l_!===0 等价为 unit 存在且未死亡。
   * DEFERRED: unitRegistry 缺失或无 getUnit 返回 null。
   * @param {*} id 单位 id
   * @returns {object|null}
   */
  uG(id) {
    const reg = this.unitRegistry;
    if (!reg || typeof reg.getUnit !== 'function') return null; // DEFERRED
    try {
      const unit = reg.getUnit(id);
      // l_ 等价 containerType（0=已销毁）；存活判定 unit 存在
      if (unit && (unit.l_ != null ? unit.l_ !== 0 : true)) return unit;
      return null;
    } catch (_) {
      return null;
    }
  }

  /**
   * YG(unit)（bundle:49970-49984）—— 容器解析。
   * bundle: na.instance().ub(unit.l_, unit.nm).eb(unit) 取容器坐标。
   * DEFERRED: 容器接口未取证，返回 unit 当前坐标（若可用）否则 null。
   * @param {object} unit
   * @returns {{containerType,x,y}|null}
   */
  YG(unit) {
    if (!unit) return null;
    // DEFERRED_CONTAINER: na.ub(l_,nm).eb 翻译待容器系统取证，退化取 unit 当前棋盘坐标
    // bundle 返回 {containerType:unit.l_, x:e.x, y:e.y}；src 单位坐标存于 gridPosition.x/y（u_ 等价）
    const gx = unit.gridPosition && typeof unit.gridPosition.x === 'number' ? unit.gridPosition.x : null;
    const gy = unit.gridPosition && typeof unit.gridPosition.y === 'number' ? unit.gridPosition.y : null;
    if (gx != null && gy != null) {
      return { containerType: unit.l_ != null ? unit.l_ : 1, x: gx, y: gy };
    }
    return null;
  }

  /**
   * pG(id,x,y)（bundle:49986-50013）—— 落子。
   * bundle: uG(id)→YG(unit)→r0._Y({type:1,DY:containerType,IY:srcX,CY:srcY,AY:1,targetX:x,targetY:y,nm})，
   *        返回 k.success，失败 console.warn('AI 设置士兵位置失败:', k.reason)。
   * 翻译：经 uG 取存活单位、YG 解析源坐标后，调 inputController 下发落子指令到 (x,y)。
   * DEFERRED: bundle 的 _Y(type:1) 是统一放置分派（按 DY 容器类型既可购买亦可移动），
   *           src 拆为 PURCHASE_AND_PLACE(购买新卡)/MOVE_UNIT(按 id 重定位现存单位)。
   *           pG 的入参是已存在单位 id（经 uG/YG），故用 MOVE_UNIT 按单位身份落子最贴近 bundle 语义；
   *           bundle 的源容器字段 DY/IY/CY 在 src 无直接等价（MOVE_UNIT 以 unitId 标识源），标 DEFERRED。
   * @param {*} id 单位 id
   * @param {number} x targetX
   * @param {number} y targetY
   * @returns {boolean} 是否成功
   */
  pG(id, x, y) {
    const unit = this.uG(id);
    if (!unit) return false;
    const src = this.YG(unit); // bundle:49996 YG 解析源坐标（DEFERRED_CONTAINER 退化取 gridPosition）
    if (!src) return false;
    const result = this.inputController.execute(new BattleInputCommand(BattleInputCommandType.MOVE_UNIT, {
      unitId: id, gridX: x, gridY: y,
    }));
    if (!result || !result.success) {
      // bundle:50008 console.warn('AI 设置士兵位置失败:', k.reason)
      try { this.logger.warn('AI 设置士兵位置失败:', result && result.reason); } catch (_) { /* 防御 */ }
      return false;
    }
    this.actions.push({ type: 'place', unitId: id, x, y, src, result });
    return true;
  }

  /**
   * jX(unit,x,y)（bundle:50014-50023）—— 调 pG 落子。
   * bundle: this.pG(unit.id, x, y)。
   * @param {object} unit
   * @param {number} x
   * @param {number} y
   */
  jX(unit, x, y) {
    if (!unit) return;
    this.pG(unit.id, x, y);
  }

  /**
   * QX(unit)（bundle:50024-50034）—— 单位回收。
   * bundle: vb.ZP.indexOf(hu[10])>=0 && (uq.au.Ji += unit.level)；
   *        unit.x_ ? vc.HP(id) : vc.WP(id)（x_=isGeneralPart 决定回收路径）。
   * 翻译：金币返还经 BattleState.opponentGold(Ji) += unit.level；回收经 unitRegistry.HP/WP（vc 等价）。
   * DEFERRED: vb.ZP.indexOf(hu[10]) 金币返还开关未解码（hu[10] 值待取证），
   *           默认按"返还"路径执行（Ji += level），与难度配置 oi 的金币返还语义一致；开关确认后可门控。
   * @param {object} unit
   */
  QX(unit) {
    if (!unit) return;
    // bundle:50030 金币返还：au.Ji += unit.level（vb.ZP 含 hu[10] 时；开关 DEFERRED 默认返还）
    const au = this._au();
    if (au && typeof au.opponentGold === 'number' && typeof unit.level === 'number') {
      au.opponentGold += unit.level;
    }
    // bundle:50030 回收：unit.x_(isGeneralPart) ? vc.HP(id)(removeSecondary) : vc.WP(id)(removeSoldier)
    const reg = this.unitRegistry;
    if (reg && typeof reg.HP === 'function' && typeof reg.WP === 'function') {
      try {
        if (unit.x_) reg.HP(unit.id); else reg.WP(unit.id);
      } catch (_) { /* 防御：回收失败不阻塞状态机 */ }
    } else {
      // DEFERRED_RECLAIM: unitRegistry 缺失或无 HP/WP 别名，仅记日志
      try { this.logger.debug('AIController.QX DEFERRED: unitRegistry 无 HP/WP', unit.id); } catch (_) { /* 防御 */ }
    }
  }

  /**
   * WX(terrainKey, unit)（bundle:49885-49953）—— 分层放置策略。
   * 候选格收集（空格 + terrainKey 匹配）→ Si<2 随机洗牌 / Si>=2 DX·TX·OG 评分排序。
   * @param {string} [terrainKey='1_1'] 地形键（bundle 默认 '1_1'）
   * @param {*} [unit] 放置单位（OG 评分用，DEFERRED）
   * @returns {boolean} 是否有候选格
   */
  WX(terrainKey = '1_1', unit) {
    const map = this._map();
    if (!map || typeof map.width !== 'number') return false;
    // bundle:49903-49909 候选收集：遍历 PA.sb，空格 + map.pe[a][c]===terrainKey push 到 zX
    this.zX.length = 0;
    for (let x = 0; x < map.width; x += 1) {
      for (let y = 0; y < map.height; y += 1) {
        // PA.sb[a][c]==null 等价 _isBoardEmpty；map.pe[a][c]===b 等价 blockAt===terrainKey
        if (this._isBoardEmpty(x, y) && map.blockAt(x, y) === terrainKey) {
          this.zX.push({ x, y });
        }
      }
    }
    if (this.zX.length === 0) return false; // bundle:49910

    const r = this.Si;
    // T24：Si<2 随机洗牌（bundle:49912 np.Ys(this.zX)）
    if (r < 2) {
      this._shuffle(this.zX);
      return true;
    }

    // T25：Si>=2 评分（bundle:49913-49948）
    const s = map; // map.pe 等价 map（blockAt 翻译）
    const t = map.me || []; // map.me 对手路线点
    const u = this.templateResolver ? this.templateResolver.wG : 0; // wG
    const v = this.templateResolver ? this.templateResolver.vG : 0; // vG 近带
    const w = this.templateResolver ? this.templateResolver._G : 0; // _G 远带
    const dirs = [[1, 0], [-1, 0], [0, 1], [0, -1]];

    // bundle:49924-49934 TX 评分函数 y(a,b,c,d)
    const y = (ax, ay, c, d) => {
      const j = 1e9; // bundle:49893 j=hu[276]，实测 hu[276]=1e9（哨兵大数，等价 Infinity，作为 min-dist 初值）；路线点缺失时早返回此值
      if (!t || u === 0) return j;
      let g = j;
      const h = d > u ? u : d;
      for (let i = c < 0 ? 0 : c; i < h; i += 1) {
        const dist = Math.abs(ax - t[i].x) + Math.abs(ay - t[i].y);
        if (dist < g) g = dist;
      }
      return g;
    };

    const z = this.zX.map(a => {
      // bundle:49939-49943 DX：四邻 blockAt==='0_1' 路线格数
      const dx = dirs.reduce((acc, [cx, cy]) => {
        const fx = a.x + cx;
        const gy = a.y + cy;
        if (fx >= 0 && gy >= 0 && fx < s.width && gy < s.height && s.blockAt(fx, gy) === '0_1') return acc + 1;
        return acc;
      }, 0);
      // bundle:49944 TX：y(a.x, a.y, v, w)
      const tx = y(a.x, a.y, v, w);
      // bundle:49945 OG：仅 Si=3 经 qj.bX（DEFERRED_PATHFINDING 退化 0）
      const og = (r === 3 && this.templateResolver) ? this.templateResolver.bX(this.GX, a.x, a.y, unit) : 0;
      return { c: a, DX: dx, TX: tx, OG: og };
    });

    // T26：排序（bundle:49949）
    z.sort((a, b) => {
      if (r === 2) {
        return b.DX !== a.DX ? b.DX - a.DX : a.TX - b.TX;
      }
      // r===3
      return b.OG !== a.OG ? b.OG - a.OG
        : (b.DX !== a.DX ? b.DX - a.DX : a.TX - b.TX);
    });

    // bundle:49949-49952：Si=2 且 zX.length>3 取前 5 洗牌 unshift
    if (r === 2 && z.length > 3) {
      const a = z.splice(0, Math.min(5, z.length));
      this._shuffle(a);
      z.unshift(...a);
    }

    // bundle:49953 this.zX = z.map(a=>a.c)
    this.zX = z.map(a => a.c);
    return true;
  }

  // ===== 快速结束/危险触发（T32）=====

  /**
   * UG（bundle:50134-50146）—— 快速结束布阵。
   * bundle: kG 守护（已触发则 return）；kG=true；
   *   const d = sk.AX(2, this.GX)  // 取最多 2 个放置候选坐标（sk.AX 见 bundle:47910，
   *                                 // 按 Si 收集候选+洗牌/评分排序，等价 WX 候选策略）
   *   for (const c of d) oc.instance.event(sS.At, false, c.x, c.y)
   *                                 // 为每个候选发 At 事件（nm=false AI 侧），
   *                                 // sS.At 为"单位放置请求"事件（bundle:26082/47038/50142）。
   *
   * 翻译：sk.AX 与 sS.At 在 src 无直接等价——sk.AX 的候选收集/评分与 WX 同源（复用 WX），
   *   sS.At 的放置事件消费者未在 src 取证。故复用 WX 取候选，经 eventBus.event 发放置请求事件
   *   （若 eventBus 可用）；eventBus 缺失或无消费者时降级为最小桩（仅置守护+记日志），不阻塞状态机。
   *   标 DEFERRED_FAST_END：At 事件消费者接入与 sk.AX 严格等价待后续取证。
   */
  UG() {
    if (this.kG) return;
    this.kG = true;
    // bundle:50141 const d = sk.AX(2, this.GX)：取最多 2 个放置候选
    // sk.AX（bundle:47910）按 Si 收集候选+洗牌/评分，与 WX 候选策略同源，故复用 WX
    const placed = [];
    try {
      if (this.WX('1_1', null)) {
        // 取前 2 个候选（sk.AX 第一参 a=2）
        const candidates = this.zX.slice(0, Math.min(2, this.zX.length));
        // bundle:50142 for(const c of d) oc.instance.event(sS.At, false, c.x, c.y)
        for (const c of candidates) {
          if (this.eventBus && typeof this.eventBus.event === 'function') {
            // DEFERRED_FAST_END: sS.At 为"AI 侧单位放置请求"事件；
            // src 无 GameEvents.At 等价常量，沿用 bundle 字符串键 'At' event，
            // 消费者（快速布阵落子）待取证接入。参数对齐 bundle: (nm, x, y)。
            this.eventBus.event('At', false, c.x, c.y);
          }
          placed.push({ x: c.x, y: c.y });
        }
      }
    } catch (_) { /* 防御：WX/event 失败不阻塞状态机 */ }
    // DEFERRED_FAST_END: At 事件消费者接入待取证；最小实现置守护+记日志
    try { this.logger.debug('AIController.UG DEFERRED_FAST_END placed', placed.length); } catch (_) { /* 防御 */ }
  }

  /**
   * XG（bundle:50148-50160）—— 玩家危险触发。
   * bundle: SG 守护（已触发则 return）；SG=true；
   *   vb.instance()._A(false, 1).Yb(rB.QY)
   *     // vb._A(side=false, slotIndex=1) 取 AI 道具栏槽 1 的道具对象，
   *     //   .Yb(rB.QY) 调道具分派——rB.QY 为危险响应道具常量（bundle:46995/47511，
   *     //   nW.QY/rB.QY 作道具哨兵/默认值，语义为"危险提示响应类道具"）。
   *
   * 翻译：vb._A（道具栏槽位访问）与 rB.QY（危险响应道具）在 src 无直接等价，
   *   Yb 分派契约已有（Yb(item)→itemEffectDispatcher.use）。故 XG 复用 Yb 分派：
   *   从 itemSlots 取索引 1 的道具（若存在）调 Yb；缺失时降级为最小桩（仅置守护+记日志）。
   *   标 DEFERRED_DANGER_TRIGGER：vb._A 槽位访问与 rB.QY 道具常量待道具系统取证接入。
   *
   * 注：XG 的触发概率 ri[Si] 由调用方判定（见 onPlayerDanger，bundle:57469），
   *   本方法仅执行触发后的危险响应行为（置守护+分派危险道具）。
   */
  XG() {
    if (this.SG) return;
    this.SG = true;
    // bundle:50152 vb.instance()._A(false, 1).Yb(rB.QY)
    // DEFERRED_DANGER_TRIGGER: vb._A 槽位访问待取证；退化取 itemSlots[1] 调 Yb
    try {
      const slot1 = this.itemSlots && this.itemSlots.length > 1 ? this.itemSlots[1] : null;
      if (slot1) {
        // rB.QY 危险响应道具常量语义复用：槽 1 道具作为危险响应道具分派
        this.Yb(slot1);
      } else {
        // 道具栏无槽 1 或为空：DEFERRED 桩仅记日志（itemEffectDispatcher 默认 no-op）
        try { this.logger.debug('AIController.XG DEFERRED_DANGER_TRIGGER no item slot 1'); } catch (_) { /* 防御 */ }
      }
    } catch (_) { /* 防御：Yb 失败不阻塞状态机 */ }
  }

  /**
   * onPlayerDanger（bundle:57469）—— 玩家危险提示触发 XG 的入口。
   *
   * bundle:57469（BattleScene 危险提示 UI 处理器，玩家危险时触发）：
   *   vT.instance().HG                  // 教程模式（vT=TutorialMgr）激活则跳过
   *   || this.Rq                         // 危险提示已触发（防重入）则跳过
   *   || vS.instance().SG                // XG 已触发守护则跳过
   *   || (this.Rq = true,                // 标记危险提示已触发
   *       Math.random() <= this.sw.My.ri[this.sw.au.Si]  // 按 ri[Si] 概率
   *       && vS.instance().XG())         // 触发 XG
   *
   * 翻译：bundle 中 XG 由 BattleScene 危险提示外部触发（非 TG 状态机内部调用），
   *   按 ri[Si] 概率判定。src 无法修改 BattleScene，故暴露 onPlayerDanger 作为
   *   外部触发入口（由危险提示系统/测试调用），读取 this._ri 概率后调 XG。
   *   教程模式与 Rq 防重入由调用方承载（src 无 TutorialMgr 等价），本方法仅还原
   *   ri[Si] 概率 + SG 守护 + XG 触发的核心逻辑。
   *
   * 注：AIDifficultyConfig.resolve(Si) 返回标量 ri（已按 Si 索引，T9 契约），
   *   故用 this._ri 直接比较，等价 bundle `Math.random() <= My.ri[Si]`，
   *   勿再 this._ri[this.Si] 索引（否则同 _ni 双重索引 bug）。
   * @returns {boolean} 是否触发了 XG（概率命中且未守护）
   */
  onPlayerDanger() {
    // SG 守护（bundle:57469 vS.instance().SG 短路）
    if (this.SG) return false;
    // bundle:57469 Math.random() <= ri[Si]：按 ri[Si] 概率判定
    // this._ri 由 startGame 经 AIDifficultyConfig.resolve(Si) 设为标量（已按 Si 索引）
    if (this._random() <= this._ri) {
      this.XG();
      return true;
    }
    return false;
  }

  // ===== 难度升降级（T11）=====

  /**
   * Tu(delta)（bundle:10544-10568）—— 难度升降级。
   * 经 rankTableResolver 跨 rank 表计算，胜+1/败-1，钳制 0-3。
   * 简化版：Si = clamp(0,3, Si+delta)；rank 表跨档标 DEFERRED_RANK_TABLE。
   * 由 BattleFlowCoordinator 在 gameOver 时触发（本任务不改 BattleFlowCoordinator，
   * 仅暴露方法 + 注入接口；调用 wiring 标 DEFERRED）。
   * @param {number} delta +1 胜 / -1 败
   * @returns {number} 新 Si
   */
  Tu(delta) {
    const resolver = this.rankTableResolver;
    let nextSi;
    if (resolver && typeof resolver.resolve === 'function') {
      // DEFERRED_RANK_TABLE: rank 表跨档计算，胜+1/败-1
      try { nextSi = resolver.resolve(this.Si, delta); } catch (_) { nextSi = this.Si + delta; }
    } else {
      nextSi = this.Si + delta;
    }
    nextSi = Math.min(3, Math.max(0, nextSi | 0));
    this.Si = nextSi;
    const au = this._au();
    if (au && typeof au.aiDifficulty !== 'undefined') au.aiDifficulty = nextSi;
    return nextSi;
  }

  // ===== 默认 DEFERRED 桩（T22/T11）=====

  /**
   * 默认道具效果分派器桩（DEFERRED_ITEM_SYSTEM）。
   * use(type, item) 返回 {success:false} + 记日志，不抛异常。
   * @param {object} owner AIController 实例（日志用）
   * @returns {{use: Function}}
   * @private
   */
  static _defaultItemEffectDispatcher(owner) {
    return {
      use(type, item) {
        try {
          (owner && owner.logger || console).debug('AIController itemEffectDispatcher DEFERRED_ITEM_SYSTEM use', type);
        } catch (_) { /* 防御 */ }
        return { success: false };
      },
    };
  }

  /**
   * 默认 rank 表解析器桩（DEFERRED_RANK_TABLE）。
   * resolve(Si, delta) 钳制 0-3 返回 Si+delta，no-op 跨档。
   * @returns {{resolve: Function}}
   * @private
   */
  static _defaultRankTableResolver() {
    return {
      resolve(Si, delta) { return Math.min(3, Math.max(0, (Si + delta) | 0)); },
    };
  }

  /**
   * 快照（调试/测试用，非 bundle 原方法）。
   * @returns {object}
   */
  snapshot() {
    return {
      started: this.started,
      Si: this.Si,
      fG: this.fG,
      step: this.step,
      yG: this.yG,
      XX: this.XX,
      KX: this.KX.slice(),
      cG: this.cG.slice(),
      rp: this.rp.slice(),
      kG: this.kG,
      SG: this.SG,
      actions: this.actions.slice(),
    };
  }
}

module.exports = { AIController };
