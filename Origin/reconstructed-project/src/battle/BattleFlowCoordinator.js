'use strict';

const { SingletonBase } = require('../core/SingletonBase');
const { GameEvents } = require('../core/EventBus');
const { BattleResult } = require('./BattleResult');

/**
 * 重建模块：战斗流程编排
 * 原始范围：bundle.strings-decoded.js:55027-55229
 * 原始主要符号：sE
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 */
class BattleFlowCoordinator extends SingletonBase {
  constructor() {
    super();
    this.initialized = false;
    this.initOrder = [];
    this.startOrder = [];
    this.lastBattleScene = null;
    this.lastBattleResult = null;
    this._gameOverInProgress = false;
    this._boundBattleFinished = this._handleBattleFinished.bind(this);
    this._battleEventRegistered = false;
  }

  configure(options = {}) { Object.assign(this, options); return this; }

  init() {
    this._requireDependencies();
    if (this.initialized) return;
    const ordered = [
      ['sceneManager', this.sceneManager],
      ['deckManager', this.deckManager],
      ['enemyManager', this.enemyManager],
      ['unitManager', this.unitManager],
      ['visualEffects', this.visualEffects],
      ['aiController', this.aiController],
      ['inputController', this.inputController],
      ['battleManager', this.battleManager],
      ['projectileManager', this.projectileManager],
      ['animationDriver', this.animationDriver],
      ['preBattleService', this.preBattleService],
      ['matchPreparation', this.matchPreparation],
      ['buffManager', this.buffManager],
      ['skillManager', this.skillManager],
      ['bossManager', this.bossManager],
      ['waveManager', this.waveManager],
      ['focusController', this.focusController],
      ['economy', this.economy],
      ['weaponManager', this.weaponManager],
    ];
    for (const [name, service] of ordered) {
      if (service && typeof service.init === 'function') service.init();
      this.initOrder.push(name);
    }
    if (!this._battleEventRegistered) { this.eventBus.on(GameEvents.BATTLE_FINISHED, this, this._boundBattleFinished); this._battleEventRegistered = true; }
    this.initialized = true;
  }

  startTutorialBattle() {
    this.tutorialController.beforeBattle();
    return this.startBattle().then(scene => {
      this.tutorialController.afterBattleSceneCreated();
      return scene;
    });
  }

  /** 原 sE.startGame 的关键路径调用顺序。 */
  startBattle() {
    if (!this.initialized) throw new Error('BattleFlowCoordinator.init() must run before startBattle()');
    this.startOrder = [];

    this.network.reportGameStart({ fail: error => this.logger.warn('[Server] start game report failed', error) });
    this.startOrder.push('network.reportGameStart');
    this.gameData.startGame();
    this.startOrder.push('gameData.startGame');
    if (this.economy && typeof this.economy.startGame === 'function') { this.economy.startGame(); this.startOrder.push('economy.startGame'); }
    this.telemetry.startGame();
    this.startOrder.push('telemetry.startGame');
    this.deckManager.startGame();
    this.startOrder.push('deckManager.startGame');
    this.preBattleService.startGame();
    this.startOrder.push('preBattleService.startGame');
    this.battleManager.startGame();
    this.startOrder.push('battleManager.startGame');
    this.enemyManager.startGame();
    this.startOrder.push('enemyManager.startGame');
    this.unitManager.startGame();
    this.startOrder.push('unitManager.startGame');
    if (this.weaponManager && typeof this.weaponManager.startGame === 'function') { this.weaponManager.startGame(); this.startOrder.push('weaponManager.startGame'); }
    this.visualEffects.startGame();
    this.startOrder.push('visualEffects.startGame');
    this.buffManager.startGame();
    this.startOrder.push('buffManager.startGame');
    if (this.skillManager) { this.skillManager.startGame(); this.startOrder.push('skillManager.startGame'); }
    if (this.bossManager && typeof this.bossManager.startGame === 'function') { this.bossManager.startGame(); this.startOrder.push('bossManager.startGame'); }
    if (this.waveManager && typeof this.waveManager.startGame === 'function') { this.waveManager.startGame(); this.startOrder.push('waveManager.startGame'); }
    this.platform.startGame();
    this.startOrder.push('platform.startGame');

    return new Promise((resolve, reject) => {
      try {
        this.sceneManager.openScene('BattleScene', false, null, scene => {
          this.inputController.startGame();
          this.startOrder.push('inputController.startGame');
          this.aiController.startGame();
          this.startOrder.push('aiController.startGame');
          this.focusController.startGame();
          this.startOrder.push('focusController.startGame');
          this.lastBattleScene = scene;
          resolve(scene);
        });
        this.sceneManager.whenLastOpenCompletes().catch(reject);
      } catch (error) { reject(error); }
    });
  }

