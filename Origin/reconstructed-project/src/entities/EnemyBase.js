'use strict';

const { EnemyEventProxy } = require('./EnemyEventProxy');
const { GameEvents } = require('../core/EventBus');

/**
 * 原代码没有独立状态枚举；以下常量是对 ro.curState 数值分支的可维护导出。
 * 数值和进入/退出行为来自 bundle.strings-decoded.js:19941-19962,20629-20654。
 */
const EnemyRuntimeState = Object.freeze({
  SPAWNING: 0,
  MOVING: 1,
  SKILL: 2,
  STUNNED: 3,
  DEAD: 4,
});

const ENEMY_BASE_SPEED = 50;
const CONTACT_ATTACK_COOLDOWN_MS = 500;
const CONTACT_DAMAGE_DELAY_MS = 50;
const TIME_UNIT_MS = 1000;

function distanceSquared(a, b) {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return dx * dx + dy * dy;
}

/**
 * 重建模块：ENEMY-RUNTIME-01
 * 原始范围：bundle.strings-decoded.js:19685-20858
 * 原始主要符号：ro
 * 重建状态：COMPLETE_FOR_MOB0_LIFECYCLE
 *
 * 说明：
 * 本类按原始 ro 的“逻辑实体持有 Laya 表现节点”结构恢复。逻辑实体不是 Sprite；
 * qE 事件接口代理到 enemy 表现节点。原始短字段映射见 ENEMY-RUNTIME-01-symbol-map.json。
 */
class EnemyBase extends EnemyEventProxy {
  constructor() {
    super();
    this._constructOnce();
  }

  _constructOnce() {
    this.isPlayerLane = true;       // nm
    this.targetable = false;        // rm
    this.id = 0;
    this.typeIndex = 0;             // type
    this.isSpecial = false;         // om
    this.fastAnimation = false;     // lm
    this.lastHitSoundTime = 0;      // um
    this.inPool = false;            // pm
    this.healthModifier = 0;        // fm
    this.baseVisualScale = 1;       // gm
    this.visualScaleModifier = 0;   // dm
    this.currentPathIndex = 0;      // Lm
    this.movementDirection = { x: 0, y: 0 }; // wm
    this.knockbackActive = false;    // vm
    this.movementLocked = false;    // _m
    this.knockbackVelocity = { x: 0, y: 0 }; // km
    this.baseMoveSpeed = ENEMY_BASE_SPEED; // Sm
    this.moveSpeedModifier = 0;     // xm
    this.playbackRate = 1;          // bm
    this.previousGridX = 0;         // Am
    this.previousGridY = 0;         // Em
    this.remainingPathDistance = Infinity; // Bm
    this.deathStarted = false;      // Cm
    this.damageContributors = [];   // Ym
    this.lastAttackTime = 0;        // Wm
    this.stopMovement = false;      // Nm
    this.currentState = EnemyRuntimeState.SPAWNING;
    this.curState = this.currentState;
    this.path = null;
    this.visual = null;
    this.enemy = null; // trace-compatible alias
    this.animation = null;
    this.maxHealthBase = 0;         // Km
    this.currentHealth = 0;         // mi/Zi
    this.gridX = 0;                 // Aw
    this.gridY = 0;                 // Ew
    this.lastPathIndex = 0;         // Hm
    this.startPosition = { x: -1, y: -1 }; // ow
    this.firstPathCenter = { x: -1, y: -1 }; // lw
    this.lastFootprintPosition = { x: 0, y: 0 };
    this.footprints = [];
    this._configured = false;
    this._registered = false;
    this._deathScheduled = false;
    this._contactDamageScheduled = false;
    this._lifecycleGeneration = 0;
    this.updateCount = 0;
    this.fixedUpdateCount = 0;
    this.lastDeltaMs = 0;
    this.movementStatus = 'RESTORED_ENEMY_RUNTIME_RO_PE';
  }

