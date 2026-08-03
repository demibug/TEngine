'use strict';
const { ProjectileBase } = require('../ProjectileBase');

/**
 * 重建模块：特殊投射物 / PikeSnakeBullet（灵蛇弹，丈八蛇矛）
 * 原始注册：bundle.strings-decoded.js:36779
 * 重建状态：PARTIAL（帧序列契约登记，灵蛇拦路移动 DEFERRED）
 *
 * 取证（analysis/mappings/ROUND-07D-symbol-map.json）：灵蛇弹 movement/hit 均为
 * source-specific/unknown，专属移动策略待 bundle 精读。纯逻辑层登记帧序列契约，
 * 命中经 applyImpactEffects 结算。
 * 帧动画契约登记：单帧 lingShe_1.png（DEFERRED: 待确认灵蛇游动帧序列）
 */
class PikeSnakeBullet extends ProjectileBase {
  initializeAppearance(appearance = {}) {
    if (this.imageNode) return;
    const img = new this.laya.Image(appearance.resourcePath || PikeSnakeBullet.DEFAULT_APPEARANCE.resourcePath);
    this.renderNode.addChild(img);
    this.imageNode = img;
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    this.applyImpactEffects(enemy);
    return result;
  }
}

PikeSnakeBullet.projectileTypeKey = 'PikeSnakeBullet';
PikeSnakeBullet.DEFAULT_APPEARANCE = Object.freeze({ resourcePath: 'resources/img/weapon/bullet/lingShe_1.png' });
module.exports = { PikeSnakeBullet };
