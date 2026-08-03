'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / LightningArrow
 * 原始注册：bundle.strings-decoded.js:36782
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：闪电链箭：Cw=命中后50%概率向随机敌方跳一条 LightningChain子弹（config.gS=nN.create, b.Cx=目标）；Wk=2
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 单帧图 + arrowTrail拖尾
 */
class LightningArrow extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || LightningArrow.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

LightningArrow.projectileTypeKey = 'LightningArrow';
LightningArrow.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/lightningArrow.png' });
module.exports = { LightningArrow };
