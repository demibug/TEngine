'use strict';
const { BuffType } = require('../../buffs/BuffTypes');
const { EffectHandle } = require('./EffectHandle');
const { queryEnemyObjects } = require('./effectTargets');

/**
 * 战吼（BattleShout，bundle:45659）。
 * 大喝一声砸晕一圈敌人，晕眩 2 秒（2000ms）。范围半径 bundle 未明示数值（"一圈"语义），
 * 由构造默认值或 execute 入参承载，默认取武将攻击范围。纯逻辑：表现/音效经可选 presentation/audioRegistry 注入。
 */
class BattleShoutEffect {
  constructor({ enemyManager, buffManager, presentation, audioRegistry, logger = console } = {}) {
    Object.assign(this, { enemyManager, buffManager, presentation, audioRegistry, logger });
    this.stunMs = 2000; // bundle:45659 "晕眩2秒"
    this.defaultRadius = null; // null → 回退到 owner.attackRange
  }

  execute({ owner, radius, stunMs } = {}) {
    if (!owner || !this.enemyManager || !this.buffManager) {
      return { status: 'MISSING_BATTLE_SHOUT_DEPENDENCY' };
    }
    const center = owner.combatCenter || { x: 0, y: 0 };
    const r = Number(radius != null ? radius : (this.defaultRadius != null ? this.defaultRadius : owner.attackRange));
    const side = owner.side;
    const durationMs = Number(stunMs != null ? stunMs : this.stunMs);
    const targets = queryEnemyObjects(this.enemyManager, center.x, center.y, r, side);
    const stunned = [];
    for (const target of targets) {
      if (!target || target.id == null) continue;
      const buffId = this.buffManager.applyBuff(target.id, BuffType.STUN, 1, false, durationMs, { source: 'BattleShout' });
      stunned.push({ id: target.id, buffId });
    }
    // bundle:45659 段未明示战吼音效 key；若 audioRegistry 与 key 就绪则播放，空时跳过。
    if (this.audioRegistry && this.audioRegistry.play) this.audioRegistry.play('battleShout_skill', { ownerId: owner.id });
    return new EffectHandle({
      ownerId: owner.id,
      persistent: false,
      metadata: { stunned, count: stunned.length, radius: r, durationMs },
      dispose: () => {},
    });
  }
}

module.exports = { BattleShoutEffect };
