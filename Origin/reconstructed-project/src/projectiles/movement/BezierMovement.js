'use strict';

/**
 * 重建模块：特殊投射物移动策略 / BezierMovement
 * 原始范围：bundle.strings-decoded.js:40071（TargetPositionBezierMovement 的简化变体）
 * 重建状态：PARTIAL（校正接入 resetData 真实生命周期）
 *
 * 校正记录：原占位骨架读 p.config（ProjectileBase 无此字段）导致脱节；
 * 本提案改为经 projectile.movementConfig 读取贝塞尔控制点，接入 resetData({movement}) 生命周期。
 */
class BezierMovement {
  constructor() {
    this.projectile = null;
    this.p0 = { x: 0, y: 0 };
    this.p1 = { x: 0, y: 0 };
    this.p2 = { x: 0, y: 0 };
    this.progress = 0;
  }

  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    const rn = projectile.renderNode;
    this.p0 = cfg.p0 || { x: rn.x, y: rn.y };
    this.p1 = cfg.p1 || { x: rn.x, y: rn.y };
    this.p2 = cfg.p2 || { x: rn.x, y: rn.y };
    this.progress = 0;
    return this;
  }

  onFire() {}

  update(deltaMs, speed = 1) {
    if (!this.projectile) return;
    this.progress = Math.min(1, this.progress + (deltaMs / 1000) * speed);
    const t = this.progress;
    const u = 1 - t;
    this.projectile.renderNode.x = u * u * this.p0.x + 2 * u * t * this.p1.x + t * t * this.p2.x;
    this.projectile.renderNode.y = u * u * this.p0.y + 2 * u * t * this.p1.y + t * t * this.p2.y;
  }

  recover() {
    this.projectile = null;
    this.progress = 0;
  }
}

module.exports = { BezierMovement };
