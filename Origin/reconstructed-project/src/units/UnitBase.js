'use strict';

const { UnitDragBase } = require('./UnitDragBase');

const UnitContainerType = Object.freeze({
  NONE: 0,
  BATTLE: 1,
  DECK: 3,
  AUXILIARY: 5,
});

const UnitState = Object.freeze({
  NONE: 'none',
  PLACING: 'skip',
  IDLE: 'UnitIdle',
  ATTACK: 'UnitAttack',
});

/**
 * 重建模块：FRIENDLY-UNIT-COMBAT-01 / UnitBase
 * 原始范围：bundle.strings-decoded.js:22694-23112
 * 原始主要符号：rc
 * 重建状态：PARTIAL_CRITICAL_PATH_IMPLEMENTATION
 */
class UnitBase extends UnitDragBase {
  constructor() {
    super();
    this.level = 1;
    this.experience = 0;                 // r_
    this.previousContainerType = 0;      // o_
    this.containerType = 0;              // l_
    this.previousGridPosition = { x: -1, y: -1 }; // c_
    this.gridPosition = { x: -1, y: -1 };         // u_
    this.placementPhase = 1;             // p_
    this.placementTweenActive = false;   // y_
    this.placementTween = {
      start: { x: 0, y: 0 },
      control: { x: 0, y: 0 },
      end: { x: 0, y: 0 },
      time: 0,
    };
    this.placementSpeedScale = 1;        // g_
    this.buffHandles = [];               // L_
    this.baseAttackPower = 0;            // m_
    this.baseAttackRange = 0;            // w_
    this.baseAttackIntervalSeconds = 1;  // v_
    this.disabled = false;               // __
    this.secondaryDisabled = false;      // k_
    this.temporaryLevelOverride = false; // S_
    this.previousLevel = 1;              // Nv
    this.isGeneralPart = false;           // x_
    this.currentState = UnitState.NONE;
    this.displayObject = null;            // Oc
    this.animation = null;                // T_
    this.id = -1;
    this.unitText = null;                  // P_
    this.side = true;                      // nm
    this.isActive = false;                 // q_
    this.destroyed = false;
    this.inPool = false;
    this._lifecycleGeneration = 0;
    this._configured = false;
    this.buffNumericModifiers = new Map();
    this.buffStateCounts = new Map();
    this.buffStateData = new Map();
    this.buffManager = null;
  }

  configure({
    laya,
    gameData,
    gameLoop,
    eventBus,
    objectPool,
    presentation,
    audio,
    logger = console,
    dragThreshold = 10,
    buffManager = null,
  } = {}) {
    const required = { laya, gameData, gameLoop, eventBus, objectPool, presentation, audio };
    for (const [name, value] of Object.entries(required)) {
      if (!value) throw new TypeError(`UnitBase requires ${name}`);
    }
    for (const method of ['createAnimation', 'resetSoldierVisual', 'resetAnimation']) {
      if (typeof presentation[method] !== 'function') throw new TypeError(`Unit presentation requires ${method}()`);
    }
    if (typeof audio.play !== 'function') throw new TypeError('Unit audio service requires play()');
    Object.assign(this, { laya, gameData, gameLoop, eventBus, objectPool, presentation, audio, logger });
    this.buffManager = buffManager || this.buffManager;
    this.configureDrag({ laya, dragThreshold });
    this._configured = true;
    return this;
  }

  eventTarget() {
    if (!this.displayObject) throw new Error('Unit event target is unavailable before initialization or after recovery');
    return this.displayObject;
  }

  /** 原 rc.Pw。 */
  setPlacement(containerType = UnitContainerType.NONE, gridX = -1, gridY = -1) {
    this.previousContainerType = this.containerType;
    this.containerType = containerType;
    this.previousGridPosition.x = this.gridPosition.x;
    this.previousGridPosition.y = this.gridPosition.y;
    this.gridPosition.x = gridX;
    this.gridPosition.y = gridY;
    return this;
  }

