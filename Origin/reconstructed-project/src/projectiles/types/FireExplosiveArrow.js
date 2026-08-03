'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / FireExplosiveArrow
 * 原始注册：bundle.strings-decoded.js:35484
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：范围爆炸箭：圆形碰撞形状 rk(radius=GD)+灼烧挂件 tx(6号,b[123]时长)；Cw=范围hit
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 单帧图
 */
class FireExplosiveArrow extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || FireExplosiveArrow.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

FireExplosiveArrow.projectileTypeKey = 'FireExplosiveArrow';
FireExplosiveArrow.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/arrow_9.png' });
module.exports = { FireExplosiveArrow };