  /** 原 sE.gameOver 的关键清理顺序。 */
  cleanupBattle(isWin = false) {
    this.gameData.battle.isGameOver = true;
    this.battleManager.gameOver();
    this.aiController.gameOver();
    this.inputController.gameOver();
    this.eventBus.event(GameEvents.BATTLE_SCENE_GAME_OVER);
    this.deckManager.gameOver();
    if (this.weaponManager && typeof this.weaponManager.gameOver === 'function') this.weaponManager.gameOver();
    if (this.economy && typeof this.economy.gameOver === 'function') this.economy.gameOver();
    if (this.bossManager && typeof this.bossManager.gameOver === 'function') this.bossManager.gameOver();
    this.enemyManager.gameOver();
    this.unitManager.gameOver();
    this.visualEffects.gameOver();
    this.preBattleService.gameOver(isWin);
    this.matchPreparation.gameOver(isWin);
    if (this.skillManager && typeof this.skillManager.gameOver === 'function') this.skillManager.gameOver(isWin);
    this.buffManager.gameOver(isWin);
    if (this.waveManager && typeof this.waveManager.gameOver === 'function') this.waveManager.gameOver(isWin);
    this.focusController.gameOver(isWin);
    if (this.projectileManager && typeof this.projectileManager.gameOver === 'function') this.projectileManager.gameOver();
    if (this.animationDriver && typeof this.animationDriver.gameOver === 'function') this.animationDriver.gameOver();
    if (this.skillPresentation && typeof this.skillPresentation.gameOver === 'function') this.skillPresentation.gameOver();
    if (this.mapTileManager && typeof this.mapTileManager.gameOver === 'function') this.mapTileManager.gameOver();
    if (this.deadEntityRegistry && typeof this.deadEntityRegistry.clear === 'function') this.deadEntityRegistry.clear();
    this.gameData.gameOver(isWin);
    // 难度升降级（bundle:10544-10568）：胜 isWin=true → Tu(1) 升级、败 isWin=false → Tu(-1) 降级，
    // 经 rankTableResolver 跨档计算后钳制 0-3 回写 au.aiDifficulty。
    // 必须在 gameData.gameOver 之后调用：gameData.gameOver → CriticalGameState.gameOver →
    // BattleState.gameOver 会重置 aiDifficulty=0（src/battle/BattleState.js），若先调 Tu 会被该重置覆盖，
    // 导致 Tu 升降级结果跨局持久化失效。
    if (this.aiController && typeof this.aiController.Tu === 'function') {
      this.aiController.Tu(isWin ? 1 : -1);
    }
    this.network.reportGameEnd(isWin, { fail: error => this.logger.warn('[Server] end game report failed', error) });
  }

  gameOver(isWin = false) {
    if (this._gameOverInProgress) return this.lastBattleResult;
    this._gameOverInProgress = true;
    const battleScene = this.sceneManager.getScene('BattleScene');
    const result = BattleResult.fromRuntime({ isWin, gameData: this.gameData, battleScene, economy: this.economy, now: this.now || (() => Date.now()) });
    this.lastBattleResult = result;
    this.cleanupBattle(isWin);
    this.eventBus.event(GameEvents.BATTLE_RESULT_READY, result);
    this.sceneManager.closeScene('BattleScene', true);
    this.sceneManager.openScene('GameOverScene', false, result, () => { this._gameOverInProgress = false; });
    return result;
  }

  _handleBattleFinished(isWin) {
    if (this._gameOverInProgress || (this.gameData && this.gameData.battle && this.gameData.battle.isGameOver)) return this.lastBattleResult;
    return this.gameOver(Boolean(isWin));
  }

  _requireDependencies() {
    const required = ['network','gameData','telemetry','deckManager','preBattleService','battleManager','enemyManager','unitManager','visualEffects','buffManager','skillManager','bossManager','waveManager','platform','sceneManager','aiController','inputController','focusController','tutorialController','matchPreparation','eventBus','logger','economy','weaponManager'];
    for (const name of required) if (!this[name]) throw new Error(`BattleFlowCoordinator requires ${name}`);
  }

  resetForTests() {
    if (this.eventBus && this._battleEventRegistered) this.eventBus.off(GameEvents.BATTLE_FINISHED, this, this._boundBattleFinished);
    this.initialized = false;
    this.initOrder = [];
    this.startOrder = [];
    this.lastBattleScene = null;
    this.lastBattleResult = null;
    this._gameOverInProgress = false;
    this._battleEventRegistered = false;
  }
}

const BattleFlowManager = BattleFlowCoordinator;
module.exports = { BattleFlowCoordinator, BattleFlowManager };
