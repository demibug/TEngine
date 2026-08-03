'use strict';
const { BuffType } = require('../../buffs/BuffTypes');
const { EffectHandle } = require('./EffectHandle');
const { queryEnemyObjects, applyDamageToObjects } = require('./effectTargets');

/**
 * 圣剑（HolySword，bundle:45902，case10 类 45663-45702）。
 * Nx 方法（45687）调 iI.FC+iI.attack 造成范围伤害；XU 方法（45696）播 holyBlade_skill 音效。
 * 范围伤害对范围内敌人结算并施加击倒（KNOCKDOWN）。击倒时长与范围半径 bundle 经 iI 委托、未明示数值，
 * 由构造默认值或 execute 入参承载。纯逻辑：表现/音效经可选 presentation/audioRegistry 注入。
 */
class HolySwordEffect {
  constructor({ enemyManager, buffManager, presentation, audioRegistry, logger = console } = {}) {
    Object.assign(this, { enemyManager, buffManager, presentation, audioRegistry, logger });
    this.knockdownMs = 2000; // bundle 未明示，可注入默认（与晕眩对齐）
    this.defaultRadius = null; // null → 回退到 owner.attackRange
  }

  execute({ owner, radius, knockdownMs, damage } = {}) {
    if (!owner || !this.enemyManager || !this.buffManager) {
      return { status: 'MISSING_HOLY_SWORD_DEPENDENCY' };
    }
    const center = owner.combatCenter || { x: 0, y: 0 };
    const r = Number(radius != null ? radius : (this.defaultRadius != null ? this.defaultRadius : owner.attackRange));
    const side = owner.side;
    const dmg = Number(damage != null ? damage : (owner.attackDamage || 0));
    const kdMs = Number(knockdownMs != null ? knockdownMs : this.knockdownMs);
    const targets = queryEnemyObjects(this.enemyManager, center.x, center.y, r, side);
    const hitIds = [];
    const knocked = [];
    for (const target of targets) {
      if (!target || target.id == null) continue;
      if (typeof target.hit === 'function') target.hit(dmg, owner);
      else if (typeof target.takeDamage === 'function') target.takeDamage(dmg, owner);
      const buffId = this.buffManager.applyBuff(target.id, BuffType.KNOCKDOWN, 1, false, kdMs, { source: 'HolySword' });
      hitIds.push(target.id);
      knocked.push({ id: target.id, buffId });
    }
    // bundle:45696 "圣剑"==a && playSound("holyBlade_skill")
    if (this.audioRegistry && this.audioRegistry.play) this.audioRegistry.play('holyBlade_skill', { ownerId: owner.id });
    return new EffectHandle({
      ownerId: owner.id,
      persistent: false,
      metadata: { hit: hitIds, knocked, count: hitIds.length, damage: dmg, radius: r, knockdownMs: kdMs },
      dispose: () => {},
    });
  }
}

module.exports = { HolySwordEffect };
