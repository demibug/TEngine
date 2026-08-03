'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / FireArrow（火焰箭）
 * 原始注册：bundle.strings-decoded.js:34522
 * 重建状态：PARTIAL（帧序列契约登记，灼烧 impact 由发射器注入）
 *
 * 取证：火焰箭命中施加灼烧（BURN_STATIC），经 impact.burn 由发射器配置注入，
 * 弹种 applyHit 转发至 applyImpactEffects 结算 burn 分支。
 * 帧动画契约登记：单帧 fireArrowEff_01.jpg（DEFERRED: 待确认火焰拖尾帧序列）
 */
class FireArrow extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || FireArrow.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy); // 含 impact.burn 灼烧结算
    return result;
  }
}

FireArrow.projectileTypeKey = 'FireArrow';
FireArrow.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/fireArrowEff_01.jpg' });
module.exports = { FireArrow };
