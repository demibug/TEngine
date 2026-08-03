'use strict';

const { MeleeAttackEffect } = require('./MeleeAttackEffect');

const CAVALRY_SWEEP_DELAY_MS = 150; // bundle.strings-decoded.js:24825

class CavalrySweepEffect extends MeleeAttackEffect {
  constructor() { super('cavalrySweep'); }

  launch({
    owner,
    target = null,
    enemyManager,
    damage = 0,
    multiplier = 1,
    radius = 96,
    delayMs = CAVALRY_SWEEP_DELAY_MS,
  } = {}) {
    this.target = target;
    const delay = Math.max(0, Number(delayMs) || 0);
    return super.launch({
      owner,
      enemyManager,
      damage,
      multiplier,
      radius,
      hitAtMs: delay,
      durationMs: delay + 120,
    });
  }

  cleanup(reason) {
    this.target = null;
    return super.cleanup(reason);
  }
}

module.exports = { CavalrySweepEffect, CAVALRY_SWEEP_DELAY_MS };
