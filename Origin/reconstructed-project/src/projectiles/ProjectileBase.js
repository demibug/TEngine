'use strict';

const { BuffType } = require('../buffs/BuffTypes');

const ProjectileEvents = Object.freeze({
  FIRED: 'projectile-fired',
  HIT: 'projectile-hit',
  REQUEST_REMOVE: 'projectile-request-remove',
  REMOVED: 'projectile-removed',
  HIT_FINISHED: 'projectile-hit-finished',
  RECOVERED: 'projectile-recovered',
});

/**
 * 重建模块：BOW-PROJECTILE-COMBAT-01 / ProjectileBase
 * 原始范围：bundle.strings-decoded.js:27874-28290
 * 原始符号：qY
 * 重建状态：COMPLETE_FOR_SIMPLE_DYNAMIC_ARROW
 *
 * qY 是逻辑对象；eS 是独立的 Laya 表现节点。两者由 ProjectileFactory
 * 作为一个复合池条目回收，符合原 `bullet_pool_<type>_<label>` 语义。
 */
class ProjectileBase {
  constructor(appearanceLabel = '') {
    this.appearanceLabel = appearanceLabel || '';
    this.projectileId = -1;
    this.renderNode = null;
    this.attacker = null;
    this.damageValue = 0;
    this.explicitDamage = false;
    this.speedScale = 1;
    this.rotationEnabled = true;
    this.hitEnabled = true;
    this.active = false;
    this.requestedRemoval = false;
    this.immediateRemoval = false;
    this.recovered = false;
    this.invalidReset = false;
    this.removeDelayMs = 0;
    this.removeDelayRemainingMs = 0;
    this.movement = null;
    this.hitStrategy = null;
    this.impact = null;
    this.buffManager = null;
    this.hitEnemyIds = new Set();
    this.startPosition = { x: 0, y: 0 };
    this.lifecycleToken = 0;
    this.manager = null;
    this._configured = false;
  }

  configure({ laya, enemyManager, gameData, effects, logger = console } = {}) {
    if (!laya || !enemyManager || !gameData || !effects) {
      throw new TypeError('ProjectileBase requires laya, enemyManager, gameData and effects');
    }
    Object.assign(this, { laya, enemyManager, gameData, effects, logger });
    this._configured = true;
    return this;
  }

  initialize(renderNode) {
    if (!renderNode) throw new TypeError('ProjectileBase.initialize requires renderNode');
    this.renderNode = renderNode;
    this.hitStrategy = null;
    this.initializeVisual();
    return this;
  }

  initializeVisual() {}
  initializeAppearance() {}
  onReset() { return undefined; }
  onUpdate() {}
  onFire() {}
  onHitFinished() {}
  onRequestRemove() {}
  onRecover() {}
  onDispose() {}
  canHit() { return true; }

  resetData(config = {}) {
    if (!this._configured) throw new Error(`${this.constructor.name}.configure() must run before resetData()`);
    this.lifecycleToken += 1;
    this.invalidReset = false;
    this.recovered = false;
    this.requestedRemoval = false;
    this.immediateRemoval = false;
    this.active = false;
    this.hitEnabled = true;
    this.attacker = config.attacker;
    this.explicitDamage = Object.prototype.hasOwnProperty.call(config, 'damage');
    if (this.explicitDamage) this.damageValue = config.damage;
    this.speedScale = config.speedScale == null ? 1 : config.speedScale;
    this.hitStrategy = config.hitStrategy || null;
    this.impact = config.impact || null;
    this.buffManager = config.buffManager || this.buffManager || null;
    this.removeDelayMs = config.removeDelayMs == null ? 0 : config.removeDelayMs;
    this.removeDelayRemainingMs = this.removeDelayMs;
    this.manager = config.manager || this.manager;

    if (this.onReset(config) === false) {
      this.invalidReset = true;
      this.requestRemove(true);
      return this;
    }

    if (!config.movement) throw new TypeError('Projectile reset requires movement strategy');
    this.movement = config.movement;
    this.movement.attach(this);
    return this;
  }

  fire() {
    if (this.active || this.recovered || this.invalidReset || this.requestedRemoval) return this;
    this.active = true;
    this.onFire();
    this.renderNode.event(ProjectileEvents.FIRED);
    this.startPosition.x = this.renderNode.x;
    this.startPosition.y = this.renderNode.y;
    this.movement.onFire();
    return this;
  }

  update(deltaMs) { this.onUpdate(deltaMs); }

  hit(enemy) {
    if (!enemy || this.hitEnemyIds.has(enemy.id) || !this.canHit(enemy)) return false;
    this.renderNode.event(ProjectileEvents.HIT, enemy.id);
    const applied = this.applyHit(enemy);
    this.hitEnemyIds.add(enemy.id);
    return applied;
  }

  applyHit() { throw new Error('ProjectileBase.applyHit() must be implemented'); }

