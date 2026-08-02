'use strict';

const { EnemyBase, EnemyRuntimeState } = require('./EnemyBase');

/**
 * 重建模块：ENEMY-RUNTIME-01 普通敌人表现/死亡层
 * 原始范围：bundle.strings-decoded.js:31262-31482
 * 原始符号：pe
 * 重建状态：COMPLETE_FOR_MOB0_LIFECYCLE
 *
 * 重要结论：pe 继承 ro；它不是 MovementController。路径移动仍由 ro/EnemyBase 实现。
 */
class NormalEnemyBase extends EnemyBase {
  constructor() {
    super();
    this.blowUpCurve = { start: null, control1: null, control2: null, time: 0 }; // QE
    this.blowUpState = 0; // ZE
    this.resourcePath = null; // JE
  }

  init(playerLane) {
    super.init(playerLane);
    this._initializeStatsAndAnimation();
    this.healthText.text = this.health.toFixed(0);
    this.healthText.visible = false;
    this.healthBarBackground.visible = false;
    this.getPath();
    this.visual.visible = false;
    const generation = this._lifecycleGeneration;
    this.presentation.playSpawn(this, () => {
      if (generation !== this._lifecycleGeneration || this.inPool) return;
      this.changeState(EnemyRuntimeState.MOVING);
      this.healthBarImmediate.width = 0;
      this.healthBarBackground.visible = true;
      this.healthBarImmediate.width = this.healthBarWidth;
    });
    return this;
  }

  _initializeStatsAndAnimation() {
    const stats = this.gameData.resolveEnemyStats(this.typeIndex, this.isPlayerLane);
    this.health = this.typeIndex === 4 ? stats.ph / 2 : stats.ph;
    this.maxHealthBase = stats.ph;
    this.baseMoveSpeed = stats.speed;
    this.healthBarImmediate.skin = this.typeIndex === 4
      ? 'resources/img/gameObject/enemy/hp3.png'
      : 'resources/img/gameObject/enemy/hp2.png';
    this.animation = this.visual.getChildByName('sp');
    if (!this.animation) {
      this.animation = this.presentation.createAnimation(this, this.resourcePath, this.fastAnimation);
      if (!this.animation || typeof this.visual.addChild !== 'function') throw new Error('Enemy presentation failed to create animation child');
      this.animation.name = 'sp';
      this.visual.addChild(this.animation);
    }
    if (typeof this.animation.play !== 'function') throw new Error('Enemy animation must implement play()');
    this.animation.play('animation', true);
  }

  get moveSpeed() {
    const speed = this.baseMoveSpeed + this.moveSpeedModifier;
    this.playbackRate = speed / this.baseMoveSpeed;
    this.presentation.setMovePlaybackRate(this, this.playbackRate);
    return speed;
  }

  hit(damage, attacker = null) {
    const applied = super.hit(damage, attacker);
    return applied;
  }

  beginDeath() {
    if (this.deathStarted) return;
    super.beginDeath();
    this._deathScheduled = true;
    const generation = this._lifecycleGeneration;
    this.presentation.playDeath(this, this.typeIndex === 4 ? '#c1f6cb' : '#000000', () => {
      if (generation !== this._lifecycleGeneration || this.inPool) return;
      this._deathScheduled = false;
      this.visual.alpha = 1;
      this.visual.visible = false;
      this.gameOver();
    });
  }

  gameOver() {
    if (this.inPool || this.__InPool) return false;
    if (this.animation) this.presentation.stopMoving(this);
    const animation = this.animation;
    const result = super.gameOver();
    this.blowUpState = 0;
    if (animation) {
      this.presentation.resetAnimation(animation);
      if (typeof animation.removeSelf === 'function') animation.removeSelf();
      if (typeof animation.recover === 'function') animation.recover();
    }
    this.animation = null;
    return result;
  }
}

module.exports = { NormalEnemyBase };
