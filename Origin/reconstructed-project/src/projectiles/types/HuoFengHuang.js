'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / HuoFengHuang（火凤凰）
 * 原始注册：bundle.strings-decoded.js:35167（bundle 注册名 HuoFengHuangArrow，源码用短名）
 * 重建状态：PARTIAL（帧序列契约登记，专属飞行/爆裂 DEFERRED）
 *
 * 取证：火凤凰为范围型火焰投射物，命中经 applyImpactEffects 结算。
 * 帧动画契约登记：单帧 huoFengHuang_01.png（DEFERRED: 待确认凤凰展翅/爆裂帧序列）
 */
class HuoFengHuang extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || HuoFengHuang.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

HuoFengHuang.projectileTypeKey = 'HuoFengHuang';
HuoFengHuang.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/huoFengHuang_01.png' });
module.exports = { HuoFengHuang };
