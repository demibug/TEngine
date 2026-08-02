'use strict';

const { SingletonBase } = require('./SingletonBase');

/** 原 pR：本轮只恢复 startGame/gameOver 明确依赖的清空契约。 */
class PlacementReservationRegistry extends SingletonBase {
  constructor() {
    super();
    this.items = new Set();
  }
  add(item) { this.items.add(item); return item; }
  delete(item) { return this.items.delete(item); }
  clear() { this.items.clear(); }
  get size() { return this.items.size; }
}

module.exports = { PlacementReservationRegistry };
