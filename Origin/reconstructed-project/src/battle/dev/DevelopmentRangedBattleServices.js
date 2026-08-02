'use strict';

class DevelopmentProjectileEffects {
  constructor() { this.calls = []; }
  showSimpleArrowHit(record) {
    this.calls.push({
      type: 'simple-arrow-hit',
      projectileId: record.arrow.projectileId,
      enemyId: record.enemy.id,
      damage: record.damage,
      alternate: record.alternate,
      applied: record.applied,
      centerX: record.centerX,
      centerY: record.centerY,
    });
  }
}

module.exports = { DevelopmentProjectileEffects };
