'use strict';

/**
 * 重建模块：特殊投射物移动策略 / TargetEnemyMovement
 * 原始范围：bundle.strings-decoded.js:41290
 * 重建状态：PARTIAL（运动学行为基于名称语义实现，DEFERRED 至 bundle 精读确认参数）
 *
 * 直线追踪敌人当前位置（bundle:41290，无贝塞尔）。经 ProjectileBase.resetData({movement, movementConfig}) 注入配置，
 * 不读未定义的 p.config。
 */
class TargetEnemyMovement {
  constructor() { this.projectile = null; this.enemyManager = null; this.targetId = -1; this.speed = 200; this.elapsed = 0; }
  attach(projectile) {
    this.projectile = projectile;
    const cfg = projectile.movementConfig || {};
    this.enemyManager = cfg.enemyManager || projectile.enemyManager || null;
    this.targetId = cfg.targetId != null ? cfg.targetId : -1;
    this.speed = cfg.speed != null ? cfg.speed : 200;
    this.elapsed = 0;
    return this;
  }
  onFire() {}
  update(deltaMs, speed = 1) {
    if (!this.projectile || !this.enemyManager || this.targetId < 0) return;
    const enemy = typeof this.enemyManager.getEnemy === 'function' ? this.enemyManager.getEnemy(this.targetId)
      : (this.enemyManager.enemies && this.enemyManager.enemies.get ? this.enemyManager.enemies.get(this.targetId) : null);
    if (!enemy) return;
    const tx = enemy.x != null ? enemy.x : (enemy.visual ? enemy.visual.x : 0);
    const ty = enemy.y != null ? enemy.y : (enemy.visual ? enemy.visual.y : 0);
    const rn = this.projectile.renderNode;
    const dx = tx - rn.x, dy = ty - rn.y;
    const dist = Math.hypot(dx, dy);
    if (dist < 1) return;
    const step = this.speed * speed * (deltaMs / 1000);
    rn.x += (dx / dist) * step;
    rn.y += (dy / dist) * step;
    this.elapsed += deltaMs;
  }
  recover() { this.projectile = null; this.enemyManager = null; this.targetId = -1; this.elapsed = 0; }
}

module.exports = { TargetEnemyMovement };
