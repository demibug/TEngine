'use strict';

const KNIFE_HIT_DELAY_BASE_MS = 500; // hu[176]
const KNIFE_ATTACK_EFFECT_TYPE = 'knifeSoliderAttack'; // 原字符串拼写

/**
 * 最小正式攻击闭包：tO → r6 → oF → vA 中刀兵实际使用的分支。
 * 原始范围：
 * - bundle.strings-decoded.js:24484-24551
 * - bundle.strings-decoded.js:24840-24860
 * - bundle.strings-decoded.js:24936-25020（触发数据）
 * - bundle.strings-decoded.js:26264-26428
 * - bundle.strings-decoded.js:37209-37450（管理器相关分支）
 * 重建状态：COMPLETE_FOR_KNIFE_HIT_TIMING
 */
class KnifeAttackTimeline {
  constructor({ laya, enemyManager, effects, logger = console } = {}) {
    if (!laya || !laya.timer) throw new TypeError('KnifeAttackTimeline requires Laya.timer');
    if (!enemyManager) throw new TypeError('KnifeAttackTimeline requires EnemyManager');
    if (!effects || typeof effects.startKnifeAttack !== 'function' || typeof effects.showKnifeHit !== 'function') {
      throw new TypeError('KnifeAttackTimeline requires startKnifeAttack() and showKnifeHit() effects');
    }
    Object.assign(this, { laya, enemyManager, effects, logger });
    this.started = [];
    this.settled = [];
  }

  start({ attacker, target, damage }) {
    if (!attacker || !target) throw new TypeError('KnifeAttackTimeline.start requires attacker and target');
    const playbackRate = attacker.animationPlaybackRate || 1;
    const delayMs = KNIFE_HIT_DELAY_BASE_MS / playbackRate;
    const generation = attacker.lifecycleGeneration;
    const record = {
      type: KNIFE_ATTACK_EFFECT_TYPE,
      attackerId: attacker.id,
      targetId: target.id,
      damage,
      delayMs,
      generation,
      startedAt: this.laya.timer.currTimer,
      settled: false,
      cancelled: false,
    };
    this.started.push(record);
    this.effects.startKnifeAttack(record, attacker, target);

    this.laya.timer.once(delayMs, attacker, () => {
      if (generation !== attacker.lifecycleGeneration || attacker.inPool || attacker.destroyed || !attacker.isActive) {
        record.cancelled = true;
        this.settled.push(record);
        return;
      }
      const enemy = this.enemyManager.getById(target.id);
      if (!enemy || !enemy.isTargetableBy(attacker.side)) {
        record.cancelled = true;
        this.settled.push(record);
        return;
      }
      enemy.hit(damage, attacker);
      this.effects.showKnifeHit(record, attacker, enemy);
      record.settled = true;
      record.settledAt = this.laya.timer.currTimer;
      this.settled.push(record);
    });
    return record;
  }

  cancelFor(attacker) {
    this.laya.timer.clearAll(attacker);
  }

  resetForTests() {
    this.started.length = 0;
    this.settled.length = 0;
  }
}

module.exports = {
  KNIFE_HIT_DELAY_BASE_MS,
  KNIFE_ATTACK_EFFECT_TYPE,
  KnifeAttackTimeline,
};
