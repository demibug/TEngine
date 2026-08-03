'use strict';

/**
 * 重建模块：特殊投射物移动策略 / TargetPositionBezierMovement
 * 原始范围：bundle.strings-decoded.js:40071
 * 重建状态：PARTIAL（运动学行为基于名称语义实现，DEFERRED 至 bundle 精读确认参数）
 *
 * 贝塞尔曲线飞向目标位置（bundle:40071）。经 ProjectileBase.resetData({movement, movementConfig}) 注入配置，
 * 不读未定义的 p.config。
 */
class TargetPositionBezierMovement {
  constructor() { this.projectile = null; this.p0 = {x:0,y:0}; this.p1 = {x:0,y:0}; this.p2 = {x:0,y:0}; this.progress = 0; }
  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    const rn = projectile.renderNode;
    this.p0 = { x: rn.x, y: rn.y };
    this.p2 = cfg.target || { x: rn.x, y: rn.y };
    this.p1 = cfg.control || { x: (this.p0.x + this.p2.x) / 2, y: this.p0.y - 50 };
    this.progress = 0;
    return this;
  }
  onFire() {}
  update(deltaMs, speed = 1) {
    if (!this.projectile) return;
    this.progress = Math.min(1, this.progress + (deltaMs / 1000) * speed);
    const t = this.progress, u = 1 - t;
    this.projectile.renderNode.x = u*u*this.p0.x + 2*u*t*this.p1.x + t*t*this.p2.x;
    this.projectile.renderNode.y = u*u*this.p0.y + 2*u*t*this.p1.y + t*t*this.p2.y;
  }
  recover() { this.projectile = null; this.progress = 0; }
}

module.exports = { TargetPositionBezierMovement };
