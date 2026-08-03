'use strict';

/**
 * 重建模块：特殊投射物移动策略 / TargetDirectionLineMovement
 * 原始范围：bundle.strings-decoded.js:36359
 * 重建状态：PARTIAL（运动学行为基于名称语义实现，DEFERRED 至 bundle 精读确认参数）
 *
 * 沿固定方向直线移动（bundle:36359）。经 ProjectileBase.resetData({movement, movementConfig}) 注入配置，
 * 不读未定义的 p.config。
 */
class TargetDirectionLineMovement {
  constructor() { this.projectile = null; this.dx = 1; this.dy = 0; this.progress = 0; }
  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    this.dx = cfg.dx != null ? cfg.dx : 1;
    this.dy = cfg.dy != null ? cfg.dy : 0;
    return this;
  }
  onFire() {}
  update(deltaMs, speed = 1) {
    if (!this.projectile) return;
    const t = (deltaMs / 1000) * speed * 100;
    this.projectile.renderNode.x += this.dx * t;
    this.projectile.renderNode.y += this.dy * t;
    this.progress += t;
  }
  recover() { this.projectile = null; this.progress = 0; }
}

module.exports = { TargetDirectionLineMovement };
