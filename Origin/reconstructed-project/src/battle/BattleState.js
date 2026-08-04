'use strict';

const { GameEvents } = require('../core/EventBus');

/**
 * 重建模块：战斗运行状态
 * 原始范围：bundle.strings-decoded.js:3163-3297
 * 原始主要符号：uo（uq.au）
 * 重建状态：COMPLETE_FOR_CRITICAL_PATH
 */
class BattleState {
  constructor(eventBus) {
    if (!eventBus) throw new TypeError('BattleState requires EventBus');
    this.eventBus = eventBus;
    this.currentRound = 0;                  // au.li 波次（玩家与AI共享）
    this.endlessMode = false;               // ci
    this.maxRounds = 20;                    // ui
    this.spawnStrategy = [];                // pi
    this.initialGold = 20;                  // yi
    this.playerRecruitCost = 10;            // fi
    this.opponentRecruitCost = 10;          // au.gi AI 刷牌阈值
    this.forceBossNextRound = false;        // di
    this.playerMaxHealth = 3;               // wi
    this._playerHealth = 3;                 // mi
    this._gold = 0;
    this.standardBattleDelayEnabled = true; // ki
    this.opponentMaxHealth = 3;             // Mi
    this._opponentHealth = 3;               // bi
    this.opponentGold = 0;                  // au.Ji AI 金币
    this.opponentAttackMultiplier = 1;       // xi
    this.delayTime = 10000;
    this.playerPlacementComplete = false;   // Yi
    this.opponentPlacementComplete = false; // au.Xi AI布阵已开始
    this.bossRounds = [];
    this.bossDecisionByRound = {};
    this.bossTypeByRound = {};
    this.isGameOver = false;
    this.contactOccurred = false;              // Gi：敌人已触发阿斗接触伤害
    this.startTime = 0;
    this.killCount = 0;
    this.bossKillCount = 0;
    this.weaponFragments = [];
    this.resultStar = 0;
    this.aiDifficulty = 0;                  // au.Si 难度档 0-3 (bundle:3177)
    this.playerDuplicateFlag = false;       // au.Fi 玩家侧武将字重复标志，dP 复制武将字后置位，bO 抽到武将字且置位则 splice 移除 (bundle:3202 初始化/46565 置位/46519 消费)
    this.opponentDuplicateFlag = false;     // au.Oi AI 侧武将字重复标志，语义同 Fi (bundle:3202/46565/46519)
  }

  get wave() { return this.currentRound; }
  set wave(value) { this.currentRound = value; }
  get endless() { return this.endlessMode; }
  set endless(value) { this.endlessMode = Boolean(value); }
  get maxWaves() { return this.maxRounds; }
  get currentGold() { return this._gold; }

  get playerHealth() { return this._playerHealth; }
  set playerHealth(value) {
    const delta = value - this._playerHealth;
    this._playerHealth = value;
    this.eventBus.event(GameEvents.HEALTH_CHANGED, true, delta);
    if (this._playerHealth <= 0) this.eventBus.event(GameEvents.BATTLE_FINISHED, false);
  }

  get gold() { return this._gold; }
  set gold(value) {
    this._gold = value;
    this.eventBus.event(GameEvents.GOLD_CHANGED);
  }

  get opponentHealth() { return this._opponentHealth; }
  set opponentHealth(value) {
    if (!this.standardBattleDelayEnabled) return;
    const delta = value - this._opponentHealth;
    this._opponentHealth = value;
    this.eventBus.event(GameEvents.HEALTH_CHANGED, false, delta);
    if (this._opponentHealth <= 0) this.eventBus.event(GameEvents.BATTLE_FINISHED, true);
  }

  startGame() {
    this.isGameOver = false;
    this.contactOccurred = false;
    this.delayTime = this.standardBattleDelayEnabled ? 10000 : 0;
    this.startTime = Date.now();
    this.killCount = 0;
    this.bossKillCount = 0;
    this.weaponFragments = [];
    this.resultStar = 0;
  }

  gameOver() {
    this.playerRecruitCost = 10;
    this.opponentRecruitCost = 10;
    this.currentRound = 0;
    this._playerHealth = this.playerMaxHealth;
    this._gold = 0;
    this._opponentHealth = this.opponentMaxHealth;
    this.opponentGold = 0;
    this.opponentAttackMultiplier = 1;       // xi
    this.playerPlacementComplete = false;
    this.opponentPlacementComplete = false;
    this.forceBossNextRound = false;
    this.aiDifficulty = 0;                  // au.Si 难度档复位
    this.playerDuplicateFlag = false;       // au.Fi 武将字重复标志复位 (bundle:3289)
    this.opponentDuplicateFlag = false;     // au.Oi 武将字重复标志复位 (bundle:3289)
    this.bossRounds.length = 0;
    this.bossDecisionByRound = {};
    this.bossTypeByRound = {};
    this.spawnStrategy = [];
    this.contactOccurred = false;
    this.startTime = 0;
    this.killCount = 0;
    this.bossKillCount = 0;
    this.weaponFragments = [];
    this.resultStar = 0;
    this.isGameOver = false;
  }
}

module.exports = { BattleState };
