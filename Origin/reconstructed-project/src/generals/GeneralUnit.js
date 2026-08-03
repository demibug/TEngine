'use strict';

const {
  getGeneralDefinition,
  getGeneralDefinitionByIndex,
  GENERAL_ATTACK_SPEED_MULTIPLIERS,
  GENERAL_DAMAGE_MULTIPLIERS,
} = require('./GeneralDefinitions');
const { ProjectileAttackEffect } = require('../combat/ProjectileAttackEffect');
const { WeaponAttackLifecycleEffect } = require('../combat/WeaponAttackLifecycleEffect');

// 武将经验升级阈值。来源 bundle Ip(bundle.strings-decoded.js:11278)
// Ip=[0,10,b[21],b[66],b[171]](b=hu),hu 解码得 hu[21]=35、hu[66]=75、hu[171]=130,
// 故完整五级阈值为 [0,10,35,75,130](已 node 独立解码复核)。
const DEFAULT_GENERAL_EXPERIENCE_THRESHOLDS = Object.freeze([0, 10, 35, 75, 130]);
const GENERAL_MAX_LEVEL = 5;

const GeneralCombatState = Object.freeze({
  IDLE: 'GeneralIdle',
  ATTACK: 'GeneralAttack',
});

const GeneralLifecycleState = Object.freeze({
  ACTIVE: 'GeneralActive',
  DEAD: 'GeneralDead',
  RECYCLED: 'GeneralRecycled',
});

/** Engine-independent holder for the recovered GeneralBase combat state. */
class GeneralUnit {
  constructor({
    id = -1,
    name,
    side = true,
    level = 1,
    parts = [],
    combat = null,
    experienceThresholds = null,
    experience = 0,
    onLevelChanged = null,
    onExperienceChanged = null,
  } = {}) {
    this.id = id;
    this.definition = getGeneralDefinition(name);
    this.name = name;
    this.side = Boolean(side);
    this.level = Math.max(1, Math.min(GENERAL_MAX_LEVEL, Number(level) || 1));
    this.parts = parts.slice();
    this.partIds = this.parts.map(part => part.id);
    this.isPlayer = this.side;
    this.experienceThresholds = this._normalizeExperienceThresholds(experienceThresholds);
    this.experience = Math.max(0, Number(experience) || 0);
    this.onLevelChanged = typeof onLevelChanged === 'function' ? onLevelChanged : null;
    this.onExperienceChanged = typeof onExperienceChanged === 'function' ? onExperienceChanged : null;
    this.weaponId = null;
    this.weapon = null;
    this.weaponAttackPowerBonus = 0;
    this.weaponRangeBonus = 0;
    this.weaponAttackSpeedBonus = 0;
    this.skill = null;
    this.skillKey = null;
    this.skillManager = null;
    this.active = true;
    this.inPool = false;
    this.lifecycleState = GeneralLifecycleState.ACTIVE;
    this.deathReason = null;
    this.stats = this.getLevelStats();

    // GeneralUnit used to stop at merge/state data. These fields form the
    // engine-neutral attack-loop contract; concrete damage/effect rules remain
    // injected by the caller and are restored in later P0 tasks.
    this.currentState = GeneralCombatState.IDLE;
    this.lastAttackTime = 0;
    this.targets = [];
    this.targetId = -1;
    this.attackCount = 0;
    this.baseAttackPower = 0;
    this.baseAttackRange = 0;
    this.baseAttackIntervalSeconds = 1;
    this.attackPowerBonus = 0;
    this.rangeBonus = 0;
    this.attackSpeedBonus = 0;
    this.combatPosition = { x: 0, y: 0, width: 0, height: 0 };
    this.enemyManager = null;
    this.targetPolicy = 'nearest';
    this.targetSelector = null;
    this.attackExecutor = null;
    this.attackEffectManager = null;
    this.projectileManager = null;
    this.combatClock = Date.now;
    this.onStateChange = null;
    this.lastAttackResult = null;
    this.combatConfigured = false;

    // 读取存档/合成时若同时带入经验，构造完成即保持等级与经验一致。
    this.refreshLevelFromExperience();

    if (combat) this.configureCombat(combat);
  }

