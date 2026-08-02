'use strict';

const { MeleeAttackEffect } = require('./MeleeAttackEffect');

class CavalrySweepEffect extends MeleeAttackEffect {
  constructor() { super('cavalrySweep'); }

  launch({ owner, target = null, enemyManager, damage = 0, multiplier = 1, radius = 96, delayMs = 0 } = {}) {
    this.target = target;
    return super.launch({
      owner,
      enemyManager,
      damage,
      multiplier,
      radius,
      hitAtMs: delayMs,
      durationMs: Math.max(0, Number(delayMs) || 0) + 120,
    });
  }

  cleanup(reason) {
    this.target = null;
    return super.cleanup(reason);
  }
}

module.exports = { CavalrySweepEffect };