  /** 原 rc.init；调用顺序由 UnitRegistry 保持为 setPlacement → initialize → register。 */
  initialize(unitText, side) {
    this._requireConfigured();
    this._lifecycleGeneration += 1;
    this.inPool = false;
    this.__InPool = false;
    this.destroyed = false;
    this.side = Boolean(side);
    this.unitText = unitText;
    this.displayObject = this.objectPool.takeByKey('soldier', this);
    if (!this.displayObject || typeof this.displayObject.getChildByName !== 'function') {
      throw new Error('Soldier visual must implement getChildByName()');
    }
    this.levelLabel = this.displayObject.getChildByName('lvl');
    if (!this.levelLabel) throw new Error('Soldier visual must contain a child named "lvl"');
    this.levelLabel.value = '1';
    this.levelLabel.text = '1';
    this.levelLabel.visible = true;
    this.displayObject.name = `pending_soldier_${this._lifecycleGeneration}`;
    this.currentState = (this.containerType === UnitContainerType.BATTLE || this.containerType === UnitContainerType.DECK)
      ? UnitState.NONE
      : UnitState.IDLE;
    this.initializeUnit(unitText);
    this.gameLoop.register(this.displayObject.name, this, this.update);
    return this;
  }

  // 原 rc.A_；由具体单位层实现。
  initializeUnit() {
    throw new Error('UnitBase.initializeUnit() must be implemented');
  }

  /** 正式放置完成入口；UI 拖拽和开发生成器均应调用该入口。 */
  activatePlacement({ parent, pixelX, pixelY, zIndex = null } = {}) {
    if (!parent || typeof parent.addChild !== 'function') throw new TypeError('Unit placement requires an addChild-capable parent');
    parent.addChild(this.displayObject);
    this.displayObject.pos(pixelX, pixelY);
    if (zIndex != null) this.displayObject.zIndex = zIndex;
    this.onMoved();
    return this;
  }

  onMoved() {
    this.isActive = this.containerType === UnitContainerType.BATTLE;
    this.changeState(UnitState.IDLE);
  }

  changeState(nextState) {
    this.onExitState(this.currentState);
    this.currentState = nextState;
    this.onEnterState(nextState);
    this.event('onStateChange', nextState);
  }

  onExitState() {}
  onEnterState() {}

  levelUp(delta = 1, showEffect = true) {
    void showEffect;
    const previous = this.level;
    this.level = Math.min(5, Math.max(1, this.level + delta));
    const experienceThreshold = this.gameData.friendlyUnits.experienceThresholds[this.level - 1];
    if (Number.isFinite(experienceThreshold)) this.experience = experienceThreshold;
    this.levelLabel.value = String(this.level);
    this.levelLabel.text = String(this.level);
    if (this.displayObject && typeof this.displayObject.event === 'function' && this.level !== previous) this.displayObject.event('onLevelChanged', this.level, previous);
    return this.level - previous;
  }

  update(deltaMs) {
    void deltaMs;
  }

  /**
   * 原 rc.gameOver。
   * 重要：与原代码一致，逻辑对象在同步调用栈内先进入类池，子类随后清理专属表现引用。
   */
  gameOver() {
    if (this.inPool || this.__InPool) return false;
    if (!this.displayObject) return false;
    const visual = this.displayObject;
    if (this.buffManager && this.id >= 0 && typeof this.buffManager.clearTarget === 'function') this.buffManager.clearTarget(this.id);
    super.gameOver();
    this._lifecycleGeneration += 1;
    this.gameLoop.unregister(visual.name);
    this.gameLoop.unregister(`${this.id}_jump`);
    this.laya.timer.clearAll(this);
    this.eventBus.offAllCaller(this);
    this.isActive = false;
    this.destroyed = true;
    this.currentState = UnitState.IDLE;
    this.setPlacement();
    this.placementPhase = 1;
    this.placementTween.time = 0;
    if (typeof visual.offAll === 'function') visual.offAll();
    if (typeof visual.removeSelf === 'function') visual.removeSelf();
    this.presentation.resetSoldierVisual(visual);
    this.objectPool.recoverByKey('soldier', visual);
    this.resetData();
    this.inPool = true;
    this.objectPool.recoverByClass(this);
    this.buffHandles.length = 0;
    this.placementTweenActive = false;
    this.placementSpeedScale = 1;
    this.buffNumericModifiers.clear();
    this.buffStateCounts.clear();
    this.buffStateData.clear();
    this.mergeDisabled = false;
    this._suppressionSnapshot = null;
    return true;
  }

  resetData() {
    this.level = 1;
    this.experience = 0;
    this.previousContainerType = 0;
    this.containerType = 0;
    this.previousGridPosition.x = -1;
    this.previousGridPosition.y = -1;
    this.gridPosition.x = -1;
    this.gridPosition.y = -1;
    if (this.levelLabel) {
      this.levelLabel.visible = true;
      this.levelLabel.value = '1';
      this.levelLabel.text = '1';
    }
    this.unitText = null;
    this.id = -1;
    this.side = true;
    this.disabled = false;
    this.secondaryDisabled = false;
    this.temporaryLevelOverride = false;
    this.previousLevel = 1;
    this.mergeDisabled = false;
    this._suppressionSnapshot = null;
  }


