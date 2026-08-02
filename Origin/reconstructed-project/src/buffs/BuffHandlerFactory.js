'use strict';
const { BuffType, BuffName } = require('./BuffTypes');
const { BuffKind, BuffDefinitions } = require('./BuffDefinitions');
const { NumberBuffHandler } = require('./NumberBuffHandler');
const { CustomBuffHandler } = require('./CustomBuffHandler');
const { BurnStaticBuffHandler } = require('./handlers/BurnStaticBuffHandler');
const { KnockbackBuffHandler } = require('./handlers/KnockbackBuffHandler');
const states = require('./handlers/TimedStateBuffHandlers');

class BuffHandlerFactory {
  constructor({ objectPool = null } = {}) {
    this.objectPool = objectPool;
    this.registry = new Map([
      [BuffType.ATTACK_POWER, NumberBuffHandler], [BuffType.ATTACK_SPEED, NumberBuffHandler], [BuffType.ATTACK_RANGE, NumberBuffHandler],
      [BuffType.MOVE_SPEED, NumberBuffHandler], [BuffType.MAX_HP, NumberBuffHandler], [BuffType.HP, NumberBuffHandler], [BuffType.SCALE, NumberBuffHandler],
      [BuffType.CUSTOM, CustomBuffHandler], [BuffType.STUN, states.StunBuffHandler], [BuffType.FALL, states.FallBuffHandler],
      [BuffType.PIERCE, states.PierceBuffHandler], [BuffType.ELECTROCUTE, states.ElectrocuteBuffHandler], [BuffType.KNOCKBACK, KnockbackBuffHandler],
      [BuffType.CHAOS, states.ChaosBuffHandler], [BuffType.BURN_STATIC, BurnStaticBuffHandler], [BuffType.LIMIT, states.LimitBuffHandler],
      [BuffType.LOCK, states.LockBuffHandler], [BuffType.KNOCKDOWN, states.KnockdownBuffHandler], [BuffType.SUPPRESSION, states.SuppressionBuffHandler],
      [BuffType.CHARM, states.CharmBuffHandler],
    ]);
    this.validate();
  }
  validate() {
    for (const [type, definition] of BuffDefinitions) {
      const ClassType = this.registry.get(type);
      if (!ClassType) throw new Error(`[BuffHandlerRegistry] BuffDefinitions declares ${BuffName[type]}, but no producer is registered`);
      const isNumber = ClassType === NumberBuffHandler;
      if (definition.kind === BuffKind.NUMBER && !isNumber) throw new Error(`[BuffHandlerRegistry] Buff.${BuffName[type]} must use NumberBuffHandler`);
      if (definition.kind !== BuffKind.NUMBER && isNumber) throw new Error(`[BuffHandlerRegistry] Buff.${BuffName[type]} cannot use NumberBuffHandler`);
      if (definition.kind === BuffKind.CUSTOM && ClassType !== CustomBuffHandler) throw new Error('[BuffHandlerRegistry] Buff.custom must use CustomBuffHandler');
    }
    for (const type of this.registry.keys()) if (!BuffDefinitions.has(type)) throw new Error(`[BuffHandlerRegistry] producer ${type} has no definition`);
  }
  create(type) {
    const ClassType = this.registry.get(Number(type));
    if (!ClassType) throw new Error(`[BuffHandlerRegistry] Buff(ID:${type} / ${BuffName[type] || 'unknown'}) is not implemented`);
    return this.objectPool ? this.objectPool.takeByClass(ClassType) : new ClassType();
  }
  recover(handler) { if (this.objectPool) this.objectPool.recoverByClass(handler); }
  keys() { return [...this.registry.keys()].sort((a,b)=>a-b); }
}
module.exports = { BuffHandlerFactory };
