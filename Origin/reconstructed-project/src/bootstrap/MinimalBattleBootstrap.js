'use strict';

/**
 * 最简战斗闭环编排器（MinimalBattleBootstrap）。
 *
 * 对应 OpenSpec change `minimal-battle-loop-gap-fix` 单元 U4（任务 4.1-4.10）。
 * 参照 DevelopmentBootstrap.js 的 wire 顺序与 CombatLifecycle/CombatServices 组合模式，
 * 但只 wire 61 个核心模块，注入 stub 跳过 out-of-scope 服务（skill/boss/AI/generals/
 * 特殊武器/多弹种/农民铲子/表现层/平台/网络），跑通最简闭环：
 *   出兵→敌人移动→打阿斗→阿斗扣血→玩家放兵→兵打敌人→死亡回收→胜负→入池复用。
 *
 * 与 DevelopmentBootstrap 的关键差异：
 *   - DeckManager minimalMode=true（只抽刀/弓/枪/骑，跳过铲注入）
 *   - WaveManager skipBoss=true（所有波 boss=false，bossManager 可不注入）
 *   - BattleManager 不传 specialSpawnPolicy（未注入时降级为无特殊生成）
 *   - EnemyFactory 只注册 Mob0；ProjectileFactory 只注册 SimpleDynamicArrow
 *   - 不注入 AIController/bossManager/skillManager（null），避免 minimalMode 抽牌泄漏
 *   - presentation/audio/effects 为内联轻量桩（记录调用不阻塞），非 Laya 真实实现
 *
 * 编排触发的抽牌路径只走 DeckManager.startGame→drawHand（两侧均用 drawText，
 * minimalMode 下只从 BASE_SOLDIER_TEXTS 抽），不触发 refresh(false)/aiRearrange，
 * 故不会读 108 元素 SO 池导致 AI 手牌污染（U1 风险规避）。
 */
const { EventBus, GameEvents } = require('../core/EventBus');
const { GameLoop } = require('../core/GameLoop');
const { ObjectPool } = require('../core/ObjectPool');
const { MathRandom } = require('../core/MathRandom');
const { PlacementReservationRegistry } = require('../core/PlacementReservationRegistry');
const { AnimationEntityPool } = require('../core/AnimationEntityPool');

const { GameDataCore } = require('../data/CriticalGameState');
// BattleDataCore 聚合名：经 src/data/index.js re-export 的 MAP_BLOCKS/MapDataCore/MapData/EnemyDataCore。
// 最简模式数据来自这些硬编码数据类（maps/waves/enemies/units/deck-pool 均为代码内常量，非外部 JSON）。
const { MapDataCore, EnemyDataCore } = require('../data/BattleDataCore');

const { BattleState } = require('../battle/BattleState');
const { BattleEconomy } = require('../battle/BattleEconomy');
const { DeadEntityRegistry } = require('../battle/DeadEntityRegistry');
const { MapData } = require('../battle/MapData');
const { MapTileManager } = require('../battle/MapTileManager');

const { EnemyFactory } = require('../battle/EnemyFactory');
const { EnemyManager } = require('../battle/EnemyManager');
const { UnitRegistry } = require('../units/UnitRegistry');
const { UnitFactory } = require('../units/UnitFactory');
const { UnitLevelService } = require('../units/UnitLevelService');
const { UnitMergeService } = require('../units/UnitMergeService');

const { BuffManager } = require('../buffs/BuffManager');
const WeaponManager = require('../weapons/WeaponManager');

const { DeckManager } = require('../deck/DeckManager');
const { BattleInputController } = require('../input/BattleInputController');
const { BattleInputCommand, BattleInputCommandType } = require('../input/BattleInputCommand');

const { AttackResolver } = require('../combat/AttackResolver');
const { AttackEffectManager } = require('../combat/AttackEffectManager');
const { MeleeAttackEffect } = require('../combat/MeleeAttackEffect');
const { KnifeAttackEffect } = require('../combat/KnifeAttackEffect');
const { KnifeAttackTimeline } = require('../combat/KnifeAttackTimeline');
const { PikeAttackEffect } = require('../combat/PikeAttackEffect');
const { CavalrySweepEffect } = require('../combat/CavalrySweepEffect');
const { ProjectileAttackEffect } = require('../combat/ProjectileAttackEffect');
const { AttackScheduler } = require('../combat/AttackScheduler');
// U4 返工-问题1：注入 DevelopmentAnimationDriver（核心模块，参照 DevelopmentBootstrap.js:272-276）。
// 弓兵 BowSoldier.attack 的箭矢发射（launchArrow）只由 Laya.Event.STOPPED 事件触发
// （src/units/BowSoldier.js:105）；dev 桩无 Spine/Laya 动画运行时，需由该驱动按攻击释放
// 时长模拟 STOPPED（src/combat/dev/DevelopmentAnimationDriver.js:82-85）。
const { DevelopmentAnimationDriver } = require('../combat/dev/DevelopmentAnimationDriver');

