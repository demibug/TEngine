'use strict';

const { SimpleDynamicArrow } = require('./SimpleDynamicArrow');
const {EagleArrow} = require('./types/EagleArrow');
const {FireArrow} = require('./types/FireArrow');
const {HuoFengHuang} = require('./types/HuoFengHuang');
const {PikeSnakeBullet} = require('./types/PikeSnakeBullet');
const {LightningChain} = require('./types/LightningChain');
const {ShenBiArrow} = require('./types/ShenBiArrow');
// 提案 ④b 新增 16 弹种
const {SimpleHitAreaBullet} = require('./types/SimpleHitAreaBullet');
const {KnifeBullet} = require('./types/KnifeBullet');
const {PikeBullet} = require('./types/PikeBullet');
const {StaticFireBall} = require('./types/StaticFireBall');
const {VirtualBullet} = require('./types/VirtualBullet');
const {SwordBullet} = require('./types/SwordBullet');
const {StarBullet} = require('./types/StarBullet');
const {FireDragonArrow} = require('./types/FireDragonArrow');
const {GroundSpikeBullet} = require('./types/GroundSpikeBullet');
const {FireExplosiveArrow} = require('./types/FireExplosiveArrow');
const {DaoQiBullet} = require('./types/DaoQiBullet');
const {AttachCustomShapeBullet} = require('./types/AttachCustomShapeBullet');
const {SimpleHitBullet} = require('./types/SimpleHitBullet');
const {LiHuaBullet} = require('./types/LiHuaBullet');
const {LightningArrow} = require('./types/LightningArrow');
const {FlyPike} = require('./types/FlyPike');

class UnresolvedProjectileTypeError extends Error {
  constructor(key) {
    super(`Projectile type ${String(key)} is not reconstructed`);
    this.name = 'UnresolvedProjectileTypeError';
  }
}

/**
 * 重建模块：BOW-PROJECTILE-COMBAT-01 / ProjectileFactory
 * 原始范围：bundle.strings-decoded.js:33698-33780
 * 原始符号：vj/vk
 * 重建状态：COMPLETE_FOR_CURRENT_REGISTERED_TYPES
 */
class ProjectileFactory {
  constructor({ laya, objectPool, enemyManager, gameData, parentResolver, effects, logger = console } = {}) {
    if (!laya || !objectPool || !enemyManager || !gameData || typeof parentResolver !== 'function' || !effects) {
      throw new TypeError('ProjectileFactory requires laya, objectPool, enemyManager, gameData, parentResolver and effects');
    }
    Object.assign(this, { laya, objectPool, enemyManager, gameData, parentResolver, effects, logger });
    this.registry = new Map();
    this.registeredPoolKeys = new Set();
    this.nextProjectileId = 0;
    this.creationLog = [];
    this.recoveryLog = [];
    this.register(SimpleDynamicArrow.projectileTypeKey, SimpleDynamicArrow);
    for (const [key, cls] of [
      ['EagleArrow', EagleArrow],
      ['FireArrow', FireArrow],
      ['HuoFengHuang', HuoFengHuang],
      ['PikeSnakeBullet', PikeSnakeBullet],
      ['LightningChain', LightningChain],
      ['ShenBiArrow', ShenBiArrow],
      // 提案 ④b 新增 16 弹种（覆盖 bundle 注册的 23 弹种全集）
      ['SimpleHitAreaBullet', SimpleHitAreaBullet],
      ['KnifeBullet', KnifeBullet],
      ['PikeBullet', PikeBullet],
      ['StaticFireBall', StaticFireBall],
      ['VirtualBullet', VirtualBullet],
      ['SwordBullet', SwordBullet],
      ['StarBullet', StarBullet],
      ['FireDragonArrow', FireDragonArrow],
      ['GroundSpikeBullet', GroundSpikeBullet],
      ['FireExplosiveArrow', FireExplosiveArrow],
      ['DaoQiBullet', DaoQiBullet],
      ['AttachCustomShapeBullet', AttachCustomShapeBullet],
      ['SimpleHitBullet', SimpleHitBullet],
      ['LiHuaBullet', LiHuaBullet],
      ['LightningArrow', LightningArrow],
      ['FlyPike', FlyPike],
    ]) this.register(key, cls);
  }

