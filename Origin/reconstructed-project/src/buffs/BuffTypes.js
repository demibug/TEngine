'use strict';

const BuffType = Object.freeze({
  ATTACK_POWER: 0,
  ATTACK_SPEED: 1,
  ATTACK_RANGE: 2,
  MOVE_SPEED: 3,
  MAX_HP: 4,
  HP: 5,
  SCALE: 6,
  CUSTOM: 7,
  STUN: 8,
  FALL: 9,
  PIERCE: 10,
  ELECTROCUTE: 11,
  KNOCKBACK: 12,
  CHAOS: 13,
  BURN_STATIC: 14,
  LIMIT: 15,
  LOCK: 16,
  KNOCKDOWN: 17,
  SUPPRESSION: 18,
  CHARM: 19,
});

const BuffName = Object.freeze({
  0: 'attPower', 1: 'attSpeed', 2: 'attRange', 3: 'moveSpeed', 4: 'maxHp',
  5: 'hp', 6: 'scale', 7: 'custom', 8: 'stun', 9: 'fall', 10: 'pierce',
  11: 'electrocute', 12: 'knockback', 13: 'chaos', 14: 'burnStatic',
  15: 'limit', 16: 'lock', 17: 'knockdown', 18: 'suppression', 19: 'charm',
});

module.exports = { BuffType, BuffName };
