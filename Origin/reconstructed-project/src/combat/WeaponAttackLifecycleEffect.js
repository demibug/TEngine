'use strict';

/**
 * 将引擎无关 WeaponAttackEffect 纳入 AttackEffectManager 生命周期。
 * 直接调用 Weapon.attack() 仍保持即时结算；GeneralUnit 通过 deferApply
 * 将结算推迟到统一攻击管理器的更新阶段。
 */
class WeaponAttackLifecycleEffect {
  constructor() { this.reset(); }

  reset() {
    this.type = 'weapon';
    this.owner = null;
    this.effect = null;
    this.result = null;
    this.active = false;
    this.applied = false;
    return this;
  }

  launch({ owner, effect } = {}) {
    if (!effect || typeof effect.apply !== 'function') {
      throw new TypeError('WeaponAttackLifecycleEffect requires WeaponAttackEffect.apply()');
    }
    this.owner = owner || null;
    this.effect = effect;
    this.result = null;
    this.applied = false;
    this.active = true;
    return this;
  }

  update() {
    if (!this.active) return false;
    this.result = this.effect.apply();
    this.applied = true;
    this.active = false;
    return false;
  }

  cleanup() {
    this.active = false;
    this.owner = null;
    this.effect = null;
    this.result = null;
    this.applied = false;
  }
}

module.exports = { WeaponAttackLifecycleEffect };