const { ProjectileFactory } = require('../projectiles/ProjectileFactory');
const { ProjectileManager } = require('../projectiles/ProjectileManager');
const { SimpleDynamicArrow } = require('../projectiles/SimpleDynamicArrow');

const { WaveManager } = require('../battle/WaveManager');
const { BattleManager } = require('../battle/BattleManager');
const { BattleTarget } = require('../entities/BattleTarget');
const { Mob0Enemy } = require('../entities/Mob0Enemy');

const { CombatServices } = require('../battle/CombatServices');
const { CombatLifecycle } = require('../battle/CombatLifecycle');

const { DevelopmentAudio } = require('../platform/dev/DevelopmentAudio');

/**
 * 内联轻量敌人表现桩（记录调用不阻塞）。
 * 实现 EnemyBase.configure 校验的 7 个必需方法 + Mob0/NormalEnemyBase 用到的扩展方法。
 * 不创建真实 Laya 渲染节点（spawn/death 立即回调 complete，使逻辑链不阻塞）。
 */
class MinimalEnemyPresentation {
  constructor({ laya, spawnDurationMs = 0, deathDurationMs = 0 } = {}) {
    if (!laya || !laya.Sprite) throw new TypeError('MinimalEnemyPresentation requires Laya');
    this.laya = laya;
    this.spawnDurationMs = spawnDurationMs;
    this.deathDurationMs = deathDurationMs;
    this.calls = [];
  }
  // Mob0Enemy.init 经 objectPool.takeByKey('mob') 取表现节点；registerKey 的 create/reset 调本方法。
  // 构造与 DevelopmentEnemyPresentation.createMobVisual 一致的结构（hpBgImg/hpImg1/hpImg2/hpNum/shadow/stun）。
  createMobVisual() {
    const root = new this.laya.Sprite();
    root.developmentTag = 'MINIMAL_RENDER_STUB';
    root.size(80, 80);
    const hpBg = new this.laya.Sprite(); hpBg.name = 'hpBgImg'; hpBg.size(60, 8);
    const hp1 = new this.laya.Image(); hp1.name = 'hpImg1'; hp1.size(60, 8);
    const hp2 = new this.laya.Image(); hp2.name = 'hpImg2'; hp2.size(60, 8);
    const hpNum = new this.laya.Text(); hpNum.name = 'hpNum'; hpNum.text = '';
    hpBg.addChild(hp1); hpBg.addChild(hp2); hpBg.addChild(hpNum);
    const shadow = new this.laya.Sprite(); shadow.name = 'shadow'; shadow.visible = true;
    const stun = new this.laya.Sprite(); stun.name = 'stun'; stun.visible = false;
    root.addChild(hpBg); root.addChild(shadow); root.addChild(stun);
    this.calls.push(['createMobVisual']);
    return root;
  }
  resetMobVisual(visual) {
    this.calls.push(['resetMobVisual']);
    if (typeof visual.offAll === 'function') visual.offAll();
    if (typeof visual.removeSelf === 'function') visual.removeSelf();
    visual.visible = true;
    visual.alpha = 1;
    visual.anchorX = 0;
    visual.anchorY = 0;
    visual.rotation = 0;
    visual.scale(1, 1);
    visual.pos(0, 0);
    visual.name = '';
    for (const child of visual.children.slice()) {
      if (child.name === 'sp') child.removeSelf();
    }
    const hpBg = visual.getChildByName('hpBgImg');
    if (hpBg) {
      hpBg.visible = true;
      const hp1 = hpBg.getChildByName('hpImg1');
      const hp2 = hpBg.getChildByName('hpImg2');
      const hpNum = hpBg.getChildByName('hpNum');
      if (hp1) hp1.width = 60;
      if (hp2) hp2.width = 60;
      if (hpNum) { hpNum.text = ''; hpNum.visible = true; }
    }
    const stun = visual.getChildByName('stun'); if (stun) stun.visible = false;
  }
  // 7 个 EnemyBase.configure 必需方法
  playSpawn(enemy, complete) { this.calls.push(['playSpawn', enemy.id]); if (typeof complete === 'function') complete(); }
  playDeath(enemy, _color, complete) { this.calls.push(['playDeath', enemy.id]); if (typeof complete === 'function') complete(); }
  setMovePlaybackRate(enemy, rate) { this.calls.push(['setMovePlaybackRate', enemy.id, rate]); }
  startMoving(enemy) { this.calls.push(['startMoving', enemy.id]); }
  stopMoving(enemy) { this.calls.push(['stopMoving', enemy.id]); }
  resetForPool(enemy) { this.calls.push(['resetForPool', enemy.id]); }
  playHitReaction(enemy, damage) { this.calls.push(['playHitReaction', enemy.id, damage]); }
  // NormalEnemyBase._initializeStatsAndAnimation 用 createAnimation 创建动画子节点
  createAnimation(_enemy, _resourcePath) {
    const anim = new this.laya.Sprite();
    anim.playLog = [];
    anim.play = function play(name) { this.playLog.push(name); this.currentAnimation = name; return this; };
    anim.bm = function setPlaybackRate(rate) { this.playbackRate = rate; };
    anim.recover = function recover() { this.recovered = true; };
    anim.pos = function pos(_x, _y) { return this; };
    anim.scale = function scale(_x, _y) { return this; };
    return anim;
  }
  resetAnimation(animation) { if (animation && typeof animation.offAll === 'function') animation.offAll(); }
  // Mob0 专属呼吸动画（记录调用即可）
  startMob0Breathing(enemy) { this.calls.push(['startMob0Breathing', enemy.id]); }
  stopMob0Breathing(enemy) { this.calls.push(['stopMob0Breathing', enemy.id]); }
  // 足迹相关（EnemyBase 仅在 presentation 实现时调用）
  createFootprint() { return null; }
  recoverFootprint() {}
}

