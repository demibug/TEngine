'use strict';
const { BuffHandlerBase } = require('./BuffHandlerBase');
const { BuffTimeMode } = require('./BuffTimeMode');

/** 原 uN：状态 Buff 默认合并为一层，数值和持续时间累加。 */
class StateBuffHandler extends BuffHandlerBase {
  constructor({ mergeLayers = true, replaceDuration = false, replaceValue = false } = {}) {
    super();
    this.mergeLayers = mergeLayers;
    this.replaceDuration = replaceDuration;
    this.replaceValue = replaceValue;
  }

  addLayer(data) {
    if (this.mergeLayers && this.layers.length) {
      const layer = this.layers[0];
      layer.num = this.replaceValue ? data.num : (Number(layer.num) || 0) + (Number(data.num) || 0);
      if (layer.time !== BuffTimeMode.PERMANENT) {
        layer.time = this.replaceDuration ? data.time : (Number(layer.time) || 0) + (Number(data.time) || 0);
        if (this.replaceDuration) layer.timer = 0;
      }
      if (data.qw != null) layer.qw = data.qw;
      this.onMergedLayer(layer, data);
      return layer.id;
    }

    const layer = this.createLayer(data);
    this.layers.push(layer);
    this.registerRoundExpiry(layer);
    this.applyState(true, data.qw);
    this.onFirstLayer(layer);
    return layer.id;
  }

  onFirstLayer() {}
  onMergedLayer() {}

  removeLayer(index) {
    const layer = this.layers[index];
    if (!layer) return false;
    this.unregisterRoundExpiry(layer.id);
    this.layers.splice(index, 1);
    if (this.layers.length === 0) this.applyState(false);
    return true;
  }

  modifyLayer(index, num, multiplicative, time, custom) {
    const layer = this.layers[index];
    if (num != null) layer.num = num;
    if (multiplicative != null) layer.Nw = Boolean(multiplicative);
    if (time != null) layer.time = time;
    if (custom !== undefined) layer.qw = custom;
    return true;
  }

  applyState(enabled, custom) {
    if (!this.target || typeof this.target.setState !== 'function') {
      throw new Error(`Buff target ${this.target && this.target.id} does not implement setState(channel, enabled, data)`);
    }
    const channels = (this.definition && this.definition.channels) || [];
    for (const channel of channels) this.target.setState(channel, enabled, custom);
  }
}

module.exports = { StateBuffHandler };
