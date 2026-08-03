'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / GroundSpikeBullet
 * 原始注册：bundle.strings-decoded.js:35481
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：地刺群+控制：Cw=5根随机位置缩放1+地面裂纹lg+hit+applyBuff(id=8,STUN)+敌人抖动tween模拟挑飞
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 单帧图，5根地刺齐出（Uv数组，缩放/淡出tween）
 */
class GroundSpikeBullet extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || GroundSpikeBullet.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

GroundSpikeBullet.projectileTypeKey = 'GroundSpikeBullet';
GroundSpikeBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/weapon_11.png' });
module.exports = { GroundSpikeBullet };