/** 内联轻量敌人音频桩（EnemyBase.configure 校验 playHit/playDeath/playContactAttack）。 */
class MinimalEnemyAudio {
  constructor() { this.calls = []; }
  playHit(enemy) { this.calls.push(['hit', enemy.id]); }
  playDeath(enemy) { this.calls.push(['death', enemy.id]); }
  playContactAttack(enemy) { this.calls.push(['contactAttack', enemy.id]); }
}

/** 内联轻量敌人效果桩（EnemyBase.configure 校验 showHit/showDeath/showContactAttack/showDamageNumber）。 */
class MinimalEnemyEffects {
  constructor() { this.calls = []; }
  showHit(enemy, damage, attacker) { this.calls.push(['hit', enemy.id, damage, attacker && attacker.id]); }
  showDeath(enemy) { this.calls.push(['death', enemy.id]); }
  showContactAttack(enemy) { this.calls.push(['contactAttack', enemy.id]); }
  showDamageNumber(enemy, damage) { this.calls.push(['damageNumber', enemy.id, damage]); }
}

/** 内联轻量敌人奖励服务桩（EnemyBase.configure 校验 onEnemyKilled；向 economy 发放击杀金币）。 */
class MinimalEnemyRewardService {
  constructor(battleState, economy = null) { this.battleState = battleState; this.economy = economy; this.calls = []; }
  onEnemyKilled({ enemy, amount, playerLane }) {
    this.calls.push({ enemyId: enemy.id, amount, playerLane });
    if (this.economy && typeof this.economy.award === 'function') this.economy.award(Boolean(playerLane), amount, 'kill');
    else if (playerLane) this.battleState.gold += amount;
    else this.battleState.opponentGold += amount;
    this.battleState.killCount += 1;
  }
}

/**
 * 内联轻量士兵表现桩（UnitBase.configure 校验 createAnimation/resetSoldierVisual/resetAnimation）。
 * 士兵动画为简单 Sprite，play/playbackRate/stop/offAll 实现 Laya 风格接口，供 SoldierBase 使用。
 *
 * U4 返工-问题1：createAnimation 接受 animationDriver（DevelopmentAnimationDriver）。当
 * `play(name, loop=false, ..., endMs)` 被调用时（如 BowSoldier.attack 的 `play('attack',false,true,0,650)`、
 * KnifeSoldier.performKnifeAttack 的 `play('attack',false)`），驱动按攻击释放时长模拟 STOPPED 事件
 * （`elapsedMs>=durationMs` 后 `animation.event(STOPPED)`），使 BowSoldier._onAttackAnimationStopped
 * →launchArrow 攻击链不断裂。参照 src/battle/dev/DevelopmentUnitServices.js:7-47
 * decorateDevelopmentUnitAnimation 的同一契约。
 */
