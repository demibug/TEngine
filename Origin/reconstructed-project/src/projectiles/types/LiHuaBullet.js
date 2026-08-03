'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / LiHuaBullet
 * 原始注册：bundle.strings-decoded.js:36644
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：旋转梨花弹：onUpdate每帧 rotation+=dt；Cw=hit+zf(梨花爆裂 lihuahit序列)
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 弹体单帧+自旋；命中特效3帧 lihuahit0→1→2
 */
class LiHuaBullet extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || LiHuaBullet.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

LiHuaBullet.projectileTypeKey = 'LiHuaBullet';
LiHuaBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/lihua.png' });
module.exports = { LiHuaBullet };
