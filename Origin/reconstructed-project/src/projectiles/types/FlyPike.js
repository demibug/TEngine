'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / FlyPike
 * 原始注册：bundle.strings-decoded.js:36966
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：飞枪(龙胆亮银枪)：背光图+weapon_19枪体+oI光圈；onUpdate枪体朝运动方向旋转；Cw=hit+Nf(专属命中爆)
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 光圈3帧循环 quan01→02→03；命中特效4帧 longDanLiangYinQiangHitEff_0→1→2→3
 */
class FlyPike extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || FlyPike.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

FlyPike.projectileTypeKey = 'FlyPike';
FlyPike.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/effect/longDanLiangYinQiangBk.png' });
module.exports = { FlyPike };
