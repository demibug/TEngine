'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / KnifeBullet
 * 原始注册：bundle.strings-decoded.js:26968
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：Cw 按 Ex 二态：Ex真→骑兵横扫 Tg（群体/带角度），Ex假→刀击 Dg（+血溅）。Ex 由 onReset.mx.Ex 配置驱动
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 无弹体图（继承 r6 透明命中区）；命中特效 knife0.png + blood0-2 循环
 */
class KnifeBullet extends ProjectileBase {
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

KnifeBullet.projectileTypeKey = 'KnifeBullet';
KnifeBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: null }); // 无弹体图（透明命中区/复用宿主）
module.exports = { KnifeBullet };
