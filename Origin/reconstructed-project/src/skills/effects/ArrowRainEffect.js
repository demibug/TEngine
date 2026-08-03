'use strict';
const { EffectHandle } = require('./EffectHandle');
const { ProjectileAttackEffect } = require('../../combat/ProjectileAttackEffect');
const { queryEnemyObjects } = require('./effectTargets');

/**
 * 箭雨（ArrowRain，bundle:46141）。
 * MF 方法（44798）按波次射出大量箭：每波 SF 支、最多 xF 波；tS:"arrowRain"、arrow_2.png；音效 general_arrow_rain（44748）。
 * 本 effect 负责"发射 N 支箭"的触发与计数，投射物实体复用 ProjectileAttackEffect/ProjectileManager，
 * 经 attackEffectManager 登记以随统一攻击效果管理器更新与回收。波次/每波箭数 bundle 未明示数值，由构造默认值或 execute 入参承载。
 */
class ArrowRainEffect {
  constructor({ enemyManager, projectileManager, attackEffectManager, audioRegistry, logger = console } = {}) {
    Object.assign(this, { enemyManager, projectileManager, attackEffectManager, audioRegistry, logger });
    this.defaultWaves = 3; // bundle SF/xF 未明示，可注入默认
    this.defaultArrowsPerWave = 5;
    this.defaultRange = null; // null → 回退到 owner.attackRange
  }

  execute({ owner, waves, arrowsPerWave, count, range, projectileManager, attackEffectManager } = {}) {
    if (!owner) return { status: 'MISSING_ARROW_RAIN_OWNER' };
    const pm = projectileManager || this.projectileManager || owner.projectileManager;
    if (!pm || typeof pm.create !== 'function') {
      return { status: 'MISSING_PROJECTILE_MANAGER', ownerId: owner.id };
    }
    const mgr = attackEffectManager || this.attackEffectManager || owner.attackEffectManager;
    const center = owner.combatCenter || { x: 0, y: 0 };
    const r = Number(range != null ? range : (this.defaultRange != null ? this.defaultRange : owner.attackRange));
    const wavesCount = Number(waves != null ? waves : this.defaultWaves);
    const perWave = Number(arrowsPerWave != null ? arrowsPerWave : this.defaultArrowsPerWave);
    const total = Number(count != null ? count : wavesCount * perWave);
    const targets = this.enemyManager ? queryEnemyObjects(this.enemyManager, center.x, center.y, r, owner.side) : [];
    const launched = [];
    const damage = Number(owner.attackDamage || 0);
    for (let i = 0; i < total; i++) {
      const target = targets.length ? targets[i % targets.length] : null;
      const tx = target ? (target.x != null ? target.x : (target.visual && target.visual.x) || center.x) : center.x;
      const ty = target ? (target.y != null ? target.y : (target.visual && target.visual.y) || center.y) : center.y;
      const startPoint = { x: tx, y: ty - 120 };
      const config = {
        attacker: owner,
        type: 'arrowRain',
        skin: 'resources/img/weapon/arrow_2.png', // bundle:44806
        damage,
        targetId: target ? target.id : null,
      };
      let lifecycle = null;
      try {
        lifecycle = new ProjectileAttackEffect().launch({ owner, projectileManager: pm, config, startPoint });
      } catch (err) {
        // 投射物创建失败不应阻塞整体计数；记录后继续。
        if (this.logger) this.logger.warn && this.logger.warn('ArrowRain launch failed:', err && err.message);
      }
      if (lifecycle) {
        if (mgr && typeof mgr.add === 'function') mgr.add(lifecycle);
        launched.push(lifecycle);
      }
    }
    // bundle:44748 playSound("general_arrow_rain")
    if (this.audioRegistry && this.audioRegistry.play) this.audioRegistry.play('general_arrow_rain', { ownerId: owner.id });
    return new EffectHandle({
      ownerId: owner.id,
      persistent: true,
      metadata: { launched, count: launched.length, requested: total, waves: wavesCount, arrowsPerWave: perWave },
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

module.exports = { ArrowRainEffect };
