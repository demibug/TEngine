'use strict';

const { SingletonBase } = require('../core/SingletonBase');
const { GameEvents } = require('../core/EventBus');
const { AttackScheduler } = require('../combat/AttackScheduler');

const BattleManagerState = Object.freeze({
  IDLE: 0,
  WAITING_TO_START: 1,
  SPAWNING: 2,
  WAITING_AFTER_WAVE: 3,
});

/**
 * 重建模块：BATTLE-MGR-01
 * 原始范围：bundle.strings-decoded.js:50323-50534
 * 原始主要符号：vU
 * 重建状态：COMPLETE_FOR_BATTLE_ENTRY
 */
class BattleManager extends SingletonBase {
  constructor() {
    super();
    this.state = BattleManagerState.IDLE;
    this.interWaveDelayMs = 5000;
    this.spawnIntervalMs = 1500;
    this.elapsedMs = 0;
    this.spawnIndex = 0;
    this.unitsThisWave = 0;
    this.playerSpecialSpawnIndex = -1;
    this.opponentSpecialSpawnIndex = -1;
    this.started = false;
    this.startCount = 0;
    this.updateCount = 0;
    this.firstFrameExecuted = false;
    this.generalExperiencePerKill = 1;
    this._experienceEventBus = null;
    this.attackScheduler = null;
    this.attackEffectManager = null;
  }

  configure({ gameData, enemyManager, eventBus, gameLoop, unitManager, placementReservations, random, specialSpawnPolicy, waveManager = null, bossManager = null, skillManager = null, weaponManager = null, economy = null, laya, now = Date.now, logger = console, generalExperiencePerKill = 1, attackScheduler = null, attackEffectManager = null } = {}) {
    Object.assign(this, { gameData, enemyManager, eventBus, gameLoop, unitManager, placementReservations, random, specialSpawnPolicy, waveManager, bossManager, skillManager, weaponManager, economy, laya, now, logger });
    this.attackScheduler = attackScheduler || new AttackScheduler({ enemyManager });
    this.attackEffectManager = attackEffectManager;
    this.generalExperiencePerKill = Number.isFinite(Number(generalExperiencePerKill)) && Number(generalExperiencePerKill) >= 0
      ? Number(generalExperiencePerKill) : 1;
    if (skillManager && unitManager && typeof unitManager.setSkillManager === 'function') unitManager.setSkillManager(skillManager);
    this._bindExperienceEvents();
    return this;
  }

  init() {
    this._requireConfigured();
    this.battleState = this.gameData.battle;
  }

  startGame() {
    this._requireInitialized();
    this.placementReservations.clear();
    this.battleState.gold += this.battleState.initialGold;
    this.battleState.opponentGold += this.battleState.initialGold;
    this.battleState.startTime = this.now();
    this.battleState.killCount = 0;
    this.battleState.bossKillCount = 0;
    this.state = BattleManagerState.WAITING_TO_START;
    this.elapsedMs = 0;
    const strategyIndex = this.random.weightedIndex(this.gameData.enemy.spawnStrategyWeights);
    if (strategyIndex === undefined) throw new Error('Enemy spawn strategy weights produced no index');
    this.battleState.spawnStrategy = this.gameData.enemy.spawnStrategies[strategyIndex];
    this.gameLoop.register('BattleMgr', this, this.update);
    this.soldiers = this.unitManager.soldiers;
    this.generals = this.unitManager.generals;
    if (this.waveManager && typeof this.waveManager.startGame === 'function') this.waveManager.startGame();
    this.started = true;
    this.startCount += 1;
  }

  update(deltaMs) {
    this.updateCount += 1;
    this.firstFrameExecuted = true;
    this._updateSpawnState(deltaMs);
    this._updateUnitAttacks();
    if (this.attackEffectManager && typeof this.attackEffectManager.update === 'function') this.attackEffectManager.update(deltaMs);
  }