  init(parts, isPlayer = true, typeIndex = this.definition.index) {
    const definition = getGeneralDefinitionByIndex(typeIndex);
    this.definition = definition;
    this.name = definition.name;
    this.parts = parts.slice();
    this.partIds = this.parts.map(part => part.id);
    this.side = Boolean(isPlayer);
    this.isPlayer = Boolean(isPlayer);
    this.active = true;
    this.inPool = false;
    this.lifecycleState = GeneralLifecycleState.ACTIVE;
    this.deathReason = null;
    this.stats = this.getLevelStats();
    this.currentState = GeneralCombatState.IDLE;
    this.lastAttackTime = 0;
    this.targets.length = 0;
    this.targetId = -1;
    this.lastAttackResult = null;
    return this;
  }

  hE(experience) {
    return this.setExperience(experience);
  }

  setExperience(experience, { refreshLevel = true, notify = true } = {}) {
    const nextExperience = Math.max(0, Number(experience) || 0);
    const previousExperience = this.experience;
    this.experience = nextExperience;
    if (refreshLevel) this.refreshLevelFromExperience();
    if (notify && previousExperience !== nextExperience && typeof this.onExperienceChanged === 'function') {
      this.onExperienceChanged(nextExperience, previousExperience, this);
    }
    return this;
  }

  addExperience(amount) {
    const value = Number(amount);
    if (!Number.isFinite(value) || value < 0) throw new TypeError('GeneralUnit experience amount must be a non-negative number');
    const previousExperience = this.experience;
    const previousLevel = this.level;
    this.setExperience(previousExperience + value);
    return {
      gained: value,
      previousExperience,
      experience: this.experience,
      previousLevel,
      level: this.level,
      levelsGained: this.level - previousLevel,
      levelChanged: this.level !== previousLevel,
    };
  }

  gainExperience(amount) {
    return this.addExperience(amount);
  }

  setLevel(level, { notify = true } = {}) {
    const previousLevel = this.level;
    this.level = Math.max(1, Math.min(GENERAL_MAX_LEVEL, Number(level) || 1));
    this.stats = this.getLevelStats();
    if (notify && previousLevel !== this.level && typeof this.onLevelChanged === 'function') {
      this.onLevelChanged(this.level, previousLevel, this);
    }
    return this;
  }

  refreshLevelFromExperience() {
    let nextLevel = this.level;
    for (let candidate = this.level + 1; candidate <= GENERAL_MAX_LEVEL; candidate += 1) {
      const threshold = this.experienceThresholds[candidate - 1];
      if (!Number.isFinite(threshold) || this.experience < threshold) break;
      nextLevel = candidate;
    }
    if (nextLevel !== this.level) this.setLevel(nextLevel);
    return this.level;
  }

  configureProgression({ experienceThresholds = this.experienceThresholds, onLevelChanged = this.onLevelChanged, onExperienceChanged = this.onExperienceChanged } = {}) {
    this.experienceThresholds = this._normalizeExperienceThresholds(experienceThresholds);
    this.onLevelChanged = typeof onLevelChanged === 'function' ? onLevelChanged : null;
    this.onExperienceChanged = typeof onExperienceChanged === 'function' ? onExperienceChanged : null;
    this.refreshLevelFromExperience();
    return this;
  }

  getExperienceToNextLevel() {
    const threshold = this.experienceThresholds[this.level];
    return Number.isFinite(threshold) ? Math.max(0, threshold - this.experience) : null;
  }

