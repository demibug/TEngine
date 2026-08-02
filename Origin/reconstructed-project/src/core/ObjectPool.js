'use strict';

/**
 * 重建模块：敌人运行时对象池适配
 * 原始范围：bundle.strings-decoded.js:13380-14360, 19184-19226
 * 原始主要符号：uu/rw、s0.produce/recover、Laya.Pool
 * 重建状态：COMPLETE_FOR_ENEMY_RUNTIME
 *
 * 说明：原工程同时使用“字符串池键”和“按类池”。本适配器保留两套语义，
 * 不在回收时销毁对象，也不改变重复回收保护。
 */
class ObjectPool {
  constructor({ laya = null } = {}) {
    this.laya = laya || globalThis.Laya || null;
    this.keyFactories = new Map();
    this.keyResetters = new Map();
    this.localKeyPools = new Map();
    this.localClassPools = new Map();
    this.takeLog = [];
    this.recoverLog = [];
  }

  configure({ laya } = {}) {
    if (laya) this.laya = laya;
    return this;
  }

  registerKey(key, create, reset = null) {
    if (!key || typeof key !== 'string') throw new TypeError('ObjectPool key must be a non-empty string');
    if (typeof create !== 'function') throw new TypeError(`ObjectPool creator for ${key} must be a function`);
    if (reset != null && typeof reset !== 'function') throw new TypeError(`ObjectPool resetter for ${key} must be a function`);
    this.keyFactories.set(key, create);
    if (reset) this.keyResetters.set(key, reset); else this.keyResetters.delete(key);
    return this;
  }

  takeByKey(key, caller = null) {
    const create = this.keyFactories.get(key);
    if (!create) throw new Error(`ObjectPool: no creator registered for key ${key}`);
    const pool = this._layaPool();
    let value;
    if (pool && typeof pool.getItemByCreateFun === 'function') {
      value = pool.getItemByCreateFun(key, create, caller);
    } else {
      const bucket = this._bucket(this.localKeyPools, key);
      value = bucket.length ? bucket.pop() : create.call(caller);
      value.__InPool = false;
    }
    if (!value) throw new Error(`ObjectPool creator returned empty value for key ${key}`);
    this.takeLog.push({ kind: 'key', key, value });
    return value;
  }

  recoverByKey(key, value) {
    if (!value || value.__InPool) return false;
    const reset = this.keyResetters.get(key);
    if (reset) reset(value);
    const pool = this._layaPool();
    if (pool && typeof pool.recover === 'function') pool.recover(key, value);
    else {
      value.__InPool = true;
      this._bucket(this.localKeyPools, key).push(value);
    }
    this.recoverLog.push({ kind: 'key', key, value });
    return true;
  }

  takeByClass(ClassType, create = null) {
    if (typeof ClassType !== 'function') throw new TypeError('ObjectPool.takeByClass requires a class');
    const pool = this._layaPool();
    let value;
    if (pool && typeof pool.createByClass === 'function' && !create) {
      value = pool.createByClass(ClassType);
    } else {
      const bucket = this._bucket(this.localClassPools, ClassType);
      value = bucket.length ? bucket.pop() : (create ? create() : new ClassType());
      value.__InPool = false;
    }
    if (!value) throw new Error(`ObjectPool class creator returned empty value: ${ClassType.name || '<anonymous>'}`);
    this.takeLog.push({ kind: 'class', key: ClassType, value });
    return value;
  }

  recoverByClass(value) {
    if (!value || value.__InPool) return false;
    const pool = this._layaPool();
    if (pool && typeof pool.recoverByClass === 'function') pool.recoverByClass(value);
    else {
      value.__InPool = true;
      this._bucket(this.localClassPools, value.constructor).push(value);
    }
    this.recoverLog.push({ kind: 'class', key: value.constructor, value });
    return true;
  }

  sizeByKey(key) {
    const pool = this._layaPool();
    if (pool && typeof pool.getPoolBySign === 'function') return pool.getPoolBySign(key).length;
    return this._bucket(this.localKeyPools, key).length;
  }

  sizeByClass(ClassType) {
    return this._bucket(this.localClassPools, ClassType).length;
  }

  clear() {
    this.localKeyPools.clear();
    this.localClassPools.clear();
    this.takeLog.length = 0;
    this.recoverLog.length = 0;
  }

  _layaPool() {
    return this.laya && this.laya.Pool ? this.laya.Pool : null;
  }

  _bucket(map, key) {
    let bucket = map.get(key);
    if (!bucket) { bucket = []; map.set(key, bucket); }
    return bucket;
  }
}

module.exports = { ObjectPool };
