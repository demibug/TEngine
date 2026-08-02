'use strict';

const { ProjectileBase } = require('./ProjectileBase');

/**
 * 重建模块：BOW-PROJECTILE-COMBAT-01 / SimpleDynamicArrow
 * 原始范围：bundle.strings-decoded.js:26698-26845
 * 原始符号：rd
 * 原始注册：bundle.strings-decoded.js:27578
 * 重建状态：COMPLETE
 */
class SimpleDynamicArrow extends ProjectileBase {
  constructor(appearanceLabel = '') {
    super(appearanceLabel);
    this.alternateHitEffect = false;
    this.imageNode = null;
  }

  initializeVisual() {
    this.renderNode.pos(0, 0);
    this.renderNode.size(22, 72);
    this.renderNode.anchorX = 0.5;
    this.renderNode.anchorY = 0.9;
  }

  initializeAppearance(appearance) {
    if (this.imageNode) return;
    const image = new this.laya.Image(appearance.resourcePath);
    image.size(22, 72);
    this.renderNode.addChild(image);
    if (appearance.size) {
      this.renderNode.size(appearance.size.x, appearance.size.y);
      image.size(appearance.size.x, appearance.size.y);
    }
    if (appearance.scale) this.renderNode.scale(appearance.scale.x, appearance.scale.y);
    if (appearance.anchor) {
      this.renderNode.anchorX = appearance.anchor.x;
      this.renderNode.anchorY = appearance.anchor.y;
    }
    this.imageNode = image;
  }

  onReset(config) {
    this.alternateHitEffect = Boolean(config.appearance && config.appearance.alternateHitEffect);
  }

  applyHit(enemy) {
    const result = enemy.hit(this.damage, this.attacker);
    const centerX = enemy.visual.width / 2;
    const centerY = enemy.visual.height / 2;
    this.effects.showSimpleArrowHit({
      arrow: this,
      enemy,
      alternate: this.alternateHitEffect,
      centerX,
      centerY,
      damage: this.damage,
      applied: result,
    });
    return result;
  }

  onRecover() {
    this.alternateHitEffect = false;
    this.renderNode.pos(0, 0);
    this.renderNode.size(22, 72);
    this.renderNode.anchorX = 0.5;
    this.renderNode.anchorY = 0.9;
    this.renderNode.scale(1, 1);
    this.renderNode.rotation = 0;
    this.renderNode.alpha = 1;
    this.renderNode.visible = true;
  }
}

SimpleDynamicArrow.projectileTypeKey = 'SimpleDynamicArrow';
SimpleDynamicArrow.DEFAULT_APPEARANCE = Object.freeze({
  label: '弓箭小兵箭矢',
  resourcePath: 'resources/img/weapon/arrow_0.png',
});
SimpleDynamicArrow.DEFAULT_SIZE = Object.freeze({ width: 22, height: 72 });

module.exports = { SimpleDynamicArrow };
