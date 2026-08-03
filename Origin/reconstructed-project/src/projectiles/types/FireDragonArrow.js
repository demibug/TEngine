'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / FireDragonArrow
 * 原始注册：bundle.strings-decoded.js:35358
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：链式龙弹：头段驱动，距离>节间距阈值自生下一段（tS=body/tail），currentLength>=xD停止；唯一硬编码 tS 的弹种
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 单帧图，链式分段动态生长（头dragonPartHead/身dragonPartBody）
 */
class FireDragonArrow extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || FireDragonArrow.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

FireDragonArrow.projectileTypeKey = 'FireDragonArrow';
FireDragonArrow.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/dragonPartHead.png' });
module.exports = { FireDragonArrow };
