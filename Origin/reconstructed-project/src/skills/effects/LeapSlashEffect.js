'use strict';
const { BuffType } = require('../../buffs/BuffTypes');
const { BuffTimeMode } = require('../../buffs/BuffTimeMode');
const { EffectHandle } = require('./EffectHandle');
const { queryEnemyObjects } = require('./effectTargets');

/**
 * 跳斩（LeapSlash，bundle:38497，nV 类 45942）。
 * 激活后施加攻速 buff（bundle init: applyBuff(id,ATTACK_SPEED,0,true)）、播 guanYu_skill_roar（45983）；
 * 接下来 5 次攻击（new qP(hu[1],5)、gT=5）每次对周围附加 50% 溅射伤害，5 次后停止。
 * 持续窗口由"剩余攻击次数"驱动：每次武将攻击经 SkillEffectPort.onOwnerAttack 通知本 effect 结算溅射并递减。
 * 纯逻辑：表现/音效经可选 presentation/audioRegistry 注入。
 */
class LeapSlashEffect {
  constructor({ enemyManager, buffManager, audioRegistry, logger = console } = {}) {
    Object.assign(this, { enemyManager, buffManager, audioRegistry, logger });
    this.splashCount = 5; // bundle:45942 new qP(hu[1],5) / gT=5
    this.splashRatio = 0.5; // bundle:38497 50% 溅射
    this.defaultSplashRadius = null; // null → 回退到 owner.attackRange
  }

  execute({ owner, splashCount, splashRadius } = {}) {
    if (!owner || !this.enemyManager) return { status: 'MISSING_LEAP_SLASH_DEPENDENCY' };
    this.owner = owner;
    this.ownerId = owner.id;
    this.remaining = Number(splashCount != null ? splashCount : this.splashCount);
    this.splashRadius = Number(splashRadius != null ? splashRadius : (this.defaultSplashRadius != null ? this.defaultSplashRadius : owner.attackRange));
    // bundle:45947 applyBuff(id, ATTACK_SPEED, 0, stacked=true) —— 施加攻速 buff 标记（ramp 由原始 iI 驱动，纯逻辑层仅留标记）
    this.buffId = this.buffManager ? this.buffManager.applyBuff(owner.id, BuffType.ATTACK_SPEED, 0, true, BuffTimeMode.PERMANENT, { source: 'LeapSlash' }) : null;
    // bundle:45983 "跳斩"==a && playSound("guanYu_skill_roar")
    if (this.audioRegistry && this.audioRegistry.play) this.audioRegistry.play('guanYu_skill_roar', { ownerId: owner.id });
    const self = this;
    const handle = new EffectHandle({
      ownerId: owner.id,
      persistent: true,
      metadata: { remaining: this.remaining, splashRatio: this.splashRatio, buffId: this.buffId },
      onOwnerAttack: (context) => self._onOwnerAttack(context, handle),
      dispose: (reason) => self._dispose(reason),
    });
    this.handle = handle;
    return handle;
  }

  _onOwnerAttack(context, handle) {
    if (!handle || handle.disposed || this.remaining <= 0) return;
    const owner = this.owner;
    if (!owner) return;
    const target = context && context.target;
    const center = target && target.x != null ? { x: target.x, y: target.y } : (owner.combatCenter || { x: 0, y: 0 });
    const enemies = queryEnemyObjects(this.enemyManager, center.x, center.y, this.splashRadius, owner.side);
    const baseDamage = Number((context && context.damage != null) ? context.damage : (owner.attackDamage || 0));
    const splash = this.splashRatio * baseDamage;
    let splashed = 0;
    for (const enemy of enemies) {
      if (!enemy || (target && enemy.id === target.id)) continue; // 不对主目标重复结算
      if (typeof enemy.hit === 'function') { enemy.hit(splash, owner); splashed++; }
      else if (typeof enemy.takeDamage === 'function') { enemy.takeDamage(splash, owner); splashed++; }
    }
    this.remaining -= 1;
    if (handle.metadata) { handle.metadata.remaining = this.remaining; handle.metadata.lastSplash = { count: splashed, damage: splash }; }
    if (this.remaining <= 0 && !handle.disposed) handle.dispose('splash-exhausted');
  }

  _dispose(reason) {
    if (this.buffManager && this.buffId != null && typeof this.buffManager.removeBuff === 'function') {
      try { this.buffManager.removeBuff(this.ownerId, BuffType.ATTACK_SPEED, this.buffId); } catch (e) { /* 忽略 */ }
    }
    this.owner = null;
  }
}

module.exports = { LeapSlashEffect };
