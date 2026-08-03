'use strict';

/**
 * 重建模块：特殊投射物移动策略 / TargetDirectionWaveMovement
 * 原始范围：bundle.strings-decoded.js:34847
 * 重建状态：PARTIAL（运动学行为基于名称语义实现，DEFERRED 至 bundle 精读确认参数）
 *
 * 沿方向波浪移动（bundle:34847）。经 ProjectileBase.resetData({movement, movementConfig}) 注入配置，
 * 不读未定义的 p.config。
 */
class TargetDirectionWaveMovement {
  constructor() { this.projectile = null; this.dx = 1; this.dy = 0; this.amplitude = 20; this.frequency = 5; this.elapsed = 0; this.baseY = 0; }
  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    this.dx = cfg.dx != null ? cfg.dx : 1;
    this.dy = cfg.dy != null ? cfg.dy : 0;
    this.amplitude = cfg.amplitude != null ? cfg.amplitude : 20;
    this.frequency = cfg.frequency != null ? cfg.frequency : 5;
    this.elapsed = 0;
    this.baseY = projectile.renderNode.y;
    return this;
  }
  onFire() {}
  update(deltaMs, speed = 1) {
    if (!this.projectile) return;
    const t = (deltaMs / 1000) * speed * 100;
    this.elapsed += deltaMs;
    this.projectile.renderNode.x += this.dx * t;
    this.projectile.renderNode.y = this.baseY + Math.sin(this.elapsed / 1000 * this.frequency * Math.PI * 2) * this.amplitude;
  }
  recover() { this.projectile = null; this.elapsed = 0; }
}

module.exports = { TargetDirectionWaveMovement };
