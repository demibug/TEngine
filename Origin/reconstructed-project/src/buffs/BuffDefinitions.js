'use strict';
const { BuffType, BuffName } = require('./BuffTypes');

const BuffKind = Object.freeze({ NUMBER: 0, STATE: 1, CUSTOM: 2 });
const stateChannels = Object.freeze({
  [BuffType.STUN]: [1, 0],
  [BuffType.ELECTROCUTE]: [1, 0],
  [BuffType.KNOCKBACK]: [5],
  [BuffType.CHAOS]: [1, 0, 2],
  [BuffType.BURN_STATIC]: [4],
  [BuffType.LOCK]: [1, 2],
  [BuffType.FALL]: [0],
  [BuffType.PIERCE]: [0],
  [BuffType.KNOCKDOWN]: [1],
  [BuffType.SUPPRESSION]: [3],
  [BuffType.CHARM]: [2, 3],
  [BuffType.LIMIT]: [6],
});

const labels = Object.freeze({
  [BuffType.STUN]: '晕眩',
  [BuffType.FALL]: '跌倒',
  [BuffType.PIERCE]: '穿刺',
  [BuffType.ELECTROCUTE]: '电击',
  [BuffType.KNOCKBACK]: '击退',
  [BuffType.CHAOS]: '混乱',
  [BuffType.BURN_STATIC]: '火焰灼烧',
  [BuffType.LIMIT]: '',
  [BuffType.LOCK]: '封锁',
  [BuffType.KNOCKDOWN]: '跌倒',
  [BuffType.SUPPRESSION]: '压制',
  [BuffType.CHARM]: '魅惑',
});

const definitions = new Map();
for (let type = BuffType.ATTACK_POWER; type <= BuffType.SCALE; type += 1) {
  definitions.set(type, Object.freeze({ type, name: BuffName[type], kind: BuffKind.NUMBER, channels: [] }));
}
definitions.set(BuffType.CUSTOM, Object.freeze({ type: BuffType.CUSTOM, name: 'custom', kind: BuffKind.CUSTOM, channels: [] }));
for (let type = BuffType.STUN; type <= BuffType.CHARM; type += 1) {
  definitions.set(type, Object.freeze({
    type,
    name: BuffName[type],
    label: labels[type] || '',
    kind: BuffKind.STATE,
    channels: Object.freeze((stateChannels[type] || []).slice()),
  }));
}

function definitionFor(type) {
  const value = definitions.get(Number(type));
  if (!value) throw new Error(`Unknown Buff type: ${type}`);
  return value;
}

module.exports = { BuffKind, BuffDefinitions: definitions, definitionFor, stateChannels };
