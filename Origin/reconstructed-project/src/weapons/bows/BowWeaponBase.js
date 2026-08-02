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
    this.buffManager = buffManager || owner?.buffManager || null;
    this.buffPort = new WeaponBuffPort(this.buffManager, this.constructor.config?.name || 'BowWeapon');
    this.onAttach();
    return this;
  }
  detach() {
    if (this.owner) this.onDetach();
    this.owner = null;
    this.buffManager = null;
    this.activeProjectiles.clear();
    return this;
  }
  onAttach() {}
  onDetach() {}
  getTargetList() {
    const manager = this.owner && (this.owner.enemyManager || this.owner.battle?.enemyManager);
    if (!manager) return [];
    if (manager.enemies) return Array.from(manager.enemies.values()).filter(enemy => enemy && enemy.currentState !== 4 && enemy.targetable !== false);
    const center=this._ownerCenter();
    return typeof manager.queryTargets === 'function' ? manager.queryTargets(center.x,center.y,Number(this.owner?.attackRange)||Infinity,this.owner?.side) : [];
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
    const baseDamage = extra.damage == null ? (this.owner.attackPower || this.owner.attackDamage || 0) : extra.damage;
    const damageMultiplier = Number(extra.damageMultiplier == null ? 1 : extra.damageMultiplier);
    const ownerCenter=this._ownerCenter();
    const targetCenter={x:(Number(target.x)||0)+(Number(target.width)||0)/2,y:(Number(target.y)||0)+(Number(target.height)||0)/2};
    const distance=Math.hypot(targetCenter.x-ownerCenter.x,targetCenter.y-ownerCenter.y);
    const distanceMultiplier=extra.distanceScale ? Math.max(1,distance/Math.max(Number(this.owner.attackRange)||1,1)) : 1;
    const speedScale = Number(extra.speedScale == null ? 1 : extra.speedScale)
      * Number(extra.speedMultiplier == null ? 1 : extra.speedMultiplier);
    const projectile = manager.create({
      ...extra,
      type,
      appearance,
      attacker: this.owner,
      damage: Number(baseDamage) * (Number.isFinite(damageMultiplier) ? damageMultiplier : 1) * distanceMultiplier,
      speedScale: Number.isFinite(speedScale) ? speedScale : 1,
      buffManager: this.buffPort.manager,
      hitStrategy,
      movement,
    }, this._ownerCenter());
    projectile.fire();
    this.activeProjectiles.add(projectile);
    return projectile;
  }
  attack(input) {
    const context = input && input.target ? input : { target: input };
    const selected = context.target || this.selectTarget();
    if (!selected) return null;
    this.attackCount += 1;
    return this.performAttack(selected, context);
  }
  performAttack() { throw new Error(`${this.constructor.name}.performAttack not implemented`); }
  update() { return false; }
  gameOver() { this.detach(); }
}
module.exports = BowWeaponBase;
