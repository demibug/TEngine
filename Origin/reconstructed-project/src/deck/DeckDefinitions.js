'use strict';
const BASE_POOL = Object.freeze(['刀', '弓', '枪', '骑']);
const DeckDefinitions = Object.freeze({
  handSize: 5, // s4.fe
  basePool: BASE_POOL,
  defaultLevel: 1,
  baseUnitCost: 1,
  maxLevel: 5,
});
module.exports = { BASE_POOL, DeckDefinitions };
