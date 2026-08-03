'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / EagleArrow（鹰箭）
 * 原始注册：bundle.strings-decoded.js:34843
 * 重建状态：PARTIAL（帧序列契约登记，专属追踪移动 DEFERRED）
 *
 * 取证：鹰箭为追踪型箭矢，命中经 applyImpactEffects 结算。
 * 帧动画契约登记：单帧 eagleArrow_01.png（DEFERRED: 待确认是否有命中爆帧）
 */
class EagleArrow extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || EagleArrow.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

EagleArrow.projectileTypeKey = 'EagleArrow';
EagleArrow.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/eagleArrow_01.png' });
module.exports = { EagleArrow };
