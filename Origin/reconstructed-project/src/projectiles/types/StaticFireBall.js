'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / StaticFireBall
 * 原始注册：bundle.strings-decoded.js:34071
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：静态地面火球：延迟激活(delay)、超 duration 缩放0移除；Cw=hit+ig(落地面火带时长灼烧)
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 4帧循环 fireGround_01→02→03→04（Kf imgLoop，随机起始帧）
 */
class StaticFireBall extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || StaticFireBall.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

StaticFireBall.projectileTypeKey = 'StaticFireBall';
StaticFireBall.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/effect/fireGround_01.png' });
module.exports = { StaticFireBall };
