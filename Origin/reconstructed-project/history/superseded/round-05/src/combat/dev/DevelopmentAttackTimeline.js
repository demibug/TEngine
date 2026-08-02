'use strict';

/**
 * DEVELOPMENT_ONLY：当前选择的刀兵在原代码中于攻击对象 LS() 启动时立即结算，
 * 因此本适配器只记录“无额外动画延迟”，不注入伪延迟。
 */
class DevelopmentAttackTimeline {
  constructor() { this.calls = []; }
  runImmediate(callback) { this.calls.push(['immediate']); return callback(); }
  runUnknownEvent(name) { throw new Error(`DevelopmentAttackTimeline: unknown animation event ${name}`); }
}
module.exports = { DevelopmentAttackTimeline };