  configure({
    laya,
    eventBus,
    gameData,
    enemyFactory,
    objectPool,
    parentResolver,
    presentation,
    audio,
    effects,
    rewardService,
    targetResolver,
    logger = console,
    buffManager = null,
    deadEntityRegistry = null,
  } = {}) {
    const required = { laya, eventBus, gameData, enemyFactory, objectPool, parentResolver, presentation, audio, effects, rewardService, targetResolver };
    for (const [name, value] of Object.entries(required)) {
      if (!value) throw new TypeError(`EnemyBase requires ${name}`);
    }
    for (const method of ['playSpawn','playDeath','setMovePlaybackRate','startMoving','stopMoving','resetForPool','playHitReaction']) {
      if (typeof presentation[method] !== 'function') throw new TypeError(`Enemy presentation requires ${method}()`);
    }
    for (const method of ['playHit','playDeath','playContactAttack']) {
      if (typeof audio[method] !== 'function') throw new TypeError(`Enemy audio service requires ${method}()`);
    }
    for (const method of ['showHit','showDeath','showContactAttack','showDamageNumber']) {
      if (typeof effects[method] !== 'function') throw new TypeError(`Enemy effects service requires ${method}()`);
    }
    if (typeof rewardService.onEnemyKilled !== 'function') throw new TypeError('Enemy reward service requires onEnemyKilled()');
    if (typeof targetResolver !== 'function') throw new TypeError('Enemy targetResolver must be a function');
    Object.assign(this, { laya, eventBus, gameData, enemyFactory, objectPool, parentResolver, presentation, audio, effects, rewardService, targetResolver, logger });
    this.buffManager = buffManager || this.buffManager || null;
    this.deadEntityRegistry = deadEntityRegistry || this.deadEntityRegistry || null;
    this._configured = true;
    return this;
  }

  /** 原 qE.am：事件接口代理到表现节点。 */
  eventTarget() {
    if (!this.visual) throw new Error('Enemy event target is unavailable before visual creation or after recovery');
    return this.visual;
  }

  /**
   * 原始方法符号：init
   * 原始源码范围：bundle.strings-decoded.js:20000-20027
   * 行为可信度：HIGH
   * 副作用：分配 ID、绑定阵营、节点/UI、出生位置并注册 EnemyManager。
   */
  init(playerLane) {
    this._requireConfigured();
    if (!this.visual) throw new Error('Enemy visual must be assigned before EnemyBase.init()');
    this._prepareForSpawn(Boolean(playerLane));
    this.id = this.gameData.allocateRuntimeId ? this.gameData.allocateRuntimeId() : EnemyBase.nextRuntimeId++;
    this.visual.name = `enemy_${this.id}`;
    this._bindVisualChildren();
    this.changeState(EnemyRuntimeState.SPAWNING);
    this.eventBus.event(GameEvents.ENEMY_VISUAL_ADDED, this.visual);
    this._resolveSpawnCoordinates();
    this.visual.pos(this.startPosition.x, this.startPosition.y);
    const parent = this.parentResolver();
    if (!parent || typeof parent.addChild !== 'function') throw new Error('Enemy parentResolver did not return an addChild-capable container');
    parent.addChild(this.visual);
    this.eventBus.event(GameEvents.ENEMY_REGISTERED, this.id, this);
    this._registered = true;
    return this;
  }

  _prepareForSpawn(playerLane) {
    this._lifecycleGeneration += 1;
    this.isPlayerLane = playerLane;
    this.side = playerLane; // compatibility alias
    this.inPool = false;
    this.__InPool = false;
    this.targetable = false;
    this.deathStarted = false;
    this._deathScheduled = false;
    this._contactDamageScheduled = false;
    this.knockbackActive = false;
    this.movementLocked = false;
    this.stopMovement = false;
    this.currentPathIndex = 0;
    this.lastPathIndex = 0;
    this.remainingPathDistance = Infinity;
    this.path = null;
    this.damageContributors.length = 0;
    this.lastAttackTime = 0;
    this.lastHitSoundTime = 0;
    this.moveSpeedModifier = 0;
    this.healthModifier = 0;
    this.visualScaleModifier = 0;
    this.movementDirection.x = 0;
    this.movementDirection.y = 0;
    this.knockbackVelocity.x = 0;
    this.knockbackVelocity.y = 0;
    this.startPosition.x = -1;
    this.startPosition.y = -1;
    this.firstPathCenter.x = -1;
    this.firstPathCenter.y = -1;
    this.previousGridX = 0;
    this.previousGridY = 0;
    this.gridX = 0;
    this.gridY = 0;
    this.updateCount = 0;
    this.fixedUpdateCount = 0;
    this.lastDeltaMs = 0;
    this.movementStatus = 'RESTORED_ENEMY_RUNTIME_RO_PE';
    this.lastFootprintPosition.x = 0;
    this.lastFootprintPosition.y = 0;
  }

