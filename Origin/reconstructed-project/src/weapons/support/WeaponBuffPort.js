'use strict';
class WeaponBuffPort {
  constructor(manager = null, weaponName = 'unknown') { this.manager = manager; this.weaponName = weaponName; }
  bind(manager) { this.manager = manager; return this; }
  _require(method, type) {
    if (!this.manager || typeof this.manager[method] !== 'function') {
      throw new Error(`[${this.weaponName}] Buff dependency unavailable: ${method}(${type})`);
    }
    return this.manager;
  }
  applyBuff(targetId, type, value, multiplicative = false, duration, custom) {
    return this._require('applyBuff', type).applyBuff(targetId, type, value, multiplicative, duration, custom);
  }
  modify(targetId, type, buffId, value, multiplicative, duration, custom) {
    return this._require('modify', type).modify(targetId, type, buffId, value, multiplicative, duration, custom);
  }
  removeBuff(targetId, type, buffId) {
    const manager = this.manager;
    if (!manager) throw new Error(`[${this.weaponName}] Buff dependency unavailable: removeBuff(${type})`);
    if (typeof manager.Jw === 'function') return manager.Jw(targetId, type, buffId);
    if (typeof manager.removeBuff === 'function') return manager.removeBuff(targetId, type, buffId);
    throw new Error(`[${this.weaponName}] Buff dependency unavailable: removeBuff(${type})`);
  }
}
module.exports = WeaponBuffPort;
