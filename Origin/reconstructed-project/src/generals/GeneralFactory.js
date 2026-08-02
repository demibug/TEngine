'use strict';

const { GeneralPart } = require('./GeneralPart');
const { GeneralUnit } = require('./GeneralUnit');
const { findGeneralByParts } = require('./GeneralDefinitions');

/** Pure recovery of the original UnitRegistry's general-part merge path. */
class GeneralFactory {
  constructor({ nextId = 1 } = {}) {
    this.nextId = nextId;
  }

  createPart({ id = this.nextId++, word, side = true, level = 1 } = {}) {
    return new GeneralPart({ id, word, side, level });
  }

  createGeneral(parts, { id = this.nextId++, side = true, isPlayer = true, weaponId = null } = {}) {
    if (!Array.isArray(parts) || parts.length !== 2) throw new Error('General merge requires exactly two parts');
    const definition = findGeneralByParts(parts);
    if (!definition) throw new Error(`Unsupported general part merge: ${parts.map(part => part.word).join('')}`);
    const general = new GeneralUnit({ id, name: definition.name, side, level: 1 });
    general.init(parts, isPlayer, definition.index);
    general.weaponId = weaponId;
    for (const part of parts) part.assignTo(general.id);
    return general;
  }
}

module.exports = { GeneralFactory };