  _updateSpawnState(deltaMs) {
    this.elapsedMs += deltaMs;
    if (this.state === BattleManagerState.WAITING_TO_START) {
      if (this.elapsedMs >= this.battleState.delayTime || (this.battleState.playerPlacementComplete && this.battleState.opponentPlacementComplete)) {
        this.elapsedMs = 0;
        this.state = BattleManagerState.SPAWNING;
        this._beginWave();
      }
      return;
    }
    if (this.state === BattleManagerState.SPAWNING) {
      this._spawnPairWhenDue();
      return;
    }
    if (this.state === BattleManagerState.WAITING_AFTER_WAVE && this.elapsedMs >= this.interWaveDelayMs) {
      this.elapsedMs = 0;
      if (this.battleState.endlessMode || this.battleState.currentRound < this.battleState.maxRounds) {
        this.state = BattleManagerState.SPAWNING;
        this._beginWave();
      } else {
        this.eventBus.event(GameEvents.BATTLE_FINISHED, true);
      }
    }
  }

  _beginWave() {
    this.battleState.currentRound += 1;
    this.eventBus.event(GameEvents.ROUND_STARTED);
    if (this.waveManager) {
      const plan = this.waveManager.beginRound(this.battleState.currentRound);
      this.unitsThisWave = plan.normalCount;
    } else {
      this.enemyManager.prepareWave();
      const counts = this.gameData.enemy.waveUnitCounts;
      this.unitsThisWave = this.battleState.endlessMode && this.battleState.currentRound > counts.length
        ? counts[counts.length - 1] + 2 * (this.battleState.currentRound - counts.length)
        : counts[this.battleState.currentRound - 1];
    }
    if (!Number.isFinite(this.unitsThisWave)) throw new Error(`Missing enemy count for round ${this.battleState.currentRound}`);
    this.spawnIndex = 0;
    this.playerSpecialSpawnIndex = this._chooseSpecialSpawnIndex();
    this.opponentSpecialSpawnIndex = this._chooseSpecialSpawnIndex();
    this.eventBus.event(GameEvents.ROUND_SPAWN_PREPARED);
  }

  _spawnPairWhenDue() {
    if (this.elapsedMs < this.spawnIntervalMs) return;
    this.elapsedMs = 0;
    if (this.waveManager) this.waveManager.spawnNormalPair(this.spawnIndex, this.playerSpecialSpawnIndex, this.opponentSpecialSpawnIndex);
    else {
      const typeIndex = this.gameData.map.enemyTypeIndex;
      this.enemyManager.spawn(typeIndex, true, this.playerSpecialSpawnIndex === this.spawnIndex);
      this.enemyManager.spawn(typeIndex, false, this.opponentSpecialSpawnIndex === this.spawnIndex);
    }
    this.spawnIndex += 1;
    if (this.spawnIndex >= this.unitsThisWave) {
      this.spawnIndex = 0;
      this.state = BattleManagerState.WAITING_AFTER_WAVE;
    }
  }

  _chooseSpecialSpawnIndex() {
    return this.specialSpawnPolicy.shouldMarkSpecialSpawn()
      ? this.random.range(0, this.unitsThisWave, true)
      : -1;
  }

  _updateUnitAttacks() {
    const currentTime = this.now();
    for (const unit of this.soldiers.values()) {
      if (!unit.isActive || unit.disabled || unit.inPool) continue;
      this._updateAttackUnit(unit, unit.displayObject, 'UnitIdle', currentTime);
    }
    for (const unit of this.generals.values()) {
      if (!unit.isActive) continue;
      // GeneralUnit is engine-neutral and owns its own combat state machine.
      // Only configured generals participate; target/range/effect data is
      // injected at merge time and is intentionally not guessed here.
      if (typeof unit.updateCombat === 'function') unit.updateCombat(currentTime);
    }
  }

