'use strict';

/**
 * 重建模块：特殊投射物移动策略 / FixedTargetMovement
 * 原始范围：bundle.strings-decoded.js:26857（TargetObjectInstantaneous 的简化变体）
 * 重建状态：PARTIAL（校正接入 resetData 真实生命周期）
 *
 * 校正记录：原占位骨架读 p.config（ProjectileBase 无此字段）导致脱节；
 * 本提案改为经 projectile.movementConfig 读取目标点，从 renderNode 读取起点。
 */
class FixedTargetMovement {
  constructor() {
    this.projectile = null;
    this.start = { x: 0, y: 0 };
    this.target = { x: 0, y: 0 };
    this.progress = 0;
  }

  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    const rn = projectile.renderNode;
    this.start = { x: rn.x, y: rn.y };
    this.target = cfg.target || { x: rn.x, y: rn.y };
    this.progress = 0;
    return this;
  }

  onFire() {}

  update(deltaMs, speed = 1) {
    if (!this.projectile) return;
    this.progress = Math.min(1, this.progress + (deltaMs / 1000) * speed);
    const t = this.progress;
    this.projectile.renderNode.x = this.start.x + (this.target.x - this.start.x) * t;
    this.projectile.renderNode.y = this.start.y + (this.target.y - this.start.y) * t;
  }

  recover() {
    this.projectile = null;
    this.progress = 0;
  }
}

module.exports = { FixedTargetMovement };
