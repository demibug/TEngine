'use strict';

/**
 * 重建来源：
 * - bundle.strings-decoded.js:24472-24544（刀兵 Nx）
 * - bundle.strings-decoded.js:24840-24860（tO）
 * - bundle.strings-decoded.js:26264-26428（r6）
 * - bundle.strings-decoded.js:27874-28110（qY.LS）
 * 状态：COMPLETE_FOR_EXPLICIT_SINGLE_TARGET_DAMAGE
 */
class KnifeHitAreaAttack {
  constructor() { this.resetForReuse(); }

  configure({ enemyManager, objectPool, presentation = null } = {}) {
    if (!enemyManager || !objectPool) throw new TypeError('KnifeHitAreaAttack requires enemyManager and objectPool');
    Object.assign(this, { enemyManager, objectPool, presentation });
    return this;
  }

  reset({ attacker, damage, targetId, targetCooldownMs, sign = 'knifeSoliderAttack' }) {
    this.attacker = attacker;
    this.damage = damage;
    this.targetId = targetId;
    this.targetCooldownMs = targetCooldownMs;
    this.sign = sign;
    this.started = false;
    this.completed = false;
    return this;
  }

  start() {
    if (this.started) return false;
    this.started = true;
    const enemy = this.enemyManager.enemies.get(this.targetId);
    if (enemy && enemy.isTargetableBy(this.attacker.side)) {
      enemy.hit(this.damage, this.attacker);
      if (this.presentation) this.presentation.onKnifeHit(this, enemy);
    }
    this.completed = true;
    this.release();
    return true;
  }

  release() {
    this.resetForReuse();
    this.objectPool.recoverByClass(this);
  }

  resetForReuse() {
    this.attacker = null;
    this.damage = 0;
    this.targetId = -1;
    this.targetCooldownMs = 0;
    this.sign = null;
    this.started = false;
    this.completed = false;
  }
}

class KnifeAttackFactory {
  constructor({ enemyManager, objectPool, presentation = null } = {}) {
    if (!enemyManager || !objectPool) throw new TypeError('KnifeAttackFactory requires enemyManager and objectPool');
    Object.assign(this, { enemyManager, objectPool, presentation });
    this.created = [];
  }

  create(config) {
    const attack = this.objectPool.takeByClass(KnifeHitAreaAttack);
    attack.configure(this).reset(config);
    this.created.push({ ...config, attack });
    return attack;
  }
}

module.exports = { KnifeHitAreaAttack, KnifeAttackFactory };