  _bindExperienceEvents() {
    if (this._experienceEventBus === this.eventBus) return;
    if (this._experienceEventBus && typeof this._experienceEventBus.off === 'function') {
      this._experienceEventBus.off(GameEvents.ENEMY_KILLED_BY, this, this._onEnemyKilled);
    }
    this._experienceEventBus = this.eventBus;
    if (this.eventBus && typeof this.eventBus.on === 'function') {
      this.eventBus.on(GameEvents.ENEMY_KILLED_BY, this, this._onEnemyKilled);
    }
  }

  _onEnemyKilled(attackerId, contributorIds, experienceReward = this.generalExperiencePerKill) {
    if (!this.unitManager || typeof this.unitManager.awardGeneralExperience !== 'function') return [];
    const ids = Array.isArray(contributorIds) ? contributorIds.slice() : [];
    if (attackerId != null && !ids.includes(attackerId)) ids.unshift(attackerId);
    const reward = Number.isFinite(Number(experienceReward)) && Number(experienceReward) >= 0
      ? Number(experienceReward) : this.generalExperiencePerKill;
    return this.unitManager.awardGeneralExperience(ids, reward);
  }

  triggerGeneralSkill(generalId, context = {}) {
    const generals = this.generals || (this.unitManager && this.unitManager.generals);
    const general = generals && generals.get(generalId);
    if (!general) return { activated: false, reason: 'unknown-general' };
    if (typeof general.triggerSkill !== 'function') return { activated: false, reason: 'general-skill-entry-missing' };
    return general.triggerSkill(context);
  }

  _updateAttackUnit(unit, displayObject, idleState, currentTime) {
    void displayObject;
    void idleState;
    return this.attackScheduler.update(unit, { enemyManager: this.enemyManager, now: () => currentTime });
  }

  gameOver() {
    if (this.laya && this.laya.timer) this.laya.timer.clearAll(this);
    this.gameLoop.unregister('BattleMgr');
    this.battleState.currentRound = 0;
    this.elapsedMs = 0;
    this.spawnIndex = 0;
    this.unitsThisWave = 0;
    if (this.waveManager && typeof this.waveManager.gameOver === 'function') this.waveManager.gameOver();
    if (this.weaponManager && typeof this.weaponManager.gameOver === 'function') this.weaponManager.gameOver();
    if (this.attackEffectManager && typeof this.attackEffectManager.gameOver === 'function') this.attackEffectManager.gameOver();
    if (this._experienceEventBus && typeof this._experienceEventBus.off === 'function') {
      this._experienceEventBus.off(GameEvents.ENEMY_KILLED_BY, this, this._onEnemyKilled);
    }
    this._experienceEventBus = null;
    this.started = false;
    this.state = BattleManagerState.IDLE;
  }

  _requireConfigured() {
    for (const name of ['gameData','enemyManager','eventBus','gameLoop','unitManager','placementReservations','random','specialSpawnPolicy']) {
      if (!this[name]) throw new Error(`BattleManager requires ${name}`);
    }
  }

  _requireInitialized() {
    this._requireConfigured();
    if (!this.battleState) throw new Error('BattleManager.init() must run before startGame()');
  }

  resetForTests() {
    if (this.gameLoop) this.gameLoop.unregister('BattleMgr');
    this.state = BattleManagerState.IDLE;
    this.elapsedMs = 0;
    this.spawnIndex = 0;
    this.unitsThisWave = 0;
    this.started = false;
    this.startCount = 0;
    this.updateCount = 0;
    this.firstFrameExecuted = false;
    this.generalExperiencePerKill = 1;
    this.attackScheduler = null;
    this.attackEffectManager = null;
    if (this._experienceEventBus && typeof this._experienceEventBus.off === 'function') {
      this._experienceEventBus.off(GameEvents.ENEMY_KILLED_BY, this, this._onEnemyKilled);
    }
    this._experienceEventBus = null;
    this.battleState = null;
  }
}

module.exports = { BattleManager, BattleManagerState };
