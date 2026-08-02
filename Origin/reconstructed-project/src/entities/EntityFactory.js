'use strict';

class EntityTypeNotRegisteredError extends Error {
  constructor(type) {
    super(`Entity type is not registered: ${type}`);
    this.name = 'EntityTypeNotRegisteredError';
    this.type = type;
  }
}

/** 严格工厂：未注册类型明确失败，不返回空对象。 */
class EntityFactory {
  constructor() { this.creators = new Map(); this.created = []; }
  register(type, creator) {
    if (!type || typeof creator !== 'function') throw new TypeError('EntityFactory.register requires type and creator');
    this.creators.set(type, creator);
  }
  create(type, ...args) {
    const creator = this.creators.get(type);
    if (!creator) throw new EntityTypeNotRegisteredError(type);
    const value = creator(...args);
    if (!value) throw new Error(`Entity creator returned an empty value: ${type}`);
    this.created.push({ type, value });
    return value;
  }
  has(type) { return this.creators.has(type); }
}

module.exports = { EntityFactory, EntityTypeNotRegisteredError };
