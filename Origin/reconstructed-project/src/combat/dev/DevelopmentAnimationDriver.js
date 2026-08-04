'use strict';

/**
 * DEVELOPMENT_ONLY：无 Spine/无 Laya 动画运行时回退桩，推进非循环动画片段。
 *
 * 回退桩说明：正式环境的 `BowSoldier` 发射点由 `Laya.Event.STOPPED` 正式动画事件驱动
 * （`_onAttackAnimationStopped`→`launchArrow`）；本驱动器仅在无 Spine/无 Laya 动画运行时
 * 按时长模拟 STOPPED——`update()` 累加 `elapsedMs >= durationMs` 后调
 * `animation.event(this.stoppedEvent)`。正式 STOPPED 由动画运行时驱动，dev 桩为回退，
 * 两者经 `BowSoldier` 同一 `_onAttackAnimationStopped` 入口触发 `launchArrow`，规则层
 * （`launchArrow`→`ProjectileAttackEffect` 登记/更新/回收）只依赖 STOPPED 到达信号，
 * 不依赖动画帧本身（对齐 CODEX_HANDOFF 行 440「不让表现动画成为规则唯一触发来源」）。
 */
class DevelopmentAnimationDriver {
  constructor({ gameLoop, stoppedEvent = 'stopped', logger = console } = {}) {
    if (!gameLoop) throw new TypeError('DevelopmentAnimationDriver requires gameLoop');
    this.gameLoop = gameLoop;
    this.stoppedEvent = stoppedEvent;
    this.logger = logger;
    this.records = new Map();
    this.eventLog = [];
    this.initialized = false;
  }

  init() {
    if (this.initialized) return;
    this.gameLoop.register('developmentAnimationDriver', this, this.update);
    this.initialized = true;
  }

  playSegment(animation, {
    owner,
    name,
    startMs = 0,
    endMs,
    effectivePlaybackRate = 1,
  } = {}) {
    if (!this.initialized) throw new Error('DevelopmentAnimationDriver.init() must run before playSegment()');
    if (!animation || !owner) throw new TypeError('Development animation segment requires animation and owner');
    if (!Number.isFinite(endMs) || endMs < startMs) throw new Error(`Invalid development animation segment ${startMs}-${endMs}`);
    if (!(effectivePlaybackRate > 0)) throw new Error('Animation playback rate must be positive');
    const record = {
      animation,
      owner,
      ownerLifecycle: owner.lifecycleGeneration,
      name,
      startMs,
      endMs,
      durationMs: (endMs - startMs) / effectivePlaybackRate,
      elapsedMs: 0,
      completed: false,
    };
    this.records.set(animation, record);
    this.eventLog.push({ type: 'play', name, startMs, endMs, durationMs: record.durationMs, ownerId: owner.id });
    return record;
  }

  cancel(animation, reason = 'cancel') {
    const record = this.records.get(animation);
    if (!record) return false;
    this.records.delete(animation);
    this.eventLog.push({ type: reason, name: record.name, ownerId: record.owner.id });
    return true;
  }

  update(deltaMs) {
    for (const [animation, record] of [...this.records]) {
      if (record.owner.lifecycleGeneration !== record.ownerLifecycle || record.owner.inPool || record.owner.destroyed) {
        this.cancel(animation, 'stale-lifecycle');
        continue;
      }
      record.elapsedMs += deltaMs;
      if (record.elapsedMs < record.durationMs) continue;
      this.records.delete(animation);
      record.completed = true;
      this.eventLog.push({
        type: 'stopped',
        name: record.name,
        ownerId: record.owner.id,
        elapsedMs: record.elapsedMs,
      });
      // 回退桩模拟 STOPPED：`elapsedMs >= durationMs` 后触发 `animation.event(this.stoppedEvent)`，
      // 模拟正式 Laya/Spine 动画运行时的 STOPPED 事件。该信号经 `BowSoldier._onAttackAnimationStopped`
      // 统一入口驱动 `launchArrow`，规则层只依赖 STOPPED 到达信号，不依赖动画帧本身。
      animation.event(this.stoppedEvent);
    }
  }

  has(animation) { return this.records.has(animation); }
  get activeCount() { return this.records.size; }

  gameOver() {
    this.records.clear();
  }

  resetForTests() {
    this.records.clear();
    this.eventLog.length = 0;
    if (this.initialized) this.gameLoop.unregister('developmentAnimationDriver');
    this.initialized = false;
  }
}

module.exports = { DevelopmentAnimationDriver };