  /** Buff runtime compatibility: original am/jw/zw/setState contract. */
  am() { return this.eventTarget(); }
  jw(type) { return this.getBuffBaseStat(type); }
  zw(type, delta, notify = true) {
    const key = Number(type);
    this.buffNumericModifiers.set(key, (this.buffNumericModifiers.get(key) || 0) + (Number(delta) || 0));
    if (typeof this.addStatModifier === 'function') this.addStatModifier(key, Number(delta) || 0);
    if (notify) this.onBuffDataChanged(key);
    return this.buffNumericModifiers.get(key);
  }
  getBuffBaseStat(type) {
    if (typeof this.getStat === 'function') { const value = this.getStat(Number(type)); if (value != null) return value; }
    return 0;
  }
  setState(channel, enabled, data) { return this.setStateEffect(channel, enabled, data); }
  setStateEffect(channel, enabled, data) {
    const key = Number(channel);
    const impulseChannel = key === 5 || key === 4;
    const count = impulseChannel ? (enabled ? 1 : 0) : Math.max(0, (this.buffStateCounts.get(key) || 0) + (enabled ? 1 : -1));
    if (!impulseChannel) { if (count) this.buffStateCounts.set(key, count); else this.buffStateCounts.delete(key); }
    if (enabled && data !== undefined) this.buffStateData.set(key, data);
    if (!count) this.buffStateData.delete(key);
    if (key === 0) this.buffMovementDisabled = count > 0;
    else if (key === 1) this.buffAttackDisabled = count > 0;
    else if (key === 2) this.buffTargetingAltered = count > 0;
    else if (key === 3) {
      this.buffSuppressed = count > 0;
      this._applySuppressionState(count > 0, data);
    }
    else if (key === 5) this.buffKnockbackVector = count > 0 ? data : null;
    else if (key === 6) this.buffLocked = count > 0;
    this.onBuffDataChanged(key);
    return count;
  }
  _applySuppressionState(enabled, data) {
    if (enabled) {
      if (!this._suppressionSnapshot) this._suppressionSnapshot = { level: this.level, mergeDisabled: Boolean(this.mergeDisabled) };
      const reduction = Math.max(1, Number(data && data.levelReduction) || 1);
      this.level = Math.max(1, this._suppressionSnapshot.level - reduction);
      this.mergeDisabled = data && data.mergeDisabled !== undefined ? Boolean(data.mergeDisabled) : true;
      if (this.levelLabel) { this.levelLabel.value = String(this.level); this.levelLabel.text = String(this.level); }
    } else if (this._suppressionSnapshot) {
      this.level = this._suppressionSnapshot.level;
      this.mergeDisabled = this._suppressionSnapshot.mergeDisabled;
      if (this.levelLabel) { this.levelLabel.value = String(this.level); this.levelLabel.text = String(this.level); }
      this._suppressionSnapshot = null;
    }
  }

  onBuffDataChanged(type) {
    if (this.displayObject && typeof this.displayObject.event === 'function') this.displayObject.event('onBuffDataChanged', type);
  }
  onBuffTypeChanged(type) {
    if (this.displayObject && typeof this.displayObject.event === 'function') this.displayObject.event('onBuffTypeChanged', type);
  }


  get x() { return this.displayObject ? this.displayObject.x : 0; }
  get y() { return this.displayObject ? this.displayObject.y : 0; }
  get width() { return this.displayObject ? this.displayObject.width : 0; }
  get height() { return this.displayObject ? this.displayObject.height : 0; }

  get Oc() { return this.displayObject; }
  get T_() { return this.animation; }
  set T_(value) { this.animation = value; }
  get P_() { return this.unitText; }
  get nm() { return this.side; }
  set nm(value) { this.side = Boolean(value); }
  get q_() { return this.isActive; }
  set q_(value) { this.isActive = Boolean(value); }
  get __() { return this.disabled; }
  set __(value) { this.disabled = Boolean(value); }
  get l_() { return this.containerType; }
  set l_(value) { this.containerType = value; }
  get u_() { return this.gridPosition; }
  get o_() { return this.previousContainerType; }
  set o_(value) { this.previousContainerType = value; }
  get c_() { return this.previousGridPosition; }
  get M_() { return this.levelLabel; }
  get lifecycleGeneration() { return this._lifecycleGeneration; }

  _requireConfigured() {
    if (!this._configured) throw new Error(`${this.constructor.name}.configure() must run before initialize()`);
  }
}

module.exports = { UnitBase, UnitContainerType, UnitState };
