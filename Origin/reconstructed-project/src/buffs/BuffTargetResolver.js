'use strict';

class BuffTargetResolver {
  constructor({ enemyManager = null, unitRegistry = null } = {}) {
    this.enemyManager = enemyManager;
    this.unitRegistry = unitRegistry;
  }
  configure({ enemyManager, unitRegistry } = {}) {
    if (enemyManager) this.enemyManager = enemyManager;
    if (unitRegistry) this.unitRegistry = unitRegistry;
    return this;
  }
  resolve(id) {
    const key = Number(id);
    if (this.enemyManager && this.enemyManager.enemies instanceof Map) {
      const enemy = this.enemyManager.enemies.get(key);
      if (enemy) return enemy;
    }
    const registry = this.unitRegistry;
    if (!registry) return null;
    for (const collection of [registry.PA, registry.BM, registry.AA]) {
      if (collection instanceof Map) {
        const value = collection.get(key);
        if (value) return value;
      }
    }
    return null;
  }
}
module.exports = { BuffTargetResolver };
