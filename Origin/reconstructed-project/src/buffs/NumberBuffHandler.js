'use strict';
const { BuffHandlerBase } = require('./BuffHandlerBase');

/** 原 uM / poolNumberBuff。 */
class NumberBuffHandler extends BuffHandlerBase {
  effectiveDelta(num, multiplicative) {
    if (!multiplicative) return Number(num) || 0;
    if (!this.target || typeof this.target.jw !== 'function') {
      throw new Error(`Buff target ${this.target && this.target.id} does not implement jw(type)`);
    }
    return (Number(this.target.jw(this.type)) || 0) * (Number(num) || 0);
  }

  addLayer(data) {
    const layer = this.createLayer(data);
    layer.appliedDelta = this.effectiveDelta(layer.num, layer.Nw);
    this.applyDelta(layer.appliedDelta, false);
    this.layers.push(layer);
    this.registerRoundExpiry(layer);
    return layer.id;
  }

  modifyLayer(index, num, multiplicative, time) {
    const layer = this.layers[index];
    this.applyDelta(-layer.appliedDelta, true);
    if (num != null) layer.num = num;
    if (multiplicative != null) layer.Nw = Boolean(multiplicative);
    if (time != null) layer.time = time;
    layer.appliedDelta = this.effectiveDelta(layer.num, layer.Nw);
    this.applyDelta(layer.appliedDelta, false);
    return true;
  }

  removeLayer(index) {
    const layer = this.layers[index];
    if (!layer) return false;
    this.applyDelta(-layer.appliedDelta, true);
    this.unregisterRoundExpiry(layer.id);
    this.layers.splice(index, 1);
    return true;
  }

  /** 原 fv/uc：添加时第三参数缺省；撤销时第三参数为 true。 */
  applyDelta(value, removing) {
    if (!this.target || typeof this.target.zw !== 'function') {
      throw new Error(`Buff target ${this.target && this.target.id} does not implement zw(type, delta)`);
    }
    this.target.zw(this.type, value, Boolean(removing));
  }

  label() {
    if (!this.layers.length) return null;
    const value = this.layers[this.layers.length - 1].num;
    const name = this.type === 0 ? '攻击力' : this.type === 1 ? '攻速' : this.type === 2 ? '范围' : '';
    if (!name || value === 0) return null;
    return `${name}${value < 0 ? '降低' : '提升'}`;
  }
}

module.exports = { NumberBuffHandler };
