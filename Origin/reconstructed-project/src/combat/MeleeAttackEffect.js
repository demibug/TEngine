'use strict';

const { AttackResolver } = require('./AttackResolver');

/** 可复用的延迟近战/范围命中效果。 */
class MeleeAttackEffect {
  constructor(type = 'melee', resolver = new AttackResolver()) {
    this.type = type;
    this.resolver = resolver;
    this.reset();
  }

  reset() {
    this.owner = null;
    this.enemyManager = null;
    this.damage = 0;
    this.multiplier = 1;
    this.radius = 0;
    this.hitSet = new Set();
    this.active = false;
    this.elapsed = 0;
    this.durationMs = 0;
    this.hitAtMs = 0;
    this.hitTriggered = false;
    return this;
  }

  launch({ owner, enemyManager, damage = 0, multiplier = 1, radius = 48, durationMs = 180, hitAtMs = durationMs * 0.25 } = {}) {
    this.owner = owner;
    this.enemyManager = enemyManager;
    this.damage = damage;
    this.multiplier = multiplier;
    this.radius = radius;
    this.durationMs = Math.max(0, Number(durationMs) || 0);
    this.hitAtMs = Math.max(0, Number(hitAtMs) || 0);
    this.elapsed = 0;
    this.hitTriggered = false;
    this.active = true;
    return this;
  }

  update(deltaMs) {
    if (!this.active) return false;
    this.elapsed += Math.max(0, Number(deltaMs) || 0);
    if (!this.hitTriggered && this.elapsed >= this.hitAtMs) {
      this.hitTriggered = true;
      this.hit();
    }
    if (this.elapsed >= this.durationMs) this.cleanup('duration-complete');
    return this.active;
  }

  hit() {
    if (!this.active || !this.owner || !this.enemyManager) return;
    const node = this.owner.displayObject || this.owner.combatPosition || { x: 0, y: 0 };
    const targets = this.resolver.queryEnemyObjects({
      enemyManager: this.enemyManager,
      center: { x: Number(node.x) || 0, y: Number(node.y) || 0 },
      range: this.radius,
      side: this.owner.side,
    });
    for (const target of targets) {
      if (this.hitSet.has(target.id)) continue;
      this.hitSet.add(target.id);
      this.resolver.hit(target, this.damage * this.multiplier, this.owner);
    }
  }

  cleanup() {
    this.active = false;
    this.owner = null;
    this.enemyManager = null;
    this.hitSet.clear();
  }
}

module.exports = { MeleeAttackEffect };