  _bindVisualChildren() {
    const required = name => {
      const child = this.visual.getChildByName(name);
      if (!child) throw new Error(`Enemy visual is missing required child: ${name}`);
      return child;
    };
    this.healthBarBackground = required('hpBgImg'); // iw
    this.healthBarDelayed = this.healthBarBackground.getChildByName('hpImg1'); // hw
    this.healthBarImmediate = this.healthBarBackground.getChildByName('hpImg2'); // ew
    this.healthText = this.healthBarBackground.getChildByName('hpNum'); // Qm
    this.shadow = required('shadow'); // nw
    this.stunIndicator = required('stun'); // Im
    if (!this.healthBarDelayed || !this.healthBarImmediate || !this.healthText) {
      throw new Error('Enemy visual hpBgImg must contain hpImg1, hpImg2 and hpNum');
    }
    this.healthBarWidth = this.healthBarImmediate.width;
    this.healthBarDelayed.width = this.healthBarWidth;
    this.healthBarImmediate.width = this.healthBarWidth;
    this.stunIndicator.visible = false;
  }

  _resolveSpawnCoordinates() {
    if (this.startPosition.x !== -1) return;
    const map = this.gameData.map;
    const entry = this.isPlayerLane ? map.playerEntry : map.opponentEntry;
    const start = this.isPlayerLane ? map.playerStart : map.opponentStart;
    this.startPosition.x = entry.x * map.gridWidth;
    this.startPosition.y = entry.y * map.gridWidth; // CONFIRMED：原 ro.rw 对 y 使用 ye，而非 gridHei。
    this.firstPathCenter.x = start.x * map.gridWidth + this.visual.width / 2;
    this.firstPathCenter.y = start.y * map.gridWidth + this.visual.height / 2;
  }

  get moveSpeed() {
    const speed = this.baseMoveSpeed + this.moveSpeedModifier;
    this.playbackRate = speed / this.baseMoveSpeed;
    this.presentation.setMovePlaybackRate(this, this.playbackRate);
    return speed;
  }

  get maxHealth() { return this.maxHealthBase + this.healthModifier; }
  get health() { return this.currentHealth; }
  set health(value) {
    this.currentHealth = value;
    if (this.healthText) this.healthText.text = Number(value).toFixed(0);
  }
  get x() { return this.visual ? this.visual.x : 0; }
  get y() { return this.visual ? this.visual.y : 0; }
  get centerX() { return this.visual.x + this.visual.width / 2; }
  get centerY() { return this.visual.y + this.visual.height / 2; }

  getPath() {
    this.path = this.gameData.map.pathForSide(this.isPlayerLane);
    if (!Array.isArray(this.path) || this.path.length === 0) throw new Error(`Enemy path is missing for side ${this.isPlayerLane}`);
    return this.path;
  }

  update(deltaMs) {
    this.updateCount += 1;
    this.fixedUpdateCount += 1;
    this.lastDeltaMs = deltaMs;
    if (this.currentState === EnemyRuntimeState.MOVING) this.move(deltaMs);
    this._fadeFootprints();
  }

  move(deltaMs) {
    this.lastPathIndex = this.currentPathIndex;
    if (!this.movementLocked) {
      if (this.knockbackActive) this._updateKnockback(deltaMs);
      else if (!this.stopMovement) this._advanceAlongPath(deltaMs);
    }
    if (this.lastPathIndex !== this.currentPathIndex) this._handlePathIndexChanged();
    this._updateGridMembership();
  }

