'use strict';

const { MeleeAttackEffect } = require('./MeleeAttackEffect');

// 来源: bundle.strings-decoded.js:24705-24749。
// 旋转 90ms、突刺 270ms 后由攻击效果开始结算，回收前再保留 120ms。
const PIKE_ATTACK_ROTATE_MS = 90;
const PIKE_ATTACK_THRUST_MS = 270;
const PIKE_HIT_DELAY_MS = PIKE_ATTACK_ROTATE_MS + PIKE_ATTACK_THRUST_MS;
const PIKE_EFFECT_DURATION_MS = PIKE_HIT_DELAY_MS + 120;

class PikeAttackEffect extends MeleeAttackEffect {
  constructor() { super('pike'); }

  launch({
    owner,
    target = null,
    enemyManager,
    damage = 0,
    radius = 48,
    playbackRate = 1,
    durationMs = null,
  } = {}) {
    this.target = target;
    const rate = Math.max(0.01, Number(playbackRate) || 1);
    const hitAtMs = PIKE_HIT_DELAY_MS / rate;
    const duration = durationMs == null
      ? PIKE_EFFECT_DURATION_MS / rate
      : Math.max(0, Number(durationMs) || 0);
    return super.launch({ owner, enemyManager, damage, radius, durationMs: duration, hitAtMs });
  }

  cleanup(reason) {
    this.target = null;
    return super.cleanup(reason);
  }
}

module.exports = {
  PikeAttackEffect,
  PIKE_ATTACK_ROTATE_MS,
  PIKE_ATTACK_THRUST_MS,
  PIKE_HIT_DELAY_MS,
  PIKE_EFFECT_DURATION_MS,
};
