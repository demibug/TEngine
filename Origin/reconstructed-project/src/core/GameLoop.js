'use strict';

const { SingletonBase } = require('./SingletonBase');

/**
 * 重建模块：全局逻辑更新循环
 * 原始范围：bundle.strings-decoded.js:3769-3874, 11920
 * 原始主要符号：pV；闭包别名 nx
 * 重建状态：COMPLETE_FOR_CRITICAL_PATH
 */
class GameLoop extends SingletonBase {
  constructor() {
    super();
    this.paused = false;
    this.delta = 0;
    this.elapsedGameTime = 0;
    this.callbacks = new Map();
    this.lastTimer = 0;
    this.serverTime = 0;
    this.laya = null;
    this.initialized = false;
  }

  configure({ laya, Laya } = {}) {
    const runtime = laya || Laya;
    if (!runtime || !runtime.timer) throw new TypeError('GameLoop requires Laya.timer');
    this.laya = runtime;
    return this;
  }

  /** 原 La：同键注册覆盖旧条目，并保持 Map 尾部顺序。 */
  register(key, caller, callback) {
    if (typeof callback !== 'function') throw new TypeError(`GameLoop callback must be a function: ${key}`);
    if (this.callbacks.has(key)) this.callbacks.delete(key);
    this.callbacks.set(key, { caller: caller || null, callback });
  }

  /** 原 wa。 */
  unregister(key) {
    this.callbacks.delete(key);
  }

  /** 原 update：500ms 截断，80ms 子步长。 */
  update() {
    if (this.paused) return;
    const currentTimer = this._requireLaya().timer.currTimer;
    let remaining = currentTimer - this.lastTimer;
    if (remaining <= 0) return;
    remaining = Math.min(remaining, GameLoop.MAX_FRAME_DELTA_MS);
    this.delta = remaining;
    while (remaining > 0) {
      const step = Math.min(GameLoop.LOGIC_STEP_MS, remaining);
      for (const { caller, callback } of this.callbacks.values()) callback.call(caller, step);
      this.elapsedGameTime += step;
      remaining -= step;
    }
    this.lastTimer = currentTimer;
  }

  pause(pauseLayaTimer = true) {
    this.paused = true;
    if (pauseLayaTimer) this._requireLaya().timer.pause();
  }

  init() {
    const laya = this._requireLaya();
    if (this.initialized) return;
    laya.timer.frameLoop(1, this, this.update);
    this.lastTimer = 0;
    this.initialized = true;
  }

  resume() {
    this._requireLaya().timer.resume();
    this.paused = false;
  }

  isRegistered(key) {
    return this.callbacks.has(key);
  }

  // 兼容第三轮测试与早期命名；不改变原注册语义。
  hasRegistration(key) { return this.isRegistered(key); }
  get elapsed() { return this.elapsedGameTime; }

  registrationKeys() {
    return [...this.callbacks.keys()];
  }

  resetForTests() {
    if (this.laya && this.laya.timer) this.laya.timer.clear(this, this.update);
    this.callbacks.clear();
    this.paused = false;
    this.delta = 0;
    this.elapsedGameTime = 0;
    this.lastTimer = 0;
    this.serverTime = 0;
    this.initialized = false;
    this.laya = null;
  }

  _requireLaya() {
    const laya = this.laya || globalThis.Laya;
    if (!laya || !laya.timer) throw new Error('Laya.timer is not available');
    return laya;
  }
}

GameLoop.LOGIC_STEP_MS = 80;
GameLoop.MAX_FRAME_DELTA_MS = 500;

module.exports = { GameLoop };