  _advanceAlongPath(deltaMs) {
    if (this.currentPathIndex < 0 || this.currentPathIndex >= this.path.length) return;
    const map = this.gameData.map;
    const point = this.path[this.currentPathIndex];
    const dx = point.x * map.gridWidth - this.visual.x;
    const dy = point.y * map.gridHeight - this.visual.y;
    const distance = Math.sqrt(dx * dx + dy * dy);
    if (distance < 1) {
      this.currentPathIndex += 1;
    } else {
      const dirX = dx / distance;
      const dirY = dy / distance;
      this.movementDirection.x = dirX;
      this.movementDirection.y = dirY;
      // CONFIRMED：On 为 px/s，原公式直接使用 deltaMilliseconds / 1000。
      this.visual.x += dirX * this.moveSpeed * deltaMs / TIME_UNIT_MS;
      this.visual.y += dirY * this.moveSpeed * deltaMs / TIME_UNIT_MS;
    }
    this.visual.zIndex = Math.floor(this.visual.y);
    this.remainingPathDistance = distance + (this.path.length - 1 - this.currentPathIndex) * map.gridWidth;
    this._tryCreateFootprint();
  }

  _updateKnockback(deltaMs) {
    this.visual.x += this.knockbackVelocity.x * deltaMs / TIME_UNIT_MS;
    this.visual.y += this.knockbackVelocity.y * deltaMs / TIME_UNIT_MS;
    this.knockbackVelocity.x *= 0.9;
    this.knockbackVelocity.y *= 0.9;
    const correction = this._pathBoundsCorrection();
    if (Math.abs(this.knockbackVelocity.x) < 0.1 && Math.abs(this.knockbackVelocity.y) < 0.1) {
      this.knockbackActive = false;
      this.currentPathIndex = this._nearestPathIndexAfterCorrection(correction);
    }
  }

  _pathBoundsCorrection() {
    const nearest = this._nearestPathIndex();
    const point = this.path[nearest];
    if (!point) return { x: 0, y: 0 };
    const map = this.gameData.map;
    const left = point.x * map.gridWidth - map.gridWidth / 2;
    const right = point.x * map.gridWidth + map.gridWidth / 2;
    const top = point.y * map.gridHeight - map.gridHeight / 2;
    const bottom = point.y * map.gridHeight + map.gridHeight / 2;
    const leftGap = Math.max(left - this.visual.x, 0);
    const rightGap = Math.max(this.visual.x - right, 0);
    const topGap = Math.max(top - this.visual.y, 0);
    const bottomGap = Math.max(this.visual.y - bottom, 0);
    if (leftGap === 0 && rightGap === 0 && topGap === 0 && bottomGap === 0) return { x: 0, y: 0 };
    const maxGap = Math.max(leftGap, rightGap, topGap, bottomGap);
    const correction = leftGap === maxGap ? { x: -maxGap, y: 0 }
      : rightGap === maxGap ? { x: maxGap, y: 0 }
        : topGap === maxGap ? { x: 0, y: -maxGap }
          : { x: 0, y: maxGap };
    this.visual.x -= correction.x;
    this.visual.y -= correction.y;
    return correction;
  }

  _nearestPathIndex() {
    let index = 0;
    let best = Infinity;
    const map = this.gameData.map;
    for (let i = 0; i < this.path.length; i += 1) {
      const dx = this.path[i].x * map.gridWidth - this.visual.x;
      const dy = this.path[i].y * map.gridHeight - this.visual.y;
      const d2 = dx * dx + dy * dy;
      if (d2 < best) { best = d2; index = i; }
    }
    return index;
  }

  _nearestPathIndexAfterCorrection() { return this._nearestPathIndex(); }

  _handlePathIndexChanged() {
    const length = this.path.length;
    if (this.currentPathIndex === length - 3) this.eventBus.event(GameEvents.ENEMY_APPROACH_WARNING, this.isPlayerLane);
    else if (this.currentPathIndex === length - 2) this.eventBus.event(GameEvents.ENEMY_FINAL_WARNING, this.isPlayerLane);
    else if (this.currentPathIndex === length - 1) this.attackBattleTarget();
    else if (this.currentPathIndex >= length) {
      this.audio.playContactAttack(this);
      if (!this.deathStarted) {
        this.deathStarted = true;
        this.event('onDead');
      }
      this.gameOver();
    }
  }

