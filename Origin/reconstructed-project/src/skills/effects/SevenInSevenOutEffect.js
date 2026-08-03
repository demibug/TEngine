'use strict';
const { EffectHandle } = require('./EffectHandle');
const { queryEnemyObjects } = require('./effectTargets');

/**
 * 七进七出（SevenInSevenOut，bundle:45655）。
 * 释放幻象冲进敌群来回突进七次（tF.skillName 描述"来回突进七次"）。本 effect 为 persistent，
 * 由 update(dt) 按突进间隔驱动 7 次突进计数，每次对范围内敌人结算路径伤害。
 * 幻象视觉表现为非目标（P2），经 presentation.createEntityVfx 注入（桩返 null 不影响结算）。
 * 纯逻辑：无表现桩时仍完成 7 次突进计数与伤害结算。
 */
class SevenInSevenOutEffect {
  constructor({ enemyManager, presentation, audioRegistry, logger = console } = {}) {
    Object.assign(this, { enemyManager, presentation, audioRegistry, logger });
    this.dashCount = 7; // bundle:45655
    this.dashIntervalMs = 200; // bundle 时序由 hU/hP 驱动，src 模型用可注入间隔
    this.defaultRadius = null; // null → 回退到 owner.attackRange
  }

  execute({ owner, dashCount, dashIntervalMs, dashDamage, radius } = {}) {
    if (!owner || !this.enemyManager) return { status: 'MISSING_SEVEN_IN_SEVEN_OUT_DEPENDENCY' };
    this.owner = owner;
    this.ownerId = owner.id;
    this.totalDashes = Number(dashCount != null ? dashCount : this.dashCount);
    this.dashIntervalMs = Number(dashIntervalMs != null ? dashIntervalMs : this.dashIntervalMs);
    this.radius = Number(radius != null ? radius : (this.defaultRadius != null ? this.defaultRadius : owner.attackRange));
    this.dashDamage = dashDamage != null ? Number(dashDamage) : Number(owner.attackDamage || 0);
    this.elapsedMs = 0;
    this.dashesDone = 0;
    const self = this;
    const handle = new EffectHandle({
      ownerId: owner.id,
      persistent: true,
      metadata: { dashCount: this.totalDashes, dashesDone: 0 },
      update: (dt) => self._update(dt, handle),
      dispose: (reason) => self._dispose(reason),
    });
    this.handle = handle;
    return handle;
  }

  _update(deltaMs, handle) {
    if (!handle || handle.disposed) return;
    this.elapsedMs += Number(deltaMs || 0);
    while (this.dashesDone < this.totalDashes && this.elapsedMs >= this.dashIntervalMs) {
      this.elapsedMs -= this.dashIntervalMs;
      this.dashesDone += 1;
      this._doDash();
      if (handle.metadata) handle.metadata.dashesDone = this.dashesDone;
    }
    if (this.dashesDone >= this.totalDashes && !handle.disposed) handle.dispose('dash-complete');
  }

  _doDash() {
    const owner = this.owner;
    if (!owner) return;
    const center = owner.combatCenter || { x: 0, y: 0 };
    const enemies = queryEnemyObjects(this.enemyManager, center.x, center.y, this.radius, owner.side);
    for (const enemy of enemies) {
      if (!enemy) continue;
      if (typeof enemy.hit === 'function') enemy.hit(this.dashDamage, owner);
      else if (typeof enemy.takeDamage === 'function') enemy.takeDamage(this.dashDamage, owner);
    }
    // 幻象表现：桩实现 createEntityVfx 返回 null，不影响结算
    if (this.presentation && typeof this.presentation.createEntityVfx === 'function') {
      try { this.presentation.createEntityVfx(owner, 'seven-in-seven-out-dash'); } catch (e) { /* 忽略表现失败 */ }
    }
  }

  _dispose(reason) {
    this.owner = null;
  }
}

module.exports = { SevenInSevenOutEffect };
