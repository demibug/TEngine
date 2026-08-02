'use strict';

const { UnitBase, UnitState } = require('./UnitBase');

/**
 * 重建模块：FRIENDLY-UNIT-COMBAT-01 / SoldierBase
 * 原始范围：bundle.strings-decoded.js:23114-23437
 * 原始符号：td
 * 重建状态：COMPLETE_FOR_BASE_SOLDIER_COMBAT
 */
class SoldierBase extends UnitBase {
  constructor() {
    super();
    this.objectType = 1;
    this.experience = 0;
    this.addAttackPower = 0;
    this.rangeBonusCells = 0;
    this.attackSpeedBonus = 0;
    this.animationPlaybackRate = 1;
    this.level = 1;
    this.lastAttackTime = 0;
    this.targets = [];
    this.typeIndex = -1;
    this.animationKey = null;
  }

  configure(options = {}) {
    super.configure(options);
    const { enemyManager, attackTimeline = null, projectileManager = null } = options;
    if (!enemyManager) throw new TypeError('SoldierBase requires enemyManager');
    // 不同正式兵种拥有不同攻击依赖：刀兵使用 attackTimeline，弓兵使用 projectileManager。
    // 基类只保留可选引用，具体兵种负责验证自身必需依赖。
    Object.assign(this, { enemyManager, attackTimeline, projectileManager });
    return this;
  }

  initializeUnit(text) {
    const config = this.gameData.friendlyUnits.getByText(text);
    this.typeIndex = config.index;
    this.id = this.gameData.allocateRuntimeId();
    this.displayObject.name = `soldier_${this.id}`;
    this.baseAttackRange = config.rangeCells * this.gameData.map.gridWidth;
    this.baseAttackPower = config.attackDamage;
    this.baseAttackIntervalSeconds = config.attackIntervalSeconds;
    this.animationKey = config.animationKey;
    this.createAnimation();
    if (this.containerType === 1) this.isActive = true;
    return this;
  }

  createAnimation() {
    if (!this.animation) {
      this.animation = this.presentation.createAnimation(this, this.animationKey);
      if (!this.animation) throw new Error(`Unit presentation did not create animation ${this.animationKey}`);
      this.animation.name = 'sp';
    }
    this.displayObject.addChild(this.animation);
    this.animation.visible = true;
    this.animation.play('zhan', true);
    this.animation.anchorX = 0.5;
    this.animation.anchorY = 0.5;
    this.animation.pos(this.gameData.map.gridWidth / 2, this.gameData.map.gridHeight / 2);
  }

  levelUp(delta = 1, showEffect = true) {
    const applied = super.levelUp(delta, showEffect);
    const stats = this.gameData.friendlyUnits.resolveLevelStats(
      this.unitText,
      this.level,
      this.gameData.map.gridWidth,
    );
    this.baseAttackIntervalSeconds = stats.attackIntervalSeconds;
    this.baseAttackPower = stats.attackDamage;
    this.event('onLevelChange', [this.level, delta > 0]);
    if (delta > 0) this.audio.play('soldier_merge_upgrade');
    return applied;
  }

  onEnterState(state) {
    if (state === UnitState.IDLE) this.playIdleAnimation();
    else if (state === UnitState.ATTACK) this.applyAttackPlaybackRate();
  }

  onExitState(state) {
    if (state === UnitState.ATTACK) this.onAttackStateExit();
  }

  onAttackStateExit() {}

  playIdleAnimation() {
    if (!this.animation) return;
    this.animation.playbackRate(1);
    this.animation.play('zhan', true);
  }

  applyAttackPlaybackRate() {
    if (this.animation) this.animation.playbackRate(this.animationPlaybackRate);
  }

  onMoved() {
    super.onMoved();
  }

  update(deltaMs) {
    if (this.currentState === UnitState.IDLE) this.idle(deltaMs);
  }

  idle(deltaMs) { void deltaMs; }

  addStatModifier(type, amount) {
    if (type === 0) this.addAttackPower += amount;
    else if (type === 2) this.rangeBonusCells += amount;
    else if (type === 1) this.attackSpeedBonus += amount;
  }

  getStat(type) {
    if (type === 0) return this.baseAttackPower;
    if (type === 2) return this.baseAttackRange / this.gameData.map.gridWidth;
    if (type === 1) return 1;
    return undefined;
  }

  get attackPower() { return this.attackDamage; }

  get attackDamage() {
    const value = this.baseAttackPower + this.addAttackPower;
    return this.side ? value : value * this.gameData.battle.opponentAttackMultiplier;
  }

  get attackRange() {
    return this.baseAttackRange + this.rangeBonusCells * this.gameData.map.gridWidth;
  }

  get attackIntervalSeconds() {
    if (this.attackSpeedBonus < 0) this.attackSpeedBonus = 0;
    const interval = this.baseAttackIntervalSeconds / (1 + this.attackSpeedBonus);
    const baseConfig = this.typeIndex < 0 ? null : this.gameData.friendlyUnits.getByIndex(this.typeIndex);
    this.animationPlaybackRate = baseConfig ? baseConfig.attackIntervalSeconds / interval : 0;
    if (this.animation && this.currentState === UnitState.ATTACK) this.animation.playbackRate(this.animationPlaybackRate);
    return interval;
  }

  get attacksPerSecond() { return 1 / this.attackIntervalSeconds; }
  get attackIntervalScale() { return this.attackIntervalSeconds; }

  // BattleManager compatibility aliases preserving original names.
  get _p() { return this.attackDamage; }
  get wp() { return this.attackRange; }
  get z_() { return this.attackIntervalSeconds; }
  get j_() { return this.animationPlaybackRate; }
  set j_(value) { this.animationPlaybackRate = value; }
  get Wm() { return this.lastAttackTime; }
  set Wm(value) { this.lastAttackTime = value; }
  get lx() { return this.targets; }
  set lx(value) { this.targets = value || []; }

  /**
   * 友军基础单位源码中没有 HP、takeDamage 或死亡状态。
   * 显式抛错防止测试/开发环境伪造不存在的战斗规则。
   */
  receiveDamage() {
    const error = new Error('Base soldiers do not expose a damage/health contract in rc/td/knife source ranges');
    error.name = 'UnsupportedFriendlyUnitDamageError';
    throw error;
  }

  resetData() {
    super.resetData();
    this.rangeBonusCells = 0;
    this.attackSpeedBonus = 0;
    // PARTIAL 兼容：完整 BuffManager 暂缓，必须在池复用边界清除其本应撤销的攻击修正。
    this.addAttackPower = 0;
    this.animationPlaybackRate = 1;
    this.lastAttackTime = 0;
    this.targets.length = 0;
    this.typeIndex = -1;
    this.baseAttackPower = 0;
    this.baseAttackRange = 0;
    this.baseAttackIntervalSeconds = 1;
  }

  gameOver() {
    if (this.inPool || this.__InPool) return false;
    const animation = this.animation;
    const result = super.gameOver();
    this.isActive = false;
    this.displayObject = null;
    this.currentState = UnitState.IDLE;
    if (animation) {
      if (typeof animation.offAll === 'function') animation.offAll();
      if (typeof animation.stop === 'function') animation.stop();
      animation.visible = false;
      this.presentation.resetAnimation(animation);
      if (typeof animation.removeSelf === 'function') animation.removeSelf();
    }
    this.animation = null;
    return result;
  }
}

module.exports = { SoldierBase };
