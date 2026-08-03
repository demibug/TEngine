'use strict';
const { EffectHandle } = require('./EffectHandle');
const { ProjectileAttackEffect } = require('../../combat/ProjectileAttackEffect');
const { queryEnemyObjects } = require('./effectTargets');

/**
 * 火箭烈（FireArrowBarrage，bundle:46145，qu 类 45704-45898，_F 方法 45726-45890）。
 * 多重数 n = floor(max(1, (level-1)/2))（bundle:45744，Math.max(1,…) 下限钳制，最小 1）；
 * 每个目标加箭 k = range(1,3,true) * n（bundle:45746，整型乘数）；tS:"fireArrowRain"；音效 general_fire_arrow_rain（45877）。
 * 火焰箭专属弹种待提案 ④，本 effect 先用通用箭发射并在 metadata 标记 DEFERRED_PROJECTILE_VARIANT，不阻塞触发与计数。
 * range(1,3,true) 默认实现返回整数 1 或 2（exclusive max）；测试可注入确定性 rangeFn。
 */
class FireArrowBarrageEffect {
  constructor({ enemyManager, projectileManager, attackEffectManager, audioRegistry, logger = console, rangeFn = null } = {}) {
    Object.assign(this, { enemyManager, projectileManager, attackEffectManager, audioRegistry, logger });
    this.rangeFn = rangeFn || ((min, max) => Math.floor(Math.random() * (max - min)) + min); // 整型 [min,max)
    this.defaultRange = null; // null → 回退到 owner.attackRange
  }

  /** 多重数 n = floor(max(1, (level-1)/2))；取证 bundle:45744。 */
  static multiCount(level) {
    const lv = Number(level || 1);
    return Math.floor(Math.max(1, (lv - 1) / 2));
  }

  execute({ owner, level, range, rangeFn, projectileManager, attackEffectManager } = {}) {
    if (!owner) return { status: 'MISSING_FIRE_ARROW_BARRAGE_OWNER' };
    const pm = projectileManager || this.projectileManager || owner.projectileManager;
    if (!pm || typeof pm.create !== 'function') {
      return { status: 'MISSING_PROJECTILE_MANAGER', ownerId: owner.id };
    }
    const mgr = attackEffectManager || this.attackEffectManager || owner.attackEffectManager;
    const center = owner.combatCenter || { x: 0, y: 0 };
    const r = Number(range != null ? range : (this.defaultRange != null ? this.defaultRange : owner.attackRange));
    const lv = Number(level != null ? level : (owner.level != null ? owner.level : (owner.stats && owner.stats.level) || 1));
    const n = FireArrowBarrageEffect.multiCount(lv);
    const rf = rangeFn || this.rangeFn;
    const targets = this.enemyManager ? queryEnemyObjects(this.enemyManager, center.x, center.y, r, owner.side) : [];
    const damage = Number(owner.attackDamage || 0);
    const launched = [];
    const perTarget = [];
    for (const target of targets) {
      const k = rf(1, 3) * n; // bundle:45746 range(1,3,true)*n
      perTarget.push({ id: target.id, n, k });
      const tx = target.x != null ? target.x : (target.visual && target.visual.x) || center.x;
      const ty = target.y != null ? target.y : (target.visual && target.visual.y) || center.y;
      for (let b = 0; b < k; b++) {
        const startPoint = { x: tx, y: ty - 120 };
        const config = {
          attacker: owner,
          type: 'fireArrowRain', // bundle:44826 tS:"fireArrowRain"
          skin: 'resources/img/weapon/arrow_2.png',
          damage,
          targetId: target.id,
          flameVariant: false, // 火焰箭专属弹种待提案 ④
        };
        let lifecycle = null;
        try {
          lifecycle = new ProjectileAttackEffect().launch({ owner, projectileManager: pm, config, startPoint });
        } catch (err) {
          if (this.logger) this.logger.warn && this.logger.warn('FireArrowBarrage launch failed:', err && err.message);
        }
        if (lifecycle) {
          if (mgr && typeof mgr.add === 'function') mgr.add(lifecycle);
          launched.push(lifecycle);
        }
      }
    }
    // bundle:45877 playSound("general_fire_arrow_rain")
    if (this.audioRegistry && this.audioRegistry.play) this.audioRegistry.play('general_fire_arrow_rain', { ownerId: owner.id });
    return new EffectHandle({
      ownerId: owner.id,
      persistent: true,
      metadata: {
        launched,
        count: launched.length,
        level: lv,
        multiCount: n,
        perTarget,
        DEFERRED_PROJECTILE_VARIANT: true, // 火焰箭弹种待提案 ④
      },
      update: () => {},
      dispose: (reason) => {
        for (const lc of launched) {
          if (lc && typeof lc.cleanup === 'function') {
            try { lc.cleanup(reason); } catch (e) { /* 忽略单支清理失败 */ }
          }
        }
      },
    });
  }
}

module.exports = { FireArrowBarrageEffect };
