'use strict';

const { SingletonBase } = require('../core/SingletonBase');
const { PlayerDataCore, StaminaConfigCore, StaminaServiceCore, RankDataCore, PropsDataCore } = require('./PlayerDataCore');
const { MapDataCore, EnemyDataCore } = require('./BattleDataCore');
const { BattleState } = require('../battle/BattleState');
const { FriendlyUnitConfig } = require('../units/UnitConfig');

/**
 * 重建模块：DATA-01 核心纵向切片聚合根
 * 原始范围：bundle.strings-decoded.js:11561-11908
 * 原始主要符号：tw；闭包别名 uq
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 */
class GameDataCore extends SingletonBase {
  constructor(options = {}) { super(); this.configure(options); }

  configure({ eventBus = null, playerState = null, developmentSample = false, playerOverrides = {}, random = Math.random, now = () => Date.now() } = {}) {
    this.eventBus = eventBus;
    this.initialPlayerState = playerState ? { ...playerState } : developmentSample ? PlayerDataCore.createDevelopmentSample(playerOverrides) : {};
    this.random = random;
    this.now = now;
    this._resetLazyData();
    return this;
  }

  _resetLazyData() {
    this._player = null;
    this._staminaConfig = null;
    this._staminaManager = null;
    this._map = null;
    this._enemy = null;
    this._battle = null;
    this._rank = null;
    this._props = null;
    this._friendlyUnits = null;
    this.runtimeId = 0; // 原 tw.uy；xy() 每次递增。
    this.initialized = false;
  }

  get player() { return this._player || (this._player = new PlayerDataCore(this.initialPlayerState)); }
  get stamina() { return this._staminaConfig || (this._staminaConfig = new StaminaConfigCore()); }
  get staminaManager() { return this._staminaManager || (this._staminaManager = new StaminaServiceCore(this.player, this.stamina, this.now)); }
  get map() { return this._map || (this._map = new MapDataCore()); }
  get enemy() { return this._enemy || (this._enemy = new EnemyDataCore()); }
  get battle() {
    if (!this.eventBus) throw new Error('GameDataCore requires EventBus before battle access');
    return this._battle || (this._battle = new BattleState(this.eventBus));
  }
  get au() { return this.battle; }
  get rank() { return this._rank || (this._rank = new RankDataCore(this.player)); }
  get props() { return this._props || (this._props = new PropsDataCore(this.player)); }
  get friendlyUnits() { return this._friendlyUnits || (this._friendlyUnits = new FriendlyUnitConfig()); }
  get Oc() { return this.friendlyUnits; }

  /** 原 tw.xy。 */
  allocateRuntimeId() { this.runtimeId += 1; return this.runtimeId; }
  xy() { return this.allocateRuntimeId(); }

  init() {
    if (!this.eventBus) throw new Error('GameDataCore requires EventBus');
    this.player.initialize();
    this.props.initialize(this.player.lowPriorityProps);
    this.rank.initialize();
    this.map.initialize(this.player.mapIndex);
    void this.enemy;
    void this.battle;
    void this.staminaManager;
    void this.friendlyUnits;
    this.initialized = true;
  }

  /** 原 tw.Dy：普通敌人属性查询。 */
  resolveEnemyStats(typeIndex, playerSide) {
    void playerSide; // CONFIRMED：tw.Dy 的 b 参数在普通敌人分支中不参与数值计算。
    return this.enemy.resolveNormalStats(typeIndex, {
      mapEnemyTypeIndex: this.map.enemyTypeIndex,
      currentRound: this.battle.currentRound,
      endlessMode: this.battle.endlessMode,
      maxRounds: this.battle.maxRounds,
      spawnStrategy: this.battle.spawnStrategy,
      playerRound: this.player.round,
      rankHealthBonus: 0, // PARTIAL：第 10 波后的段位加血依赖完整 RankData，当前最小玩家数据未恢复该表。
    });
  }


  /** 原 tw.Iy：Boss 属性查询。 */
  resolveBossStats(typeIndex, playerSide) {
    void playerSide;
    return this.enemy.resolveBossStats(typeIndex, {
      mapEnemyTypeIndex: this.map.enemyTypeIndex,
      currentRound: this.battle.currentRound,
      endlessMode: this.battle.endlessMode,
      maxRounds: this.battle.maxRounds,
      spawnStrategy: this.battle.spawnStrategy,
      playerRound: this.player.round,
      rankHealthBonus: 0,
      gridWidth: this.map.gridWidth,
    });
  }

  /** 原 tw.startGame：map → enemy → battle → player。 */
  startGame() {
    if (!this.initialized) throw new Error('GameDataCore.init() must run before startGame()');
    this.map.startGame(this.player.mapIndex);
    this.enemy.startGame();
    this.battle.startGame();
    this.player.startGame();
  }

  gameOver(isWin) {
    this.map.gameOver();
    this.enemy.gameOver();
    this.battle.gameOver();
    this.player.gameOver(Boolean(isWin));
  }
}

const CriticalGameState = GameDataCore;
module.exports = { GameDataCore, CriticalGameState };
