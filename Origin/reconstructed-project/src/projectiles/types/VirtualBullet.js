'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / VirtualBullet
 * 原始注册：bundle.strings-decoded.js:35170
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：纯逻辑占位弹：所有生命周期方法空，行为由外部 tS/挂件驱动（jumpBack/jumpSlash 等虚弹）
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 无图（仅 anchor 0.5/0.5）
 */
class VirtualBullet extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    // 无弹体图：仅设几何（size/anchor 由 onReset 或子类覆写）
    if (this.imageNode) return;
    if (appearance.resourcePath) {
      const img = new this.laya.Image(appearance.resourcePath);
      this.renderNode.addChild(img);
      this.imageNode = img;
    }
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

VirtualBullet.projectileTypeKey = 'VirtualBullet';
VirtualBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: null }); // 无弹体图（透明命中区/复用宿主）
module.exports = { VirtualBullet };
