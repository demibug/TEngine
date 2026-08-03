'use strict';
const { EffectHandle } = require('./EffectHandle');
const { ProjectileAttackEffect } = require('../../combat/ProjectileAttackEffect');
const { queryEnemyObjects } = require('./effectTargets');

/**
 * 陨石打击（MeteorStrike，bundle:3541-3543 阿斗被动技能 "meteor"/"陨石"）。
 *
 * 取证偏差（DEFERRED）：bundle 原始实现（bundle:27450-27500 方法 EP）为纯 Laya.Image + Tween 视觉特效，
 * 使用 meteor_2/3.png 皮肤与 pit.png 落点，全程不走弹种工厂通道。StaticFireBall（bundle:34071）与
 * GroundSpikeBullet（bundle:35481）在 bundle 中仅注册、从未被实例化（孤儿弹种）。
 *
 * 本 effect 为纯逻辑层弹种化重建：将陨石触发连接到 StaticFireBall/GroundSpikeBullet 专属弹种实体，
 * 经 ProjectileFactory.produce 创建，登记 ProjectileManager/AttackEffectManager，完成创建→移动→命中→回收
 * 完整生命周期。触发条件（敌人接近阿斗）由调用方经 execute 入参承载；视觉特效资源为 P2 非目标。
 */
class MeteorStrikeEffect {
  constructor({ enemyManager, projectileManager, attackEffectManager, logger = console } = {}) {
    Object.assign(this, { enemyManager, projectileManager, attackEffectManager, logger });
    // DEFERRED: bundle 原始为纯特效，弹种化重建的触发参数以可注入默认值承载
    this.defaultCount = 3; // PARTIAL: bundle 未明示每次陨石数量
    this.defaultRange = null; // null → 回退到 owner.attackRange 或入参 range
  }

  execute({ owner, center, range, count, targets, projectileType = 'StaticFireBall', projectileManager, attackEffectManager } = {}) {
    if (!owner) return { status: 'MISSING_METEOR_OWNER' };
    const pm = projectileManager || this.projectileManager || owner.projectileManager;
    if (!pm || typeof pm.create !== 'function') {
      return { status: 'MISSING_PROJECTILE_MANAGER', ownerId: owner.id };
    }
    const mgr = attackEffectManager || this.attackEffectManager || owner.attackEffectManager;
    const origin = center || owner.combatCenter || { x: Number(owner.x) || 0, y: Number(owner.y) || 0 };
    const r = Number(range != null ? range : (this.defaultRange != null ? this.defaultRange : owner.attackRange) || 96);
    const total = Number(count != null ? count : this.defaultCount);
    const victims = targets && targets.length ? targets
      : (this.enemyManager ? queryEnemyObjects(this.enemyManager, origin.x, origin.y, r, owner.side) : []);
    const launched = [];
    const damage = Number(owner.attackDamage || 0);
    for (let i = 0; i < total; i += 1) {
      const target = victims.length ? victims[i % victims.length] : null;
      const tx = target ? (Number(target.x) || (target.visual && target.visual.x) || origin.x) : origin.x;
      const ty = target ? (Number(target.y) || (target.visual && target.visual.y) || origin.y) : origin.y;
      // 交替使用 StaticFireBall 与 GroundSpikeBullet，使两个孤儿弹种均有触发路径
      const typeKey = projectileType === 'GroundSpikeBullet' ? 'GroundSpikeBullet'
        : (i % 2 === 0 ? 'StaticFireBall' : 'GroundSpikeBullet');
      const startPoint = { x: tx, y: ty - 140 };
      const config = {
        attacker: owner,
        type: typeKey,
        appearance: { label: 'meteor-strike' },
        damage,
        targetId: target ? target.id : null,
      };
      let lifecycle = null;
      try {
        lifecycle = new ProjectileAttackEffect().launch({ owner, projectileManager: pm, config, startPoint });
      } catch (err) {
        if (this.logger) this.logger.warn && this.logger.warn('MeteorStrike launch failed:', err && err.message);
      }
      if (lifecycle) {
        if (mgr && typeof mgr.add === 'function') mgr.add(lifecycle);
        launched.push(lifecycle);
      }
    }
    return new EffectHandle({
      ownerId: owner.id,
      persistent: true,
      metadata: { launched, count: launched.length, requested: total, projectileTypes: ['StaticFireBall', 'GroundSpikeBullet'] },
      update: () => {},
      dispose: (reason) => {
        for (const lc of launched) {
          if (lc && typeof lc.cleanup === 'function') {
            try { lc.cleanup(reason); } catch (e) { /* 忽略单个清理失败 */ }
          }
        }
      },
    });
  }
}

module.exports = { MeteorStrikeEffect };