  _normalizeExperienceThresholds(thresholds) {
    const source = thresholds == null ? DEFAULT_GENERAL_EXPERIENCE_THRESHOLDS : thresholds;
    if (!Array.isArray(source)) throw new TypeError('GeneralUnit experienceThresholds must be an array');
    const normalized = Array.from({ length: GENERAL_MAX_LEVEL }, (_, index) => source[index] ?? null);
    normalized[0] = 0;
    let previous = 0;
    for (let index = 1; index < normalized.length; index += 1) {
      if (normalized[index] == null) continue;
      const value = Number(normalized[index]);
      if (!Number.isFinite(value) || value < previous) throw new TypeError('GeneralUnit experienceThresholds must be ascending finite numbers or null');
      normalized[index] = value;
      previous = value;
    }
    return Object.freeze(normalized);
  }

  getLevelStats() {
    const index = this.level - 1;
    return Object.freeze({
      level: this.level,
      attackSpeedMultiplier: GENERAL_ATTACK_SPEED_MULTIPLIERS[index],
      damageMultiplier: GENERAL_DAMAGE_MULTIPLIERS[index],
    });
  }

  attachWeapon(weapon, buffManager = null) {
    if (this.weapon && this.weapon !== weapon) this.detachWeapon();
    this.weapon = weapon;
    if (!weapon) return null;
    if (typeof weapon.attach === 'function') weapon.attach(this, buffManager);
    this._applyWeaponCombatModifiers(weapon);
    return weapon;
  }

  detachWeapon() {
    const weapon = this.weapon;
    if (!weapon) return null;
    if (typeof weapon.detach === 'function') weapon.detach();
    this.weapon = null;
    this.weaponAttackPowerBonus = 0;
    this.weaponRangeBonus = 0;
    this.weaponAttackSpeedBonus = 0;
    return weapon;
  }

  _applyWeaponCombatModifiers(weapon) {
    const raw = typeof weapon.getCombatModifiers === 'function'
      ? weapon.getCombatModifiers()
      : {
        attackPower: weapon.attackPowerBonus ?? weapon.addAttackPower,
        range: weapon.attackRangeBonus ?? weapon.rangeBonus,
        attackSpeed: weapon.attackSpeedBonus,
      };
    const modifiers = raw && typeof raw === 'object' ? raw : {};
    this.weaponAttackPowerBonus = this._nonNegativeNumber(modifiers.attackPower);
    this.weaponRangeBonus = this._nonNegativeNumber(modifiers.range);
    this.weaponAttackSpeedBonus = this._nonNegativeNumber(modifiers.attackSpeed);
  }

  _nonNegativeNumber(value) {
    return Number.isFinite(Number(value)) ? Math.max(0, Number(value)) : 0;
  }

  attachSkill(skill) {
    this.skill = skill;
    this.skillKey = skill && skill.key ? skill.key : this.skillKey;
    if (skill && typeof skill.bindOwner === 'function') skill.bindOwner(this);
    return skill;
  }

  configureSkill({ skillManager = null, skillKey = null, skill = null } = {}) {
    if (skillManager != null && typeof skillManager.attach !== 'function') {
      throw new TypeError('GeneralUnit skillManager requires attach()');
    }
    if (skillKey != null && typeof skillKey !== 'string') throw new TypeError('GeneralUnit skillKey must be a string');
    this.skillManager = skillManager;
    this.skillKey = skillKey;
    if (skill) this.attachSkill(skill);
    else if (skillManager && skillKey) this.attachSkill(skillManager.attach(this, skillKey));
    return this;
  }

  canTriggerSkill() {
    if (!this.active || !this.skill) return false;
    return typeof this.skill.canActivate === 'function' ? this.skill.canActivate() : typeof this.skill.activate === 'function';
  }

  triggerSkill(context = {}) {
    if (!this.active) return { activated: false, reason: 'owner-inactive' };
    if (!this.skill) return { activated: false, reason: 'no-skill' };
    if (this.skillManager && this.skillKey && typeof this.skillManager.activate === 'function') {
      return this.skillManager.activate(this.id, this.skillKey, { owner: this, ...context });
    }
    if (typeof this.skill.activate !== 'function') return { activated: false, reason: 'skill-not-activatable' };
    return this.skill.activate({ owner: this, ...context });
  }

