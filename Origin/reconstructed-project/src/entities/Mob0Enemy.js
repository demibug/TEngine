'use strict';

const { NormalEnemyBase } = require('./NormalEnemyBase');

/**
 * 重建模块：ENEMY-RUNTIME-01 / Mob0
 * 原始范围：bundle.strings-decoded.js:31062-31114
 * 原始主要符号：st
 * 重建状态：COMPLETE_FOR_MOB0_LIFECYCLE
 */
class Mob0Enemy extends NormalEnemyBase {
  constructor() {
    super();
    this.resourcePath = 'resources/img/gameObject/enemy/mob_0.png'; // JE
    this.typeIndex = 0;
    this.visualPoolKey = 'mob';
  }

  /**
   * 原始方法符号：init
   * 原始源码范围：bundle.strings-decoded.js:31071-31077
   * 行为可信度：HIGH
   */
  init(playerLane) {
    this.fastAnimation = false;
    this.visual = this.objectPool.takeByKey(this.visualPoolKey, this);
    this.enemy = this.visual;
    super.init(playerLane);
    if (!this.animation || typeof this.animation.pos !== 'function') throw new Error('Mob0 animation must implement pos()');
    this.animation.pos(this.visual.width / 2, this.visual.height);
    return this;
  }

  startMovingAnimation() {
    super.startMovingAnimation();
    this.presentation.startMob0Breathing(this);
  }

  stopMovingAnimation() {
    this.presentation.stopMob0Breathing(this);
    super.stopMovingAnimation();
  }

  static resetIdsForTests() {
    const { EnemyBase } = require('./EnemyBase');
    EnemyBase.resetRuntimeIdsForTests();
  }

  /** 原 st.gameOver：先执行 pe/ro 回收，再回收字符串池键 mob 的表现节点。 */
  gameOver() {
    if (this.inPool || this.__InPool) return false;
    const visual = this.visual;
    const result = super.gameOver();
    if (visual) this.objectPool.recoverByKey(this.visualPoolKey, visual);
    // CONFIRMED：原 st.gameOver 回收表现节点后仍保留 enemy 引用；
    // ro.move() 会在 Mw/gameOver 后继续执行一次 Pw 网格检查。
    return result;
  }
}

module.exports = { Mob0Enemy };
