'use strict';

/**
 * 重建模块：特殊投射物移动策略 / ForwardMovement
 * 原始范围：bundle.strings-decoded.js:39661
 * 重建状态：PARTIAL（运动学行为基于名称语义实现，DEFERRED 至 bundle 精读确认参数）
 *
 * 向前方移动（bundle:39661，方向由 attacker 朝向决定）。经 ProjectileBase.resetData({movement, movementConfig}) 注入配置，
 * 不读未定义的 p.config。
 */
class ForwardMovement {
  constructor() { this.projectile = null; this.dx = 0; this.dy = -1; this.progress = 0; }
  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    const attacker = projectile.attacker;
    // DEFERRED: bundle:39661 朝向来源待精读；暂用 movementConfig.direction 或默认向上
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

module.exports = { ForwardMovement };
