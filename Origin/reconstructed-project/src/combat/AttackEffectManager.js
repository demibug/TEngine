'use strict';

/** 统一管理近战/范围攻击效果的更新、移除和战斗结束清理。 */
class AttackEffectManager {
  constructor({ objectPool = null } = {}) {
    this.effects = new Set();
    this.records = new Map();
    this.objectPool = objectPool;
    this.updateCount = 0;
  }

  create(ClassType, create = null) {
    if (typeof ClassType !== 'function') throw new TypeError('AttackEffectManager.create requires an effect class');
    if (this.objectPool && typeof this.objectPool.takeByClass === 'function') {
      const effect = this.objectPool.takeByClass(ClassType, create || (() => new ClassType()));
      if (typeof effect.reset === 'function') effect.reset();
      return effect;
    }
    return create ? create() : new ClassType();
  }

  add(effect, { poolClass = effect && effect.constructor } = {}) {
    if (!effect || typeof effect.update !== 'function' || typeof effect.cleanup !== 'function') {
      throw new TypeError('AttackEffectManager requires effects with update() and cleanup()');
    }
    this.effects.add(effect);
    this.records.set(effect, { poolClass });
    return effect;
  }

  update(deltaMs) {
    this.updateCount += 1;
    for (const effect of [...this.effects]) {
      if (!effect.active) {
        this._release(effect, 'effect-inactive');
        continue;
      }
      effect.update(deltaMs);
      if (!effect.active) this._release(effect, 'effect-complete');
    }
  }

  remove(effect, reason = 'removed') {
    if (!this.effects.has(effect)) return false;
    this._release(effect, reason);
    return true;
  }

  cancelOwner(owner, reason = 'owner-removed') {
    let count = 0;
    for (const effect of [...this.effects]) {
      if (effect.owner !== owner && effect.owner?.id !== owner?.id) continue;
      if (this.remove(effect, reason)) count += 1;
    }
    return count;
  }

  gameOver() {
    for (const effect of [...this.effects]) this.remove(effect, 'game-over');
    this.effects.clear();
  }

  resetForTests() {
    this.gameOver();
    this.updateCount = 0;
  }

  get activeCount() { return this.effects.size; }

  _release(effect, reason) {
    const record = this.records.get(effect);
    this.effects.delete(effect);
    this.records.delete(effect);
    effect.cleanup(reason);
    if (this.objectPool && record && record.poolClass && typeof this.objectPool.recoverByClass === 'function') {
      this.objectPool.recoverByClass(effect);
    }
  }
}

module.exports = { AttackEffectManager };
