'use strict';

const { SingletonBase } = require('../core/SingletonBase');
const { HitEnemyStrategy } = require('./HitEnemyStrategy');

/**
 * 重建模块：BOW-PROJECTILE-COMBAT-01 / ProjectileManager
 * 原始范围：bundle.strings-decoded.js:37209-37474
 * 原始符号：vA
 * 重建状态：COMPLETE_FOR_SIMPLE_DYNAMIC_ARROW
 */
class ProjectileManager extends SingletonBase {
  constructor() {
    super();
    this.offscreenMargin = 100;
    this.activeProjectiles = [];
    this.initialized = false;
    this.updateLog = [];
    this.removalLog = [];
  }

  configure({ gameLoop, enemyManager, gameData, projectileFactory, laya, logger = console } = {}) {
    if (!gameLoop || !enemyManager || !gameData || !projectileFactory || !laya) {
      throw new TypeError('ProjectileManager requires gameLoop, enemyManager, gameData, projectileFactory and laya');
    }
    Object.assign(this, { gameLoop, enemyManager, gameData, projectileFactory, laya, logger });
    return this;
  }

  init() {
    if (this.initialized) return;
    this.activeProjectiles = [];
    this.gridWidth = this.gameData.map.gridWidth;
    this.gridHeight = this.gameData.map.gridHeight;
    this.gameLoop.register('bulletMgr', this, this.update);
    this.initialized = true;
  }

  create(config, startPoint = { x: 0, y: 0 }) {
    if (!this.initialized) throw new Error('ProjectileManager.init() must run before create()');
    const projectile = this.projectileFactory.produce(config);
    projectile.manager = this;
    projectile.pos(startPoint.x, startPoint.y);
    projectile.resetData({
      ...config,
      attacker: config.attacker,
      speedScale: config.speedScale,
      hitStrategy: config.hitStrategy,
      movement: config.movement,
      manager: this,
    });
    this.activeProjectiles.push(projectile);
    return projectile;
  }

  getById(projectileId) {
    return this.activeProjectiles.find(projectile => projectile.projectileId === projectileId) || null;
  }

  removeById(projectileId) {
    const index = this.activeProjectiles.findIndex(projectile => projectile.projectileId === projectileId);
    return index >= 0 ? this._removeAt(index, 'removeById') : false;
  }

  remove(projectile) {
    const index = this.activeProjectiles.indexOf(projectile);
    return index >= 0 ? this._removeAt(index, 'remove') : false;
  }

  update(deltaMs) {
    // CONFIRMED：原 vA 从数组尾到头更新，允许当前索引同步 splice。
    for (let index = this.activeProjectiles.length - 1; index >= 0; index -= 1) {
      const projectile = this.activeProjectiles[index];
      if (!projectile.attacker) continue;

      let shouldRemove = false;
      if (!projectile.renderNode.parent || this._isOutsideStage(projectile)) shouldRemove = true;

      if (projectile.active) {
        projectile.movement.update(deltaMs, projectile.speedScale);
        projectile.update(deltaMs);
      }

      shouldRemove = shouldRemove || projectile.requestedRemoval;
      if (projectile.active && projectile.hitStrategy instanceof HitEnemyStrategy && !projectile.hitStrategy.completed) {
        const strategy = projectile.hitStrategy;
        const trigger = shouldRemove && (strategy.triggerMode === 'requestRemove' || strategy.triggerMode === 'both')
          || projectile.hitEnabled && (strategy.triggerMode === 'hitEnable' || strategy.triggerMode === 'both');
        if (trigger) {
          if (strategy.delayMs > 0) {
            if (!strategy.delayStarted) {
              strategy.delayStarted = true;
              projectile.hitDelayRemainingMs = strategy.delayMs;
            } else {
              projectile.hitDelayRemainingMs -= deltaMs;
              if (projectile.hitDelayRemainingMs <= 0) {
                this._applyTargetStrategy(projectile, strategy);
                if (strategy.removeAfterHit) shouldRemove = true;
              }
            }
          } else {
            this._applyTargetStrategy(projectile, strategy);
            if (strategy.removeAfterHit) shouldRemove = true;
          }
        }
      }

      this.updateLog.push({
        projectileId: projectile.projectileId,
        deltaMs,
        x: projectile.x,
        y: projectile.y,
        progress: projectile.movement ? projectile.movement.progress : null,
        requestedRemoval: projectile.requestedRemoval,
      });

      if (shouldRemove) {
        projectile.notifyRequestRemove();
        if (projectile.removeDelayMs === 0 || projectile.immediateRemoval) {
          this._removeAt(index, projectile.immediateRemoval ? 'immediate' : 'completed');
        } else {
          projectile.removeDelayRemainingMs -= deltaMs;
          if (projectile.removeDelayRemainingMs <= 0) this._removeAt(index, 'delayed');
        }
      }
    }
  }

  gameOver() {
    for (let index = this.activeProjectiles.length - 1; index >= 0; index -= 1) {
      const projectile = this.activeProjectiles[index];
      projectile.renderNode.offAll();
      this._removeAt(index, 'gameOver');
    }
    this.activeProjectiles = [];
  }

  resetForTests() {
    if (this.initialized && this.gameLoop) this.gameLoop.unregister('bulletMgr');
    this.gameOver();
    this.initialized = false;
    this.updateLog.length = 0;
    this.removalLog.length = 0;
  }

  get activeCount() { return this.activeProjectiles.length; }

  _applyTargetStrategy(projectile, strategy) {
    let hitAny = false;
    for (const targetId of strategy.targetIds) {
      const enemy = this.enemyManager.enemies.get(targetId);
      if (enemy) {
        projectile.hit(enemy);
        hitAny = true;
      }
    }
    if (hitAny) projectile.finishHit();
    strategy.completed = true;
  }

  _removeAt(index, reason) {
    const projectile = this.activeProjectiles[index];
    if (!projectile) return false;
    if (projectile.movement && typeof projectile.movement.beforeRecover === 'function') projectile.movement.beforeRecover();
    const projectileId = projectile.projectileId;
    this.projectileFactory.recover(projectile);
    this.activeProjectiles.splice(index, 1);
    this.removalLog.push({ projectileId, reason });
    return true;
  }

  _isOutsideStage(projectile) {
    const stage = this.laya.stage;
    if (!stage) return false;
    return projectile.x > stage.width + this.offscreenMargin
      || projectile.y > stage.height + this.offscreenMargin
      || projectile.x < -this.offscreenMargin
      || projectile.y < -this.offscreenMargin;
  }
}

module.exports = { ProjectileManager };
