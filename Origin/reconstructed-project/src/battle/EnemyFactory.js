'use strict';

const { SingletonBase } = require('../core/SingletonBase');

/**
 * 重建模块：敌人工厂
 * 原始范围：bundle.strings-decoded.js:19184-19242
 * 原始符号：s0、ss
 * 重建状态：COMPLETE_FOR_MOB0_POOLING
 */
class EnemyFactory extends SingletonBase {
  constructor() {
    super();
    this.creators = new Map();
    this.objectPool = null;
    this.createLog = [];
    this.recoverLog = [];
  }

  configure({ objectPool } = {}) {
    if (!objectPool) throw new TypeError('EnemyFactory requires ObjectPool');
    this.objectPool = objectPool;
    return this;
  }

  register(typeName, creator) {
    if (typeof creator !== 'function') throw new TypeError(`Enemy creator for ${typeName} must be a function`);
    this.creators.set(typeName, creator);
  }

  registerPooledClass(typeName, ClassType, configureInstance) {
    if (!this.objectPool) throw new Error('EnemyFactory.configure() must run before registerPooledClass()');
    if (typeof ClassType !== 'function') throw new TypeError(`Enemy class for ${typeName} must be a constructor`);
    if (typeof configureInstance !== 'function') throw new TypeError(`Enemy configure callback for ${typeName} must be a function`);
    this.register(typeName, () => {
      const instance = this.objectPool.takeByClass(ClassType, () => new ClassType());
      configureInstance(instance);
      return instance;
    });
  }

  create(typeName) {
    const creator = this.creators.get(typeName);
    if (!creator) throw new Error(`EnemyFactory: 未为类型 ${typeName} 注册创建器`);
    const result = creator();
    if (!result) throw new Error(`EnemyFactory creator returned empty value: ${typeName}`);
    this.createLog.push({ typeName, enemy: result });
    return result;
  }

  /** 原 s0.produce。 */
  produce(ClassType) {
    if (!this.objectPool) throw new Error('EnemyFactory requires ObjectPool before produce()');
    return this.objectPool.takeByClass(ClassType);
  }

  /** 原 s0.recover。 */
  recover(enemy) {
    if (!this.objectPool) throw new Error('EnemyFactory requires ObjectPool before recover()');
    const recovered = this.objectPool.recoverByClass(enemy);
    if (recovered) this.recoverLog.push(enemy);
    return recovered;
  }

  init() {}
  resetForTests() {
    this.creators.clear();
    this.createLog.length = 0;
    this.recoverLog.length = 0;
    this.objectPool = null;
  }
}

const ENEMY_TYPE_KEYS = Object.freeze(['Mob0', 'Mob1', 'Mob2', 'Mob3', 'Zombie', 'Cavalry', 'Puppet']);
const BOSS_TYPE_KEYS = Object.freeze(['ZhangLiang','ZhangBao','ZhangJiao','SunShangXiang','ZhenFu','DiaoChan','HuaXiong','LvBu','DongZhuo','DianWei','XiaHouDun','CaoCao']);
module.exports = { EnemyFactory, ENEMY_TYPE_KEYS, BOSS_TYPE_KEYS, EnemyTypeNames: ENEMY_TYPE_KEYS, BossTypeNames: BOSS_TYPE_KEYS };
