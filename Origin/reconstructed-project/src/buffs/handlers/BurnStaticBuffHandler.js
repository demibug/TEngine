'use strict';
const { StateBuffHandler } = require('../StateBuffHandler');

/** 原 uR：各火焰层独立计时，每 1000ms 汇总所有层伤害。 */
class BurnStaticBuffHandler extends StateBuffHandler {
  constructor() {
    super({ mergeLayers: false });
    this.tickIntervalMs = 1000;
    this.tickTimer = 1000;
  }

  needsUpdate() { return true; }

  update(deltaMs) {
    this.tickTimer += deltaMs;
    if (this.tickTimer >= this.tickIntervalMs && this.layers.length) {
      this.tickTimer = 0;
      const damage = this.layers.reduce((sum, layer) => sum + (Number(layer.num) || 0), 0);
      if (damage) this.target.setState(4, true, damage);
    }
    super.update(deltaMs);
  }

  label() { return '火焰灼烧'; }
  remove() { super.remove(); this.tickTimer = this.tickIntervalMs; }
}

module.exports = { BurnStaticBuffHandler };
