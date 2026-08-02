'use strict';

const {
  distance,
  distanceSquared,
  displayAngle,
  quadraticTangentDegrees,
  quadraticBezier,
} = require('./ProjectileMath');

/**
 * 重建模块：BOW-PROJECTILE-COMBAT-01 / target-tracking Bezier movement
 * 原始范围：bundle.strings-decoded.js:27588-27870
 * 原始符号：pP → on
 * 原始池键：TargetEnemyBezierMovement
 * 重建状态：COMPLETE_FOR_SIMPLE_DYNAMIC_ARROW
 */
class TargetEnemyBezierMovement {
  constructor() {
    this.projectile = null;
    this.enemyManager = null;
    this.gameData = null;
    this.targetId = -1;
    this.curveHeight = 50;
    this.distanceScaling = true;
    this.smoothRotation = false;
    this.hitRadiusEnabled = true;
    this.movementRate = 1;
    this.progress = 0;
    this.targetMissing = true;
    this.hitRadiusSquared = 0;
    this.startPosition = { x: 0, y: 0 };
    this.lastPosition = { x: 0, y: 0 };
    this.controlPoint = { x: 0, y: 0 };
    this.targetPosition = { x: 0, y: 0 };
  }

  configure({ enemyManager, gameData } = {}) {
    if (!enemyManager || !gameData) throw new TypeError('TargetEnemyBezierMovement requires enemyManager and gameData');
    this.enemyManager = enemyManager;
    this.gameData = gameData;
    return this;
  }

  reset(curveHeight = 50, distanceScaling = true, smoothRotation = false, hitRadiusEnabled = true) {
    this.curveHeight = Number(curveHeight);
    this.distanceScaling = Boolean(distanceScaling);
    this.smoothRotation = Boolean(smoothRotation);
    this.hitRadiusEnabled = Boolean(hitRadiusEnabled);
    this.movementRate = 1;
    this.progress = 0;
    this.targetMissing = true;
    this.hitRadiusSquared = 0;
    this.targetId = -1;
    this.projectile = null;
    this.startPosition.x = this.startPosition.y = 0;
    this.lastPosition.x = this.lastPosition.y = 0;
    this.controlPoint.x = this.controlPoint.y = 0;
    this.targetPosition.x = this.targetPosition.y = 0;
    return this;
  }

  setTargetId(targetId) {
    this.targetId = Number.isFinite(targetId) ? targetId : -1;
    this._refreshTargetPosition();
    return this;
  }

  attach(projectile) {
    this.projectile = projectile;
    const radius = this.hitRadiusEnabled ? projectile.renderNode.height / 1.5 : 0;
    this.hitRadiusSquared = radius * radius;
    if (this.targetMissing) {
      projectile.requestRemove(true);
      projectile.hide();
    }
  }

  onFire() {
    this.progress = 0;
    if (this.targetMissing) return;
    this.lastPosition.x = this.projectile.x;
    this.lastPosition.y = this.projectile.y;
    this.startPosition.x = this.projectile.x;
    this.startPosition.y = this.projectile.y;
    this._refreshTargetPosition();
    this.controlPoint.x = this.startPosition.x + (this.targetPosition.x - this.startPosition.x) / 2;
    this.controlPoint.y = this.startPosition.y + (this.targetPosition.y - this.startPosition.y) / 2 - this.curveHeight;
    if (this.projectile.rotationEnabled) {
      this.projectile.rotation = quadraticTangentDegrees(
        this.startPosition,
        this.controlPoint,
        this.targetPosition,
        0,
      ) + 90;
    }
  }

  /**
   * 原 on.Tk：delta 为毫秒；归一化进度增量为
   * delta * movementRate * projectileSpeedScale / 500。
   */
  update(deltaMs, projectileSpeedScale) {
    let progressDelta = deltaMs * this.movementRate * projectileSpeedScale / 500;
    if (!this.targetMissing) this._refreshTargetPosition();

    if (this.distanceScaling) {
      const originalDistance = distance(this.startPosition, this.targetPosition);
      const currentDistance = distance(this.projectile, this.targetPosition);
      if (originalDistance > 0) progressDelta *= Math.sqrt(Math.max(0.1, currentDistance / originalDistance));
    }

    this.progress += progressDelta;
    const renderNode = this.projectile.renderNode;
    if (!(distanceSquared(this.targetPosition, renderNode) < this.hitRadiusSquared) && this.progress < 1) {
      quadraticBezier(this.startPosition, this.controlPoint, this.targetPosition, renderNode, this.progress);
      if (this.projectile.rotationEnabled) {
        const nextAngle = displayAngle(this.lastPosition, renderNode);
        if (this.smoothRotation) {
          const difference = renderNode.rotation - nextAngle;
          const largeDifference = difference > 10;
          const amount = largeDifference ? deltaMs / (1.5 * difference) : 1;
          renderNode.rotation += (nextAngle - renderNode.rotation) * amount;
        } else renderNode.rotation = nextAngle;
      }
      this.lastPosition.x = renderNode.x;
      this.lastPosition.y = renderNode.y;
    } else {
      this.projectile.requestRemove();
    }
    this.projectile.hitEnabled = this.progress >= 0.8;
  }

  angleFrom(startPoint = this.startPosition) {
    if (this.targetMissing) return null;
    this._refreshTargetPosition();
    this.controlPoint.x = startPoint.x + (this.targetPosition.x - startPoint.x) / 2;
    this.controlPoint.y = startPoint.y + (this.targetPosition.y - startPoint.y) / 2 - this.curveHeight;
    return quadraticTangentDegrees(startPoint, this.controlPoint, this.targetPosition, 0) + 90;
  }

  beforeRecover() {}

  recover() {
    this.reset();
    this.enemyManager = null;
    this.gameData = null;
    TargetEnemyBezierMovement._pool.push(this);
  }

  _refreshTargetPosition() {
    const enemy = this.enemyManager && this.enemyManager.enemies.get(this.targetId);
    if (!enemy) {
      // CONFIRMED：飞行中丢失目标后保留最后终点，最终按旧 ID 尝试命中并安全失效。
      this.targetMissing = true;
      return false;
    }
    this.targetMissing = false;
    this.targetPosition.x = enemy.visual.x + this.gameData.map.gridWidth / 2;
    this.targetPosition.y = enemy.visual.y + this.gameData.map.gridHeight / 2;
    return true;
  }

  static create({ enemyManager, gameData, curveHeight = 50, distanceScaling = true, smoothRotation = false, hitRadiusEnabled = true } = {}) {
    const movement = this._pool.length ? this._pool.pop() : new TargetEnemyBezierMovement();
    return movement
      .reset(curveHeight, distanceScaling, smoothRotation, hitRadiusEnabled)
      .configure({ enemyManager, gameData });
  }
}

TargetEnemyBezierMovement.POOL_KEY = 'TargetEnemyBezierMovement';
TargetEnemyBezierMovement._pool = [];

module.exports = { TargetEnemyBezierMovement };