  register(typeKey, ClassType) {
    if (this.registry.has(typeKey)) throw new Error(`Duplicate projectile registration: ${typeKey}`);
    this.registry.set(typeKey, ClassType);
    return this;
  }

  produce(config) {
    const typeKey = this._resolveTypeKey(config.type);
    const ClassType = this.registry.get(typeKey);
    if (!ClassType) throw new UnresolvedProjectileTypeError(typeKey);
    const appearance = this._normalizeAppearance(config.appearance || config.XS || {});
    const poolKey = `${ProjectileFactory.POOL_PREFIX}_${typeKey}_${appearance.label}`;
    if (!this.registeredPoolKeys.has(poolKey)) {
      this.objectPool.registerKey(poolKey, () => {
        const renderNode = new this.laya.Sprite();
        const projectile = new ClassType(appearance.label);
        projectile.configure({
          laya: this.laya,
          enemyManager: this.enemyManager,
          gameData: this.gameData,
          effects: this.effects,
          logger: this.logger,
        });
        projectile.initialize(renderNode);
        projectile.poolKey = poolKey;
        return projectile;
      });
      this.registeredPoolKeys.add(poolKey);
    }

    const projectile = this.objectPool.takeByKey(poolKey);
    const newlyCreated = !projectile.imageNode;
    projectile.configure({
      laya: this.laya,
      enemyManager: this.enemyManager,
      gameData: this.gameData,
      effects: this.effects,
      logger: this.logger,
    });
    projectile.poolKey = poolKey;
    projectile.appearanceLabel = appearance.label;
    projectile.projectileId = this.nextProjectileId++;
    if (newlyCreated || !projectile.imageNode) projectile.initializeAppearance(appearance);

    const parent = this.parentResolver();
    if (!parent || typeof parent.addChild !== 'function') throw new Error('Projectile parent is unavailable');
    parent.addChild(projectile.renderNode);
    projectile.renderNode.zIndex = 0;
    this.creationLog.push({ typeKey, poolKey, projectileId: projectile.projectileId, newlyCreated, projectile });
    return projectile;
  }

  recover(projectile) {
    if (!projectile || projectile.recovered) return false;
    if (this.laya.Tween && typeof this.laya.Tween.killAll === 'function') this.laya.Tween.killAll(projectile.renderNode);
    const poolKey = projectile.poolKey;
    const projectileId = projectile.projectileId;
    projectile.recover();
    projectile.renderNode.removeSelf();
    this.objectPool.recoverByKey(poolKey, projectile);
    this.recoveryLog.push({ poolKey, projectileId, projectile });
    return true;
  }

  _resolveTypeKey(type) {
    if (typeof type === 'string') return type;
    if (type && type.projectileTypeKey) return type.projectileTypeKey;
    if (typeof type === 'function' && type.name) return type.name;
    throw new UnresolvedProjectileTypeError(type);
  }

  _normalizeAppearance(appearance) {
    return {
      label: appearance.label || appearance.tS || '',
      resourcePath: appearance.resourcePath || appearance.RS || '',
      size: appearance.size || appearance.US || null,
      scale: appearance.scale || appearance.OS || null,
      anchor: appearance.anchor || appearance.YS || null,
      alternateHitEffect: appearance.alternateHitEffect || appearance.CS || false,
    };
  }

  resetForTests() {
    this.registry.clear();
    this.registeredPoolKeys.clear();
    this.nextProjectileId = 0;
    this.creationLog.length = 0;
    this.recoveryLog.length = 0;
  }
}

ProjectileFactory.POOL_PREFIX = 'bullet_pool';

module.exports = { ProjectileFactory, UnresolvedProjectileTypeError };
