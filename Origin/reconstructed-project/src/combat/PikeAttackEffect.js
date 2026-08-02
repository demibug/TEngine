'use strict';

const { MeleeAttackEffect } = require('./MeleeAttackEffect');

class PikeAttackEffect extends MeleeAttackEffect {
  constructor() { super('pike'); }

  launch({ owner, target = null, enemyManager, damage = 0, radius = 48, durationMs = 180 } = {}) {
    this.target = target;
    return super.launch({ owner, enemyManager, damage, radius, durationMs, hitAtMs: durationMs * 0.25 });
  }

  cleanup(reason) {
    this.target = null;
    return super.cleanup(reason);
  }
}

module.exports = { PikeAttackEffect };
