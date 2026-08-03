'use strict';

/**
 * 武将主动技能 effect 共用的目标查询与伤害小工具。
 * 优先使用 enemyManager.queryEnemyObjects（返回含 .hit 的敌人对象）；
 * 若仅有 queryTargets（返回 DTO），则通过 getById 解析为对象。纯逻辑，不依赖表现层。
 */
function queryEnemyObjects(enemyManager, centerX, centerY, radius, side) {
  if (!enemyManager) return [];
  if (typeof enemyManager.queryEnemyObjects === 'function') {
    return enemyManager.queryEnemyObjects(centerX, centerY, radius, side) || [];
  }
  if (typeof enemyManager.queryTargets === 'function') {
    const dtos = enemyManager.queryTargets(centerX, centerY, radius, side) || [];
    const objs = [];
    for (const dto of dtos) {
      if (!dto || dto.id == null) continue;
      const enemy = typeof enemyManager.getById === 'function' ? enemyManager.getById(dto.id) : null;
      if (enemy) objs.push(enemy);
    }
    return objs;
  }
  return [];
}

/** 对一组敌人对象结算伤害；仅调用 enemy.hit，不引入表现层。返回实际命中数。 */
function applyDamageToObjects(enemies, damage, attacker) {
  let hit = 0;
  for (const enemy of enemies) {
    if (!enemy) continue;
    if (typeof enemy.hit === 'function') { enemy.hit(damage, attacker); hit++; }
    else if (typeof enemy.takeDamage === 'function') { enemy.takeDamage(damage, attacker); hit++; }
  }
  return hit;
}

module.exports = { queryEnemyObjects, applyDamageToObjects };
