'use strict';

const { MeleeAttackEffect } = require('./MeleeAttackEffect');

const CAVALRY_SWEEP_DELAY_MS = 150; // bundle.strings-decoded.js:24825

class CavalrySweepEffect extends MeleeAttackEffect {
  constructor() { super('cavalrySweep'); }

  reset() {
    // 纯逻辑层生命周期标志：不持渲染对象，DEFERRED 桩 no-op 时为 null/false。
    this.sweepVisual = null;
    this.sweepVisualActive = false;
    this.presentation = null;
    return super.reset();
  }

  launch({
    owner,
    target = null,
    enemyManager,
    damage = 0,
    multiplier = 1,
    radius = 96,
    delayMs = CAVALRY_SWEEP_DELAY_MS,
    // 表现 port（可选）：经 createCavalrySweepVisual/removeCavalrySweepVisual 调度 sweep 视觉对象。
    // DEFERRED 桩 no-op（create 返回 null、remove 空体）时不影响规则层伤害结算。
    presentation = null,
  } = {}) {
    this.target = target;
    this.presentation = presentation || null;
    const delay = Math.max(0, Number(delayMs) || 0);
    const result = super.launch({
      owner,
      enemyManager,
      damage,
      multiplier,
      radius,
      hitAtMs: delay,
      durationMs: delay + 120,
    });
    // 经表现 port 调度 sweep 视觉对象创建（对齐 bundle:24818-24820 vA.gx(n) 创建两个 sweep 视觉对象）。
    // 纯逻辑层只持生命周期标志，伤害结算（hit()）不依赖视觉对象。
    this._createSweepVisual();
    return result;
  }

  cleanup(reason) {
    // 横扫完成经表现 port 调 removeCavalrySweepVisual（DEFERRED 桩 no-op 不操作渲染对象）。
    // 仅在视觉已创建且未移除时调度一次，保证重复 cleanup（update 完成与 manager 回收）幂等。
    this._removeSweepVisual();
    this.target = null;
    return super.cleanup(reason);
  }

  // 经表现 port 调度 sweep 视觉对象创建。无 port 注入时跳过（规则层行为不变）。
  _createSweepVisual() {
    if (!this.presentation || typeof this.presentation.createCavalrySweepVisual !== 'function') return;
    // config 携带 sweep 参数（半径/倍率/延迟）供 P2 渲染；当前 DEFERRED 桩 no-op 不读取。
    const config = {
      radius: this.radius,
      multiplier: this.multiplier,
      delayMs: this.hitAtMs,
    };
    this.sweepVisual = this.presentation.createCavalrySweepVisual(this.owner, config);
    this.sweepVisualActive = true;
  }

  // 经表现 port 调度 sweep 视觉对象移除。幂等：仅调度一次，重复 cleanup 不重复调用。
  _removeSweepVisual() {
    if (!this.sweepVisualActive) return;
    if (this.presentation && typeof this.presentation.removeCavalrySweepVisual === 'function') {
      this.presentation.removeCavalrySweepVisual(this.sweepVisual);
    }
    this.sweepVisual = null;
    this.sweepVisualActive = false;
  }
}

module.exports = { CavalrySweepEffect, CAVALRY_SWEEP_DELAY_MS };
