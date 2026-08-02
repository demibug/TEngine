'use strict';

const { SoldierBase } = require('./SoldierBase');
const { UnitState } = require('./UnitBase');

function distance(a, b) {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return Math.sqrt(dx * dx + dy * dy);
}

/**
 * 重建模块：FRIENDLY-UNIT-COMBAT-01 / KnifeSoldier
 * 原始范围：bundle.strings-decoded.js:24443-24551
 * 原始工厂位置：tb.zx[0]
 * 原始表现键：knife
 * 重建状态：COMPLETE_FOR_KNIFE_COMBAT
 */
class KnifeSoldier extends SoldierBase {
  constructor() {
    super();
    this.animationKey = 'knife';
  }

  configure(options = {}) {
    super.configure(options);
    if (!options.attackTimeline) throw new TypeError('KnifeSoldier requires attackTimeline');
    this.attackTimeline = options.attackTimeline;
    return this;
  }

  initialize(unitText, side) {
    this.animationKey = 'knife';
    return super.initialize(unitText, side);
  }

  createAnimation() {
    super.createAnimation();
    this.animation.scale(1, 1);
  }

  attack() {
    return this.performKnifeAttack();
  }

  /** 原 Nx。会再次查询目标，而不是直接复用 BattleManager 上一轮候选。 */
  performKnifeAttack() {
    const centerX = this.displayObject.x + this.displayObject.width / 2;
    const centerY = this.displayObject.y + this.displayObject.height / 2;
    this.targets = this.enemyManager.queryTargets(centerX, centerY, this.attackRange, this.side);
    if (!this.targets || this.targets.length === 0) {
      this.changeState(UnitState.IDLE);
      return null;
    }

    // CONFIRMED：原首候选距离使用 Oc 左上角与候选左上角；后续候选使用双方中心点。
    let target = this.targets[0];
    let bestDistance = distance(
      { x: this.displayObject.x, y: this.displayObject.y },
      { x: target.x, y: target.y },
    );
    for (let index = 1; index < this.targets.length; index += 1) {
      const candidate = this.targets[index];
      const candidateDistance = distance(
        { x: centerX, y: centerY },
        {
          x: candidate.x + this.gameData.map.gridWidth / 2,
          y: candidate.y + this.gameData.map.gridHeight / 2,
        },
      );
      if (candidateDistance < bestDistance) {
        target = candidate;
        bestDistance = candidateDistance;
      }
    }

    const attackRecord = this.attackTimeline.start({
      attacker: this,
      target,
      damage: this.attackDamage,
    });
    this.audio.play('knife_attack');
    if (this.animation) {
      this.animation.on(this.laya.Event.STOPPED, this, this._onAttackAnimationStopped);
      this.animation.play('attack', false);
    }
    return attackRecord;
  }

  _onAttackAnimationStopped() {
    if (this.animation) this.animation.offAll(this.laya.Event.STOPPED);
  }

  onAttackStateExit() {
    if (this.animation) this.animation.offAll(this.laya.Event.STOPPED);
  }
}

module.exports = { KnifeSoldier };
