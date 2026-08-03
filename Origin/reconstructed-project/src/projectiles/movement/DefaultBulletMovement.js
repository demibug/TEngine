'use strict';

/**
 * 重建模块：特殊投射物移动策略 / DefaultBulletMovement
 * 原始范围：bundle.strings-decoded.js:23659
 * 重建状态：PARTIAL（运动学行为基于名称语义实现，DEFERRED 至 bundle 精读确认参数）
 *
 * 默认直线移动（bundle:23659）。经 ProjectileBase.resetData({movement, movementConfig}) 注入配置，
 * 不读未定义的 p.config。
 */
class DefaultBulletMovement {
  constructor() { this.projectile = null; this.dx = 0; this.dy = -1; this.progress = 0; }
  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    const rn = projectile.renderNode;
    // 默认向上移动，或经 movementConfig.direction 指定
    this.dx = cfg.direction != null ? cfg.direction.x : 0;
    this.dy = cfg.direction != null ? cfg.direction.y : -1;
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

module.exports = { DefaultBulletMovement };