  /**
   * Inject the pure combat dependencies for a merged general.
   *
   * No presentation object is required: position is a plain x/y/width/height
   * value (or a compatible display object supplied by an adapter).
   */
  configureCombat({
    enemyManager,
    position = this.combatPosition,
    attackPower = this.baseAttackPower,
    attackRange = this.baseAttackRange,
    attackIntervalSeconds = this.baseAttackIntervalSeconds,
    attackPowerBonus = this.attackPowerBonus,
    rangeBonus = this.rangeBonus,
    attackSpeedBonus = this.attackSpeedBonus,
    targetPolicy = 'nearest',
    targetSelector = null,
    attackExecutor = null,
    attackEffectManager = null,
    projectileManager = null,
    now = Date.now,
    onStateChange = null,
  } = {}) {
    if (!enemyManager || typeof enemyManager.queryTargets !== 'function') {
      throw new TypeError('GeneralUnit combat requires enemyManager.queryTargets()');
    }
    if (!Number.isFinite(Number(attackPower)) || Number(attackPower) < 0) {
      throw new TypeError('GeneralUnit attackPower must be a non-negative number');
    }
    if (!Number.isFinite(Number(attackRange)) || Number(attackRange) < 0) {
      throw new TypeError('GeneralUnit attackRange must be a non-negative number');
    }
    if (!Number.isFinite(Number(attackIntervalSeconds)) || Number(attackIntervalSeconds) <= 0) {
      throw new TypeError('GeneralUnit attackIntervalSeconds must be greater than zero');
    }
    for (const [name, value] of Object.entries({ attackPowerBonus, rangeBonus, attackSpeedBonus })) {
      if (!Number.isFinite(Number(value)) || Number(value) < 0) {
        throw new TypeError(`GeneralUnit ${name} must be a non-negative number`);
      }
    }
    if (typeof now !== 'function') throw new TypeError('GeneralUnit combat now must be a function');
    if (typeof targetPolicy !== 'string' && typeof targetPolicy !== 'function') {
      throw new TypeError('GeneralUnit targetPolicy must be a string or function');
    }
    if (targetSelector != null && typeof targetSelector !== 'function') {
      throw new TypeError('GeneralUnit targetSelector must be a function');
    }
    if (attackExecutor != null && typeof attackExecutor !== 'function') {
      throw new TypeError('GeneralUnit attackExecutor must be a function');
    }
    if (attackEffectManager != null && typeof attackEffectManager.add !== 'function') {
      throw new TypeError('GeneralUnit attackEffectManager requires add()');
    }

    this.enemyManager = enemyManager;
    this.setCombatPosition(position);
    this.baseAttackPower = Number(attackPower);
    this.baseAttackRange = Number(attackRange);
    this.baseAttackIntervalSeconds = Number(attackIntervalSeconds);
    this.attackPowerBonus = Number(attackPowerBonus);
    this.rangeBonus = Number(rangeBonus);
    this.attackSpeedBonus = Number(attackSpeedBonus);
    this.targetPolicy = targetPolicy;
    this.targetSelector = targetSelector;
    this.attackExecutor = attackExecutor;
    this.attackEffectManager = attackEffectManager;
    this.projectileManager = projectileManager;
    this.combatClock = now;
    this.onStateChange = onStateChange;
    this.combatConfigured = true;
    this.currentState = GeneralCombatState.IDLE;
    this.lastAttackTime = 0;
    this.targets.length = 0;
    this.targetId = -1;
    this.lastAttackResult = null;
    return this;
  }

  setCombatPosition(position = this.combatPosition) {
    if (!position || typeof position !== 'object') throw new TypeError('GeneralUnit combat position must be an object');
    this.combatPosition = position;
    return this;
  }

  get isActive() {
    return this.active && !this.inPool;
  }

  get isDead() {
    return this.lifecycleState === GeneralLifecycleState.DEAD || this.lifecycleState === GeneralLifecycleState.RECYCLED;
  }

