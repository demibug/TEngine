'use strict';

/** 原 ow/oc：关键路径事件中心。 */
class EventBus {
  constructor() { this.listeners = new Map(); }

  on(type, caller, listener, presetArgs) {
    return this._add(type, caller, listener, presetArgs, false);
  }

  once(type, caller, listener, presetArgs) {
    return this._add(type, caller, listener, presetArgs, true);
  }

  off(type, caller, listener) {
    if (typeof caller === 'function' && listener === undefined) { listener = caller; caller = null; }
    const list = this.listeners.get(type);
    if (!list) return this;
    const next = list.filter(entry => !(entry.caller === (caller || null) && entry.listener === listener));
    if (next.length) this.listeners.set(type, next); else this.listeners.delete(type);
    return this;
  }

  offAll(type) {
    if (type === undefined || type === null) this.listeners.clear(); else this.listeners.delete(type);
    return this;
  }

  offAllCaller(caller) {
    for (const [type, list] of this.listeners) {
      const next = list.filter(entry => entry.caller !== caller);
      if (next.length) this.listeners.set(type, next); else this.listeners.delete(type);
    }
    return this;
  }

  event(type, ...args) {
    const list = this.listeners.get(type);
    if (!list || list.length === 0) return false;
    const eventArgs = args.length === 1 && Array.isArray(args[0]) ? args[0] : args;
    for (const entry of list.slice()) {
      if (!(this.listeners.get(type) || []).includes(entry)) continue;
      const preset = entry.presetArgs == null ? [] : Array.isArray(entry.presetArgs) ? entry.presetArgs : [entry.presetArgs];
      entry.listener.call(entry.caller, ...preset, ...eventArgs);
      if (entry.once) this.off(type, entry.caller, entry.listener);
    }
    return true;
  }

  hasListener(type) { return (this.listeners.get(type) || []).length > 0; }
  listenerCount(type) { return (this.listeners.get(type) || []).length; }
  resetForTests() { this.listeners.clear(); }

  _add(type, caller, listener, presetArgs, once) {
    if (typeof caller === 'function' && listener === undefined) { listener = caller; caller = null; }
    if (typeof listener !== 'function') throw new TypeError('Event listener must be a function');
    const list = this.listeners.get(type) || [];
    const existing = list.find(entry => entry.caller === (caller || null) && entry.listener === listener);
    const value = { caller: caller || null, listener, presetArgs, once };
    if (existing) Object.assign(existing, value); else list.push(value);
    this.listeners.set(type, list);
    return this;
  }
}

const GameEvents = Object.freeze({
  BATTLE_FINISHED: 'l',
  ROUND_STARTED: 'Ft',
  ROUND_SPAWN_PREPARED: 'Jt',
  BATTLE_SCENE_GAME_OVER: 'It',
  BATTLE_RESULT_READY: 'o',
  HEALTH_CHANGED: 'Ct',
  GOLD_CHANGED: 'Dt',
  ENEMY_CREATED: 'Ht',
  ENEMY_REGISTERED: 'nt',
  ENEMY_REMOVED: 'ot',
  ENEMY_GRID_LEFT: 'ft',
  ENEMY_VISUAL_ADDED: 'bt',
  ENEMY_GRID_ENTERED: 'vt',
  ENEMY_GRID_ENTITY_ENTERED: '_t',
  ENEMY_KILLED_BY: 'ht',
  ENEMY_APPROACH_WARNING: 'kt',
  ENEMY_FINAL_WARNING: 'wt',
  // 灵魂投射到达事件（bundle 符号 sS["ut"]，字符串键 "ut"）。
  // 参数：(isPlayerLane, enemyX, enemyY, currentPathIndex)。消费者（召唤方）属提案 ②③，DEFERRED 接入。
  ENEMY_SOUL_DELIVERED: 'ut',
  // 傀儡路径同步事件（bundle 符号 sS["yt"]，字符串键 "yt"，bundle:31805/32291）。
  // 由被操控士兵/主控方死亡流程经 oc.event("yt", this["Lm"]) 发出，携带 pathIndex；
  // Puppet 订阅此事件经 nB(pathIndex) 更新 currentPathIndex（同步到被操控士兵真实路径）。
  PUPPET_PATH_SYNC: 'yt',
  SKILL_EFFECT_REQUESTED: 'skill:effect:requested',
  BOSS_SPAWNED: 'boss:spawned',
  BOSS_REMOVED: 'boss:removed',
  WAVE_PLANNED: 'wave:planned',
});

module.exports = { EventBus, GameEvents, CoreEvents: GameEvents };
