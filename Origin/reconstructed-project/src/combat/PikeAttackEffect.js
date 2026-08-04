'use strict';

const { MeleeAttackEffect } = require('./MeleeAttackEffect');

// 来源: bundle.strings-decoded.js:24705-24749 / 命中段 24733-24741。
// 旋转 90ms、突刺 270ms 后由攻击效果开始结算，回收前再保留 120ms。
// PIKE_HIT_DELAY_MS / PIKE_EFFECT_DURATION_MS 为原始 Tween 链第三段 onStart 等价常量（bundle:24733-24741）：
//   段1 旋转 l/j_ → 段2 突刺 b[96]/j_（段末显隐 Qx 枪尖）→ 段3 回收 b[111]/j_（段首 onStart 调 U.LS() 命中）→ 段4 归位。
// 正式 Spine/Tween 动画事件接入后，此常量作「校准基线」与「无 Spine 回退」：
//   无 animationEventTimingProvider 注入时 hitAtMs = PIKE_HIT_DELAY_MS / playbackRate（与 v0.8.1 现状一致）；
//   正式动画事件到达时经 calibrateHitTiming 校准 hitAtMs 为事件时机，本常量为校准前的回退基线。
const PIKE_ATTACK_ROTATE_MS = 90;
const PIKE_ATTACK_THRUST_MS = 270;
const PIKE_HIT_DELAY_MS = PIKE_ATTACK_ROTATE_MS + PIKE_ATTACK_THRUST_MS;
const PIKE_EFFECT_DURATION_MS = PIKE_HIT_DELAY_MS + 120;

class PikeAttackEffect extends MeleeAttackEffect {
  constructor() { super('pike'); }

  reset() {
    // 枪兵专属字段重置：动画事件校准 provider、枪尖 Qx 表现 port 与视觉句柄。
    // 规则层命中字段（hitAtMs/elapsed/hitTriggered 等）由 super.reset() 处理。
    this.animationEventTimingProvider = null;
    this.pikeTipPresentation = null;
    this.pikeTipVisual = null;
    return super.reset();
  }

  launch({
    owner,
    target = null,
    enemyManager,
    damage = 0,
    radius = 48,
    playbackRate = 1,
    durationMs = null,
    // 可选：动画事件时机校准 provider（DEFERRED，正式 Spine/Tween 第三段 onStart 接入后由动画回调调 calibrateHitTiming）。
    // 无 provider 时命中回退 PIKE_HIT_DELAY_MS / playbackRate 常量基线（与 v0.8.1 现状一致）。
    animationEventTimingProvider = null,
    // 可选：枪尖 Qx 表现 port（DEFERRED 桩 no-op，实体 VFX 归 P2）。无 port 时跳过 Qx 视觉调度，不影响规则层命中。
    pikeTipPresentation = null,
  } = {}) {
    this.target = target;
    this.animationEventTimingProvider = animationEventTimingProvider || null;
    this.pikeTipPresentation = pikeTipPresentation || null;
    this.pikeTipVisual = null;
    const rate = Math.max(0.01, Number(playbackRate) || 1);
    // 命中时机基线：PIKE_HIT_DELAY_MS(360) / playbackRate（原始 Tween 链第三段 onStart 等价常量）。
    // 无论是否注入 provider，均以此常量基线起步——保证规则层始终有管理器驱动的非动画回退（满足 HANDOFF 行 440 约束）。
    // 正式动画事件到达时经 calibrateHitTiming 校准 hitAtMs 为事件时机，校准前此值为回退基线。
    const hitAtMs = PIKE_HIT_DELAY_MS / rate;
    const duration = durationMs == null
      ? PIKE_EFFECT_DURATION_MS / rate
      : Math.max(0, Number(durationMs) || 0);
    const result = super.launch({ owner, enemyManager, damage, radius, durationMs: duration, hitAtMs });

    // 4.4 枪尖 Qx 表现 port 调度（DEFERRED 桩 no-op，实体 VFX 归 P2）。
    // 对齐 bundle:24585/24736：突刺段（段2）创建 Qx 枪尖 pikeEff1.png 并 Tween.to("y",...) 位移。
    // port 为 DEFERRED 桩时 createPikeTipVisual 返回 null、animatePikeTipThrust no-op，不影响规则层命中结算。
    // 命中结算由 update()→hit() 驱动，不依赖 Qx 视觉对象。
    const presentation = this.pikeTipPresentation;
    if (presentation && typeof presentation.createPikeTipVisual === 'function' && this.owner) {
      const visual = presentation.createPikeTipVisual(this.owner);
      this.pikeTipVisual = visual; // 可能为 null（DEFERRED 桩），hidePikeTipVisual 容忍 null。
      if (visual && typeof presentation.animatePikeTipThrust === 'function') {
        // 突刺段时长 = PIKE_ATTACK_THRUST_MS(270) / playbackRate（bundle:24733-24741 段2 时长等价）。
        presentation.animatePikeTipThrust(visual, PIKE_ATTACK_THRUST_MS / rate);
      }
    }
    return result;
  }

  /**
   * 动画事件校准命中时机（覆盖基类 no-op 钩子，实现 2.3）。
   * 正式 Spine/Tween 接入后，动画第三段 onStart 到达时由动画回调（经 animationEventTimingProvider 路由）调用本方法，
   * 将 hitAtMs 校准为「当前 elapsed」（即动画事件到达时机），使下次 update() 满足 elapsed >= hitAtMs 触发 hit()。
   * 命中结算 MUST 仍走 update()→hit() 规则路径——本方法只重置 hitAtMs/elapsed 关系，不直接调 hit()（不倒退为动画回调直接结算）。
   * @param {number} _animationEventMs 动画事件到达时机（ms，相对效果启动）；当前校准目标为当前 elapsed（设计决策 1），
   *   参数保留供 P2 Spine 接入时按需记录事件时间戳，不影响校准语义。
   * @returns {boolean} true=已校准 hitAtMs（尚未命中且效果激活）；false=未校准（已命中/未激活，保持现状）。
   */
  calibrateHitTiming(_animationEventMs) {
    if (!this.active || this.hitTriggered) return false; // 已命中或未激活时不校准，避免重复结算。
    // 校准 hitAtMs 为当前 elapsed：下次 update() 即满足 elapsed >= hitAtMs 触发 hit()（规则路径）。
    this.hitAtMs = this.elapsed;
    return true;
  }

  cleanup(reason) {
    // 4.4 回收段枪尖 Qx 表现 port 调度（DEFERRED 桩 no-op）。
    // 对齐 bundle:24740：段3末 this.Qx.visible=false（枪尖特效回收段隐藏）。
    // 命中结算已由规则层 hit() 完成，不依赖本隐藏动作；port 为 DEFERRED 桩时 no-op 不影响规则层。
    const presentation = this.pikeTipPresentation;
    if (presentation && typeof presentation.hidePikeTipVisual === 'function' && this.pikeTipVisual) {
      presentation.hidePikeTipVisual(this.pikeTipVisual);
    }
    this.pikeTipVisual = null;
    this.pikeTipPresentation = null;
    this.animationEventTimingProvider = null;
    this.target = null;
    return super.cleanup(reason);
  }
}

module.exports = {
  PikeAttackEffect,
  PIKE_ATTACK_ROTATE_MS,
  PIKE_ATTACK_THRUST_MS,
  PIKE_HIT_DELAY_MS,
  PIKE_EFFECT_DURATION_MS,
};
