'use strict';

const { SingletonBase } = require('./SingletonBase');

/**
 * 重建模块：战斗动画对象池
 * 原始范围：bundle.strings-decoded.js:18534-18608
 * 原始主要符号：nz
 * 重建状态：COMPLETE_FOR_ADOU_CREATION
 *
 * 原始 `uz` 骨骼动画包装类尚未独立恢复，因此由 bootstrap 注入创建函数。
 * 对象池键、资源路径、快速模式开关和回收顺序保持原代码。
 */
class AnimationEntityPool extends SingletonBase {
  constructor() {
    super();
    this.nonFastAnimationIds = Object.freeze(['aDou', 'boss0', 'boss1', 'grass', 'huaXiong']);
    this.createLog = [];
    this.recoverLog = [];
  }

  configure({
    laya,
    isFrameAnimation,
    createFrameAnimation,
    createSkeletonAnimation,
  } = {}) {
    if (!laya || !laya.Pool) throw new TypeError('AnimationEntityPool requires Laya.Pool');
    if (typeof isFrameAnimation !== 'function') throw new TypeError('AnimationEntityPool requires isFrameAnimation');
    if (typeof createFrameAnimation !== 'function') throw new TypeError('AnimationEntityPool requires createFrameAnimation');
    if (typeof createSkeletonAnimation !== 'function') throw new TypeError('AnimationEntityPool requires createSkeletonAnimation');
    Object.assign(this, { laya, isFrameAnimation, createFrameAnimation, createSkeletonAnimation });
    return this;
  }

  /** 原 $d。 */
  create(animationId) {
    this._requireConfigured();
    let entity;
    let poolKey;
    let resourcePath = null;
    if (this.isFrameAnimation(animationId)) {
      poolKey = `fsk_${animationId}`;
      entity = this.laya.Pool.getItemByCreateFun(
        poolKey,
        () => this.createFrameAnimation(animationId),
      );
    } else {
      poolKey = `sk_${animationId}`;
      resourcePath = `resources/anim/${animationId}/skeleton.json`;
      entity = this.laya.Pool.getItemByCreateFun(
        poolKey,
        () => this.createSkeletonAnimation(resourcePath, animationId),
      );
      if (!entity || typeof entity.setIsFastMode !== 'function') {
        throw new Error(`Skeleton animation ${animationId} must expose setIsFastMode`);
      }
      entity.setIsFastMode(!this.nonFastAnimationIds.includes(animationId));
    }
    this.createLog.push({ animationId, poolKey, resourcePath, entity });
    return entity;
  }

  /** 原 Vd/Qd。 */
  createBossByIndex(index) {
    return this.create(this.resolveBossAnimationId(index));
  }

  resolveBossAnimationId(index) {
    if (index === 0 || index === 1 || index === 2) return 'boss0';
    if (index === 3 || index === 4 || index === 5) return 'boss1';
    if (index === 6) return 'huaXiong';
    if (index === 7) return 'lvBu';
    if (index === 8) return 'dongZhuo';
    if (index === 9 || index === 10 || index === 11) return 'boss2';
    return 'boss0';
  }

  /** 原 Zd：先重置对象，再回收到原池键。 */
  recover(entity, animationId) {
    this._requireConfigured();
    const frameAnimation = this.isFrameAnimation(animationId);
    const poolKey = `${frameAnimation ? 'fsk' : 'sk'}_${animationId}`;
    if (frameAnimation) {
      if (!entity || typeof entity.resetForPool !== 'function') {
        throw new Error(`Frame animation ${animationId} must expose resetForPool`);
      }
      entity.resetForPool();
    } else {
      if (!entity || typeof entity.Td !== 'function') {
        throw new Error(`Skeleton animation ${animationId} must expose Td`);
      }
      entity.Td();
    }
    this.laya.Pool.recover(poolKey, entity);
    this.recoverLog.push({ animationId, poolKey, entity });
  }

  init() {}

  resetForTests() {
    this.createLog.length = 0;
    this.recoverLog.length = 0;
    this.laya = null;
    this.isFrameAnimation = null;
    this.createFrameAnimation = null;
    this.createSkeletonAnimation = null;
  }

  _requireConfigured() {
    if (!this.laya || !this.isFrameAnimation || !this.createFrameAnimation || !this.createSkeletonAnimation) {
      throw new Error('AnimationEntityPool.configure() must run before use');
    }
  }
}

module.exports = { AnimationEntityPool };
