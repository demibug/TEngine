'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / PikeBullet
 * 原始注册：bundle.strings-decoded.js:26997
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：Cw 按 Ax 二态：Ax真→骑兵横扫 Tg，Ax假→枪刺 Cg。Ax 由 onReset.mx.Ax 配置驱动（刺/扫双态）
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 无弹体图（继承 r6 透明命中区）；命中特效 pike0.png
 */
class PikeBullet extends ProjectileBase {
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

PikeBullet.projectileTypeKey = 'PikeBullet';
PikeBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: null }); // 无弹体图（透明命中区/复用宿主）
module.exports = { PikeBullet };
