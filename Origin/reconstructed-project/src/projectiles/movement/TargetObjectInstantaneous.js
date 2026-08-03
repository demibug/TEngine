'use strict';

/**
 * 重建模块：特殊投射物移动策略 / TargetObjectInstantaneous
 * 原始范围：bundle.strings-decoded.js:26857
 * 重建状态：PARTIAL（运动学行为基于名称语义实现，DEFERRED 至 bundle 精读确认参数）
 *
 * 瞬时命中目标对象（bundle:26857，无飞行过程）。经 ProjectileBase.resetData({movement, movementConfig}) 注入配置，
 * 不读未定义的 p.config。
 */
class TargetObjectInstantaneous {
  constructor() { this.projectile = null; this.target = null; this.applied = false; }
  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    this.target = cfg.target || null;
    this.applied = false;
    return this;
  }
  onFire() {}
  update(deltaMs, speed = 1) {
    // 瞬时：update 首次调用即将投射物置于目标位置
    if (!this.projectile || this.applied || !this.target) return;
    const tx = typeof this.target.x === 'number' ? this.target.x : (this.target.visual ? this.target.visual.x : 0);
    const ty = typeof this.target.y === 'number' ? this.target.y : (this.target.visual ? this.target.visual.y : 0);
    this.projectile.renderNode.x = tx;
    this.projectile.renderNode.y = ty;
    this.applied = true;
  }
  recover() { this.projectile = null; this.target = null; this.applied = false; }
}

module.exports = { TargetObjectInstantaneous };
