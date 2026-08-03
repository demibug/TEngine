'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / AttachCustomShapeBullet
 * 原始注册：bundle.strings-decoded.js:36356
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：附着宿主弹：resetData挂到宿主父节点，宿主移除时同步移除；onReset复刻宿主width/height/anchor/pos；Cw=hit+Cg(枪刺)
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 无自身图（复用宿主几何）
 */
class AttachCustomShapeBullet extends ProjectileBase {
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

AttachCustomShapeBullet.projectileTypeKey = 'AttachCustomShapeBullet';
AttachCustomShapeBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: null }); // 无弹体图（透明命中区/复用宿主）
module.exports = { AttachCustomShapeBullet };
