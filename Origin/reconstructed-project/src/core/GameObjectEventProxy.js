'use strict';

/**
 * 重建基础类：qE
 * 原始范围：bundle.strings-decoded.js:19600-19684
 * 重建状态：COMPLETE
 *
 * qE 并非敌人专用基类。它把事件调用转发到逻辑对象持有的 Laya 表现节点。
 */
class GameObjectEventProxy {
  constructor() {
    this.objectType = 0;
  }

  eventTarget() {
    throw new Error('GameObjectEventProxy.eventTarget() must be implemented');
  }

  once(type, caller, listener) {
    const target = this.eventTarget();
    return listener ? target.once(type, caller, listener) : target.once(type, caller);
  }

  on(type, caller, listener) {
    const target = this.eventTarget();
    return listener ? target.on(type, caller, listener) : target.on(type, caller);
  }

  off(type, caller, listener) {
    const target = this.eventTarget();
    if (listener !== undefined) return target.off(type, caller, listener);
    return target.off(type, caller);
  }

  event(type, ...args) {
    return this.eventTarget().event(type, ...args);
  }

  offAllCaller(caller) {
    return this.eventTarget().offAllCaller(caller);
  }

  offAll(type) {
    return this.eventTarget().offAll(type);
  }

  gameOver() {
    this.event('onDestroy');
  }
}

module.exports = { GameObjectEventProxy };
