'use strict';
const { PlayerDataCore } = require('../../src/data/PlayerDataCore');
class PlayerDataCoreMock extends PlayerDataCore {
  constructor(overrides = {}) { super(PlayerDataCore.createDevelopmentSample(overrides)); }
}
module.exports = { PlayerDataCoreMock };
