'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / DaoQiBullet
 * 原始注册：bundle.strings-decoded.js:35947
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：刀气弹：青蓝拖尾 daoqiTrail(#8ae0f1ff)；onReset按mx.width/height设可变尺寸；Cw=hit+Eg(daoqiHit序列)
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 弹体单帧；命中特效3帧 daoqiHitEff0→1→2
 */
class DaoQiBullet extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || DaoQiBullet.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

DaoQiBullet.projectileTypeKey = 'DaoQiBullet';
DaoQiBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/newDQ.png' });
module.exports = { DaoQiBullet };
