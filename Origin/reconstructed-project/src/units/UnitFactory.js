'use strict';

const { SingletonBase } = require('../core/SingletonBase');
const { KnifeSoldier } = require('./KnifeSoldier');
const { BowSoldier } = require('./BowSoldier');
const { SpearSoldier } = require('./SpearSoldier');
const { CavalrySoldier } = require('./CavalrySoldier');

class UnresolvedFriendlyUnitTypeError extends Error {
  constructor(key) {
    super(`Friendly unit type ${String(key)} is not reconstructed`);
    this.name = 'UnresolvedFriendlyUnitTypeError';
  }
}

/**
 * 原 sc + tb.zx 的可维护组合。
 * sc 负责按类池获取；tb.zx 保存兵种索引到构造函数的注册表。
 */
class UnitFactory extends SingletonBase {
  constructor() {
    super();
    this.byIndex = new Map();
    this.byText = new Map();
    this.creationLog = [];
    this._configured = false;
  }

  configure({ objectPool, dependencyResolver } = {}) {
    if (!objectPool) throw new TypeError('UnitFactory requires ObjectPool');
    if (typeof dependencyResolver !== 'function') throw new TypeError('UnitFactory requires dependencyResolver()');
    Object.assign(this, { objectPool, dependencyResolver });
    this.byIndex.clear();
    this.byText.clear();
    this.register(0, '刀', KnifeSoldier);
    this.register(1, '弓', BowSoldier);
    this.register(2, '枪', SpearSoldier);
    this.register(3, '骑', CavalrySoldier);
    this._configured = true;
    return this;
  }

  register(index, text, ClassType) {
    if (this.byIndex.has(index) || this.byText.has(text)) throw new Error(`Duplicate friendly unit registration: ${index}/${text}`);
    const entry = Object.freeze({ index, text, ClassType });
    this.byIndex.set(index, entry);
    this.byText.set(text, entry);
    return this;
  }

  createByIndex(index) {
    const entry = this.byIndex.get(index);
    if (!entry) throw new UnresolvedFriendlyUnitTypeError(index);
    return this._create(entry);
  }

  createByText(text) {
    const entry = this.byText.get(text);
    if (!entry) throw new UnresolvedFriendlyUnitTypeError(text);
    return this._create(entry);
  }

  _create(entry) {
    if (!this._configured) throw new Error('UnitFactory.configure() must run before create');
    const unit = this.objectPool.takeByClass(entry.ClassType);
    unit.configure(this.dependencyResolver(unit));
    this.creationLog.push({ index: entry.index, text: entry.text, unit });
    return unit;
  }

  resetForTests() {
    this.byIndex.clear();
    this.byText.clear();
    this.creationLog.length = 0;
    this._configured = false;
    this.objectPool = null;
    this.dependencyResolver = null;
  }
}

module.exports = { UnitFactory, UnresolvedFriendlyUnitTypeError };