class MinimalUnitPresentation {
  constructor({ laya, animationDriver = null } = {}) {
    if (!laya || !laya.Sprite) throw new TypeError('MinimalUnitPresentation requires Laya');
    this.laya = laya;
    this.animationDriver = animationDriver;
    this.calls = [];
  }
  createSoldierVisual() {
    const root = new this.laya.Sprite();
    root.name = 'soldier';
    root.size(80, 80);
    const level = new this.laya.Text();
    level.name = 'lvl';
    level.value = '1';
    level.text = '1';
    level.visible = true;
    root.addChild(level);
    this.calls.push(['createSoldierVisual']);
    return root;
  }
  resetSoldierVisual(root) {
    this.calls.push(['resetSoldierVisual', root.name]);
    for (const child of root.children.slice()) {
      if (child.name !== 'lvl') child.removeSelf();
    }
    const level = root.getChildByName('lvl');
    if (level) { level.value = '1'; level.text = '1'; level.visible = true; }
    root.name = 'soldier';
    root.pos(0, 0);
    root.scale(1, 1);
    root.rotation = 0;
    root.alpha = 1;
    root.visible = true;
    root.offAll();
  }
  createAnimation(unit, key) {
    const anim = new this.laya.Sprite();
    anim.key = key;
    anim.ownerUnit = unit; // 供 DevelopmentAnimationDriver.update 校验生命周期（stale-lifecycle 取消）
    anim.playCalls = [];
    anim.initialRate = 1;
    anim.rate = 1;
    anim.stopped = false;
    anim.size(80, 80);
    anim.setInitPlaybackRate = function setInitPlaybackRate(value) {
      if (!(value > 0)) throw new RangeError('Initial animation playback rate must be positive');
      this.initialRate = value;
    };
    anim.playbackRate = function playbackRate(value) {
      if (!(value > 0)) throw new RangeError('Animation playback rate must be positive');
      this.rate = value;
    };
    const driver = this.animationDriver;
    // play 契约对齐 decorateDevelopmentUnitAnimation（DevelopmentUnitServices.js:23-41）：
    // 非循环动画且提供有限 endMs 时，向驱动登记片段，由驱动按时长模拟 STOPPED。
    anim.play = function play(name, loop, _force = false, startMs = 0, endMs = null) {
      this.playCalls.push({ name, loop: Boolean(loop), startMs, endMs });
      this.stopped = false;
      if (driver) driver.cancel(this, 'animation-replaced');
      if (!loop && Number.isFinite(endMs)) {
        // 仅在驱动已 init 后登记片段；驱动未 init（如 configure 阶段 idle 探测）时跳过，
        // 不影响逻辑链（idle 为循环动画，不走此分支）。
        if (driver && driver.initialized) {
          driver.playSegment(this, {
            owner: this.ownerUnit,
            name,
            startMs,
            endMs,
            effectivePlaybackRate: this.initialRate * this.rate,
          });
        }
      }
      this.currentAnimation = name;
      return this;
    };
    anim.stop = function stop() {
      this.stopped = true;
      if (driver) driver.cancel(this, 'animation-stopped');
    };
    anim.pos = function pos(_x, _y) { return this; };
    anim.scale = function scale(_x, _y) { return this; };
    this.calls.push(['createAnimation', key]);
    return anim;
  }
  resetAnimation(animation) {
    this.calls.push(['resetAnimation', animation && animation.key]);
    if (animation && typeof animation.offAll === 'function') animation.offAll();
    if (animation && typeof animation.stop === 'function') animation.stop();
  }
}

/** 内联轻量士兵音频桩（UnitBase.configure 校验 play）。 */
class MinimalUnitAudio {
  constructor() { this.calls = []; }
  play(name) { this.calls.push(name); }
}

/** 内联轻量刀兵效果桩（KnifeAttackTimeline 构造校验 startKnifeAttack/showKnifeHit）。 */
class MinimalKnifeEffects {
  constructor() { this.calls = []; }
  startKnifeAttack(record, attacker, target) { this.calls.push(['startKnifeAttack', attacker.id, target.id]); }
  showKnifeHit(record, attacker, enemy) { this.calls.push(['showKnifeHit', attacker.id, enemy.id]); }
}

/** 内联轻量投射物效果桩（SimpleDynamicArrow.applyHit 调用 showSimpleArrowHit）。 */
class MinimalProjectileEffects {
  constructor() { this.calls = []; }
  showSimpleArrowHit(record) { this.calls.push(['simpleArrowHit', record.enemy.id, record.damage]); }
}

/**
 * 最简战斗闭环编排器。
 *
 * 用法：
 *   const bootstrap = new MinimalBattleBootstrap({ Laya });
 *   const ctx = bootstrap.createContext();
 *   ctx.gameLoop.init();          // 启动 80ms 子步 frameLoop
 *   ctx.combatLifecycle.start();  // 启动战斗（startGame 全链）
 *   Laya.timer.tick(80);          // 推进一帧（测试驱动）
 */
class MinimalBattleBootstrap {
  constructor(options = {}) {
    if (!options.Laya) throw new TypeError('MinimalBattleBootstrap requires an explicit Laya runtime or mock');
    this.Laya = options.Laya;
    this.randomSource = options.random || (() => 0.5);
    this.now = options.now || (() => this.Laya.timer.currTimer + 5001);
    this.logger = options.logger || console;
    this.context = null;
  }

