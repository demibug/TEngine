'use strict';

const { SoldierBase } = require('./SoldierBase');

/**
 * ROUND-07A-SPEAR
 * 原始范围：bundle.strings-decoded.js:24556-24749
 * 原始符号：匿名类 e[2] / 表现键 pike
 *
 * CONFIRMED:
 * - 继承 td(SoldierBase)
 * - 兵种索引 2
 * - 表现资源 pike / pikeEff1
 * - 攻击包含枪身旋转、攻击动画、枪尖表现和攻击对象创建
 *
 * 当前保留逻辑边界：
 * - 命中/贯穿对象需要完整 sv/u6/vA 闭包依赖，未在本轮强行伪造。
 */
class SpearSoldier extends SoldierBase {
  constructor() {
    super();
    this.animationKey = 'pike';
    this.pikeRotation = 0;
    this.pendingAttack = null;
  }

  configure(options = {}) {
    super.configure(options);
    if (!options.attackEffectManager || typeof options.attackEffectManager.add !== 'function') {
      throw new TypeError('SpearSoldier requires attackEffectManager');
    }
    this.attackEffectManager = options.attackEffectManager;
    return this;
  }

  initialize(unitText, side) {
    this.animationKey = 'pike';
    this.pikeRotation = 0;
    this.pendingAttack = null;
    return super.initialize(unitText, side);
  }

  attack() {
    const targets=this.enemyManager.queryTargets(this.displayObject.x,this.displayObject.y,this.attackRange,this.side)||[];
    const target=targets[0];
    if(!target)return null;
    const {PikeAttackEffect}=require('../combat/PikeAttackEffect');
    const effect=this.attackEffectManager.create(PikeAttackEffect).launch({owner:this,target,enemyManager:this.enemyManager,damage:this.getAttackDamage?this.getAttackDamage():this.baseAttackPower});
    this.pendingAttack=effect;
    this.attackEffectManager.add(effect);
    return effect;
  }

  gameOver() {
    if (this.attackEffectManager && typeof this.attackEffectManager.cancelOwner === 'function') this.attackEffectManager.cancelOwner(this);
    this.pendingAttack = null;
    this.pikeRotation = 0;
    return super.gameOver();
  }
}

module.exports = { SpearSoldier };
