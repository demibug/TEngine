'use strict';

const { UnitContainerType } = require('../units/UnitBase');
const { GENERAL_PART_WORDS } = require('./GeneralDefinitions');

const GeneralPartState = Object.freeze({
  NONE: 'GeneralPartNone',
  WAIT: 'GeneralPartWait',
  MERGE: 'GeneralPartMerge',
});

/** Engine-independent recovery of the original qo GeneralPart object. */
class GeneralPart {
  constructor({ id = -1, word, side = true, level = 1 } = {}) {
    this.id = id;
    this.objectType = 3;
    this.word = '';
    this.side = Boolean(side);
    this.level = Math.max(1, Number(level) || 1);
    this.ownerId = -1;
    this.state = GeneralPartState.NONE;
    this.containerType = UnitContainerType.NONE;
    this.gridPosition = { x: 0, y: 0 };
    this.placement = null;
    this.active = false;
    if (word != null) this.init(word, side);
  }

  init(word, side = this.side) {
    if (GENERAL_PART_WORDS.indexOf(word) < 0) throw new Error(`Unknown general part: ${word}`);
    this.word = word;
    this.side = Boolean(side);
    this.ownerId = -1;
    this.state = GeneralPartState.NONE;
    this.active = true;
    return this;
  }

  setPlacement(containerType, gridX, gridY) {
    this.containerType = containerType;
    this.gridPosition = { x: gridX, y: gridY };
    return this;
  }

  activatePlacement({ parent = null, pixelX = 0, pixelY = 0, zIndex = 0 } = {}) {
    this.placement = { parent, pixelX, pixelY, zIndex };
    this.active = true;
    return this;
  }

  changeState(state) {
    if (Object.values(GeneralPartState).indexOf(state) < 0) throw new Error(`Unknown general part state: ${state}`);
    this.state = state;
    return this;
  }

  assignTo(generalId) {
    this.ownerId = generalId;
    this.state = GeneralPartState.MERGE;
    return this;
  }

  unbindFromGeneral(generalId = null) {
    if (generalId != null && this.ownerId !== generalId) return false;
    this.ownerId = -1;
    this.state = GeneralPartState.NONE;
    this.active = true;
    return true;
  }

  gameOver() {
    this.active = false;
    this.ownerId = -1;
    this.state = GeneralPartState.NONE;
    this.placement = null;
    return true;
  }
}

module.exports = { GeneralPart, GeneralPartState };