  /**
   * 原始方法符号：attack
   * 原始源码范围：bundle.strings-decoded.js:20311-20339
   * 行为可信度：HIGH
   * 副作用：50ms 后对当前阵营对应阿斗造成固定 1 点伤害。
   */
  attackBattleTarget() {
    const now = this.laya.timer.currTimer;
    if (now - this.lastAttackTime < CONTACT_ATTACK_COOLDOWN_MS) return false;
    if (!this.path || this.path.length < 2) return false;
    this.effects.showContactAttack(this);
    this._contactDamageScheduled = true;
    const generation = this._lifecycleGeneration;
    this.laya.timer.once(CONTACT_DAMAGE_DELAY_MS, this, () => {
      if (generation !== this._lifecycleGeneration) return;
      this._contactDamageScheduled = false;
      if (!this.targetable || this.currentState === EnemyRuntimeState.DEAD) return;
      const target = this.targetResolver(this.isPlayerLane);
      if (!target || typeof target.receiveEnemyContact !== 'function') {
        throw new Error('Battle target does not implement receiveEnemyContact()');
      }
      target.receiveEnemyContact(1, this);
    });
    this.lastAttackTime = now;
    return true;
  }

  /**
   * 原始方法符号：hit
   * 原始源码范围：bundle.strings-decoded.js:20546-20574
   * 行为可信度：HIGH
   */
  hit(damage, attacker = null) {
    if (this.health <= 0) return false;
    const now = this.laya.timer.currTimer;
    if (!this.lastHitSoundTime || now - this.lastHitSoundTime > 50) {
      this.audio.playHit(this);
      this.lastHitSoundTime = now;
    }
    this.health -= damage;
    if (this.health < 0) this.health = 0;
    this.effects.showHit(this, damage, attacker);
    this.event('onHit');
    const ratio = this.maxHealth > 0 ? this.health / this.maxHealth : 0;
    this.healthBarImmediate.width = this.healthBarWidth * ratio;
    // 原 hpImg1 使用 500ms 线性 Tween；逻辑值保留为目标宽度，表现端记录过渡。
    this.healthBarDelayed.width = this.healthBarImmediate.width;
    this.effects.showDamageNumber(this, damage);
    this.presentation.playHitReaction(this, damage);
    if (this.health <= 0) this.changeState(EnemyRuntimeState.DEAD);
    if (attacker && attacker.id != null) {
      if (!this.damageContributors.includes(attacker.id)) this.damageContributors.push(attacker.id);
      if (this.health <= 0) this.eventBus.event(GameEvents.ENEMY_KILLED_BY, attacker.id, this.damageContributors.slice());
    }
    return true;
  }

  takeDamage(damage, attacker = null) { return this.hit(damage, attacker); }

  _beginDeath() {
    if (this.deathStarted) return;
    this.deathStarted = true;
    if (this.deadEntityRegistry && !this.isBoss) this.deadEntityRegistry.recordEnemy(this);
    this.stunIndicator.visible = false;
    this.event('onDead');
    const reward = this.isSpecial ? 10 : 1;
    this.rewardService.onEnemyKilled({ enemy: this, amount: reward, playerLane: this.isPlayerLane });
    this.audio.playDeath(this);
    this.effects.showDeath(this);
  }

  /** Subclasses preserve the original visual completion boundary. */
  beginDeath() { this._beginDeath(); }

  changeState(nextState) {
    if (this.currentState === nextState) return false;
    this._exitState(this.currentState);
    this.currentState = nextState;
    this.curState = nextState;
    this._enterState(nextState);
    return true;
  }

  _exitState(state) {
    if (state === EnemyRuntimeState.SPAWNING) this.targetable = true;
    else if (state === EnemyRuntimeState.MOVING) this.stopMovingAnimation();
    else if (state === EnemyRuntimeState.SKILL) this.onSkillExit();
    else if (state === EnemyRuntimeState.STUNNED) this.stunIndicator.visible = false;
  }

  _enterState(state) {
    if (state === EnemyRuntimeState.SPAWNING) this.targetable = false;
    else if (state === EnemyRuntimeState.MOVING) this.startMovingAnimation();
    else if (state === EnemyRuntimeState.SKILL) this.onSkillEnter();
    else if (state === EnemyRuntimeState.STUNNED) this.onStunnedEnter();
    else if (state === EnemyRuntimeState.DEAD) {
      this.targetable = false;
      this.beginDeath();
    }
  }