  createContext() {
    // ── 4.1 核心：EventBus/GameLoop/ObjectPool/MathRandom/PlacementReservationRegistry ──
    const eventBus = new EventBus();
    // 设置全局 Laya（GameLoop._requireLaya 取 this.laya||globalThis.Laya；显式 configure 优先）
    globalThis.Laya = this.Laya;

    const gameLoop = GameLoop.instance().configure({ laya: this.Laya });
    const objectPool = new ObjectPool({ laya: this.Laya });
    const random = new MathRandom(this.randomSource);

    const placementReservations = PlacementReservationRegistry.instance();
    placementReservations.clear();

    // AnimationEntityPool：aDou（阿斗）骨骼动画创建所需。最简模式 aDou 走骨骼动画分支，
    // createSkeletonAnimation 返回 BattleTarget 节点（与 DevelopmentBootstrap 一致）。
    const animationEntityPool = AnimationEntityPool.instance().configure({
      laya: this.Laya,
      isFrameAnimation: () => false,
      createFrameAnimation: animationId => {
        throw new Error(`Frame animation ${animationId} is not part of the minimal battle loop`);
      },
      createSkeletonAnimation: (_resourcePath, animationId) => {
        if (animationId !== 'aDou') {
          throw new Error(`Skeleton animation ${animationId} is not part of the minimal battle loop`);
        }
        return new BattleTarget({ laya: this.Laya });
      },
    });

    // ── 4.2 数据：GameDataCore(CriticalGameState)/PlayerDataCore/BattleDataCore ──
    // maps/waves/enemies/units/deck-pool 配置为代码内硬编码常量（MAP_BLOCKS/EnemyDataCore/
    // FriendlyUnitConfig/DeckDefinitions），非外部 JSON 文件。GameDataCore 单例聚合所有数据 getter。
    const gameData = GameDataCore.instance().configure({
      eventBus,
      developmentSample: true,
      random: this.randomSource,
      now: this.now,
    });
    // 预触发懒加载 getter，确保 map/enemy/battle/player/friendlyUnits 就绪
    gameData.init();

    // ── 4.3 MapData(changeMap 0)/BattleState/BattleEconomy/DeadEntityRegistry ──
    // MapDataCore 即 MapData 类（src/data/BattleDataCore.js:92 MapDataCore=MapData），
    // 构造默认 changeMap(0)（src/battle/MapData.js:207）。gameData.map 已是 MapData 实例，
    // 此处单独构造供需要独立 MapData 引用的组件使用（与 DevelopmentBootstrap 一致用 gameData.map）。
    void new MapData(); // 等价 changeMap(0)，确认 MAP_BLOCKS[0] 可用
    void MapDataCore;
    void EnemyDataCore;

    const battleState = gameData.battle; // BattleState（含 currentRound/gold/health 等）
    const economy = new BattleEconomy({ battleState, eventBus, logger: this.logger });
    const deadEntityRegistry = new DeadEntityRegistry({ eventBus, logger: this.logger });

    // ── 4.9 dev presentation/audio 桩（记录调用不阻塞） ──
    const enemyPresentation = new MinimalEnemyPresentation({
      laya: this.Laya,
      spawnDurationMs: 0, // 立即回调 complete，不阻塞逻辑链
      deathDurationMs: 0,
    });
    objectPool.registerKey('mob', () => enemyPresentation.createMobVisual(), visual => enemyPresentation.resetMobVisual(visual));
    objectPool.registerKey('boss', () => enemyPresentation.createMobVisual(), visual => enemyPresentation.resetMobVisual(visual));

    const enemyAudio = new MinimalEnemyAudio();
    const enemyEffects = new MinimalEnemyEffects();
    const enemyRewards = new MinimalEnemyRewardService(battleState, economy);

    // BattleTarget（阿斗）需在 enemyDependencies 之前构造：enemyDependencies.targetResolver
    // 闭包引用 playerAdou/opponentAdou/minimalAdouBound，故先创建并绑定。
    // 两侧阿斗均绑定同一 battleState（playerHealth/opponentHealth），敌人接触时扣血触发胜负。
    const playerAdou = new BattleTarget({ laya: this.Laya });
    playerAdou.bindBattleTarget({ battleState, playerLaneTarget: true });
    const opponentAdou = new BattleTarget({ laya: this.Laya });
    opponentAdou.bindBattleTarget({ battleState, playerLaneTarget: false });
    const minimalAdouBound = true;

    // battleContainer：敌人士兵的父容器（parentResolver 返回它）。最简模式用常驻 Sprite。
    const battleContainer = new this.Laya.Sprite();
    battleContainer.name = 'minimalBattleContainer';

    // ── 4.4 EnemyFactory(只注册 Mob0)/EnemyManager ──
    const enemyFactory = EnemyFactory.instance();
    enemyFactory.resetForTests();
    enemyFactory.configure({ objectPool });

    // 敌人运行时依赖（EnemyBase.configure 必需：laya/eventBus/gameData/enemyFactory/objectPool/
    // parentResolver/presentation/audio/effects/rewardService/targetResolver）。
    const enemyDependencies = {
      laya: this.Laya,
      eventBus,
      gameData,
      enemyFactory,
      objectPool,
      parentResolver: () => battleContainer,
      presentation: enemyPresentation,
      audio: enemyAudio,
      effects: enemyEffects,
      rewardService: enemyRewards,
      buffManager: null, // 最简模式不接入 BuffManager（敌人无 buff）
      deadEntityRegistry,
      gameLoop, // 吹飞 Xw/Gw 经 gameLoop.register 推进（最简模式不触发吹飞，但接口需存在）
      // targetResolver：敌人接触阿斗时解析目标。playerLane=true→玩家阿斗，false→对手阿斗。
      targetResolver: playerLane => {
        if (!minimalAdouBound) throw new Error('aDou targets are not bound yet');
        return playerLane ? playerAdou : opponentAdou;
      },
      logger: this.logger,
    };

    const enemyManager = EnemyManager.instance().configure({
      gameLoop,
      gameData,
      eventBus,
      factory: enemyFactory,
      laya: this.Laya,
      buffManager: null,
      logger: this.logger,
    });
    enemyManager.setRandomSource(this.randomSource);
    // 只注册 Mob0（ENEMY_TYPE_KEYS[0]）
    enemyFactory.registerPooledClass('Mob0', Mob0Enemy, enemy => enemy.configure(enemyDependencies));

    // ── 4.5 ProjectileFactory(只注册 SimpleDynamicArrow)/ProjectileManager + combat 全链 ──
    const attackEffectManager = new AttackEffectManager({ objectPool });
    const attackResolver = new AttackResolver();
    const attackScheduler = new AttackScheduler({ enemyManager, resolver: attackResolver });
    // MeleeAttackEffect/PikeAttackEffect/CavalrySweepEffect/KnifeAttackEffect/ProjectileAttackEffect
    // 均为 AttackEffectManager 按需 takeByClass 创建的纯逻辑效果，构造无需额外注入，
    // 此处 require 引用确认模块存在并参与 combat 全链（AttackEffectManager.create(ClassType) 动态实例化）。
    void MeleeAttackEffect;
    void PikeAttackEffect;
    void CavalrySweepEffect;
    void KnifeAttackEffect;
    void ProjectileAttackEffect;

    const knifeEffects = new MinimalKnifeEffects();
    const knifeAttackTimeline = new KnifeAttackTimeline({
      laya: this.Laya,
      enemyManager,
      effects: knifeEffects,
      attackEffectManager,
      logger: this.logger,
    });

    const projectileEffects = new MinimalProjectileEffects();
    // ProjectileFactory 构造自动注册 23 弹种；为满足"只注册 SimpleDynamicArrow"，
    // 构造后调 resetForTests 清空全部注册，再单独 register SimpleDynamicArrow。
    const projectileFactory = new ProjectileFactory({
      laya: this.Laya,
      objectPool,
      enemyManager,
      gameData,
      parentResolver: () => battleContainer,
      effects: projectileEffects,
      logger: this.logger,
    });
    projectileFactory.resetForTests();
    projectileFactory.register(SimpleDynamicArrow.projectileTypeKey, SimpleDynamicArrow);

    const projectileManager = ProjectileManager.instance().configure({
      gameLoop,
      enemyManager,
      gameData,
      projectileFactory,
      laya: this.Laya,
      logger: this.logger,
    });

    // ── 4.6 UnitFactory(4 兵)/UnitRegistry/UnitLevelService/UnitMergeService ──
    const unitManager = UnitRegistry.instance();
    unitManager.resetForTests();

    // U4 返工-问题1：DevelopmentAnimationDriver 按攻击释放时长模拟 STOPPED（参照
    // DevelopmentBootstrap.js:272-276）。stoppedEvent 取 Laya.Event.STOPPED（'stopped'）。
    // init() 在 start() 中调用（向 gameLoop 注册 'developmentAnimationDriver' 更新）。
    const animationDriver = new DevelopmentAnimationDriver({
      gameLoop,
      stoppedEvent: this.Laya.Event.STOPPED,
      logger: this.logger,
    });

    const unitPresentation = new MinimalUnitPresentation({ laya: this.Laya, animationDriver });
    const unitAudio = new MinimalUnitAudio();
    objectPool.registerKey(
      'soldier',
      () => unitPresentation.createSoldierVisual(),
      visual => unitPresentation.resetSoldierVisual(visual),
    );

    const unitFactory = UnitFactory.instance();
    unitFactory.resetForTests();
    unitFactory.configure({
      objectPool,
      dependencyResolver: () => ({
        laya: this.Laya,
        gameData,
        gameLoop,
        eventBus,
        objectPool,
        presentation: unitPresentation,
        audio: unitAudio,
        enemyManager,
        attackTimeline: knifeAttackTimeline,
        attackEffectManager,
        projectileManager,
        buffManager: null, // 最简模式不接入 BuffManager
        logger: this.logger,
      }),
    });

    // UnitRegistry minimalMode 不需要 GeneralFactory dummy：configure 未传 generalFactory 时
    // 内部 `new GeneralFactory()` 兜底（src/units/UnitRegistry.js:53），仅用于武将字合成路径；
    // minimalMode 抽牌只出刀/弓/枪/骑，永不走 GeneralPart 分支，故无需注入 dummy。
    const mapTileManager = new MapTileManager({
      gameData,
      placementReservations,
      randomSource: this.randomSource,
      logger: this.logger,
    });
    unitManager.configure({
      unitFactory,
      gameData,
      eventBus,
      placementReservations,
      parentResolver: _side => battleContainer,
      buffManager: null,
      logger: this.logger,
      mapTileManager,
      weaponManager: null, // 最简模式不接入 WeaponManager（弓兵直接用 projectileManager）
      attackEffectManager,
      projectileManager,
      enemyManager,
    });

    const levelService = new UnitLevelService({ maxLevel: gameData.friendlyUnits.maxLevel });
    const mergeService = new UnitMergeService({ unitRegistry: unitManager, levelService, logger: this.logger });

    // ── 4.7 DeckManager(minimalMode=true)/BattleInputController/BattleInputCommand ──
    const deckManager = new DeckManager({
      gameData,
      economy,
      randomSource: this.randomSource,
      logger: this.logger,
      minimalMode: true, // D1：只抽刀/弓/枪/骑，跳过铲注入
    });
    const inputController = new BattleInputController({
      deckManager,
      economy,
      unitRegistry: unitManager,
      mergeService,
      mapTileManager,
      logger: this.logger,
    });
    // BattleInputCommand/BattleInputCommandType 经 require 引入确认模块存在（输入命令枚举）。
    void BattleInputCommand;
    void BattleInputCommandType;

    // ── 4.8 WaveManager(skipBoss=true)/BattleManager(不传 specialSpawnPolicy)/BattleTarget/
    //       CombatServices/CombatLifecycle ──
    const waveManager = WaveManager.instance().configure({
      gameData,
      enemyManager,
      bossManager: null, // skipBoss=true 时 bossManager 可选
      eventBus,
      randomSource: this.randomSource,
      logger: this.logger,
      skipBoss: true, // D2：所有波 boss=false，不调 bossManager.spawn
    });

    // BattleManager 不传 specialSpawnPolicy（D3：未注入时降级为无特殊生成）；
    // 不传 bossManager/skillManager（null）。
    const battleManager = BattleManager.instance().configure({
      gameData,
      enemyManager,
      eventBus,
      gameLoop,
      unitManager,
      placementReservations,
      random,
      specialSpawnPolicy: undefined, // D3：不传，_chooseSpecialSpawnIndex 返回 -1
      waveManager,
      bossManager: null,
      skillManager: null,
      weaponManager: null,
      economy,
      laya: this.Laya,
      now: this.now,
      logger: this.logger,
      attackEffectManager,
      attackScheduler,
    });

    // BattleTarget（阿斗）：已在 4.4 之前构造并绑定（enemyDependencies.targetResolver 闭包引用）。
    // 两侧阿斗均绑定同一 battleState（playerHealth/opponentHealth），敌人接触时扣血触发胜负。
    void playerAdou;
    void opponentAdou;
    void minimalAdouBound;

    // ── dev audio 桩（记录调用不阻塞） ──
    const audio = new DevelopmentAudio();

    // ── 4.9 注入 null bossManager/skillManager（已上方 configure 时传 null） ──
    // CombatServices 是命名服务容器；CombatLifecycle.start() 按 START_ORDER 调 startGame()。
    // aiController/bossManager/skillManager 不注入（undefined），CombatLifecycle.call 对 falsy 服务跳过。
    const combatServices = new CombatServices({
      economy,
      deckManager,
      battleManager,
      enemyManager,
      unitManager,
      // weaponManager 不注入（最简模式不接入；BattleManager.startGame 不调 weaponManager.startGame）
      projectileManager,
      // buffManager 不注入（最简模式不接入 BuffManager；CombatLifecycle 会跳过）
      // skillManager 不注入（null）
      // bossManager 不注入（null）
      waveManager,
      inputController,
      // aiController 不注入（null）—— 关键：避免 minimalMode DeckManager 触发 AI 抽牌路径
    });
    const combatLifecycle = new CombatLifecycle(combatServices);

    // U4 返工-问题2：最小胜负判定。BattleFlowCoordinator（监听 BATTLE_FINISHED 置 isGameOver，
    // src/battle/BattleFlowCoordinator.js:120/168-171）未注入且不在 61 核心模块内。此处补最小判负/判胜
    // 信号消费：监听 BATTLE_FINISHED(isWin)，置 battleState.isGameOver=true，使战斗结束信号可达。
    // 判负信号源已存在——BattleState.playerHealth setter 在血量≤0 时发 BATTLE_FINISHED(false)
    // （src/battle/BattleState.js:61）；判胜信号源——opponentHealth≤0 发 BATTLE_FINISHED(true)
    // （:76），及 BattleManager 第 20 波清空后发 BATTLE_FINISHED(true)（src/battle/BattleManager.js:106）。
    // 此监听器镜像 BattleFlowCoordinator._handleBattleFinished 的核心副作用（置 isGameOver），
    // 不注入完整 BattleFlowCoordinator，不破坏既有判胜路径。
    const minimalBattleFinishedHandler = (isWin) => {
      // 幂等守卫：镜像 BattleFlowCoordinator._handleBattleFinished 的"首信号胜出"语义
      // （src/battle/BattleFlowCoordinator.js:169 `isGameOver` 守卫）。首个 BATTLE_FINISHED 信号
      // 固定 isGameOver+lastBattleResult，后续信号（含同帧/相邻帧先后到达的判胜/判负）被忽略。
      // 修复双侧同归时 [false,true] 触发顺序导致判负(false) 结果被后到判胜(true) 覆盖的问题：
      // playerHealth 与 opponentHealth 先后归零时，先到信号（判负）固定结果，后到判胜被守卫拦截。
      if (battleState.isGameOver) return;
      battleState.isGameOver = true;
      battleState.lastBattleResult = Boolean(isWin);
      this.logger.log('[MinimalBattleBootstrap] BATTLE_FINISHED', Boolean(isWin), '→ isGameOver=true');
    };
    eventBus.on(GameEvents.BATTLE_FINISHED, null, minimalBattleFinishedHandler);

    this.context = {
      Laya: this.Laya,
      eventBus,
      gameLoop,
      objectPool,
      random,
      placementReservations,
      animationEntityPool,
      gameData,
      battleState,
      economy,
      deadEntityRegistry,
      mapTileManager,
      // 敌人
      enemyFactory,
      enemyManager,
      enemyPresentation,
      enemyAudio,
      enemyEffects,
      enemyRewards,
      // combat 链
      attackResolver,
      attackEffectManager,
      attackScheduler,
      knifeAttackTimeline,
      knifeEffects,
      // U4 返工-问题1：动画驱动（按时长模拟 STOPPED，修复弓兵攻击链）
      animationDriver,
      // 投射物
      projectileFactory,
      projectileManager,
      projectileEffects,
      // 友军
      unitFactory,
      unitManager,
      unitRegistry: unitManager,
      unitPresentation,
      unitAudio,
      levelService,
      mergeService,
      // 牌组/输入
      deckManager,
      inputController,
      // 战斗
      waveManager,
      battleManager,
      battleTarget: { player: playerAdou, opponent: opponentAdou },
      // 编排
      combatServices,
      combatLifecycle,
      // stub
      audio,
      bossManager: null,
      skillManager: null,
      aiController: null,
      // 容器
      battleContainer,
    };

    return this.context;
  }

