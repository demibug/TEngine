'use strict';

/**
 * 重建模块：特殊投射物移动策略 / LineMovement
 * 原始范围：bundle.strings-decoded.js:36359（TargetDirectionLineMovement 的简化变体）
 * 重建状态：PARTIAL（校正接入 resetData 真实生命周期）
 *
 * 校正记录：原占位骨架读 p.config（ProjectileBase 无此字段）导致脱节；
 * 本提案改为经 projectile.movementConfig 读取方向向量 dx/dy，接入 resetData({movement}) 生命周期。
 */
class LineMovement {
  constructor() {
    this.projectile = null;
    this.dx = 1;
    this.dy = 0;
    this.progress = 0;
  }

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
    const t = (deltaMs / 1000) * speed;
    this.projectile.renderNode.x += this.dx * t;
    this.projectile.renderNode.y += this.dy * t;
    this.progress += t;
  }

  recover() {
    this.projectile = null;
    this.progress = 0;
  }
}

module.exports = { LineMovement };
