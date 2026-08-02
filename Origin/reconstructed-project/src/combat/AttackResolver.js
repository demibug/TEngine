'use strict';

/**
 * 统一攻击目标解析和命中边界。
 * 该对象只依赖 EnemyManager 的纯逻辑查询接口，不承载动画或对象池状态。
 */
class AttackResolver {
  queryTargets({ enemyManager, center, range, side } = {}) {
    if (!enemyManager || typeof enemyManager.queryTargets !== 'function') return [];
    const point = center || { x: 0, y: 0 };
    return enemyManager.queryTargets(point.x, point.y, range, side) || [];
  }

  queryEnemyObjects({ enemyManager, center, range, side } = {}) {
    if (!enemyManager || typeof enemyManager.queryEnemyObjects !== 'function') return [];
    const point = center || { x: 0, y: 0 };
    return enemyManager.queryEnemyObjects(point.x, point.y, range, side, []) || [];
  }

  hit(target, damage, attacker) {
    if (!target) return false;
    if (typeof target.hit === 'function') return target.hit(damage, attacker);
    if (typeof target.takeDamage === 'function') return target.takeDamage(damage, attacker);
    return false;
  }
}

module.exports = { AttackResolver };
