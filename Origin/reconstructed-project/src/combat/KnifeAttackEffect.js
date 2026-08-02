'use strict';

/** 由 AttackEffectManager 驱动的刀兵延迟命中效果。 */
class KnifeAttackEffect {
  constructor() { this.reset(); }

  reset() {
    this.type = 'knifeSoliderAttack';
    this.owner = null;
    this.timeline = null;
    this.target = null;
    this.damage = 0;
    this.record = null;
    this.elapsed = 0;
    this.delayMs = 0;
    this.active = false;
    this.usesTimer = false;
    return this;
  }

  launch({ owner, timeline, target, damage, record, delayMs } = {}) {
    this.owner = owner;
    this.timeline = timeline;
    this.target = target;
    this.damage = damage;
    this.record = record;
    this.delayMs = Math.max(0, Number(delayMs) || 0);
    this.elapsed = 0;
    this.active = true;
    if (timeline && timeline.laya && timeline.laya.timer && typeof timeline.laya.timer.once === 'function') {
      this.usesTimer = true;
      timeline.laya.timer.once(this.delayMs, this, () => {
        if (!this.active) return;
        this.timeline.resolve(this);
        this.active = false;
      });
    }
    return this;
  }

  update(deltaMs) {
    if (!this.active) return false;
    if (this.usesTimer) return this.active;
    this.elapsed += Math.max(0, Number(deltaMs) || 0);
    if (this.elapsed >= this.delayMs) {
      this.timeline.resolve(this);
      this.active = false;
    }
    return this.active;
  }

  cleanup() {
    if (this.active && this.usesTimer && this.timeline && this.timeline.laya && this.timeline.laya.timer) {
      this.timeline.laya.timer.clearAll(this);
    }
    if (this.active && this.timeline) this.timeline.cancel(this);
    this.active = false;
    this.owner = null;
    this.timeline = null;
    this.target = null;
    this.record = null;
    this.usesTimer = false;
  }
}

module.exports = { KnifeAttackEffect };
