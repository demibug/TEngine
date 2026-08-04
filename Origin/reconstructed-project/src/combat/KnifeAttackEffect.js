'use strict';

/**
 * 由 AttackEffectManager 驱动的刀兵延迟命中效果。
 *
 * 刀兵时序为原始 Laya timer 方案（hu[176]→500，bundle:18885 同模式 timer），
 * 非管理器驱动：usesTimer 路径下命中由 Laya.timer.once 精确触发，
 * 管理器 update() 对该效果只做存活跟踪（return active），不推进 elapsed，
 * 以避免固定步进漂移。无 Laya 运行时（timeline.laya.timer.once 缺失）时
 * usesTimer=false，回退到管理器推进路径（update() 累加 elapsed>=delayMs 触发 resolve），
 * 保证无 Laya 环境刀兵命中仍可用。
 */
class KnifeAttackEffect {
  constructor() { this.reset(); }

  reset() {
    this.type = 'knifeSoliderAttack';
    this.owner = null;
    this.timeline = null;
    this.target = null;
    this.damage = 0;
    this.record = null;
    this.elapsed = 0;
    this.delayMs = 0;
    this.active = false;
    this.usesTimer = false;
    return this;
  }

  /**
   * 启动刀兵延迟命中效果。
   *
   * usesTimer 路径（原始 Laya timer 方案，hu[176]→500，bundle:18885）：
   * 检测到 timeline.laya.timer.once 存在即置 usesTimer=true，由 Laya.timer.once(delayMs, resolve)
   * 精确触发命中——命中时机由 Laya timer 一次性回调精确决定，不依赖管理器固定步进，
   * 避免固定步进漂移。此为正式方案，与原始 bundle 一致。
   */
  launch({ owner, timeline, target, damage, record, delayMs } = {}) {
    this.owner = owner;
    this.timeline = timeline;
    this.target = target;
    this.damage = damage;
    this.record = record;
    this.delayMs = Math.max(0, Number(delayMs) || 0);
    this.elapsed = 0;
    this.active = true;
    if (timeline && timeline.laya && timeline.laya.timer && typeof timeline.laya.timer.once === 'function') {
      this.usesTimer = true;
      timeline.laya.timer.once(this.delayMs, this, () => {
        if (!this.active) return;
        this.timeline.resolve(this);
        this.active = false;
      });
    }
    return this;
  }

  /**
   * 更新效果存活状态。
   *
   * usesTimer 分支（Scenario: 刀兵 usesTimer 路径由 Laya.timer 精确触发）：
   * 仅 return active 做存活跟踪，不累加 elapsed、不推进计时——命中由
   * launch() 注册的 Laya.timer.once 精确触发，避免管理器固定步进漂移。
   *
   * 回退分支（Scenario: 无 Laya 运行时回退管理器推进）：
   * usesTimer=false（timeline.laya.timer.once 缺失）时，由管理器推进
   * elapsed >= delayMs 后触发 timeline.resolve，保证无 Laya 运行时
   * 刀兵命中仍可用。
   */
  update(deltaMs) {
    if (!this.active) return false;
    if (this.usesTimer) return this.active;
    this.elapsed += Math.max(0, Number(deltaMs) || 0);
    if (this.elapsed >= this.delayMs) {
      this.timeline.resolve(this);
      this.active = false;
    }
    return this.active;
  }

  cleanup() {
    if (this.active && this.usesTimer && this.timeline && this.timeline.laya && this.timeline.laya.timer) {
      this.timeline.laya.timer.clearAll(this);
    }
    if (this.active && this.timeline) this.timeline.cancel(this);
    this.active = false;
    this.owner = null;
    this.timeline = null;
    this.target = null;
    this.record = null;
    this.usesTimer = false;
  }
}

module.exports = { KnifeAttackEffect };