  die(reason = 'combat') {
    if (this.inPool || this.lifecycleState === GeneralLifecycleState.DEAD) return false;
    if (this.attackEffectManager && typeof this.attackEffectManager.cancelOwner === 'function') {
      this.attackEffectManager.cancelOwner(this);
    }
    this.active = false;
    this.lifecycleState = GeneralLifecycleState.DEAD;
    this.deathReason = String(reason);
    this.currentState = GeneralCombatState.IDLE;
    this.targets.length = 0;
    this.targetId = -1;
    this.lastAttackResult = null;
    return true;
  }

  releaseParts() {
    const parts = this.parts.slice();
    for (const part of parts) {
      if (!part) continue;
      if (typeof part.unbindFromGeneral === 'function') part.unbindFromGeneral(this.id);
      else if (part.ownerId === this.id) part.ownerId = -1;
    }
    this.parts = [];
    this.partIds = [];
    return parts;
  }

  changeState(nextState) {
    if (nextState !== GeneralCombatState.IDLE && nextState !== GeneralCombatState.ATTACK) {
      throw new Error(`Unknown general combat state: ${nextState}`);
    }
    if (this.currentState === nextState) return this;
    const previous = this.currentState;
    this.currentState = nextState;
    if (typeof this.onStateChange === 'function') this.onStateChange(nextState, previous, this);
    return this;
  }

  get combatCenter() {
    const position = this.combatPosition || {};
    return {
      x: (Number(position.x) || 0) + (Number(position.width) || 0) / 2,
      y: (Number(position.y) || 0) + (Number(position.height) || 0) / 2,
    };
  }

  get attackDamage() {
    return this.baseAttackPower * this.stats.damageMultiplier + this.attackPowerBonus + this.weaponAttackPowerBonus;
  }

  get attackPower() {
    return this.attackDamage;
  }

  get attackRange() {
    return this.baseAttackRange + this.rangeBonus + this.weaponRangeBonus;
  }

  get attackIntervalSeconds() {
    const speed = this.stats.attackSpeedMultiplier * (1 + this.attackSpeedBonus + this.weaponAttackSpeedBonus);
    return this.baseAttackIntervalSeconds / speed;
  }

  get attacksPerSecond() {
    return 1 / this.attackIntervalSeconds;
  }

  selectTargets() {
    if (!this.combatConfigured || !this.active) return [];
    const center = this.combatCenter;
    const targets = this.targetSelector
      ? this.targetSelector({ owner: this, center, range: this.attackRange, side: this.side })
      : this.enemyManager.queryTargets(center.x, center.y, this.attackRange, this.side);
    this.targets = Array.isArray(targets) ? targets.filter(Boolean) : [];
    this.targets = this._sortTargets(this.targets, center);
    this.targetId = this.targets.length > 0 && this.targets[0].id != null ? this.targets[0].id : -1;
    return this.targets;
  }

  _sortTargets(targets, center) {
    if (typeof this.targetPolicy === 'function') {
      const sorted = this.targetPolicy({ owner: this, targets: targets.slice(), center, side: this.side });
      return Array.isArray(sorted) ? sorted.filter(Boolean) : targets;
    }
    if (this.targetPolicy === 'first') return targets;
    if (this.targetPolicy === 'closest_end') {
      return targets.slice().sort((a, b) => (Number(a.Bm) || 0) - (Number(b.Bm) || 0));
    }
    // The default policy matches the recovered knife-soldier strategy: nearest
    // target first, with the query order retained for equal distances.
    return targets
      .map((target, index) => ({ target, index, distance: this._targetDistanceSquared(target, center) }))
      .sort((a, b) => a.distance - b.distance || a.index - b.index)
      .map(entry => entry.target);
  }

  _targetDistanceSquared(target, center) {
    const dx = (Number(target.x) || 0) - center.x;
    const dy = (Number(target.y) || 0) - center.y;
    return dx * dx + dy * dy;
  }

  canAttackAt(now) {
    return Number(now) - this.lastAttackTime >= this.attackIntervalSeconds * 1000;
  }

