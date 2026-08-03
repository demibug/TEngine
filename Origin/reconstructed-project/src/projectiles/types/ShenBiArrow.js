'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / ShenBiArrow
 * 原始注册：bundle.strings-decoded.js:36365（错误串 bundle:626 确认原始名 ShenBiArrow）
 * 重建状态：PARTIAL_SKELETON（校正自误标 ShenBiPunch）
 *
 * 校正记录：原重建误标为 ShenBiPunch（projectileTypeKey/资源 shenBiPunch.png），
 * bundle 原始注册名为 ShenBiArrow，资源为 shenBiArrow.png。本提案校正对齐 bundle。
 */
class ShenBiArrow extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || ShenBiArrow.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

ShenBiArrow.projectileTypeKey = 'ShenBiArrow';
ShenBiArrow.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/shenBiArrow.png' });
module.exports = { ShenBiArrow };