  /**
   * 启动最简战斗闭环（4.10）：
   *   1. gameData.init()（createContext 已调，此处幂等）
   *   2. 各服务 init()（CombatLifecycle 只调 startGame 不调 init，故 bootstrap 先调 init）：
   *      enemyManager/projectileManager/battleManager/waveManager 等
   *   3. combatLifecycle.start() → 按 START_ORDER 调 economy/deckManager/battleManager/
   *      enemyManager/unitManager/projectileManager/waveManager/inputController 的 startGame()
   *   4. gameLoop.init() → laya.timer.frameLoop(1, this, update) 启动 80ms 子步循环
   *
   * 注：bossManager/skillManager/aiController 未注入，combatLifecycle.call 对 falsy 服务跳过，
   *     不触发 throw。deckManager.startGame 走 drawHand（minimalMode 安全），不触发 refresh/AI 抽牌。
   *
   * @returns {object} context
   */
  start() {
    if (!this.context) this.createContext();
    const { combatLifecycle, gameLoop, enemyManager, projectileManager, battleManager, waveManager, unitManager, animationDriver } = this.context;
    // init 阶段：CombatLifecycle 不调 init，由 bootstrap 显式调用需要 init 的服务。
    // init 顺序：enemyManager（注册 enemyMgr 更新）→ projectileManager（注册 bulletMgr 更新）
    //           → battleManager（设 battleState）→ unitManager（设 initialized）→ waveManager（空 init）
    enemyManager.init();
    projectileManager.init();
    battleManager.init();
    unitManager.init();
    if (waveManager && typeof waveManager.init === 'function') waveManager.init();
    // U4 返工-问题1：动画驱动 init——向 gameLoop 注册 'developmentAnimationDriver' 更新，
    // 使 BowSoldier.attack 的非循环攻击片段按 release 时长模拟 STOPPED→launchArrow。
    if (animationDriver && typeof animationDriver.init === 'function') animationDriver.init();
    // startGame 阶段：CombatLifecycle 按 START_ORDER 调 startGame。
    combatLifecycle.start();
    // 启动 80ms 子步 frameLoop（GameLoop.update 内 500ms 截断 + 80ms 子步）。
    gameLoop.init();
    return this.context;
  }

  /** TEST_ONLY：重置所有单例，便于测试隔离（参照 DevelopmentBootstrap.resetSingletonsForTests）。 */
  static resetSingletonsForTests() {
    for (const Type of [
      GameLoop,
      GameDataCore,
      PlacementReservationRegistry,
      AnimationEntityPool,
      UnitRegistry,
      UnitFactory,
      EnemyFactory,
      EnemyManager,
      BattleManager,
      ProjectileManager,
      WaveManager,
    ]) Type.resetInstanceForTests();
    Mob0Enemy.resetIdsForTests();
  }
}

module.exports = { MinimalBattleBootstrap };