  startMovingAnimation() { this.presentation.startMoving(this); }
  stopMovingAnimation() { this.presentation.stopMoving(this); }
  onSkillEnter() {}
  onSkillExit() {}
  onStunnedEnter() {}

  isTargetableBy(playerLane) {
    return this.currentState !== EnemyRuntimeState.SPAWNING &&
      this.isPlayerLane === playerLane &&
      this.currentState !== EnemyRuntimeState.DEAD &&
      this.targetable;
  }
  cw(playerLane) { return this.isTargetableBy(playerLane); }


  /** Buff runtime compatibility: original am/jw/zw/setState contract. */
  am() { return this.eventTarget(); }
  jw(type) {
    switch (Number(type)) {
      case 3: return Number(this.baseMoveSpeed || 0);
      case 4: return Number(this.maxHealthBase || 0);
      case 6: return Number(this.baseVisualScale || 1);
      default: return undefined;
    }
  }
  zw(type, delta, removing = false) {
    const value = Number(delta) || 0;
    switch (Number(type)) {
      case 3:
        this.moveSpeedModifier += value;
        break;
      case 4:
        this.healthModifier += value;
        if (!removing && value > 0) this.health += value;
        if (this.health > this.maxHealth) this.health = this.maxHealth;
        break;
      case 6:
        this.visualScaleModifier += value;
        if (this.visual && typeof this.visual.scale === 'function') {
          const scale = (this.baseVisualScale || 1) + this.visualScaleModifier;
          this.visual.scale(scale, scale);
        }
        break;
      default:
        break;
    }
    this.onBuffDataChanged(type);
    return value;
  }
  setState(channel, enabled, data) { return this.setStateEffect(channel, enabled, data); }
  onBuffDataChanged(type) { if (this.visual && typeof this.visual.event === 'function') this.visual.event('onBuffDataChanged', type); }
  onBuffTypeChanged(type) { if (this.visual && typeof this.visual.event === 'function') this.visual.event('onBuffTypeChanged', type); }

  setStateEffect(effectType, enabled, value) {
    this.buffStateFlags = this.buffStateFlags || new Map();
    const impulseChannel = effectType === 5 || effectType === 4;
    const count = impulseChannel ? (enabled ? 1 : 0) : Math.max(0, (this.buffStateFlags.get(effectType) || 0) + (enabled ? 1 : -1));
    if (!impulseChannel) { if (count) this.buffStateFlags.set(effectType, count); else this.buffStateFlags.delete(effectType); }
    const active = count > 0;
    if (effectType === 0) this.stopMovement = active;
    else if (effectType === 1) this.attackDisabled = active;
    else if (effectType === 2) this.targetingAltered = active;
    else if (effectType === 3) this.suppressed = active;
    else if (effectType === 5) {
      if (!active) { this.knockbackActive = false; return; }
      this.knockbackActive = true;
      this._setKnockbackVelocity(value);
    } else if (effectType === 4) {
      if (enabled) this.hit(value, null);
    } else if (effectType === 6) this.movementLocked = active;
  }

  _setKnockbackVelocity(value) {
    if (value && typeof value === 'object') {
      this.knockbackVelocity.x = Number(value.x) || 0;
      this.knockbackVelocity.y = Number(value.y) || 0;
    }
  }

  _updateGridMembership() {
    const map = this.gameData.map;
    const nextX = Math.floor((this.visual.x + this.visual.width / 2) / map.gridWidth);
    const nextY = Math.floor((this.visual.y + this.visual.height / 2) / map.gridHeight);
    if (nextX === this.gridX && nextY === this.gridY) return;
    if (this.targetable) this.eventBus.event(GameEvents.ENEMY_GRID_LEFT, this.id, this);
    const currentTopLeft = { x: this.visual.x, y: this.visual.y };
    const cellTopLeft = { x: nextX * map.gridWidth, y: nextY * map.gridHeight };
    if (distanceSquared(currentTopLeft, cellTopLeft) > 25) return;
    this.previousGridX = this.gridX;
    this.previousGridY = this.gridY;
    this.gridX = nextX;
    this.gridY = nextY;
    this.eventBus.event(GameEvents.ENEMY_GRID_ENTERED, this.isPlayerLane, this.gridX, this.gridY);
    this.eventBus.event(GameEvents.ENEMY_GRID_ENTITY_ENTERED, this.isPlayerLane, this.gridX, this.gridY, this.id);
  }

