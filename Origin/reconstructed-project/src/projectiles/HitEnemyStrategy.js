'use strict';

/**
 * 原始范围：bundle.strings-decoded.js:24035-24040, 24936-24983
 * 原始符号：rW/oE；工厂 tc/oF；策略编号 100
 * 本轮只恢复 BowSoldier 使用的单目标 requestRemove 分支。
 */
class HitEnemyStrategy {
  constructor() {
    this.typeCode = HitEnemyStrategy.TYPE_CODE;
    this.targetIds = [];
    this.delayMs = 0;
    this.removeAfterHit = true;
    this.triggerMode = 'requestRemove';
    this.delayStarted = false;
    this.completed = false;
    this.poolKey = `HitEnemyStrategy${HitEnemyStrategy.TYPE_CODE}`;
  }

  reset({ targetId, targetIds, delayMs = 0, removeAfterHit = true, triggerMode = 'requestRemove' } = {}) {
    this.targetIds = Array.isArray(targetIds)
      ? targetIds.slice()
      : Number.isFinite(targetId) ? [targetId] : [];
    this.delayMs = Number(delayMs) || 0;
    this.removeAfterHit = removeAfterHit !== false;
    this.triggerMode = triggerMode || 'requestRemove';
    this.delayStarted = false;
    this.completed = false;
    return this;
  }

  recover() {
    this.targetIds.length = 0;
    this.delayMs = -1;
    this.removeAfterHit = true;
    this.triggerMode = 'requestRemove';
    this.delayStarted = false;
    this.completed = false;
    HitEnemyStrategy._pool.push(this);
  }

  static create(options) {
    const strategy = this._pool.length ? this._pool.pop() : new HitEnemyStrategy();
    return strategy.reset(options);
  }
}

HitEnemyStrategy.TYPE_CODE = 100;
HitEnemyStrategy._pool = [];

module.exports = { HitEnemyStrategy };
