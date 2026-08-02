'use strict';
const { BuffHandlerBase } = require('./BuffHandlerBase');
class CustomBuffHandler extends BuffHandlerBase {
  addLayer(data) {
    const custom = data.qw;
    if (!custom || typeof custom.onStart !== 'function') throw new TypeError('Buff.custom requires qw.onStart(handler)');
    const layer = this.createLayer(data);
    layer.Bv = custom.Bv;
    layer.onStart = custom.onStart;
    layer.onEnd = typeof custom.onEnd === 'function' ? custom.onEnd : null;
    layer.num = 0; layer.Nw = false;
    this.layers.push(layer);
    layer.onStart(this);
    return layer.id;
  }
  modifyLayer(index, _num, _multiplicative, time) { if (time != null) this.layers[index].time = time; return true; }
  removeLayer(index) { const layer = this.layers[index]; if (!layer) return false; this.layers.splice(index, 1); if (layer.onEnd) layer.onEnd.call(layer, this); return true; }
}
module.exports = { CustomBuffHandler };
