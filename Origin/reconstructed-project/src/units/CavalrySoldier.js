'use strict';

const { SoldierBase } = require('./SoldierBase');
const { CavalrySweepEffect } = require('../combat/CavalrySweepEffect');

/**
 * ROUND-07B-CAVALRY
 * 来源: bundle.strings-decoded.js:24750-24831
 * 原始符号: e[3] / 匿名类 extends td
 * CONFIRMED: cavalry表现键、attack->Nx、双攻击对象、cavalry_attack音效。
 */
class CavalrySoldier extends SoldierBase {
  constructor() {
    super();
    this.animationKey = 'cavalry';
    this.pendingSweeps = [];
  }

  configure(options = {}) {
    super.configure(options);
    if (!options.attackEffectManager || typeof options.attackEffectManager.add !== 'function') {
      throw new TypeError('CavalrySoldier requires attackEffectManager');
    }
    this.attackEffectManager = options.attackEffectManager;
    this.pendingSweeps = [];
    return this;
  }

  initialize(unitText, side) {
    this.animationKey = 'cavalry';
    this.pendingSweeps = [];
    return super.initialize(unitText, side);
  }

  attack() {
    const targets=this.enemyManager.queryTargets(this.displayObject.x,this.displayObject.y,this.attackRange,this.side)||[];
    if(!targets.length)return null;
    const damage=this.getAttackDamage?this.getAttackDamage():this.baseAttackPower;
    const effects=[
      this.attackEffectManager.create(CavalrySweepEffect).launch({owner:this,enemyManager:this.enemyManager,damage,multiplier:0.5,radius:this.attackRange}),
      this.attackEffectManager.create(CavalrySweepEffect).launch({owner:this,enemyManager:this.enemyManager,damage,multiplier:1,radius:this.attackRange,delayMs:80})
    ];
    this.pendingSweeps=effects;
    for (const effect of effects) this.attackEffectManager.add(effect);
    return {effects};
  }

  _createSweep(target, length) {
    const effect = new CavalrySweepEffect();
    return effect.launch({ owner: this, target, length });
  }

  gameOver() {
    if (this.attackEffectManager && typeof this.attackEffectManager.cancelOwner === 'function') this.attackEffectManager.cancelOwner(this);
    for (const effect of this.pendingSweeps) {
      if (effect && typeof effect.cleanup === 'function') effect.cleanup();
    }
    this.pendingSweeps.length = 0;
    return super.gameOver();
  }
}

module.exports = { CavalrySoldier };
