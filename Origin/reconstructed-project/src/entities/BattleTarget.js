'use strict';

/**
 * 重建对象：aDou 战斗目标
 * 原始创建位置：bundle.strings-decoded.js:58444-58489
 * 敌人接触伤害：bundle.strings-decoded.js:20311-20339
 * 重建状态：COMPLETE_FOR_ENEMY_CONTACT_DAMAGE
 *
 * 原敌人并不调用独立 aDou 组件的受击方法，而是直接修改 uq.au.Zi/Ki。
 * 本文件把该写入封装为兼容方法，数值、50ms 延迟与胜负事件仍由原状态对象负责。
 */
class BattleTarget {
  constructor({ laya }) {
    if (!laya || !laya.Sprite) throw new TypeError('BattleTarget requires Laya.Sprite');
    const node = new laya.Sprite();
    node.entityType = 'aDou';
    node.animationId = 'aDou';
    node.resourcePath = 'resources/anim/aDou/skeleton.json';
    node.fastMode = null;
    node.setIsFastMode = function setIsFastMode(value) { this.fastMode = Boolean(value); };
    node.side = null;
    node.isPlayerLaneTarget = null;
    node.battleState = null;
    node.damageLog = [];
    node.battleTargetState = 'CREATED';
    node.bindBattleTarget = function bindBattleTarget({ battleState, playerLaneTarget }) {
      if (!battleState) throw new TypeError('BattleTarget.bindBattleTarget requires BattleState');
      this.battleState = battleState;
      this.isPlayerLaneTarget = Boolean(playerLaneTarget);
      this.side = this.isPlayerLaneTarget;
      this.battleTargetState = 'ACTIVE';
      return this;
    };
    Object.defineProperties(node, {
      health: {
        enumerable: true,
        get() {
          if (!this.battleState) return null;
          return this.isPlayerLaneTarget ? this.battleState.playerHealth : this.battleState.opponentHealth;
        },
      },
      alive: { enumerable: true, get() { return this.health == null ? false : this.health > 0; } },
    });
    node.receiveEnemyContact = function receiveEnemyContact(amount, sourceEnemy) {
      if (!this.battleState) throw new Error('BattleTarget is not bound to BattleState');
      if (!Number.isFinite(amount) || amount <= 0) throw new TypeError('BattleTarget contact damage must be a positive number');
      if (!this.alive) return false;
      const before = this.health;
      this.battleState.contactOccurred = true; // Gi compatibility meaning established by contact write site.
      if (this.isPlayerLaneTarget) this.battleState.playerHealth -= amount;
      else this.battleState.opponentHealth -= amount;
      this.damageLog.push({ amount, before, after: this.health, sourceEnemyId: sourceEnemy ? sourceEnemy.id : null });
      if (!this.alive) this.battleTargetState = 'DESTROYED';
      return true;
    };
    node.Td = function resetSkeletonForPool() {
      this.removeSelf();
      this.battleTargetState = 'POOLED';
      this.battleState = null;
      this.isPlayerLaneTarget = null;
      this.side = null;
      this.damageLog.length = 0;
    };
    node.gameOver = function gameOver() { this.battleTargetState = 'ENDED'; };
    return node;
  }
}

module.exports = { BattleTarget };