  _tryCreateFootprint() {
    // Footprints are presentation-only. The original threshold is 30px; creation remains delegated.
    if (typeof this.presentation.createFootprint !== 'function') return;
    const current = { x: this.visual.x, y: this.visual.y };
    if (distanceSquared(this.lastFootprintPosition, current) < 30 * 30) return;
    const footprint = this.presentation.createFootprint(this);
    if (footprint) {
      this.footprints.push(footprint);
      this.lastFootprintPosition.x = current.x;
      this.lastFootprintPosition.y = current.y;
    }
  }

  _fadeFootprints() {
    for (let i = this.footprints.length - 1; i >= 0; i -= 1) {
      const footprint = this.footprints[i];
      footprint.alpha -= 0.01;
      if (footprint.alpha <= 0) {
        if (typeof this.presentation.recoverFootprint !== 'function') throw new Error('Enemy presentation created footprints but has no recoverFootprint()');
        this.presentation.recoverFootprint(footprint);
        this.footprints.splice(i, 1);
      }
    }
  }

  /**
   * 原始方法符号：gameOver
   * 原始源码范围：bundle.strings-decoded.js:19702-19731
   * 行为可信度：HIGH
   */
  gameOver() {
    if (this.inPool || this.__InPool) return false;
    super.gameOver();
    if (this.buffManager && typeof this.buffManager.clearTarget === 'function') this.buffManager.clearTarget(this.id);
    if (this._registered) {
      this.eventBus.event(GameEvents.ENEMY_REMOVED, this.id, this);
      this._registered = false;
    }
    if (this.visual) this.visual.offAll();
    this.laya.timer.clearAll(this);
    this.remainingPathDistance = Infinity;
    this.targetable = false;
    this.knockbackActive = false;
    this.knockbackVelocity.x = 0;
    this.knockbackVelocity.y = 0;
    this.deathStarted = false;
    this._deathScheduled = false;
    this._contactDamageScheduled = false;
    this.path = null;
    this.lastPathIndex = 0;
    this.currentPathIndex = 0;
    this.currentState = EnemyRuntimeState.SPAWNING;
    this.curState = this.currentState;
    if (this.visual) {
      this.visual.visible = true;
      this.visual.anchorX = 0;
      this.visual.anchorY = 0;
      this.visual.scale(1, 1);
      this.visual.rotation = 0;
      this.visual.alpha = 1;
      this.visual.removeSelf();
    }
    if (this.healthBarDelayed) this.healthBarDelayed.width = this.healthBarWidth;
    if (this.healthBarImmediate) this.healthBarImmediate.width = this.healthBarWidth;
    this.startPosition.x = -1; this.startPosition.y = -1;
    this.firstPathCenter.x = -1; this.firstPathCenter.y = -1;
    this.damageContributors.length = 0;
    if (this.stunIndicator) this.stunIndicator.visible = false;
    for (const footprint of this.footprints.splice(0)) {
      if (typeof this.presentation.recoverFootprint !== 'function') throw new Error('Enemy presentation created footprints but has no recoverFootprint()');
      this.presentation.recoverFootprint(footprint);
    }
    this.presentation.resetForPool(this);
    this.inPool = true;
    this._lifecycleGeneration += 1;
    this.enemyFactory.recover(this);
    return true;
  }

  pos(x, y) { return this.visual.pos(x, y); }

  _requireConfigured() {
    if (!this._configured) throw new Error('EnemyBase.configure() must run before init()');
  }

  static resetRuntimeIdsForTests() { EnemyBase.nextRuntimeId = 1; }
}

EnemyBase.nextRuntimeId = 1;
EnemyBase.State = EnemyRuntimeState;
EnemyBase.CONTACT_ATTACK_COOLDOWN_MS = CONTACT_ATTACK_COOLDOWN_MS;
EnemyBase.CONTACT_DAMAGE_DELAY_MS = CONTACT_DAMAGE_DELAY_MS;

module.exports = { EnemyBase, EnemyRuntimeState };
