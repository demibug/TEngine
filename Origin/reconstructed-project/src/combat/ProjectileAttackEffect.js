'use strict';

/** 统一投射物创建/回收适配器；具体飞行和命中仍由 ProjectileManager 负责。 */
class ProjectileAttackEffect {
  constructor() { this.reset(); }

  reset() {
    this.type = 'projectile';
    this.owner = null;
    this.projectileManager = null;
    this.projectile = null;
    this.active = false;
    return this;
  }

  launch({ owner, projectileManager, config = {}, startPoint = { x: 0, y: 0 } } = {}) {
    if (!owner || !projectileManager || typeof projectileManager.create !== 'function') {
      throw new TypeError('ProjectileAttackEffect requires owner and projectileManager.create()');
    }
    this.owner = owner;
    this.projectileManager = projectileManager;
    this.projectile = projectileManager.create({ ...config, attacker: owner }, startPoint);
    if (this.projectile && typeof this.projectile.fire === 'function') this.projectile.fire();
    this.active = Boolean(this.projectile);
    return this;
  }

  adopt({ owner, projectileManager, projectile } = {}) {
    if (!owner || !projectileManager || !projectile) {
      throw new TypeError('ProjectileAttackEffect.adopt requires owner, projectileManager and projectile');
    }
    this.owner = owner;
    this.projectileManager = projectileManager;
    this.projectile = projectile;
    this.active = Boolean(projectile.active);
    return this;
  }

  update() {
    if (!this.active) return false;
    if (!this.projectile || !this.projectile.active) this.cleanup('projectile-complete');
    return this.active;
  }

  cleanup(reason = 'cleanup') {
    if (this.projectile && this.projectile.active && this.projectileManager && typeof this.projectileManager.remove === 'function') {
      this.projectileManager.remove(this.projectile, reason);
    }
    this.reset();
  }
}

module.exports = { ProjectileAttackEffect };
