'use strict';

const { AttackResolver } = require('./AttackResolver');

/** 可复用的延迟近战/范围命中效果。 */
class MeleeAttackEffect {
  constructor(type = 'melee', resolver = new AttackResolver()) {
    this.type = type;
    this.resolver = resolver;
    this.reset();
  }

  reset() {
    this.owner = null;
    this.enemyManager = null;
    this.damage = 0;
    this.multiplier = 1;
    this.radius = 0;
    this.hitSet = new Set();
    this.active = false;
    this.elapsed = 0;
    this.durationMs = 0;
    this.hitAtMs = 0;
    this.hitTriggered = false;
    return this;
  }

  launch({ owner, enemyManager, damage = 0, multiplier = 1, radius = 48, durationMs = 180, hitAtMs = durationMs * 0.25 } = {}) {
    this.owner = owner;
    this.enemyManager = enemyManager;
    this.damage = damage;
    this.multiplier = multiplier;
    this.radius = radius;
    this.durationMs = Math.max(0, Number(durationMs) || 0);
    this.hitAtMs = Math.max(0, Number(hitAtMs) || 0);
    this.elapsed = 0;
    this.hitTriggered = false;
    this.active = true;
    return this;
  }

  /**
   * 动画事件校准钩子（可选，默认 no-op）。
   * 用途：正式 Spine/Tween 接入后，动画命中事件（如枪兵 Tween 链第三段 onStart）到达时调用，
   * 允许子类重置 hitAtMs/elapsed 关系以校准剩余命中时机——固定常量在 playbackRate 变速时与真实动画段时长偏移，
   * 校准钩子让正式动画事件修正偏移。命中结算 MUST 仍由 update()→hit() 规则路径触发，钩子不直接调 hit()。
   * 基类默认 no-op：不改变现有常量基线行为（hitAtMs/elapsed 不变），仅提供钩子供子类覆盖（如 PikeAttackEffect）。
   * @param {number} _animationEventMs 动画事件到达时机（ms，相对效果启动）；基类忽略此参数（no-op）。
   * @returns {boolean} 是否已校准（基类恒返回 false，表示未校准，保持常量基线）。
   */
  calibrateHitTiming(_animationEventMs) {
    // 基类默认 no-op：不重置 hitAtMs/elapsed，保持 launch() 设定的常量基线行为。
    return false;
  }

  update(deltaMs) {
    if (!this.active) return false;
    this.elapsed += Math.max(0, Number(deltaMs) || 0);
    if (!this.hitTriggered && this.elapsed >= this.hitAtMs) {
      this.hitTriggered = true;
      this.hit();
    }
    if (this.elapsed >= this.durationMs) this.cleanup('duration-complete');
    return this.active;
  }

  hit() {
    if (!this.active || !this.owner || !this.enemyManager) return;
    const node = this.owner.displayObject || this.owner.combatPosition || { x: 0, y: 0 };
    const targets = this.resolver.queryEnemyObjects({
      enemyManager: this.enemyManager,
      center: { x: Number(node.x) || 0, y: Number(node.y) || 0 },
      range: this.radius,
      side: this.owner.side,
    });
    for (const target of targets) {
      if (this.hitSet.has(target.id)) continue;
      this.hitSet.add(target.id);
      this.resolver.hit(target, this.damage * this.multiplier, this.owner);
    }
  }

  cleanup() {
    this.active = false;
    this.owner = null;
    this.enemyManager = null;
    this.hitSet.clear();
  }
}

module.exports = { MeleeAttackEffect };
