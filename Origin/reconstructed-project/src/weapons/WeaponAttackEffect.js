'use strict';

function targetIsUsable(target, attacker) {
  if (!target) return false;
  if (target.currentState === 4 || target.targetable === false) return false;
  if (attacker && typeof target.isTargetableBy === 'function' && !target.isTargetableBy(attacker.side)) return false;
  return typeof target.hit === 'function' || typeof target.takeDamage === 'function';
}

/**
 * Engine-neutral weapon impact. It deliberately owns no timers or visuals;
 * projectile and Unity adapters can use the same result contract.
 */
class WeaponAttackEffect {
  constructor({
    type = 'direct',
    attacker = null,
    target = null,
    targets = [],
    enemyManager = null,
    center = null,
    radius = 0,
    damage = 0,
    multiplier = 1,
    random = Math.random,
    maxTargets = Infinity,
    allowRepeat = false,
  } = {}) {
    this.type = type;
    this.attacker = attacker;
    this.target = target;
    this.targets = Array.isArray(targets) ? targets.filter(Boolean) : [];
    this.enemyManager = enemyManager;
    this.center = center;
    this.radius = Number(radius) || 0;
    this.damage = Number(damage) || 0;
    this.multiplier = Number(multiplier) || 1;
    this.random = typeof random === 'function' ? random : Math.random;
    this.maxTargets = Number.isFinite(Number(maxTargets)) ? Math.max(0, Number(maxTargets)) : Infinity;
    this.allowRepeat = Boolean(allowRepeat);
    this.hits = [];
    this.completed = false;
  }

  apply() {
    if (this.completed) return this.result();
    if (this.type === 'meteor-shower') this._applyMeteorShower();
    else if (this.type === 'area') this._applyArea();
    else this._hit(this.target || this.targets[0], this.damage * this.multiplier);
    this.completed = true;
    return this.result();
  }

  result() {
    return {
      type: this.type,
      damage: this.damage * this.multiplier,
      hits: this.hits.slice(),
      completed: this.completed,
    };
  }

  _applyArea() {
    const candidates = this._resolveAreaTargets();
    for (const target of candidates) this._hit(target, this.damage * this.multiplier);
  }

  _applyMeteorShower() {
    const candidates = this._resolveAreaTargets();
    if (!candidates.length) return;
    const count = Math.min(this.maxTargets, 5);
    const selected = [];
    for (let index = 0; index < count; index += 1) {
      const target = candidates[Math.floor(this.random() * candidates.length) % candidates.length];
      if (!this.allowRepeat && selected.includes(target)) {
        index -= 1;
        if (selected.length >= candidates.length) break;
        continue;
      }
      selected.push(target);
      this._hit(target, this.damage * this.multiplier);
    }
  }

  _resolveAreaTargets() {
    if (this.enemyManager && this.center && this.radius > 0) {
      const query = this.enemyManager.queryEnemyObjects || this.enemyManager.queryTargets;
      if (typeof query === 'function') {
        const result = query.call(this.enemyManager, this.center.x, this.center.y, this.radius, this.attacker?.side, []);
        if (Array.isArray(result) && result.length) return this._uniqueUsable(result);
      }
    }
    return this._uniqueUsable(this.targets.length ? this.targets : [this.target]);
  }

  _uniqueUsable(targets) {
    const seen = new Set();
    return targets.filter(target => {
      if (!targetIsUsable(target, this.attacker)) return false;
      const key = target.id == null ? target : target.id;
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    });
  }

  _hit(target, damage) {
    if (!targetIsUsable(target, this.attacker)) return false;
    const hit = typeof target.hit === 'function' ? target.hit.bind(target) : target.takeDamage.bind(target);
    const applied = hit(damage, this.attacker);
    this.hits.push({ targetId: target.id == null ? null : target.id, damage, applied });
    return applied !== false;
  }
}

module.exports = { WeaponAttackEffect };
