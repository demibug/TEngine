'use strict';

/**
 * DEVELOPMENT_ONLY：在缺失正式 Spine/prefab 时推进非循环动画片段。
 * 正式 BowSoldier 仍只依赖 Laya.Event.STOPPED，不依赖本驱动器类型。
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
