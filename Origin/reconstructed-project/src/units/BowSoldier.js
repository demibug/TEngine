'use strict';

const { SoldierBase } = require('./SoldierBase');
const { HitEnemyStrategy } = require('../projectiles/HitEnemyStrategy');
const { TargetEnemyBezierMovement } = require('../projectiles/TargetEnemyBezierMovement');
const { SimpleDynamicArrow } = require('../projectiles/SimpleDynamicArrow');
const { quadraticTangentDegrees } = require('../projectiles/ProjectileMath');
const { ProjectileAttackEffect } = require('../combat/ProjectileAttackEffect');

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
    if (!options.attackEffectManager || typeof options.attackEffectManager.add !== 'function') {
      throw new TypeError('BowSoldier requires attackEffectManager');
    }
    this.projectileManager = options.projectileManager;
    this.attackEffectManager = options.attackEffectManager;
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
    if (this.attackEffectManager && typeof this.attackEffectManager.cancelOwner === 'function') {
      this.attackEffectManager.cancelOwner(this);
    }
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
    // STOPPED 正式动画事件契约：发射点由 `Laya.Event.STOPPED` 触发（`_onAttackAnimationStopped`→`launchArrow`）。
    // 正式 Laya/Spine 动画运行时接入后，攻击动画播到释放段末（`attackReleaseEventMs=650`）由动画事件驱动 STOPPED；
    // 无 Spine/无 Laya 动画运行时环境下由 `DevelopmentAnimationDriver` 按时长模拟 STOPPED（dev 桩为无 Spine 回退）。
    // 正式环境与 dev 回退经同一 `_onAttackAnimationStopped` 入口；规则层（`launchArrow`→`ProjectileAttackEffect`
    // 登记/更新/回收）只依赖「STOPPED 事件到达」这一契约信号，不依赖动画帧本身（对齐 CODEX_HANDOFF 行 440
    // 「不让表现动画成为规则唯一触发来源」）。
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

  /**
   * STOPPED 事件统一入口：无论 STOPPED 来自正式 Laya/Spine 动画事件还是
   * `DevelopmentAnimationDriver` 的时长模拟回退，均经此方法移除监听后调 `launchArrow`。
   * 规则层（`launchArrow`→`ProjectileAttackEffect` 登记/更新/回收）不关心 STOPPED 信号源，
   * 只依赖「STOPPED 事件到达」这一契约信号，不依赖动画帧本身。
   */
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
    const effect = this.attackEffectManager.create(ProjectileAttackEffect).launch({
      owner: this,
      projectileManager: this.projectileManager,
      config: {
        type: SimpleDynamicArrow.projectileTypeKey,
        appearance: SimpleDynamicArrow.DEFAULT_APPEARANCE,
        damage: this.attackDamage,
        speedScale: this.projectileSpeedScale,
        hitStrategy,
        movement,
      },
      startPoint,
    });
    this.attackEffectManager.add(effect);
    return effect.projectile;
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
