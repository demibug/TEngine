'use strict';
const { BuffTimeMode } = require('./BuffTimeMode');
const { createBuffData } = require('./BuffData');
const { GameEvents } = require('../core/EventBus');

/** 原 rE：一个 Buff type 对应一个 handler，handler 内保存一个或多个子 Buff 层。 */
class BuffHandlerBase {
  constructor() {
    this.id = 0;
    this.layers = [];
    this.target = null;
    this.type = -1;
    this.manager = null;
    this.definition = null;
    this.roundListeners = new Map();
  }

  configure({ manager, target, type, definition }) {
    Object.assign(this, { manager, target, type, definition });
    this.id = manager.allocateBuffId();
    return this;
  }

  applyData(data) { return this.apply(this.target, this.type, data.num, data.Nw, data.time, data.qw); }

  apply(target, type, num, multiplicative, time, custom) {
    this.target = target;
    this.type = Number(type);
    const id = this.addLayer(createBuffData(num, multiplicative, time, custom));
    this.notifyTargetData();
    this.notifyTargetType();
    return id;
  }

  add(data) {
    const id = this.addLayer(data);
    this.notifyTargetData();
    return id;
  }

  addLayer(data) {
    const layer = this.createLayer(data);
    this.layers.push(layer);
    this.registerRoundExpiry(layer);
    return layer.id;
  }

  createLayer(data) {
    return {
      id: this.manager.allocateBuffId(),
      num: data.num,
      Nw: Boolean(data.Nw),
      time: data.time,
      timer: 0,
      qw: data.qw,
    };
  }

  registerRoundExpiry(layer) {
    if (layer.time !== BuffTimeMode.ROUND) return;
    const eventBus = this.manager && this.manager.eventBus;
    if (!eventBus || typeof eventBus.once !== 'function') {
      throw new Error(`Buff ${this.type} uses round duration but EventBus is unavailable`);
    }
    const listener = () => this.removeLayerById(layer.id);
    this.roundListeners.set(layer.id, listener);
    eventBus.once(GameEvents.ROUND_STARTED, this, listener);
  }

  unregisterRoundExpiry(layerId) {
    const listener = this.roundListeners.get(layerId);
    if (!listener) return;
    const eventBus = this.manager && this.manager.eventBus;
    if (eventBus && typeof eventBus.off === 'function') eventBus.off(GameEvents.ROUND_STARTED, this, listener);
    this.roundListeners.delete(layerId);
  }

  modify(id, num, multiplicative, time, custom) {
    const index = this.layers.findIndex(layer => layer.id === Number(id));
    if (index < 0) {
      if (this.manager && this.manager.logger) this.manager.logger.warn(`Buff(${this.id})中不存在ID为${id}的子Buff`);
      return false;
    }
    const oldTime = this.layers[index].time;
    const result = this.modifyLayer(index, num, multiplicative, time, custom);
    if (result) {
      if (oldTime === BuffTimeMode.ROUND && this.layers[index].time !== BuffTimeMode.ROUND) this.unregisterRoundExpiry(id);
      if (oldTime !== BuffTimeMode.ROUND && this.layers[index].time === BuffTimeMode.ROUND) this.registerRoundExpiry(this.layers[index]);
      this.notifyTargetData();
    }
    return result;
  }

  modifyLayer(index, num, multiplicative, time, custom) {
    const layer = this.layers[index];
    if (num != null) layer.num = num;
    if (multiplicative != null) layer.Nw = Boolean(multiplicative);
    if (time != null) layer.time = time;
    if (custom !== undefined) layer.qw = custom;
    return true;
  }

  removeLayerById(id) {
    const index = this.layers.findIndex(layer => layer.id === Number(id));
    if (index < 0) {
      if (this.manager && this.manager.logger) this.manager.logger.warn(`Buff(${this.id})中不存在ID为${id}的子Buff`);
      return false;
    }
    const removed = this.removeLayer(index);
    if (removed && this.layers.length === 0) this.manager.onHandlerEmpty(this.target, this.type);
    return removed;
  }

  removeLayer(index) {
    const layer = this.layers[index];
    if (!layer) return false;
    this.unregisterRoundExpiry(layer.id);
    this.layers.splice(index, 1);
    return true;
  }

  needsUpdate() {
    return this.layers.some(layer => layer.time !== BuffTimeMode.PERMANENT && layer.time !== BuffTimeMode.ROUND);
  }

  update(deltaMs) {
    for (let index = this.layers.length - 1; index >= 0; index -= 1) {
      const layer = this.layers[index];
      if (layer.time === BuffTimeMode.PERMANENT || layer.time === BuffTimeMode.ROUND) continue;
      layer.timer += deltaMs;
      if (layer.timer >= layer.time) this.removeLayerById(layer.id);
    }
  }

  remove() {
    const target = this.target;
    for (let i = this.layers.length - 1; i >= 0; i -= 1) this.removeLayer(i);
    this.layers.length = 0;
    this.onRemoved();
    if (target) {
      this.notifyTargetData(target);
      this.notifyTargetType(target);
    }
    if (this.manager && this.manager.eventBus) this.manager.eventBus.offAllCaller(this);
    this.roundListeners.clear();
    this.target = null;
    this.type = -1;
    this.id = 0;
    this.manager = null;
    this.definition = null;
  }

  onRemoved() {}
  label() { return (this.definition && this.definition.label) || ''; }
  isNegative(num) { return Number(num) < 0; }

  notifyTargetData(target = this.target) {
    if (!target) return;
    if (typeof target.onBuffDataChanged === 'function') target.onBuffDataChanged(this.type);
    else if (typeof target.am === 'function') {
      const node = target.am();
      if (node && typeof node.event === 'function') node.event('onBuffDataChanged');
    }
  }

  notifyTargetType(target = this.target) {
    if (!target) return;
    if (typeof target.onBuffTypeChanged === 'function') target.onBuffTypeChanged(this.type);
    else if (typeof target.am === 'function') {
      const node = target.am();
      if (node && typeof node.event === 'function') node.event('onBuffTypeChanged');
    }
  }

  // Original-symbol aliases.
  tv(target, type, data) { return this.apply(target, type, data.num, data.Nw, data.time, data.qw); }
  iv(data) { return this.add(data); }
  hv(id, num, multiplicative, time, custom) { return this.modify(id, num, multiplicative, time, custom); }
  Jw(id) { return this.removeLayerById(id); }
  lv() { return this.needsUpdate(); }
  onUpdate(deltaMs) { return this.update(deltaMs); }
}

module.exports = { BuffHandlerBase };
