'use strict';

/**
 * 重建模块：DATA-01 最小玩家数据
 * 原始范围：bundle.strings-decoded.js:8525-9429, 11436-11445
 * 原始主要符号：tY（player）、r9（体力配置）
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 */
class PlayerDataCore {
  constructor(initial = {}) {
    Object.assign(this, {
      nick: '',
      gameAvatar: 1,
      avatarUrl: '',
      province: '',
      registerTime: 0,
      saveTime: 0,
      gold: 0,
      win: 0,
      lose: 0,
      winDay: 0,
      loseDay: 0,
      stamina: 0,
      lastRecoverStaminaTime: 0,
      currentStar: 0,
      mapIndex: 0,
      openProps: false,
      lowPriorityProps: [],
      hasPlacedActivePropThisBattle: false,
    }, initial);
    this.lowPriorityProps = Array.isArray(this.lowPriorityProps) ? this.lowPriorityProps.slice() : [];
    this.initialized = false;
  }

  initialize() { this.initialized = true; }
  startGame() {}

  gameOver(isWin) {
    if (isWin) { this.win += 1; this.winDay += 1; }
    else { this.lose += 1; this.loseDay += 1; }
  }

  get round() { return this.win + this.lose; }
  get roundDay() { return this.winDay + this.loseDay + 1; }
  get curStar() { return this.currentStar; }
  set curStar(value) { this.currentStar = value; }

  static createDevelopmentSample(overrides = {}) {
    return {
      source: 'DEVELOPMENT_SAMPLE',
      stamina: 30,
      mapIndex: 0,
      currentStar: 0,
      win: 0,
      lose: 0,
      winDay: 0,
      loseDay: 0,
      lowPriorityProps: [],
      ...overrides,
    };
  }
}

class StaminaConfigCore {
  constructor() {
    this.maxStamina = 30;
    this.recoverIntervalMs = 300000;
    this.battleCost = 5;
    this.rewardedVideoAmount = 10;
    this.shareAmount = 5;
  }
}

class StaminaServiceCore {
  constructor(player, config = new StaminaConfigCore(), now = () => Date.now()) {
    if (!player) throw new TypeError('StaminaServiceCore requires PlayerDataCore');
    this.player = player;
    this.config = config;
    this.now = now;
  }
  canStartBattle() { return this.player.stamina >= this.config.battleCost; }
  consumeForBattle() {
    const wasFull = this.player.stamina >= this.config.maxStamina;
    this.player.stamina -= this.config.battleCost;
    if (wasFull) this.player.lastRecoverStaminaTime = this.now();
    return this.player.stamina;
  }
  get battleCost() { return this.config.battleCost; }
  get maxStamina() { return this.config.maxStamina; }
}

class RankDataCore {
  constructor(player) { this.player = player; this.current = { rank: '军士.壹', level: 0 }; }
  initialize() {}
}

class PropsDataCore {
  constructor(player) {
    this.player = player;
    this.activeSlotCount = 2;
    this.passiveSlotCount = 6;
    this.ta = 2;
    this.sa = 6;
    this.lowPriority = [];
  }
  initialize(lowPriorityProps = []) {
    this.lowPriority = Array.isArray(lowPriorityProps) ? lowPriorityProps.slice() : [];
  }
}

module.exports = { PlayerDataCore, StaminaConfigCore, StaminaServiceCore, RankDataCore, PropsDataCore };
