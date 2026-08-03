'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / StarBullet
 * 原始注册：bundle.strings-decoded.js:35275
 * 重建状态：PARTIAL（帧序列契约登记 + 专属逻辑钩子，渲染 DEFERRED 至 P2）
 *
 * 取证专属逻辑：流星弹：arrowTrail拖尾 + vn移动组件跟踪速度向量；Cw=hit+Xg(星爆全局坐标)；wS=旋转+呼吸缩放
 *
 * 帧动画契约登记（纯逻辑层登记，表现层渲染为 P2 非目标）：
 * - 弹体单帧；命中特效4帧 boomStar0→1→2→3
 */
class StarBullet extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || StarBullet.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

StarBullet.projectileTypeKey = 'StarBullet';
StarBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/props/star.png' });
module.exports = { StarBullet };
