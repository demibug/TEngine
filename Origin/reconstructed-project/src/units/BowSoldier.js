'use strict';

const { SoldierBase } = require('./SoldierBase');
const { HitEnemyStrategy } = require('../projectiles/HitEnemyStrategy');
const { TargetEnemyBezierMovement } = require('../projectiles/TargetEnemyBezierMovement');
const { SimpleDynamicArrow } = require('../projectiles/SimpleDynamicArrow');
const { quadraticTangentDegrees } = require('../projectiles/ProjectileMath');

/**
 * 重建模块：BOW-PROJECTILE-COMBAT-01 / BowSoldier
 * 原始范围：bundle.strings-decoded.js:26093-26264
 * 原始主要符号：ok
 * 重建状态：COMPLETE_FOR_BOW_PROJECTILE_COMBAT
 */
class BowSoldier extends SoldierBase {
  constructor() {
    super();
    this.animationKey = 'bow';
    this.targetId = -1; // ux
    this.initialAnimationPlaybackRate = 1.25;
    this.attackAnimationEndMs = 1000;
    this.attackReleaseEventMs = 650;
    this.projectileSpeedScale = 1.75;
    this.pendingInitialAngle = null;
  }

  configure(options = {}) {
    super.configure(options);
    if (!options.projectileManager) throw new TypeError('BowSoldier requires projectileManager');
    this.projectileManager = options.projectileManager;
    return this;
  }

  initialize(unitText, side) {
    this.animationKey = 'bow';
    this.targetId = -1;
    this.pendingInitialAngle = null;
    return super.initialize(unitText, side);
  }

  createAnimation() {
    super.createAnimation();
    if (typeof this.animation.setInitPlaybackRate !== 'function') {
      throw new Error('Bow animation must implement setInitPlaybackRate()');
    }
    this.animation.setInitPlaybackRate(this.initialAnimationPlaybackRate);
  }

  gameOver() {
    if (this.animation) {
      if (this.laya.Tween && typeof this.laya.Tween.killAll === 'function') this.laya.Tween.killAll(this.animation);
      this.animation.rotation = 0;
      this.animation.offAll(this.laya.Event.STOPPED);
    }
    this.targetId = -1;
    this.pendingInitialAngle = null;
    return super.gameOver();
  }

  /** 原 J_：恢复到 0° 的 650ms 线性 Tween。 */
  resetAttackRotation() {
    if (!this.animation) return;
    if (this.laya.Tween && typeof this.laya.Tween.create === 'function') {
      this.laya.Tween.create(this.animation)
        .to('rotation', 0)
        .duration(650)
        .ease(this.laya.Ease.linearInOut);
    } else this.animation.rotation = 0;
  }

  /** 原 ox：按剩余路径距离 Bm 最小值选择，非几何最近距离。 */
  selectTarget(validateCurrentEnemy = false) {
    let selected = { id: -1, x: 0, y: 0, Bm: Infinity };
    for (const candidate of this.targets) {
      if (validateCurrentEnemy) {
        const enemy = this.enemyManager.enemies.get(candidate.id);
        if (!enemy || !enemy.targetable) continue;
      }
      if (candidate.Bm < selected.Bm) selected = candidate;
    }
    return selected;
  }

  attack() {
    const selected = this.selectTarget(false);
    this.targetId = selected.id;
    if (this.targetId < 0) return null;

    const start = this._unitCenter();
    this.pendingInitialAngle = this._calculateLaunchAngle(start, this.targetId, 120);
    this.animation.on(this.laya.Event.STOPPED, this, this._onAttackAnimationStopped);
    this.animation.play('attack', false, true, 0, this.attackReleaseEventMs);

    const currentRotation = this.animation.rotation;
    const delta = shortestAngleDelta(currentRotation, this.pendingInitialAngle);
    if (this.laya.Tween && typeof this.laya.Tween.to === 'function') {
      this.laya.Tween.to(
        this.animation,
        { rotation: currentRotation + delta },
        this.attackReleaseEventMs,
        this.laya.Ease.linearInOut,
      );
    } else this.animation.rotation = currentRotation + delta;
    return { targetId: this.targetId, launchAngle: this.pendingInitialAngle };
  }

  _onAttackAnimationStopped() {
    if (!this.animation) return;
    this.animation.offAll(this.laya.Event.STOPPED);
    this.launchArrow();
  }

  /** 原 yx：STOPPED 到来后再次验证目标，再创建正式动态箭矢。 */
  launchArrow() {
    this.audio.play('bow_attack');
    const current = this.enemyManager.enemies.get(this.targetId);
    if (!current || !current.targetable) this.targetId = this.selectTarget(true).id;

    const hitStrategy = HitEnemyStrategy.create({ targetId: this.targetId });
    const movement = TargetEnemyBezierMovement.create({
      enemyManager: this.enemyManager,
      gameData: this.gameData,
      curveHeight: 120,
      distanceScaling: true,
      smoothRotation: false,
      hitRadiusEnabled: true,
    }).setTargetId(this.targetId);

    this.animation.play('attack', false, true, this.attackReleaseEventMs, this.attackAnimationEndMs);
    const startPoint = this._unitCenter();
    const projectile = this.projectileManager.create({
      type: SimpleDynamicArrow.projectileTypeKey,
      appearance: SimpleDynamicArrow.DEFAULT_APPEARANCE,
      attacker: this,
      damage: this.attackDamage,
      speedScale: this.projectileSpeedScale,
      hitStrategy,
      movement,
    }, startPoint);
    projectile.fire();
    return projectile;
  }

  _unitCenter() {
    return {
      x: this.displayObject.x + this.displayObject.width / 2,
      y: this.displayObject.y + this.displayObject.height / 2,
    };
  }

  _targetCenter(targetId) {
    const enemy = this.enemyManager.enemies.get(targetId);
    if (!enemy) return null;
    return {
      x: enemy.visual.x + this.gameData.map.gridWidth / 2,
      y: enemy.visual.y + this.gameData.map.gridHeight / 2,
    };
  }

  _calculateLaunchAngle(start, targetId, curveHeight) {
    const target = this._targetCenter(targetId);
    if (!target) return null;
    const control = {
      x: start.x + (target.x - start.x) / 2,
      y: start.y + (target.y - start.y) / 2 - curveHeight,
    };
    return quadraticTangentDegrees(start, control, target, 0) + 90;
  }

  onAttackStateExit() {
    if (this.animation) this.animation.offAll(this.laya.Event.STOPPED);
    this.resetAttackRotation();
  }

  resetData() {
    super.resetData();
    this.targetId = -1;
    this.pendingInitialAngle = null;
    this.initialAnimationPlaybackRate = 1.25;
    this.attackAnimationEndMs = 1000;
    this.attackReleaseEventMs = 650;
    this.projectileSpeedScale = 1.75;
  }
}

function shortestAngleDelta(from, to) {
  if (!Number.isFinite(to)) return 0;
  let delta = (to - from) % 360;
  if (delta > 180) delta -= 360;
  if (delta < -180) delta += 360;
  return delta;
}

module.exports = { BowSoldier, shortestAngleDelta };