  /** Apply weapon-specific logic after the primary projectile hit. */
  applyImpactEffects(primaryTarget) {
    const impact=this.impact;
    if(!impact)return { applied:false, effects:[] };
    const effects=[];
    const primaryDamage=this.damage;
    const targetId=primaryTarget?.id;
    if(impact.burn && this.buffManager && primaryTarget?.id!=null){
      const burn=impact.burn;
      const buffId=this.buffManager.applyBuff(primaryTarget.id,BuffType.BURN_STATIC,
        Number(burn.damage==null?primaryDamage:burn.damage),false,Number(burn.durationMs||5000));
      effects.push({type:'burn',buffId,targetId});
    }
    if(impact.knockback && this.buffManager && primaryTarget?.id!=null){
      const knockback=impact.knockback;
      const from=this.attacker?.combatCenter || {x:Number(this.attacker?.x)||0,y:Number(this.attacker?.y)||0};
      const to={x:Number(primaryTarget.x)||0,y:Number(primaryTarget.y)||0};
      const dx=to.x-from.x,dy=to.y-from.y,length=Math.sqrt(dx*dx+dy*dy)||1;
      const vector={x:dx/length*(Number(knockback.magnitude)||1),y:dy/length*(Number(knockback.magnitude)||1)};
      const buffId=this.buffManager.applyBuff(primaryTarget.id,BuffType.KNOCKBACK,null,false,Number(knockback.durationMs||500),vector);
      effects.push({type:'knockback',buffId,targetId,vector});
    }
    if(impact.area && this.enemyManager){
      const area=impact.area;
      const center={x:Number(primaryTarget?.x)||this.x,y:Number(primaryTarget?.y)||this.y};
      const query=this.enemyManager.queryEnemyObjects || this.enemyManager.queryTargets;
      const candidates=typeof query==='function'
        ? query.call(this.enemyManager,center.x,center.y,Number(area.radius||0),this.attacker?.side,[])
        : [];
      for(const target of Array.isArray(candidates)?candidates:[]){
        if(!target || target.id===targetId || target.currentState===4 || target.targetable===false)continue;
        const damage=primaryDamage*Number(area.damageMultiplier==null?1:area.damageMultiplier);
        const applied=typeof target.hit==='function'?target.hit(damage,this.attacker):target.takeDamage?.(damage,this.attacker);
        effects.push({type:'area',targetId:target.id,damage,applied});
      }
    }
    if(impact.ricochet && this.enemyManager){
      const ricochet=impact.ricochet;
      const random=typeof ricochet.random==='function'?ricochet.random:Math.random;
      if(random()<Number(ricochet.chance==null ? .5 : ricochet.chance)){
        const query=this.enemyManager.queryTargets;
        const candidates=typeof query==='function'
          ? query.call(this.enemyManager,Number(primaryTarget?.x)||this.x,Number(primaryTarget?.y)||this.y,Number(ricochet.radius||Infinity),this.attacker?.side)
          : [];
        const available=(Array.isArray(candidates)?candidates:[]).filter(target=>target&&target.id!==targetId&&target.currentState!==4&&target.targetable!==false);
        const maxTargets=Math.max(0,Number(ricochet.maxTargets==null?1:ricochet.maxTargets));
        for(let index=0;index<Math.min(maxTargets,available.length);index+=1){
          const target=available[index];
          const damage=primaryDamage*Number(ricochet.damageMultiplier==null?1:ricochet.damageMultiplier);
          const applied=typeof target.hit==='function'?target.hit(damage,this.attacker):target.takeDamage?.(damage,this.attacker);
          effects.push({type:'ricochet',targetId:target.id,damage,applied});
        }
      }
    }
    return { applied:effects.length>0, effects };
  }

  finishHit() {
    this.onHitFinished();
    this.renderNode.event(ProjectileEvents.HIT_FINISHED);
  }

  notifyRequestRemove() {
    this.onRequestRemove();
    this.renderNode.event(ProjectileEvents.REQUEST_REMOVE);
  }

  requestRemove(immediate = false) {
    if (this.recovered) return;
    this.immediateRemoval = Boolean(immediate);
    this.requestedRemoval = true;
  }

  hide() { this.renderNode.visible = false; }
  show() { this.renderNode.visible = true; }
  pos(x, y) { this.renderNode.pos(x, y); return this; }
  get x() { return this.renderNode.x; }
  get y() { return this.renderNode.y; }
  set rotation(value) { this.renderNode.rotation = value; }
  get rotation() { return this.renderNode.rotation; }
  get damage() {
    if (!this.explicitDamage) this.damageValue = this.attacker ? this.attacker.attackDamage : 0;
    return this.damageValue;
  }

  recover() {
    if (this.recovered) return false;
    this.onRecover();
    this.recovered = true;
    this.active = false;
    this.hitEnabled = true;
    this.requestedRemoval = false;
    this.immediateRemoval = false;
    this.invalidReset = false;
    if (this.movement) this.movement.recover();
    this.movement = null;
    this.hitEnemyIds.clear();
    if (this.hitStrategy && typeof this.hitStrategy.recover === 'function') this.hitStrategy.recover();
    this.hitStrategy = null;
    this.impact = null;
    this.buffManager = null;
    this.attacker = null;
    this.damageValue = 0;
    this.explicitDamage = false;
    this.speedScale = 1;
    this.removeDelayMs = 0;
    this.removeDelayRemainingMs = 0;
    this.manager = null;
    this.projectileId = -1;
    this.startPosition.x = this.startPosition.y = 0;
    this.show();
    this.renderNode.event(ProjectileEvents.RECOVERED);
    this.renderNode.offAll();
    return true;
  }

  gameOver() { this.onDispose(); }
}

ProjectileBase.projectileTypeKey = '';

module.exports = { ProjectileBase, ProjectileEvents };
