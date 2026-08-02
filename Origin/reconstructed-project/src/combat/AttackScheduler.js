'use strict';

const { UnitState } = require('../units/UnitBase');
const { AttackResolver } = require('./AttackResolver');

/** 统一普通友军的目标查询、冷却和攻击派发。 */
class AttackScheduler {
  constructor({ enemyManager = null, resolver = new AttackResolver() } = {}) {
    this.enemyManager = enemyManager;
    this.resolver = resolver;
  }

  update(unit, { enemyManager = this.enemyManager, now = Date.now } = {}) {
    if (!unit || !unit.isActive || unit.disabled || unit.inPool) return { attacked: false, reason: 'inactive' };
    if (!unit.displayObject) throw new Error('Battle unit is missing its display object');
    const centerX = unit.displayObject.x + unit.displayObject.width / 2;
    const centerY = unit.displayObject.y + unit.displayObject.height / 2;
    const currentTime = now();
    const intervalMs = 1000 * (unit.attackIntervalSeconds != null ? unit.attackIntervalSeconds : unit.attackIntervalScale);

    if (unit.currentState !== UnitState.ATTACK) {
      unit.targets = this.resolver.queryTargets({ enemyManager, center: { x: centerX, y: centerY }, range: unit.attackRange, side: unit.side });
      if (unit.targets.length > 0 && currentTime - unit.lastAttackTime >= intervalMs) unit.changeState(UnitState.ATTACK);
      return { attacked: false, reason: unit.targets.length > 0 ? 'ready' : 'no-target' };
    }
    if (unit.disabled || unit.inPool) {
      unit.changeState(UnitState.IDLE);
      return { attacked: false, reason: 'inactive' };
    }
    if (currentTime - unit.lastAttackTime < intervalMs) return { attacked: false, reason: 'cooldown' };
    unit.lastAttackTime = currentTime;
    unit.targets = this.resolver.queryTargets({ enemyManager, center: { x: centerX, y: centerY }, range: unit.attackRange, side: unit.side });
    if (!unit.targets || unit.targets.length === 0) {
      unit.changeState(UnitState.IDLE);
      return { attacked: false, reason: 'no-target' };
    }
    const result = unit.weapon && typeof unit.weapon.attack === 'function'
      ? unit.weapon.attack(unit.targets[0])
      : unit.attack();
    return { attacked: true, result };
  }
}

module.exports = { AttackScheduler };
