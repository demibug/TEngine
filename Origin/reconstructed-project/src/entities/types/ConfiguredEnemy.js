'use strict';
const { NormalEnemyBase } = require('../NormalEnemyBase');

class ConfiguredEnemy extends NormalEnemyBase {
  constructor({ typeKey, typeIndex, resourcePath, baseSpeedOverride = null, visualPoolKey = 'mob' } = {}) {
    super();
    this.typeKey = typeKey;
    this.typeIndex = typeIndex;
    this.resourcePath = resourcePath;
    this.baseSpeedOverride = baseSpeedOverride;
    this.visualPoolKey = visualPoolKey;
  }
  init(playerLane) {
    this.fastAnimation = false;
    this.visual = this.objectPool.takeByKey(this.visualPoolKey, this);
    this.enemy = this.visual;
    super.init(playerLane);
    if (this.baseSpeedOverride != null) this.baseMoveSpeed = this.baseSpeedOverride;
    if (this.animation && typeof this.animation.pos === 'function') this.animation.pos(this.visual.width / 2, this.visual.height);
    return this;
  }
  gameOver() {
    if (this.inPool || this.__InPool) return false;
    const visual = this.visual;
    const result = super.gameOver();
    if (visual) this.objectPool.recoverByKey(this.visualPoolKey, visual);
    return result;
  }
}
module.exports = { ConfiguredEnemy };