  /** One attack decision/dispatch step. */
  updateCombat(now = this.combatClock()) {
    if (!this.combatConfigured || !this.active) return { attacked: false, reason: 'not-ready' };
    if (this.currentState !== GeneralCombatState.ATTACK) {
      if (!this.canAttackAt(now)) return { attacked: false, reason: 'cooldown' };
      if (this.selectTargets().length === 0) return { attacked: false, reason: 'no-target' };
      this.changeState(GeneralCombatState.ATTACK);
      return { attacked: false, state: GeneralCombatState.ATTACK, targetId: this.targetId };
    }
    if (!this.canAttackAt(now)) return { attacked: false, reason: 'cooldown' };
    return this.attack(now);
  }

  attack(now = this.combatClock()) {
    const targets = this.selectTargets();
    if (targets.length === 0) {
      this.changeState(GeneralCombatState.IDLE);
      return { attacked: false, reason: 'no-target' };
    }

    const context = {
      owner: this,
      target: targets[0],
      targets: targets.slice(),
      damage: this.attackDamage,
      range: this.attackRange,
      deferApply: Boolean(this.attackEffectManager),
    };
    let result = null;
    if (this.weapon && typeof this.weapon.attack === 'function') result = this.weapon.attack(context);
    else if (this.attackExecutor) result = this.attackExecutor(context);
    result = this._registerAttackEffects(result);
    // 技能每次攻击 hook（如跳斩溅射）：经 SkillEffectPort.onOwnerAttack 通知该武将名下活跃技能 effect。
    if (this.skillManager && this.skillManager.effectPort && typeof this.skillManager.effectPort.onOwnerAttack === 'function') {
      this.skillManager.effectPort.onOwnerAttack(this.id, context);
    }
    this.lastAttackTime = Number(now);
    this.attackCount += 1;
    this.lastAttackResult = result;
    this.changeState(GeneralCombatState.IDLE);
    return { attacked: true, targetId: this.targetId, result };
  }

  _registerAttackEffects(result) {
    const manager = this.attackEffectManager;
    if (!manager || result == null) return result;

    if (result.effect && typeof result.effect.apply === 'function') {
      const lifecycle = manager.create(WeaponAttackLifecycleEffect).launch({ owner: this, effect: result.effect });
      manager.add(lifecycle);
      result.effectHandle = lifecycle;
      return result;
    }

    const projectiles = Array.isArray(result) ? result : [result];
    const handles = [];
    for (const projectile of projectiles) {
      if (!projectile || projectile.projectileId == null || !projectile.manager) continue;
      const lifecycle = manager.create(ProjectileAttackEffect).adopt({
        owner: this,
        projectileManager: projectile.manager,
        projectile,
      });
      manager.add(lifecycle);
      handles.push(lifecycle);
    }
    if (handles.length) return { projectiles: result, effectHandles: handles };
    return result;
  }

  recycle(reason = 'game-over') {
    if (this.inPool) return false;
    this.die(reason);
    const weapon = this.detachWeapon();
    if (weapon && typeof weapon.gameOver === 'function') weapon.gameOver();
    if (this.skill && this.skillManager && typeof this.skillManager.removeOwner === 'function') this.skillManager.removeOwner(this.id);
    else if (this.skill && typeof this.skill.gameOver === 'function') this.skill.gameOver();
    this.active = false;
    this.weapon = null;
    this.skill = null;
    this.skillKey = null;
    this.skillManager = null;
    this.releaseParts();
    this.currentState = GeneralCombatState.IDLE;
    this.targets.length = 0;
    this.targetId = -1;
    this.lastAttackResult = null;
    this.combatConfigured = false;
    this.enemyManager = null;
    this.targetPolicy = 'nearest';
    this.targetSelector = null;
    this.attackExecutor = null;
    this.attackEffectManager = null;
    this.projectileManager = null;
    this.inPool = true;
    this.lifecycleState = GeneralLifecycleState.RECYCLED;
    return true;
  }

  gameOver() {
    return this.recycle('game-over');
  }
}

module.exports = { GeneralUnit, GeneralCombatState, GeneralLifecycleState };
