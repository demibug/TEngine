'use strict';
const WeaponBase = require('../WeaponBase');
const WeaponBuffPort = require('../support/WeaponBuffPort');
const { TargetEnemyBezierMovement } = require('../../projectiles/TargetEnemyBezierMovement');
const { HitEnemyStrategy } = require('../../projectiles/HitEnemyStrategy');

/** Holder-driven q9 bow runtime. It delegates timing to the owning unit/general. */
class BowWeaponBase extends WeaponBase {
  constructor() {
    super();
    this.attackCount = 0;
    this.owner = null;
    this.projectileType = 'SimpleDynamicArrow';
    this.buffPort = new WeaponBuffPort(null, this.constructor.config?.name || 'BowWeapon');
    this.activeProjectiles = new Set();
  }
  attach(owner, buffManager) {
    if (this.owner && this.owner !== owner) this.detach();
    this.owner = owner;
    this.buffPort = new WeaponBuffPort(buffManager, this.constructor.config?.name || 'BowWeapon');
    this.onAttach();
    return this;
  }
  detach() {
    if (this.owner) this.onDetach();
    this.owner = null;
    this.activeProjectiles.clear();
    return this;
  }
  onAttach() {}
  onDetach() {}
  getTargetList() {
    const manager = this.owner && (this.owner.enemyManager || this.owner.battle?.enemyManager);
    return manager && manager.enemies
      ? Array.from(manager.enemies.values()).filter(enemy => enemy && enemy.currentState !== 4 && enemy.targetable !== false)
      : [];
  }
  selectTarget() { return this.getTargetList()[0] || null; }
  _ownerCenter() {
    const node = this.owner && (this.owner.displayObject || this.owner.Oc || this.owner.visual);
    if (node) return { x: node.x + (node.width || 0) / 2, y: node.y + (node.height || 0) / 2 };
    return { x: Number(this.owner?.x) || 0, y: Number(this.owner?.y) || 0 };
  }
  createProjectile(type, target, extra = {}) {
    const manager = this.owner && (this.owner.projectileManager || this.owner.battle?.projectileManager);
    if (!manager || !target) return null;
    const targetId = Number(target.id);
    const movement = extra.movement || TargetEnemyBezierMovement.create({
      enemyManager: manager.enemyManager,
      gameData: manager.gameData,
      curveHeight: Number(extra.curveHeight == null ? 50 : extra.curveHeight),
      distanceScaling: extra.distanceScaling !== false,
      smoothRotation: Boolean(extra.smoothRotation),
      hitRadiusEnabled: extra.hitRadiusEnabled !== false,
    }).setTargetId(targetId);
    const hitStrategy = extra.hitStrategy || HitEnemyStrategy.create({
      targetId,
      delayMs: Number(extra.hitDelayMs) || 0,
      removeAfterHit: extra.removeAfterHit !== false,
      triggerMode: extra.triggerMode || 'requestRemove',
    });
    const visual = extra.visual || {};
    const appearance = extra.appearance || {
      label: visual.label || `${this.constructor.config?.name || 'Bow'} projectile`,
      resourcePath: visual.resourcePath || visual.image || '',
      size: visual.size || null,
      scale: visual.scale || null,
      anchor: visual.anchor || null,
    };
    const projectile = manager.create({
      ...extra,
      type,
      appearance,
      attacker: this.owner,
      damage: extra.damage == null ? (this.owner.attackPower || this.owner.attackDamage || 0) : extra.damage,
      speedScale: extra.speedScale == null ? 1 : extra.speedScale,
      hitStrategy,
      movement,
    }, this._ownerCenter());
    projectile.fire();
    this.activeProjectiles.add(projectile);
    return projectile;
  }
  attack(target) {
    const selected = target || this.selectTarget();
    if (!selected) return null;
    this.attackCount += 1;
    return this.performAttack(selected);
  }
  performAttack() { throw new Error(`${this.constructor.name}.performAttack not implemented`); }
  update() { return false; }
  gameOver() { this.detach(); }
}
module.exports = BowWeaponBase;
